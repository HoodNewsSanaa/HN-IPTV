using HnIptv.Models;

namespace HnIptv.Navigation;

public sealed class ChannelIndex(IReadOnlyList<Channel> channels)
{
    private readonly Dictionary<string, List<Channel>> _groups = channels.GroupBy(c => c.GroupId)
        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    public List<Channel> Filter(string group, string query, IReadOnlySet<string> hidden, CancellationToken ct = default)
    {
        query = query.Trim();
        IReadOnlyList<Channel> candidates = group is "*" or "fav" ? channels
            : _groups.GetValueOrDefault(group) ?? (IReadOnlyList<Channel>)Array.Empty<Channel>();
        var result = new List<Channel>();
        foreach (var channel in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!channel.IsVisible || hidden.Contains(channel.GroupId) || (group == "fav" && !channel.IsFavorite)) continue;
            if (query.Length == 0 || channel.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) result.Add(channel);
        }
        return result;
    }
}
