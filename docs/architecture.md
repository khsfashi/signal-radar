# Architecture

Signal Radar separates deterministic collection and ranking from optional generative analysis.

## Project boundaries

- `SignalRadar.Domain`: immutable article, assessment, feedback, and topic models.
- `SignalRadar.Application`: collection use cases, persistence contracts, ranked and saved read contracts, and provider-neutral Markdown export.
- `SignalRadar.Infrastructure`: PostgreSQL, HTTP, XML, JSON, source-state, ranking, feedback, and saved-list implementations.
- `SignalRadar.Bot`: Discord gateway, allow-list enforcement, application commands, article buttons, and ephemeral response rendering.
- `SignalRadar.Worker`: composition root and concurrent polling lifecycle.

Dependencies point inward:

```text
Domain <- Application <- Infrastructure
                     <- Bot
                     <- Worker
```

Domain and Application do not depend on Discord, PostgreSQL, HTTP libraries, or LLM SDKs.

## Ingestion flow

```text
Discord / RSS / Atom / GitHub Releases / Hacker News
    -> source-specific bounded collector
    -> normalized article candidate
    -> canonical URL normalization
    -> deterministic topic classification and scoring
    -> PostgreSQL article insert
```

Canonical URLs provide the universal duplicate guard. Sources with stable upstream identifiers also use `(source, external_id)` uniqueness. Source polling state, validators, retries, quarantine, and expiring leases are persisted separately from articles.

## Personalized read flow

```text
PostgreSQL articles + aggregate feedback
    -> ranked query or title/source search
    -> actor-specific hidden filter
    -> Discord ephemeral embeds
    -> feedback or saved-list button
```

Feedback changes effective ranking without overwriting the deterministic base score. Saved articles use a separate `(article_id, actor_id)` relation because an interest signal and a deliberate reading-list choice are different user actions.

## Saved export flow

```text
Authenticated Discord user
    -> actor-scoped saved query
    -> bounded set of up to 100 articles
    -> deterministic UTF-8 Markdown renderer
    -> ephemeral Discord attachment
```

The export contains source links, timestamps, topics, and current scores. It does not contain the Discord user ID and does not invoke an LLM. This keeps manual export available without an API key and allows later provider adapters to consume the same source material.

## Persistence and concurrency

- Embedded SQL migrations are ordered and checksum-verified.
- A transaction advisory lock is acquired before first-start migration DDL.
- Article, feedback, and saved-list identity is enforced by database constraints.
- Polling sources and Discord receipts use expiring tokenized leases.
- Due source claims use row locking with `FOR UPDATE SKIP LOCKED`.
- PostgreSQL integration tests are serialized because they intentionally share one test database and perform table resets.

## Performance and security

Performance rules include bounded collector concurrency, one pooled `NpgsqlDataSource`, one long-lived `HttpClient` per HTTP pipeline, bounded response buffering with `ArrayPool<byte>`, and maximum entry counts per source.

Security rules include Discord guild and channel allow lists, actor identity derived from authenticated interactions, no committed secrets or private source lists, prohibited XML DTDs, explicit redirect validation, private-network target rejection by default, and bounded request size, duration, redirect, retry, and export counts.

LLM output, when added later, remains downstream of deterministic source preservation and never becomes the source of truth.
