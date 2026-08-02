using System.Text.Json;

namespace AdministracionEmpleados;

internal sealed class AresSettings
{
    public string ServerUrl { get; set; } = "https://ares-3bic.onrender.com";
    public string ApiKey { get; set; } = "CAMBIAR-ESTA-CLAVE";

    public static AresSettings Cargar()
    {
        string ruta = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(ruta)) return new AresSettings();
        return JsonSerializer.Deserialize<AresSettings>(File.ReadAllText(ruta),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AresSettings();
    }
}
