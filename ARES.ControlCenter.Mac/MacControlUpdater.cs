using System.Diagnostics;

namespace ARES.ControlCenter.Mac;

internal static class MacControlUpdater
{
    private static int started;
    public static string Status { get; private set; } = "Al dia";
    public static async Task StartAsync(string url, string apiKey)
    {
        if (Interlocked.Exchange(ref started, 1) != 0 || string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Status = "Descargando";
            string path = Path.Combine(Path.GetTempPath(), "ARES-Centro-Control-Update.pkg");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; http.DefaultRequestHeaders.Add("X-ARES-Key", apiKey);
            await File.WriteAllBytesAsync(path, await http.GetByteArrayAsync(url));
            Status = "Instalando";
            Process.Start(new ProcessStartInfo { FileName = "open", UseShellExecute = false, ArgumentList = { path } });
        }
        catch { Status = "Error"; Interlocked.Exchange(ref started, 0); }
    }
}
