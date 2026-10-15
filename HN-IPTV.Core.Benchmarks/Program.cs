using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HnIptv.Services;
using HnIptv.Navigation;
using HnIptv.Models;

int count = args.Length > 0 ? int.Parse(args[0]) : 50000;
var builder = new StringBuilder("#EXTM3U\n");
for (int i = 0; i < count; i++) builder.AppendLine($"#EXTINF:-1 group-title=\"G{i % 100}\" tvg-id=\"id{i}\",Channel {i}\nhttps://example.test/live/{i}.ts");
string input = builder.ToString();
IReadOnlyList<Channel> channels = M3uService.Parse(input);
var directory = Path.Combine(Path.GetTempPath(), "hn-benchmark-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
try
{
    // Same harness measures Update 6's real portable encryption and Update 7's plain streaming storage.
    var legacyType = typeof(PlaylistLibrary).Assembly.GetType("HnIptv.Services.PortableVaultProtector");
    var protector = legacyType == null ? null : (IPlaylistProtector)Activator.CreateInstance(legacyType, "fixture-password")!;
    var library = new PlaylistLibrary(Path.Combine(directory, "library.dat"), protector!);
    library.Playlists.Add(new PlaylistProfile { Name = "Synthetic", CachedChannels = channels.ToList() });
    library.SelectedId = library.Playlists[0].Id;
    var results = new Dictionary<string, object>
    {
        ["environment"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        ["runtime"] = Environment.Version.ToString(), ["channels"] = count,
        ["legacy_encrypted_portable"] = legacyType != null,
        ["m3u_parse"] = Measure(() => channels = M3uService.Parse(input)),
        ["capture_snapshot"] = Measure(() => library.CaptureSnapshot()),
        ["portable_save"] = Measure(library.Save),
        ["portable_load"] = Measure(library.Load),
        ["full_scan_filter"] = Measure(() => channels.Where(c => ChannelCatalog.Includes(c, "G1", "Channel")).ToList())
    };
    var indexType = typeof(ChannelCatalog).Assembly.GetType("HnIptv.Navigation.ChannelIndex");
    if (indexType != null)
    {
        object index = Activator.CreateInstance(indexType, channels)!;
        var filter = indexType.GetMethod("Filter")!;
        results["indexed_group_filter"] = Measure(() => filter.Invoke(index, ["G1", "Channel", new HashSet<string>(), CancellationToken.None]));
    }
    results["library_bytes"] = new FileInfo(Path.Combine(directory, "library.dat")).Length;
    results["process_peak_working_set_bytes"] = Process.GetCurrentProcess().PeakWorkingSet64;
    Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
}
finally { Directory.Delete(directory, true); }

static object Measure(Action action)
{
    action(); // JIT/cache warmup is not part of the five reported samples.
    var times = new List<double>(); var allocations = new List<long>();
    for (int i = 0; i < 5; i++)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetTotalAllocatedBytes(true); var clock = Stopwatch.StartNew();
        action(); clock.Stop();
        times.Add(clock.Elapsed.TotalMilliseconds); allocations.Add(GC.GetTotalAllocatedBytes(true) - before);
    }
    return new { median_ms = times.Order().ElementAt(2), median_allocated_bytes = allocations.Order().ElementAt(2), samples_ms = times };
}
