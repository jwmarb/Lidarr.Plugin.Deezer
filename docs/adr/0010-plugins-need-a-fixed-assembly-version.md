# Plugin assemblies must be built with a fixed AssemblyVersion

`src/Directory.Build.props` is inherited from Lidarr's own build and sets
`<AssemblyVersion>10.0.0.*</AssemblyVersion>` with `Deterministic=false`. The
wildcard is correct for building Lidarr, where every assembly is stamped and
shipped together. It is wrong for building a plugin.

Because the wildcard changes on every build, the `Lidarr.Core` and
`Lidarr.Common` assemblies compiled from the `ext/Lidarr` submodule get a
different version each time, and the plugin records a reference to whichever
build produced it. That reference can never be satisfied by the `Lidarr.Core`
inside a released container. Installing the plugin into a real Lidarr
3.1.2.4913 (`pr-plugins-e964d88`) failed with:

```
System.IO.FileNotFoundException: Could not load file or assembly
'Lidarr.Core, Version=10.0.0.29664, ...
```

Plugins are therefore built with an explicit fixed version:

```
dotnet build -c Release -p:AssemblyVersion=1.0.0 -p:FileVersion=1.0.0 -p:Deterministic=true
```

This produces a reference to `Lidarr.Core/1.0.0`, which is what the independently
built Tidal plugin in the same installation references, and what a released
container satisfies. Verified: the plugin then loads, registers its indexer and
download client, and performs a real search and download.

## Consequences

The failure this prevents is not contained to our own plugin. Lidarr enumerates
types across loaded plugin assemblies during registration, and the broken Deezer
assembly threw while its types were being enumerated. The already-working Tidal
plugin's indexer and download client both vanished from the running instance:

```
[Warn] IndexerRepository: Skipping provider of unknown type TidalIndexerSettings
[Warn] DownloadClientRepository: Skipping provider of unknown type TidalSettings
```

So a version-mismatched plugin de-registers other vendors' working plugins. The
build configuration is a correctness concern, not packaging hygiene, and the
release workflow must not fall back to the inherited wildcard.
