using MesterX.Application.DTOs;
using MesterX.Domain.Entities.Phase3;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesterX.Application.Services.Phase3;

// ──────────────────────────────────────────────────────────────────────────
//  DTOs
// ──────────────────────────────────────────────────────────────────────────
public record ServiceDto
{
    public Guid    Id          { get; init; }
    public string  Name        { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int     DurationMin { get; init; }
    public decimal Price       { get; init; }
    public string? ImageUrl    { get; init; }
    public List<StaffSummaryDto> AvailableStaff { get; init; } = [];
}

public record StaffSummaryDto(Guid Id, string Name, string? Specialty, string? AvatarUrl);

public record TimeSlotDto(TimeOnly StartTime, TimeOnly EndTime, bool IsAvailable);

public record BookingAvailabilityResponse
{
    public DateOnly Date    { get; init; }
    public Guid StaffId     { get; init; }
    public string StaffName { get; init; } = string.Empty;
    public List<TimeSlotDto> Slots { get; init; } = [];
}

public record CreateBookingRequest(
    Guid    StoreId,
    Guid    ServiceId,
    Guid?   StaffId,
    DateOnly BookedDate,
    TimeOnly StartTime,
    string? Notes
);

public record BookingDto
{
    public Guid     Id          { get; init; }
    public string   BookingRef  { get; init; } = string.Empty;
    public string   StoreName   { get; init; } = string.Empty;
    public string   ServiceName { get; init; } = string.Empty;
    public string?  StaffName   { get; init; }
    public string   Status      { get; init; } = string.Empty;
    public string   StatusAr    { get; init; } = string.Empty;
    public string   Date        { get; init; } = string.Empty;
    public string   StartTime   { get; init; } = string.Empty;
    public string   EndTime     { get; init; } = string.Empty;
    public decimal  Price       { get; init; }
    public string?  Notes       { get; init; }
    public DateTime CreatedAt   { get; init; }
}

public record UpdateBookingStatusRequest(string Status, string? Reason);

// ──────────────────────────────────────────────────────────────────────────
//  SERVICE
// ──────────────────────────────────────────────────────────────────────────
public interface IBookingService
{
    // Customer
    Task<ApiResponse<List<ServiceDto>>>          GetServicesAsync(Guid storeId, CancellationToken ct = default);
    Task<ApiResponse<BookingAvailabilityResponse>> GetAvailabilityAsync(Guid storeId, Guid serviceId, Guid? staffId, DateOnly date, CancellationToken ct = default);
    Task<ApiResponse<BookingDto>>                CreateBookingAsync(Guid customerId, CreateBookingRequest req, CancellationToken ct = default);
    Task<ApiResponse<List<BookingDto>>>          GetMyBookingsAsync(Guid customerId, CancellationToken ct = default);
    Task<ApiResponse>                            CancelBookingAsync(Guid customerId, Guid bookingId, string reason, CancellationToken ct = default);

    // Store Owner
    Task<ApiResponse<List<BookingDto>>>          GetStoreDayBookingsAsync(Guid storeId, DateOnly date, CancellationToken ct = default);
    Task<ApiResponse>                            UpdateBookingStatusAsync(Guid storeId, Guid bookingId, UpdateBookingStatusRequest req, CancellationToken ct = default);
}

public class BookingService : IBookingService
{
    private readonly MesterXDbContext _db;
    private readonly ILogger<BookingService> _log;

    public BookingService(MesterXDbContext db, ILogger<BookingService> log)
    { _db = db; _log = log; }

    // ─── GET SERVICES ─────────────────────────────────────────────────────
    public async Task<ApiResponse<List<ServiceDto>>> GetServicesAsync(
        Guid storeId, CancellationToken ct = default)
    {
        var services = await _db.Set<Service>()
            .AsNoTracking()
            .Include(s => s.StaffServices).ThenInclude(ss => ss.Staff)
            .Where(s => s.StoreId == storeId && s.IsActive)
            .ToListAsync(ct);

        return ApiResponse<List<ServiceDto>>.Ok(services.Select(s => new ServiceDto
        {
            Id          = s.Id,
            Name        = s.Name,
            Description = s.Description,
            DurationMin = s.DurationMin,
            Price       = s.Price,
            ImageUrl    = s.ImageUrl,
            AvailableStaff = s.StaffServices
                .Where(ss => ss.Staff.IsActive)
                .Select(ss => new StaffSummaryDto(
                    ss.Staff.Id, ss.Staff.Name, ss.Staff.Specialty, ss.Staff.AvatarUrl))
                .ToList()
        }).ToList());
    }

