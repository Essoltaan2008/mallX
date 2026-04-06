-- ═══════════════════════════════════════════════════════════════════════════
-- MallX Phase 6 — Mall Map + Search Optimization + Driver Assignment
-- ═══════════════════════════════════════════════════════════════════════════

BEGIN;

-- ═══════════════════════════════════════════════════════════════════════════
--  MALL MAP MODULE
-- ═══════════════════════════════════════════════════════════════════════════

-- ─── MALL FLOORS ─────────────────────────────────────────────────────────
CREATE TABLE mall_floors (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id     UUID         NOT NULL REFERENCES malls(id) ON DELETE CASCADE,
    floor_num   INTEGER      NOT NULL,
    name        VARCHAR(100) NOT NULL,
    name_ar     VARCHAR(100),
    map_svg_url TEXT,                   -- SVG floor plan
    width_m     NUMERIC(8,2),           -- physical dimensions
    height_m    NUMERIC(8,2),
    is_active   BOOLEAN DEFAULT TRUE,
    sort_order  INTEGER DEFAULT 0,
    created_at  TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(mall_id, floor_num)
);
CREATE INDEX idx_mall_floors ON mall_floors(mall_id, is_active);

-- ─── STORE LOCATIONS (on the map) ────────────────────────────────────────
CREATE TABLE store_locations (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    store_id    UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    floor_id    UUID NOT NULL REFERENCES mall_floors(id),
    pos_x       NUMERIC(8,2) NOT NULL,      -- position on floor SVG
    pos_y       NUMERIC(8,2) NOT NULL,
    width       NUMERIC(8,2) DEFAULT 60,
    height      NUMERIC(8,2) DEFAULT 40,
    shape       VARCHAR(20)  DEFAULT 'rect', -- rect | circle | polygon
    color       VARCHAR(20)  DEFAULT '#3B82F6',
    qr_code     TEXT,                        -- QR code content (URL)
    created_at  TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(store_id)
);
CREATE INDEX idx_store_locations_floor ON store_locations(floor_id);

-- ─── MAP AMENITIES (toilets, entrances, elevators, ATMs...) ─────────────
CREATE TABLE map_amenities (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    floor_id    UUID         NOT NULL REFERENCES mall_floors(id),
    type        VARCHAR(50)  NOT NULL,   -- Toilet | Entrance | Exit | Elevator | ATM | Prayer | Parking
    name        VARCHAR(100),
    name_ar     VARCHAR(100),
    pos_x       NUMERIC(8,2) NOT NULL,
    pos_y       NUMERIC(8,2) NOT NULL,
    icon        VARCHAR(50),
    created_at  TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_amenities_floor ON map_amenities(floor_id, type);

-- ═══════════════════════════════════════════════════════════════════════════
--  FULL-TEXT SEARCH INDEXES
-- ═══════════════════════════════════════════════════════════════════════════

-- Product search (name + description, Arabic + English)
ALTER TABLE products
    ADD COLUMN IF NOT EXISTS search_vector tsvector
    GENERATED ALWAYS AS (
        setweight(to_tsvector('simple', coalesce(name, '')), 'A') ||
        setweight(to_tsvector('simple', coalesce(description, '')), 'B')
    ) STORED;

CREATE INDEX idx_products_search ON products USING gin(search_vector);
CREATE INDEX idx_products_barcode_hash ON products USING hash(barcode)
    WHERE barcode IS NOT NULL;

-- Store search
ALTER TABLE tenants
    ADD COLUMN IF NOT EXISTS search_vector tsvector
    GENERATED ALWAYS AS (
        setweight(to_tsvector('simple', coalesce(name, '')), 'A')
    ) STORED;

CREATE INDEX idx_tenants_search ON tenants USING gin(search_vector);

-- Menu items search
ALTER TABLE menu_items
    ADD COLUMN IF NOT EXISTS search_vector tsvector
    GENERATED ALWAYS AS (
        setweight(to_tsvector('simple', coalesce(name, '')), 'A') ||
        setweight(to_tsvector('simple', coalesce(name_ar, '')), 'A') ||
        setweight(to_tsvector('simple', coalesce(description, '')), 'B')
    ) STORED;

CREATE INDEX idx_menu_items_search ON menu_items USING gin(search_vector);

-- ═══════════════════════════════════════════════════════════════════════════
--  DRIVER ASSIGNMENT + TRACKING LOG
-- ═══════════════════════════════════════════════════════════════════════════

-- Driver location history (lightweight — 24h rolling)
CREATE TABLE driver_location_history (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    driver_id   UUID          NOT NULL REFERENCES drivers(id),
    lat         NUMERIC(10,7) NOT NULL,
    lng         NUMERIC(10,7) NOT NULL,
    speed_kmh   NUMERIC(6,2),
    heading     NUMERIC(5,2),
    recorded_at TIMESTAMPTZ   DEFAULT NOW()
) PARTITION BY RANGE (recorded_at);

-- Today's partition
CREATE TABLE driver_location_history_today
    PARTITION OF driver_location_history
    FOR VALUES FROM (NOW() - INTERVAL '1 day') TO (NOW() + INTERVAL '1 day');

CREATE INDEX idx_driver_history ON driver_location_history (driver_id, recorded_at DESC);

-- Driver availability slots
CREATE TABLE driver_shifts (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    driver_id   UUID NOT NULL REFERENCES drivers(id),
    mall_id     UUID NOT NULL REFERENCES malls(id),
    shift_date  DATE NOT NULL,
    start_time  TIME NOT NULL,
    end_time    TIME NOT NULL,
    is_active   BOOLEAN DEFAULT TRUE,
    created_at  TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(driver_id, shift_date)
);
CREATE INDEX idx_driver_shifts ON driver_shifts(mall_id, shift_date, is_active);

-- ═══════════════════════════════════════════════════════════════════════════
--  CUSTOMER SEARCH HISTORY + RECOMMENDATIONS
-- ═══════════════════════════════════════════════════════════════════════════

CREATE TABLE customer_search_history (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    customer_id UUID NOT NULL REFERENCES mall_customers(id) ON DELETE CASCADE,
    mall_id     UUID NOT NULL REFERENCES malls(id),
    query       VARCHAR(200) NOT NULL,
    result_type VARCHAR(30),   -- Store | Product | MenuItem
    clicked_id  UUID,
    created_at  TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_search_history ON customer_search_history(customer_id, created_at DESC);

-- Trending searches per mall (aggregated)
CREATE TABLE trending_searches (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id     UUID         NOT NULL REFERENCES malls(id),
    query       VARCHAR(200) NOT NULL,
    search_count INTEGER     DEFAULT 1,
    date        DATE         DEFAULT CURRENT_DATE,
    UNIQUE(mall_id, query, date)
);
CREATE INDEX idx_trending ON trending_searches(mall_id, date DESC, search_count DESC);

-- ═══════════════════════════════════════════════════════════════════════════
--  SEED: Demo floor data
-- ═══════════════════════════════════════════════════════════════════════════
INSERT INTO mall_floors (mall_id, floor_num, name, name_ar, sort_order, is_active)
SELECT id, 0, 'Ground Floor', 'الدور الأرضي', 0, TRUE FROM malls WHERE slug = 'mallx-demo'
UNION ALL
SELECT id, 1, 'First Floor',  'الدور الأول',  1, TRUE FROM malls WHERE slug = 'mallx-demo'
UNION ALL
SELECT id, 2, 'Second Floor', 'الدور الثاني', 2, TRUE FROM malls WHERE slug = 'mallx-demo';

COMMIT;
