# Architecture

```text
Discord / RSS / Atom / future APIs
  -> boundary adapters
  -> deterministic normalization
  -> canonical URL deduplication
  -> PostgreSQL persistence
  -> future ranking
  -> Discord delivery / Markdown export / optional LLM summaries
```

Dependencies point inward:

```text
Domain <- Application <- Infrastructure
                     <- Bot
                     <- Worker
```

Domain and Application do not depend on Discord, PostgreSQL, HTTP libraries, or LLM SDKs. PostgreSQL unique constraints are the final article duplicate guard.

Discord messages use expiring receipt leases. Feed sources are claimed using `FOR UPDATE SKIP LOCKED` and lease tokens, allowing multiple worker instances without collecting the same source concurrently. Conditional HTTP validators reduce transfer and parsing work, while persisted failure state controls retry and quarantine.

Performance rules include bounded collector concurrency, one pooled `NpgsqlDataSource`, one long-lived `HttpClient`, bounded response buffering with `ArrayPool<byte>`, and a maximum number of entries processed per feed.

Security rules include Discord allow lists, no committed secrets or private source lists, prohibited XML DTDs, explicit redirect validation, private-network target rejection by default, and request size, duration, redirect, and retry limits.
