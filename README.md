# Signal Radar

Signal Radar is a personal technology-intelligence pipeline for collecting, normalizing, deduplicating, classifying, ranking, and publishing AI, game-industry, game-development, and software-development news.

Discord is the primary inbox and reading surface. PostgreSQL is the source of truth, and LLM summarization is optional rather than part of ingestion.

## What it does

- Receives articles from allow-listed Discord guilds, channels, and optionally authors.
- Polls configured RSS/Atom feeds, GitHub Releases, and Hacker News.
- Canonicalizes URLs and deduplicates by canonical URL plus stable `(source, external_id)` identity.
- Classifies articles into deterministic multi-label topics and inspectable score components.
- Exposes private Discord `/top`, `/search`, `/saved`, `/export`, `/summarize`, `/digest`, `/status`, and `/help` workflows.
- Stores interested, not-interested, hidden, and saved state per Discord actor.
- Automatically publishes newly collected high-scoring articles to configured public Discord channels or Forum posts.
- Sends optional public daily and weekly Discord digests with PostgreSQL delivery receipts.
- Exports up to 100 saved links as provider-neutral UTF-8 Markdown.
- Supports optional OpenAI Responses and Gemini Generate Content structured summaries.
- Retrieves bounded HTML excerpts only for explicit summary requests.
- Honors robots rules, validates redirects, rejects private-network targets, and limits content type, size, time, concurrency, and prompt length.
- Caches normalized article text and structured summaries with deterministic SHA-256 identities.
- Runs ordered checksum-verified PostgreSQL migrations under an advisory lock.
- Provides a non-root, read-only production container and Docker health check.
- Builds, tests, runs PostgreSQL integration tests, and validates the production container in GitHub Actions.

LLM calls are never part of ingestion. Collection, deduplication, classification, ranking, feedback, saved articles, automatic publishing, and digests remain available when every summary provider is disabled.

## Quick start on Windows

Requirements:

- Windows with CPU virtualization enabled
- WSL 2 / Virtual Machine Platform
- Docker Desktop using the WSL 2 engine
- Git
- A Discord application and bot already invited to the target server

Clone the repository and enter it:

```powershell
git clone https://github.com/khsfashi/signal-radar.git
Set-Location signal-radar
```

Create local configuration files:

```powershell
Copy-Item .env.example .env
Copy-Item config\feed-sources.starter.json config\feed-sources.json
Copy-Item config\github-repositories.starter.json config\github-repositories.json
Copy-Item config\ranking-profile.example.json config\ranking-profile.json
```

Replace every example password, Discord ID, and token in `.env`. Production mode intentionally rejects placeholder secrets such as `replace-me` and `change-me`.

Validate Compose configuration without printing resolved secrets:

```powershell
docker compose config --quiet
```

Build and start PostgreSQL and the Worker:

```powershell
docker compose up -d --build
docker compose ps
docker compose logs --tail=200 worker
```

A healthy startup includes messages similar to:

```text
PostgreSQL migrations and startup health check completed.
Discord inbox gateway started.
Discord commands synchronized for guild ...
Discord /help command synchronized.
Gateway Ready
```

Useful commands:

```powershell
# Current container state
docker compose ps

# Recent Worker logs
docker compose logs --tail=200 worker

# Follow Worker logs
docker compose logs -f worker

# Recreate Worker after changing .env
docker compose up -d --force-recreate worker

# Rebuild after pulling new code
docker compose up -d --build --force-recreate worker

# Stop / start without deleting data
docker compose stop
docker compose start
```

Do not use `docker compose down -v` unless you intentionally want to delete the PostgreSQL volume and all Signal Radar data.

For the complete Korean installation walkthrough, see [Discord 실제 적용 가이드](docs/discord-setup-ko.md). For backups and recovery, see [운영·백업·복구 가이드](docs/operations-ko.md).

## Discord visibility model

Signal Radar deliberately separates personal query results from shared channel output.

