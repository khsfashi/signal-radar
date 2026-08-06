CREATE TABLE automatic_topic_publishing_state (
    singleton boolean PRIMARY KEY DEFAULT TRUE,
    activated_at timestamptz NOT NULL,
    CONSTRAINT ck_automatic_topic_publishing_state_singleton CHECK (singleton)
);

CREATE TABLE automatic_topic_publication_receipts (
    article_id uuid NOT NULL REFERENCES articles(id) ON DELETE CASCADE,
    channel_id numeric(20, 0) NOT NULL,
    publication_kind varchar(100) NOT NULL,
    lease_token uuid NOT NULL,
    lease_expires_at timestamptz NOT NULL,
    completed_at timestamptz NULL,
    discord_resource_id numeric(20, 0) NULL,
    attempt_count integer NOT NULL DEFAULT 1,
    last_failed_at timestamptz NULL,
    last_error varchar(1000) NULL,
    PRIMARY KEY (article_id, channel_id, publication_kind),
    CONSTRAINT ck_automatic_topic_publication_channel_id CHECK (channel_id >= 0),
    CONSTRAINT ck_automatic_topic_publication_resource_id CHECK (
        discord_resource_id IS NULL OR discord_resource_id >= 0),
    CONSTRAINT ck_automatic_topic_publication_attempt_count CHECK (attempt_count >= 1),
    CONSTRAINT ck_automatic_topic_publication_kind_length CHECK (
        char_length(publication_kind) BETWEEN 1 AND 100)
);

CREATE INDEX ix_automatic_topic_publication_pending
    ON automatic_topic_publication_receipts (channel_id, lease_expires_at)
    WHERE completed_at IS NULL;

CREATE INDEX ix_articles_automatic_topic_publication
    ON articles (primary_topic, collected_at, base_score DESC);
