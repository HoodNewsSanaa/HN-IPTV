using System.Net;
using System.Text;
using HnIptv.Models;
using HnIptv.Navigation;
using HnIptv.Services;
using HnIptv.Preferences;
using HnIptv.Playback;

var playlist = "#EXTM3U\n#EXTINF:-1 tvg-logo=\"https://example.test/icon.png\" group-title=\"News\",World One\nhttps://example.test/stream.m3u8";
var parsed = M3uService.Parse(playlist);
Check(parsed.Count == 1 && parsed[0].GroupName == "News" &&
    parsed[0].Command == "https://example.test/stream.m3u8", "M3U parser");
Check(ChannelCatalog.Includes(parsed[0], "News", "world") &&
    !ChannelCatalog.Includes(parsed[0], "Sports", null), "channel filtering");
// Channel visibility is UI-independent and persists per provider.
var testChannel = parsed[0];
Check(testChannel.IsVisible, "default channel visibility");
testChannel.IsFavorite = true;
testChannel.IsVisible = false;
Check(!ChannelCatalog.Includes(testChannel, "*", null), "hidden channel filtering");
var prefsPath = Path.Combine(Path.GetTempPath(), "lumatv-test-" + Guid.NewGuid() + ".json");
var key = ChannelPreferences.ProviderKey("m3u", "test-playlist");
try
{
    new ChannelPreferences(prefsPath).Save(key, parsed);
    var fresh = M3uService.Parse(playlist);
    var reloadedPreferences = new ChannelPreferences(prefsPath);
    reloadedPreferences.Load();
    reloadedPreferences.Apply(key, fresh);
    Check(fresh[0].IsFavorite && fresh[0].IsVisible, "favorites preserved and obsolete channel hiding cleared");
    Check(ChannelPreferences.ProviderKey("m3u", "test-playlist") !=
          ChannelPreferences.ProviderKey("m3u", "other-playlist"), "provider isolation");
    var now = new DateTimeOffset(2026, 10, 14, 17, 30, 0, TimeSpan.Zero);
    fresh[0].SetEpgPreview([new ProgramEntry("News Hour", "", now.AddMinutes(-30), now.AddMinutes(30))], now);
    Check(fresh[0].EpgPreview == "Now: News Hour" && Math.Abs(fresh[0].EpgProgress - 50) < 0.01,
        "inline EPG current program and progress");
    fresh[0].SetEpgPreview([], now);
    Check(fresh[0].EpgPreview == "No EPG information" && fresh[0].EpgProgress == 0,
        "missing EPG fallback");
}
finally { if (File.Exists(prefsPath)) File.Delete(prefsPath); }