| Feature | Visibility |
| --- | --- |
| `/top`, `/search`, `/saved`, `/export` | Private to the command user |
| `/summarize`, `/digest`, `/status`, `/help` | Private to the command user |
| Feedback/save confirmations | Private to the user who clicked |
| Scheduled daily/weekly digest | Public channel message |
| Automatic topic publication | Public channel message or public Forum post |

An automatically published article remains visible to everyone who can view the destination channel. Its `관심`, `별로`, `저장`, and `숨김` buttons can still be used independently by each Discord user; the confirmation response is private.

## Discord commands

The bot synchronizes guild-scoped commands after the Discord Gateway reaches Ready.

```text
/help       command guide and public/private visibility explanation
/top        ranked recent articles
/search     title and source search
/saved      personal reading list
/export     saved links as Markdown
/summarize  optional structured AI briefing
/digest     daily or weekly ranked digest on demand
/status     DB, collection, cache, provider, and scheduler status
```

`/summarize` is registered only when a summary provider is configured. Commands and buttons work only in configured guilds and channels.

See [Discord interactions](docs/discord-interactions.md) and [Discord 실제 적용 가이드](docs/discord-setup-ko.md).

## Automatic public topic publishing

Automatic topic publishing removes the need to manually run `/top` just to discover new high-scoring articles. The Worker periodically looks for newly collected articles, applies the configured score threshold, routes each article by primary topic, and posts qualifying articles as normal public Discord content.

Enable it in `.env`:

```env
DISCORD_TOPIC_PUBLISHING_ENABLED=true

# Every automatic destination must also be allow-listed.
DISCORD_ALLOWED_CHANNEL_IDS=111111111111111111

# Several topics may share the same destination channel.
DISCORD_TOPIC_CHANNELS=ai:111111111111111111,game-industry:111111111111111111,game-development:111111111111111111,developer-tools:111111111111111111,research:111111111111111111,business:111111111111111111,security:111111111111111111,other:111111111111111111

DISCORD_TOPIC_MIN_SCORE=60
DISCORD_TOPIC_BATCH_SIZE=10
DISCORD_TOPIC_POLL_SECONDS=30
DISCORD_TOPIC_LEASE_SECONDS=120
DISCORD_TOPIC_RETRY_SECONDS=60
```

Supported route keys are exactly:

```text
ai
game-industry
game-development
developer-tools
research
business
security
other
```

Each route uses `topic:channelId`. A topic may be configured only once, while multiple topics may point to the same channel.

Text and announcement destinations receive ordinary public messages. Forum destinations receive public Forum posts. The bot therefore needs permission to send messages in text destinations and to create public threads/posts in Forum destinations.

The first time automatic publishing is enabled, Signal Radar persists an activation timestamp. Articles collected before that timestamp are not backfilled, preventing an existing archive from flooding Discord. Articles collected after activation remain eligible across Worker restarts.

Each `(article, channel, publication kind)` is tracked with a PostgreSQL delivery receipt and lease. Completed deliveries are suppressed on later polling cycles; failed deliveries become retryable after `DISCORD_TOPIC_RETRY_SECONDS`.

Discord delivery and PostgreSQL receipt completion are separate network operations, so a process crash in the narrow interval after Discord accepts a post but before the receipt commit can produce a rare duplicate after lease expiry.

See [Automatic Discord topic publishing](docs/automatic-topic-publishing.md).

## Summary providers

Disable summaries completely:

```env
SUMMARY_PROVIDER=disabled
```

OpenAI Responses:

```env
SUMMARY_PROVIDER=openai-responses
OPENAI_API_KEY=replace-with-real-key
OPENAI_SUMMARY_MODEL=your-model-id
OPENAI_RESPONSES_ENDPOINT=https://api.openai.com/v1/responses
```

Gemini Generate Content:

```env
SUMMARY_PROVIDER=gemini-generate-content
GEMINI_API_KEY=replace-with-real-key
GEMINI_SUMMARY_MODEL=gemini-3.6-flash
```

