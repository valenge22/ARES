using ARES.Shared.Modelos;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace ARES.Agent.Servicios;

public sealed class NetworkService : IDisposable
{
    private readonly HttpClient cliente = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly AgentSettings configuracion = AgentSettings.Cargar();
    public event Action<string, bool>? EstadoCambiado;

    public async Task IniciarAsync(CancellationToken cancelacion)
    {
        cliente.DefaultRequestHeaders.Add("X-ARES-Key", configuracion.ApiKey);

        while (!cancelacion.IsCancellationRequested)
        {
            try
            {
                var heartbeat = new AgentHeartbeat
                {
                    Id = ObtenerIdEquipo(),
                    Equipo = Environment.MachineName,
                    Usuario = Environment.UserName,
                    Sistema = Environment.OSVersion.VersionString,
                    Version = "1.0"
                };

                using HttpResponseMessage respuesta = await cliente.PostAsJsonAsync(
                    $"{configuracion.ServerUrl.TrimEnd('/')}/api/agents/heartbeat",
                    heartbeat, cancelacion);
                respuesta.EnsureSuccessStatusCode();
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

    private static string ObtenerIdEquipo()
    {
        string origen = $"{Environment.MachineName}|{Environment.UserDomainName}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(origen));
        return Convert.ToHexString(hash)[..24];
    }

    public void Dispose() => cliente.Dispose();
}
