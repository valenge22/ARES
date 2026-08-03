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
    public string NombrePersonalizado { get; set; } = "";
    public DateTimeOffset UltimaConexionUtc { get; set; }
    public bool EstaEnLinea { get; set; }
    public bool BloqueadoAdministrativamente { get; set; }
    public bool SolicitudDesbloqueoPendiente { get; set; }
    public DateTimeOffset? SolicitudDesbloqueoUtc { get; set; }
    public string Grupo { get; set; } = "Grupo 1";
}

public sealed class RenameAgentRequest
{
    public string Nombre { get; set; } = "";
}

public sealed class HeartbeatResponse
{
    public bool Accepted { get; set; }
    public DateTimeOffset ServerTimeUtc { get; set; }
    public bool BloqueadoAdministrativamente { get; set; }
    public long HorarioVersion { get; set; }
    public List<ScheduleInterval> Horarios { get; set; } = [];
}

public sealed class RestrictionRequest
{
    public bool Bloqueado { get; set; }
}

public sealed class GroupRequest
{
    public string Grupo { get; set; } = "Grupo 1";
}

public sealed class ScheduleInterval
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AgentId { get; set; } = "";
    public string Empleado { get; set; } = "";
    public DateTimeOffset InicioUtc { get; set; }
    public DateTimeOffset FinUtc { get; set; }
}

public class SchedulePublication
{
    public int Mes { get; set; }
    public int Anio { get; set; }
    public string ZonaHoraria { get; set; } = "America/Argentina/Buenos_Aires";
    public List<ScheduleInterval> Horarios { get; set; } = [];
}

public sealed class ScheduleState : SchedulePublication
{
    public long Version { get; set; }
    public DateTimeOffset PublicadoUtc { get; set; }
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
