using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MesterX.Application.Services.Mall;
using MesterX.Application.Services.Phase4;
using MesterX.Application.Services.Phase3;
using MesterX.Domain.Entities.Mall;
using MesterX.Domain.Entities.Phase3;
using MesterX.Domain.Entities.Phase4;
using MesterX.Infrastructure.Caching;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MallX.Tests;

// ──────────────────────────────────────────────────────────────────────────
//  TEST DATABASE FACTORY
// ──────────────────────────────────────────────────────────────────────────
public static class TestDb
{
    public static MesterXDbContext Create(string name = "")
    {
        var opts = new DbContextOptionsBuilder<MesterXDbContext>()
            .UseInMemoryDatabase(name.Length > 0 ? name : Guid.NewGuid().ToString())
            .Options;
        return new MesterXDbContext(opts);
    }

    public static (MallCustomer customer, Mall mall) Seed(MesterXDbContext db)
    {
        var mall = new Mall { Id = Guid.NewGuid(), Name = "Test Mall", Slug = "test" };
        var customer = new MallCustomer
        {
            Id           = Guid.NewGuid(),
            MallId       = mall.Id,
            FirstName    = "أحمد",
            LastName     = "محمد",
            Email        = "ahmed@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            LoyaltyPoints= 500,
            Tier         = CustomerTier.Bronze,
        };
        db.Malls.Add(mall);
        db.MallCustomers.Add(customer);
        db.SaveChanges();
        return (customer, mall);
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  CUSTOMER AUTH TESTS
// ──────────────────────────────────────────────────────────────────────────
public class CustomerAuthServiceTests
{
    private readonly Mock<Microsoft.Extensions.Configuration.IConfiguration> _config = new();
    private readonly Mock<ILogger<MallCustomerAuthService>> _log = new();

    [Fact]
    public async Task Register_WithNewEmail_ShouldSucceed()
    {
        using var db = TestDb.Create();
        var (_, mall) = TestDb.Seed(db);
        _config.Setup(c => c["Jwt:Secret"]).Returns("test-secret-key-minimum-64-chars-long-for-jwt-signing!!");
        _config.Setup(c => c["Jwt:Issuer"]).Returns("test");
        _config.Setup(c => c["Jwt:Audience"]).Returns("test");
        _config.Setup(c => c["Jwt:CustomerExpiryMinutes"]).Returns("60");

        var svc    = new MallCustomerAuthService(db, _config.Object, _log.Object);
        var result = await svc.RegisterAsync(new(
            "محمود", "علي", "mahmoud@test.com", "01012345678",
            "StrongPass123!", mall.Slug), "127.0.0.1");

        Assert.True(result.Success);
        Assert.NotNull(result.Data?.AccessToken);
        Assert.Equal("محمود", result.Data!.Customer.FirstName);
        Assert.Equal("Bronze", result.Data.Customer.Tier);
    }

    [Fact]
    public async Task Register_WithExistingEmail_ShouldFail()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);
        _config.Setup(c => c["Jwt:Secret"]).Returns("test-secret-key-minimum-64-chars-long-for-jwt-signing!!");
        _config.Setup(c => c["Jwt:Issuer"]).Returns("test");
        _config.Setup(c => c["Jwt:Audience"]).Returns("test");
        _config.Setup(c => c["Jwt:CustomerExpiryMinutes"]).Returns("60");

        var svc    = new MallCustomerAuthService(db, _config.Object, _log.Object);
        var result = await svc.RegisterAsync(new(
            "Test", "User", customer.Email, "01012345678",
            "Password123!", mall.Slug), "127.0.0.1");

        Assert.False(result.Success);
        Assert.Contains("مسجل", result.Error);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ShouldSucceed()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);
        _config.Setup(c => c["Jwt:Secret"]).Returns("test-secret-key-minimum-64-chars-long-for-jwt-signing!!");
        _config.Setup(c => c["Jwt:Issuer"]).Returns("test");
        _config.Setup(c => c["Jwt:Audience"]).Returns("test");
        _config.Setup(c => c["Jwt:CustomerExpiryMinutes"]).Returns("60");

        var svc    = new MallCustomerAuthService(db, _config.Object, _log.Object);
        var result = await svc.LoginAsync(
            new(customer.Email, "Password123!", mall.Slug),
            "127.0.0.1", "TestApp");

