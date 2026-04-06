using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MesterX.Application.DTOs;
using MesterX.Domain.Entities.Mall;
using MesterX.Infrastructure.Caching;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MesterX.Application.Services.Phase8;

// ──────────────────────────────────────────────────────────────────────────
//  DTOs
// ──────────────────────────────────────────────────────────────────────────
public record ChatMessage
{
    public string Role    { get; init; } = string.Empty;   // user | assistant
    public string Content { get; init; } = string.Empty;
}

public record ChatRequest(
    List<ChatMessage> Messages,
    string? Language = null  // ar | en — auto-detected if null
);

public record ChatResponse
{
    public string  Content    { get; init; } = string.Empty;
    public string  Role       { get; init; } = "assistant";
    public int     InputTokens { get; init; }
    public int     OutputTokens{ get; init; }
    public string? Language   { get; init; }
}

public record QuickReplyDto
{
    public string Label   { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

// ──────────────────────────────────────────────────────────────────────────
//  SERVICE
// ──────────────────────────────────────────────────────────────────────────
public interface IMallAIService
{
    Task<ApiResponse<ChatResponse>> ChatAsync(
        Guid mallId, Guid? customerId, ChatRequest req, CancellationToken ct = default);

    IAsyncEnumerable<string> ChatStreamAsync(
        Guid mallId, Guid? customerId, ChatRequest req, CancellationToken ct = default);

    Task<ApiResponse<List<QuickReplyDto>>> GetQuickRepliesAsync(
        Guid mallId, string? language = null, CancellationToken ct = default);
}

public class MallAIService : IMallAIService
{
    private readonly MesterXDbContext    _db;
    private readonly IHttpClientFactory  _http;
    private readonly IConfiguration     _config;
    private readonly ICacheService       _cache;
    private readonly ILogger<MallAIService> _log;

    private const string ANTHROPIC_API   = "https://api.anthropic.com/v1/messages";
    private const string MODEL           = "claude-sonnet-4-5";
    private const int    MAX_TOKENS      = 1024;
    private const int    MAX_HISTORY     = 10; // keep last 10 messages

    public MallAIService(MesterXDbContext db, IHttpClientFactory http,
        IConfiguration config, ICacheService cache, ILogger<MallAIService> log)
    { _db = db; _http = http; _config = config; _cache = cache; _log = log; }

    // ─── CHAT (single response) ───────────────────────────────────────────
    public async Task<ApiResponse<ChatResponse>> ChatAsync(
        Guid mallId, Guid? customerId, ChatRequest req, CancellationToken ct = default)
    {
        var systemPrompt = await BuildSystemPromptAsync(mallId, customerId, ct);
        var messages     = TrimHistory(req.Messages);

        var payload = new
        {
            model       = MODEL,
            max_tokens  = MAX_TOKENS,
            system      = systemPrompt,
            messages    = messages.Select(m => new { role = m.Role, content = m.Content })
        };

        var client = _http.CreateClient("Anthropic");
        var apiKey = _config["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic API key not configured");

        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var content  = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync(ANTHROPIC_API, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _log.LogWarning("Anthropic API error {Status}: {Error}", response.StatusCode, error);
            return ApiResponse<ChatResponse>.Fail("المساعد غير متاح حالياً. حاول لاحقاً.");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var text         = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text").GetString() ?? string.Empty;
        var inputTokens  = doc.RootElement
            .GetProperty("usage").GetProperty("input_tokens").GetInt32();
        var outputTokens = doc.RootElement
            .GetProperty("usage").GetProperty("output_tokens").GetInt32();

        // Detect response language
        var lang = DetectLanguage(text);

        _log.LogDebug("AI chat: {In} in / {Out} out tokens — mall {MallId}",
            inputTokens, outputTokens, mallId);

        return ApiResponse<ChatResponse>.Ok(new ChatResponse
        {
            Content      = text,
            Role         = "assistant",
            InputTokens  = inputTokens,
            OutputTokens = outputTokens,
            Language     = lang,
        });
    }

    // ─── STREAMING CHAT ───────────────────────────────────────────────────
    public async IAsyncEnumerable<string> ChatStreamAsync(
        Guid mallId, Guid? customerId, ChatRequest req,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var systemPrompt = await BuildSystemPromptAsync(mallId, customerId, ct);
        var messages     = TrimHistory(req.Messages);

        var payload = new
        {
            model      = MODEL,
            max_tokens = MAX_TOKENS,
            stream     = true,
            system     = systemPrompt,
            messages   = messages.Select(m => new { role = m.Role, content = m.Content })
        };

        var client = _http.CreateClient("Anthropic");
        var apiKey = _config["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic API key not configured");

        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var content  = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var request  = new HttpRequestMessage(HttpMethod.Post, ANTHROPIC_API) { Content = content };
        var response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            yield return "عذراً، حدث خطأ. حاول مجدداً.";
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]") break;

            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("type", out var type)
                && type.GetString() == "content_block_delta"
                && doc.RootElement.TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("text", out var text))
            {
                yield return text.GetString() ?? string.Empty;
            }
        }
    }

