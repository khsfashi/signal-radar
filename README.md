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

This repository is in the bootstrap phase. The first milestone establishes the .NET solution boundaries and a small, testable article-ingestion vertical slice.

## Technology baseline

- .NET 10 LTS
- C#
- PostgreSQL for persistent storage
- Docker Compose for local infrastructure
- xUnit for tests
- GitHub Actions for build and test validation

## Quick start

Requirements:

- .NET 10 SDK
- Docker with Docker Compose

```bash
dotnet restore SignalRadar.sln
dotnet build SignalRadar.sln --configuration Release
dotnet test SignalRadar.sln --configuration Release --no-build
docker compose up -d postgres
```

Run the Discord inbox worker:

```bash
dotnet run --project src/SignalRadar.Worker
```

## Configuration

Copy `.env.example` to `.env` for local infrastructure. Never commit bot tokens, LLM API keys, database passwords, or Discord identifiers.

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

The token is read only from `DISCORD_BOT_TOKEN`; it must never be committed. The current receipt store and article inbox are intentionally in-memory and will be replaced by PostgreSQL persistence in the next milestone.
