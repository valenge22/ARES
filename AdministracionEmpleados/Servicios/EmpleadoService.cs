using ARES.Shared.Modelos;

namespace AdministracionEmpleados.Servicios
{
    public class EmpleadoService
    {
        private readonly List<Empleado> empleados;

        public EmpleadoService()
        {
            empleados = new List<Empleado>();
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

        public void ActualizarEquiposDetectados(IEnumerable<AgenteDetectado> detectados)
        {
            foreach (Empleado empleado in empleados)
                empleado.Computadora.EstaEncendida = false;

            foreach (AgenteDetectado agente in detectados)
            {
                Empleado? empleado = empleados.FirstOrDefault(e =>
                    e.Computadora.AgentId == agente.Id ||
                    e.Computadora.Nombre.Equals(agente.Equipo, StringComparison.OrdinalIgnoreCase) ||
                    e.Computadora.DireccionIP == agente.DireccionIp);

                if (empleado == null)
                {
                    empleado = new Empleado
                    {
                        Nombre = agente.Usuario,
                        Computadora = new Computadora { Nombre = agente.Equipo }
                    };
                    empleados.Add(empleado);
                }

                empleado.Nombre = agente.Usuario;
                empleado.Grupo = string.IsNullOrWhiteSpace(agente.Grupo) ? "Grupo 1" : agente.Grupo;
                empleado.Computadora.AgentId = agente.Id;
                empleado.Computadora.Nombre = agente.Equipo;
                empleado.Computadora.DireccionIP = agente.DireccionIp;
                empleado.Computadora.SistemaOperativo = agente.Sistema;
                empleado.Computadora.EstaEncendida = true;
                empleado.Computadora.EstaLogueada = !string.IsNullOrWhiteSpace(agente.Usuario);
                empleado.Computadora.EstaBloqueada = agente.Bloqueado;
                empleado.Computadora.SolicitudDesbloqueoPendiente = agente.SolicitudDesbloqueo;
                empleado.Computadora.SolicitudDesbloqueoUtc = agente.SolicitudUtc;
                empleado.Computadora.MotivoBloqueo = agente.MotivoBloqueo;
                empleado.Computadora.ProximoCambioUtc = agente.ProximoCambioUtc;
                empleado.Computadora.ExcepcionHastaUtc = agente.ExcepcionHastaUtc;
                empleado.Computadora.ActualizacionDisponible = agente.ActualizacionDisponible;
                empleado.Computadora.Version = agente.Version;
                empleado.Computadora.UltimaVersion = agente.UltimaVersion;
            }
        }
    }
}
