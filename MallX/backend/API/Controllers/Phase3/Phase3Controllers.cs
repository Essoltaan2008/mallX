using System.Security.Claims;
using MesterX.Application.DTOs;
using MesterX.Application.Services.Phase3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesterX.API.Controllers.Phase3;

// ══════════════════════════════════════════════════════════════════════════
//  RESTAURANT
// ══════════════════════════════════════════════════════════════════════════

/// <summary>Customer — تصفح المنيو</summary>
[Route("api/mall/stores/{storeId:guid}/menu")]
[ApiController, Produces("application/json")]
public class MenuController : ControllerBase
{
    private readonly IRestaurantService _restaurant;
    public MenuController(IRestaurantService restaurant) => _restaurant = restaurant;

    [HttpGet]
    public async Task<IActionResult> GetMenu(Guid storeId, CancellationToken ct)
        => Ok(await _restaurant.GetMenuAsync(storeId, ct));

    [Authorize, HttpGet("queue/{ticketId:guid}")]
    public async Task<IActionResult> GetTicket(Guid storeId, Guid ticketId, CancellationToken ct)
    {
        var result = await _restaurant.GetTicketStatusAsync(ticketId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }
}

/// <summary>Store Owner — إدارة المنيو والطابور</summary>
[Authorize, Route("api/mall/store/restaurant")]
[ApiController, Produces("application/json")]
public class RestaurantManageController : ControllerBase
{
    private readonly IRestaurantService _restaurant;
    protected Guid TenantId => Guid.TryParse(
        User.FindFirstValue("tenant_id"), out var id) ? id : Guid.Empty;

    public RestaurantManageController(IRestaurantService restaurant) => _restaurant = restaurant;

