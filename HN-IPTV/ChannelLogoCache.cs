using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace HnIptv;

internal sealed class ChannelLogoCache : IDisposable
{
    private readonly string _directory;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _limit = new(4);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<BitmapSource?>>> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (BitmapSource? Image, DateTimeOffset Expires)> _memory = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly object _sync = new();
    public ChannelLogoCache(string directory) => _directory = directory;
    public async Task<BitmapSource?> GetAsync(string url, TimeSpan maxAge, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return null;
        url = uri.AbsoluteUri;
        lock (_sync)
            if (_memory.TryGetValue(url, out var hit) && hit.Expires > DateTimeOffset.UtcNow) return hit.Image;
        if (_pending.Count >= 128 && !_pending.ContainsKey(url)) return null;
        var lazy = _pending.GetOrAdd(url, _ => new Lazy<Task<BitmapSource?>>(() => Task.Run(() => LoadAsync(uri, maxAge, ct))));
        var task = lazy.Value;
        _ = task.ContinueWith(_ => _pending.TryRemove(new KeyValuePair<string, Lazy<Task<BitmapSource?>>>(url, lazy)),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try { return await task.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            // The same logo may have been requested in the previous category.
            // Retry under this category's live token rather than inherit its cancellation.
            _pending.TryRemove(new KeyValuePair<string, Lazy<Task<BitmapSource?>>>(url, lazy));
            return await GetAsync(url, maxAge, ct).ConfigureAwait(false);
        }
    }
    public Task<BitmapSource?> GetCachedAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            return Task.FromResult<BitmapSource?>(null);
        url = uri.AbsoluteUri;
        lock (_sync) if (_memory.TryGetValue(url, out var hit) && hit.Image != null) return Task.FromResult<BitmapSource?>(hit.Image);
        return Task.Run<BitmapSource?>(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
                var path = new[] { ".image", ".png", ".jpg", ".gif", ".bmp" }
                    .Select(ext => Path.Combine(_directory, hash + ext)).FirstOrDefault(File.Exists);
                return path == null ? null : Decode(File.ReadAllBytes(path));
            }
            catch (IOException) { return null; }
            catch (Exception) when (!ct.IsCancellationRequested) { return null; }
        }, ct);
    }
    private async Task<BitmapSource?> LoadAsync(Uri uri, TimeSpan maxAge, CancellationToken generation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(generation, _lifetime.Token);
        var ct = linked.Token;
        await _limit.WaitAsync(ct).ConfigureAwait(false);
        BitmapSource? image = null;
        try
        {
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant();
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, hash + ".image");
            // Reuse Update 6 cache files, too.
            string? cached = new[] { ".image", ".png", ".jpg", ".gif", ".bmp" }
                .Select(ext => Path.Combine(_directory, hash + ext)).FirstOrDefault(File.Exists);
            if (cached != null) { try { image = Decode(File.ReadAllBytes(cached)); } catch (Exception) { } }
            if (image == null || cached == null || DateTime.UtcNow - File.GetLastWriteTimeUtc(cached) >= maxAge)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (cached != null && image != null) request.Headers.IfModifiedSince = File.GetLastWriteTimeUtc(cached);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.NotModified && cached != null && image != null)
                { File.SetLastWriteTimeUtc(cached, DateTime.UtcNow); return Remember(uri.AbsoluteUri, image, maxAge); }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 2_000_000) throw new InvalidDataException();
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var bytes = new MemoryStream();
                byte[] buffer = new byte[16384];
                int count;
                while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
                {
                    if (bytes.Length + count > 2_000_000) throw new InvalidDataException();
                    bytes.Write(buffer, 0, count);
                }
                var data = bytes.ToArray();
                image = Decode(data); // Resize and freeze on this worker, never decode on the dispatcher.
                if (image != null)
                {
                    string temp = path + ".tmp";
                    await File.WriteAllBytesAsync(temp, data, ct).ConfigureAwait(false);
                    File.Move(temp, path, true);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { /* Failed logos get a short negative cache, not continuous retries. */ }
        finally { _limit.Release(); }
        return Remember(uri.AbsoluteUri, image, image == null ? TimeSpan.FromMinutes(5) : maxAge);
    }
    private BitmapSource? Remember(string url, BitmapSource? image, TimeSpan maxAge)
    {
        lock (_sync)
        {
            if (!_memory.ContainsKey(url)) _order.Enqueue(url);
            _memory[url] = (image, DateTimeOffset.UtcNow + (image == null ? TimeSpan.FromMinutes(5) : maxAge));
            while (_memory.Count > 128) _memory.Remove(_order.Dequeue());
        }
        return image;
    }
    private static BitmapSource Decode(byte[] data)
    {
        using var stream = new MemoryStream(data);
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 72; image.StreamSource = stream; image.EndInit(); image.Freeze();
        return image;
    }
    public Task PruneAsync() => Task.Run(() =>
    {
        try
        {
            if (!Directory.Exists(_directory)) return;
            long size = 0;
            foreach (var file in new DirectoryInfo(_directory).EnumerateFiles().OrderByDescending(f => f.LastWriteTimeUtc))
            {
                size += file.Length;
                if (size > 128L * 1024 * 1024 || file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-30)) file.Delete();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    });
    public void Dispose()
    {
        _lifetime.Cancel(); _http.Dispose();
        // Semaphore remains valid until already-running downloads release it.
    }
}
