namespace ARES.Shared.Modelos;

public class AgentHeartbeat
{
    public string Id { get; set; } = "";
    public string Equipo { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string Sistema { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public bool BloqueadoLocalmente { get; set; }
    public string RequestToken { get; set; } = "";
}

public sealed class AgentStatus : AgentHeartbeat
{
    public DateTimeOffset UltimaConexionUtc { get; set; }
    public bool EstaEnLinea { get; set; }
    public bool BloqueadoAdministrativamente { get; set; }
    public bool SolicitudDesbloqueoPendiente { get; set; }
    public DateTimeOffset? SolicitudDesbloqueoUtc { get; set; }
}

public sealed class HeartbeatResponse
{
    public bool Accepted { get; set; }
    public DateTimeOffset ServerTimeUtc { get; set; }
    public bool BloqueadoAdministrativamente { get; set; }
}

public sealed class RestrictionRequest
{
    public bool Bloqueado { get; set; }
}

public sealed class AgentAuditEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AgentId { get; set; } = "";
    public string Equipo { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Detalle { get; set; } = "";
    public DateTimeOffset FechaUtc { get; set; }
}
