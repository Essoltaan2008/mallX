-- ═══════════════════════════════════════════════════════════════════════════
-- MallX Phase 9 — E-Wallet + Referrals + Subscriptions + WhatsApp + SuperAdmin
-- ═══════════════════════════════════════════════════════════════════════════

BEGIN;

-- ═══════════════════════════════════════════
--  CUSTOMER E-WALLET
-- ═══════════════════════════════════════════

CREATE TYPE wallet_txn_type AS ENUM (
    'TopUp','Purchase','Refund','Bonus','Transfer','Withdrawal','Adjustment'
);
CREATE TYPE wallet_txn_status AS ENUM ('Pending','Completed','Failed','Reversed');

CREATE TABLE customer_wallets (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    customer_id     UUID NOT NULL REFERENCES mall_customers(id) ON DELETE CASCADE,
    mall_id         UUID NOT NULL REFERENCES malls(id),
    balance         NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (balance >= 0),
    total_topped_up NUMERIC(14,2) DEFAULT 0,
    total_spent     NUMERIC(14,2) DEFAULT 0,
    is_active       BOOLEAN DEFAULT TRUE,
    created_at      TIMESTAMPTZ DEFAULT NOW(),
    updated_at      TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(customer_id, mall_id)
);
CREATE INDEX idx_wallet_customer ON customer_wallets(customer_id);

