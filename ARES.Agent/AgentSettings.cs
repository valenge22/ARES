using System.Text.Json;

namespace ARES.Agent;

internal sealed class AgentSettings
{
    public string ServerUrl { get; set; } = "https://ares-3bic.onrender.com";
    public string ApiKey { get; set; } = "CAMBIAR-ESTA-CLAVE";
    public string DeviceCredential { get; set; } = "";
    public int HeartbeatSeconds { get; set; } = 10;
    public string ManagedUser { get; set; } = "";
    public string RequestToken { get; set; } = "";

    public static AgentSettings Cargar()
    {
        string ruta = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(ruta)) return new AgentSettings();
        return JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(ruta),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AgentSettings();
    }
}
