using System.IO;
using System.Windows;
using HnIptv.Services;

namespace HnIptv;

internal static class AppStorage
{
    internal static string? TestDataDirectory { get; set; }
    public static bool IsPortable { get; private set; }
    public static string DataDirectory { get; private set; } = "";
    public static string PlaylistPath => Path.Combine(DataDirectory, IsPortable ? "saved-playlists.json" : "saved-playlists.dpapi");
    public static bool HasLegacyVault { get; private set; }
    public static PlaylistLibrary CreateLibrary() => new(PlaylistPath,
        IsPortable ? null : new WindowsDpapiProtector());

    public static void Initialize()
    {
        IsPortable = TestDataDirectory != null || File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.mode"));
        DataDirectory = TestDataDirectory ?? (IsPortable ? Path.Combine(AppContext.BaseDirectory, "data") :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HN-IPTV"));
        HasLegacyVault = IsPortable && File.Exists(Path.Combine(DataDirectory, "saved-playlists.vault"));
        Directory.CreateDirectory(DataDirectory);
        // Verify writability before accepting new playlists. No password or vault code.
        string probe = Path.Combine(DataDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        using (File.Create(probe)) { }
        File.Delete(probe);
        if (IsPortable) return; // Keep legacy encrypted files untouched; see PORTABILITY.md.
        var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaTV");
        foreach (var file in new[] { "saved-playlists.dpapi", "channel-preferences.json" })
        {
            string source = Path.Combine(old, file), destination = Path.Combine(DataDirectory, file);
            if (!File.Exists(destination) && File.Exists(source)) File.Copy(source, destination);
        }
    }

    public static string ImportM3u(string path, string profileId)
    {
        if (!IsPortable) return Path.GetFullPath(path);
        string fullSource = Path.GetFullPath(path);
        string existingRoot = Path.GetFullPath(Path.Combine(DataDirectory, "playlists")) + Path.DirectorySeparatorChar;
        if (fullSource.StartsWith(existingRoot, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(DataDirectory, fullSource); // Preserve the old portable favorites identity.
        string folder = Path.Combine(DataDirectory, "playlists");
        Directory.CreateDirectory(folder);
        string filename = profileId + ".m3u";
        string destination = Path.Combine(folder, filename);
        if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            File.Copy(path, destination, overwrite: true);
        return Path.Combine("playlists", filename);
    }
    public static string ResolveM3u(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        var full = Path.GetFullPath(Path.Combine(DataDirectory, path));
        var root = Path.GetFullPath(DataDirectory) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid playlist path.");
        return full;
    }
}
