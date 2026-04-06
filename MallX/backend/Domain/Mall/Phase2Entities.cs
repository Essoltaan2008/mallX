using System.ComponentModel.DataAnnotations;
using MesterX.Domain.Entities.Mall;

namespace MesterX.Domain.Entities.Payment;

// ─── ENUMs ────────────────────────────────────────────────────────────────
public enum PaymentStatus    { Pending, Completed, Failed, Refunded, PartialRefund }
public enum PaymentGateway   { Cash, Paymob, Fawry, VodafoneCash, Internal }
public enum SettlementStatus { Pending, Processing, Completed, Failed }

// ─── PAYMENT TRANSACTION ──────────────────────────────────────────────────
public class PaymentTransaction
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallOrderId  { get; set; }
    [Required] public Guid CustomerId   { get; set; }
    public decimal Amount               { get; set; }
    [MaxLength(5)] public string Currency { get; set; } = "EGP";
    public PaymentGateway Gateway       { get; set; } = PaymentGateway.Cash;
    public PaymentStatus  Status        { get; set; } = PaymentStatus.Pending;
    public string? GatewayOrderId       { get; set; }   // Paymob order_id
    public string? GatewayTxnId         { get; set; }   // Paymob transaction_id
    public string? GatewayResponse      { get; set; }   // JSON raw
    public string? FailureReason        { get; set; }
    public DateTime? PaidAt             { get; set; }
    public DateTime? RefundedAt         { get; set; }
    public decimal RefundAmount         { get; set; } = 0;
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt           { get; set; } = DateTime.UtcNow;

    public virtual MallOrder MallOrder { get; set; } = null!;
}

// ─── COMMISSION SETTLEMENT ────────────────────────────────────────────────
public class CommissionSettlement
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallId        { get; set; }
    [Required] public Guid StoreId       { get; set; }
    public DateTime PeriodStart          { get; set; }
    public DateTime PeriodEnd            { get; set; }
    public int TotalOrders               { get; set; } = 0;
    public decimal GrossRevenue          { get; set; } = 0;
    public decimal CommissionRate        { get; set; } = 0.05m;
    public decimal CommissionAmt         { get; set; } = 0;
    public decimal NetPayable            { get; set; } = 0;  // للمحل
    public SettlementStatus Status       { get; set; } = SettlementStatus.Pending;
    public DateTime? SettledAt           { get; set; }
    public string? Notes                 { get; set; }
    public Guid? CreatedBy               { get; set; }
    public DateTime CreatedAt            { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt            { get; set; } = DateTime.UtcNow;
}

// ─── DELIVERY ZONE ────────────────────────────────────────────────────────
public class DeliveryZone
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallId          { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public string? Description             { get; set; }
    public decimal Fee                     { get; set; } = 15m;
    public decimal MinOrderFee             { get; set; } = 0;
    public decimal? FreeAbove              { get; set; }   // توصيل مجاني
    public string? Polygon                 { get; set; }   // JSON coordinates
    public bool IsActive                   { get; set; } = true;
    public DateTime CreatedAt              { get; set; } = DateTime.UtcNow;
}

// ─── DRIVER ──────────────────────────────────────────────────────────────
public class Driver
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallId              { get; set; }
    [Required, MaxLength(200)] public string Name  { get; set; } = string.Empty;
    [Required, MaxLength(20)]  public string Phone { get; set; } = string.Empty;
    [MaxLength(50)] public string VehicleType  { get; set; } = "Motorcycle";
    public string? VehiclePlate                { get; set; }
    public decimal? CurrentLat                 { get; set; }
    public decimal? CurrentLng                 { get; set; }
    public bool IsAvailable                    { get; set; } = true;
    public bool IsActive                       { get; set; } = true;
    public DateTime? LastSeenAt                { get; set; }
    public DateTime CreatedAt                  { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt                  { get; set; } = DateTime.UtcNow;
}

// ─── DELIVERY ASSIGNMENT ─────────────────────────────────────────────────
public class DeliveryAssignment
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallOrderId { get; set; }
    public Guid? DriverId               { get; set; }
    public DateTime? AssignedAt         { get; set; }
    public DateTime? PickedUpAt         { get; set; }
    public DateTime? DeliveredAt        { get; set; }
    public decimal? DistanceKm          { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "Pending";
    public string? Notes                { get; set; }
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;

    public virtual MallOrder MallOrder { get; set; } = null!;
    public virtual Driver? Driver      { get; set; }
}

// ─── MALL ANALYTICS DAILY ────────────────────────────────────────────────
public class MallAnalyticsDaily
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallId   { get; set; }
    public DateOnly SnapshotDate    { get; set; }
    public int TotalOrders          { get; set; } = 0;
    public decimal TotalRevenue     { get; set; } = 0;
    public decimal TotalCommission  { get; set; } = 0;
    public int NewCustomers         { get; set; } = 0;
    public int ActiveStores         { get; set; } = 0;
    public decimal AvgOrderValue    { get; set; } = 0;
    public DateTime CreatedAt       { get; set; } = DateTime.UtcNow;
}
