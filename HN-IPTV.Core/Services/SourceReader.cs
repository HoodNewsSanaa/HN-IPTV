using System.Net;
using System.Net.Http;
using System.IO.Compression;
namespace HnIptv.Services;

// Limit decompressed input as well as HTTP payloads.
public static class SourceReader
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        { Timeout = TimeSpan.FromSeconds(45) };
    public static bool IsWebUrl(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    public static async Task<MemoryStream> ReadAsync(string source, int limit, CancellationToken ct = default)
    {
        HttpResponseMessage? response = null;
        Stream? input = null;
        try
        {
            if (IsWebUrl(source))
            {
                response = await Client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Source is too large.");
                input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            }
            else input = File.OpenRead(source);
            if (source.Split('?')[0].EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                input = new GZipStream(input, CompressionMode.Decompress);
            var output = new MemoryStream();
            try
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
                {
                    if (output.Length + count > limit) throw new InvalidDataException("Source is too large.");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                }
                output.Position = 0;
                return output;
            }
            catch { output.Dispose(); throw; }
        }
        finally { if (input != null) await input.DisposeAsync(); response?.Dispose(); }
    }
}
