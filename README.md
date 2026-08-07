# Signal Radar

Signal Radar is a personal technology-intelligence pipeline for collecting, normalizing, deduplicating, classifying, and ranking AI, game-industry, and software-development news.

## Current capabilities

- Receives articles from an allow-listed Discord guild and channel.
- Polls configured RSS and Atom feeds, GitHub Releases, and Hacker News.
- Uses conditional requests, bounded retries, source leases, quarantine, and persistent health state.
- Canonicalizes URLs and deduplicates by URL plus stable `(source, external_id)` identity.
- Classifies articles into deterministic multi-label topics and inspectable score components.
- Exposes private Discord `/top`, `/search`, `/saved`, `/export`, `/digest`, and `/status` workflows.
- Stores interested, not-interested, hidden, and saved states per Discord actor.
- Sends optional daily and weekly Discord digests with PostgreSQL delivery receipts.
- Exports up to 100 saved links as provider-neutral UTF-8 Markdown.
- Supports optional OpenAI Responses and Gemini Generate Content structured summaries.
- Retrieves bounded HTML excerpts only for explicit summary requests.
- Honors robots rules, validates every redirect, rejects private networks, and limits content type, size, time, concurrency, and prompt length.
- Caches normalized article text and structured summaries with deterministic SHA-256 identities.
- Runs ordered checksum-verified PostgreSQL migrations under an advisory lock.
- Provides a non-root, read-only production container and Docker Health Check.
- Builds, tests, and validates the production container in GitHub Actions.

LLM calls are never part of ingestion. Deterministic collection, filtering, classification, and ranking remain available when every summary provider is disabled.

## Recommended Docker start

```bash
cp .env.example .env
cp config/feed-sources.starter.json config/feed-sources.json
cp config/github-repositories.starter.json config/github-repositories.json
cp config/ranking-profile.example.json config/ranking-profile.json
```

Replace every example password, Discord ID, and token in `.env`, then run:

```bash
docker compose up -d --build
docker compose ps
docker compose logs -f worker
```

Production mode rejects example values such as `replace-me` and `change-me`.

For a complete Korean walkthrough, use:

- [Discord 실제 적용 가이드](docs/discord-setup-ko.md)
- [운영·백업·복구 가이드](docs/operations-ko.md)

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

## Discord commands

The bot synchronizes guild-scoped commands after the Gateway reaches Ready.

```text
/top       ranked recent articles
/search    title and source search
/saved     personal reading list
/export    saved links as Markdown
/summarize optional structured AI briefing
/digest    daily or weekly ranked digest
/status    DB, collection, cache, provider, and scheduler status
```

Ranked results include `관심`, `별로`, `저장`, and `숨김` buttons. Commands and buttons work only in configured guilds and channels. `/summarize` is registered only when a provider is configured.

See [Discord interactions](docs/discord-interactions.md) and [Discord 실제 적용 가이드](docs/discord-setup-ko.md).

## Summary providers

Disabled:

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

Both adapters implement the same application interface, request JSON-schema-constrained output, and pass the result through local validation before caching.

Before a provider call, the article reader downloads only explicitly requested saved URLs. It does not execute JavaScript, use login cookies, bypass paywalls, or crawl discovered links. Failed retrievals fall back to metadata without failing the entire summary.

See [Structured summaries](docs/summaries.md).

## Scheduled digests

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

The actor user ID applies that user's hidden and feedback state to the ranking. The channel must also appear in `DISCORD_ALLOWED_CHANNEL_IDS`. Completed period deliveries are protected by a persistent receipt; failed leases can be retried.

## Starter source pack

The starter feed file contains official OpenAI, Unreal Engine, Godot, and GitHub feeds. The starter release file contains selected .NET, AI-tooling, and game-engine repositories.

These files are starting points, not a promise that every source will always keep the same endpoint. Source failure and quarantine state is visible in PostgreSQL and `/status`.

## Persistence guarantees

- Canonical article URLs are unique.
- Stable external items are unique by `(source, external_id)`.
- Score components and ranking-profile version are preserved.
- Feedback and saved articles are actor-scoped and independent.
- Hidden articles are filtered only for the actor who hid them.
- Article content stores bounded normalized text, status, hash, and diagnostics rather than raw HTML.
- Structured summaries are immutable for a deterministic input hash.
- Scheduled digest deliveries are unique per delivery key and scheduled window.
- Discord receipts and polling sources use expiring tokenized leases.
- Applied SQL migration checksums are verified on every startup.

## Documentation

- [Architecture](docs/architecture.md)
- [Discord 실제 적용 가이드](docs/discord-setup-ko.md)
- [Discord interactions](docs/discord-interactions.md)
- [운영·백업·복구 가이드](docs/operations-ko.md)
- [Structured summaries](docs/summaries.md)
- [Feed sources](docs/feed-sources.md)
- [External sources](docs/external-sources.md)
- [Ranking and feedback](docs/ranking.md)
- [Roadmap](docs/roadmap.md)
