using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Plugin.Deezer;

namespace NzbDrone.Core.Indexers.Deezer
{
    public class DeezerRequestGenerator : IIndexerRequestGenerator
    {
        private const int PageSize = 100;
        private const int MaxPages = 30;

        public DeezerIndexerSettings Settings { get; set; }
        public Logger Logger { get; set; }
        public DeezerSession Session { get; set; }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            // Deezer has no usable "recent releases" feed for this plugin, and
            // SupportsRss is false, so there is nothing to return.
            return new IndexerPageableRequestChain();
        }

        public IndexerPageableRequestChain GetSearchRequests(AlbumSearchCriteria searchCriteria)
        {
            var chain = new IndexerPageableRequestChain();

            // Tier order decides recall: Lidarr only advances to the next tier
            // when the current one returns nothing, so the broader query must come
            // first. The field-qualified form is narrower and sometimes returns
            // zero where the plain form returns results, which previously made
            // whole albums unreachable (ADR-0006).
            chain.AddTier(GetRequests($"{searchCriteria.ArtistQuery} {searchCriteria.AlbumQuery}"));
            chain.AddTier(GetRequests($"artist:\"{searchCriteria.ArtistQuery}\" album:\"{searchCriteria.AlbumQuery}\""));

            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(ArtistSearchCriteria searchCriteria)
        {
            var chain = new IndexerPageableRequestChain();

            chain.AddTier(GetRequests(searchCriteria.ArtistQuery));
            chain.AddTier(GetRequests($"artist:\"{searchCriteria.ArtistQuery}\""));

            return chain;
        }

        private IEnumerable<IndexerRequest> GetRequests(string searchParameters)
        {
            if (Session == null)
            {
                yield break;
            }

            for (var page = 0; page < MaxPages; page++)
            {
                var data = new JObject
                {
                    ["query"] = searchParameters,
                    ["start"] = (page * PageSize).ToString(CultureInfo.InvariantCulture),
                    ["nb"] = PageSize.ToString(CultureInfo.InvariantCulture),
                    ["output"] = "ALBUM",
                    ["filter"] = "ALL"
                };

                var request = new IndexerRequest(GatewayUrl("search.music"), HttpAccept.Json);
                request.HttpRequest.SetContent(data.ToString(Formatting.None));
                request.HttpRequest.Method = System.Net.Http.HttpMethod.Post;
                request.HttpRequest.Cookies.Add("sid", Session.Transport.SessionId);

                yield return request;
            }
        }

        private string GatewayUrl(string method)
        {
            var parameters = new Dictionary<string, string>
            {
                ["api_version"] = "1.0",
                ["api_token"] = Session.Transport.ApiToken,
                ["input"] = "3",
                ["method"] = method
            };

            return DeezerGateway.Url(parameters);
        }
    }
}
