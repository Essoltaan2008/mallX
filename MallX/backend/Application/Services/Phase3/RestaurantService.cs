using MesterX.Application.DTOs;
using MesterX.Domain.Entities.Phase3;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesterX.Application.Services.Phase3;

// ──────────────────────────────────────────────────────────────────────────
//  DTOs
// ──────────────────────────────────────────────────────────────────────────
public record MenuCategoryDto(Guid Id, string Name, string? NameAr, string? Icon, int SortOrder, List<MenuItemDto> Items);
public record MenuItemDto
{
    public Guid     Id          { get; init; }
    public string   Name        { get; init; } = string.Empty;
    public string?  NameAr      { get; init; }
    public string?  Description { get; init; }
    public decimal  Price       { get; init; }
    public string?  ImageUrl    { get; init; }
    public int      PrepTimeMin { get; init; }
    public int?     Calories    { get; init; }
    public bool     IsAvailable { get; init; }
    public bool     IsFeatured  { get; init; }
    public string[] Tags        { get; init; } = [];
    public List<MenuItemOptionDto> Options { get; init; } = [];
}
public record MenuItemOptionDto(Guid Id, string Name, bool IsRequired, string ChoicesJson);

public record CreateMenuItemRequest(
    Guid? CategoryId, string Name, string? NameAr,
    string? Description, decimal Price, string? ImageUrl,
    int PrepTimeMin, int? Calories, bool IsFeatured, string[]? Tags
);
public record UpdateMenuItemRequest(
    string? Name, string? NameAr, string? Description,
    decimal? Price, bool? IsAvailable, bool? IsFeatured
);

public record QueueTicketDto
{
    public Guid   Id             { get; init; }
    public int    TicketNumber   { get; init; }
    public string Status         { get; init; } = string.Empty;
    public string StatusAr       { get; init; } = string.Empty;
    public DateTime? EstimatedReady { get; init; }
    public int    WaitingAhead   { get; init; }   // كم طلب قبلك
    public string OrderNumber    { get; init; } = string.Empty;
}

public record RestaurantQueueDto
{
    public int CurrentServing    { get; init; }
    public int TotalWaiting      { get; init; }
    public int AvgPrepTimeMins   { get; init; }
    public List<QueueTicketDto> Tickets { get; init; } = [];
}

// ──────────────────────────────────────────────────────────────────────────
//  SERVICE
// ──────────────────────────────────────────────────────────────────────────
public interface IRestaurantService
{
    // Customer
    Task<ApiResponse<List<MenuCategoryDto>>> GetMenuAsync(Guid storeId, CancellationToken ct = default);
    Task<ApiResponse<QueueTicketDto>>        GetTicketStatusAsync(Guid ticketId, CancellationToken ct = default);

    // Store Owner
    Task<ApiResponse<MenuItemDto>>  AddMenuItemAsync(Guid storeId, CreateMenuItemRequest req, CancellationToken ct = default);
    Task<ApiResponse<MenuItemDto>>  UpdateMenuItemAsync(Guid storeId, Guid itemId, UpdateMenuItemRequest req, CancellationToken ct = default);
    Task<ApiResponse>               ToggleAvailabilityAsync(Guid storeId, Guid itemId, CancellationToken ct = default);
    Task<ApiResponse>               DeleteMenuItemAsync(Guid storeId, Guid itemId, CancellationToken ct = default);
    Task<ApiResponse<RestaurantQueueDto>> GetQueueAsync(Guid storeId, CancellationToken ct = default);
    Task<ApiResponse>               AdvanceTicketAsync(Guid storeId, Guid ticketId, CancellationToken ct = default);
    Task<ApiResponse<QueueTicketDto>> CreateQueueTicketAsync(Guid storeOrderId, CancellationToken ct = default);
}

public class RestaurantService : IRestaurantService
{
    private readonly MesterXDbContext _db;
    private readonly ILogger<RestaurantService> _log;

