# Automatic Discord topic publishing

Signal Radar can publish newly collected, high-scoring articles to topic-specific Discord destinations without requiring `/top` or `/digest`.

## Visibility

Automatic topic publications are normal Discord resources, not ephemeral interaction responses.

- Text and announcement channels receive a public message.
- Forum channels receive a public forum post.
- Everyone who can view the destination channel can view the article.
- Feedback and save button confirmations remain private to the user who clicked them.
- Slash-command results remain private by design.

## Configuration

Enable the publisher and map one primary topic to each destination channel:

```env
DISCORD_TOPIC_PUBLISHING_ENABLED=true
DISCORD_ALLOWED_CHANNEL_IDS=111111111111111111,222222222222222222
DISCORD_TOPIC_CHANNELS=ai:111111111111111111,game-development:222222222222222222
DISCORD_TOPIC_MIN_SCORE=60
DISCORD_TOPIC_BATCH_SIZE=10
DISCORD_TOPIC_POLL_SECONDS=30
DISCORD_TOPIC_LEASE_SECONDS=120
DISCORD_TOPIC_RETRY_SECONDS=60
```

Supported topic keys are:

- `ai`
- `game-industry`
- `game-development`
- `developer-tools`
- `research`
- `business`
- `security`
- `other`

Every destination must also appear in `DISCORD_ALLOWED_CHANNEL_IDS`. A primary topic can be configured only once, while several topics may share one destination channel.

## Selection and deduplication

The publisher selects articles whose primary topic matches the route and whose effective score meets `DISCORD_TOPIC_MIN_SCORE`. It processes candidates sequentially in collection order, up to `DISCORD_TOPIC_BATCH_SIZE` per route and polling cycle.

The first time the feature is enabled, Signal Radar stores an activation timestamp. Articles collected before that timestamp are not backfilled, which prevents an existing archive from flooding Discord. The activation timestamp survives Worker restarts, so articles collected during downtime remain eligible.

Each `(article, channel, publication kind)` has a PostgreSQL receipt. Expiring leases prevent concurrent duplicate sends. Completed receipts permanently suppress redelivery, while failed sends become eligible after `DISCORD_TOPIC_RETRY_SECONDS`.

## Commands

`/help` explains which features are public and which are private. Automatic publications include `관심`, `별로`, `저장`, and `숨김` buttons. The article message stays public; each button confirmation is ephemeral.
