# 🏬 MallX — Complete Integration Guide

> Built on MesterXPro v2 Foundation | 5 Phases | 45+ Files | 11,000+ Lines

---

## 📁 Project Structure

```
MallX/
├── backend/
│   ├── API/
│   │   ├── Controllers/
│   │   │   ├── MallControllers.cs          ← Phase 1: Auth, Cart, Orders
│   │   │   ├── Phase2Controllers.cs        ← Phase 2: Payment, Commission
│   │   │   ├── Phase3/Phase3Controllers.cs ← Phase 3: Restaurant, Booking, Ratings
│   │   │   └── Phase4/Phase4Controllers.cs ← Phase 4: Loyalty, Promotions, Push
│   │   └── Program.cs                      ← Phase 5: FINAL complete Program.cs
│   ├── Application/
│   │   ├── DTOs/MallDTOs.cs
│   │   └── Services/
│   │       ├── MallCustomerAuthService.cs  ← Customer B2C auth
│   │       ├── CartService.cs              ← Cart management
│   │       ├── MallOrderService.cs         ← Order splitting engine
│   │       ├── Phase2/CommissionService.cs
│   │       ├── Phase2/PaymentService.cs    ← Paymob + Cash + Webhook
│   │       ├── Phase3/RestaurantService.cs ← Menu + Queue
│   │       ├── Phase3/BookingService.cs    ← Appointments
│   │       ├── Phase3/RatingService.cs     ← Reviews
│   │       ├── Phase4/LoyaltyService.cs    ← Points + Tiers
│   │       └── Phase4/PromotionService.cs  ← Coupons + FCM + Geo
│   ├── Domain/
│   │   ├── Mall/MallEntities.cs            ← Phase 1 entities
│   │   ├── Mall/Phase2Entities.cs          ← Payment entities
│   │   ├── Restaurant/Phase3Entities.cs    ← Restaurant + Booking + Rating
│   │   └── Loyalty/Phase4Entities.cs       ← Loyalty + Promotions + Push
│   ├── Hubs/SignalRHubs.cs                 ← Phase 5: Real-time
│   └── Infrastructure/
│       ├── Caching/RedisCacheService.cs    ← Phase 5: Redis
│       └── BackgroundJobs/Phase5Jobs.cs   ← Phase 5: Background jobs
├── database/
│   ├── phase1_mallx_migration.sql          ← Run first
│   ├── phase2/phase2_commission_payments.sql
│   ├── phase3/phase3_restaurant_booking_ratings.sql
│   └── phase4/phase4_loyalty_promotions_push.sql
├── flutter/ (Customer Mobile App)
│   └── lib/
│       ├── main.dart
│       ├── core/theme/app_theme.dart
│       ├── data/services/api_service.dart
│       ├── providers/providers.dart
│       └── screens/
│           ├── auth/login_screen.dart
│           ├── home/main_nav_screen.dart
│           ├── checkout/checkout_tracking.dart
│           ├── restaurant/restaurant_screens.dart
│           ├── booking/booking_rating_screens.dart
│           ├── loyalty/loyalty_promotions_screens.dart
│           └── tracking/realtime_tracking.dart  ← Phase 5: SignalR
├── frontend/ (Next.js Admin)
│   └── pages/
│       ├── mall-admin/index.tsx            ← MallAdmin dashboard
│       ├── mall-admin/promotions/index.tsx ← Coupons + Flash + Push
│       └── store/dashboard.tsx             ← Store owner dashboard
└── docker/
    ├── docker-compose.prod.yml             ← Phase 5: Production
    ├── nginx.prod.conf                     ← WebSocket + SignalR support
    └── .env.example
```

---

## 🚀 Quick Start (Development)

### Step 1: Database Setup
```bash
# Start existing MesterXPro database
cd docker && docker-compose up -d db

# Run migrations in order
psql -h localhost -U mallx -d mallxpro \
  -f ../database/phase1_mallx_migration.sql
psql -h localhost -U mallx -d mallxpro \
  -f ../database/phase2/phase2_commission_payments.sql
psql -h localhost -U mallx -d mallxpro \
  -f ../database/phase3/phase3_restaurant_booking_ratings.sql
psql -h localhost -U mallx -d mallxpro \
  -f ../database/phase4/phase4_loyalty_promotions_push.sql
```

