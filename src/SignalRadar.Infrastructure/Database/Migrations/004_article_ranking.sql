ALTER TABLE articles
    ADD COLUMN topics integer NOT NULL DEFAULT 128,
    ADD COLUMN primary_topic smallint NOT NULL DEFAULT 128,
    ADD COLUMN source_trust smallint NOT NULL DEFAULT 50,
    ADD COLUMN topic_interest smallint NOT NULL DEFAULT 35,
    ADD COLUMN practical_impact smallint NOT NULL DEFAULT 40,
    ADD COLUMN freshness smallint NOT NULL DEFAULT 50,
    ADD COLUMN base_score numeric(5, 2) NOT NULL DEFAULT 43.50,
    ADD COLUMN ranking_profile_version varchar(100) NOT NULL DEFAULT 'unclassified';

ALTER TABLE articles
    ADD CONSTRAINT ck_articles_topics_positive CHECK (topics > 0),
    ADD CONSTRAINT ck_articles_primary_topic CHECK (
        primary_topic > 0
        AND (primary_topic & (primary_topic - 1)) = 0
        AND (topics & primary_topic) <> 0),
    ADD CONSTRAINT ck_articles_source_trust CHECK (source_trust BETWEEN 0 AND 100),
    ADD CONSTRAINT ck_articles_topic_interest CHECK (topic_interest BETWEEN 0 AND 100),
    ADD CONSTRAINT ck_articles_practical_impact CHECK (practical_impact BETWEEN 0 AND 100),
    ADD CONSTRAINT ck_articles_freshness CHECK (freshness BETWEEN 0 AND 100),
    ADD CONSTRAINT ck_articles_base_score CHECK (base_score BETWEEN 0 AND 100);

CREATE INDEX ix_articles_ranking
    ON articles (base_score DESC, published_at DESC);

CREATE INDEX ix_articles_topics
    ON articles (topics);

CREATE TABLE article_feedback (
    article_id uuid NOT NULL REFERENCES articles(id) ON DELETE CASCADE,
    actor_id varchar(200) NOT NULL,
    kind smallint NOT NULL,
    weight smallint NOT NULL,
    occurred_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (article_id, actor_id),
    CONSTRAINT ck_article_feedback_kind CHECK (kind IN (-3, -2, 3)),
    CONSTRAINT ck_article_feedback_weight CHECK (weight = kind)
);

CREATE INDEX ix_article_feedback_article_id
    ON article_feedback (article_id);
