using HnIptv.Models;
namespace HnIptv.Services;
public static class CatalogSnapshot
{
    public static bool Matches(IReadOnlyList<Channel> current, IReadOnlyList<Channel> incoming)
    {
        if (current.Count != incoming.Count) return false;
        for (int i = 0; i < current.Count; i++)
        {
            var a = current[i]; var b = incoming[i];
            if (a.Id != b.Id || a.Name != b.Name || a.Number != b.Number || a.GroupId != b.GroupId ||
                a.GroupName != b.GroupName || a.Logo != b.Logo || a.EpgId != b.EpgId || a.Command != b.Command ||
                a.UserAgent != b.UserAgent || a.Referer != b.Referer) return false;
        }
        return true;
    }
}
