using MesterX.Application.DTOs;
using MesterX.Application.DTOs.Mall;
using MesterX.Domain.Entities.Mall;
using MesterX.Domain.Entities.Pos;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesterX.Application.Services.Mall;

public interface IMallOrderService
{
    // Customer
    Task<ApiResponse<MallOrderDto>>        CheckoutAsync(Guid customerId, CheckoutRequest req, CancellationToken ct = default);
    Task<ApiResponse<MallOrderDto>>        GetOrderAsync(Guid customerId, Guid orderId, CancellationToken ct = default);
    Task<ApiResponse<List<MallOrderDto>>>  GetOrderHistoryAsync(Guid customerId, int page, int size, CancellationToken ct = default);

    // Store Owner
    Task<ApiResponse<List<IncomingStoreOrderDto>>> GetIncomingAsync(Guid storeId, CancellationToken ct = default);
    Task<ApiResponse>                              UpdateStoreOrderStatusAsync(Guid storeId, Guid storeOrderId, UpdateStoreOrderStatusRequest req, CancellationToken ct = default);

    // Mall Admin
    Task<ApiResponse<MallAdminDashboardDto>> GetAdminDashboardAsync(Guid mallId, CancellationToken ct = default);
}

public class MallOrderService : IMallOrderService
{
    private readonly MesterXDbContext _db;
    private readonly ILogger<MallOrderService> _log;

    public MallOrderService(MesterXDbContext db, ILogger<MallOrderService> log)
    { _db = db; _log = log; }

    // ══════════════════════════════════════════════════════════════════════
    //  CHECKOUT — قلب المنصة: Cart → MallOrder → StoreOrders
    // ══════════════════════════════════════════════════════════════════════
    public async Task<ApiResponse<MallOrderDto>> CheckoutAsync(
        Guid customerId, CheckoutRequest req, CancellationToken ct = default)
    {
        // 1. Load cart with all relations
        var cart = await _db.Carts
            .Include(c => c.Items).ThenInclude(i => i.Product)
            .Include(c => c.Items).ThenInclude(i => i.Store)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);

        if (cart == null || cart.IsEmpty)
            return ApiResponse<MallOrderDto>.Fail("السلة فارغة.");

