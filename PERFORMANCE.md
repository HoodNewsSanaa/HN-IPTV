# Update 7 measurements

Measured on Linux x64, Ubuntu 24.04, .NET SDK 10.0.100/runtime 10.0.0. The baseline is the saved Update 6 source, not a downloaded release binary. No original installer was available. MiB means 1,048,576 bytes; filesystem allocation can differ.

## Windows package size

Both payloads are Release, self-contained Windows x64 publishes with ReadyToRun disabled and no managed trimming. ZIPs use .NET SmallestSize compression. The baseline ZIP contains the unchanged rebuilt publish directory; Update 7 also includes its portable marker, empty data directory and notices, so it is a package comparison rather than identical file lists.

| Component | Update 6 MiB | Update 7 MiB |
|---|---:|---:|
| Application and notices | 0.99 | 1.02 |
| .NET runtime | 138.44 | 130.09 |
| VLC x64, full plugin set | 106.42 | 106.42 |
| Unused VLC architectures | 192.84 | 0.00 |
| Extracted application payload | 438.69 | 237.54 |
| Portable ZIP | 197.29 | 104.48 |
| Windows installer | Not built | Not built |
| Combined installer + ZIP artifact | Not available | Not available |

Application payload fell **45.9%**. ZIP size fell **47.0%**. Installed size will include the generated uninstaller and user data and must be measured after an actual installation. GitHub's combined artifact is the sum of the two packages plus a small wrapper and checksums, not the installed size. CI measures each package and uploads no duplicate publish directory.

The optional ReadyToRun build was also cross-published successfully: **238.03 MiB** extracted versus **237.54 MiB** normally. This is a size measurement only; Windows startup benefit has not been measured. The default stays off pending same-machine startup tests. All x64 VLC plugins remain; no speculative codec pruning or WPF trimming was performed.

## 50,000-channel core benchmark

Same generated M3U/catalog, one warm-up and five samples per operation; medians below. Update 6 uses its password-encrypted portable store; Update 7 uses the new plain portable store. Synthetic results do not predict network, GPU, native playback or UI latency.

| Operation | Update 6 | Update 7 |
|---|---:|---:|
| Parse M3U string | 115.784 ms | 121.271 ms |
| Capture library snapshot | 0.073 ms | 0.036 ms |
| Save portable library | 143.496 ms | 54.189 ms |
| Load portable library | 185.296 ms | 107.436 ms |
| Full catalog filter | 1.232 ms | 1.196 ms |
| Indexed group filter | Not present | 0.068 ms |
| Saved library | 12,996,136 bytes | 11,196,110 bytes |
| Benchmark process peak working set | 260.70 MiB | 162.27 MiB |

Parsing did **not** get faster in this run. Its UI benefit comes from moving work off the dispatcher, cancellation and file streaming. The index measurement uses the same query in a selected group; it does not imply all-channel search is 18 times faster. Snapshot allocations fell from 400,848 to 960 bytes, and save allocations from 52,388,056 to 8,248 bytes in this harness. Peak working set is for the entire benchmark process, not HN-IPTV playback memory.

Raw samples, allocations, size bytes and build/test logs are in `validation/`. The benchmark project accepts `-p:CoreProject=/absolute/path/to/Update6/HN-IPTV.Core/HN-IPTV.Core.csproj` to repeat the baseline comparison. Run each in its own process after cleaning benchmark bin/obj when changing the referenced project.

## Measurements still required on Windows

Use `profile-startup.ps1 -Executable <path>` on both versions, with equivalent data and the same machine. Separate cold launch from warm launches. The script records time to a responsive main window and working/private bytes; it does not measure provider catalog completion. No Windows startup improvement is claimed yet.

Run the Windows smoke harness and inspect a real playing window during repeated minimize/restore, fullscreen, tune and shutdown. Capture CPU/dispatcher/native-thread traces with Windows Performance Recorder if freezes remain. Diagnostics measure dispatcher delay and operations without recording URLs, credentials or exception messages.
