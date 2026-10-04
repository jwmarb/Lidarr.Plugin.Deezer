# Release Guid format is frozen for blocklist compatibility

The indexer publishes `ReleaseInfo.Guid` as `Deezer-{AlbumId}-{qualityCode}`
where the quality code is the magic integer 1, 3 or 9. Replacing those codes with
the `Quality` enum's names would be the obvious cleanup while building a module
that owns quality encoding. We are not doing it.

The Guid is a persisted identity that the blocklist matches on.
`DeezerBlocklist.IsBlocklisted` looks up by `release.Guid`
(`Blocklisting/DeezerBlocklist.cs:24`) and `SameRelease` compares it to the
stored `TorrentInfoHash` (`:46-51`). Changing the format orphans every existing
blocklist row for current users: releases they previously blocklisted silently
become grabbable again, and the plugin cannot migrate rows it does not own.

The codes stay 1, 3 and 9, as compatibility constants inside the quality module
rather than magic numbers at call sites.

## Consequences

The lookup is a **substring** match, not equality —
`Query(e => e.TorrentInfoHash.Contains(torrentInfoHash))`
(`Blocklisting/BlocklistRepository.cs:28-31`) — and
`IndexerBase.CleanupReleases` prefixes the stored value with the definition id
(`IndexerBase.cs:81`). So the code is not safely extensible by appending digits:
a new multi-character code could collide with existing rows under `Contains`.
A fourth quality, if Deezer ever offers one, needs a deliberate decision about
its code rather than the next free integer.

## Considered options

Using the enum name in the Guid, and accepting a one-time blocklist reset.
Rejected: the regression is silent, user-visible, and unrecoverable for anyone
who has built up a blocklist.
