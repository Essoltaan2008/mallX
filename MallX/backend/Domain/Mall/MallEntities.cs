using System.ComponentModel.DataAnnotations;
using MesterX.Domain.Entities.Core;
using MesterX.Domain.Entities.Pos;

namespace MesterX.Domain.Entities.Mall;

// ─── ENUMs ────────────────────────────────────────────────────────────────
public enum StoreType        { Restaurant, Retail, Service }
public enum FulfillmentType  { Delivery, Pickup, InStore }
public enum MallOrderStatus  { Placed, Confirmed, Preparing, Ready, PickedUp, Delivered, Cancelled }
public enum StoreOrderStatus { Placed, Confirmed, Preparing, Ready, Cancelled }
public enum CustomerTier     { Bronze, Silver, Gold }

// ─── MALL ─────────────────────────────────────────────────────────────────
public class Mall
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required, MaxLength(200)] public string Name   { get; set; } = string.Empty;
    public string? NameAr    { get; set; }
    [Required, MaxLength(100)] public string Slug   { get; set; } = string.Empty;
    public string? Address   { get; set; }
    public decimal? GeoLat   { get; set; }
    public decimal? GeoLng   { get; set; }
    public int GeoRadiusM    { get; set; } = 200;
    public string? LogoUrl   { get; set; }
    public string? CoverUrl  { get; set; }
    public string? Phone     { get; set; }
    public string? Email     { get; set; }
    public bool IsActive     { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<MallCustomer> Customers { get; set; } = [];
    public virtual ICollection<MallOrder>   Orders    { get; set; } = [];
}

// ─── MALL CUSTOMER (B2C — مستقل عن B2B users) ────────────────────────────
public class MallCustomer
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallId { get; set; }
    [Required, MaxLength(100)] public string FirstName    { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string LastName     { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Email        { get; set; } = string.Empty;
    public string? Phone          { get; set; }
    [Required] public string PasswordHash { get; set; } = string.Empty;
    public string? AvatarUrl      { get; set; }
    public int LoyaltyPoints      { get; set; } = 0;
    public CustomerTier Tier      { get; set; } = CustomerTier.Bronze;
    public DateTime TierUpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt{ get; set; } = DateTime.UtcNow;
    public int FailedAttempts     { get; set; } = 0;
    public DateTime? LockoutEnd   { get; set; }
    public bool IsActive          { get; set; } = true;
    public bool IsDeleted         { get; set; } = false;
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt     { get; set; } = DateTime.UtcNow;

    // Computed
    public string FullName => $"{FirstName} {LastName}";
    public bool IsLocked   => LockoutEnd.HasValue && LockoutEnd > DateTime.UtcNow;

    // Tier thresholds
    public static CustomerTier CalculateTier(int points) => points switch
    {
        >= 5000 => CustomerTier.Gold,
        >= 1000 => CustomerTier.Silver,
        _       => CustomerTier.Bronze
    };

    public virtual Mall Mall { get; set; } = null!;
    public virtual ICollection<CustomerAddress>         Addresses      { get; set; } = [];
    public virtual ICollection<CustomerRefreshToken>    RefreshTokens  { get; set; } = [];
    public virtual ICollection<MallOrder>               Orders         { get; set; } = [];
    public virtual Cart?                                Cart           { get; set; }
}

// ─── CUSTOMER ADDRESS ─────────────────────────────────────────────────────
public class CustomerAddress
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid CustomerId { get; set; }
    [MaxLength(50)] public string Label { get; set; } = "Home";
    [Required] public string Address { get; set; } = string.Empty;
    public decimal? GeoLat    { get; set; }
    public decimal? GeoLng    { get; set; }
    public bool IsDefault     { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public virtual MallCustomer Customer { get; set; } = null!;
}

// ─── CUSTOMER REFRESH TOKEN ───────────────────────────────────────────────
public class CustomerRefreshToken
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid CustomerId  { get; set; }
    [Required] public string TokenHash { get; set; } = string.Empty;
    [Required] public string TokenSalt { get; set; } = string.Empty;
    public string? DeviceInfo          { get; set; }
    public string? IpAddress           { get; set; }
    public DateTime ExpiresAt          { get; set; }
    public bool IsRevoked              { get; set; } = false;
    public DateTime CreatedAt          { get; set; } = DateTime.UtcNow;
    public virtual MallCustomer Customer { get; set; } = null!;
}

