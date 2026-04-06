using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MesterX.Application.DTOs;
using MesterX.Domain.Entities.Phase4;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MesterX.Application.Services.Phase4;

// ──────────────────────────────────────────────────────────────────────────
//  PROMOTIONS DTOs
// ──────────────────────────────────────────────────────────────────────────
public record ActivePromotionsDto
{
    public List<CouponDto>    Coupons    { get; init; } = [];
    public List<FlashSaleDto> FlashSales { get; init; } = [];
}

public record CouponDto
{
    public Guid    Id            { get; init; }
    public string  Code          { get; init; } = string.Empty;
    public string  Name          { get; init; } = string.Empty;
    public string? Description   { get; init; }
    public string  DiscountType  { get; init; } = string.Empty;
    public decimal DiscountValue { get; init; }
    public decimal MinOrderValue { get; init; }
    public decimal? MaxDiscount  { get; init; }
    public string? MinTier       { get; init; }
    public string  ValidTo       { get; init; } = string.Empty;
    public bool    IsExpiringSoon{ get; init; }
}

public record FlashSaleDto
{
    public Guid    Id            { get; init; }
    public string  Title         { get; init; } = string.Empty;
    public string? TitleAr       { get; init; }
    public decimal OriginalPrice { get; init; }
    public decimal FlashPrice    { get; init; }
    public double  DiscountPct   { get; init; }
    public int     Remaining     { get; init; }
    public string  EndsAt        { get; init; } = string.Empty;
    public int     SecondsLeft   { get; init; }
    public string? BannerUrl     { get; init; }
    public bool    IsLive        { get; init; }
}

public record ApplyCouponRequest(string Code, Guid MallOrderId);
public record ApplyCouponResult
{
    public Guid    CouponId      { get; init; }
    public string  Code          { get; init; } = string.Empty;
    public decimal DiscountAmt   { get; init; }
    public decimal NewTotal      { get; init; }
    public string  Message       { get; init; } = string.Empty;
}

public record CreateCouponRequest(
    string Code, string Name, string? Description,
    string DiscountType, decimal DiscountValue,
    decimal MinOrderValue, decimal? MaxDiscount,
    int? MaxUses, int UsesPerCustomer,
    string? MinTier, Guid? StoreId, DateTime ValidTo
);

public record CreateFlashSaleRequest(
    string Title, string? TitleAr, Guid? ProductId,
    decimal? OriginalPrice, decimal FlashPrice,
    int QuantityLimit, DateTime StartsAt, DateTime EndsAt,
    string? BannerUrl, Guid? StoreId
);

// Push notification DTOs
public record SendCampaignRequest(
    string Title, string? TitleAr, string Body, string? BodyAr,
    string? ImageUrl, string Target, string? ActionType, string? ActionId,
    DateTime? ScheduledAt
);

public record RegisterDeviceRequest(string FcmToken, string Platform, string? DeviceName);

public record GeoCheckInRequest(decimal Lat, decimal Lng);

// ──────────────────────────────────────────────────────────────────────────
//  PROMOTIONS SERVICE
// ──────────────────────────────────────────────────────────────────────────
public interface IPromotionService
{
    Task<ApiResponse<ActivePromotionsDto>> GetActivePromotionsAsync(Guid mallId, Guid customerId, CancellationToken ct = default);
    Task<ApiResponse<ApplyCouponResult>>   ApplyCouponAsync(Guid customerId, ApplyCouponRequest req, CancellationToken ct = default);
    Task<ApiResponse<CouponDto>>           CreateCouponAsync(Guid mallId, Guid createdBy, CreateCouponRequest req, CancellationToken ct = default);
    Task<ApiResponse<FlashSaleDto>>        CreateFlashSaleAsync(Guid mallId, CreateFlashSaleRequest req, CancellationToken ct = default);
    Task<ApiResponse>                      RegisterDeviceAsync(Guid customerId, RegisterDeviceRequest req, CancellationToken ct = default);
    Task<ApiResponse>                      SendCampaignAsync(Guid mallId, Guid createdBy, SendCampaignRequest req, CancellationToken ct = default);
    Task<ApiResponse>                      HandleGeoCheckInAsync(Guid customerId, Guid mallId, GeoCheckInRequest req, CancellationToken ct = default);
}

