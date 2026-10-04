# Stay on HttpIndexerBase despite its synchronous parser

`IndexerBase<TSettings>` declares `Fetch` as `abstract Task<IList<ReleaseInfo>>`
(`IndexerBase.cs:70-73`). The request-generator/parser split and the synchronous
`IParseIndexerResponse.ParseResponse` are introduced entirely by
`HttpIndexerBase`, which is the only type in Lidarr deriving from `IndexerBase`.
So deriving from `IndexerBase` instead would make the search path genuinely
async, deleting every sync-over-async block in the parser, and would let the
plugin own its paging, its enrichment concurrency budget, and its search tier
strategy.

We stay on `HttpIndexerBase` anyway.

`HttpIndexerBase` provides, for free, integration we would otherwise have to
re-implement: `IIndexerStatusService` success/failure recording across roughly
twelve exception paths (`HttpIndexerBase.cs:171-250`), which drives indexer
health and automatic backoff in the Lidarr UI; per-indexer rate limiting plumbed
onto each request (`:302-307`); and retry-time parsing for 429 and 503
(`:197-216`). Losing user-visible indexer health and rate limiting to fix an
internal shape is the wrong trade, especially for an indexer whose upstream is
known to throttle and ban.

## Consequences

Sync-over-async remains, and must be confined to as thin a shell as possible
around an otherwise async search module.

The search-tier recall defect is fixed by changing tier *order* rather than by
changing base class. Lidarr advances to the next tier only when the current one
returns nothing (`HttpIndexerBase.cs:150-153`), so a narrow tier placed first
caps recall permanently: measured live, `artist:"Nirvana" album:"Nevermind"`
returns 4 albums where the plain query returns 14, and the structured tier for
Daft Punk / Discovery returns 0 where plain returns 2. Putting the plain query
first, or emitting one tier whose generator runs both strategies and
concatenates, restores recall without touching the host integration.

## Considered options

Deriving from `IndexerBase` and re-implementing status recording and rate
limiting. Rejected now, but worth revisiting if the N+1 album enrichment or the
tier strategy later becomes the binding constraint — the enrichment is one album
page read per search hit with no concurrency budget, and that is the real request
cost, not pagination.
