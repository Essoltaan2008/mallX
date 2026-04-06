using System.Security.Claims;
using MesterX.Application.DTOs;
using MesterX.Application.DTOs.Mall;
using MesterX.Application.Services.Mall;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MesterX.API.Controllers.Mall;

// ─── BASE MALL CONTROLLER ─────────────────────────────────────────────────
[ApiController, Produces("application/json")]
public abstract class MallBaseController : ControllerBase
{
    protected Guid CustomerId => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    protected Guid MallId => Guid.TryParse(
        User.FindFirstValue("mall_id"), out var id) ? id : Guid.Empty;
    protected bool IsCustomerToken =>
        User.FindFirstValue("token_type") == "customer";
    protected string ClientIp =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    protected string UserAgent =>
        Request.Headers.UserAgent.ToString();
}

// ══════════════════════════════════════════════════════════════════════════
//  CUSTOMER AUTH
// ══════════════════════════════════════════════════════════════════════════
[Route("api/mall/auth")]
public class MallCustomerAuthController : MallBaseController
{
    private readonly IMallCustomerAuthService _auth;
    public MallCustomerAuthController(IMallCustomerAuthService auth) => _auth = auth;

    /// <summary>تسجيل عميل جديد</summary>
    [HttpPost("register")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Register(
        [FromBody] CustomerRegisterRequest req, CancellationToken ct)
    {
        var result = await _auth.RegisterAsync(req, ClientIp, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>تسجيل دخول العميل</summary>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(
        [FromBody] CustomerLoginRequest req, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(req, ClientIp, UserAgent, ct);
        return result.Success ? Ok(result) : Unauthorized(result);
    }

    /// <summary>تجديد التوكن</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest req, CancellationToken ct)
    {
        var result = await _auth.RefreshAsync(req.RefreshToken, ClientIp, ct);
        return result.Success ? Ok(result) : Unauthorized(result);
    }

    /// <summary>تسجيل الخروج</summary>
    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshTokenRequest req, CancellationToken ct)
    {
        await _auth.LogoutAsync(req.RefreshToken, ct);
        return Ok(ApiResponse.Ok());
    }

    /// <summary>بيانات العميل الحالي</summary>
    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!IsCustomerToken) return Forbid();
        var result = await _auth.GetProfileAsync(CustomerId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  CART
// ══════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/mall/cart")]
public class CartController : MallBaseController
{
    private readonly ICartService _cart;
    public CartController(ICartService cart) => _cart = cart;

    /// <summary>جلب السلة الحالية</summary>
    [HttpGet]
    public async Task<IActionResult> GetCart(CancellationToken ct)
    {
        if (!IsCustomerToken) return Forbid();
        var result = await _cart.GetCartAsync(CustomerId, ct);
        return Ok(result);
    }

    /// <summary>إضافة منتج للسلة</summary>
    [HttpPost("items")]
    public async Task<IActionResult> AddItem(
        [FromBody] AddToCartRequest req, CancellationToken ct)
    {
        if (!IsCustomerToken) return Forbid();
        var result = await _cart.AddItemAsync(CustomerId, MallId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>تعديل كمية منتج</summary>
    [HttpPut("items")]
    public async Task<IActionResult> UpdateItem(
        [FromBody] UpdateCartItemRequest req, CancellationToken ct)
    {
        if (!IsCustomerToken) return Forbid();
        var result = await _cart.UpdateItemAsync(CustomerId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>حذف منتج من السلة</summary>
    [HttpDelete("items/{productId:guid}")]
    public async Task<IActionResult> RemoveItem(Guid productId, CancellationToken ct)
    {
        if (!IsCustomerToken) return Forbid();
        var result = await _cart.RemoveItemAsync(CustomerId, productId, ct);
        return Ok(result);
    }

    /// <summary>تفريغ السلة كاملاً</summary>
    [HttpDelete]
    public async Task<IActionResult> ClearCart(CancellationToken ct)
    {
        if (!IsCustomerToken) return Forbid();
        await _cart.ClearCartAsync(CustomerId, ct);
        return Ok(ApiResponse.Ok());
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  MALL ORDERS (Customer)
// ══════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/mall/orders")]
public class MallOrderController : MallBaseController
{
    private readonly IMallOrderService _orders;
    public MallOrderController(IMallOrderService orders) => _orders = orders;

    /// <summary>إتمام الطلب (Checkout)</summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutRequest req, CancellationToken ct)
    {
        if (!IsCustomerToken) return Forbid();
        var result = await _orders.CheckoutAsync(CustomerId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>تفاصيل طلب محدد</summary>
    [HttpGet("{orderId:guid}")]
    public async Task<IActionResult> GetOrder(Guid orderId, CancellationToken ct)
    {
        if (!IsCustomerToken) return Forbid();
        var result = await _orders.GetOrderAsync(CustomerId, orderId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    /// <summary>سجل الطلبات</summary>
    [HttpGet]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int page = 1, [FromQuery] int size = 10, CancellationToken ct = default)
    {
        if (!IsCustomerToken) return Forbid();
        var result = await _orders.GetOrderHistoryAsync(CustomerId, page, size, ct);
        return Ok(result);
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  STORE OWNER — Dashboard
// ══════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/mall/store")]
public class StoreDashboardController : ControllerBase
{
    private readonly IMallOrderService _orders;

    protected Guid TenantId => Guid.TryParse(
        User.FindFirstValue("tenant_id"), out var id) ? id : Guid.Empty;

    public StoreDashboardController(IMallOrderService orders) => _orders = orders;

    /// <summary>الطلبات الواردة للمحل</summary>
    [HttpGet("orders/incoming")]
    public async Task<IActionResult> GetIncoming(CancellationToken ct)
    {
        var result = await _orders.GetIncomingAsync(TenantId, ct);
        return Ok(result);
    }

    /// <summary>تحديث حالة طلب محدد</summary>
    [HttpPatch("orders/{storeOrderId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid storeOrderId,
        [FromBody] UpdateStoreOrderStatusRequest req,
        CancellationToken ct)
    {
        var result = await _orders.UpdateStoreOrderStatusAsync(TenantId, storeOrderId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  MALL ADMIN
// ══════════════════════════════════════════════════════════════════════════
[Authorize(Roles = "PlatformOwner,CompanyOwner"), Route("api/mall/admin")]
public class MallAdminController : ControllerBase
{
    private readonly IMallOrderService _orders;

    protected Guid MallId => Guid.TryParse(
        User.FindFirstValue("mall_id"), out var id) ? id : Guid.Empty;

    public MallAdminController(IMallOrderService orders) => _orders = orders;

    /// <summary>لوحة تحكم المول — KPIs</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(
        [FromQuery] Guid? mallId, CancellationToken ct)
    {
        var id = mallId ?? MallId;
        var result = await _orders.GetAdminDashboardAsync(id, ct);
        return Ok(result);
    }
}
