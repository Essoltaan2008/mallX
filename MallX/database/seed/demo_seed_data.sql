-- ═══════════════════════════════════════════════════════════════════════════
-- MallX Demo Seed Data
-- بيانات تجريبية كاملة لاختبار النظام
-- ═══════════════════════════════════════════════════════════════════════════

BEGIN;

-- ─── 1. MALL ─────────────────────────────────────────────────────────────
UPDATE malls SET
    name         = 'City Centre MallX',
    name_ar      = 'سيتي سنتر مول إكس',
    address      = 'طريق النصر، مدينة نصر، القاهرة',
    geo_lat      = 30.0626,
    geo_lng      = 31.3283,
    geo_radius_m = 300,
    phone        = '+20 2 2690 0000',
    email        = 'info@mallx-demo.com'
WHERE slug = 'mallx-demo';

-- ─── 2. SUBSCRIPTION PLANS update ────────────────────────────────────────
UPDATE subscription_plans SET commission_rate = 0.05 WHERE name = 'Basic';
UPDATE subscription_plans SET commission_rate = 0.03 WHERE name = 'Pro';

-- ─── 3. STORE TENANTS ────────────────────────────────────────────────────
DO $$
DECLARE
    mall_id UUID := (SELECT id FROM malls WHERE slug = 'mallx-demo');
    pro_plan UUID := (SELECT id FROM subscription_plans WHERE name = 'Pro');
    basic_plan UUID := (SELECT id FROM subscription_plans WHERE name = 'Basic');

    store_food UUID;
    store_burger UUID;
    store_fashion UUID;
    store_electronics UUID;
    store_salon UUID;
    store_pharmacy UUID;

BEGIN

-- Food Court
INSERT INTO tenants (id, name, slug, plan_id, status, email, phone, currency, vat_rate, is_active)
VALUES
(gen_random_uuid(), 'مطعم الشيف العربي', 'chef-arabi', pro_plan, 'Active', 'chef@mallx.com', '01001234567', 'EGP', 0.14, TRUE),
(gen_random_uuid(), 'برجر فاكتوري', 'burger-factory', pro_plan, 'Active', 'burger@mallx.com', '01001234568', 'EGP', 0.14, TRUE),
(gen_random_uuid(), 'متجر الأناقة', 'elegance-store', pro_plan, 'Active', 'elegance@mallx.com', '01001234569', 'EGP', 0.14, TRUE),
(gen_random_uuid(), 'تك ستور', 'tech-store', basic_plan, 'Active', 'tech@mallx.com', '01001234570', 'EGP', 0.14, TRUE),
(gen_random_uuid(), 'صالون لوريال', 'loreal-salon', pro_plan, 'Active', 'salon@mallx.com', '01001234571', 'EGP', 0.14, TRUE),
(gen_random_uuid(), 'صيدلية النهضة', 'nahda-pharmacy', basic_plan, 'Active', 'pharmacy@mallx.com', '01001234572', 'EGP', 0.14, TRUE)
RETURNING id INTO store_food;

-- Assign to mall
UPDATE tenants SET
    mall_id = mall_id,
    store_type = CASE
        WHEN slug IN ('chef-arabi', 'burger-factory') THEN 'Restaurant'
        WHEN slug IN ('loreal-salon') THEN 'Service'
        ELSE 'Retail'
    END,
    floor_number = CASE
        WHEN slug IN ('chef-arabi', 'burger-factory') THEN 0
        WHEN slug IN ('elegance-store', 'tech-store') THEN 1
        WHEN slug IN ('loreal-salon', 'nahda-pharmacy') THEN 2
        ELSE 1
    END,
    commission = CASE
        WHEN slug IN ('chef-arabi', 'burger-factory') THEN 0.03
        WHEN slug IN ('loreal-salon') THEN 0.03
        ELSE 0.05
    END
WHERE slug IN ('chef-arabi','burger-factory','elegance-store','tech-store','loreal-salon','nahda-pharmacy');

END $$;

-- ─── 4. CATEGORIES ───────────────────────────────────────────────────────
INSERT INTO categories (tenant_id, name, color, icon, is_active)
SELECT t.id, c.name, c.color, c.icon, TRUE
FROM tenants t
CROSS JOIN (VALUES
    ('مشويات', '#F59E0B', '🥩'),
    ('مقبلات', '#10B981', '🥗'),
    ('مشروبات', '#3B82F6', '🥤'),
    ('حلويات', '#EC4899', '🍰')
) AS c(name, color, icon)
WHERE t.slug = 'chef-arabi';

