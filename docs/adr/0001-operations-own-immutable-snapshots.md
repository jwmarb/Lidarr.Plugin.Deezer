# An operation owns an immutable snapshot of what it needs

Every defect we reproduced in the download path traced to one cause: a download
read what it needed — settings, credential, cancellation — from shared mutable
state at the moment of use rather than capturing it when accepted. Verified
consequences: a download queued under `/music/A` wrote to `/music/B` after a
routine queue poll rewrote the queue's single settings field, and 29% of
downloads (1158/4000 trials) began holding `CancellationToken.None` because the
item was published to the channel before its cancellation source was registered.

We decided that accepting a download captures everything it will ever need, as
values, in one atomic transition. Nothing an operation needs may be read from
shared mutable state after acceptance. Changing a setting or the ARL affects
operations accepted afterwards; it never retargets work already in flight.

## Consequences

Two credentials may be live at once during an ARL change, because in-flight
downloads keep the session they were accepted with. This is deliberate: the
alternative is the current behaviour, where changing the ARL mutates the session
underneath a running download.