public class PromotionService : IPromotionService
{
    private readonly MesterXDbContext    _db;
    private readonly IConfiguration     _config;
    private readonly IHttpClientFactory  _http;
    private readonly ILogger<PromotionService> _log;

    public PromotionService(MesterXDbContext db, IConfiguration config,
        IHttpClientFactory http, ILogger<PromotionService> log)
    { _db = db; _config = config; _http = http; _log = log; }

    // ─── GET ACTIVE PROMOTIONS ────────────────────────────────────────────
    public async Task<ApiResponse<ActivePromotionsDto>> GetActivePromotionsAsync(
        Guid mallId, Guid customerId, CancellationToken ct = default)
    {
        var customer = await _db.MallCustomers.FindAsync([customerId], ct);
        var tier     = customer?.Tier.ToString() ?? "Bronze";

        var coupons = await _db.Set<Coupon>()
            .AsNoTracking()
            .Where(c => c.MallId == mallId
                && c.Status == CouponStatus.Active
                && DateTime.UtcNow >= c.ValidFrom
                && DateTime.UtcNow <= c.ValidTo
                && (c.MinTier == null || CompareTier(tier, c.MinTier!) >= 0))
            .OrderBy(c => c.ValidTo)
            .Take(20)
            .ToListAsync(ct);

        var flash = await _db.Set<FlashSale>()
            .AsNoTracking()
            .Where(f => f.MallId == mallId && f.IsActive
                && DateTime.UtcNow >= f.StartsAt && DateTime.UtcNow <= f.EndsAt
                && f.QuantitySold < f.QuantityLimit)
            .OrderBy(f => f.EndsAt)
            .Take(10)
            .ToListAsync(ct);

        return ApiResponse<ActivePromotionsDto>.Ok(new ActivePromotionsDto
        {
            Coupons    = coupons.Select(MapCoupon).ToList(),
            FlashSales = flash.Select(MapFlash).ToList(),
        });
    }

    // ─── APPLY COUPON ─────────────────────────────────────────────────────
    public async Task<ApiResponse<ApplyCouponResult>> ApplyCouponAsync(
        Guid customerId, ApplyCouponRequest req, CancellationToken ct = default)
    {
        var coupon = await _db.Set<Coupon>()
            .FirstOrDefaultAsync(c => c.Code == req.Code.ToUpperInvariant(), ct);

        if (coupon == null || !coupon.IsValid)
            return ApiResponse<ApplyCouponResult>.Fail("الكوبون غير صالح أو منتهي الصلاحية.");

        var order = await _db.MallOrders
            .FirstOrDefaultAsync(o => o.Id == req.MallOrderId && o.CustomerId == customerId, ct);
        if (order == null)
            return ApiResponse<ApplyCouponResult>.Fail("الطلب غير موجود.");

        if (order.Total < coupon.MinOrderValue)
            return ApiResponse<ApplyCouponResult>.Fail(
                $"الحد الأدنى للطلب لاستخدام هذا الكوبون هو {coupon.MinOrderValue} ج.م.");

        // Check per-customer uses
        var customerUses = await _db.Set<CouponUse>()
            .CountAsync(u => u.CouponId == coupon.Id && u.CustomerId == customerId, ct);
        if (customerUses >= coupon.UsesPerCustomer)
            return ApiResponse<ApplyCouponResult>.Fail("لقد استخدمت هذا الكوبون من قبل.");

        // Calculate discount
        decimal discount = coupon.DiscountType switch
        {
            DiscountType.Percentage   => Math.Round(order.Total * coupon.DiscountValue / 100, 2),
            DiscountType.FixedAmount  => coupon.DiscountValue,
            DiscountType.FreeDelivery => order.DeliveryFee,
            _ => 0
        };

        if (coupon.MaxDiscount.HasValue)
            discount = Math.Min(discount, coupon.MaxDiscount.Value);
        discount = Math.Min(discount, order.Total);

        // Apply to order
        order.DiscountAmount += discount;
        order.Total          -= discount;
        order.UpdatedAt       = DateTime.UtcNow;

        // Record use + increment counter
        _db.Set<CouponUse>().Add(new CouponUse
        {
            CouponId    = coupon.Id,
            CustomerId  = customerId,
            MallOrderId = order.Id,
            DiscountAmt = discount,
        });
        coupon.UsedCount++;
        if (coupon.MaxUses.HasValue && coupon.UsedCount >= coupon.MaxUses)
            coupon.Status = CouponStatus.Depleted;

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Coupon {Code} applied: -{Discount} EGP on order {Order}",
            req.Code, discount, req.MallOrderId);

