CREATE TABLE title_translation_cache (
    source_hash char(64) NOT NULL,
    target_language varchar(16) NOT NULL,
    provider varchar(100) NOT NULL,
    translated_title text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (source_hash, target_language, provider),
    CONSTRAINT ck_title_translation_cache_hash
        CHECK (source_hash ~ '^[0-9A-F]{64}$'),
    CONSTRAINT ck_title_translation_cache_language
        CHECK (char_length(target_language) BETWEEN 2 AND 16),
    CONSTRAINT ck_title_translation_cache_provider
        CHECK (char_length(provider) BETWEEN 1 AND 100),
    CONSTRAINT ck_title_translation_cache_title
        CHECK (char_length(translated_title) BETWEEN 1 AND 1000)
);

CREATE TABLE actor_muted_sources (
    actor_id varchar(200) NOT NULL,
    source varchar(200) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (actor_id, source),
    CONSTRAINT ck_actor_muted_sources_actor
        CHECK (char_length(actor_id) BETWEEN 1 AND 200),
    CONSTRAINT ck_actor_muted_sources_source
        CHECK (char_length(source) BETWEEN 1 AND 200)
);

CREATE TABLE discord_topic_routes (
    topic integer PRIMARY KEY,
    channel_id numeric(20, 0) NOT NULL,
    minimum_score numeric(5, 2) NOT NULL DEFAULT 0,
    batch_window_seconds integer NOT NULL DEFAULT 1800,
    enabled boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_discord_topic_routes_topic CHECK (topic > 0),
    CONSTRAINT ck_discord_topic_routes_channel CHECK (channel_id > 0),
    CONSTRAINT ck_discord_topic_routes_min_score
        CHECK (minimum_score BETWEEN 0 AND 100),
    CONSTRAINT ck_discord_topic_routes_batch_window
        CHECK (batch_window_seconds BETWEEN 300 AND 86400)
);

CREATE INDEX ix_discord_topic_routes_enabled
    ON discord_topic_routes (enabled, topic);

CREATE TABLE discord_topic_batch_state (
    singleton boolean PRIMARY KEY DEFAULT true,
    activated_at timestamptz NOT NULL,
    CONSTRAINT ck_discord_topic_batch_state_singleton CHECK (singleton)
);
