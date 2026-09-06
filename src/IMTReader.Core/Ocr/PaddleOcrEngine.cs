using System.Windows.Media.Imaging;
using IMTReader.Core.Imaging;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using WpfPoint = System.Windows.Point;

namespace IMTReader.Core.Ocr;

/// <summary>
/// 本地 PaddleOCR 引擎，使用飞桨官方 PP-OCRv5 推理模型。
/// 流程为“检测 → 按四点透视裁剪 → 竖栏旋转为横排 → 批量识别”，
/// 竖排整栏因此能作为一行送入识别模型，输出坐标仍为原图坐标。
/// 引擎初始化约 1-2 秒，实例应复用。
/// </summary>
public sealed class PaddleOcrEngine : IOcrEngine, IDisposable
{
    private const string DetectionModelFolder = "PP-OCRv5_mobile_det_infer";
    private const string RecognitionModelFolder = "PP-OCRv5_mobile_rec_infer";
    private const string ClassificationModelFolder = "PP-LCNet_x0_25_textline_ori_infer";

    /// <summary>高宽比超过该值的裁剪块视为竖栏，逆时针旋转 90° 后再识别。</summary>
    private const double VerticalCropRatio = 1.5;

    private readonly object _gate = new();
    private readonly string _modelDir;
    private readonly int _threads;
    private readonly bool _angleCls;
    private readonly int _maxSideLen;
    private readonly int _batchSize;

    private PaddleOcrDetector? _detector;
    private PaddleOcrRecognizer? _recognizer;
    private PaddleOcrClassifier? _classifier;
    private bool _disposed;

    public PaddleOcrEngine(string? modelDirectory, int threads, bool angleCls, int maxSideLen, int batchSize)
    {
        _modelDir = string.IsNullOrWhiteSpace(modelDirectory) ? DefaultModelDirectory : modelDirectory!;
        _threads = Math.Clamp(threads, 1, 64);
        _angleCls = angleCls;
        _maxSideLen = Math.Clamp(maxSideLen, 480, 8192);
        _batchSize = Math.Clamp(batchSize, 1, 256);
    }

    /// <summary>随程序分发的官方模型目录。</summary>
    public static string DefaultModelDirectory => Path.Combine(AppContext.BaseDirectory, "OcrModels");

    public string Name => "PaddleOCR PP-OCRv5";

    /// <summary>检查模型文件与原生推理库是否齐备。</summary>
    public static bool IsRuntimeAvailable(string? modelDirectory = null)
    {
        var dir = string.IsNullOrWhiteSpace(modelDirectory) ? DefaultModelDirectory : modelDirectory!;
        return HasModel(Path.Combine(dir, DetectionModelFolder))
               && HasModel(Path.Combine(dir, RecognitionModelFolder))
               && HasNativeRuntime();
    }

    /// <summary>常规生成时原生库位于 runtimes\win-x64\native；自包含发布时会展平到根目录。</summary>
    private static bool HasNativeRuntime()
    {
        var baseDir = AppContext.BaseDirectory;
        return File.Exists(Path.Combine(baseDir, "paddle_inference_c.dll"))
               || File.Exists(Path.Combine(baseDir, "runtimes", "win-x64", "native", "paddle_inference_c.dll"));
    }

    private static bool HasModel(string dir) =>
        Directory.Exists(dir) && File.Exists(Path.Combine(dir, "inference.pdiparams"))
                              && (File.Exists(Path.Combine(dir, "inference.json")) || File.Exists(Path.Combine(dir, "inference.pdmodel")));

