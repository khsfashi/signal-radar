# Structured summaries

Signal Radar keeps LLM calls outside ingestion. Collection, deduplication, classification, base scoring, feedback, saving, and Markdown export continue to work when no summary provider is configured.

## Current scope

`/summarize` creates an on-demand briefing from the authenticated Discord user's saved articles. Before calling the configured provider, Signal Radar attempts to obtain a bounded text excerpt from each linked page.

The source material can contain:

- article titles and source names;
- canonical links;
- publication and save times;
- deterministic topics and scores;
- extraction status and failure detail;
- bounded article-text excerpts when retrieval and cleaning succeed.

The structured result contains a short title, an overview, one to five key signals, why they may matter, optional next checks, and optional caveats.

Extracted text is explicitly marked as untrusted source material. The prompt tells the provider to ignore instructions found inside article text, avoid implying that extraction captured a complete page, distinguish source claims from confirmed facts, and avoid inventing details absent from the supplied material.

## Article retrieval policy

Article retrieval is performed only for an explicit summary request. Signal Radar does not crawl links discovered inside an article.

For every article URL and every redirect target, the reader:

1. requires an absolute HTTP or HTTPS URL;
2. rejects localhost, loopback, link-local, private, carrier-grade NAT, and multicast targets by default;
3. obtains and evaluates `/robots.txt` for the relevant origin;
4. follows at most the configured redirect count and repeats target and robots checks after each redirect;
5. accepts only `text/html` or `application/xhtml+xml`;
6. streams the response with a timeout and a hard byte limit;
7. parses HTML without executing JavaScript;
8. removes scripts, styles, navigation, forms, sidebars, cookie banners, subscription panels, and similar boilerplate;
9. selects an article-like semantic block and stores only bounded normalized text.

The robots parser follows the RFC 9309 group-selection, longest-match, wildcard, end-anchor, and allow-wins-ties behavior. A 4xx robots response is treated as unavailable and allows retrieval; a 5xx response or network failure is treated as unreachable and disallows retrieval. Robots documents are cached in memory for at most the configured duration.

Dynamic pages that require JavaScript, authenticated pages, paywalled pages, non-HTML documents, very large pages, and pages without a sufficiently long article-like block fall back to metadata-only input for that article.

## Content persistence

`article_content_cache` stores one current extraction result per article:

- canonical URL;
- extraction status;
- normalized text and uppercase SHA-256 hash for successful extraction;
- fetch and refresh timestamps;
- optional HTTP status, content type, and bounded diagnostic detail.

Raw HTML, cookies, authentication data, and HTTP headers are not persisted. Successful text is cached for seven days by default. Unsupported, too-large, and too-short pages use the same long cache because repeated requests are unlikely to help. Temporary network and server failures use a shorter cache.

## Provider abstraction

Application code depends on `IArticleSummaryProvider`, not a vendor SDK. The first implementation uses the OpenAI Responses HTTP API with strict JSON Schema output. Another provider can implement the same interface without changing the summary use case, article reader, or PostgreSQL caches.

Enable the current provider with:

```text
SUMMARY_PROVIDER=openai-responses
OPENAI_API_KEY=replace-me
OPENAI_SUMMARY_MODEL=your-model-id
OPENAI_RESPONSES_ENDPOINT=https://api.openai.com/v1/responses
```

`SUMMARY_PROVIDER=disabled` or an unset value removes `/summarize` from the synchronized Discord command set. `/export` remains available without an API key.

## Cache identity

The summary input hash is SHA-256 over a deterministic JSON representation of:

- provider name;
- model name;
- prompt version;
- requested language;
- exact system instructions;
- exact bounded summary input, including extraction status, content hash, and included excerpt.

The uppercase hexadecimal hash is the primary key of `article_summary_cache`. Repeating an identical request returns the cached structured result without another provider call. A changed model, language, prompt, article order, score, extraction status, or extracted text creates a different cache key.

Concurrent first writers use `ON CONFLICT DO NOTHING`. A successful result is immutable for a specific input hash.

## Limits

Default limits are:

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

Korean (`ko`) and English (`en`) output are supported. Limits are configurable within validated bounds through the environment variables shown in `.env.example`.

## Stored data and privacy

The summary cache stores provider and model identifiers, prompt version, language, article count, structured JSON result, generated timestamp, and optional provider response identifier. API keys and Discord user identifiers are not stored in either summary cache.

The cache can be shared when two users produce exactly the same provider, model, language, prompt version, and ordered article input. User identity is deliberately excluded because it is not sent to the provider or used by the prompt.

Article excerpts are sent to the configured external provider only when the user explicitly invokes `/summarize`. Users who do not want article text sent to a provider can continue to use `/export` and process the source links manually.
