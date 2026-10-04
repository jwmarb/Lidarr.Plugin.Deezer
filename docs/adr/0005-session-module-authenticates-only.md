# The session module authenticates and nothing else

Three independent interface designs for the authenticated session converged on a
single entry point — authenticate an ARL, receive an opaque session that cannot
be retargeted — and all three made an anonymous session unrepresentable rather
than merely checked. They disagreed only on scope: one folded search and
download acceptance into the session module, making the authenticated capability
the organising principle for everything Deezer.

We keep the session module to authentication alone. Album page and track page
reads, and track fetching, belong to a sibling catalogue module that receives an
opaque session capability and never holds a `DeezerClient`.

The deciding argument is the deletion test applied to the fused alternative:
deleting a combined module would expose several unrelated clusters, and its
interface would grow once per Deezer operation — the large-shallow-interface
failure. Keeping the clusters separate means deleting either one concentrates
real complexity.

## Consequences

The session module's interface is one method, so its depth comes entirely from
what acquisition proves: non-anonymous account, streaming entitlement, frozen
entitlement snapshot, generation isolation under credential change, token
refresh, and secret redaction.

Callers must pass a session to the catalogue module rather than reaching for
ambient state. That is more explicit than the static it replaces and needs
lifetime tests, particularly that the queue holds a session from accept through
terminal state.

Entitlement and availability stay distinct (see CONTEXT.md): the session proves
what the account may fetch, the catalogue proves what an album actually has. A
release may only be published from a value the catalogue mints after intersecting
both, so an entitled-but-unavailable quality cannot be offered.
