using MesterX.Application.DTOs;
using MesterX.Infrastructure.Caching;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace MesterX.Application.Services.Phase9;

// ──────────────────────────────────────────────────────────────────────────
//  DOMAIN ENTITIES (inline for Phase 9)
// ──────────────────────────────────────────────────────────────────────────
public enum WalletTxnType   { TopUp, Purchase, Refund, Bonus, Transfer, Withdrawal, Adjustment }
public enum WalletTxnStatus { Pending, Completed, Failed, Reversed }

public class CustomerWallet
{
    [Key] public Guid   Id           { get; set; } = Guid.NewGuid();
    [Required] public Guid CustomerId{ get; set; }
    [Required] public Guid MallId    { get; set; }
    public decimal Balance           { get; set; } = 0;
    public decimal TotalToppedUp     { get; set; } = 0;
    public decimal TotalSpent        { get; set; } = 0;
    public bool IsActive             { get; set; } = true;
    public DateTime CreatedAt        { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt        { get; set; } = DateTime.UtcNow;

    public virtual ICollection<WalletTransaction> Transactions { get; set; } = [];
}

public class WalletTransaction
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid WalletId    { get; set; }
    [Required] public Guid CustomerId  { get; set; }
    public Guid? MallOrderId           { get; set; }
    public WalletTxnType   Type        { get; set; }
    public WalletTxnStatus Status      { get; set; } = WalletTxnStatus.Completed;
    public decimal Amount              { get; set; }
    public decimal BalanceBefore       { get; set; }
    public decimal BalanceAfter        { get; set; }
    public string? Reference           { get; set; }
    public string? Description         { get; set; }
    public DateTime CreatedAt          { get; set; } = DateTime.UtcNow;
    public virtual CustomerWallet Wallet { get; set; } = null!;
}

// ──────────────────────────────────────────────────────────────────────────
//  DTOs
// ──────────────────────────────────────────────────────────────────────────
public record WalletDto
{
    public Guid    Id            { get; init; }
    public decimal Balance       { get; init; }
    public decimal TotalToppedUp { get; init; }
    public decimal TotalSpent    { get; init; }
    public string  BalanceLabel  { get; init; } = string.Empty;  // formatted
    public List<WalletTxnDto> RecentTransactions { get; init; } = [];
}

public record WalletTxnDto
{
    public Guid   Id          { get; init; }
    public string Type        { get; init; } = string.Empty;
    public string TypeAr      { get; init; } = string.Empty;
    public decimal Amount     { get; init; }
    public decimal BalanceAfter{ get; init; }
    public string? Description { get; init; }
    public string? Reference   { get; init; }
    public bool   IsCredit    => Amount > 0;
    public DateTime CreatedAt { get; init; }
}

public record TopUpRequest(
    decimal Amount,          // EGP
    string  Gateway,         // Paymob | Cash | BankTransfer
    string? GatewayRef       // transaction reference from gateway
);

public record SpendFromWalletRequest(
    Guid    MallOrderId,
    decimal Amount           // max = wallet balance
);

public record WithdrawRequest(
    decimal Amount,
    string  BankAccount,
    string  BankName
);

// ──────────────────────────────────────────────────────────────────────────
//  SERVICE
// ──────────────────────────────────────────────────────────────────────────
public interface IWalletService
{
    Task<ApiResponse<WalletDto>>  GetWalletAsync(Guid customerId, Guid mallId, CancellationToken ct = default);
    Task<ApiResponse<WalletDto>>  TopUpAsync(Guid customerId, Guid mallId, TopUpRequest req, CancellationToken ct = default);
    Task<ApiResponse<WalletTxnDto>> SpendAsync(Guid customerId, SpendFromWalletRequest req, CancellationToken ct = default);
    Task<ApiResponse>             RefundToWalletAsync(Guid customerId, Guid mallOrderId, decimal amount, string reason, CancellationToken ct = default);
    Task<ApiResponse<List<WalletTxnDto>>> GetHistoryAsync(Guid customerId, int page, int size, CancellationToken ct = default);
    Task<ApiResponse>             AwardBonusAsync(Guid customerId, Guid mallId, decimal amount, string reason, CancellationToken ct = default);
}

public class WalletService : IWalletService
{
    private readonly MesterXDbContext _db;
    private readonly ICacheService    _cache;
    private readonly ILogger<WalletService> _log;

    private const decimal MIN_TOPUP    = 10m;
    private const decimal MAX_TOPUP    = 5000m;
    private const decimal MAX_SPEND_PCT= 1.0m;  // can pay 100% from wallet

    public WalletService(MesterXDbContext db, ICacheService cache, ILogger<WalletService> log)
    { _db = db; _cache = cache; _log = log; }

