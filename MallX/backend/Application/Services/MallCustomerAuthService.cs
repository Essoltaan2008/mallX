using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MesterX.Application.DTOs;
using MesterX.Application.DTOs.Mall;
using MesterX.Domain.Entities.Mall;
using MesterX.Infrastructure.Data;
using MesterX.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace MesterX.Application.Services.Mall;

public interface IMallCustomerAuthService
{
    Task<ApiResponse<CustomerAuthResponse>> RegisterAsync(CustomerRegisterRequest req, string ip, CancellationToken ct = default);
    Task<ApiResponse<CustomerAuthResponse>> LoginAsync(CustomerLoginRequest req, string ip, string ua, CancellationToken ct = default);
    Task<ApiResponse<CustomerAuthResponse>> RefreshAsync(string rawToken, string ip, CancellationToken ct = default);
    Task<ApiResponse> LogoutAsync(string rawToken, CancellationToken ct = default);
    Task<ApiResponse<CustomerProfileDto>> GetProfileAsync(Guid customerId, CancellationToken ct = default);
}

public class MallCustomerAuthService : IMallCustomerAuthService
{
    private readonly MesterXDbContext _db;
    private readonly IConfiguration   _config;
    private readonly ILogger<MallCustomerAuthService> _log;

    private const int MAX_FAILED  = 5;
    private const int LOCKOUT_MIN = 15;

    public MallCustomerAuthService(MesterXDbContext db, IConfiguration config,
        ILogger<MallCustomerAuthService> log)
    { _db = db; _config = config; _log = log; }

    // ─── REGISTER ─────────────────────────────────────────────────────────
    public async Task<ApiResponse<CustomerAuthResponse>> RegisterAsync(
        CustomerRegisterRequest req, string ip, CancellationToken ct = default)
    {
        // Resolve mall
        var mall = await _db.Malls
            .FirstOrDefaultAsync(m => m.Slug == req.MallSlug && m.IsActive, ct);
        if (mall == null)
            return ApiResponse<CustomerAuthResponse>.Fail("المول غير موجود.");

        // Unique email
        var exists = await _db.MallCustomers
            .AnyAsync(c => c.Email == req.Email.ToLowerInvariant() && !c.IsDeleted, ct);
        if (exists)
            return ApiResponse<CustomerAuthResponse>.Fail("البريد الإلكتروني مسجل مسبقاً.");

        // Validate password strength
        if (req.Password.Length < 8)
            return ApiResponse<CustomerAuthResponse>.Fail("كلمة المرور يجب أن تكون 8 أحرف على الأقل.");

        var customer = new MallCustomer
        {
            MallId       = mall.Id,
            FirstName    = req.FirstName.Trim(),
            LastName     = req.LastName.Trim(),
            Email        = req.Email.ToLowerInvariant().Trim(),
            Phone        = req.Phone?.Trim(),
            PasswordHash = SecurityHelper.HashPassword(req.Password),
        };

        _db.MallCustomers.Add(customer);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("New customer registered: {Email} in mall {Mall}", customer.Email, mall.Slug);
        return await BuildAuthResponseAsync(customer, ip, "registration", ct);
    }

    // ─── LOGIN ────────────────────────────────────────────────────────────
    public async Task<ApiResponse<CustomerAuthResponse>> LoginAsync(
        CustomerLoginRequest req, string ip, string ua, CancellationToken ct = default)
    {
        var mall = await _db.Malls
            .FirstOrDefaultAsync(m => m.Slug == req.MallSlug && m.IsActive, ct);
        if (mall == null)
            return ApiResponse<CustomerAuthResponse>.Fail("بيانات الدخول غير صحيحة.");

        var customer = await _db.MallCustomers
            .Include(c => c.RefreshTokens)
            .FirstOrDefaultAsync(c => c.Email == req.Email.ToLowerInvariant()
                && c.MallId == mall.Id && !c.IsDeleted, ct);

        if (customer == null || !customer.IsActive)
            return ApiResponse<CustomerAuthResponse>.Fail("بيانات الدخول غير صحيحة.");

        if (customer.IsLocked)
            return ApiResponse<CustomerAuthResponse>.Fail(
                $"الحساب مقفل حتى {customer.LockoutEnd:HH:mm}. حاول لاحقاً.");

        if (!SecurityHelper.VerifyPassword(req.Password, customer.PasswordHash))
        {
            customer.FailedAttempts++;
            if (customer.FailedAttempts >= MAX_FAILED)
                customer.LockoutEnd = DateTime.UtcNow.AddMinutes(LOCKOUT_MIN);
            customer.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return ApiResponse<CustomerAuthResponse>.Fail("بيانات الدخول غير صحيحة.");
        }

        customer.FailedAttempts  = 0;
        customer.LockoutEnd      = null;
        customer.LastActivityAt  = DateTime.UtcNow;
        customer.UpdatedAt       = DateTime.UtcNow;

        return await BuildAuthResponseAsync(customer, ip, ua, ct);
    }

