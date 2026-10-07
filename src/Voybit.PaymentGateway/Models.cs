using System.Text.Json;
using System.Text.Json.Serialization;

namespace Voybit.PaymentGateway;

public sealed class CreatePaymentRequest
{
    [JsonPropertyName("asset_id")]
    public string AssetId { get; set; } = "";

    [JsonPropertyName("crypto_amount")]
    public string CryptoAmount { get; set; } = "";

    [JsonPropertyName("amount_minor")]
    public long AmountMinor { get; set; }

    [JsonPropertyName("fiat_currency")]
    public string FiatCurrency { get; set; } = "";

    [JsonPropertyName("gateway_id")]
    public string? GatewayId { get; set; }

    [JsonPropertyName("expires_in_seconds")]
    public long? ExpiresInSeconds { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

public sealed class Payment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("public_id")]
    public string PublicId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("checkout_url")]
    public string CheckoutUrl { get; set; } = "";

    [JsonPropertyName("amount_minor")]
    public long AmountMinor { get; set; }

    [JsonPropertyName("fiat_currency")]
    public string FiatCurrency { get; set; } = "";

    [JsonPropertyName("crypto_asset")]
    public string CryptoAsset { get; set; } = "";

    [JsonPropertyName("crypto_network")]
    public string CryptoNetwork { get; set; } = "";

    [JsonPropertyName("expected_amount")]
    public string ExpectedAmount { get; set; } = "";

    [JsonPropertyName("received_amount")]
    public string ReceivedAmount { get; set; } = "";

    [JsonPropertyName("deposit_instructions")]
    public Deposit Deposit { get; set; } = new();
}

public sealed class Deposit
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("payment_uri")]
    public string? Uri { get; set; }

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("asset")]
    public string? Asset { get; set; }

    [JsonPropertyName("network")]
    public string? Network { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class CreatedPayment
{
    public CreatedPayment(Payment payment, bool replayed, string requestId)
    {
        Payment = payment;
        Replayed = replayed;
        RequestId = requestId;
    }

    public Payment Payment { get; }
    public bool Replayed { get; }
    public string RequestId { get; }
}

public sealed class Event
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("payment_id")]
    public string PaymentId { get; set; } = "";

    [JsonPropertyName("public_id")]
    public string PublicId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("checkout_url")]
    public string CheckoutUrl { get; set; } = "";
}

public sealed class VoybitException : Exception
{
    public VoybitException(int status, string errorCode, string message, string requestId)
        : this(status, errorCode, message, requestId, TimeSpan.Zero)
    {
    }

    internal VoybitException(int status, string errorCode, string message, string requestId, TimeSpan retryAfter)
        : base(string.IsNullOrWhiteSpace(message) ? $"payment gateway returned HTTP {status}" : message)
    {
        Status = status;
        ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "unknown_error" : errorCode;
        RequestId = requestId ?? "";
        RetryAfter = retryAfter;
    }

    public int Status { get; }
    public string ErrorCode { get; }
    public string RequestId { get; }
    internal TimeSpan RetryAfter { get; }
}

static class JsonOptions
{
    public static readonly JsonSerializerOptions Request = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly JsonSerializerOptions Response = new();
}
