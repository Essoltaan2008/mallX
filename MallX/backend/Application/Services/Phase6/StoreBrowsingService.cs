using MesterX.Application.DTOs;
using MesterX.Domain.Entities.Mall;
using MesterX.Infrastructure.Caching;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesterX.Application.Services.Phase6;

// ──────────────────────────────────────────────────────────────────────────
//  DTOs
// ──────────────────────────────────────────────────────────────────────────
public record MallHomeDto
{
    public MallInfoDto           Mall        { get; init; } = null!;
    public List<StoreCardDto>    Featured    { get; init; } = [];
    public List<StoreCardDto>    Restaurants { get; init; } = [];
    public List<StoreCardDto>    Retail      { get; init; } = [];
    public List<StoreCardDto>    Services    { get; init; } = [];
    public List<FlashBannerDto>  Banners     { get; init; } = [];
}

public record MallInfoDto
{
    public Guid    Id          { get; init; }
    public string  Name        { get; init; } = string.Empty;
    public string? NameAr      { get; init; }
    public string? LogoUrl     { get; init; }
    public string? CoverUrl    { get; init; }
    public string? Address     { get; init; }
    public string? Phone       { get; init; }
    public int     TotalStores { get; init; }
}

public record StoreCardDto
{
    public Guid    Id          { get; init; }
    public string  Name        { get; init; } = string.Empty;
    public string  StoreType   { get; init; } = string.Empty;
    public string? LogoUrl     { get; init; }
    public string? CoverUrl    { get; init; }
    public string? Description { get; init; }
    public int     FloorNumber { get; init; }
    public decimal AvgRating   { get; init; }
    public int     TotalRatings{ get; init; }
    public bool    IsOpen      { get; init; }
    public string? Tags        { get; init; }
    public bool    IsNew       { get; init; }
}

public record StoreDetailDto : StoreCardDto
{
    public string? Phone       { get; init; }
    public string? Email       { get; init; }
    public string? WorkingHours{ get; init; }
    public decimal CommissionRate { get; init; }
    public List<ProductCardDto> FeaturedProducts { get; init; } = [];
    public List<Phase3.MenuItemDto> FeaturedMenu { get; init; } = [];
}

public record ProductCardDto
{
    public Guid    Id          { get; init; }
    public string  Name        { get; init; } = string.Empty;
    public string? ImageUrl    { get; init; }
    public decimal Price       { get; init; }
    public bool    InStock     { get; init; }
    public int     StockQty    { get; init; }
    public Guid    StoreId     { get; init; }
    public string  StoreName   { get; init; } = string.Empty;
}

public record FlashBannerDto
{
    public string  Title        { get; init; } = string.Empty;
    public string? Subtitle     { get; init; }
    public string? ImageUrl     { get; init; }
    public string? ActionType   { get; init; }
    public string? ActionId     { get; init; }
    public string  BackgroundColor { get; init; } = "#3B82F6";
}

public record SearchRequest(
    Guid   MallId,
    string Query,
    string? Type,         // All | Store | Product | MenuItem
    string? StoreType,    // Restaurant | Retail | Service
    int    Page = 1,
    int    Size = 20
);

public record SearchResultDto
{
    public string              Query      { get; init; } = string.Empty;
    public int                 TotalCount { get; init; }
    public List<StoreCardDto>  Stores     { get; init; } = [];
    public List<ProductCardDto>Products   { get; init; } = [];
    public List<Phase3.MenuItemDto> MenuItems { get; init; } = [];
    public List<string>        Suggestions{ get; init; } = [];
}

public record StoreListRequest(
    Guid    MallId,
    string? StoreType = null,
    int?    Floor     = null,
    string? SortBy    = "rating",   // rating | name | newest
    int     Page      = 1,
    int     Size      = 20
);

// Driver assignment
public record AvailableDriverDto
{
    public Guid   Id          { get; init; }
    public string Name        { get; init; } = string.Empty;
    public string Phone       { get; init; } = string.Empty;
    public string VehicleType { get; init; } = string.Empty;
    public double? Lat        { get; init; }
    public double? Lng        { get; init; }
    public double? DistanceKm { get; init; }
}

