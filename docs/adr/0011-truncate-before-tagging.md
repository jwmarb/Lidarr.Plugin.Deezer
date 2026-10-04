# Truncate a downloaded track to its declared size before tagging it

DeezNET's `WriteRawTrackToFile` writes the whole final 2048-byte Deezer CDN
cipher block instead of stopping at the track's declared size, so every file it
produces carries up to 2047 bytes of trailing junk past the end of the audio
stream. Confirmed in a real Lidarr install: a download Lidarr reported as
`completed` with `sizeleft=0` produced a FLAC that fails integrity checking —
`flac -t` reports `ERROR while decoding data`, and ffmpeg reports
`invalid frame header` and non-monotonic timestamps.

We truncate the raw payload to the declared size, and we do it **before**
applying metadata.

## Consequences

The order is load-bearing and the wrong order is not obviously wrong.
`ApplyMetadataToFile` rewrites the file through TagLib, adding tag blocks and
cover art, so the tagged file is legitimately *larger* than the declared size —
measured, 14,378,085 bytes on disk against a declared 14,310,593, an excess of
67,492 bytes that is mostly tags and artwork rather than cipher padding.

The declared size therefore does not describe the tagged file at all. Truncating
after tagging cuts into TagLib's rewritten structure: tested, it leaves the file
failing `flac -t` with a different error. It looks like a fix, because the file
shrinks to a plausible number, while still being corrupt.

So the sequence is: write raw, truncate to declared size, then tag. The declared
size is already available at the call site — `DownloadItem` holds it in `_tracks`
for progress accounting — so no extra fetch is needed.

This is an upstream defect in DeezNET 1.2.1, not in our own code, which is why it
must be pinned by a test that performs a real download and runs an integrity
check. A test asserting "written size == declared + 2047" against a fake adapter
would replay the defect rather than detect it, and would keep passing if upstream
fixed it (see ADR-0007).
