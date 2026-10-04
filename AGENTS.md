# Lidarr.Plugin.Deezer — Project Knowledge Base

## OVERVIEW
A plugin for Lidarr's `plugins` branch that is both a Deezer indexer and a Deezer download client in one assembly. It talks to Deezer's `gw-light.php` gateway directly (no Deemix middleman), publishes each album as up to three releases (MP3 128, MP3 320, FLAC — gated by the account's entitlements), and writes tagged audio with cover art and optional `.lrc` lyrics into Lidarr's download folder. net8.0; dependencies are merged into a single shipped DLL by ILRepack.

## STRUCTURE
- `src/` — the plugin: solution, MSBuild props/targets, `global.json`, `NuGet.config`, `stylecop.json`, and the `src/Lidarr.Plugin.Deezer/` project (all C# code lives under it)
- `ext/Lidarr/` — git submodule pinned to Lidarr's `plugins` branch; referenced only for `Lidarr.Common` + `Lidarr.Core` (csproj `ProjectReference`). Not this repo's code — do not edit
- `_plugins/` — build output (gitignored); CI uploads from `_plugins/net8.0/Lidarr.Plugin.Deezer/`
- `_temp/` — intermediate obj/bin output (gitignored)
- `docs/adr/` — 11 ADRs; the design authority for the queue, credential handling, and release identity
- `CONTEXT.md` — domain glossary (ARL, Session, Entitlement, Operation, Accept, Acknowledgement, Cancellation, Release, Search tier, ...)
- `.github/workflows/build.yml` — the only CI; doubles as the release pipeline (main branch → zip + GitHub release)
- `.env` — local Deezer ARL cookie (gitignored; a full account bearer credential — never commit)

## WHERE TO LOOK
| Task | Path |
| --- | --- |
| Plugin entry / name / owner | `src/Lidarr.Plugin.Deezer/Plugin.cs` (Name/Owner/GithubUrl must match the GitHub repo) |
| Search / indexer | `src/Lidarr.Plugin.Deezer/Indexers/` (`Deezer.cs`, `DeezerParser.cs`, `DeezerRequestGenerator.cs`) |
| Download client | `src/Lidarr.Plugin.Deezer/Download/Clients/Deezer/` |
| Queue + immutable download snapshots | `src/Lidarr.Plugin.Deezer/Queue/DownloadQueue.cs` |
| ARL / sessions / gateway URL building | `src/Lidarr.Plugin.Deezer/Session/` (`DeezerGateway.cs`, `DeezerSessions.cs`, `DeezNetTransport.cs`) |
| Quality decoding (Bitrate enum ↔ 1/3/9 ↔ `FILESIZE_*`) | `src/Lidarr.Plugin.Deezer/Quality/DeezerQuality.cs` |
| Album/track page parsing | `src/Lidarr.Plugin.Deezer/Catalogue/DeezerCatalogue.cs` |
| File naming | `src/Lidarr.Plugin.Deezer/Naming/TrackNaming.cs` |
| Blocklisting | `src/Lidarr.Plugin.Deezer/Blocklisting/DeezerBlocklist.cs` |
| Version pinning / output paths | `src/Directory.Build.props` |
| Single-assembly DLL merge | `src/Lidarr.Plugin.Deezer/ILRepack.targets` |
| Design decisions | `docs/adr/` |

## CONVENTIONS
- Plugin class lives in `NzbDrone.Core.Plugins`; plugin code lives in `NzbDrone.Plugin.Deezer` (the NzbDrone namespace is kept deliberately).
- Operations own immutable snapshots: everything a search or download needs is captured at accept time; nothing is read from shared mutable state afterwards (ADR-0001/0003). The session module authenticates only (ADR-0005).
- Terminal downloads (completed/failed/cancelled) are retained until Lidarr acknowledges the import (ADR-0002); cancellation is reported as a warning, not an error (ADR-0004).
- Release GUID format is frozen (ADR-0008) — do not change release identity encoding.
- Local builds must pin assembly versions (see COMMANDS); `ext/Lidarr` stamps its assemblies `10.0.0.*` (ADR-0010).
- `TreatWarningsAsErrors` is `true` in `src/Directory.Build.props` but overridden to `False` in the csproj for both configs; CI also passes `-p:TreatWarningsAsErrors=false`.
- Single-assembly shipping: ILRepack merges DeezNET, TagLibSharp, AngleSharp(+XPath), SkiaSharp, BouncyCastle with `Internalize=true`. Newtonsoft.Json is deliberately NOT merged — it resolves against the copy Lidarr ships.
- `src/stylecop.json` is inert: the csproj removes it from `AdditionalFiles`.

## COMMANDS
Run from the repo root. There is no test suite in this repo — a clean build is the local verification.

```sh
# Local build (the version pin is NOT optional — see ADR-0010)
dotnet build src/*.sln -c Release \
  -p:AssemblyVersion=1.0.0 -p:FileVersion=1.0.0 -p:Deterministic=true
```

Output lands in `_plugins/net8.0/Lidarr.Plugin.Deezer/` (`dll`, `pdb`, `deps.json`); copy those into `<lidarr-config>/plugins/jwmarb/Lidarr.Plugin.Deezer/` and restart Lidarr.

CI (`.github/workflows/build.yml`, on `main` push / PR / dispatch): SDK 8.0.405; `dotnet restore` + `dotnet build src/*.sln -c Release -f net8.0` with `-p:TreatWarningsAsErrors=false`, after `sed`-pinning `AssemblyVersion` in `src/Directory.Build.props` (plugin `10.1.0.<run>`) and in `ext/Lidarr/src/Directory.Build.props` (minimum Lidarr `3.0.0.4855`). On `main` it zips the artifact and publishes a GitHub release.

## NOTES
- Clone with `git clone --recurse-submodules` — the build `ProjectReference`s `ext/Lidarr/src/NzbDrone.Common` and `NzbDrone.Core`, so a bare clone will not build.
- The plugin only loads on Lidarr's `plugins` branch, not `master`.
- The csproj `PostBuild` target copies the DLL to `C:\ProgramData\Lidarr\plugins\TrevTV/Lidarr.Plugin.Deezer` — a stale Windows path (owner is `jwmarb`, not TrevTV). `ContinueOnError="true"` makes it a silent no-op elsewhere; it is harmless, just do not trust it.
- `.env` holds a real ARL — treat it as a live credential. It is gitignored (`.env`, `.env.*`).
- Known limitations (README): `flac -t` fails on downloads (trailing bytes past the stream end, upstream in DeezNET — ADR-0011); the indexer's Test button passes even with an invalid ARL; the ARL is stored in clear text in Lidarr's config.
- Local `src/global.json` pins SDK 6.0.0 with `rollForward: latestMinor`; CI overwrites `global.json` with 8.0.405 at build time.
- Deezer actively limits/bans accounts used by downloading tools — the README's warning is a real user-facing risk, not boilerplate.
