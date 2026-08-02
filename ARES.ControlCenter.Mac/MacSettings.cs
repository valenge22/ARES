using System.Text.Json;

namespace ARES.ControlCenter.Mac;

internal sealed class MacSettings
{
    public string ServerUrl { get; set; } = "https://ares-3bic.onrender.com";
    public string ApiKey { get; set; } = "CAMBIAR-ESTA-CLAVE";
    private static string PathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ARES", "ares.local.json");

    public static MacSettings Load()
    {
        try { return File.Exists(PathName) ? JsonSerializer.Deserialize<MacSettings>(File.ReadAllText(PathName)) ?? new() : new(); }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
