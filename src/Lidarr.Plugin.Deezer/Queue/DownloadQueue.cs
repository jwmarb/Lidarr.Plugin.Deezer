using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;

namespace NzbDrone.Plugin.Deezer
{
    /// <summary>
    /// Everything one download needs, captured when it is accepted. All values;
    /// nothing here is read from shared mutable state later (ADR-0001).
    /// </summary>
    public sealed class DownloadRequest
    {
        public DownloadRequest(
            AlbumFacts album,
            Quality quality,
            string title,
            DeezerSession session,
            string downloadPath,
            bool saveSyncedLyrics,
            bool useFallbackLyrics)
        {
            Album = album;
            Quality = quality;
            Title = title;
            Session = session;
            DownloadPath = downloadPath;
            SaveSyncedLyrics = saveSyncedLyrics;
            UseFallbackLyrics = useFallbackLyrics;
        }

        public AlbumFacts Album { get; }
        public Quality Quality { get; }
        public string Title { get; }
        public DeezerSession Session { get; }
        public string DownloadPath { get; }
        public bool SaveSyncedLyrics { get; }
        public bool UseFallbackLyrics { get; }
    }

    /// <summary>A point-in-time view of one download. A value; never live state.</summary>
    public sealed class DownloadSnapshot
    {
        public DownloadSnapshot(
            string id,
            string title,
            DownloadItemStatus status,
            long totalSize,
            long downloadedSize,
            TimeSpan? remainingTime,
            string outputPath)
        {
            Id = id;
            Title = title;
            Status = status;
            TotalSize = totalSize;
            DownloadedSize = downloadedSize;
            RemainingTime = remainingTime;
            OutputPath = outputPath;
        }

        public string Id { get; }
        public string Title { get; }
        public DownloadItemStatus Status { get; }
        public long TotalSize { get; }
        public long DownloadedSize { get; }
        public TimeSpan? RemainingTime { get; }
        public string OutputPath { get; }

        /// <summary>Clamped at zero: progress accounting must never report a negative remainder.</summary>
        public long RemainingSize => Math.Max(0, TotalSize - DownloadedSize);
    }

    /// <summary>
    /// Owns the lifecycle of accepted downloads.
    ///
    /// Interface notes:
    /// - <see cref="Accept"/> captures everything atomically and returns an id.
    ///   The item is registered with its cancellation before it is runnable, so a
    ///   download can never execute with CancellationToken.None. In the previous
    ///   implementation 29% did.
    /// - <see cref="List"/> returns freshly allocated values, so a host poll
    ///   cannot tear live state or mutate ours.
    /// - <see cref="Remove"/> is idempotent and distinguishes its two callers: a
    ///   non-terminal item means the user cancelled, a terminal one means Lidarr
    ///   finished importing and the record may be dropped (ADR-0002).
    /// - Terminal records are retained until Lidarr acknowledges them, because
    ///   evicting early makes a completed download untrackable before import runs.
    /// - All members are safe to call from any thread.
    /// </summary>
    public interface IDownloadQueue
    {
        string Accept(DownloadRequest request);

        IReadOnlyList<DownloadSnapshot> List();

        void Remove(string id);
    }

    public class DownloadQueue : IDownloadQueue, IDisposable
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, DownloadEntry> _entries = new();
        private readonly SemaphoreSlim _concurrency;
        private readonly Logger _logger;
        private bool _disposed;

        public DownloadQueue(Logger logger)
            : this(logger, 1)
        {
        }

        public DownloadQueue(Logger logger, int maxConcurrentAlbums)
        {
            _logger = logger;
            _concurrency = new SemaphoreSlim(maxConcurrentAlbums, maxConcurrentAlbums);
        }

        public string Accept(DownloadRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var entry = new DownloadEntry(request);

            // Register before starting, so the runner can never observe an
            // unregistered cancellation. This ordering is the fix for the
            // publish-before-register race.
            lock (_lock)
            {
                _entries[entry.Id] = entry;
            }

            entry.Run = Task.Run(() => RunAsync(entry));

            return entry.Id;
        }

        public IReadOnlyList<DownloadSnapshot> List()
        {
            lock (_lock)
            {
                // Display order: completed first, then in-flight, then waiting.
                return _entries.Values
                    .OrderBy(e => e.Status switch
                    {
                        DownloadItemStatus.Completed => 0,
                        DownloadItemStatus.Downloading => 1,
                        DownloadItemStatus.Queued => 2,
                        _ => 3
                    })
                    .ThenBy(e => e.AcceptedAt)
                    .Select(e => e.Snapshot())
                    .ToArray();
            }
        }

