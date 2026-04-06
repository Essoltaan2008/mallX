using MesterX.Application.DTOs;
using MesterX.Domain.Entities.Phase4;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesterX.Application.Services.Phase4;

// ──────────────────────────────────────────────────────────────────────────
//  DTOs
// ──────────────────────────────────────────────────────────────────────────
public record LoyaltyWalletDto
{
    public Guid    AccountId      { get; init; }
    public int     AvailablePoints{ get; init; }
    public int     LifetimePoints { get; init; }
    public string  Tier           { get; init; } = string.Empty;
    public string  TierAr         { get; init; } = string.Empty;
    public int     PointsToNext   { get; init; }
    public string? NextTier       { get; init; }
    public decimal EgpValue       { get; init; }          // 100 pts = 1 EGP
    public DateTime? ExpiresAt    { get; init; }
    public List<PointsTransactionDto> RecentTransactions { get; init; } = [];
    public TierBenefitsDto Benefits { get; init; } = new();
}

public record PointsTransactionDto
{
    public Guid        Id          { get; init; }
    public string      Source      { get; init; } = string.Empty;
    public string      SourceAr    { get; init; } = string.Empty;
    public int         Points      { get; init; }
    public int         BalanceAfter{ get; init; }
    public string?     Description { get; init; }
    public DateTime    CreatedAt   { get; init; }
    public bool        IsEarning   => Points > 0;
}

public record TierBenefitsDto
{
    public decimal Multiplier    { get; init; } = 1;
    public bool    FreeDelivery  { get; init; } = false;
    public string  Description   { get; init; } = string.Empty;
}

public record RedeemPointsRequest(
    Guid   MallOrderId,
    int    PointsToRedeem
);

public record RedeemResultDto
{
    public int     PointsUsed    { get; init; }
    public decimal DiscountApplied { get; init; }
    public int     RemainingPoints { get; init; }
}

public record EarnPointsRequest(
    Guid   CustomerId,
    Guid   MallOrderId,
    decimal OrderTotal,
    Guid   MallId
);

// ──────────────────────────────────────────────────────────────────────────
//  SERVICE
// ──────────────────────────────────────────────────────────────────────────
public interface ILoyaltyService
{
    Task<ApiResponse<LoyaltyWalletDto>> GetWalletAsync(Guid customerId, Guid mallId, CancellationToken ct = default);
    Task<ApiResponse<List<PointsTransactionDto>>> GetHistoryAsync(Guid customerId, int page, int size, CancellationToken ct = default);
    Task<ApiResponse<RedeemResultDto>> RedeemAsync(Guid customerId, RedeemPointsRequest req, CancellationToken ct = default);
    Task EarnPointsAsync(EarnPointsRequest req, CancellationToken ct = default);
    Task AwardBonusAsync(Guid customerId, Guid mallId, PointsSource source, int points, string description, CancellationToken ct = default);
    Task ProcessExpiryAsync(CancellationToken ct = default);
}

public class LoyaltyService : ILoyaltyService
{
    private readonly MesterXDbContext _db;
    private readonly ILogger<LoyaltyService> _log;
    private const decimal PTS_PER_EGP  = 1.0m;       // 1 EGP = 1 pt
    private const decimal EGP_PER_100_PTS = 1.0m;    // 100 pts = 1 EGP
    private const decimal MAX_REDEEM_PCT  = 0.20m;    // max 20% of order by points

    public LoyaltyService(MesterXDbContext db, ILogger<LoyaltyService> log)
    { _db = db; _log = log; }

