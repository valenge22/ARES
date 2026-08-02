using ARES.Shared.Modelos;
using System.Text.Json;

namespace AdministracionEmpleados.Servicios;

public sealed class AuditCacheService
{
    private readonly string ruta = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ARES", "audit-cache.json");

    public IReadOnlyList<AgentAuditEvent> Cargar()
    {
        try
        {
            if (!File.Exists(ruta)) return [];
            return JsonSerializer.Deserialize<List<AgentAuditEvent>>(File.ReadAllText(ruta),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<AgentAuditEvent> CombinarYGuardar(IEnumerable<AgentAuditEvent> eventosRemotos)
    {
        List<AgentAuditEvent> combinados = Cargar()
            .Concat(eventosRemotos)
            .GroupBy(evento => evento.Id, StringComparer.OrdinalIgnoreCase)
            .Select(grupo => grupo.OrderByDescending(evento => evento.FechaUtc).First())
            .OrderByDescending(evento => evento.FechaUtc)
            .Take(5000)
            .ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        string temporal = ruta + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(combinados,
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporal, ruta, true);
        return combinados;
    }
}