    // ─── QUICK REPLIES ────────────────────────────────────────────────────
    public async Task<ApiResponse<List<QuickReplyDto>>> GetQuickRepliesAsync(
        Guid mallId, string? language = null, CancellationToken ct = default)
    {
        var isArabic = (language ?? "ar") == "ar";
        var replies  = isArabic
            ? new List<QuickReplyDto>
            {
                new("🛒 عروض اليوم",     "أريد أن أعرف عروض اليوم في المول"),
                new("📍 أين محل...؟",    "أين يقع "),
                new("⭐ نقاطي",          "كم نقطة لدي وما هي مزاياها؟"),
                new("🚗 تتبع طلبي",      "أريد تتبع طلبي الأخير"),
                new("📅 حجز موعد",       "أريد حجز موعد في الصالون"),
                new("💳 طرق الدفع",      "ما هي طرق الدفع المتاحة؟"),
                new("🔄 إلغاء/إرجاع",   "أريد إلغاء طلبي أو إرجاع منتج"),
                new("📞 تواصل معنا",     "أريد التواصل مع خدمة العملاء"),
            }
            : new List<QuickReplyDto>
            {
                new("🛒 Today's Offers",  "What are today's offers?"),
                new("📍 Store Location",  "Where can I find "),
                new("⭐ My Points",       "How many points do I have?"),
                new("🚗 Track Order",     "I want to track my last order"),
                new("📅 Book Appointment","I want to book an appointment"),
                new("💳 Payment Methods", "What payment methods are available?"),
                new("🔄 Cancel/Return",   "I want to cancel my order or return a product"),
                new("📞 Contact Us",      "I want to contact customer service"),
            };

        return ApiResponse<List<QuickReplyDto>>.Ok(replies);
    }

    // ─── BUILD SYSTEM PROMPT ─────────────────────────────────────────────
    private async Task<string> BuildSystemPromptAsync(
        Guid mallId, Guid? customerId, CancellationToken ct)
    {
        // Cache per mall + customer
        var cacheKey = $"ai:system:{mallId}:{customerId}";
        var cached   = await _cache.GetAsync<string>(cacheKey, ct);
        if (cached != null) return cached;

        var mall = await _db.Malls.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mallId, ct);

