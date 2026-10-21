using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace HnIptv.Translation;

// Text only. Keys are headers on the fixed HTTPS endpoint, never query parameters.
public sealed class AzureTranslatorClient
{
    public const string Endpoint = "https://api.cognitive.microsofttranslator.com";
    private static readonly HttpClient SharedHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private readonly HttpClient _http;
    private readonly string _key, _region;
    private readonly SemaphoreSlim _gate = new(1,1);
    private readonly Dictionary<(string Source,string Target,string Text),string> _cache = [];
    private readonly Queue<(string Source,string Target,string Text)> _order = new();
    private string? _blocked;
    private DateTimeOffset _retryAt;
    public AzureTranslatorClient(string key, string region = "global", HttpClient? http = null)
    {
        _key = key.Trim();
        if (string.IsNullOrWhiteSpace(_key) || _key.Length > 512 || _key.Any(char.IsWhiteSpace) || _key.Any(char.IsControl))
            throw new ArgumentException("Paste a valid Azure Translator key in the Azure settings tab.");
        _region = NormalizeRegion(region);
        _http = http ?? SharedHttp;
    }
    public static string NormalizeRegion(string? region)
    {
        var value = (region ?? "").Trim().ToLowerInvariant();
        if (value.Length == 0) return "global";
        if (!Regex.IsMatch(value, "^[a-z0-9-]{1,40}$"))
            throw new ArgumentException("Use the Azure region code, such as eastus, or global.");
        return value;
    }
    private static string Language(string value) => value switch {
        "zh" => "zh-Hans", "zt" => "zh-Hant", _ => value
    };
    public async Task<string> TranslateAsync(string text, string source, string target, CancellationToken ct = default)
    {
        text = text.Trim();
        if (text.Length == 0 || source == target) return text;
        if (text.Length > 5000 || !Regex.IsMatch(source,"^(auto|[a-z]{2,3})$") || !Regex.IsMatch(target,"^[a-z]{2,3}$"))
            throw new ArgumentException("Unsupported translation text or language code.");
        var cacheKey = (source,target,text);
        await _gate.WaitAsync(ct);
        try {
            if (_cache.TryGetValue(cacheKey,out var cached)) return cached;
            if (_blocked != null) throw new InvalidOperationException(_blocked);
            if (DateTimeOffset.UtcNow < _retryAt)
                throw new InvalidOperationException("Azure is rate limited. Waiting before another request.");
            var url = Endpoint+"/translate?api-version=3.0&to="+Uri.EscapeDataString(Language(target));
            if (source != "auto") url += "&from="+Uri.EscapeDataString(Language(source));
            using var request = new HttpRequestMessage(HttpMethod.Post,url);
            request.Headers.Add("Ocp-Apim-Subscription-Key",_key);
            if (_region != "global") request.Headers.Add("Ocp-Apim-Subscription-Region",_region);
            request.Content = JsonContent.Create(new[] { new Dictionary<string,string> { ["Text"] = text } });
            HttpResponseMessage response;
            try { response = await _http.SendAsync(request,ct); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                throw new InvalidOperationException("Azure translation timed out. Check your connection.");
            }
            catch (HttpRequestException) {
                throw new InvalidOperationException("Could not connect to Azure Translator. Check your connection.");
            }
            using (response) {
                if (!response.IsSuccessStatusCode) {
                    string error = response.StatusCode switch {
                        HttpStatusCode.Unauthorized => "Azure rejected the key. Check Key 1 or Key 2 and the region in Azure settings.",
                        HttpStatusCode.Forbidden => "Azure access was denied or the quota is exhausted. Check the resource, region and Azure usage.",
                        HttpStatusCode.TooManyRequests => "Azure translation is rate limited. Waiting before another request.",
                        HttpStatusCode.BadRequest => "Azure rejected this language pair or request. Check the input and output languages.",
                        _ => "Azure Translator is unavailable (HTTP "+(int)response.StatusCode+"). Try again later."
                    };
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) _blocked = error;
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                        _retryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 15,5,120));
                    throw new InvalidOperationException(error);
                }
                try {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    string result = json.RootElement[0].GetProperty("translations")[0].GetProperty("text").GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(result) || result.Length > 20000)
                        throw new InvalidOperationException("Azure returned an empty or oversized translation.");
                    _cache[cacheKey] = result; _order.Enqueue(cacheKey);
                    while (_order.Count > 256) _cache.Remove(_order.Dequeue());
                    return result;
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException) {
                    throw new InvalidOperationException("Azure returned an invalid translation response.");
                }
            }
        }
        finally { _gate.Release(); }
    }
}

