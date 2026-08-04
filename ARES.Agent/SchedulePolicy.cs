using ARES.Shared.Modelos;
using System.Text.Json;

namespace ARES.Agent;

internal sealed class SchedulePolicy
{
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ARES", "schedule.json");
    private List<ScheduleInterval> intervals = [];
    private long version;
    private DateTimeOffset? overrideUntilUtc;
    private bool? overrideAllow;
    private int earlyMinutes;
    private int lateMinutes;
    private bool cachedManualBlock;
    public SchedulePolicy() => Load();
    public void Update(HeartbeatResponse response)
    {
        if (response.HorarioVersion <= 0 || response.HorarioVersion < version) return;
        version = response.HorarioVersion; intervals = response.Horarios ?? [];
        overrideUntilUtc = response.ExcepcionHastaUtc; overrideAllow = response.ExcepcionPermitirUso;
        earlyMinutes = response.MargenEntradaMinutos; lateMinutes = response.MargenSalidaMinutos;
        cachedManualBlock = response.BloqueadoAdministrativamente;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Cache { Version = version, Intervals = intervals,
            OverrideUntilUtc = overrideUntilUtc, OverrideAllow = overrideAllow, EarlyMinutes = earlyMinutes, LateMinutes = lateMinutes,
            ManualBlock = cachedManualBlock }));
    }
    public PolicyDecision Evaluate(bool? manualBlock, DateTimeOffset nowUtc)
    {
        if (manualBlock.HasValue) cachedManualBlock = manualBlock.Value;
        if (overrideUntilUtc > nowUtc && overrideAllow.HasValue)
            return new PolicyDecision(!overrideAllow.Value, overrideAllow.Value ? "Excepcion: uso permitido" : "Excepcion: bloqueo temporal");
        if (cachedManualBlock) return new PolicyDecision(true, "Bloqueo manual");
        if (version <= 0 || intervals.Count == 0) return new PolicyDecision(false, "Sin horario asignado");
        bool inside = intervals.Any(x => nowUtc >= x.InicioUtc.AddMinutes(-earlyMinutes) && nowUtc < x.FinUtc.AddMinutes(lateMinutes));
        return new PolicyDecision(!inside, inside ? "Dentro del turno" : "Fuera del horario");
    }
    private void Load()
    {
        try
        {
            if (!File.Exists(path)) return;
            Cache? cache = JsonSerializer.Deserialize<Cache>(File.ReadAllText(path));
            if (cache is null) return;
            version = cache.Version; intervals = cache.Intervals; overrideUntilUtc = cache.OverrideUntilUtc;
            overrideAllow = cache.OverrideAllow; earlyMinutes = cache.EarlyMinutes; lateMinutes = cache.LateMinutes;
            cachedManualBlock = cache.ManualBlock;
        }
        catch { }
    }
    public long Version => version;
    private sealed class Cache { public long Version { get; set; } public List<ScheduleInterval> Intervals { get; set; } = [];
        public DateTimeOffset? OverrideUntilUtc { get; set; } public bool? OverrideAllow { get; set; }
        public int EarlyMinutes { get; set; } public int LateMinutes { get; set; } public bool ManualBlock { get; set; } }
}

internal sealed record PolicyDecision(bool Blocked, string Reason);
