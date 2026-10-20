using System.Text.Json;
using HnIptv.Models;

namespace HnIptv.Services;

// Encrypt at the platform boundary: Windows implements this with DPAPI;
// Android can substitute Keystore without changing the portable library.
public interface IPlaylistProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

public sealed class PlaylistProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Playlist";
    public string Type { get; set; } = "m3u";
    public string ServerUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string PlaylistUrl { get; set; } = "";
    public string EpgSource { get; set; } = "";
    public string StreamFormat { get; set; } = "Automatic";
    public string SelectedGroupId { get; set; } = "*";
    public int ChannelRefreshHours { get; set; } = 6;
    public int EpgRefreshMinutes { get; set; } = 30;
    public int LogoRefreshHours { get; set; } = 24;
    public DateTimeOffset LastChannelRefresh { get; set; }
    public DateTimeOffset LastEpgRefresh { get; set; }
    public DateTimeOffset LastLogoRefresh { get; set; }
    public List<Channel> CachedChannels { get; set; } = new();
    public HashSet<string> HiddenGroupIds { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<ProgramEntry>> CachedProviderPrograms { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, DateTimeOffset> ProviderEpgFetchedAt { get; set; } = new(StringComparer.Ordinal);
    public static string EpgKey(string groupId, string channelId) => groupId + "\u001f" + channelId;
}

public sealed class PlaylistLibrary
{
    private readonly string _path;
    private readonly IPlaylistProtector? _protector;
    public List<PlaylistProfile> Playlists { get; private set; } = new();
    public string SelectedId { get; set; } = "";
    public PlaylistLibrary(string path, IPlaylistProtector? protector = null)
    {
        _path = path;
        _protector = protector;
    }
    public void Load()
    {
        if (!File.Exists(_path)) return;
        // Do not overwrite an unreadable or corrupt vault with a blank library.
        using var input = _protector == null
            ? (Stream)File.OpenRead(_path)
            : new MemoryStream(_protector.Unprotect(File.ReadAllBytes(_path)));
        var doc = JsonSerializer.Deserialize<LibraryDocument>(input)
            ?? throw new InvalidDataException("The saved playlist library could not be read.");
        if (doc.Playlists == null || doc.Playlists.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id) ||
            p.CachedChannels == null || p.CachedChannels.Any(ch => ch == null) || p.HiddenGroupIds == null ||
            p.CachedProviderPrograms == null || p.CachedProviderPrograms.Any(kv => kv.Value == null) || p.ProviderEpgFetchedAt == null))
            throw new InvalidDataException("Invalid playlist library; the original file was preserved.");
        Playlists = doc.Playlists;
        SelectedId = doc.SelectedId ?? "";
    }
    public sealed record LibrarySnapshot(string SelectedId, List<PlaylistProfile> Playlists);

    public LibrarySnapshot CaptureSnapshot() => new(SelectedId, Playlists.Select(p => new PlaylistProfile
    {
        Id = p.Id, Name = p.Name, Type = p.Type, ServerUrl = p.ServerUrl,
        Username = p.Username, Password = p.Password,
        LocalPath = p.LocalPath, PlaylistUrl = p.PlaylistUrl, EpgSource = p.EpgSource, StreamFormat = p.StreamFormat, SelectedGroupId = p.SelectedGroupId,
        ChannelRefreshHours = p.ChannelRefreshHours, EpgRefreshMinutes = p.EpgRefreshMinutes,
        LogoRefreshHours = p.LogoRefreshHours, LastChannelRefresh = p.LastChannelRefresh,
        LastEpgRefresh = p.LastEpgRefresh, LastLogoRefresh = p.LastLogoRefresh,
        // Catalog metadata is immutable after publication. UI-only channel flags are
        // persisted separately, so no O(channel-count) copy is needed on the dispatcher.
        CachedChannels = p.CachedChannels,
        ProviderEpgFetchedAt = new(p.ProviderEpgFetchedAt, StringComparer.Ordinal),
        HiddenGroupIds = new HashSet<string>(p.HiddenGroupIds, StringComparer.Ordinal),
        CachedProviderPrograms = p.CachedProviderPrograms.ToDictionary(kv => kv.Key,
            kv => kv.Value.ToList(), StringComparer.Ordinal)
    }).ToList());

    public void Save() => SaveSnapshot(CaptureSnapshot());

    public void SaveSnapshot(LibrarySnapshot snapshot)
    {
        var document = new LibraryDocument(snapshot.SelectedId, snapshot.Playlists);
        AtomicFile.Write(_path, stream =>
        {
            if (_protector == null) JsonSerializer.Serialize(stream, document);
            else stream.Write(_protector.Protect(JsonSerializer.SerializeToUtf8Bytes(document)));
        });
    }
    private sealed record LibraryDocument(string? SelectedId, List<PlaylistProfile>? Playlists);
}
