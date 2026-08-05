using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class AresAuthService
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string? supabaseUrl;
    private readonly string? anonKey;
    private readonly string? serviceRoleKey;
    private readonly AresPersistence persistence;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(supabaseUrl) && !string.IsNullOrWhiteSpace(anonKey);
    public bool RecoveryConfigured => IsConfigured && !string.IsNullOrWhiteSpace(serviceRoleKey);

    public AresAuthService(IConfiguration configuration, AresPersistence persistence)
    {
        supabaseUrl = (configuration["SUPABASE_URL"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL"))?.TrimEnd('/');
        anonKey = configuration["SUPABASE_ANON_KEY"] ?? Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY");
        serviceRoleKey = configuration["SUPABASE_SERVICE_ROLE_KEY"] ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY");
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
        if (VerifiedFactorId(document.RootElement) is not null && ReadAal(accessToken) != "aal2") return null;
        AdminUser? admin = await persistence.GetAdminAsync(id);
        if (admin is null || !admin.Enabled) return null;
        string email = document.RootElement.TryGetProperty("email", out JsonElement item) ? item.GetString() ?? "" : "";
        await persistence.UpdateAdminEmailAsync(admin.UserId, email);
        return new(admin.UserId, admin.OrganizationId, email, admin.DisplayName, admin.Role);
    }

    public async Task<SignUpResult> SignUpAsync(string email, string password, string displayName, string redirectUrl, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Supabase Auth no esta configurado en Render.");
        using var request = CreateRequest(HttpMethod.Post, $"/auth/v1/signup?redirect_to={Uri.EscapeDataString(redirectUrl)}");
        request.Content = JsonContent.Create(new { email, password, data = new { display_name = displayName } });
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string code = ""; string message = "Supabase rechazó el registro.";
            try
            {
                using JsonDocument error = JsonDocument.Parse(payload);
                if (error.RootElement.TryGetProperty("error_code", out JsonElement errorCode)) code = errorCode.ToString();
                else if (error.RootElement.TryGetProperty("code", out JsonElement codeElement)) code = codeElement.ToString();
                if (error.RootElement.TryGetProperty("msg", out JsonElement msg)) message = msg.GetString() ?? message;
                else if (error.RootElement.TryGetProperty("message", out JsonElement detail)) message = detail.GetString() ?? message;
                else if (error.RootElement.TryGetProperty("error_description", out JsonElement description)) message = description.GetString() ?? message;
                else if (error.RootElement.TryGetProperty("error", out JsonElement generic)) message = generic.ToString();
            }
            catch { }
            return new(null, code, message);
        }
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        JsonElement user = root.TryGetProperty("user", out JsonElement nested) ? nested : root;
        return user.TryGetProperty("id", out JsonElement id) && Guid.TryParse(id.GetString(), out Guid value)
            ? new(value, "", "") : new(null, "invalid_signup_response", "Supabase no devolvió la identidad del usuario.");
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
        string? factorId = await GetVerifiedFactorIdAsync(token.AccessToken, cancellationToken);
        bool mfaRequired = factorId is not null && ReadAal(token.AccessToken) != "aal2";
        return new(token.AccessToken, token.RefreshToken, token.ExpiresIn,
            new(admin.UserId, admin.OrganizationId, token.User.Email ?? admin.Email, admin.DisplayName, admin.Role), mfaRequired, factorId ?? "");
    }

    public async Task<JsonElement?> EnrollMfaAsync(string accessToken, CancellationToken cancellationToken)
        => await MfaJsonAsync(HttpMethod.Post, "/auth/v1/factors", accessToken, new { factor_type = "totp", friendly_name = "ARES Authenticator" }, cancellationToken);

    public async Task<JsonElement?> ListMfaAsync(string accessToken, CancellationToken cancellationToken)
        => await MfaJsonAsync(HttpMethod.Get, "/auth/v1/user", accessToken, null, cancellationToken);

    public async Task<AuthResult?> VerifyMfaAsync(string accessToken, string factorId, string code, CancellationToken cancellationToken)
    {
        JsonElement? challenge = await MfaJsonAsync(HttpMethod.Post, $"/auth/v1/factors/{Uri.EscapeDataString(factorId)}/challenge", accessToken, new { }, cancellationToken);
        if (!challenge.HasValue || !challenge.Value.TryGetProperty("id", out JsonElement challengeId)) return null;
        JsonElement? verified = await MfaJsonAsync(HttpMethod.Post, $"/auth/v1/factors/{Uri.EscapeDataString(factorId)}/verify", accessToken,
            new { challenge_id = challengeId.GetString(), code }, cancellationToken);
        if (!verified.HasValue) return null;
        AuthTokenPayload? token = verified.Value.Deserialize<AuthTokenPayload>();
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken)) return null;
        if (token.User is null)
        {
            using var userRequest = CreateRequest(HttpMethod.Get, "/auth/v1/user"); userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using HttpResponseMessage userResponse = await http.SendAsync(userRequest, cancellationToken);
            if (!userResponse.IsSuccessStatusCode) return null;
            token.User = await userResponse.Content.ReadFromJsonAsync<AuthUser>(cancellationToken);
        }
        if (token.User is null || !Guid.TryParse(token.User.Id, out Guid userId)) return null;
        AdminUser? admin = await persistence.GetAdminAsync(userId); if (admin is null || !admin.Enabled) return null;
        return new(token.AccessToken, token.RefreshToken, token.ExpiresIn,
            new(admin.UserId, admin.OrganizationId, token.User.Email ?? admin.Email, admin.DisplayName, admin.Role), false, "");
    }

    public async Task<bool> UnenrollMfaAsync(string accessToken, string factorId, CancellationToken cancellationToken)
        => (await MfaJsonAsync(HttpMethod.Delete, $"/auth/v1/factors/{Uri.EscapeDataString(factorId)}", accessToken, null, cancellationToken)).HasValue;

    public async Task<(Guid UserId, AuthenticatedAdmin Admin)?> IdentifyForRecoveryAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(accessToken)) return null;
        using var request = CreateRequest(HttpMethod.Get, "/auth/v1/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("id", out JsonElement id) || !Guid.TryParse(id.GetString(), out Guid userId)) return null;
        AdminUser? admin = await persistence.GetAdminAsync(userId); if (admin is null || !admin.Enabled) return null;
        string email = document.RootElement.TryGetProperty("email", out JsonElement emailValue) ? emailValue.GetString() ?? admin.Email : admin.Email;
        return (userId, new AuthenticatedAdmin(admin.UserId, admin.OrganizationId, email, admin.DisplayName, admin.Role));
    }

    public async Task<bool> RemoveMfaFactorAsAdminAsync(Guid userId, string factorId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceRoleKey) || string.IsNullOrWhiteSpace(factorId)) return false;
        using var request = new HttpRequestMessage(HttpMethod.Delete,
            $"{supabaseUrl}/auth/v1/admin/users/{userId:D}/factors/{Uri.EscapeDataString(factorId)}");
        request.Headers.Add("apikey", serviceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<string?> GetVerifiedFactorIdAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "/auth/v1/user"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken); if (!response.IsSuccessStatusCode) return null;
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return VerifiedFactorId(document.RootElement);
    }

    private async Task<JsonElement?> MfaJsonAsync(HttpMethod method, string path, string accessToken, object? body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken); if (!response.IsSuccessStatusCode) return null;
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)); return document.RootElement.Clone();
    }

    private static string? VerifiedFactorId(JsonElement user)
    {
        string? found = null;
        void Visit(JsonElement value)
        {
            if (found is not null) return;
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement factor in value.EnumerateArray()) Visit(factor);
                return;
            }
            if (value.ValueKind != JsonValueKind.Object) return;
            if (value.TryGetProperty("id", out JsonElement id) &&
                value.TryGetProperty("status", out JsonElement status) && status.GetString() == "verified" &&
                (!value.TryGetProperty("factor_type", out JsonElement type) || type.GetString() == "totp"))
            {
                found = id.GetString();
                return;
            }
            foreach (string property in new[] { "factors", "all", "totp" })
                if (value.TryGetProperty(property, out JsonElement nested)) Visit(nested);
        }
        Visit(user);
        return found;
    }

    private static string ReadAal(string jwt)
    {
        try
        {
            string part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/'); part = part.PadRight((part.Length + 3) / 4 * 4, '=');
            using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(part));
            return document.RootElement.TryGetProperty("aal", out JsonElement aal) ? aal.GetString() ?? "aal1" : "aal1";
        }
        catch { return "aal1"; }
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
        [JsonPropertyName("user")]
        public AuthUser? User { get; set; }
    }
    private sealed class AuthUser { [JsonPropertyName("id")] public string Id { get; set; } = ""; [JsonPropertyName("email")] public string? Email { get; set; } }
}

