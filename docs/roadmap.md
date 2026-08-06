# Roadmap

## M0 — Repository bootstrap

- [x] Establish solution boundaries.
- [x] Add strict compiler settings.
- [x] Add CI build and tests.
- [x] Add PostgreSQL Docker Compose configuration.
- [x] Implement a minimal URL-normalized article ingestion slice.

## M1 — Discord inbox

- [x] Add Discord.Net through an adapter isolated in `SignalRadar.Bot`.
- [x] Restrict processing to configured guild and channel identifiers.
- [x] Parse GeekNews bot messages and embeds.
- [x] Persist Discord message identifiers for idempotency.
- [ ] Add save, dismiss, and export interactions.

## M2 — Persistent collection

- [x] Add PostgreSQL schema and transactional embedded migrations.
- [x] Add PostgreSQL article and leased Discord receipt stores.
- [x] Add startup database health validation.
- [x] Add PostgreSQL integration tests in GitHub Actions.
- [ ] Add RSS and Atom collectors.
- [ ] Add GitHub Releases and Hacker News collectors.
- [ ] Add retry policy, source health, and failure quarantine.
- [ ] Add source and external-ID unique constraints.

## M3 — Ranking and feedback

- [ ] Add source trust, novelty, practical impact, and momentum scoring.
- [ ] Add topic and negative-keyword weights.
- [ ] Learn user preference weights from explicit reactions.
- [ ] Route ranked items to Discord topic channels.

## M4 — Summaries and exports

- [ ] Export selected articles as Markdown.
- [ ] Add provider-neutral `ILlmProvider`.
- [ ] Add structured summary schemas.
- [ ] Cache summaries by content hash, provider, model, and prompt version.
- [ ] Keep manual export usable without any API key.

## M5 — Trend radar

- [ ] Cluster articles describing the same event.
- [ ] Compare short-window volume against a longer baseline.
- [ ] Require independent sources for high-confidence trends.
- [ ] Produce daily and weekly trend reports.
