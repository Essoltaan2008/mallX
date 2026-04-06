using MesterX.Application.DTOs;
using MesterX.Application.DTOs.Mall;
using MesterX.Domain.Entities.Mall;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesterX.Application.Services.Mall;

public interface ICartService
{
    Task<ApiResponse<CartDto>> GetCartAsync(Guid customerId, CancellationToken ct = default);
    Task<ApiResponse<CartDto>> AddItemAsync(Guid customerId, Guid mallId, AddToCartRequest req, CancellationToken ct = default);
    Task<ApiResponse<CartDto>> UpdateItemAsync(Guid customerId, UpdateCartItemRequest req, CancellationToken ct = default);
    Task<ApiResponse<CartDto>> RemoveItemAsync(Guid customerId, Guid productId, CancellationToken ct = default);
    Task<ApiResponse>          ClearCartAsync(Guid customerId, CancellationToken ct = default);
}

public class CartService : ICartService
{
    private readonly MesterXDbContext _db;
    private readonly ILogger<CartService> _log;
    private const decimal DELIVERY_FEE = 15m; // يُعدَّل لاحقاً حسب منطقة التوصيل

    public CartService(MesterXDbContext db, ILogger<CartService> log)
    { _db = db; _log = log; }

    // ─── GET CART ─────────────────────────────────────────────────────────
    public async Task<ApiResponse<CartDto>> GetCartAsync(
        Guid customerId, CancellationToken ct = default)
    {
        var cart = await LoadCartAsync(customerId, ct);
        if (cart == null)
            return ApiResponse<CartDto>.Ok(new CartDto
            {
                Id = Guid.Empty, ItemCount = 0, Subtotal = 0, Total = 0, Stores = []
            });

        return ApiResponse<CartDto>.Ok(await MapCartAsync(cart, ct));
    }

    // ─── ADD ITEM ─────────────────────────────────────────────────────────
    public async Task<ApiResponse<CartDto>> AddItemAsync(
        Guid customerId, Guid mallId, AddToCartRequest req, CancellationToken ct = default)
    {
        // Validate product exists + in stock
        var stockItem = await _db.StockItems
            .Include(s => s.Product)
            .FirstOrDefaultAsync(s => s.Product.Id == req.ProductId
                && s.Product.TenantId == req.StoreId
                && s.Product.IsActive
                && !s.Product.IsDeleted, ct);

        if (stockItem == null)
            return ApiResponse<CartDto>.Fail("المنتج غير موجود.");

        if (stockItem.AvailableQuantity < req.Quantity)
            return ApiResponse<CartDto>.Fail(
                $"الكمية المطلوبة ({req.Quantity}) غير متوفرة. المتوفر: {stockItem.AvailableQuantity}");

        // Get or create cart
        var cart = await LoadCartAsync(customerId, ct)
            ?? new Cart { CustomerId = customerId, MallId = mallId };

        if (cart.Id == Guid.Empty)
        {
            _db.Carts.Add(cart);
            await _db.SaveChangesAsync(ct);
        }

        // Check if item already in cart
        var existing = cart.Items.FirstOrDefault(i => i.ProductId == req.ProductId);
        if (existing != null)
        {
            existing.Quantity  = Math.Min(existing.Quantity + req.Quantity, stockItem.AvailableQuantity);
            existing.Notes     = req.Notes ?? existing.Notes;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            cart.Items.Add(new CartItem
            {
                CartId    = cart.Id,
                StoreId   = req.StoreId,
                ProductId = req.ProductId,
                Quantity  = req.Quantity,
                UnitPrice = stockItem.Product.SalePrice,
                Notes     = req.Notes,
                ItemType  = "Product"
            });
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogDebug("Cart updated for customer {CustomerId}: +{Qty} x {ProductId}",
            customerId, req.Quantity, req.ProductId);

        return ApiResponse<CartDto>.Ok(await MapCartAsync(cart, ct));
    }

    // ─── UPDATE ITEM ──────────────────────────────────────────────────────
    public async Task<ApiResponse<CartDto>> UpdateItemAsync(
        Guid customerId, UpdateCartItemRequest req, CancellationToken ct = default)
    {
        var cart = await LoadCartAsync(customerId, ct);
        if (cart == null)
            return ApiResponse<CartDto>.Fail("السلة فارغة.");

        var item = cart.Items.FirstOrDefault(i => i.ProductId == req.ProductId);
        if (item == null)
            return ApiResponse<CartDto>.Fail("المنتج غير موجود في السلة.");

        if (req.Quantity <= 0)
        {
            _db.CartItems.Remove(item);
        }
        else
        {
            item.Quantity  = req.Quantity;
            item.UpdatedAt = DateTime.UtcNow;
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<CartDto>.Ok(await MapCartAsync(cart, ct));
    }

    // ─── REMOVE ITEM ──────────────────────────────────────────────────────
    public async Task<ApiResponse<CartDto>> RemoveItemAsync(
        Guid customerId, Guid productId, CancellationToken ct = default)
    {
        var cart = await LoadCartAsync(customerId, ct);
        if (cart == null)
            return ApiResponse<CartDto>.Ok(new CartDto());

        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            _db.CartItems.Remove(item);
            cart.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return ApiResponse<CartDto>.Ok(await MapCartAsync(cart, ct));
    }

    // ─── CLEAR CART ───────────────────────────────────────────────────────
    public async Task<ApiResponse> ClearCartAsync(Guid customerId, CancellationToken ct = default)
    {
        var cart = await LoadCartAsync(customerId, ct);
        if (cart != null)
        {
            _db.CartItems.RemoveRange(cart.Items);
            await _db.SaveChangesAsync(ct);
        }
        return ApiResponse.Ok();
    }

    // ─── PRIVATE HELPERS ──────────────────────────────────────────────────
    private async Task<Cart?> LoadCartAsync(Guid customerId, CancellationToken ct)
        => await _db.Carts
            .Include(c => c.Items)
                .ThenInclude(i => i.Product)
            .Include(c => c.Items)
                .ThenInclude(i => i.Store)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);

    private async Task<CartDto> MapCartAsync(Cart cart, CancellationToken ct)
    {
        // Load stock availability
        var productIds = cart.Items.Select(i => i.ProductId).ToList();
        var stocks = await _db.StockItems
            .AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, s => s.AvailableQuantity, ct);

        var storeGroups = cart.GroupByStore()
            .Select(g => new CartStoreGroup
            {
                StoreId   = g.Key,
                StoreName = g.First().Store?.Name ?? "المحل",
                StoreType = g.First().Store?.StoreType.ToString() ?? "Retail",
                StoreSubtotal = g.Sum(i => i.LineTotal),
                Items     = g.Select(i => new CartItemDto
                {
                    CartItemId  = i.Id,
                    ProductId   = i.ProductId,
                    ProductName = i.Product?.Name ?? string.Empty,
                    ImageUrl    = i.Product?.ImageUrl,
                    UnitPrice   = i.UnitPrice,
                    Quantity    = i.Quantity,
                    LineTotal   = i.LineTotal,
                    Notes       = i.Notes,
                    InStock     = stocks.GetValueOrDefault(i.ProductId, 0) >= i.Quantity
                }).ToList()
            }).ToList();

        var subtotal = cart.Subtotal;
        var delivery = subtotal > 0 ? DELIVERY_FEE : 0;

        return new CartDto
        {
            Id          = cart.Id,
            MallId      = cart.MallId,
            Stores      = storeGroups,
            Subtotal    = subtotal,
            DeliveryFee = delivery,
            Total       = subtotal + delivery,
            ItemCount   = cart.TotalItems
        };
    }
}
