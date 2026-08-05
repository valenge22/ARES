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
    public string MotivoEstadoLocal { get; set; } = "";
    public long HorarioVersionAplicada { get; set; }
    public bool EsServicioSistema { get; set; }
}

public sealed class AgentStatus : AgentHeartbeat
{
    public Guid OrganizationId { get; set; }
    public string NombrePersonalizado { get; set; } = "";
    public DateTimeOffset UltimaConexionUtc { get; set; }
    public bool EstaEnLinea { get; set; }
    public bool BloqueadoAdministrativamente { get; set; }
    public bool SolicitudDesbloqueoPendiente { get; set; }
    public DateTimeOffset? SolicitudDesbloqueoUtc { get; set; }
    public string Grupo { get; set; } = "Grupo 1";
    public DateTimeOffset? ExcepcionHastaUtc { get; set; }
    public bool? ExcepcionPermitirUso { get; set; }
    public string MotivoBloqueo { get; set; } = "Sin bloqueo";
    public DateTimeOffset? ProximoCambioUtc { get; set; }
    public bool ActualizacionDisponible { get; set; }
    public string UltimaVersion { get; set; } = "";
    public bool ActualizacionSolicitada { get; set; }
    public bool HorarioPendienteSincronizar { get; set; }
    public bool CredencialIndividual { get; set; }
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
    public DateTimeOffset? ExcepcionHastaUtc { get; set; }
    public bool? ExcepcionPermitirUso { get; set; }
    public int MargenEntradaMinutos { get; set; }
    public int MargenSalidaMinutos { get; set; }
    public string UltimaVersion { get; set; } = "";
    public string UrlActualizacion { get; set; } = "";
    public bool ActualizarAhora { get; set; }
    public string NuevaCredencialDispositivo { get; set; } = "";
}

public sealed class RestrictionRequest
{
    public bool Bloqueado { get; set; }
}

public sealed class GroupRequest
{
    public string Grupo { get; set; } = "Grupo 1";
}

public sealed class TemporaryOverrideRequest
{
    public bool PermitirUso { get; set; } = true;
    public DateTimeOffset HastaUtc { get; set; }
    public string Motivo { get; set; } = "Excepcion temporal";
}

public sealed class GroupPolicy
{
    public string Grupo { get; set; } = "Grupo 1";
    public int MargenEntradaMinutos { get; set; }
    public int MargenSalidaMinutos { get; set; }
}

public sealed class GroupPoliciesRequest
{
    public List<GroupPolicy> Grupos { get; set; } = [];
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

public sealed class ScheduleRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset FechaUtc { get; set; }
    public string Accion { get; set; } = "Publicacion";
    public ScheduleState Estado { get; set; } = new();
}

public sealed class RollbackScheduleRequest
{
    public string RevisionId { get; set; } = "";
}

public sealed class AgentAuditEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid OrganizationId { get; set; }
    public string AgentId { get; set; } = "";
    public string Equipo { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Detalle { get; set; } = "";
    public DateTimeOffset FechaUtc { get; set; }
}

public class ControlSessionHeartbeat
{
    public string Id { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string Equipo { get; set; } = "";
    public string Plataforma { get; set; } = "";
    public string Version { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string EstadoActualizacion { get; set; } = "Al dia";
}

public sealed class ControlSessionStatus : ControlSessionHeartbeat
{
    public Guid OrganizationId { get; set; }
    public DateTimeOffset UltimaConexionUtc { get; set; }
    public bool Activa { get; set; }
    public bool ActualizacionSolicitada { get; set; }
    public bool ActualizacionDisponible { get; set; }
    public string UltimaVersion { get; set; } = "";
}

public sealed class ControlSessionHeartbeatResponse
{
    public int Activas { get; set; }
    public bool ActualizarAhora { get; set; }
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class ControlUpdateRequest
{
    public List<string> SessionIds { get; set; } = [];
}