Both adapters implement the same application interface, request JSON-schema-constrained output, and pass results through local validation before caching.

Before a provider call, the article reader downloads only explicitly requested saved URLs. It does not execute JavaScript, use login cookies, bypass paywalls, or crawl discovered links. Failed retrievals fall back to metadata without failing the entire summary.

See [Structured summaries](docs/summaries.md).

## Scheduled public digests

Example Seoul-time schedule:

```env
DIGEST_DAILY_ENABLED=true
DIGEST_DAILY_TIME=08:00
DIGEST_WEEKLY_ENABLED=true
DIGEST_WEEKLY_DAY=Monday
DIGEST_WEEKLY_TIME=08:00
DIGEST_TIME_ZONE=Asia/Seoul
DIGEST_CHANNEL_ID=234567890123456789
DIGEST_ACTOR_USER_ID=345678901234567890
DIGEST_TOPIC=all
DIGEST_LIMIT=10
```

The actor user ID applies that user's hidden and feedback state to ranking. The destination channel must also appear in `DISCORD_ALLOWED_CHANNEL_IDS`. Completed scheduled deliveries are protected by persistent receipts; failed leases can be retried.

The manual `/digest` command remains private to the user who invokes it. Scheduled digest delivery is public.

## Local .NET start

Start PostgreSQL, prepare the same configuration files, and export the variables from `.env` into the operating-system environment. The .NET process does not automatically load `.env`.

```bash
dotnet restore SignalRadar.sln
dotnet build SignalRadar.sln --configuration Release
dotnet test SignalRadar.sln --configuration Release --no-build
dotnet run --project src/SignalRadar.Worker
```

Container or local DB-only health check:

```bash
dotnet run --project src/SignalRadar.Worker -- --healthcheck
```

## Starter source pack

The starter feed file contains official OpenAI, Unreal Engine, Godot, and GitHub feeds. The starter release file contains selected .NET, AI-tooling, and game-engine repositories.

These files are starting points, not a promise that every source will always keep the same endpoint. One broken feed does not stop other collectors. Source failure and quarantine state is persisted and exposed through operational status.

## Persistence guarantees

- Canonical article URLs are unique.
- Stable external items are unique by `(source, external_id)`.
- Score components and ranking-profile version are preserved.
- Feedback and saved articles are actor-scoped and independent.
- Hidden articles are filtered only for the actor who hid them.
- Article content stores bounded normalized text, status, hash, and diagnostics rather than raw HTML.
- Structured summaries are immutable for a deterministic input hash.
- Scheduled digest deliveries are unique per delivery key and scheduled window.
- Automatic topic publications use persistent per-article/channel receipts and expiring leases.
- Discord ingestion receipts and polling sources use expiring tokenized leases.
- Applied SQL migration checksums are verified on every startup.

## Architecture

```text
Discord / RSS / Atom / GitHub Releases / Hacker News
                    │
                    ▼
               Collection
                    │
                    ▼
       Normalize + deterministic dedupe
                    │
                    ▼
          Classify + deterministic rank
                    │
                    ▼
               PostgreSQL
              ┌─────┴─────┐
              ▼           ▼
       Discord reading   Public automatic
       and feedback      topic publishing
              │
              ▼
       Optional summaries / export
```

The repository keeps Domain, Application, Infrastructure, Bot, and Worker concerns separated. PostgreSQL remains the durable source of truth; Discord is a delivery and interaction surface rather than the canonical database.

## Documentation

- [Architecture](docs/architecture.md)
- [Discord 실제 적용 가이드](docs/discord-setup-ko.md)
- [Discord interactions](docs/discord-interactions.md)
- [Automatic Discord topic publishing](docs/automatic-topic-publishing.md)
- [운영·백업·복구 가이드](docs/operations-ko.md)
- [Structured summaries](docs/summaries.md)
- [Feed sources](docs/feed-sources.md)
- [External sources](docs/external-sources.md)
- [Ranking and feedback](docs/ranking.md)
- [Roadmap](docs/roadmap.md)
