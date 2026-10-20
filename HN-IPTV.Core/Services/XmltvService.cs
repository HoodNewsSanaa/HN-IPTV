using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using HnIptv.Models;
namespace HnIptv.Services;

public static class XmltvService
{
    public static Dictionary<string, List<ProgramEntry>> Parse(Stream input, IEnumerable<string> channelIds, CancellationToken ct = default)
    {
        var wanted = channelIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, List<ProgramEntry>>(StringComparer.Ordinal);
        using var reader = XmlReader.Create(input, new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 128 * 1024 * 1024,
            IgnoreComments = true, IgnoreWhitespace = true });
        var now = DateTimeOffset.UtcNow;
        int count = 0;
        while (!reader.EOF)
        {
            ct.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "programme") { reader.Read(); continue; }
            if (++count > 1_000_000) throw new InvalidDataException("Guide contains too many programmes.");
            string id = reader.GetAttribute("channel") ?? "";
            if (!wanted.Contains(id)) { reader.Skip(); continue; }
            var node = (XElement)XNode.ReadFrom(reader);
            if (!TryTime((string?)node.Attribute("start"), out var start) ||
                !TryTime((string?)node.Attribute("stop"), out var end) || end <= start ||
                end < now.AddDays(-1) || start > now.AddDays(14)) continue;
            if (!result.TryGetValue(id, out var programs)) result[id] = programs = new();
            if (programs.Count < 1000)
                programs.Add(new ProgramEntry(node.Element("title")?.Value ?? "Untitled",
                    node.Element("desc")?.Value ?? "", start.ToLocalTime(), end.ToLocalTime()));
        }
        foreach (var programs in result.Values) programs.Sort((a,b) => Nullable.Compare(a.Start, b.Start));
        return result;
    }
    private static bool TryTime(string? value, out DateTimeOffset time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string stamp = parts[0], offset = parts.Length > 1 ? parts[1] : "+0000";
        if (offset.Length == 5 && offset[0] is '+' or '-') offset = offset.Insert(3, ":");
        return DateTimeOffset.TryParseExact(stamp + " " + offset,
            new[] { "yyyyMMddHHmmss zzz", "yyyyMMddHHmm zzz" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }
}