        return ApiResponse<ApplyCouponResult>.Ok(new ApplyCouponResult
        {
            CouponId    = coupon.Id,
            Code        = coupon.Code,
            DiscountAmt = discount,
            NewTotal    = order.Total,
            Message     = $"تم تطبيق الكوبون! وفّرت {discount:N2} ج.م 🎉",
        });
    }

    // ─── CREATE COUPON (Admin) ────────────────────────────────────────────
    public async Task<ApiResponse<CouponDto>> CreateCouponAsync(
        Guid mallId, Guid createdBy, CreateCouponRequest req, CancellationToken ct = default)
    {
        if (!Enum.TryParse<DiscountType>(req.DiscountType, out var dtype))
            return ApiResponse<CouponDto>.Fail("نوع الخصم غير صالح.");

        var exists = await _db.Set<Coupon>().AnyAsync(
            c => c.Code == req.Code.ToUpperInvariant(), ct);
        if (exists) return ApiResponse<CouponDto>.Fail("هذا الكود مستخدم مسبقاً.");

        var coupon = new Coupon
        {
            MallId         = mallId,
            StoreId        = req.StoreId,
            Code           = req.Code.ToUpperInvariant().Trim(),
            Name           = req.Name,
            Description    = req.Description,
            DiscountType   = dtype,
            DiscountValue  = req.DiscountValue,
            MinOrderValue  = req.MinOrderValue,
            MaxDiscount    = req.MaxDiscount,
            MaxUses        = req.MaxUses,
            UsesPerCustomer= req.UsesPerCustomer,
            MinTier        = req.MinTier,
            ValidTo        = req.ValidTo,
            CreatedBy      = createdBy,
        };
        _db.Set<Coupon>().Add(coupon);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<CouponDto>.Ok(MapCoupon(coupon));
    }

    // ─── CREATE FLASH SALE (Admin) ────────────────────────────────────────
    public async Task<ApiResponse<FlashSaleDto>> CreateFlashSaleAsync(
        Guid mallId, CreateFlashSaleRequest req, CancellationToken ct = default)
    {
        if (req.StartsAt >= req.EndsAt)
            return ApiResponse<FlashSaleDto>.Fail("تاريخ النهاية يجب أن يكون بعد تاريخ البداية.");

        var sale = new FlashSale
        {
            MallId        = mallId,
            StoreId       = req.StoreId,
            Title         = req.Title,
            TitleAr       = req.TitleAr,
            ProductId     = req.ProductId,
            OriginalPrice = req.OriginalPrice,
            FlashPrice    = req.FlashPrice,
            QuantityLimit = req.QuantityLimit,
            StartsAt      = req.StartsAt,
            EndsAt        = req.EndsAt,
            BannerUrl     = req.BannerUrl,
        };
        _db.Set<FlashSale>().Add(sale);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<FlashSaleDto>.Ok(MapFlash(sale));
    }

    // ─── REGISTER DEVICE TOKEN ────────────────────────────────────────────
    public async Task<ApiResponse> RegisterDeviceAsync(
        Guid customerId, RegisterDeviceRequest req, CancellationToken ct = default)
    {
        var existing = await _db.Set<CustomerDevice>()
            .FirstOrDefaultAsync(d => d.CustomerId == customerId
                && d.FcmToken == req.FcmToken, ct);

        if (existing != null)
        {
            existing.IsActive   = true;
            existing.LastSeen   = DateTime.UtcNow;
            existing.DeviceName = req.DeviceName ?? existing.DeviceName;
        }
        else
        {
            _db.Set<CustomerDevice>().Add(new CustomerDevice
            {
                CustomerId = customerId,
                FcmToken   = req.FcmToken,
                Platform   = req.Platform,
                DeviceName = req.DeviceName,
            });
        }
        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok();
    }

    // ─── SEND CAMPAIGN (Firebase FCM) ────────────────────────────────────
    public async Task<ApiResponse> SendCampaignAsync(
        Guid mallId, Guid createdBy, SendCampaignRequest req, CancellationToken ct = default)
    {
        if (!Enum.TryParse<NotifTarget>(req.Target, out var target))
            return ApiResponse.Fail("جمهور الإشعار غير صالح.");

        var campaign = new NotificationCampaign
        {
            MallId    = mallId,
            Title     = req.Title,
            TitleAr   = req.TitleAr,
            Body      = req.Body,
            BodyAr    = req.BodyAr,
            ImageUrl  = req.ImageUrl,
            ActionType= req.ActionType,
            ActionId  = req.ActionId,
            Target    = target,
            CreatedBy = createdBy,
            Status    = req.ScheduledAt.HasValue ? NotifStatus.Scheduled : NotifStatus.Sending,
            ScheduledAt = req.ScheduledAt,
        };
        _db.Set<NotificationCampaign>().Add(campaign);
        await _db.SaveChangesAsync(ct);

        if (!req.ScheduledAt.HasValue)
            await DispatchCampaignAsync(campaign, ct);

        return ApiResponse.Ok($"تم إنشاء الحملة {campaign.Id}");
    }

    // ─── GEO CHECK-IN ────────────────────────────────────────────────────
    public async Task<ApiResponse> HandleGeoCheckInAsync(
        Guid customerId, Guid mallId, GeoCheckInRequest req, CancellationToken ct = default)
    {
        var mall = await _db.Malls.FindAsync([mallId], ct);
        if (mall == null) return ApiResponse.Fail("المول غير موجود.");

        // Calculate distance from mall center
        var dist = HaversineKm(
            req.Lat, req.Lng,
            mall.GeoLat ?? 0, mall.GeoLng ?? 0) * 1000; // meters

        if (dist > (mall.GeoRadiusM + 50)) // 50m tolerance
            return ApiResponse.Ok("خارج النطاق");

        // Find active triggers
        var triggers = await _db.Set<GeoFenceTrigger>()
            .Where(t => t.MallId == mallId && t.IsActive
                && t.TriggerType == "Enter"
                && t.ValidFrom <= DateTime.UtcNow
                && (t.ValidTo == null || t.ValidTo > DateTime.UtcNow))
            .ToListAsync(ct);

        foreach (var trigger in triggers)
        {
            // Check cooldown
            var recent = await _db.Set<GeoFenceEvent>()
                .AnyAsync(e => e.TriggerId == trigger.Id
                    && e.CustomerId == customerId
                    && e.NotifSent
                    && e.CreatedAt > DateTime.UtcNow.AddHours(-trigger.CooldownHours), ct);

            if (recent) continue;

            // Record event + send push
            var geoEvent = new GeoFenceEvent
            {
                TriggerId   = trigger.Id,
                CustomerId  = customerId,
                EventType   = "Enter",
                CustomerLat = req.Lat,
                CustomerLng = req.Lng,
            };
            _db.Set<GeoFenceEvent>().Add(geoEvent);

            var devices = await _db.Set<CustomerDevice>()
                .Where(d => d.CustomerId == customerId && d.IsActive)
                .Select(d => d.FcmToken)
                .ToListAsync(ct);

            if (devices.Any())
            {
                await SendFcmAsync(devices, trigger.NotifTitleAr ?? trigger.NotifTitle,
                    trigger.NotifBodyAr ?? trigger.NotifBody,
                    trigger.ActionType, trigger.ActionId, ct);
                geoEvent.NotifSent = true;
            }
        }

        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok("تم معالجة الدخول للمول");
    }

    // ─── FIREBASE FCM SENDER ─────────────────────────────────────────────
    private async Task DispatchCampaignAsync(
        NotificationCampaign campaign, CancellationToken ct)
    {
        // Get target tokens
        IQueryable<CustomerDevice> query = _db.Set<CustomerDevice>()
            .Where(d => d.IsActive);

        if (campaign.Target != NotifTarget.AllCustomers)
        {
            var tierFilter = campaign.Target switch
            {
                NotifTarget.TierGold   => "Gold",
                NotifTarget.TierSilver => "Silver",
                NotifTarget.TierBronze => "Bronze",
                _ => null
            };
            if (tierFilter != null)
                query = query.Where(d => _db.MallCustomers
                    .Any(c => c.Id == d.CustomerId
                        && c.Tier.ToString() == tierFilter));
        }

        var tokens = await query.Select(d => d.FcmToken).ToListAsync(ct);
        if (!tokens.Any())
        {
            campaign.Status = NotifStatus.Sent;
            await _db.SaveChangesAsync(ct);
            return;
        }

        // Batch send (FCM max 500 per request)
        int sent = 0;
        foreach (var batch in tokens.Chunk(500))
        {
            try
            {
                await SendFcmAsync(batch.ToList(),
                    campaign.TitleAr ?? campaign.Title,
                    campaign.BodyAr  ?? campaign.Body,
                    campaign.ActionType, campaign.ActionId, ct);
                sent += batch.Length;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "FCM batch send failed");
            }
        }

        campaign.Status    = NotifStatus.Sent;
        campaign.SentAt    = DateTime.UtcNow;
        campaign.SentCount = sent;
        campaign.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Campaign {Id} sent to {Count} devices", campaign.Id, sent);
    }

    private async Task SendFcmAsync(List<string> tokens, string title, string body,
        string? actionType, string? actionId, CancellationToken ct)
    {
        var serverKey = _config["Firebase:ServerKey"];
        if (string.IsNullOrEmpty(serverKey))
        {
            _log.LogWarning("Firebase ServerKey not configured — skipping push");
            return;
        }

        var client = _http.CreateClient("Firebase");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("key", $"={serverKey}");

        var payload = new
        {
            registration_ids = tokens,
            notification = new { title, body },
            data = new
            {
                action_type = actionType,
                action_id   = actionId,
                click_action = "FLUTTER_NOTIFICATION_CLICK"
            },
            priority = "high"
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            "https://fcm.googleapis.com/fcm/send", content, ct);

        if (!response.IsSuccessStatusCode)
            _log.LogWarning("FCM returned {Status}: {Body}",
                response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }

    // ─── MAPPERS ─────────────────────────────────────────────────────────
    private static CouponDto MapCoupon(Coupon c) => new()
    {
        Id            = c.Id,
        Code          = c.Code,
        Name          = c.Name,
        Description   = c.Description,
        DiscountType  = c.DiscountType.ToString(),
        DiscountValue = c.DiscountValue,
        MinOrderValue = c.MinOrderValue,
        MaxDiscount   = c.MaxDiscount,
        MinTier       = c.MinTier,
        ValidTo       = c.ValidTo.ToString("dd/MM/yyyy HH:mm"),
        IsExpiringSoon= (c.ValidTo - DateTime.UtcNow).TotalHours < 48,
    };

    private static FlashSaleDto MapFlash(FlashSale f) => new()
    {
        Id            = f.Id,
        Title         = f.Title,
        TitleAr       = f.TitleAr,
        OriginalPrice = f.OriginalPrice ?? 0,
        FlashPrice    = f.FlashPrice,
        DiscountPct   = f.DiscountPct,
        Remaining     = f.Remaining,
        EndsAt        = f.EndsAt.ToString("dd/MM HH:mm"),
        SecondsLeft   = Math.Max(0, (int)(f.EndsAt - DateTime.UtcNow).TotalSeconds),
        BannerUrl     = f.BannerUrl,
        IsLive        = f.IsLive,
    };

    // Haversine distance formula
    private static double HaversineKm(decimal lat1, decimal lng1, decimal lat2, decimal lng2)
    {
        const double R = 6371;
        var dLat = (double)(lat2 - lat1) * Math.PI / 180;
        var dLng = (double)(lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos((double)lat1 * Math.PI / 180)
            * Math.Cos((double)lat2 * Math.PI / 180)
            * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static int CompareTier(string a, string b)
    {
        var order = new[] { "Bronze", "Silver", "Gold" };
        return Array.IndexOf(order, a) - Array.IndexOf(order, b);
    }
}