        public void Remove(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            DownloadEntry entry;

            lock (_lock)
            {
                // Idempotent: an unknown id is not an error. The old RemoveItem
                // threw KeyNotFoundException here.
                if (!_entries.TryGetValue(id, out entry))
                {
                    return;
                }

                _entries.Remove(id);
            }

            if (entry.IsTerminal)
            {
                // Acknowledgement: Lidarr imported it and is done with the record.
                _logger.Debug($"Deezer download acknowledged and dropped: {entry.Request.Title}");
            }
            else
            {
                // Cancellation: the user asked for in-flight work to stop.
                _logger.Info($"Deezer download cancelled: {entry.Request.Title}");
                entry.Cancel();
            }

            entry.Dispose();
        }

        private async Task RunAsync(DownloadEntry entry)
        {
            var token = entry.Token;

            try
            {
                await _concurrency.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancelled while waiting for a slot; never started, nothing to undo.
                entry.MarkCancelled();
                return;
            }

            try
            {
                entry.MarkDownloading();
                await DownloadAlbumAsync(entry, token).ConfigureAwait(false);
                entry.MarkCompleted();
            }
            catch (OperationCanceledException)
            {
                // Catches TaskCanceledException too, since it derives from this.
                // The old code caught only the derived type, so a cancel was
                // logged as an error and counted as a failed track.
                entry.MarkCancelled();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Deezer download failed: {entry.Request.Title}");
                entry.MarkFailed();
            }
            finally
            {
                _concurrency.Release();
            }
        }

