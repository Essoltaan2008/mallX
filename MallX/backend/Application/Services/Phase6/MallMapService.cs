using MesterX.Application.DTOs;
using MesterX.Infrastructure.Caching;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesterX.Application.Services.Phase6;

// ──────────────────────────────────────────────────────────────────────────
//  MALL MAP DTOs
// ──────────────────────────────────────────────────────────────────────────
public record FloorDto
{
    public Guid   Id         { get; init; }
    public int    FloorNum   { get; init; }
    public string Name       { get; init; } = string.Empty;
    public string? NameAr    { get; init; }
    public string? MapSvgUrl { get; init; }
    public List<StoreLocationDto> Stores    { get; init; } = [];
    public List<AmenityDto>       Amenities { get; init; } = [];
}

public record StoreLocationDto
{
    public Guid   StoreId   { get; init; }
    public string StoreName { get; init; } = string.Empty;
    public string StoreType { get; init; } = string.Empty;
    public string? LogoUrl  { get; init; }
    public double PosX      { get; init; }
    public double PosY      { get; init; }
    public double Width     { get; init; }
    public double Height    { get; init; }
    public string Shape     { get; init; } = "rect";
    public string Color     { get; init; } = "#3B82F6";
    public string? QrCode   { get; init; }
    public decimal AvgRating{ get; init; }
    public bool   IsOpen    { get; init; }
}

public record AmenityDto
{
    public Guid   Id    { get; init; }
    public string Type  { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? NameAr { get; init; }
    public double PosX  { get; init; }
    public double PosY  { get; init; }
    public string? Icon { get; init; }
}

public record MallMapDto
{
    public Guid          MallId  { get; init; }
    public string        Name    { get; init; } = string.Empty;
    public List<FloorDto> Floors { get; init; } = [];
}

public record PlaceStoreRequest(
    Guid StoreId, Guid FloorId,
    double PosX, double PosY,
    double Width, double Height,
    string? Color, string? QrCode
);

// ──────────────────────────────────────────────────────────────────────────
//  MALL MAP SERVICE
// ──────────────────────────────────────────────────────────────────────────
public interface IMallMapService
{
    Task<ApiResponse<MallMapDto>>  GetMapAsync(Guid mallId, CancellationToken ct = default);
    Task<ApiResponse<FloorDto>>    GetFloorAsync(Guid floorId, CancellationToken ct = default);
    Task<ApiResponse>              PlaceStoreAsync(PlaceStoreRequest req, CancellationToken ct = default);
    Task<ApiResponse<string>>      GenerateStoreQrAsync(Guid storeId, CancellationToken ct = default);
}

public class MallMapService : IMallMapService
{
    private readonly MesterXDbContext _db;
    private readonly ICacheService    _cache;

    public MallMapService(MesterXDbContext db, ICacheService cache)
    { _db = db; _cache = cache; }

    public async Task<ApiResponse<MallMapDto>> GetMapAsync(
        Guid mallId, CancellationToken ct = default)
    {
        var cacheKey = $"mall:{mallId}:map";
        return await _cache.GetOrSetAsync(cacheKey, async () =>
        {
            var mall = await _db.Malls.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == mallId && m.IsActive, ct);
            if (mall == null) return ApiResponse<MallMapDto>.Fail("المول غير موجود.");

            var floors = await _db.Set<MallFloor>()
                .AsNoTracking()
                .Where(f => f.MallId == mallId && f.IsActive)
                .OrderBy(f => f.SortOrder)
                .ToListAsync(ct);

            var floorDtos = new List<FloorDto>();
            foreach (var floor in floors)
                floorDtos.Add(await BuildFloorDto(floor, ct));

            return ApiResponse<MallMapDto>.Ok(new MallMapDto
            {
                MallId = mallId, Name = mall.Name, Floors = floorDtos
            });
        }, TimeSpan.FromMinutes(10), ct);
    }