    // ─── GET AVAILABILITY ─────────────────────────────────────────────────
    public async Task<ApiResponse<BookingAvailabilityResponse>> GetAvailabilityAsync(
        Guid storeId, Guid serviceId, Guid? staffId,
        DateOnly date, CancellationToken ct = default)
    {
        var service = await _db.Set<Service>()
            .Include(s => s.StaffServices).ThenInclude(ss => ss.Staff)
                .ThenInclude(st => st.WorkingHours)
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.StoreId == storeId, ct);

        if (service == null)
            return ApiResponse<BookingAvailabilityResponse>.Fail("الخدمة غير موجودة.");

        // Pick staff
        var staff = staffId.HasValue
            ? service.StaffServices.FirstOrDefault(ss => ss.StaffId == staffId)?.Staff
            : service.StaffServices.FirstOrDefault()?.Staff;

        if (staff == null)
            return ApiResponse<BookingAvailabilityResponse>.Fail("لا يوجد موظفون متاحون.");

        // Get working hours for this day
        var dayOfWeek = (int)date.DayOfWeek;
        var wh = staff.WorkingHours.FirstOrDefault(h => h.DayOfWeek == dayOfWeek && h.IsActive);
        if (wh == null)
            return ApiResponse<BookingAvailabilityResponse>.Ok(new BookingAvailabilityResponse
            {
                Date = date, StaffId = staff.Id, StaffName = staff.Name, Slots = []
            });

        // Get existing bookings for this day
        var existing = await _db.Set<Booking>()
            .AsNoTracking()
            .Where(b => b.StaffId == staff.Id
                && b.BookedDate == date
                && b.Status != BookingStatus.Cancelled
                && b.Status != BookingStatus.NoShow)
            .Select(b => new { b.StartTime, b.EndTime })
            .ToListAsync(ct);

        // Generate slots every (service.DurationMin) minutes
        var slots = new List<TimeSlotDto>();
        var current = wh.StartTime;
        while (current.Add(TimeSpan.FromMinutes(service.DurationMin)) <= wh.EndTime)
        {
            var slotEnd = current.Add(TimeSpan.FromMinutes(service.DurationMin));
            var isPast  = date == DateOnly.FromDateTime(DateTime.UtcNow.Date)
                && current < TimeOnly.FromDateTime(DateTime.UtcNow.AddMinutes(30));
            var isTaken = existing.Any(e =>
                (current >= e.StartTime && current < e.EndTime) ||
                (slotEnd  > e.StartTime && slotEnd <= e.EndTime));

            slots.Add(new TimeSlotDto(current, slotEnd, !isPast && !isTaken));
            current = slotEnd;
        }

        return ApiResponse<BookingAvailabilityResponse>.Ok(new BookingAvailabilityResponse
        {
            Date = date, StaffId = staff.Id, StaffName = staff.Name, Slots = slots
        });
    }

    // ─── CREATE BOOKING ───────────────────────────────────────────────────
    public async Task<ApiResponse<BookingDto>> CreateBookingAsync(
        Guid customerId, CreateBookingRequest req, CancellationToken ct = default)
    {
        var service = await _db.Set<Service>()
            .FirstOrDefaultAsync(s => s.Id == req.ServiceId && s.StoreId == req.StoreId, ct);
        if (service == null)
            return ApiResponse<BookingDto>.Fail("الخدمة غير موجودة.");

        // Confirm slot still available
        var endTime = req.StartTime.Add(TimeSpan.FromMinutes(service.DurationMin));
        var conflict = await _db.Set<Booking>().AnyAsync(b =>
            b.StaffId == req.StaffId
            && b.BookedDate == req.BookedDate
            && b.Status != BookingStatus.Cancelled
            && b.Status != BookingStatus.NoShow
            && ((req.StartTime >= b.StartTime && req.StartTime < b.EndTime)
             || (endTime > b.StartTime && endTime <= b.EndTime)), ct);

        if (conflict)
            return ApiResponse<BookingDto>.Fail("هذا الوقت محجوز. اختر وقتاً آخر.");

        var booking = new Booking
        {
            StoreId    = req.StoreId,
            CustomerId = customerId,
            ServiceId  = req.ServiceId,
            StaffId    = req.StaffId,
            BookingRef = await GenerateRefAsync(ct),
            BookedDate = req.BookedDate,
            StartTime  = req.StartTime,
            EndTime    = endTime,
            Price      = service.Price,
            Notes      = req.Notes,
        };
        _db.Set<Booking>().Add(booking);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Booking {Ref} created: {Service} on {Date} at {Time}",
            booking.BookingRef, service.Name, req.BookedDate, req.StartTime);

