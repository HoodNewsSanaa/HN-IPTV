using HnIptv.Models;

namespace HnIptv.Navigation;

/// <summary>
/// UI-independent filtering shared by desktop, TV and handheld layouts.
/// TV D-pad selection and touch gestures belong to the platform UI, not here.
/// </summary>
public static class ChannelCatalog
{
    public static bool Includes(Channel channel, string? groupId, string? search)
    {
        if (!channel.IsVisible) return false;
        if (groupId == "fav" && !channel.IsFavorite) return false;
        if (!string.IsNullOrWhiteSpace(groupId) && groupId is not ("*" or "fav")
            && !string.Equals(groupId, channel.GroupId, StringComparison.Ordinal)) return false;
        return string.IsNullOrWhiteSpace(search) ||
            channel.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<ChannelGroup> GroupsFor(IReadOnlyList<Channel> channels)
        => [new("*", "All Channels"), new("fav", "★  Favorites"),
            .. channels.Select(ch => new ChannelGroup(ch.GroupId, ch.GroupName))
                .DistinctBy(g => g.Id).OrderBy(g => g.Name)];
}
