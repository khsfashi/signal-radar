# Signal Radar

Signal Radar is a personal technology-intelligence pipeline for collecting, normalizing, ranking, and summarizing AI, game-industry, and software-development news.

## Initial goals

- Collect trusted signals from Discord, RSS/Atom, official blogs, GitHub releases, and APIs.
- Preserve source URLs and raw metadata before enrichment.
- Remove duplicates before using an LLM.
- Keep ranking deterministic and explainable.
- Support both manual Markdown export and optional BYOK LLM providers.
- Treat Discord as an inbox and control surface, not the primary database.

## Repository status

The repository currently has a working Discord ingestion boundary and PostgreSQL-backed article and receipt persistence. Ranking, additional collectors, and export workflows remain future milestones.

## Technology baseline

- .NET 10 LTS
- C#
- PostgreSQL with Npgsql
- Docker Compose for local infrastructure
- xUnit for unit and PostgreSQL integration tests
- GitHub Actions for build and test validation

## Quick start

Requirements:

- .NET 10 SDK
- Docker with Docker Compose

```bash
cp .env.example .env
docker compose up -d postgres
dotnet restore SignalRadar.sln
dotnet build SignalRadar.sln --configuration Release
dotnet test SignalRadar.sln --configuration Release --no-build
```

Export the variables from `.env` through your preferred local environment loader, then run:

```bash
dotnet run --project src/SignalRadar.Worker
```

At startup, the worker acquires a PostgreSQL advisory lock, applies pending embedded SQL migrations transactionally, verifies the database with a health query, and only then connects to Discord.

## Configuration

Copy `.env.example` to `.env` for local infrastructure. Never commit bot tokens, LLM API keys, database passwords, or Discord identifiers.

`DATABASE_CONNECTION_STRING` is consumed by the worker. `POSTGRES_*` variables configure the local Docker container.

## Documentation

- [Architecture](docs/architecture.md)
- [Roadmap](docs/roadmap.md)

## Discord inbox configuration

The worker connects to Discord through `Discord.Net.WebSocket` and accepts messages only when all configured allow-list checks pass.

1. Create a Discord application and bot in the Developer Portal.
2. Enable the **Message Content Intent** on the bot page.
3. Invite the bot with permission to view the private source channel and read message history.
4. Copy `.env.example` values into your runtime environment.
5. Set `DISCORD_ALLOWED_GUILD_IDS` and `DISCORD_ALLOWED_CHANNEL_IDS` to comma-separated Discord snowflake IDs.
6. Set `DISCORD_ALLOWED_AUTHOR_IDS` to the GeekNews bot or webhook user ID when known.

The token is read only from `DISCORD_BOT_TOKEN`; it must never be committed. Article URLs are protected by a PostgreSQL unique index. Discord deliveries use leased receipt records so an interrupted message can be retried after its lease expires, while completed messages remain idempotent across restarts.
