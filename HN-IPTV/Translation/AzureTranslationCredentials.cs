using System.IO;
using System.Text;
using HnIptv.Services;
namespace HnIptv;

internal static class AzureTranslationCredentials
{
    internal static string PathForKey => Path.Combine(AppStorage.DataDirectory,"azure-translator-key.dat");
    internal static string Load()
    {
        if (!File.Exists(PathForKey)) return "";
        try { return Encoding.UTF8.GetString(new WindowsDpapiProtector().Unprotect(File.ReadAllBytes(PathForKey))); }
        catch { throw new InvalidOperationException("The saved Azure key could not be opened for this Windows account. Paste it again in Azure settings."); }
    }
    internal static void Save(string key)
    {
        key = key.Trim();
        if (key.Length == 0) {
            if (File.Exists(PathForKey)) File.Delete(PathForKey);
            if (File.Exists(PathForKey+".bak")) File.Delete(PathForKey+".bak");
            return;
        }
        byte[] encrypted = new WindowsDpapiProtector().Protect(Encoding.UTF8.GetBytes(key));
        AtomicFile.Write(PathForKey,stream => stream.Write(encrypted));
    }
}

