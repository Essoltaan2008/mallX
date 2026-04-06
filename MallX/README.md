# 🏬 MallX — Multi-Vendor Mall Super App

> **بُني فوق MesterXPro v2** | ASP.NET Core 8 + Flutter + Next.js

---

## 🏗️ Architecture Overview

```
MallX/
├── backend/                    ← ASP.NET Core 8 API
│   ├── API/                    ← Controllers + Middleware + Program.cs
│   ├── Application/            ← Services + DTOs (business logic)
│   ├── Domain/                 ← Entities + Enums
│   ├── Infrastructure/         ← DbContext + Background Jobs
│   └── Hubs/                   ← SignalR Hubs
├── database/
│   ├── phase1_mallx_migration.sql    ← Mall + Customer + Cart + Orders
│   ├── phase2/phase2_*.sql           ← Payment + Commission + Delivery
│   ├── phase3/phase3_*.sql           ← Restaurant + Booking + Ratings
│   └── phase4/phase4_*.sql           ← Loyalty + Promotions + Push + Geo
├── flutter/                    ← Flutter Customer App
│   └── lib/
│       ├── screens/            ← All screens
│       ├── providers/          ← State management
│       └── data/               ← Models + API service
├── frontend/                   ← Next.js Admin + Store Dashboard
│   └── pages/
│       ├── mall-admin/         ← KPIs + Commission + Promotions
│       └── store/              ← Store Owner Dashboard
└── docker/
    └── production/             ← Production Docker Compose + Nginx
```

---

## 🚀 Quick Start

### 1. Environment Setup
```bash
cp .env.example .env
# Edit .env with your values
```

### 2. Run with Docker (Production)
```bash
cd docker/production
docker compose -f docker-compose.prod.yml up -d --build
```

### 3. Run Locally (Development)
```bash
# Backend
cd backend
dotnet run --project src/MesterX.API

# Frontend
cd frontend && npm install && npm run dev

# Flutter App
cd flutter && flutter pub get && flutter run
```

### 4. Database Migration
```bash
# Run all migrations in order
psql -d mallxpro -f database/phase1_mallx_migration.sql
psql -d mallxpro -f database/phase2/phase2_commission_payments.sql
psql -d mallxpro -f database/phase3/phase3_restaurant_booking_ratings.sql
psql -d mallxpro -f database/phase4/phase4_loyalty_promotions_push.sql
```

---

## 📡 API Endpoints Summary

### 🔐 Customer Auth
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/mall/auth/register` | تسجيل عميل جديد |
| POST | `/api/mall/auth/login` | تسجيل دخول |
| POST | `/api/mall/auth/refresh` | تجديد التوكن |
| POST | `/api/mall/auth/logout` | تسجيل خروج |
| GET  | `/api/mall/auth/me` | بيانات العميل |

### 🛒 Cart
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET    | `/api/mall/cart` | السلة الحالية |
| POST   | `/api/mall/cart/items` | إضافة منتج |
| PUT    | `/api/mall/cart/items` | تعديل كمية |
| DELETE | `/api/mall/cart/items/{id}` | حذف منتج |
| DELETE | `/api/mall/cart` | تفريغ السلة |

### 📦 Orders
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/mall/orders/checkout` | إتمام الطلب |
| GET  | `/api/mall/orders/{id}` | تفاصيل طلب |
| GET  | `/api/mall/orders` | سجل الطلبات |

