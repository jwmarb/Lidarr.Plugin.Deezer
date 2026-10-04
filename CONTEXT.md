# Lidarr.Plugin.Deezer

A Lidarr plugin that provides a Deezer indexer and download client, talking to
Deezer directly rather than through Deemix. Lidarr hosts it: Lidarr owns the DI
container, constructs the indexer and the download client, polls the download
client for queue state, and asks the indexer to turn search criteria into
releases.

## Language

### Credential and session

**ARL**:
Deezer's long-lived authentication cookie. A full account bearer credential: a
192-character hex string that grants whatever the account grants.
_Avoid_: token, API key, password.

**Session**:
An authenticated connection to Deezer established from one ARL, carrying the
entitlements that ARL actually proved.
_Avoid_: client, connection, login.

**Entitlement**:
A capability the authenticated account genuinely has, as reported by Deezer:
streaming, high quality, lossless. Distinct from whether a given album has bytes
available at that quality.
_Avoid_: permission, right, option, feature flag.

**Anonymous session**:
A session Deezer accepted without authenticating anyone, reported as `USER_ID=0`
with every entitlement false. The failure mode an expired ARL produces, and the
one the plugin historically could not detect.
_Avoid_: invalid session, guest, logged-out.

### Catalogue

**Album page**:
Deezer's untyped JSON description of one album, including its track list and the
per-quality file sizes.
_Avoid_: album JSON, album data, album response.

**Track page**:
Deezer's untyped JSON description of one track. Overlaps the album page but is
not identical: album title and version can differ between the two, and release
dates can disagree.
_Avoid_: song data, track JSON.

**Quality**:
One of the three forms Deezer serves a track in: MP3 128, MP3 320, FLAC. The
domain concept behind what the code variously encodes as a `Bitrate` enum, the
magic integers 1/3/9, codec-plus-container strings, and `FILESIZE_*` key names.
_Avoid_: bitrate, format, codec, container.

**Declared size**:
The byte count Deezer reports for a track at a given quality. Authoritative for
progress accounting and for verifying a finished download; not necessarily equal
to the number of bytes the CDN transfers.
_Avoid_: filesize, expected size.

### Search

**Search tier**:
One ordered attempt at answering a search. Lidarr tries the next tier only if the
current one returned nothing, so tier order decides recall.
_Avoid_: fallback, retry, pass, strategy.

**Release**:
One offer the indexer publishes to Lidarr: an album at a specific quality. One
album yields up to three releases, one per entitled quality.
_Avoid_: result, item, candidate.

### Downloading

**Operation**:
A unit of host-initiated work — a search or a download. The thing that owns an
immutable snapshot of everything it needs: settings, credential, quality,
cancellation lifetime.
_Avoid_: job, task, request, work item.

**Accept**:
The moment a download operation is admitted to the queue, when everything it will
need is captured. Nothing an operation needs may be read from shared mutable
state after this point.
_Avoid_: enqueue, submit, queue up.

**Acknowledgement**:
Lidarr telling the plugin it has finished importing a completed download, and the
record may be dropped. Arrives through the same host call as a cancellation and is
distinguished from it by whether the download had already reached a terminal
state.
_Avoid_: removal, cleanup, completion.

**Cancellation**:
The user asking for in-flight work to stop. Not a failure, and not an
acknowledgement.
_Avoid_: abort, removal.

**Terminal state**:
A state a download cannot leave: completed, failed, or cancelled. Every accepted
download must reach one.
_Avoid_: final status, end state.
