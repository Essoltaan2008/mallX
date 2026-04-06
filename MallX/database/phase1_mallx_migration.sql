-- ═══════════════════════════════════════════════════════════════════════════
-- MallX Phase 1 — Migration SQL
-- يُشغَّل فوق قاعدة بيانات MesterXPro الحالية
-- ═══════════════════════════════════════════════════════════════════════════

BEGIN;

-- ─── 1. ENUMs جديدة ────────────────────────────────────────────────────────
CREATE TYPE store_type         AS ENUM ('Restaurant','Retail','Service');
CREATE TYPE fulfillment_type   AS ENUM ('Delivery','Pickup','InStore');
CREATE TYPE mall_order_status  AS ENUM ('Placed','Confirmed','Preparing','Ready','PickedUp','Delivered','Cancelled');
CREATE TYPE store_order_status AS ENUM ('Placed','Confirmed','Preparing','Ready','Cancelled');
CREATE TYPE customer_tier      AS ENUM ('Bronze','Silver','Gold');

-- ─── 2. جدول MALLS ────────────────────────────────────────────────────────
CREATE TABLE malls (
    id             UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name           VARCHAR(200)     NOT NULL,
    name_ar        VARCHAR(200),
    slug           VARCHAR(100)     UNIQUE NOT NULL,
    address        TEXT,
    geo_lat        NUMERIC(10,7),
    geo_lng        NUMERIC(10,7),
    geo_radius_m   INTEGER          DEFAULT 200,       -- geo-fence radius
    logo_url       TEXT,
    cover_url      TEXT,
    phone          VARCHAR(20),
    email          VARCHAR(150),
    opening_hours  JSONB,                              -- {"sat":"09:00-22:00", ...}
    is_active      BOOLEAN          DEFAULT TRUE,
    created_at     TIMESTAMPTZ      DEFAULT NOW(),
    updated_at     TIMESTAMPTZ      DEFAULT NOW()
);
CREATE INDEX idx_malls_slug ON malls(slug);

-- ─── 3. تعديل tenants ← يصبح Stores ──────────────────────────────────────
ALTER TABLE tenants
    ADD COLUMN IF NOT EXISTS mall_id      UUID         REFERENCES malls(id),
    ADD COLUMN IF NOT EXISTS store_type   store_type   DEFAULT 'Retail',
    ADD COLUMN IF NOT EXISTS floor_number INTEGER      DEFAULT 1,
    ADD COLUMN IF NOT EXISTS store_qr     TEXT,
    ADD COLUMN IF NOT EXISTS commission   NUMERIC(5,4) DEFAULT 0.05;

CREATE INDEX idx_tenants_mall ON tenants(mall_id) WHERE mall_id IS NOT NULL;

-- ─── 4. MALL_CUSTOMERS (B2C — مستقل عن B2B users) ─────────────────────────
CREATE TABLE mall_customers (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id             UUID         NOT NULL REFERENCES malls(id),
    first_name          VARCHAR(100) NOT NULL,
    last_name           VARCHAR(100) NOT NULL,
    email               VARCHAR(150) UNIQUE NOT NULL,
    phone               VARCHAR(20)  UNIQUE,
    password_hash       VARCHAR(255) NOT NULL,
    avatar_url          TEXT,
    loyalty_points      INTEGER      DEFAULT 0,
    tier                customer_tier DEFAULT 'Bronze',
    tier_updated_at     TIMESTAMPTZ  DEFAULT NOW(),
    last_activity_at    TIMESTAMPTZ  DEFAULT NOW(),
    failed_attempts     INTEGER      DEFAULT 0,
    lockout_end         TIMESTAMPTZ,
    is_active           BOOLEAN      DEFAULT TRUE,
    is_deleted          BOOLEAN      DEFAULT FALSE,
    created_at          TIMESTAMPTZ  DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  DEFAULT NOW()
);
CREATE INDEX idx_mall_customers_email ON mall_customers(email);
CREATE INDEX idx_mall_customers_mall  ON mall_customers(mall_id, tier);

