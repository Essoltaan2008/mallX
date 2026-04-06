using System.Security.Claims;
using MesterX.Application.Services.Phase6;
using MesterX.Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesterX.API.Controllers.Phase6;

[ApiController, Produces("application/json")]
public abstract class Phase6Base : ControllerBase
{
    protected Guid CustomerId => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    protected Guid MallId => Guid.TryParse(
        User.FindFirstValue("mall_id"), out var id) ? id : Guid.Empty;
    protected bool IsCustomer => User.FindFirstValue("token_type") == "customer";
}

// ══════════════════════════════════════════════════════════════════════════
//  MALL HOME & STORE BROWSING
// ══════════════════════════════════════════════════════════════════════════

/// <summary>Customer-facing mall discovery — No auth required</summary>
[Route("api/mall/{mallId:guid}")]
public class MallBrowsingController : Phase6Base
{
    private readonly IStoreBrowsingService _browse;
    public MallBrowsingController(IStoreBrowsingService browse) => _browse = browse;

    /// <summary>Mall home page — featured stores + banners + flash sales</summary>
    [HttpGet]
    public async Task<IActionResult> Home(Guid mallId, CancellationToken ct)
    {
        var cid    = IsCustomer ? CustomerId : (Guid?)null;
        var result = await _browse.GetMallHomeAsync(mallId, cid, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    /// <summary>Browse all stores with filtering + sorting</summary>
    [HttpGet("stores")]
    public async Task<IActionResult> Stores(
        Guid mallId,
        [FromQuery] string? type,
        [FromQuery] int? floor,
        [FromQuery] string? sortBy = "rating",
        [FromQuery] int page = 1, [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        var result = await _browse.GetStoresAsync(
            new StoreListRequest(mallId, type, floor, sortBy, page, size), ct);
        return Ok(result);
    }

    /// <summary>Store detail — info + featured products + menu</summary>
    [HttpGet("stores/{storeId:guid}")]
    public async Task<IActionResult> StoreDetail(
        Guid mallId, Guid storeId, CancellationToken ct)
    {
        var result = await _browse.GetStoreDetailAsync(storeId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    /// <summary>Store product catalog with search</summary>
    [HttpGet("stores/{storeId:guid}/products")]
    public async Task<IActionResult> StoreProducts(
        Guid mallId, Guid storeId,
        [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        var result = await _browse.GetStoreProductsAsync(storeId, q, page, size, ct);
        return Ok(result);
    }

    /// <summary>Full-text search across stores + products + menu</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        Guid mallId,
        [FromQuery] string q,
        [FromQuery] string? type,
        [FromQuery] int page = 1, [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(ApiResponse.Fail("أدخل نص البحث."));

        var cid    = IsCustomer ? CustomerId : (Guid?)null;
        var result = await _browse.SearchAsync(
            new SearchRequest(mallId, q, type, null, page, size), cid, ct);
        return Ok(result);
    }

    /// <summary>Trending searches in this mall</summary>
    [HttpGet("search/trending")]
    public async Task<IActionResult> Trending(Guid mallId, CancellationToken ct)
        => Ok(await _browse.GetTrendingSearchesAsync(mallId, ct));
}

// ══════════════════════════════════════════════════════════════════════════
//  MALL MAP
// ══════════════════════════════════════════════════════════════════════════

[Route("api/mall/{mallId:guid}/map")]
public class MallMapController : Phase6Base
{
    private readonly IMallMapService _map;
    public MallMapController(IMallMapService map) => _map = map;

    /// <summary>Full mall map — all floors + store positions</summary>
    [HttpGet]
    public async Task<IActionResult> GetMap(Guid mallId, CancellationToken ct)
    {
        var result = await _map.GetMapAsync(mallId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    /// <summary>Single floor with stores + amenities</summary>
    [HttpGet("floors/{floorId:guid}")]
    public async Task<IActionResult> GetFloor(
        Guid mallId, Guid floorId, CancellationToken ct)
    {
        var result = await _map.GetFloorAsync(floorId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    /// <summary>Generate QR code for a store</summary>
    [Authorize(Roles = "PlatformOwner,CompanyOwner"), HttpPost("stores/{storeId:guid}/qr")]
    public async Task<IActionResult> GenerateQr(
        Guid mallId, Guid storeId, CancellationToken ct)
        => Ok(await _map.GenerateStoreQrAsync(storeId, ct));

    /// <summary>Place / move a store on the map (MallAdmin)</summary>
    [Authorize(Roles = "PlatformOwner,CompanyOwner"), HttpPut("stores")]
    public async Task<IActionResult> PlaceStore(
        Guid mallId, [FromBody] PlaceStoreRequest req, CancellationToken ct)
    {
        var result = await _map.PlaceStoreAsync(req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  DRIVER MANAGEMENT (MallAdmin)
// ══════════════════════════════════════════════════════════════════════════

[Authorize, Route("api/mall/admin/drivers")]
public class DriverAdminController : Phase6Base
{
    private readonly IStoreBrowsingService _browse;
    public DriverAdminController(IStoreBrowsingService browse) => _browse = browse;

    /// <summary>List available drivers in the mall</summary>
    [HttpGet]
    public async Task<IActionResult> Available(
        [FromQuery] Guid? mallId, CancellationToken ct)
        => Ok(await _browse.GetAvailableDriversAsync(mallId ?? MallId, ct));

    /// <summary>Manually assign driver to an order</summary>
    [HttpPost("{orderId:guid}/assign")]
    public async Task<IActionResult> Assign(Guid orderId, CancellationToken ct)
    {
        var result = await _browse.AssignDriverAsync(orderId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
