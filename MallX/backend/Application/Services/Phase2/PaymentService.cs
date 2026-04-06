using System.Text;
using System.Text.Json;
using MesterX.Application.DTOs;
using MesterX.Domain.Entities.Mall;
using MesterX.Domain.Entities.Payment;
using MesterX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MesterX.Application.Services.Phase2;

// ──────────────────────────────────────────────────────────────────────────
//  DTOs
// ──────────────────────────────────────────────────────────────────────────
public record InitiatePaymentRequest(
    Guid   MallOrderId,
    string Gateway,           // Cash | Paymob | Fawry | VodafoneCash
    string? CustomerPhone     // for mobile wallets
);

public record PaymentInitiatedDto
{
    public Guid   TransactionId  { get; init; }
    public string Gateway        { get; init; } = string.Empty;
    public string Status         { get; init; } = string.Empty;
    public string? PaymentUrl    { get; init; }     // Paymob iframe URL
    public string? PaymobToken   { get; init; }     // for mobile SDK
    public bool   IsCash         { get; init; }     // Cash = confirm immediately
}

public record PaymobWebhookPayload
{
    public string? OrderId      { get; init; }
    public string? TransactionId{ get; init; }
    public bool    Success      { get; init; }
    public string? ResponseCode { get; init; }
    public string? HmacSha512  { get; init; }
}

// ──────────────────────────────────────────────────────────────────────────
//  SERVICE
// ──────────────────────────────────────────────────────────────────────────
public interface IPaymentService
{
    Task<ApiResponse<PaymentInitiatedDto>> InitiateAsync(
        Guid customerId, InitiatePaymentRequest req, CancellationToken ct = default);

    Task<ApiResponse> ConfirmCashAsync(Guid mallOrderId, CancellationToken ct = default);

    Task<ApiResponse> HandlePaymobWebhookAsync(
        PaymobWebhookPayload payload, string rawBody, CancellationToken ct = default);

    Task<ApiResponse> RefundAsync(
        Guid mallOrderId, decimal amount, string reason, CancellationToken ct = default);
}

public class PaymentService : IPaymentService
{
    private readonly MesterXDbContext _db;
    private readonly IConfiguration   _config;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<PaymentService> _log;

    public PaymentService(MesterXDbContext db, IConfiguration config,
        IHttpClientFactory http, ILogger<PaymentService> log)
    { _db = db; _config = config; _http = http; _log = log; }

    // ─── INITIATE ─────────────────────────────────────────────────────────
    public async Task<ApiResponse<PaymentInitiatedDto>> InitiateAsync(
        Guid customerId, InitiatePaymentRequest req, CancellationToken ct = default)
    {
        var order = await _db.MallOrders
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == req.MallOrderId
                && o.CustomerId == customerId, ct);

        if (order == null)
            return ApiResponse<PaymentInitiatedDto>.Fail("الطلب غير موجود.");

        if (!Enum.TryParse<PaymentGateway>(req.Gateway, out var gateway))
            return ApiResponse<PaymentInitiatedDto>.Fail("بوابة دفع غير صالحة.");

        // Check no active transaction
        var existing = await _db.Set<PaymentTransaction>()
            .AnyAsync(t => t.MallOrderId == req.MallOrderId
                && t.Status == PaymentStatus.Completed, ct);
        if (existing)
            return ApiResponse<PaymentInitiatedDto>.Fail("الطلب مدفوع مسبقاً.");

        var txn = new PaymentTransaction
        {
            MallOrderId = order.Id,
            CustomerId  = customerId,
            Amount      = order.Total,
            Gateway     = gateway,
            Status      = PaymentStatus.Pending,
        };
        _db.Set<PaymentTransaction>().Add(txn);
        await _db.SaveChangesAsync(ct);

        // Cash — confirm immediately
        if (gateway == PaymentGateway.Cash)
        {
            return ApiResponse<PaymentInitiatedDto>.Ok(new PaymentInitiatedDto
            {
                TransactionId = txn.Id,
                Gateway       = "Cash",
                Status        = "Pending",
                IsCash        = true,
            });
        }

