using System.Windows;
using IMTReader.App.ViewModels;

namespace IMTReader.App.Dialogs;

public partial class AiAssistantWindow : Window
{
    private readonly DocumentViewModel _doc;
    private readonly Action<string> _appendToNotes;
    private BlockViewModel? _block;
    private CancellationTokenSource? _cts;
    private string _lastAction = string.Empty;

    public AiAssistantWindow(DocumentViewModel doc, Action<string> appendToNotes)
    {
        InitializeComponent();
        _doc = doc;
        _appendToNotes = appendToNotes;
        Title = $"AI 助手 — {doc.Title}";
    }

    public void SetSource(string text, BlockViewModel? block)
    {
        _block = block;
        SourceBox.Text = text;
        SourceInfo.Text = block != null ? $"来自文本块 {block.Label}（第 {_doc.PageNumber} 页）" : $"第 {_doc.PageNumber} 页";
        ReplaceButton.IsEnabled = false;
    }

    private void LoadBlock_Click(object sender, RoutedEventArgs e)
    {
        var block = _doc.SelectedBlock;
        if (block == null)
        {
            StatusText.Text = "请先在阅读界面选中一个文本块";
            return;
        }
        SetSource(block.DisplayText, block);
    }

    private void LoadPage_Click(object sender, RoutedEventArgs e) => SetSource(_doc.CurrentPageText(_doc.ShowSimplified), null);

    private async Task RunAsync(string action, Func<string, CancellationToken, Task<string>> op)
    {
        var text = SourceBox.Text.Trim();
        if (text.Length == 0)
        {
            StatusText.Text = "请先载入或输入原文";
            return;
        }
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _lastAction = action;
        CancelButton.IsEnabled = true;
        ReplaceButton.IsEnabled = false;
        StatusText.Text = "正在请求 AI 服务…";
        OutputBox.Text = string.Empty;
        try
        {
            var result = await op(text, _cts.Token);
            OutputBox.Text = result;
            StatusText.Text = "完成";
            ReplaceButton.IsEnabled = action == "punctuate" && _block != null;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消";
        }
        catch (Exception ex)
        {
            StatusText.Text = "失败：" + ex.Message;
            AppServices.Log.Error("AI 请求失败", ex);
        }
        finally
        {
            CancelButton.IsEnabled = false;
        }
    }

    private async void Punctuate_Click(object sender, RoutedEventArgs e) => await RunAsync("punctuate", AppServices.Ai.PunctuateAsync);
    private async void Translate_Click(object sender, RoutedEventArgs e) => await RunAsync("translate", AppServices.Ai.TranslateAsync);
    private async void Summarize_Click(object sender, RoutedEventArgs e) => await RunAsync("summarize", AppServices.Ai.SummarizeAsync);
    private async void Entities_Click(object sender, RoutedEventArgs e) => await RunAsync("entities", AppServices.Ai.ExtractEntitiesAsync);
    private async void Explain_Click(object sender, RoutedEventArgs e) => await RunAsync("explain", AppServices.Ai.ExplainAsync);

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(OutputBox.Text); StatusText.Text = "已复制"; } catch { }
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        if (_block == null || OutputBox.Text.Length == 0) return;
        if (_doc.ShowSimplified)
        {
            StatusText.Text = "简体显示模式下不可替换，请先切回原文";
            return;
        }
        if (MessageBox.Show(this, $"用 AI 结果替换文本块 {_block.Label} 的内容？替换后请核对并点「保存修改」。", "替换", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _doc.ReplaceBlockText(_block, OutputBox.Text);
        StatusText.Text = "已替换（未保存）";
    }

    private void AppendNotes_Click(object sender, RoutedEventArgs e)
    {
        if (OutputBox.Text.Length == 0) return;
        var header = _lastAction switch
        {
            "punctuate" => "## AI 标点",
            "translate" => "## AI 白话译文",
            "summarize" => "## AI 摘要",
            "entities" => "## AI 实体",
            _ => "## AI 注释"
        };
        _appendToNotes($"{header}（{_doc.Citation(_block?.Order)}）\n\n{OutputBox.Text}");
        StatusText.Text = "已追加到笔记";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }
}
