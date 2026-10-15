using System.Diagnostics;
using System.Text;

namespace HnIptv.Diagnostics;

// No exception messages, parameters, source paths, URLs, or provider data.
public static class SafeException
{
    public static string Describe(Exception exception)
    {
        var result = new StringBuilder();
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (result.Length > 0) result.Append(" inner=");
            result.Append(current.GetType().FullName).Append(" hresult=0x").Append(current.HResult.ToString("X8"));
            foreach (var frame in new StackTrace(current, false).GetFrames().Take(12))
            {
                var method = frame.GetMethod();
                // Only known app frames. Generic type arguments may contain user-defined names.
                var type = method?.DeclaringType;
                if (type?.Namespace?.StartsWith("HnIptv", StringComparison.Ordinal) == true)
                    result.Append(" at=").Append(type.Name).Append('.').Append(method!.Name);
            }
        }
        return result.ToString();
    }
}
