-- ═══════════════════════════════════════════════════════════════════════════
-- MallX Phase 2 — Commission + Payments + Delivery
-- يُشغَّل بعد phase1_mallx_migration.sql
-- ═══════════════════════════════════════════════════════════════════════════

BEGIN;

-- ─── 1. PAYMENT_TRANSACTIONS ───────────────────────────────────────────────
CREATE TYPE payment_status AS ENUM ('Pending','Completed','Failed','Refunded','PartialRefund');
CREATE TYPE payment_gateway AS ENUM ('Cash','Paymob','Fawry','VodafoneCash','Internal');

CREATE TABLE payment_transactions (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_order_id   UUID          NOT NULL REFERENCES mall_orders(id),
    customer_id     UUID          NOT NULL REFERENCES mall_customers(id),
    amount          NUMERIC(12,2) NOT NULL,
    currency        VARCHAR(5)    DEFAULT 'EGP',
    gateway         payment_gateway DEFAULT 'Cash',
    status          payment_status  DEFAULT 'Pending',
    gateway_order_id VARCHAR(100),          -- Paymob order ID
    gateway_txn_id   VARCHAR(100),          -- Paymob transaction ID
    gateway_response JSONB,                 -- raw gateway response
    failure_reason   TEXT,
    paid_at          TIMESTAMPTZ,
    refunded_at      TIMESTAMPTZ,
    refund_amount    NUMERIC(12,2) DEFAULT 0,
    created_at       TIMESTAMPTZ DEFAULT NOW(),
    updated_at       TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_payment_txn_order    ON payment_transactions(mall_order_id);
CREATE INDEX idx_payment_txn_customer ON payment_transactions(customer_id, created_at DESC);
CREATE INDEX idx_payment_txn_gateway  ON payment_transactions(gateway_txn_id)
    WHERE gateway_txn_id IS NOT NULL;

-- ─── 2. COMMISSION_SETTLEMENTS ────────────────────────────────────────────
CREATE TYPE settlement_status AS ENUM ('Pending','Processing','Completed','Failed');

CREATE TABLE commission_settlements (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id         UUID          NOT NULL REFERENCES malls(id),
    store_id        UUID          NOT NULL REFERENCES tenants(id),
    period_start    TIMESTAMPTZ   NOT NULL,
    period_end      TIMESTAMPTZ   NOT NULL,
    total_orders    INTEGER       DEFAULT 0,
    gross_revenue   NUMERIC(12,2) DEFAULT 0,
    commission_rate NUMERIC(5,4)  DEFAULT 0.05,
    commission_amt  NUMERIC(12,2) DEFAULT 0,
    net_payable     NUMERIC(12,2) DEFAULT 0,   -- للمحل
    status          settlement_status DEFAULT 'Pending',
    settled_at      TIMESTAMPTZ,
    notes           TEXT,
    created_by      UUID REFERENCES users(id),
    created_at      TIMESTAMPTZ DEFAULT NOW(),
    updated_at      TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_settlements_store ON commission_settlements(store_id, period_start DESC);
CREATE INDEX idx_settlements_mall  ON commission_settlements(mall_id, status);

-- ─── 3. DELIVERY_ZONES ────────────────────────────────────────────────────
CREATE TABLE delivery_zones (
    id            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id       UUID          NOT NULL REFERENCES malls(id),
    name          VARCHAR(100)  NOT NULL,
    description   TEXT,
    fee           NUMERIC(8,2)  NOT NULL DEFAULT 15,
    min_order_fee NUMERIC(12,2) DEFAULT 0,       -- حد أدنى للطلب
    free_above    NUMERIC(12,2),                 -- توصيل مجاني فوق هذا
    polygon       JSONB,                         -- [{lat,lng}, ...] حدود المنطقة
    is_active     BOOLEAN DEFAULT TRUE,
    created_at    TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_delivery_zones_mall ON delivery_zones(mall_id, is_active);

-- ─── 4. DRIVERS (Phase 5 — placeholder for SignalR) ──────────────────────
CREATE TABLE drivers (
    id            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id       UUID         NOT NULL REFERENCES malls(id),
    name          VARCHAR(200) NOT NULL,
    phone         VARCHAR(20)  NOT NULL,
    vehicle_type  VARCHAR(50)  DEFAULT 'Motorcycle',
    vehicle_plate VARCHAR(20),
    current_lat   NUMERIC(10,7),
    current_lng   NUMERIC(10,7),
    is_available  BOOLEAN DEFAULT TRUE,
    is_active     BOOLEAN DEFAULT TRUE,
    last_seen_at  TIMESTAMPTZ,
    created_at    TIMESTAMPTZ DEFAULT NOW(),
    updated_at    TIMESTAMPTZ DEFAULT NOW()
);

-- ─── 5. DELIVERY_ASSIGNMENTS ──────────────────────────────────────────────
CREATE TABLE delivery_assignments (
    id            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_order_id UUID NOT NULL REFERENCES mall_orders(id),
    driver_id     UUID REFERENCES drivers(id),
    assigned_at   TIMESTAMPTZ,
    picked_up_at  TIMESTAMPTZ,
    delivered_at  TIMESTAMPTZ,
    distance_km   NUMERIC(8,2),
    status        VARCHAR(30) DEFAULT 'Pending',   -- Pending|Assigned|PickedUp|Delivered
    notes         TEXT,
    created_at    TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_delivery_assignment_order  ON delivery_assignments(mall_order_id);
CREATE INDEX idx_delivery_assignment_driver ON delivery_assignments(driver_id, status);

-- ─── 6. MALL_ANALYTICS (daily snapshots) ─────────────────────────────────
CREATE TABLE mall_analytics_daily (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id         UUID         NOT NULL REFERENCES malls(id),
    snapshot_date   DATE         NOT NULL,
    total_orders    INTEGER      DEFAULT 0,
    total_revenue   NUMERIC(14,2)DEFAULT 0,
    total_commission NUMERIC(14,2)DEFAULT 0,
    new_customers   INTEGER      DEFAULT 0,
    active_stores   INTEGER      DEFAULT 0,
    avg_order_value NUMERIC(12,2)DEFAULT 0,
    created_at      TIMESTAMPTZ  DEFAULT NOW(),
    UNIQUE(mall_id, snapshot_date)
);
CREATE INDEX idx_analytics_mall_date ON mall_analytics_daily(mall_id, snapshot_date DESC);

-- ─── 7. Extend mall_orders with payment fields ────────────────────────────
ALTER TABLE mall_orders
    ADD COLUMN IF NOT EXISTS payment_status  VARCHAR(20) DEFAULT 'Pending',
    ADD COLUMN IF NOT EXISTS payment_txn_id  UUID REFERENCES payment_transactions(id),
    ADD COLUMN IF NOT EXISTS delivery_zone_id UUID REFERENCES delivery_zones(id),
    ADD COLUMN IF NOT EXISTS driver_id        UUID REFERENCES drivers(id);

-- ─── 8. Seed default delivery zone ───────────────────────────────────────
INSERT INTO delivery_zones (mall_id, name, description, fee, free_above, is_active)
SELECT id, 'المنطقة الافتراضية', 'نطاق التوصيل الرئيسي', 15.00, 200.00, TRUE
FROM malls WHERE slug = 'mallx-demo';

COMMIT;
