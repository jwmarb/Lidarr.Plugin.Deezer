using System;
using System.Globalization;
using DeezNET.Data;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Plugin.Deezer
{
    /// <summary>
    /// One of the three forms Deezer serves a track in. The single domain concept
    /// behind what Deezer, Lidarr and DeezNET each encode differently.
    /// </summary>
    public enum Quality
    {
        Mp3_128,
        Mp3_320,
        Flac
    }

    /// <summary>
    /// Thrown when a release's fields do not decode to a Quality this plugin
    /// published. Decoding is deliberately partial: see ADR-0009. Silently
    /// downgrading to MP3 128, as the previous implementation did, delivers a file
    /// the user did not ask for while Lidarr records the quality it requested.
    /// </summary>
    public class UnsupportedReleaseQualityException : Exception
    {
        public UnsupportedReleaseQualityException()
        {
        }

        public UnsupportedReleaseQualityException(string message)
            : base(message)
        {
        }

        public UnsupportedReleaseQualityException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Owns every encoding of Quality, in both directions.
    ///
    /// Interface notes:
    /// - <see cref="Describe"/> and <see cref="Decode"/> round-trip for all three
    ///   Quality values, and <see cref="Decode"/> throws rather than guessing.
    /// - The legacy codes 1/3/9 in the release Guid are frozen for blocklist
    ///   compatibility (ADR-0008); the blocklist matches on Guid by substring, so
    ///   changing them orphans existing rows.
    /// - Pure and stateless; safe to call from any thread.
    /// </summary>
    public static class DeezerQuality
    {
        public static Quality[] All { get; } = new[] { Quality.Mp3_128, Quality.Mp3_320, Quality.Flac };

        /// <summary>The Deezer album-page key holding this Quality's declared size.</summary>
        public static string DeclaredSizeKey(Quality quality) => quality switch
        {
            Quality.Mp3_128 => "FILESIZE_MP3_128",
            Quality.Mp3_320 => "FILESIZE_MP3_320",
            Quality.Flac => "FILESIZE_FLAC",
            _ => throw new UnsupportedReleaseQualityException($"No declared-size key for quality '{quality}'.")
        };

        public static string FileExtension(Quality quality) => quality switch
        {
            Quality.Mp3_128 => "mp3",
            Quality.Mp3_320 => "mp3",
            Quality.Flac => "flac",
            _ => throw new UnsupportedReleaseQualityException($"No file extension for quality '{quality}'.")
        };

        public static Bitrate ToBitrate(Quality quality) => quality switch
        {
            Quality.Mp3_128 => Bitrate.MP3_128,
            Quality.Mp3_320 => Bitrate.MP3_320,
            Quality.Flac => Bitrate.FLAC,
            _ => throw new UnsupportedReleaseQualityException($"No DeezNET bitrate for quality '{quality}'.")
        };

        /// <summary>Frozen for blocklist compatibility. See ADR-0008.</summary>
        private static int LegacyCode(Quality quality) => quality switch
        {
            Quality.Mp3_128 => 1,
            Quality.Mp3_320 => 3,
            Quality.Flac => 9,
            _ => throw new UnsupportedReleaseQualityException($"No legacy code for quality '{quality}'.")
        };

        private static string Codec(Quality quality) => quality == Quality.Flac ? "FLAC" : "MP3";

        private static string Container(Quality quality) => quality switch
        {
            Quality.Mp3_128 => "128",
            Quality.Mp3_320 => "320",
            Quality.Flac => "Lossless",
            _ => throw new UnsupportedReleaseQualityException($"No container for quality '{quality}'.")
        };

        private static string DisplayFormat(Quality quality) => quality switch
        {
            Quality.Mp3_128 => "MP3 128",
            Quality.Mp3_320 => "MP3 320",
            Quality.Flac => "FLAC",
            _ => throw new UnsupportedReleaseQualityException($"No display format for quality '{quality}'.")
        };

        public static string ReleaseGuid(long albumId, Quality quality) =>
            string.Format(CultureInfo.InvariantCulture, "Deezer-{0}-{1}", albumId, LegacyCode(quality));

        public static string AlbumUrl(long albumId) =>
            string.Format(CultureInfo.InvariantCulture, "https://deezer.com/album/{0}", albumId);

        /// <summary>
        /// Builds the release title. This is the one place the "[Explicit]" and
        /// "[WEB]" markers are assembled; the queue reuses the accepted title
        /// rather than rebuilding its own.
        /// </summary>
        public static string ReleaseTitle(string artist, string album, int? year, bool isExplicit, Quality quality)
        {
            var title = $"{artist} - {album}";

            if (year.HasValue && year.Value > 0)
            {
                title += string.Format(CultureInfo.InvariantCulture, " ({0})", year.Value);
            }

            if (isExplicit)
            {
                title += " [Explicit]";
            }

            return title + $" [{DisplayFormat(quality)}] [WEB]";
        }

        /// <summary>
        /// Stamps the Lidarr release fields for a Quality. Paired with
        /// <see cref="Decode"/>, which recovers the Quality from them.
        /// </summary>
        public static void Describe(ReleaseInfo release, Quality quality)
        {
            if (release == null)
            {
                throw new ArgumentNullException(nameof(release));
            }

            release.Codec = Codec(quality);
            release.Container = Container(quality);
        }

        /// <summary>
        /// Recovers the Quality a release was published at. Partial by design:
        /// unrecognised input throws instead of degrading to MP3 128 (ADR-0009).
        /// </summary>
        public static Quality Decode(ReleaseInfo release)
        {
            if (release == null)
            {
                throw new ArgumentNullException(nameof(release));
            }

            foreach (var quality in All)
            {
                if (string.Equals(release.Codec, Codec(quality), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(release.Container, Container(quality), StringComparison.OrdinalIgnoreCase))
                {
                    return quality;
                }
            }

            throw new UnsupportedReleaseQualityException(
                $"Release '{release.Title}' has codec '{release.Codec}' and container '{release.Container}', " +
                "which do not match any quality this plugin publishes.");
        }

        /// <summary>Extracts the Deezer album id from a release's download URL.</summary>
        public static long DecodeAlbumId(ReleaseInfo release)
        {
            if (release == null)
            {
                throw new ArgumentNullException(nameof(release));
            }

            var url = (release.DownloadUrl ?? string.Empty).Trim();
            var lastSlash = url.LastIndexOf('/');

            if (lastSlash >= 0 && lastSlash < url.Length - 1 &&
                long.TryParse(url.Substring(lastSlash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                return id;
            }

            throw new UnsupportedReleaseQualityException($"Could not read a Deezer album id from '{url}'.");
        }
    }
}
