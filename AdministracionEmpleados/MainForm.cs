namespace AdministracionEmpleados
{
    public partial class MainForm : Form
    {
        private Form? formularioActivo;

        public MainForm()
        {
            InitializeComponent();
        }

        private void AbrirFormulario(Form formulario)
        {
            if (formularioActivo != null)
            {
                formularioActivo.Close();
            }

            formularioActivo = formulario;

            formulario.TopLevel = false;
            formulario.FormBorderStyle = FormBorderStyle.None;
            formulario.Dock = DockStyle.Fill;

            pnlContenido.Controls.Clear();
            pnlContenido.Controls.Add(formulario);
            pnlContenido.Tag = formulario;

            formulario.BringToFront();
            formulario.Show();
        }

        private void MostrarEmpleados()
        {
            AbrirFormulario(new EmpleadosForm());
            MarcarBotonActivo(btnEmpleados);
        }

        private void MarcarBotonActivo(Button botonActivo)
        {
            foreach (Control control in pnlMenu.Controls)
            {
                if (control is Button boton)
                {
                    boton.BackColor = Color.FromArgb(31, 41, 55);
                    boton.ForeColor = Color.White;
                }
            }

            botonActivo.BackColor = Color.FromArgb(55, 65, 81);
            botonActivo.ForeColor = Color.White;
        }

        private void btnEmpleados_Click(object sender, EventArgs e)
        {
            MostrarEmpleados();
        }

        private void lblARES_Click(object sender, EventArgs e)
        {
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            MostrarEmpleados();
        }
    }
}