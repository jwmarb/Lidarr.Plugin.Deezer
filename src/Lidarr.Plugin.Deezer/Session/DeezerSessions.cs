using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DeezNET;
using Newtonsoft.Json.Linq;

namespace NzbDrone.Plugin.Deezer
{
    /// <summary>What an authenticated account may actually fetch, as Deezer reports it.</summary>
    public sealed class Entitlements
    {
        public Entitlements(bool streaming, bool highQuality, bool lossless)
        {
            Streaming = streaming;
            HighQuality = highQuality;
            Lossless = lossless;
        }

        public bool Streaming { get; }
        public bool HighQuality { get; }
        public bool Lossless { get; }

        /// <summary>
        /// Whether the account may fetch this quality. Distinct from whether a
        /// given album has bytes at that quality, which is the album's declared
        /// size. Conflating the two is how a 0-byte release gets published.
        /// </summary>
        public bool Allows(Quality quality) => quality switch
        {
            Quality.Mp3_128 => Streaming,
            Quality.Mp3_320 => HighQuality,
            Quality.Flac => Lossless,
            _ => false
        };
    }

    /// <summary>
    /// Thrown when an ARL does not yield a usable session. Distinguishes a bad
    /// credential from Deezer being unreachable, which the old code could not.
    /// </summary>
    public class DeezerAuthenticationException : Exception
    {
        public DeezerAuthenticationException()
        {
        }

        public DeezerAuthenticationException(string message)
            : base(message)
        {
        }

        public DeezerAuthenticationException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// The port at the Deezer seam. Two adapters justify it: the production
    /// adapter over DeezNET, and a deterministic fake for tests (ADR-0007).
    /// </summary>
    public interface IDeezerTransport
    {
        Task<JToken> AuthenticateAsync(string arl, CancellationToken cancellation);

        Task<JToken> GetAlbumPageAsync(long albumId, CancellationToken cancellation);

        Task<JToken> GetTrackPageAsync(long trackId, CancellationToken cancellation);

        Task WriteTrackAsync(long trackId, string path, Quality quality, CancellationToken cancellation);

        Task ApplyMetadataAsync(long trackId, string path, string plainLyrics, CancellationToken cancellation);

        Task<(string PlainLyrics, IReadOnlyList<(string Timestamp, string Line)> SyncedLyrics)?> GetDeezerLyricsAsync(
            long trackId, CancellationToken cancellation);

        Task<(string PlainLyrics, IReadOnlyList<(string Timestamp, string Line)> SyncedLyrics)?> GetFallbackLyricsAsync(
            string title, string artist, string album, int durationSeconds, CancellationToken cancellation);

        string SessionId { get; }

        string ApiToken { get; }
    }

    /// <summary>
    /// An authenticated Deezer session, pinned to the ARL it was created from.
    ///
    /// Interface notes:
    /// - Only <see cref="DeezerSessions"/> constructs one, and only after proving
    ///   the session is not anonymous. An anonymous session is therefore not
    ///   representable: holding a DeezerSession means authentication succeeded.
    /// - <see cref="Entitlements"/> is an immutable snapshot taken at
    ///   authentication. It cannot change under an in-flight operation.
    /// - The underlying client is never exposed. Callers reach Deezer through
    ///   <see cref="Transport"/>, which speaks in domain terms.
    /// </summary>
    public sealed class DeezerSession
    {
        internal DeezerSession(long userId, Entitlements entitlements, IDeezerTransport transport)
        {
            UserId = userId;
            Entitlements = entitlements;
            Transport = transport;
        }

        public long UserId { get; }

        public Entitlements Entitlements { get; }

        internal IDeezerTransport Transport { get; }

        /// <summary>
        /// The qualities this session may fetch for an album, intersecting account
        /// entitlement with the album actually having bytes at that quality.
        /// </summary>
        public IReadOnlyList<Quality> AvailableQualities(AlbumFacts album)
        {
            if (album == null)
            {
                throw new ArgumentNullException(nameof(album));
            }

            var available = new List<Quality>();

            foreach (var quality in DeezerQuality.All)
            {
                if (Entitlements.Allows(quality) && album.TotalDeclaredSize(quality) > 0)
                {
                    available.Add(quality);
                }
            }

            return available;
        }
    }

    public interface IDeezerSessions
    {
        /// <summary>
        /// Returns a session proven to be authenticated with streaming rights, or
        /// throws. Never returns an anonymous session.
        /// </summary>
        Task<DeezerSession> AuthenticateAsync(string arl, CancellationToken cancellation = default);
    }

    /// <summary>
    /// Authenticates ARLs and nothing else (ADR-0005).
    ///
    /// Registered as a plugin interface, so Lidarr's container gives the indexer
    /// and the download client the same instance (ADR-0003). No static state.
    /// </summary>
    public class DeezerSessions : IDeezerSessions
    {
        private readonly Dictionary<string, DeezerSession> _sessions = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Func<IDeezerTransport> _transportFactory;

        public DeezerSessions()
            : this(() => new DeezNetTransport())
        {
        }

        internal DeezerSessions(Func<IDeezerTransport> transportFactory)
        {
            _transportFactory = transportFactory;
        }

        public async Task<DeezerSession> AuthenticateAsync(string arl, CancellationToken cancellation = default)
        {
            if (string.IsNullOrWhiteSpace(arl))
            {
                throw new DeezerAuthenticationException("No ARL was configured for this indexer.");
            }

            var key = arl.Trim();

            await _gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (_sessions.TryGetValue(key, out var existing))
                {
                    return existing;
                }

                // Authenticate on a transport of its own, so a failed candidate
                // cannot disturb a session already serving other operations. The
                // old ARLUtilities.IsValid mutated the shared client to "validate".
                var transport = _transportFactory();
                var userData = await transport.AuthenticateAsync(key, cancellation).ConfigureAwait(false);

                var session = Interpret(userData, transport);
                _sessions[key] = session;
                return session;
            }
            finally
            {
                _gate.Release();
            }
        }

        private static DeezerSession Interpret(JToken userData, IDeezerTransport transport)
        {
            var user = userData?["USER"];

            if (user == null)
            {
                throw new DeezerAuthenticationException("Deezer returned no user data for this ARL.");
            }

            var userId = user["USER_ID"]?.Value<long>() ?? 0L;
            var options = user["OPTIONS"];

            var entitlements = new Entitlements(
                options?["web_streaming"]?.Value<bool>() ?? false,
                options?["web_hq"]?.Value<bool>() ?? false,
                options?["web_lossless"]?.Value<bool>() ?? false);

            // USER_ID == 0 is Deezer's anonymous session. SetARL does not throw on a
            // bad ARL, it silently returns this, which is the trap that let an
            // expired credential pass every gate in the old implementation.
            if (userId == 0)
            {
                throw new DeezerAuthenticationException(
                    "The ARL did not authenticate: Deezer returned an anonymous session. It is expired or invalid.");
            }

            if (!entitlements.Streaming)
            {
                throw new DeezerAuthenticationException(
                    $"The ARL authenticated as user {userId}, but the account has no streaming entitlement.");
            }

            return new DeezerSession(userId, entitlements, transport);
        }
    }
}