    // ─── REFRESH ──────────────────────────────────────────────────────────
    public async Task<ApiResponse<CustomerAuthResponse>> RefreshAsync(
        string rawToken, string ip, CancellationToken ct = default)
    {
        var allTokens = await _db.CustomerRefreshTokens
            .Include(t => t.Customer)
            .Where(t => !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);

        var match = allTokens.FirstOrDefault(t =>
            SecurityHelper.VerifyRefreshToken(rawToken, t.TokenHash, t.TokenSalt));

        if (match == null)
            return ApiResponse<CustomerAuthResponse>.Fail("رمز التحديث غير صالح.");

        match.IsRevoked = true; // Rotate
        await _db.SaveChangesAsync(ct);

        return await BuildAuthResponseAsync(match.Customer, ip, "refresh", ct);
    }

    // ─── LOGOUT ───────────────────────────────────────────────────────────
    public async Task<ApiResponse> LogoutAsync(string rawToken, CancellationToken ct = default)
    {
        var tokens = await _db.CustomerRefreshTokens
            .Where(t => !t.IsRevoked)
            .ToListAsync(ct);

        var match = tokens.FirstOrDefault(t =>
            SecurityHelper.VerifyRefreshToken(rawToken, t.TokenHash, t.TokenSalt));

        if (match != null)
        {
            match.IsRevoked = true;
            await _db.SaveChangesAsync(ct);
        }
        return ApiResponse.Ok();
    }

    // ─── GET PROFILE ──────────────────────────────────────────────────────
    public async Task<ApiResponse<CustomerProfileDto>> GetProfileAsync(
        Guid customerId, CancellationToken ct = default)
    {
        var customer = await _db.MallCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId && !c.IsDeleted, ct);

        return customer == null
            ? ApiResponse<CustomerProfileDto>.Fail("العميل غير موجود.")
            : ApiResponse<CustomerProfileDto>.Ok(CustomerProfileDto.From(customer));
    }

    // ─── PRIVATE: Build JWT + Refresh Token ───────────────────────────────
    private async Task<ApiResponse<CustomerAuthResponse>> BuildAuthResponseAsync(
        MallCustomer customer, string ip, string ua, CancellationToken ct)
    {
        var (access, expiry) = GenerateAccessToken(customer);
        var (raw, hash, salt) = SecurityHelper.GenerateRefreshToken();

        _db.CustomerRefreshTokens.Add(new CustomerRefreshToken
        {
            CustomerId = customer.Id,
            TokenHash  = hash,
            TokenSalt  = salt,
            DeviceInfo = ua?[..Math.Min(ua.Length, 200)],
            IpAddress  = ip,
            ExpiresAt  = DateTime.UtcNow.AddDays(30)
        });
        await _db.SaveChangesAsync(ct);

        return ApiResponse<CustomerAuthResponse>.Ok(new CustomerAuthResponse
        {
            AccessToken  = access,
            RefreshToken = raw,
            ExpiresAt    = expiry,
            Customer     = CustomerProfileDto.From(customer)
        });
    }

    private (string token, DateTime expiry) GenerateAccessToken(MallCustomer customer)
    {
        var secret  = _config["Jwt:Secret"] ?? throw new InvalidOperationException("JWT not configured");
        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var expiry  = DateTime.UtcNow.AddMinutes(
            int.Parse(_config["Jwt:CustomerExpiryMinutes"] ?? "60"));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, customer.Id.ToString()),
            new Claim(ClaimTypes.Email,          customer.Email),
            new Claim(ClaimTypes.GivenName,      customer.FirstName),
            new Claim("customer_tier",            customer.Tier.ToString()),
            new Claim("mall_id",                  customer.MallId.ToString()),
            new Claim("token_type",               "customer"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer:   _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims:   claims,
            expires:  expiry,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiry);
    }
}
