using MesterX.Application.DTOs;
using MesterX.Domain.Entities.Mall;
using MesterX.Infrastructure.Caching;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesterX.Application.Services.Phase8;

// ──────────────────────────────────────────────────────────────────────────
//  DTOs
// ──────────────────────────────────────────────────────────────────────────
public record AnalyticsPeriod(DateTime From, DateTime To, string Label);

public record MallAnalyticsDto
{
    public string Period           { get; init; } = string.Empty;
    public RevenueStatsDto Revenue { get; init; } = new();
    public OrderStatsDto   Orders  { get; init; } = new();
    public CustomerStatsDto Customers{ get; init; } = new();
    public List<DailyMetric>  RevenueChart   { get; init; } = [];
    public List<DailyMetric>  OrdersChart    { get; init; } = [];
    public List<StoreRankDto> TopStores      { get; init; } = [];
    public List<CategorySalesDto> TopCategories { get; init; } = [];
    public List<HourlySalesDto>   HourlySales   { get; init; } = [];
    public LoyaltyStatsDto  Loyalty{ get; init; } = new();
}

public record RevenueStatsDto
{
    public decimal Total        { get; init; }
    public decimal TotalCommission { get; init; }
    public decimal AvgOrderValue{ get; init; }
    public decimal GrowthPct    { get; init; }
    public decimal GrowthAmount { get; init; }
}

public record OrderStatsDto
{
    public int Total        { get; init; }
    public int Completed    { get; init; }
    public int Cancelled    { get; init; }
    public int Pending      { get; init; }
    public double SuccessRate { get; init; }
    public double GrowthPct { get; init; }
    public Dictionary<string, int> ByFulfillmentType { get; init; } = new();
    public Dictionary<string, int> ByPaymentMethod   { get; init; } = new();
}

public record CustomerStatsDto
{
    public int TotalActive  { get; init; }
    public int NewThisPeriod{ get; init; }
    public int Returning    { get; init; }
    public double RetentionRate { get; init; }
    public Dictionary<string, int> ByTier { get; init; } = new();
    public List<TopCustomerDto> TopSpenders { get; init; } = [];
}

public record DailyMetric(string Date, decimal Value, int Count = 0);
public record HourlySalesDto(int Hour, decimal Revenue, int Orders);

public record StoreRankDto
{
    public Guid    StoreId    { get; init; }
    public string  StoreName  { get; init; } = string.Empty;
    public string  StoreType  { get; init; } = string.Empty;
    public decimal Revenue    { get; init; }
    public int     Orders     { get; init; }
    public decimal Commission { get; init; }
    public double  Rating     { get; init; }
    public decimal GrowthPct  { get; init; }
}

public record CategorySalesDto
{
    public string  Category   { get; init; } = string.Empty;
    public decimal Revenue    { get; init; }
    public int     Orders     { get; init; }
    public double  Pct        { get; init; }
}

public record TopCustomerDto
{
    public string CustomerName { get; init; } = string.Empty;
    public decimal TotalSpent  { get; init; }
    public int     OrderCount  { get; init; }
    public string  Tier        { get; init; } = string.Empty;
}

public record LoyaltyStatsDto
{
    public int PointsIssued  { get; init; }
    public int PointsRedeemed{ get; init; }
    public int ActiveAccounts{ get; init; }
    public Dictionary<string, int> ByTier { get; init; } = new();
}

// Store-level analytics
public record StoreAnalyticsDto
{
    public string Period          { get; init; } = string.Empty;
    public RevenueStatsDto Revenue{ get; init; } = new();
    public OrderStatsDto   Orders { get; init; } = new();
    public List<DailyMetric> Chart{ get; init; } = [];
    public List<ProductSalesDto> TopProducts { get; init; } = [];
    public List<HourlySalesDto>  HourlySales { get; init; } = [];
    public double AvgRating       { get; init; }
    public int    TotalRatings    { get; init; }
    public decimal NetAfterCommission { get; init; }
    public decimal CommissionPaid     { get; init; }
}

public record ProductSalesDto
{
    public string  ProductName { get; init; } = string.Empty;
    public int     Quantity    { get; init; }
    public decimal Revenue     { get; init; }
    public int     Rank        { get; init; }
}

// ──────────────────────────────────────────────────────────────────────────
//  SERVICE
// ──────────────────────────────────────────────────────────────────────────
public interface IAnalyticsService
{
    Task<ApiResponse<MallAnalyticsDto>>  GetMallAnalyticsAsync(Guid mallId, string period, CancellationToken ct = default);
    Task<ApiResponse<StoreAnalyticsDto>> GetStoreAnalyticsAsync(Guid storeId, string period, CancellationToken ct = default);
    Task<ApiResponse<List<DailyMetric>>> GetRevenueChartAsync(Guid mallId, DateTime from, DateTime to, CancellationToken ct = default);
}

