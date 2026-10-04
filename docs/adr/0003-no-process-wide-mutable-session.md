# No process-wide mutable Deezer session

`DeezerAPI.Instance` is a mutable static reached into at 19 live call sites
across 5 files, and it is the only reason the indexer and the download client
currently share one authenticated session. It looked load-bearing. It is not.

Lidarr registers every plugin-defined **interface** as `Reuse.Singleton` in the
single application container (`Composition/Extensions.cs:28-31`), and plugin
assemblies are appended to the same assembly list that registration consumes
(`Bootstrap.cs:225-235`). The indexer and the download client therefore already
receive the same instance of any plugin interface they both depend on. The
sharing the static exists to provide is a guarantee the host already makes.

We decided to delete the static and inject the session as a plugin-defined
interface instead.

## Consequences

Sharing works only when injecting by **interface**: concrete types register as
`Reuse.Transient` (`Extensions.cs:33-35`), so resolving a concrete class yields a
fresh instance per resolution.

This also removes a plausible cause of plugin-reload failure. `PluginLoadContext`
is a collectible `AssemblyLoadContext` (`PluginLoadContext.cs:12-16`) and
`PluginLoader.UnloadPlugins` gives up after ten GC attempts
(`PluginLoader.cs:50-78`). A static field rooting a live `DeezerClient` and its
open `HttpClient` inside a collectible context is exactly the shape that keeps
the context alive.

## Considered options

A static registry of immutable session generations, keyed by credential and never
exposing a mutable current client, was a reasonable fallback if the host could not
guarantee a shared lifetime. The DI finding makes it unnecessary.
