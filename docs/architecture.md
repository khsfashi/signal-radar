# Architecture

Signal Radar separates deterministic collection and ranking from optional retrieval and generative analysis.

## Project boundaries

- `SignalRadar.Domain`: immutable article, assessment, feedback, and topic models.
- `SignalRadar.Application`: collection use cases, persistence contracts, ranked and saved read contracts, provider-neutral export, content-reader contracts, deterministic prompt construction, and summary orchestration.
- `SignalRadar.Infrastructure`: PostgreSQL, bounded HTTP, XML, JSON, HTML parsing, robots policy, source state, ranking, feedback, saved lists, article content, summary caches, and provider adapters.
- `SignalRadar.Bot`: Discord gateway, allow-list enforcement, application commands, article buttons, deferred summary interactions, and ephemeral response rendering.
- `SignalRadar.Worker`: composition root, optional provider configuration, and concurrent polling lifecycle.

Dependencies point inward:

```text
Domain <- Application <- Infrastructure
                     <- Bot
                     <- Worker
```

Domain and Application do not depend on Discord, PostgreSQL, HTTP libraries, HTML parsers, or vendor LLM SDKs.

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

Ingestion never downloads article bodies and never invokes an LLM.

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

The export contains source links, timestamps, topics, and current scores. It does not contain the Discord user ID and does not invoke an LLM.

## On-demand summary flow

```text
Authenticated Discord user invokes /summarize
    -> actor-scoped saved query, up to 20 articles
    -> bounded article-content reads with concurrency limit
        -> public-target validation
        -> RFC 9309 robots policy
        -> redirect revalidation
        -> HTML/XHTML content-type and byte limit
        -> AngleSharp parse without script execution
        -> boilerplate removal and article-like block selection
        -> normalized text + SHA-256 content hash
        -> PostgreSQL article-content cache
    -> deterministic prompt construction and SHA-256 summary key
    -> PostgreSQL summary cache lookup
    -> optional external summary provider
    -> local structured-output validation
    -> ephemeral Discord result
```

Expected article retrieval failures do not fail the whole summary. Each unavailable, robots-disallowed, non-HTML, too-large, or too-short article contributes its metadata and extraction status while successfully extracted articles contribute bounded text excerpts.

Extracted text is treated as untrusted data. Prompt instructions tell the provider to ignore embedded commands, avoid claiming complete article coverage, distinguish source claims from confirmed facts, and report uncertainty.

Summary cache identity excludes the Discord actor because actor identity is neither sent to the provider nor used in the prompt. Two users with an identical ordered input, provider, model, prompt version, language, extraction statuses, and excerpts can share the same cached result.

## Persistence and concurrency

- Embedded SQL migrations are ordered and checksum-verified.
- A transaction advisory lock is acquired before first-start migration DDL.
- Article, feedback, and saved-list identity is enforced by database constraints.
- `article_content_cache` stores one current bounded extraction result per article.
- `article_summary_cache` keeps one immutable result per deterministic input hash.
- Summary first writers use `ON CONFLICT DO NOTHING`.
- Polling sources and Discord receipts use expiring tokenized leases.
- Due source claims use row locking with `FOR UPDATE SKIP LOCKED`.
- PostgreSQL integration tests are serialized because they intentionally share one test database and perform table resets.

## Performance and security

Performance rules include bounded collector concurrency, one pooled `NpgsqlDataSource`, long-lived `HttpClient` instances per HTTP pipeline, bounded response buffering with `ArrayPool<byte>`, maximum entries per source, article retrieval concurrency limits, bounded HTML parsing, and global plus per-article prompt budgets.

Security rules include Discord guild and channel allow lists, actor identity derived from authenticated interactions, no committed secrets or private source lists, prohibited XML DTDs, explicit redirect validation, private-network target rejection by default, robots policy enforcement, accepted content-type lists, HTTPS summary-provider endpoints except loopback tests, and bounded request size, duration, redirect, retry, extraction, export, and summary counts.

Article target validation resolves and checks every returned address before each request. Deployments facing hostile DNS should also apply network-level egress restrictions because validation and socket connection establishment are separate operations.

Generative output remains downstream of preserved source links and deterministic source material. It is never treated as the source of truth.
