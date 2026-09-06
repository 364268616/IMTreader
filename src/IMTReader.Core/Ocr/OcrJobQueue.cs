using System.Threading.Channels;
using IMTReader.Core.Imaging;
using IMTReader.Core.Models;
using IMTReader.Core.Services;

namespace IMTReader.Core.Ocr;

public enum OcrJobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>一个识别任务：某文献的若干页。</summary>
public sealed class OcrJob
{
    private static int _nextId;

    internal readonly CancellationTokenSource Cts = new();

    public int Id { get; } = Interlocked.Increment(ref _nextId);
    public DocumentInfo Document { get; }
    public int[] Pages { get; }
    public OcrOptions Options { get; }
    public string Title { get; }
    public OcrJobStatus Status { get; internal set; } = OcrJobStatus.Pending;
    public int Completed { get; internal set; }
    public int Failed { get; internal set; }
    public string? Error { get; internal set; }
    public DateTime CreatedAt { get; } = DateTime.Now;
    public DateTime? StartedAt { get; internal set; }
    public DateTime? FinishedAt { get; internal set; }

    public int Total => Pages.Length;
    public double Progress => Total == 0 ? 1 : (Completed + Failed) / (double)Total;
    public bool IsFinished => Status is OcrJobStatus.Completed or OcrJobStatus.Failed or OcrJobStatus.Cancelled;

    public string StatusText => Status switch
    {
        OcrJobStatus.Pending => "排队中",
        OcrJobStatus.Running => $"识别中 {Completed + Failed}/{Total}",
        OcrJobStatus.Completed => Failed == 0 ? "识别完成" : $"完成（{Failed} 页失败）",
        OcrJobStatus.Failed => "失败",
        _ => "已取消"
    };

    public OcrJob(DocumentInfo document, int[] pages, OcrOptions options, string title)
    {
        Document = document;
        Pages = pages;
        Options = options;
        Title = title;
    }
}

/// <summary>任务中心：串行执行识别任务的后台队列。事件在创建队列的同步上下文（UI 线程）上回调。</summary>
public sealed class OcrJobQueue : IDisposable
{
    private readonly Channel<OcrJob> _channel = Channel.CreateUnbounded<OcrJob>();
    private readonly List<OcrJob> _jobs = new();
    private readonly object _gate = new();
    private readonly OcrService _ocr;
    private readonly Func<DocumentInfo, IPageSource> _openSource;
    private readonly Logger _log;
    private readonly SynchronizationContext? _sync;
    private readonly CancellationTokenSource _shutdown = new();

    public event Action<OcrJob>? JobChanged;
    public event Action<OcrJob, int, IReadOnlyList<OcrBlock>>? PageCompleted;

    public OcrJobQueue(OcrService ocr, Func<DocumentInfo, IPageSource> openSource, Logger log)
    {
        _ocr = ocr;
        _openSource = openSource;
        _log = log;
        _sync = SynchronizationContext.Current;
        _ = Task.Run(WorkerLoopAsync);
    }

    public IReadOnlyList<OcrJob> Jobs
    {
        get { lock (_gate) return _jobs.ToList(); }
    }

    public bool IsBusy
    {
        get { lock (_gate) return _jobs.Any(j => !j.IsFinished); }
    }

    public OcrJob Enqueue(DocumentInfo document, IEnumerable<int> pages, OcrOptions options, string title)
    {
        var job = new OcrJob(document, pages.Distinct().OrderBy(p => p).ToArray(), options.Clone(), title);
        lock (_gate) _jobs.Add(job);
        _channel.Writer.TryWrite(job);
        Raise(() => JobChanged?.Invoke(job));
        return job;
    }

    public void Cancel(int jobId)
    {
        OcrJob? job;
        lock (_gate) job = _jobs.FirstOrDefault(j => j.Id == jobId);
        if (job == null || job.IsFinished) return;
        job.Cts.Cancel();
        if (job.Status == OcrJobStatus.Pending)
        {
            job.Status = OcrJobStatus.Cancelled;
            job.FinishedAt = DateTime.Now;
            Raise(() => JobChanged?.Invoke(job));
        }
    }

    public void ClearFinished()
    {
        lock (_gate) _jobs.RemoveAll(j => j.IsFinished);
    }

    private async Task WorkerLoopAsync()
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(_shutdown.Token))
            {
                while (_channel.Reader.TryRead(out var job))
                {
                    if (job.Status == OcrJobStatus.Cancelled) continue;
                    await RunJobAsync(job);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 应用退出
        }
    }

    private async Task RunJobAsync(OcrJob job)
    {
        job.Status = OcrJobStatus.Running;
        job.StartedAt = DateTime.Now;
        Raise(() => JobChanged?.Invoke(job));

        IPageSource? source = null;
        try
        {
            source = _openSource(job.Document);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(job.Cts.Token, _shutdown.Token);
            foreach (var page in job.Pages)
            {
                if (linked.IsCancellationRequested) break;
                try
                {
                    var blocks = await _ocr.RecognizePageAsync(job.Document, source, page, job.Options, linked.Token);
                    job.Completed++;
                    Raise(() => PageCompleted?.Invoke(job, page, blocks));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    job.Failed++;
                    job.Error = ex.Message;
                    _log.Error($"识别失败：《{job.Document.Title}》第 {page + 1} 页", ex);
                }
                Raise(() => JobChanged?.Invoke(job));
            }

            job.Status = job.Cts.IsCancellationRequested
                ? OcrJobStatus.Cancelled
                : job.Completed == 0 && job.Failed > 0 ? OcrJobStatus.Failed : OcrJobStatus.Completed;
        }
        catch (Exception ex)
        {
            job.Status = OcrJobStatus.Failed;
            job.Error = ex.Message;
            _log.Error($"任务失败：{job.Title}", ex);
        }
        finally
        {
            source?.Dispose();
            job.FinishedAt = DateTime.Now;
            Raise(() => JobChanged?.Invoke(job));
        }
    }

    private void Raise(Action action)
    {
        if (_sync != null) _sync.Post(_ => action(), null);
        else action();
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _channel.Writer.TryComplete();
    }
}
