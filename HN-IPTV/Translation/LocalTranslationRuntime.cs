using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using HnIptv.Services;
using HnIptv.Translation;
namespace HnIptv;

internal static class LocalTranslationRuntime
{
    internal static string Root => Path.Combine(AppStorage.DataDirectory, "translation");
    internal static string Python => Path.Combine(Root, "python", "python.exe");
    internal static string Models => Path.Combine(Root, "models");
    internal static string Worker => Path.Combine(AppContext.BaseDirectory, "TranslationRuntime", "worker.py");
    internal static bool Ready => File.Exists(Python) && File.Exists(Path.Combine(Models, "ready.json"));
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(20) };
    private const string PythonHash = "4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3";

    internal static async Task InstallAsync(TranslationSettings settings, Action<string> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        if (!File.Exists(Python))
        {
            progress("Downloading the private Python runtime...");
            string archive = Path.Combine(Root, "python.zip");
            await DownloadAsync("https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip", archive, ct);
            await PackageIntegrity.VerifyAsync(archive, PythonHash, ct);
            ZipFile.ExtractToDirectory(archive, Path.GetDirectoryName(Python)!, true);
            File.Delete(archive);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Python)!, "python312._pth"),
                "python312.zip\n.\nLib/site-packages\nimport site\n");
        }
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(Python)!, "Lib", "site-packages", "pip", "__main__.py")))
        {
            progress("Preparing the private package installer...");
            // Pin pip and verify its published PyPI SHA-256 before execution.
            using var metadata = JsonDocument.Parse(await Http.GetStringAsync("https://pypi.org/pypi/pip/25.1.1/json", ct));
            var wheel = metadata.RootElement.GetProperty("urls").EnumerateArray()
                .First(x => x.GetProperty("filename").GetString()!.EndsWith("py3-none-any.whl"));
            string archive = Path.Combine(Root, "pip.whl");
            await DownloadAsync(wheel.GetProperty("url").GetString()!, archive, ct);
            await PackageIntegrity.VerifyAsync(archive, wheel.GetProperty("digests").GetProperty("sha256").GetString()!, ct);
            string site = Path.Combine(Path.GetDirectoryName(Python)!, "Lib", "site-packages");
            Directory.CreateDirectory(site); ZipFile.ExtractToDirectory(archive, site, true); File.Delete(archive);
        }
        string ready = Path.Combine(Root, "dependencies-v1.ready");
        if (!File.Exists(ready))
        {
            progress("Installing CPU translation and recognition libraries. This may take several minutes...");
            await RunAsync(["-m", "pip", "install", "--disable-pip-version-check", "setuptools==80.9.0", "wheel==0.45.1"], progress, ct);
            await RunAsync(["-m", "pip", "install", "--disable-pip-version-check", "--index-url", "https://download.pytorch.org/whl/cpu",
                "torch==2.8.0", "torchvision==0.23.0"], progress, ct);
            await RunAsync(["-m", "pip", "install", "--disable-pip-version-check", "-r",
                Path.Combine(AppContext.BaseDirectory, "TranslationRuntime", "requirements.txt")], progress, ct);
            await File.WriteAllTextAsync(ready, "1", ct);
        }
        progress("Downloading requested language models. Existing models stay cached...");
        await RunAsync([Worker, "--setup", "--models", Models, "--size", settings.SpeechModel,
            "--inputs", string.Join(",", settings.InstalledInputs), "--target", settings.Target], progress, ct);
    }
    private static async Task DownloadAsync(string url, string path, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidDataException("Runtime downloads require HTTPS.");
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        string temporary = path + ".partial";
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using (var output = File.Create(temporary)) {
                byte[] buffer = new byte[81920]; long size = 0; int count;
                while ((count = await input.ReadAsync(buffer, ct)) != 0) {
                    size += count; if (size > 512L * 1024 * 1024) throw new InvalidDataException("Runtime download is too large.");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static ProcessStartInfo StartInfo(IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(Python) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = true, WorkingDirectory = Root };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["PYTHONUNBUFFERED"] = "1";
        info.Environment["PYTHONUTF8"] = "1";
        info.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";
        info.Environment["OMP_NUM_THREADS"] = "2";
        info.Environment["MKL_NUM_THREADS"] = "2";
        info.Environment["TOKENIZERS_PARALLELISM"] = "false";
        info.Environment["XDG_CACHE_HOME"] = Path.Combine(Root, "cache");
        return info;
    }
    private static async Task RunAsync(IEnumerable<string> args, Action<string> progress, CancellationToken ct)
    {
        using var process = Process.Start(StartInfo(args)) ?? throw new IOException("Could not start local model setup.");
        using var registration = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        async Task Drain(StreamReader reader) {
            while (await reader.ReadLineAsync(ct) is { } line) if (!string.IsNullOrWhiteSpace(line)) progress(line);
        }
        await Task.WhenAll(Drain(process.StandardOutput), Drain(process.StandardError), process.WaitForExitAsync(ct));
        if (process.ExitCode != 0) throw new IOException("Local model setup did not complete. Check the setup log and retry.");
    }
}

internal sealed class LocalTranslationWorker : IAsyncDisposable
{
    private readonly Process _process;
    private long _sequence;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Task _errors;
    internal LocalTranslationWorker(TranslationSettings settings)
    {
        var info = LocalTranslationRuntime.StartInfo([LocalTranslationRuntime.Worker, "--models", LocalTranslationRuntime.Models,
            "--size", settings.SpeechModel]);
        info.Environment["HF_HUB_OFFLINE"] = "1";
        info.Environment["TRANSFORMERS_OFFLINE"] = "1";
        _process = Process.Start(info) ?? throw new IOException("Could not start local translation.");
        // Consume diagnostics without logging media content.
        _errors = Task.Run(async () => { try { while (await _process.StandardError.ReadLineAsync() != null) { } } catch { } });
    }
    internal async Task<TranslationResult> TranslateAsync(string kind, byte[] data, TranslationSettings settings, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            long id = ++_sequence;
            var line = JsonSerializer.Serialize(new { id, kind, data = Convert.ToBase64String(data),
                source = settings.InputLanguage, target = settings.Target });
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), ct);
            await _process.StandardInput.FlushAsync(ct);
            var response = await _process.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromMinutes(2), ct)
                ?? throw new IOException("The local translation worker stopped.");
            using var result = JsonDocument.Parse(response);
            var root = result.RootElement;
            if (root.GetProperty("id").GetInt64() != id) throw new IOException("Translation response was out of sequence.");
            string? error = root.TryGetProperty("error", out var value) ? value.GetString() : null;
            return new TranslationResult(root.GetProperty("text").GetString() ?? "", root.GetProperty("source").GetString() ?? "",
                root.GetProperty("target").GetString() ?? "", error);
        }
        finally { _gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        try { if (!_process.HasExited) _process.Kill(true); } catch { }
        try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); await _errors; } catch { }
        _process.Dispose();
    }
}
