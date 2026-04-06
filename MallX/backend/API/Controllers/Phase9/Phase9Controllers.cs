using System.Security.Claims;
using MesterX.Application.DTOs;
using MesterX.Application.Services.Phase9;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesterX.API.Controllers.Phase9;

[ApiController, Produces("application/json")]
public abstract class Phase9Base : ControllerBase
{
    protected Guid CustomerId => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    protected Guid MallId    => Guid.TryParse(
        User.FindFirstValue("mall_id"),    out var id) ? id : Guid.Empty;
    protected Guid TenantId  => Guid.TryParse(
        User.FindFirstValue("tenant_id"),  out var id) ? id : Guid.Empty;
    protected bool IsCustomer => User.FindFirstValue("token_type") == "customer";
}

// ══════════════════════════════════════════════════════════════════════════
//  E-WALLET (Customer)
// ══════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/mall/wallet")]
public class WalletController : Phase9Base
{
    private readonly IWalletService _wallet;
    public WalletController(IWalletService wallet) => _wallet = wallet;

    /// <summary>محفظة العميل — الرصيد + آخر المعاملات</summary>
    [HttpGet]
    public async Task<IActionResult> GetWallet(
        [FromQuery] Guid? mallId, CancellationToken ct)
    {
        var mid    = mallId ?? MallId;
        var result = await _wallet.GetWalletAsync(CustomerId, mid, ct);
        return Ok(result);
    }

    /// <summary>شحن المحفظة</summary>
    [HttpPost("topup")]
    public async Task<IActionResult> TopUp(
        [FromBody] TopUpRequest req,
        [FromQuery] Guid? mallId, CancellationToken ct)
    {
        var mid    = mallId ?? MallId;
        var result = await _wallet.TopUpAsync(CustomerId, mid, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>دفع من المحفظة على طلب</summary>
    [HttpPost("spend")]
    public async Task<IActionResult> Spend(
        [FromBody] SpendFromWalletRequest req, CancellationToken ct)
    {
        var result = await _wallet.SpendAsync(CustomerId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>سجل معاملات المحفظة</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History(
        [FromQuery] int page = 1, [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        var result = await _wallet.GetHistoryAsync(CustomerId, page, size, ct);
        return Ok(result);
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  REFERRAL (Customer)
// ══════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/mall/referral")]
public class ReferralController : Phase9Base
{
    private readonly IReferralService _referral;
    public ReferralController(IReferralService referral) => _referral = referral;

    /// <summary>كود الإحالة الخاص بالعميل</summary>
    [HttpGet("code")]
    public async Task<IActionResult> GetCode(
        [FromQuery] Guid? mallId, CancellationToken ct)
    {
        var mid    = mallId ?? MallId;
        var result = await _referral.GetOrCreateCodeAsync(CustomerId, mid, ct);
        return Ok(result);
    }

    /// <summary>تطبيق كود إحالة عند التسجيل</summary>
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(
        [FromBody] ApplyReferralRequest req, CancellationToken ct)
    {
        var mid    = req.MallId ?? MallId;
        var result = await _referral.ApplyReferralAsync(CustomerId, req.Code, mid, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

public record ApplyReferralRequest(string Code, Guid? MallId = null);

// ══════════════════════════════════════════════════════════════════════════
//  WHATSAPP (internal — called from order service)
// ══════════════════════════════════════════════════════════════════════════
[Authorize(Roles = "PlatformOwner,CompanyOwner"), Route("api/mall/admin/whatsapp")]
public class WhatsappAdminController : Phase9Base
{
    private readonly IWhatsappService _wa;
    public WhatsappAdminController(IWhatsappService wa) => _wa = wa;

    /// <summary>إرسال رسالة واتساب مخصصة</summary>
    [HttpPost("send")]
    public async Task<IActionResult> Send(
        [FromBody] SendWhatsappRequest req, CancellationToken ct)
    {
        await _wa.SendCustomAsync(req.Phone, req.Template, req.Variables, ct);
        return Ok(ApiResponse.Ok());
    }
}

public record SendWhatsappRequest(
    string Phone, string Template,
    Dictionary<string, string> Variables
);

// ══════════════════════════════════════════════════════════════════════════
//  SUPERADMIN — Platform-wide management
// ══════════════════════════════════════════════════════════════════════════
[Authorize(Roles = "PlatformOwner"), Route("api/superadmin")]
public class SuperAdminController : Phase9Base
{
    private readonly ISuperAdminService _admin;
    public SuperAdminController(ISuperAdminService admin) => _admin = admin;

    /// <summary>نظرة عامة على المنصة كلها</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
        => Ok(await _admin.GetOverviewAsync(ct));

    /// <summary>إنشاء مول جديد</summary>
    [HttpPost("malls")]
    public async Task<IActionResult> CreateMall(
        [FromBody] CreateMallRequest req, CancellationToken ct)
    {
        var result = await _admin.CreateMallAsync(req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>اشتراكات محلات مول</summary>
    [HttpGet("malls/{mallId:guid}/subscriptions")]
    public async Task<IActionResult> Subscriptions(Guid mallId, CancellationToken ct)
        => Ok(await _admin.GetSubscriptionsAsync(mallId, ct));

    /// <summary>تعليق محل</summary>
    [HttpPost("stores/{storeId:guid}/suspend")]
    public async Task<IActionResult> Suspend(
        Guid storeId, [FromBody] SuspendRequest req, CancellationToken ct)
    {
        var result = await _admin.SuspendStoreAsync(storeId, req.Reason, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>تفعيل محل</summary>
    [HttpPost("stores/{storeId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid storeId, CancellationToken ct)
        => Ok(await _admin.ActivateStoreAsync(storeId, ct));

    /// <summary>إعدادات المنصة</summary>
    [HttpGet("settings/{key}")]
    public async Task<IActionResult> GetSetting(string key, CancellationToken ct)
    {
        var result = await _admin.GetSettingAsync(key, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPut("settings/{key}")]
    public async Task<IActionResult> SetSetting(
        string key, [FromBody] SetSettingRequest req, CancellationToken ct)
        => Ok(await _admin.SetSettingAsync(key, req.Value, ct));
}

public record SuspendRequest(string Reason);
public record SetSettingRequest(string Value);
