using System.Text.RegularExpressions;
using HnIptv.Models;
using HnIptv.Preferences;

namespace HnIptv.Services;

public static partial class M3uService
{
    [GeneratedRegex("([\\w-]+)=\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AttributePattern();

    public static IReadOnlyList<Channel> Parse(string playlist, CancellationToken ct = default)
    {
        using var reader = new StringReader(playlist);
        return Parse(reader, ct);
    }

    public static Task<IReadOnlyList<Channel>> ParseFileAsync(string path, CancellationToken ct = default)
        => Task.Run(() => { using var reader = File.OpenText(path); return Parse(reader, ct); }, ct);

    public static IReadOnlyList<Channel> Parse(TextReader reader, CancellationToken ct = default)
    {
        var channels = new List<Channel>();
        string? title = null;
        string group = "Uncategorized";
        string logo = "";
        string tvgId = "";
        int index = 1;
        string userAgent = "", referer = "";

        // Stream lines instead of materializing one string object per channel/metadata row.
        while (reader.ReadLine() is { } raw)
        {
            ct.ThrowIfCancellationRequested();
            var line = raw.Trim().TrimStart('\ufeff');
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                bool quoted = false;
                int comma = -1;
                for (int i = 0; i < line.Length; i++)
                {
                    if (line[i] == '"') quoted = !quoted;
                    else if (line[i] == ',' && !quoted) { comma = i; break; }
                }
                title = comma >= 0 ? line[(comma + 1)..].Trim() : "Unnamed channel";
                if (string.IsNullOrEmpty(title)) title = "Unnamed channel";
                group = "Uncategorized"; logo = ""; tvgId = ""; userAgent = ""; referer = "";
                // Enumerate value matches over metadata only: no per-channel
                // dictionary, Match/Group objects or allocations for unused tags.
                var metadata = line.AsSpan(0, comma >= 0 ? comma : line.Length);
                foreach (var match in AttributePattern().EnumerateMatches(metadata))
                {
                    var attribute = metadata.Slice(match.Index, match.Length);
                    int equal = attribute.IndexOf('=');
                    var name = attribute[..equal];
                    var value = attribute[(equal + 2)..^1];
                    if (name.Equals("group-title", StringComparison.OrdinalIgnoreCase)) group = value.ToString();
                    else if (name.Equals("tvg-logo", StringComparison.OrdinalIgnoreCase)) logo = value.ToString();
                    else if (name.Equals("tvg-id", StringComparison.OrdinalIgnoreCase)) tvgId = value.ToString();
                }
            }
            else if (title != null && line.StartsWith("#EXTVLCOPT:http-user-agent=", StringComparison.OrdinalIgnoreCase))
                userAgent = line["#EXTVLCOPT:http-user-agent=".Length..].Trim();
            else if (title != null && line.StartsWith("#EXTVLCOPT:http-referrer=", StringComparison.OrdinalIgnoreCase))
                referer = line["#EXTVLCOPT:http-referrer=".Length..].Trim();
            else if (!line.StartsWith('#') && !string.IsNullOrWhiteSpace(line) && title != null)
            {
                channels.Add(new Channel
                {
                    Id = "m3u-" + ChannelPreferences.Hash(
                        (string.IsNullOrWhiteSpace(tvgId) ? line : tvgId) + "\n" + group + "\n" + title),
                    Name = title, Number = index++,
                    GroupId = group, GroupName = group, Logo = logo, EpgId = tvgId, Command = line, UserAgent = userAgent, Referer = referer
                });
                title = null;
            }
        }
        return channels;
    }
}
