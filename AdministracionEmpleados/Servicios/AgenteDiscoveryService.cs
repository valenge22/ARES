using ARES.Shared.Modelos;
using ARES.Shared.Servicios;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AdministracionEmpleados.Servicios;

public sealed record AgenteDetectado(string Id, string Equipo, string Usuario, string DireccionIp,
    string Sistema, bool Bloqueado, bool SolicitudDesbloqueo, DateTimeOffset? SolicitudUtc, string Grupo,
    string MotivoBloqueo, DateTimeOffset? ProximoCambioUtc, DateTimeOffset? ExcepcionHastaUtc,
    bool ActualizacionDisponible, string Version, string UltimaVersion, bool HorarioPendiente, bool CredencialIndividual);

public sealed class AgenteDiscoveryService
{
    private static readonly string sessionId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Environment.MachineName}|{Environment.UserName}|ARES.ControlCenter")))[..24];
    public async Task<OrganizationSetupInfo?> ObtenerConfiguracionInicialAsync(CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente();
        using HttpResponseMessage response = await client.GetAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/onboarding", cancelacion);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound || response.Content.Headers.ContentLength == 0) return null;
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancelacion);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<OrganizationSetupInfo>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    public async Task CompletarConfiguracionInicialAsync(CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente(); using HttpResponseMessage response = await client.PostAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/onboarding/complete", null, cancelacion);
        response.EnsureSuccessStatusCode();
    }
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
                a.MotivoBloqueo, a.ProximoCambioUtc, a.ExcepcionHastaUtc, a.ActualizacionDisponible, a.Version, a.UltimaVersion, a.HorarioPendienteSincronizar, a.CredencialIndividual))
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
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync(cancelacion);
            try { detail = JsonDocument.Parse(detail).RootElement.GetProperty("error").GetString() ?? detail; } catch { }
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? $"No se pudieron guardar los grupos ({(int)response.StatusCode})." : detail);
        }
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

    public async Task<List<RegistrationRequestInfo>> ObtenerSolicitudesRegistroAsync(CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente();
        return await client.GetFromJsonAsync<List<RegistrationRequestInfo>>($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/admin/registrations", cancelacion) ?? [];
    }

    public async Task<List<AdminUserInfo>> ObtenerUsuariosPanelAsync(CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente();
        return await client.GetFromJsonAsync<List<AdminUserInfo>>($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/admin/users", cancelacion) ?? [];
    }

    public async Task AprobarRegistroAsync(Guid id, string role, CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente(); using HttpResponseMessage response = await client.PostAsJsonAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/admin/registrations/{id}/approve", new { role }, cancelacion); response.EnsureSuccessStatusCode();
    }

    public async Task RechazarRegistroAsync(Guid id, CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente(); using HttpResponseMessage response = await client.PostAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/admin/registrations/{id}/reject", null, cancelacion); response.EnsureSuccessStatusCode();
    }

    public async Task ActualizarUsuarioPanelAsync(Guid id, string role, bool enabled, CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente(); using HttpResponseMessage response = await client.PutAsJsonAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/admin/users/{id}", new { role, enabled }, cancelacion); response.EnsureSuccessStatusCode();
    }
    public async Task EliminarUsuarioPanelAsync(Guid id, CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente(); using HttpResponseMessage response = await client.DeleteAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/admin/users/{id}", cancelacion); response.EnsureSuccessStatusCode();
    }
    public async Task<List<InvitationInfo>> ObtenerInvitacionesAsync(CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente(); return await client.GetFromJsonAsync<List<InvitationInfo>>($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/admin/invitations", cancelacion) ?? [];
    }
    public async Task<CreatedInvitation> CrearInvitacionAsync(int maxUses, int durationHours, string role, CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente(); using HttpResponseMessage response = await client.PostAsJsonAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/admin/invitations", new { maxUses, durationHours, role }, cancelacion); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreatedInvitation>(cancelacion) ?? throw new InvalidDataException("Respuesta de invitación inválida.");
    }
    public async Task RevocarInvitacionAsync(Guid id, CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente(); using HttpResponseMessage response = await client.DeleteAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/admin/invitations/{id}", cancelacion); response.EnsureSuccessStatusCode();
    }
    public async Task<CreatedDeviceEnrollment> CrearVinculacionEquipoAsync(int maxUses, int durationHours, string group, CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente(); using HttpResponseMessage response = await client.PostAsJsonAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/admin/device-enrollments", new { maxUses, durationHours, group }, cancelacion); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreatedDeviceEnrollment>(cancelacion) ?? throw new InvalidDataException("Respuesta de vinculación inválida.");
    }
    public async Task RenovarCredencialEquipoAsync(string id, CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente();
        using HttpResponseMessage response = await client.PostAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/admin/devices/{Uri.EscapeDataString(id)}/rotate", null, cancelacion);
        response.EnsureSuccessStatusCode();
    }
    public async Task RevocarCredencialEquipoAsync(string id, CancellationToken cancelacion = default)
    {
        using HttpClient client = CrearCliente();
        using HttpResponseMessage response = await client.DeleteAsync($"{AresSettings.Cargar().ServerUrl.TrimEnd('/')}/api/admin/devices/{Uri.EscapeDataString(id)}", cancelacion);
        response.EnsureSuccessStatusCode();
    }
}
