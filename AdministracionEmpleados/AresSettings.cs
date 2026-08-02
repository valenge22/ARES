using System.Text.Json;

namespace AdministracionEmpleados;

internal sealed class AresSettings
{
    private const string NombreConfiguracionLocal = "ares.local.json";
    public string ServerUrl { get; set; } = "https://ares-3bic.onrender.com";
    public string ApiKey { get; set; } = "CAMBIAR-ESTA-CLAVE";

    public static AresSettings Cargar()
    {
        string local = RutaConfiguracionLocal();
        string general = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        string? ruta = File.Exists(local) ? local : File.Exists(general) ? general : null;
        if (ruta == null) return new AresSettings();
        return JsonSerializer.Deserialize<AresSettings>(File.ReadAllText(ruta), OpcionesJson()) ?? new AresSettings();
    }

    public void GuardarLocal()
    {
        string ruta = RutaConfiguracionLocal();
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonSerializerOptions OpcionesJson() => new() { PropertyNameCaseInsensitive = true };
    private static string RutaConfiguracionLocal() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ARES", NombreConfiguracionLocal);
}
