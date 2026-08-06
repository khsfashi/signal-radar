# Roadmap

## M0 — Repository bootstrap

- [x] Establish solution boundaries, strict compiler settings, CI, and PostgreSQL Docker Compose.
- [x] Implement URL-normalized article ingestion.

## M1 — Discord inbox

- [x] Add an isolated Discord.Net adapter and allow-list policy.
- [x] Parse GeekNews-style messages and embeds.
- [x] Persist Discord receipt leases.
- [x] Add private `/top` and `/search` interactions.
- [x] Connect interested, not-interested, and hidden buttons.
- [ ] Add saved-item lists and Markdown export interactions.

## M2 — Persistent collection

- [x] Add ordered checksum-verified PostgreSQL migrations.
- [x] Add RSS and Atom collectors.
- [x] Add source health, retries, leases, and failure quarantine.
- [x] Add canonical URL uniqueness.
- [x] Add GitHub Releases and Hacker News collectors.
- [x] Add source and external-ID identity where stable IDs exist.

## M3 — Ranking and feedback

- [x] Add deterministic source-trust, topic-interest, practical-impact, and freshness scoring.
- [x] Add multi-label topic classification and a configurable personal ranking profile.
- [x] Persist inspectable score components and profile versions.
- [x] Store explicit interested, not-interested, and hidden feedback per actor.
- [x] Query ranked articles with bounded feedback adjustment.
- [x] Route ranked reads and explicit feedback through Discord interactions.
- [ ] Add title-similarity novelty scoring and negative-keyword penalties.

## M4 — Summaries and exports

- [ ] Export selected articles as Markdown.
- [ ] Add provider-neutral structured LLM summaries and caching.
- [ ] Keep manual export usable without an API key.

## M5 — Trend radar

- [ ] Cluster the same event across independent sources.
- [ ] Compare short-window volume against a longer baseline.
- [ ] Add momentum scoring after event clustering exists.
- [ ] Produce daily and weekly trend reports.
