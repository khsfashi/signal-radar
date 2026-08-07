# Discord interactions

Signal Radar registers guild-scoped application commands for every configured `DISCORD_ALLOWED_GUILD_IDS` entry after the Discord Gateway becomes ready.

Article-reading commands and buttons are accepted only in configured guilds and allowed channels. Feed, route, reclassification, and source-preference commands are also guild-scoped; manager-only commands require Discord `Administrator` or `Manage Guild` permission.

The Discord application must be installed with the `bot` and `applications.commands` scopes. Discord-message ingestion additionally requires **Message Content Intent**.

## Visibility model

Signal Radar deliberately separates personal reads from shared publication.

| Surface | Visibility |
| --- | --- |
| `/top`, `/search`, `/saved`, `/export` | Private to the command user |
| `/summarize`, `/digest`, `/status`, `/help` | Private to the command user |
| `/source-mute`, `/source-unmute`, `/source-muted` | Private to the command user |
| `/feed-*`, `/route-*`, `/reclassify` responses | Private to the manager/admin who invoked them |
| Feedback/save confirmations | Private to the user who clicked |
| Scheduled daily/weekly digest | Public channel message |
| Automatic topic publication | Public text/announcement message or Forum post |

A public article remains visible to everyone who can view its destination. Its feedback buttons still write actor-scoped state and return private confirmations.

## Reading commands

### `/top`

Returns the highest effective-score recent articles in an ephemeral response.

- `days`: 1 to 30, default 3.
- `topic`: optional topic slug.
- `limit`: 1 to 5, default 5.

### `/search`

Searches stored article titles and source names.

- `query`: required, 2 to 100 characters.
- `days`: 1 to 30, default 30.
- `topic`: optional topic slug.
- `limit`: 1 to 5, default 5.

### `/saved`

Returns the invoking user's saved articles in reverse save order.

- `days`: 1 to 3650, default 365.
- `topic`: optional topic slug.
- `limit`: 1 to 5, default 5.

### `/export`

Exports the invoking user's saved articles as an ephemeral UTF-8 Markdown attachment.

- `days`: 1 to 3650, default 365.
- `topic`: optional topic slug.
- `limit`: 1 to 100, default 100.

### `/summarize`

Registered only when OpenAI Responses or Gemini Generate Content is configured. It defers the interaction, performs bounded robots-aware article retrieval, and returns one structured ephemeral briefing.

- `days`: 1 to 3650, default 30.
- `topic`: optional topic slug.
- `limit`: 1 to 20, default 10.
- `language`: `ko` or `en`, default `ko`.

Unavailable, blocked, non-HTML, oversized, or too-short pages fall back to metadata instead of failing the complete briefing. The result identifies provider/model/cache context and whether content-aware source text was available.

### `/digest`

Returns a deterministic ranked digest without invoking an LLM.

- `period`: `daily` or `weekly`, default `daily`.
- `topic`: optional topic slug.
- `limit`: 1 to 10, default 10.

The daily period reads the previous 24 hours; weekly reads the previous seven days. The invoking Discord actor's hidden, feedback, and personal source-preference state is applied to actor-aware ranking.

### `/status`

Returns an ephemeral operational snapshot containing uptime, PostgreSQL article/save counts, enabled source counts, latest collection state, cache counts, ranking profile, configured summary provider, and scheduler state.

### `/help`

Returns a compact Korean command guide and explains which Signal Radar surfaces are private versus publicly posted.

## Per-user source preferences

These commands do not require manager permission and affect only the invoking Discord actor.

### `/source-mute`

```text
/source-mute source:<exact source name>
```

Adds the source to the actor's personal mute set. The source remains collected and can still appear in shared public publication; it is excluded from that actor's personal actor-aware reads.

### `/source-unmute`

```text
/source-unmute source:<exact source name>
```

Removes a source from the actor's personal mute set.

### `/source-muted`

Lists the actor's current muted sources.

## Manager commands — RSS / Atom feeds

The following commands require Discord `Administrator` or `Manage Guild` permission. Runtime feed state is stored in PostgreSQL and survives Worker restarts.

### `/feed-add`

```text
/feed-add name:<unique name> url:<absolute HTTP(S) feed URL> minutes:<1..1440>
```

- `name`: required unique source name.
- `url`: required absolute HTTP(S) RSS/Atom URL.
- `minutes`: optional polling interval, default 30.

The new feed is enabled immediately and becomes eligible for the next polling cycle.

