using ARES.Shared.Modelos;
using System.Net.Http.Json;

namespace AdministracionEmpleados.Servicios;

public sealed record AgenteDetectado(string Equipo, string Usuario, string DireccionIp, string Sistema);

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
            .Select(a => new AgenteDetectado(a.Equipo, a.Usuario, "Remoto", a.Sistema))
            .ToList();
    }
}
