CREATE TABLE articles (
    id uuid PRIMARY KEY,
    canonical_url text NOT NULL,
    title text NOT NULL,
    source text NOT NULL,
    published_at timestamptz NOT NULL,
    collected_at timestamptz NOT NULL,
    CONSTRAINT ck_articles_canonical_url_length
        CHECK (char_length(canonical_url) BETWEEN 1 AND 4096),
    CONSTRAINT ck_articles_title_length
        CHECK (char_length(title) BETWEEN 1 AND 1000),
    CONSTRAINT ck_articles_source_length
        CHECK (char_length(source) BETWEEN 1 AND 200)
);

CREATE UNIQUE INDEX ux_articles_canonical_url
    ON articles (canonical_url);

CREATE INDEX ix_articles_published_at
    ON articles (published_at DESC);

CREATE TABLE discord_message_receipts (
    message_id numeric(20, 0) PRIMARY KEY,
    lease_token uuid NOT NULL,
    lease_expires_at timestamptz NOT NULL,
    completed_at timestamptz NULL,
    CONSTRAINT ck_discord_message_receipts_message_id
        CHECK (message_id >= 0)
);

CREATE INDEX ix_discord_message_receipts_incomplete_lease
    ON discord_message_receipts (lease_expires_at)
    WHERE completed_at IS NULL;