// ─── CART ─────────────────────────────────────────────────────────────────
public class Cart
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid CustomerId { get; set; }
    [Required] public Guid MallId    { get; set; }
    public DateTime ExpiresAt         { get; set; } = DateTime.UtcNow.AddDays(7);
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt         { get; set; } = DateTime.UtcNow;

    public virtual MallCustomer Customer  { get; set; } = null!;
    public virtual ICollection<CartItem> Items { get; set; } = [];

    // Computed
    public decimal Subtotal    => Items.Sum(i => i.UnitPrice * i.Quantity);
    public int     TotalItems  => Items.Sum(i => i.Quantity);
    public bool    IsEmpty     => !Items.Any();

    // Group items by store for splitting
    public IEnumerable<IGrouping<Guid, CartItem>> GroupByStore()
        => Items.GroupBy(i => i.StoreId);
}

// ─── CART ITEM ────────────────────────────────────────────────────────────
public class CartItem
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid CartId    { get; set; }
    [Required] public Guid StoreId   { get; set; }
    [Required] public Guid ProductId { get; set; }
    public int Quantity              { get; set; } = 1;
    public decimal UnitPrice         { get; set; }
    public string? Notes             { get; set; }
    [MaxLength(20)] public string ItemType { get; set; } = "Product";
    public DateTime CreatedAt        { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt        { get; set; } = DateTime.UtcNow;

    // Computed
    public decimal LineTotal => UnitPrice * Quantity;

    public virtual Cart    Cart    { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
    public virtual Tenant  Store   { get; set; } = null!;
}

// ─── MALL ORDER ───────────────────────────────────────────────────────────
public class MallOrder
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid CustomerId { get; set; }
    [Required] public Guid MallId    { get; set; }
    [Required, MaxLength(30)] public string OrderNumber { get; set; } = string.Empty;
    public MallOrderStatus  Status          { get; set; } = MallOrderStatus.Placed;
    public FulfillmentType  FulfillmentType { get; set; } = FulfillmentType.Delivery;
    public decimal Subtotal         { get; set; }
    public decimal DeliveryFee      { get; set; } = 0;
    public decimal DiscountAmount   { get; set; } = 0;
    public decimal Total            { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public string? DeliveryAddress  { get; set; }
    public decimal? DeliveryLat     { get; set; }
    public decimal? DeliveryLng     { get; set; }
    public string? Notes            { get; set; }
    public DateTime PlacedAt        { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt    { get; set; }
    public DateTime CreatedAt       { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt       { get; set; } = DateTime.UtcNow;

    public virtual MallCustomer Customer { get; set; } = null!;
    public virtual Mall         Mall     { get; set; } = null!;
    public virtual ICollection<StoreOrder>         StoreOrders    { get; set; } = [];
    public virtual ICollection<OrderStatusHistory> StatusHistory  { get; set; } = [];
}

// ─── STORE ORDER (السلة المفككة) ──────────────────────────────────────────
public class StoreOrder
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallOrderId { get; set; }
    [Required] public Guid StoreId     { get; set; }
    public StoreOrderStatus Status     { get; set; } = StoreOrderStatus.Placed;
    public decimal Subtotal            { get; set; }
    public decimal CommissionRate      { get; set; } = 0.05m;
    public decimal CommissionAmt       { get; set; }
    public decimal StoreTotal          { get; set; }
    public string? Notes               { get; set; }
    public DateTime? ConfirmedAt       { get; set; }
    public DateTime? ReadyAt           { get; set; }
    public DateTime CreatedAt          { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt          { get; set; } = DateTime.UtcNow;

    public virtual MallOrder MallOrder { get; set; } = null!;
    public virtual Tenant    Store     { get; set; } = null!;
    public virtual ICollection<StoreOrderItem> Items { get; set; } = [];
}

// ─── STORE ORDER ITEM ─────────────────────────────────────────────────────
public class StoreOrderItem
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid StoreOrderId { get; set; }
    [Required] public Guid ProductId    { get; set; }
    [Required, MaxLength(200)] public string ProductName { get; set; } = string.Empty;
    public int Quantity                 { get; set; }
    public decimal UnitPrice            { get; set; }
    public string? Notes                { get; set; }
    public decimal Total                { get; set; }
    public virtual StoreOrder StoreOrder { get; set; } = null!;
}

// ─── ORDER STATUS HISTORY ─────────────────────────────────────────────────
public class OrderStatusHistory
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallOrderId  { get; set; }
    public Guid? StoreOrderId           { get; set; }
    public string? OldStatus            { get; set; }
    [Required] public string NewStatus  { get; set; } = string.Empty;
    public string? Note                 { get; set; }
    public Guid? ChangedBy              { get; set; }
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;
    public virtual MallOrder MallOrder  { get; set; } = null!;
}