// ──────────────────────────────────────────────────────────────────────────
//  SERVICE
// ──────────────────────────────────────────────────────────────────────────
public interface IStoreBrowsingService
{
    Task<ApiResponse<MallHomeDto>>     GetMallHomeAsync(Guid mallId, Guid? customerId, CancellationToken ct = default);
    Task<ApiResponse<List<StoreCardDto>>> GetStoresAsync(StoreListRequest req, CancellationToken ct = default);
    Task<ApiResponse<StoreDetailDto>>  GetStoreDetailAsync(Guid storeId, CancellationToken ct = default);
    Task<ApiResponse<List<ProductCardDto>>> GetStoreProductsAsync(Guid storeId, string? search, int page, int size, CancellationToken ct = default);
    Task<ApiResponse<SearchResultDto>> SearchAsync(SearchRequest req, Guid? customerId, CancellationToken ct = default);
    Task<ApiResponse<List<string>>>    GetTrendingSearchesAsync(Guid mallId, CancellationToken ct = default);

    // Driver assignment
    Task<ApiResponse<AvailableDriverDto>> AssignDriverAsync(Guid mallOrderId, CancellationToken ct = default);
    Task<ApiResponse<List<AvailableDriverDto>>> GetAvailableDriversAsync(Guid mallId, CancellationToken ct = default);
}

public class StoreBrowsingService : IStoreBrowsingService
{
    private readonly MesterXDbContext _db;
    private readonly ICacheService    _cache;
    private readonly IHubs.IHubNotifier _hub;
    private readonly ILogger<StoreBrowsingService> _log;

    public StoreBrowsingService(MesterXDbContext db, ICacheService cache,
        IHubs.IHubNotifier hub, ILogger<StoreBrowsingService> log)
    { _db = db; _cache = cache; _hub = hub; _log = log; }

