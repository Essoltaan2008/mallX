using System.Security.Claims;
using MesterX.Application.DTOs;
using MesterX.Application.Services.Phase4;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesterX.API.Controllers.Phase4;

[ApiController, Produces("application/json")]
public abstract class Phase4Base : ControllerBase
{
    protected Guid CustomerId => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    protected Guid MallId => Guid.TryParse(
        User.FindFirstValue("mall_id"), out var id) ? id : Guid.Empty;
    protected Guid TenantId => Guid.TryParse(
        User.FindFirstValue("tenant_id"), out var id) ? id : Guid.Empty;
    protected Guid UserId => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    protected bool IsCustomer =>
        User.FindFirstValue("token_type") == "customer";
}

// ══════════════════════════════════════════════════════════════════════════
//  LOYALTY WALLET (Customer)
// ══════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/mall/loyalty")]
public class LoyaltyController : Phase4Base
{
    private readonly ILoyaltyService _loyalty;
    public LoyaltyController(ILoyaltyService loyalty) => _loyalty = loyalty;

    /// <summary>محفظة النقاط الكاملة</summary>
    [HttpGet("wallet")]
    public async Task<IActionResult> Wallet(
        [FromQuery] Guid? mallId, CancellationToken ct)
    {
        var mid    = mallId ?? MallId;
        var result = await _loyalty.GetWalletAsync(CustomerId, mid, ct);
        return Ok(result);
    }

    /// <summary>سجل المعاملات</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History(
        [FromQuery] int page = 1, [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        var result = await _loyalty.GetHistoryAsync(CustomerId, page, size, ct);
        return Ok(result);
    }

    /// <summary>استبدال نقاط بخصم على طلب</summary>
    [HttpPost("redeem")]
    public async Task<IActionResult> Redeem(
        [FromBody] RedeemPointsRequest req, CancellationToken ct)
    {
        var result = await _loyalty.RedeemAsync(CustomerId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  PROMOTIONS (Customer)
// ══════════════════════════════════════════════════════════════════════════
[Route("api/mall/promotions")]
public class PromotionCustomerController : Phase4Base
{
    private readonly IPromotionService _promo;
    public PromotionCustomerController(IPromotionService promo) => _promo = promo;

    /// <summary>العروض والكوبونات النشطة</summary>
    [Authorize, HttpGet]
    public async Task<IActionResult> Active(
        [FromQuery] Guid? mallId, CancellationToken ct)
    {
        var mid    = mallId ?? MallId;
        var result = await _promo.GetActivePromotionsAsync(mid, CustomerId, ct);
        return Ok(result);
    }

    /// <summary>تطبيق كوبون خصم</summary>
    [Authorize, HttpPost("coupon/apply")]
    public async Task<IActionResult> ApplyCoupon(
        [FromBody] ApplyCouponRequest req, CancellationToken ct)
    {
        var result = await _promo.ApplyCouponAsync(CustomerId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  PROMOTIONS ADMIN (MallAdmin)
// ══════════════════════════════════════════════════════════════════════════
[Authorize(Roles = "PlatformOwner,CompanyOwner"), Route("api/mall/admin/promotions")]
public class PromotionAdminController : Phase4Base
{
    private readonly IPromotionService _promo;
    public PromotionAdminController(IPromotionService promo) => _promo = promo;

    /// <summary>إنشاء كوبون جديد</summary>
    [HttpPost("coupons")]
    public async Task<IActionResult> CreateCoupon(
        [FromBody] CreateCouponRequest req,
        [FromQuery] Guid? mallId,
        CancellationToken ct)
    {
        var mid    = mallId ?? MallId;
        var result = await _promo.CreateCouponAsync(mid, UserId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>إنشاء فلاش سيل</summary>
    [HttpPost("flash-sales")]
    public async Task<IActionResult> CreateFlashSale(
        [FromBody] CreateFlashSaleRequest req,
        [FromQuery] Guid? mallId,
        CancellationToken ct)
    {
        var mid    = mallId ?? MallId;
        var result = await _promo.CreateFlashSaleAsync(mid, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  PUSH NOTIFICATIONS
// ══════════════════════════════════════════════════════════════════════════

/// <summary>Customer — تسجيل جهاز لاستقبال الإشعارات</summary>
[Authorize, Route("api/mall/devices")]
public class DeviceController : Phase4Base
{
    private readonly IPromotionService _promo;
    public DeviceController(IPromotionService promo) => _promo = promo;

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDeviceRequest req, CancellationToken ct)
    {
        var result = await _promo.RegisterDeviceAsync(CustomerId, req, ct);
        return Ok(result);
    }
}

/// <summary>MallAdmin — إرسال حملات الإشعارات</summary>
[Authorize(Roles = "PlatformOwner,CompanyOwner"), Route("api/mall/admin/campaigns")]
public class CampaignController : Phase4Base
{
    private readonly IPromotionService _promo;
    public CampaignController(IPromotionService promo) => _promo = promo;

    [HttpPost]
    public async Task<IActionResult> Send(
        [FromBody] SendCampaignRequest req,
        [FromQuery] Guid? mallId,
        CancellationToken ct)
    {
        var mid    = mallId ?? MallId;
        var result = await _promo.SendCampaignAsync(mid, UserId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

/// <summary>Customer — إرسال موقع للـ Geo-fence check</summary>
[Authorize, Route("api/mall/geo")]
public class GeoController : Phase4Base
{
    private readonly IPromotionService _promo;
    public GeoController(IPromotionService promo) => _promo = promo;

    [HttpPost("checkin")]
    public async Task<IActionResult> CheckIn(
        [FromBody] GeoCheckInRequest req,
        [FromQuery] Guid? mallId,
        CancellationToken ct)
    {
        var mid    = mallId ?? MallId;
        var result = await _promo.HandleGeoCheckInAsync(CustomerId, mid, req, ct);
        return Ok(result);
    }
}