INSERT INTO categories (tenant_id, name, color, icon, is_active)
SELECT t.id, c.name, c.color, c.icon, TRUE
FROM tenants t
CROSS JOIN (VALUES
    ('برجر كلاسيك', '#F59E0B', '🍔'),
    ('برجر خاص', '#EF4444', '🌶️'),
    ('مشروبات', '#3B82F6', '🥤'),
    ('إضافات', '#8B5CF6', '🍟')
) AS c(name, color, icon)
WHERE t.slug = 'burger-factory';

-- ─── 5. PRODUCTS ─────────────────────────────────────────────────────────
-- Chef Arabi products
WITH chef AS (SELECT id FROM tenants WHERE slug = 'chef-arabi'),
     cat AS (SELECT id FROM categories WHERE tenant_id = (SELECT id FROM chef) AND name = 'مشويات' LIMIT 1)
INSERT INTO products (tenant_id, category_id, name, description, sku, sale_price, cost_price, vat_rate, min_stock_level, is_active)
SELECT
    chef.id, cat.id, p.name, p.desc, p.sku, p.price, p.cost, 0.14, 5, TRUE
FROM chef, cat
CROSS JOIN (VALUES
    ('كفتة مشوية', 'كفتة لحم مشوية على الفحم مع الخبز والسلطة', 'CHEF-001', 89.00, 45.00),
    ('شيش طاووق', 'دجاج متبل مشوي مع صوص ثوم وخبز عربي', 'CHEF-002', 79.00, 38.00),
    ('كباب هاشمي', 'كباب لحمة ودجاج مع الخضار المشوية', 'CHEF-003', 129.00, 65.00),
    ('مزة كاملة', 'طبق مزة مع حمص وبابا غنوج وورق عنب', 'CHEF-004', 59.00, 28.00)
) AS p(name, desc, sku, price, cost);

-- Burger Factory products
WITH burger AS (SELECT id FROM tenants WHERE slug = 'burger-factory'),
     cat AS (SELECT id FROM categories WHERE tenant_id = (SELECT id FROM burger) AND name = 'برجر كلاسيك' LIMIT 1)
INSERT INTO products (tenant_id, category_id, name, description, sku, sale_price, cost_price, vat_rate, min_stock_level, is_active)
SELECT
    burger.id, cat.id, p.name, p.desc, p.sku, p.price, p.cost, 0.14, 5, TRUE
FROM burger, cat
CROSS JOIN (VALUES
    ('كلاسيك برجر', 'لحم بقري 150g مع خس وطماطم وصوص خاص', 'BRG-001', 55.00, 25.00),
    ('دبل تشيز برجر', 'لحمتين مع جبنة تشيدر مضاعفة وبيكون', 'BRG-002', 79.00, 38.00),
    ('كريسبي تشيكن برجر', 'دجاج مقرمش مع صوص مايونيز حار', 'BRG-003', 65.00, 30.00),
    ('ميلتي برجر', 'برجر مع 4 أنواع جبنة مشكلة', 'BRG-004', 89.00, 42.00)
) AS p(name, desc, sku, price, cost);

-- ─── 6. STOCK ITEMS ──────────────────────────────────────────────────────
WITH branch AS (
    SELECT b.id, b.tenant_id FROM branches b
    JOIN tenants t ON t.id = b.tenant_id
    WHERE t.slug IN ('chef-arabi', 'burger-factory', 'elegance-store', 'tech-store')
    LIMIT 4
)
INSERT INTO stock_items (tenant_id, branch_id, product_id, quantity, reserved_quantity)
SELECT p.tenant_id, br.id, p.id, 100, 0
FROM products p
JOIN branch br ON br.tenant_id = p.tenant_id
ON CONFLICT (branch_id, product_id) DO NOTHING;

-- ─── 7. MENU ITEMS (Restaurant module) ───────────────────────────────────
WITH chef AS (SELECT id FROM tenants WHERE slug = 'chef-arabi')
INSERT INTO menu_items (store_id, name, name_ar, description, price, prep_time_min, is_available, is_featured, tags)
SELECT
    chef.id, m.name_en, m.name_ar, m.desc, m.price, m.prep, TRUE, m.featured, m.tags
FROM chef
CROSS JOIN (VALUES
    ('Grilled Kofta', 'كفتة مشوية', 'كفتة لحم مشوية على الفحم', 89, 15, TRUE,  ARRAY['مشوي','لحم']),
    ('Chicken Shawarma', 'شاورما دجاج', 'شاورما دجاج مع صوص طحينة', 45, 10, TRUE,  ARRAY['شاورما']),
    ('Mixed Grill', 'مشاوي مشكلة', 'طبق مشاوي مشكل للشخصين', 179, 25, TRUE,  ARRAY['مشوي','كبير']),
    ('Fattoush Salad', 'فتوش', 'سلطة فتوش طازجة', 35, 5, FALSE, ARRAY['سلطة','صحي']),
    ('Umm Ali', 'أم علي', 'أم علي بالمكسرات والقشطة', 39, 8, FALSE, ARRAY['حلويات'])
) AS m(name_en, name_ar, desc, price, prep, featured, tags);

