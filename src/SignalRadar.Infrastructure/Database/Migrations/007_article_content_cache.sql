CREATE TABLE article_content_cache (
    article_id uuid PRIMARY KEY REFERENCES articles(id) ON DELETE CASCADE,
    canonical_url text NOT NULL,
    status smallint NOT NULL,
    content_text text,
    content_hash character(64),
    fetched_at timestamptz NOT NULL,
    refresh_after timestamptz NOT NULL,
    http_status smallint,
    content_type varchar(200),
    detail varchar(500),
    CONSTRAINT ck_article_content_cache_status CHECK (status BETWEEN 1 AND 7),
    CONSTRAINT ck_article_content_cache_refresh CHECK (refresh_after >= fetched_at),
    CONSTRAINT ck_article_content_cache_http_status CHECK (
        http_status IS NULL OR http_status BETWEEN 100 AND 599),
    CONSTRAINT ck_article_content_cache_payload CHECK (
        (status = 1 AND content_text IS NOT NULL AND content_hash IS NOT NULL)
        OR (status <> 1 AND content_text IS NULL AND content_hash IS NULL))
);

CREATE INDEX ix_article_content_cache_refresh_after
    ON article_content_cache (refresh_after);
