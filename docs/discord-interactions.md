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

## Feedback buttons

Each returned article has three buttons:

- `관심`: stores `Interested` with weight `+3`.
- `별로`: stores `NotInterested` with weight `-2`.
- `숨김`: stores `Hidden` with weight `-3` and excludes that article from later `/top` and `/search` results for the same Discord user.

The button custom ID contains only the action and article UUID. The actor identity is derived from the authenticated Discord interaction as `discord:<user-id>` and is never accepted from button data.

A user's next click for the same article replaces their previous feedback. The deterministic base score remains unchanged; feedback is applied only while ranked results are read.

## Response limits

Results are ephemeral and contain at most five embeds. Each article consumes one component row with three buttons, matching Discord's five-row message-component limit.

## Command synchronization

The ready handler bulk-overwrites the application's guild command set with Signal Radar's current command definitions. This makes schema changes deterministic and immediately visible for guild commands. Any additional commands for the same Discord application should therefore be added to the same code-controlled command set.
