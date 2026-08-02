using AdministracionEmpleados.Servicios;
using ARES.Shared.Modelos;

namespace AdministracionEmpleados
{
    public partial class MainForm : Form
    {
        private readonly EmpleadoService empleadoService = new();
        private readonly AgenteDiscoveryService discoveryService = new();
        private readonly System.Windows.Forms.Timer temporizadorDescubrimiento = new() { Interval = 8000 };
        private readonly Dictionary<Button, (string Titulo, string Subtitulo)> secciones;
        private Button? botonActivo;
        private bool buscandoEquipos;

        public MainForm()
        {
            InitializeComponent();

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
                empleadoService.ActualizarEquiposDetectados(detectados);

                int conectados = detectados.Count;
                lblPuntoConexion.ForeColor = conectados > 0 ? Color.FromArgb(34, 197, 94) : Color.FromArgb(239, 68, 68);
                lblConexion.ForeColor = conectados > 0 ? Color.FromArgb(22, 101, 52) : Color.FromArgb(153, 27, 27);
                pnlEstadoConexion.BackColor = conectados > 0 ? Color.FromArgb(240, 253, 244) : Color.FromArgb(254, 242, 242);
                lblConexion.Text = conectados == 1 ? "1 equipo conectado" : $"{conectados} equipos conectados";

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
                botonActivo.BackColor = Color.FromArgb(15, 23, 42);
                botonActivo.ForeColor = Color.FromArgb(203, 213, 225);
            }

            botonActivo = activo;
            activo.BackColor = Color.FromArgb(30, 64, 175);
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
            encabezado.Padding = new Padding(22, 13, 18, 13);
            encabezado.Controls.Add(actualizar);
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
                RowTemplate = { Height = 55 },
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

            tabla.Columns.Add(new DataGridViewTextBoxColumn { Name = "Equipo", HeaderText = "EQUIPO", FillWeight = 120 });
            tabla.Columns.Add(new DataGridViewTextBoxColumn { Name = "Estado", HeaderText = "ESTADO", FillWeight = 95 });
            tabla.Columns.Add(new DataGridViewTextBoxColumn { Name = "Usuario", HeaderText = "USUARIO", FillWeight = 110 });
            tabla.Columns.Add(new DataGridViewTextBoxColumn { Name = "IP", HeaderText = "DIRECCIÓN IP", FillWeight = 100 });
            tabla.Columns.Add(new DataGridViewButtonColumn { Name = "Ver", HeaderText = "", Text = "Ver", UseColumnTextForButtonValue = true, FillWeight = 55, FlatStyle = FlatStyle.Flat });
            tabla.Columns.Add(new DataGridViewButtonColumn { Name = "Accion", HeaderText = "ACCIÓN", FillWeight = 85, FlatStyle = FlatStyle.Flat });

            foreach (Empleado empleado in empleadoService.ObtenerEmpleados())
            {
                Computadora pc = empleado.Computadora;
                string indicador = pc.EstaEncendida ? "●  " : "●  ";
                int fila = tabla.Rows.Add(indicador + pc.Nombre,
                    pc.EstaBloqueada ? "Bloqueado" : "Desbloqueado",
                    empleado.Nombre, pc.DireccionIP, "Ver",
                    pc.EstaBloqueada ? "Desbloquear" : "Bloquear");
                tabla.Rows[fila].Tag = empleado;
                tabla.Rows[fila].Cells[0].Style.ForeColor = pc.EstaEncendida
                    ? Color.FromArgb(22, 163, 74) : Color.FromArgb(220, 38, 38);
            }