### Step 2: Backend
```bash
cd MesterXPro/backend

# 1. Copy new files to backend project
cp -r /path/to/MallX/backend/Application/Services/ src/MesterX.Application/Services/Mall/
cp -r /path/to/MallX/backend/Domain/ src/MesterX.Domain/Entities/
cp -r /path/to/MallX/backend/Hubs/ src/MesterX.API/Hubs/
cp -r /path/to/MallX/backend/Infrastructure/Caching/ src/MesterX.Infrastructure/
cp -r /path/to/MallX/backend/Infrastructure/BackgroundJobs/Phase5Jobs.cs \
      src/MesterX.Infrastructure/BackgroundJobs/

# 2. Add NuGet packages
dotnet add src/MesterX.API package Microsoft.AspNetCore.SignalR.StackExchangeRedis
dotnet add src/MesterX.Infrastructure package StackExchange.Redis
dotnet add src/MesterX.Infrastructure package Microsoft.Extensions.Caching.StackExchangeRedis

# 3. Replace Program.cs
cp /path/to/MallX/backend/API/Program.cs src/MesterX.API/Program.cs

# 4. Run
dotnet run --project src/MesterX.API
```

### Step 3: Flutter App
```bash
cd MallX/flutter

# 1. Update server IP
sed -i 's/YOUR_SERVER_IP/192.168.1.xxx/g' lib/data/services/api_service.dart
sed -i 's/YOUR_SERVER_IP/192.168.1.xxx/g' lib/screens/tracking/realtime_tracking.dart

# 2. Install dependencies
flutter pub get

# 3. Run
flutter run
```

### Step 4: Frontend
```bash
cd MallX/frontend

# 1. Copy pages to existing Next.js project
cp -r pages/mall-admin/ ../MesterXPro/frontend/pages/
cp -r pages/store/ ../MesterXPro/frontend/pages/

# 2. Update API URL in .env.local
echo "NEXT_PUBLIC_API_URL=http://localhost:5000/api" > .env.local

# 3. Run
npm run dev
```

---

## 🔑 Key Configuration

### appsettings.json additions
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=mallxpro;Username=mallx;Password=xxx"
  },
  "Redis": {
    "ConnectionString": "localhost:6379",
    "Password": "xxx"
  },
  "Jwt": {
    "Secret": "64-char-secret",
    "Issuer": "mallxpro",
    "Audience": "mallxpro-client",
    "ExpiryMinutes": "60",
    "CustomerExpiryMinutes": "60"
  },
  "Paymob": {
    "ApiKey": "xxx",
    "IntegrationId": "xxx",
    "IframeId": "xxx",
    "HmacSecret": "xxx"
  },
  "Firebase": {
    "ServerKey": "xxx"
  },
  "AllowedOrigins": ["http://localhost:3000"]
}
```

---

## 📡 Complete API Reference

### Customer Endpoints (require Customer JWT)
| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/mall/auth/register` | Register new customer |
| POST | `/api/mall/auth/login` | Customer login |
| POST | `/api/mall/auth/refresh` | Refresh token |
| GET  | `/api/mall/auth/me` | Current customer |
| GET  | `/api/mall/cart` | Get cart |
| POST | `/api/mall/cart/items` | Add item |
| PUT  | `/api/mall/cart/items` | Update quantity |
| DELETE | `/api/mall/cart/items/{productId}` | Remove item |
| POST | `/api/mall/orders/checkout` | Place order |
| GET  | `/api/mall/orders` | Order history |
| GET  | `/api/mall/orders/{id}` | Order details |
| POST | `/api/mall/payments/initiate` | Start payment |
| POST | `/api/mall/payments/{id}/confirm-cash` | Confirm COD |
| GET  | `/api/mall/loyalty/wallet` | Loyalty wallet |
| GET  | `/api/mall/loyalty/history` | Points history |
| POST | `/api/mall/loyalty/redeem` | Redeem points |
| GET  | `/api/mall/promotions` | Active offers |
| POST | `/api/mall/promotions/coupon/apply` | Apply coupon |
| POST | `/api/mall/devices/register` | Register FCM token |
| POST | `/api/mall/geo/checkin` | Geo-fence check |

