// ═══════════════════════════════════════════════════════════════════════════
//  MallX — Program.cs (Phase 5 Final)
//  يستبدل الـ Program.cs الموجود في MesterXPro
// ═══════════════════════════════════════════════════════════════════════════
using System.Text;
using System.Threading.RateLimiting;
using MesterX.Application.Services;
using MesterX.Application.Services.Mall;
using MesterX.Application.Services.Phase2;
using MesterX.Application.Services.Phase3;
using MesterX.Application.Services.Phase4;
using MesterX.Application.Services.Phase5;
using MesterX.Hubs;
using MesterX.Infrastructure.BackgroundJobs;
using MesterX.Infrastructure.Data;
using MesterX.API.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;

// ─── LOGGING ──────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/mallx-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
var config = builder.Configuration;

// ─── DATABASE ─────────────────────────────────────────────────────────────
builder.Services.AddDbContext<MesterXDbContext>(opt =>
    opt.UseNpgsql(config.GetConnectionString("DefaultConnection")!, pg =>
    {
        pg.EnableRetryOnFailure(3);
        pg.CommandTimeout(30);
    }));

// ─── REDIS ────────────────────────────────────────────────────────────────
var redisConn = config.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConn));
builder.Services.AddSingleton<ICacheService, RedisCacheService>();
builder.Services.AddSingleton<IRedisRateLimiter, RedisRateLimiter>();

// SignalR with Redis backplane (for multi-instance scaling)
builder.Services.AddSignalR(opt =>
{
    opt.EnableDetailedErrors = builder.Environment.IsDevelopment();
    opt.MaximumReceiveMessageSize = 32 * 1024; // 32KB
    opt.ClientTimeoutInterval      = TimeSpan.FromSeconds(60);
    opt.KeepAliveInterval          = TimeSpan.FromSeconds(15);
}).AddStackExchangeRedis(redisConn, opt =>
{
    opt.Configuration.ChannelPrefix = RedisChannel.Literal("mallx");
});

// ─── JWT AUTH ─────────────────────────────────────────────────────────────
var jwtSecret = config["Jwt:Secret"] ?? throw new InvalidOperationException("JWT Secret not configured");
var key       = Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = config["Jwt:Issuer"],
            ValidAudience            = config["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(key),
            ClockSkew                = TimeSpan.FromSeconds(30)
        };

        // Allow SignalR token from query string
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                var path  = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            },
            OnChallenge = ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode  = 401;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    error   = "غير مصرح. يرجى تسجيل الدخول."
                });
            }
        };
    });

builder.Services.AddAuthorization();

// ─── RATE LIMITING ────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(opt =>
{
    opt.AddFixedWindowLimiter("login", l =>
    {
        l.Window = TimeSpan.FromMinutes(15);
        l.PermitLimit = 10;
        l.QueueLimit  = 0;
    });
    opt.AddFixedWindowLimiter("customer-register", l =>
    {
        l.Window      = TimeSpan.FromMinutes(60);
        l.PermitLimit = 3;
        l.QueueLimit  = 0;
    });
    opt.AddFixedWindowLimiter("checkout", l =>
    {
        l.Window      = TimeSpan.FromMinutes(1);
        l.PermitLimit = 5;
        l.QueueLimit  = 0;
    });
    opt.RejectionStatusCode = 429;
});

// ─── CORS ─────────────────────────────────────────────────────────────────
var origins = config.GetSection("AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000"];

builder.Services.AddCors(opt => opt.AddPolicy("Default", p =>
    p.WithOrigins(origins)
     .AllowAnyMethod()
     .AllowAnyHeader()
     .AllowCredentials()));  // Required for SignalR

// ─── HTTP CLIENT FACTORY ──────────────────────────────────────────────────
builder.Services.AddHttpClient("Paymob");
builder.Services.AddHttpClient("Firebase");