        Assert.True(result.Success);
        Assert.NotEmpty(result.Data!.AccessToken);
        Assert.NotEmpty(result.Data.RefreshToken);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShouldFail_AndIncrementAttempts()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);
        _config.Setup(c => c["Jwt:Secret"]).Returns("test-secret-key-minimum-64-chars-long-for-jwt-signing!!");
        _config.Setup(c => c["Jwt:Issuer"]).Returns("test");
        _config.Setup(c => c["Jwt:Audience"]).Returns("test");
        _config.Setup(c => c["Jwt:CustomerExpiryMinutes"]).Returns("60");

        var svc = new MallCustomerAuthService(db, _config.Object, _log.Object);
        await svc.LoginAsync(new(customer.Email, "WrongPass!", mall.Slug), "127.0.0.1", "UA");

        var updated = await db.MallCustomers.FindAsync(customer.Id);
        Assert.Equal(1, updated!.FailedAttempts);
    }

    [Fact]
    public async Task Login_AfterFiveFailedAttempts_ShouldLockAccount()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);
        _config.Setup(c => c["Jwt:Secret"]).Returns("test-secret-key-minimum-64-chars-long-for-jwt-signing!!");
        _config.Setup(c => c["Jwt:Issuer"]).Returns("test");
        _config.Setup(c => c["Jwt:Audience"]).Returns("test");
        _config.Setup(c => c["Jwt:CustomerExpiryMinutes"]).Returns("60");

        var svc = new MallCustomerAuthService(db, _config.Object, _log.Object);
        for (int i = 0; i < 5; i++)
            await svc.LoginAsync(new(customer.Email, "WrongPass!", mall.Slug), "127.0.0.1", "UA");

        var result = await svc.LoginAsync(new(customer.Email, "Password123!", mall.Slug), "127.0.0.1", "UA");
        Assert.False(result.Success);
        Assert.Contains("مقفل", result.Error);
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  CART SERVICE TESTS
// ──────────────────────────────────────────────────────────────────────────
public class CartServiceTests
{
    private readonly Mock<ILogger<CartService>> _log = new();

    private (MesterXDbContext db, MallCustomer customer, Mall mall,
             Domain.Entities.Core.Tenant store, Domain.Entities.Core.Product product)
    SetupCartData()
    {
        var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);

        var store = new Domain.Entities.Core.Tenant
        {
            Id   = Guid.NewGuid(), Name = "Test Store",
            Slug = "test-store", IsActive = true, IsDeleted = false,
        };
        var product = new Domain.Entities.Core.Product
        {
            Id = Guid.NewGuid(), TenantId = store.Id, Name = "Test Product",
            SalePrice = 100m, Sku = "SKU001", IsActive = true, IsDeleted = false,
        };
        var stockItem = new Domain.Entities.Core.StockItem
        {
            Id = Guid.NewGuid(), TenantId = store.Id, BranchId = Guid.NewGuid(),
            ProductId = product.Id, Quantity = 50,
        };

        db.Tenants.Add(store);
        db.Products.Add(product);
        db.StockItems.Add(stockItem);
        db.SaveChanges();

        return (db, customer, mall, store, product);
    }

    [Fact]
    public async Task AddItem_WhenProductExists_ShouldCreateCart()
    {
        var (db, customer, mall, store, product) = SetupCartData();
        var svc    = new CartService(db, _log.Object);
        var result = await svc.AddItemAsync(customer.Id, mall.Id, new(product.Id, store.Id, 2, null));

        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.Stores.Count);
        Assert.Equal(2, result.Data.ItemCount);
        Assert.Equal(200m, result.Data.Subtotal);
    }

    [Fact]
    public async Task AddItem_SameProductTwice_ShouldIncrementQuantity()
    {
        var (db, customer, mall, store, product) = SetupCartData();
        var svc = new CartService(db, _log.Object);
        await svc.AddItemAsync(customer.Id, mall.Id, new(product.Id, store.Id, 1, null));
        var result = await svc.AddItemAsync(customer.Id, mall.Id, new(product.Id, store.Id, 2, null));

        Assert.True(result.Success);
        Assert.Equal(3, result.Data!.ItemCount);
    }

    [Fact]
    public async Task AddItem_WhenQuantityExceedsStock_ShouldFail()
    {
        var (db, customer, mall, store, product) = SetupCartData();
        var svc    = new CartService(db, _log.Object);
        var result = await svc.AddItemAsync(customer.Id, mall.Id, new(product.Id, store.Id, 100, null));

        Assert.False(result.Success);
        Assert.Contains("غير متوفرة", result.Error);
    }

    [Fact]
    public async Task RemoveItem_ShouldReduceCartCount()
    {
        var (db, customer, mall, store, product) = SetupCartData();
        var svc = new CartService(db, _log.Object);
        await svc.AddItemAsync(customer.Id, mall.Id, new(product.Id, store.Id, 2, null));
        var result = await svc.RemoveItemAsync(customer.Id, product.Id);

        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.ItemCount);
    }

    [Fact]
    public async Task GetCart_WhenEmpty_ShouldReturnEmptyDto()
    {
        var (db, customer, _, _, _) = SetupCartData();
        var svc    = new CartService(db, _log.Object);
        var result = await svc.GetCartAsync(customer.Id);

        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.ItemCount);
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  LOYALTY SERVICE TESTS
// ──────────────────────────────────────────────────────────────────────────
public class LoyaltyServiceTests
{
    private readonly Mock<ILogger<LoyaltyService>> _log = new();

