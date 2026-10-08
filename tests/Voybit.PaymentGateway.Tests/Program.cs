using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Voybit.PaymentGateway;

var handler = new RecordingHandler
{
    Status = HttpStatusCode.Created,
    Body = """{"id":"pay_1","status":"pending","checkout_url":"https://voybit.com/pay/pub_1","amount_minor":2500,"deposit_instructions":{"address":"TExample"}}"""
};
using (var client = new Client("vb_test_example_secret", "http://127.0.0.1", handler))
{
    var created = await client.CreatePaymentAsync(new CreatePaymentRequest
    {
        AssetId = "asset",
        CryptoAmount = "25.0000",
        AmountMinor = 2500,
        FiatCurrency = "USD",
        Description = "Order 1001",
        Metadata = new Dictionary<string, object?> { ["order_id"] = "1001" }
    }, "order:1001:attempt:1");
    Check(created.Payment.CheckoutUrl == "https://voybit.com/pay/pub_1", "checkout");
    Check(created.Payment.Deposit.Address == "TExample", "deposit");
    Check(handler.Calls == 1, "calls");
    Check(handler.ApiKey == "vb_test_example_secret", "api key");
    Check(handler.Idempotency == "order:1001:attempt:1", "idempotency");
    using var sent = JsonDocument.Parse(handler.RequestBody ?? "{}");
    Check(sent.RootElement.GetProperty("amount_minor").GetInt64() == 2500, "amount");
    Check(!sent.RootElement.TryGetProperty("gateway_id", out _), "omitted gateway");
}

handler = new RecordingHandler
{
    Status = HttpStatusCode.Created,
    Body = """{"session_id":"7155d76a-9f81-40eb-9233-878aac50eb20","public_id":"nYVvXxsYGr5LZk8Dn7hU0Q","status":"open","checkout_url":"https://voybit.com/pay/nYVvXxsYGr5LZk8Dn7hU0Q","fiat_amount":"25","fiat_currency":"USD"}"""
};
using (var client = new Client("vb_test_example_secret", "http://127.0.0.1", handler))
{
    var created = await client.CreateCheckoutSessionAsync(new CreateCheckoutSessionRequest
    {
        FiatAmount = "25.00",
        FiatCurrency = "USD",
        Description = "Order 1001",
        Metadata = new Dictionary<string, object?> { ["order_id"] = "1001" },
        PaymentWindowSeconds = 1800
    }, "order:1001:attempt:1");
    Check(handler.Path == "/gateway/checkout-sessions", "checkout session path");
    Check(created.CheckoutSession.Status == "open", "checkout session status");
    Check(created.CheckoutSession.SessionId == "7155d76a-9f81-40eb-9233-878aac50eb20", "checkout session id");
    using var sent = JsonDocument.Parse(handler.RequestBody ?? "{}");
    Check(!sent.RootElement.TryGetProperty("asset_id", out _), "buyer chooses asset");
    Check(sent.RootElement.GetProperty("fiat_amount").GetString() == "25.00", "fiat amount");
}

handler = new RecordingHandler
{
    Status = HttpStatusCode.UnprocessableEntity,
    Body = """{"error":{"code":"asset_unavailable","message":"That asset is not enabled for this gateway."}}"""
};
using (var client = new Client("vb_test_example_secret", "http://127.0.0.1", handler))
{
    try
    {
        await client.CreatePaymentAsync(new CreatePaymentRequest { AssetId = "asset", CryptoAmount = "1", AmountMinor = 100, FiatCurrency = "USD" }, "order:1001:attempt:1");
        throw new Exception("validation error was not raised");
    }
    catch (VoybitException error)
    {
        Check(error.ErrorCode == "asset_unavailable", "code");
        Check(handler.Calls == 1, "validation calls");
    }
}

var raw = Encoding.UTF8.GetBytes("""{"type":"payment.paid","status":"paid"}""");
var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
var timestamp = "1700000000";
var signature = "v1=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("whsec_example"), Encoding.UTF8.GetBytes($"delivery-1.{timestamp}.").Concat(raw).ToArray())).ToLowerInvariant();
Webhook.Verify("whsec_example", "delivery-1", timestamp, signature, raw, now);
Check(Webhook.Parse(raw).Status == "paid", "status");
try
{
    Webhook.Verify("whsec_example", "delivery-1", timestamp, signature, Encoding.UTF8.GetBytes("""{"type":"payment.paid","status":"paid"} """), now);
    throw new Exception("tampered body was accepted");
}
catch (ArgumentException error)
{
    Check(error.Message.Contains("does not match", StringComparison.Ordinal), "tamper");
}

Console.WriteLine("dotnet ok");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

sealed class RecordingHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    public string? RequestBody { get; private set; }
    public string? ApiKey { get; private set; }
    public string? Idempotency { get; private set; }
    public string? Path { get; private set; }
    public HttpStatusCode Status { get; init; }
    public string Body { get; init; } = "{}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        RequestBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        ApiKey = request.Headers.TryGetValues("X-Voybit-Api-Key", out var key) ? key.FirstOrDefault() : "";
        Idempotency = request.Headers.TryGetValues("Idempotency-Key", out var idempotency) ? idempotency.FirstOrDefault() : "";
        Path = request.RequestUri?.AbsolutePath;
        var response = new HttpResponseMessage(Status)
        {
            Content = new StringContent(Body, Encoding.UTF8, "application/json")
        };
        return response;
    }
}