        // Paymob integration
        if (gateway == PaymentGateway.Paymob)
        {
            var (url, token, paymobOrderId) = await CreatePaymobOrderAsync(
                order, txn.Id, ct);

            txn.GatewayOrderId = paymobOrderId;
            await _db.SaveChangesAsync(ct);

            return ApiResponse<PaymentInitiatedDto>.Ok(new PaymentInitiatedDto
            {
                TransactionId = txn.Id,
                Gateway       = "Paymob",
                Status        = "Pending",
                PaymentUrl    = url,
                PaymobToken   = token,
                IsCash        = false,
            });
        }

        // Fawry / VodafoneCash - placeholder
        return ApiResponse<PaymentInitiatedDto>.Ok(new PaymentInitiatedDto
        {
            TransactionId = txn.Id,
            Gateway       = req.Gateway,
            Status        = "Pending",
            IsCash        = false,
        });
    }

    // ─── CONFIRM CASH ─────────────────────────────────────────────────────
    public async Task<ApiResponse> ConfirmCashAsync(
        Guid mallOrderId, CancellationToken ct = default)
    {
        var txn = await _db.Set<PaymentTransaction>()
            .Include(t => t.MallOrder)
            .FirstOrDefaultAsync(t => t.MallOrderId == mallOrderId
                && t.Gateway == PaymentGateway.Cash, ct);

        if (txn == null) return ApiResponse.Fail("معاملة دفع غير موجودة.");

        txn.Status    = PaymentStatus.Completed;
        txn.PaidAt    = DateTime.UtcNow;
        txn.UpdatedAt = DateTime.UtcNow;

        txn.MallOrder.PaymentStatus = "Completed";
        txn.MallOrder.UpdatedAt     = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Cash payment confirmed for order {OrderId}", mallOrderId);
        return ApiResponse.Ok();
    }

    // ─── PAYMOB WEBHOOK ───────────────────────────────────────────────────
    public async Task<ApiResponse> HandlePaymobWebhookAsync(
        PaymobWebhookPayload payload, string rawBody, CancellationToken ct = default)
    {
        // Verify HMAC signature
        var hmacSecret = _config["Paymob:HmacSecret"];
        if (!string.IsNullOrEmpty(hmacSecret))
        {
            var valid = VerifyPaymobHmac(rawBody, payload.HmacSha512, hmacSecret);
            if (!valid)
            {
                _log.LogWarning("Paymob webhook HMAC mismatch");
                return ApiResponse.Fail("HMAC غير صالح.");
            }
        }

        var txn = await _db.Set<PaymentTransaction>()
            .Include(t => t.MallOrder)
            .FirstOrDefaultAsync(t => t.GatewayOrderId == payload.OrderId, ct);

        if (txn == null)
        {
            _log.LogWarning("Paymob webhook: order {OrderId} not found", payload.OrderId);
            return ApiResponse.Ok(); // Return 200 to Paymob always
        }

        txn.GatewayTxnId   = payload.TransactionId;
        txn.GatewayResponse= JsonSerializer.Serialize(payload);
        txn.UpdatedAt      = DateTime.UtcNow;

        if (payload.Success)
        {
            txn.Status    = PaymentStatus.Completed;
            txn.PaidAt    = DateTime.UtcNow;
            txn.MallOrder.PaymentStatus = "Completed";
            txn.MallOrder.UpdatedAt     = DateTime.UtcNow;

            _log.LogInformation("Paymob payment success: order {OrderId}, txn {TxnId}",
                payload.OrderId, payload.TransactionId);
        }
        else
        {
            txn.Status        = PaymentStatus.Failed;
            txn.FailureReason = payload.ResponseCode;
            _log.LogWarning("Paymob payment failed: order {OrderId}, code {Code}",
                payload.OrderId, payload.ResponseCode);
        }

        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok();
    }

    // ─── REFUND ───────────────────────────────────────────────────────────
    public async Task<ApiResponse> RefundAsync(
        Guid mallOrderId, decimal amount, string reason, CancellationToken ct = default)
    {
        var txn = await _db.Set<PaymentTransaction>()
            .FirstOrDefaultAsync(t => t.MallOrderId == mallOrderId
                && t.Status == PaymentStatus.Completed, ct);

        if (txn == null) return ApiResponse.Fail("لا توجد معاملة دفع مكتملة لهذا الطلب.");
        if (amount > txn.Amount) return ApiResponse.Fail("مبلغ الاسترداد أكبر من المدفوع.");

        txn.Status        = amount >= txn.Amount
            ? PaymentStatus.Refunded : PaymentStatus.PartialRefund;
        txn.RefundAmount  = amount;
        txn.RefundedAt    = DateTime.UtcNow;
        txn.UpdatedAt     = DateTime.UtcNow;

        // For Paymob, call refund API here (Phase implementation)
        _log.LogInformation("Refund processed: order {OrderId}, amount {Amount}, reason {Reason}",
            mallOrderId, amount, reason);

        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok();
    }

    // ─── PAYMOB HELPERS ───────────────────────────────────────────────────
    private async Task<(string iframeUrl, string token, string orderId)> CreatePaymobOrderAsync(
        MallOrder order, Guid txnId, CancellationToken ct)
    {
        var apiKey     = _config["Paymob:ApiKey"] ?? throw new InvalidOperationException("Paymob API key missing");
        var integrationId = _config["Paymob:IntegrationId"] ?? "0";
        var iframeId   = _config["Paymob:IframeId"] ?? "0";
        var client     = _http.CreateClient("Paymob");

        // Step 1: Auth token
        var authRes = await client.PostAsync("https://accept.paymob.com/api/auth/tokens",
            new StringContent(JsonSerializer.Serialize(new { api_key = apiKey }),
                Encoding.UTF8, "application/json"), ct);
        var authBody = JsonSerializer.Deserialize<JsonElement>(
            await authRes.Content.ReadAsStringAsync(ct));
        var authToken = authBody.GetProperty("token").GetString()!;

        // Step 2: Create order
        var orderPayload = new
        {
            auth_token       = authToken,
            delivery_needed  = false,
            amount_cents     = (int)(order.Total * 100),
            currency         = "EGP",
            merchant_order_id= txnId.ToString(),
            items            = Array.Empty<object>()
        };
        var orderRes = await client.PostAsync("https://accept.paymob.com/api/ecommerce/orders",
            new StringContent(JsonSerializer.Serialize(orderPayload), Encoding.UTF8, "application/json"), ct);
        var orderBody = JsonSerializer.Deserialize<JsonElement>(
            await orderRes.Content.ReadAsStringAsync(ct));
        var paymobOrderId = orderBody.GetProperty("id").GetInt64().ToString();

        // Step 3: Payment key
        var keyPayload = new
        {
            auth_token      = authToken,
            amount_cents    = (int)(order.Total * 100),
            expiration      = 3600,
            order_id        = paymobOrderId,
            billing_data    = new
            {
                apartment       = "N/A", email = order.Customer.Email,
                floor           = "N/A", first_name = order.Customer.FirstName,
                street          = "N/A", building   = "N/A",
                phone_number    = order.Customer.Phone ?? "N/A",
                shipping_method = "NA", postal_code = "N/A",
                city            = "Cairo", country = "EG",
                last_name       = order.Customer.LastName, state = "N/A"
            },
            currency        = "EGP",
            integration_id  = int.Parse(integrationId),
        };
        var keyRes = await client.PostAsync(
            "https://accept.paymob.com/api/acceptance/payment_keys",
            new StringContent(JsonSerializer.Serialize(keyPayload), Encoding.UTF8, "application/json"), ct);
        var keyBody = JsonSerializer.Deserialize<JsonElement>(
            await keyRes.Content.ReadAsStringAsync(ct));
        var paymentToken = keyBody.GetProperty("token").GetString()!;

        var iframeUrl = $"https://accept.paymob.com/api/acceptance/iframes/{iframeId}?payment_token={paymentToken}";
        return (iframeUrl, paymentToken, paymobOrderId);
    }

    private static bool VerifyPaymobHmac(string rawBody, string? signature, string secret)
    {
        if (string.IsNullOrEmpty(signature)) return false;
        using var hmac = new System.Security.Cryptography.HMACSHA512(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody)));
        return string.Equals(computed, signature, StringComparison.OrdinalIgnoreCase);
    }
}
