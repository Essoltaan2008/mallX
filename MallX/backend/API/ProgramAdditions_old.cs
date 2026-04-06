// ═══════════════════════════════════════════════════════════════════════════
//  أضف هذا الكود لـ Program.cs الحالي في MesterXPro
//  ضعه بعد سطر: builder.Services.AddScoped<IAIService, AIService>();
// ═══════════════════════════════════════════════════════════════════════════

// MallX Services
// builder.Services.AddScoped<IMallCustomerAuthService, MallCustomerAuthService>();
// builder.Services.AddScoped<ICartService, CartService>();
// builder.Services.AddScoped<IMallOrderService, MallOrderService>();

// ─── Rate limiting مخصص للـ Customer login ────────────────────────────────
// (أضف هذا داخل AddRateLimiter بجانب الـ "login" limiter الحالي)
// opt.AddFixedWindowLimiter("customer-register", l => {
//     l.Window = TimeSpan.FromMinutes(60);
//     l.PermitLimit = 3;
//     l.QueueLimit = 0;
// });

// ─── appsettings.json — أضف هذا ──────────────────────────────────────────
/*
{
  "Jwt": {
    "Secret": "...",
    "Issuer": "mesterxpro",
    "Audience": "mesterxpro-client",
    "ExpiryMinutes": "60",
    "CustomerExpiryMinutes": "60"   // ← أضف هذا
  }
}
*/

// ═══════════════════════════════════════════════════════════════════════════
//  MallX API Endpoints Summary (للـ Swagger documentation)
// ═══════════════════════════════════════════════════════════════════════════

/*
╔══════════════════════════════════════════════════════════════════╗
║  CUSTOMER AUTH                                                    ║
╠══════════════════════════════════════════════════════════════════╣
║  POST   /api/mall/auth/register    ← تسجيل عميل جديد            ║
║  POST   /api/mall/auth/login       ← تسجيل الدخول               ║
║  POST   /api/mall/auth/refresh     ← تجديد التوكن               ║
║  POST   /api/mall/auth/logout      ← تسجيل الخروج  [Auth]       ║
║  GET    /api/mall/auth/me          ← بيانات العميل  [Auth]       ║
╠══════════════════════════════════════════════════════════════════╣
║  CART                                                             ║
╠══════════════════════════════════════════════════════════════════╣
║  GET    /api/mall/cart             ← جلب السلة      [Auth]       ║
║  POST   /api/mall/cart/items       ← إضافة منتج     [Auth]       ║
║  PUT    /api/mall/cart/items       ← تعديل كمية     [Auth]       ║
║  DELETE /api/mall/cart/items/{id}  ← حذف منتج       [Auth]       ║
║  DELETE /api/mall/cart             ← تفريغ السلة    [Auth]       ║
╠══════════════════════════════════════════════════════════════════╣
║  ORDERS (Customer)                                                ║
╠══════════════════════════════════════════════════════════════════╣
║  POST   /api/mall/orders/checkout  ← إتمام الطلب    [Auth]       ║
║  GET    /api/mall/orders/{id}      ← تفاصيل طلب     [Auth]       ║
║  GET    /api/mall/orders           ← سجل الطلبات    [Auth]       ║
╠══════════════════════════════════════════════════════════════════╣
║  STORE DASHBOARD                                                  ║
╠══════════════════════════════════════════════════════════════════╣
║  GET    /api/mall/store/orders/incoming           [Auth:Store]    ║
║  PATCH  /api/mall/store/orders/{id}/status        [Auth:Store]    ║
╠══════════════════════════════════════════════════════════════════╣
║  MALL ADMIN                                                       ║
╠══════════════════════════════════════════════════════════════════╣
║  GET    /api/mall/admin/dashboard  ← KPIs          [Auth:Admin]   ║
╚══════════════════════════════════════════════════════════════════╝
*/