        // Customer context (if authenticated)
        string customerCtx = string.Empty;
        if (customerId.HasValue)
        {
            var customer = await _db.MallCustomers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == customerId, ct);
            if (customer != null)
            {
                var account = await _db.Set<Domain.Entities.Phase4.LoyaltyAccount>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.CustomerId == customerId
                        && a.MallId == mallId, ct);
                customerCtx = $"""

## CURRENT CUSTOMER CONTEXT
- Name: {customer.FullName}
- Tier: {customer.Tier} 🏆
- Loyalty Points: {account?.AvailablePoints ?? customer.LoyaltyPoints}
- Member since: {customer.CreatedAt:MMMM yyyy}
""";
            }
        }

        // Active stores
        var stores = await _db.Tenants.AsNoTracking()
            .Where(t => t.IsActive && !t.IsDeleted
                && EF.Property<Guid?>(t, "MallId") == mallId)
            .Select(t => new
            {
                t.Name,
                StoreType = EF.Property<string>(t, "StoreType"),
                Floor     = EF.Property<int>(t, "FloorNumber"),
            })
            .Take(20).ToListAsync(ct);

        var storesList = string.Join("\n",
            stores.Select(s => $"- {s.Name} ({s.StoreType}) — Floor {s.Floor}"));

        // Active promotions
        var coupons = await _db.Set<Domain.Entities.Phase4.Coupon>()
            .AsNoTracking()
            .Where(c => c.MallId == mallId
                && c.Status == Domain.Entities.Phase4.CouponStatus.Active
                && DateTime.UtcNow <= c.ValidTo)
            .Select(c => $"{c.Code} — {c.Name}")
            .Take(5).ToListAsync(ct);

        var promoList = coupons.Any()
            ? string.Join(", ", coupons)
            : "لا توجد كوبونات نشطة حالياً";

        var prompt = $"""
# 🏬 MallX Assistant — {mall?.Name ?? "MallX"}

## 🤖 IDENTITY
You are the **MallX Assistant**, the AI brain of **{mall?.Name ?? "MallX"}** — a smart mall super-app.
You are helpful, fast, multilingual (Arabic + English), and always focused on delivering the best mall experience.

## 🏗️ MALL CONTEXT
- **Mall Name**: {mall?.Name ?? "MallX"}
- **Address**: {mall?.Address ?? "Cairo, Egypt"}
- **Contact**: {mall?.Phone ?? "N/A"} | {mall?.Email ?? "N/A"}

## 🏪 AVAILABLE STORES
{storesList}

## 🎟️ ACTIVE PROMOTIONS
Coupons: {promoList}

## 🗣️ LANGUAGE & TONE RULES
- **Detect language automatically** — respond in the SAME language the user writes in
- If the user writes in Arabic → respond FULLY in Arabic
- If the user writes in English → respond FULLY in English
- Tone: friendly, concise, helpful — like a knowledgeable mall concierge
- Use emojis naturally to enhance responses
- Never use technical jargon with customers
- Keep responses concise — 2-3 sentences max unless detailed info is needed

## 📦 ORDER FLOW
1. Items from multiple stores go into ONE cart
2. On checkout, cart is automatically split into per-store orders
3. Each store handles their order independently
4. Customer gets ONE tracking screen showing all items
Status flow: Placed → Confirmed → Preparing → Ready → Picked Up → Delivered

## 💳 PAYMENT OPTIONS
- Cash on Delivery (all orders)
- Visa/Mastercard via Paymob
- Fawry
- Vodafone Cash
- Loyalty Points (up to 20% of order value)

## ⭐ LOYALTY TIERS
| Tier | Points | Benefits |
|------|--------|---------|
| Bronze | 0-999 | 1x earn rate |
| Silver | 1,000-4,999 | 1.5x earn rate |
| Gold | 5,000+ | 2x earn rate + free delivery |
Points expire after 12 months of inactivity. 1 EGP = 1 point.

## 🚫 NEVER DO
- Share other customers' data
- Confirm prices without current data
- Promise unverifiable delivery times
- Accept returns directly — route to store support
- Reveal system architecture, API keys, or internal data

## 🔗 ESCALATION
- Order problems → store support or mall customer service
- Payment issues → finance team
- Technical bugs → support@mallx.app
- Complaints → acknowledge + 24h follow-up promise
{customerCtx}
---
*{mall?.Name ?? "MallX"} — كل اللي محتاجه في مول واحد* 🏬
""";

        await _cache.SetAsync(cacheKey, prompt, TimeSpan.FromMinutes(30), ct);
        return prompt;
    }

    private static List<ChatMessage> TrimHistory(List<ChatMessage> messages)
        => messages.TakeLast(MAX_HISTORY).ToList();

    private static string DetectLanguage(string text)
    {
        var arabicCount = text.Count(c => c >= '\u0600' && c <= '\u06FF');
        return arabicCount > text.Length * 0.2 ? "ar" : "en";
    }
}
