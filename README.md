# Signal Radar

Signal Radar is a personal technology-intelligence pipeline for collecting, normalizing, deduplicating, and eventually ranking AI, game-industry, and software-development news.

## Current capabilities

- Receives articles from an allow-listed Discord guild and channel.
- Polls configured RSS 2.0, RSS 1.0-style, and Atom feeds.
- Uses conditional HTTP requests with ETag and Last-Modified validators.
- Canonicalizes article URLs before PostgreSQL insertion.
- Preserves idempotency across restarts with database unique constraints and leases.
- Tracks feed success, failure, retry, and quarantine state.
- Runs PostgreSQL migrations with checksum validation and an advisory lock.
- Builds and runs unit plus PostgreSQL integration tests in GitHub Actions.

LLM calls are intentionally not part of ingestion. Deterministic filtering and deduplication happen first.

## Quick start

Start PostgreSQL and prepare a local source file:

```bash
docker compose up -d postgres
cp config/feed-sources.example.json config/feed-sources.json
```

The worker reads operating-system environment variables directly. `.env` is used by Docker Compose but is not automatically loaded by the .NET process.

```text
DATABASE_CONNECTION_STRING=Host=localhost;Port=5432;Database=signal_radar;Username=signal_radar;Password=replace-me
DISCORD_ENABLED=true
FEED_SOURCE_CONFIG_PATH=config/feed-sources.json
```

Enable one or both ingestion pipelines, then run:

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

## Persistence guarantees

- Canonical article URLs are unique.
- Discord messages and feed sources use expiring tokenized leases.
- Five consecutive feed failures quarantine a source for six hours.
- Applied SQL migration checksums are verified on every startup.

## Documentation

- [Architecture](docs/architecture.md)
- [Feed sources](docs/feed-sources.md)
- [Roadmap](docs/roadmap.md)
