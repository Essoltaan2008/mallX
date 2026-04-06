-- ═══════════════════════════════════════════════════════════════════════════
-- MallX Phase 4 — Loyalty + Promotions + Geo-Push
-- يُشغَّل بعد phase3
-- ═══════════════════════════════════════════════════════════════════════════

BEGIN;

-- ═══════════════════════════════════════════════
--  LOYALTY SYSTEM
-- ═══════════════════════════════════════════════

-- ─── LOYALTY ACCOUNTS ─────────────────────────────────────────────────────
-- ملاحظة: loyalty_points موجود في mall_customers — هنا نضيف الـ tier history
CREATE TABLE loyalty_accounts (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    customer_id         UUID NOT NULL REFERENCES mall_customers(id) ON DELETE CASCADE,
    mall_id             UUID NOT NULL REFERENCES malls(id),
    lifetime_points     INTEGER NOT NULL DEFAULT 0,    -- إجمالي النقاط المكتسبة تاريخياً
    redeemed_points     INTEGER NOT NULL DEFAULT 0,    -- ما تم استخدامه
    available_points    INTEGER GENERATED ALWAYS AS (lifetime_points - redeemed_points) STORED,
    tier                VARCHAR(20) NOT NULL DEFAULT 'Bronze',
    tier_updated_at     TIMESTAMPTZ DEFAULT NOW(),
    points_expire_at    TIMESTAMPTZ,                   -- 12 months inactivity
    created_at          TIMESTAMPTZ DEFAULT NOW(),
    updated_at          TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(customer_id, mall_id)
);
CREATE INDEX idx_loyalty_customer ON loyalty_accounts(customer_id);
CREATE INDEX idx_loyalty_tier     ON loyalty_accounts(mall_id, tier);

-- ─── POINTS TRANSACTIONS ─────────────────────────────────────────────────
CREATE TYPE points_source AS ENUM (
    'Purchase','Referral','Birthday','Rating','Signup',
    'Redemption','Adjustment','Expiry'
);

CREATE TABLE points_transactions (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    account_id      UUID          NOT NULL REFERENCES loyalty_accounts(id),
    customer_id     UUID          NOT NULL REFERENCES mall_customers(id),
    mall_order_id   UUID          REFERENCES mall_orders(id),
    source          points_source NOT NULL,
    points          INTEGER       NOT NULL,   -- موجب=ربح، سالب=صرف
    balance_after   INTEGER       NOT NULL,
    description     TEXT,
    expires_at      TIMESTAMPTZ,
    created_at      TIMESTAMPTZ   DEFAULT NOW()
);
CREATE INDEX idx_pts_txn_account ON points_transactions(account_id, created_at DESC);
CREATE INDEX idx_pts_txn_order   ON points_transactions(mall_order_id) WHERE mall_order_id IS NOT NULL;