        return ApiResponse<BookingDto>.Ok(await MapBookingAsync(booking.Id, ct));
    }

    // ─── MY BOOKINGS ──────────────────────────────────────────────────────
    public async Task<ApiResponse<List<BookingDto>>> GetMyBookingsAsync(
        Guid customerId, CancellationToken ct = default)
    {
        var ids = await _db.Set<Booking>()
            .AsNoTracking()
            .Where(b => b.CustomerId == customerId)
            .OrderByDescending(b => b.BookedDate)
            .ThenByDescending(b => b.StartTime)
            .Select(b => b.Id)
            .Take(20)
            .ToListAsync(ct);

        var dtos = new List<BookingDto>();
        foreach (var id in ids)
            dtos.Add(await MapBookingAsync(id, ct));
        return ApiResponse<List<BookingDto>>.Ok(dtos);
    }

    // ─── CANCEL ───────────────────────────────────────────────────────────
    public async Task<ApiResponse> CancelBookingAsync(
        Guid customerId, Guid bookingId, string reason, CancellationToken ct = default)
    {
        var b = await _db.Set<Booking>()
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.CustomerId == customerId, ct);
        if (b == null) return ApiResponse.Fail("الحجز غير موجود.");
        if (b.Status == BookingStatus.Completed)
            return ApiResponse.Fail("لا يمكن إلغاء حجز مكتمل.");

        b.Status       = BookingStatus.Cancelled;
        b.CancelReason = reason;
        b.CancelledAt  = DateTime.UtcNow;
        b.UpdatedAt    = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok();
    }

    // ─── STORE: DAY SCHEDULE ──────────────────────────────────────────────
    public async Task<ApiResponse<List<BookingDto>>> GetStoreDayBookingsAsync(
        Guid storeId, DateOnly date, CancellationToken ct = default)
    {
        var ids = await _db.Set<Booking>()
            .AsNoTracking()
            .Where(b => b.StoreId == storeId && b.BookedDate == date)
            .OrderBy(b => b.StartTime)
            .Select(b => b.Id)
            .ToListAsync(ct);

        var dtos = new List<BookingDto>();
        foreach (var id in ids)
            dtos.Add(await MapBookingAsync(id, ct));
        return ApiResponse<List<BookingDto>>.Ok(dtos);
    }

    // ─── STORE: UPDATE STATUS ────────────────────────────────────────────
    public async Task<ApiResponse> UpdateBookingStatusAsync(
        Guid storeId, Guid bookingId, UpdateBookingStatusRequest req,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<BookingStatus>(req.Status, out var status))
            return ApiResponse.Fail("حالة غير صالحة.");

        var b = await _db.Set<Booking>()
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.StoreId == storeId, ct);
        if (b == null) return ApiResponse.Fail("الحجز غير موجود.");

        b.Status     = status;
        b.UpdatedAt  = DateTime.UtcNow;
        if (status == BookingStatus.Confirmed)  b.ConfirmedAt = DateTime.UtcNow;
        if (status == BookingStatus.Completed)  b.CompletedAt = DateTime.UtcNow;
        if (status == BookingStatus.Cancelled)
        {
            b.CancelledAt  = DateTime.UtcNow;
            b.CancelReason = req.Reason;
        }

        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok();
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────
    private async Task<string> GenerateRefAsync(CancellationToken ct)
    {
        var prefix = DateTime.UtcNow.ToString("yyMMdd");
        var count  = await _db.Set<Booking>()
            .CountAsync(b => b.BookedDate == DateOnly.FromDateTime(DateTime.UtcNow.Date), ct);
        return $"BK-{prefix}-{(count + 1):D3}";
    }

    private async Task<BookingDto> MapBookingAsync(Guid id, CancellationToken ct)
    {
        var b = await _db.Set<Booking>()
            .AsNoTracking()
            .Include(x => x.Service)
            .Include(x => x.Staff)
            .FirstAsync(x => x.Id == id, ct);

        var store = await _db.Tenants.FindAsync([b.StoreId], ct);

        string StatusAr(BookingStatus s) => s switch
        {
            BookingStatus.Pending    => "في الانتظار",
            BookingStatus.Confirmed  => "مؤكد",
            BookingStatus.InProgress => "قيد التنفيذ",
            BookingStatus.Completed  => "مكتمل",
            BookingStatus.Cancelled  => "ملغى",
            BookingStatus.NoShow     => "لم يحضر",
            _ => s.ToString()
        };

        return new BookingDto
        {
            Id          = b.Id,
            BookingRef  = b.BookingRef,
            StoreName   = store?.Name ?? string.Empty,
            ServiceName = b.Service.Name,
            StaffName   = b.Staff?.Name,
            Status      = b.Status.ToString(),
            StatusAr    = StatusAr(b.Status),
            Date        = b.BookedDate.ToString("dd/MM/yyyy"),
            StartTime   = b.StartTime.ToString("hh:mm tt"),
            EndTime     = b.EndTime.ToString("hh:mm tt"),
            Price       = b.Price,
            Notes       = b.Notes,
            CreatedAt   = b.CreatedAt,
        };
    }
}
