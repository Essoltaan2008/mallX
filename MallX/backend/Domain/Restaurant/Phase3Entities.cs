using System.ComponentModel.DataAnnotations;
using MesterX.Domain.Entities.Mall;

namespace MesterX.Domain.Entities.Phase3;

// ══════════════════════════════════════════════════════════════════════════
//  RESTAURANT ENTITIES
// ══════════════════════════════════════════════════════════════════════════

public class MenuCategory
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid StoreId { get; set; }
    [Required, MaxLength(100)] public string Name   { get; set; } = string.Empty;
    public string? NameAr  { get; set; }
    public string? Icon    { get; set; }
    public int SortOrder   { get; set; } = 0;
    public bool IsActive   { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<MenuItem> Items { get; set; } = [];
}

public class MenuItem
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid StoreId    { get; set; }
    public Guid? CategoryId           { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? NameAr             { get; set; }
    public string? Description        { get; set; }
    public string? DescriptionAr      { get; set; }
    public decimal Price              { get; set; }
    public string? ImageUrl           { get; set; }
    public int PrepTimeMin            { get; set; } = 15;
    public int? Calories              { get; set; }
    public bool IsAvailable           { get; set; } = true;
    public bool IsFeatured            { get; set; } = false;
    public int SortOrder              { get; set; } = 0;
    public string[]? Tags             { get; set; }
    public bool IsDeleted             { get; set; } = false;
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt         { get; set; } = DateTime.UtcNow;

    public virtual MenuCategory? Category { get; set; }
    public virtual ICollection<MenuItemOption> Options { get; set; } = [];
}

public class MenuItemOption
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid ItemId     { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public bool IsRequired            { get; set; } = false;
    public string Choices             { get; set; } = "[]"; // JSON
    public virtual MenuItem Item      { get; set; } = null!;
}

public enum TicketStatus { Waiting, Preparing, Ready, Collected, Cancelled }

public class QueueTicket
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid StoreOrderId { get; set; }
    [Required] public Guid StoreId      { get; set; }
    public int TicketNumber             { get; set; }
    public TicketStatus Status          { get; set; } = TicketStatus.Waiting;
    public DateTime? EstimatedReady     { get; set; }
    public DateTime? ReadyAt            { get; set; }
    public DateTime? CollectedAt        { get; set; }
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;

    public virtual StoreOrder StoreOrder { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════
//  BOOKING ENTITIES
// ══════════════════════════════════════════════════════════════════════════

public class ServiceStaff
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid StoreId    { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? Specialty          { get; set; }
    public string? AvatarUrl          { get; set; }
    public bool IsActive              { get; set; } = true;
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;

    public virtual ICollection<WorkingHour>  WorkingHours  { get; set; } = [];
    public virtual ICollection<StaffService> StaffServices { get; set; } = [];
}

public class Service
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid StoreId    { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? Description        { get; set; }
    public int DurationMin            { get; set; } = 30;
    public decimal Price              { get; set; }
    public string? ImageUrl           { get; set; }
    public bool IsActive              { get; set; } = true;
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt         { get; set; } = DateTime.UtcNow;

    public virtual ICollection<StaffService> StaffServices { get; set; } = [];
}

public class StaffService
{
    public Guid StaffId   { get; set; }
    public Guid ServiceId { get; set; }
    public virtual ServiceStaff Staff   { get; set; } = null!;
    public virtual Service      Service { get; set; } = null!;
}

public class WorkingHour
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid StaffId { get; set; }
    public int DayOfWeek          { get; set; } // 0=Sun … 6=Sat
    public TimeOnly StartTime     { get; set; }
    public TimeOnly EndTime       { get; set; }
    public bool IsActive          { get; set; } = true;
    public virtual ServiceStaff Staff { get; set; } = null!;
}

public enum BookingStatus
{
    Pending, Confirmed, InProgress, Completed, Cancelled, NoShow
}

public class Booking
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid StoreId    { get; set; }
    [Required] public Guid CustomerId { get; set; }
    [Required] public Guid ServiceId  { get; set; }
    public Guid? StaffId              { get; set; }
    [Required, MaxLength(20)] public string BookingRef { get; set; } = string.Empty;
    public BookingStatus Status       { get; set; } = BookingStatus.Pending;
    public DateOnly BookedDate        { get; set; }
    public TimeOnly StartTime         { get; set; }
    public TimeOnly EndTime           { get; set; }
    public decimal Price              { get; set; }
    public string? Notes              { get; set; }
    public bool ReminderSent          { get; set; } = false;
    public DateTime? ConfirmedAt      { get; set; }
    public DateTime? CompletedAt      { get; set; }
    public DateTime? CancelledAt      { get; set; }
    public string? CancelReason       { get; set; }
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt         { get; set; } = DateTime.UtcNow;

    public virtual MallCustomer Customer { get; set; } = null!;
    public virtual Service      Service  { get; set; } = null!;
    public virtual ServiceStaff? Staff   { get; set; }
}

// ══════════════════════════════════════════════════════════════════════════
//  RATING ENTITIES
// ══════════════════════════════════════════════════════════════════════════

public enum RatingSubject { Store, Delivery, Overall, MenuItem }

public class Rating
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? MallOrderId          { get; set; }
    public Guid? BookingId            { get; set; }
    [Required] public Guid CustomerId { get; set; }
    [Required] public Guid StoreId    { get; set; }
    public RatingSubject Subject      { get; set; } = RatingSubject.Store;
    public Guid? SubjectId            { get; set; }   // MenuItem ID
    public short Stars                { get; set; }
    public string? Title              { get; set; }
    public string? Body               { get; set; }
    public string[]? Images           { get; set; }
    public bool IsAnonymous           { get; set; } = false;
    public bool IsPublished           { get; set; } = true;
    public string? StoreReply         { get; set; }
    public DateTime? StoreRepliedAt   { get; set; }
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;

    public virtual MallCustomer Customer { get; set; } = null!;
}

public class StoreRatingSummary
{
    [Key] public Guid StoreId    { get; set; }
    public decimal AvgStars      { get; set; } = 0;
    public int TotalRatings      { get; set; } = 0;
    public int FiveStar          { get; set; } = 0;
    public int FourStar          { get; set; } = 0;
    public int ThreeStar         { get; set; } = 0;
    public int TwoStar           { get; set; } = 0;
    public int OneStar           { get; set; } = 0;
    public DateTime UpdatedAt    { get; set; } = DateTime.UtcNow;
}
