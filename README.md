# Signal Radar

Signal Radar is a personal technology-intelligence pipeline for collecting, normalizing, deduplicating, classifying, and ranking AI, game-industry, and software-development news.

## Current capabilities

- Receives articles from an allow-listed Discord guild and channel.
- Polls configured RSS and Atom feeds.
- Polls GitHub Releases for configured repositories.
- Polls Hacker News top, best, or new story lists.
- Uses conditional HTTP requests with ETag and Last-Modified validators.
- Canonicalizes URLs before PostgreSQL insertion.
- Deduplicates by canonical URL and, where available, stable `(source, external_id)` identity.
- Classifies articles into multiple deterministic topics.
- Scores source trust, personal topic interest, practical impact, and freshness.
- Persists every score component and the ranking-profile version.
- Exposes private Discord `/top`, `/search`, `/saved`, and `/export` workflows.
- Stores interested, not-interested, and per-user hidden feedback from Discord buttons.
- Stores an independent per-user article reading list.
- Exports up to 100 saved source links as provider-neutral UTF-8 Markdown.
- Optionally registers `/summarize` for on-demand structured saved-article briefings.
- Retrieves bounded HTML article excerpts only for explicit summary requests.
- Honors robots rules, validates redirect targets, rejects private networks, and enforces content-type, size, and timeout limits.
- Caches normalized article text and structured summaries with deterministic SHA-256 identities.
- Preserves idempotency across restarts with database constraints and expiring leases.
- Tracks source success, failure, retry, and quarantine state.
- Runs PostgreSQL migrations with checksum validation and an advisory lock.
- Builds and runs unit plus PostgreSQL integration tests in GitHub Actions.

LLM calls are intentionally not part of ingestion. Deterministic collection, filtering, deduplication, classification, and base scoring happen first. Article retrieval and summary generation are optional and happen only after an explicit user command.

## Quick start

Start PostgreSQL and prepare local source files:

```bash
docker compose up -d postgres
cp config/feed-sources.example.json config/feed-sources.json
cp config/github-repositories.example.json config/github-repositories.json
cp config/ranking-profile.example.json config/ranking-profile.json
```

The ranking profile is optional. When `RANKING_PROFILE_PATH` is not set, the built-in `default-v1` profile is used.

The worker reads operating-system environment variables directly. `.env` is used by Docker Compose but is not automatically loaded by the .NET process.

```text
DATABASE_CONNECTION_STRING=Host=localhost;Port=5432;Database=signal_radar;Username=signal_radar;Password=replace-me
RANKING_PROFILE_PATH=config/ranking-profile.json
DISCORD_ENABLED=true
FEED_SOURCE_CONFIG_PATH=config/feed-sources.json
GITHUB_RELEASE_SOURCE_CONFIG_PATH=config/github-repositories.json
HACKER_NEWS_ENABLED=true
SUMMARY_PROVIDER=disabled
```

Enable at least one ingestion pipeline, then run:

```bash
dotnet restore SignalRadar.sln
dotnet build SignalRadar.sln --configuration Release
dotnet test SignalRadar.sln --configuration Release --no-build
dotnet run --project src/SignalRadar.Worker
```

## Discord

When `DISCORD_ENABLED=true`, configure the bot token and allow-listed guild, channel, and optional ingestion-author identifiers. Enable **Message Content Intent** for article ingestion, and install the application with the `bot` and `applications.commands` scopes.

The gateway synchronizes guild-scoped `/top`, `/search`, `/saved`, and `/export` commands. Ranked results are ephemeral and include article-specific `관심`, `별로`, `저장`, and `숨김` buttons. Saved-list results provide `저장 해제`, and hidden articles are excluded from later ranked results for the same Discord user.

`/export` creates a bounded Markdown attachment containing the user's saved source links, timestamps, topics, and current scores. The export is usable manually with any analysis tool and does not require an LLM API key.

When `SUMMARY_PROVIDER=openai-responses` and the required OpenAI settings are present, the synchronized command set also includes `/summarize`. It summarizes one to twenty saved articles in Korean or English and returns an ephemeral structured briefing. Signal Radar attempts to include cleaned article excerpts and records extraction failures as metadata-only fallbacks.

See [Discord interactions](docs/discord-interactions.md) for command options, topic slugs, feedback behavior, saved-list behavior, and command synchronization.

## Optional structured summaries

The first provider implementation calls the OpenAI Responses endpoint through a provider-neutral application interface. It requests strict JSON Schema output and validates all fields again before caching or displaying them.

```text
SUMMARY_PROVIDER=openai-responses
OPENAI_API_KEY=replace-me
OPENAI_SUMMARY_MODEL=your-model-id
OPENAI_RESPONSES_ENDPOINT=https://api.openai.com/v1/responses
```

Before the provider call, a robots-aware HTML reader retrieves only the requested saved-article URLs. It does not execute JavaScript or crawl discovered links. It accepts only HTML/XHTML, removes common navigation and promotional boilerplate, selects an article-like block, and stores bounded normalized text rather than raw HTML.

The summary cache key includes provider, model, prompt version, language, exact instructions, article metadata, extraction statuses, content hashes, and the exact bounded excerpts sent to the provider. Repeating the same request returns the PostgreSQL-cached result without another provider call. API keys and Discord user identifiers are not stored in the summary cache.

See [Structured summaries](docs/summaries.md) for retrieval policy, cache identity, limits, security behavior, and privacy implications.

## RSS and Atom

Feed definitions are synchronized into PostgreSQL at startup:

```json
[
  {
    "name": "official-product-blog",
    "url": "https://example.com/feed.xml",
    "pollIntervalMinutes": 15,
    "enabled": true
  }
]
```

See [Feed sources](docs/feed-sources.md) for retries, quarantine, HTTP limits, and security behavior.

## GitHub Releases and Hacker News

GitHub repository definitions are loaded from a local JSON file. `GITHUB_API_TOKEN` is optional for public repositories but useful for authenticated rate limits. Hacker News is configured with environment variables and uses the official Firebase API.

See [External sources](docs/external-sources.md) for source identity, filters, HTTP limits, and configuration.

## Ranking

Articles can have several topics, while the highest-priority matching rule becomes the primary topic. The deterministic base score combines source trust, personal topic interest, practical impact, and freshness. Explicit interested, not-interested, and hidden feedback adjusts ranked queries without overwriting the original base score.

See [Ranking and feedback](docs/ranking.md) for the formula, profile format, persisted fields, and current limitations.

## Persistence guarantees

- Canonical article URLs are unique.
- Stable external items are unique by `(source, external_id)` even when their URLs change.
- Ranking components and the profile version used at collection time are preserved.
- Explicit feedback is unique per article and actor.
- Saved articles are unique per article and actor and remain separate from feedback.
- A user's hidden articles are excluded from their later Discord ranked queries.
- Article content has one current status and bounded normalized-text cache per article.
- Structured summaries are immutable for a deterministic input hash.
- Discord messages, feeds, and external API sources use expiring tokenized leases.
- Five consecutive polling failures quarantine a source for six hours.
- Applied SQL migration checksums are verified on every startup.

## Documentation

- [Architecture](docs/architecture.md)
- [Discord interactions](docs/discord-interactions.md)
- [Structured summaries](docs/summaries.md)
- [Feed sources](docs/feed-sources.md)
- [External sources](docs/external-sources.md)
- [Ranking and feedback](docs/ranking.md)
- [Roadmap](docs/roadmap.md)