    [Fact]
    public async Task EarnPoints_AfterOrder_ShouldCreditAccount()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);
        var svc = new LoyaltyService(db, _log.Object);

        var order = new MallOrder
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, MallId = mall.Id,
            OrderNumber = "MX-001", Total = 500m, Subtotal = 500m,
            Status = MallOrderStatus.Delivered,
        };
        db.MallOrders.Add(order);
        await db.SaveChangesAsync();

        await svc.EarnPointsAsync(new(customer.Id, order.Id, 500m, mall.Id));

        var account = await db.Set<LoyaltyAccount>()
            .FirstOrDefaultAsync(a => a.CustomerId == customer.Id);
        Assert.NotNull(account);
        Assert.Equal(500, account!.LifetimePoints);  // 1 pt per EGP
    }

    [Fact]
    public async Task EarnPoints_SilverTier_ShouldApply1_5Multiplier()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);
        customer.Tier  = CustomerTier.Silver;
        customer.LoyaltyPoints = 1000;
        await db.SaveChangesAsync();

        var svc = new LoyaltyService(db, _log.Object);
        var order = new MallOrder
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, MallId = mall.Id,
            OrderNumber = "MX-002", Total = 100m, Subtotal = 100m,
            Status = MallOrderStatus.Delivered,
        };
        db.MallOrders.Add(order);
        await db.SaveChangesAsync();

        await svc.EarnPointsAsync(new(customer.Id, order.Id, 100m, mall.Id));

        var account = await db.Set<LoyaltyAccount>()
            .FirstOrDefaultAsync(a => a.CustomerId == customer.Id);
        Assert.Equal(150, account!.LifetimePoints - 1000); // 100 × 1.5
    }

    [Fact]
    public async Task EarnPoints_WhenCrossingTierThreshold_ShouldUpgradeTier()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);
        customer.LoyaltyPoints = 900;
        await db.SaveChangesAsync();

        var svc   = new LoyaltyService(db, _log.Object);
        var order = new MallOrder
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, MallId = mall.Id,
            OrderNumber = "MX-003", Total = 200m, Subtotal = 200m,
            Status = MallOrderStatus.Delivered,
        };
        db.MallOrders.Add(order);
        await db.SaveChangesAsync();

        await svc.EarnPointsAsync(new(customer.Id, order.Id, 200m, mall.Id));

        var account = await db.Set<LoyaltyAccount>()
            .FirstOrDefaultAsync(a => a.CustomerId == customer.Id);
        Assert.Equal("Silver", account!.Tier); // crossed 1000
    }

    [Fact]
    public async Task RedeemPoints_Max20PercentOfOrder_ShouldApplyCorrectly()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);
        customer.LoyaltyPoints = 5000; // 50 EGP value
        await db.SaveChangesAsync();

        var order = new MallOrder
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, MallId = mall.Id,
            OrderNumber = "MX-004", Total = 100m, Subtotal = 100m, DeliveryFee = 0,
            Status = MallOrderStatus.Placed,
        };
        db.MallOrders.Add(order);
        await db.SaveChangesAsync();

        var svc    = new LoyaltyService(db, _log.Object);
        var result = await svc.RedeemAsync(customer.Id,
            new(order.Id, 5000)); // try to use all 5000

        Assert.True(result.Success);
        Assert.Equal(20m, result.Data!.DiscountApplied); // max 20% of 100 = 20 EGP
        Assert.Equal(2000, result.Data.PointsUsed);      // 20 EGP = 2000 pts
    }

    [Fact]
    public async Task GetWallet_NewCustomer_ShouldCreateAccount()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);
        var svc    = new LoyaltyService(db, _log.Object);
        var result = await svc.GetWalletAsync(customer.Id, mall.Id);

        Assert.True(result.Success);
        Assert.Equal("Bronze", result.Data!.Tier);
        Assert.Equal("برونزي 🥉", result.Data.TierAr);
        Assert.NotNull(result.Data.Benefits);
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  COUPON SERVICE TESTS
// ──────────────────────────────────────────────────────────────────────────
public class CouponTests
{
    private readonly Mock<IConfiguration> _config = new();
    private readonly Mock<IHttpClientFactory> _http  = new();
    private readonly Mock<ILogger<Application.Services.Phase4.PromotionService>> _log = new();

