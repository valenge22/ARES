using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed class MercadoPagoService
{
    private readonly HttpClient http = new() { BaseAddress = new Uri("https://api.mercadopago.com"), Timeout = TimeSpan.FromSeconds(25) };
    private readonly string accessToken;
    private readonly string webhookSecret;
    public decimal UsdArsRate { get; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(accessToken) && UsdArsRate > 0;

    public MercadoPagoService(IConfiguration configuration)
    {
        accessToken = configuration["MERCADOPAGO_ACCESS_TOKEN"] ?? Environment.GetEnvironmentVariable("MERCADOPAGO_ACCESS_TOKEN") ?? "";
        webhookSecret = configuration["MERCADOPAGO_WEBHOOK_SECRET"] ?? Environment.GetEnvironmentVariable("MERCADOPAGO_WEBHOOK_SECRET") ?? "";
        string rate = configuration["ARES_USD_ARS_RATE"] ?? Environment.GetEnvironmentVariable("ARES_USD_ARS_RATE") ?? "0";
        decimal.TryParse(rate, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed); UsdArsRate = parsed;
        if (!string.IsNullOrWhiteSpace(accessToken)) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<MercadoPagoCreateResult> CreateSubscriptionAsync(Guid organizationId, string email, string description,
        decimal amountArs, string returnUrl, string notificationUrl, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return new(null, "Mercado Pago no está configurado.");
        using HttpResponseMessage response = await http.PostAsJsonAsync("/preapproval", new
        {
            reason = description,
            external_reference = organizationId.ToString("D"),
            payer_email = email,
            back_url = returnUrl,
            notification_url = notificationUrl,
            auto_recurring = new { frequency = 1, frequency_type = "months", transaction_amount = decimal.Round(amountArs, 2), currency_id = "ARS" },
            status = "pending"
        }, cancellationToken);
        if (response.IsSuccessStatusCode)
            return new(await ReadSubscriptionAsync(response, cancellationToken), "");
        return new(null, await ReadApiErrorAsync(response, cancellationToken));
    }

    public async Task<MercadoPagoSubscription?> GetSubscriptionAsync(string id, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync($"/preapproval/{Uri.EscapeDataString(id)}", cancellationToken);
        return await ReadSubscriptionAsync(response, cancellationToken);
    }

    public async Task<bool> CancelSubscriptionAsync(string id, CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(id)) return false;
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/preapproval/{Uri.EscapeDataString(id)}") { Content = JsonContent.Create(new { status = "canceled" }) };
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken); return response.IsSuccessStatusCode;
    }

    public async Task<MercadoPagoAuthorizedPayment?> GetAuthorizedPaymentAsync(string id, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return null;
        using HttpResponseMessage response = await http.GetAsync($"/authorized_payments/{Uri.EscapeDataString(id)}", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)); JsonElement root = document.RootElement;
        string paymentStatus = root.TryGetProperty("payment", out JsonElement payment) && payment.TryGetProperty("status", out JsonElement status) ? status.GetString() ?? "" : "";
        DateTimeOffset? debitDate = root.TryGetProperty("debit_date", out JsonElement date) && DateTimeOffset.TryParse(date.GetString(), out DateTimeOffset parsed) ? parsed : null;
        return new(root.TryGetProperty("preapproval_id", out JsonElement subscription) ? subscription.GetString() ?? "" : "", paymentStatus, debitDate);
    }

    public bool ValidateWebhook(string dataId, string requestId, string signature)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret)) return false;
        Dictionary<string,string> parts = signature.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2)).Where(x => x.Length == 2).ToDictionary(x => x[0].Trim(), x => x[1].Trim(), StringComparer.OrdinalIgnoreCase);
        if (!parts.TryGetValue("ts", out string? timestamp) || !parts.TryGetValue("v1", out string? supplied)) return false;
        string manifest = $"id:{dataId.ToLowerInvariant()};request-id:{requestId};ts:{timestamp};";
        string expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(webhookSecret), Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(supplied.ToLowerInvariant()));
    }

    private static async Task<MercadoPagoSubscription?> ReadSubscriptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode) return null;
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)); JsonElement root = document.RootElement;
        decimal amount = root.TryGetProperty("auto_recurring", out JsonElement recurring) && recurring.TryGetProperty("transaction_amount", out JsonElement amountValue) ? amountValue.GetDecimal() : 0;
        return new(root.TryGetProperty("id", out JsonElement id) ? id.GetString() ?? "" : "",
            root.TryGetProperty("status", out JsonElement status) ? status.GetString() ?? "" : "",
            root.TryGetProperty("external_reference", out JsonElement reference) ? reference.GetString() ?? "" : "",
            root.TryGetProperty("init_point", out JsonElement point) ? point.GetString() ?? "" : "", amount);
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string fallback = $"Mercado Pago rechazó la solicitud ({(int)response.StatusCode}).";
        try
        {
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            string message = root.TryGetProperty("message", out JsonElement messageValue) ? messageValue.GetString() ?? "" : "";
            string code = root.TryGetProperty("error", out JsonElement errorValue) ? errorValue.ToString() : "";
            if (root.TryGetProperty("cause", out JsonElement causes) && causes.ValueKind == JsonValueKind.Array)
            {
                string cause = string.Join("; ", causes.EnumerateArray().Select(item =>
                {
                    string itemCode = item.TryGetProperty("code", out JsonElement c) ? c.ToString() : "";
                    string description = item.TryGetProperty("description", out JsonElement d) ? d.GetString() ?? "" : "";
                    return string.Join(": ", new[] { itemCode, description }.Where(x => !string.IsNullOrWhiteSpace(x)));
                }).Where(x => !string.IsNullOrWhiteSpace(x)));
                if (!string.IsNullOrWhiteSpace(cause)) message = string.Join(" — ", new[] { message, cause }.Where(x => !string.IsNullOrWhiteSpace(x)));
            }
            string detail = string.Join(": ", new[] { code, message }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return string.IsNullOrWhiteSpace(detail) ? fallback : $"Mercado Pago: {detail}";
        }
        catch
        {
            return fallback;
        }
    }
}

internal sealed record MercadoPagoCreateResult(MercadoPagoSubscription? Subscription, string Error);
internal sealed record MercadoPagoSubscription(string Id, string Status, string ExternalReference, string CheckoutUrl, decimal AmountArs);
internal sealed record MercadoPagoAuthorizedPayment(string SubscriptionId, string PaymentStatus, DateTimeOffset? DebitDate);
internal sealed record BillingCheckoutRequest(string Plan, int AdditionalDevices, int AdditionalPanelUsers);
