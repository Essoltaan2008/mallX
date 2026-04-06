using MesterX.Application.DTOs;
using MesterX.Domain.Entities.Phase3;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesterX.Application.Services.Phase3;

// ──────────────────────────────────────────────────────────────────────────
//  DTOs
// ──────────────────────────────────────────────────────────────────────────
public record SubmitRatingRequest(
    Guid?  MallOrderId,
    Guid?  BookingId,
    Guid   StoreId,
    string Subject,       // Store | Delivery | Overall | MenuItem
    Guid?  SubjectId,     // for MenuItem
    short  Stars,
    string? Title,
    string? Body,
    bool   IsAnonymous
);

public record RatingDto
{
    public Guid     Id           { get; init; }
    public string   AuthorName   { get; init; } = string.Empty;  // masked if anon
    public string   Subject      { get; init; } = string.Empty;
    public short    Stars        { get; init; }
    public string?  Title        { get; init; }
    public string?  Body         { get; init; }
    public string?  StoreReply   { get; init; }
    public DateTime CreatedAt    { get; init; }
}

public record StoreSummaryDto
{
    public Guid    StoreId      { get; init; }
    public string  StoreName    { get; init; } = string.Empty;
    public decimal AvgStars     { get; init; }
    public int     TotalRatings { get; init; }
    public Dictionary<int, int> Breakdown { get; init; } = new();
    public List<RatingDto>     Recent     { get; init; } = [];
}

public record StoreReplyRequest(string Reply);

// ──────────────────────────────────────────────────────────────────────────
//  SERVICE
// ──────────────────────────────────────────────────────────────────────────
public interface IRatingService
{
    Task<ApiResponse<RatingDto>>      SubmitAsync(Guid customerId, SubmitRatingRequest req, CancellationToken ct = default);
    Task<ApiResponse<StoreSummaryDto>> GetStoreSummaryAsync(Guid storeId, CancellationToken ct = default);
    Task<ApiResponse<List<RatingDto>>> GetStoreRatingsAsync(Guid storeId, int page, int size, CancellationToken ct = default);
    Task<ApiResponse>                 ReplyAsync(Guid storeId, Guid ratingId, StoreReplyRequest req, CancellationToken ct = default);
    Task<ApiResponse>                 HideRatingAsync(Guid storeId, Guid ratingId, CancellationToken ct = default);
}

public class RatingService : IRatingService
{
    private readonly MesterXDbContext _db;
    private readonly ILogger<RatingService> _log;

    public RatingService(MesterXDbContext db, ILogger<RatingService> log)
    { _db = db; _log = log; }

