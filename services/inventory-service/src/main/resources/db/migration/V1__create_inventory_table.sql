CREATE TABLE inventory (
                           product_id BIGINT PRIMARY KEY,
                           available_quantity INTEGER NOT NULL,
                           CONSTRAINT chk_inventory_quantity_nonnegative
                               CHECK (available_quantity >= 0)
);

INSERT INTO inventory (product_id, available_quantity)
VALUES
    (1, 100),
    (2, 50),
    (3, 0);