    [Fact]
    public async Task ApplyCoupon_ValidCode_ShouldApplyDiscount()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);

        var coupon = new Coupon
        {
            Id = Guid.NewGuid(), MallId = mall.Id,
            Code = "TEST10", Name = "Test 10%",
            DiscountType = DiscountType.Percentage, DiscountValue = 10,
            ValidTo = DateTime.UtcNow.AddDays(7), Status = CouponStatus.Active,
        };
        db.Set<Coupon>().Add(coupon);

        var order = new MallOrder
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, MallId = mall.Id,
            OrderNumber = "MX-005", Total = 200m, Subtotal = 200m,
            Status = MallOrderStatus.Placed, DeliveryFee = 0,
        };
        db.MallOrders.Add(order);
        await db.SaveChangesAsync();

        var svc    = new Application.Services.Phase4.PromotionService(db, _config.Object, _http.Object, _log.Object);
        var result = await svc.ApplyCouponAsync(customer.Id, new("TEST10", order.Id));

        Assert.True(result.Success);
        Assert.Equal(20m, result.Data!.DiscountAmt);  // 10% of 200
        Assert.Equal(180m, result.Data.NewTotal);
    }

    [Fact]
    public async Task ApplyCoupon_ExpiredCode_ShouldFail()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);

        var expired = new Coupon
        {
            Id = Guid.NewGuid(), MallId = mall.Id,
            Code = "EXPIRED", Name = "Expired",
            DiscountType = DiscountType.FixedAmount, DiscountValue = 50,
            ValidTo = DateTime.UtcNow.AddDays(-1), Status = CouponStatus.Active,
        };
        db.Set<Coupon>().Add(expired);
        await db.SaveChangesAsync();

        var svc    = new Application.Services.Phase4.PromotionService(db, _config.Object, _http.Object, _log.Object);
        var result = await svc.ApplyCouponAsync(customer.Id, new("EXPIRED", Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Contains("غير صالح", result.Error);
    }

    [Fact]
    public async Task ApplyCoupon_UsedMoreThanAllowed_ShouldFail()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);

        var coupon = new Coupon
        {
            Id = Guid.NewGuid(), MallId = mall.Id,
            Code = "ONCE", Name = "Once Only",
            DiscountType = DiscountType.FixedAmount, DiscountValue = 10,
            ValidTo = DateTime.UtcNow.AddDays(7), Status = CouponStatus.Active,
            UsesPerCustomer = 1,
        };
        db.Set<Coupon>().Add(coupon);

        // Record a previous use
        var prevOrder = new MallOrder
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, MallId = mall.Id,
            OrderNumber = "MX-006", Total = 100m, Subtotal = 100m,
            Status = MallOrderStatus.Delivered, DeliveryFee = 0,
        };
        db.MallOrders.Add(prevOrder);
        db.Set<CouponUse>().Add(new CouponUse
        {
            CouponId = coupon.Id, CustomerId = customer.Id,
            MallOrderId = prevOrder.Id, DiscountAmt = 10,
        });
        await db.SaveChangesAsync();

        var newOrder = new MallOrder
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, MallId = mall.Id,
            OrderNumber = "MX-007", Total = 100m, Subtotal = 100m,
            Status = MallOrderStatus.Placed, DeliveryFee = 0,
        };
        db.MallOrders.Add(newOrder);
        await db.SaveChangesAsync();

        var svc    = new Application.Services.Phase4.PromotionService(db, _config.Object, _http.Object, _log.Object);
        var result = await svc.ApplyCouponAsync(customer.Id, new("ONCE", newOrder.Id));

        Assert.False(result.Success);
        Assert.Contains("استخدمت", result.Error);
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  RATING SERVICE TESTS
// ──────────────────────────────────────────────────────────────────────────
public class RatingServiceTests
{
    private readonly Mock<ILogger<RatingService>> _log = new();

