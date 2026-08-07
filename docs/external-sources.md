# External sources

Signal Radar currently supports GitHub Releases and Hacker News through the same PostgreSQL lease, retry, and quarantine model used by scheduled collectors.

## GitHub Releases

Copy the example configuration:

```bash
cp config/github-repositories.example.json config/github-repositories.json
```

Each entry accepts `owner`, `repository`, optional `displayName`, polling interval, prerelease policy, and enabled state. The stable article source key is `github:<owner>/<repository>`, and the GitHub release ID is stored as `external_id`.

Draft releases are always ignored. Prereleases are included only when `includePrereleases` is true. `GITHUB_API_TOKEN` is optional and is never stored in PostgreSQL.

## Hacker News

Enable the collector with `HACKER_NEWS_ENABLED=true`. Supported lists are `topstories`, `beststories`, and `newstories`.

Useful settings:

```text
HACKER_NEWS_STORY_LIST=topstories
HACKER_NEWS_MAX_ITEMS=50
HACKER_NEWS_MIN_SCORE=10
HACKER_NEWS_POLL_INTERVAL_MINUTES=5
HACKER_NEWS_ITEM_CONCURRENCY=8
```

The Hacker News item ID is stored as `external_id`. Stories without an outbound URL use their discussion page as the canonical URL.

## HTTP behavior

External API responses have bounded duration and size. Requests retry transient network failures, timeouts, HTTP 408, HTTP 429, and server errors. ETag and Last-Modified validators are persisted when the upstream API provides them.

GitHub and Hacker News endpoints are constructed from validated repository identifiers or fixed official API hosts. Automatic redirects are disabled.

## Reliability

External sources are claimed with `FOR UPDATE SKIP LOCKED` and expiring tokens, so multiple workers do not intentionally poll the same source at once. Successful requests reset failure state. Failed requests retry after 1 minute, 5 minutes, 15 minutes, and 1 hour; the fifth consecutive failure quarantines the source for six hours.
