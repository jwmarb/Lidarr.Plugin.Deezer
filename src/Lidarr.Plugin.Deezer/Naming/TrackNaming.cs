using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NzbDrone.Plugin.Deezer
{
    /// <summary>Thrown when an expanded template cannot form a safe relative path.</summary>
    public class InvalidTrackPathException : Exception
    {
        public InvalidTrackPathException()
        {
        }

        public InvalidTrackPathException(string message)
            : base(message)
        {
        }

        public InvalidTrackPathException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// A relative path proven safe to join beneath the download root. The
    /// constructor is private, so an unvalidated path cannot be represented.
    /// </summary>
    public sealed class ValidatedRelativePath
    {
        private ValidatedRelativePath(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public override string ToString() => Value;

        /// <summary>
        /// Validates the whole assembled path, not each substitution. The previous
        /// implementation sanitised substitutions individually and never checked
        /// the result, so a traversal in the template itself was never rejected.
        /// </summary>
        internal static ValidatedRelativePath Create(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                throw new InvalidTrackPathException("Track path expanded to nothing.");
            }

            var normalised = candidate.Replace('\\', '/');

            if (normalised.StartsWith("/", StringComparison.Ordinal))
            {
                throw new InvalidTrackPathException($"Track path '{candidate}' is rooted.");
            }

            // Windows drive-qualified ("C:/...") and UNC ("//server/share") paths.
            if (normalised.StartsWith("//", StringComparison.Ordinal) ||
                (normalised.Length > 1 && normalised[1] == ':'))
            {
                throw new InvalidTrackPathException($"Track path '{candidate}' is not relative.");
            }

            var segments = normalised.Split('/').Where(s => s.Length > 0).ToArray();

            if (segments.Length == 0)
            {
                throw new InvalidTrackPathException($"Track path '{candidate}' has no segments.");
            }

            foreach (var segment in segments)
            {
                if (segment == "." || segment == "..")
                {
                    throw new InvalidTrackPathException($"Track path '{candidate}' contains a '{segment}' segment.");
                }
            }

            return new ValidatedRelativePath(string.Join("/", segments));
        }
    }

    /// <summary>The paths one track writes to, relative to the download root.</summary>
    public sealed class TrackPaths
    {
        public TrackPaths(ValidatedRelativePath audio, ValidatedRelativePath syncedLyrics)
        {
            Audio = audio;
            SyncedLyrics = syncedLyrics;
        }

        public ValidatedRelativePath Audio { get; }
        public ValidatedRelativePath SyncedLyrics { get; }
    }

    /// <summary>
    /// Builds the relative paths for a track from typed facts.
    ///
    /// Interface notes:
    /// - Takes <see cref="AlbumFacts"/> and <see cref="TrackDetail"/>, never raw
    ///   Deezer documents, so Deezer's JSON shape does not reach naming.
    /// - Album substitutions come only from the album, track substitutions only
    ///   from the track. Both titles already carry their own VERSION suffix.
    /// - A missing release date yields an empty %year%; it never throws. The old
    ///   implementation hard-parsed the date and died on "0000-00-00".
    /// - Returns validated paths, so callers cannot write an unchecked path.
    /// - Pure and stateless; safe to call from any thread.
    /// </summary>
    public static class TrackNaming
    {
        public const string DefaultAlbumTemplate = "%albumartist%/%album%/";
        public const string DefaultFileTemplate = "%track% - %title%.%ext%";

        public static TrackPaths CreatePaths(AlbumFacts album, TrackDetail track, Quality quality)
        {
            return CreatePaths(album, track, quality, DefaultAlbumTemplate, DefaultFileTemplate);
        }

        public static TrackPaths CreatePaths(
            AlbumFacts album,
            TrackDetail track,
            Quality quality,
            string albumTemplate,
            string fileTemplate)
        {
            if (album == null)
            {
                throw new ArgumentNullException(nameof(album));
            }

            if (track == null)
            {
                throw new ArgumentNullException(nameof(track));
            }

            var directory = Expand(albumTemplate, album, track, string.Empty);

            var audio = ValidatedRelativePath.Create(
                directory + Expand(fileTemplate, album, track, DeezerQuality.FileExtension(quality)));

            var lyrics = ValidatedRelativePath.Create(
                directory + Expand(fileTemplate, album, track, "lrc"));

            return new TrackPaths(audio, lyrics);
        }

        private static string Expand(string template, AlbumFacts album, TrackDetail track, string extension)
        {
            var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["%title%"] = track.Title,
                ["%album%"] = album.Title,
                ["%albumartist%"] = album.Artist,
                ["%artist%"] = track.Artist,
                ["%albumartists%"] = string.Join("; ", album.Artists),
                ["%artists%"] = string.Join("; ", track.Artists),
                ["%track%"] = track.TrackNumber.ToString("00", CultureInfo.InvariantCulture),
                ["%trackcount%"] = album.TrackCount.ToString(CultureInfo.InvariantCulture),
                ["%year%"] = album.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["%ext%"] = extension
            };

            var builder = new StringBuilder(template);

            foreach (var pair in replacements)
            {
                builder.Replace(pair.Key, CleanSegment(pair.Value));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Strips characters illegal in a filename, plus both separators, so a
        /// substituted value cannot invent a new path segment. An album literally
        /// titled "AC/DC" must not become two directories.
        /// </summary>
        private static string CleanSegment(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);

            foreach (var c in value)
            {
                builder.Append(invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c);
            }

            return builder.ToString();
        }
    }
}
