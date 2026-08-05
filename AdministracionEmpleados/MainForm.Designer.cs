#nullable enable

namespace AdministracionEmpleados
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer? components = null;
        private Panel pnlMenu = null!;
        private Panel pnlMarca = null!;
        private Label lblEscudo = null!;
        private Label lblARES = null!;
        private Label lblVersion = null!;
        private FlowLayoutPanel flpNavegacion = null!;
        private Button btnEmpleados = null!;
        private Button btnEquipos = null!;
        private Button btnMonitor = null!;
        private Button btnSeguridad = null!;
        private Button btnPantallas = null!;
        private Button btnConfiguracion = null!;
        private Panel pnlCabecera = null!;
        private Label lblTituloSeccion = null!;
        private Label lblSubtitulo = null!;
        private Panel pnlEstadoConexion = null!;
        private Label lblPuntoConexion = null!;
        private Label lblConexion = null!;
        private Panel pnlContenido = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            pnlMenu = new Panel();
            flpNavegacion = new FlowLayoutPanel();
            btnEmpleados = CrearBotonMenu("  👥  Empleados");
            btnEquipos = CrearBotonMenu("  💻  Equipos");
            btnMonitor = CrearBotonMenu("  📊  Monitor");
            btnSeguridad = CrearBotonMenu("  🔒  Seguridad");
            btnPantallas = CrearBotonMenu("  📸  Pantallas");
            btnConfiguracion = CrearBotonMenu("  ⚙  Configuración");
            pnlMarca = new Panel();
            lblEscudo = new Label();
            lblARES = new Label();
            lblVersion = new Label();
            pnlCabecera = new Panel();
            lblTituloSeccion = new Label();
            lblSubtitulo = new Label();
            pnlEstadoConexion = new Panel();
            lblPuntoConexion = new Label();
            lblConexion = new Label();
            pnlContenido = new Panel();

            SuspendLayout();

            pnlMenu.BackColor = Color.FromArgb(11, 35, 64);
            pnlMenu.Dock = DockStyle.Left;
            pnlMenu.Width = 205;
            pnlMenu.Controls.Add(flpNavegacion);
            pnlMenu.Controls.Add(pnlMarca);

            pnlMarca.Dock = DockStyle.Top;
            pnlMarca.Height = 76;
            pnlMarca.BackColor = Color.FromArgb(11, 35, 64);
            pnlMarca.Controls.Add(lblEscudo);
            pnlMarca.Controls.Add(lblARES);
            pnlMarca.Controls.Add(lblVersion);

            lblEscudo.AutoSize = true;
            lblEscudo.Font = new Font("Segoe UI Emoji", 21F);
            lblEscudo.ForeColor = Color.FromArgb(56, 189, 248);
            lblEscudo.BackColor = Color.Transparent;
            lblEscudo.Location = new Point(17, 17);
            lblEscudo.Text = "🛡";

            lblARES.AutoSize = true;
            lblARES.Font = new Font("Segoe UI", 19F, FontStyle.Bold);
            lblARES.ForeColor = Color.White;
            lblARES.BackColor = Color.Transparent;
            lblARES.Location = new Point(59, 14);
            lblARES.Text = "ARES";

            lblVersion.AutoSize = true;
            lblVersion.Font = new Font("Segoe UI", 9F);
            lblVersion.ForeColor = Color.FromArgb(125, 211, 252);
            lblVersion.BackColor = Color.Transparent;
            lblVersion.Location = new Point(61, 45);
            lblVersion.Text = "CENTRO DE CONTROL";

            flpNavegacion.Dock = DockStyle.Fill;
            flpNavegacion.FlowDirection = FlowDirection.TopDown;
            flpNavegacion.WrapContents = false;
            flpNavegacion.Padding = new Padding(0, 10, 0, 0);
            flpNavegacion.BackColor = Color.FromArgb(11, 35, 64);
            flpNavegacion.Controls.AddRange(new Control[] {
                btnEmpleados, btnEquipos, btnMonitor, btnSeguridad,
                btnPantallas, btnConfiguracion
            });

            pnlCabecera.BackColor = Color.White;
            pnlCabecera.Dock = DockStyle.Top;
            pnlCabecera.Height = 76;
            pnlCabecera.Padding = new Padding(22, 0, 22, 0);
            pnlCabecera.Controls.Add(lblTituloSeccion);
            pnlCabecera.Controls.Add(lblSubtitulo);
            pnlCabecera.Controls.Add(pnlEstadoConexion);

            lblTituloSeccion.AutoSize = true;
            lblTituloSeccion.Font = new Font("Segoe UI", 15F, FontStyle.Regular);
            lblTituloSeccion.ForeColor = Color.FromArgb(48, 55, 62);
            lblTituloSeccion.Location = new Point(22, 13);
            lblTituloSeccion.Text = "Equipos";

            lblSubtitulo.AutoSize = true;
            lblSubtitulo.Font = new Font("Segoe UI", 8.5F);
            lblSubtitulo.ForeColor = Color.FromArgb(117, 125, 133);
            lblSubtitulo.Location = new Point(24, 44);
            lblSubtitulo.Text = "Administrá los dispositivos conectados a ARES";

            pnlEstadoConexion.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pnlEstadoConexion.BackColor = Color.FromArgb(240, 253, 244);
            pnlEstadoConexion.Location = new Point(750, 17);
            pnlEstadoConexion.Size = new Size(190, 40);
            pnlEstadoConexion.Controls.Add(lblPuntoConexion);
            pnlEstadoConexion.Controls.Add(lblConexion);

            lblPuntoConexion.AutoSize = true;
            lblPuntoConexion.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            lblPuntoConexion.ForeColor = Color.FromArgb(34, 197, 94);
            lblPuntoConexion.Location = new Point(12, 8);
            lblPuntoConexion.Text = "●";

            lblConexion.AutoSize = true;
            lblConexion.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            lblConexion.ForeColor = Color.FromArgb(22, 101, 52);
            lblConexion.Location = new Point(36, 11);
            lblConexion.Text = "Comprobando conexión";

            pnlContenido.BackColor = Color.FromArgb(235, 237, 239);
            pnlContenido.Dock = DockStyle.Fill;
            pnlContenido.Padding = new Padding(18);

            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(235, 237, 239);
            ClientSize = new Size(1200, 720);
            MinimumSize = new Size(1000, 620);
            Controls.Add(pnlContenido);
            Controls.Add(pnlCabecera);
            Controls.Add(pnlMenu);
            Font = new Font("Segoe UI", 9F);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "ARES — Centro de control";
            Load += MainForm_Load;
            ResumeLayout(false);
        }

        private static Button CrearBotonMenu(string texto)
        {
            return new Button
            {
                BackColor = Color.FromArgb(11, 35, 64),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.2F),
                ForeColor = Color.FromArgb(238, 238, 238),
                Height = 42,
                Margin = new Padding(0),
                Padding = new Padding(10, 0, 0, 0),
                Size = new Size(205, 42),
                Text = texto,
                TextAlign = ContentAlignment.MiddleLeft,
                UseVisualStyleBackColor = false
            };
        }
    }
}