CREATE TABLE wallet_transactions (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    wallet_id       UUID NOT NULL REFERENCES customer_wallets(id),
    customer_id     UUID NOT NULL REFERENCES mall_customers(id),
    mall_order_id   UUID REFERENCES mall_orders(id),
    type            wallet_txn_type   NOT NULL,
    status          wallet_txn_status DEFAULT 'Completed',
    amount          NUMERIC(12,2) NOT NULL,   -- موجب = دخول, سالب = خروج
    balance_before  NUMERIC(12,2) NOT NULL,
    balance_after   NUMERIC(12,2) NOT NULL,
    reference       VARCHAR(100),             -- external ref (gateway txn)
    description     TEXT,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_wallet_txn_wallet   ON wallet_transactions(wallet_id, created_at DESC);
CREATE INDEX idx_wallet_txn_customer ON wallet_transactions(customer_id, created_at DESC);

-- ═══════════════════════════════════════════
--  REFERRAL SYSTEM
-- ═══════════════════════════════════════════

CREATE TABLE referral_programs (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id             UUID NOT NULL REFERENCES malls(id),
    name                VARCHAR(100) NOT NULL,
    referrer_reward_pts INTEGER DEFAULT 200,    -- نقاط للمُحيل
    referee_reward_pts  INTEGER DEFAULT 100,    -- نقاط للمُحال
    referrer_wallet_egp NUMERIC(8,2) DEFAULT 0, -- جنيهات للمُحيل
    referee_discount_pct NUMERIC(5,2) DEFAULT 0,-- خصم للمُحال على أول طلب
    min_order_to_unlock NUMERIC(12,2) DEFAULT 0,-- حد أدنى للطلب لفتح المكافأة
    max_referrals       INTEGER,                -- NULL = unlimited
    is_active           BOOLEAN DEFAULT TRUE,
    valid_from          TIMESTAMPTZ DEFAULT NOW(),
    valid_to            TIMESTAMPTZ,
    created_at          TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE referral_codes (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    customer_id UUID NOT NULL REFERENCES mall_customers(id),
    mall_id     UUID NOT NULL REFERENCES malls(id),
    code        VARCHAR(20) UNIQUE NOT NULL,
    uses_count  INTEGER DEFAULT 0,
    created_at  TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(customer_id, mall_id)
);
CREATE INDEX idx_referral_code ON referral_codes(code);

CREATE TABLE referral_uses (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    program_id      UUID NOT NULL REFERENCES referral_programs(id),
    referrer_id     UUID NOT NULL REFERENCES mall_customers(id),
    referee_id      UUID NOT NULL REFERENCES mall_customers(id),
    mall_order_id   UUID REFERENCES mall_orders(id),
    referrer_rewarded BOOLEAN DEFAULT FALSE,
    referee_rewarded  BOOLEAN DEFAULT FALSE,
    created_at      TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(referee_id, program_id)  -- كل عميل يُحال مرة واحدة
);

-- ═══════════════════════════════════════════
--  STORE SUBSCRIPTIONS (المحل يدفع للمول)
-- ═══════════════════════════════════════════

CREATE TYPE sub_billing_cycle AS ENUM ('Monthly','Quarterly','Annual');
CREATE TYPE sub_status AS ENUM ('Active','Suspended','Cancelled','PastDue','Trial');

CREATE TABLE store_subscriptions (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    store_id        UUID NOT NULL REFERENCES tenants(id),
    mall_id         UUID NOT NULL REFERENCES malls(id),
    plan_id         UUID NOT NULL REFERENCES subscription_plans(id),
    billing_cycle   sub_billing_cycle DEFAULT 'Monthly',
    status          sub_status DEFAULT 'Trial',
    amount          NUMERIC(12,2) NOT NULL,
    trial_ends_at   TIMESTAMPTZ,
    current_period_start TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    current_period_end   TIMESTAMPTZ NOT NULL,
    next_billing_at TIMESTAMPTZ,
    cancelled_at    TIMESTAMPTZ,
    cancel_reason   TEXT,
    auto_renew      BOOLEAN DEFAULT TRUE,
    created_at      TIMESTAMPTZ DEFAULT NOW(),
    updated_at      TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(store_id)
);
CREATE INDEX idx_store_sub_mall ON store_subscriptions(mall_id, status);
CREATE INDEX idx_store_sub_next ON store_subscriptions(next_billing_at) WHERE status = 'Active';

CREATE TABLE subscription_invoices (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    subscription_id UUID NOT NULL REFERENCES store_subscriptions(id),
    store_id        UUID NOT NULL REFERENCES tenants(id),
    amount          NUMERIC(12,2) NOT NULL,
    status          VARCHAR(20) DEFAULT 'Pending',   -- Pending|Paid|Failed
    due_date        TIMESTAMPTZ NOT NULL,
    paid_at         TIMESTAMPTZ,
    gateway_ref     VARCHAR(100),
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

-- ═══════════════════════════════════════════
--  WHATSAPP NOTIFICATIONS LOG
-- ═══════════════════════════════════════════

CREATE TYPE whatsapp_status AS ENUM ('Queued','Sent','Delivered','Read','Failed');

CREATE TABLE whatsapp_messages (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    customer_id     UUID REFERENCES mall_customers(id),
    mall_order_id   UUID REFERENCES mall_orders(id),
    phone           VARCHAR(20) NOT NULL,
    template        VARCHAR(100) NOT NULL,
    variables       JSONB,               -- template variables
    status          whatsapp_status DEFAULT 'Queued',
    provider_msg_id VARCHAR(100),        -- Twilio / WhatsApp Business ID
    error_message   TEXT,
    sent_at         TIMESTAMPTZ,
    delivered_at    TIMESTAMPTZ,
    read_at         TIMESTAMPTZ,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_wa_customer ON whatsapp_messages(customer_id, created_at DESC);
CREATE INDEX idx_wa_order    ON whatsapp_messages(mall_order_id);

-- ═══════════════════════════════════════════
--  SUPERADMIN — MULTI-MALL MANAGEMENT
-- ═══════════════════════════════════════════

CREATE TABLE platform_settings (
    key         VARCHAR(100) PRIMARY KEY,
    value       TEXT NOT NULL,
    description TEXT,
    updated_at  TIMESTAMPTZ DEFAULT NOW()
);

INSERT INTO platform_settings (key, value, description) VALUES
('platform.name',              'MallX',            'Platform brand name'),
('platform.default_commission','0.05',             'Default commission rate (5%)'),
('platform.trial_days',        '14',               'Free trial days for new stores'),
('platform.support_email',     'support@mallx.app','Support email'),
('platform.min_withdrawal',    '500',              'Min wallet withdrawal (EGP)'),
('referral.enabled',           'true',             'Enable referral program'),
('whatsapp.enabled',           'false',            'Enable WhatsApp notifications');

CREATE TABLE mall_staff (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id     UUID NOT NULL REFERENCES malls(id),
    user_id     UUID NOT NULL REFERENCES users(id),
    role        VARCHAR(50) NOT NULL DEFAULT 'Staff', -- MallAdmin|Staff|Support
    permissions JSONB,
    is_active   BOOLEAN DEFAULT TRUE,
    created_at  TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(mall_id, user_id)
);

-- Platform revenue tracking
CREATE TABLE platform_revenue (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    mall_id         UUID NOT NULL REFERENCES malls(id),
    source          VARCHAR(50) NOT NULL,  -- Commission|Subscription|TopUp
    amount          NUMERIC(14,2) NOT NULL,
    reference_id    UUID,
    reference_type  VARCHAR(50),
    recorded_at     TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_platform_rev ON platform_revenue(recorded_at DESC);

-- ─── SEED: Default referral program ──────────────────────────────────────
INSERT INTO referral_programs
    (mall_id, name, referrer_reward_pts, referee_reward_pts, referee_discount_pct, min_order_to_unlock, is_active)
SELECT id, 'برنامج الإحالة', 200, 100, 10, 50, TRUE
FROM malls WHERE slug = 'mallx-demo';

COMMIT;
