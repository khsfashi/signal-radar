# Contributing

Thanks for considering a contribution to Signal Radar.

## Development requirements

- .NET 10 SDK
- Docker with PostgreSQL 17 available for integration tests
- Git

## Local validation

Restore, build, and run the full test suite before opening a pull request:

```bash
dotnet restore SignalRadar.sln
dotnet build SignalRadar.sln --configuration Release --no-restore
dotnet test SignalRadar.sln --configuration Release --no-build
```

For the PostgreSQL integration tests, set `SIGNAL_RADAR_TEST_DATABASE_CONNECTION_STRING` to a dedicated disposable test database. Do not point tests at a production database.

Validate the production container when deployment-related code changes:

```bash
docker build --tag signal-radar:local .
```

## Configuration and secrets

Never commit real `.env` files, Discord credentials, API keys, production database credentials, or personal runtime configuration. Use the checked-in example/starter files as templates.

## Pull requests

Keep changes focused and include tests for behavior changes. The CI pipeline treats compiler warnings as errors, runs unit and PostgreSQL integration tests, builds the production container, and performs secret scanning.

For security-sensitive issues, follow `SECURITY.md` instead of opening a public issue.
