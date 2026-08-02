using AdministracionEmpleados.Modelos;

namespace AdministracionEmpleados.Servicios
{
    public class EmpleadoService
    {
        private readonly List<Empleado> empleados;

        public EmpleadoService()
        {
            empleados = new List<Empleado>
            {
                new Empleado
                {
                    Nombre = "Valentín",
                    Computadora = new Computadora
                    {
                        Nombre = "PC-01",
                        DireccionIP = "192.168.1.10",
                        EstaEncendida = true,
                        EstaLogueada = true,
                        EstaBloqueada = false,
                        SistemaOperativo = "Windows 11"
                    }
                },

                new Empleado
                {
                    Nombre = "Melany",
                    Computadora = new Computadora
                    {
                        Nombre = "PC-02",
                        DireccionIP = "192.168.1.11",
                        EstaEncendida = true,
                        EstaLogueada = true,
                        EstaBloqueada = true,
                        SistemaOperativo = "Windows 11"
                    }
                },

                new Empleado
                {
                    Nombre = "Agostina",
                    Computadora = new Computadora
                    {
                        Nombre = "PC-03",
                        DireccionIP = "192.168.1.12",
                        EstaEncendida = false,
                        EstaLogueada = false,
                        EstaBloqueada = true,
                        SistemaOperativo = "Windows 10"
                    }
                }
            };
        }

        public List<Empleado> ObtenerEmpleados()
        {
            return empleados;
        }

        public void Bloquear(Empleado empleado)
        {
            empleado.Computadora.EstaBloqueada = true;
        }

        public void Desbloquear(Empleado empleado)
        {
            empleado.Computadora.EstaBloqueada = false;
        }
    }
}