using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeezNET;
using DeezNET.Data;
using Newtonsoft.Json.Linq;

namespace NzbDrone.Plugin.Deezer
{
    /// <summary>
    /// The production adapter at the Deezer seam. Owns the one and only
    /// <see cref="DeezerClient"/>, and is the only code that knows DeezNET exists.
    ///
    /// Each instance holds its own client, authenticated once. Nothing re-points
    /// a live client at a different ARL, which is what made the previous static
    /// session unsafe under a credential change.
    /// </summary>
    internal sealed class DeezNetTransport : IDeezerTransport
    {
        private readonly DeezerClient _client = new();

        public string SessionId => _client.SID;

        public string ApiToken => _client.GWApi.ActiveUserData?["checkForm"]?.ToString() ?? string.Empty;

        public async Task<JToken> AuthenticateAsync(string arl, CancellationToken cancellation)
        {
            try
            {
                await _client.SetARL(arl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new DeezerAuthenticationException($"Could not reach Deezer to authenticate: {ex.Message}", ex);
            }

            return _client.GWApi.ActiveUserData;
        }

        public Task<JToken> GetAlbumPageAsync(long albumId, CancellationToken cancellation) =>
            _client.GWApi.GetAlbumPage(albumId, cancellation);

        public Task<JToken> GetTrackPageAsync(long trackId, CancellationToken cancellation) =>
            _client.GWApi.GetTrackPage(trackId, cancellation);

        public Task WriteTrackAsync(long trackId, string path, Quality quality, CancellationToken cancellation) =>
            _client.Downloader.WriteRawTrackToFile(
                trackId, path, DeezerQuality.ToBitrate(quality), null, cancellation);

        public Task ApplyMetadataAsync(long trackId, string path, string plainLyrics, CancellationToken cancellation) =>
            _client.Downloader.ApplyMetadataToFile(trackId, path, CoverArtResolution, plainLyrics, token: cancellation);

        public async Task<(string PlainLyrics, IReadOnlyList<(string Timestamp, string Line)> SyncedLyrics)?>
            GetDeezerLyricsAsync(long trackId, CancellationToken cancellation)
        {
            var lyrics = await _client.Downloader.FetchLyricsFromDeezer(trackId, cancellation).ConfigureAwait(false);
            return Convert(lyrics);
        }

        public async Task<(string PlainLyrics, IReadOnlyList<(string Timestamp, string Line)> SyncedLyrics)?>
            GetFallbackLyricsAsync(
                string title, string artist, string album, int durationSeconds, CancellationToken cancellation)
        {
            var lyrics = await _client.Downloader
                .FetchLyricsFromLRCLIB(LrclibInstance, title, artist, album, durationSeconds, cancellation)
                .ConfigureAwait(false);

            return Convert(lyrics);
        }

        private const int CoverArtResolution = 512;
        private const string LrclibInstance = "lrclib.net";

        private static (string, IReadOnlyList<(string, string)>)? Convert(
            (string plainLyrics, List<SyncLyrics> syncLyrics)? lyrics)
        {
            if (!lyrics.HasValue)
            {
                return null;
            }

            var synced = (lyrics.Value.syncLyrics ?? new List<SyncLyrics>())
                .Where(l => !string.IsNullOrEmpty(l.LrcTimestamp) && !string.IsNullOrEmpty(l.Line))
                .Select(l => (l.LrcTimestamp, l.Line))
                .ToArray();

            return (lyrics.Value.plainLyrics ?? string.Empty, synced);
        }
    }

    /// <summary>
    /// Writes one track to disk, correctly.
    ///
    /// This is where the verified upstream defect is corrected: DeezNET's
    /// WriteRawTrackToFile emits the whole final 2048-byte CDN cipher block
    /// instead of stopping at the declared size, so every file it produces carries
    /// up to 2047 bytes of junk past the end of the audio stream and fails
    /// `flac -t`. We truncate to the declared size, and we do it BEFORE tagging:
    /// tagging rewrites the file and legitimately grows it, so truncating
    /// afterwards cuts into the tag structure and leaves a different corruption.
    /// See ADR-0011.
    /// </summary>
    internal static class TrackWriter
    {
        public static async Task WriteAsync(
            IDeezerTransport transport,
            long trackId,
            string path,
            Quality quality,
            long declaredSize,
            string plainLyrics,
            CancellationToken cancellation)
        {
            await transport.WriteTrackAsync(trackId, path, quality, cancellation).ConfigureAwait(false);

            TruncateToDeclaredSize(path, declaredSize);

            await transport.ApplyMetadataAsync(trackId, path, plainLyrics, cancellation).ConfigureAwait(false);
        }

        private static void TruncateToDeclaredSize(string path, long declaredSize)
        {
            if (declaredSize <= 0)
            {
                return;
            }

            var info = new FileInfo(path);

            if (!info.Exists || info.Length <= declaredSize)
            {
                return;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            stream.SetLength(declaredSize);
        }
    }
}
