using AdministracionEmpleados.Modelos;
using AdministracionEmpleados.Servicios;

namespace AdministracionEmpleados
{
    public partial class EmpleadosForm : Form
    {
        private readonly EmpleadoService empleadoService;
        private readonly List<Empleado> empleados;

        public EmpleadosForm()
        {
            InitializeComponent();

            empleadoService = new EmpleadoService();
            empleados = empleadoService.ObtenerEmpleados();

            CargarEmpleados();
        }

        private void CargarEmpleados()
        {
            dgvEmpleados.Rows.Clear();

            foreach (Empleado empleado in empleados)
            {
                dgvEmpleados.Rows.Add(
                    empleado.Nombre,
                    empleado.Computadora.Nombre,
                    empleado.Computadora.EstaEncendida ? "Sí" : "No",
                    empleado.Computadora.EstaLogueada ? "Sí" : "No",
                    empleado.Computadora.EstaBloqueada
                        ? "Bloqueada"
                        : "Desbloqueada"
                );
            }
        }

        private Empleado? ObtenerEmpleadoSeleccionado()
        {
            if (dgvEmpleados.CurrentRow == null)
            {
                return null;
            }

            int indiceSeleccionado = dgvEmpleados.CurrentRow.Index;

            if (indiceSeleccionado < 0 ||
                indiceSeleccionado >= empleados.Count)
            {
                return null;
            }

            return empleados[indiceSeleccionado];
        }

        private void BloquearEmpleadoSeleccionado()
        {
            Empleado? empleadoSeleccionado =
                ObtenerEmpleadoSeleccionado();

            if (empleadoSeleccionado == null)
            {
                MessageBox.Show(
                    "Seleccioná un empleado antes de bloquear.",
                    "ARES",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            empleadoService.Bloquear(empleadoSeleccionado);

            CargarEmpleados();
        }

        private void DesbloquearEmpleadoSeleccionado()
        {
            Empleado? empleadoSeleccionado =
                ObtenerEmpleadoSeleccionado();

            if (empleadoSeleccionado == null)
            {
                MessageBox.Show(
                    "Seleccioná un empleado antes de desbloquear.",
                    "ARES",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            empleadoService.Desbloquear(empleadoSeleccionado);

            CargarEmpleados();
        }

        private void btnBloquear_Click(
            object sender,
            EventArgs e)
        {
            BloquearEmpleadoSeleccionado();
        }

        private void btnDesbloquear_Click(
            object sender,
            EventArgs e)
        {
            DesbloquearEmpleadoSeleccionado();
        }

        private void dgvEmpleados_CellContentClick(
            object sender,
            DataGridViewCellEventArgs e)
        {
        }

        private void panel1_Paint(
            object sender,
            PaintEventArgs e)
        {
        }

        private void button1_Click(
            object sender,
            EventArgs e)
        {
            BloquearEmpleadoSeleccionado();
        }

        private void btnDesbloquear_Click_1(
            object sender,
            EventArgs e)
        {
            DesbloquearEmpleadoSeleccionado();
        }
    }
}