using ARES.Shared.Modelos;
using System.Text.Json;

namespace ARES.Agent;

internal sealed class SchedulePolicy
{
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ARES", "schedule.json");
    private List<ScheduleInterval> intervals = [];
    private long version;
    public SchedulePolicy() => Load();
    public void Update(HeartbeatResponse response)
    {
        if (response.HorarioVersion <= 0 || response.HorarioVersion < version) return;
        version = response.HorarioVersion; intervals = response.Horarios ?? [];
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Cache { Version = version, Intervals = intervals }));
    }
    public bool? MustBlock(DateTimeOffset nowUtc) => version <= 0 || intervals.Count == 0
        ? null : !intervals.Any(x => nowUtc >= x.InicioUtc && nowUtc < x.FinUtc);
    private void Load()
    {
        try
        {
            if (!File.Exists(path)) return;
            Cache? cache = JsonSerializer.Deserialize<Cache>(File.ReadAllText(path));
            if (cache is null) return;
            version = cache.Version; intervals = cache.Intervals;
        }
        catch { }
    }
    private sealed class Cache { public long Version { get; set; } public List<ScheduleInterval> Intervals { get; set; } = []; }
}
