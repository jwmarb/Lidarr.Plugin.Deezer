using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace NzbDrone.Plugin.Deezer
{
    /// <summary>
    /// Thrown when a Deezer document is missing a fact we require. Named so the
    /// caller learns which document and which fact, without learning Deezer's
    /// JSON key paths.
    /// </summary>
    public class CatalogueDataException : Exception
    {
        public CatalogueDataException()
        {
        }

        public CatalogueDataException(string message)
            : base(message)
        {
        }

        public CatalogueDataException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>Per-quality declared sizes for one track, as Deezer reports them.</summary>
    public sealed class DeclaredSizes
    {
        private readonly Dictionary<Quality, long> _sizes;

        public DeclaredSizes(IReadOnlyDictionary<Quality, long> sizes)
        {
            _sizes = new Dictionary<Quality, long>();
            foreach (var pair in sizes)
            {
                _sizes[pair.Key] = pair.Value;
            }
        }

        public long For(Quality quality) => _sizes.TryGetValue(quality, out var size) ? size : 0L;
    }

    public sealed class TrackFacts
    {
        public TrackFacts(long id, DeclaredSizes declaredSizes, bool isAvailable)
        {
            Id = id;
            DeclaredSizes = declaredSizes;
            IsAvailable = isAvailable;
        }

        public long Id { get; }
        public DeclaredSizes DeclaredSizes { get; }

        /// <summary>False when Deezer reports FILESIZE == 0, its missing-track signal.</summary>
        public bool IsAvailable { get; }
    }

    /// <summary>
    /// An album, parsed once. Album naming facts come only from the album page;
    /// mixing them with track-page facts is what produced inconsistent titles.
    /// </summary>
    public sealed class AlbumFacts
    {
        public AlbumFacts(
            long id,
            string title,
            string artist,
            IReadOnlyList<string> artists,
            DateTime? releaseDate,
            bool isExplicit,
            IReadOnlyList<TrackFacts> tracks)
        {
            Id = id;
            Title = title;
            Artist = artist;
            Artists = artists;
            ReleaseDate = releaseDate;
            Explicit = isExplicit;
            Tracks = tracks;
        }

        public long Id { get; }

        /// <summary>Already carries the album page's VERSION suffix when present.</summary>
        public string Title { get; }
        public string Artist { get; }
        public IReadOnlyList<string> Artists { get; }

        /// <summary>Null when Deezer gave no usable date. Never throws on "0000-00-00".</summary>
        public DateTime? ReleaseDate { get; }
        public bool Explicit { get; }
        public IReadOnlyList<TrackFacts> Tracks { get; }

        public int? Year => ReleaseDate?.Year;
        public int TrackCount => Tracks.Count;

        public long TotalDeclaredSize(Quality quality) => Tracks.Sum(t => t.DeclaredSizes.For(quality));

        public int MissingTrackCount => Tracks.Count(t => !t.IsAvailable);
    }

    /// <summary>One track, parsed once, with only track-page facts.</summary>
    public sealed class TrackDetail
    {
        public TrackDetail(
            long id,
            string title,
            string artist,
            IReadOnlyList<string> artists,
            int trackNumber,
            int durationSeconds)
        {
            Id = id;
            Title = title;
            Artist = artist;
            Artists = artists;
            TrackNumber = trackNumber;
            DurationSeconds = durationSeconds;
        }

        public long Id { get; }

        /// <summary>Already carries the track page's VERSION suffix when present.</summary>
        public string Title { get; }
        public string Artist { get; }
        public IReadOnlyList<string> Artists { get; }
        public int TrackNumber { get; }
        public int DurationSeconds { get; }
    }

    /// <summary>
    /// Parses Deezer's untyped documents into domain facts, once.
    ///
    /// Interface notes:
    /// - This is the only place that knows Deezer's JSON shape. No caller touches
    ///   a <see cref="JToken"/>.
    /// - One rule for malformed dates: "0000-00-00", empty, and unparseable all
    ///   become null. The indexer and the download path previously disagreed about
    ///   this, so an album that indexed fine threw at download time.
    /// - Pure and stateless; safe to call from any thread.
    /// </summary>
    public static class DeezerCatalogue
    {
        public static AlbumFacts ReadAlbum(JToken albumPage)
        {
            if (albumPage == null)
            {
                throw new CatalogueDataException("Album page was null.");
            }

            var data = albumPage["DATA"] ?? throw new CatalogueDataException("Album page has no DATA.");

            var id = RequireLong(data, "ALB_ID", "album page", "album id");
            var title = WithVersion(RequireString(data, "ALB_TITLE", "album page", "album title"), data["VERSION"]);
            var artist = RequireString(data, "ART_NAME", "album page", "album artist");

            var songs = albumPage["SONGS"]?["data"];
            if (songs == null)
            {
                throw new CatalogueDataException("Album page has no SONGS.data track list.");
            }

            var tracks = new List<TrackFacts>();
            foreach (var song in songs)
            {
                var trackId = RequireLong(song, "SNG_ID", "album page", "track id");

                var sizes = new Dictionary<Quality, long>();
                foreach (var quality in DeezerQuality.All)
                {
                    sizes[quality] = OptionalLong(song[DeezerQuality.DeclaredSizeKey(quality)]);
                }

                // FILESIZE == 0 is Deezer's missing-track signal.
                var available = OptionalLong(song["FILESIZE"]) != 0L;

                tracks.Add(new TrackFacts(trackId, new DeclaredSizes(sizes), available));
            }

            return new AlbumFacts(
                id,
                title,
                artist,
                ReadArtists(data, artist),
                ReadReleaseDate(data),
                ReadExplicit(data),
                tracks);
        }

        public static TrackDetail ReadTrack(JToken trackPage)
        {
            if (trackPage == null)
            {
                throw new CatalogueDataException("Track page was null.");
            }

            var data = trackPage["DATA"] ?? throw new CatalogueDataException("Track page has no DATA.");

            var id = RequireLong(data, "SNG_ID", "track page", "track id");
            var title = WithVersion(RequireString(data, "SNG_TITLE", "track page", "track title"), data["VERSION"]);
            var artist = RequireString(data, "ART_NAME", "track page", "track artist");

            var trackNumber = (int)OptionalLong(data["TRACK_NUMBER"]);
            var duration = (int)OptionalLong(data["DURATION"]);

            return new TrackDetail(id, title, artist, ReadArtists(data, artist), trackNumber, duration);
        }

        /// <summary>
        /// Reads one album summary from a search result. Note that search results
        /// omit DIGITAL_RELEASE_DATE, so the date falls through to the physical one.
        /// </summary>
        public static long ReadSearchResultAlbumId(JToken searchResult)
        {
            if (searchResult == null)
            {
                throw new CatalogueDataException("Search result was null.");
            }

            return RequireLong(searchResult, "ALB_ID", "search result", "album id");
        }

        private static IReadOnlyList<string> ReadArtists(JToken data, string fallback)
        {
            var artists = data["ARTISTS"];
            if (artists == null)
            {
                return new[] { fallback };
            }

            var names = artists
                .Select(a => a?["ART_NAME"]?.ToString())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToArray();

            return names.Length > 0 ? names : new[] { fallback };
        }

        private static bool ReadExplicit(JToken data)
        {
            // Preserves the rule the old DeezerGwAlbum DTO encoded.
            var content = data["EXPLICIT_ALBUM_CONTENT"];
            if (content == null)
            {
                return false;
            }

            var lyrics = OptionalLong(content["EXPLICIT_LYRICS_STATUS"]);
            var cover = OptionalLong(content["EXPLICIT_COVER_STATUS"]);

            return lyrics == 1 || lyrics == 4 || cover == 1;
        }

        /// <summary>
        /// The single malformed-date rule. Deezer serves "0000-00-00" and empty
        /// strings; both mean "no date", never an exception.
        /// </summary>
        private static DateTime? ReadReleaseDate(JToken data)
        {
            return ParseDate(data["DIGITAL_RELEASE_DATE"]) ?? ParseDate(data["PHYSICAL_RELEASE_DATE"]);
        }

        private static DateTime? ParseDate(JToken token)
        {
            var raw = token?.ToString();

            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("0000", StringComparison.Ordinal))
            {
                return null;
            }

            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : (DateTime?)null;
        }

        private static string WithVersion(string title, JToken version)
        {
            var suffix = version?.ToString();
            return string.IsNullOrWhiteSpace(suffix) ? title : $"{title} {suffix}";
        }

        private static string RequireString(JToken data, string key, string document, string fact)
        {
            var value = data[key]?.ToString();

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new CatalogueDataException($"The {document} is missing its {fact}.");
            }

            return value;
        }

        private static long RequireLong(JToken data, string key, string document, string fact)
        {
            var token = data[key];
            var raw = token?.ToString();

            if (!string.IsNullOrWhiteSpace(raw) &&
                long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            throw new CatalogueDataException($"The {document} is missing its {fact}.");
        }

        private static long OptionalLong(JToken token)
        {
            var raw = token?.ToString();

            return !string.IsNullOrWhiteSpace(raw) &&
                   long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0L;
        }
    }
}
