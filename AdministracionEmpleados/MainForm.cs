using AdministracionEmpleados.Servicios;
using ARES.Shared.Modelos;
using ARES.Shared.Servicios;

namespace AdministracionEmpleados
{
    public partial class MainForm : Form
    {
        private static void DibujarEscudoMarca(object? sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddLines([new PointF(19, 3), new PointF(32, 8), new PointF(31, 21), new PointF(27, 29), new PointF(19, 35), new PointF(11, 29), new PointF(7, 21), new PointF(6, 8), new PointF(19, 3)]);
            using var fill = new SolidBrush(Color.FromArgb(14, 165, 233));
            using var pen = new Pen(Color.FromArgb(125, 211, 252), 2.2F) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(pen, path);
        }
        private readonly EmpleadoService empleadoService = new();
        private readonly AgenteDiscoveryService discoveryService = new();
        private readonly AuditCacheService auditCacheService = new();
        private readonly System.Windows.Forms.Timer temporizadorDescubrimiento = new() { Interval = 8000 };
        private readonly Dictionary<Button, (string Titulo, string Subtitulo)> secciones;
        private Button? botonActivo;
        private bool buscandoEquipos;
        private string grupoVisible = "Todos";
        private readonly HashSet<string> alertasMostradas = [];
        private HashSet<string>? conectadosAnteriores;
        private readonly NotifyIcon notificador = new() { Icon = SystemIcons.Shield, Visible = true, Text = "ARES Centro de Control" };
        private List<ControlSessionStatus> sesionesPanel = [];
        private List<GroupPolicy> grupos = [new() { Grupo = "General" }];

        public MainForm()
        {
            InitializeComponent();
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield;

            secciones = new()
            {
                [btnEmpleados] = ("Empleados", "Consultá las personas y los equipos que tienen asignados"),
                [btnEquipos] = ("Equipos", "Administrá los dispositivos conectados a ARES"),
                [btnMonitor] = ("Monitor", "Revisá el estado general de la infraestructura"),
                [btnSeguridad] = ("Seguridad", "Controlá bloqueos y alertas de seguridad"),
                [btnPantallas] = ("Pantallas", "Accedé a las capturas recientes de los equipos"),
                [btnConfiguracion] = ("Configuración", "Personalizá la conexión y el comportamiento de ARES")
            };

            btnEmpleados.Click += (_, _) => MostrarSeccion(btnEmpleados);
            btnEquipos.Click += (_, _) => MostrarSeccion(btnEquipos);
            btnMonitor.Click += (_, _) => MostrarSeccion(btnMonitor);
            btnSeguridad.Click += (_, _) => MostrarSeccion(btnSeguridad);
            btnPantallas.Click += (_, _) => MostrarSeccion(btnPantallas);
            btnConfiguracion.Click += (_, _) => MostrarSeccion(btnConfiguracion);
            temporizadorDescubrimiento.Tick += async (_, _) => await BuscarEquiposAsync();
            Shown += async (_, _) => await MostrarAsistenteInicialAsync();
        }