    public async Task<ApiResponse<FloorDto>> GetFloorAsync(
        Guid floorId, CancellationToken ct = default)
    {
        var floor = await _db.Set<MallFloor>()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == floorId, ct);
        if (floor == null) return ApiResponse<FloorDto>.Fail("الطابق غير موجود.");
        return ApiResponse<FloorDto>.Ok(await BuildFloorDto(floor, ct));
    }

    public async Task<ApiResponse> PlaceStoreAsync(
        PlaceStoreRequest req, CancellationToken ct = default)
    {
        var existing = await _db.Set<StoreLocation>()
            .FirstOrDefaultAsync(s => s.StoreId == req.StoreId, ct);

        if (existing != null)
        {
            existing.FloorId  = req.FloorId;
            existing.PosX     = (decimal)req.PosX;
            existing.PosY     = (decimal)req.PosY;
            existing.Width    = (decimal)req.Width;
            existing.Height   = (decimal)req.Height;
            existing.Color    = req.Color ?? existing.Color;
            existing.QrCode   = req.QrCode ?? existing.QrCode;
        }
        else
        {
            _db.Set<StoreLocation>().Add(new StoreLocation
            {
                StoreId = req.StoreId, FloorId = req.FloorId,
                PosX    = (decimal)req.PosX,    PosY = (decimal)req.PosY,
                Width   = (decimal)req.Width,   Height = (decimal)req.Height,
                Color   = req.Color ?? "#3B82F6",
                QrCode  = req.QrCode,
            });
        }

        await _db.SaveChangesAsync(ct);

        // Bust cache
        var floor = await _db.Set<MallFloor>().FindAsync([req.FloorId], ct);
        if (floor != null)
            await _cache.DeleteAsync($"mall:{floor.MallId}:map");

        return ApiResponse.Ok();
    }

    public async Task<ApiResponse<string>> GenerateStoreQrAsync(
        Guid storeId, CancellationToken ct = default)
    {
        var qrContent = $"mallx://store/{storeId}";
        var loc = await _db.Set<StoreLocation>()
            .FirstOrDefaultAsync(s => s.StoreId == storeId, ct);
        if (loc != null)
        {
            loc.QrCode = qrContent;
            await _db.SaveChangesAsync(ct);
        }
        return ApiResponse<string>.Ok(qrContent);
    }

    private async Task<FloorDto> BuildFloorDto(MallFloor floor, CancellationToken ct)
    {
        var storeLocs = await _db.Set<StoreLocation>()
            .AsNoTracking()
            .Where(s => s.FloorId == floor.Id)
            .ToListAsync(ct);

        var storeIds = storeLocs.Select(s => s.StoreId).ToList();
        var stores   = await _db.Tenants.AsNoTracking()
            .Where(t => storeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);

        var ratings  = await _db.Set<Domain.Entities.Phase3.StoreRatingSummary>()
            .AsNoTracking()
            .Where(r => storeIds.Contains(r.StoreId))
            .ToDictionaryAsync(r => r.StoreId, ct);

        var amenities = await _db.Set<MapAmenity>()
            .AsNoTracking()
            .Where(a => a.FloorId == floor.Id)
            .ToListAsync(ct);

        return new FloorDto
        {
            Id       = floor.Id,
            FloorNum = floor.FloorNum,
            Name     = floor.Name,
            NameAr   = floor.NameAr,
            MapSvgUrl= floor.MapSvgUrl,
            Stores   = storeLocs.Select(loc =>
            {
                var s = stores.GetValueOrDefault(loc.StoreId);
                return new StoreLocationDto
                {
                    StoreId   = loc.StoreId,
                    StoreName = s?.Name ?? string.Empty,
                    StoreType = s?.EfProperty<string>("StoreType") ?? "Retail",
                    LogoUrl   = s?.LogoUrl,
                    PosX = (double)loc.PosX, PosY = (double)loc.PosY,
                    Width= (double)loc.Width, Height=(double)loc.Height,
                    Shape= loc.Shape, Color = loc.Color,
                    QrCode   = loc.QrCode,
                    AvgRating= ratings.GetValueOrDefault(loc.StoreId)?.AvgStars ?? 0,
                    IsOpen   = s?.IsActive ?? false,
                };
            }).ToList(),
            Amenities = amenities.Select(a => new AmenityDto
            {
                Id    = a.Id, Type = a.Type, Name = a.Name, NameAr = a.NameAr,
                PosX  = (double)a.PosX, PosY = (double)a.PosY, Icon = a.Icon,
            }).ToList(),
        };
    }
}

// ─── Domain Entities ──────────────────────────────────────────────────────
public class MallFloor
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public Guid   MallId      { get; set; }
    public int    FloorNum    { get; set; }
    public string Name        { get; set; } = string.Empty;
    public string? NameAr     { get; set; }
    public string? MapSvgUrl  { get; set; }
    public bool   IsActive    { get; set; } = true;
    public int    SortOrder   { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class StoreLocation
{
    public Guid    Id       { get; set; } = Guid.NewGuid();
    public Guid    StoreId  { get; set; }
    public Guid    FloorId  { get; set; }
    public decimal PosX     { get; set; }
    public decimal PosY     { get; set; }
    public decimal Width    { get; set; } = 60;
    public decimal Height   { get; set; } = 40;
    public string  Shape    { get; set; } = "rect";
    public string  Color    { get; set; } = "#3B82F6";
    public string? QrCode   { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MapAmenity
{
    public Guid   Id       { get; set; } = Guid.NewGuid();
    public Guid   FloorId  { get; set; }
    public string Type     { get; set; } = string.Empty;
    public string? Name    { get; set; }
    public string? NameAr  { get; set; }
    public decimal PosX    { get; set; }
    public decimal PosY    { get; set; }
    public string? Icon    { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