-- ─── 5. CUSTOMER_ADDRESSES ─────────────────────────────────────────────────
CREATE TABLE customer_addresses (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    customer_id UUID         NOT NULL REFERENCES mall_customers(id) ON DELETE CASCADE,
    label       VARCHAR(50)  DEFAULT 'Home',           -- Home | Work | Other
    address     TEXT         NOT NULL,
    geo_lat     NUMERIC(10,7),
    geo_lng     NUMERIC(10,7),
    is_default  BOOLEAN      DEFAULT FALSE,
    created_at  TIMESTAMPTZ  DEFAULT NOW()
);

-- ─── 6. CUSTOMER_REFRESH_TOKENS ────────────────────────────────────────────
CREATE TABLE customer_refresh_tokens (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    customer_id UUID         NOT NULL REFERENCES mall_customers(id) ON DELETE CASCADE,
    token_hash  VARCHAR(255) NOT NULL,
    token_salt  VARCHAR(100) NOT NULL,
    device_info VARCHAR(250),
    ip_address  VARCHAR(50),
    expires_at  TIMESTAMPTZ  NOT NULL,
    is_revoked  BOOLEAN      DEFAULT FALSE,
    created_at  TIMESTAMPTZ  DEFAULT NOW()
);
CREATE INDEX idx_crt_customer ON customer_refresh_tokens(customer_id, is_revoked);

-- ─── 7. CARTS ──────────────────────────────────────────────────────────────
CREATE TABLE carts (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    customer_id UUID         NOT NULL REFERENCES mall_customers(id) ON DELETE CASCADE,
    mall_id     UUID         NOT NULL REFERENCES malls(id),
    expires_at  TIMESTAMPTZ  DEFAULT NOW() + INTERVAL '7 days',
    created_at  TIMESTAMPTZ  DEFAULT NOW(),
    updated_at  TIMESTAMPTZ  DEFAULT NOW(),
    UNIQUE(customer_id)                              -- واحد سلة لكل عميل
);

-- ─── 8. CART_ITEMS ─────────────────────────────────────────────────────────
CREATE TABLE cart_items (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    cart_id     UUID            NOT NULL REFERENCES carts(id) ON DELETE CASCADE,
    store_id    UUID            NOT NULL REFERENCES tenants(id),
    product_id  UUID            NOT NULL REFERENCES products(id),
    quantity    INTEGER         NOT NULL DEFAULT 1 CHECK (quantity > 0),
    unit_price  NUMERIC(12,2)   NOT NULL,
    notes       TEXT,                                -- طلبات خاصة
    item_type   VARCHAR(20)     DEFAULT 'Product',   -- Product | Food | Service
    created_at  TIMESTAMPTZ     DEFAULT NOW(),
    updated_at  TIMESTAMPTZ     DEFAULT NOW(),
    UNIQUE(cart_id, product_id)
);
CREATE INDEX idx_cart_items_cart  ON cart_items(cart_id);
CREATE INDEX idx_cart_items_store ON cart_items(store_id);

-- ─── 9. MALL_ORDERS ────────────────────────────────────────────────────────
CREATE TABLE mall_orders (
    id               UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    customer_id      UUID             NOT NULL REFERENCES mall_customers(id),
    mall_id          UUID             NOT NULL REFERENCES malls(id),
    order_number     VARCHAR(30)      UNIQUE NOT NULL,
    status           mall_order_status DEFAULT 'Placed',
    fulfillment_type fulfillment_type  DEFAULT 'Delivery',
    subtotal         NUMERIC(12,2)    NOT NULL DEFAULT 0,
    delivery_fee     NUMERIC(12,2)    DEFAULT 0,
    discount_amount  NUMERIC(12,2)    DEFAULT 0,
    total            NUMERIC(12,2)    NOT NULL DEFAULT 0,
    payment_method   payment_method   DEFAULT 'Cash',
    delivery_address TEXT,
    delivery_lat     NUMERIC(10,7),
    delivery_lng     NUMERIC(10,7),
    notes            TEXT,
    placed_at        TIMESTAMPTZ      DEFAULT NOW(),
    delivered_at     TIMESTAMPTZ,
    created_at       TIMESTAMPTZ      DEFAULT NOW(),
    updated_at       TIMESTAMPTZ      DEFAULT NOW()
);
CREATE INDEX idx_mall_orders_customer ON mall_orders(customer_id, created_at DESC);
CREATE INDEX idx_mall_orders_mall     ON mall_orders(mall_id, status);
CREATE INDEX idx_mall_orders_number   ON mall_orders(order_number);