        // 2. Validate stock availability for all items
        var productIds = cart.Items.Select(i => i.ProductId).ToList();
        var stocks = await _db.StockItems
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, ct);

        var stockErrors = cart.Items
            .Where(i => !stocks.ContainsKey(i.ProductId)
                || stocks[i.ProductId].AvailableQuantity < i.Quantity)
            .Select(i => i.Product.Name)
            .ToList();

        if (stockErrors.Any())
            return ApiResponse<MallOrderDto>.Fail(
                $"المنتجات التالية غير متوفرة بالكمية المطلوبة: {string.Join(", ", stockErrors)}");

        // 3. Parse enums
        if (!Enum.TryParse<FulfillmentType>(req.FulfillmentType, out var fulfillment))
            return ApiResponse<MallOrderDto>.Fail("نوع التسليم غير صالح.");

        if (!Enum.TryParse<PaymentMethod>(req.PaymentMethod, out var payment))
            return ApiResponse<MallOrderDto>.Fail("طريقة الدفع غير صالحة.");

        using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var subtotal    = cart.Subtotal;
            var deliveryFee = fulfillment == FulfillmentType.Delivery ? 15m : 0m;

            // 4. Create MallOrder
            var mallOrder = new MallOrder
            {
                CustomerId      = customerId,
                MallId          = cart.MallId,
                OrderNumber     = await GenerateOrderNumberAsync(ct),
                Status          = MallOrderStatus.Placed,
                FulfillmentType = fulfillment,
                Subtotal        = subtotal,
                DeliveryFee     = deliveryFee,
                Total           = subtotal + deliveryFee,
                PaymentMethod   = payment,
                DeliveryAddress = req.DeliveryAddress,
                DeliveryLat     = req.DeliveryLat,
                DeliveryLng     = req.DeliveryLng,
                Notes           = req.Notes,
            };
            _db.MallOrders.Add(mallOrder);
            await _db.SaveChangesAsync(ct);

            // 5. Split cart by store → StoreOrders
            foreach (var storeGroup in cart.GroupByStore())
            {
                var store     = storeGroup.First().Store;
                var storeRate = store?.Commission ?? 0.05m;
                var storeSub  = storeGroup.Sum(i => i.LineTotal);
                var commission= Math.Round(storeSub * storeRate, 2);

                var storeOrder = new StoreOrder
                {
                    MallOrderId    = mallOrder.Id,
                    StoreId        = storeGroup.Key,
                    Status         = StoreOrderStatus.Placed,
                    Subtotal       = storeSub,
                    CommissionRate = storeRate,
                    CommissionAmt  = commission,
                    StoreTotal     = storeSub - commission,
                    Items          = storeGroup.Select(i => new StoreOrderItem
                    {
                        ProductId   = i.ProductId,
                        ProductName = i.Product.Name,
                        Quantity    = i.Quantity,
                        UnitPrice   = i.UnitPrice,
                        Notes       = i.Notes,
                        Total       = i.LineTotal
                    }).ToList()
                };
                _db.StoreOrders.Add(storeOrder);

                // 6. Deduct stock per branch (use first available branch for now)
                foreach (var item in storeGroup)
                {
                    if (stocks.TryGetValue(item.ProductId, out var stockItem))
                    {
                        stockItem.Quantity -= item.Quantity;
                        stockItem.UpdatedAt = DateTime.UtcNow;

                        _db.StockMovements.Add(new StockMovement
                        {
                            TenantId       = storeGroup.Key,
                            BranchId       = stockItem.BranchId,
                            ProductId      = item.ProductId,
                            MovementType   = StockMovementType.Sale,
                            Quantity       = -item.Quantity,
                            BeforeQuantity = stockItem.Quantity + item.Quantity,
                            AfterQuantity  = stockItem.Quantity,
                            ReferenceId    = mallOrder.Id,
                            ReferenceType  = "MallOrder"
                        });
                    }
                }
            }

            // 7. Record status history
            _db.OrderStatusHistory.Add(new OrderStatusHistory
            {
                MallOrderId = mallOrder.Id,
                NewStatus   = mallOrder.Status.ToString(),
                Note        = "تم استلام الطلب"
            });

            // 8. Clear cart
            _db.CartItems.RemoveRange(cart.Items);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _log.LogInformation("MallOrder {OrderNumber} created — {StoreCount} stores, Total: {Total} EGP",
                mallOrder.OrderNumber, mallOrder.StoreOrders.Count, mallOrder.Total);

            return ApiResponse<MallOrderDto>.Ok(await GetOrderDtoAsync(mallOrder.Id, ct));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _log.LogError(ex, "Checkout failed for customer {CustomerId}", customerId);
            return ApiResponse<MallOrderDto>.Fail("حدث خطأ أثناء إتمام الطلب. حاول مجدداً.");
        }
    }

    // ─── GET ORDER ────────────────────────────────────────────────────────
    public async Task<ApiResponse<MallOrderDto>> GetOrderAsync(
        Guid customerId, Guid orderId, CancellationToken ct = default)
    {
        var order = await _db.MallOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == customerId, ct);

        if (order == null)
            return ApiResponse<MallOrderDto>.Fail("الطلب غير موجود.");

        return ApiResponse<MallOrderDto>.Ok(await GetOrderDtoAsync(orderId, ct));
    }

    // ─── ORDER HISTORY ────────────────────────────────────────────────────
    public async Task<ApiResponse<List<MallOrderDto>>> GetOrderHistoryAsync(
        Guid customerId, int page, int size, CancellationToken ct = default)
    {
        var orderIds = await _db.MallOrders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.PlacedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(o => o.Id)
            .ToListAsync(ct);

        var dtos = new List<MallOrderDto>();
        foreach (var id in orderIds)
            dtos.Add(await GetOrderDtoAsync(id, ct));

        return ApiResponse<List<MallOrderDto>>.Ok(dtos);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  STORE OWNER — Incoming Orders Dashboard
    // ══════════════════════════════════════════════════════════════════════
    public async Task<ApiResponse<List<IncomingStoreOrderDto>>> GetIncomingAsync(
        Guid storeId, CancellationToken ct = default)
    {
        var storeOrders = await _db.StoreOrders
            .AsNoTracking()
            .Include(so => so.MallOrder).ThenInclude(mo => mo.Customer)
            .Include(so => so.Items)
            .Where(so => so.StoreId == storeId
                && so.Status != StoreOrderStatus.Cancelled)
            .OrderByDescending(so => so.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var dtos = storeOrders.Select(so => new IncomingStoreOrderDto
        {
            Id              = so.Id,
            OrderNumber     = so.MallOrder.OrderNumber,
            Status          = so.Status.ToString(),
            CustomerName    = so.MallOrder.Customer.FullName,
            CustomerPhone   = so.MallOrder.Customer.Phone,
            FulfillmentType = so.MallOrder.FulfillmentType.ToString(),
            Total           = so.Subtotal,
            PlacedAt        = so.CreatedAt,
            Notes           = so.Notes,
            Items           = so.Items.Select(i => new StoreOrderItemDto
            {
                ProductName = i.ProductName,
                Quantity    = i.Quantity,
                UnitPrice   = i.UnitPrice,
                Total       = i.Total,
                Notes       = i.Notes
            }).ToList()
        }).ToList();

        return ApiResponse<List<IncomingStoreOrderDto>>.Ok(dtos);
    }

    // ─── UPDATE STORE ORDER STATUS ────────────────────────────────────────
    public async Task<ApiResponse> UpdateStoreOrderStatusAsync(
        Guid storeId, Guid storeOrderId, UpdateStoreOrderStatusRequest req,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<StoreOrderStatus>(req.Status, out var newStatus))
            return ApiResponse.Fail("حالة غير صالحة.");

        var storeOrder = await _db.StoreOrders
            .Include(so => so.MallOrder).ThenInclude(mo => mo.StoreOrders)
            .FirstOrDefaultAsync(so => so.Id == storeOrderId && so.StoreId == storeId, ct);

        if (storeOrder == null)
            return ApiResponse.Fail("الطلب غير موجود.");

        storeOrder.Status    = newStatus;
        storeOrder.UpdatedAt = DateTime.UtcNow;

        if (newStatus == StoreOrderStatus.Confirmed) storeOrder.ConfirmedAt = DateTime.UtcNow;
        if (newStatus == StoreOrderStatus.Ready)     storeOrder.ReadyAt     = DateTime.UtcNow;

        // Sync MallOrder status if all stores agree
        var allStatuses = storeOrder.MallOrder.StoreOrders.Select(s =>
            s.Id == storeOrderId ? newStatus : s.Status).ToList();

        if (allStatuses.All(s => s == StoreOrderStatus.Ready))
            storeOrder.MallOrder.Status = MallOrderStatus.Ready;
        else if (allStatuses.Any(s => s == StoreOrderStatus.Preparing))
            storeOrder.MallOrder.Status = MallOrderStatus.Preparing;
        else if (allStatuses.All(s => s == StoreOrderStatus.Confirmed))
            storeOrder.MallOrder.Status = MallOrderStatus.Confirmed;

        _db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            MallOrderId   = storeOrder.MallOrderId,
            StoreOrderId  = storeOrder.Id,
            OldStatus     = storeOrder.Status.ToString(),
            NewStatus     = newStatus.ToString(),
            Note          = req.Note
        });

        storeOrder.MallOrder.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MALL ADMIN DASHBOARD
    // ══════════════════════════════════════════════════════════════════════
    public async Task<ApiResponse<MallAdminDashboardDto>> GetAdminDashboardAsync(
        Guid mallId, CancellationToken ct = default)
    {
        var now       = DateTime.UtcNow;
        var monthStart= new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var orders = await _db.MallOrders
            .AsNoTracking()
            .Where(o => o.MallId == mallId && o.PlacedAt >= monthStart)
            .ToListAsync(ct);

        var commissions = await _db.StoreOrders
            .AsNoTracking()
            .Include(so => so.MallOrder)
            .Include(so => so.Store)
            .Where(so => so.MallOrder.MallId == mallId && so.CreatedAt >= monthStart)
            .ToListAsync(ct);

        var topStores = commissions
            .GroupBy(so => new { so.StoreId, so.Store.Name })
            .Select(g => new TopStoreDto
            {
                StoreId    = g.Key.StoreId,
                StoreName  = g.Key.Name,
                Revenue    = g.Sum(s => s.Subtotal),
                Orders     = g.Count(),
                Commission = g.Sum(s => s.CommissionAmt)
            })
            .OrderByDescending(s => s.Revenue)
            .Take(5)
            .ToList();

        return ApiResponse<MallAdminDashboardDto>.Ok(new MallAdminDashboardDto
        {
            TotalRevenue    = orders.Sum(o => o.Total),
            TotalCommission = commissions.Sum(c => c.CommissionAmt),
            TotalOrders     = orders.Count,
            ActiveStores    = await _db.Tenants.CountAsync(t => t.MallId == mallId && t.IsActive, ct),
            TotalCustomers  = await _db.MallCustomers.CountAsync(c => c.MallId == mallId && !c.IsDeleted, ct),
            TopStores       = topStores
        });
    }

    // ─── PRIVATE HELPERS ──────────────────────────────────────────────────
    private async Task<string> GenerateOrderNumberAsync(CancellationToken ct)
    {
        var today  = DateTime.UtcNow.ToString("yyyyMMdd");
        var count  = await _db.MallOrders.CountAsync(
            o => o.PlacedAt.Date == DateTime.UtcNow.Date, ct);
        return $"MX-{today}-{(count + 1):D4}";
    }

    private async Task<MallOrderDto> GetOrderDtoAsync(Guid orderId, CancellationToken ct)
    {
        var order = await _db.MallOrders
            .AsNoTracking()
            .Include(o => o.StoreOrders).ThenInclude(so => so.Items)
            .Include(o => o.StoreOrders).ThenInclude(so => so.Store)
            .Include(o => o.StatusHistory)
            .FirstAsync(o => o.Id == orderId, ct);

        return new MallOrderDto
        {
            Id              = order.Id,
            OrderNumber     = order.OrderNumber,
            Status          = order.Status.ToString(),
            FulfillmentType = order.FulfillmentType.ToString(),
            Subtotal        = order.Subtotal,
            DeliveryFee     = order.DeliveryFee,
            Total           = order.Total,
            PaymentMethod   = order.PaymentMethod.ToString(),
            DeliveryAddress = order.DeliveryAddress,
            PlacedAt        = order.PlacedAt,
            DeliveredAt     = order.DeliveredAt,
            StoreOrders     = order.StoreOrders.Select(so => new StoreOrderDto
            {
                Id          = so.Id,
                StoreId     = so.StoreId,
                StoreName   = so.Store?.Name ?? string.Empty,
                Status      = so.Status.ToString(),
                Subtotal    = so.Subtotal,
                StoreTotal  = so.StoreTotal,
                ConfirmedAt = so.ConfirmedAt,
                ReadyAt     = so.ReadyAt,
                Items       = so.Items.Select(i => new StoreOrderItemDto
                {
                    ProductName = i.ProductName,
                    Quantity    = i.Quantity,
                    UnitPrice   = i.UnitPrice,
                    Total       = i.Total,
                    Notes       = i.Notes
                }).ToList()
            }).ToList(),
            Timeline = order.StatusHistory
                .OrderBy(h => h.CreatedAt)
                .Select(h => new OrderStatusHistoryDto
                {
                    NewStatus = h.NewStatus,
                    Note      = h.Note,
                    CreatedAt = h.CreatedAt
                }).ToList()
        };
    }
}