    // ─── GET WALLET ───────────────────────────────────────────────────────
    public async Task<ApiResponse<LoyaltyWalletDto>> GetWalletAsync(
        Guid customerId, Guid mallId, CancellationToken ct = default)
    {
        var account = await GetOrCreateAccountAsync(customerId, mallId, ct);

        var recent = await _db.Set<PointsTransaction>()
            .AsNoTracking()
            .Where(t => t.AccountId == account.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        return ApiResponse<LoyaltyWalletDto>.Ok(new LoyaltyWalletDto
        {
            AccountId       = account.Id,
            AvailablePoints = account.AvailablePoints,
            LifetimePoints  = account.LifetimePoints,
            Tier            = account.Tier,
            TierAr          = TierAr(account.Tier),
            PointsToNext    = LoyaltyAccount.PointsToNextTier(account.LifetimePoints),
            NextTier        = NextTier(account.Tier),
            EgpValue        = Math.Round(account.AvailablePoints / 100m * EGP_PER_100_PTS, 2),
            ExpiresAt       = account.PointsExpireAt,
            RecentTransactions = recent.Select(MapTxn).ToList(),
            Benefits        = GetBenefits(account.Tier),
        });
    }

    // ─── GET HISTORY ──────────────────────────────────────────────────────
    public async Task<ApiResponse<List<PointsTransactionDto>>> GetHistoryAsync(
        Guid customerId, int page, int size, CancellationToken ct = default)
    {
        var accountId = await _db.Set<LoyaltyAccount>()
            .Where(a => a.CustomerId == customerId)
            .Select(a => a.Id)
            .FirstOrDefaultAsync(ct);

        if (accountId == Guid.Empty)
            return ApiResponse<List<PointsTransactionDto>>.Ok([]);

        var txns = await _db.Set<PointsTransaction>()
            .AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);

        return ApiResponse<List<PointsTransactionDto>>.Ok(txns.Select(MapTxn).ToList());
    }

    // ─── REDEEM ───────────────────────────────────────────────────────────
    public async Task<ApiResponse<RedeemResultDto>> RedeemAsync(
        Guid customerId, RedeemPointsRequest req, CancellationToken ct = default)
    {
        var order = await _db.MallOrders
            .FirstOrDefaultAsync(o => o.Id == req.MallOrderId
                && o.CustomerId == customerId, ct);
        if (order == null)
            return ApiResponse<RedeemResultDto>.Fail("الطلب غير موجود.");

        var account = await GetOrCreateAccountAsync(customerId, order.MallId, ct);

        if (req.PointsToRedeem > account.AvailablePoints)
            return ApiResponse<RedeemResultDto>.Fail(
                $"نقاطك المتاحة {account.AvailablePoints} أقل من المطلوب.");

        // Max 20% of order total can be paid by points
        var maxDiscount    = Math.Round(order.Total * MAX_REDEEM_PCT, 2);
        var pointsDiscount = Math.Round(req.PointsToRedeem / 100m * EGP_PER_100_PTS, 2);
        var actualDiscount = Math.Min(pointsDiscount, maxDiscount);
        var actualPoints   = (int)Math.Round(actualDiscount / EGP_PER_100_PTS * 100);

        if (actualPoints <= 0)
            return ApiResponse<RedeemResultDto>.Fail("القيمة أقل من الحد الأدنى للاستبدال.");

        // Deduct from account
        account.RedeemedPoints += actualPoints;
        account.UpdatedAt       = DateTime.UtcNow;

        // Record transaction
        _db.Set<PointsTransaction>().Add(new PointsTransaction
        {
            AccountId    = account.Id,
            CustomerId   = customerId,
            MallOrderId  = req.MallOrderId,
            Source       = PointsSource.Redemption,
            Points       = -actualPoints,
            BalanceAfter = account.AvailablePoints,
            Description  = $"استبدال {actualPoints} نقطة مقابل خصم {actualDiscount} ج.م",
        });

        // Apply discount to order
        order.DiscountAmount += actualDiscount;
        order.Total          -= actualDiscount;
        order.UpdatedAt       = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Customer {Id} redeemed {Pts} pts for {EGP} EGP off order {Order}",
            customerId, actualPoints, actualDiscount, req.MallOrderId);