    public RestaurantService(MesterXDbContext db, ILogger<RestaurantService> log)
    { _db = db; _log = log; }

    // ─── GET MENU ─────────────────────────────────────────────────────────
    public async Task<ApiResponse<List<MenuCategoryDto>>> GetMenuAsync(
        Guid storeId, CancellationToken ct = default)
    {
        var categories = await _db.Set<MenuCategory>()
            .AsNoTracking()
            .Include(c => c.Items.Where(i => i.IsAvailable && !i.IsDeleted))
                .ThenInclude(i => i.Options)
            .Where(c => c.StoreId == storeId && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(ct);

        // Items without category
        var uncategorized = await _db.Set<MenuItem>()
            .AsNoTracking()
            .Include(i => i.Options)
            .Where(i => i.StoreId == storeId && i.CategoryId == null
                && i.IsAvailable && !i.IsDeleted)
            .ToListAsync(ct);

        var result = categories.Select(MapCategory).ToList();

        if (uncategorized.Any())
            result.Add(new MenuCategoryDto(
                Guid.Empty, "أخرى", null, null, 999,
                uncategorized.Select(MapItem).ToList()));

        return ApiResponse<List<MenuCategoryDto>>.Ok(result);
    }

    // ─── GET TICKET STATUS ────────────────────────────────────────────────
    public async Task<ApiResponse<QueueTicketDto>> GetTicketStatusAsync(
        Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await _db.Set<QueueTicket>()
            .AsNoTracking()
            .Include(t => t.StoreOrder).ThenInclude(so => so.MallOrder)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct);

        if (ticket == null)
            return ApiResponse<QueueTicketDto>.Fail("التذكرة غير موجودة.");

        var waitingAhead = await _db.Set<QueueTicket>()
            .CountAsync(t => t.StoreId == ticket.StoreId
                && t.Status == TicketStatus.Waiting
                && t.TicketNumber < ticket.TicketNumber, ct);

        return ApiResponse<QueueTicketDto>.Ok(MapTicket(ticket, waitingAhead));
    }

    // ─── ADD MENU ITEM ────────────────────────────────────────────────────
    public async Task<ApiResponse<MenuItemDto>> AddMenuItemAsync(
        Guid storeId, CreateMenuItemRequest req, CancellationToken ct = default)
    {
        var item = new MenuItem
        {
            StoreId     = storeId,
            CategoryId  = req.CategoryId,
            Name        = req.Name.Trim(),
            NameAr      = req.NameAr?.Trim(),
            Description = req.Description,
            Price       = req.Price,
            ImageUrl    = req.ImageUrl,
            PrepTimeMin = req.PrepTimeMin,
            Calories    = req.Calories,
            IsFeatured  = req.IsFeatured,
            Tags        = req.Tags ?? [],
        };
        _db.Set<MenuItem>().Add(item);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Menu item added: {Name} for store {StoreId}", item.Name, storeId);
        return ApiResponse<MenuItemDto>.Ok(MapItem(item));
    }

    // ─── UPDATE MENU ITEM ─────────────────────────────────────────────────
    public async Task<ApiResponse<MenuItemDto>> UpdateMenuItemAsync(
        Guid storeId, Guid itemId, UpdateMenuItemRequest req, CancellationToken ct = default)
    {
        var item = await _db.Set<MenuItem>()
            .Include(i => i.Options)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.StoreId == storeId, ct);

        if (item == null) return ApiResponse<MenuItemDto>.Fail("الصنف غير موجود.");

