using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Plugin.Deezer;

namespace NzbDrone.Core.Indexers.Deezer
{
    public class DeezerParser : IParseIndexerResponse
    {
        /// <summary>
        /// Caps how many album pages we fetch concurrently while enriching a page
        /// of search hits. Enrichment is one request per hit and was previously
        /// unbounded, against an API that rate-limits and bans.
        /// </summary>
        private const int MaxConcurrentEnrichment = 4;

        public DeezerIndexerSettings Settings { get; set; }
        public Logger Logger { get; set; }
        public DeezerSession Session { get; set; }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse response)
        {
            if (Session == null)
            {
                return Array.Empty<ReleaseInfo>();
            }

            var albumIds = ReadAlbumIds(response);

            if (albumIds.Count == 0)
            {
                return Array.Empty<ReleaseInfo>();
            }

            // IParseIndexerResponse is synchronous upstream, so the async
            // enrichment has to be awaited here. The blocking is confined to this
            // one call, with a bounded fan-out behind it (ADR-0006).
            var releases = EnrichAsync(albumIds).GetAwaiter().GetResult();

            return releases.OrderByDescending(r => r.Size).ToArray();
        }

        private List<long> ReadAlbumIds(IndexerResponse response)
        {
            var ids = new List<long>();

            JToken data;
            try
            {
                data = JObject.Parse(response.Content)["results"]?["data"];
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                Logger?.Warn(ex, "Could not parse the Deezer search response.");
                return ids;
            }

            if (data == null)
            {
                return ids;
            }

            foreach (var result in data)
            {
                try
                {
                    ids.Add(DeezerCatalogue.ReadSearchResultAlbumId(result));
                }
                catch (CatalogueDataException ex)
                {
                    // One malformed search hit should not fail the whole page.
                    Logger?.Trace(ex, "Skipping a malformed Deezer search result.");
                }
            }

            return ids;
        }

        private async Task<List<ReleaseInfo>> EnrichAsync(IReadOnlyList<long> albumIds)
        {
            var releases = new List<ReleaseInfo>();
            var sync = new object();

            using var gate = new SemaphoreSlim(MaxConcurrentEnrichment, MaxConcurrentEnrichment);

            var tasks = albumIds.Select(async albumId =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    var page = await Session.Transport.GetAlbumPageAsync(albumId, default).ConfigureAwait(false);
                    var album = DeezerCatalogue.ReadAlbum(page);

                    if (Settings?.HideAlbumsWithMissing == true && album.MissingTrackCount > 0)
                    {
                        return;
                    }

                    var built = BuildReleases(album);

                    lock (sync)
                    {
                        releases.AddRange(built);
                    }
                }
                catch (CatalogueDataException ex)
                {
                    Logger?.Trace(ex, $"Skipping Deezer album {albumId}: its page is missing required data.");
                }
                catch (Exception ex)
                {
                    Logger?.Warn(ex, $"Could not read Deezer album {albumId}.");
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);

            return releases;
        }

        /// <summary>
        /// One release per quality the session may fetch AND the album actually
        /// has bytes for. Gating on entitlement alone, as before, publishes
        /// releases that cannot be downloaded.
        /// </summary>
        private IEnumerable<ReleaseInfo> BuildReleases(AlbumFacts album)
        {
            var url = DeezerQuality.AlbumUrl(album.Id);

            foreach (var quality in Session.AvailableQualities(album))
            {
                var release = new ReleaseInfo
                {
                    Guid = DeezerQuality.ReleaseGuid(album.Id, quality),
                    Artist = album.Artist,
                    Album = album.Title,
                    Title = DeezerQuality.ReleaseTitle(
                        album.Artist, album.Title, album.Year, album.Explicit, quality),
                    DownloadUrl = url,
                    InfoUrl = url,
                    PublishDate = album.ReleaseDate ?? DateTime.UtcNow,
                    Size = album.TotalDeclaredSize(quality),
                    DownloadProtocol = nameof(DeezerDownloadProtocol)
                };

                DeezerQuality.Describe(release, quality);

                yield return release;
            }
        }
    }
}