    // ─── SUBMIT ───────────────────────────────────────────────────────────
    public async Task<ApiResponse<RatingDto>> SubmitAsync(
        Guid customerId, SubmitRatingRequest req, CancellationToken ct = default)
    {
        if (req.Stars is < 1 or > 5)
            return ApiResponse<RatingDto>.Fail("التقييم يجب أن يكون بين 1 و 5 نجوم.");

        if (!Enum.TryParse<RatingSubject>(req.Subject, out var subject))
            return ApiResponse<RatingDto>.Fail("نوع التقييم غير صالح.");

        // For order-based ratings: verify customer owns the order
        if (req.MallOrderId.HasValue)
        {
            var orderOwned = await _db.MallOrders.AnyAsync(
                o => o.Id == req.MallOrderId && o.CustomerId == customerId
                && o.Status == MallOrderStatus.Delivered, ct);
            if (!orderOwned)
                return ApiResponse<RatingDto>.Fail("لا يمكن تقييم طلب لم يتم تسليمه.");
        }

        // Check for duplicate
        var dup = await _db.Set<Rating>().AnyAsync(r =>
            r.CustomerId == customerId && r.StoreId == req.StoreId
            && r.MallOrderId == req.MallOrderId
            && r.Subject == subject, ct);
        if (dup)
            return ApiResponse<RatingDto>.Fail("لقد قمت بتقييم هذا الطلب مسبقاً.");

        var customer = await _db.MallCustomers.FindAsync([customerId], ct);

        var rating = new Rating
        {
            MallOrderId = req.MallOrderId,
            BookingId   = req.BookingId,
            CustomerId  = customerId,
            StoreId     = req.StoreId,
            Subject     = subject,
            SubjectId   = req.SubjectId,
            Stars       = req.Stars,
            Title       = req.Title?.Trim(),
            Body        = req.Body?.Trim(),
            IsAnonymous = req.IsAnonymous,
            IsPublished = true,
        };
        _db.Set<Rating>().Add(rating);
        await _db.SaveChangesAsync(ct);

        // Award loyalty points for rating (5 points)
        if (customer != null)
        {
            customer.LoyaltyPoints += 5;
            customer.UpdatedAt      = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        _log.LogInformation("Rating submitted: {Stars}⭐ for store {StoreId}", req.Stars, req.StoreId);
        return ApiResponse<RatingDto>.Ok(MapRating(rating, customer));
    }

    // ─── STORE SUMMARY ────────────────────────────────────────────────────
    public async Task<ApiResponse<StoreSummaryDto>> GetStoreSummaryAsync(
        Guid storeId, CancellationToken ct = default)
    {
        var summary = await _db.Set<StoreRatingSummary>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == storeId, ct);

        var store = await _db.Tenants.FindAsync([storeId], ct);

        var recent = await _db.Set<Rating>()
            .AsNoTracking()
            .Include(r => r.Customer)
            .Where(r => r.StoreId == storeId && r.IsPublished)
            .OrderByDescending(r => r.CreatedAt)
            .Take(5)
            .ToListAsync(ct);

        return ApiResponse<StoreSummaryDto>.Ok(new StoreSummaryDto
        {
            StoreId      = storeId,
            StoreName    = store?.Name ?? string.Empty,
            AvgStars     = summary?.AvgStars     ?? 0,
            TotalRatings = summary?.TotalRatings ?? 0,
            Breakdown    = new Dictionary<int, int>
            {
                { 5, summary?.FiveStar  ?? 0 },
                { 4, summary?.FourStar  ?? 0 },
                { 3, summary?.ThreeStar ?? 0 },
                { 2, summary?.TwoStar   ?? 0 },
                { 1, summary?.OneStar   ?? 0 },
            },
            Recent = recent.Select(r => MapRating(r, r.Customer)).ToList()
        });
    }

    // ─── LIST RATINGS ─────────────────────────────────────────────────────
    public async Task<ApiResponse<List<RatingDto>>> GetStoreRatingsAsync(
        Guid storeId, int page, int size, CancellationToken ct = default)
    {
        var ratings = await _db.Set<Rating>()
            .AsNoTracking()
            .Include(r => r.Customer)
            .Where(r => r.StoreId == storeId && r.IsPublished)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);

        return ApiResponse<List<RatingDto>>.Ok(
            ratings.Select(r => MapRating(r, r.Customer)).ToList());
    }

    // ─── STORE REPLY ──────────────────────────────────────────────────────
    public async Task<ApiResponse> ReplyAsync(
        Guid storeId, Guid ratingId, StoreReplyRequest req, CancellationToken ct = default)
    {
        var rating = await _db.Set<Rating>()
            .FirstOrDefaultAsync(r => r.Id == ratingId && r.StoreId == storeId, ct);
        if (rating == null) return ApiResponse.Fail("التقييم غير موجود.");

        rating.StoreReply     = req.Reply.Trim();
        rating.StoreRepliedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok();
    }

    // ─── HIDE (soft mod) ─────────────────────────────────────────────────
    public async Task<ApiResponse> HideRatingAsync(
        Guid storeId, Guid ratingId, CancellationToken ct = default)
    {
        var r = await _db.Set<Rating>()
            .FirstOrDefaultAsync(x => x.Id == ratingId && x.StoreId == storeId, ct);
        if (r == null) return ApiResponse.Fail("التقييم غير موجود.");
        r.IsPublished = false;
        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok();
    }

    // ─── MAPPER ───────────────────────────────────────────────────────────
    private static RatingDto MapRating(Rating r, MallCustomer? customer) => new()
    {
        Id         = r.Id,
        AuthorName = r.IsAnonymous ? "مستخدم مجهول"
                   : customer?.FullName ?? "عميل",
        Subject    = r.Subject.ToString(),
        Stars      = r.Stars,
        Title      = r.Title,
        Body       = r.Body,
        StoreReply = r.StoreReply,
        CreatedAt  = r.CreatedAt,
    };
}