        if (req.Name        != null) item.Name        = req.Name;
        if (req.NameAr      != null) item.NameAr      = req.NameAr;
        if (req.Description != null) item.Description = req.Description;
        if (req.Price       != null) item.Price        = req.Price.Value;
        if (req.IsAvailable != null) item.IsAvailable  = req.IsAvailable.Value;
        if (req.IsFeatured  != null) item.IsFeatured   = req.IsFeatured.Value;
        item.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return ApiResponse<MenuItemDto>.Ok(MapItem(item));
    }

    // ─── TOGGLE AVAILABILITY ──────────────────────────────────────────────
    public async Task<ApiResponse> ToggleAvailabilityAsync(
        Guid storeId, Guid itemId, CancellationToken ct = default)
    {
        var item = await _db.Set<MenuItem>()
            .FirstOrDefaultAsync(i => i.Id == itemId && i.StoreId == storeId, ct);
        if (item == null) return ApiResponse.Fail("الصنف غير موجود.");

        item.IsAvailable = !item.IsAvailable;
        item.UpdatedAt   = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ApiResponse.Ok($"الصنف أصبح {(item.IsAvailable ? "متاحاً" : "غير متاح")}");
    }

    // ─── DELETE MENU ITEM (soft) ──────────────────────────────────────────
    public async Task<ApiResponse> DeleteMenuItemAsync(
        Guid storeId, Guid itemId, CancellationToken ct = default)
    {
        var item = await _db.Set<MenuItem>()
            .FirstOrDefaultAsync(i => i.Id == itemId && i.StoreId == storeId, ct);
        if (item == null) return ApiResponse.Fail("الصنف غير موجود.");

        item.IsDeleted  = true;
        item.UpdatedAt  = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok();
    }

    // ─── GET QUEUE (Store Owner view) ─────────────────────────────────────
    public async Task<ApiResponse<RestaurantQueueDto>> GetQueueAsync(
        Guid storeId, CancellationToken ct = default)
    {
        var tickets = await _db.Set<QueueTicket>()
            .AsNoTracking()
            .Include(t => t.StoreOrder).ThenInclude(so => so.MallOrder)
            .Where(t => t.StoreId == storeId
                && t.Status != TicketStatus.Collected
                && t.Status != TicketStatus.Cancelled
                && t.CreatedAt.Date == DateTime.UtcNow.Date)
            .OrderBy(t => t.TicketNumber)
            .ToListAsync(ct);

        var waiting  = tickets.Where(t => t.Status == TicketStatus.Waiting).ToList();
        var avgPrep  = tickets.Any()
            ? (int)tickets.Average(t => (t.ReadyAt ?? DateTime.UtcNow)
                .Subtract(t.CreatedAt).TotalMinutes) : 15;

        return ApiResponse<RestaurantQueueDto>.Ok(new RestaurantQueueDto
        {
            CurrentServing  = tickets.FirstOrDefault(t => t.Status == TicketStatus.Preparing)?.TicketNumber ?? 0,
            TotalWaiting    = waiting.Count,
            AvgPrepTimeMins = Math.Max(5, avgPrep),
            Tickets         = tickets.Select((t, i) => MapTicket(t,
                waiting.IndexOf(waiting.FirstOrDefault(w => w.Id == t.Id)!))).ToList()
        });
    }

    // ─── ADVANCE TICKET STATUS ────────────────────────────────────────────
    public async Task<ApiResponse> AdvanceTicketAsync(
        Guid storeId, Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await _db.Set<QueueTicket>()
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.StoreId == storeId, ct);
        if (ticket == null) return ApiResponse.Fail("التذكرة غير موجودة.");

        ticket.Status = ticket.Status switch
        {
            TicketStatus.Waiting   => TicketStatus.Preparing,
            TicketStatus.Preparing => TicketStatus.Ready,
            TicketStatus.Ready     => TicketStatus.Collected,
            _ => ticket.Status
        };

        if (ticket.Status == TicketStatus.Ready)    ticket.ReadyAt     = DateTime.UtcNow;
        if (ticket.Status == TicketStatus.Collected) ticket.CollectedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok(ticket.Status.ToString());
    }

    // ─── CREATE QUEUE TICKET (called after StoreOrder created) ────────────
    public async Task<ApiResponse<QueueTicketDto>> CreateQueueTicketAsync(
        Guid storeOrderId, CancellationToken ct = default)
    {
        var storeOrder = await _db.StoreOrders
            .Include(so => so.MallOrder)
            .FirstOrDefaultAsync(so => so.Id == storeOrderId, ct);
        if (storeOrder == null) return ApiResponse<QueueTicketDto>.Fail("الطلب غير موجود.");

        // Atomic ticket number generation
        var today  = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var counter = await _db.Set<QueueDailyCounter>()
            .FirstOrDefaultAsync(c => c.StoreId == storeOrder.StoreId && c.Date == today, ct);

        if (counter == null)
        {
            counter = new QueueDailyCounter { StoreId = storeOrder.StoreId, Date = today, LastTicket = 0 };
            _db.Set<QueueDailyCounter>().Add(counter);
        }
        counter.LastTicket++;

        // Estimate ready time
        var avgPrep = 15;
        var queueSize = await _db.Set<QueueTicket>()
            .CountAsync(t => t.StoreId == storeOrder.StoreId
                && t.Status == TicketStatus.Waiting, ct);
        var estimatedReady = DateTime.UtcNow.AddMinutes(avgPrep * (queueSize + 1));

        var ticket = new QueueTicket
        {
            StoreOrderId   = storeOrderId,
            StoreId        = storeOrder.StoreId,
            TicketNumber   = counter.LastTicket,
            Status         = TicketStatus.Waiting,
            EstimatedReady = estimatedReady,
        };
        _db.Set<QueueTicket>().Add(ticket);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Queue ticket #{Num} created for store {StoreId}",
            ticket.TicketNumber, storeOrder.StoreId);

        return ApiResponse<QueueTicketDto>.Ok(MapTicket(ticket, queueSize));
    }

    // ─── MAPPERS ──────────────────────────────────────────────────────────
    private static MenuCategoryDto MapCategory(MenuCategory c) => new(
        c.Id, c.Name, c.NameAr, c.Icon, c.SortOrder,
        c.Items.OrderBy(i => i.SortOrder).Select(MapItem).ToList());

    private static MenuItemDto MapItem(MenuItem i) => new()
    {
        Id = i.Id, Name = i.Name, NameAr = i.NameAr, Description = i.Description,
        Price = i.Price, ImageUrl = i.ImageUrl, PrepTimeMin = i.PrepTimeMin,
        Calories = i.Calories, IsAvailable = i.IsAvailable, IsFeatured = i.IsFeatured,
        Tags = i.Tags ?? [],
        Options = i.Options.Select(o => new MenuItemOptionDto(
            o.Id, o.Name, o.IsRequired, o.Choices)).ToList()
    };

    private static string TicketStatusAr(TicketStatus s) => s switch
    {
        TicketStatus.Waiting   => "في الانتظار",
        TicketStatus.Preparing => "قيد التحضير",
        TicketStatus.Ready     => "جاهز للاستلام!",
        TicketStatus.Collected => "تم الاستلام",
        TicketStatus.Cancelled => "ملغى",
        _ => s.ToString()
    };

    private static QueueTicketDto MapTicket(QueueTicket t, int waitingAhead) => new()
    {
        Id             = t.Id,
        TicketNumber   = t.TicketNumber,
        Status         = t.Status.ToString(),
        StatusAr       = TicketStatusAr(t.Status),
        EstimatedReady = t.EstimatedReady,
        WaitingAhead   = Math.Max(0, waitingAhead),
        OrderNumber    = t.StoreOrder?.MallOrder?.OrderNumber ?? string.Empty
    };
}

// Helper entity (not full domain, just for EF)
public class QueueDailyCounter
{
    public Guid StoreId      { get; set; }
    public DateOnly Date     { get; set; }
    public int LastTicket    { get; set; } = 0;
}
