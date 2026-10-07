using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Voybit.PaymentGateway;

public static class Webhook
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static void Verify(string secret, string id, string timestamp, string signature, byte[] rawBody, DateTimeOffset? now = null)
    {
        var hex = signature is not null && signature.StartsWith("v1=", StringComparison.Ordinal) ? signature[3..] : "";
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(id) || !Digits(timestamp) || rawBody is null || hex.Length != 64 || !Hex(hex))
            throw new ArgumentException("webhook signature is invalid");
        var seconds = long.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture);
        var current = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        if (Math.Abs(current - seconds) > (long)Tolerance.TotalSeconds)
            throw new ArgumentException("webhook timestamp is outside the 5 minute window");

        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            throw new ArgumentException("webhook signature is invalid");
        }

        var prefix = Encoding.UTF8.GetBytes(id + "." + timestamp + ".");
        var signed = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(signed, 0);
        rawBody.CopyTo(signed, prefix.Length);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed);
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            throw new ArgumentException("webhook signature does not match");
    }

    public static Event Parse(byte[] rawBody)
    {
        try
        {
            return JsonSerializer.Deserialize<Event>(rawBody, JsonOptions.Response) ?? throw new ArgumentException("webhook body must be an object");
        }
        catch (JsonException)
        {
            throw new ArgumentException("webhook body must be an object");
        }
    }

    private static bool Digits(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var character in value)
        {
            if (character < '0' || character > '9') return false;
        }
        return true;
    }

    private static bool Hex(string value)
    {
        foreach (var character in value)
        {
            var hex = character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!hex) return false;
        }
        return true;
    }
}
