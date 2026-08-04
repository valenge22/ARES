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
        return Apply(auth);
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
        accessToken = ""; expiresUtc = default; User = null; tokenStore.Delete();
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
}

public sealed class AuthResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; }
    public AuthenticatedControlUser User { get; set; } = new();
}

public sealed class AuthenticatedControlUser
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
}