var seen = new List<Uri>();
using var http = new HttpClient(new FakeXtreamHandler(uri =>
{
    seen.Add(uri);
    var query = uri.Query;
    if (!query.Contains("username=user%2Bname", StringComparison.OrdinalIgnoreCase) ||
        !query.Contains("password=p%40ss%26word", StringComparison.OrdinalIgnoreCase))
        throw new Exception("Xtream credentials were not correctly URL-escaped.");
    if (query.Contains("action=get_live_categories"))
        return """[{"category_id":"10","category_name":"News"},{"category_id":20,"category_name":"Sports"}]""";
    if (query.Contains("action=get_live_streams"))
        return """[{"stream_id":15,"name":"World News","num":6,"stream_icon":"https://example.test/news.png","category_id":"10"},{"stream_id":"22","name":"Stadium","category_id":"20"}]""";
    if (query.Contains("action=get_short_epg"))
        return """{"epg_listings":[{"title":"VGVzdCBTaG93","description":"TGl2ZSB0ZXN0","start_timestamp":"1735000000","stop_timestamp":"1735003600"}]}""";
    return """{"user_info":{"auth":1,"status":"Active","allowed_output_formats":["ts","m3u8"]}}""";
}));
using (var xtream = new XtreamClient("https://tv.example.test:8443/subscription/player_api.php?ignored=1",
    "user+name", "p@ss&word", XtreamStreamFormat.Hls, http))
{
    await xtream.ConnectAsync();
    Check(xtream.SelectedFormat == "HLS (.m3u8)", "Xtream stream format");
    var data = await xtream.GetLiveTvAsync();
    Check(data.Channels.Count == 2 && data.Groups.Count == 2, "Xtream live categories/streams");
    // GetLiveTvAsync deliberately sorts by channel number, not by API response order.
    // Resolve the fixture channel by its stable Xtream stream ID instead of list index.
    var worldNews = data.Channels.Single(ch => ch.Id == "15");
    Check(worldNews.Number == 6 && worldNews.GroupName == "News" &&
        worldNews.Name == "World News", "Xtream channel mapping");
    Check(data.Channels[0].Number <= data.Channels[1].Number, "Xtream channel-number sorting");
    Check(worldNews.Command == "", "Credentials must not be embedded in channel models");
    var stream = xtream.ResolveLiveStream(worldNews);
    Check(stream.AbsoluteUri == "https://tv.example.test:8443/subscription/live/user%2Bname/p%40ss%26word/15.m3u8",
        "Xtream stream URL construction");
    var epg = await xtream.GetEpgAsync(worldNews.Id);
    Check(epg.Count == 1 && epg[0].Title == "Test Show" && epg[0].Description == "Live test" &&
        epg[0].Start is not null && epg[0].End is not null, "Xtream EPG/base64");
}
Check(seen.Count == 4, "Xtream request count");

using var badHttp = new HttpClient(new FakeXtreamHandler(_ => """{"user_info":{"auth":0,"status":"Expired"}}"""));
using (var badAccount = new XtreamClient("http://tv.example.test:8080", "user", "bad", httpClient: badHttp))
{
    bool rejected = false;
    try { await badAccount.ConnectAsync(); }
    catch (UnauthorizedAccessException) { rejected = true; }
    Check(rejected, "Xtream authentication failure handling");
}

using var hlsOnlyHttp = new HttpClient(new FakeXtreamHandler(_ =>
    """{"user_info":{"auth":1,"status":"Active","allowed_output_formats":["m3u8"]}}"""));
using (var automatic = new XtreamClient("https://tv.example.test", "user", "secret", httpClient: hlsOnlyHttp))
{
    await automatic.ConnectAsync();
    Check(automatic.SelectedFormat == "HLS (.m3u8)", "Xtream automatic HLS fallback");
}
using (var disallowed = new XtreamClient("https://tv.example.test", "user", "secret",
    XtreamStreamFormat.MpegTs, hlsOnlyHttp))
{
    bool rejected = false;
    try { await disallowed.ConnectAsync(); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "Xtream disallowed output format");
}

var largeXtreamJson = "[" + string.Join(',', Enumerable.Range(1, 50000).Select(i =>
    $"{{\"stream_id\":{i},\"name\":\"Channel {i}\",\"num\":{50001 - i},\"category_id\":\"g{i % 100}\"}}")) + "]";
using (var largeHttp = new HttpClient(new FakeXtreamHandler(uri => uri.Query.Contains("get_live_streams") ? largeXtreamJson :
    uri.Query.Contains("get_live_categories") ? "[]" : """{"user_info":{"auth":1,"status":"Active"}}""")))
