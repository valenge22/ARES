namespace ARES.Shared.Modelos
{
    public class Computadora
    {
        public string Nombre { get; set; } = "";
        public string AgentId { get; set; } = "";
        public string DireccionIP { get; set; } = "";
        public bool EstaEncendida { get; set; }
        public bool EstaBloqueada { get; set; }
        public bool SolicitudDesbloqueoPendiente { get; set; }
        public DateTimeOffset? SolicitudDesbloqueoUtc { get; set; }
        public bool EstaLogueada { get; set; }
        public string SistemaOperativo { get; set; } = "";
    }
}
