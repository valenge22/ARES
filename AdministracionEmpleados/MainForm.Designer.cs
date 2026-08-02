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

            pnlMenu.BackColor = Color.FromArgb(15, 23, 42);
            pnlMenu.Dock = DockStyle.Left;
            pnlMenu.Width = 230;
            pnlMenu.Controls.Add(flpNavegacion);
            pnlMenu.Controls.Add(pnlMarca);

            pnlMarca.Dock = DockStyle.Top;
            pnlMarca.Height = 104;
            pnlMarca.BackColor = Color.FromArgb(15, 23, 42);
            pnlMarca.Controls.Add(lblEscudo);
            pnlMarca.Controls.Add(lblARES);
            pnlMarca.Controls.Add(lblVersion);

            lblEscudo.AutoSize = true;
            lblEscudo.Font = new Font("Segoe UI Emoji", 25F);
            lblEscudo.ForeColor = Color.FromArgb(56, 189, 248);
            lblEscudo.Location = new Point(20, 25);
            lblEscudo.Text = "🛡";

            lblARES.AutoSize = true;
            lblARES.Font = new Font("Segoe UI", 22F, FontStyle.Bold);
            lblARES.ForeColor = Color.White;
            lblARES.Location = new Point(70, 22);
            lblARES.Text = "ARES";

            lblVersion.AutoSize = true;
            lblVersion.Font = new Font("Segoe UI", 9F);
            lblVersion.ForeColor = Color.FromArgb(148, 163, 184);
            lblVersion.Location = new Point(73, 64);
            lblVersion.Text = "CONTROL CENTER  v1.0";

            flpNavegacion.Dock = DockStyle.Fill;
            flpNavegacion.FlowDirection = FlowDirection.TopDown;
            flpNavegacion.WrapContents = false;
            flpNavegacion.Padding = new Padding(12, 18, 12, 0);
            flpNavegacion.BackColor = Color.FromArgb(15, 23, 42);
            flpNavegacion.Controls.AddRange(new Control[] {
                btnEmpleados, btnEquipos, btnMonitor, btnSeguridad,
                btnPantallas, btnConfiguracion
            });

            pnlCabecera.BackColor = Color.White;
            pnlCabecera.Dock = DockStyle.Top;
            pnlCabecera.Height = 104;
            pnlCabecera.Padding = new Padding(30, 0, 30, 0);
            pnlCabecera.Controls.Add(lblTituloSeccion);
            pnlCabecera.Controls.Add(lblSubtitulo);
            pnlCabecera.Controls.Add(pnlEstadoConexion);

            lblTituloSeccion.AutoSize = true;
            lblTituloSeccion.Font = new Font("Segoe UI", 20F, FontStyle.Bold);
            lblTituloSeccion.ForeColor = Color.FromArgb(15, 23, 42);
            lblTituloSeccion.Location = new Point(30, 20);
            lblTituloSeccion.Text = "Equipos";

            lblSubtitulo.AutoSize = true;
            lblSubtitulo.Font = new Font("Segoe UI", 10F);
            lblSubtitulo.ForeColor = Color.FromArgb(100, 116, 139);
            lblSubtitulo.Location = new Point(33, 62);
            lblSubtitulo.Text = "Administrá los dispositivos conectados a ARES";

            pnlEstadoConexion.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pnlEstadoConexion.BackColor = Color.FromArgb(240, 253, 244);
            pnlEstadoConexion.Location = new Point(735, 29);
            pnlEstadoConexion.Size = new Size(205, 44);
            pnlEstadoConexion.Controls.Add(lblPuntoConexion);
            pnlEstadoConexion.Controls.Add(lblConexion);

            lblPuntoConexion.AutoSize = true;
            lblPuntoConexion.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            lblPuntoConexion.ForeColor = Color.FromArgb(34, 197, 94);
            lblPuntoConexion.Location = new Point(14, 10);
            lblPuntoConexion.Text = "●";

            lblConexion.AutoSize = true;
            lblConexion.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            lblConexion.ForeColor = Color.FromArgb(22, 101, 52);
            lblConexion.Location = new Point(39, 13);
            lblConexion.Text = "Comprobando conexión";

            pnlContenido.BackColor = Color.FromArgb(241, 245, 249);
            pnlContenido.Dock = DockStyle.Fill;
            pnlContenido.Padding = new Padding(30);

            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(241, 245, 249);
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
                BackColor = Color.FromArgb(15, 23, 42),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = Color.FromArgb(203, 213, 225),
                Height = 50,
                Margin = new Padding(0, 3, 0, 3),
                Padding = new Padding(8, 0, 0, 0),
                Size = new Size(206, 50),
                Text = texto,
                TextAlign = ContentAlignment.MiddleLeft,
                UseVisualStyleBackColor = false
            };
        }
    }
}
