using MesterX.Application.DTOs;
using MesterX.Domain.Entities.Mall;
using MesterX.Domain.Entities.Payment;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesterX.Application.Services.Phase2;

// ──────────────────────────────────────────────────────────────────────────
//  DTOs
// ──────────────────────────────────────────────────────────────────────────
public record CommissionReportDto
{
    public Guid   StoreId      { get; init; }
    public string StoreName    { get; init; } = string.Empty;
    public string StoreType    { get; init; } = string.Empty;
    public int    TotalOrders  { get; init; }
    public decimal GrossRevenue{ get; init; }
    public decimal CommissionRate { get; init; }
    public decimal CommissionAmt  { get; init; }
    public decimal NetPayable     { get; init; }
    public string  PeriodLabel    { get; init; } = string.Empty;
}

public record MallRevenueBreakdownDto
{
    public decimal TotalRevenue     { get; init; }
    public decimal TotalCommission  { get; init; }
    public decimal TotalPaidOut     { get; init; }     // للمحلات
    public int     TotalOrders      { get; init; }
    public decimal AvgOrderValue    { get; init; }
    public decimal AvgCommissionRate{ get; init; }
    public List<CommissionReportDto> ByStore { get; init; } = [];
    public List<DailyRevenueDto>     Daily   { get; init; } = [];
}

public record DailyRevenueDto
{
    public string  Date       { get; init; } = string.Empty;
    public decimal Revenue    { get; init; }
    public decimal Commission { get; init; }
    public int     Orders     { get; init; }
}

public record SettlementRequest(
    Guid   MallId,
    Guid   StoreId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    string? Notes
);

public record StoreFinancialsDto
{
    public Guid    StoreId          { get; init; }
    public string  StoreName        { get; init; } = string.Empty;
    public decimal ThisMonthRevenue { get; init; }
    public decimal ThisMonthCommission { get; init; }
    public decimal ThisMonthNet     { get; init; }
    public decimal PendingSettlement{ get; init; }
    public List<CommissionSettlementDto> Settlements { get; init; } = [];
}

public record CommissionSettlementDto
{
    public Guid     Id           { get; init; }
    public string   PeriodLabel  { get; init; } = string.Empty;
    public int      TotalOrders  { get; init; }
    public decimal  GrossRevenue { get; init; }
    public decimal  CommissionAmt{ get; init; }
    public decimal  NetPayable   { get; init; }
    public string   Status       { get; init; } = string.Empty;
    public DateTime? SettledAt   { get; init; }
    public DateTime CreatedAt    { get; init; }
}

// ──────────────────────────────────────────────────────────────────────────
//  SERVICE
// ──────────────────────────────────────────────────────────────────────────
public interface ICommissionService
{
    Task<ApiResponse<MallRevenueBreakdownDto>> GetRevenueBreakdownAsync(
        Guid mallId, DateTime from, DateTime to, CancellationToken ct = default);

    Task<ApiResponse<StoreFinancialsDto>> GetStoreFinancialsAsync(
        Guid storeId, CancellationToken ct = default);

    Task<ApiResponse<CommissionSettlementDto>> CreateSettlementAsync(
        SettlementRequest req, Guid createdBy, CancellationToken ct = default);

    Task<ApiResponse> MarkSettlementCompletedAsync(
        Guid mallId, Guid settlementId, CancellationToken ct = default);

    Task SnapshotDailyAnalyticsAsync(Guid mallId, CancellationToken ct = default);
}

public class CommissionService : ICommissionService
{
    private readonly MesterXDbContext _db;
    private readonly ILogger<CommissionService> _log;

    public CommissionService(MesterXDbContext db, ILogger<CommissionService> log)
    { _db = db; _log = log; }

