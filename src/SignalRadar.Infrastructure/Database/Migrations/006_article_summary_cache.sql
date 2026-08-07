CREATE TABLE article_summary_cache (
    input_hash character(64) PRIMARY KEY,
    provider varchar(100) NOT NULL,
    model varchar(200) NOT NULL,
    prompt_version varchar(100) NOT NULL,
    language varchar(20) NOT NULL,
    article_count smallint NOT NULL,
    summary_json jsonb NOT NULL,
    provider_response_id varchar(200),
    generated_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_article_summary_cache_hash CHECK (
        input_hash ~ '^[0-9A-F]{64}$'),
    CONSTRAINT ck_article_summary_cache_count CHECK (
        article_count BETWEEN 1 AND 20)
);

CREATE INDEX ix_article_summary_cache_generated_at
    ON article_summary_cache (generated_at DESC);
