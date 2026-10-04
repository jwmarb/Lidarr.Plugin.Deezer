# The queue retains terminal downloads until Lidarr acknowledges them

Three independent interface designs for the download queue all proposed evicting
completed downloads on a count or age policy (500 items, 24 hours, 7 days), and
two of them named "no acknowledgement entry point" as a cost they were forced to
accept. That cost does not exist. Lidarr already has the protocol:
`UpdateTrackable` marks any tracked download absent from `GetItems()` as
untrackable (`TrackedDownloadService.cs:238-246`), import only fires for items
still visible as `Completed` (`CompletedDownloadService.cs:57-70`), and after a
successful import the host calls the plugin's own `RemoveItem`
(`DownloadEventHub.cs:71-95`), gated on `RemoveCompletedDownloads`, which
defaults to `true` (`DownloadClientDefinition.cs:9`).

We therefore retain a terminal download until Lidarr removes it. `RemoveItem` is
the acknowledgement. Evicting earlier would make a completed download untrackable
before import could run, silently losing the user's download.

## Consequences

`RemoveItem` arrives for two different reasons and we distinguish them by the
item's state: a non-terminal item means the user cancelled and in-flight work
must stop; a terminal item means the host finished importing and the record may
be dropped. The current code treats both as a cancellation.

A count cap, if kept at all, is a backstop against unbounded growth and may only
evict records the host has already acknowledged.