    [Fact]
    public async Task SubmitRating_AfterDeliveredOrder_ShouldSucceed()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);

        var store = new Domain.Entities.Core.Tenant
        {
            Id = Guid.NewGuid(), Name = "Food Store",
            Slug = "food-store", IsActive = true,
        };
        var order = new MallOrder
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, MallId = mall.Id,
            OrderNumber = "MX-008", Total = 150m, Subtotal = 150m,
            Status = MallOrderStatus.Delivered, DeliveryFee = 0,
        };
        db.Tenants.Add(store);
        db.MallOrders.Add(order);
        await db.SaveChangesAsync();

        var svc    = new RatingService(db, _log.Object);
        var result = await svc.SubmitAsync(customer.Id, new(
            order.Id, null, store.Id, "Store", null,
            5, "رائع!", "خدمة ممتازة", false));

        Assert.True(result.Success);
        Assert.Equal(5, result.Data!.Stars);

        // Should have awarded 5 loyalty points
        var updated = await db.MallCustomers.FindAsync(customer.Id);
        Assert.Equal(505, updated!.LoyaltyPoints);
    }

    [Fact]
    public async Task SubmitRating_DuplicateForSameOrder_ShouldFail()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);

        var store = new Domain.Entities.Core.Tenant
        {
            Id = Guid.NewGuid(), Name = "Retail", Slug = "retail", IsActive = true,
        };
        var order = new MallOrder
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, MallId = mall.Id,
            OrderNumber = "MX-009", Total = 100m, Subtotal = 100m,
            Status = MallOrderStatus.Delivered, DeliveryFee = 0,
        };
        db.Tenants.Add(store);
        db.MallOrders.Add(order);
        db.Set<Rating>().Add(new Rating
        {
            CustomerId = customer.Id, StoreId = store.Id,
            MallOrderId = order.Id, Subject = RatingSubject.Store, Stars = 4,
        });
        await db.SaveChangesAsync();

        var svc    = new RatingService(db, _log.Object);
        var result = await svc.SubmitAsync(customer.Id,
            new(order.Id, null, store.Id, "Store", null, 5, null, null, false));

        Assert.False(result.Success);
        Assert.Contains("مسبقاً", result.Error);
    }

    [Fact]
    public async Task GetStoreSummary_AfterRatings_ShouldReturnCorrectAverage()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);

        var store = new Domain.Entities.Core.Tenant
        {
            Id = Guid.NewGuid(), Name = "My Store", Slug = "my-store", IsActive = true,
        };
        db.Tenants.Add(store);

        db.Set<StoreRatingSummary>().Add(new StoreRatingSummary
        {
            StoreId = store.Id, AvgStars = 4.5m, TotalRatings = 10,
            FiveStar = 6, FourStar = 3, ThreeStar = 1,
        });
        await db.SaveChangesAsync();

        var svc    = new RatingService(db, _log.Object);
        var result = await svc.GetStoreSummaryAsync(store.Id);

        Assert.True(result.Success);
        Assert.Equal(4.5m, result.Data!.AvgStars);
        Assert.Equal(10, result.Data.TotalRatings);
        Assert.Equal(6, result.Data.Breakdown[5]);
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  BOOKING SERVICE TESTS
// ──────────────────────────────────────────────────────────────────────────
public class BookingServiceTests
{
    private readonly Mock<ILogger<BookingService>> _log = new();