    // ─── REVENUE BREAKDOWN (MallAdmin) ────────────────────────────────────
    public async Task<ApiResponse<MallRevenueBreakdownDto>> GetRevenueBreakdownAsync(
        Guid mallId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var storeOrders = await _db.StoreOrders
            .AsNoTracking()
            .Include(so => so.MallOrder)
            .Include(so => so.Store)
            .Where(so => so.MallOrder.MallId == mallId
                && so.CreatedAt >= from && so.CreatedAt <= to
                && so.MallOrder.Status != MallOrderStatus.Cancelled)
            .ToListAsync(ct);

        var byStore = storeOrders
            .GroupBy(so => new { so.StoreId, Name = so.Store?.Name ?? "؟" })
            .Select(g => new CommissionReportDto
            {
                StoreId       = g.Key.StoreId,
                StoreName     = g.Key.Name,
                TotalOrders   = g.Count(),
                GrossRevenue  = g.Sum(s => s.Subtotal),
                CommissionRate= g.Average(s => s.CommissionRate),
                CommissionAmt = g.Sum(s => s.CommissionAmt),
                NetPayable    = g.Sum(s => s.StoreTotal),
                PeriodLabel   = $"{from:dd/MM} – {to:dd/MM/yyyy}"
            })
            .OrderByDescending(s => s.GrossRevenue)
            .ToList();

        // Daily breakdown (last N days within range)
        var mallOrders = await _db.MallOrders
            .AsNoTracking()
            .Where(o => o.MallId == mallId
                && o.PlacedAt >= from && o.PlacedAt <= to
                && o.Status != MallOrderStatus.Cancelled)
            .ToListAsync(ct);

        var daily = mallOrders
            .GroupBy(o => o.PlacedAt.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var dayStoreOrders = storeOrders
                    .Where(so => so.CreatedAt.Date == g.Key).ToList();
                return new DailyRevenueDto
                {
                    Date       = g.Key.ToString("yyyy-MM-dd"),
                    Revenue    = g.Sum(o => o.Total),
                    Commission = dayStoreOrders.Sum(s => s.CommissionAmt),
                    Orders     = g.Count()
                };
            }).ToList();

        var totalRevenue    = byStore.Sum(s => s.GrossRevenue);
        var totalCommission = byStore.Sum(s => s.CommissionAmt);

