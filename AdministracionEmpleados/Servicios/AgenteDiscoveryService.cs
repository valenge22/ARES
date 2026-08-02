using ARES.Shared.Modelos;
using System.Net.Http.Json;

namespace AdministracionEmpleados.Servicios;

public sealed record AgenteDetectado(string Equipo, string Usuario, string DireccionIp, string Sistema);

public sealed class AgenteDiscoveryService
{
    private readonly AresSettings configuracion = AresSettings.Cargar();

    public async Task<IReadOnlyList<AgenteDetectado>> BuscarAsync(
        IEnumerable<string> direccionesConocidas,
        CancellationToken cancelacion = default)
    {
        using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        cliente.DefaultRequestHeaders.Add("X-ARES-Key", configuracion.ApiKey);
        List<AgentStatus>? agentes = await cliente.GetFromJsonAsync<List<AgentStatus>>(
            $"{configuracion.ServerUrl.TrimEnd('/')}/api/agents", cancelacion);

        return (agentes ?? [])
            .Where(a => a.EstaEnLinea)
            .Select(a => new AgenteDetectado(a.Equipo, a.Usuario, "Remoto", a.Sistema))
            .ToList();
    }
}
