using System.Security.Claims;
using MesterX.Infrastructure.Data;
using MesterX.Infrastructure.Caching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MesterX.Hubs;

// ══════════════════════════════════════════════════════════════════════════
//  ORDER TRACKING HUB
//  Clients join groups by orderId to receive real-time status updates
// ══════════════════════════════════════════════════════════════════════════
[Authorize]
public class OrderTrackingHub : Hub
{
    private readonly MesterXDbContext _db;
    private readonly ICacheService    _cache;

    public OrderTrackingHub(MesterXDbContext db, ICacheService cache)
    { _db = db; _cache = cache; }

    // Customer joins order room
    public async Task JoinOrder(string orderId)
    {
        var customerId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(customerId)) return;

        // Verify ownership
        var owned = await _db.MallOrders
            .AnyAsync(o => o.Id == Guid.Parse(orderId)
                && o.CustomerId == Guid.Parse(customerId));
        if (!owned) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, $"order-{orderId}");
        await Clients.Caller.SendAsync("Joined", orderId);
    }

    // Store joins their order management room
    public async Task JoinStoreRoom(string storeId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"store-{storeId}");
        await Clients.Caller.SendAsync("Joined", $"store-{storeId}");
    }

    // Driver joins tracking room
    public async Task JoinDriverRoom(string driverId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"driver-{driverId}");
        await Clients.Caller.SendAsync("Joined", $"driver-{driverId}");
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connId = Context.ConnectionId;
        await _cache.DeleteAsync($"conn:{connId}");
        await base.OnDisconnectedAsync(exception);
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  DRIVER LOCATION HUB
//  Drivers push GPS updates; customers & store owners receive them
// ══════════════════════════════════════════════════════════════════════════
[Authorize]
public class DriverLocationHub : Hub
{
    private readonly ICacheService    _cache;
    private readonly MesterXDbContext _db;

    public DriverLocationHub(ICacheService cache, MesterXDbContext db)
    { _cache = cache; _db = db; }

    // Driver pushes their location
    public async Task UpdateLocation(string driverId, double lat, double lng)
    {
        var location = new DriverLocation
        {
            DriverId  = driverId,
            Lat       = lat,
            Lng       = lng,
            UpdatedAt = DateTime.UtcNow
        };

        // Cache driver location (60s TTL)
        await _cache.SetAsync($"driver:loc:{driverId}", location, TimeSpan.FromSeconds(60));

        // Update DB (throttled — every 10s)
        var lastDbUpdate = await _cache.GetAsync<DateTime?>($"driver:db-update:{driverId}");
        if (lastDbUpdate == null || (DateTime.UtcNow - lastDbUpdate.Value).TotalSeconds > 10)
        {
            var driver = await _db.Set<Domain.Entities.Payment.Driver>()
                .FindAsync([Guid.Parse(driverId)]);
            if (driver != null)
            {
                driver.CurrentLat = (decimal)lat;
                driver.CurrentLng = (decimal)lng;
                driver.LastSeenAt = DateTime.UtcNow;
                driver.UpdatedAt  = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            await _cache.SetAsync($"driver:db-update:{driverId}",
                DateTime.UtcNow, TimeSpan.FromSeconds(15));
        }

        // Broadcast to all customers tracking this driver
        await Clients.Group($"driver-track:{driverId}")
            .SendAsync("DriverLocationUpdated", new
            {
                driverId,
                lat,
                lng,
                updatedAt = location.UpdatedAt
            });
    }

    // Customer subscribes to driver location
    public async Task TrackDriver(string driverId, string orderId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"driver-track:{driverId}");

        // Send last known location immediately
        var cached = await _cache.GetAsync<DriverLocation>($"driver:loc:{driverId}");
        if (cached != null)
            await Clients.Caller.SendAsync("DriverLocationUpdated", cached);
    }

    public async Task StopTracking(string driverId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"driver-track:{driverId}");
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  Hub Notifier — called from services to push events
// ──────────────────────────────────────────────────────────────────────────
public interface IHubNotifier
{
    Task NotifyOrderStatusChangedAsync(string orderId, string newStatus, string? note = null);
    Task NotifyStoreNewOrderAsync(string storeId, object orderSummary);
    Task NotifyDriverAssignedAsync(string orderId, string driverId);
}

public class HubNotifier : IHubNotifier
{
    private readonly IHubContext<OrderTrackingHub> _orderHub;

    public HubNotifier(IHubContext<OrderTrackingHub> orderHub)
        => _orderHub = orderHub;

    public async Task NotifyOrderStatusChangedAsync(
        string orderId, string newStatus, string? note = null)
    {
        await _orderHub.Clients.Group($"order-{orderId}")
            .SendAsync("OrderStatusChanged", new
            {
                orderId,
                newStatus,
                note,
                updatedAt = DateTime.UtcNow
            });
    }

    public async Task NotifyStoreNewOrderAsync(string storeId, object orderSummary)
    {
        await _orderHub.Clients.Group($"store-{storeId}")
            .SendAsync("NewOrderReceived", orderSummary);
    }

    public async Task NotifyDriverAssignedAsync(string orderId, string driverId)
    {
        await _orderHub.Clients.Group($"order-{orderId}")
            .SendAsync("DriverAssigned", new { orderId, driverId });
    }
}

// ──────────────────────────────────────────────────────────────────────────
//  Supporting types
// ──────────────────────────────────────────────────────────────────────────
public record DriverLocation
{
    public string   DriverId  { get; init; } = string.Empty;
    public double   Lat       { get; init; }
    public double   Lng       { get; init; }
    public DateTime UpdatedAt { get; init; }
}
