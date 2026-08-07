# Architecture

Signal Radar separates deterministic collection and ranking from optional retrieval and generative analysis. PostgreSQL is the durable source of truth; Discord is an interaction and delivery surface rather than the canonical database.

The central design rule is simple:

> The news radar must remain useful when every LLM provider is unavailable.

## Project boundaries

- `SignalRadar.Domain`: immutable article, assessment, feedback, topic, and core value models.
- `SignalRadar.Application`: collection, ranking, saved-list, digest, status, content-reader, export, feed/route administration, source preferences, reclassification, translation, and provider-neutral summary contracts/use cases.
- `SignalRadar.Infrastructure`: PostgreSQL stores, migrations, bounded HTTP, XML/JSON/HTML parsing, robots policy, caches, source collectors, title translation, ranking implementations, delivery receipts, and summary-provider adapters.
- `SignalRadar.Bot`: Discord Gateway, allow-list enforcement, private reading commands, article buttons, manager commands, digest rendering, scheduled/public delivery, and ephemeral responses.
- `SignalRadar.Worker`: composition root, runtime configuration, polling/publishing/digest lifecycles, provider wiring, startup validation, health-check mode, cancellation, and redacted logging.

Dependencies point inward:

```text
Domain <- Application <- Infrastructure
                     <- Bot
                     <- Worker
```

Domain and Application do not depend on Discord, PostgreSQL, HTTP libraries, HTML parsers, LibreTranslate, or LLM vendor SDKs.

## End-to-end flow

```text
Discord / RSS / Atom / GitHub Releases / Hacker News
    -> bounded collection
    -> normalized candidate
    -> canonical URL + external identity dedupe
    -> deterministic topic classification and scoring
    -> PostgreSQL
         |-> actor-aware private reads / feedback / saved state
         |-> batched public topic publishing
         |-> scheduled deterministic digests
         |-> provider-neutral Markdown export
         `-> explicit optional article retrieval + LLM summary
```

No article body download or LLM call is required for ingestion.

## Ingestion flow

```text
source scheduler / Discord gateway
    -> source-specific parser
    -> normalized candidate
    -> canonical URL normalization
    -> stable source identity validation
    -> deterministic assessment
    -> PostgreSQL article insert
```

### Duplicate identity

Two independent guards are used where available:

- canonical article URL;
- stable `(source, external_id)` identity.

Canonical URLs provide the universal duplicate guard while stable source identifiers prevent duplicate processing even when upstream link formatting changes.

### Source coordination

Polling collectors persist source state and use expiring ownership leases. Due PostgreSQL polling claims use `FOR UPDATE SKIP LOCKED` so multiple workers can claim independent work without serializing the entire source set.

Failures are bounded, persisted, and can lead to source quarantine without stopping unrelated collectors.

## Deterministic assessment

Classification and base scoring occur before article persistence and do not call an LLM.

The current assessment retains:

- multi-label topic mask;
- primary topic;
- source-trust score;
- topic-interest score;
- practical-impact score;
- freshness score;
- final base score;
- ranking-profile version.

Hostname text remains available to source-trust logic but is excluded from topic/practical-impact keyword matching so domain fragments such as `.net` do not accidentally become classification signals.

Economy and Markets are explicit topics alongside AI, game-industry, game-development, developer-tools, research, business, security, and other.

See [Ranking and feedback](ranking.md).

## Personalized read flow

```text
PostgreSQL articles + aggregate feedback
    -> ranked query/search
    -> actor-specific hidden filter
    -> actor-specific muted-source filter
    -> Discord ephemeral result
    -> feedback / save / un-save action
```

Feedback changes effective ranking without overwriting the deterministic base score. Saved articles use an independent `(article_id, actor_id)` relation.

Personal source mutes affect actor-aware private reads only. They do not mutate the article, disable collection, or retroactively hide a public Discord message for other users.

## Runtime source and route administration

Managers with Discord `Administrator` or `Manage Guild` permission can change selected operational state without editing `.env` or redeploying the Worker.

### Feed administration

```text
/feed-add
/feed-list
/feed-enable
/feed-disable
    -> PostgreSQL managed feed definitions
    -> polling scheduler
```

Checked-in feed configuration remains useful as bootstrap/code-managed configuration while runtime feed state is persisted separately.

### Topic-route administration

```text
/route-set
/route-list
/route-remove
    -> PostgreSQL runtime route
    -> topic destination + batch window + minimum score
```

Runtime routes are the normal operational path and take precedence over legacy/bootstrap environment routes for the same topic.

## Automatic public topic publishing

```text
PostgreSQL eligible articles
    -> route resolution
    -> minimum-score filter
    -> batch-window eligibility
    -> acquire per-article/channel publication leases
    -> optional cached title translation
    -> one Discord message or Forum post for the batch
    -> complete every article receipt with delivered resource ID
```

### Batching

Articles are no longer necessarily posted one at a time when they arrive. Each route can wait for a configured batching window and publish a compact group up to the configured batch size.

Each article still has its own durable publication receipt even when several articles share one Discord message/thread.

### Translation

Display-title translation is downstream of ingestion and ranking.

```text
original title
    -> Korean-text bypass?
    -> SHA-256 translation-cache lookup
    -> LibreTranslate on cache miss
    -> cache success
    -> translated title OR original-title fallback
