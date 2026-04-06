-- ═══════════════════════════════════════════════════════════════════════════
-- MallX Phase 3 — Restaurant + Booking + Ratings
-- يُشغَّل بعد phase2_commission_payments.sql
-- ═══════════════════════════════════════════════════════════════════════════

BEGIN;

-- ═══════════════════════════════════════════
--  RESTAURANT MODULE
-- ═══════════════════════════════════════════

-- ─── MENU CATEGORIES ──────────────────────────────────────────────────────
CREATE TABLE menu_categories (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    store_id    UUID         NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name        VARCHAR(100) NOT NULL,
    name_ar     VARCHAR(100),
    icon        VARCHAR(50),
    sort_order  INTEGER      DEFAULT 0,
    is_active   BOOLEAN      DEFAULT TRUE,
    created_at  TIMESTAMPTZ  DEFAULT NOW()
);
CREATE INDEX idx_menu_cats_store ON menu_categories(store_id, is_active);

-- ─── MENU ITEMS ───────────────────────────────────────────────────────────
CREATE TABLE menu_items (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    store_id        UUID          NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    category_id     UUID          REFERENCES menu_categories(id),
    name            VARCHAR(200)  NOT NULL,
    name_ar         VARCHAR(200),
    description     TEXT,
    description_ar  TEXT,
    price           NUMERIC(10,2) NOT NULL,
    image_url       TEXT,
    prep_time_min   INTEGER       DEFAULT 15,   -- وقت التحضير بالدقائق
    calories        INTEGER,
    is_available    BOOLEAN       DEFAULT TRUE,
    is_featured     BOOLEAN       DEFAULT FALSE,
    sort_order      INTEGER       DEFAULT 0,
    tags            TEXT[],                      -- ['spicy','vegetarian','new']
    is_deleted      BOOLEAN       DEFAULT FALSE,
    created_at      TIMESTAMPTZ   DEFAULT NOW(),
    updated_at      TIMESTAMPTZ   DEFAULT NOW()
);
CREATE INDEX idx_menu_items_store    ON menu_items(store_id, is_available, is_deleted);
CREATE INDEX idx_menu_items_category ON menu_items(category_id);

-- ─── MENU ITEM OPTIONS (Customization) ────────────────────────────────────
CREATE TABLE menu_item_options (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    item_id     UUID         NOT NULL REFERENCES menu_items(id) ON DELETE CASCADE,
    name        VARCHAR(100) NOT NULL,       -- e.g. "الحجم"
    is_required BOOLEAN      DEFAULT FALSE,
    choices     JSONB        NOT NULL        -- [{"label":"صغير","price":0}, ...]
);

-- ─── QUEUE TICKETS ────────────────────────────────────────────────────────
CREATE TYPE ticket_status AS ENUM ('Waiting','Preparing','Ready','Collected','Cancelled');

CREATE TABLE queue_tickets (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    store_order_id  UUID          NOT NULL REFERENCES store_orders(id),
    store_id        UUID          NOT NULL REFERENCES tenants(id),
    ticket_number   INTEGER       NOT NULL,
    status          ticket_status DEFAULT 'Waiting',
    estimated_ready TIMESTAMPTZ,
    ready_at        TIMESTAMPTZ,
    collected_at    TIMESTAMPTZ,
    created_at      TIMESTAMPTZ   DEFAULT NOW(),
    UNIQUE(store_id, ticket_number)
);
CREATE INDEX idx_queue_store ON queue_tickets(store_id, status);

-- ─── Daily ticket counter per store ───────────────────────────────────────
CREATE TABLE queue_daily_counter (
    store_id    UUID NOT NULL REFERENCES tenants(id),
    date        DATE NOT NULL DEFAULT CURRENT_DATE,
    last_ticket INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (store_id, date)
);

-- ═══════════════════════════════════════════
--  BOOKING MODULE
-- ═══════════════════════════════════════════

-- ─── SERVICE STAFF ────────────────────────────────────────────────────────
CREATE TABLE service_staff (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    store_id    UUID         NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name        VARCHAR(200) NOT NULL,
    specialty   VARCHAR(100),
    avatar_url  TEXT,
    is_active   BOOLEAN      DEFAULT TRUE,
    created_at  TIMESTAMPTZ  DEFAULT NOW()
);
CREATE INDEX idx_staff_store ON service_staff(store_id, is_active);

-- ─── SERVICES (ما يقدمه المحل) ───────────────────────────────────────────
CREATE TABLE services (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    store_id        UUID          NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name            VARCHAR(200)  NOT NULL,
    description     TEXT,
    duration_min    INTEGER       NOT NULL DEFAULT 30,    -- مدة الخدمة
    price           NUMERIC(10,2) NOT NULL,
    image_url       TEXT,
    is_active       BOOLEAN       DEFAULT TRUE,
    created_at      TIMESTAMPTZ   DEFAULT NOW(),
    updated_at      TIMESTAMPTZ   DEFAULT NOW()
);
CREATE INDEX idx_services_store ON services(store_id, is_active);

-- ─── STAFF_SERVICES (من يقدم ماذا) ───────────────────────────────────────
CREATE TABLE staff_services (
    staff_id    UUID NOT NULL REFERENCES service_staff(id) ON DELETE CASCADE,
    service_id  UUID NOT NULL REFERENCES services(id)      ON DELETE CASCADE,
    PRIMARY KEY (staff_id, service_id)
);

