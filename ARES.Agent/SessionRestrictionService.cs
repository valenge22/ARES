using ARES.Agent.Servicios;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ARES.Agent;

/// <summary>
/// Se ejecuta como SYSTEM desde el arranque. Mantiene el canal remoto activo aunque
/// la cuenta administrada este deshabilitada o no haya una sesion iniciada.
/// </summary>
internal sealed class SessionRestrictionService
{
    private readonly AgentSettings settings = AgentSettings.Cargar();
    private readonly NetworkService network = new();
    private bool? estadoAplicado;

    public async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(settings.ManagedUser))
            return;

        network.RestriccionCambiada += Aplicar;
        try
        {
            await network.IniciarAsync(CancellationToken.None);
        }
        finally
        {
            network.Dispose();
        }
    }

    private void Aplicar(bool bloqueado)
    {
        if (estadoAplicado == bloqueado)
            return;

        if (!CambiarEstadoCuenta(settings.ManagedUser, habilitada: !bloqueado))
            return;

        estadoAplicado = bloqueado;
        GuardarEstado(bloqueado);

        if (bloqueado)
            DesconectarSesionesDelUsuario(settings.ManagedUser);
    }

    private static bool CambiarEstadoCuenta(string usuario, bool habilitada)
    {
        // ArgumentList evita interpretar el nombre de cuenta como parte de un comando.
        using var proceso = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "net.exe"),
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        proceso.StartInfo.ArgumentList.Add("user");
        proceso.StartInfo.ArgumentList.Add(usuario);
        proceso.StartInfo.ArgumentList.Add(habilitada ? "/active:yes" : "/active:no");
        proceso.Start();
        proceso.WaitForExit(10_000);
        return proceso.HasExited && proceso.ExitCode == 0;
    }

    private static void DesconectarSesionesDelUsuario(string usuario)
    {
        IntPtr servidor = IntPtr.Zero;
        if (!WTSEnumerateSessions(servidor, 0, 1, out IntPtr sesiones, out int cantidad))
            return;

        try
        {
            int tamano = Marshal.SizeOf<WTS_SESSION_INFO>();
            for (int i = 0; i < cantidad; i++)
            {
                var sesion = Marshal.PtrToStructure<WTS_SESSION_INFO>(sesiones + i * tamano);
                if (ObtenerUsuario(servidor, sesion.SessionId).Equals(usuario, StringComparison.OrdinalIgnoreCase))
                    WTSDisconnectSession(servidor, sesion.SessionId, false);
            }
        }
        finally { WTSFreeMemory(sesiones); }
    }

    private static string ObtenerUsuario(IntPtr servidor, int sessionId)
    {
        if (!WTSQuerySessionInformation(servidor, sessionId, WTS_INFO_CLASS.WTSUserName,
                out IntPtr buffer, out _))
            return "";
        try { return Marshal.PtrToStringUni(buffer) ?? ""; }
        finally { WTSFreeMemory(buffer); }
    }

    private static void GuardarEstado(bool bloqueado)
    {
        string carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ARES");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "restriction.state"), bloqueado ? "blocked" : "unblocked");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WTS_SESSION_INFO
    {
        public int SessionId;
        public IntPtr WinStationName;
        public int State;
    }

    private enum WTS_INFO_CLASS { WTSUserName = 5 }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version,
        out IntPtr sessionInfo, out int count);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId,
        WTS_INFO_CLASS infoClass, out IntPtr buffer, out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSDisconnectSession(IntPtr server, int sessionId, bool wait);
}
