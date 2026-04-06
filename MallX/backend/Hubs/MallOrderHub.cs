using System.Security.Claims;
using MesterX.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MesterX.Hubs;

// ══════════════════════════════════════════════════════════════════════════
//  MALL ORDER HUB — real-time for customers, stores, drivers
// ══════════════════════════════════════════════════════════════════════════
[Authorize]
public class MallOrderHub : Hub
{
    private readonly MesterXDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<MallOrderHub> _log;

    // Redis key patterns
    private const string DRIVER_LOC_KEY  = "driver:loc:{0}";          // driver GPS
    private const string ORDER_GROUP_KEY = "order:{0}";               // SignalR group
    private const string STORE_GROUP_KEY = "store:{0}";               // store group
    private const string DRIVER_GRP_KEY  = "driver:{0}";              // driver group

    public MallOrderHub(MesterXDbContext db,
        IConnectionMultiplexer redis, ILogger<MallOrderHub> log)
    { _db = db; _redis = redis; _log = log; }

    // ── Connection lifecycle ──────────────────────────────────────────────
    public override async Task OnConnectedAsync()
    {
        var userId   = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var tokenType= Context.User?.FindFirstValue("token_type");
        var mallId   = Context.User?.FindFirstValue("mall_id");
        var tenantId = Context.User?.FindFirstValue("tenant_id");

        _log.LogDebug("Hub connected: {UserId} type={Type}", userId, tokenType);

        // Join appropriate groups
        if (tokenType == "customer" && userId != null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"customer:{userId}");

            // Rejoin active order groups
            var activeOrders = await _db.MallOrders
                .AsNoTracking()
                .Where(o => o.CustomerId == Guid.Parse(userId)
                    && o.Status != MallOrderStatus.Delivered
                    && o.Status != MallOrderStatus.Cancelled)
                .Select(o => o.Id)
                .ToListAsync();

            foreach (var orderId in activeOrders)
                await Groups.AddToGroupAsync(Context.ConnectionId,
                    string.Format(ORDER_GROUP_KEY, orderId));
        }
        else if (tenantId != null) // Store owner
        {
            await Groups.AddToGroupAsync(Context.ConnectionId,
                string.Format(STORE_GROUP_KEY, tenantId));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _log.LogDebug("Hub disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CLIENT → SERVER METHODS
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Customer joins an order's tracking room</summary>
    public async Task TrackOrder(string orderId)
    {
        var customerId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var order      = await _db.MallOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == Guid.Parse(orderId)
                && o.CustomerId == Guid.Parse(customerId!));

        if (order == null) return;

        await Groups.AddToGroupAsync(Context.ConnectionId,
            string.Format(ORDER_GROUP_KEY, orderId));

        // Send current status immediately
        await Clients.Caller.SendAsync("OrderStatus", new
        {
            orderId,
            status = order.Status.ToString(),
            updatedAt = order.UpdatedAt
        });

        // Send driver location if exists (from Redis)
        var db    = _redis.GetDatabase();
        var locKey= string.Format(DRIVER_LOC_KEY,
            order.DriverId?.ToString() ?? "none");
        var locJson = await db.StringGetAsync(locKey);
        if (locJson.HasValue)
            await Clients.Caller.SendAsync("DriverLocation",
                System.Text.Json.JsonSerializer.Deserialize<object>(locJson!));
    }

    /// <summary>Driver updates their GPS location (every 5s)</summary>
    public async Task UpdateDriverLocation(
        string orderId, double lat, double lng, double? heading, double? speed)
    {
        var driverId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (driverId == null) return;

        var payload = new
        {
            driverId,
            lat, lng,
            heading = heading ?? 0,
            speed   = speed   ?? 0,
            ts      = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Persist to Redis (TTL 30s — if driver disconnects, location auto-expires)
        var db     = _redis.GetDatabase();
        var locKey = string.Format(DRIVER_LOC_KEY, driverId);
        await db.StringSetAsync(locKey,
            System.Text.Json.JsonSerializer.Serialize(payload),
            TimeSpan.FromSeconds(30));

        // Broadcast to order group (customer sees real-time location)
        await Clients
            .Group(string.Format(ORDER_GROUP_KEY, orderId))
            .SendAsync("DriverLocation", payload);

        // Update DB every 30s to avoid excessive writes
        var tsKey = $"driver:last_db_write:{driverId}";
        var last  = await db.StringGetAsync(tsKey);
        if (!last.HasValue)
        {
            await db.StringSetAsync(tsKey, "1", TimeSpan.FromSeconds(30));
            await _db.Set<Driver>()
                .Where(d => d.Id == Guid.Parse(driverId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.CurrentLat,  (decimal)lat)
                    .SetProperty(d => d.CurrentLng,  (decimal)lng)
                    .SetProperty(d => d.LastSeenAt,  DateTime.UtcNow));
        }
    }

    /// <summary>Driver marks order as picked up</summary>
    public async Task MarkPickedUp(string orderId)
    {
        var order = await _db.MallOrders
            .FirstOrDefaultAsync(o => o.Id == Guid.Parse(orderId));
        if (order == null) return;

        order.Status    = MallOrderStatus.PickedUp;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await Clients
            .Group(string.Format(ORDER_GROUP_KEY, orderId))
            .SendAsync("OrderStatus", new
            {
                orderId,
                status    = "PickedUp",
                message   = "السائق في طريقه إليك! 🚗",
                updatedAt = order.UpdatedAt
            });
    }

    /// <summary>Driver marks order as delivered</summary>
    public async Task MarkDelivered(string orderId)
    {
        var order = await _db.MallOrders
            .Include(o => o.StoreOrders)
            .FirstOrDefaultAsync(o => o.Id == Guid.Parse(orderId));
        if (order == null) return;

        order.Status      = MallOrderStatus.Delivered;
        order.DeliveredAt = DateTime.UtcNow;
        order.UpdatedAt   = DateTime.UtcNow;

        // Notify all stores in this order
        foreach (var so in order.StoreOrders)
        {
            await Clients
                .Group(string.Format(STORE_GROUP_KEY, so.StoreId))
                .SendAsync("StoreOrderDelivered", new { storeOrderId = so.Id, orderId });
        }

        await _db.SaveChangesAsync();

        await Clients
            .Group(string.Format(ORDER_GROUP_KEY, orderId))
            .SendAsync("OrderStatus", new
            {
                orderId,
                status    = "Delivered",
                message   = "تم التسليم! 🎉 شاركنا رأيك في تجربتك",
                updatedAt = order.UpdatedAt
            });
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  HUB NOTIFIER — called from services to push events
// ══════════════════════════════════════════════════════════════════════════
public interface IHubNotifier
{
    Task NotifyOrderStatusChangedAsync(string orderId, string status, string? message = null);
    Task NotifyNewStoreOrderAsync(string storeId, object orderData);
    Task NotifyQueueTicketReadyAsync(string storeId, int ticketNumber, string ticketId);
}

public class HubNotifier : IHubNotifier
{
    private readonly IHubContext<MallOrderHub> _hub;

    public HubNotifier(IHubContext<MallOrderHub> hub) => _hub = hub;

    public async Task NotifyOrderStatusChangedAsync(
        string orderId, string status, string? message = null)
        => await _hub.Clients
            .Group($"order:{orderId}")
            .SendAsync("OrderStatus", new { orderId, status, message,
                updatedAt = DateTime.UtcNow });

    public async Task NotifyNewStoreOrderAsync(string storeId, object orderData)
        => await _hub.Clients
            .Group($"store:{storeId}")
            .SendAsync("NewOrder", orderData);

    public async Task NotifyQueueTicketReadyAsync(
        string storeId, int ticketNumber, string ticketId)
        => await _hub.Clients
            .Group($"store:{storeId}")
            .SendAsync("TicketReady", new { ticketNumber, ticketId,
                message = $"🔔 تذكرة #{ticketNumber} جاهزة!" });
}
