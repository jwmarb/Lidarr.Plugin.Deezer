using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Plugin.Deezer;

namespace NzbDrone.Core.Download.Clients.Deezer
{
    public class Deezer : DownloadClientBase<DeezerSettings>
    {
        private readonly IDownloadQueue _queue;
        private readonly IDeezerSessions _sessions;

        public Deezer(IDownloadQueue queue,
                      IDeezerSessions sessions,
                      IConfigService configService,
                      IDiskProvider diskProvider,
                      IRemotePathMappingService remotePathMappingService,
                      ILocalizationService localizationService,
                      Logger logger)
            : base(configService, diskProvider, remotePathMappingService, localizationService, logger)
        {
            _queue = queue;
            _sessions = sessions;
        }

        public override string Protocol => nameof(DeezerDownloadProtocol);

        public override string Name => "Deezer";

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            var info = DownloadClientItemClientInfo.FromDownloadClient(this, false);

            // Freshly allocated per poll: the host mutates these (it sets
            // DownloadClientInfo here, and Removed elsewhere), so they must never
            // be shared with queue state.
            return _queue.List().Select(snapshot => new DownloadClientItem
            {
                DownloadClientInfo = info,
                DownloadId = snapshot.Id,
                Title = snapshot.Title,
                TotalSize = snapshot.TotalSize,
                RemainingSize = snapshot.RemainingSize,
                RemainingTime = snapshot.RemainingTime,
                Status = snapshot.Status,
                CanMoveFiles = true,
                CanBeRemoved = true,
                OutputPath = snapshot.OutputPath.IsNotNullOrWhiteSpace()
                    ? new OsPath(snapshot.OutputPath)
                    : default
            }).ToList();
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            if (deleteData)
            {
                DeleteItemData(item);
            }

            // Lidarr calls this both when the user cancels and after a successful
            // import. The queue tells them apart by the item's state (ADR-0002).
            _queue.Remove(item.DownloadId);
        }

        public override async Task<string> Download(RemoteAlbum remoteAlbum, IIndexer indexer)
        {
            // The ARL belongs to the indexer definition that produced this release.
            // The host hands us that indexer on every call; the previous
            // implementation discarded it and relied on process-wide mutable state.
            var arl = ArlFrom(indexer);

            var session = await _sessions.AuthenticateAsync(arl).ConfigureAwait(false);

            var quality = DeezerQuality.Decode(remoteAlbum.Release);
            var albumId = DeezerQuality.DecodeAlbumId(remoteAlbum.Release);

            var albumPage = await session.Transport.GetAlbumPageAsync(albumId, default).ConfigureAwait(false);
            var album = DeezerCatalogue.ReadAlbum(albumPage);

            if (album.TotalDeclaredSize(quality) <= 0)
            {
                throw new DownloadClientException(
                    $"Deezer has no {quality} audio for '{album.Title}'.");
            }

            // Everything the download needs is captured here, as values.
            return _queue.Accept(new DownloadRequest(
                album,
                quality,
                remoteAlbum.Release.Title,
                session,
                Settings.DownloadPath,
                Settings.SaveSyncedLyrics,
                Settings.UseLRCLIB));
        }

        private static string ArlFrom(IIndexer indexer)
        {
            if (indexer?.Definition?.Settings is Indexers.Deezer.DeezerIndexerSettings settings &&
                settings.Arl.IsNotNullOrWhiteSpace())
            {
                return settings.Arl;
            }

            throw new DownloadClientException(
                "Could not read an ARL from the indexer that produced this release. " +
                "Check that the Deezer indexer still exists and has an ARL configured.");
        }

        public override DownloadClientInfo GetStatus()
        {
            return new DownloadClientInfo
            {
                IsLocalhost = true,
                OutputRootFolders = new List<OsPath> { new OsPath(Settings.DownloadPath) }
            };
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            // The download client owns no ARL, so it cannot test the credential;
            // that belongs to the indexer. It can check its own setting.
            if (Settings.DownloadPath.IsNullOrWhiteSpace())
            {
                failures.Add(new ValidationFailure(nameof(Settings.DownloadPath), "A download path is required."));
            }
        }
    }
}
