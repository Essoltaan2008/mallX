using System.Security.Claims;
using MesterX.Application.DTOs;
using MesterX.Application.Services.Phase2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesterX.API.Controllers.Phase2;

// ══════════════════════════════════════════════════════════════════════════
//  PAYMENT CONTROLLER
// ══════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/mall/payments")]
[ApiController, Produces("application/json")]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _payment;
    protected Guid CustomerId => Guid.TryParse(
        User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier),
        out var id) ? id : Guid.Empty;

    public PaymentController(IPaymentService payment) => _payment = payment;

    /// <summary>بدء عملية الدفع (نقدي / Paymob / Fawry)</summary>
    [HttpPost("initiate")]
    public async Task<IActionResult> Initiate(
        [FromBody] InitiatePaymentRequest req, CancellationToken ct)
    {
        var result = await _payment.InitiateAsync(CustomerId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>تأكيد الدفع النقدي عند التسليم</summary>
    [HttpPost("{orderId:guid}/confirm-cash")]
    public async Task<IActionResult> ConfirmCash(Guid orderId, CancellationToken ct)
    {
        var result = await _payment.ConfirmCashAsync(orderId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

/// <summary>Paymob Webhook — بدون Auth</summary>
[Route("api/webhooks/paymob")]
[ApiController]
public class PaymobWebhookController : ControllerBase
{
    private readonly IPaymentService _payment;
    public PaymobWebhookController(IPaymentService payment) => _payment = payment;

    [HttpPost]
    public async Task<IActionResult> Handle(
        [FromBody] PaymobWebhookPayload payload, CancellationToken ct)
    {
        var rawBody = await new System.IO.StreamReader(Request.Body).ReadToEndAsync(ct);
        await _payment.HandlePaymobWebhookAsync(payload, rawBody, ct);
        return Ok(); // Paymob يتوقع 200 دائماً
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  COMMISSION CONTROLLER (MallAdmin)
// ══════════════════════════════════════════════════════════════════════════
[Authorize(Roles = "PlatformOwner,CompanyOwner"), Route("api/mall/admin/commission")]
[ApiController, Produces("application/json")]
public class CommissionAdminController : ControllerBase
{
    private readonly ICommissionService _commission;

    protected Guid MallId => Guid.TryParse(
        User.FindFirstValue("mall_id"), out var id) ? id : Guid.Empty;
    protected Guid UserId => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public CommissionAdminController(ICommissionService commission) => _commission = commission;

    /// <summary>تقرير الإيرادات والعمولات لفترة محددة</summary>
    [HttpGet("report")]
    public async Task<IActionResult> Report(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] Guid? mallId, CancellationToken ct = default)
    {
        var id  = mallId ?? MallId;
        var end = to   ?? DateTime.UtcNow;
        var start = from ?? end.AddDays(-30);
        var result = await _commission.GetRevenueBreakdownAsync(id, start, end, ct);
        return Ok(result);
    }

    /// <summary>إنشاء تسوية للمحل</summary>
    [HttpPost("settlements")]
    public async Task<IActionResult> CreateSettlement(
        [FromBody] SettlementRequest req, CancellationToken ct)
    {
        var result = await _commission.CreateSettlementAsync(req, UserId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>تأكيد إتمام التسوية</summary>
    [HttpPatch("settlements/{id:guid}/complete")]
    public async Task<IActionResult> Complete(
        Guid id, [FromQuery] Guid? mallId, CancellationToken ct)
    {
        var mid    = mallId ?? MallId;
        var result = await _commission.MarkSettlementCompletedAsync(mid, id, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

/// <summary>Store Owner — مالياتي</summary>
[Authorize, Route("api/mall/store/financials")]
[ApiController, Produces("application/json")]
public class StoreFinancialsController : ControllerBase
{
    private readonly ICommissionService _commission;
    protected Guid TenantId => Guid.TryParse(
        User.FindFirstValue("tenant_id"), out var id) ? id : Guid.Empty;

    public StoreFinancialsController(ICommissionService commission) => _commission = commission;

    /// <summary>ملخص مالي + تسويات المحل</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await _commission.GetStoreFinancialsAsync(TenantId, ct);
        return Ok(result);
    }
}
