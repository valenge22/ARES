using System.Diagnostics;
using System.IO.Compression;

namespace ARES.Agent;

internal static class AgentUpdater
{
    private static int started;
    public static async Task StartAsync(string url, string apiKey, string deviceCredential)
    {
        if (Interlocked.Exchange(ref started, 1) != 0 || string.IsNullOrWhiteSpace(url)) return;
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ARES", "update");
            if (Directory.Exists(root)) Directory.Delete(root, true); Directory.CreateDirectory(root);
            string zip = Path.Combine(root, "agent.zip");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            if (!string.IsNullOrWhiteSpace(deviceCredential)) http.DefaultRequestHeaders.Add("X-ARES-Device", deviceCredential);
            else http.DefaultRequestHeaders.Add("X-ARES-Key", apiKey);
            await File.WriteAllBytesAsync(zip, await http.GetByteArrayAsync(url));
            string extracted = Path.Combine(root, "package"); ZipFile.ExtractToDirectory(zip, extracted);
            string source = Path.Combine(extracted, "app");
            if (!File.Exists(Path.Combine(source, "ARES.Agent.exe"))) throw new InvalidDataException("Paquete de actualizacion invalido.");
            string destination = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string script = Path.Combine(root, "apply-update.ps1");
            File.WriteAllText(script, $$"""
            $ErrorActionPreference='Stop'
            Stop-ScheduledTask -TaskName 'ARES Agent' -ErrorAction SilentlyContinue
            Stop-ScheduledTask -TaskName 'ARES Agent Service' -ErrorAction SilentlyContinue
            Stop-Process -Name 'ARES.Agent' -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 3
            Copy-Item -Path '{{source}}\*' -Destination '{{destination}}' -Recurse -Force
            Start-ScheduledTask -TaskName 'ARES Agent Service' -ErrorAction SilentlyContinue
            Start-ScheduledTask -TaskName 'ARES Agent' -ErrorAction SilentlyContinue
            """);
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe", UseShellExecute = false, CreateNoWindow = true,
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }
            });
        }
        catch { Interlocked.Exchange(ref started, 0); }
    }
}