public class AnalyticsService : IAnalyticsService
{
    private readonly MesterXDbContext _db;
    private readonly ICacheService    _cache;
    private readonly ILogger<AnalyticsService> _log;

    public AnalyticsService(MesterXDbContext db, ICacheService cache,
        ILogger<AnalyticsService> log)
    { _db = db; _cache = cache; _log = log; }

    // ─── MALL ANALYTICS ───────────────────────────────────────────────────
    public async Task<ApiResponse<MallAnalyticsDto>> GetMallAnalyticsAsync(
        Guid mallId, string period, CancellationToken ct = default)
    {
        var (from, to, label) = ParsePeriod(period);
        var cacheKey = $"analytics:mall:{mallId}:{period}";

        return await _cache.GetOrSetAsync(cacheKey, async () =>
        {
            var prevFrom = from.AddTicks(-(to - from).Ticks);
            var prevTo   = from;

            // Orders
            var orders = await _db.MallOrders.AsNoTracking()
                .Where(o => o.MallId == mallId && o.PlacedAt >= from && o.PlacedAt <= to)
                .ToListAsync(ct);

            var prevOrders = await _db.MallOrders.AsNoTracking()
                .Where(o => o.MallId == mallId && o.PlacedAt >= prevFrom && o.PlacedAt <= prevTo)
                .ToListAsync(ct);

            // Store Orders for commission
            var storeOrders = await _db.StoreOrders.AsNoTracking()
                .Include(so => so.MallOrder)
                .Include(so => so.Store)
                .Where(so => so.MallOrder.MallId == mallId
                    && so.CreatedAt >= from && so.CreatedAt <= to)
                .ToListAsync(ct);

            // Revenue
            var totalRevenue = orders.Where(o => o.Status != MallOrderStatus.Cancelled)
                .Sum(o => o.Total);
            var prevRevenue  = prevOrders.Where(o => o.Status != MallOrderStatus.Cancelled)
                .Sum(o => o.Total);
            var commission   = storeOrders.Sum(s => s.CommissionAmt);
            var growthPct    = prevRevenue > 0
                ? Math.Round((double)(totalRevenue - prevRevenue) / (double)prevRevenue * 100, 1)
                : 0;

            // Orders stats
            var completed = orders.Count(o => o.Status == MallOrderStatus.Delivered);
            var cancelled = orders.Count(o => o.Status == MallOrderStatus.Cancelled);
            var orderGrowth = prevOrders.Count > 0
                ? Math.Round((double)(orders.Count - prevOrders.Count) / prevOrders.Count * 100, 1)
                : 0;

            // Daily chart
            var dailyRevenue = orders
                .Where(o => o.Status != MallOrderStatus.Cancelled)
                .GroupBy(o => o.PlacedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new DailyMetric(
                    g.Key.ToString("yyyy-MM-dd"),
                    g.Sum(o => o.Total),
                    g.Count()))
                .ToList();

            // Fill missing days
            dailyRevenue = FillMissingDays(dailyRevenue, from, to);

            // Orders by day
            var dailyOrders = orders
                .GroupBy(o => o.PlacedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new DailyMetric(g.Key.ToString("yyyy-MM-dd"), g.Count(), g.Count()))
                .ToList();

            // Hourly sales
            var hourly = orders
                .Where(o => o.Status != MallOrderStatus.Cancelled)
                .GroupBy(o => o.PlacedAt.Hour)
                .Select(g => new HourlySalesDto(g.Key, g.Sum(o => o.Total), g.Count()))
                .OrderBy(h => h.Hour).ToList();

            // Top stores
            var topStores = storeOrders
                .GroupBy(so => new { so.StoreId, Name = so.Store?.Name ?? "?" })
                .Select(g => new StoreRankDto
                {
                    StoreId   = g.Key.StoreId,
                    StoreName = g.Key.Name,
                    Revenue   = g.Sum(s => s.Subtotal),
                    Orders    = g.Count(),
                    Commission= g.Sum(s => s.CommissionAmt),
                })
                .OrderByDescending(s => s.Revenue)
                .Take(10).ToList();

            // Fulfillment breakdown
            var fulfillment = orders
                .GroupBy(o => o.FulfillmentType.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            // Payment method
            var payments = orders
                .GroupBy(o => o.PaymentMethod.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            // Customer stats
            var customerIds = orders.Select(o => o.CustomerId).Distinct().ToList();
            var totalCustomers = await _db.MallCustomers
                .CountAsync(c => c.MallId == mallId && !c.IsDeleted, ct);
            var newCustomers = await _db.MallCustomers
                .CountAsync(c => c.MallId == mallId && !c.IsDeleted
                    && c.CreatedAt >= from && c.CreatedAt <= to, ct);

            var tierBreakdown = await _db.MallCustomers.AsNoTracking()
                .Where(c => c.MallId == mallId && !c.IsDeleted)
                .GroupBy(c => c.Tier.ToString())
                .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);

            // Loyalty
            var loyaltyAccounts = await _db.Set<Domain.Entities.Phase4.LoyaltyAccount>()
                .AsNoTracking().Where(a => a.MallId == mallId).ToListAsync(ct);

            var pointsTxns = await _db.Set<Domain.Entities.Phase4.PointsTransaction>()
                .AsNoTracking()
                .Where(t => t.CreatedAt >= from && t.CreatedAt <= to)
                .ToListAsync(ct);

            return ApiResponse<MallAnalyticsDto>.Ok(new MallAnalyticsDto
            {
                Period = label,
                Revenue = new RevenueStatsDto
                {
                    Total          = totalRevenue,
                    TotalCommission= commission,
                    AvgOrderValue  = orders.Count > 0
                        ? Math.Round(totalRevenue / orders.Count, 2) : 0,
                    GrowthPct    = (decimal)growthPct,
                    GrowthAmount = totalRevenue - prevRevenue,
                },
                Orders = new OrderStatsDto
                {
                    Total       = orders.Count,
                    Completed   = completed,
                    Cancelled   = cancelled,
                    Pending     = orders.Count - completed - cancelled,
                    SuccessRate = orders.Count > 0
                        ? Math.Round((double)completed / orders.Count * 100, 1) : 0,
                    GrowthPct   = orderGrowth,
                    ByFulfillmentType = fulfillment,
                    ByPaymentMethod   = payments,
                },
                Customers = new CustomerStatsDto
                {
                    TotalActive   = totalCustomers,
                    NewThisPeriod = newCustomers,
                    Returning     = customerIds.Count - newCustomers,
                    RetentionRate = customerIds.Count > 0
                        ? Math.Round((double)(customerIds.Count - newCustomers) / customerIds.Count * 100, 1) : 0,
                    ByTier = tierBreakdown,
                },
                RevenueChart = dailyRevenue,
                OrdersChart  = dailyOrders,
                TopStores    = topStores,
                HourlySales  = hourly,
                Loyalty = new LoyaltyStatsDto
                {
                    PointsIssued   = pointsTxns.Where(t => t.Points > 0).Sum(t => t.Points),
                    PointsRedeemed = Math.Abs(pointsTxns.Where(t => t.Points < 0).Sum(t => t.Points)),
                    ActiveAccounts = loyaltyAccounts.Count(a => a.AvailablePoints > 0),
                    ByTier         = tierBreakdown,
                },
            });
        }, TimeSpan.FromMinutes(5), ct);
    }

    // ─── STORE ANALYTICS ──────────────────────────────────────────────────
    public async Task<ApiResponse<StoreAnalyticsDto>> GetStoreAnalyticsAsync(
        Guid storeId, string period, CancellationToken ct = default)
    {
        var (from, to, label) = ParsePeriod(period);
        var cacheKey = $"analytics:store:{storeId}:{period}";

        return await _cache.GetOrSetAsync(cacheKey, async () =>
        {
            var prevFrom = from.AddTicks(-(to - from).Ticks);
            var prevTo   = from;

            var storeOrders = await _db.StoreOrders.AsNoTracking()
                .Include(so => so.MallOrder)
                .Include(so => so.Items)
                .Where(so => so.StoreId == storeId
                    && so.CreatedAt >= from && so.CreatedAt <= to)
                .ToListAsync(ct);

            var prevStoreOrders = await _db.StoreOrders.AsNoTracking()
                .Include(so => so.MallOrder)
                .Where(so => so.StoreId == storeId
                    && so.CreatedAt >= prevFrom && so.CreatedAt <= prevTo)
                .ToListAsync(ct);

            var completedOrders = storeOrders
                .Where(so => so.MallOrder.Status == MallOrderStatus.Delivered).ToList();
            var revenue    = completedOrders.Sum(s => s.Subtotal);
            var prevRevenue= prevStoreOrders
                .Where(so => so.MallOrder.Status == MallOrderStatus.Delivered)
                .Sum(s => s.Subtotal);
            var commission = completedOrders.Sum(s => s.CommissionAmt);
            var growthPct  = prevRevenue > 0
                ? Math.Round((double)(revenue - prevRevenue) / (double)prevRevenue * 100, 1) : 0;

            // Daily chart
            var daily = completedOrders
                .GroupBy(so => so.CreatedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new DailyMetric(g.Key.ToString("yyyy-MM-dd"), g.Sum(s => s.Subtotal), g.Count()))
                .ToList();
            daily = FillMissingDays(daily, from, to);

            // Top products
            var allItems = storeOrders.SelectMany(so => so.Items).ToList();
            var topProducts = allItems
                .GroupBy(i => i.ProductName)
                .Select(g => new ProductSalesDto
                {
                    ProductName = g.Key,
                    Quantity    = g.Sum(i => i.Quantity),
                    Revenue     = g.Sum(i => i.Total),
                })
                .OrderByDescending(p => p.Revenue)
                .Take(10)
                .Select((p, i) => p with { Rank = i + 1 })
                .ToList();

            // Hourly
            var hourly = completedOrders
                .GroupBy(so => so.CreatedAt.Hour)
                .Select(g => new HourlySalesDto(g.Key, g.Sum(s => s.Subtotal), g.Count()))
                .OrderBy(h => h.Hour).ToList();

            // Rating summary
            var ratingSummary = await _db.Set<Domain.Entities.Phase3.StoreRatingSummary>()
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.StoreId == storeId, ct);

            return ApiResponse<StoreAnalyticsDto>.Ok(new StoreAnalyticsDto
            {
                Period = label,
                Revenue = new RevenueStatsDto
                {
                    Total        = revenue,
                    AvgOrderValue= storeOrders.Count > 0 ? Math.Round(revenue / storeOrders.Count, 2) : 0,
                    GrowthPct    = (decimal)growthPct,
                    GrowthAmount = revenue - prevRevenue,
                },
                Orders = new OrderStatsDto
                {
                    Total     = storeOrders.Count,
                    Completed = completedOrders.Count,
                    Cancelled = storeOrders.Count(so => so.Status == Domain.Entities.Phase3.StoreOrderStatus.Cancelled),
                    SuccessRate= storeOrders.Count > 0
                        ? Math.Round((double)completedOrders.Count / storeOrders.Count * 100, 1) : 0,
                    GrowthPct = growthPct,
                },
                Chart              = daily,
                TopProducts        = topProducts,
                HourlySales        = hourly,
                AvgRating          = (double)(ratingSummary?.AvgStars ?? 0),
                TotalRatings       = ratingSummary?.TotalRatings ?? 0,
                NetAfterCommission = revenue - commission,
                CommissionPaid     = commission,
            });
        }, TimeSpan.FromMinutes(5), ct);
    }