        return ApiResponse<MallRevenueBreakdownDto>.Ok(new MallRevenueBreakdownDto
        {
            TotalRevenue      = totalRevenue,
            TotalCommission   = totalCommission,
            TotalPaidOut      = byStore.Sum(s => s.NetPayable),
            TotalOrders       = mallOrders.Count,
            AvgOrderValue     = mallOrders.Count > 0
                ? Math.Round(mallOrders.Average(o => o.Total), 2) : 0,
            AvgCommissionRate = byStore.Count > 0
                ? Math.Round(byStore.Average(s => s.CommissionRate) * 100, 2) : 0,
            ByStore = byStore,
            Daily   = daily
        });
    }

    // ─── STORE FINANCIALS (Store Owner) ───────────────────────────────────
    public async Task<ApiResponse<StoreFinancialsDto>> GetStoreFinancialsAsync(
        Guid storeId, CancellationToken ct = default)
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1,
            0, 0, 0, DateTimeKind.Utc);

        var thisMonth = await _db.StoreOrders
            .AsNoTracking()
            .Include(so => so.MallOrder)
            .Where(so => so.StoreId == storeId
                && so.CreatedAt >= monthStart
                && so.MallOrder.Status != MallOrderStatus.Cancelled)
            .ToListAsync(ct);

        var settlements = await _db.Set<CommissionSettlement>()
            .AsNoTracking()
            .Where(s => s.StoreId == storeId)
            .OrderByDescending(s => s.PeriodStart)
            .Take(12)
            .ToListAsync(ct);

        var store = await _db.Tenants.FindAsync([storeId], ct);

        var thisMonthRevenue    = thisMonth.Sum(s => s.Subtotal);
        var thisMonthCommission = thisMonth.Sum(s => s.CommissionAmt);
        var pendingSettlement   = thisMonth
            .Where(s => s.MallOrder.Status == MallOrderStatus.Delivered)
            .Sum(s => s.StoreTotal);

        return ApiResponse<StoreFinancialsDto>.Ok(new StoreFinancialsDto
        {
            StoreId              = storeId,
            StoreName            = store?.Name ?? string.Empty,
            ThisMonthRevenue     = thisMonthRevenue,
            ThisMonthCommission  = thisMonthCommission,
            ThisMonthNet         = thisMonthRevenue - thisMonthCommission,
            PendingSettlement    = pendingSettlement,
            Settlements          = settlements.Select(s => new CommissionSettlementDto
            {
                Id            = s.Id,
                PeriodLabel   = $"{s.PeriodStart:dd/MM} – {s.PeriodEnd:dd/MM/yyyy}",
                TotalOrders   = s.TotalOrders,
                GrossRevenue  = s.GrossRevenue,
                CommissionAmt = s.CommissionAmt,
                NetPayable    = s.NetPayable,
                Status        = s.Status.ToString(),
                SettledAt     = s.SettledAt,
                CreatedAt     = s.CreatedAt
            }).ToList()
        });
    }

    // ─── CREATE SETTLEMENT ────────────────────────────────────────────────
    public async Task<ApiResponse<CommissionSettlementDto>> CreateSettlementAsync(
        SettlementRequest req, Guid createdBy, CancellationToken ct = default)
    {
        // Check no overlap
        var overlap = await _db.Set<CommissionSettlement>().AnyAsync(s =>
            s.StoreId == req.StoreId
            && s.PeriodStart < req.PeriodEnd
            && s.PeriodEnd   > req.PeriodStart
            && s.Status != SettlementStatus.Failed, ct);

        if (overlap)
            return ApiResponse<CommissionSettlementDto>.Fail(
                "يوجد تسوية مداخلة لنفس الفترة. راجع التسويات الموجودة.");

        var storeOrders = await _db.StoreOrders
            .AsNoTracking()
            .Include(so => so.MallOrder)
            .Where(so => so.StoreId == req.StoreId
                && so.CreatedAt >= req.PeriodStart
                && so.CreatedAt <= req.PeriodEnd
                && so.MallOrder.Status == MallOrderStatus.Delivered)
            .ToListAsync(ct);

        if (!storeOrders.Any())
            return ApiResponse<CommissionSettlementDto>.Fail(
                "لا يوجد طلبات مسلّمة في هذه الفترة.");

        var gross      = storeOrders.Sum(s => s.Subtotal);
        var commission = storeOrders.Sum(s => s.CommissionAmt);

        var settlement = new CommissionSettlement
        {
            MallId         = req.MallId,
            StoreId        = req.StoreId,
            PeriodStart    = req.PeriodStart,
            PeriodEnd      = req.PeriodEnd,
            TotalOrders    = storeOrders.Count,
            GrossRevenue   = gross,
            CommissionRate = storeOrders.Average(s => s.CommissionRate),
            CommissionAmt  = commission,
            NetPayable     = gross - commission,
            Notes          = req.Notes,
            CreatedBy      = createdBy,
        };
        _db.Set<CommissionSettlement>().Add(settlement);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Settlement created: Store {StoreId} — {Period} — Net {Net} EGP",
            req.StoreId,
            $"{req.PeriodStart:dd/MM} – {req.PeriodEnd:dd/MM/yyyy}",
            settlement.NetPayable);

        return ApiResponse<CommissionSettlementDto>.Ok(new CommissionSettlementDto
        {
            Id            = settlement.Id,
            PeriodLabel   = $"{req.PeriodStart:dd/MM} – {req.PeriodEnd:dd/MM/yyyy}",
            TotalOrders   = settlement.TotalOrders,
            GrossRevenue  = settlement.GrossRevenue,
            CommissionAmt = settlement.CommissionAmt,
            NetPayable    = settlement.NetPayable,
            Status        = settlement.Status.ToString(),
            CreatedAt     = settlement.CreatedAt
        });
    }

    // ─── MARK SETTLED ────────────────────────────────────────────────────
    public async Task<ApiResponse> MarkSettlementCompletedAsync(
        Guid mallId, Guid settlementId, CancellationToken ct = default)
    {
        var s = await _db.Set<CommissionSettlement>()
            .FirstOrDefaultAsync(x => x.Id == settlementId && x.MallId == mallId, ct);
        if (s == null) return ApiResponse.Fail("التسوية غير موجودة.");

        s.Status     = SettlementStatus.Completed;
        s.SettledAt  = DateTime.UtcNow;
        s.UpdatedAt  = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok();
    }

    // ─── DAILY ANALYTICS SNAPSHOT ────────────────────────────────────────
    public async Task SnapshotDailyAnalyticsAsync(Guid mallId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var start = DateTime.UtcNow.Date;
        var end   = start.AddDays(1);

        var orders = await _db.MallOrders
            .AsNoTracking()
            .Where(o => o.MallId == mallId
                && o.PlacedAt >= start && o.PlacedAt < end)
            .ToListAsync(ct);

        var storeOrders = await _db.StoreOrders
            .AsNoTracking()
            .Include(so => so.MallOrder)
            .Where(so => so.MallOrder.MallId == mallId
                && so.CreatedAt >= start && so.CreatedAt < end)
            .ToListAsync(ct);

        var newCustomers = await _db.MallCustomers
            .CountAsync(c => c.MallId == mallId
                && c.CreatedAt >= start && c.CreatedAt < end, ct);

        var snapshot = await _db.Set<MallAnalyticsDaily>()
            .FirstOrDefaultAsync(a => a.MallId == mallId && a.SnapshotDate == today, ct)
            ?? new MallAnalyticsDaily { MallId = mallId, SnapshotDate = today };

        snapshot.TotalOrders     = orders.Count;
        snapshot.TotalRevenue    = orders.Sum(o => o.Total);
        snapshot.TotalCommission = storeOrders.Sum(s => s.CommissionAmt);
        snapshot.NewCustomers    = newCustomers;
        snapshot.ActiveStores    = storeOrders.Select(s => s.StoreId).Distinct().Count();
        snapshot.AvgOrderValue   = orders.Count > 0 ? orders.Average(o => o.Total) : 0;

        if (snapshot.Id == Guid.Empty)
            _db.Set<MallAnalyticsDaily>().Add(snapshot);

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Analytics snapshot for {Mall} on {Date} saved", mallId, today);
    }
}