internal sealed record AuthenticatedAdmin(Guid UserId, Guid OrganizationId, string Email, string DisplayName, string Role);
internal sealed record SignUpResult(Guid? UserId, string ErrorCode, string ErrorMessage);
internal sealed record AuthResult(string AccessToken, string RefreshToken, int ExpiresIn, AuthenticatedAdmin User, bool MfaRequired = false, string FactorId = "");
internal sealed record LoginRequest(string Email, string Password);
internal sealed record RefreshRequest(string RefreshToken);
internal sealed record RegisterRequest(string DisplayName, string Email, string Password, string InvitationCode, string OrganizationName);
internal sealed record RecoverRequest(string Email);
internal sealed record UpdatePasswordRequest(string AccessToken, string Password);
internal sealed record ChangePasswordRequest(string Password);
internal sealed record MfaVerifyRequest(string AccessToken, string FactorId, string Code);
internal sealed record MfaRecoveryRequest(string AccessToken, string RefreshToken, string FactorId, string RecoveryCode);
internal sealed record MfaFactorRequest(string FactorId);
internal sealed record ApproveRegistrationRequest(string Role);
internal sealed record UpdateAdminRequest(string Role, bool Enabled);
internal sealed record CreateInvitationRequest(int MaxUses, int DurationHours, string Role = "Operator");
internal sealed record CreateDeviceEnrollmentRequest(int MaxUses, int DurationHours, string Group = "General");
internal sealed record UpdateOrganizationRequest(string Name);
internal sealed record UpdateLicenseRequest(string Plan, string Status, int MaxDevices, DateTimeOffset? ExpiresAt, int GraceDays);
internal sealed record EnrollDeviceRequest(string Code, string DeviceId, string MachineName);
