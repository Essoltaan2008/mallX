using System.Text.Json;
using MesterX.Application.DTOs.Mall;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MesterX.Application.Services.Phase5;

// ══════════════════════════════════════════════════════════════════════════
//  CACHE KEY REGISTRY
// ══════════════════════════════════════════════════════════════════════════
public static class CacheKeys
{
    // Mall / Stores
    public static string MallStores(Guid mallId)          => $"mall:{mallId}:stores";
    public static string StoreMenu(Guid storeId)          => $"store:{storeId}:menu";
    public static string StoreRating(Guid storeId)        => $"store:{storeId}:rating";
    public static string ActiveFlashSales(Guid mallId)    => $"mall:{mallId}:flash";
    public static string ActiveCoupons(Guid mallId)       => $"mall:{mallId}:coupons";

    // Customer
    public static string CustomerWallet(Guid customerId)  => $"customer:{customerId}:wallet";
    public static string CustomerCart(Guid customerId)    => $"customer:{customerId}:cart";
    public static string CustomerSession(string token)    => $"session:{token}";

    // Queue
    public static string StoreQueue(Guid storeId)         => $"queue:{storeId}:live";
    public static string QueueTicket(Guid ticketId)       => $"ticket:{ticketId}";

    // Driver
    public static string DriverLocation(Guid driverId)    => $"driver:loc:{driverId}";
    public static string DriverOrderMap(Guid orderId)     => $"order:{orderId}:driver";

    // Analytics
    public static string DashboardKpi(Guid mallId, string period) => $"kpi:{mallId}:{period}";
    public static string TopStores(Guid mallId)           => $"mall:{mallId}:top_stores";

    // Rate limiting
    public static string RateLimit(string key)            => $"ratelimit:{key}";
}

