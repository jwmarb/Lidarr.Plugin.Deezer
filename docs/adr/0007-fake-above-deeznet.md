# Fake the Deezer dependency above DeezNET, and pin DeezNET with live checks

The Deezer dependency is truly external, so the catalogue and session modules
take it as an injected port with two adapters: a production adapter owning
DeezNET, and a deterministic fake for tests. The open question was where the
fake sits.

A fake **below** DeezNET — reproducing Deezer's gateway JSON, cookies and
encrypted CDN responses — would exercise DeezNET's real decryption and file
writing, so a test could catch the trailing-padding defect. But DeezNET 1.2.1
does not accept an interchangeable transport, so this would require forking the
dependency before it produced any leverage.

We put the fake **above** DeezNET, and accept that module tests therefore do not
prove DeezNET's own behaviour. The production adapter gets a small number of
opt-in checks against a live credential to pin what DeezNET actually does.

## Consequences

Module tests prove our logic deterministically and with no network: anonymous
sessions cannot cross the interface, validation cannot mutate a live session,
entitlement and availability gating, session pinning under credential change,
cancellation reaching a terminal state, and error mapping.

They do **not** prove that DeezNET returns the fields the production adapter
expects, that `SetARL` still yields an anonymous session rather than throwing in
a future version, or that the downloader emits the byte stream we assume. Those
require adapter-level verification.

In particular the verified padding defect — `WriteRawTrackToFile` writing a whole
final 2048-byte cipher block past the declared size, so every FLAC fails
`flac -t` — must be pinned against the production adapter by a real download. A
passing scripted-adapter test that merely replays a byte count would be
self-fulfilling and would not detect the defect regressing or being fixed
upstream.

Live checks must be opt-in: they need a working ARL, they hit a third party that
throttles and bans, and they cannot run in CI by default.