### 💳 Payments
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/mall/payments/initiate` | بدء الدفع |
| POST | `/api/mall/payments/{id}/confirm-cash` | تأكيد نقدي |
| POST | `/api/webhooks/paymob` | Paymob webhook |

### 🍔 Restaurant
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET  | `/api/mall/stores/{id}/menu` | منيو المطعم |
| GET  | `/api/mall/stores/{id}/menu/queue/{tid}` | تتبع التذكرة |
| POST | `/api/mall/store/restaurant/menu` | إضافة صنف |
| GET  | `/api/mall/store/restaurant/queue` | طابور المطعم |
| PATCH | `/api/mall/store/restaurant/queue/{id}/advance` | تقديم التذكرة |

### 📅 Booking
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET  | `/api/mall/bookings/stores/{id}/services` | الخدمات المتاحة |
| GET  | `/api/mall/bookings/stores/{id}/availability` | المواعيد المتاحة |
| POST | `/api/mall/bookings` | إنشاء حجز |
| GET  | `/api/mall/bookings/my` | حجوزاتي |
| DELETE | `/api/mall/bookings/{id}` | إلغاء حجز |

### ⭐ Ratings
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET  | `/api/mall/ratings/stores/{id}` | ملخص التقييمات |
| GET  | `/api/mall/ratings/stores/{id}/list` | قائمة التقييمات |
| POST | `/api/mall/ratings` | إرسال تقييم |
| POST | `/api/mall/ratings/{id}/reply` | رد المحل |

### ⭐ Loyalty
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET  | `/api/mall/loyalty/wallet` | محفظة النقاط |
| GET  | `/api/mall/loyalty/history` | سجل المعاملات |
| POST | `/api/mall/loyalty/redeem` | استبدال نقاط |

### 🎯 Promotions
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET  | `/api/mall/promotions` | العروض النشطة |
| POST | `/api/mall/promotions/coupon/apply` | تطبيق كوبون |
| POST | `/api/mall/devices/register` | تسجيل FCM |
| POST | `/api/mall/geo/checkin` | Geo check-in |

### 🏪 Store Owner
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET   | `/api/mall/store/orders/incoming` | الطلبات الواردة |
| PATCH | `/api/mall/store/orders/{id}/status` | تحديث حالة طلب |
| GET   | `/api/mall/store/financials` | المالية والعمولات |
| GET   | `/api/mall/store/bookings` | جدول الحجوزات |

### 🏬 Mall Admin
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET  | `/api/mall/admin/dashboard` | لوحة التحكم |
| GET  | `/api/mall/admin/commission/report` | تقرير العمولات |
| POST | `/api/mall/admin/commission/settlements` | إنشاء تسوية |
| POST | `/api/mall/admin/promotions/coupons` | إنشاء كوبون |
| POST | `/api/mall/admin/promotions/flash-sales` | إنشاء فلاش سيل |
| POST | `/api/mall/admin/campaigns` | إرسال إشعار جماعي |

---

## ⚡ SignalR Hub

**Endpoint:** `wss://your-domain.com/hubs/mall-order?access_token=JWT`

### Client → Server Methods
| Method | Params | Description |
|--------|--------|-------------|
| `TrackOrder` | `orderId` | انضم لمجموعة تتبع الطلب |
| `UpdateDriverLocation` | `orderId, lat, lng, heading, speed` | تحديث موقع السائق |
| `MarkPickedUp` | `orderId` | تأكيد استلام الطلب |
| `MarkDelivered` | `orderId` | تأكيد التسليم |

### Server → Client Events
| Event | Data | Description |
|-------|------|-------------|
| `OrderStatus` | `{orderId, status, message}` | تغيير حالة الطلب |
| `DriverLocation` | `{lat, lng, heading, speed}` | موقع السائق |
| `NewOrder` | `{...orderData}` | طلب جديد للمحل |
| `TicketReady` | `{ticketNumber, ticketId}` | تذكرة جاهزة |

---

## 🎯 Flutter Connection

```dart
// In api_service.dart — update base URL:
static const String baseUrl  = 'https://your-api-domain.com/api';
static const String mallSlug = 'your-mall-slug';

// SignalR (requires signalr_netcore package):
// hub.connect('wss://your-domain.com/hubs/mall-order?access_token=$token');
```

---

## 📊 Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | ASP.NET Core 8 |
| Database | PostgreSQL 16 + pg_trgm |
| Cache | Redis 7 |
| Real-time | SignalR (WebSockets) |
| Push | Firebase FCM |
| Payments | Paymob / Cash |
| Frontend | Next.js 14 + TailwindCSS |
| Mobile | Flutter 3.19+ (Dart) |
| Proxy | Nginx 1.25 |
| Deploy | Docker Compose → Kubernetes |

---

## 📈 Project Stats

| Metric | Value |
|--------|-------|
| Total Files | 45+ |
| Total Lines | 12,000+ |
| API Endpoints | 45+ |
| Database Tables | 35+ |
| Background Jobs | 5 |
| Flutter Screens | 12 |

---

*MallX — كل اللي محتاجه في مول واحد* 🏬