    [Fact]
    public async Task CreateBooking_WhenSlotFree_ShouldSucceed()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);

        var store = new Domain.Entities.Core.Tenant
        {
            Id = Guid.NewGuid(), Name = "Salon", Slug = "salon", IsActive = true,
        };
        var staff = new ServiceStaff
        {
            Id = Guid.NewGuid(), StoreId = store.Id, Name = "Sara", IsActive = true,
        };
        var service = new Service
        {
            Id = Guid.NewGuid(), StoreId = store.Id, Name = "Haircut",
            DurationMin = 30, Price = 80m, IsActive = true,
        };
        var wh = new WorkingHour
        {
            Id = Guid.NewGuid(), StaffId = staff.Id, DayOfWeek = 0, // Sunday
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0), IsActive = true,
        };
        db.Tenants.Add(store);
        db.Set<ServiceStaff>().Add(staff);
        db.Set<Service>().Add(service);
        db.Set<WorkingHour>().Add(wh);
        await db.SaveChangesAsync();

        var svc    = new BookingService(db, _log.Object);
        var result = await svc.CreateBookingAsync(customer.Id, new(
            store.Id, service.Id, staff.Id,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            new TimeOnly(10, 0), null));

        Assert.True(result.Success);
        Assert.NotEmpty(result.Data!.BookingRef);
        Assert.StartsWith("BK-", result.Data.BookingRef);
    }

    [Fact]
    public async Task CancelBooking_OwnedByCustomer_ShouldSucceed()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);

        var store   = new Domain.Entities.Core.Tenant { Id = Guid.NewGuid(), Name = "Gym", Slug = "gym", IsActive = true };
        var service = new Service { Id = Guid.NewGuid(), StoreId = store.Id, Name = "PT", DurationMin = 60, Price = 100m };
        var booking = new Booking
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, StoreId = store.Id,
            ServiceId = service.Id, BookingRef = "BK-TEST-001",
            BookedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime  = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0),
            Price = 100m, Status = BookingStatus.Confirmed,
        };
        db.Tenants.Add(store); db.Set<Service>().Add(service); db.Set<Booking>().Add(booking);
        await db.SaveChangesAsync();

        var svc    = new BookingService(db, _log.Object);
        var result = await svc.CancelBookingAsync(customer.Id, booking.Id, "تغيير الموعد");

        Assert.True(result.Success);
        var updated = await db.Set<Booking>().FindAsync(booking.Id);
        Assert.Equal(BookingStatus.Cancelled, updated!.Status);
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  INTEGRATION-STYLE TESTS
// ──────────────────────────────────────────────────────────────────────────
public class OrderFlowIntegrationTests
{
    [Fact]
    public async Task FullOrderFlow_CartToCheckout_ShouldSplitByStore()
    {
        using var db = TestDb.Create();
        var (customer, mall) = TestDb.Seed(db);

        // Two stores
        var store1 = new Domain.Entities.Core.Tenant { Id = Guid.NewGuid(), Name = "Store1", Slug = "s1", IsActive = true };
        var store2 = new Domain.Entities.Core.Tenant { Id = Guid.NewGuid(), Name = "Store2", Slug = "s2", IsActive = true };

        var prod1  = new Domain.Entities.Core.Product { Id = Guid.NewGuid(), TenantId = store1.Id, Name = "Product1", SalePrice = 100m, Sku = "S1P1" };
        var prod2  = new Domain.Entities.Core.Product { Id = Guid.NewGuid(), TenantId = store2.Id, Name = "Product2", SalePrice = 200m, Sku = "S2P1" };

        var stock1 = new Domain.Entities.Core.StockItem { Id = Guid.NewGuid(), TenantId = store1.Id, BranchId = Guid.NewGuid(), ProductId = prod1.Id, Quantity = 20 };
        var stock2 = new Domain.Entities.Core.StockItem { Id = Guid.NewGuid(), TenantId = store2.Id, BranchId = Guid.NewGuid(), ProductId = prod2.Id, Quantity = 20 };

        db.Tenants.AddRange(store1, store2);
        db.Products.AddRange(prod1, prod2);
        db.StockItems.AddRange(stock1, stock2);
        await db.SaveChangesAsync();

        // Add to cart
        var cartSvc = new CartService(db, new Mock<ILogger<CartService>>().Object);
        await cartSvc.AddItemAsync(customer.Id, mall.Id, new(prod1.Id, store1.Id, 1, null));
        await cartSvc.AddItemAsync(customer.Id, mall.Id, new(prod2.Id, store2.Id, 2, null));

        // Checkout
        var orderSvc = new MallOrderService(db, new Mock<ILogger<MallOrderService>>().Object);
        var result   = await orderSvc.CheckoutAsync(customer.Id, new(
            "Delivery", "Cash", "123 Test St", null, null, null, null));

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.StoreOrders.Count); // 2 store orders
        Assert.Equal(500m, result.Data.Subtotal);        // 100 + (200×2)
        Assert.Equal(515m, result.Data.Total);           // + 15 delivery

        // Verify stock deducted
        var updatedStock1 = await db.StockItems.FindAsync(stock1.Id);
        Assert.Equal(19, updatedStock1!.Quantity);

        // Verify cart cleared
        var cart = await db.Carts.FirstOrDefaultAsync(c => c.CustomerId == customer.Id);
        var cartItems = cart != null
            ? await db.CartItems.Where(i => i.CartId == cart.Id).ToListAsync()
            : new List<CartItem>();
        Assert.Empty(cartItems);
    }
}
