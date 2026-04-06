using MesterX.Domain.Entities.Mall;
using MesterX.Application.DTOs;

namespace MesterX.Application.DTOs.Mall;

// ══════════════════════════════════════════════════
//  CUSTOMER AUTH DTOs
// ══════════════════════════════════════════════════

public record CustomerRegisterRequest(
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    string Password,
    string MallSlug
);

public record CustomerLoginRequest(
    string Email,
    string Password,
    string MallSlug
);

public record CustomerAuthResponse
{
    public string AccessToken  { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime ExpiresAt  { get; init; }
    public CustomerProfileDto Customer { get; init; } = null!;
}

public record CustomerProfileDto
{
    public Guid   Id            { get; init; }
    public string FirstName     { get; init; } = string.Empty;
    public string LastName      { get; init; } = string.Empty;
    public string Email         { get; init; } = string.Empty;
    public string? Phone        { get; init; }
    public string? AvatarUrl    { get; init; }
    public int    LoyaltyPoints { get; init; }
    public string Tier          { get; init; } = string.Empty;
    public int    PointsToNext  { get; init; }   // نقاط للـ tier التالي
    public Guid   MallId        { get; init; }

    public static CustomerProfileDto From(MallCustomer c) => new()
    {
        Id            = c.Id,
        FirstName     = c.FirstName,
        LastName      = c.LastName,
        Email         = c.Email,
        Phone         = c.Phone,
        AvatarUrl     = c.AvatarUrl,
        LoyaltyPoints = c.LoyaltyPoints,
        Tier          = c.Tier.ToString(),
        PointsToNext  = c.Tier switch
        {
            CustomerTier.Bronze => 1000 - c.LoyaltyPoints,
            CustomerTier.Silver => 5000 - c.LoyaltyPoints,
            _                   => 0
        },
        MallId = c.MallId
    };
}

// ══════════════════════════════════════════════════
//  MALL DTOs
// ══════════════════════════════════════════════════

public record MallDto
{
    public Guid    Id          { get; init; }
    public string  Name        { get; init; } = string.Empty;
    public string? NameAr      { get; init; }
    public string? Address     { get; init; }
    public string? LogoUrl     { get; init; }
    public string? CoverUrl    { get; init; }
    public string? Phone       { get; init; }
    public decimal? GeoLat     { get; init; }
    public decimal? GeoLng     { get; init; }
    public int     GeoRadiusM  { get; init; }
    public int     TotalStores { get; init; }
}

public record StoreDto
{
    public Guid    Id          { get; init; }
    public string  Name        { get; init; } = string.Empty;
    public string  StoreType   { get; init; } = string.Empty;
    public int     Floor       { get; init; }
    public string? LogoUrl     { get; init; }
    public string? Phone       { get; init; }
    public bool    IsOpen      { get; init; }
    public double  Rating      { get; init; }
    public int     ReviewCount { get; init; }
}

// ══════════════════════════════════════════════════
//  CART DTOs
// ══════════════════════════════════════════════════

public record AddToCartRequest(
    Guid   ProductId,
    Guid   StoreId,
    int    Quantity,
    string? Notes
);

public record UpdateCartItemRequest(
    Guid   ProductId,
    int    Quantity
);

public record CartDto
{
    public Guid             Id         { get; init; }
    public Guid             MallId     { get; init; }
    public List<CartStoreGroup> Stores { get; init; } = [];
    public decimal          Subtotal   { get; init; }
    public decimal          DeliveryFee{ get; init; }
    public decimal          Total      { get; init; }
    public int              ItemCount  { get; init; }
}

public record CartStoreGroup
{
    public Guid              StoreId   { get; init; }
    public string            StoreName { get; init; } = string.Empty;
    public string            StoreType { get; init; } = string.Empty;
    public List<CartItemDto> Items     { get; init; } = [];
    public decimal           StoreSubtotal { get; init; }
}

public record CartItemDto
{
    public Guid    CartItemId  { get; init; }
    public Guid    ProductId   { get; init; }
    public string  ProductName { get; init; } = string.Empty;
    public string? ImageUrl    { get; init; }
    public decimal UnitPrice   { get; init; }
    public int     Quantity    { get; init; }
    public decimal LineTotal   { get; init; }
    public string? Notes       { get; init; }
    public bool    InStock     { get; init; }
}

// ══════════════════════════════════════════════════
//  ORDER DTOs
// ══════════════════════════════════════════════════

public record CheckoutRequest(
    string FulfillmentType,   // Delivery | Pickup | InStore
    string PaymentMethod,     // Cash | Card | EWallet | LoyaltyPoints
    string? DeliveryAddress,
    decimal? DeliveryLat,
    decimal? DeliveryLng,
    string? Notes,
    Guid? AddressId           // من القائمة المحفوظة
);

public record MallOrderDto
{
    public Guid              Id              { get; init; }
    public string            OrderNumber     { get; init; } = string.Empty;
    public string            Status          { get; init; } = string.Empty;
    public string            FulfillmentType { get; init; } = string.Empty;
    public decimal           Subtotal        { get; init; }
    public decimal           DeliveryFee     { get; init; }
    public decimal           Total           { get; init; }
    public string            PaymentMethod   { get; init; } = string.Empty;
    public string?           DeliveryAddress { get; init; }
    public DateTime          PlacedAt        { get; init; }
    public DateTime?         DeliveredAt     { get; init; }
    public List<StoreOrderDto> StoreOrders   { get; init; } = [];
    public List<OrderStatusHistoryDto> Timeline { get; init; } = [];
}

public record StoreOrderDto
{
    public Guid              Id         { get; init; }
    public Guid              StoreId    { get; init; }
    public string            StoreName  { get; init; } = string.Empty;
    public string            Status     { get; init; } = string.Empty;
    public decimal           Subtotal   { get; init; }
    public decimal           StoreTotal { get; init; }
    public List<StoreOrderItemDto> Items { get; init; } = [];
    public DateTime?         ConfirmedAt { get; init; }
    public DateTime?         ReadyAt     { get; init; }
}

public record StoreOrderItemDto
{
    public string  ProductName { get; init; } = string.Empty;
    public int     Quantity    { get; init; }
    public decimal UnitPrice   { get; init; }
    public decimal Total       { get; init; }
    public string? Notes       { get; init; }
}

public record OrderStatusHistoryDto
{
    public string   NewStatus  { get; init; } = string.Empty;
    public string?  Note       { get; init; }
    public DateTime CreatedAt  { get; init; }
}

// Store Dashboard — incoming order view
public record IncomingStoreOrderDto
{
    public Guid              Id           { get; init; }
    public string            OrderNumber  { get; init; } = string.Empty;
    public string            Status       { get; init; } = string.Empty;
    public string            CustomerName { get; init; } = string.Empty;
    public string?           CustomerPhone{ get; init; }
    public string            FulfillmentType { get; init; } = string.Empty;
    public decimal           Total        { get; init; }
    public List<StoreOrderItemDto> Items  { get; init; } = [];
    public DateTime          PlacedAt     { get; init; }
    public string?           Notes        { get; init; }
}

public record UpdateStoreOrderStatusRequest(string Status, string? Note);

// Mall Admin dashboard
public record MallAdminDashboardDto
{
    public decimal TotalRevenue      { get; init; }
    public decimal TotalCommission   { get; init; }
    public int     TotalOrders       { get; init; }
    public int     ActiveStores      { get; init; }
    public int     TotalCustomers    { get; init; }
    public List<TopStoreDto> TopStores { get; init; } = [];
}

public record TopStoreDto
{
    public Guid    StoreId   { get; init; }
    public string  StoreName { get; init; } = string.Empty;
    public decimal Revenue   { get; init; }
    public int     Orders    { get; init; }
    public decimal Commission{ get; init; }
}
