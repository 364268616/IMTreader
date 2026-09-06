using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace IMTReader.Core.Imaging;

/// <summary>位图工具：解码、编码、裁剪、缩放、反白、二值化，以及 WPF 与 OpenCV 之间的转换。所有返回的位图均已冻结，可跨线程使用。</summary>
public static class BitmapUtil
{
    public static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif", ".webp" };

    public static bool IsSupportedImage(string path) =>
        SupportedImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsPdf(string path) =>
        string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>读取图片文件中的某一帧（TIFF 多页），转换为 Bgra32。</summary>
    public static BitmapSource LoadFrame(string path, int frameIndex = 0)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[Math.Clamp(frameIndex, 0, decoder.Frames.Count - 1)];
        return Detach(frame);
    }

    /// <summary>
    /// 复制像素生成独立的冻结位图，切断对 BitmapDecoder 的引用。
    /// 解码得到的帧即便自身已冻结，仍通过 Decoder 属性绑定在解码线程上；
    /// 一旦在其它线程把它包进新的 FormatConvertedBitmap 并冻结，WPF 会重新遍历整条链并抛出线程亲和异常。
    /// 页面位图要在渲染线程与识别线程之间传递，因此必须在解码处就地脱钩。
    /// </summary>
    private static BitmapSource Detach(BitmapSource source)
    {
        var src = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        src.CopyPixels(px, stride, 0);
        return FromBgra(px, w, h, stride);
    }

    public static int GetFrameCount(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return Math.Max(1, decoder.Frames.Count);
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>以指定宽度解码缩略图（JPEG/PNG 可利用解码器缩放，速度快）。</summary>
    public static BitmapSource LoadThumbnail(string path, int frameIndex, int maxWidth)
    {
        if (frameIndex == 0)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = maxWidth;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                return Detach(bi);
            }
            catch
            {
                // 回退到常规解码
            }
        }
        var full = LoadFrame(path, frameIndex);
        double scale = Math.Min(1.0, maxWidth / (double)full.PixelWidth);
        return scale < 1.0 ? Resize(full, scale) : full;
    }

    public static BitmapSource DecodeFrozen(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return Detach(decoder.Frames[0]);
    }

    public static BitmapSource ToBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32 && source.IsFrozen) return source;
        var conv = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        conv.Freeze();
        return conv;
    }

    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static byte[] EncodeJpeg(BitmapSource source, int quality = 90)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static BitmapSource Crop(BitmapSource source, Int32Rect rect)
    {
        rect = Clamp(rect, source.PixelWidth, source.PixelHeight);
        var cropped = new CroppedBitmap(source, rect);
        cropped.Freeze();
        return cropped;
    }

    public static Int32Rect Clamp(Int32Rect rect, int width, int height)
    {
        int x = Math.Clamp(rect.X, 0, Math.Max(0, width - 1));
        int y = Math.Clamp(rect.Y, 0, Math.Max(0, height - 1));
        int w = Math.Clamp(rect.Width, 1, Math.Max(1, width - x));
        int h = Math.Clamp(rect.Height, 1, Math.Max(1, height - y));
        return new Int32Rect(x, y, w, h);
    }

    public static BitmapSource Resize(BitmapSource source, double scale)
    {
        var tb = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        tb.Freeze();
        return tb;
    }

    public static BitmapSource Rotate(BitmapSource source, int degrees)
    {
        degrees = ((degrees % 360) + 360) % 360;
        if (degrees == 0) return source;
        var tb = new TransformedBitmap(source, new RotateTransform(degrees));
        tb.Freeze();
        return tb;
    }

    // ------------------------------------------------------------------ 像素处理

    private static (byte[] pixels, int width, int height, int stride) GetBgra(BitmapSource source)
    {
        var src = ToBgra32(source);
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        src.CopyPixels(px, stride, 0);
        return (px, w, h, stride);
    }

    private static BitmapSource FromBgra(byte[] pixels, int width, int height, int stride)
    {
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>反白：用于拓片（白字黑底）等场景。</summary>
    public static BitmapSource Invert(BitmapSource source)
    {
        var (px, w, h, stride) = GetBgra(source);
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = (byte)(255 - px[i]);
            px[i + 1] = (byte)(255 - px[i + 1]);
            px[i + 2] = (byte)(255 - px[i + 2]);
        }
        return FromBgra(px, w, h, stride);
    }

    public static BitmapSource ToGrayscale(BitmapSource source)
    {
        var (px, w, h, stride) = GetBgra(source);
        for (int i = 0; i < px.Length; i += 4)
        {
            byte g = Gray(px[i], px[i + 1], px[i + 2]);
            px[i] = px[i + 1] = px[i + 2] = g;
        }
        return FromBgra(px, w, h, stride);
    }

    private static byte Gray(byte b, byte g, byte r) => (byte)((r * 299 + g * 587 + b * 114) / 1000);

    /// <summary>Otsu 全局阈值二值化。</summary>
    public static BitmapSource Binarize(BitmapSource source)
    {
        var (px, w, h, stride) = GetBgra(source);
        var hist = new int[256];
        var gray = new byte[w * h];
        for (int i = 0, j = 0; i < px.Length; i += 4, j++)
        {
            gray[j] = Gray(px[i], px[i + 1], px[i + 2]);
            hist[gray[j]]++;
        }
        int threshold = OtsuThreshold(hist, gray.Length);
        for (int i = 0, j = 0; i < px.Length; i += 4, j++)
        {
            byte v = gray[j] > threshold ? (byte)255 : (byte)0;
            px[i] = px[i + 1] = px[i + 2] = v;
            px[i + 3] = 255;
        }
        return FromBgra(px, w, h, stride);
    }

    public static int OtsuThreshold(int[] hist, int total)
    {
        double sum = 0;
        for (int i = 0; i < 256; i++) sum += i * (double)hist[i];
        double sumB = 0;
        int wB = 0;
        double maxVar = -1;
        int threshold = 127;
        for (int t = 0; t < 256; t++)
        {
            wB += hist[t];
            if (wB == 0) continue;
            int wF = total - wB;
            if (wF == 0) break;
            sumB += t * (double)hist[t];
            double mB = sumB / wB, mF = (sum - sumB) / wF;
            double between = (double)wB * wF * (mB - mF) * (mB - mF);
            if (between > maxVar)
            {
                maxVar = between;
                threshold = t;
            }
        }
        return threshold;
    }

    /// <summary>简单对比度/亮度调整（contrast 1.0 = 不变）。</summary>
    public static BitmapSource AdjustContrast(BitmapSource source, double contrast, int brightness = 0)
    {
        var (px, w, h, stride) = GetBgra(source);
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
            lut[i] = (byte)Math.Clamp((i - 128) * contrast + 128 + brightness, 0, 255);
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = lut[px[i]];
            px[i + 1] = lut[px[i + 1]];
            px[i + 2] = lut[px[i + 2]];
        }
        return FromBgra(px, w, h, stride);
    }

    // ------------------------------------------------------------------ OpenCV 互转（PaddleOCR 以 Mat 为输入）

    /// <summary>转换为 OpenCV 的 BGR 三通道 Mat。调用方负责释放。</summary>
    public static Mat ToMat(BitmapSource source)
    {
        // 只读取像素，不冻结临时转换对象：冻结会重新遍历整条位图链，可能触碰到线程亲和的解码器
        var conv = source.Format == PixelFormats.Bgr24 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        int stride = (w * 3 + 3) / 4 * 4;
        var buf = new byte[stride * h];
        conv.CopyPixels(buf, stride, 0);
        // FromPixelData 只是包装托管数组，必须 Clone 出独立缓冲区再交给原生代码
        using var view = Mat.FromPixelData(h, w, MatType.CV_8UC3, buf, stride);
        return view.Clone();
    }
}
