using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using HnIptv.Services;
namespace HnIptv;

internal static class AppUpdater
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };
    internal static async Task<string> PrepareAsync(AppRelease release, bool portable, CancellationToken ct)
    {
        string folder = Path.Combine(Path.GetTempPath(), "HN-IPTV-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string package = Path.Combine(folder, release.AssetName);
        using var response = await Client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        const long limit = 1024L * 1024 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Update package is too large.");
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(package))
        {
            var buffer = new byte[81920]; long total = 0; int count;
            while ((count = await input.ReadAsync(buffer, ct)) != 0)
            {
                total += count;
                if (total > limit) throw new InvalidDataException("Update package is too large.");
                await output.WriteAsync(buffer.AsMemory(0, count), ct);
            }
        }
        await PackageIntegrity.VerifyAsync(package, release.Sha256, ct);
        if (!portable) return package;
        string staging = Path.Combine(folder, "staged");
        await Task.Run(() => {
            using var zip = ZipFile.OpenRead(package);
            if (zip.Entries.Count > 10000 || zip.Entries.Sum(e => e.Length) > 2L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Invalid update archive.");
            ZipFile.ExtractToDirectory(package, staging);
        }, ct);
        string source = Path.Combine(staging, "HN-IPTV-Portable");
        if (!File.Exists(Path.Combine(source, "HN-IPTV.exe")) || !File.Exists(Path.Combine(source, "portable.mode")))
            throw new InvalidDataException("Portable update is incomplete.");
        string destination = Path.GetFullPath(AppContext.BaseDirectory);
        // Test write access before closing the player.
        string probe = Path.Combine(destination, ".update-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(probe, ""); File.Delete(probe);
        string manifest = Path.Combine(folder, "update.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new {
            source, destination, process = Environment.ProcessId, backup = Path.Combine(folder, "backup")
        }));
        string script = Path.Combine(folder, "apply-update.ps1");
        File.WriteAllText(script, PortableScript);
        return script;
    }
    internal static void Start(string prepared, bool portable)
    {
        if (!portable) { Process.Start(new ProcessStartInfo(prepared) { UseShellExecute = true }); return; }
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", prepared })
            info.ArgumentList.Add(arg);
        _ = Process.Start(info) ?? throw new IOException("Could not start the update helper.");
    }
    private const string PortableScript = """
param([switch]$NoRestart)
$ErrorActionPreference = 'Stop'
$job = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'update.json') -Raw | ConvertFrom-Json
Wait-Process -Id $job.process -ErrorAction SilentlyContinue
$sourceRoot = [IO.Path]::GetFullPath($job.source).TrimEnd('\') + '\'
$destinationRoot = [IO.Path]::GetFullPath($job.destination).TrimEnd('\') + '\'
$backupRoot = [IO.Path]::GetFullPath($job.backup).TrimEnd('\') + '\'
$replaced = @()
try {
    $files = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse | Sort-Object FullName)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($sourceRoot.Length)
        if ($relative -eq 'data' -or $relative.StartsWith('data\', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $target = [IO.Path]::GetFullPath((Join-Path $destinationRoot $relative))
        $backup = [IO.Path]::GetFullPath((Join-Path $backupRoot $relative))
        if (-not $target.StartsWith($destinationRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not $backup.StartsWith($backupRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid update path.' }
        # Refuse redirected directories so an update cannot overwrite external files.
        $parent = [IO.DirectoryInfo]([IO.Path]::GetDirectoryName($target))
        while ($null -ne $parent -and $parent.FullName.StartsWith($destinationRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
            if ($parent.Exists -and ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Update directory is redirected.' }
            $parent = $parent.Parent
        }
        if ((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Update file is redirected.' }
        $existed = Test-Path -LiteralPath $target
        if ($existed) {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null
            Copy-Item -LiteralPath $target -Destination $backup -Force
        }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        $replaced += [pscustomobject]@{ target = $target; backup = $backup; existed = $existed }
        for ($attempt = 0; ; $attempt++) {
            try { Copy-Item -LiteralPath $file.FullName -Destination $target -Force; break }
            catch { if ($attempt -ge 19) { throw }; Start-Sleep -Milliseconds 500 }
        }
    }
    if (-not $NoRestart) { Start-Process -FilePath (Join-Path $destinationRoot 'HN-IPTV.exe') -WorkingDirectory $destinationRoot }
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'update-error.txt')
    foreach ($file in $replaced) {
        if ($file.existed) { Copy-Item -LiteralPath $file.backup -Destination $file.target -Force -ErrorAction Continue }
        elseif (Test-Path -LiteralPath $file.target) { Remove-Item -LiteralPath $file.target -Force -ErrorAction Continue }
    }
    if ($NoRestart) { throw }
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show('The update could not complete. Previous files were restored. Details: ' + $PSScriptRoot, 'HN-IPTV update')
}
""";
}
