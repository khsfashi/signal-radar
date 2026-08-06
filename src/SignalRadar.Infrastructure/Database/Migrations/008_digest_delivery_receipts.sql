CREATE TABLE digest_delivery_receipts (
    delivery_key varchar(200) NOT NULL,
    window_start timestamptz NOT NULL,
    lease_token uuid NOT NULL,
    lease_expires_at timestamptz NOT NULL,
    delivered_at timestamptz NULL,
    discord_message_id numeric(20, 0) NULL,
    last_error varchar(1000) NULL,
    PRIMARY KEY (delivery_key, window_start),
    CONSTRAINT ck_digest_delivery_receipts_key_length
        CHECK (char_length(delivery_key) BETWEEN 1 AND 200),
    CONSTRAINT ck_digest_delivery_receipts_message_id
        CHECK (discord_message_id IS NULL OR discord_message_id >= 0)
);

CREATE INDEX ix_digest_delivery_receipts_pending_lease
    ON digest_delivery_receipts (lease_expires_at)
    WHERE delivered_at IS NULL;
