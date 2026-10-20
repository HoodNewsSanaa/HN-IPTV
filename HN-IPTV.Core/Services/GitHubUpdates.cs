using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace HnIptv.Services;

public sealed record AppRelease(Version Version, string PageUrl, string AssetName, string DownloadUrl, string Sha256);

public sealed class GitHubUpdates(HttpClient client)
{
    public const string Repository = "HoodNewsSanaa/HN-IPTV";
    public async Task<AppRelease?> CheckAsync(Version current, bool portable, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repository + "/releases/latest");
        request.Headers.UserAgent.ParseAdd("HN-IPTV/" + current);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 2_000_000) throw new InvalidDataException("Release response is too large.");
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (json.Length > 2_000_000) throw new InvalidDataException("Release response is too large.");
        return Parse(json, current, portable);
    }
    public static AppRelease? Parse(string json, Version current, bool portable)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v'), out var version) ||
            new Version(version.Major, version.Minor, Math.Max(0, version.Build)) <=
            new Version(current.Major, current.Minor, Math.Max(0, current.Build))) return null;
        string name = portable ? "HN-IPTV-Portable.zip" : "HN-IPTV-Setup.exe";
        string? url = root.GetProperty("assets").EnumerateArray()
            .Where(a => a.GetProperty("name").GetString() == name)
            .Select(a => a.GetProperty("browser_download_url").GetString()).FirstOrDefault();
        if (!TrustedAsset(url, tag, name)) throw new InvalidDataException("Release package is missing.");
        var body = root.TryGetProperty("body", out var notes) ? notes.GetString() ?? "" : "";
        var match = Regex.Match(body, @"(?im)^([a-f0-9]{64})[ \t]+\*?" + Regex.Escape(name) + @"[ \t]*\r?$");
        if (!match.Success) throw new InvalidDataException("Release has no package checksum.");
        return new AppRelease(version, "https://github.com/" + Repository + "/releases/tag/" + Uri.EscapeDataString(tag),
            name, url!, match.Groups[1].Value.ToUpperInvariant());
    }
    private static bool TrustedAsset(string? url, string tag, string name) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com" &&
        uri.AbsolutePath == "/" + Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + name &&
        string.IsNullOrEmpty(uri.Query);
}
