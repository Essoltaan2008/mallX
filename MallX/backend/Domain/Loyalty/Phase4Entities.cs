using System.ComponentModel.DataAnnotations;
using MesterX.Domain.Entities.Mall;

namespace MesterX.Domain.Entities.Phase4;

// ══════════════════════════════════════════════════════════════════════════
//  LOYALTY
// ══════════════════════════════════════════════════════════════════════════

public enum PointsSource
{
    Purchase, Referral, Birthday, Rating, Signup,
    Redemption, Adjustment, Expiry
}

public class LoyaltyAccount
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid CustomerId   { get; set; }
    [Required] public Guid MallId       { get; set; }
    public int LifetimePoints           { get; set; } = 0;
    public int RedeemedPoints           { get; set; } = 0;
    public int AvailablePoints          => LifetimePoints - RedeemedPoints;
    [MaxLength(20)] public string Tier  { get; set; } = "Bronze";
    public DateTime TierUpdatedAt       { get; set; } = DateTime.UtcNow;
    public DateTime? PointsExpireAt     { get; set; }
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt           { get; set; } = DateTime.UtcNow;

    public virtual MallCustomer Customer { get; set; } = null!;
    public virtual ICollection<PointsTransaction> Transactions { get; set; } = [];

    // Tier boundaries
    public static string CalculateTier(int points) => points switch
    {
        >= 5000 => "Gold",
        >= 1000 => "Silver",
        _       => "Bronze"
    };

    public static decimal GetMultiplier(string tier) => tier switch
    {
        "Gold"   => 2.0m,
        "Silver" => 1.5m,
        _        => 1.0m
    };

    public static int PointsToNextTier(int current) => current switch
    {
        >= 5000 => 0,
        >= 1000 => 5000 - current,
        _       => 1000 - current
    };
}

public class PointsTransaction
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid AccountId   { get; set; }
    [Required] public Guid CustomerId  { get; set; }
    public Guid? MallOrderId           { get; set; }
    public PointsSource Source         { get; set; }
    public int Points                  { get; set; }       // +/-
    public int BalanceAfter            { get; set; }
    public string? Description         { get; set; }
    public DateTime? ExpiresAt         { get; set; }
    public DateTime CreatedAt          { get; set; } = DateTime.UtcNow;

    public virtual LoyaltyAccount Account { get; set; } = null!;
}

public class LoyaltyRule
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallId        { get; set; }
    public Guid? StoreId                 { get; set; }     // null = mall-wide
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public decimal PointsPerEgp          { get; set; } = 1.0m;
    public decimal MinOrderValue         { get; set; } = 0;
    public string TierMultiplierJson     { get; set; } = """{"Bronze":1,"Silver":1.5,"Gold":2}""";
    public bool IsActive                 { get; set; } = true;
    public DateTime ValidFrom            { get; set; } = DateTime.UtcNow;
    public DateTime? ValidTo             { get; set; }
    public DateTime CreatedAt            { get; set; } = DateTime.UtcNow;
}

// ══════════════════════════════════════════════════════════════════════════
//  PROMOTIONS
// ══════════════════════════════════════════════════════════════════════════

public enum DiscountType   { Percentage, FixedAmount, FreeDelivery, BuyXGetY }
public enum PromotionScope { MallWide, Store, Category, Product }
public enum CouponStatus   { Active, Paused, Expired, Depleted }

public class Coupon
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallId          { get; set; }
    public Guid? StoreId                   { get; set; }
    [Required, MaxLength(30)]  public string Code { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public string? Description             { get; set; }
    public DiscountType   DiscountType     { get; set; }
    public decimal DiscountValue           { get; set; }
    public decimal MinOrderValue           { get; set; } = 0;
    public decimal? MaxDiscount            { get; set; }
    public int? MaxUses                    { get; set; }
    public int UsesPerCustomer             { get; set; } = 1;
    public int UsedCount                   { get; set; } = 0;
    public PromotionScope Scope            { get; set; } = PromotionScope.MallWide;
    public Guid? ScopeId                   { get; set; }
    public CouponStatus Status             { get; set; } = CouponStatus.Active;
    public string? MinTier                 { get; set; }
    public DateTime ValidFrom              { get; set; } = DateTime.UtcNow;
    public DateTime ValidTo                { get; set; }
    public Guid? CreatedBy                 { get; set; }
    public DateTime CreatedAt              { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt              { get; set; } = DateTime.UtcNow;

    public bool IsValid => Status == CouponStatus.Active
        && DateTime.UtcNow >= ValidFrom
        && DateTime.UtcNow <= ValidTo
        && (MaxUses == null || UsedCount < MaxUses);
}

public class CouponUse
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid CouponId     { get; set; }
    [Required] public Guid CustomerId   { get; set; }
    [Required] public Guid MallOrderId  { get; set; }
    public decimal DiscountAmt          { get; set; }
    public DateTime UsedAt              { get; set; } = DateTime.UtcNow;
}