    // ─── MALL HOME ────────────────────────────────────────────────────────
    public async Task<ApiResponse<MallHomeDto>> GetMallHomeAsync(
        Guid mallId, Guid? customerId, CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.MallInfo(mallId);
        return await _cache.GetOrSetAsync(cacheKey, async () =>
        {
            var mall = await _db.Malls.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == mallId && m.IsActive, ct);
            if (mall == null) return ApiResponse<MallHomeDto>.Fail("المول غير موجود.");

            var allStores = await GetStoreCardsQueryAsync(mallId, ct);
            var flash = await _db.Set<Domain.Entities.Phase4.FlashSale>()
                .AsNoTracking()
                .Where(f => f.MallId == mallId && f.IsActive
                    && DateTime.UtcNow >= f.StartsAt && DateTime.UtcNow <= f.EndsAt)
                .Take(5).ToListAsync(ct);

            return ApiResponse<MallHomeDto>.Ok(new MallHomeDto
            {
                Mall = new MallInfoDto
                {
                    Id = mall.Id, Name = mall.Name, NameAr = mall.NameAr,
                    LogoUrl = mall.LogoUrl, CoverUrl = mall.CoverUrl,
                    Address = mall.Address, Phone = mall.Phone,
                    TotalStores = allStores.Count
                },
                Featured    = allStores.Where(s => s.AvgRating >= 4).Take(6).ToList(),
                Restaurants = allStores.Where(s => s.StoreType == "Restaurant").Take(8).ToList(),
                Retail      = allStores.Where(s => s.StoreType == "Retail").Take(8).ToList(),
                Services    = allStores.Where(s => s.StoreType == "Service").Take(8).ToList(),
                Banners     = flash.Select(f => new FlashBannerDto
                {
                    Title    = f.TitleAr ?? f.Title,
                    Subtitle = $"خصم {f.DiscountPct:N0}% — باقي {f.Remaining} فقط!",
                    ImageUrl = f.BannerUrl,
                    ActionType = "FlashSale",
                    ActionId   = f.Id.ToString(),
                    BackgroundColor = "#F59E0B"
                }).ToList(),
            });
        }, CacheExtensions.InfoTtl, ct);
    }

    // ─── LIST STORES ──────────────────────────────────────────────────────
    public async Task<ApiResponse<List<StoreCardDto>>> GetStoresAsync(
        StoreListRequest req, CancellationToken ct = default)
    {
        var cacheKey = $"mall:{req.MallId}:stores:{req.StoreType}:{req.Floor}:{req.SortBy}:{req.Page}";
        return await _cache.GetOrSetAsync(cacheKey, async () =>
        {
            var cards = await GetStoreCardsQueryAsync(req.MallId, ct);

            if (!string.IsNullOrEmpty(req.StoreType))
                cards = cards.Where(s => s.StoreType == req.StoreType).ToList();
            if (req.Floor.HasValue)
                cards = cards.Where(s => s.FloorNumber == req.Floor).ToList();

            cards = req.SortBy switch
            {
                "name"   => cards.OrderBy(s => s.Name).ToList(),
                "newest" => cards.OrderByDescending(s => s.IsNew).ToList(),
                _        => cards.OrderByDescending(s => s.AvgRating)
                                 .ThenByDescending(s => s.TotalRatings).ToList(),
            };

            var paged = cards.Skip((req.Page - 1) * req.Size).Take(req.Size).ToList();
            return ApiResponse<List<StoreCardDto>>.Ok(paged);
        }, CacheExtensions.StoresTtl, ct);
    }

    // ─── STORE DETAIL ─────────────────────────────────────────────────────
    public async Task<ApiResponse<StoreDetailDto>> GetStoreDetailAsync(
        Guid storeId, CancellationToken ct = default)
    {
        var store = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == storeId && t.IsActive && !t.IsDeleted, ct);
        if (store == null) return ApiResponse<StoreDetailDto>.Fail("المحل غير موجود.");

        var summary = await _db.Set<Domain.Entities.Phase3.StoreRatingSummary>()
            .AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == storeId, ct);

        // Featured products (top rated / newest)
        var products = await _db.Products.AsNoTracking()
            .Include(p => p.StockItems)
            .Where(p => p.TenantId == storeId && p.IsActive && !p.IsDeleted)
            .OrderBy(p => Guid.NewGuid())   // random featured
            .Take(8).ToListAsync(ct);

        // Featured menu (if restaurant)
        var menuItems = await _db.Set<Domain.Entities.Phase3.MenuItem>()
            .AsNoTracking()
            .Where(m => m.StoreId == storeId && m.IsAvailable && m.IsFeatured && !m.IsDeleted)
            .Take(6).ToListAsync(ct);

        var storeType = store.EfProperty<string>("StoreType") ?? "Retail";
        var floor     = store.EfProperty<int>("FloorNumber");
        var commission= store.EfProperty<decimal>("Commission");

        return ApiResponse<StoreDetailDto>.Ok(new StoreDetailDto
        {
            Id = store.Id, Name = store.Name, StoreType = storeType,
            LogoUrl = store.LogoUrl, FloorNumber = floor,
            Phone = store.Phone, Email = store.Email,
            IsOpen = store.IsActive, IsNew = store.CreatedAt > DateTime.UtcNow.AddDays(-30),
            AvgRating    = summary?.AvgStars ?? 0,
            TotalRatings = summary?.TotalRatings ?? 0,
            CommissionRate = commission,
            FeaturedProducts = products.Select(p => new ProductCardDto
            {
                Id       = p.Id, Name = p.Name, ImageUrl = p.ImageUrl,
                Price    = p.SalePrice, StoreId = storeId, StoreName = store.Name,
                InStock  = p.StockItems.Any(s => s.AvailableQuantity > 0),
                StockQty = p.StockItems.Sum(s => s.AvailableQuantity),
            }).ToList(),
            FeaturedMenu = menuItems.Select(m => new Phase3.MenuItemDto
            {
                Id = m.Id, Name = m.Name, NameAr = m.NameAr,
                Price = m.Price, ImageUrl = m.ImageUrl,
                PrepTimeMin = m.PrepTimeMin, IsAvailable = m.IsAvailable,
                IsFeatured = m.IsFeatured, Tags = m.Tags ?? [],
            }).ToList(),
        });
    }

    // ─── STORE PRODUCTS ───────────────────────────────────────────────────
    public async Task<ApiResponse<List<ProductCardDto>>> GetStoreProductsAsync(
        Guid storeId, string? search, int page, int size, CancellationToken ct = default)
    {
        var query = _db.Products.AsNoTracking()
            .Include(p => p.StockItems)
            .Where(p => p.TenantId == storeId && p.IsActive && !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => EF.Functions.ToTsVector("simple", p.Name)
                .Matches(EF.Functions.PlainToTsQuery("simple", search)));

        var store = await _db.Tenants.AsNoTracking()
            .Select(t => new { t.Id, t.Name }).FirstOrDefaultAsync(t => t.Id == storeId, ct);

        var products = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);

        return ApiResponse<List<ProductCardDto>>.Ok(products.Select(p => new ProductCardDto
        {
            Id       = p.Id, Name = p.Name, ImageUrl = p.ImageUrl,
            Price    = p.SalePrice, StoreId = storeId,
            StoreName= store?.Name ?? string.Empty,
            InStock  = p.StockItems.Any(s => s.AvailableQuantity > 0),
            StockQty = p.StockItems.Sum(s => s.AvailableQuantity),
        }).ToList());
    }

    // ─── SEARCH ───────────────────────────────────────────────────────────
    public async Task<ApiResponse<SearchResultDto>> SearchAsync(
        SearchRequest req, Guid? customerId, CancellationToken ct = default)
    {
        var q = req.Query.Trim();
        if (q.Length < 2)
            return ApiResponse<SearchResultDto>.Fail("أدخل كلمتين على الأقل للبحث.");

        // Log search (async, don't wait)
        _ = LogSearchAsync(req.MallId, customerId, q, ct);

        var stores    = new List<StoreCardDto>();
        var products  = new List<ProductCardDto>();
        var menuItems = new List<Phase3.MenuItemDto>();

        var searchType = req.Type ?? "All";

        // Store search
        if (searchType is "All" or "Store")
        {
            var dbStores = await _db.Tenants.AsNoTracking()
                .Where(t => t.IsActive && !t.IsDeleted
                    && EF.Functions.ToTsVector("simple", t.Name)
                        .Matches(EF.Functions.PlainToTsQuery("simple", q)))
                .Take(10).ToListAsync(ct);

            var ratingMap = await _db.Set<Domain.Entities.Phase3.StoreRatingSummary>()
                .AsNoTracking()
                .Where(r => dbStores.Select(s => s.Id).Contains(r.StoreId))
                .ToDictionaryAsync(r => r.StoreId, ct);

            stores = dbStores.Select(s => new StoreCardDto
            {
                Id = s.Id, Name = s.Name,
                StoreType   = s.EfProperty<string>("StoreType") ?? "Retail",
                LogoUrl     = s.LogoUrl,
                FloorNumber = s.EfProperty<int>("FloorNumber"),
                AvgRating   = ratingMap.GetValueOrDefault(s.Id)?.AvgStars ?? 0,
                TotalRatings= ratingMap.GetValueOrDefault(s.Id)?.TotalRatings ?? 0,
                IsOpen      = s.IsActive,
                IsNew       = s.CreatedAt > DateTime.UtcNow.AddDays(-30),
            }).ToList();
        }

        // Product search
        if (searchType is "All" or "Product")
        {
            // Get all mall store IDs
            var mallStoreIds = await _db.Tenants.AsNoTracking()
                .Where(t => t.EfProperty<Guid?>("MallId") == req.MallId && t.IsActive)
                .Select(t => t.Id).ToListAsync(ct);

            var dbProducts = await _db.Products.AsNoTracking()
                .Include(p => p.StockItems)
                .Where(p => mallStoreIds.Contains(p.TenantId)
                    && p.IsActive && !p.IsDeleted
                    && EF.Functions.ToTsVector("simple", p.Name)
                        .Matches(EF.Functions.PlainToTsQuery("simple", q)))
                .Take(15).ToListAsync(ct);

            var storeNames = await _db.Tenants.AsNoTracking()
                .Where(t => dbProducts.Select(p => p.TenantId).Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

            products = dbProducts.Select(p => new ProductCardDto
            {
                Id       = p.Id, Name = p.Name, ImageUrl = p.ImageUrl,
                Price    = p.SalePrice, StoreId = p.TenantId,
                StoreName= storeNames.GetValueOrDefault(p.TenantId) ?? string.Empty,
                InStock  = p.StockItems.Any(s => s.AvailableQuantity > 0),
                StockQty = p.StockItems.Sum(s => s.AvailableQuantity),
            }).ToList();
        }

        // Menu item search
        if (searchType is "All" or "MenuItem")
        {
            var dbMenu = await _db.Set<Domain.Entities.Phase3.MenuItem>()
                .AsNoTracking()
                .Where(m => m.IsAvailable && !m.IsDeleted
                    && EF.Functions.ToTsVector("simple", m.Name)
                        .Matches(EF.Functions.PlainToTsQuery("simple", q)))
                .Take(10).ToListAsync(ct);

            menuItems = dbMenu.Select(m => new Phase3.MenuItemDto
            {
                Id = m.Id, Name = m.Name, NameAr = m.NameAr,
                Price = m.Price, ImageUrl = m.ImageUrl,
                PrepTimeMin = m.PrepTimeMin, IsAvailable = m.IsAvailable,
                IsFeatured = m.IsFeatured, Tags = m.Tags ?? [],
            }).ToList();
        }

        // Suggestions from trending
        var suggestions = await _db.Set<TrendingSearch>()
            .AsNoTracking()
            .Where(t => t.MallId == req.MallId
                && t.Query.StartsWith(q) && t.Query != q)
            .OrderByDescending(t => t.SearchCount)
            .Select(t => t.Query)
            .Take(5).ToListAsync(ct);

        return ApiResponse<SearchResultDto>.Ok(new SearchResultDto
        {
            Query      = q,
            TotalCount = stores.Count + products.Count + menuItems.Count,
            Stores     = stores,
            Products   = products,
            MenuItems  = menuItems,
            Suggestions= suggestions,
        });
    }

    // ─── TRENDING SEARCHES ────────────────────────────────────────────────
    public async Task<ApiResponse<List<string>>> GetTrendingSearchesAsync(
        Guid mallId, CancellationToken ct = default)
    {
        var trending = await _db.Set<TrendingSearch>()
            .AsNoTracking()
            .Where(t => t.MallId == mallId && t.Date >= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)))
            .GroupBy(t => t.Query)
            .Select(g => new { Query = g.Key, Total = g.Sum(x => x.SearchCount) })
            .OrderByDescending(g => g.Total)
            .Take(10)
            .Select(g => g.Query)
            .ToListAsync(ct);

        return ApiResponse<List<string>>.Ok(trending);
    }

    // ─── ASSIGN DRIVER ────────────────────────────────────────────────────
    public async Task<ApiResponse<AvailableDriverDto>> AssignDriverAsync(
        Guid mallOrderId, CancellationToken ct = default)
    {
        var order = await _db.MallOrders
            .FirstOrDefaultAsync(o => o.Id == mallOrderId, ct);
        if (order == null) return ApiResponse<AvailableDriverDto>.Fail("الطلب غير موجود.");

        // Find nearest available driver
        var drivers = await _db.Set<Domain.Entities.Payment.Driver>()
            .Where(d => d.MallId == order.MallId && d.IsAvailable && d.IsActive)
            .ToListAsync(ct);

        if (!drivers.Any())
            return ApiResponse<AvailableDriverDto>.Fail("لا يوجد سائقون متاحون حالياً.");

        // Sort by distance if delivery coordinates known
        Domain.Entities.Payment.Driver? best;
        if (order.DeliveryLat.HasValue && drivers.Any(d => d.CurrentLat.HasValue))
        {
            best = drivers
                .Where(d => d.CurrentLat.HasValue && d.CurrentLng.HasValue)
                .OrderBy(d => HaversineKm(
                    (double)d.CurrentLat!, (double)d.CurrentLng!,
                    (double)order.DeliveryLat!, (double)order.DeliveryLng!))
                .First();
        }
        else
        {
            best = drivers.First();
        }

        // Mark driver as unavailable + link to order
        best.IsAvailable = false;
        best.UpdatedAt   = DateTime.UtcNow;

        // Create delivery assignment
        _db.Set<Domain.Entities.Payment.DeliveryAssignment>().Add(
            new Domain.Entities.Payment.DeliveryAssignment
            {
                MallOrderId = mallOrderId,
                DriverId    = best.Id,
                AssignedAt  = DateTime.UtcNow,
                Status      = "Assigned"
            });

        await _db.SaveChangesAsync(ct);

        // Notify via SignalR
        await _hub.NotifyDriverAssignedAsync(mallOrderId.ToString(), best.Id.ToString());

        _log.LogInformation("Driver {DriverId} assigned to order {OrderId}", best.Id, mallOrderId);

        var dist = order.DeliveryLat.HasValue && best.CurrentLat.HasValue
            ? HaversineKm((double)best.CurrentLat!, (double)best.CurrentLng!,
                (double)order.DeliveryLat!, (double)order.DeliveryLng!)
            : (double?)null;

        return ApiResponse<AvailableDriverDto>.Ok(new AvailableDriverDto
        {
            Id = best.Id, Name = best.Name, Phone = best.Phone,
            VehicleType = best.VehicleType,
            Lat = best.CurrentLat.HasValue ? (double)best.CurrentLat : null,
            Lng = best.CurrentLng.HasValue ? (double)best.CurrentLng : null,
            DistanceKm = dist,
        });
    }

    // ─── AVAILABLE DRIVERS ────────────────────────────────────────────────
    public async Task<ApiResponse<List<AvailableDriverDto>>> GetAvailableDriversAsync(
        Guid mallId, CancellationToken ct = default)
    {
        var drivers = await _db.Set<Domain.Entities.Payment.Driver>()
            .AsNoTracking()
            .Where(d => d.MallId == mallId && d.IsAvailable && d.IsActive)
            .ToListAsync(ct);

        return ApiResponse<List<AvailableDriverDto>>.Ok(drivers.Select(d => new AvailableDriverDto
        {
            Id = d.Id, Name = d.Name, Phone = d.Phone, VehicleType = d.VehicleType,
            Lat = d.CurrentLat.HasValue ? (double)d.CurrentLat : null,
            Lng = d.CurrentLng.HasValue ? (double)d.CurrentLng : null,
        }).ToList());
    }

    // ─── PRIVATE HELPERS ─────────────────────────────────────────────────
    private async Task<List<StoreCardDto>> GetStoreCardsQueryAsync(Guid mallId, CancellationToken ct)
    {
        var stores = await _db.Tenants.AsNoTracking()
            .Where(t => t.IsActive && !t.IsDeleted
                && EF.Functions.JsonContains(
                    EF.Property<string>(t, "MallId"), mallId.ToString()))
            .ToListAsync(ct);

        var storeIds = stores.Select(s => s.Id).ToList();
        var ratings  = await _db.Set<Domain.Entities.Phase3.StoreRatingSummary>()
            .AsNoTracking()
            .Where(r => storeIds.Contains(r.StoreId))
            .ToDictionaryAsync(r => r.StoreId, ct);

        return stores.Select(s => new StoreCardDto
        {
            Id          = s.Id,
            Name        = s.Name,
            StoreType   = s.EfProperty<string>("StoreType") ?? "Retail",
            LogoUrl     = s.LogoUrl,
            FloorNumber = s.EfProperty<int>("FloorNumber"),
            AvgRating   = ratings.GetValueOrDefault(s.Id)?.AvgStars ?? 0,
            TotalRatings= ratings.GetValueOrDefault(s.Id)?.TotalRatings ?? 0,
            IsOpen      = s.IsActive,
            IsNew       = s.CreatedAt > DateTime.UtcNow.AddDays(-30),
        }).ToList();
    }

    private async Task LogSearchAsync(Guid mallId, Guid? customerId, string query, CancellationToken ct)
    {
        try
        {
            if (customerId.HasValue)
                _db.Set<CustomerSearchHistory>().Add(new CustomerSearchHistory
                {
                    CustomerId = customerId.Value,
                    MallId     = mallId,
                    Query      = query,
                });

            // Upsert trending
            var today   = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var existing = await _db.Set<TrendingSearch>()
                .FirstOrDefaultAsync(t => t.MallId == mallId
                    && t.Query == query && t.Date == today, ct);
            if (existing != null) existing.SearchCount++;
            else _db.Set<TrendingSearch>().Add(new TrendingSearch
                { MallId = mallId, Query = query, Date = today });

            await _db.SaveChangesAsync(ct);
        }
        catch { /* Non-critical */ }
    }

    private static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
            * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

// ─── Helper Entities ──────────────────────────────────────────────────────
public class TrendingSearch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MallId { get; set; }
    public string Query { get; set; } = string.Empty;
    public int SearchCount { get; set; } = 1;
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.Date);
}

public class CustomerSearchHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Guid MallId { get; set; }
    public string Query { get; set; } = string.Empty;
    public string? ResultType { get; set; }
    public Guid? ClickedId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// EF shadow property helper
public static class EntityExtensions
{
    public static T EfProperty<T>(this object entity, string name)
    {
        try { return Microsoft.EntityFrameworkCore.EF.Property<T>(entity, name); }
        catch { return default!; }
    }
}
