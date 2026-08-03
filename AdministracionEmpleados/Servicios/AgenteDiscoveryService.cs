using ARES.Shared.Modelos;
using System.Net.Http.Json;

namespace AdministracionEmpleados.Servicios;

public sealed record AgenteDetectado(string Id, string Equipo, string Usuario, string DireccionIp,
    string Sistema, bool Bloqueado, bool SolicitudDesbloqueo, DateTimeOffset? SolicitudUtc, string Grupo);

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
            .Select(a => new AgenteDetectado(a.Id,
                string.IsNullOrWhiteSpace(a.NombrePersonalizado) ? a.Equipo : a.NombrePersonalizado,
                a.Usuario, "Remoto", a.Sistema,
                a.BloqueadoAdministrativamente, a.SolicitudDesbloqueoPendiente, a.SolicitudDesbloqueoUtc, a.Grupo))
            .ToList();
    }
    public async Task EstablecerGrupoAsync(string agentId, string grupo, CancellationToken cancelacion = default)
    {
        using HttpClient cliente = CrearCliente();
        using HttpResponseMessage respuesta = await cliente.PutAsJsonAsync(
            $"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/agents/{agentId}/group", new GroupRequest { Grupo = grupo }, cancelacion);
        respuesta.EnsureSuccessStatusCode();
    }

    public async Task<ScheduleState> ObtenerHorariosAsync(CancellationToken cancelacion = default)
    {
        using HttpClient cliente = CrearCliente();
        return await cliente.GetFromJsonAsync<ScheduleState>($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/schedule", cancelacion) ?? new();
    }

    public async Task PublicarHorariosAsync(SchedulePublication horarios, CancellationToken cancelacion = default)
    {
        using HttpClient cliente = CrearCliente();
        using HttpResponseMessage respuesta = await cliente.PutAsJsonAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/schedule", horarios, cancelacion);
        respuesta.EnsureSuccessStatusCode();
    }

    private static HttpClient CrearCliente()
    {
        AresSettings settings = AresSettings.Cargar();
        var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        cliente.DefaultRequestHeaders.Add("X-ARES-Key", settings.ApiKey);
        return cliente;
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

    public async Task BorrarEquiposAsync(CancellationToken cancelacion = default)
    {
        AresSettings configuracion = AresSettings.Cargar();
        using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        cliente.DefaultRequestHeaders.Add("X-ARES-Key", configuracion.ApiKey);
        using HttpResponseMessage respuesta = await cliente.DeleteAsync(
            $"{configuracion.ServerUrl.TrimEnd('/')}/api/agents", cancelacion);
        respuesta.EnsureSuccessStatusCode();
    }
    public async Task RenombrarEquipoAsync(string agentId, string nombre, CancellationToken cancelacion = default)
    {
        AresSettings configuracion = AresSettings.Cargar();
        using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        cliente.DefaultRequestHeaders.Add("X-ARES-Key", configuracion.ApiKey);
        using HttpResponseMessage respuesta = await cliente.PutAsJsonAsync(
            $"{configuracion.ServerUrl.TrimEnd('/')}/api/agents/{agentId}/name",
            new RenameAgentRequest { Nombre = nombre }, cancelacion);
        respuesta.EnsureSuccessStatusCode();
    }
}
