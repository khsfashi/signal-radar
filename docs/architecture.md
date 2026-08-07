# Architecture

Signal Radar separates deterministic collection and ranking from optional retrieval and generative analysis.

## Project boundaries

- `SignalRadar.Domain`: immutable article, assessment, feedback, and topic models.
- `SignalRadar.Application`: collection, ranking, saved-list, digest, status, content-reader, export, and provider-neutral summary contracts and use cases.
- `SignalRadar.Infrastructure`: PostgreSQL, bounded HTTP, XML, JSON, HTML parsing, robots policy, ranking stores, caches, status readers, delivery receipts, and provider adapters.
- `SignalRadar.Bot`: Discord Gateway, allow-list enforcement, commands, buttons, digest rendering, scheduled channel delivery, and ephemeral responses.
- `SignalRadar.Worker`: composition root, polling and digest lifecycles, provider configuration, startup validation, health-check mode, and redacted logging.

Dependencies point inward:

```text
Domain <- Application <- Infrastructure
                     <- Bot
                     <- Worker
```

Domain and Application do not depend on Discord, PostgreSQL, HTTP libraries, HTML parsers, or vendor SDKs.

## Ingestion flow

```text
Discord / RSS / Atom / GitHub Releases / Hacker News
    -> bounded source collector
    -> normalized candidate
    -> canonical URL normalization
    -> deterministic topic classification and scoring
    -> PostgreSQL article insert
```

Ingestion never downloads article bodies and never invokes an LLM. Canonical URLs provide the universal duplicate guard; stable sources also use `(source, external_id)`.

## Personalized read flow

```text
PostgreSQL articles + aggregate feedback
    -> ranked query or search
    -> actor-specific hidden filter
    -> Discord ephemeral result
    -> feedback or saved-list action
```

Feedback changes effective ranking without overwriting the base score. Saved articles use a separate `(article_id, actor_id)` relation.

## On-demand summary flow

```text
/summarize
    -> actor-scoped saved query, maximum 20
    -> bounded concurrent article reads
        -> public-target and redirect validation
        -> robots policy
        -> HTML/XHTML and byte limits
        -> non-executing AngleSharp parse
        -> boilerplate removal
        -> normalized text + SHA-256
        -> content cache
    -> deterministic prompt and summary hash
    -> summary cache
    -> OpenAI Responses or Gemini Generate Content adapter
    -> local structured-output validation
    -> ephemeral Discord embed
```

Expected retrieval failures fall back to article metadata. Article text is untrusted data, not an instruction channel.

## Digest flow

Manual digest:

```text
/digest
    -> actor-scoped ranked query
    -> previous 24 hours or seven days
    -> deterministic Discord embed
```

Scheduled digest:

```text
local timezone schedule
    -> derive UTC occurrence
    -> acquire (delivery_key, window_start) PostgreSQL lease
    -> actor-scoped ranked query
    -> allow-listed public Discord channel
    -> store delivered message ID and completion timestamp
```

A completed occurrence is never sent twice. An interrupted lease is reclaimable after expiry; a failed send records a bounded diagnostic and becomes retryable. Zero-article occurrences complete without posting.

## Operational status and deployment

`/status` uses one bounded aggregate PostgreSQL query plus process metadata to report uptime, article and save counts, enabled source counts, latest collection, cache counts, ranking profile, summary provider, and scheduler state.

The production container is built in two stages, runs as the .NET image's non-root application user, uses a read-only root filesystem under Compose, disables diagnostics, and exposes a Docker Health Check through Worker `--healthcheck` mode. This health check validates PostgreSQL connectivity without opening a public HTTP port.

Production startup rejects example secrets. Logs are timestamped, rotate through Docker settings, and redact configured connection strings and tokens.

## Persistence and concurrency

- SQL migrations are ordered, embedded, checksum-verified, and protected by a transaction advisory lock.
- Article, feedback, saved-list, content-cache, summary-cache, and digest-delivery identity is enforced by database constraints.
- Summary cache writes use immutable hash keys and `ON CONFLICT DO NOTHING`.
- Discord ingestion, polling sources, and scheduled delivery use expiring ownership tokens.
- Due polling source claims use `FOR UPDATE SKIP LOCKED`.
- Integration tests sharing one PostgreSQL database are serialized.

## Security boundaries

- Discord commands and buttons require configured Guild and Channel allow lists.
- Scheduled delivery channels must also be allow-listed.
- Actor identity is derived from authenticated Discord interactions or explicit local scheduler configuration.
- XML DTDs and external entities are prohibited.
- Article retrieval rejects non-public targets by default and revalidates redirects.
- Only HTML/XHTML is accepted for article extraction.
- JavaScript execution, login sessions, authentication cookies, and paywall bypass are unsupported.
- HTTP size, duration, redirect, retry, concurrency, extraction, export, summary, and digest counts are bounded.
- Provider endpoints require HTTPS except loopback tests.
- API keys, raw HTML, cookies, and authorization headers are not persisted.

Generative output remains downstream of preserved source links and deterministic source material and is never treated as the source of truth.
