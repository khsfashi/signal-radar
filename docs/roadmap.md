# Roadmap

## M0 — Repository bootstrap

- [x] Establish solution boundaries, strict compiler settings, CI, and PostgreSQL Docker Compose.
- [x] Implement URL-normalized article ingestion.

## M1 — Discord inbox

- [x] Add an isolated Discord.Net adapter and allow-list policy.
- [x] Parse GeekNews-style messages and embeds.
- [x] Persist Discord receipt leases.
- [x] Add private `/top`, `/search`, `/saved`, and `/export` interactions.
- [x] Connect interested, not-interested, hidden, save, and remove buttons.

## M2 — Persistent collection

- [x] Add ordered checksum-verified PostgreSQL migrations.
- [x] Add RSS and Atom collectors.
- [x] Add source health, retries, leases, and failure quarantine.
- [x] Add canonical URL plus stable external-ID uniqueness.
- [x] Add GitHub Releases and Hacker News collectors.

## M3 — Ranking and feedback

- [x] Add deterministic source-trust, topic-interest, practical-impact, and freshness scoring.
- [x] Add multi-label topic classification and configurable personal profiles.
- [x] Persist inspectable score components and profile versions.
- [x] Store actor-scoped feedback and apply bounded ranking adjustment.

## M4 — Summaries and exports

- [x] Export saved articles as provider-neutral Markdown without an API key.
- [x] Add provider-neutral structured-summary contracts and validation.
- [x] Add deterministic SHA-256 summary caching.
- [x] Add OpenAI Responses and Gemini Generate Content adapters.
- [x] Add `/summarize` with robots-aware bounded article extraction.
- [x] Persist bounded normalized text, extraction state, and content hashes.

## M5 — Personal digest

- [x] Add deterministic daily and weekly digest queries.
- [x] Add Discord `/digest` with period, topic, and limit controls.
- [x] Add optional timezone-aware scheduled Discord delivery.
- [x] Add persistent delivery receipts and expiring leases.

## M6 — v0.1 release and operations

- [x] Add a non-root production Worker Dockerfile and complete Compose deployment.
- [x] Add `/status` for database, source, cache, provider, and scheduler state.
- [x] Add startup validation, graceful cancellation, Docker Health Check, bounded logs, and secret redaction.
- [x] Add Korean Discord installation and operations/recovery runbooks.
- [x] Add starter official feeds and selected GitHub Release sources.
- [x] Add CI validation for unit tests, PostgreSQL integration tests, and the production container build.
- [x] Complete the initial security, migration, and failure-recovery review.
- [x] Mark the completed pull request ready for review.
- [ ] Merge to `main` and create the `v0.1.0` tag/release.

## Post-v0.1 backlog

These are intentionally outside the initial release:

- title-similarity novelty scoring and negative-keyword penalties;
- clustering the same event across independent sources;
- volume-baseline and momentum scoring;
- web dashboard and vector search;
- automatic retrieval for authenticated or JavaScript-only pages;
- retention policies and administrative data-pruning commands.
