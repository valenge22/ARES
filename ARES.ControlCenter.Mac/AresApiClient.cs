using ARES.Shared.Modelos;
using System.Net.Http.Json;

namespace ARES.ControlCenter.Mac;

internal sealed class AresApiClient
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
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
}
