CREATE TABLE article_saves (
    article_id uuid NOT NULL REFERENCES articles(id) ON DELETE CASCADE,
    actor_id varchar(200) NOT NULL,
    saved_at timestamptz NOT NULL,
    PRIMARY KEY (article_id, actor_id)
);

CREATE INDEX ix_article_saves_actor_saved_at
    ON article_saves (actor_id, saved_at DESC, article_id);
