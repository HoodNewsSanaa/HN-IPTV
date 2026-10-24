using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace HnIptv.Services;

public sealed record AppRelease(Version Version, string PageUrl, string AssetName, string DownloadUrl, string Sha256, bool HasAssetDigest = false);

public sealed class GitHubUpdates(HttpClient client)
{
    public const string Repository = "HoodNewsSanaa/HN-IPTV";
    public async Task<AppRelease?> CheckAsync(Version current, bool portable, CancellationToken ct, bool featureRich = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repository + (featureRich ? "/releases?per_page=30" : "/releases/latest"));
        request.Headers.UserAgent.ParseAdd("HN-IPTV/" + current);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 2_000_000) throw new InvalidDataException("Release response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[16384]; int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0) {
            if (bytes.Length + count > 2_000_000) throw new InvalidDataException("Release response is too large.");
            bytes.Write(buffer, 0, count);
        }
        var json = System.Text.Encoding.UTF8.GetString(bytes.GetBuffer(),0,(int)bytes.Length);
        if (!featureRich) return Parse(json, current, portable);
        using var releases = JsonDocument.Parse(json);
        return releases.RootElement.EnumerateArray().Select(item => Parse(item.GetRawText(), current, portable, true))
            .Where(item => item != null).OrderByDescending(item => item!.Version).FirstOrDefault();
    }
    public static AppRelease? Parse(string json, Version current, bool portable, bool featureRich = false)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || (!featureRich && root.GetProperty("prerelease").GetBoolean())) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        const string previewPrefix = "feature-rich-v";
        if (featureRich && !tag.StartsWith(previewPrefix, StringComparison.Ordinal)) return null;
        string versionText = featureRich ? tag[previewPrefix.Length..] : tag.TrimStart('v');
        if (!Version.TryParse(versionText, out var version) ||
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
        var asset=root.GetProperty("assets").EnumerateArray().Single(a=>a.GetProperty("name").GetString()==name);
        bool hasDigest=asset.TryGetProperty("digest",out var digest)&&digest.ValueKind==JsonValueKind.String;
        if(hasDigest&&!string.Equals(digest.GetString(),"sha256:"+match.Groups[1].Value,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Release checksum conflicts with GitHub asset digest.");
        return new AppRelease(version, "https://github.com/" + Repository + "/releases/tag/" + Uri.EscapeDataString(tag),
            name, url!, match.Groups[1].Value.ToUpperInvariant(),hasDigest);
    }
    public static void ValidateAsset(AppRelease release,bool portable)
    {
        string name=portable?"HN-IPTV-Portable.zip":"HN-IPTV-Setup.exe";
        string tag="feature-rich-v"+release.Version;
        if(!release.PageUrl.EndsWith("/"+tag,StringComparison.Ordinal))tag="v"+release.Version;
        if(release.AssetName!=name||!TrustedAsset(release.DownloadUrl,tag,name)||
            release.PageUrl!="https://github.com/"+Repository+"/releases/tag/"+tag||
            !Regex.IsMatch(release.Sha256,@"\A[a-fA-F0-9]{64}\z"))throw new InvalidDataException("Untrusted update package.");
    }
    private static bool TrustedAsset(string? url, string tag, string name) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com" &&
        uri.AbsolutePath == "/" + Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + name &&
        string.IsNullOrEmpty(uri.Query)&&string.IsNullOrEmpty(uri.Fragment)&&string.IsNullOrEmpty(uri.UserInfo)&&uri.IsDefaultPort;
}
