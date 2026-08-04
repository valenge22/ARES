using System.Diagnostics;
using System.IO.Compression;

namespace AdministracionEmpleados;

internal static class ControlCenterUpdater
{
    private static int started;
    public static string Status { get; private set; } = "Al dia";
    public static async Task StartAsync(string url)
    {
        if (Interlocked.Exchange(ref started, 1) != 0 || string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Status = "Descargando";
            AresSettings settings = AresSettings.Cargar(); string root = Path.Combine(Path.GetTempPath(), "ARES-Control-Update");
            if (Directory.Exists(root)) Directory.Delete(root, true); Directory.CreateDirectory(root);
            string zip = Path.Combine(root, "control.zip"); using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.Add("X-ARES-Key", settings.ApiKey); await File.WriteAllBytesAsync(zip, await http.GetByteArrayAsync(url));
            string extracted = Path.Combine(root, "package"); ZipFile.ExtractToDirectory(zip, extracted);
            string source = Path.Combine(extracted, "app"); if (!File.Exists(Path.Combine(source, "ARES.ControlCenter.exe"))) throw new InvalidDataException("Paquete invalido.");
            string destination = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar); string executable = Path.Combine(destination, "ARES.ControlCenter.exe");
            string script = Path.Combine(root, "apply.ps1"); File.WriteAllText(script, $$"""
            $ErrorActionPreference='Stop'
            Stop-Process -Name 'ARES.ControlCenter' -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
            Copy-Item -Path '{{source}}\*' -Destination '{{destination}}' -Recurse -Force
            Start-Process -FilePath '{{executable}}'
            """);
            Status = "Instalando";
            Process.Start(new ProcessStartInfo { FileName = "powershell.exe", UseShellExecute = false, CreateNoWindow = true,
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script } });
        }
        catch { Status = "Error"; Interlocked.Exchange(ref started, 0); }
    }
}