-- ─── LOYALTY RULES (per store or mall-wide) ──────────────────────────────
CREATE TABLE loyalty_rules (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id         UUID         NOT NULL REFERENCES malls(id),
    store_id        UUID         REFERENCES tenants(id),   -- NULL = mall-wide
    name            VARCHAR(100) NOT NULL,
    points_per_egp  NUMERIC(8,4) DEFAULT 1.0,   -- نقاط لكل جنيه
    min_order_value NUMERIC(12,2) DEFAULT 0,
    tier_multiplier JSONB DEFAULT '{"Bronze":1,"Silver":1.5,"Gold":2}',
    is_active       BOOLEAN DEFAULT TRUE,
    valid_from      TIMESTAMPTZ DEFAULT NOW(),
    valid_to        TIMESTAMPTZ,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_loyalty_rules_mall ON loyalty_rules(mall_id, is_active);

-- ═══════════════════════════════════════════
--  PROMOTIONS / COUPONS / FLASH SALES
-- ═══════════════════════════════════════════

CREATE TYPE discount_type   AS ENUM ('Percentage','FixedAmount','FreeDelivery','BuyXGetY');
CREATE TYPE promotion_scope AS ENUM ('MallWide','Store','Category','Product');
CREATE TYPE coupon_status   AS ENUM ('Active','Paused','Expired','Depleted');

-- ─── COUPONS ──────────────────────────────────────────────────────────────
CREATE TABLE coupons (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id         UUID          NOT NULL REFERENCES malls(id),
    store_id        UUID          REFERENCES tenants(id),    -- NULL = mall-wide
    code            VARCHAR(30)   UNIQUE NOT NULL,
    name            VARCHAR(100)  NOT NULL,
    description     TEXT,
    discount_type   discount_type NOT NULL,
    discount_value  NUMERIC(10,2) NOT NULL,                  -- % أو مبلغ
    min_order_value NUMERIC(12,2) DEFAULT 0,
    max_discount    NUMERIC(12,2),                           -- حد أقصى للخصم
    max_uses        INTEGER,                                 -- NULL = unlimited
    uses_per_customer INTEGER DEFAULT 1,
    used_count      INTEGER       DEFAULT 0,
    scope           promotion_scope DEFAULT 'MallWide',
    scope_id        UUID,                                    -- category/product ID
    status          coupon_status  DEFAULT 'Active',
    min_tier        VARCHAR(20),                             -- Bronze | Silver | Gold
    valid_from      TIMESTAMPTZ   DEFAULT NOW(),
    valid_to        TIMESTAMPTZ   NOT NULL,
    created_by      UUID          REFERENCES users(id),
    created_at      TIMESTAMPTZ   DEFAULT NOW(),
    updated_at      TIMESTAMPTZ   DEFAULT NOW()
);
CREATE INDEX idx_coupons_mall   ON coupons(mall_id, status, valid_to);
CREATE INDEX idx_coupons_code   ON coupons(code, status);
CREATE INDEX idx_coupons_store  ON coupons(store_id) WHERE store_id IS NOT NULL;

-- ─── COUPON USES ──────────────────────────────────────────────────────────
CREATE TABLE coupon_uses (
    id            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    coupon_id     UUID          NOT NULL REFERENCES coupons(id),
    customer_id   UUID          NOT NULL REFERENCES mall_customers(id),
    mall_order_id UUID          NOT NULL REFERENCES mall_orders(id),
    discount_amt  NUMERIC(12,2) NOT NULL,
    used_at       TIMESTAMPTZ   DEFAULT NOW(),
    UNIQUE(coupon_id, customer_id, mall_order_id)
);
CREATE INDEX idx_coupon_uses_coupon   ON coupon_uses(coupon_id);
CREATE INDEX idx_coupon_uses_customer ON coupon_uses(customer_id, coupon_id);

-- ─── FLASH SALES ──────────────────────────────────────────────────────────
CREATE TABLE flash_sales (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id         UUID          NOT NULL REFERENCES malls(id),
    store_id        UUID          REFERENCES tenants(id),
    title           VARCHAR(200)  NOT NULL,
    title_ar        VARCHAR(200),
    product_id      UUID          REFERENCES products(id),
    original_price  NUMERIC(12,2),
    flash_price     NUMERIC(12,2) NOT NULL,
    discount_pct    NUMERIC(5,2)  GENERATED ALWAYS AS (
        CASE WHEN original_price > 0
        THEN ROUND((1 - flash_price/original_price)*100, 1)
        ELSE 0 END
    ) STORED,
    quantity_limit  INTEGER       DEFAULT 100,
    quantity_sold   INTEGER       DEFAULT 0,
    starts_at       TIMESTAMPTZ   NOT NULL,
    ends_at         TIMESTAMPTZ   NOT NULL,
    banner_url      TEXT,
    is_active       BOOLEAN       DEFAULT TRUE,
    created_at      TIMESTAMPTZ   DEFAULT NOW()
);
CREATE INDEX idx_flash_sales_active ON flash_sales(mall_id, is_active, starts_at, ends_at);

-- ═══════════════════════════════════════════
--  PUSH NOTIFICATIONS + GEO-FENCING
-- ═══════════════════════════════════════════

-- ─── DEVICE TOKENS (Firebase FCM) ───────────────────────────────────────
CREATE TABLE customer_devices (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    customer_id UUID         NOT NULL REFERENCES mall_customers(id) ON DELETE CASCADE,
    fcm_token   TEXT         NOT NULL,
    platform    VARCHAR(20)  DEFAULT 'Flutter',   -- Flutter | iOS | Android
    device_name VARCHAR(100),
    is_active   BOOLEAN      DEFAULT TRUE,
    last_seen   TIMESTAMPTZ  DEFAULT NOW(),
    created_at  TIMESTAMPTZ  DEFAULT NOW(),
    UNIQUE(customer_id, fcm_token)
);
CREATE INDEX idx_devices_customer ON customer_devices(customer_id, is_active);
CREATE INDEX idx_devices_fcm      ON customer_devices(fcm_token) WHERE is_active;

-- ─── PUSH NOTIFICATION CAMPAIGNS ────────────────────────────────────────
CREATE TYPE notif_target   AS ENUM ('AllCustomers','TierBronze','TierSilver','TierGold','InMallZone','CustomSegment');
CREATE TYPE notif_status   AS ENUM ('Draft','Scheduled','Sending','Sent','Failed','Cancelled');

CREATE TABLE notification_campaigns (
    id            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id       UUID          NOT NULL REFERENCES malls(id),
    title         VARCHAR(200)  NOT NULL,
    title_ar      VARCHAR(200),
    body          TEXT          NOT NULL,
    body_ar       TEXT,
    image_url     TEXT,
    action_type   VARCHAR(30),                    -- OpenStore | OpenPromo | OpenOrder
    action_id     VARCHAR(100),
    target        notif_target   DEFAULT 'AllCustomers',
    status        notif_status   DEFAULT 'Draft',
    scheduled_at  TIMESTAMPTZ,
    sent_at       TIMESTAMPTZ,
    sent_count    INTEGER       DEFAULT 0,
    open_count    INTEGER       DEFAULT 0,
    created_by    UUID          REFERENCES users(id),
    created_at    TIMESTAMPTZ   DEFAULT NOW(),
    updated_at    TIMESTAMPTZ   DEFAULT NOW()
);
CREATE INDEX idx_notif_campaigns_mall ON notification_campaigns(mall_id, status);

-- ─── GEO-FENCE TRIGGERS ──────────────────────────────────────────────────
CREATE TABLE geo_fence_triggers (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id         UUID         NOT NULL REFERENCES malls(id),
    name            VARCHAR(100) NOT NULL,
    trigger_type    VARCHAR(30)  DEFAULT 'Enter',     -- Enter | Exit | Dwell
    radius_m        INTEGER      DEFAULT 200,
    notif_title     VARCHAR(200) NOT NULL,
    notif_body      TEXT         NOT NULL,
    notif_title_ar  VARCHAR(200),
    notif_body_ar   TEXT,
    action_type     VARCHAR(30),
    action_id       VARCHAR(100),
    cooldown_hours  INTEGER      DEFAULT 24,           -- لا ترسل مرتين في 24h
    is_active       BOOLEAN      DEFAULT TRUE,
    valid_from      TIMESTAMPTZ  DEFAULT NOW(),
    valid_to        TIMESTAMPTZ,
    created_at      TIMESTAMPTZ  DEFAULT NOW()
);

-- ─── GEO-FENCE EVENTS (log) ──────────────────────────────────────────────
CREATE TABLE geo_fence_events (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    trigger_id      UUID          NOT NULL REFERENCES geo_fence_triggers(id),
    customer_id     UUID          NOT NULL REFERENCES mall_customers(id),
    event_type      VARCHAR(30)   NOT NULL,
    customer_lat    NUMERIC(10,7),
    customer_lng    NUMERIC(10,7),
    notif_sent      BOOLEAN       DEFAULT FALSE,
    created_at      TIMESTAMPTZ   DEFAULT NOW()
);
CREATE INDEX idx_geo_events_customer ON geo_fence_events(customer_id, trigger_id, created_at DESC);

-- ─── SEED: Welcome notification trigger ─────────────────────────────────
INSERT INTO geo_fence_triggers
    (mall_id, name, trigger_type, radius_m, notif_title, notif_body,
     notif_title_ar, notif_body_ar, cooldown_hours, is_active)
SELECT id, 'Welcome to MallX', 'Enter', 200,
    'Welcome to MallX! 🛍️', 'Check out today''s exclusive deals just for you.',
    'أهلاً بك في MallX! 🛍️', 'تفقد عروض اليوم الحصرية المخصصة لك.',
    24, TRUE
FROM malls WHERE slug = 'mallx-demo';

COMMIT;
