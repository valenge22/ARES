using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ARES.Shared.Servicios;

public interface IRefreshTokenStore
{
    string? Load();
    void Save(string token);
    void Delete();
}

public sealed class ControlAuthClient
{
    private readonly Func<string> serverUrl;
    private readonly IRefreshTokenStore tokenStore;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private string accessToken = "";
    private DateTimeOffset expiresUtc;
    public AuthenticatedControlUser? User { get; private set; }
    public bool IsSignedIn => User is not null;
    public bool MfaRequired { get; private set; }
    private string pendingMfaAccessToken = "";
    private string pendingMfaFactorId = "";

    public ControlAuthClient(Func<string> serverUrl, IRefreshTokenStore tokenStore)
    {
        this.serverUrl = serverUrl;
        this.tokenStore = tokenStore;
    }

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        string? token = tokenStore.Load();
        if (string.IsNullOrWhiteSpace(token)) return false;
        return await RefreshAsync(token, cancellationToken);
    }

    public async Task<bool> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using HttpResponseMessage response = await http.PostAsJsonAsync($"{serverUrl().TrimEnd('/')}/api/auth/login",
            new { email, password }, cancellationToken);
        if (!response.IsSuccessStatusCode) return false;
        AuthResponse? auth = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken);
        if (auth?.MfaRequired == true)
        {
            MfaRequired = true; pendingMfaAccessToken = auth.AccessToken; pendingMfaFactorId = auth.FactorId;
            return false;
        }
        return Apply(auth);
    }

    public async Task<bool> CompleteMfaAsync(string code, CancellationToken cancellationToken = default)
    {
        if (!MfaRequired || string.IsNullOrWhiteSpace(pendingMfaAccessToken) || string.IsNullOrWhiteSpace(pendingMfaFactorId)) return false;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using HttpResponseMessage response = await http.PostAsJsonAsync($"{serverUrl().TrimEnd('/')}/api/auth/mfa/verify",
            new { accessToken = pendingMfaAccessToken, factorId = pendingMfaFactorId, code = code.Trim() }, cancellationToken);
        if (!response.IsSuccessStatusCode) return false;
        return Apply(await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken));
    }

    public async Task<(bool Success, string Message)> RegisterAsync(string name, string email, string password, string invitationCode, string organizationName = "", CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using HttpResponseMessage response = await http.PostAsJsonAsync($"{serverUrl().TrimEnd('/')}/api/auth/register",
            new { displayName = name, email, password, invitationCode, organizationName }, cancellationToken);
        ApiMessage? message = await response.Content.ReadFromJsonAsync<ApiMessage>(cancellationToken);
        return (response.IsSuccessStatusCode, message?.Message ?? message?.Error ?? (response.IsSuccessStatusCode ? "Cuenta creada." : "No se pudo crear la cuenta."));
    }

    public async Task RecoverAsync(string email, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using HttpResponseMessage response = await http.PostAsJsonAsync($"{serverUrl().TrimEnd('/')}/api/auth/recover", new { email }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(accessToken) && expiresUtc > DateTimeOffset.UtcNow.AddMinutes(2)) return accessToken;
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(accessToken) && expiresUtc > DateTimeOffset.UtcNow.AddMinutes(2)) return accessToken;
            string? refreshToken = tokenStore.Load();
            if (string.IsNullOrWhiteSpace(refreshToken) || !await RefreshAsync(refreshToken, cancellationToken))
                throw new UnauthorizedAccessException("La sesión venció. Iniciá sesión nuevamente.");
            return accessToken;
        }
        finally { refreshLock.Release(); }
    }

    public void Logout()
    {
        accessToken = ""; expiresUtc = default; User = null; MfaRequired = false; pendingMfaAccessToken = ""; pendingMfaFactorId = ""; tokenStore.Delete();
    }

    public HttpClient CreateHttpClient(TimeSpan? timeout = null) => new(new BearerHandler(this))
    {
        Timeout = timeout ?? TimeSpan.FromSeconds(20)
    };

    private async Task<bool> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using HttpResponseMessage response = await http.PostAsJsonAsync($"{serverUrl().TrimEnd('/')}/api/auth/refresh",
            new { refreshToken }, cancellationToken);
        if (!response.IsSuccessStatusCode) { Logout(); return false; }
        return Apply(await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken));
    }

    private bool Apply(AuthResponse? auth)
    {
        if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken) || string.IsNullOrWhiteSpace(auth.RefreshToken)) return false;
        accessToken = auth.AccessToken;
        expiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, auth.ExpiresIn));
        User = auth.User;
        MfaRequired = false; pendingMfaAccessToken = ""; pendingMfaFactorId = "";
        tokenStore.Save(auth.RefreshToken);
        return true;
    }

    private sealed class BearerHandler(ControlAuthClient auth) : HttpClientHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.GetAccessTokenAsync(cancellationToken));
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class ApiMessage { public string? Message { get; set; } public string? Error { get; set; } }
}

public sealed class AuthResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; }
    public AuthenticatedControlUser User { get; set; } = new();
    public bool MfaRequired { get; set; }
    public string FactorId { get; set; } = "";
}

public sealed class AuthenticatedControlUser
{
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
}

public sealed class OrganizationSetupInfo
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public bool OnboardingCompleted { get; set; }
}

public sealed class RegistrationRequestInfo
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class AdminUserInfo
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public bool Enabled { get; set; }
}

public sealed class InvitationInfo
{
    public Guid InvitationId { get; set; }
    public string CodePrefix { get; set; } = "";
    public int MaxUses { get; set; }
    public int UsedCount { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Revoked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreatedInvitation
{
    public string Code { get; set; } = "";
    public InvitationInfo Invitation { get; set; } = new();
}

public sealed class DeviceEnrollmentInfo
{
    public Guid EnrollmentId { get; set; }
    public string CodePrefix { get; set; } = "";
    public string AssignedGroup { get; set; } = "General";
    public int MaxUses { get; set; }
    public int UsedCount { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Revoked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreatedDeviceEnrollment
{
    public string Code { get; set; } = "";
    public DeviceEnrollmentInfo Enrollment { get; set; } = new();
}
