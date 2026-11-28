ALTER TABLE inventory ADD COLUMN version BIGINT NOT NULL DEFAULT 0;
CREATE TABLE inventory_reservation (
    reservation_id UUID PRIMARY KEY,
    status VARCHAR(16) NOT NULL CHECK (status IN ('RESERVED', 'RELEASED')),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE inventory_reservation_item (
    reservation_id UUID NOT NULL REFERENCES inventory_reservation(reservation_id),
    product_id BIGINT NOT NULL REFERENCES inventory(product_id),
    quantity INTEGER NOT NULL CHECK (quantity > 0),
    PRIMARY KEY (reservation_id, product_id)
);