```

The default Docker Compose stack includes self-hosted LibreTranslate. Translation failure never blocks article collection or the publication batch; the original title is used instead.

### Activation and delivery semantics

The first automatic-publishing activation timestamp is persisted so an existing archive is not dumped into Discord when the feature is first enabled.

Discord delivery and PostgreSQL receipt completion are separate network operations. A process crash after Discord accepts a message but before receipt completion can produce a rare duplicate after lease expiry. The system therefore implements durable at-least-once processing with completed-delivery deduplication rather than claiming exactly-once delivery across independent systems.

See [Automatic topic publishing](automatic-topic-publishing.md).

## Historical reclassification

Managers can re-evaluate a bounded recent window using the currently loaded topic/ranking policy:

```text
/reclassify days:<1..3650>
    -> bounded keyset/page scan
    -> deterministic reassessment using original collection timestamp
    -> collect changed assessments
    -> batched PostgreSQL UPDATE
```

Reclassification changes assessment fields only. It preserves article identity, actor feedback, saves, source preferences, and completed publication receipts.

This allows ranking-policy corrections to be applied to historical articles without intentionally replaying publication.

## On-demand summary flow

```text
/summarize
    -> actor-scoped saved query, bounded count
    -> bounded concurrent article reads
        -> public-target validation
        -> redirect revalidation
        -> robots policy
        -> HTML/XHTML + byte limits
        -> non-executing HTML parse
        -> boilerplate reduction
        -> normalized text + SHA-256
        -> content cache
    -> deterministic prompt/summary identity
    -> summary-cache lookup
    -> OpenAI Responses or Gemini Generate Content adapter
    -> local structured-output validation
    -> ephemeral Discord response
```

Expected retrieval failures fall back to article metadata. JavaScript is not executed, login cookies are not used, and paywalls are not bypassed.

Article text is untrusted source material and is not treated as an instruction channel.

## Digest flow

### Manual digest

```text
/digest
    -> actor-aware ranked query
    -> previous 24 hours or seven days
    -> deterministic Discord embed
```

No LLM is required.

### Scheduled digest

```text
local timezone schedule
    -> derive UTC occurrence
    -> acquire PostgreSQL delivery lease
    -> actor-aware ranked query
    -> allow-listed public Discord channel
    -> store delivered message ID and completion timestamp
```

A completed occurrence is not normally sent twice after restart. Interrupted or failed leases can become retryable. Zero-article occurrences complete without posting an empty message.

## Operational status and deployment

`/status` reads bounded PostgreSQL/process state to report operational information such as uptime, article/save counts, source counts, latest collection, caches, ranking profile, summary provider, and scheduler state.

The production image uses a multi-stage .NET build and runs as the .NET image's non-root application user. The default Compose deployment additionally uses:

- read-only Worker root filesystem;
- writable bounded `/tmp` via `tmpfs`;
- `no-new-privileges`;
- PostgreSQL isolated inside the Compose network by default;
- Docker health checking through Worker `--healthcheck` mode;
- bounded Docker log rotation;
- graceful process cancellation.

Production startup rejects known placeholder secrets. Configured connection strings and tokens are redacted from application logging.

The default stack contains:

```text
PostgreSQL 17
LibreTranslate
Signal Radar Worker
```

A separate development override can expose PostgreSQL to loopback when host access is intentionally required.

## Persistence and concurrency guarantees

- SQL migrations are ordered, embedded, checksum-verified, and protected by a transaction advisory lock.
- Article, feedback, saved-list, source-preference, content-cache, summary-cache, translation-cache, route, and delivery identities are enforced by PostgreSQL constraints where appropriate.
- Immutable hash-keyed cache writes tolerate races with conflict-safe inserts.
- Discord ingestion, source polling, scheduled digest delivery, and automatic publication use expiring ownership tokens/leases.
- Integration tests sharing one PostgreSQL database are serialized where required.
- Historical reclassification batches database writes to avoid per-row network round trips.

## Security boundaries

- Discord article interactions require configured guild/channel allow lists.
- Manager commands additionally require Discord `Administrator` or `Manage Guild` permission.
- Actor identity is derived from authenticated Discord interactions or explicit scheduler configuration.
- XML DTDs and external entities are prohibited.
- Article retrieval rejects non-public targets by default and revalidates redirects.
- Only supported bounded content types are accepted for article extraction.
- JavaScript execution, login sessions, authentication cookies, and paywall bypass are unsupported.
- HTTP size, duration, redirect, retry, concurrency, extraction, export, summary, and digest counts are bounded.
- Provider endpoints require HTTPS except explicit loopback/test cases.
- API keys, raw HTML, cookies, and authorization headers are not persisted.
- The repository runs full-history Gitleaks scanning in GitHub Actions and keeps local secret/config files out of the Docker build context.

Generative output always remains downstream of preserved source links and deterministic source material and is never treated as the source of truth.
