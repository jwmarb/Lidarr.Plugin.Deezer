# A cancelled download reports Warning

Lidarr's `DownloadItemStatus` has no `Cancelled` member: it offers `Queued`,
`Paused`, `Downloading`, `Completed`, `Failed`, `Warning`. A cancelled download
has to report one of them, and the choice is not obvious.

We report `Warning` and then stop listing the download. `Failed` is wrong because
it feeds Lidarr's failed-download handling, which can blocklist the release and
trigger an automatic search for a replacement — the user asked to stop, not to
find something else. Vanishing silently is worse for the user, who gets no
explanation for a row disappearing.

## Consequences

The module must drop a cancelled download from its own listing, because the host
will not call `RemoveItem` for a cancellation it did not initiate, and
`DownloadIsTrackable` keeps polling a `Warning` item indefinitely
(`DownloadMonitoringService.cs:136-153`). Cancellation is therefore the one
terminal state the plugin retires itself, rather than waiting for acknowledgement
as in ADR-0002.

Verified separately: `catch (TaskCanceledException)` does not catch a bare
`OperationCanceledException`, which is what `ThrowIfCancellationRequested()`
throws. Cancellation handling must catch the base type or a user cancel is logged
as an error and counted as a failed track.
