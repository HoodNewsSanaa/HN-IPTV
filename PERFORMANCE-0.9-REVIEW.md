# 0.9.0 performance review and engine comparison

The review covered the desktop app, native player lifetime, PiP/Win32 ownership, asynchronous UI handlers, cancellation, catalog/search work, logos, EPG queues, persistence, diagnostics and the build/release scripts. No live provider credentials were used.

## Changes made

- M3U metadata parsing enumerates regex value spans and keeps only the three used tags. It no longer allocates a dictionary plus Match/Group objects per channel. Duplicate keys still use the last value and quoted commas work. Titles cannot override metadata attributes.
- Logo requests link cancellation to their category generation. Leaving a category cancels queued and active HTTP work, freeing all four slots. A request for the same logo in the new category retries under the new token; cancelled downloads do not create a negative cache entry.
- Fresh cached EPG results bypass the two-request provider gate. Selected requests retain priority and shared network requests remain deduplicated.
- Volume changes coalesce for 50 ms rather than sending every slider tick through the player gate.
- Diagnostics drain up to 32 queued messages per file append. Credential-bearing native output is not copied into app logs.
- Playback telemetry stays on workers, skips rather than queues when Stop is busy, and pauses when not needed. First playback progress is sampled at 200 ms, then the Info panel/PiP refresh at 1 s. Native queue wait and Stop duration are logged separately. This is playback progress, not exact time to first visible pixel or HTTP first byte.
- Status remains Buffering until position advances. It no longer declares Playing as soon as a native Play call returns.
- Playback controls wrap within narrow player columns rather than overflowing a fixed-width grid.

## Local measurement

Linux .NET 10.0.13, five samples after warm-up, synthetic 50,000-channel M3U:

| M3U parse | 0.8.0 baseline | 0.9.0 |
|---|---:|---:|
| Median duration | 169.85 ms | 86.53 ms |
| Median allocated bytes | 134,760,008 | 60,360,008 |

The input, machine, harness and runtime were the same. This is approximately 49% less elapsed parsing time and 55% fewer allocated bytes in this local trial. It does not establish Windows startup or channel tuning speed. Raw benchmark JSON is in validation/.

## Retained decisions

The full x64 VLC codec set, untrimmed self-contained .NET runtime, one stable native video target, cancellation-safe channel switching, provider caching, atomic snapshots, one-active/one-pending saves, virtualized channel rows, and installer identity remain. WPF trimming, codec removal, overlapping provider connections to pre-tune channels, and changing default cache size were not justified by measurements.

A synchronous VLC Stop can still delay the next tune, although it never blocks WPF. Replacing the player during Stop or pre-opening another stream could exceed provider connection limits and introduce native lifetime races. The new queue/progress timings make this measurable before changing it.

## Stream information

The panel beneath the video shows selected-track resolution, source/estimated frame rate, input data bitrate, video codec and selected audio codec/channels/sample rate where exposed. Input bitrate is the rate of received bytes (including buffer filling and idle intervals), not an encoder's fixed bit budget. Unknown values remain unavailable. PiP uses display aspect including sample-aspect ratio; existing black pixels encoded into the source are not cropped.

## Engine comparison limits

VLC remains the release default. mpv is pinned by archive and DLL checksums. MPC-HC 2.8.3 is pinned by the upstream archive SHA-256, uses its documented `/slave` API, and runs with a separate portable INI. MPC-HC is an experimental embedded child process; the full player package and child memory/CPU are counted. Its slave API does not expose the stream information fields or equivalent buffer settings. It cannot be treated as a drop-in feature-equivalent winner.

The comparison workflow runs three fixture trials per engine on the same Windows runner. Failed trials are retained and excluded from speed medians; three successful short trials still do not prove long-session stability. A virtual runner cannot validate a physical GPU, audible output, actual provider behavior, DPI changes or visual overlay quality. Comparison binaries are not included in regular releases. Source/build references for any future mpv/MPC distribution must be supplied with applicable dependency licenses.

## Manual Windows acceptance

Test the setup over the existing install without uninstalling; keep saved data. Test PiP hover/show/hide, dragging the heading, resizing corners and edges, 16:9/4:3/portrait streams, multiple monitors/DPI, Return, minimize/restore, fullscreen, stream-info changes and slow provider tuning. The window removes its own bars/padding; source-encoded letterboxing remains.
