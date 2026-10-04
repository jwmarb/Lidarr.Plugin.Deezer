# Local builds must pin the submodule's AssemblyVersion, as CI already does

`ext/Lidarr/src/Directory.Build.props:77` sets
`<AssemblyVersion>10.0.0.*</AssemblyVersion>` with `Deterministic=false` (`:85`).
The wildcard is correct for building Lidarr itself, where every assembly is
stamped and shipped together. It is wrong for building a plugin against Lidarr,
because the plugin records a reference to whichever randomly-stamped
`Lidarr.Core` produced it, and no released container can satisfy that reference.

The release workflow already handles this. `.github/workflows/build.yml:52`
rewrites the submodule's version to `MINIMUM_LIDARR_VERSION` (currently
`3.0.0.4855`, `:21`) before building, so CI artifacts reference a version real
containers provide. A plain local `dotnet build` skips that step and produces an
assembly that cannot load anywhere.

Local builds must therefore pin the version too:

```sh
dotnet build src/*.sln -c Release \
  -p:AssemblyVersion=1.0.0 -p:FileVersion=1.0.0 -p:Deterministic=true
```

Verified: an unpinned local build installed into Lidarr 3.1.2.4913
(`pr-plugins-e964d88`) failed with
`FileNotFoundException: Could not load file or assembly 'Lidarr.Core, Version=10.0.0.29664'`.
Rebuilt with the pin, the same plugin loaded, registered its indexer and download
client, and completed a real search and download.

## Consequences

The failure is not contained to our own plugin. Lidarr enumerates types across
loaded plugin assemblies during registration, and the broken assembly threw while
its types were being enumerated. The unrelated, already-working Tidal plugin's
indexer and download client both vanished from the running instance:

```
[Warn] IndexerRepository: Skipping provider of unknown type TidalIndexerSettings
[Warn] DownloadClientRepository: Skipping provider of unknown type TidalSettings
```

So a version-mismatched plugin de-registers other vendors' working plugins. That
makes this a correctness concern rather than packaging hygiene, and it is why the
pin belongs in the documented build command and not only in CI: anyone testing a
local build against a real Lidarr hits it, and the symptom points at the wrong
plugin.

## Considered options

Changing `ext/Lidarr/src/Directory.Build.props` directly. Rejected: it is a git
submodule tracking upstream Lidarr, so the edit would either be lost on update or
have to be carried as a local modification to someone else's repository.
