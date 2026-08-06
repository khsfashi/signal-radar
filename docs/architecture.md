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
- **Application** contains use cases and ports.
- **Infrastructure** implements persistence, HTTP clients, feed readers, and external providers.
- **Bot** adapts Discord input and interactions to application commands.
- **Worker** is the composition root and background-process host.

Domain and Application must not depend on Discord, PostgreSQL, HTTP libraries, or LLM SDKs.

## Initial vertical slice

The bootstrap slice accepts an article candidate, canonicalizes its URL, creates a validated domain article, and inserts it into an inbox only when the canonical URL is new.

The first duplicate key is the canonical URL. Later milestones add:

1. source and external identifier
2. content hash
3. title fingerprint
4. semantic event clustering

LLMs are intentionally excluded from deterministic duplicate removal.

## Performance principles

- Avoid LLM calls before filtering and deduplication.
- Prefer bounded channels for asynchronous pipelines.
- Reuse `HttpClient` instances through `IHttpClientFactory`.
- Cache parsed source configuration and generated summaries.
- Keep article bodies outside hot listing queries.
- Use database unique constraints as the final duplicate guard.
- Measure allocations and throughput before introducing distributed queues.

## Security boundaries

- Process only explicitly allowed Discord guilds, channels, and users.
- Never commit tokens or API keys.
- Block private-network destinations before fetching user-controlled URLs.
- Limit response size, redirect count, and request duration.
- Preserve original URLs and evidence alongside generated summaries.