### `/feed-list`

Shows configured runtime feed sources and their enabled state, polling interval, and URL. The response is bounded to keep Discord output manageable.

### `/feed-enable`

```text
/feed-enable name:<source name>
```

Enables a stored feed.

### `/feed-disable`

```text
/feed-disable name:<source name>
```

Disables a stored feed without deleting its definition.

Checked-in `config/feed-sources.json` remains useful for bootstrap/code-managed sources while runtime feed administration is persisted in PostgreSQL.

## Manager commands — topic routes

Runtime topic routes are the preferred way to map a topic to a Discord destination. They take precedence over the legacy/bootstrap `DISCORD_TOPIC_CHANNELS` setting.

### `/route-set`

```text
/route-set topic:<topic> channel:<channel> batch-minutes:<5..1440> min-score:<0..100>
```

- `topic`: required concrete topic.
- `channel`: required destination in the same Discord guild.
- `batch-minutes`: optional batching window, default 30.
- `min-score`: optional minimum score, default 0.

A score of `0` means the route does not suppress articles purely because of ranking score.

### `/route-list`

Lists the current PostgreSQL-backed runtime routes with destination, batching window, and minimum score.

### `/route-remove`

```text
/route-remove topic:<topic>
```

Removes the runtime route for that topic.

## Manager command — historical reassessment

### `/reclassify`

```text
/reclassify days:<1..3650>
```

Default: 30 days.

The command defers its ephemeral Discord response, scans the selected recent window in bounded pages, evaluates articles with the currently loaded ranking/topic policy, and updates only assessment fields that changed.

Reclassification preserves article identity, the original `collected_at` timestamp used for freshness, actor saves/feedback/preferences, and completed publication receipts. It therefore does not intentionally redeliver already published articles.

## Supported topic slugs

```text
all
ai
game-industry
game-development
developer-tools
research
business
security
economy
markets
other
```

`all` is a query/digest filter rather than a concrete publication route.

## Article buttons

Articles returned by ranked/search views expose actor-scoped actions:

- `관심`: stores positive interest feedback.
- `별로`: stores negative interest feedback.
- `저장`: adds the article to the user's saved list idempotently.
- `숨김`: stores hidden feedback and removes the article from that actor's later ranked results.

Saved-list views replace `저장` with `저장 해제`. Button custom IDs contain action and article identity only; actor identity is derived from the authenticated Discord interaction.

## Automatic public topic publishing

When automatic topic publishing is enabled, the Worker periodically selects eligible uncompleted articles and groups them by configured topic route.

Current runtime behavior includes:

- a configurable per-route batching window;
- a configurable per-route minimum score;
- batched Discord output rather than one message for every new article;
- title translation through the optional self-hosted LibreTranslate service;
- original-title fallback on translation failure;
- PostgreSQL-backed per-article/channel publication receipts and expiring leases.

Text and announcement destinations receive public messages. Forum destinations receive public Forum posts.

The first activation timestamp prevents historical archive flooding. Articles collected before initial activation are not automatically backfilled.

Discord delivery and PostgreSQL receipt completion are separate network operations. A process crash after Discord accepts a post but before the receipt commit can therefore produce a rare duplicate after lease expiry; the model provides durable at-least-once processing with completed-delivery deduplication rather than claiming cross-system exactly-once delivery.

See [Automatic topic publishing](automatic-topic-publishing.md) and the [Korean news operations guide](news-delivery-ko.md).

## Scheduled delivery

Daily and weekly schedules are optional. A schedule specifies timezone, local clock time, public destination channel, actor user ID, topic, and article limit.

The destination channel must also be in `DISCORD_ALLOWED_CHANNEL_IDS`. The actor user ID supplies personalized hidden, feedback, and applicable preference state; it is not mentioned in the public message.

Each occurrence uses persistent delivery state:

- a completed occurrence is not sent twice after restart;
- an interrupted lease can be reclaimed after expiry;
- a failed send records a bounded error and becomes retryable;
- a zero-article occurrence completes without posting an empty message;
- manual `/digest` calls do not consume scheduled delivery receipts.

## Response and synchronization behavior

I/O-heavy article commands defer early enough to avoid Discord's interaction acknowledgement timeout and later modify/follow up the private response.

Guild command definitions are synchronized after the Gateway reaches Ready. Optional commands such as `/summarize` are registered only when their corresponding runtime capability is enabled.

For installation details, see [Discord에 Signal Radar 적용하기](discord-setup-ko.md).
