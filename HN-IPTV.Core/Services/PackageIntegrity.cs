using System.Security.Cryptography;
namespace HnIptv.Services;
public static class PackageIntegrity
{
    public static async Task VerifyAsync(string path, string expected, CancellationToken ct = default)
    {
        await using var input = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false));
        if (expected.Length != 64 || !string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update checksum did not match. The update was not installed.");
    }
}