using (var largeClient = new XtreamClient("https://example.test", "fixture-user", "fixture-password", httpClient: largeHttp))
{
    await largeClient.ConnectAsync();
    var large = await largeClient.GetLiveTvAsync();
    Check(large.Channels.Count == 50000 && large.Groups.Count == 100 && large.Channels[0].Id == "50000", "50,000 channel Xtream mapping and sorting");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    bool stopped = false;
    try { await largeClient.GetLiveTvAsync(cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
    Check(stopped, "Xtream cancellation");
}

Console.WriteLine("Shared Core and mocked Xtream API smoke checks passed.");

// Encrypted library persistence remains supported without external EPG matching.
var vaultPath = Path.Combine(Path.GetTempPath(), "lumatv-vault-" + Guid.NewGuid() + ".dpapi");
try
{
    // Fake reversible protector is for Core tests only; Windows uses actual DPAPI.
    var vault = new PlaylistLibrary(vaultPath, new TestProtector());
    vault.Playlists.Add(new PlaylistProfile { Name = "Private", Type = "xtream", Password = "unique-test-secret",
        CachedChannels = [new Channel { Id = "9", Name = "Test", GroupName = "Group", GroupId = "g" }] });
    vault.SelectedId = vault.Playlists[0].Id;
    var stableSnapshot = vault.CaptureSnapshot();
    vault.Playlists[0].Name = "Changed after snapshot";
    vault.SaveSnapshot(stableSnapshot);
    Check(!File.ReadAllText(vaultPath).Contains("unique-test-secret"), "no plaintext credentials in vault");
    var reloaded = new PlaylistLibrary(vaultPath, new TestProtector());
    reloaded.Load();
    Check(reloaded.SelectedId == vault.SelectedId &&
        reloaded.Playlists[0].Name == "Private" &&
        reloaded.Playlists[0].Password == "unique-test-secret" &&
        reloaded.Playlists[0].CachedChannels.Count == 1, "playlist restore roundtrip");
}
finally { if (File.Exists(vaultPath)) File.Delete(vaultPath); }
Console.WriteLine("Encrypted playlist-library checks passed.");


// Portable storage requires no password and can be moved with its relative paths.
var directory = Path.Combine(Path.GetTempPath(), "hn-iptv-tests-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "saved-playlists.json");
    var portable = new PlaylistLibrary(path);
    var profile = new PlaylistProfile { Name = "Portable", Password = "fixture-secret", LocalPath = "playlists/example.m3u",
        CachedChannels = parsed.ToList(), HiddenGroupIds = ["hidden"], SelectedGroupId = "News" };
    portable.Playlists.Add(profile); portable.SelectedId = profile.Id;
    portable.Save();
    var moved = Path.Combine(directory, "moved.json");
    File.Copy(path, moved);
    var restored = new PlaylistLibrary(moved); restored.Load();
    Check(restored.Playlists[0].Password == "fixture-secret" && restored.Playlists[0].LocalPath == "playlists/example.m3u" &&
        restored.SelectedId == profile.Id && restored.Playlists[0].HiddenGroupIds.Contains("hidden"), "password-free portable roundtrip");
    profile.Name = "New name"; portable.Save();
    var backup = new PlaylistLibrary(path + ".bak"); backup.Load();
    Check(backup.Playlists[0].Name == "Portable", "last good backup");
    var before = File.ReadAllBytes(path);
    try { AtomicFile.Write(path, stream => { stream.WriteByte(42); throw new IOException("fixture failure"); }); }
    catch (IOException) { }
    Check(File.ReadAllBytes(path).SequenceEqual(before), "failed save preserves original");
    File.WriteAllText(moved, "broken");
    bool failed = false;
    try { new PlaylistLibrary(moved).Load(); } catch (System.Text.Json.JsonException) { failed = true; }
    Check(failed && File.ReadAllText(moved) == "broken", "corrupt file is not silently discarded");

    var tricky = M3uService.Parse("#EXTM3U\n#EXTINF:-1 group-title=\"News, World\" tvg-id=\"first\" tvg-id=\"last\",A, B\nhttps://example.test/a.ts");
    Check(tricky.Count == 1 && tricky[0].Name == "A, B" && tricky[0].GroupName == "News, World" && tricky[0].EpgId == "last", "quoted commas and duplicate attributes");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    bool cancelledParse = false;
    try { M3uService.Parse(playlist, cancelled.Token); } catch (OperationCanceledException) { cancelledParse = true; }
    Check(cancelledParse, "M3U cancellation");
    var builder = new StringBuilder("#EXTM3U\n");
    for (int i = 0; i < 50000; i++) builder.AppendLine($"#EXTINF:-1 group-title=\"G{i % 100}\",Channel {i}\nhttps://example.test/{i}.ts");
    string largeFile = Path.Combine(directory, "large.m3u"); File.WriteAllText(largeFile, builder.ToString());
    var large = await M3uService.ParseFileAsync(largeFile);
    var index = new ChannelIndex(large);
    Check(large.Count == 50000 && index.Filter("G1", "", new HashSet<string>()).Count == 500, "50,000 channel file and group index");
    large[1].IsFavorite = true;
    Check(index.Filter("fav", "channel 1", new HashSet<string>()).Count == 1 &&
        index.Filter("fav", "", new HashSet<string> { "G1" }).Count == 0, "favorites, search and hidden group combined");
    using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
    var writes = new List<string>();
    var queue = new LatestSaveQueue<string>(value =>
    { if (value == "first") { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); } lock(writes) writes.Add(value); });
    queue.Enqueue("first");
    Check(entered.Wait(TimeSpan.FromSeconds(10)), "save worker started");
    for (int i = 0; i < 1000; i++) queue.Enqueue(i.ToString());
    release.Set(); await queue.FlushAsync();
    Check(writes.SequenceEqual(new[] { "first", "999" }), "save queue bounded and newest snapshot wins");
    queue.Enqueue("next"); await queue.FlushAsync();
    Check(writes.Last() == "next", "save queue restarts after flush");
    bool failWrite = true;
    var failing = new LatestSaveQueue<string>(_ => { if (failWrite) throw new IOException(); });
    failing.Enqueue("failure");
    bool observed = false;
    try { await failing.FlushAsync(); } catch (IOException) { observed = true; }
    Check(observed, "save errors reach close/flush");
    failWrite = false; failing.Enqueue("retry"); await failing.FlushAsync();
}
finally { Directory.Delete(directory, recursive: true); }
Console.WriteLine("Portable, atomic persistence, cancellation, large-catalog and save-queue checks passed.");

// Selected EPG work overtakes queued previews; cancellation never leaks capacity.
var priorityGate = new PriorityWorkGate(1);
var busyLease = await priorityGate.EnterAsync("busy", false, default);
var previewOne = priorityGate.EnterAsync("preview-one", false, default);
var selectedGuide = priorityGate.EnterAsync("selected", false, default);
using var cancelledGuide = new CancellationTokenSource();
var abandoned = priorityGate.EnterAsync("abandoned", false, cancelledGuide.Token);
cancelledGuide.Cancel();
priorityGate.Promote("selected"); busyLease.Dispose();
using (await selectedGuide.WaitAsync(TimeSpan.FromSeconds(2)))
    Check(!previewOne.IsCompleted, "selected EPG promotion");
using (await previewOne.WaitAsync(TimeSpan.FromSeconds(2))) { }
try { await abandoned; Check(false, "queued guide cancellation"); } catch (OperationCanceledException) { }
using (await priorityGate.EnterAsync("after-cancel", true, default).WaitAsync(TimeSpan.FromSeconds(2))) { }

var settingsDirectory = Path.Combine(Path.GetTempPath(), "hn-player-settings-" + Guid.NewGuid().ToString("N"));
try
{
    var path = Path.Combine(settingsDirectory, "player-settings.json");
    new PlayerSettings { BufferMilliseconds = 3000, PipWidth = 640, PipLeft = -500 }.Save(path);
    var savedSettings = PlayerSettings.Load(path);
    Check(savedSettings.BufferMilliseconds == 3000 && savedSettings.PipWidth == 640 && savedSettings.PipLeft == -500,
        "portable buffer and PiP settings round trip");
    var safeSettings = new PlayerSettings { BufferMilliseconds = -1, PipWidth = double.NaN, PipHeight = 0 }.Normalize();
    Check(safeSettings.BufferMilliseconds == 250 && safeSettings.PipWidth == 480 && safeSettings.PipHeight == 64,
        "invalid player settings bounded");
}
finally { Directory.Delete(settingsDirectory, true); }
try { throw new InvalidOperationException("https://provider.test/user/secret?password=hidden"); }
catch (Exception ex)
{
    string diagnostic = HnIptv.Diagnostics.SafeException.Describe(ex);
    Check(diagnostic.Contains("InvalidOperationException") && !diagnostic.Contains("secret") &&
        !diagnostic.Contains("provider.test") && !diagnostic.Contains("password") && !diagnostic.Contains("Program.cs"),
        "sanitized diagnostics omit credentials and source paths");
}
Console.WriteLine("EPG priority/cancellation, player settings and diagnostic privacy checks passed.");

// Real format/geometry edge cases, including anamorphic and portrait streams.
var info = new StreamInfo { Width = 720, Height = 576, DisplayAspect = 16d / 9,
    FramesPerSecond = 30000d / 1001, InputBitsPerSecond = 4_500_000,
    AudioCodec = "AAC", AudioChannels = 2, AudioSampleRate = 48000 };
Check(info.FrameRateText == "29.97 fps" && info.BitrateText == "4.50 Mb/s" && info.AudioText.Contains("48 kHz"), "stream units and fractional frame rate");
Check(StreamInfo.Empty.FrameRateText == "Unavailable" && StreamInfo.Empty.BitrateText == "Unavailable", "unknown stats do not invent values");
foreach (double aspect in new[] { 16d / 9, 4d / 3, 9d / 16, 2.35 }) {
    var fit = VideoGeometry.Fit(640, aspect, 1024, 768);
    Check(Math.Abs(fit.Width / fit.Height - aspect) < 0.00001 && fit.Width <= 1024 && fit.Height <= 768, "aspect preservation within bounds");
}
var attributes = M3uService.Parse("#EXTM3U\n#EXTINF:-1 group-title=\"First\" GROUP-TITLE=\"News, live\" unknown=\"skip\" tvg-id=\"one\",Title group-title=\"Do not parse me\"\nhttps://example.test/live.ts");
Check(attributes.Single().GroupName == "News, live" && attributes.Single().Name == "Title group-title=\"Do not parse me\"", "metadata boundary and duplicate attributes");
Console.WriteLine("Stream telemetry, PiP aspect and allocation-efficient parser checks passed.");


// Reconnecting has a finite budget, reset only by an explicit user tune.
var recovery = new RecoveryBudget();
Check(recovery.NextDelay() == TimeSpan.FromSeconds(1) && recovery.NextDelay() == TimeSpan.FromSeconds(2) &&
    recovery.NextDelay() == TimeSpan.FromSeconds(4) && recovery.NextDelay() == null, "bounded reconnect backoff");
recovery.Reset();
Check(recovery.NextDelay() == TimeSpan.FromSeconds(1), "manual tune resets retry budget");
var headerChannels = M3uService.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"one\",One\n#EXTVLCOPT:http-user-agent=Test Player\n#EXTVLCOPT:http-referrer=https://example.test/\nhttps://example.test/one\n#EXTINF:-1,Two\nhttps://example.test/two");
Check(headerChannels[0].UserAgent == "Test Player" && headerChannels[0].Referer == "https://example.test/" &&
    headerChannels[1].UserAgent == "" && headerChannels[1].Referer == "", "per-channel HTTP headers do not leak");
