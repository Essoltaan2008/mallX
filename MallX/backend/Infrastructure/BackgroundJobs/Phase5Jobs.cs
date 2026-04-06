using MesterX.Application.Services.Phase4;
using MesterX.Infrastructure.Caching;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MesterX.Infrastructure.BackgroundJobs;

// ══════════════════════════════════════════════════════════════════════════
//  LOYALTY EXPIRY JOB — runs daily at 02:00 UTC
// ══════════════════════════════════════════════════════════════════════════
public class LoyaltyExpiryJob : BackgroundService
{
    private readonly IServiceScopeFactory _scope;
    private readonly ILogger<LoyaltyExpiryJob> _log;

    public LoyaltyExpiryJob(IServiceScopeFactory scope, ILogger<LoyaltyExpiryJob> log)
    { _scope = scope; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now    = DateTime.UtcNow;
            var target = new DateTime(now.Year, now.Month, now.Day, 2, 0, 0, DateTimeKind.Utc);
            if (now >= target) target = target.AddDays(1);

            var delay = target - now;
            _log.LogInformation("LoyaltyExpiryJob: next run in {H:N0}h", delay.TotalHours);
            await Task.Delay(delay, ct);

            using var svc = _scope.CreateScope();
            var loyalty   = svc.ServiceProvider.GetRequiredService<ILoyaltyService>();
            try
            {
                await loyalty.ProcessExpiryAsync(ct);
                _log.LogInformation("LoyaltyExpiryJob: completed");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "LoyaltyExpiryJob: failed");
            }
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  ANALYTICS SNAPSHOT JOB — runs every day at 23:55 UTC
// ══════════════════════════════════════════════════════════════════════════
public class AnalyticsSnapshotJob : BackgroundService
{
    private readonly IServiceScopeFactory _scope;
    private readonly ILogger<AnalyticsSnapshotJob> _log;

    public AnalyticsSnapshotJob(IServiceScopeFactory scope, ILogger<AnalyticsSnapshotJob> log)
    { _scope = scope; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now    = DateTime.UtcNow;
            var target = new DateTime(now.Year, now.Month, now.Day, 23, 55, 0, DateTimeKind.Utc);
            if (now >= target) target = target.AddDays(1);

            await Task.Delay(target - now, ct);

            using var svc   = _scope.CreateScope();
            var db          = svc.ServiceProvider.GetRequiredService<MesterXDbContext>();
            var commService = svc.ServiceProvider
                .GetRequiredService<Application.Services.Phase2.ICommissionService>();
            try
            {
                var malls = await db.Malls.Where(m => m.IsActive).Select(m => m.Id).ToListAsync(ct);
                foreach (var mallId in malls)
                    await commService.SnapshotDailyAnalyticsAsync(mallId, ct);

                _log.LogInformation("AnalyticsSnapshot: {N} malls processed", malls.Count);
            }
            catch (Exception ex) { _log.LogError(ex, "AnalyticsSnapshotJob failed"); }
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  CAMPAIGN SCHEDULER JOB — checks every minute for due campaigns
// ══════════════════════════════════════════════════════════════════════════
public class CampaignSchedulerJob : BackgroundService
{
    private readonly IServiceScopeFactory _scope;
    private readonly ILogger<CampaignSchedulerJob> _log;

    public CampaignSchedulerJob(IServiceScopeFactory scope, ILogger<CampaignSchedulerJob> log)
    { _scope = scope; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct);

            using var svc = _scope.CreateScope();
            var db        = svc.ServiceProvider.GetRequiredService<MesterXDbContext>();
            var promoSvc  = svc.ServiceProvider
                .GetRequiredService<Application.Services.Phase4.IPromotionService>();
            try
            {
                // Find campaigns due to send
                var due = await db.Set<Domain.Entities.Phase4.NotificationCampaign>()
                    .Where(c => c.Status == Domain.Entities.Phase4.NotifStatus.Scheduled
                        && c.ScheduledAt <= DateTime.UtcNow)
                    .ToListAsync(ct);

                foreach (var campaign in due)
                {
                    _log.LogInformation("Dispatching scheduled campaign {Id}", campaign.Id);
                    // Trigger re-send via service
                    await promoSvc.SendCampaignAsync(campaign.MallId,
                        campaign.CreatedBy ?? Guid.Empty,
                        new Application.Services.Phase4.SendCampaignRequest(
                            campaign.Title, campaign.TitleAr,
                            campaign.Body,  campaign.BodyAr,
                            campaign.ImageUrl, campaign.Target.ToString(),
                            campaign.ActionType, campaign.ActionId, null), ct);
                }
            }
            catch (Exception ex) { _log.LogError(ex, "CampaignSchedulerJob failed"); }
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  FLASH SALE CLEANUP JOB — expires stale flash sales every 5 minutes
// ══════════════════════════════════════════════════════════════════════════
public class FlashSaleCleanupJob : BackgroundService
{
    private readonly IServiceScopeFactory _scope;
    private readonly ILogger<FlashSaleCleanupJob> _log;

    public FlashSaleCleanupJob(IServiceScopeFactory scope, ILogger<FlashSaleCleanupJob> log)
    { _scope = scope; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), ct);

            using var svc  = _scope.CreateScope();
            var db         = svc.ServiceProvider.GetRequiredService<MesterXDbContext>();
            var cache      = svc.ServiceProvider.GetRequiredService<ICacheService>();
            try
            {
                // Deactivate expired flash sales
                var expired = await db.Set<Domain.Entities.Phase4.FlashSale>()
                    .Where(f => f.IsActive && f.EndsAt < DateTime.UtcNow)
                    .ToListAsync(ct);

                if (expired.Any())
                {
                    foreach (var f in expired) f.IsActive = false;
                    await db.SaveChangesAsync(ct);

                    // Bust cache for affected malls
                    var mallIds = expired.Select(f => f.MallId).Distinct();
                    foreach (var mid in mallIds)
                        await cache.DeleteAsync(CacheKeys.ActiveFlashSales(mid));

                    _log.LogInformation("FlashSaleCleanup: deactivated {N} expired sales", expired.Count);
                }

                // Also expire stale coupons
                var stale = await db.Set<Domain.Entities.Phase4.Coupon>()
                    .Where(c => c.Status == Domain.Entities.Phase4.CouponStatus.Active
                        && c.ValidTo < DateTime.UtcNow)
                    .ToListAsync(ct);

                if (stale.Any())
                {
                    foreach (var c in stale) c.Status = Domain.Entities.Phase4.CouponStatus.Expired;
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex) { _log.LogError(ex, "FlashSaleCleanupJob failed"); }
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  QUEUE TICKET CLEANUP — auto-cancel stale tickets after 2h
// ══════════════════════════════════════════════════════════════════════════
public class QueueCleanupJob : BackgroundService
{
    private readonly IServiceScopeFactory _scope;
    private readonly ILogger<QueueCleanupJob> _log;

    public QueueCleanupJob(IServiceScopeFactory scope, ILogger<QueueCleanupJob> log)
    { _scope = scope; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(15), ct);

            using var svc = _scope.CreateScope();
            var db        = svc.ServiceProvider.GetRequiredService<MesterXDbContext>();
            try
            {
                var cutoff = DateTime.UtcNow.AddHours(-2);
                var stale  = await db.Set<Domain.Entities.Phase3.QueueTicket>()
                    .Where(t => t.Status == Domain.Entities.Phase3.TicketStatus.Waiting
                        && t.CreatedAt < cutoff)
                    .ToListAsync(ct);

                if (stale.Any())
                {
                    foreach (var t in stale)
                        t.Status = Domain.Entities.Phase3.TicketStatus.Cancelled;
                    await db.SaveChangesAsync(ct);
                    _log.LogInformation("QueueCleanup: cancelled {N} stale tickets", stale.Count);
                }
            }
            catch (Exception ex) { _log.LogError(ex, "QueueCleanupJob failed"); }
        }
    }
}
