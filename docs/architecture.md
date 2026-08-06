# Architecture

Signal Radar separates deterministic collection and ranking from optional generative analysis.

## Project boundaries

- `SignalRadar.Domain`: immutable article, assessment, feedback, and topic models.
- `SignalRadar.Application`: collection use cases, persistence contracts, ranked and saved read contracts, Markdown export, provider-neutral summary contracts, deterministic prompt construction, and input hashing.
- `SignalRadar.Infrastructure`: PostgreSQL, HTTP, XML, JSON, source-state, ranking, feedback, saved-list, summary-cache, and provider implementations.
- `SignalRadar.Bot`: Discord gateway, allow-list enforcement, application commands, article buttons, deferred summary interactions, and ephemeral response rendering.
- `SignalRadar.Worker`: composition root, optional provider configuration, and concurrent polling lifecycle.

Dependencies point inward:

```text
Domain <- Application <- Infrastructure
                     <- Bot
                     <- Worker
```

Domain and Application do not depend on Discord, PostgreSQL, HTTP libraries, or vendor LLM SDKs.

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

The export contains source links, timestamps, topics, and current scores. It does not contain the Discord user ID and does not invoke an LLM. This keeps manual export available without an API key.

## Optional summary flow

```text
Authenticated Discord user
    -> actor-scoped saved query, maximum 20 articles
    -> deterministic prompt and canonical JSON input
    -> SHA-256(provider + model + prompt version + language + ordered input)
    -> PostgreSQL cache lookup
       -> cache hit: validated structured result
       -> cache miss: IArticleSummaryProvider
            -> strict structured response
            -> local validation
            -> immutable cache insert
    -> ephemeral Discord summary embed
```

The current provider adapter uses the OpenAI Responses HTTP API, while Application depends only on `IArticleSummaryProvider`. API keys remain in process environment variables and are not written to PostgreSQL. The current prompt receives saved article metadata, not downloaded article bodies, and explicitly prohibits claiming otherwise.

Summary cache identity excludes the Discord actor because actor identity is neither sent to the provider nor used in the prompt. Two users with an identical ordered input, provider, model, prompt version, and language can share the same cached result.

## Persistence and concurrency

- Embedded SQL migrations are ordered and checksum-verified.
- A transaction advisory lock is acquired before first-start migration DDL.
- Article, feedback, saved-list, and summary-cache identity is enforced by database constraints.
- Summary first writers use an immutable hash key and `ON CONFLICT DO NOTHING`.
- Polling sources and Discord receipts use expiring tokenized leases.
- Due source claims use row locking with `FOR UPDATE SKIP LOCKED`.
- PostgreSQL integration tests are serialized because they intentionally share one test database and perform table resets.

## Performance and security

Performance rules include bounded collector concurrency, one pooled `NpgsqlDataSource`, one long-lived `HttpClient` per HTTP pipeline, bounded response buffering with `ArrayPool<byte>`, and maximum entry counts per source or summary request.

Security rules include Discord guild and channel allow lists, actor identity derived from authenticated interactions, no committed secrets or private source lists, prohibited XML DTDs, explicit redirect validation, private-network target rejection by default, HTTPS summary endpoints except loopback tests, and bounded request size, duration, redirect, retry, export, and summary counts.

Generative output remains downstream of preserved source links and deterministic metadata. It is never treated as the source of truth.
