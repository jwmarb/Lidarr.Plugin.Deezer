using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Plugin.Deezer;

namespace NzbDrone.Core.Indexers.Deezer
{
    public class Deezer : HttpIndexerBase<DeezerIndexerSettings>
    {
        private readonly IDeezerSessions _sessions;

        public Deezer(IDeezerSessions sessions,
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IConfigService configService,
            IParsingService parsingService,
            Logger logger)
            : base(httpClient, indexerStatusService, configService, parsingService, logger)
        {
            _sessions = sessions;
        }

        public override string Name => "Deezer";
        public override string Protocol => nameof(DeezerDownloadProtocol);
        public override bool SupportsRss => false;
        public override bool SupportsSearch => true;
        public override int PageSize => 100;
        public override TimeSpan RateLimit => TimeSpan.FromSeconds(1);

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new DeezerRequestGenerator
            {
                Settings = Settings,
                Logger = _logger,
                Session = Authenticate()
            };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new DeezerParser
            {
                Settings = Settings,
                Logger = _logger,
                Session = Authenticate()
            };
        }

        /// <summary>
        /// Resolves the session for this indexer definition's own ARL. Sessions are
        /// cached per credential, so the generator and the parser built for one
        /// search share one session, and a later ARL change cannot retarget work
        /// already accepted (ADR-0001).
        /// </summary>
        private DeezerSession Authenticate()
        {
            try
            {
                return _sessions.AuthenticateAsync(Settings.Arl).GetAwaiter().GetResult();
            }
            catch (DeezerAuthenticationException ex)
            {
                _logger.Error(ex, "Deezer authentication failed; this indexer will return no results.");
                return null;
            }
        }

        protected override async Task Test(List<ValidationFailure> failures)
        {
            // A real assertion. The previous implementation's Test() had an empty
            // body, so the UI reported success for an expired or junk ARL.
            try
            {
                var session = await _sessions.AuthenticateAsync(Settings.Arl).ConfigureAwait(false);

                _logger.Info(
                    $"Deezer ARL authenticated as user {session.UserId} " +
                    $"(hq={session.Entitlements.HighQuality}, lossless={session.Entitlements.Lossless}).");
            }
            catch (DeezerAuthenticationException ex)
            {
                failures.Add(new ValidationFailure(nameof(Settings.Arl), ex.Message));
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure(string.Empty, $"Could not reach Deezer: {ex.Message}"));
            }
        }
    }
}
