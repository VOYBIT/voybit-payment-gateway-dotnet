using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Voybit.PaymentGateway;

public sealed class Client : IDisposable
{
    public const string DefaultBaseUrl = "https://api.voybit.com/api/v1";
    private const string UserAgent = "voybit-payment-gateway-dotnet/0.1.0";
    private static readonly System.Text.RegularExpressions.Regex Idempotency = new("^[A-Za-z0-9][A-Za-z0-9._:-]{7,127}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public Client(string apiKey, string? baseUrl = null, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));
        _apiKey = apiKey;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/');
        handler ??= new SocketsHttpHandler { AllowAutoRedirect = false };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<CreatedPayment> CreatePaymentAsync(CreatePaymentRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrEmpty(idempotencyKey) || !Idempotency.IsMatch(idempotencyKey))
            throw new ArgumentException("Idempotency-Key must be 8 to 128 URL-safe characters.", nameof(idempotencyKey));

        var json = JsonSerializer.Serialize(request, JsonOptions.Request);
        Exception? last = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                return await PostAsync(json, idempotencyKey, cancellationToken).ConfigureAwait(false);
            }
            catch (VoybitException error) when (IsRetryable(error.Status) && attempt < 3 && !cancellationToken.IsCancellationRequested)
            {
                last = error;
                await Task.Delay(Delay(attempt, error.RetryAfter), cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException error) when (attempt < 3 && !cancellationToken.IsCancellationRequested)
            {
                last = error;
                await Task.Delay(Delay(attempt, TimeSpan.Zero), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 3)
            {
                last = new TimeoutException("payment gateway request timed out");
                await Task.Delay(Delay(attempt, TimeSpan.Zero), cancellationToken).ConfigureAwait(false);
            }
        }

        throw last ?? new HttpRequestException("payment gateway request failed");
    }

    public void Dispose() => _http.Dispose();

    private async Task<CreatedPayment> PostAsync(string json, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(TimeSpan.FromSeconds(20));
        using var message = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/gateway/payments");
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Content = content;
        message.Headers.TryAddWithoutValidation("X-Voybit-Api-Key", _apiKey);
        message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        message.Headers.Accept.ParseAdd("application/json");
        message.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

        using var response = await _http.SendAsync(message, attempt.Token).ConfigureAwait(false);
        var raw = await response.Content.ReadAsByteArrayAsync(attempt.Token).ConfigureAwait(false);
        if (raw.Length > 1 << 20)
            throw new VoybitException((int)response.StatusCode, "body_too_large", "response was too large", RequestId(response));
        var requestId = RequestId(response);
        if (response.IsSuccessStatusCode)
        {
            Payment payment;
            try
            {
                payment = raw.Length == 0
                    ? new Payment()
                    : JsonSerializer.Deserialize<Payment>(raw, JsonOptions.Response) ?? new Payment();
            }
            catch (JsonException)
            {
                throw new VoybitException((int)response.StatusCode, "invalid_response", "response was not JSON", requestId);
            }

            var replayed = response.Headers.TryGetValues("Idempotency-Replayed", out var values) && values.Contains("true");
            return new CreatedPayment(payment, replayed, requestId);
        }

        string code = "unknown_error";
        string messageText = "";
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("code", out var codeValue)) code = codeValue.GetString() ?? code;
                if (error.TryGetProperty("message", out var messageValue)) messageText = messageValue.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
        }

        var retryAfter = TimeSpan.Zero;
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            retryAfter = delta > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delta;
        throw new VoybitException((int)response.StatusCode, code, messageText, requestId, retryAfter);
    }

    private static string RequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Request-ID", out var values) ? values.FirstOrDefault() ?? "" : "";

    private static bool IsRetryable(int status) => status is 408 or 429 or 500 or 502 or 503 or 504;

    private static TimeSpan Delay(int attempt, TimeSpan retryAfter)
    {
        if (retryAfter > TimeSpan.Zero) return retryAfter;
        var milliseconds = Math.Min(500 * (1 << attempt), 8000);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
