using System.Security.Claims;
using System.Text;
using MesterX.Application.DTOs;
using MesterX.Application.Services.Phase8;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesterX.API.Controllers.Phase8;

[ApiController, Produces("application/json")]
public abstract class Phase8Base : ControllerBase
{
    protected Guid CustomerId => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    protected Guid MallId => Guid.TryParse(
        User.FindFirstValue("mall_id"), out var id) ? id : Guid.Empty;
    protected Guid TenantId => Guid.TryParse(
        User.FindFirstValue("tenant_id"), out var id) ? id : Guid.Empty;
    protected bool IsCustomer => User.FindFirstValue("token_type") == "customer";
}

// ══════════════════════════════════════════════════════════════════════════
//  AI CHAT CONTROLLER
// ══════════════════════════════════════════════════════════════════════════
[Route("api/mall/{mallId:guid}/ai")]
public class MallAIChatController : Phase8Base
{
    private readonly IMallAIService _ai;
    public MallAIChatController(IMallAIService ai) => _ai = ai;

    /// <summary>Chat with the MallX AI Assistant (standard response)</summary>
    [Authorize, HttpPost("chat")]
    public async Task<IActionResult> Chat(
        Guid mallId, [FromBody] ChatRequest req, CancellationToken ct)
    {
        var cid    = IsCustomer ? CustomerId : (Guid?)null;
        var result = await _ai.ChatAsync(mallId, cid, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>Chat with streaming response (Server-Sent Events)</summary>
    [Authorize, HttpPost("chat/stream")]
    public async Task ChatStream(
        Guid mallId, [FromBody] ChatRequest req, CancellationToken ct)
    {
        Response.Headers["Content-Type"]  = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"]    = "keep-alive";

        var cid = IsCustomer ? CustomerId : (Guid?)null;

        await foreach (var chunk in _ai.ChatStreamAsync(mallId, cid, req, ct))
        {
            if (ct.IsCancellationRequested) break;
            var data = $"data: {System.Text.Json.JsonSerializer.Serialize(new { text = chunk })}\n\n";
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(data), ct);
            await Response.Body.FlushAsync(ct);
        }

        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes("data: [DONE]\n\n"), ct);
        await Response.Body.FlushAsync(ct);
    }

    /// <summary>Get quick reply suggestions for the chat UI</summary>
    [HttpGet("chat/quick-replies")]
    public async Task<IActionResult> QuickReplies(
        Guid mallId, [FromQuery] string? lang, CancellationToken ct)
        => Ok(await _ai.GetQuickRepliesAsync(mallId, lang, ct));
}

// ══════════════════════════════════════════════════════════════════════════
//  ANALYTICS CONTROLLER — Mall Admin
// ══════════════════════════════════════════════════════════════════════════
[Authorize(Roles = "PlatformOwner,CompanyOwner"), Route("api/mall/admin/analytics")]
public class MallAnalyticsController : Phase8Base
{
    private readonly IAnalyticsService _analytics;
    public MallAnalyticsController(IAnalyticsService analytics) => _analytics = analytics;

    /// <summary>Full mall analytics dashboard — KPIs + Charts + Rankings</summary>
    [HttpGet]
    public async Task<IActionResult> Dashboard(
        [FromQuery] Guid?   mallId,
        [FromQuery] string  period = "month",
        CancellationToken   ct     = default)
    {
        var mid    = mallId ?? MallId;
        var result = await _analytics.GetMallAnalyticsAsync(mid, period, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>Revenue time-series chart</summary>
    [HttpGet("chart")]
    public async Task<IActionResult> Chart(
        [FromQuery] Guid?    mallId,
        [FromQuery] string?  from,
        [FromQuery] string?  to,
        CancellationToken    ct = default)
    {
        var mid  = mallId ?? MallId;
        var dtFrom = from != null ? DateTime.Parse(from) : DateTime.UtcNow.AddDays(-30);
        var dtTo   = to   != null ? DateTime.Parse(to)   : DateTime.UtcNow;
        var result = await _analytics.GetRevenueChartAsync(mid, dtFrom, dtTo, ct);
        return Ok(result);
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  ANALYTICS CONTROLLER — Store Owner
// ══════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/mall/store/analytics")]
public class StoreAnalyticsController : Phase8Base
{
    private readonly IAnalyticsService _analytics;
    public StoreAnalyticsController(IAnalyticsService analytics) => _analytics = analytics;

    /// <summary>Store-level analytics — revenue, top products, orders</summary>
    [HttpGet]
    public async Task<IActionResult> Dashboard(
        [FromQuery] string period = "month",
        CancellationToken  ct    = default)
    {
        var result = await _analytics.GetStoreAnalyticsAsync(TenantId, period, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
