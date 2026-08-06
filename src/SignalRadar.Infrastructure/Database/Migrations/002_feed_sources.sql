CREATE TABLE feed_sources (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    feed_url text NOT NULL,
    enabled boolean NOT NULL DEFAULT true,
    polling_interval_seconds integer NOT NULL,
    etag text NULL,
    last_modified timestamptz NULL,
    next_poll_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    consecutive_failures integer NOT NULL DEFAULT 0,
    last_success_at timestamptz NULL,
    last_failure_at timestamptz NULL,
    last_error text NULL,
    quarantine_until timestamptz NULL,
    lease_token uuid NULL,
    lease_expires_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_feed_sources_name_length
        CHECK (char_length(name) BETWEEN 1 AND 200),
    CONSTRAINT ck_feed_sources_url_length
        CHECK (char_length(feed_url) BETWEEN 1 AND 4096),
    CONSTRAINT ck_feed_sources_polling_interval
        CHECK (polling_interval_seconds BETWEEN 60 AND 86400),
    CONSTRAINT ck_feed_sources_consecutive_failures
        CHECK (consecutive_failures >= 0),
    CONSTRAINT ck_feed_sources_etag_length
        CHECK (etag IS NULL OR char_length(etag) <= 2000),
    CONSTRAINT ck_feed_sources_last_error_length
        CHECK (last_error IS NULL OR char_length(last_error) <= 2000)
);

CREATE UNIQUE INDEX ux_feed_sources_feed_url
    ON feed_sources (feed_url);

CREATE INDEX ix_feed_sources_due
    ON feed_sources (next_poll_at)
    WHERE enabled;

CREATE INDEX ix_feed_sources_expired_lease
    ON feed_sources (lease_expires_at)
    WHERE lease_token IS NOT NULL;
