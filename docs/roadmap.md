# Roadmap

## M0 — Repository bootstrap

- [x] Establish solution boundaries, strict compiler settings, CI, and PostgreSQL Docker Compose.
- [x] Implement URL-normalized article ingestion.

## M1 — Discord inbox

- [x] Add an isolated Discord.Net adapter and allow-list policy.
- [x] Parse GeekNews-style messages and embeds.
- [x] Persist Discord receipt leases.
- [x] Add private `/top` and `/search` interactions.
- [x] Connect interested, not-interested, hidden, save, and remove buttons.
- [x] Add per-user saved-item lists.

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

## M4 — Summaries and exports

- [x] Export selected saved articles as provider-neutral Markdown.
- [x] Keep manual export usable without an API key.
- [x] Add provider-neutral structured LLM summary contracts and validation.
- [x] Add deterministic SHA-256 summary cache identity and PostgreSQL persistence.
- [x] Add an optional OpenAI Responses provider and Discord `/summarize` workflow.
- [x] Add robots-aware article-body extraction with target, redirect, content-type, timeout, and size policies.
- [x] Persist bounded normalized article text and extraction failure state.
- [ ] Add a Gemini provider adapter behind the same application interface.

## M5 — Personal digest

- [ ] Add a deterministic daily and weekly digest query over ranked articles.
- [ ] Add Discord `/digest` with time-window, topic, and result-count controls.
- [ ] Add optional scheduled delivery to a configured Discord channel.
- [ ] Record digest delivery receipts so restarts cannot duplicate a report.

## M6 — v0.1 release and operations

- [ ] Add a production Worker Dockerfile and complete Compose deployment.
- [ ] Add `/status` for database, source, collector, and provider health.
- [ ] Add startup validation, graceful shutdown checks, and bounded log redaction.
- [ ] Add a Korean deployment and Discord-bot setup runbook.
- [ ] Add a practical starter source pack for AI, game industry, engines, and developer tools.
- [ ] Complete a final security, migration, and failure-recovery review.
- [ ] Mark the pull request ready and release `v0.1.0`.

## Post-v0.1 backlog

These are intentionally outside the initial finish line:

- title-similarity novelty scoring and negative-keyword penalties;
- clustering the same event across independent sources;
- volume-baseline and momentum scoring;
- web dashboard and vector search;
- automatic full-text retrieval for authenticated or JavaScript-only pages.
