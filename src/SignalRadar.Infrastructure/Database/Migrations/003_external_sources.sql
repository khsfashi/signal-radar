ALTER TABLE articles
    ADD COLUMN external_id text NULL;

ALTER TABLE articles
    ADD CONSTRAINT ck_articles_external_id_length
    CHECK (external_id IS NULL OR char_length(external_id) BETWEEN 1 AND 500);

CREATE UNIQUE INDEX ux_articles_source_external_id
    ON articles (source, external_id)
    WHERE external_id IS NOT NULL;

CREATE TABLE external_sources (
    id uuid PRIMARY KEY,
    source_type text NOT NULL,
    source_key text NOT NULL,
    display_name text NOT NULL,
    endpoint_url text NOT NULL,
    settings_json jsonb NOT NULL DEFAULT '{}'::jsonb,
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
    CONSTRAINT uq_external_sources_type_key
        UNIQUE (source_type, source_key),
    CONSTRAINT ck_external_sources_source_type_length
        CHECK (char_length(source_type) BETWEEN 1 AND 100),
    CONSTRAINT ck_external_sources_source_key_length
        CHECK (char_length(source_key) BETWEEN 1 AND 500),
    CONSTRAINT ck_external_sources_display_name_length
        CHECK (char_length(display_name) BETWEEN 1 AND 300),
    CONSTRAINT ck_external_sources_endpoint_url_length
        CHECK (char_length(endpoint_url) BETWEEN 1 AND 4096),
    CONSTRAINT ck_external_sources_polling_interval
        CHECK (polling_interval_seconds BETWEEN 60 AND 86400),
    CONSTRAINT ck_external_sources_consecutive_failures
        CHECK (consecutive_failures >= 0)
);

CREATE INDEX ix_external_sources_due
    ON external_sources (next_poll_at, id)
    WHERE enabled;

CREATE INDEX ix_external_sources_incomplete_lease
    ON external_sources (lease_expires_at)
    WHERE lease_expires_at IS NOT NULL;