    // ─── GET WALLET ───────────────────────────────────────────────────────
    public async Task<ApiResponse<WalletDto>> GetWalletAsync(
        Guid customerId, Guid mallId, CancellationToken ct = default)
    {
        var wallet = await GetOrCreateWalletAsync(customerId, mallId, ct);

        var recent = await _db.Set<WalletTransaction>()
            .AsNoTracking()
            .Where(t => t.WalletId == wallet.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        return ApiResponse<WalletDto>.Ok(new WalletDto
        {
            Id           = wallet.Id,
            Balance      = wallet.Balance,
            TotalToppedUp= wallet.TotalToppedUp,
            TotalSpent   = wallet.TotalSpent,
            BalanceLabel = $"{wallet.Balance:N2} ج.م",
            RecentTransactions = recent.Select(MapTxn).ToList(),
        });
    }

    // ─── TOP UP ───────────────────────────────────────────────────────────
    public async Task<ApiResponse<WalletDto>> TopUpAsync(
        Guid customerId, Guid mallId, TopUpRequest req, CancellationToken ct = default)
    {
        if (req.Amount < MIN_TOPUP)
            return ApiResponse<WalletDto>.Fail($"الحد الأدنى للشحن {MIN_TOPUP} ج.م.");
        if (req.Amount > MAX_TOPUP)
            return ApiResponse<WalletDto>.Fail($"الحد الأقصى للشحن {MAX_TOPUP} ج.م في المرة الواحدة.");

        using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var wallet = await GetOrCreateWalletAsync(customerId, mallId, ct);
            var before = wallet.Balance;

            wallet.Balance       += req.Amount;
            wallet.TotalToppedUp += req.Amount;
            wallet.UpdatedAt      = DateTime.UtcNow;

            _db.Set<WalletTransaction>().Add(new WalletTransaction
            {
                WalletId      = wallet.Id,
                CustomerId    = customerId,
                Type          = WalletTxnType.TopUp,
                Status        = WalletTxnStatus.Completed,
                Amount        = req.Amount,
                BalanceBefore = before,
                BalanceAfter  = wallet.Balance,
                Reference     = req.GatewayRef,
                Description   = $"شحن محفظة عبر {req.Gateway}",
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await _cache.DeleteAsync($"wallet:{customerId}:{mallId}");

            _log.LogInformation("Wallet top-up: {EGP} EGP for customer {Id} via {Gateway}",
                req.Amount, customerId, req.Gateway);

            return await GetWalletAsync(customerId, mallId, ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _log.LogError(ex, "TopUp failed for customer {Id}", customerId);
            return ApiResponse<WalletDto>.Fail("فشل عملية الشحن. حاول مجدداً.");
        }
    }

    // ─── SPEND ────────────────────────────────────────────────────────────
    public async Task<ApiResponse<WalletTxnDto>> SpendAsync(
        Guid customerId, SpendFromWalletRequest req, CancellationToken ct = default)
    {
        var order = await _db.MallOrders
            .FirstOrDefaultAsync(o => o.Id == req.MallOrderId && o.CustomerId == customerId, ct);
        if (order == null) return ApiResponse<WalletTxnDto>.Fail("الطلب غير موجود.");

        var wallet = await GetOrCreateWalletAsync(customerId, order.MallId, ct);

        if (wallet.Balance < req.Amount)
            return ApiResponse<WalletTxnDto>.Fail(
                $"رصيد المحفظة ({wallet.Balance:N2} ج.م) غير كافٍ.");

        if (req.Amount > order.Total)
            return ApiResponse<WalletTxnDto>.Fail("المبلغ أكبر من قيمة الطلب.");

        using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var before = wallet.Balance;
            wallet.Balance    -= req.Amount;
            wallet.TotalSpent += req.Amount;
            wallet.UpdatedAt   = DateTime.UtcNow;

            // Apply to order
            order.DiscountAmount += req.Amount;
            order.Total          -= req.Amount;
            order.UpdatedAt       = DateTime.UtcNow;

            var txn = new WalletTransaction
            {
                WalletId      = wallet.Id,
                CustomerId    = customerId,
                MallOrderId   = req.MallOrderId,
                Type          = WalletTxnType.Purchase,
                Amount        = -req.Amount,
                BalanceBefore = before,
                BalanceAfter  = wallet.Balance,
                Description   = $"دفع طلب {order.OrderNumber}",
            };
            _db.Set<WalletTransaction>().Add(txn);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await _cache.DeleteAsync($"wallet:{customerId}:{order.MallId}");

            return ApiResponse<WalletTxnDto>.Ok(MapTxn(txn));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _log.LogError(ex, "Wallet spend failed");
            return ApiResponse<WalletTxnDto>.Fail("فشل عملية الدفع. حاول مجدداً.");
        }
    }

    // ─── REFUND TO WALLET ─────────────────────────────────────────────────
    public async Task<ApiResponse> RefundToWalletAsync(
        Guid customerId, Guid mallOrderId, decimal amount, string reason, CancellationToken ct = default)
    {
        var order = await _db.MallOrders
            .FirstOrDefaultAsync(o => o.Id == mallOrderId && o.CustomerId == customerId, ct);
        if (order == null) return ApiResponse.Fail("الطلب غير موجود.");

        var wallet = await GetOrCreateWalletAsync(customerId, order.MallId, ct);
        var before = wallet.Balance;

        wallet.Balance   += amount;
        wallet.UpdatedAt  = DateTime.UtcNow;

        _db.Set<WalletTransaction>().Add(new WalletTransaction
        {
            WalletId      = wallet.Id,
            CustomerId    = customerId,
            MallOrderId   = mallOrderId,
            Type          = WalletTxnType.Refund,
            Amount        = amount,
            BalanceBefore = before,
            BalanceAfter  = wallet.Balance,
            Description   = $"استرداد طلب {order.OrderNumber} — {reason}",
        });

        await _db.SaveChangesAsync(ct);
        await _cache.DeleteAsync($"wallet:{customerId}:{order.MallId}");

        _log.LogInformation("Wallet refund: {EGP} EGP for order {Order}", amount, mallOrderId);
        return ApiResponse.Ok();
    }

    // ─── GET HISTORY ──────────────────────────────────────────────────────
    public async Task<ApiResponse<List<WalletTxnDto>>> GetHistoryAsync(
        Guid customerId, int page, int size, CancellationToken ct = default)
    {
        var walletId = await _db.Set<CustomerWallet>()
            .Where(w => w.CustomerId == customerId)
            .Select(w => w.Id)
            .FirstOrDefaultAsync(ct);

        if (walletId == Guid.Empty)
            return ApiResponse<List<WalletTxnDto>>.Ok([]);

        var txns = await _db.Set<WalletTransaction>()
            .AsNoTracking()
            .Where(t => t.WalletId == walletId)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);

        return ApiResponse<List<WalletTxnDto>>.Ok(txns.Select(MapTxn).ToList());
    }

    // ─── AWARD BONUS ──────────────────────────────────────────────────────
    public async Task<ApiResponse> AwardBonusAsync(
        Guid customerId, Guid mallId, decimal amount, string reason, CancellationToken ct = default)
    {
        var wallet = await GetOrCreateWalletAsync(customerId, mallId, ct);
        var before = wallet.Balance;
        wallet.Balance   += amount;
        wallet.UpdatedAt  = DateTime.UtcNow;

        _db.Set<WalletTransaction>().Add(new WalletTransaction
        {
            WalletId      = wallet.Id,
            CustomerId    = customerId,
            Type          = WalletTxnType.Bonus,
            Amount        = amount,
            BalanceBefore = before,
            BalanceAfter  = wallet.Balance,
            Description   = reason,
        });

        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok();
    }

    // ─── HELPERS ──────────────────────────────────────────────────────────
    private async Task<CustomerWallet> GetOrCreateWalletAsync(
        Guid customerId, Guid mallId, CancellationToken ct)
    {
        var w = await _db.Set<CustomerWallet>()
            .FirstOrDefaultAsync(x => x.CustomerId == customerId && x.MallId == mallId, ct);
        if (w != null) return w;

        w = new CustomerWallet { CustomerId = customerId, MallId = mallId };
        _db.Set<CustomerWallet>().Add(w);
        await _db.SaveChangesAsync(ct);
        return w;
    }

    private static string TypeAr(WalletTxnType t) => t switch
    {
        WalletTxnType.TopUp      => "شحن محفظة",
        WalletTxnType.Purchase   => "دفع طلب",
        WalletTxnType.Refund     => "استرداد",
        WalletTxnType.Bonus      => "مكافأة",
        WalletTxnType.Transfer   => "تحويل",
        WalletTxnType.Withdrawal => "سحب",
        WalletTxnType.Adjustment => "تعديل",
        _ => t.ToString()
    };

    private static WalletTxnDto MapTxn(WalletTransaction t) => new()
    {
        Id           = t.Id,
        Type         = t.Type.ToString(),
        TypeAr       = TypeAr(t.Type),
        Amount       = t.Amount,
        BalanceAfter = t.BalanceAfter,
        Description  = t.Description,
        Reference    = t.Reference,
        CreatedAt    = t.CreatedAt,
    };
}