        private async Task DownloadAlbumAsync(DownloadEntry entry, CancellationToken cancellation)
        {
            var request = entry.Request;
            var transport = request.Session.Transport;

            foreach (var track in request.Album.Tracks)
            {
                cancellation.ThrowIfCancellationRequested();

                if (!track.IsAvailable)
                {
                    _logger.Debug($"Skipping unavailable Deezer track {track.Id}");
                    continue;
                }

                var declaredSize = track.DeclaredSizes.For(request.Quality);

                var trackPage = await transport.GetTrackPageAsync(track.Id, cancellation).ConfigureAwait(false);
                var detail = DeezerCatalogue.ReadTrack(trackPage);

                var paths = TrackNaming.CreatePaths(request.Album, detail, request.Quality);
                var audioPath = Path.Combine(request.DownloadPath, paths.Audio.Value);
                var directory = Path.GetDirectoryName(audioPath);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    entry.SetOutputPath(directory);
                }

                var lyrics = await ResolveLyricsAsync(request, detail, cancellation).ConfigureAwait(false);

                await TrackWriter
                    .WriteAsync(transport, track.Id, audioPath, request.Quality, declaredSize, lyrics.Plain, cancellation)
                    .ConfigureAwait(false);

                if (request.SaveSyncedLyrics && lyrics.Synced.Count > 0)
                {
                    await WriteLrcAsync(
                        Path.Combine(request.DownloadPath, paths.SyncedLyrics.Value), lyrics.Synced, cancellation)
                        .ConfigureAwait(false);
                }

                // Credit bytes actually on disk, not the table figure.
                entry.AddDownloadedBytes(ActualSize(audioPath, declaredSize));
            }
        }

        private static long ActualSize(string path, long fallback)
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : fallback;
        }

        private static async Task<(string Plain, IReadOnlyList<(string Timestamp, string Line)> Synced)>
            ResolveLyricsAsync(DownloadRequest request, TrackDetail detail, CancellationToken cancellation)
        {
            var transport = request.Session.Transport;

            var plain = string.Empty;
            IReadOnlyList<(string Timestamp, string Line)> synced = Array.Empty<(string, string)>();

            var fromDeezer = await transport.GetDeezerLyricsAsync(detail.Id, cancellation).ConfigureAwait(false);

            if (fromDeezer.HasValue)
            {
                plain = fromDeezer.Value.PlainLyrics ?? string.Empty;
                synced = fromDeezer.Value.SyncedLyrics ?? Array.Empty<(string, string)>();
            }

            var needPlain = string.IsNullOrWhiteSpace(plain);
            var needSynced = request.SaveSyncedLyrics && synced.Count == 0;

            if (request.UseFallbackLyrics && (needPlain || needSynced))
            {
                var fallback = await transport
                    .GetFallbackLyricsAsync(
                        detail.Title, detail.Artist, request.Album.Title, detail.DurationSeconds, cancellation)
                    .ConfigureAwait(false);

                if (fallback.HasValue)
                {
                    if (needPlain)
                    {
                        plain = fallback.Value.PlainLyrics ?? plain;
                    }

                    if (needSynced)
                    {
                        synced = fallback.Value.SyncedLyrics ?? synced;
                    }
                }
            }

            return (plain, synced);
        }

        private static async Task WriteLrcAsync(
            string path, IReadOnlyList<(string Timestamp, string Line)> synced, CancellationToken cancellation)
        {
            var builder = new StringBuilder();

            foreach (var (timestamp, line) in synced)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"{timestamp} {line}");
            }

            await File.WriteAllTextAsync(path, builder.ToString(), cancellation).ConfigureAwait(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
            {
                return;
            }

            _disposed = true;

            List<DownloadEntry> entries;
            lock (_lock)
            {
                entries = _entries.Values.ToList();
                _entries.Clear();
            }

            foreach (var entry in entries)
            {
                entry.Cancel();
                entry.Dispose();
            }

            // Only after in-flight work has been signalled and awaited, so a
            // detached task cannot release a disposed semaphore.
            try
            {
                Task.WaitAll(entries.Select(e => e.Run).Where(t => t != null).ToArray(), TimeSpan.FromSeconds(10));
            }
            catch (AggregateException)
            {
                // Cancellation faults during shutdown are expected.
            }

            _concurrency.Dispose();
        }

        /// <summary>
        /// One accepted download. Owns its own cancellation and its progress; all
        /// mutation goes through the queue's lock.
        /// </summary>
        private sealed class DownloadEntry : IDisposable
        {
            private readonly CancellationTokenSource _cancellation = new();
            private readonly object _progressLock = new();
            private long _downloadedSize;
            private string _outputPath;
            private DateTime? _startedAt;

            public DownloadEntry(DownloadRequest request)
            {
                Request = request;
                Id = Guid.NewGuid().ToString();
                AcceptedAt = DateTime.UtcNow;
                Status = DownloadItemStatus.Queued;
                TotalSize = request.Album.TotalDeclaredSize(request.Quality);
            }

            public string Id { get; }
            public DownloadRequest Request { get; }
            public DateTime AcceptedAt { get; }
            public long TotalSize { get; }
            public DownloadItemStatus Status { get; private set; }
            public Task Run { get; set; }

            public CancellationToken Token => _cancellation.Token;

            public bool IsTerminal => Status is DownloadItemStatus.Completed
                or DownloadItemStatus.Failed
                or DownloadItemStatus.Warning;

            public void Cancel()
            {
                try
                {
                    _cancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            public void MarkDownloading()
            {
                lock (_progressLock)
                {
                    Status = DownloadItemStatus.Downloading;
                    _startedAt = DateTime.UtcNow;
                }
            }

            public void MarkCompleted() => SetStatus(DownloadItemStatus.Completed);

            public void MarkFailed() => SetStatus(DownloadItemStatus.Failed);

            /// <summary>
            /// A cancelled download reports Warning: the upstream enum has no
            /// Cancelled member, and Failed would feed Lidarr's failed-download
            /// handling and could blocklist the release (ADR-0004).
            /// </summary>
            public void MarkCancelled() => SetStatus(DownloadItemStatus.Warning);

            private void SetStatus(DownloadItemStatus status)
            {
                lock (_progressLock)
                {
                    Status = status;
                }
            }

            public void AddDownloadedBytes(long bytes)
            {
                lock (_progressLock)
                {
                    _downloadedSize += bytes;
                }
            }

            public void SetOutputPath(string path)
            {
                lock (_progressLock)
                {
                    _outputPath ??= path;
                }
            }

            public DownloadSnapshot Snapshot()
            {
                lock (_progressLock)
                {
                    return new DownloadSnapshot(
                        Id,
                        Request.Title,
                        Status,
                        TotalSize,
                        _downloadedSize,
                        EstimateRemaining(),
                        _outputPath);
                }
            }

            private TimeSpan? EstimateRemaining()
            {
                if (Status != DownloadItemStatus.Downloading ||
                    _startedAt == null ||
                    TotalSize <= 0 ||
                    _downloadedSize <= 0)
                {
                    return null;
                }

                var fraction = Math.Min(1.0, _downloadedSize / (double)TotalSize);

                if (fraction <= 0)
                {
                    return null;
                }

                var elapsed = DateTime.UtcNow - _startedAt.Value;
                return TimeSpan.FromTicks((long)(elapsed.Ticks * (1 - fraction) / fraction));
            }

            public void Dispose() => _cancellation.Dispose();
        }
    }
}
