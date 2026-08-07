# Roadmap

Signal Radar's initial public engineering baseline is complete on `main`. The project is now in a post-v0.1 iteration phase focused on better signal quality, source operations, and portfolio-quality observability without weakening the deterministic core.

## M0 — Repository bootstrap ✅

- [x] Establish solution boundaries, strict compiler settings, CI, and PostgreSQL Docker Compose.
- [x] Implement URL-normalized article ingestion.

## M1 — Discord inbox ✅

- [x] Add an isolated Discord.Net adapter and allow-list policy.
- [x] Parse GeekNews-style messages and embeds.
- [x] Persist Discord receipt leases.
- [x] Add private `/top`, `/search`, `/saved`, and `/export` interactions.
- [x] Connect interested, not-interested, hidden, save, and remove buttons.

## M2 — Persistent collection ✅

- [x] Add ordered checksum-verified PostgreSQL migrations.
- [x] Add RSS and Atom collectors.
- [x] Add source health, retries, leases, and failure quarantine.
- [x] Add canonical URL plus stable external-ID uniqueness.
- [x] Add GitHub Releases and Hacker News collectors.

## M3 — Ranking and feedback ✅

- [x] Add deterministic source-trust, topic-interest, practical-impact, and freshness scoring.
- [x] Add multi-label topic classification and configurable personal profiles.
- [x] Persist inspectable score components and profile versions.
- [x] Store actor-scoped feedback and apply bounded ranking adjustment.

## M4 — Summaries and exports ✅

- [x] Export saved articles as provider-neutral Markdown without an API key.
- [x] Add provider-neutral structured-summary contracts and validation.
- [x] Add deterministic SHA-256 summary caching.
- [x] Add OpenAI Responses and Gemini Generate Content adapters.
- [x] Add `/summarize` with robots-aware bounded article extraction.
- [x] Persist bounded normalized text, extraction state, and content hashes.

## M5 — Personal digest ✅

- [x] Add deterministic daily and weekly digest queries.
- [x] Add Discord `/digest` with period, topic, and limit controls.
- [x] Add optional timezone-aware scheduled Discord delivery.
- [x] Add persistent delivery receipts and expiring leases.

## M6 — Production and public-readiness baseline ✅

- [x] Add a non-root production Worker Dockerfile and complete Compose deployment.
- [x] Add `/status` for database, source, cache, provider, and scheduler state.
- [x] Add startup validation, graceful cancellation, Docker Health Check, bounded logs, and secret redaction.
- [x] Add Korean Discord installation and operations/recovery runbooks.
- [x] Add starter official feeds and selected GitHub Release sources.
- [x] Add CI validation for unit tests, PostgreSQL integration tests, and the production container build.
- [x] Complete the initial security, migration, and failure-recovery review.
- [x] Add public contribution and security policies.
- [x] Add Dependabot and full-history Gitleaks scanning.
- [x] Harden the Docker build context and keep PostgreSQL private by default in production-style Compose.
- [x] Publish the repository publicly.

## Post-v0.1 improvements already on `main` ✅

These features were added after the original v0.1 implementation baseline and are part of the current public repository state.

### News delivery and operations

- [x] Add self-hosted LibreTranslate title translation with PostgreSQL caching and original-title fallback.
- [x] Batch public topic delivery instead of posting every article immediately.
- [x] Add runtime `/feed-add`, `/feed-list`, `/feed-enable`, and `/feed-disable` administration.
- [x] Add runtime `/route-set`, `/route-list`, and `/route-remove` topic routing.
- [x] Add per-user `/source-mute`, `/source-unmute`, and `/source-muted` preferences.
- [x] Add Economy and Markets topics plus broader Korean game-industry and market discovery sources.

### Maintenance and runtime hardening

- [x] Add manager-only `/reclassify days:<1..3650>` for bounded historical reassessment.
- [x] Preserve original collection timestamps during reclassification.
- [x] Batch reclassification writes instead of issuing one PostgreSQL update per article.
- [x] Split the Worker composition root into focused runtime components.
- [x] Fix Discord interaction acknowledgement timeouts for I/O-heavy commands.
- [x] Add regression coverage for hostname text leaking into topic classification.

## Release packaging

The code and public-readiness baseline are already on `main`. A GitHub tag/release should be created from the final documentation-polished `main` revision so the release page reflects the same public-facing README and roadmap as the repository landing page.

Suggested first public release:

```text
v0.1.0 — deterministic collection, ranking, Discord delivery, optional summaries,
         PostgreSQL durability, production Docker deployment, and CI hardening
```

## Next — signal quality

These are the highest-value improvements that preserve the current deterministic design.

- [ ] Add title-similarity novelty scoring and configurable negative-keyword penalties.
- [ ] Cluster the same event across independent sources while preserving individual source links.
- [ ] Add source-volume baselines and momentum scoring.
- [ ] Surface why an article ranked highly in Discord without requiring an LLM.
- [ ] Add ranking-regression fixtures for representative AI, game-industry, game-development, economy, and market articles.

## Later — exploration and administration

These are deliberately outside the core collection/ranking path and should remain optional.

- [ ] Add a read-only web dashboard for corpus and ranking inspection.
- [ ] Add vector-assisted search as an optional secondary retrieval path.
- [ ] Add retention policies and administrative data-pruning commands.
- [ ] Add richer operational metrics and long-term source-quality reporting.

## Explicit non-goals for the current baseline

- Automatic retrieval of authenticated or paywalled content.
- Browser automation for JavaScript-only pages in the ingestion path.
- Replacing deterministic ranking with an opaque LLM ranking call.
- Treating generated summaries as the canonical article record.
