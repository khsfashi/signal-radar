# Feed sources

Set `FEED_SOURCE_CONFIG_PATH` to a JSON array of source definitions. URLs must be absolute HTTP or HTTPS URLs, polling intervals must be between 1 and 1440 minutes, and duplicate URLs are rejected before startup.

The collector sends ETag and Last-Modified validators, handles redirects explicitly, streams into a bounded buffer, and retries timeouts, network failures, HTTP 408, HTTP 429, and HTTP 5xx. Retry-After is capped at five minutes.

Private, loopback, link-local, and local-host targets are rejected by default. `FEED_HTTP_ALLOW_PRIVATE_NETWORKS=true` is an explicit opt-in intended only for trusted local source configuration.

```text
FEED_HTTP_TIMEOUT_SECONDS=20
FEED_HTTP_MAX_RESPONSE_BYTES=4194304
FEED_HTTP_MAX_ATTEMPTS=3
FEED_HTTP_MAX_REDIRECTS=5
FEED_HTTP_MAX_CONNECTIONS_PER_SERVER=8
FEED_HTTP_ALLOW_PRIVATE_NETWORKS=false
FEED_POLL_BATCH_SIZE=4
FEED_LEASE_SECONDS=120
FEED_IDLE_DELAY_SECONDS=30
```

Due sources are claimed using `FOR UPDATE SKIP LOCKED` and an expiring lease. Failure scheduling is 1 minute, 5 minutes, 15 minutes, and 1 hour; the fifth consecutive failure quarantines the source for six hours. A successful download or HTTP 304 resets the failure count.

XML DTD processing and external resolution are prohibited. HTTP and XML sizes are capped, at most 500 usable entries are processed per document, and entries without a title plus HTTP or HTTPS link are ignored.