var sameCatalog = M3uService.Parse(playlist);
Check(CatalogSnapshot.Matches(parsed, sameCatalog), "unchanged metadata reuses catalog");
Check(!CatalogSnapshot.Matches(parsed, [new Channel { Id = parsed[0].Id, Name = "Renamed" }]), "changed metadata invalidates catalog");
Check(!CatalogSnapshot.Matches(headerChannels, M3uService.Parse("#EXTM3U\n#EXTINF:-1,One\nhttps://example.test/one")), "headers and catalog length affect refresh");
var day = DateTimeOffset.UtcNow.Date;
string stamp(DateTime value) => value.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
var xml = $"<tv><programme channel='one' start='{stamp(day.AddHours(12))} +0200' stop='{stamp(day.AddHours(13))} +0200'><title>News &amp; Weather</title><desc>Details</desc></programme><programme channel='other' start='{stamp(day)} +0000' stop='{stamp(day.AddHours(1))} +0000'><title>Skip</title></programme></tv>";
using (var input = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
{
    var guide = XmltvService.Parse(input, ["one"]);
    Check(guide.Count == 1 && guide["one"][0].Title == "News & Weather" &&
        guide["one"][0].Start!.Value.UtcDateTime.Hour == 10, "XMLTV exact IDs, entity decoding and timezone");
}
bool dtdRejected = false;
try { using var input = new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE tv [<!ENTITY x SYSTEM 'file:///private'>]><tv/>")); XmltvService.Parse(input, ["one"]); }
catch (System.Xml.XmlException) { dtdRejected = true; }
Check(dtdRejected, "XMLTV rejects external entities");
using (var input = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
{
    bool cancelled = false;
    try { XmltvService.Parse(input, ["one"], new CancellationToken(true)); } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "guide parsing cancels");
}
var updateJson = System.Text.Json.JsonSerializer.Serialize(new {
    draft = false, prerelease = false, tag_name = "v0.10.0",
    assets = new[] { new { name = "HN-IPTV-Setup.exe", browser_download_url = "https://github.com/HoodNewsSanaa/HN-IPTV/releases/download/v0.10.0/HN-IPTV-Setup.exe" },
        new { name = "HN-IPTV-Portable.zip", browser_download_url = "https://github.com/HoodNewsSanaa/HN-IPTV/releases/download/v0.10.0/HN-IPTV-Portable.zip" } },
    body = new string('a', 64) + "  HN-IPTV-Setup.exe\n" + new string('b', 64) + "  HN-IPTV-Portable.zip"
});
Check(GitHubUpdates.Parse(updateJson, new Version(0,9,2,0), false)?.AssetName == "HN-IPTV-Setup.exe", "installed update asset");
Check(GitHubUpdates.Parse(updateJson, new Version(0,9,2,0), true)?.AssetName == "HN-IPTV-Portable.zip", "portable update asset");
Check(GitHubUpdates.Parse(updateJson, new Version(0,10,0,0), true) == null &&
    GitHubUpdates.Parse(updateJson, new Version(1,0,0), false) == null, "equal or older release is a no-op");
Check(GitHubUpdates.Parse(updateJson.Replace("\"prerelease\":false", "\"prerelease\":true"), new Version(0,9,2), true) == null, "skip prereleases");
bool wrongAssetRejected = false;
try { GitHubUpdates.Parse(updateJson.Replace("https://github.com/", "https://example.test/"), new Version(0,9,2), false); }
catch (InvalidDataException) { wrongAssetRejected = true; }
Check(wrongAssetRejected, "reject update packages outside the repository");
var updateFixture = Path.Combine(Path.GetTempPath(), "hn-update-test-" + Guid.NewGuid().ToString("N"));
try
{
    await File.WriteAllTextAsync(updateFixture, "package fixture");
    var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(updateFixture)));
    await PackageIntegrity.VerifyAsync(updateFixture, digest);
    await File.AppendAllTextAsync(updateFixture, "tampered");
    bool mismatch = false;
    try { await PackageIntegrity.VerifyAsync(updateFixture, digest); } catch (InvalidDataException) { mismatch = true; }
    Check(mismatch, "tampered update cannot install");
    bool oversized = false;
    try { using var ignored = await SourceReader.ReadAsync(updateFixture, 2); } catch (InvalidDataException) { oversized = true; }
    Check(oversized, "source size limit");
}
finally { File.Delete(updateFixture); }
Console.WriteLine("Catalog caching, reconnect budget, M3U headers, XMLTV and update integrity checks passed.");

