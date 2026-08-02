using ARES.Shared.Modelos;
using System.Net.Http.Json;

namespace AdministracionEmpleados.Servicios;

public sealed record AgenteDetectado(string Id, string Equipo, string Usuario, string DireccionIp,
    string Sistema, bool Bloqueado, bool SolicitudDesbloqueo, DateTimeOffset? SolicitudUtc);

public sealed class AgenteDiscoveryService
{
    public async Task<IReadOnlyList<AgenteDetectado>> BuscarAsync(
        IEnumerable<string> direccionesConocidas,
        CancellationToken cancelacion = default)
    {
        AresSettings configuracion = AresSettings.Cargar();
        using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        cliente.DefaultRequestHeaders.Add("X-ARES-Key", configuracion.ApiKey);
        using HttpResponseMessage respuesta = await cliente.GetAsync(
            $"{configuracion.ServerUrl.TrimEnd('/')}/api/agents", cancelacion);
        if (respuesta.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("La clave ARES no coincide con la configurada en Render.");
        respuesta.EnsureSuccessStatusCode();
        List<AgentStatus>? agentes = await respuesta.Content.ReadFromJsonAsync<List<AgentStatus>>(cancelacion);

        return (agentes ?? [])
            .Where(a => a.EstaEnLinea)
            .Select(a => new AgenteDetectado(a.Id, a.Equipo, a.Usuario, "Remoto", a.Sistema,
                a.BloqueadoAdministrativamente, a.SolicitudDesbloqueoPendiente, a.SolicitudDesbloqueoUtc))
            .ToList();
    }

    public async Task EstablecerRestriccionAsync(string agentId, bool bloqueado, CancellationToken cancelacion = default)
    {
        AresSettings configuracion = AresSettings.Cargar();
        using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        cliente.DefaultRequestHeaders.Add("X-ARES-Key", configuracion.ApiKey);
        using HttpResponseMessage respuesta = await cliente.PutAsJsonAsync(
            $"{configuracion.ServerUrl.TrimEnd('/')}/api/agents/{agentId}/restriction",
            new RestrictionRequest { Bloqueado = bloqueado }, cancelacion);
        if (respuesta.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("La clave ARES no coincide con Render.");
        respuesta.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<AgentAuditEvent>> ObtenerAuditoriaAsync(CancellationToken cancelacion = default)
    {
        AresSettings configuracion = AresSettings.Cargar();
        using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        cliente.DefaultRequestHeaders.Add("X-ARES-Key", configuracion.ApiKey);
        return await cliente.GetFromJsonAsync<List<AgentAuditEvent>>(
            $"{configuracion.ServerUrl.TrimEnd('/')}/api/audit", cancelacion) ?? [];
    }
}
