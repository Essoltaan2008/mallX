using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MesterX.Infrastructure.Caching;

// ──────────────────────────────────────────────────────────────────────────
//  INTERFACE
// ──────────────────────────────────────────────────────────────────────────
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task      SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task      DeleteAsync(string key, CancellationToken ct = default);
    Task      DeletePatternAsync(string pattern, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    Task<T>   GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken ct = default);
    Task      IncrementAsync(string key, int by = 1, TimeSpan? expiry = null, CancellationToken ct = default);
}

// ──────────────────────────────────────────────────────────────────────────
//  REDIS IMPLEMENTATION
// ──────────────────────────────────────────────────────────────────────────
public class RedisCacheService : ICacheService
{
    private readonly IDatabase _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _log;

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> log)
    {
        _redis = redis;
        _db    = redis.GetDatabase();
        _log   = log;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var val = await _db.StringGetAsync(key);
            if (!val.HasValue) return default;
            return JsonSerializer.Deserialize<T>(val!, _opts);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Redis GET failed for key {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null,
        CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, _opts);
            await _db.StringSetAsync(key, json, expiry ?? TimeSpan.FromMinutes(30));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Redis SET failed for key {Key}", key);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try { await _db.KeyDeleteAsync(key); }
        catch (Exception ex) { _log.LogWarning(ex, "Redis DEL failed for key {Key}", key); }
    }

    public async Task DeletePatternAsync(string pattern, CancellationToken ct = default)
    {
        try
        {
            var endpoints = _redis.GetEndPoints();
            foreach (var ep in endpoints)
            {
                var server = _redis.GetServer(ep);
                var keys   = server.Keys(pattern: pattern).ToArray();
                if (keys.Any())
                    await _db.KeyDeleteAsync(keys);
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Redis DEL pattern {P} failed", pattern); }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try { return await _db.KeyExistsAsync(key); }
        catch { return false; }
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory,
        TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached != null) return cached;

        var value = await factory();
        await SetAsync(key, value, expiry, ct);
        return value;
    }

    public async Task IncrementAsync(string key, int by = 1,
        TimeSpan? expiry = null, CancellationToken ct = default)
    {
        try
        {
            await _db.StringIncrementAsync(key, by);
            if (expiry.HasValue && !await _db.KeyExpireAsync(key, expiry))
                await _db.KeyExpireAsync(key, expiry);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Redis INCR failed for key {Key}", key); }
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  CACHE KEY CONSTANTS
// ──────────────────────────────────────────────────────────────────────────
public static class CacheKeys
{
    // Mall
    public static string MallInfo(Guid mallId)         => $"mall:{mallId}";
    public static string MallStores(Guid mallId)        => $"mall:{mallId}:stores";
    public static string StoreMenu(Guid storeId)        => $"store:{storeId}:menu";
    public static string ActiveFlashSales(Guid mallId)  => $"mall:{mallId}:flash";
    public static string ActiveCoupons(Guid mallId)     => $"mall:{mallId}:coupons";

    // Customer
    public static string CustomerProfile(Guid cid)     => $"customer:{cid}:profile";
    public static string CustomerCart(Guid cid)        => $"customer:{cid}:cart";
    public static string LoyaltyWallet(Guid cid)       => $"customer:{cid}:loyalty";

    // Orders
    public static string OrderStatus(Guid orderId)     => $"order:{orderId}:status";
    public static string StoreQueue(Guid storeId)      => $"store:{storeId}:queue";

    // Driver
    public static string DriverLocation(string drvId)  => $"driver:loc:{drvId}";
    public static string DriverDbUpdate(string drvId)  => $"driver:db-update:{drvId}";

    // Rate limiting
    public static string LoginAttempts(string email)   => $"ratelimit:login:{email}";
    public static string ApiRequests(string ip)        => $"ratelimit:api:{ip}";

    // Analytics
    public static string DailyStats(Guid mallId)       => $"analytics:{mallId}:{DateTime.UtcNow:yyyyMMdd}";
}

// ──────────────────────────────────────────────────────────────────────────
//  CACHE-AWARE EXTENSIONS (decorators for services)
// ──────────────────────────────────────────────────────────────────────────
public static class CacheExtensions
{
    /// <summary>Cache menu for 5 minutes, bust on any item change</summary>
    public static TimeSpan MenuTtl    => TimeSpan.FromMinutes(5);

    /// <summary>Store list — 10 minutes</summary>
    public static TimeSpan StoresTtl  => TimeSpan.FromMinutes(10);

    /// <summary>Flash sales — 30 seconds (they're time-sensitive!)</summary>
    public static TimeSpan FlashTtl   => TimeSpan.FromSeconds(30);

    /// <summary>Customer profile — 15 minutes</summary>
    public static TimeSpan ProfileTtl => TimeSpan.FromMinutes(15);

    /// <summary>Loyalty wallet — 5 minutes</summary>
    public static TimeSpan LoyaltyTtl => TimeSpan.FromMinutes(5);

    /// <summary>Mall/store info — 30 minutes</summary>
    public static TimeSpan InfoTtl    => TimeSpan.FromMinutes(30);
}
