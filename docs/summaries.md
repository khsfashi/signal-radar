# Structured summaries

Signal Radar keeps LLM calls outside ingestion. Collection, deduplication, classification, base scoring, feedback, saving, Markdown export, and digest generation continue to work when no summary provider is configured.

## Current scope

`/summarize` creates an on-demand briefing from the authenticated Discord user's saved articles. Before calling the configured provider, Signal Radar attempts to obtain a bounded text excerpt from each linked page.

The source material can contain titles, sources, canonical links, timestamps, deterministic topics and scores, extraction status, and bounded article excerpts. Extracted text is explicitly marked as untrusted source material. The prompt tells the provider to ignore instructions found inside article text, avoid implying complete page coverage, distinguish source claims from confirmed facts, and report uncertainty.

The structured result contains a short title, overview, one to five key signals, why they may matter, optional next checks, and optional caveats.

## Article retrieval policy

Article retrieval occurs only after an explicit summary request. Signal Radar does not crawl links found inside an article.

For every original URL and redirect target, the reader:

1. requires absolute HTTP or HTTPS;
2. rejects localhost, loopback, link-local, private, carrier-grade NAT, and multicast targets by default;
3. obtains and evaluates `/robots.txt` for the relevant origin;
4. repeats target and robots validation after each redirect;
5. accepts only HTML or XHTML;
6. streams with a timeout and hard byte limit;
7. parses HTML without executing JavaScript;
8. removes common navigation, forms, scripts, advertising, cookie, and subscription boilerplate;
9. persists only bounded normalized text and diagnostics.

A 4xx robots response is treated as unavailable and allows retrieval. A 5xx response or network failure is treated as unreachable and disallows retrieval. Dynamic, authenticated, paywalled, non-HTML, oversized, or too-short pages fall back to metadata-only input for that article.

## Provider abstraction

Application code depends on `IArticleSummaryProvider`, not a vendor SDK. Both implementations use direct bounded HTTP adapters and the same local output validator and PostgreSQL cache.

OpenAI Responses:

```env
SUMMARY_PROVIDER=openai-responses
OPENAI_API_KEY=replace-with-real-key
OPENAI_SUMMARY_MODEL=your-model-id
OPENAI_RESPONSES_ENDPOINT=https://api.openai.com/v1/responses
```

Gemini Generate Content:

```env
SUMMARY_PROVIDER=gemini-generate-content
GEMINI_API_KEY=replace-with-real-key
GEMINI_SUMMARY_MODEL=gemini-2.5-flash
```

The Gemini adapter sends `systemInstruction`, user `contents`, and a JSON response format with a schema. Both providers return the same `ArticleSummaryContent` structure. `SUMMARY_PROVIDER=disabled` removes `/summarize`; `/export` and `/digest` remain available.

## Cache identity

The summary input hash is SHA-256 over deterministic JSON containing provider, model, prompt version, language, exact system instructions, and exact bounded input including article metadata, extraction status, content hash, and included excerpt.

The uppercase hash is the primary key of `article_summary_cache`. A changed provider, model, language, prompt, article order, score, extraction status, or excerpt creates a new cache key. Concurrent first writers use `ON CONFLICT DO NOTHING`, preserving one immutable successful result for the exact input.

## Limits

Default limits:

```text
Saved articles per summary:       1 to 20
Concurrent article retrievals:    4
Article HTML response:            2 MiB
robots.txt parsing prefix:        512 KiB
Normalized article text:          30,000 characters
Excerpt per article in prompt:    10,000 characters
Total article text in prompt:     60,000 characters
Article request timeout:          15 seconds
Provider timeout:                 60 seconds
Provider response:                256 KiB
```

Korean (`ko`) and English (`en`) output are supported. Validated environment bounds are listed in `.env.example`.

## Stored data and privacy

`article_content_cache` stores one current status, bounded normalized text, hash, timestamps, content type, HTTP status, and bounded diagnostics per article. Raw HTML, cookies, authorization data, and arbitrary response headers are not persisted.

`article_summary_cache` stores provider and model identifiers, prompt version, language, article count, structured result, generated timestamp, and optional provider response ID. API keys and Discord user IDs are not stored in either summary cache.

Article excerpts are sent to the configured external provider only when `/summarize` is explicitly invoked. Users who do not want article text sent externally can use `/export` or `/digest` instead.
