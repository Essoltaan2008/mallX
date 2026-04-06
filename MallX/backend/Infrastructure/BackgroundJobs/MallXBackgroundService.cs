using MesterX.Application.Services.Phase4;
using MesterX.Application.Services.Phase5;
using MesterX.Domain.Entities.Phase4;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MesterX.Infrastructure.BackgroundJobs;

// ══════════════════════════════════════════════════════════════════════════
//  MASTER BACKGROUND SERVICE — orchestrates all scheduled jobs
// ══════════════════════════════════════════════════════════════════════════
public class MallXBackgroundService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<MallXBackgroundService> _log;

    // Schedule intervals
    private static readonly TimeSpan ANALYTICS_INTERVAL     = TimeSpan.FromHours(1);
    private static readonly TimeSpan LOYALTY_EXPIRY_INTERVAL = TimeSpan.FromHours(24);
    private static readonly TimeSpan CAMPAIGN_CHECK_INTERVAL = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan POINTS_EARN_INTERVAL   = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FLASH_SALE_INTERVAL    = TimeSpan.FromMinutes(1);

    // Last run trackers
    private DateTime _lastAnalytics     = DateTime.MinValue;
    private DateTime _lastLoyaltyExpiry = DateTime.MinValue;
    private DateTime _lastCampaignCheck = DateTime.MinValue;
    private DateTime _lastPointsEarn    = DateTime.MinValue;
    private DateTime _lastFlashCheck    = DateTime.MinValue;

    public MallXBackgroundService(IServiceProvider sp, ILogger<MallXBackgroundService> log)
    { _sp = sp; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("🚀 MallX Background Service started");

        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            // Every 2 minutes — award loyalty points for delivered orders
            if (now - _lastPointsEarn >= POINTS_EARN_INTERVAL)
            {
                await RunSafeAsync("LoyaltyPointsEarning", AwardPendingLoyaltyPointsAsync, ct);
                _lastPointsEarn = now;
            }

            // Every 5 minutes — send scheduled campaigns
            if (now - _lastCampaignCheck >= CAMPAIGN_CHECK_INTERVAL)
            {
                await RunSafeAsync("ScheduledCampaigns", ProcessScheduledCampaignsAsync, ct);
                _lastCampaignCheck = now;
            }

            // Every minute — expire flash sales
            if (now - _lastFlashCheck >= FLASH_SALE_INTERVAL)
            {
                await RunSafeAsync("FlashSaleExpiry", ProcessFlashSaleExpiryAsync, ct);
                _lastFlashCheck = now;
            }

            // Every hour — snapshot analytics
            if (now - _lastAnalytics >= ANALYTICS_INTERVAL)
            {
                await RunSafeAsync("AnalyticsSnapshot", SnapshotAnalyticsAsync, ct);
                _lastAnalytics = now;
            }

            // Every 24 hours — loyalty points expiry + tier recalculation
            if (now - _lastLoyaltyExpiry >= LOYALTY_EXPIRY_INTERVAL)
            {
                await RunSafeAsync("LoyaltyExpiry", ProcessLoyaltyExpiryAsync, ct);
                _lastLoyaltyExpiry = now;
            }

            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    // ── JOB 1: Award points for delivered orders ──────────────────────────
    private async Task AwardPendingLoyaltyPointsAsync(CancellationToken ct)
    {
        using var scope   = _sp.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<MesterXDbContext>();
        var loyaltyService= scope.ServiceProvider.GetRequiredService<ILoyaltyService>();
        var cache         = scope.ServiceProvider.GetRequiredService<ICacheService>();

        // Find delivered orders where points haven't been awarded
        // We use a simple flag: check if a Purchase transaction exists for this order
        var deliveredOrders = await db.MallOrders
            .AsNoTracking()
            .Where(o => o.Status == MallOrderStatus.Delivered
                && o.DeliveredAt >= DateTime.UtcNow.AddHours(-2)
                && !db.Set<PointsTransaction>()
                    .Any(t => t.MallOrderId == o.Id
                        && t.Source == PointsSource.Purchase))
            .Take(50)
            .ToListAsync(ct);

        foreach (var order in deliveredOrders)
        {
            await loyaltyService.EarnPointsAsync(new EarnPointsRequest(
                order.CustomerId, order.Id, order.Total, order.MallId), ct);

            // Invalidate customer wallet cache
            await cache.DeleteAsync(CacheKeys.CustomerWallet(order.CustomerId));
        }

        if (deliveredOrders.Any())
            _log.LogInformation("Loyalty points awarded for {Count} orders", deliveredOrders.Count);
    }

    // ── JOB 2: Send scheduled push campaigns ─────────────────────────────
    private async Task ProcessScheduledCampaignsAsync(CancellationToken ct)
    {
        using var scope     = _sp.CreateScope();
        var db              = scope.ServiceProvider.GetRequiredService<MesterXDbContext>();
        var promoService    = scope.ServiceProvider.GetRequiredService<IPromotionService>();

        var dueCampaigns = await db.Set<NotificationCampaign>()
            .Where(c => c.Status == NotifStatus.Scheduled
                && c.ScheduledAt <= DateTime.UtcNow
                && c.ScheduledAt >= DateTime.UtcNow.AddHours(-1))
            .ToListAsync(ct);

        foreach (var campaign in dueCampaigns)
        {
            campaign.Status    = NotifStatus.Sending;
            campaign.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await promoService.SendCampaignAsync(
                campaign.MallId,
                campaign.CreatedBy ?? Guid.Empty,
                new SendCampaignRequest(
                    campaign.Title, campaign.TitleAr,
                    campaign.Body,  campaign.BodyAr,
                    campaign.ImageUrl, campaign.Target.ToString(),
                    campaign.ActionType, campaign.ActionId, null), ct);
        }
    }

    // ── JOB 3: Mark expired flash sales ──────────────────────────────────
    private async Task ProcessFlashSaleExpiryAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<MesterXDbContext>();
        var cache       = scope.ServiceProvider.GetRequiredService<ICacheService>();

        var count = await db.Set<FlashSale>()
            .Where(f => f.IsActive && f.EndsAt < DateTime.UtcNow)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(f => f.IsActive, false), ct);

        if (count > 0)
        {
            // Invalidate flash sale caches across all malls
            await cache.DeleteByPatternAsync("mall:*:flash");
            _log.LogInformation("Expired {Count} flash sales", count);
        }
    }

    // ── JOB 4: Analytics snapshot ────────────────────────────────────────
    private async Task SnapshotAnalyticsAsync(CancellationToken ct)
    {
        using var scope   = _sp.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<MesterXDbContext>();
        var promoService  = scope.ServiceProvider.GetRequiredService<IPromotionService>();

        // Get all active malls
        var mallIds = await db.Malls
            .AsNoTracking()
            .Where(m => m.IsActive)
            .Select(m => m.Id)
            .ToListAsync(ct);

        // Note: SnapshotDailyAnalyticsAsync is on CommissionService (Phase2)
        // We'll call it via a simple direct approach
        foreach (var mallId in mallIds)
        {
            try
            {
                var commService = scope.ServiceProvider
                    .GetRequiredService<MesterX.Application.Services.Phase2.ICommissionService>();
                await commService.SnapshotDailyAnalyticsAsync(mallId, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Analytics snapshot failed for mall {MallId}", mallId);
            }
        }
    }

    // ── JOB 5: Loyalty points expiry ─────────────────────────────────────
    private async Task ProcessLoyaltyExpiryAsync(CancellationToken ct)
    {
        using var scope   = _sp.CreateScope();
        var loyaltyService= scope.ServiceProvider.GetRequiredService<ILoyaltyService>();
        await loyaltyService.ProcessExpiryAsync(ct);
    }

    // ── SAFE RUNNER ───────────────────────────────────────────────────────
    private async Task RunSafeAsync(string jobName,
        Func<CancellationToken, Task> job, CancellationToken ct)
    {
        try
        {
            _log.LogDebug("⚙️ Running job: {Job}", jobName);
            await job(ct);
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        catch (Exception ex)
        {
            _log.LogError(ex, "❌ Background job failed: {Job}", jobName);
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  BOOKING REMINDER JOB (separate — checks every 15 minutes)
// ══════════════════════════════════════════════════════════════════════════
public class BookingReminderService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<BookingReminderService> _log;

    public BookingReminderService(IServiceProvider sp, ILogger<BookingReminderService> log)
    { _sp = sp; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await SendRemindersAsync(ct);
            await Task.Delay(TimeSpan.FromMinutes(15), ct);
        }
    }

    private async Task SendRemindersAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<MesterXDbContext>();
        var promo       = scope.ServiceProvider.GetRequiredService<IPromotionService>();

        // Find bookings in 1 hour that haven't been reminded
        var reminderWindow = DateTime.UtcNow.AddHours(1).AddMinutes(15);
        var soon           = DateTime.UtcNow.AddHours(1).AddMinutes(-15);

        var bookings = await db.Set<MesterX.Domain.Entities.Phase3.Booking>()
            .AsNoTracking()
            .Include(b => b.Service)
            .Include(b => b.Customer)
            .Where(b => !b.ReminderSent
                && b.Status == MesterX.Domain.Entities.Phase3.BookingStatus.Confirmed
                && b.BookedDate == DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .ToListAsync(ct);

        foreach (var booking in bookings)
        {
            // Build start datetime
            var bookingStart = booking.BookedDate.ToDateTime(booking.StartTime);
            if (bookingStart < soon || bookingStart > reminderWindow) continue;

            // Register and send push to customer's devices
            try
            {
                await promo.SendCampaignAsync(booking.Customer.MallId, Guid.Empty,
                    new SendCampaignRequest(
                        $"Reminder: {booking.Service.Name}",
                        $"تذكير: {booking.Service.Name}",
                        $"Your appointment is in 1 hour at {booking.StartTime}",
                        $"موعدك خلال ساعة — {booking.StartTime.ToString("hh:mm tt")} 📅",
                        null, "CustomSegment", "OpenBooking", booking.Id.ToString(), null),
                    ct);

                // Mark as sent
                await db.Set<MesterX.Domain.Entities.Phase3.Booking>()
                    .Where(b => b.Id == booking.Id)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(b => b.ReminderSent, true), ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Reminder failed for booking {Id}", booking.Id);
            }
        }
    }
}
