using ARES.Shared.Modelos;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AdministracionEmpleados.Servicios;

public sealed record AgenteDetectado(string Id, string Equipo, string Usuario, string DireccionIp,
    string Sistema, bool Bloqueado, bool SolicitudDesbloqueo, DateTimeOffset? SolicitudUtc, string Grupo,
    string MotivoBloqueo, DateTimeOffset? ProximoCambioUtc, DateTimeOffset? ExcepcionHastaUtc,
    bool ActualizacionDisponible, string Version, string UltimaVersion, bool HorarioPendiente);

public sealed class AgenteDiscoveryService
{
    private static readonly string sessionId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Environment.MachineName}|{Environment.UserName}|ARES.ControlCenter")))[..24];
    public async Task<IReadOnlyList<AgenteDetectado>> BuscarAsync(
        IEnumerable<string> direccionesConocidas,
        CancellationToken cancelacion = default)
    {
        AresSettings configuracion = AresSettings.Cargar();
        using var cliente = AresControlAuth.Client.CreateHttpClient(TimeSpan.FromSeconds(10));
        using HttpResponseMessage respuesta = await cliente.GetAsync(
            $"{configuracion.ServerUrl.TrimEnd('/')}/api/agents", cancelacion);
        if (respuesta.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("La sesión venció. Iniciá sesión nuevamente.");
        respuesta.EnsureSuccessStatusCode();
        List<AgentStatus>? agentes = await respuesta.Content.ReadFromJsonAsync<List<AgentStatus>>(cancelacion);

        return (agentes ?? [])
            .Where(a => a.EstaEnLinea)
            .Select(a => new AgenteDetectado(a.Id,
                string.IsNullOrWhiteSpace(a.NombrePersonalizado) ? a.Equipo : a.NombrePersonalizado,
                a.Usuario, "Remoto", a.Sistema,
                a.BloqueadoAdministrativamente, a.SolicitudDesbloqueoPendiente, a.SolicitudDesbloqueoUtc, a.Grupo,
                a.MotivoBloqueo, a.ProximoCambioUtc, a.ExcepcionHastaUtc, a.ActualizacionDisponible, a.Version, a.UltimaVersion, a.HorarioPendienteSincronizar))
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

    public async Task EstablecerExcepcionAsync(string agentId, DateTimeOffset hastaUtc, bool permitir, CancellationToken cancelacion = default)
    {
        using HttpClient cliente = CrearCliente();
        using HttpResponseMessage respuesta = await cliente.PutAsJsonAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/agents/{agentId}/override",
            new TemporaryOverrideRequest { HastaUtc = hastaUtc, PermitirUso = permitir, Motivo = "Cambio de ultimo momento desde el panel" }, cancelacion);
        respuesta.EnsureSuccessStatusCode();
    }

    public async Task QuitarExcepcionAsync(string agentId, CancellationToken cancelacion = default)
    {
        using HttpClient cliente = CrearCliente();
        using HttpResponseMessage respuesta = await cliente.DeleteAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/agents/{agentId}/override", cancelacion);
        respuesta.EnsureSuccessStatusCode();
    }

    public async Task SolicitarActualizacionAsync(string agentId, CancellationToken cancelacion = default)
    {
        using HttpClient cliente = CrearCliente();
        using HttpResponseMessage respuesta = await cliente.PostAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/agents/{agentId}/update", null, cancelacion);
        respuesta.EnsureSuccessStatusCode();
    }

    public async Task CargarPaqueteActualizacionAsync(string path, string version, CancellationToken cancelacion = default)
    {
        AresSettings settings = AresSettings.Cargar(); using var cliente = AresControlAuth.Client.CreateHttpClient(TimeSpan.FromMinutes(5));
        using var content = new MultipartFormDataContent();
        await using FileStream stream = File.OpenRead(path); content.Add(new StreamContent(stream), "file", Path.GetFileName(path)); content.Add(new StringContent(version), "version");
        using HttpResponseMessage response = await cliente.PostAsync($"{settings.ServerUrl.TrimEnd('/')}/api/update-package", content, cancelacion); response.EnsureSuccessStatusCode();
    }

    public async Task<List<GroupPolicy>> ObtenerPoliticasGrupoAsync(CancellationToken cancelacion = default)
    {
        using HttpClient cliente = CrearCliente();
        return await cliente.GetFromJsonAsync<List<GroupPolicy>>($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/group-policies", cancelacion) ?? [];
    }

    public async Task GuardarPoliticasGrupoAsync(List<GroupPolicy> policies, CancellationToken cancelacion = default)
    {
        using HttpClient cliente = CrearCliente();
        using HttpResponseMessage response = await cliente.PutAsJsonAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/group-policies", new GroupPoliciesRequest { Grupos = policies }, cancelacion);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<ScheduleRevision>> ObtenerHistorialHorariosAsync(CancellationToken cancelacion = default)
    {
        using HttpClient cliente = CrearCliente();
        return await cliente.GetFromJsonAsync<List<ScheduleRevision>>($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/schedule/history", cancelacion) ?? [];
    }

    public async Task RestaurarHorarioAsync(string revisionId, CancellationToken cancelacion = default)
    {
        using HttpClient cliente = CrearCliente();
        using HttpResponseMessage response = await cliente.PostAsJsonAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/schedule/rollback", new RollbackScheduleRequest { RevisionId = revisionId }, cancelacion);
        response.EnsureSuccessStatusCode();
    }

    private static HttpClient CrearCliente()
    {
        return AresControlAuth.Client.CreateHttpClient(TimeSpan.FromSeconds(20));
    }

    public async Task<ControlSessionHeartbeatResponse> RegistrarSesionPanelAsync(CancellationToken cancelacion = default)
    {
        using HttpClient cliente = CrearCliente();
        using HttpResponseMessage response = await cliente.PostAsJsonAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/control-sessions/heartbeat",
            new ControlSessionHeartbeat { Id = sessionId, Usuario = Environment.UserName, Equipo = Environment.MachineName,
                Plataforma = "Windows", Version = typeof(AgenteDiscoveryService).Assembly.GetName().Version?.ToString(3) ?? "",
                EstadoActualizacion = ControlCenterUpdater.Status }, cancelacion);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ControlSessionHeartbeatResponse>(cancelacion) ?? new();
    }

    public async Task<List<ControlSessionStatus>> ObtenerSesionesPanelAsync(CancellationToken cancelacion = default)
    {
        using HttpClient cliente = CrearCliente();
        return await cliente.GetFromJsonAsync<List<ControlSessionStatus>>($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/control-sessions", cancelacion) ?? [];
    }

    public async Task RenombrarSesionPanelAsync(string id, string nombre, CancellationToken cancelacion = default)
    {
        using HttpClient cliente = CrearCliente();
        using HttpResponseMessage response = await cliente.PutAsJsonAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/control-sessions/{id}/name", new RenameAgentRequest { Nombre = nombre }, cancelacion);
        response.EnsureSuccessStatusCode();
    }

    public async Task CargarPaquetePanelAsync(string platform, string path, CancellationToken cancelacion = default)
    {
        AresSettings settings = AresSettings.Cargar(); using var client = AresControlAuth.Client.CreateHttpClient(TimeSpan.FromMinutes(5));
        using var content = new MultipartFormDataContent();
        await using FileStream stream = File.OpenRead(path); content.Add(new StreamContent(stream), "file", Path.GetFileName(path));
        using HttpResponseMessage response = await client.PostAsync($"{settings.ServerUrl.TrimEnd('/')}/api/control-update/package/{platform}", content, cancelacion); response.EnsureSuccessStatusCode();
    }

    public async Task SolicitarActualizacionPanelesAsync(IEnumerable<string> sessionIds, CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente(); using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/control-update/request", new ControlUpdateRequest { SessionIds = sessionIds.ToList() }, cancelacion);
        response.EnsureSuccessStatusCode();
    }

    public async Task EstablecerRestriccionAsync(string agentId, bool bloqueado, CancellationToken cancelacion = default)
    {
        AresSettings configuracion = AresSettings.Cargar();
        using var cliente = AresControlAuth.Client.CreateHttpClient(TimeSpan.FromSeconds(10));
        using HttpResponseMessage respuesta = await cliente.PutAsJsonAsync(
            $"{configuracion.ServerUrl.TrimEnd('/')}/api/agents/{agentId}/restriction",
            new RestrictionRequest { Bloqueado = bloqueado }, cancelacion);
        if (respuesta.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("La sesión ARES venció.");
        respuesta.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<AgentAuditEvent>> ObtenerAuditoriaAsync(CancellationToken cancelacion = default)
    {
        AresSettings configuracion = AresSettings.Cargar();
        using var cliente = AresControlAuth.Client.CreateHttpClient(TimeSpan.FromSeconds(10));
        return await cliente.GetFromJsonAsync<List<AgentAuditEvent>>(
            $"{configuracion.ServerUrl.TrimEnd('/')}/api/audit", cancelacion) ?? [];
    }

    public async Task BorrarEquiposAsync(CancellationToken cancelacion = default)
    {
        AresSettings configuracion = AresSettings.Cargar();
        using var cliente = AresControlAuth.Client.CreateHttpClient(TimeSpan.FromSeconds(10));
        using HttpResponseMessage respuesta = await cliente.DeleteAsync(
            $"{configuracion.ServerUrl.TrimEnd('/')}/api/agents", cancelacion);
        respuesta.EnsureSuccessStatusCode();
    }
    public async Task RenombrarEquipoAsync(string agentId, string nombre, CancellationToken cancelacion = default)
    {
        AresSettings configuracion = AresSettings.Cargar();
        using var cliente = AresControlAuth.Client.CreateHttpClient(TimeSpan.FromSeconds(10));
        using HttpResponseMessage respuesta = await cliente.PutAsJsonAsync(
            $"{configuracion.ServerUrl.TrimEnd('/')}/api/agents/{agentId}/name",
            new RenameAgentRequest { Nombre = nombre }, cancelacion);
        respuesta.EnsureSuccessStatusCode();
    }
}
