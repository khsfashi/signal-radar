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
- Stores explicit per-actor feedback and queries feedback-adjusted rankings.
- Preserves idempotency across restarts with database constraints and expiring leases.
- Tracks source success, failure, retry, and quarantine state.
- Runs PostgreSQL migrations with checksum validation and an advisory lock.
- Builds and runs unit plus PostgreSQL integration tests in GitHub Actions.

LLM calls are intentionally not part of ingestion. Deterministic collection, filtering, deduplication, classification, and base scoring happen first.

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
```

Enable at least one ingestion pipeline, then run:

```bash
dotnet restore SignalRadar.sln
dotnet build SignalRadar.sln --configuration Release
dotnet test SignalRadar.sln --configuration Release --no-build
dotnet run --project src/SignalRadar.Worker
```

## Discord

When `DISCORD_ENABLED=true`, configure the bot token and allow-listed guild, channel, and optional author identifiers. Enable **Message Content Intent** in the Discord Developer Portal.

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
- Discord messages, feeds, and external API sources use expiring tokenized leases.
- Five consecutive polling failures quarantine a source for six hours.
- Applied SQL migration checksums are verified on every startup.

## Documentation

- [Architecture](docs/architecture.md)
- [Feed sources](docs/feed-sources.md)
- [External sources](docs/external-sources.md)
- [Ranking and feedback](docs/ranking.md)
- [Roadmap](docs/roadmap.md)