var previewJson = updateJson.Replace("v0.10.0", "feature-rich-v0.10.0")
    .Replace("\"prerelease\":false", "\"prerelease\":true");
Check(GitHubUpdates.Parse(previewJson, new Version(0,9,2), false, true) != null, "feature update channel accepts its own previews");
Check(GitHubUpdates.Parse(previewJson, new Version(0,9,2), false) == null, "stable update channel excludes previews");
Check(GitHubUpdates.Parse(updateJson, new Version(0,9,2), false, true) == null, "feature update channel does not switch to stable builds");
var translationSettings = new HnIptv.Translation.TranslationSettings { InputLanguage = "JA-jp", OutputLanguage = "es", SpeechModel = "invalid", Acceleration = "invalid" }.Normalize();
Check(translationSettings.InputLanguage == "ja" && translationSettings.Target == "es" && translationSettings.SpeechModel == "auto", "translation language/model normalization");
Check(translationSettings.Acceleration == "auto", "invalid acceleration falls back to auto");
var gpuSettings = (translationSettings with { SpeechModel = "medium", Acceleration = "cuda" }).Normalize();
Check(gpuSettings.SpeechModel == "medium" && gpuSettings.Acceleration == "cuda", "medium GPU translation settings remain selectable");
var automaticTranslation = new HnIptv.Translation.TranslationSettings { SpeechModel = "turbo", SpeechPipeline = "staged", TextModel = "auto", NoiseSuppression = true }.Normalize();
Check(automaticTranslation.SpeechModel == "turbo" && automaticTranslation.SpeechPipeline == "staged" && automaticTranslation.NoiseSuppression, "separate speech translation and optional noise settings persist");
var translationVersion = new HnIptv.Translation.TranslationGeneration();
long firstTranslation = translationVersion.Current;
translationVersion.Advance();
Check(!translationVersion.IsCurrent(firstTranslation), "channel changes invalidate old translation results");
var translationFile = Path.Combine(Path.GetTempPath(), "hn-translation-" + Guid.NewGuid().ToString("N") + ".json");
try {
    gpuSettings.Save(translationFile);
    var savedTranslation = HnIptv.Translation.TranslationSettings.Load(translationFile);
    Check(savedTranslation.InputLanguage == "ja" && savedTranslation.OutputLanguage == "es" && savedTranslation.Acceleration == "cuda", "manual translation languages and GPU preference persist");
} finally { File.Delete(translationFile); }
var captionHistory = new HnIptv.Translation.CaptionHistory();
Check(captionHistory.Append("Hello world.") == "Hello world.", "initial caption");
Check(captionHistory.Append("") == "Hello world.", "empty inference preserves readable caption");
Check(captionHistory.Append("World! Here is the news.") == "Hello world. Here is the news.", "caption overlap retains context");
captionHistory.Clear(); Check(captionHistory.Append("New channel") == "New channel", "channel reset clears caption history");
var segmenter = new HnIptv.Translation.SpeechAudioBuffer();
byte[] voicedPacket = new byte[HnIptv.Translation.SpeechAudioBuffer.BytesPerSecond / 10];
for (int i = 0; i < voicedPacket.Length; i += 2) { voicedPacket[i] = 0; voicedPacket[i + 1] = 10; }
byte[]? speechSegment = null;
for (int i = 0; i < 15; i++) speechSegment = segmenter.Append(voicedPacket);
Check(speechSegment?.Length == HnIptv.Translation.SpeechAudioBuffer.BytesPerSecond * 3 / 2, "continuous speech is emitted within 1.5 seconds");
for (int i = 0; i < 8; i++) segmenter.Append(voicedPacket);
byte[] silence = new byte[voicedPacket.Length];
for (int i = 0; i < 2; i++) speechSegment = segmenter.Append(silence);
Check(speechSegment != null && speechSegment.Length >= HnIptv.Translation.SpeechAudioBuffer.BytesPerSecond * 2, "phrase segmentation carries overlap across a pause");
var rollingAudio = new HnIptv.Translation.SpeechAudioBuffer();
int rollingEmissions = 0;
for (int packet = 0; packet < 240; packet++)
    if (rollingAudio.Append(voicedPacket) is { } rollingSegment) {
        rollingEmissions++;
        Check(rollingSegment.Length <= HnIptv.Translation.SpeechAudioBuffer.BytesPerSecond * 15, "rolling audio memory stays bounded");
        if (rollingEmissions > 1) Check(rollingAudio.LastContextSeconds >= 1.5, "recognition retains sentence context");
    }
