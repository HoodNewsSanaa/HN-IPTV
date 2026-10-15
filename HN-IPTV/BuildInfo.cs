namespace HnIptv;

internal static class BuildInfo
{
#if USE_MPV
    public const string Engine = "mpv";
#else
    public const string Engine = "vlc";
#endif
    public static string Version => typeof(BuildInfo).Assembly.GetName().Version?.ToString() ?? "unknown";
}
