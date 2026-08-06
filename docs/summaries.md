# Structured summaries

Signal Radar keeps LLM calls outside ingestion. Collection, deduplication, classification, base scoring, feedback, and saving continue to work when no summary provider is configured.

## Current scope

`/summarize` creates an on-demand briefing from the authenticated Discord user's saved articles. The current source material contains article titles, source names, canonical links, publication and save times, deterministic topics, and scores. Signal Radar does not claim that the linked page bodies were read.

The structured result contains:

- a short title;
- an overview;
- one to five key signals;
- why the signals may matter;
- optional next checks;
- optional caveats and missing context.

The prompt explicitly requires the provider to avoid inventing product details, dates, benchmarks, causes, or conclusions that are not present in the supplied metadata.

## Provider abstraction

Application code depends on `IArticleSummaryProvider`, not a vendor SDK. The first implementation uses the OpenAI Responses HTTP API with strict JSON Schema output. A later provider can implement the same interface without changing the summary use case or PostgreSQL cache.

Enable the current provider with:

```text
SUMMARY_PROVIDER=openai-responses
OPENAI_API_KEY=replace-me
OPENAI_SUMMARY_MODEL=your-model-id
OPENAI_RESPONSES_ENDPOINT=https://api.openai.com/v1/responses
```

`SUMMARY_PROVIDER=disabled` or an unset value removes `/summarize` from the synchronized Discord command set. `/export` remains available without an API key.

## Cache identity

The input hash is SHA-256 over a deterministic JSON representation of:

- provider name;
- model name;
- prompt version;
- requested language;
- ordered saved-article metadata and scores.

The uppercase hexadecimal hash is the primary key of `article_summary_cache`. Repeating an identical request returns the cached structured result without another provider call. Changing the model, language, prompt version, article order, or article metadata creates a different cache key.

Concurrent first writers use `ON CONFLICT DO NOTHING`. A successful result is immutable for a specific input hash.

## Limits

- one to twenty saved articles per summary;
- Korean (`ko`) and English (`en`) output;
- provider timeout from 5 to 300 seconds;
- provider response size from 1 KiB to 4 MiB;
- at most eight provider connections per server;
- HTTPS endpoints, except loopback HTTP for tests.

The default response-size limit is 256 KiB and the default timeout is 60 seconds.

## Stored data

The cache stores the provider and model identifiers, prompt version, language, article count, structured JSON result, generated timestamp, and optional provider response identifier. API keys and Discord user identifiers are not stored in the summary cache.

The current cache is shared when two users produce exactly the same provider, model, language, prompt version, and ordered article input. User identity is deliberately excluded because it is not sent to the provider or used by the prompt.
