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

        // Stream lines instead of materializing one string object per channel/metadata row.
        while (reader.ReadLine() is { } raw)
        {
            ct.ThrowIfCancellationRequested();
            var line = raw.Trim().TrimStart('\ufeff');
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match match in AttributePattern().Matches(line)) attributes[match.Groups[1].Value] = match.Groups[2].Value;
                bool quoted = false;
                int comma = -1;
                for (int i = 0; i < line.Length; i++)
                {
                    if (line[i] == '"') quoted = !quoted;
                    else if (line[i] == ',' && !quoted) { comma = i; break; }
                }
                title = comma >= 0 ? line[(comma + 1)..].Trim() : "Unnamed channel";
                if (string.IsNullOrEmpty(title)) title = "Unnamed channel";
                group = attributes.GetValueOrDefault("group-title", "Uncategorized");
                logo = attributes.GetValueOrDefault("tvg-logo", "");
                tvgId = attributes.GetValueOrDefault("tvg-id", "");
            }
            else if (!line.StartsWith('#') && !string.IsNullOrWhiteSpace(line) && title != null)
            {
                channels.Add(new Channel
                {
                    Id = "m3u-" + ChannelPreferences.Hash(
                        (string.IsNullOrWhiteSpace(tvgId) ? line : tvgId) + "\n" + group + "\n" + title),
                    Name = title, Number = index++,
                    GroupId = group, GroupName = group, Logo = logo, EpgId = tvgId, Command = line
                });
                title = null;
            }
        }
        return channels;
    }
}