-- ─── WORKING HOURS ────────────────────────────────────────────────────────
CREATE TABLE working_hours (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    staff_id    UUID         NOT NULL REFERENCES service_staff(id) ON DELETE CASCADE,
    day_of_week SMALLINT     NOT NULL CHECK (day_of_week BETWEEN 0 AND 6), -- 0=Sun
    start_time  TIME         NOT NULL,
    end_time    TIME         NOT NULL,
    is_active   BOOLEAN      DEFAULT TRUE,
    UNIQUE(staff_id, day_of_week)
);

-- ─── BOOKINGS ─────────────────────────────────────────────────────────────
CREATE TYPE booking_status AS ENUM (
    'Pending','Confirmed','InProgress','Completed','Cancelled','NoShow'
);

CREATE TABLE bookings (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    store_id        UUID          NOT NULL REFERENCES tenants(id),
    customer_id     UUID          NOT NULL REFERENCES mall_customers(id),
    service_id      UUID          NOT NULL REFERENCES services(id),
    staff_id        UUID          REFERENCES service_staff(id),
    booking_ref     VARCHAR(20)   UNIQUE NOT NULL,
    status          booking_status DEFAULT 'Pending',
    booked_date     DATE          NOT NULL,
    start_time      TIME          NOT NULL,
    end_time        TIME          NOT NULL,
    price           NUMERIC(10,2) NOT NULL,
    notes           TEXT,
    reminder_sent   BOOLEAN       DEFAULT FALSE,
    confirmed_at    TIMESTAMPTZ,
    completed_at    TIMESTAMPTZ,
    cancelled_at    TIMESTAMPTZ,
    cancel_reason   TEXT,
    created_at      TIMESTAMPTZ   DEFAULT NOW(),
    updated_at      TIMESTAMPTZ   DEFAULT NOW()
);
CREATE INDEX idx_bookings_customer ON bookings(customer_id, booked_date DESC);
CREATE INDEX idx_bookings_store    ON bookings(store_id, booked_date, status);
CREATE INDEX idx_bookings_staff    ON bookings(staff_id, booked_date);
CREATE INDEX idx_bookings_ref      ON bookings(booking_ref);

-- ═══════════════════════════════════════════
--  RATINGS MODULE
-- ═══════════════════════════════════════════

CREATE TYPE rating_subject AS ENUM ('Store','Delivery','Overall','MenuItem');

CREATE TABLE ratings (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_order_id   UUID          REFERENCES mall_orders(id),
    booking_id      UUID          REFERENCES bookings(id),
    customer_id     UUID          NOT NULL REFERENCES mall_customers(id),
    store_id        UUID          NOT NULL REFERENCES tenants(id),
    subject         rating_subject NOT NULL DEFAULT 'Store',
    subject_id      UUID,                       -- e.g. menu_item_id
    stars           SMALLINT      NOT NULL CHECK (stars BETWEEN 1 AND 5),
    title           VARCHAR(100),
    body            TEXT,
    images          TEXT[],                     -- URLs
    is_anonymous    BOOLEAN       DEFAULT FALSE,
    is_published    BOOLEAN       DEFAULT TRUE,
    store_reply     TEXT,
    store_replied_at TIMESTAMPTZ,
    created_at      TIMESTAMPTZ   DEFAULT NOW(),
    -- واحد rating لكل طلب لكل محل
    UNIQUE(mall_order_id, customer_id, store_id, subject)
);
CREATE INDEX idx_ratings_store    ON ratings(store_id, is_published, created_at DESC);
CREATE INDEX idx_ratings_customer ON ratings(customer_id, created_at DESC);
CREATE INDEX idx_ratings_order    ON ratings(mall_order_id);

-- Store rating summary (materialized view بسيطة)
CREATE TABLE store_rating_summary (
    store_id      UUID PRIMARY KEY REFERENCES tenants(id),
    avg_stars     NUMERIC(3,2) DEFAULT 0,
    total_ratings INTEGER      DEFAULT 0,
    five_star     INTEGER      DEFAULT 0,
    four_star     INTEGER      DEFAULT 0,
    three_star    INTEGER      DEFAULT 0,
    two_star      INTEGER      DEFAULT 0,
    one_star      INTEGER      DEFAULT 0,
    updated_at    TIMESTAMPTZ  DEFAULT NOW()
);

-- Auto-update rating summary trigger
CREATE OR REPLACE FUNCTION update_store_rating_summary()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO store_rating_summary (store_id, avg_stars, total_ratings,
        five_star, four_star, three_star, two_star, one_star)
    SELECT
        NEW.store_id,
        ROUND(AVG(stars)::NUMERIC, 2),
        COUNT(*),
        COUNT(*) FILTER (WHERE stars=5),
        COUNT(*) FILTER (WHERE stars=4),
        COUNT(*) FILTER (WHERE stars=3),
        COUNT(*) FILTER (WHERE stars=2),
        COUNT(*) FILTER (WHERE stars=1)
    FROM ratings WHERE store_id = NEW.store_id AND is_published = TRUE
    ON CONFLICT (store_id) DO UPDATE SET
        avg_stars     = EXCLUDED.avg_stars,
        total_ratings = EXCLUDED.total_ratings,
        five_star     = EXCLUDED.five_star,
        four_star     = EXCLUDED.four_star,
        three_star    = EXCLUDED.three_star,
        two_star      = EXCLUDED.two_star,
        one_star      = EXCLUDED.one_star,
        updated_at    = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_update_rating_summary
AFTER INSERT OR UPDATE ON ratings
FOR EACH ROW EXECUTE FUNCTION update_store_rating_summary();

COMMIT;