// ══════════════════════════════════════════════════════════════════════════
//  CACHE SERVICE
// ══════════════════════════════════════════════════════════════════════════
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class;
    Task DeleteAsync(string key);
    Task DeleteByPatternAsync(string pattern);
    Task<bool> ExistsAsync(string key);

    // Atomic operations
    Task<long> IncrementAsync(string key, TimeSpan? ttl = null);
    Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan ttl);

    // Hash (for driver locations, live data)
    Task HashSetAsync(string key, string field, string value, TimeSpan? ttl = null);
    Task<string?> HashGetAsync(string key, string field);

    // Sorted set (leaderboard, top stores)
    Task SortedSetAddAsync(string key, string member, double score);
    Task<IList<(string Member, double Score)>> SortedSetRangeAsync(string key, int count = 10);
}

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _log;
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Default TTLs
    private static readonly TimeSpan SHORT  = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MEDIUM = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LONG   = TimeSpan.FromHours(6);

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> log)
    { _redis = redis; _log = log; }

    private IDatabase Db => _redis.GetDatabase();

    // ── GET ───────────────────────────────────────────────────────────────
    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var val = await Db.StringGetAsync(key);
            if (!val.HasValue) return null;
            return JsonSerializer.Deserialize<T>(val!, _json);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Cache GET failed for key {Key}", key);
            return null;
        }
    }

    // ── SET ───────────────────────────────────────────────────────────────
    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(value, _json);
            await Db.StringSetAsync(key, json, ttl ?? MEDIUM);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Cache SET failed for key {Key}", key);
        }
    }

    // ── DELETE ────────────────────────────────────────────────────────────
    public async Task DeleteAsync(string key)
    {
        try { await Db.KeyDeleteAsync(key); }
        catch (Exception ex) { _log.LogWarning(ex, "Cache DEL failed for {Key}", key); }
    }

    // ── DELETE BY PATTERN ─────────────────────────────────────────────────
    public async Task DeleteByPatternAsync(string pattern)
    {
        try
        {
            var server  = _redis.GetServer(_redis.GetEndPoints().First());
            var keys    = server.Keys(pattern: pattern).ToArray();
            if (keys.Any()) await Db.KeyDeleteAsync(keys);
            _log.LogDebug("Cache pattern delete: {Pattern} — {Count} keys", pattern, keys.Length);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Cache pattern DEL failed: {Pattern}", pattern); }
    }

    public async Task<bool> ExistsAsync(string key)
        => await Db.KeyExistsAsync(key);

    // ── ATOMIC OPS ────────────────────────────────────────────────────────
    public async Task<long> IncrementAsync(string key, TimeSpan? ttl = null)
    {
        var val = await Db.StringIncrementAsync(key);
        if (val == 1 && ttl.HasValue)   // just created — set expiry
            await Db.KeyExpireAsync(key, ttl);
        return val;
    }

    public async Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan ttl)
        => await Db.StringSetAsync(key, value, ttl, When.NotExists);

    // ── HASH ─────────────────────────────────────────────────────────────
    public async Task HashSetAsync(string key, string field, string value, TimeSpan? ttl = null)
    {
        await Db.HashSetAsync(key, field, value);
        if (ttl.HasValue) await Db.KeyExpireAsync(key, ttl);
    }

    public async Task<string?> HashGetAsync(string key, string field)
    {
        var val = await Db.HashGetAsync(key, field);
        return val.HasValue ? val.ToString() : null;
    }

    // ── SORTED SET ────────────────────────────────────────────────────────
    public async Task SortedSetAddAsync(string key, string member, double score)
        => await Db.SortedSetAddAsync(key, member, score);

    public async Task<IList<(string Member, double Score)>> SortedSetRangeAsync(
        string key, int count = 10)
    {
        var entries = await Db.SortedSetRangeByRankWithScoresAsync(
            key, 0, count - 1, Order.Descending);
        return entries.Select(e => (e.Element.ToString(), e.Score)).ToList();
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  CACHED SERVICE DECORATORS — wrap existing services with caching
// ══════════════════════════════════════════════════════════════════════════
public class CachedProductService
{
    private readonly ICacheService _cache;
    private readonly MesterX.Infrastructure.Data.MesterXDbContext _db;

    public CachedProductService(ICacheService cache,
        MesterX.Infrastructure.Data.MesterXDbContext db)
    { _cache = cache; _db = db; }

    /// <summary>Get store products with 5-minute cache</summary>
    public async Task<List<MesterX.Domain.Entities.Core.Product>> GetStoreProductsAsync(
        Guid storeId, string? search = null, CancellationToken ct = default)
    {
        var cacheKey = $"store:{storeId}:products:{search ?? "all"}";

        // Try cache first
        var cached = await _cache.GetAsync<List<MesterX.Domain.Entities.Core.Product>>(cacheKey);
        if (cached != null) return cached;

        // Fetch from DB
        var query = _db.Products
            .AsNoTracking()
            .Where(p => p.TenantId == storeId && p.IsActive && !p.IsDeleted);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(p =>
                Microsoft.EntityFrameworkCore.EF.Functions.ILike(p.Name, $"%{search}%"));

        var products = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(query, ct);

        // Cache for 5 minutes
        await _cache.SetAsync(cacheKey, products, TimeSpan.FromMinutes(5));
        return products;
    }

    /// <summary>Invalidate product cache when updated</summary>
    public async Task InvalidateStoreProductsAsync(Guid storeId)
        => await _cache.DeleteByPatternAsync($"store:{storeId}:products:*");
}

// ══════════════════════════════════════════════════════════════════════════
//  RATE LIMITER SERVICE (Redis-backed sliding window)
// ══════════════════════════════════════════════════════════════════════════
public interface IRedisRateLimiter
{
    Task<(bool Allowed, int Remaining, int RetryAfterSeconds)> CheckAsync(
        string key, int maxRequests, TimeSpan window);
}

public class RedisRateLimiter : IRedisRateLimiter
{
    private readonly ICacheService _cache;

    public RedisRateLimiter(ICacheService cache) => _cache = cache;

    public async Task<(bool Allowed, int Remaining, int RetryAfterSeconds)> CheckAsync(
        string key, int maxRequests, TimeSpan window)
    {
        var count = await _cache.IncrementAsync(
            CacheKeys.RateLimit(key), window);

        var remaining = Math.Max(0, maxRequests - (int)count);
        var allowed   = count <= maxRequests;
        var retry     = allowed ? 0 : (int)window.TotalSeconds;

        return (allowed, remaining, retry);
    }
}
