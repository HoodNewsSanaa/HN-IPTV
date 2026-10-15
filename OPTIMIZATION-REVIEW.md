# Update 7 investigation

The saved Update 6 source was compiled unchanged and benchmarked before edits. Its publish directory contained three complete VLC architectures even though the application target was Windows x64. The native package enables all architectures when MSBuild Platform remains AnyCPU; setting RuntimeIdentifier alone did not disable them. Update 7 explicitly enables VlcWindowsX64Enabled and disables VlcWindowsX86Enabled/VlcWindowsArm64Enabled in the project, and validates the final directory.

Code inspection found dispatcher calls to VLC Stop, Play, native initialization and Dispose; synchronous preference reads/writes; full-catalog filtering on every keystroke; dispatcher image decoding; and a save queue capable of retaining repeated large snapshots. VideoLAN documents Stop as synchronous and warns against calling it from VLC callbacks. Update 7 uses a serialized worker controller and a persistent native HWND. It does not attach any minimize/restore handler that resets playback, rebuilds ItemsSource, or detaches video.

The original video wrapper includes a foreground overlay window. HN-IPTV had no overlay content, so a direct HwndHost avoids that extra window lifecycle. This is a targeted change, not proof that the original reported minimize freeze was caused by the overlay. A Windows trace and the playback fixture harness are still required to establish native runtime behavior.

Core profiling measured saved-library and catalog operations against the same synthetic 50,000-channel fixture. See PERFORMANCE.md for measured numbers, including the parser result that did not improve. No Windows startup, decoder or GPU measurements are inferred from Linux core benchmarks.

The full x64 VLC codec/access/demux/output set is preserved. Smaller plugin profiles, UPX, unsafe WPF trimming, and native single-file extraction were not enabled without a compatibility matrix. ReadyToRun remains an optional benchmark build, not a promised startup improvement. Package ZIP compression uses the smallest-size mode; the installer uses solid LZMA2.

Relevant upstream documentation:

- https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html
- https://docs.videolan.me/libvlcsharp/docs/best_practices.html
- https://github.com/videolan/libvlcsharp/blob/3.10.2/src/LibVLCSharp.WPF/VideoView.cs
- https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run
- https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities
