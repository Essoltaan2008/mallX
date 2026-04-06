using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MesterX.Application.DTOs;
using MesterX.Domain.Entities.Phase4;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace MesterX.Application.Services.Phase9;

// ══════════════════════════════════════════════════════════════════════════
//  REFERRAL ENTITIES
// ══════════════════════════════════════════════════════════════════════════
public class ReferralProgram
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallId          { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public int     ReferrerRewardPts        { get; set; } = 200;
    public int     RefereeRewardPts         { get; set; } = 100;
    public decimal ReferrerWalletEgp        { get; set; } = 0;
    public decimal RefereeDiscountPct       { get; set; } = 0;
    public decimal MinOrderToUnlock         { get; set; } = 0;
    public int?    MaxReferrals             { get; set; }
    public bool    IsActive                 { get; set; } = true;
    public DateTime ValidFrom               { get; set; } = DateTime.UtcNow;
    public DateTime? ValidTo                { get; set; }
    public DateTime CreatedAt               { get; set; } = DateTime.UtcNow;
}

public class ReferralCode
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid CustomerId { get; set; }
    [Required] public Guid MallId    { get; set; }
    [Required, MaxLength(20)] public string Code { get; set; } = string.Empty;
    public int UsesCount              { get; set; } = 0;
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;
}

public class ReferralUse
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid ProgramId  { get; set; }
    [Required] public Guid ReferrerId { get; set; }
    [Required] public Guid RefereeId  { get; set; }
    public Guid? MallOrderId          { get; set; }
    public bool ReferrerRewarded      { get; set; } = false;
    public bool RefereeRewarded       { get; set; } = false;
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;
}

// ══════════════════════════════════════════════════════════════════════════
//  REFERRAL SERVICE
// ══════════════════════════════════════════════════════════════════════════
public record ReferralInfoDto
{
    public string  Code         { get; init; } = string.Empty;
    public string  ShareUrl     { get; init; } = string.Empty;
    public string  ShareMessage { get; init; } = string.Empty;
    public int     UsesCount    { get; init; }
    public int     ReferrerPts  { get; init; }
    public int     RefereePts   { get; init; }
    public decimal RefereeDiscount { get; init; }
}

public interface IReferralService
{
    Task<ApiResponse<ReferralInfoDto>> GetOrCreateCodeAsync(Guid customerId, Guid mallId, CancellationToken ct = default);
    Task<ApiResponse>                  ApplyReferralAsync(Guid refereeId, string code, Guid mallId, CancellationToken ct = default);
    Task                               ProcessReferralRewardAsync(Guid refereeId, Guid mallOrderId, CancellationToken ct = default);
}

public class ReferralService : IReferralService
{
    private readonly MesterXDbContext _db;
    private readonly ILoyaltyService  _loyalty;
    private readonly IWalletService   _wallet;
    private readonly ILogger<ReferralService> _log;

    public ReferralService(MesterXDbContext db, ILoyaltyService loyalty,
        IWalletService wallet, ILogger<ReferralService> log)
    { _db = db; _loyalty = loyalty; _wallet = wallet; _log = log; }

    public async Task<ApiResponse<ReferralInfoDto>> GetOrCreateCodeAsync(
        Guid customerId, Guid mallId, CancellationToken ct = default)
    {
        var existing = await _db.Set<ReferralCode>()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId && c.MallId == mallId, ct);

        if (existing == null)
        {
            var customer = await _db.MallCustomers.FindAsync([customerId], ct);
            var code = GenerateCode(customer?.FirstName ?? "USER");
            existing = new ReferralCode { CustomerId = customerId, MallId = mallId, Code = code };
            _db.Set<ReferralCode>().Add(existing);
            await _db.SaveChangesAsync(ct);
        }

        var program = await _db.Set<ReferralProgram>()
            .FirstOrDefaultAsync(p => p.MallId == mallId && p.IsActive, ct);

