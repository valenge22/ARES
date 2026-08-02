using ARES.Shared.Modelos;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace ARES.Agent.Servicios;

public sealed class NetworkService : IDisposable
{
    private readonly HttpClient cliente = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly AgentSettings configuracion = AgentSettings.Cargar();
    private readonly string agentId = ObtenerIdEquipo();
    public event Action<string, bool>? EstadoCambiado;
    public event Action<bool>? RestriccionCambiada;

    public async Task IniciarAsync(CancellationToken cancelacion)
    {
        cliente.DefaultRequestHeaders.Add("X-ARES-Key", configuracion.ApiKey);

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
                };

                using HttpResponseMessage respuesta = await cliente.PostAsJsonAsync(
                    $"{configuracion.ServerUrl.TrimEnd('/')}/api/agents/heartbeat",
                    heartbeat, cancelacion);
                respuesta.EnsureSuccessStatusCode();
                HeartbeatResponse? politica = await respuesta.Content.ReadFromJsonAsync<HeartbeatResponse>(cancellationToken: cancelacion);
                if (politica != null)
                    RestriccionCambiada?.Invoke(politica.BloqueadoAdministrativamente);
                EstadoCambiado?.Invoke("Conectado al servidor remoto", true);
            }
            catch (OperationCanceledException) when (cancelacion.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
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
            cierre.DefaultRequestHeaders.Add("X-ARES-Key", configuracion.ApiKey);
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
        string origen = $"{Environment.MachineName}|{Environment.UserDomainName}";
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
}
