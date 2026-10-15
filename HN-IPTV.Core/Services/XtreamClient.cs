using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using HnIptv.Models;

namespace HnIptv.Services;

/// <summary>
/// Platform-neutral client for the Xtream-compatible player_api.php endpoint.
/// Credentials remain in memory, are never logged by this client, and should only
/// be used with a service that the user is authorized to access.
/// </summary>
public sealed class XtreamClient : IDisposable
{
    private readonly Uri _serverRoot;
    private readonly string _username;
    private readonly string _password;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly XtreamStreamFormat _requestedFormat;
    private bool _connected;
    private string _extension = "ts";

    public string SelectedFormat => _extension == "m3u8" ? "HLS (.m3u8)" : "MPEG-TS (.ts)";

    public XtreamClient(string serverUrl, string username, string password,
        XtreamStreamFormat format = XtreamStreamFormat.Automatic, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("An Xtream username and password are required.");
        _serverRoot = NormalizeServerUrl(serverUrl);
        _username = username.Trim();
        _password = password; // Passwords are not trimmed; whitespace may be significant.
        _requestedFormat = format;
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate |
                                     DecompressionMethods.Brotli
        }) { Timeout = TimeSpan.FromSeconds(25) };
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        using var account = await GetJsonAsync(null, null, ct).ConfigureAwait(false);
        if (account.RootElement.ValueKind != JsonValueKind.Object ||
            !account.RootElement.TryGetProperty("user_info", out var userInfo) ||
            userInfo.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The server did not return Xtream account information.");

        if (!IsAuthorized(userInfo))
            throw new UnauthorizedAccessException("Xtream authentication failed. Check the username, password and account status.");

        var status = ReadText(userInfo, "status");
        if (status.Equals("Expired", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Banned", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"The Xtream account is {status.ToLowerInvariant()}.");

        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (userInfo.TryGetProperty("allowed_output_formats", out var formats) &&
            formats.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in formats.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } value)
                    available.Add(value);
        }

        var wanted = _requestedFormat switch
        {
            XtreamStreamFormat.Hls => "m3u8",
            XtreamStreamFormat.MpegTs => "ts",
            _ => available.Contains("ts") || available.Count == 0 ? "ts" :
                available.Contains("m3u8") ? "m3u8" : "ts"
        };
        if (available.Count > 0 && !available.Contains(wanted))
            throw new InvalidOperationException("The provider does not allow the selected output format. Choose Automatic or another format.");

        _extension = wanted;
        _connected = true;
    }

    public async Task<(List<Channel> Channels, List<ChannelGroup> Groups)> GetLiveTvAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var groups = new List<ChannelGroup>();
        var seenGroups = new HashSet<string>(StringComparer.Ordinal);
        // Category endpoints are optional on some Xtream-compatible services.
        try
        {
            using var categoryDoc = await GetJsonAsync("get_live_categories", null, ct).ConfigureAwait(false);
            if (categoryDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var category in categoryDoc.RootElement.EnumerateArray())
                {
                    var id = ReadText(category, "category_id");
                    var name = ReadText(category, "category_name");
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrWhiteSpace(name) &&
                        seenGroups.Add(id))
                        groups.Add(new ChannelGroup(id, name));
                }
            }
        }
        catch (HttpRequestException) when (!ct.IsCancellationRequested) { }
        catch (InvalidOperationException) when (!ct.IsCancellationRequested) { }
        catch (JsonException) when (!ct.IsCancellationRequested) { }

        using var streamDoc = await GetJsonAsync("get_live_streams", null, ct).ConfigureAwait(false);
        if (streamDoc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The provider returned an invalid live-channel list.");
        var groupNames = groups.ToDictionary(g => g.Id, g => g.Name, StringComparer.Ordinal);
        var channels = new List<Channel>();
        foreach (var item in streamDoc.RootElement.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = ReadText(item, "stream_id");
            var name = ReadText(item, "name");
            if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(name)) continue;
            var groupId = ReadText(item, "category_id");
            if (string.IsNullOrWhiteSpace(groupId)) groupId = "uncategorized";
            if (!groupNames.TryGetValue(groupId, out var groupName))
            {
                groupName = "Uncategorized";
                groupNames.Add(groupId, groupName);
                groups.Add(new ChannelGroup(groupId, groupName));
            }
            var number = int.TryParse(ReadText(item, "num"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var num) && num > 0 ? num : channels.Count + 1;
            channels.Add(new Channel
            {
                Id = id, Name = name, Number = number,
                GroupId = groupId, GroupName = groupName,
                Logo = ReadText(item, "stream_icon"),
                EpgId = ReadText(item, "epg_channel_id"),
                // Do not embed username/password in the channel model.
                Command = ""
            });
        }
        if (channels.Count == 0)
            throw new InvalidOperationException("Login succeeded, but the provider returned no live channels.");
        return (channels.OrderBy(ch => ch.Number).ThenBy(ch => ch.Name).ToList(), groups);
    }

    public Uri ResolveLiveStream(Channel channel)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(channel.Id) || !long.TryParse(channel.Id,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var streamId) || streamId <= 0)
            throw new ArgumentException("This channel has no valid Xtream stream ID.", nameof(channel));
        var relative = "live/" + Uri.EscapeDataString(_username) + "/" +
            Uri.EscapeDataString(_password) + "/" + streamId.ToString(CultureInfo.InvariantCulture) + "." + _extension;
        return new Uri(_serverRoot, relative);
    }

    public async Task<IReadOnlyList<ProgramEntry>> GetEpgAsync(string streamId, CancellationToken ct = default)
    {
        EnsureConnected();
        using var doc = await GetJsonAsync("get_short_epg", new Dictionary<string, string>
        {
            ["stream_id"] = streamId, ["limit"] = "12"
        }, ct).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("epg_listings", out var listings) ||
            listings.ValueKind != JsonValueKind.Array)
            return [];

        var programs = new List<ProgramEntry>();
        foreach (var item in listings.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var title = DecodePossibleBase64(ReadText(item, "title"));
            if (string.IsNullOrWhiteSpace(title)) continue;
            programs.Add(new ProgramEntry(title,
                DecodePossibleBase64(ReadText(item, "description")),
                ParseUnixTime(item, "start_timestamp"), ParseUnixTime(item, "stop_timestamp")));
        }
        return programs;
    }

    private async Task<JsonDocument> GetJsonAsync(string? action,
        IReadOnlyDictionary<string, string>? additional, CancellationToken ct)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("username", _username), new("password", _password)
        };
        if (!string.IsNullOrEmpty(action)) query.Add(new("action", action));
        if (additional != null) query.AddRange(additional);
        var queryString = string.Join("&", query.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        var api = new Uri(_serverRoot, "player_api.php?" + queryString);
        using var request = new HttpRequestMessage(HttpMethod.Get, api);
        request.Headers.UserAgent.ParseAdd("HN-IPTV/0.7");
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Xtream server returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");
            await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(body, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (HttpRequestException) when (!ct.IsCancellationRequested)
        {
            // Never include the credential-bearing request URL in an exception.
            throw new InvalidOperationException("Unable to reach the Xtream API. Check the server address and network connection.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("The Xtream server did not respond in time.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The server returned an invalid API response instead of Xtream JSON.");
        }
    }

    private static Uri NormalizeServerUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Enter a complete http:// or https:// Xtream server URL.", nameof(value));
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(".php", StringComparison.OrdinalIgnoreCase))
            path = path[..path.LastIndexOf('/')];
        var builder = new UriBuilder(uri) { Path = path.TrimEnd('/') + "/", Query = "", Fragment = "" };
        return builder.Uri;
    }

    private static bool IsAuthorized(JsonElement userInfo) =>
        userInfo.TryGetProperty("auth", out var auth) &&
        (auth.ValueKind == JsonValueKind.True || ReadValue(auth) == "1");

    private static string ReadText(JsonElement item, string property) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value)
            ? ReadValue(value) : "";

    private static string ReadValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "1",
        JsonValueKind.False => "0",
        _ => ""
    };

    private static DateTimeOffset? ParseUnixTime(JsonElement item, string key)
    {
        if (!long.TryParse(ReadText(item, key), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var seconds) || seconds <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime(); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static string DecodePossibleBase64(string text)
    {
        if (text.Length < 4 || text.Length % 4 != 0) return text;
        try
        {
            var bytes = Convert.FromBase64String(text);
            var decoded = new UTF8Encoding(false, true).GetString(bytes);
            return decoded.Any(char.IsControl) ? text : decoded;
        }
        catch (FormatException) { return text; }
        catch (DecoderFallbackException) { return text; }
    }

    private void EnsureConnected()
    {
        if (!_connected) throw new InvalidOperationException("Connect to the Xtream account first.");
    }

    public void Dispose()
    {
        _connected = false;
        if (_ownsHttp) _http.Dispose();
    }
}

public enum XtreamStreamFormat
{
    Automatic,
    MpegTs,
    Hls
}
