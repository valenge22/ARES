using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class AresAuthService
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string? supabaseUrl;
    private readonly string? anonKey;
    private readonly AresPersistence persistence;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(supabaseUrl) && !string.IsNullOrWhiteSpace(anonKey);

    public AresAuthService(IConfiguration configuration, AresPersistence persistence)
    {
        supabaseUrl = (configuration["SUPABASE_URL"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL"))?.TrimEnd('/');
        anonKey = configuration["SUPABASE_ANON_KEY"] ?? Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY");
        this.persistence = persistence;
    }

    public Task<AuthResult?> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
        RequestTokenAsync("password", new { email, password }, cancellationToken);
    public Task<AuthResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        RequestTokenAsync("refresh_token", new { refresh_token = refreshToken }, cancellationToken);

    public async Task<AuthenticatedAdmin?> ValidateAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(accessToken)) return null;
        using var request = CreateRequest(HttpMethod.Get, "/auth/v1/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("id", out JsonElement idElement) || !Guid.TryParse(idElement.GetString(), out Guid id)) return null;
        AdminUser? admin = await persistence.GetAdminAsync(id);
        if (admin is null || !admin.Enabled) return null;
        string email = document.RootElement.TryGetProperty("email", out JsonElement item) ? item.GetString() ?? "" : "";
        await persistence.UpdateAdminEmailAsync(admin.UserId, email);
        return new(admin.UserId, admin.OrganizationId, email, admin.DisplayName, admin.Role);
    }

    public async Task<Guid?> SignUpAsync(string email, string password, string displayName, string redirectUrl, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Supabase Auth no esta configurado en Render.");
        using var request = CreateRequest(HttpMethod.Post, $"/auth/v1/signup?redirect_to={Uri.EscapeDataString(redirectUrl)}");
        request.Content = JsonContent.Create(new { email, password, data = new { display_name = displayName } });
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        JsonElement root = document.RootElement;
        JsonElement user = root.TryGetProperty("user", out JsonElement nested) ? nested : root;
        return user.TryGetProperty("id", out JsonElement id) && Guid.TryParse(id.GetString(), out Guid value) ? value : null;
    }

    public async Task<bool> RecoverAsync(string email, string redirectUrl, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return false;
        using var request = CreateRequest(HttpMethod.Post, $"/auth/v1/recover?redirect_to={Uri.EscapeDataString(redirectUrl)}");
        request.Content = JsonContent.Create(new { email });
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdatePasswordAsync(string accessToken, string password, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, "/auth/v1/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { password });
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<AuthResult?> RequestTokenAsync(string grantType, object body, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Supabase Auth no esta configurado en Render.");
        using var request = CreateRequest(HttpMethod.Post, $"/auth/v1/token?grant_type={grantType}");
        request.Content = JsonContent.Create(body);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        AuthTokenPayload? token = await response.Content.ReadFromJsonAsync<AuthTokenPayload>(cancellationToken);
        if (token?.User is null || !Guid.TryParse(token.User.Id, out Guid userId)) return null;
        AdminUser? admin = await persistence.GetAdminAsync(userId);
        if (admin is null || !admin.Enabled) return null;
        await persistence.UpdateAdminEmailAsync(admin.UserId, token.User.Email ?? "");
        return new(token.AccessToken, token.RefreshToken, token.ExpiresIn,
            new(admin.UserId, admin.OrganizationId, token.User.Email ?? admin.Email, admin.DisplayName, admin.Role));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"{supabaseUrl}{path}");
        request.Headers.Add("apikey", anonKey);
        return request;
    }

    private sealed class AuthTokenPayload
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = "";
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
        public AuthUser? User { get; set; }
    }
    private sealed class AuthUser { public string Id { get; set; } = ""; public string? Email { get; set; } }
}

internal sealed record AuthenticatedAdmin(Guid UserId, Guid OrganizationId, string Email, string DisplayName, string Role);
internal sealed record AuthResult(string AccessToken, string RefreshToken, int ExpiresIn, AuthenticatedAdmin User);
internal sealed record LoginRequest(string Email, string Password);
internal sealed record RefreshRequest(string RefreshToken);
internal sealed record RegisterRequest(string DisplayName, string Email, string Password, string InvitationCode);
internal sealed record RecoverRequest(string Email);
internal sealed record UpdatePasswordRequest(string AccessToken, string Password);
internal sealed record ApproveRegistrationRequest(string Role);
internal sealed record UpdateAdminRequest(string Role, bool Enabled);
internal sealed record CreateInvitationRequest(int MaxUses, int DurationHours, string Role = "Viewer");
internal sealed record CreateDeviceEnrollmentRequest(int MaxUses, int DurationHours, string Group = "Grupo 1");
internal sealed record EnrollDeviceRequest(string Code, string DeviceId, string MachineName);