// ─── SERVICES — Phase 1 (MesterXPro base) ─────────────────────────────────
builder.Services.AddScoped<IAuthService,    AuthService>();
builder.Services.AddScoped<IPosService,     PosService>();
builder.Services.AddScoped<ISyncService,    SyncService>();
builder.Services.AddScoped<IAIService,      AIService>();

// ─── SERVICES — Phase 1 (MallX Foundation) ────────────────────────────────
builder.Services.AddScoped<IMallCustomerAuthService, MallCustomerAuthService>();
builder.Services.AddScoped<ICartService,             CartService>();
builder.Services.AddScoped<IMallOrderService,        MallOrderService>();

// ─── SERVICES — Phase 2 (Commission + Payments) ────────────────────────────
builder.Services.AddScoped<ICommissionService, CommissionService>();
builder.Services.AddScoped<IPaymentService,    PaymentService>();

// ─── SERVICES — Phase 3 (Restaurant + Booking + Ratings) ──────────────────
builder.Services.AddScoped<IRestaurantService, RestaurantService>();
builder.Services.AddScoped<IBookingService,    BookingService>();
builder.Services.AddScoped<IRatingService,     RatingService>();

// ─── SERVICES — Phase 4 (Loyalty + Promotions + Push) ─────────────────────
builder.Services.AddScoped<ILoyaltyService,   LoyaltyService>();
builder.Services.AddScoped<IPromotionService, PromotionService>();

// ─── SERVICES — Phase 5 (Caching + Hub Notifier) ──────────────────────────
builder.Services.AddSingleton<CachedProductService>();
builder.Services.AddScoped<IHubNotifier, HubNotifier>();

// ─── BACKGROUND JOBS ──────────────────────────────────────────────────────
builder.Services.AddHostedService<AIRecommendationJob>();       // MesterXPro AI
builder.Services.AddHostedService<MallXBackgroundService>();    // MallX orchestrator
builder.Services.AddHostedService<BookingReminderService>();    // Booking reminders

// ─── CONTROLLERS + SWAGGER ────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title   = "MallX API — Built on MesterXPro",
        Version = "v1",
        Description = "Multi-Vendor Mall Platform"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name        = "Authorization",
        Type        = SecuritySchemeType.Http,
        Scheme      = "Bearer",
        BearerFormat= "JWT",
        In          = ParameterLocation.Header
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement {{
        new OpenApiSecurityScheme {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
        }, Array.Empty<string>()
    }});

    // Group by controller tag
    c.TagActionsBy(api => [api.GroupName ?? api.ActionDescriptor.RouteValues["controller"]!]);
});

builder.Services.AddHealthChecks()
    .AddNpgsql(config.GetConnectionString("DefaultConnection")!)
    .AddRedis(redisConn);

// ─── BUILD ────────────────────────────────────────────────────────────────
var app = builder.Build();

// ─── MIDDLEWARE PIPELINE ──────────────────────────────────────────────────
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseSerilogRequestLogging(opt =>
{
    opt.MessageTemplate = "HTTP {RequestMethod} {RequestPath} → {StatusCode} in {Elapsed:0}ms";
    opt.EnrichDiagnosticContext = (dc, ctx) =>
    {
        dc.Set("RequestHost", ctx.Request.Host.Value);
        dc.Set("UserAgent",   ctx.Request.Headers.UserAgent.ToString());
    };
});

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MallX API v1");
    c.RoutePrefix     = "swagger";
    c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
});

app.UseCors("Default");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantMiddleware>();

// ─── ENDPOINTS ────────────────────────────────────────────────────────────
app.MapControllers();
app.MapHealthChecks("/health");

// SignalR Hub
app.MapHub<MallOrderHub>("/hubs/mall-order");

// ─── DATABASE MIGRATION ───────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MesterXDbContext>();
    try
    {
        db.Database.Migrate();
        Log.Information("✅ Database migrated");
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "⚠️ Migration skipped — run schema.sql manually if first run");
    }
}

Log.Information("🚀 MallX API started — SignalR: /hubs/mall-order");
app.Run();