        return ApiResponse<RedeemResultDto>.Ok(new RedeemResultDto
        {
            PointsUsed      = actualPoints,
            DiscountApplied = actualDiscount,
            RemainingPoints = account.AvailablePoints,
        });
    }

    // ─── EARN POINTS (called after order delivered) ───────────────────────
    public async Task EarnPointsAsync(EarnPointsRequest req, CancellationToken ct = default)
    {
        var account    = await GetOrCreateAccountAsync(req.CustomerId, req.MallId, ct);
        var rule       = await GetActiveRuleAsync(req.MallId, null, ct);
        var multiplier = LoyaltyAccount.GetMultiplier(account.Tier);
        var baseRate   = rule?.PointsPerEgp ?? PTS_PER_EGP;
        var earned     = (int)Math.Round(req.OrderTotal * baseRate * multiplier);

        if (earned <= 0) return;

        account.LifetimePoints += earned;
        account.PointsExpireAt  = DateTime.UtcNow.AddMonths(12);
        account.UpdatedAt       = DateTime.UtcNow;

        // Update customer loyalty_points
        var customer = await _db.MallCustomers.FindAsync([req.CustomerId], ct);
        if (customer != null)
        {
            customer.LoyaltyPoints = account.AvailablePoints;
            customer.UpdatedAt     = DateTime.UtcNow;
        }

        _db.Set<PointsTransaction>().Add(new PointsTransaction
        {
            AccountId    = account.Id,
            CustomerId   = req.CustomerId,
            MallOrderId  = req.MallOrderId,
            Source       = PointsSource.Purchase,
            Points       = earned,
            BalanceAfter = account.AvailablePoints,
            Description  = $"مكافأة شراء — {req.OrderTotal:N2} ج.م × {baseRate} × {multiplier}x",
            ExpiresAt    = DateTime.UtcNow.AddMonths(12),
        });

        // Check tier upgrade
        var newTier = LoyaltyAccount.CalculateTier(account.LifetimePoints);
        if (newTier != account.Tier)
        {
            _log.LogInformation("Customer {Id} upgraded from {Old} to {New}!",
                req.CustomerId, account.Tier, newTier);
            account.Tier          = newTier;
            account.TierUpdatedAt = DateTime.UtcNow;
            if (customer != null) customer.Tier = Enum.Parse<CustomerTier>(newTier);
        }

        await _db.SaveChangesAsync(ct);
        _log.LogDebug("Earned {Pts} pts for customer {Id} (order {Order})",
            earned, req.CustomerId, req.MallOrderId);
    }

    // ─── AWARD BONUS ──────────────────────────────────────────────────────
    public async Task AwardBonusAsync(Guid customerId, Guid mallId,
        PointsSource source, int points, string description, CancellationToken ct = default)
    {
        var account = await GetOrCreateAccountAsync(customerId, mallId, ct);
        account.LifetimePoints += points;
        account.UpdatedAt       = DateTime.UtcNow;

        _db.Set<PointsTransaction>().Add(new PointsTransaction
        {
            AccountId    = account.Id,
            CustomerId   = customerId,
            Source       = source,
            Points       = points,
            BalanceAfter = account.AvailablePoints,
            Description  = description,
        });

        var customer = await _db.MallCustomers.FindAsync([customerId], ct);
        if (customer != null)
        {
            customer.LoyaltyPoints = account.AvailablePoints;
            customer.UpdatedAt     = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    // ─── PROCESS EXPIRY (Background Job) ─────────────────────────────────
    public async Task ProcessExpiryAsync(CancellationToken ct = default)
    {
        var expired = await _db.Set<LoyaltyAccount>()
            .Where(a => a.PointsExpireAt.HasValue
                && a.PointsExpireAt < DateTime.UtcNow
                && a.AvailablePoints > 0)
            .ToListAsync(ct);

        foreach (var account in expired)
        {
            var expiredPts = account.AvailablePoints;
            account.RedeemedPoints += expiredPts;   // "consume" expired
            account.PointsExpireAt  = null;
            account.UpdatedAt       = DateTime.UtcNow;

            _db.Set<PointsTransaction>().Add(new PointsTransaction
            {
                AccountId    = account.Id,
                CustomerId   = account.CustomerId,
                Source       = PointsSource.Expiry,
                Points       = -expiredPts,
                BalanceAfter = 0,
                Description  = "انتهاء صلاحية النقاط (12 شهراً من عدم النشاط)",
            });

            _log.LogInformation("Expired {Pts} pts for customer {Id}",
                expiredPts, account.CustomerId);
        }

        if (expired.Any()) await _db.SaveChangesAsync(ct);
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────
    private async Task<LoyaltyAccount> GetOrCreateAccountAsync(
        Guid customerId, Guid mallId, CancellationToken ct)
    {
        var account = await _db.Set<LoyaltyAccount>()
            .FirstOrDefaultAsync(a => a.CustomerId == customerId && a.MallId == mallId, ct);

        if (account != null) return account;

        // Sync initial points from customer record
        var customer = await _db.MallCustomers.FindAsync([customerId], ct);
        account = new LoyaltyAccount
        {
            CustomerId     = customerId,
            MallId         = mallId,
            LifetimePoints = customer?.LoyaltyPoints ?? 0,
            Tier           = customer?.Tier.ToString() ?? "Bronze",
        };
        _db.Set<LoyaltyAccount>().Add(account);
        await _db.SaveChangesAsync(ct);
        return account;
    }

    private async Task<LoyaltyRule?> GetActiveRuleAsync(
        Guid mallId, Guid? storeId, CancellationToken ct)
        => await _db.Set<LoyaltyRule>()
            .AsNoTracking()
            .Where(r => r.MallId == mallId && r.IsActive
                && (r.StoreId == null || r.StoreId == storeId)
                && r.ValidFrom <= DateTime.UtcNow
                && (r.ValidTo == null || r.ValidTo > DateTime.UtcNow))
            .OrderByDescending(r => r.StoreId)   // store-specific overrides mall-wide
            .FirstOrDefaultAsync(ct);

    private static TierBenefitsDto GetBenefits(string tier) => tier switch
    {
        "Gold"   => new TierBenefitsDto { Multiplier = 2, FreeDelivery = true,
            Description = "2x نقاط + توصيل مجاني على جميع الطلبات" },
        "Silver" => new TierBenefitsDto { Multiplier = 1.5m,
            Description = "1.5x نقاط على جميع مشترياتك" },
        _        => new TierBenefitsDto { Multiplier = 1,
            Description = "اجمع نقاطك للوصول لـ Silver وتحصل على مزايا أكثر!" },
    };

    private static string TierAr(string tier) => tier switch
    {
        "Gold"   => "ذهبي 🥇",
        "Silver" => "فضي 🥈",
        _        => "برونزي 🥉",
    };

    private static string? NextTier(string tier) => tier switch
    {
        "Bronze" => "Silver",
        "Silver" => "Gold",
        _        => null,
    };

    private static string SourceAr(PointsSource s) => s switch
    {
        PointsSource.Purchase   => "مكافأة شراء",
        PointsSource.Referral   => "إحالة صديق",
        PointsSource.Birthday   => "هدية عيد الميلاد 🎂",
        PointsSource.Rating     => "مكافأة تقييم",
        PointsSource.Signup     => "ترحيب بالعضو الجديد",
        PointsSource.Redemption => "استبدال نقاط",
        PointsSource.Adjustment => "تعديل إداري",
        PointsSource.Expiry     => "انتهاء صلاحية",
        _ => s.ToString()
    };

    private static PointsTransactionDto MapTxn(PointsTransaction t) => new()
    {
        Id           = t.Id,
        Source       = t.Source.ToString(),
        SourceAr     = SourceAr(t.Source),
        Points       = t.Points,
        BalanceAfter = t.BalanceAfter,
        Description  = t.Description,
        CreatedAt    = t.CreatedAt,
    };
}
