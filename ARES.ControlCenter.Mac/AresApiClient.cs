using ARES.Shared.Modelos;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace ARES.ControlCenter.Mac;

internal sealed class AresApiClient
{
    private static readonly string sessionId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Environment.MachineName}|{Environment.UserName}|ARES.ControlCenter.Mac")))[..24];
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private MacSettings settings;
    public AresApiClient(MacSettings settings) => this.settings = settings;
    public void Update(MacSettings value) => settings = value;

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"{settings.ServerUrl.TrimEnd('/')}{path}");
        request.Headers.Add("X-ARES-Key", settings.ApiKey);
        return request;
    }
    public async Task<List<AgentStatus>> AgentsAsync()
    {
        using var request = Request(HttpMethod.Get, "/api/agents");
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<AgentStatus>>() ?? [];
    }
    public async Task RestrictAsync(string id, bool blocked)
    {
        using var request = Request(HttpMethod.Put, $"/api/agents/{Uri.EscapeDataString(id)}/restriction");
        request.Content = JsonContent.Create(new RestrictionRequest { Bloqueado = blocked });
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
    public async Task<List<AgentAuditEvent>> AuditAsync()
    {
        using var request = Request(HttpMethod.Get, "/api/audit");
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<AgentAuditEvent>>() ?? [];
    }
    public async Task ClearAgentsAsync()
    {
        using var request = Request(HttpMethod.Delete, "/api/agents");
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
    public async Task RenameAgentAsync(string id, string name)
    {
        using var request = Request(HttpMethod.Put, $"/api/agents/{Uri.EscapeDataString(id)}/name");
        request.Content = JsonContent.Create(new RenameAgentRequest { Nombre = name });
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
    public async Task SetGroupAsync(string id, string group)
    {
        using var request = Request(HttpMethod.Put, $"/api/agents/{Uri.EscapeDataString(id)}/group"); request.Content = JsonContent.Create(new GroupRequest { Grupo = group });
        using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode();
    }
    public async Task OverrideAsync(string id, DateTimeOffset untilUtc)
    {
        using var request = Request(HttpMethod.Put, $"/api/agents/{Uri.EscapeDataString(id)}/override"); request.Content = JsonContent.Create(new TemporaryOverrideRequest { PermitirUso = true, HastaUtc = untilUtc, Motivo = "Excepcion desde panel macOS" });
        using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode();
    }
    public async Task UpdateAgentAsync(string id)
    {
        using var request = Request(HttpMethod.Post, $"/api/agents/{Uri.EscapeDataString(id)}/update"); using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode();
    }
    public async Task<ScheduleState> ScheduleAsync()
    {
        using var request = Request(HttpMethod.Get, "/api/schedule"); using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ScheduleState>() ?? new();
    }
    public async Task<ControlSessionHeartbeatResponse> HeartbeatControlSessionAsync()
    {
        using var request = Request(HttpMethod.Post, "/api/control-sessions/heartbeat");
        request.Content = JsonContent.Create(new ControlSessionHeartbeat { Id = sessionId, Usuario = Environment.UserName,
            Equipo = Environment.MachineName, Plataforma = "macOS", Version = typeof(AresApiClient).Assembly.GetName().Version?.ToString(3) ?? "",
            EstadoActualizacion = MacControlUpdater.Status });
        using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode();
        ControlSessionHeartbeatResponse policy = await response.Content.ReadFromJsonAsync<ControlSessionHeartbeatResponse>() ?? new();
        if (policy.ActualizarAhora) _ = MacControlUpdater.StartAsync(policy.Url, settings.ApiKey);
        return policy;
    }
    public async Task<List<ControlSessionStatus>> ControlSessionsAsync()
    {
        using var request = Request(HttpMethod.Get, "/api/control-sessions"); using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ControlSessionStatus>>() ?? [];
    }
    public async Task RenameControlSessionAsync(string id, string name)
    {
        using var request = Request(HttpMethod.Put, $"/api/control-sessions/{Uri.EscapeDataString(id)}/name"); request.Content = JsonContent.Create(new RenameAgentRequest { Nombre = name });
        using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode();
    }
    public async Task UploadControlPackageAsync(string platform, Stream stream, string fileName)
    {
        using var request = Request(HttpMethod.Post, $"/api/control-update/package/{platform}"); using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(stream), "file", fileName); request.Content = content; using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode();
    }
    public async Task RequestControlUpdatesAsync(IEnumerable<string> ids)
    {
        using var request = Request(HttpMethod.Post, "/api/control-update/request"); request.Content = JsonContent.Create(new ControlUpdateRequest { SessionIds = ids.ToList() });
        using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode();
    }
}