        return ApiResponse<ReferralInfoDto>.Ok(new ReferralInfoDto
        {
            Code         = existing.Code,
            ShareUrl     = $"https://mallx.app/join?ref={existing.Code}",
            ShareMessage = $"انضم لـ MallX واحصل على خصم {program?.RefereeDiscountPct ?? 10}% على أول طلب! استخدم كودي: {existing.Code}",
            UsesCount    = existing.UsesCount,
            ReferrerPts  = program?.ReferrerRewardPts ?? 200,
            RefereePts   = program?.RefereeRewardPts  ?? 100,
            RefereeDiscount = program?.RefereeDiscountPct ?? 10,
        });
    }

    public async Task<ApiResponse> ApplyReferralAsync(
        Guid refereeId, string code, Guid mallId, CancellationToken ct = default)
    {
        var referralCode = await _db.Set<ReferralCode>()
            .FirstOrDefaultAsync(c => c.Code == code.ToUpperInvariant() && c.MallId == mallId, ct);

        if (referralCode == null) return ApiResponse.Fail("كود الإحالة غير صالح.");
        if (referralCode.CustomerId == refereeId) return ApiResponse.Fail("لا يمكنك استخدام كودك الخاص.");

        var program = await _db.Set<ReferralProgram>()
            .FirstOrDefaultAsync(p => p.MallId == mallId && p.IsActive, ct);
        if (program == null) return ApiResponse.Fail("برنامج الإحالة غير متاح.");

        // Check if referee already used a referral
        var alreadyUsed = await _db.Set<ReferralUse>()
            .AnyAsync(u => u.RefereeId == refereeId && u.ProgramId == program.Id, ct);
        if (alreadyUsed) return ApiResponse.Fail("لقد استخدمت كود إحالة من قبل.");

        _db.Set<ReferralUse>().Add(new ReferralUse
        {
            ProgramId   = program.Id,
            ReferrerId  = referralCode.CustomerId,
            RefereeId   = refereeId,
        });
        referralCode.UsesCount++;
        await _db.SaveChangesAsync(ct);

        // Award referee bonus points immediately
        if (program.RefereeRewardPts > 0)
            await _loyalty.AwardBonusAsync(refereeId, mallId,
                PointsSource.Referral, program.RefereeRewardPts,
                "مكافأة الانضمام عبر الإحالة", ct);

        _log.LogInformation("Referral applied: {Code} by customer {RefereeId}", code, refereeId);
        return ApiResponse.Ok();
    }

    public async Task ProcessReferralRewardAsync(
        Guid refereeId, Guid mallOrderId, CancellationToken ct = default)
    {
        var order = await _db.MallOrders.FindAsync([mallOrderId], ct);
        if (order == null) return;

        var use = await _db.Set<ReferralUse>()
            .Include(u => u.Program)
            .FirstOrDefaultAsync(u => u.RefereeId == refereeId
                && !u.ReferrerRewarded && u.MallOrderId == null, ct);
        if (use == null) return;

        var program = use.Program;
        if (order.Total < program.MinOrderToUnlock) return; // not enough

        use.MallOrderId      = mallOrderId;
        use.ReferrerRewarded = true;

        // Reward referrer
        if (program.ReferrerRewardPts > 0)
            await _loyalty.AwardBonusAsync(use.ReferrerId, order.MallId,
                PointsSource.Referral, program.ReferrerRewardPts,
                "مكافأة إحالة ناجحة", ct);

        if (program.ReferrerWalletEgp > 0)
            await _wallet.AwardBonusAsync(use.ReferrerId, order.MallId,
                program.ReferrerWalletEgp, "مكافأة إحالة — محفظة", ct);

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Referral reward processed for referrer {Id}", use.ReferrerId);
    }

    private static string GenerateCode(string name)
    {
        var prefix = name.Length >= 3 ? name[..3].ToUpperInvariant() : "MLX";
        var suffix = Random.Shared.Next(1000, 9999);
        return $"{prefix}{suffix}";
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  WHATSAPP SERVICE (Twilio / WhatsApp Business API)
// ══════════════════════════════════════════════════════════════════════════
public enum WhatsappStatus { Queued, Sent, Delivered, Read, Failed }

public class WhatsappMessage
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CustomerId  { get; set; }
    public Guid? MallOrderId { get; set; }
    [Required, MaxLength(20)] public string Phone { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Template { get; set; } = string.Empty;
    public string? VariablesJson  { get; set; }
    public WhatsappStatus Status  { get; set; } = WhatsappStatus.Queued;
    public string? ProviderMsgId  { get; set; }
    public string? ErrorMessage   { get; set; }
    public DateTime? SentAt       { get; set; }
    public DateTime? DeliveredAt  { get; set; }
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
}

public interface IWhatsappService
{
    Task SendOrderConfirmedAsync(string phone, string customerName, string orderNum, decimal total, CancellationToken ct = default);
    Task SendOrderReadyAsync(string phone, string customerName, string orderNum, CancellationToken ct = default);
    Task SendOrderDeliveredAsync(string phone, string customerName, string orderNum, CancellationToken ct = default);
    Task SendBookingReminderAsync(string phone, string customerName, string serviceName, string date, string time, CancellationToken ct = default);
    Task SendCustomAsync(string phone, string template, Dictionary<string, string> vars, CancellationToken ct = default);
}

public class WhatsappService : IWhatsappService
{
    private readonly MesterXDbContext _db;
    private readonly IConfiguration   _config;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<WhatsappService> _log;

    public WhatsappService(MesterXDbContext db, IConfiguration config,
        IHttpClientFactory http, ILogger<WhatsappService> log)
    { _db = db; _config = config; _http = http; _log = log; }

    public async Task SendOrderConfirmedAsync(string phone, string customerName,
        string orderNum, decimal total, CancellationToken ct = default)
        => await SendAsync(phone, "order_confirmed", new()
        {
            ["customer_name"] = customerName,
            ["order_number"]  = orderNum,
            ["total"]         = $"{total:N0} ج.م",
        }, ct);

    public async Task SendOrderReadyAsync(string phone, string customerName,
        string orderNum, CancellationToken ct = default)
        => await SendAsync(phone, "order_ready", new()
        {
            ["customer_name"] = customerName,
            ["order_number"]  = orderNum,
        }, ct);

    public async Task SendOrderDeliveredAsync(string phone, string customerName,
        string orderNum, CancellationToken ct = default)
        => await SendAsync(phone, "order_delivered", new()
        {
            ["customer_name"] = customerName,
            ["order_number"]  = orderNum,
        }, ct);

    public async Task SendBookingReminderAsync(string phone, string customerName,
        string serviceName, string date, string time, CancellationToken ct = default)
        => await SendAsync(phone, "booking_reminder", new()
        {
            ["customer_name"] = customerName,
            ["service_name"]  = serviceName,
            ["date"]          = date,
            ["time"]          = time,
        }, ct);

    public async Task SendCustomAsync(string phone, string template,
        Dictionary<string, string> vars, CancellationToken ct = default)
        => await SendAsync(phone, template, vars, ct);

    private async Task SendAsync(string phone, string template,
        Dictionary<string, string> vars, CancellationToken ct)
    {
        var enabled = _config["WhatsApp:Enabled"] == "true";
        var msg = new WhatsappMessage
        {
            Phone        = phone,
            Template     = template,
            VariablesJson= JsonSerializer.Serialize(vars),
        };
        _db.Set<WhatsappMessage>().Add(msg);

        if (!enabled)
        {
            msg.Status = WhatsappStatus.Sent;
            msg.SentAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _log.LogDebug("WhatsApp (disabled): {Template} to {Phone}", template, phone);
            return;
        }

        try
        {
            var accountSid = _config["WhatsApp:AccountSid"] ?? string.Empty;
            var authToken  = _config["WhatsApp:AuthToken"]  ?? string.Empty;
            var from       = _config["WhatsApp:FromNumber"] ?? "whatsapp:+14155238886";

            var client = _http.CreateClient("Twilio");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}")));

            // Build message body from template + vars
            var body = BuildBody(template, vars);

            var formData = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("From", from),
                new KeyValuePair<string, string>("To",   $"whatsapp:{phone}"),
                new KeyValuePair<string, string>("Body", body),
            ]);

            var response = await client.PostAsync(
                $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json",
                formData, ct);

            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            msg.ProviderMsgId = json.RootElement
                .TryGetProperty("sid", out var sid) ? sid.GetString() : null;
            msg.Status = response.IsSuccessStatusCode
                ? WhatsappStatus.Sent : WhatsappStatus.Failed;
            if (!response.IsSuccessStatusCode)
                msg.ErrorMessage = json.RootElement
                    .TryGetProperty("message", out var err) ? err.GetString() : "Unknown error";
            msg.SentAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            msg.Status       = WhatsappStatus.Failed;
            msg.ErrorMessage = ex.Message;
            _log.LogWarning(ex, "WhatsApp send failed: {Template} to {Phone}", template, phone);
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string BuildBody(string template, Dictionary<string, string> vars)
    {
        // Template messages — replace with your WhatsApp Business approved templates
        var templates = new Dictionary<string, string>
        {
            ["order_confirmed"] = "مرحباً {customer_name} 👋\nتم تأكيد طلبك #{order_number} بقيمة {total} ✅\nسنخبرك فور جهوزيته!",
            ["order_ready"]     = "طلبك جاهز! 🎉\nمرحباً {customer_name}، طلبك #{order_number} جاهز للاستلام الآن. 🛍️",
            ["order_delivered"] = "تم التسليم ✅\nمرحباً {customer_name}، تم تسليم طلبك #{order_number} بنجاح. شكراً لاختيارك MallX! ❤️",
            ["booking_reminder"]= "تذكير بموعدك 📅\nمرحباً {customer_name}، موعدك لـ {service_name} غداً {date} الساعة {time}. لا تتأخر! 😊",
        };

        var body = templates.GetValueOrDefault(template, template);
        foreach (var (key, val) in vars)
            body = body.Replace($"{{{key}}}", val);
        return body;
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  SUPERADMIN SERVICE — Multi-mall platform management
// ══════════════════════════════════════════════════════════════════════════
public record PlatformOverviewDto
{
    public int     TotalMalls      { get; init; }
    public int     TotalStores     { get; init; }
    public int     TotalCustomers  { get; init; }
    public decimal TotalRevenue    { get; init; }
    public decimal TotalCommission { get; init; }
    public int     ActiveSubs      { get; init; }
    public int     TrialSubs       { get; init; }
    public List<MallSummaryDto> Malls { get; init; } = [];
}

public record MallSummaryDto
{
    public Guid   MallId     { get; init; }
    public string Name       { get; init; } = string.Empty;
    public int    Stores     { get; init; }
    public int    Customers  { get; init; }
    public decimal Revenue   { get; init; }
    public decimal Commission{ get; init; }
    public bool   IsActive   { get; init; }
}

public record CreateMallRequest(
    string Name, string? NameAr, string Slug,
    string? Address, decimal? GeoLat, decimal? GeoLng,
    string? Phone, string? Email
);

public record StoreSubscriptionDto
{
    public Guid   StoreId      { get; init; }
    public string StoreName    { get; init; } = string.Empty;
    public string PlanName     { get; init; } = string.Empty;
    public string Status       { get; init; } = string.Empty;
    public decimal Amount      { get; init; }
    public string BillingCycle { get; init; } = string.Empty;
    public DateTime? NextBilling{ get; init; }
    public DateTime? TrialEnds { get; init; }
}

public interface ISuperAdminService
{
    Task<ApiResponse<PlatformOverviewDto>>        GetOverviewAsync(CancellationToken ct = default);
    Task<ApiResponse<Domain.Entities.Mall.Mall>>  CreateMallAsync(CreateMallRequest req, CancellationToken ct = default);
    Task<ApiResponse<List<StoreSubscriptionDto>>> GetSubscriptionsAsync(Guid mallId, CancellationToken ct = default);
    Task<ApiResponse>                             SuspendStoreAsync(Guid storeId, string reason, CancellationToken ct = default);
    Task<ApiResponse>                             ActivateStoreAsync(Guid storeId, CancellationToken ct = default);
    Task<ApiResponse<string>>                     GetSettingAsync(string key, CancellationToken ct = default);
    Task<ApiResponse>                             SetSettingAsync(string key, string value, CancellationToken ct = default);
}

public class SuperAdminService : ISuperAdminService
{
    private readonly MesterXDbContext _db;
    private readonly ILogger<SuperAdminService> _log;

    public SuperAdminService(MesterXDbContext db, ILogger<SuperAdminService> log)
    { _db = db; _log = log; }

    public async Task<ApiResponse<PlatformOverviewDto>> GetOverviewAsync(
        CancellationToken ct = default)
    {
        var malls = await _db.Malls.AsNoTracking().Where(m => m.IsActive).ToListAsync(ct);

        var mallSummaries = new List<MallSummaryDto>();
        decimal totalRevenue = 0, totalCommission = 0;
        int totalStores = 0, totalCustomers = 0;

        foreach (var mall in malls)
        {
            var stores    = await _db.Tenants.CountAsync(
                t => EF.Property<Guid?>(t, "MallId") == mall.Id && t.IsActive, ct);
            var customers = await _db.MallCustomers.CountAsync(
                c => c.MallId == mall.Id && !c.IsDeleted, ct);

            var monthStart  = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0,0,0, DateTimeKind.Utc);
            var mallRevenue = await _db.MallOrders.AsNoTracking()
                .Where(o => o.MallId == mall.Id
                    && o.PlacedAt >= monthStart
                    && o.Status != MallOrderStatus.Cancelled)
                .SumAsync(o => o.Total, ct);

            var mallCommission = await _db.StoreOrders.AsNoTracking()
                .Include(so => so.MallOrder)
                .Where(so => so.MallOrder.MallId == mall.Id && so.CreatedAt >= monthStart)
                .SumAsync(so => so.CommissionAmt, ct);

            totalRevenue    += mallRevenue;
            totalCommission += mallCommission;
            totalStores     += stores;
            totalCustomers  += customers;

            mallSummaries.Add(new MallSummaryDto
            {
                MallId     = mall.Id,    Name     = mall.Name,
                Stores     = stores,     Customers= customers,
                Revenue    = mallRevenue, Commission= mallCommission,
                IsActive   = mall.IsActive,
            });
        }

        var activeSubs = await _db.Set<StoreSubscriptionEntity>()
            .CountAsync(s => s.Status == SubStatus.Active, ct);
        var trialSubs  = await _db.Set<StoreSubscriptionEntity>()
            .CountAsync(s => s.Status == SubStatus.Trial, ct);

        return ApiResponse<PlatformOverviewDto>.Ok(new PlatformOverviewDto
        {
            TotalMalls      = malls.Count,
            TotalStores     = totalStores,
            TotalCustomers  = totalCustomers,
            TotalRevenue    = totalRevenue,
            TotalCommission = totalCommission,
            ActiveSubs      = activeSubs,
            TrialSubs       = trialSubs,
            Malls           = mallSummaries.OrderByDescending(m => m.Revenue).ToList(),
        });
    }

    public async Task<ApiResponse<Domain.Entities.Mall.Mall>> CreateMallAsync(
        CreateMallRequest req, CancellationToken ct = default)
    {
        var exists = await _db.Malls.AnyAsync(m => m.Slug == req.Slug, ct);
        if (exists) return ApiResponse<Domain.Entities.Mall.Mall>.Fail("هذا الـ slug مستخدم مسبقاً.");

        var mall = new Domain.Entities.Mall.Mall
        {
            Name    = req.Name,  NameAr  = req.NameAr,
            Slug    = req.Slug,  Address = req.Address,
            GeoLat  = req.GeoLat, GeoLng = req.GeoLng,
            Phone   = req.Phone,  Email  = req.Email,
        };
        _db.Malls.Add(mall);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("New mall created: {Name} ({Slug})", mall.Name, mall.Slug);
        return ApiResponse<Domain.Entities.Mall.Mall>.Ok(mall);
    }

    public async Task<ApiResponse<List<StoreSubscriptionDto>>> GetSubscriptionsAsync(
        Guid mallId, CancellationToken ct = default)
    {
        var subs = await _db.Set<StoreSubscriptionEntity>()
            .AsNoTracking()
            .Include(s => s.Store)
            .Include(s => s.Plan)
            .Where(s => s.MallId == mallId)
            .OrderBy(s => s.Status)
            .ToListAsync(ct);

        return ApiResponse<List<StoreSubscriptionDto>>.Ok(subs.Select(s => new StoreSubscriptionDto
        {
            StoreId     = s.StoreId,
            StoreName   = s.Store?.Name ?? string.Empty,
            PlanName    = s.Plan?.Name  ?? string.Empty,
            Status      = s.Status.ToString(),
            Amount      = s.Amount,
            BillingCycle= s.BillingCycle.ToString(),
            NextBilling = s.NextBillingAt,
            TrialEnds   = s.TrialEndsAt,
        }).ToList());
    }

    public async Task<ApiResponse> SuspendStoreAsync(
        Guid storeId, string reason, CancellationToken ct = default)
    {
        var store = await _db.Tenants.FindAsync([storeId], ct);
        if (store == null) return ApiResponse.Fail("المحل غير موجود.");
        store.IsActive = false;
        store.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _log.LogWarning("Store {Id} suspended. Reason: {Reason}", storeId, reason);
        return ApiResponse.Ok();
    }

    public async Task<ApiResponse> ActivateStoreAsync(Guid storeId, CancellationToken ct = default)
    {
        var store = await _db.Tenants.FindAsync([storeId], ct);
        if (store == null) return ApiResponse.Fail("المحل غير موجود.");
        store.IsActive = true;
        store.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok();
    }

    public async Task<ApiResponse<string>> GetSettingAsync(string key, CancellationToken ct = default)
    {
        var setting = await _db.Set<PlatformSetting>()
            .FirstOrDefaultAsync(s => s.Key == key, ct);
        return setting == null
            ? ApiResponse<string>.Fail("الإعداد غير موجود.")
            : ApiResponse<string>.Ok(setting.Value);
    }

    public async Task<ApiResponse> SetSettingAsync(string key, string value, CancellationToken ct = default)
    {
        var setting = await _db.Set<PlatformSetting>()
            .FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting == null)
        {
            _db.Set<PlatformSetting>().Add(new PlatformSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value     = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok();
    }
}

// ─── Placeholder entities for EF ──────────────────────────────────────────
public enum SubStatus { Active, Suspended, Cancelled, PastDue, Trial }
public enum SubBillingCycle { Monthly, Quarterly, Annual }

public class StoreSubscriptionEntity
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid StoreId     { get; set; }
    [Required] public Guid MallId      { get; set; }
    [Required] public Guid PlanId      { get; set; }
    public SubBillingCycle BillingCycle{ get; set; } = SubBillingCycle.Monthly;
    public SubStatus Status            { get; set; } = SubStatus.Trial;
    public decimal Amount              { get; set; }
    public DateTime? TrialEndsAt       { get; set; }
    public DateTime CurrentPeriodStart { get; set; } = DateTime.UtcNow;
    public DateTime CurrentPeriodEnd   { get; set; } = DateTime.UtcNow.AddMonths(1);
    public DateTime? NextBillingAt     { get; set; }
    public bool AutoRenew              { get; set; } = true;
    public DateTime CreatedAt          { get; set; } = DateTime.UtcNow;
    public virtual Domain.Entities.Core.Tenant? Store { get; set; }
    public virtual Domain.Entities.Core.SubscriptionPlan? Plan { get; set; }
}

public class PlatformSetting
{
    [Key, MaxLength(100)] public string Key { get; set; } = string.Empty;
    [Required] public string Value          { get; set; } = string.Empty;
    public string? Description              { get; set; }
    public DateTime UpdatedAt               { get; set; } = DateTime.UtcNow;
}
