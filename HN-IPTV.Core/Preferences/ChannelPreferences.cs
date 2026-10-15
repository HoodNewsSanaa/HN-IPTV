using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HnIptv.Models;
using HnIptv.Services;

namespace HnIptv.Preferences;

/// <summary>
/// Cross-platform preferences format. Persist channel flags under a SHA-256 hash of
/// the provider identity, never the raw portal MAC or Xtream password.
/// </summary>
public sealed class ChannelPreferences
{
    private readonly string _filePath;
    private Dictionary<string, Dictionary<string, ChannelFlags>> _providers = new(StringComparer.Ordinal);

    public ChannelPreferences(string filePath) => _filePath = filePath;
    public bool LoadFailed { get; private set; }
    public void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            using var input = File.OpenRead(_filePath);
            _providers = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ChannelFlags>>>(input)
                ?? throw new JsonException();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { LoadFailed = true; throw; }
    }

    public static string ProviderKey(string providerType, string identity)
        => Hash(providerType + "\n" + identity.Trim());

    public static string Hash(string input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    public void Apply(string providerKey, IEnumerable<Channel> channels)
    {
        if (!_providers.TryGetValue(providerKey, out var values)) return;
        foreach (var channel in channels)
        {
            if (values.TryGetValue(channel.Id, out var flags))
            {
                channel.IsFavorite = flags.Favorite;
                // Legacy per-channel hiding was removed in Update 4. Group visibility
                // is now stored separately in PlaylistProfile.HiddenGroupIds.
                channel.IsVisible = true;
            }
        }
    }

    public void SetFavorite(string providerKey, Channel channel)
    {
        if (string.IsNullOrEmpty(providerKey)) return;
        if (!_providers.TryGetValue(providerKey, out var flags))
            _providers[providerKey] = flags = new(StringComparer.Ordinal);
        if (channel.IsFavorite) flags[channel.Id] = new(true, true);
        else flags.Remove(channel.Id);
    }
    public Dictionary<string, Dictionary<string, ChannelFlags>> CaptureSnapshot() => _providers.ToDictionary(
        kv => kv.Key, kv => new Dictionary<string, ChannelFlags>(kv.Value, StringComparer.Ordinal), StringComparer.Ordinal);
    public void SaveSnapshot(Dictionary<string, Dictionary<string, ChannelFlags>> snapshot)
    {
        if (LoadFailed) throw new InvalidDataException("Unreadable preferences were preserved.");
        AtomicFile.Write(_filePath, stream => JsonSerializer.Serialize(stream, snapshot));
    }
    public void Save(string providerKey, IEnumerable<Channel> channels)
    {
        foreach (var channel in channels) SetFavorite(providerKey, channel);
        SaveSnapshot(CaptureSnapshot());
    }

    public sealed record ChannelFlags(bool Favorite, bool Visible);
}
