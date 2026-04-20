using System.Net.Http;
using System.Text.Json;

namespace VoiceBuddy.Services;

public sealed record DeepLLanguage(string Code, string Name)
{
    // "EN-US" stays upper, display as "English (American) (EN-US)".
    // "auto" pseudo-entry displays as just its name.
    public string Label => Code.Equals("auto", StringComparison.OrdinalIgnoreCase)
        ? Name
        : $"{Name} ({Code})";
}

/// <summary>
/// Fetches the DeepL-supported source and target language sets so the pickers only accept
/// real codes. Cached in memory; re-runs RefreshAsync whenever the API key or host change.
/// </summary>
public sealed class DeepLLanguagesService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly DebugLog _debug;

    public DeepLLanguagesService(DebugLog debug) => _debug = debug;

    public IReadOnlyList<DeepLLanguage> Source { get; private set; } = Array.Empty<DeepLLanguage>();
    public IReadOnlyList<DeepLLanguage> Target { get; private set; } = Array.Empty<DeepLLanguage>();

    public event EventHandler? Updated;

    private string _cacheKey = "";

    public async Task RefreshAsync(string host, string apiKey, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(host)) return;
        var key = host + "|" + apiKey;
        if (!force && key == _cacheKey && Source.Count > 0 && Target.Count > 0) return;

        try
        {
            var src = await FetchAsync(host, apiKey, "source");
            var tgt = await FetchAsync(host, apiKey, "target");
            Source = src;
            Target = tgt;
            _cacheKey = key;
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _debug.Log(LogDirection.In, "languages fetch failed", ex.Message);
        }
    }

    private async Task<List<DeepLLanguage>> FetchAsync(string host, string apiKey, string type)
    {
        var url = $"https://{host}/v2/languages?type={type}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {apiKey}");
        _debug.Log(LogDirection.Out, $"GET {url}", "");

        using var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        _debug.Log(LogDirection.In, $"HTTP {(int)resp.StatusCode}", body);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var list = new List<DeepLLanguage>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var code = el.GetProperty("language").GetString() ?? "";
            var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? code : code;
            if (!string.IsNullOrEmpty(code)) list.Add(new DeepLLanguage(code, name));
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }
}
