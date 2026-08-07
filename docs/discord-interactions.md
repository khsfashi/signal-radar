# Discord interactions

Signal Radar registers guild-scoped application commands for every configured `DISCORD_ALLOWED_GUILD_IDS` entry when the Gateway becomes ready. Commands and buttons are accepted only when both guild and channel appear in the configured allow lists.

The Discord application must be installed with the `bot` and `applications.commands` scopes. Article-message ingestion additionally requires **Message Content Intent**.

## Commands

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

Unavailable, blocked, non-HTML, oversized, or too-short pages fall back to metadata without failing the complete briefing. The Footer identifies provider, model, cache state, and whether the content-aware prompt was used.

### `/digest`

Returns a deterministic ranked digest without invoking an LLM.

- `period`: `daily` or `weekly`, default `daily`.
- `topic`: optional topic slug.
- `limit`: 1 to 10, default 10.

The daily period reads the previous 24 hours; weekly reads the previous seven days. The invoking Discord user's actor ID applies their hidden and feedback state to the result.

### `/status`

Returns an ephemeral operational snapshot containing uptime, PostgreSQL article and saved counts, enabled feed and external-source counts, summary and content cache counts, ranking profile, configured summary provider, scheduled-digest state, and latest collection timestamp.

Supported topic slugs:

```text
all
ai
game-industry
game-development
developer-tools
research
business
security
other
```

## Article buttons

Articles returned by `/top` and `/search` have four buttons:

- `관심`: stores `Interested` with weight `+3`.
- `별로`: stores `NotInterested` with weight `-2`.
- `저장`: adds the article to the user's saved list idempotently.
- `숨김`: stores `Hidden` with weight `-3` and excludes the article from that user's later ranked results.

Articles returned by `/saved` replace `저장` with `저장 해제`. Button custom IDs contain only action and article UUID. Actor identity is derived from the authenticated interaction as `discord:<user-id>`.

## Scheduled delivery

Daily and weekly schedules are optional. A schedule specifies timezone, local clock time, public destination channel, actor user ID, topic, and article limit.

The destination channel must also be in `DISCORD_ALLOWED_CHANNEL_IDS`. The actor user ID supplies personalized hidden and feedback state; it is not mentioned in the public message.

Each occurrence uses a persistent `(delivery_key, window_start)` receipt with an expiring ownership token:

- a completed occurrence is not sent twice after restart;
- an interrupted lease can be reclaimed after expiry;
- a failed send records a bounded error and becomes retryable;
- a zero-article occurrence is completed without posting an empty message;
- manual `/digest` calls do not consume scheduled receipts.

## Response and synchronization behavior

Interactive results are ephemeral. Ranked and saved results contain at most five embeds because each article consumes one Discord component row. `/digest` and `/status` produce one bounded embed. Scheduled digests are public messages in the configured channel.

The Ready handler bulk-overwrites the application's Guild command set. `/summarize` is included only when a provider runtime is enabled. Guild commands usually appear shortly after the Worker reaches Ready.

For the complete installation process, see [Discord에 Signal Radar 적용하기](discord-setup-ko.md).