-- ─── 8. DEMO CUSTOMERS ───────────────────────────────────────────────────
WITH mall AS (SELECT id FROM malls WHERE slug = 'mallx-demo')
INSERT INTO mall_customers (mall_id, first_name, last_name, email, phone, password_hash, loyalty_points, tier, is_active)
SELECT
    mall.id, c.fn, c.ln, c.email, c.phone,
    -- Password: 'Demo123456' for all demo customers
    '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/LewdBGrSLGJdVF.bS',
    c.pts, c.tier, TRUE
FROM mall
CROSS JOIN (VALUES
    ('أحمد', 'محمد', 'ahmed@demo.com', '01012345678', 3500, 'Silver'),
    ('مريم', 'علي', 'maryam@demo.com', '01023456789', 6200, 'Gold'),
    ('كريم', 'حسن', 'karim@demo.com', '01034567890', 450, 'Bronze'),
    ('سارة', 'أحمد', 'sara@demo.com', '01045678901', 1200, 'Silver'),
    ('عمر', 'محمود', 'omar@demo.com', '01056789012', 0, 'Bronze')
) AS c(fn, ln, email, phone, pts, tier)
ON CONFLICT (email) DO NOTHING;

-- ─── 9. LOYALTY ACCOUNTS ────────────────────────────────────────────────
WITH mall AS (SELECT id FROM malls WHERE slug = 'mallx-demo')
INSERT INTO loyalty_accounts (customer_id, mall_id, lifetime_points, redeemed_points, tier, points_expire_at)
SELECT
    c.id, mall.id, c.loyalty_points, 0, c.tier::VARCHAR, NOW() + INTERVAL '12 months'
FROM mall_customers c, mall
ON CONFLICT (customer_id, mall_id) DO NOTHING;

-- ─── 10. DEMO COUPONS ────────────────────────────────────────────────────
WITH mall AS (SELECT id FROM malls WHERE slug = 'mallx-demo')
INSERT INTO coupons (mall_id, code, name, description, discount_type, discount_value, min_order_value, max_uses, uses_per_customer, valid_to, status)
SELECT
    mall.id, cp.code, cp.name, cp.desc, cp.dtype::discount_type, cp.val, cp.min_ord, cp.max_u, 1,
    NOW() + INTERVAL '30 days', 'Active'
FROM mall
CROSS JOIN (VALUES
    ('WELCOME20', 'خصم ترحيب 20%', 'للعملاء الجدد فقط', 'Percentage', 20, 0, 100),
    ('SAVE50',    'خصم 50 جنيه',   'خصم ثابت على الطلب', 'FixedAmount', 50, 200, 200),
    ('FREEDEL',   'توصيل مجاني',   'بدون رسوم توصيل',    'FreeDelivery', 0, 100, NULL),
    ('GOLD30',    'خصم Gold 30%',  'للعملاء Gold فقط',   'Percentage', 30, 150, 50)
) AS cp(code, name, desc, dtype, val, min_ord, max_u);

-- Set Gold coupon for Gold tier only
UPDATE coupons SET min_tier = 'Gold' WHERE code = 'GOLD30';

-- ─── 11. FLASH SALE ──────────────────────────────────────────────────────
WITH mall AS (SELECT id FROM malls WHERE slug = 'mallx-demo'),
     burger_store AS (SELECT id FROM tenants WHERE slug = 'burger-factory')
INSERT INTO flash_sales (mall_id, store_id, title, title_ar, original_price, flash_price, quantity_limit, starts_at, ends_at, is_active)
SELECT
    mall.id, burger_store.id,
    'Burger Flash Deal!', 'عرض برجر فلاش!',
    79.00, 49.00, 50,
    NOW(), NOW() + INTERVAL '24 hours',
    TRUE
FROM mall, burger_store;

-- ─── 12. GEO FENCE TRIGGER ───────────────────────────────────────────────
-- Already seeded in phase4 migration

-- ─── 13. MALL FLOORS ─────────────────────────────────────────────────────
-- Already seeded in phase6 migration

-- ─── 14. STORE LOCATIONS ON MAP ──────────────────────────────────────────
WITH floors AS (
    SELECT f.id, f.floor_num, f.mall_id
    FROM mall_floors f
    JOIN malls m ON m.id = f.mall_id
    WHERE m.slug = 'mallx-demo'
)
INSERT INTO store_locations (store_id, floor_id, pos_x, pos_y, width, height, color, qr_code)
SELECT
    t.id,
    (SELECT id FROM floors WHERE floor_num = sl.floor),
    sl.x, sl.y, sl.w, sl.h, sl.color,
    'mallx://store/' || t.id::text