-- ─── 10. STORE_ORDERS (السلة المفككة لكل محل) ─────────────────────────────
CREATE TABLE store_orders (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_order_id   UUID              NOT NULL REFERENCES mall_orders(id),
    store_id        UUID              NOT NULL REFERENCES tenants(id),
    status          store_order_status DEFAULT 'Placed',
    subtotal        NUMERIC(12,2)    NOT NULL DEFAULT 0,
    commission_rate NUMERIC(5,4)     NOT NULL DEFAULT 0.05,
    commission_amt  NUMERIC(12,2)    NOT NULL DEFAULT 0,
    store_total     NUMERIC(12,2)    NOT NULL DEFAULT 0,
    notes           TEXT,
    confirmed_at    TIMESTAMPTZ,
    ready_at        TIMESTAMPTZ,
    created_at      TIMESTAMPTZ      DEFAULT NOW(),
    updated_at      TIMESTAMPTZ      DEFAULT NOW()
);
CREATE INDEX idx_store_orders_mall_order ON store_orders(mall_order_id);
CREATE INDEX idx_store_orders_store      ON store_orders(store_id, status, created_at DESC);

-- ─── 11. STORE_ORDER_ITEMS ─────────────────────────────────────────────────
CREATE TABLE store_order_items (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    store_order_id  UUID          NOT NULL REFERENCES store_orders(id) ON DELETE CASCADE,
    product_id      UUID          NOT NULL REFERENCES products(id),
    product_name    VARCHAR(200)  NOT NULL,
    quantity        INTEGER       NOT NULL,
    unit_price      NUMERIC(12,2) NOT NULL,
    notes           TEXT,
    total           NUMERIC(12,2) NOT NULL
);

-- ─── 12. ORDER_STATUS_HISTORY ──────────────────────────────────────────────
CREATE TABLE order_status_history (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_order_id   UUID        NOT NULL REFERENCES mall_orders(id),
    store_order_id  UUID        REFERENCES store_orders(id),
    old_status      VARCHAR(30),
    new_status      VARCHAR(30) NOT NULL,
    note            TEXT,
    changed_by      UUID        REFERENCES users(id),
    created_at      TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_osh_mall_order ON order_status_history(mall_order_id, created_at);

-- ─── 13. SEED: Default Mall ────────────────────────────────────────────────
INSERT INTO malls (name, name_ar, slug, address, geo_radius_m, is_active) VALUES
('MallX Demo', 'مول اكس تجريبي', 'mallx-demo', 'القاهرة — مصر', 200, TRUE);

-- ─── 14. Indexes إضافية للـ Performance ───────────────────────────────────
CREATE INDEX idx_mall_orders_status ON mall_orders(status) WHERE status != 'Delivered';
CREATE INDEX idx_store_orders_pending ON store_orders(store_id, status)
    WHERE status IN ('Placed','Confirmed','Preparing');

COMMIT;

-- ═══════════════════════════════════════════════════════════════════════════
-- للتحقق من نجاح الـ migration:
-- SELECT table_name FROM information_schema.tables
--   WHERE table_schema = 'public' AND table_name IN
--   ('malls','mall_customers','carts','cart_items','mall_orders','store_orders');
-- ═══════════════════════════════════════════════════════════════════════════