    [HttpPost("menu")]
    public async Task<IActionResult> AddItem(
        [FromBody] CreateMenuItemRequest req, CancellationToken ct)
    {
        var result = await _restaurant.AddMenuItemAsync(TenantId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("menu/{itemId:guid}")]
    public async Task<IActionResult> UpdateItem(
        Guid itemId, [FromBody] UpdateMenuItemRequest req, CancellationToken ct)
    {
        var result = await _restaurant.UpdateMenuItemAsync(TenantId, itemId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPatch("menu/{itemId:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid itemId, CancellationToken ct)
        => Ok(await _restaurant.ToggleAvailabilityAsync(TenantId, itemId, ct));

    [HttpDelete("menu/{itemId:guid}")]
    public async Task<IActionResult> Delete(Guid itemId, CancellationToken ct)
        => Ok(await _restaurant.DeleteMenuItemAsync(TenantId, itemId, ct));

    [HttpGet("queue")]
    public async Task<IActionResult> GetQueue(CancellationToken ct)
        => Ok(await _restaurant.GetQueueAsync(TenantId, ct));

    [HttpPatch("queue/{ticketId:guid}/advance")]
    public async Task<IActionResult> Advance(Guid ticketId, CancellationToken ct)
        => Ok(await _restaurant.AdvanceTicketAsync(TenantId, ticketId, ct));
}

// ══════════════════════════════════════════════════════════════════════════
//  BOOKING
// ══════════════════════════════════════════════════════════════════════════

/// <summary>Customer — تصفح وحجز الخدمات</summary>
[Route("api/mall/bookings")]
[ApiController, Produces("application/json")]
public class BookingCustomerController : ControllerBase
{
    private readonly IBookingService _booking;
    protected Guid CustomerId => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public BookingCustomerController(IBookingService booking) => _booking = booking;

    [HttpGet("stores/{storeId:guid}/services")]
    public async Task<IActionResult> GetServices(Guid storeId, CancellationToken ct)
        => Ok(await _booking.GetServicesAsync(storeId, ct));

    [HttpGet("stores/{storeId:guid}/availability")]
    public async Task<IActionResult> GetAvailability(
        Guid storeId,
        [FromQuery] Guid serviceId,
        [FromQuery] Guid? staffId,
        [FromQuery] string date,
        CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var d))
            return BadRequest("تاريخ غير صالح. استخدم yyyy-MM-dd");
        var result = await _booking.GetAvailabilityAsync(storeId, serviceId, staffId, d, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Book(
        [FromBody] CreateBookingRequest req, CancellationToken ct)
    {
        var result = await _booking.CreateBookingAsync(CustomerId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [Authorize, HttpGet("my")]
    public async Task<IActionResult> MyBookings(CancellationToken ct)
        => Ok(await _booking.GetMyBookingsAsync(CustomerId, ct));

    [Authorize, HttpDelete("{bookingId:guid}")]
    public async Task<IActionResult> Cancel(
        Guid bookingId, [FromQuery] string reason = "إلغاء بطلب العميل",
        CancellationToken ct = default)
    {
        var result = await _booking.CancelBookingAsync(CustomerId, bookingId, reason, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

/// <summary>Store Owner — جدول الحجوزات</summary>
[Authorize, Route("api/mall/store/bookings")]
[ApiController, Produces("application/json")]
public class BookingStoreController : ControllerBase
{
    private readonly IBookingService _booking;
    protected Guid TenantId => Guid.TryParse(
        User.FindFirstValue("tenant_id"), out var id) ? id : Guid.Empty;

    public BookingStoreController(IBookingService booking) => _booking = booking;

    [HttpGet]
    public async Task<IActionResult> DaySchedule(
        [FromQuery] string? date, CancellationToken ct)
    {
        var d = date != null && DateOnly.TryParse(date, out var pd)
            ? pd : DateOnly.FromDateTime(DateTime.UtcNow.Date);
        return Ok(await _booking.GetStoreDayBookingsAsync(TenantId, d, ct));
    }

    [HttpPatch("{bookingId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid bookingId, [FromBody] UpdateBookingStatusRequest req, CancellationToken ct)
    {
        var result = await _booking.UpdateBookingStatusAsync(TenantId, bookingId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  RATINGS
// ══════════════════════════════════════════════════════════════════════════

[Route("api/mall/ratings")]
[ApiController, Produces("application/json")]
public class RatingController : ControllerBase
{
    private readonly IRatingService _ratings;
    protected Guid CustomerId => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    protected Guid TenantId => Guid.TryParse(
        User.FindFirstValue("tenant_id"), out var id) ? id : Guid.Empty;

    public RatingController(IRatingService ratings) => _ratings = ratings;

    /// <summary>ملخص تقييمات المحل (عام)</summary>
    [HttpGet("stores/{storeId:guid}")]
    public async Task<IActionResult> Summary(Guid storeId, CancellationToken ct)
        => Ok(await _ratings.GetStoreSummaryAsync(storeId, ct));

    /// <summary>قائمة التقييمات (عامة)</summary>
    [HttpGet("stores/{storeId:guid}/list")]
    public async Task<IActionResult> List(
        Guid storeId,
        [FromQuery] int page = 1, [FromQuery] int size = 10,
        CancellationToken ct = default)
        => Ok(await _ratings.GetStoreRatingsAsync(storeId, page, size, ct));

    /// <summary>إرسال تقييم (عميل)</summary>
    [Authorize, HttpPost]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitRatingRequest req, CancellationToken ct)
    {
        var result = await _ratings.SubmitAsync(CustomerId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>رد المحل على تقييم</summary>
    [Authorize, HttpPost("{ratingId:guid}/reply")]
    public async Task<IActionResult> Reply(
        Guid ratingId, [FromBody] StoreReplyRequest req, CancellationToken ct)
    {
        var result = await _ratings.ReplyAsync(TenantId, ratingId, req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>إخفاء تقييم (إدارة المحل)</summary>
    [Authorize, HttpPatch("{ratingId:guid}/hide")]
    public async Task<IActionResult> Hide(Guid ratingId, CancellationToken ct)
        => Ok(await _ratings.HideRatingAsync(TenantId, ratingId, ct));
}