### Store & Restaurant Endpoints
| Method | Path | Description |
|--------|------|-------------|
| GET  | `/api/mall/stores/{id}/menu` | Restaurant menu |
| POST | `/api/mall/store/restaurant/menu` | Add menu item |
| PUT  | `/api/mall/store/restaurant/menu/{id}` | Update item |
| PATCH | `/api/mall/store/restaurant/menu/{id}/toggle` | Toggle availability |
| GET  | `/api/mall/store/restaurant/queue` | View queue |
| PATCH | `/api/mall/store/restaurant/queue/{id}/advance` | Advance ticket |
| GET  | `/api/mall/store/orders/incoming` | Incoming orders |
| PATCH | `/api/mall/store/orders/{id}/status` | Update order status |
| GET  | `/api/mall/store/financials` | Store financials |
| GET  | `/api/mall/store/bookings` | Day schedule |
| PATCH | `/api/mall/store/bookings/{id}/status` | Update booking |

### Booking & Rating Endpoints
| Method | Path | Description |
|--------|------|-------------|
| GET  | `/api/mall/bookings/stores/{id}/services` | List services |
| GET  | `/api/mall/bookings/stores/{id}/availability` | Available slots |
| POST | `/api/mall/bookings` | Create booking |
| GET  | `/api/mall/bookings/my` | My bookings |
| DELETE | `/api/mall/bookings/{id}` | Cancel booking |
| GET  | `/api/mall/ratings/stores/{id}` | Store rating summary |
| POST | `/api/mall/ratings` | Submit rating |
| POST | `/api/mall/ratings/{id}/reply` | Store reply |

### MallAdmin Endpoints
| Method | Path | Description |
|--------|------|-------------|
| GET  | `/api/mall/admin/dashboard` | KPIs |
| GET  | `/api/mall/admin/commission/report` | Revenue report |
| POST | `/api/mall/admin/commission/settlements` | Create settlement |
| POST | `/api/mall/admin/promotions/coupons` | Create coupon |
| POST | `/api/mall/admin/promotions/flash-sales` | Create flash sale |
| POST | `/api/mall/admin/campaigns` | Send push notification |

### SignalR Hubs (WebSocket)
| Hub | Path | Events |
|-----|------|--------|
| OrderTrackingHub | `/hubs/orders` | `OrderStatusChanged`, `DriverAssigned` |
| DriverLocationHub | `/hubs/drivers` | `DriverLocationUpdated` |

---

## 🔄 Background Jobs Schedule

| Job | Interval | Purpose |
|-----|----------|---------|
| `LoyaltyExpiryJob` | Daily @ 02:00 UTC | Expire inactive points (12 months) |
| `AnalyticsSnapshotJob` | Daily @ 23:55 UTC | Save daily KPI snapshot |
| `CampaignSchedulerJob` | Every 1 minute | Send scheduled push campaigns |
| `FlashSaleCleanupJob` | Every 5 minutes | Deactivate expired sales + coupons |
| `QueueCleanupJob` | Every 15 minutes | Cancel stale queue tickets (>2h) |
| `AIRecommendationJob` | Existing | AI recommendations (inherited) |

---

## 🏗️ Deployment (Production)

```bash
cd MallX/docker

# 1. Copy .env
cp .env.example .env
# Edit .env with real values

# 2. Launch
docker-compose -f docker-compose.prod.yml up -d --build

# 3. Check health
curl http://localhost/health

# 4. View logs
docker-compose -f docker-compose.prod.yml logs -f api
```

---

## 📊 What Was Built — Full Summary

| Phase | Features | Files | Lines |
|-------|----------|-------|-------|
| Phase 1 | Mall+Store setup, Customer Auth, Cart, Orders, Order Splitting | 8 | 2,100 |
| Phase 2 | Payments (Paymob+Cash), Commission, Store Dashboard, MallAdmin | 6 | 1,800 |
| Phase 3 | Restaurant Menu+Queue, Booking System, Ratings | 6 | 2,000 |
| Phase 4 | Loyalty Points+Tiers, Coupons, Flash Sales, Firebase FCM, Geo-fence | 6 | 2,400 |
| Phase 5 | SignalR Real-time, Redis Cache, 5 Background Jobs, Prod Docker | 7 | 1,200 |
| **Total** | **Full Multi-Vendor Mall App** | **45+** | **11,500+** |

---

*MallX — كل اللي محتاجه في مول واحد 🏬*
*Built on MesterXPro Foundation — Excellence86*
