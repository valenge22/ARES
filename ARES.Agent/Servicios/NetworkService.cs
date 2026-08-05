using ARES.Shared.Modelos;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using System.Security.Principal;

namespace ARES.Agent.Servicios;

public sealed class NetworkService : IDisposable
{
    private readonly HttpClient cliente = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly AgentSettings configuracion = AgentSettings.Cargar();
    private readonly string agentId = ObtenerIdEquipo();
    private readonly SchedulePolicy schedule = new();
    private string motivoActual = "Iniciando";
    public event Action<string, bool>? EstadoCambiado;
    public event Action<bool>? RestriccionCambiada;

    public async Task IniciarAsync(CancellationToken cancelacion)
    {
        AgregarCredencial(cliente);

        while (!cancelacion.IsCancellationRequested)
        {
            try
            {
                var heartbeat = new AgentHeartbeat
                {
                    Id = agentId,
                    Equipo = Environment.MachineName,
                    Usuario = string.IsNullOrWhiteSpace(configuracion.ManagedUser)
                        ? Environment.UserName
                        : configuracion.ManagedUser,
                    Sistema = Environment.OSVersion.VersionString,
                    Version = typeof(NetworkService).Assembly.GetName().Version?.ToString(3) ?? "1.3.1",
                    BloqueadoLocalmente = LeerEstadoLocal()
                    ,RequestToken = configuracion.RequestToken
                    ,MotivoEstadoLocal = motivoActual
                    ,HorarioVersionAplicada = schedule.Version
                    ,EsServicioSistema = WindowsIdentity.GetCurrent().IsSystem
                };

                using HttpResponseMessage respuesta = await cliente.PostAsJsonAsync(
                    $"{configuracion.ServerUrl.TrimEnd('/')}/api/agents/heartbeat",
                    heartbeat, cancelacion);
                respuesta.EnsureSuccessStatusCode();
                HeartbeatResponse? politica = await respuesta.Content.ReadFromJsonAsync<HeartbeatResponse>(cancellationToken: cancelacion);
                if (politica != null)
                {
                    schedule.Update(politica);
                    PolicyDecision decision = schedule.Evaluate(politica.BloqueadoAdministrativamente, politica.ServerTimeUtc);
                    motivoActual = decision.Reason;
                    RestriccionCambiada?.Invoke(decision.Blocked);
                    if (politica.ActualizarAhora)
                        _ = AgentUpdater.StartAsync(politica.UrlActualizacion, configuracion.ApiKey, configuracion.DeviceCredential);
                }
                EstadoCambiado?.Invoke("Conectado al servidor remoto", true);
            }
            catch (OperationCanceledException) when (cancelacion.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                PolicyDecision local = schedule.Evaluate(null, DateTimeOffset.UtcNow);
                motivoActual = local.Reason;
                RestriccionCambiada?.Invoke(local.Blocked);
                EstadoCambiado?.Invoke($"Sin conexión: {ex.Message}", false);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, configuracion.HeartbeatSeconds)), cancelacion);
        }
    }

    public async Task NotificarCierreAsync()
    {
        try
        {
            using var cierre = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            AgregarCredencial(cierre);
            await cierre.PostAsync($"{configuracion.ServerUrl.TrimEnd('/')}/api/agents/{agentId}/closed", null);
        }
        catch { }
    }

    public async Task<bool> SolicitarDesbloqueoAsync()
    {
        try
        {
            using HttpResponseMessage respuesta = await cliente.PostAsync(
                $"{configuracion.ServerUrl.TrimEnd('/')}/api/agents/{agentId}/unlock-request", null);
            return respuesta.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static string ObtenerIdEquipo()
    {
        // MachineGuid es estable e idéntico para SYSTEM y para el usuario interactivo.
        // UserDomainName producía dos IDs para una misma PC y dividía su estado remoto.
        string machineGuid = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
            "MachineGuid", "")?.ToString() ?? "";
        string origen = string.IsNullOrWhiteSpace(machineGuid)
            ? Environment.MachineName
            : machineGuid;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(origen));
        return Convert.ToHexString(hash)[..24];
    }

    private static bool LeerEstadoLocal()
    {
        try
        {
            string ruta = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ARES", "restriction.state");
            return File.Exists(ruta) &&
                   File.ReadAllText(ruta).Trim().Equals("blocked", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public void Dispose() => cliente.Dispose();

    private void AgregarCredencial(HttpClient client)
    {
        if (!string.IsNullOrWhiteSpace(configuracion.DeviceCredential)) client.DefaultRequestHeaders.Add("X-ARES-Device", configuracion.DeviceCredential);
        else client.DefaultRequestHeaders.Add("X-ARES-Key", configuracion.ApiKey);
    }
}
