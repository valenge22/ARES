using ARES.Shared.Modelos;
using System.Collections.Concurrent;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string apiKey = builder.Configuration["ARES_API_KEY"]
    ?? Environment.GetEnvironmentVariable("ARES_API_KEY")
    ?? "CAMBIAR-ESTA-CLAVE";
string dataPath = Path.Combine(AppContext.BaseDirectory, "data", "agents.json");
Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);

var agents = new ConcurrentDictionary<string, AgentStatus>(StringComparer.OrdinalIgnoreCase);
var saveLock = new SemaphoreSlim(1, 1);
if (File.Exists(dataPath))
{
    foreach (AgentStatus agent in JsonSerializer.Deserialize<List<AgentStatus>>(File.ReadAllText(dataPath)) ?? [])
        agents[agent.Id] = agent;
}

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/health") &&
        (!context.Request.Headers.TryGetValue("X-ARES-Key", out var supplied) || supplied != apiKey))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Clave ARES inválida." });
        return;
    }
    await next();
});

app.MapGet("/health", () => Results.Ok(new { service = "ARES Server", status = "ok" }));

app.MapPost("/api/agents/heartbeat", async (AgentHeartbeat heartbeat) =>
{
    if (string.IsNullOrWhiteSpace(heartbeat.Id) || string.IsNullOrWhiteSpace(heartbeat.Equipo))
        return Results.BadRequest(new { error = "Identidad de agente incompleta." });

    agents[heartbeat.Id] = new AgentStatus
    {
        Id = heartbeat.Id,
        Equipo = heartbeat.Equipo,
        Usuario = heartbeat.Usuario,
        Sistema = heartbeat.Sistema,
        Version = heartbeat.Version,
        UltimaConexionUtc = DateTimeOffset.UtcNow,
        EstaEnLinea = true
    };
    await GuardarAsync();
    return Results.Ok(new { accepted = true, serverTimeUtc = DateTimeOffset.UtcNow });
});

app.MapGet("/api/agents", () =>
{
    DateTimeOffset limite = DateTimeOffset.UtcNow.AddSeconds(-35);
    return agents.Values
        .Select(a => new AgentStatus
        {
            Id = a.Id, Equipo = a.Equipo, Usuario = a.Usuario, Sistema = a.Sistema,
            Version = a.Version, UltimaConexionUtc = a.UltimaConexionUtc,
            EstaEnLinea = a.UltimaConexionUtc >= limite
        })
        .OrderBy(a => a.Equipo);
});

app.Run();

async Task GuardarAsync()
{
    await saveLock.WaitAsync();
    try
    {
        string temporal = dataPath + ".tmp";
        await File.WriteAllTextAsync(temporal, JsonSerializer.Serialize(agents.Values,
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporal, dataPath, true);
    }
    finally { saveLock.Release(); }
}
