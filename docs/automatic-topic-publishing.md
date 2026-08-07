# Automatic Discord topic publishing

Signal Radar can publish newly collected articles to topic-specific Discord destinations without requiring `/top` or `/digest`. Articles are no longer posted one by one as soon as they arrive. Each route waits for its configured batch window and publishes a compact list of translated titles in one public Discord message or Forum post.

For the Korean operational walkthrough, source strategy, and example channel layout, see [뉴스 운영 가이드](news-delivery-ko.md).

## Visibility

Automatic topic batches are normal Discord resources, not ephemeral interaction responses.

- Text and announcement channels receive a public message.
- Forum channels receive a public forum post.
- Everyone who can view the destination channel sees the same batch.
- Per-user source mutes affect actor-aware private reads such as `/top`, `/search`, and `/digest`; they cannot retroactively hide a shared public channel message for one member.
- Slash-command results remain private by design.

The bot needs permission to send messages in text destinations and to create public threads/posts in Forum destinations.

## Runtime route management

The normal operating path is Discord-based configuration instead of editing `.env` for every destination:

```text
/route-set topic:<topic> channel:<channel> batch-minutes:<5~1440> min-score:<0~100>
/route-list
/route-remove topic:<topic>
```

`/route-set`, `/route-list`, and `/route-remove` require the Discord `Administrator` or `Manage Guild` permission. Runtime routes are persisted in PostgreSQL and override legacy environment routes for the same topic.

Supported topic keys are:

- `ai`
- `game-industry`
- `game-development`
- `developer-tools`
- `research`
- `business`
- `security`
- `economy`
- `markets`
- `other`

Several topics may point to the same Discord destination. One topic has one effective destination at a time.

## Bootstrap and legacy environment configuration

Environment routes remain supported for initial bootstrap or compatibility:

```env
DISCORD_TOPIC_PUBLISHING_ENABLED=true
DISCORD_TOPIC_CHANNELS=
DISCORD_TOPIC_MIN_SCORE=0
DISCORD_TOPIC_BATCH_SIZE=10
DISCORD_TOPIC_BATCH_WINDOW_MINUTES=30
DISCORD_TOPIC_POLL_SECONDS=30
DISCORD_TOPIC_LEASE_SECONDS=120
DISCORD_TOPIC_RETRY_SECONDS=60
```

`DISCORD_TOPIC_MIN_SCORE=0` is the default so articles are not hidden only because of ranking score. If volume later becomes excessive, prefer improving sources and channel routing first; then raise the per-route minimum score with `/route-set` if necessary.

Legacy `DISCORD_TOPIC_CHANNELS` entries still use `topic:channelId` and those bootstrap channel IDs must appear in `DISCORD_ALLOWED_CHANNEL_IDS`. Runtime `/route-set` destinations are constrained to an allowed guild instead, so normal route changes do not require editing the environment channel list.

## Batch selection

For a route with `batch-minutes=30`, a newly collected article becomes eligible after it has spent 30 minutes in the collection window. Eligible articles are selected in collection order and grouped up to `DISCORD_TOPIC_BATCH_SIZE` in one message.

The first time automatic publishing is enabled, Signal Radar stores an activation timestamp. Articles collected before that timestamp are not backfilled, preventing an existing archive from flooding Discord. The activation timestamp survives Worker restarts, so articles collected during downtime remain eligible.

## Translation

Display titles pass through the optional title translator before the batch is sent. The default Docker Compose deployment uses self-hosted LibreTranslate for English-to-Korean translation.

Translation is deliberately outside ingestion and ranking. Successful results are cached in PostgreSQL by deterministic SHA-256 source-title identity. Korean titles bypass translation, and any translation error or timeout falls back to the original title without failing the batch.

## Delivery receipts and retries

Each `(article, channel, publication kind)` still has its own PostgreSQL receipt even though several articles can share one Discord resource. Expiring leases prevent concurrent duplicate work. When a batch succeeds, every article lease is completed with the same Discord message/thread ID; on failure, every leased article becomes retryable after `DISCORD_TOPIC_RETRY_SECONDS`.

Discord delivery and PostgreSQL receipt completion are separate network operations. A Worker crash after Discord accepts a batch but before every receipt is completed can therefore produce a rare duplicate after lease expiry. The design otherwise keeps durable at-least-once processing with completed-delivery deduplication.

## Feed and personal source commands

Administrators can manage RSS/Atom subscriptions at runtime:

```text
/feed-add
/feed-list
/feed-enable
/feed-disable
```

Each user can independently filter sources from actor-aware private reads:

```text
/source-mute
/source-unmute
/source-muted
```

## Apply locally

After pulling this version, rebuild the stack so the PostgreSQL migration and LibreTranslate sidecar are applied:

```powershell
git pull origin main
docker compose up -d --build
docker compose ps
docker compose logs --tail=200 worker
```

Do not use `docker compose down -v` unless deleting the PostgreSQL volume is intentional.