Check(rollingEmissions == 16, "continuous rolling captions refresh every 1.5 seconds");
var screenViewport = HnIptv.Translation.ScreenOverlayLayout.Viewport(1280, 960, 16d / 9);
Check(Math.Abs(screenViewport.Y - 120) < 0.001 && Math.Abs(screenViewport.Height - 720) < 0.001, "overlay respects letterboxing");
var translatedRegion = new HnIptv.Translation.ScreenTranslationRegion(0.1, 0.8, 0.4, 0.1, "Translated headline");
var mappedRegion = HnIptv.Translation.ScreenOverlayLayout.Map(translatedRegion, screenViewport);
Check(Math.Abs(mappedRegion.X - 128) < 0.001 && Math.Abs(mappedRegion.Y - 696) < 0.001, "OCR coordinates map into decoded video area");
var clippedRegion = HnIptv.Translation.ScreenOverlayLayout.Map(translatedRegion with { X = -0.1, Width = 0.2 }, screenViewport);
Check(Math.Abs(clippedRegion.Width - 128) < 0.001, "partly offscreen overlay clips safely");
Check(!HnIptv.Translation.ScreenOverlayLayout.Valid(translatedRegion with { X = double.NaN }), "non-finite overlay rejected");
var regionTracker = new HnIptv.Translation.ScreenRegionTracker();
var firstRegion = regionTracker.Update(new[] { translatedRegion })[0];
var jitteredRegion = regionTracker.Update(new[] { translatedRegion with { X = 0.105 } })[0];
Check(firstRegion.Id == jitteredRegion.Id && jitteredRegion.Region.X < 0.105, "overlay keeps identity and smooths small OCR jitter");
Check(regionTracker.Update(Array.Empty<HnIptv.Translation.ScreenTranslationRegion>()).Count == 1, "single missed OCR frame does not flash");
Check(regionTracker.Update(Array.Empty<HnIptv.Translation.ScreenTranslationRegion>()).Count == 0, "stale translated boxes disappear");
regionTracker.Clear();
Check(HnIptv.Translation.TranslationSettings.Load(translationFile+"-missing-overlay-test").ScreenOverlay == "positioned", "new screen overlay default");
Console.WriteLine("Translation settings, caption continuity, phrase segmentation, positioned overlays and stale-result cancellation checks passed.");

await AzureTranslationChecks.RunAsync();

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name + " check failed");
}

sealed class FakeXtreamHandler(Func<Uri, string> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responder(request.RequestUri!), Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}


sealed class TestProtector : IPlaylistProtector
{
    public byte[] Protect(byte[] data) => Convert.ToBase64String(data).Select(c => (byte)c).ToArray();
    public byte[] Unprotect(byte[] data) => Convert.FromBase64String(Encoding.ASCII.GetString(data));
}