    private void EnsureEngines()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_detector != null) return;

            var detDir = Path.Combine(_modelDir, DetectionModelFolder);
            var recDir = Path.Combine(_modelDir, RecognitionModelFolder);
            if (!HasModel(detDir) || !HasModel(recDir))
                throw new InvalidOperationException($"未找到 PP-OCRv5 模型文件，请检查目录：{_modelDir}");

            Action<PaddleConfig> device = PaddleDevice.Mkldnn(cpuMathThreadCount: _threads);
            _detector = new PaddleOcrDetector(DetectionModel.FromDirectory(detDir, ModelVersion.V5), device)
            {
                MaxSize = _maxSideLen
            };
            _recognizer = new PaddleOcrRecognizer(RecognizationModel.FromDirectoryV5(recDir), device);

            var clsDir = Path.Combine(_modelDir, ClassificationModelFolder);
            if (_angleCls && HasModel(clsDir))
                _classifier = new PaddleOcrClassifier(ClassificationModel.FromDirectory(clsDir, ModelVersion.V4), device);
        }
    }

    public Task<OcrPageResult> RecognizeAsync(BitmapSource image, OcrOptions options, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            EnsureEngines();

            var page = new OcrPageResult
            {
                EngineName = Name,
                ImageWidth = image.PixelWidth,
                ImageHeight = image.PixelHeight
            };

            using var src = BitmapUtil.ToMat(image);
            RotatedRect[] boxes;
            lock (_gate)
            {
                ct.ThrowIfCancellationRequested();
                boxes = _detector!.Run(src);
            }
            if (boxes.Length == 0) return page;

            var crops = new List<Mat>(boxes.Length);
            var kept = new List<RotatedRect>(boxes.Length);
            try
            {
                foreach (var box in boxes)
                {
                    ct.ThrowIfCancellationRequested();
                    var crop = CropBox(src, box);
                    if (crop == null) continue;
                    crops.Add(crop);
                    kept.Add(box);
                }
                if (crops.Count == 0) return page;

                PaddleOcrRecognizerResult[] results;
                lock (_gate)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_classifier != null)
                    {
                        for (int i = 0; i < crops.Count; i++)
                        {
                            var rotated = _classifier.Run(crops[i]);
                            if (!ReferenceEquals(rotated, crops[i]))
                            {
                                crops[i].Dispose();
                                crops[i] = rotated;
                            }
                        }
                    }
                    results = _recognizer!.Run(crops.ToArray(), _batchSize);
                }

                for (int i = 0; i < results.Length && i < kept.Count; i++)
                {
                    var text = results[i].Text?.Trim();
                    if (string.IsNullOrEmpty(text)) continue;
                    float score = results[i].Score;
                    page.Lines.Add(new OcrLine
                    {
                        Text = text,
                        Score = float.IsNaN(score) ? 0 : score,
                        Quad = ToQuad(kept[i])
                    });
                }
            }
            finally
            {
                foreach (var c in crops) c.Dispose();
            }
            return page;
        }, ct);
    }

    /// <summary>按检测框的四个角做透视裁剪（可校正倾斜），竖栏再旋转为横排。</summary>
    private static Mat? CropBox(Mat src, RotatedRect box)
    {
        var bounding = box.BoundingRect();
        int bx = Math.Clamp(bounding.X, 0, Math.Max(0, src.Width - 1));
        int by = Math.Clamp(bounding.Y, 0, Math.Max(0, src.Height - 1));
        int bw = Math.Clamp(bounding.Width, 0, src.Width - bx);
        int bh = Math.Clamp(bounding.Height, 0, src.Height - by);
        if (bw < 2 || bh < 2) return null;

        Point2f[] p = box.Points();
        var byY = p.OrderBy(q => q.Y).ToArray();
        var top = byY.Take(2).OrderBy(q => q.X).ToArray();
        var bottom = byY.Skip(2).OrderBy(q => q.X).ToArray();
        Point2f tl = top[0], tr = top[1], br = bottom[1], bl = bottom[0];

        int w = (int)Math.Round(Math.Max(Distance(tl, tr), Distance(bl, br)));
        int h = (int)Math.Round(Math.Max(Distance(tl, bl), Distance(tr, br)));

        Mat crop;
        if (w < 2 || h < 2)
        {
            crop = new Mat(src, new Rect(bx, by, bw, bh)).Clone();
        }
        else
        {
            using var matrix = Cv2.GetPerspectiveTransform(
                new[] { tl, tr, br, bl },
                new[] { new Point2f(0, 0), new Point2f(w, 0), new Point2f(w, h), new Point2f(0, h) });
            crop = new Mat();
            Cv2.WarpPerspective(src, crop, matrix, new Size(w, h), InterpolationFlags.Cubic, BorderTypes.Replicate);
        }

        if (crop.Height > crop.Width * VerticalCropRatio)
        {
            var rotated = new Mat();
            // 逆时针旋转：竖栏的顶端转到左端，识别顺序即为原来的自上而下
            Cv2.Rotate(crop, rotated, RotateFlags.Rotate90Counterclockwise);
            crop.Dispose();
            crop = rotated;
        }
        return crop;
    }

    private static double Distance(Point2f a, Point2f b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static WpfPoint[] ToQuad(RotatedRect box)
    {
        var p = box.Points();
        var quad = new WpfPoint[4];
        for (int i = 0; i < 4; i++) quad[i] = new WpfPoint(p[i].X, p[i].Y);
        return quad;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _detector?.Dispose(); } catch { /* 忽略释放异常 */ }
            try { _recognizer?.Dispose(); } catch { /* 忽略释放异常 */ }
            try { _classifier?.Dispose(); } catch { /* 忽略释放异常 */ }
            _detector = null;
            _recognizer = null;
            _classifier = null;
        }
    }
}
