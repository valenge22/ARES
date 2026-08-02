namespace ARES.Shared.Modelos;

public class AgentHeartbeat
{
    public string Id { get; set; } = "";
    public string Equipo { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string Sistema { get; set; } = "";
    public string Version { get; set; } = "1.0";
}

public sealed class AgentStatus : AgentHeartbeat
{
    public DateTimeOffset UltimaConexionUtc { get; set; }
    public bool EstaEnLinea { get; set; }
}
