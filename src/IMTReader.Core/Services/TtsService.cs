using System.Speech.Synthesis;

namespace IMTReader.Core.Services;

/// <summary>朗读服务（使用 Windows 内置语音）。</summary>
public sealed class TtsService : IDisposable
{
    private SpeechSynthesizer? _synth;

    public bool IsSpeaking => _synth?.State == SynthesizerState.Speaking;

    public IReadOnlyList<string> GetChineseVoices()
    {
        try
        {
            using var s = new SpeechSynthesizer();
            return s.GetInstalledVoices()
                .Where(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                .Select(v => v.VoiceInfo.Name)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _synth ??= CreateSynth();
        _synth.SpeakAsyncCancelAll();
        _synth.SpeakAsync(text);
    }

    public void Stop() => _synth?.SpeakAsyncCancelAll();

    private static SpeechSynthesizer CreateSynth()
    {
        var s = new SpeechSynthesizer();
        var zh = s.GetInstalledVoices()
            .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        if (zh != null) s.SelectVoice(zh.VoiceInfo.Name);
        s.Rate = 0;
        return s;
    }

    public void Dispose()
    {
        _synth?.Dispose();
        _synth = null;
    }
}