        private async Task MostrarAsistenteInicialAsync()
        {
            if (AresControlAuth.Client.User?.Role != "Owner") return;
            try
            {
                OrganizationSetupInfo? setup = await discoveryService.ObtenerConfiguracionInicialAsync();
                if (setup is not null && !setup.OnboardingCompleted)
                {
                    using var wizard = new OnboardingForm(discoveryService, setup);
                    if (wizard.ShowDialog(this) == DialogResult.OK) await BuscarEquiposAsync();
                }
            }
            catch (Exception ex) { MessageBox.Show($"No se pudo abrir la configuración inicial.\n\n{ex.Message}", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void MainForm_Load(object? sender, EventArgs e)
        {
            MostrarSeccion(btnEquipos);
            ActualizarEstadoConexion();
            temporizadorDescubrimiento.Start();
            _ = BuscarEquiposAsync();
        }

        private async Task BuscarEquiposAsync()
        {
            if (buscandoEquipos) return;
            buscandoEquipos = true;

            try
            {
                lblConexion.Text = "Buscando equipos…";
                IReadOnlyList<AgenteDetectado> detectados = await discoveryService.BuscarAsync(
                    empleadoService.ObtenerEmpleados().Select(e => e.Computadora.DireccionIP));
                grupos = await discoveryService.ObtenerPoliticasGrupoAsync();
                if (grupos.Count == 0) grupos = [new() { Grupo = "General" }];
                empleadoService.ActualizarEquiposDetectados(detectados);
                ControlSessionHeartbeatResponse panelPolicy = await discoveryService.RegistrarSesionPanelAsync();
                if (panelPolicy.ActualizarAhora) _ = ControlCenterUpdater.StartAsync(panelPolicy.Url);
                sesionesPanel = await discoveryService.ObtenerSesionesPanelAsync();

                int conectados = detectados.Count;
                HashSet<string> actuales = detectados.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (conectadosAnteriores is not null)
                    foreach (string desconectado in conectadosAnteriores.Except(actuales))
                        MostrarAlerta($"Equipo desconectado: {empleadoService.ObtenerEmpleados().FirstOrDefault(x => x.Computadora.AgentId == desconectado)?.Computadora.Nombre ?? desconectado}");
                conectadosAnteriores = actuales;
                foreach (AgenteDetectado agente in detectados.Where(x => x.SolicitudDesbloqueo))
                    if (alertasMostradas.Add("unlock-" + agente.Id)) MostrarAlerta($"Solicitud de desbloqueo: {agente.Equipo}");
                foreach (AgenteDetectado agente in detectados.Where(x => x.ActualizacionDisponible))
                    if (alertasMostradas.Add("update-" + agente.Id)) MostrarAlerta($"Actualizacion disponible para {agente.Equipo}: {agente.UltimaVersion}");
                foreach (AgenteDetectado agente in detectados.Where(x => x.HorarioPendiente))
                    if (alertasMostradas.Add("schedule-" + agente.Id)) MostrarAlerta($"{agente.Equipo} aun no recibio la nueva programacion.");
                lblPuntoConexion.ForeColor = conectados > 0 ? Color.FromArgb(34, 197, 94) : Color.FromArgb(239, 68, 68);
                lblConexion.ForeColor = conectados > 0 ? Color.FromArgb(22, 101, 52) : Color.FromArgb(153, 27, 27);
                pnlEstadoConexion.BackColor = conectados > 0 ? Color.FromArgb(240, 253, 244) : Color.FromArgb(254, 242, 242);
                lblConexion.Text = $"{conectados} equipos · {sesionesPanel.Count} paneles";

                if (botonActivo == btnEquipos || botonActivo == btnEmpleados ||
                    botonActivo == btnMonitor || botonActivo == btnSeguridad)
                    MostrarSeccion(botonActivo);
            }
            catch (Exception ex)
            {
                lblConexion.Text = $"Error de detección: {ex.Message}";
            }
            finally
            {
                buscandoEquipos = false;
            }
        }

        private void MostrarSeccion(Button boton)
        {
            MarcarBotonActivo(boton);
            (lblTituloSeccion.Text, lblSubtitulo.Text) = secciones[boton];
            pnlContenido.Controls.Clear();

            Control vista = boton == btnEquipos ? CrearVistaEquipos()
                : boton == btnEmpleados ? CrearVistaEmpleados()
                : boton == btnMonitor ? CrearVistaMonitor()
                : boton == btnSeguridad ? CrearVistaSeguridad()
                : boton == btnPantallas ? CrearVistaPantallas()
                : CrearVistaConfiguracion();

            vista.Dock = DockStyle.Fill;
            pnlContenido.Controls.Add(vista);
        }

        private void MarcarBotonActivo(Button activo)
        {
            if (botonActivo != null)
            {
                botonActivo.BackColor = Color.FromArgb(11, 35, 64);
                botonActivo.ForeColor = Color.FromArgb(238, 238, 238);
            }

            botonActivo = activo;
            activo.BackColor = Color.FromArgb(37, 99, 235);
            activo.ForeColor = Color.White;
        }

        private Control CrearVistaEquipos()
        {
            var contenedor = CrearTarjeta();
            var encabezado = CrearEncabezadoTarjeta("Equipos registrados", "Estado y acciones disponibles en tiempo real");
            var actualizar = new Button
            {
                Text = "↻  Actualizar",
                Dock = DockStyle.Right,
                Width = 132,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 18, 18, 18)
            };
            actualizar.FlatAppearance.BorderSize = 0;
            actualizar.Click += async (_, _) =>
            {
                actualizar.Enabled = false;
                actualizar.Text = "Actualizando…";
                await BuscarEquiposAsync();
            };
            var borrar = new Button
            {
                Text = "Borrar lista",
                Dock = DockStyle.Right,
                Width = 120,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 18, 8, 18)
            };
            borrar.FlatAppearance.BorderSize = 0;
            borrar.Click += async (_, _) =>
            {
                if (MessageBox.Show(
                    "¿Borrar todos los equipos registrados?\n\nLos agentes conectados volverán a aparecer automáticamente. Los registros se conservarán.",
                    "ARES", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                borrar.Enabled = false;
                try { await discoveryService.BorrarEquiposAsync(); await BuscarEquiposAsync(); }
                catch (Exception ex) { MessageBox.Show($"No se pudo borrar la lista.\n\n{ex.Message}", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { borrar.Enabled = true; }
            };
            encabezado.Padding = new Padding(22, 13, 18, 13);
            var carpetas = CrearSelectorCarpetas();
            encabezado.Controls.Add(carpetas);
            encabezado.Controls.Add(borrar);
            encabezado.Controls.Add(actualizar);
            var publicar = new Button
            {
                Text = "Publicar Agent",
                Dock = DockStyle.Right,
                Width = 135,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            publicar.FlatAppearance.BorderSize = 0;
            publicar.Click += async (_, _) =>
            {
                using var file = new OpenFileDialog { Filter = "Paquete ARES Agent (*.zip)|*.zip", Title = "Seleccioná el ZIP oficial del Agent" };
                if (file.ShowDialog(this) != DialogResult.OK) return;
                string? version = PedirVersionAgent();
                if (version is null) return;
                publicar.Enabled = false;
                try
                {
                    await discoveryService.CargarPaqueteActualizacionAsync(file.FileName, version);
                    await BuscarEquiposAsync();
                    MessageBox.Show($"ARES Agent {version} quedó publicado. Los equipos anteriores ahora mostrarán el botón Actualizar.", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { publicar.Enabled = true; }
            };
            encabezado.Controls.Add(publicar);
            var vincular = new Button { Text = "Vincular equipo", Dock = DockStyle.Right, Width = 135, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(14, 116, 144), ForeColor = Color.White, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Cursor = Cursors.Hand };
            vincular.FlatAppearance.BorderSize = 0;
            vincular.Click += async (_, _) => await MostrarNuevaVinculacionAsync();
            encabezado.Controls.Add(vincular);
            var tabla = CrearTablaEquipos();
            tabla.Dock = DockStyle.Fill;
            contenedor.Controls.Add(tabla);
            contenedor.Controls.Add(encabezado);
            return contenedor;
        }

        private DataGridView CrearTablaEquipos()
        {
            var tabla = new DataGridView
            {
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeight = 46,
                EnableHeadersVisualStyles = false,
                GridColor = Color.FromArgb(226, 232, 240),
                MultiSelect = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                RowTemplate = { Height = 43 },
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            tabla.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Padding = new Padding(8, 0, 0, 0),
                SelectionBackColor = Color.FromArgb(248, 250, 252)
            };
            tabla.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font("Segoe UI", 9.5F),
                Padding = new Padding(8, 0, 0, 0),
                SelectionBackColor = Color.FromArgb(239, 246, 255),
                SelectionForeColor = Color.FromArgb(30, 41, 59)
            };
            tabla.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 245, 245) };

            tabla.Columns.Add(new DataGridViewTextBoxColumn { Name = "Equipo", HeaderText = "EQUIPO", FillWeight = 120 });
            tabla.Columns.Add(new DataGridViewTextBoxColumn { Name = "Estado", HeaderText = "ESTADO", FillWeight = 95 });
            tabla.Columns.Add(new DataGridViewTextBoxColumn { Name = "Usuario", HeaderText = "USUARIO", FillWeight = 110 });
            tabla.Columns.Add(new DataGridViewTextBoxColumn { Name = "IP", HeaderText = "DIRECCIÓN IP", FillWeight = 100 });
            tabla.Columns.Add(new DataGridViewTextBoxColumn { Name = "Solicitud", HeaderText = "SOLICITUD", FillWeight = 120 });
            tabla.Columns.Add(new DataGridViewTextBoxColumn { Name = "Motivo", HeaderText = "MOTIVO / PROXIMO CAMBIO", FillWeight = 135 });
            tabla.Columns.Add(new DataGridViewTextBoxColumn { Name = "Grupo", HeaderText = "GRUPO", FillWeight = 70 });
            tabla.Columns.Add(new DataGridViewButtonColumn { Name = "CambiarGrupo", HeaderText = "", Text = "Mover", UseColumnTextForButtonValue = true, FillWeight = 55, FlatStyle = FlatStyle.Flat });
            tabla.Columns.Add(new DataGridViewButtonColumn { Name = "Excepcion", HeaderText = "", Text = "Excepcion", UseColumnTextForButtonValue = true, FillWeight = 65, FlatStyle = FlatStyle.Flat });
            tabla.Columns.Add(new DataGridViewButtonColumn { Name = "ActualizarAgente", HeaderText = "VERSION", FillWeight = 65, FlatStyle = FlatStyle.Flat });
            tabla.Columns.Add(new DataGridViewButtonColumn { Name = "Renombrar", HeaderText = "", Text = "Cambiar nombre", UseColumnTextForButtonValue = true, FillWeight = 85, FlatStyle = FlatStyle.Flat });
            tabla.Columns.Add(new DataGridViewButtonColumn { Name = "Ver", HeaderText = "", Text = "Ver", UseColumnTextForButtonValue = true, FillWeight = 55, FlatStyle = FlatStyle.Flat });
            tabla.Columns.Add(new DataGridViewButtonColumn { Name = "Credencial", HeaderText = "", Text = "Seguridad", UseColumnTextForButtonValue = true, FillWeight = 70, FlatStyle = FlatStyle.Flat });
            tabla.Columns.Add(new DataGridViewButtonColumn { Name = "Accion", HeaderText = "ACCIÓN", FillWeight = 85, FlatStyle = FlatStyle.Flat });

            foreach (Empleado empleado in empleadoService.ObtenerEmpleados().Where(x => grupoVisible == "Todos" || x.Grupo == grupoVisible).OrderBy(x => x.Grupo).ThenBy(x => x.Nombre))
            {
                Computadora pc = empleado.Computadora;
                string indicador = pc.EstaEncendida ? "●  " : "●  ";
                int fila = tabla.Rows.Add(indicador + pc.Nombre,
                    pc.EstaBloqueada ? "Bloqueado" : "Desbloqueado",
                    empleado.Nombre, pc.DireccionIP,
                    pc.SolicitudDesbloqueoPendiente ? "🔔 Desbloqueo solicitado" : "—",
                    pc.MotivoBloqueo + (pc.ProximoCambioUtc.HasValue ? $"\n{pc.ProximoCambioUtc.Value.ToLocalTime():dd/MM HH:mm}" : ""),
                    empleado.Grupo, "Mover", "Excepcion", pc.ActualizacionDisponible ? $"Actualizar {pc.UltimaVersion}" : $"v{pc.Version}", "Cambiar nombre", "Ver", "Seguridad",
                    pc.EstaBloqueada ? "Desbloquear" : "Bloquear");
                tabla.Rows[fila].Tag = empleado;
                tabla.Rows[fila].Cells[0].Style.ForeColor = pc.EstaEncendida
                    ? Color.FromArgb(22, 163, 74) : Color.FromArgb(220, 38, 38);
                if (pc.SolicitudDesbloqueoPendiente)
                {
                    tabla.Rows[fila].Cells[4].Style.ForeColor = Color.FromArgb(234, 88, 12);
                    tabla.Rows[fila].Cells[4].Style.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                    tabla.Rows[fila].DefaultCellStyle.BackColor = Color.FromArgb(255, 247, 237);
                }
            }

            tabla.CellContentClick += async (_, e) =>
            {
                if (e.RowIndex < 0 || tabla.Rows[e.RowIndex].Tag is not Empleado empleado) return;
                if (tabla.Columns[e.ColumnIndex].Name == "Renombrar")
                {
                    string? nombre = PedirNombreEquipo(empleado.Computadora.Nombre);
                    if (string.IsNullOrWhiteSpace(nombre)) return;
                    try { await discoveryService.RenombrarEquipoAsync(empleado.Computadora.AgentId, nombre.Trim()); await BuscarEquiposAsync(); }
                    catch (Exception ex) { MessageBox.Show($"No se pudo cambiar el nombre.\n\n{ex.Message}", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
                else if (tabla.Columns[e.ColumnIndex].Name == "CambiarGrupo")
                {
                    string? grupo = PedirGrupo(empleado.Grupo);
                    if (grupo is null) return;
                    try { await discoveryService.EstablecerGrupoAsync(empleado.Computadora.AgentId, grupo); await BuscarEquiposAsync(); }
                    catch (Exception ex) { MessageBox.Show($"No se pudo cambiar el grupo.\n\n{ex.Message}", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
                else if (tabla.Columns[e.ColumnIndex].Name == "Excepcion")
                {
                    var seleccion = PedirExcepcion(empleado.Computadora.ExcepcionHastaUtc);
                    if (seleccion is null) return;
                    try
                    {
                        if (seleccion.Value.Quitar) await discoveryService.QuitarExcepcionAsync(empleado.Computadora.AgentId);
                        else await discoveryService.EstablecerExcepcionAsync(empleado.Computadora.AgentId, seleccion.Value.Hasta.ToUniversalTime(), seleccion.Value.Permitir);
                        await BuscarEquiposAsync();
                    }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
                else if (tabla.Columns[e.ColumnIndex].Name == "ActualizarAgente")
                {
                    if (!empleado.Computadora.ActualizacionDisponible) return;
                    if (MessageBox.Show($"Actualizar {empleado.Computadora.Nombre} a ARES Agent {empleado.Computadora.UltimaVersion}?", "ARES", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try
                    {
                        await discoveryService.SolicitarActualizacionAsync(empleado.Computadora.AgentId);
                        MessageBox.Show("Orden enviada. La actualización comenzará en el próximo heartbeat.", "ARES");
                    }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
                else if (tabla.Columns[e.ColumnIndex].Name == "Ver")
                {
                    Computadora pc = empleado.Computadora;
                    MessageBox.Show($"Equipo: {pc.Nombre}\nUsuario: {empleado.Nombre}\nIP: {pc.DireccionIP}\nSistema: {pc.SistemaOperativo}\nEstado: {(pc.EstaEncendida ? "En línea" : "Sin conexión")}",
                        "Detalle del equipo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (tabla.Columns[e.ColumnIndex].Name == "Credencial")
                {
                    Computadora pc = empleado.Computadora;
                    if (!pc.CredencialIndividual)
                    {
                        MessageBox.Show("Este equipo todavía utiliza la clave compartida anterior. Para administrarlo individualmente debe vincularse con el instalador nuevo.", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    DialogResult choice = MessageBox.Show(
                        $"Seguridad de {pc.Nombre}\n\nSí: renovar la credencial de forma remota.\nNo: revocar el acceso de este equipo.\nCancelar: no realizar cambios.",
                        "Credencial del equipo", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                    try
                    {
                        if (choice == DialogResult.Yes)
                        {
                            await discoveryService.RenovarCredencialEquipoAsync(pc.AgentId);
                            MessageBox.Show("Renovación solicitada. Se completará automáticamente cuando el servicio ARES se conecte.", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else if (choice == DialogResult.No)
                        {
                            if (MessageBox.Show("La PC perderá acceso al servidor inmediatamente y necesitará un nuevo código de vinculación. ¿Continuar?", "Revocar equipo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                            await discoveryService.RevocarCredencialEquipoAsync(pc.AgentId);
                            await BuscarEquiposAsync();
                        }
                    }
                    catch (Exception ex) { MessageBox.Show($"No se pudo modificar la credencial.\n\n{ex.Message}", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
                else if (tabla.Columns[e.ColumnIndex].Name == "Accion")
                {
                    Computadora pc = empleado.Computadora;
                    if (string.IsNullOrWhiteSpace(pc.AgentId))
                    {
                        MessageBox.Show("Este equipo todavía no está registrado en el servidor remoto.", "ARES",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    bool bloquear = !pc.EstaBloqueada;
                    string accion = bloquear ? "bloquear" : "desbloquear";
                    if (MessageBox.Show($"¿Confirmás que querés {accion} {pc.Nombre}?", "ARES",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

                    try
                    {
                        tabla.Enabled = false;
                        await discoveryService.EstablecerRestriccionAsync(pc.AgentId, bloquear);
                        pc.EstaBloqueada = bloquear;
                        await BuscarEquiposAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"No se pudo {accion} el equipo.\n\n{ex.Message}", "ARES",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally { if (!tabla.IsDisposed) tabla.Enabled = true; }
                }
            };
            return tabla;
        }

        private static string? PedirNombreEquipo(string actual)
        {
            using var dialogo = new Form { Text = "Cambiar nombre", Width = 430, Height = 180, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
            var texto = new TextBox { Text = actual, Left = 20, Top = 42, Width = 370, MaxLength = 50 };
            var etiqueta = new Label { Text = "Nombre visible del equipo", Left = 20, Top = 18, AutoSize = true };
            var guardar = new Button { Text = "Guardar", Left = 290, Top = 82, Width = 100, DialogResult = DialogResult.OK };
            var cancelar = new Button { Text = "Cancelar", Left = 180, Top = 82, Width = 100, DialogResult = DialogResult.Cancel };
            dialogo.Controls.AddRange([etiqueta, texto, cancelar, guardar]); dialogo.AcceptButton = guardar; dialogo.CancelButton = cancelar;
            return dialogo.ShowDialog() == DialogResult.OK ? texto.Text : null;
        }

        private static string? PedirVersionAgent()
        {
            using var form = new Form { Text = "Publicar ARES Agent", Width = 410, Height = 175, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
            var version = new TextBox { Text = "1.6.6", Left = 22, Top = 45, Width = 350 };
            var publish = new Button { Text = "Publicar", Left = 272, Top = 84, Width = 100, DialogResult = DialogResult.OK };
            form.Controls.AddRange([new Label { Text = "Versión incluida en el ZIP (ejemplo: 1.6.6)", Left = 22, Top = 20, AutoSize = true }, version, publish]);
            form.AcceptButton = publish;
            if (form.ShowDialog() != DialogResult.OK) return null;
            return Version.TryParse(version.Text.Trim(), out Version? parsed) ? parsed.ToString(3) : null;
        }

        private async Task MostrarNuevaVinculacionAsync()
        {
            using var form = new Form { Text = "Vincular computadora", Width = 430, Height = 330, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
            var group = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            group.Items.AddRange(grupos.Select(x => x.Grupo).Cast<object>().ToArray()); group.SelectedIndex = group.Items.Count > 0 ? 0 : -1;
            var uses = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 1, Width = 160 };
            var hours = new NumericUpDown { Minimum = 1, Maximum = 720, Value = 24, Width = 160 };
            var generate = new Button { Text = "Generar código", Width = 160, Height = 38 };
            var layout = new FlowLayoutPanel { Location = new Point(30, 24), Width = 350, Height = 260, FlowDirection = FlowDirection.TopDown, WrapContents = false, Controls = { new Label { Text = "Grupo inicial", AutoSize = true }, group, new Label { Text = "Cantidad máxima de equipos", AutoSize = true }, uses, new Label { Text = "Validez en horas", AutoSize = true }, hours, generate } };
            form.Controls.Add(layout);
            generate.Click += async (_, _) =>
            {
                try
                {
                    CreatedDeviceEnrollment result = await discoveryService.CrearVinculacionEquipoAsync((int)uses.Value, (int)hours.Value, group.Text);
                    Clipboard.SetText(result.Code);
                    MessageBox.Show($"Código copiado al portapapeles:\n\n{result.Code}\n\nUsalo en el instalador de ARES Agent. Se mostrará completo solamente esta vez.", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    form.Close();
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            form.ShowDialog(this);
        }

        private string? PedirGrupo(string actual)
        {
            using var dialogo = new Form { Text = "Mover a carpeta", Width = 380, Height = 175, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog };
            var lista = new ComboBox { Left = 20, Top = 42, Width = 325, DropDownStyle = ComboBoxStyle.DropDownList };
            lista.Items.AddRange(grupos.Select(x => x.Grupo).Cast<object>().ToArray()); lista.SelectedItem = actual; if (lista.SelectedIndex < 0 && lista.Items.Count > 0) lista.SelectedIndex = 0;
            var guardar = new Button { Text = "Mover", Left = 245, Top = 82, Width = 100, DialogResult = DialogResult.OK };
            dialogo.Controls.AddRange([new Label { Text = "Carpeta del empleado y equipo", Left = 20, Top = 18, AutoSize = true }, lista, guardar]); dialogo.AcceptButton = guardar;
            return dialogo.ShowDialog() == DialogResult.OK ? lista.SelectedItem?.ToString() : null;
        }

        private static (DateTimeOffset Hasta, bool Permitir, bool Quitar)? PedirExcepcion(DateTimeOffset? actual)
        {
            using var form = new Form { Text = "Excepcion temporal", Width = 455, Height = 245, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog };
            var until = new DateTimePicker { Left = 22, Top = 48, Width = 390, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm", Value = actual?.ToLocalTime().DateTime ?? DateTime.Now.AddHours(2) };
            var mode = new ComboBox { Left = 22, Top = 105, Width = 390, DropDownStyle = ComboBoxStyle.DropDownList };
            mode.Items.AddRange(["Permitir uso hasta esa hora", "Bloquear hasta esa hora"]); mode.SelectedIndex = 0;
            var save = new Button { Text = "Aplicar", Left = 312, Top = 150, Width = 100, DialogResult = DialogResult.OK };
            var remove = new Button { Text = "Quitar excepcion", Left = 175, Top = 150, Width = 128, DialogResult = DialogResult.Retry };
            form.Controls.AddRange([new Label { Text = "Vigente hasta", Left = 22, Top = 24, AutoSize = true }, until,
                new Label { Text = "Accion", Left = 22, Top = 82, AutoSize = true }, mode, remove, save]);
            DialogResult result = form.ShowDialog();
            if (result == DialogResult.Retry) return (DateTimeOffset.Now, true, true);
            return result == DialogResult.OK ? (new DateTimeOffset(until.Value), mode.SelectedIndex == 0, false) : null;
        }

        private Control CrearVistaEmpleados()
        {
            var tarjeta = CrearTarjeta();
            List<Empleado> visibles = empleadoService.ObtenerEmpleados().Where(x => grupoVisible == "Todos" || x.Grupo == grupoVisible).OrderBy(x => x.Grupo).ThenBy(x => x.Nombre).ToList();
            var tabla = CrearListaSimple(
                new[] { "CARPETA", "EMPLEADO", "EQUIPO ASIGNADO", "SESIÓN", "ARES AGENT", "SISTEMA" },
                visibles.Select(e => new[] {
                    e.Grupo, e.Nombre, e.Computadora.Nombre,
                    e.Computadora.EstaLogueada ? "Iniciada" : "Sin iniciar",
                    "●  Verificando…", e.Computadora.SistemaOperativo }).ToList());
            tarjeta.Controls.Add(tabla);
            var encabezado = CrearEncabezadoTarjeta("Directorio de empleados", "Carpetas y asignaciones activas dentro de la organización");
            var horarios = new Button { Text = "📅 Horarios", Dock = DockStyle.Right, Width = 130, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White };
            horarios.FlatAppearance.BorderSize = 0; horarios.Click += (_, _) => new ScheduleForm(discoveryService, empleadoService.ObtenerEmpleados()).ShowDialog(this);
            encabezado.Controls.Add(horarios); encabezado.Controls.Add(CrearSelectorCarpetas()); tarjeta.Controls.Add(encabezado);
            _ = ActualizarEstadoAgentesAsync(tabla);
            return tarjeta;
        }

        private async Task ActualizarEstadoAgentesAsync(DataGridView tabla)
        {
            List<Empleado> empleados = empleadoService.ObtenerEmpleados().Where(x => grupoVisible == "Todos" || x.Grupo == grupoVisible).OrderBy(x => x.Grupo).ThenBy(x => x.Nombre).ToList();
            if (tabla.IsDisposed || tabla.Disposing) return;
            await Task.CompletedTask;
            for (int i = 0; i < empleados.Count && i < tabla.Rows.Count; i++)
            {
                bool activo = empleados[i].Computadora.EstaEncendida;
                DataGridViewCell celda = tabla.Rows[i].Cells[4];
                celda.Value = activo ? "●  Activo" : "●  Inactivo";
                celda.Style.ForeColor = activo
                    ? Color.FromArgb(22, 163, 74)
                    : Color.FromArgb(220, 38, 38);
                celda.Style.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            }
        }

        private Control CrearVistaMonitor()
        {
            var panel = new TableLayoutPanel { ColumnCount = 4, RowCount = 2, BackColor = Color.Transparent };
            for (int i = 0; i < 4; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var equipos = empleadoService.ObtenerEmpleados();
            panel.Controls.Add(CrearMetrica("Equipos totales", equipos.Count.ToString(), Color.FromArgb(37, 99, 235)), 0, 0);
            panel.Controls.Add(CrearMetrica("En línea", equipos.Count(e => e.Computadora.EstaEncendida).ToString(), Color.FromArgb(22, 163, 74)), 1, 0);
            panel.Controls.Add(CrearMetrica("Bloqueados", equipos.Count(e => e.Computadora.EstaBloqueada).ToString(), Color.FromArgb(234, 88, 12)), 2, 0);
            panel.Controls.Add(CrearMetrica("Sesiones del panel", sesionesPanel.Count.ToString(), Color.FromArgb(124, 58, 237)), 3, 0);
            var actividad = CrearTarjeta();
            actividad.Margin = new Padding(8, 20, 8, 0);
            actividad.Controls.Add(CrearTablaSesionesPanel());
            var sessionsHeader = CrearEncabezadoTarjeta("Sesiones activas del Centro de Control", "Nombre editable, usuario, equipo, plataforma y última conexión");
            var updateAll = new Button { Text = "Actualizar todos", Dock = DockStyle.Right, Width = 140, BackColor = Color.FromArgb(124, 58, 237), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            updateAll.FlatAppearance.BorderSize = 0; updateAll.Click += async (_, _) => await ActualizarSesionesPanelAsync(sesionesPanel.Where(x => x.ActualizacionDisponible).ToList());
            sessionsHeader.Controls.Add(updateAll); actividad.Controls.Add(sessionsHeader);
            panel.Controls.Add(actividad, 0, 1);
            panel.SetColumnSpan(actividad, 4);
            return panel;
        }

        private DataGridView CrearTablaSesionesPanel()
        {
            var table = CrearListaSimple(new[] { "NOMBRE DE SESION", "USUARIO", "EQUIPO", "PLATAFORMA", "VERSION", "ACTUALIZACION", "ULTIMA CONEXION" },
                sesionesPanel.Select(x => new[] { x.Nombre, x.Usuario, x.Equipo, x.Plataforma, x.Version,
                    x.ActualizacionDisponible ? $"Disponible {x.UltimaVersion}" : x.EstadoActualizacion,
                    x.UltimaConexionUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") }).ToList());
            table.Columns.Add(new DataGridViewButtonColumn { Name = "RenombrarSesion", HeaderText = "", Text = "Cambiar nombre", UseColumnTextForButtonValue = true });
            table.Columns.Add(new DataGridViewButtonColumn { Name = "ActualizarSesion", HeaderText = "", FillWeight = 70 });
            for (int i = 0; i < table.Rows.Count && i < sesionesPanel.Count; i++) table.Rows[i].Tag = sesionesPanel[i];
            table.CellContentClick += async (_, e) =>
            {
                if (e.RowIndex < 0 || table.Rows[e.RowIndex].Tag is not ControlSessionStatus session) return;
                if (table.Columns[e.ColumnIndex].Name == "ActualizarSesion")
                { if (session.ActualizacionDisponible) await ActualizarSesionesPanelAsync([session]); return; }
                if (table.Columns[e.ColumnIndex].Name != "RenombrarSesion") return;
                string? name = PedirNombreEquipo(session.Nombre); if (string.IsNullOrWhiteSpace(name)) return;
                try
                {
                    await discoveryService.RenombrarSesionPanelAsync(session.Id, name);
                    sesionesPanel = await discoveryService.ObtenerSesionesPanelAsync(); MostrarSeccion(btnMonitor);
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            for (int i = 0; i < table.Rows.Count && i < sesionesPanel.Count; i++)
                table.Rows[i].Cells["ActualizarSesion"].Value = sesionesPanel[i].ActualizacionDisponible ? "Actualizar" : "Al dia";
            return table;
        }

        private async Task ActualizarSesionesPanelAsync(List<ControlSessionStatus> sessions)
        {
            if (sessions.Count == 0) { MessageBox.Show("Todos los paneles activos estan actualizados.", "ARES"); return; }
            try
            {
                foreach (var platformGroup in sessions.GroupBy(x => x.Plataforma.Contains("mac", StringComparison.OrdinalIgnoreCase) ? "macos" : "windows"))
                {
                    bool mac = platformGroup.Key == "macos";
                    using var dialog = new OpenFileDialog { Filter = mac ? "Instalador macOS (*.pkg)|*.pkg" : "Paquete Windows (*.zip)|*.zip",
                        Title = mac ? "Selecciona el Centro de Control macOS nuevo" : "Selecciona el Centro de Control Windows nuevo" };
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    await discoveryService.CargarPaquetePanelAsync(platformGroup.Key, dialog.FileName);
                }
                await discoveryService.SolicitarActualizacionPanelesAsync(sessions.Select(x => x.Id));
                MessageBox.Show($"Se enviaron {sessions.Count} ordenes de actualizacion.", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private Control CrearVistaSeguridad()
        {
            var tarjeta = CrearTarjeta();
            var tabla = CrearListaSimple(
                new[] { "FECHA Y HORA", "EQUIPO", "EVENTO", "DETALLE" }, []);
            tarjeta.Controls.Add(tabla);
            tarjeta.Controls.Add(CrearEncabezadoTarjeta("Registro de seguridad", "Bloqueos, desbloqueos y conexiones de los agentes"));
            _ = CargarAuditoriaAsync(tabla);
            return tarjeta;
        }

        private async Task CargarAuditoriaAsync(DataGridView tabla)
        {
            IReadOnlyList<AgentAuditEvent> eventos = auditCacheService.Cargar();
            MostrarEventosAuditoria(tabla, eventos);

            try
            {
                IReadOnlyList<AgentAuditEvent> remotos = await discoveryService.ObtenerAuditoriaAsync();
                if (tabla.IsDisposed) return;
                eventos = auditCacheService.CombinarYGuardar(remotos);
                tabla.Rows.Clear();
                MostrarEventosAuditoria(tabla, eventos);
            }
            catch (Exception ex)
            {
                if (!tabla.IsDisposed && eventos.Count == 0)
                    tabla.Rows.Add(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), "Servidor", "Error", ex.Message);
            }
        }

        private static void MostrarEventosAuditoria(DataGridView tabla, IEnumerable<AgentAuditEvent> eventos)
        {
            if (tabla.IsDisposed) return;
            foreach (AgentAuditEvent evento in eventos.OrderByDescending(e => e.FechaUtc))
            {
                DateTimeOffset horaLocal = evento.FechaUtc.ToLocalTime();
                tabla.Rows.Add(horaLocal.ToString("dd/MM/yyyy HH:mm:ss"), evento.Equipo,
                    FormatearTipoEvento(evento.Tipo), evento.Detalle);
            }
        }

        private static string FormatearTipoEvento(string tipo) => tipo switch
        {
            "USUARIO_BLOQUEADO" => "Bloqueado",
            "USUARIO_DESBLOQUEADO" => "Desbloqueado",
            "AGENTE_CONECTADO" => "Agente conectado",
            "AGENTE_CERRADO" => "Programa cerrado",
            "AGENTE_DESCONECTADO" => "Conexión perdida",
            "SOLICITUD_DESBLOQUEO" => "Solicitud de desbloqueo",
            _ => tipo
        };

        private Control CrearVistaPantallas()
        {
            var tarjeta = CrearTarjeta();
            tarjeta.Controls.Add(CrearMensajeCentral("Todavía no hay capturas", "Seleccioná “Ver” en un equipo para consultar sus datos. Las capturas remotas se mostrarán en esta sección."));
            return tarjeta;
        }

        private Control CrearVistaConfiguracion()
        {
            var tarjeta = CrearTarjeta();
            var titulo = CrearEncabezadoTarjeta("Servidor remoto", "Configuración usada por esta consola administrativa");
            AresSettings configuracion = AresSettings.Cargar();
            var contenido = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(28, 22, 28, 28), WrapContents = false };
            TextBox txtServidor = CrearCampoConfiguracion("URL HTTPS del servidor", configuracion.ServerUrl, contenido);
            string usuario = AresControlAuth.Client.User is null ? "" : $"{AresControlAuth.Client.User.DisplayName} · {AresControlAuth.Client.User.Email} · {AresControlAuth.Client.User.Role}";
            contenido.Controls.Add(new Label { AutoSize = true, ForeColor = Color.FromArgb(51, 65, 85), Margin = new Padding(0, 14, 0, 0), Text = $"Sesión: {usuario}" });
            var estado = new Label { AutoSize = true, ForeColor = Color.FromArgb(100, 116, 139), Margin = new Padding(0, 14, 0, 0), Text = "La sesión está protegida para este usuario de Windows." };
            var guardar = new Button { Text = "Guardar y probar conexión", Width = 220, Height = 40, Margin = new Padding(0, 20, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, Cursor = Cursors.Hand };
            guardar.FlatAppearance.BorderSize = 0;
            guardar.Click += async (_, _) =>
            {
                if (!Uri.TryCreate(txtServidor.Text.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
                {
                    estado.ForeColor = Color.FromArgb(220, 38, 38);
                    estado.Text = "Ingresá una dirección HTTPS válida.";
                    return;
                }
                new AresSettings { ServerUrl = txtServidor.Text.TrimEnd('/'), ApiKey = configuracion.ApiKey }.GuardarLocal();
                guardar.Enabled = false;
                estado.ForeColor = Color.FromArgb(37, 99, 235);
                estado.Text = "Probando conexión…";
                await BuscarEquiposAsync();
                guardar.Enabled = true;
                estado.ForeColor = lblConexion.Text.StartsWith("Error") ? Color.FromArgb(220, 38, 38) : Color.FromArgb(22, 163, 74);
                estado.Text = lblConexion.Text.StartsWith("Error") ? lblConexion.Text : "Configuración guardada. Conexión correcta.";
            };
            contenido.Controls.Add(guardar);
            if (AresControlAuth.Client.User?.Role is "Owner" or "Administrator")
            {
                var manageGroups = new Button { Text = "Administrar grupos", Width = 180, Height = 38, Margin = new Padding(0, 12, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(14, 116, 144), ForeColor = Color.White };
                manageGroups.Click += async (_, _) => { using var form = new GroupManagementForm(discoveryService); if (form.ShowDialog(this) == DialogResult.OK) { grupos = await discoveryService.ObtenerPoliticasGrupoAsync(); if (botonActivo is not null) MostrarSeccion(botonActivo); } };
                contenido.Controls.Add(manageGroups);
            }
            if (AresControlAuth.Client.User?.Role == "Owner")
            {
                var usuarios = new Button { Text = "Administrar usuarios", Width = 180, Height = 38, Margin = new Padding(0, 12, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(124, 58, 237), ForeColor = Color.White };
                usuarios.Click += async (_, _) => await MostrarUsuariosAsync(); contenido.Controls.Add(usuarios);
            }
            var cerrarSesion = new Button { Text = "Cerrar sesión", Width = 150, Height = 38, Margin = new Padding(0, 12, 0, 0), FlatStyle = FlatStyle.Flat };
            cerrarSesion.Click += (_, _) => { AresControlAuth.Client.Logout(); Application.Restart(); Environment.Exit(0); };
            contenido.Controls.Add(cerrarSesion);
            contenido.Controls.Add(estado);
            tarjeta.Controls.Add(contenido);
            tarjeta.Controls.Add(titulo);
            return tarjeta;
        }

        private async Task MostrarUsuariosAsync()
        {
            using var dialog = new Form { Text = "Usuarios del panel ARES", Width = 900, Height = 620, StartPosition = FormStartPosition.CenterParent };
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var pendingPage = new TabPage("Solicitudes pendientes"); var usersPage = new TabPage("Usuarios autorizados"); var invitationsPage = new TabPage("Códigos de invitación");
            tabs.TabPages.Add(pendingPage); tabs.TabPages.Add(usersPage); tabs.TabPages.Add(invitationsPage); dialog.Controls.Add(tabs);

            async Task LoadAsync()
            {
                pendingPage.Controls.Clear(); usersPage.Controls.Clear(); invitationsPage.Controls.Clear();
                List<ARES.Shared.Servicios.RegistrationRequestInfo> requests = await discoveryService.ObtenerSolicitudesRegistroAsync();
                var pending = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(15) };
                foreach (var item in requests.Where(x => x.Status == "Pending"))
                {
                    var role = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList }; role.Items.AddRange(["Administrator", "Supervisor", "Viewer"]); role.SelectedIndex = 1;
                    var approve = new Button { Text = "Aprobar", Width = 90 }; var reject = new Button { Text = "Rechazar", Width = 90 };
                    var row = new FlowLayoutPanel { Width = 820, Height = 48, Controls = { new Label { Text = $"{item.DisplayName}\n{item.Email}", Width = 360, Height = 42 }, role, approve, reject } };
                    approve.Click += async (_, _) => { await discoveryService.AprobarRegistroAsync(item.UserId, role.Text); await LoadAsync(); };
                    reject.Click += async (_, _) => { await discoveryService.RechazarRegistroAsync(item.UserId); await LoadAsync(); };
                    pending.Controls.Add(row);
                }
                if (pending.Controls.Count == 0) pending.Controls.Add(new Label { Text = "No hay solicitudes pendientes.", AutoSize = true }); pendingPage.Controls.Add(pending);

                var users = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(15) };
                foreach (var item in await discoveryService.ObtenerUsuariosPanelAsync())
                {
                    var role = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = item.Role != "Owner" }; role.Items.AddRange(["Administrator", "Supervisor", "Viewer"]); role.SelectedItem = item.Role;
                    var toggle = new Button { Text = item.Enabled ? "Suspender" : "Habilitar", Width = 100, Enabled = item.Role != "Owner" };
                    var save = new Button { Text = "Guardar rol", Width = 100, Enabled = item.Role != "Owner" };
                    var remove = new Button { Text = "Eliminar acceso", Width = 110, Enabled = item.Role != "Owner" };
                    var row = new FlowLayoutPanel { Width = 850, Height = 48, Controls = { new Label { Text = $"{item.DisplayName}\n{item.Email}", Width = 330, Height = 42 }, role, save, toggle, remove } };
                    save.Click += async (_, _) => { await discoveryService.ActualizarUsuarioPanelAsync(item.UserId, role.Text, item.Enabled); await LoadAsync(); };
                    toggle.Click += async (_, _) => { await discoveryService.ActualizarUsuarioPanelAsync(item.UserId, item.Role, !item.Enabled); await LoadAsync(); };
                    remove.Click += async (_, _) => { if (MessageBox.Show($"¿Eliminar el acceso de {item.DisplayName}?", "ARES", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) { await discoveryService.EliminarUsuarioPanelAsync(item.UserId); await LoadAsync(); } };
                    users.Controls.Add(row);
                }
                usersPage.Controls.Add(users);

                var inviteRoot = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };
                var inviteHeader = new Panel { Dock = DockStyle.Top, Height = 55 };
                var inviteList = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 8, 0, 0) };
                var createInvite = new Button { Text = "Crear código", Width = 150, Height = 38, Location = new Point(0, 4), BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
                createInvite.FlatAppearance.BorderSize = 0;
                inviteHeader.Controls.Add(createInvite);
                inviteRoot.Controls.Add(inviteList);
                inviteRoot.Controls.Add(inviteHeader);
                invitationsPage.Controls.Add(inviteRoot);
                createInvite.Click += async (_, _) =>
                {
                    using var form = new Form { Text = "Nueva invitación", Width = 420, Height = 350, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog };
                    var uses = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 1, Width = 160 }; var hours = new NumericUpDown { Minimum = 1, Maximum = 720, Value = 48, Width = 160 };
                    var role = new ComboBox { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList }; role.Items.AddRange(["Administrator", "Supervisor", "Viewer"]); role.SelectedItem = "Viewer";
                    var create = new Button { Text = "Generar código", Width = 160, Height = 38 };
                    var layout = new FlowLayoutPanel { Location = new Point(30, 25), Width = 340, Height = 280, FlowDirection = FlowDirection.TopDown, WrapContents = false, Controls = { new Label { Text = "Rol que recibirá el invitado", AutoSize = true }, role, new Label { Text = "Cantidad máxima de usos", AutoSize = true }, uses, new Label { Text = "Duración en horas", AutoSize = true }, hours, create } };
                    form.Controls.Add(layout);
                    create.Click += async (_, _) => { CreatedInvitation result = await discoveryService.CrearInvitacionAsync((int)uses.Value, (int)hours.Value, role.Text); Clipboard.SetText(result.Code); MessageBox.Show($"Código creado para el rol {role.Text} y copiado:\n\n{result.Code}\n\nSe mostrará completo solamente esta vez.", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Information); form.Close(); await LoadAsync(); };
                    form.ShowDialog(dialog);
                };
                try
                {
                    foreach (var invite in await discoveryService.ObtenerInvitacionesAsync())
                    {
                        bool active = !invite.Revoked && invite.ExpiresAt > DateTimeOffset.UtcNow && invite.UsedCount < invite.MaxUses;
                        var revoke = new Button { Text = "Revocar", Width = 90, Enabled = active };
                        revoke.Click += async (_, _) => { await discoveryService.RevocarInvitacionAsync(invite.InvitationId); await LoadAsync(); };
                        inviteList.Controls.Add(new FlowLayoutPanel { Width = 820, Height = 45, Controls = { new Label { Text = $"{invite.CodePrefix}-••••-•••• · {invite.UsedCount}/{invite.MaxUses} usos · vence {invite.ExpiresAt.ToLocalTime():dd/MM/yyyy HH:mm} · {(active ? "Activo" : "Inactivo")}", Width = 680, Height = 35 }, revoke } });
                    }
                }
                catch (Exception ex)
                {
                    inviteList.Controls.Add(new Label { AutoSize = true, ForeColor = Color.FromArgb(220, 38, 38), Text = $"No se pudo cargar el listado: {ex.Message}" });
                }
            }
            dialog.Shown += async (_, _) =>
            {
                try { await LoadAsync(); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            dialog.ShowDialog(this);
        }

        private static Panel CrearTarjeta() => new()
        {
            BackColor = Color.FromArgb(222, 225, 228),
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            Margin = new Padding(0)
        };

        private ComboBox CrearSelectorCarpetas()
        {
            var selector = new ComboBox { Dock = DockStyle.Right, Width = 125, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(6, 18, 6, 18) };
            selector.Items.Add("Todos"); selector.Items.AddRange(grupos.Select(x => x.Grupo).Cast<object>().ToArray()); selector.SelectedItem = grupoVisible;
            if (selector.SelectedIndex < 0) { grupoVisible = "Todos"; selector.SelectedIndex = 0; }
            selector.SelectedIndexChanged += (_, _) => { grupoVisible = selector.SelectedItem?.ToString() ?? "Todos"; if (botonActivo is not null) MostrarSeccion(botonActivo); };
            return selector;
        }

        private static Panel CrearEncabezadoTarjeta(string titulo, string detalle)
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.FromArgb(37, 99, 235), Padding = new Padding(18, 9, 10, 0) };
            panel.Controls.Add(new Label { Text = detalle, AutoSize = true, Location = new Point(20, 37), ForeColor = Color.FromArgb(225, 235, 247), Font = new Font("Segoe UI", 8F) });
            panel.Controls.Add(new Label { Text = titulo, AutoSize = true, Location = new Point(18, 10), ForeColor = Color.White, Font = new Font("Segoe UI", 11F, FontStyle.Bold) });
            return panel;
        }

        private static DataGridView CrearListaSimple(string[] columnas, List<string[]> filas)
        {
            var tabla = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersHeight = 38, RowTemplate = { Height = 40 }, EnableHeadersVisualStyles = false };
            tabla.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(248, 250, 252), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            tabla.DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.FromArgb(30, 41, 59), SelectionBackColor = Color.FromArgb(239, 246, 255), SelectionForeColor = Color.FromArgb(30, 41, 59), Padding = new Padding(10, 0, 0, 0) };
            tabla.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(243, 243, 243) };
            foreach (string columna in columnas) tabla.Columns.Add(columna, columna);
            foreach (string[] fila in filas) tabla.Rows.Add(fila);
            return tabla;
        }

        private static Panel CrearMetrica(string etiqueta, string valor, Color color)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(8), BackColor = Color.White };
            panel.Controls.Add(new Label { Text = valor, AutoSize = true, Font = new Font("Segoe UI", 27F, FontStyle.Bold), ForeColor = color, Location = new Point(23, 48) });
            panel.Controls.Add(new Label { Text = etiqueta, AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105), Location = new Point(25, 23) });
            return panel;
        }

        private static Panel CrearMensajeCentral(string titulo, string detalle)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            var tituloLabel = new Label { Text = titulo, AutoSize = true, Font = new Font("Segoe UI", 15F, FontStyle.Bold), ForeColor = Color.FromArgb(51, 65, 85) };
            var detalleLabel = new Label { Text = detalle, AutoSize = true, MaximumSize = new Size(620, 0), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10F), ForeColor = Color.FromArgb(100, 116, 139) };
            panel.Controls.Add(tituloLabel);
            panel.Controls.Add(detalleLabel);
            panel.Resize += (_, _) => { tituloLabel.Left = (panel.Width - tituloLabel.Width) / 2; tituloLabel.Top = Math.Max(50, panel.Height / 2 - 45); detalleLabel.Left = (panel.Width - detalleLabel.Width) / 2; detalleLabel.Top = tituloLabel.Bottom + 12; };
            return panel;
        }

        private static TextBox CrearCampoConfiguracion(string etiqueta, string valor, Control contenedor)
        {
            var panel = new Panel { Width = 500, Height = 72, Margin = new Padding(0, 0, 0, 8) };
            panel.Controls.Add(new Label { Text = etiqueta, AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105), Location = new Point(0, 0) });
            var campo = new TextBox { Text = valor, Width = 430, Location = new Point(0, 27), Font = new Font("Segoe UI", 10F), BorderStyle = BorderStyle.FixedSingle };
            panel.Controls.Add(campo);
            contenedor.Controls.Add(panel);
            return campo;
        }

        private async void ActualizarEstadoConexion()
        {
            await BuscarEquiposAsync();
        }

        private void MostrarAlerta(string mensaje) => notificador.ShowBalloonTip(6000, "ARES", mensaje, ToolTipIcon.Info);

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            notificador.Visible = false; notificador.Dispose(); base.OnFormClosed(e);
        }
    }
}