public class FlashSale
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallId      { get; set; }
    public Guid? StoreId               { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    public string? TitleAr             { get; set; }
    public Guid? ProductId             { get; set; }
    public decimal? OriginalPrice      { get; set; }
    public decimal FlashPrice          { get; set; }
    public int QuantityLimit           { get; set; } = 100;
    public int QuantitySold            { get; set; } = 0;
    public DateTime StartsAt           { get; set; }
    public DateTime EndsAt             { get; set; }
    public string? BannerUrl           { get; set; }
    public bool IsActive               { get; set; } = true;
    public DateTime CreatedAt          { get; set; } = DateTime.UtcNow;

    public bool IsLive => IsActive && DateTime.UtcNow >= StartsAt && DateTime.UtcNow <= EndsAt;
    public int Remaining => Math.Max(0, QuantityLimit - QuantitySold);
    public double DiscountPct => OriginalPrice > 0
        ? Math.Round((1 - (double)FlashPrice / (double)OriginalPrice!) * 100, 1) : 0;
}

// ══════════════════════════════════════════════════════════════════════════
//  PUSH NOTIFICATIONS
// ══════════════════════════════════════════════════════════════════════════

public enum NotifTarget { AllCustomers, TierBronze, TierSilver, TierGold, InMallZone, CustomSegment }
public enum NotifStatus { Draft, Scheduled, Sending, Sent, Failed, Cancelled }

public class CustomerDevice
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid CustomerId   { get; set; }
    [Required] public string FcmToken   { get; set; } = string.Empty;
    [MaxLength(20)] public string Platform { get; set; } = "Flutter";
    public string? DeviceName           { get; set; }
    public bool IsActive                { get; set; } = true;
    public DateTime LastSeen            { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;

    public virtual MallCustomer Customer { get; set; } = null!;
}

public class NotificationCampaign
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallId           { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    public string? TitleAr                  { get; set; }
    [Required] public string Body           { get; set; } = string.Empty;
    public string? BodyAr                   { get; set; }
    public string? ImageUrl                 { get; set; }
    public string? ActionType               { get; set; }
    public string? ActionId                 { get; set; }
    public NotifTarget Target               { get; set; } = NotifTarget.AllCustomers;
    public NotifStatus Status               { get; set; } = NotifStatus.Draft;
    public DateTime? ScheduledAt            { get; set; }
    public DateTime? SentAt                 { get; set; }
    public int SentCount                    { get; set; } = 0;
    public int OpenCount                    { get; set; } = 0;
    public Guid? CreatedBy                  { get; set; }
    public DateTime CreatedAt               { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt               { get; set; } = DateTime.UtcNow;
}

public class GeoFenceTrigger
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid MallId              { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(30)] public string TriggerType  { get; set; } = "Enter";
    public int RadiusM                         { get; set; } = 200;
    [Required, MaxLength(200)] public string NotifTitle { get; set; } = string.Empty;
    [Required] public string NotifBody         { get; set; } = string.Empty;
    public string? NotifTitleAr                { get; set; }
    public string? NotifBodyAr                 { get; set; }
    public string? ActionType                  { get; set; }
    public string? ActionId                    { get; set; }
    public int CooldownHours                   { get; set; } = 24;
    public bool IsActive                       { get; set; } = true;
    public DateTime ValidFrom                  { get; set; } = DateTime.UtcNow;
    public DateTime? ValidTo                   { get; set; }
    public DateTime CreatedAt                  { get; set; } = DateTime.UtcNow;
}

public class GeoFenceEvent
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid TriggerId   { get; set; }
    [Required] public Guid CustomerId  { get; set; }
    [MaxLength(30)] public string EventType { get; set; } = string.Empty;
    public decimal? CustomerLat        { get; set; }
    public decimal? CustomerLng        { get; set; }
    public bool NotifSent              { get; set; } = false;
    public DateTime CreatedAt          { get; set; } = DateTime.UtcNow;
}
