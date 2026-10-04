# Decoding a release's Quality is partial, not total

`DownloadItem.From` decodes the quality a release was published at by comparing
Lidarr's release fields:

```csharp
if      (Release.Codec == "FLAC")    bitrate = Bitrate.FLAC;
else if (Release.Container == "320") bitrate = Bitrate.MP3_320;
else                                 bitrate = Bitrate.MP3_128;
```

The three qualities we publish do round-trip correctly through this, so the
encoding is not lossy. The defect is that the decode is **total where it should
be partial**: the `else` absorbs every unrecognised input as MP3 128. Enumerated,
`("MP3", "Lossless")`, `("MP3", "")`, `("", "")` and `("FLAC2", "320")` all
decode to a quality nobody asked for, with no error and no log line.

We decided decoding rejects input it does not recognise, by throwing, and that
failure fails acceptance rather than downgrading silently.

## Consequences

A release whose fields do not match a quality we published can no longer be
downloaded at all, where previously it downloaded at 128 kbps. That is the
intended behaviour: silently delivering the wrong quality is worse than refusing,
because the user gets a file they did not ask for and Lidarr records it as the
quality it requested.

This is the one place a closed `Quality` enum has a cost: a quality Deezer adds
in future requires an explicit code change rather than being absorbed. That is
the right default for a decoder whose input comes from persisted host state.
