using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IMTReader.Core.Models;
using IMTReader.Core.Services;

namespace IMTReader.Core.Ai;

/// <summary>
/// OpenAI 兼容的对话接口客户端（DeepSeek、通义千问、智谱、Ollama 本地等均可）。
/// 提供古籍整理常用能力：自动标点、白话翻译、摘要、实体抽取、疑难注释、视觉识别。
/// </summary>
public sealed class AiClient
{
    private const string SystemPrompt =
        "你是精通中国古籍整理的专家，熟悉文言文、繁体字、异体字、历史典章制度与历史文献。只输出结果本身，不要寒暄、不要解释你的做法。";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly Func<AppSettings> _settings;
    private readonly Logger _log;

    public AiClient(Func<AppSettings> settings, Logger log)
    {
        _settings = settings;
        _log = log;
    }

    public bool IsConfigured
    {
        get
        {
            var s = _settings();
            return s.AiEnabled && !string.IsNullOrWhiteSpace(s.AiBaseUrl) && !string.IsNullOrWhiteSpace(s.AiModel);
        }
    }

    public async Task<string> ChatAsync(string userText, byte[]? imagePng, CancellationToken ct, string? model = null, double temperature = 0.2)
    {
        var s = _settings();
        if (string.IsNullOrWhiteSpace(s.AiBaseUrl) || string.IsNullOrWhiteSpace(s.AiModel))
            throw new InvalidOperationException("未配置 AI 服务，请在“设置”中填写接口地址与模型。");

        var url = s.AiBaseUrl.TrimEnd('/') + "/chat/completions";
        object userContent = imagePng == null
            ? userText
            : new object[]
            {
                new { type = "text", text = userText },
                new { type = "image_url", image_url = new { url = "data:image/png;base64," + Convert.ToBase64String(imagePng) } }
            };
        var payload = new
        {
            model = string.IsNullOrWhiteSpace(model) ? s.AiModel : model,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userContent }
            },
            temperature,
            stream = false
        };
        var json = JsonSerializer.Serialize(payload);

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(s.AiApiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.AiApiKey.Trim());

        _log.Info($"调用 AI：{payload.model}，{userText.Length} 字" + (imagePng != null ? "，含图片" : string.Empty));
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"AI 服务返回 {(int)resp.StatusCode}：{Truncate(body, 300)}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            throw new InvalidOperationException("AI 服务返回了空结果：" + Truncate(body, 300));
        var content = choices[0].GetProperty("message").GetProperty("content");
        return (content.ValueKind == JsonValueKind.String ? content.GetString() : content.ToString())?.Trim() ?? string.Empty;
    }

    public Task<string> PunctuateAsync(string text, CancellationToken ct) => ChatAsync(
        "请为下面的古文添加现代标点（句读）。要求：不改动、不增删任何原有文字，保持繁简用字原貌，不翻译，不加注释，按原文分段，只输出加标点后的文本。\n\n" + text,
        null, ct);

    public Task<string> TranslateAsync(string text, CancellationToken ct) => ChatAsync(
        "请把下面的文言文翻译成通顺的现代汉语白话文。人名、地名、官职、书名照录；只输出译文，按原文分段。\n\n" + text,
        null, ct);

    public Task<string> SummarizeAsync(string text, CancellationToken ct) => ChatAsync(
        "请用现代汉语概括下面这段史料的主要内容（时间、地点、人物、事件、要点），150 字以内。\n\n" + text,
        null, ct);

    public Task<string> ExtractEntitiesAsync(string text, CancellationToken ct) => ChatAsync(
        "请从下面的古文中抽取命名实体，按“类型：名称（在文中的原字）”每行一条列出，类型限于：人名、地名、官职、时间、书名、机构、其他。同一实体只列一次；没有的类型不列。\n\n" + text,
        null, ct);

    public Task<string> ExplainAsync(string text, CancellationToken ct) => ChatAsync(
        "请为下面的古文做简明注释：列出其中的疑难字词、典故、职官、地名、纪年，并逐条给出解释；若有疑似 OCR 错字，请单独指出并给出可能的正确字。用现代汉语作答。\n\n" + text,
        null, ct);

    public Task<string> VisionOcrAsync(byte[] png, CancellationToken ct)
    {
        var s = _settings();
        var model = string.IsNullOrWhiteSpace(s.AiVisionModel) ? null : s.AiVisionModel;
        return ChatAsync(
            "请识别图片中的全部文字并转写为文本。要求：保留原字（繁体、异体照录，不要转为简体）；若为竖排，请按从右到左、从上到下的阅读顺序转写为横排；" +
            "每一栏或每一段落单独成段，段落之间用一个空行分隔；双行小注请以【】括起并放在所注正文之后；不要添加任何解释、标题或说明。",
            png, ct, model, 0.0);
    }

    public Task<string> TestAsync(CancellationToken ct) => ChatAsync("请只回复四个字：连接成功", null, ct, null, 0.0);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