    // ─── REVENUE CHART ────────────────────────────────────────────────────
    public async Task<ApiResponse<List<DailyMetric>>> GetRevenueChartAsync(
        Guid mallId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var daily = await _db.MallOrders.AsNoTracking()
            .Where(o => o.MallId == mallId
                && o.PlacedAt >= from && o.PlacedAt <= to
                && o.Status != MallOrderStatus.Cancelled)
            .GroupBy(o => o.PlacedAt.Date)
            .Select(g => new { Date = g.Key, Revenue = g.Sum(o => o.Total), Count = g.Count() })
            .OrderBy(d => d.Date)
            .ToListAsync(ct);

        var filled = FillMissingDays(
            daily.Select(d => new DailyMetric(
                d.Date.ToString("yyyy-MM-dd"), d.Revenue, d.Count)).ToList(), from, to);

        return ApiResponse<List<DailyMetric>>.Ok(filled);
    }

    // ─── HELPERS ──────────────────────────────────────────────────────────
    private static (DateTime from, DateTime to, string label) ParsePeriod(string period)
    {
        var now = DateTime.UtcNow;
        return period switch
        {
            "today"    => (now.Date, now, "اليوم"),
            "week"     => (now.Date.AddDays(-7), now, "آخر 7 أيام"),
            "month"    => (new DateTime(now.Year, now.Month, 1, 0,0,0, DateTimeKind.Utc), now, "هذا الشهر"),
            "quarter"  => (now.AddMonths(-3), now, "آخر 3 أشهر"),
            "year"     => (new DateTime(now.Year, 1, 1, 0,0,0, DateTimeKind.Utc), now, "هذه السنة"),
            _          => (now.Date.AddDays(-30), now, "آخر 30 يوم")
        };
    }

    private static List<DailyMetric> FillMissingDays(
        List<DailyMetric> data, DateTime from, DateTime to)
    {
        var dict = data.ToDictionary(d => d.Date, d => d);
        var result = new List<DailyMetric>();
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
        {
            var key = d.ToString("yyyy-MM-dd");
            result.Add(dict.TryGetValue(key, out var v) ? v : new DailyMetric(key, 0, 0));
        }
        return result;
    }
}
