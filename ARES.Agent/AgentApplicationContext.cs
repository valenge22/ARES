using ARES.Agent.Servicios;
using System.Runtime.InteropServices;

namespace ARES.Agent;

internal sealed class AgentApplicationContext : ApplicationContext
{
    private readonly NotifyIcon icono;
    private readonly NetworkService network = new();
    private readonly CancellationTokenSource cancelacion = new();
    private readonly SynchronizationContext contextoUi;
    private readonly List<RestrictionForm> restricciones = [];
    private readonly System.Windows.Forms.Timer monitorEstadoLocal;

    public AgentApplicationContext()
    {
        contextoUi = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Estado: iniciando…").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Acerca de ARES Agent", null, MostrarInformacion);

        icono = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield,
            Text = "ARES Agent — Iniciando",
            ContextMenuStrip = menu,
            Visible = true
        };
        icono.DoubleClick += MostrarInformacion;
        icono.ShowBalloonTip(3000, "ARES Agent", "El agente está iniciando y permanecerá visible en el área de notificación.", ToolTipIcon.Info);

        monitorEstadoLocal = new System.Windows.Forms.Timer { Interval = 1000 };
        monitorEstadoLocal.Tick += (_, _) => AplicarRestriccion(LeerEstadoLocal());
        monitorEstadoLocal.Start();

        _ = EjecutarServidorAsync();
    }

    private async Task EjecutarServidorAsync()
    {
        try
        {
            network.EstadoCambiado += ActualizarEstado;
            network.RestriccionCambiada += AplicarRestriccion;
            await network.IniciarAsync(cancelacion.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ActualizarEstado($"Error: {ex.Message}", false);
        }
    }

    private void AplicarRestriccion(bool bloqueado)
    {
        contextoUi.Post(_ =>
        {
            if (bloqueado && restricciones.Count == 0)
            {
                foreach (Screen pantalla in Screen.AllScreens)
                {
                    var formulario = new RestrictionForm(pantalla, network.SolicitarDesbloqueoAsync);
                    restricciones.Add(formulario);
                    formulario.Show();
                }

                // Debe invocarse desde el proceso interactivo del empleado. Un proceso
                // SYSTEM en la sesion 0 no puede bloquear visualmente la consola activa.
                LockWorkStation();
            }
            else if (!bloqueado && restricciones.Count > 0)
            {
                foreach (RestrictionForm formulario in restricciones.ToArray())
                    formulario.RetirarRestriccion();
                restricciones.Clear();
            }
        }, null);
    }

    private void ActualizarEstado(string estado, bool activo)
    {
        contextoUi.Post(_ => AplicarEstado(estado, activo), null);
    }

    private void AplicarEstado(string estado, bool activo)
    {
        icono.Text = activo ? "ARES Agent — Activo" : "ARES Agent — Sin conexión";
        icono.ContextMenuStrip!.Items[0].Text = estado;
    }

    private static bool LeerEstadoLocal()
    {
        try
        {
            string ruta = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ARES", "restriction.state");
            return File.Exists(ruta) &&
                   File.ReadAllText(ruta).Trim().Equals("blocked", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void MostrarInformacion(object? sender, EventArgs e)
    {
        MessageBox.Show(
            $"ARES Agent v1.0\n\nEquipo: {Environment.MachineName}\nConexión: servidor remoto HTTPS\n\nEsta aplicación permite administrar este equipo desde un servidor ARES autorizado.",
            "ARES Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            network.NotificarCierreAsync().GetAwaiter().GetResult();
            foreach (RestrictionForm formulario in restricciones.ToArray())
                formulario.RetirarRestriccion();
            cancelacion.Cancel();
            monitorEstadoLocal.Stop();
            monitorEstadoLocal.Dispose();
            cancelacion.Dispose();
            network.Dispose();
            icono.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}
