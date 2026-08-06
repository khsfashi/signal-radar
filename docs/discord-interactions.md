# Discord interactions

Signal Radar registers guild-scoped application commands for every configured `DISCORD_ALLOWED_GUILD_IDS` entry when the gateway becomes ready. Commands and buttons are accepted only when both the guild and channel are present in the configured allow lists.

The Discord application must be installed with the `bot` and `applications.commands` scopes. Article ingestion additionally requires **Message Content Intent** because GeekNews-style messages and embeds are parsed by the gateway.

## Commands

### `/top`

Returns the highest effective-score articles in a private response.

Options:

- `days`: 1 to 30, default 3.
- `topic`: optional topic slug.
- `limit`: 1 to 5, default 5.

### `/search`

Searches stored article titles and source names and returns a private ranked response.

Options:

- `query`: required, 2 to 100 characters.
- `days`: 1 to 30, default 30.
- `topic`: optional topic slug.
- `limit`: 1 to 5, default 5.

### `/saved`

Returns the invoking user's saved articles in reverse save order. Saved articles are independent from ranking feedback, so marking an article as interesting does not automatically place it in the saved list.

Options:

- `days`: saved within 1 to 3650 days, default 365.
- `topic`: optional topic slug.
- `limit`: 1 to 5, default 5.

### `/export`

Exports the invoking user's saved articles as a UTF-8 Markdown file in a private response. The file contains source links, publication and save times, deterministic topics, and current effective scores. It is provider-neutral and can be supplied manually to ChatGPT, Gemini, or another analysis tool without configuring an LLM API key in Signal Radar.

Options:

- `days`: saved within 1 to 3650 days, default 365.
- `topic`: optional topic slug.
- `limit`: 1 to 100, default 100.

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
- `저장`: adds the article to the invoking user's saved list. Repeated clicks are idempotent.
- `숨김`: stores `Hidden` with weight `-3` and excludes that article from later `/top` and `/search` results for the same Discord user.

Articles returned by `/saved` replace `저장` with `저장 해제`. Removing a saved article does not delete the article or alter its feedback.

Button custom IDs contain only the action and article UUID. Actor identity is derived from the authenticated Discord interaction as `discord:<user-id>` and is never accepted from button data.

A user's next feedback click for the same article replaces their previous feedback. The deterministic base score remains unchanged; feedback is applied only while ranked results are read.

## Markdown export

Exports are capped at 100 articles. The generated file includes a verification notice because Signal Radar preserves source links but does not claim that collected headlines or external content are accurate. File names include the UTC export timestamp.

No Discord user ID is written into the export file. Saved rows remain private to their actor ID in PostgreSQL, and only the authenticated user can request their own list through these commands.

## Response limits

Interactive article results are ephemeral and contain at most five embeds. Each article consumes one component row with four buttons, matching Discord's five-row message-component limit. Markdown export is also ephemeral and produces one bounded attachment.

## Command synchronization

The ready handler bulk-overwrites the application's guild command set with Signal Radar's current command definitions. This makes schema changes deterministic and immediately visible for guild commands. Any additional commands for the same Discord application should therefore be added to the same code-controlled command set.