FROM tenants t
JOIN (VALUES
    ('chef-arabi',       0, 50,  50, 120, 80, '#F59E0B'),
    ('burger-factory',   0, 200, 50, 100, 80, '#EF4444'),
    ('elegance-store',   1, 50,  50, 100, 70, '#8B5CF6'),
    ('tech-store',       1, 180, 50, 90,  70, '#3B82F6'),
    ('loreal-salon',     2, 50,  50, 110, 70, '#EC4899'),
    ('nahda-pharmacy',   2, 190, 50, 100, 70, '#10B981')
) AS sl(slug, floor, x, y, w, h, color) ON sl.slug = t.slug
WHERE t.slug IN ('chef-arabi','burger-factory','elegance-store','tech-store','loreal-salon','nahda-pharmacy')
ON CONFLICT (store_id) DO NOTHING;

-- ─── 15. MAP AMENITIES ───────────────────────────────────────────────────
WITH floors AS (
    SELECT f.id, f.floor_num FROM mall_floors f
    JOIN malls m ON m.id = f.mall_id WHERE m.slug = 'mallx-demo'
)
INSERT INTO map_amenities (floor_id, type, name, name_ar, pos_x, pos_y, icon)
SELECT f.id, a.type, a.name, a.name_ar, a.x, a.y, a.icon
FROM floors f
CROSS JOIN (VALUES
    ('Toilet',   'Restroom',   'دورة المياه', 350, 50, '🚻'),
    ('ATM',      'ATM Machine','ماكينة صراف',  380, 50, '🏧'),
    ('Elevator', 'Elevator',   'مصعد',         400, 100,'🛗'),
    ('Entrance', 'Main Gate',  'المدخل الرئيسي',10,  50, '🚪')
) AS a(type, name, name_ar, x, y, icon)
WHERE f.floor_num = 0;

-- ─── 16. SERVICES (for Salon) ────────────────────────────────────────────
WITH salon AS (SELECT id FROM tenants WHERE slug = 'loreal-salon')
INSERT INTO services (store_id, name, description, duration_min, price, is_active)
SELECT
    salon.id, s.name, s.desc, s.dur, s.price, TRUE
FROM salon
CROSS JOIN (VALUES
    ('قصة شعر للسيدات', 'قص وتشكيل الشعر مع تجفيف', 60, 120),
    ('صبغة شعر',        'صبغة كاملة بأفضل المنتجات', 90, 250),
    ('كيراتين',          'علاج كيراتين برازيلي', 150, 450),
    ('مانيكير وباديكير','عناية بالأظافر يدين وقدمين', 45, 80),
    ('ميك أب',           'مكياج للمناسبات', 60, 200)
) AS s(name, desc, dur, price);

-- ─── 17. SERVICE STAFF ───────────────────────────────────────────────────
WITH salon AS (SELECT id FROM tenants WHERE slug = 'loreal-salon')
INSERT INTO service_staff (store_id, name, specialty, is_active)
SELECT salon.id, st.name, st.spec, TRUE
FROM salon
CROSS JOIN (VALUES
    ('سارة خالد', 'تصفيف وصبغات'),
    ('نور محمد',  'كيراتين ومعالجات'),
    ('ليلى أحمد', 'مكياج ومانيكير')
) AS st(name, spec);

-- ─── 18. WORKING HOURS ───────────────────────────────────────────────────
INSERT INTO working_hours (staff_id, day_of_week, start_time, end_time, is_active)
SELECT s.id, d.day, '10:00'::TIME, '22:00'::TIME, TRUE
FROM service_staff s
CROSS JOIN generate_series(0, 5) AS d(day)
ON CONFLICT (staff_id, day_of_week) DO NOTHING;

COMMIT;

-- ═══════════════════════════════════════════════════════════════════════════
-- Verify seed
-- ═══════════════════════════════════════════════════════════════════════════
DO $$
BEGIN
    RAISE NOTICE '✅ Demo seed complete!';
    RAISE NOTICE '   Malls:     %', (SELECT COUNT(*) FROM malls);
    RAISE NOTICE '   Stores:    %', (SELECT COUNT(*) FROM tenants WHERE is_active AND NOT is_deleted);
    RAISE NOTICE '   Products:  %', (SELECT COUNT(*) FROM products WHERE is_active AND NOT is_deleted);
    RAISE NOTICE '   Customers: %', (SELECT COUNT(*) FROM mall_customers WHERE is_active);
    RAISE NOTICE '   Coupons:   %', (SELECT COUNT(*) FROM coupons WHERE status = ''Active'');
    RAISE NOTICE '   Services:  %', (SELECT COUNT(*) FROM services WHERE is_active);
END $$;