            tabla.CellContentClick += (_, e) =>
            {
                if (e.RowIndex < 0 || tabla.Rows[e.RowIndex].Tag is not Empleado empleado) return;
                if (tabla.Columns[e.ColumnIndex].Name == "Ver")
                {
                    Computadora pc = empleado.Computadora;
                    MessageBox.Show($"Equipo: {pc.Nombre}\nUsuario: {empleado.Nombre}\nIP: {pc.DireccionIP}\nSistema: {pc.SistemaOperativo}\nEstado: {(pc.EstaEncendida ? "En línea" : "Sin conexión")}",
                        "Detalle del equipo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (tabla.Columns[e.ColumnIndex].Name == "Accion")
                {
                    if (empleado.Computadora.EstaBloqueada)
                        empleadoService.Desbloquear(empleado);
                    else
                        empleadoService.Bloquear(empleado);
                    MostrarSeccion(btnEquipos);
                }
            };
            return tabla;
        }

        private Control CrearVistaEmpleados()
        {
            var tarjeta = CrearTarjeta();
            var tabla = CrearListaSimple(
                new[] { "EMPLEADO", "EQUIPO ASIGNADO", "SESIÓN", "ARES AGENT", "SISTEMA" },
                empleadoService.ObtenerEmpleados().Select(e => new[] {
                    e.Nombre, e.Computadora.Nombre,
                    e.Computadora.EstaLogueada ? "Iniciada" : "Sin iniciar",
                    "●  Verificando…", e.Computadora.SistemaOperativo }).ToList());
            tarjeta.Controls.Add(tabla);
            tarjeta.Controls.Add(CrearEncabezadoTarjeta("Directorio de empleados", "Asignaciones activas dentro de la organización"));
            _ = ActualizarEstadoAgentesAsync(tabla);
            return tarjeta;
        }

        private async Task ActualizarEstadoAgentesAsync(DataGridView tabla)
        {
            List<Empleado> empleados = empleadoService.ObtenerEmpleados();
            if (tabla.IsDisposed || tabla.Disposing) return;
            await Task.CompletedTask;
            for (int i = 0; i < empleados.Count && i < tabla.Rows.Count; i++)
            {
                bool activo = empleados[i].Computadora.EstaEncendida;
                DataGridViewCell celda = tabla.Rows[i].Cells[3];
                celda.Value = activo ? "●  Activo" : "●  Inactivo";
                celda.Style.ForeColor = activo
                    ? Color.FromArgb(22, 163, 74)
                    : Color.FromArgb(220, 38, 38);
                celda.Style.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            }
        }

        private Control CrearVistaMonitor()
        {
            var panel = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, BackColor = Color.Transparent };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var equipos = empleadoService.ObtenerEmpleados();
            panel.Controls.Add(CrearMetrica("Equipos totales", equipos.Count.ToString(), Color.FromArgb(37, 99, 235)), 0, 0);
            panel.Controls.Add(CrearMetrica("En línea", equipos.Count(e => e.Computadora.EstaEncendida).ToString(), Color.FromArgb(22, 163, 74)), 1, 0);
            panel.Controls.Add(CrearMetrica("Bloqueados", equipos.Count(e => e.Computadora.EstaBloqueada).ToString(), Color.FromArgb(234, 88, 12)), 2, 0);
            var actividad = CrearTarjeta();
            actividad.Margin = new Padding(8, 20, 8, 0);
            actividad.Controls.Add(CrearMensajeCentral("La actividad de los equipos aparecerá aquí", "ARES actualizará este panel cuando los agentes envíen métricas."));
            panel.Controls.Add(actividad, 0, 1);
            panel.SetColumnSpan(actividad, 3);
            return panel;
        }

        private Control CrearVistaSeguridad()
        {
            int bloqueados = empleadoService.ObtenerEmpleados().Count(e => e.Computadora.EstaBloqueada);
            var tarjeta = CrearTarjeta();
            tarjeta.Controls.Add(CrearMensajeCentral("Protección activa", $"{bloqueados} equipos bloqueados · No se detectaron alertas críticas."));
            return tarjeta;
        }

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
            TextBox txtClave = CrearCampoConfiguracion("Clave compartida ARES", configuracion.ApiKey, contenido);
            txtClave.UseSystemPasswordChar = true;
            var estado = new Label { AutoSize = true, ForeColor = Color.FromArgb(100, 116, 139), Margin = new Padding(0, 14, 0, 0), Text = "La clave se guarda solamente en esta computadora." };
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
                if (string.IsNullOrWhiteSpace(txtClave.Text) || txtClave.Text == "CAMBIAR-ESTA-CLAVE")
                {
                    estado.ForeColor = Color.FromArgb(220, 38, 38);
                    estado.Text = "Ingresá la misma clave configurada como ARES_API_KEY en Render.";
                    return;
                }

                new AresSettings { ServerUrl = txtServidor.Text.TrimEnd('/'), ApiKey = txtClave.Text }.GuardarLocal();
                guardar.Enabled = false;
                estado.ForeColor = Color.FromArgb(37, 99, 235);
                estado.Text = "Probando conexión…";
                await BuscarEquiposAsync();
                guardar.Enabled = true;
                estado.ForeColor = lblConexion.Text.StartsWith("Error") ? Color.FromArgb(220, 38, 38) : Color.FromArgb(22, 163, 74);
                estado.Text = lblConexion.Text.StartsWith("Error") ? lblConexion.Text : "Configuración guardada. Conexión correcta.";
            };
            contenido.Controls.Add(guardar);
            contenido.Controls.Add(estado);
            tarjeta.Controls.Add(contenido);
            tarjeta.Controls.Add(titulo);
            return tarjeta;
        }

        private static Panel CrearTarjeta() => new()
        {
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            Margin = new Padding(0)
        };

        private static Panel CrearEncabezadoTarjeta(string titulo, string detalle)
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.White, Padding = new Padding(22, 13, 10, 0) };
            panel.Controls.Add(new Label { Text = detalle, AutoSize = true, Location = new Point(24, 45), ForeColor = Color.FromArgb(100, 116, 139), Font = new Font("Segoe UI", 9F) });
            panel.Controls.Add(new Label { Text = titulo, AutoSize = true, Location = new Point(22, 14), ForeColor = Color.FromArgb(30, 41, 59), Font = new Font("Segoe UI", 13F, FontStyle.Bold) });
            return panel;
        }

        private static DataGridView CrearListaSimple(string[] columnas, List<string[]> filas)
        {
            var tabla = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersHeight = 44, RowTemplate = { Height = 50 }, EnableHeadersVisualStyles = false };
            tabla.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(248, 250, 252), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            tabla.DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.FromArgb(30, 41, 59), SelectionBackColor = Color.FromArgb(239, 246, 255), SelectionForeColor = Color.FromArgb(30, 41, 59), Padding = new Padding(10, 0, 0, 0) };
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
    }
}
