# Architecture

## Purpose

Signal Radar is a personal technology-intelligence system. It collects a wide set of signals but exposes a deliberately small, ranked queue to the user.

```text
Sources
  -> collectors
  -> normalization
  -> deterministic deduplication
  -> classification and ranking
  -> persistence
  -> Discord and dashboard delivery
  -> optional LLM summarization or Markdown export
```

## Dependency rule

Dependencies point inward:

```text
Domain <- Application <- Infrastructure
                     <- Bot
                     <- Worker
```

- **Domain** contains stable business concepts and invariants.
- **Application** contains use cases and persistence contracts.
- **Infrastructure** implements PostgreSQL storage, migrations, URL normalization, and future external providers.
- **Bot** adapts Discord input and interactions to application commands.
- **Worker** is the composition root and background-process host.

Domain and Application do not depend on Discord, PostgreSQL, HTTP libraries, or LLM SDKs.

## Current ingestion slice

The current slice accepts an allowed Discord message, extracts an article candidate, canonicalizes its URL, creates a validated domain article, and inserts it only when its canonical URL is new.

LLMs are intentionally excluded from deterministic filtering and duplicate removal.

## PostgreSQL persistence

One long-lived, thread-safe `NpgsqlDataSource` owns connection pooling for the process. Individual operations open short-lived logical connections or commands and return them to the pool immediately.

- `articles.canonical_url` has a unique index and is the final article duplicate guard.
- Discord snowflakes use `numeric(20, 0)` so the entire unsigned 64-bit range is preserved.
- Discord processing uses a tokenized lease rather than a permanent pre-processing marker.
- Completed receipts remain idempotent across process restarts.
- Interrupted receipts can be reclaimed after lease expiry.
- A crash after article insertion but before receipt completion is safe: retry observes an article duplicate and completes the receipt.

## Migrations

SQL migrations are embedded in `SignalRadar.Infrastructure` and applied in filename order. Startup migration execution:

1. opens a PostgreSQL connection and transaction,
2. creates the migration ledger when necessary,
3. acquires a transaction-scoped PostgreSQL advisory lock,
4. verifies SHA-256 checksums for applied migrations,
5. applies pending migrations,
6. commits atomically,
7. runs a database health query before connecting to Discord.

This prevents concurrent worker instances from racing during schema initialization and prevents silently rewriting an already-applied migration.

## Performance principles

- Avoid LLM calls before filtering and deduplication.
- Use database unique constraints instead of read-before-write duplicate queries.
- Keep database commands short and parameterized.
- Prefer bounded channels for future asynchronous pipelines.
- Reuse `HttpClient` instances through `IHttpClientFactory` when collectors are added.
- Cache parsed source configuration and generated summaries.
- Keep article bodies outside hot listing queries.
- Measure allocations and throughput before introducing distributed queues.

## Security boundaries

- Process only explicitly allowed Discord guilds, channels, and users.
- Never commit tokens, database passwords, or API keys.
- Send all SQL values through parameters.
- Enforce length and range constraints at the database boundary.
- Block private-network destinations before fetching user-controlled URLs.
- Limit response size, redirect count, and request duration.
- Preserve original URLs and evidence alongside generated summaries.
