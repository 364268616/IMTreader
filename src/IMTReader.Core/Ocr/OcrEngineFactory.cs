using IMTReader.Core.Ai;
using IMTReader.Core.Models;
using IMTReader.Core.Services;

namespace IMTReader.Core.Ocr;

/// <summary>按当前设置提供 OCR 引擎实例；PaddleOCR 实例按配置缓存复用。</summary>
public sealed class OcrEngineFactory : IDisposable
{
    private readonly object _gate = new();
    private readonly SettingsStore _settings;
    private readonly AiClient _ai;
    private PaddleOcrEngine? _paddle;
    private (string ModelDir, int Threads, bool Cls, int MaxSide, int Batch) _paddleKey;

    public OcrEngineFactory(SettingsStore settings, AiClient ai)
    {
        _settings = settings;
        _ai = ai;
    }

    public IOcrEngine Get(OcrEngineKind? kindOverride = null)
    {
        var s = _settings.Current;
        var kind = kindOverride ?? s.OcrEngine;
        return kind switch
        {
            OcrEngineKind.WindowsBuiltIn => new WindowsOcrEngine(s.WindowsOcrLanguage),
            OcrEngineKind.VisionLlm => new VisionLlmOcrEngine(_ai),
            _ => GetPaddle(s)
        };
    }

    private PaddleOcrEngine GetPaddle(AppSettings s)
    {
        var key = (s.OcrModelDirectory ?? string.Empty, s.OcrThreads, s.UseAngleClassifier, s.OcrMaxSideLen, s.OcrBatchSize);
        lock (_gate)
        {
            if (_paddle == null || key != _paddleKey)
            {
                _paddle?.Dispose();
                _paddle = new PaddleOcrEngine(s.OcrModelDirectory, s.OcrThreads, s.UseAngleClassifier, s.OcrMaxSideLen, s.OcrBatchSize);
                _paddleKey = key;
            }
            return _paddle;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _paddle?.Dispose();
            _paddle = null;
        }
    }
}
