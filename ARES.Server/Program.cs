using ARES.Shared.Modelos;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string apiKey = builder.Configuration["ARES_API_KEY"]
    ?? Environment.GetEnvironmentVariable("ARES_API_KEY")
    ?? "CAMBIAR-ESTA-CLAVE";
string dataPath = Path.Combine(AppContext.BaseDirectory, "data", "agents.json");
string auditPath = Path.Combine(AppContext.BaseDirectory, "data", "audit.json");
Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);

var agents = new ConcurrentDictionary<string, AgentStatus>(StringComparer.OrdinalIgnoreCase);
var audit = new ConcurrentQueue<AgentAuditEvent>();
var saveLock = new SemaphoreSlim(1, 1);
var requestLimits = new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);
if (File.Exists(dataPath))
{
    foreach (AgentStatus agent in JsonSerializer.Deserialize<List<AgentStatus>>(File.ReadAllText(dataPath)) ?? [])
        agents[agent.Id] = agent;
}
if (File.Exists(auditPath))
{
    foreach (AgentAuditEvent evento in JsonSerializer.Deserialize<List<AgentAuditEvent>>(File.ReadAllText(auditPath)) ?? [])
        audit.Enqueue(evento);
}

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/health") &&
        !context.Request.Path.StartsWithSegments("/solicitar") &&
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

    bool existia = agents.TryGetValue(heartbeat.Id, out AgentStatus? anterior);
    bool estabaEnLinea = existia && anterior!.EstaEnLinea;
    agents[heartbeat.Id] = new AgentStatus
    {
        Id = heartbeat.Id,
        Equipo = heartbeat.Equipo,
        Usuario = heartbeat.Usuario,
        Sistema = heartbeat.Sistema,
        Version = heartbeat.Version,
        UltimaConexionUtc = DateTimeOffset.UtcNow,
        EstaEnLinea = true,
        // Si Render reinicio sin almacenamiento persistente, el agente conserva el
        // bloqueo local y lo vuelve a registrar para evitar un desbloqueo accidental.
        BloqueadoAdministrativamente = anterior?.BloqueadoAdministrativamente ?? heartbeat.BloqueadoLocalmente,
        SolicitudDesbloqueoPendiente = anterior?.SolicitudDesbloqueoPendiente ?? false,
        SolicitudDesbloqueoUtc = anterior?.SolicitudDesbloqueoUtc
        ,RequestToken = string.IsNullOrWhiteSpace(heartbeat.RequestToken) ? anterior?.RequestToken ?? "" : heartbeat.RequestToken,
        NombrePersonalizado = anterior?.NombrePersonalizado ?? ""
    };
    if (!estabaEnLinea)
        await RegistrarEventoAsync(heartbeat.Id, heartbeat.Equipo, "AGENTE_CONECTADO", "ARES Agent inició o recuperó la conexión.");
    await GuardarAsync();
    return Results.Ok(new HeartbeatResponse
    {
        Accepted = true,
        ServerTimeUtc = DateTimeOffset.UtcNow,
        BloqueadoAdministrativamente = agents[heartbeat.Id].BloqueadoAdministrativamente
    });
});

app.MapPut("/api/agents/{id}/restriction", async (string id, RestrictionRequest request) =>
{
    if (!agents.TryGetValue(id, out AgentStatus? agente))
        return Results.NotFound(new { error = "El agente no está registrado." });

    agente.BloqueadoAdministrativamente = request.Bloqueado;
    agente.SolicitudDesbloqueoPendiente = false;
    agente.SolicitudDesbloqueoUtc = null;
    await RegistrarEventoAsync(id, agente.Equipo,
        request.Bloqueado ? "USUARIO_BLOQUEADO" : "USUARIO_DESBLOQUEADO",
        request.Bloqueado ? "Restricción activada desde la consola ARES." : "Restricción retirada desde la consola ARES.");
    await GuardarAsync();
    return Results.Ok(new { updated = true, bloqueado = request.Bloqueado });
});

app.MapPost("/api/agents/{id}/unlock-request", async (string id) =>
{
    if (!agents.TryGetValue(id, out AgentStatus? agente))
        return Results.NotFound(new { error = "El agente no está registrado." });
    if (!agente.BloqueadoAdministrativamente)
        return Results.Conflict(new { error = "El equipo no está bloqueado." });

    if (!agente.SolicitudDesbloqueoPendiente)
    {
        agente.SolicitudDesbloqueoPendiente = true;
        agente.SolicitudDesbloqueoUtc = DateTimeOffset.UtcNow;
        await RegistrarEventoAsync(id, agente.Equipo, "SOLICITUD_DESBLOQUEO",
            "El usuario solicitó al administrador que retire la restricción.");
        await GuardarAsync();
    }
    return Results.Ok(new { received = true, requestedAtUtc = agente.SolicitudDesbloqueoUtc });
});

app.MapPut("/api/agents/{id}/name", async (string id, RenameAgentRequest request) =>
{
    if (!agents.TryGetValue(id, out AgentStatus? agente))
        return Results.NotFound(new { error = "El agente no está registrado." });
    string nombre = request.Nombre.Trim();
    if (nombre.Length is < 1 or > 50)
        return Results.BadRequest(new { error = "El nombre debe tener entre 1 y 50 caracteres." });
    agente.NombrePersonalizado = nombre;
    await RegistrarEventoAsync(id, nombre, "EQUIPO_RENOMBRADO", $"Nombre real: {agente.Equipo}.");
    await GuardarAsync();
    return Results.Ok(new { updated = true, nombre });
});

app.MapGet("/solicitar/{token}", (string token) =>
{
    AgentStatus? agente = agents.Values.FirstOrDefault(a =>
        !string.IsNullOrWhiteSpace(a.RequestToken) &&
        a.RequestToken.Length == token.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a.RequestToken), Encoding.UTF8.GetBytes(token)));
    if (agente is null) return Results.NotFound("Enlace de solicitud inválido.");
    string equipo = System.Net.WebUtility.HtmlEncode(agente.Equipo);
    string html = $$"""
    <!doctype html><html lang="es"><meta name="viewport" content="width=device-width,initial-scale=1">
    <title>ARES · Solicitar desbloqueo</title><style>
    body{margin:0;background:#0f172a;color:#fff;font:16px Segoe UI,Arial;display:grid;place-items:center;min-height:100vh}
    main{width:min(420px,85vw);text-align:center;padding:32px;background:#172554;border-radius:20px}
    h1{color:#38bdf8}button{border:0;border-radius:10px;padding:14px 22px;background:#2563eb;color:white;font-weight:700;font-size:16px}
    </style><main><h1>ARES</h1><h2>{{equipo}}</h2><p>Enviá una solicitud al administrador para recuperar el acceso.</p>
    <form method="post"><button type="submit">Solicitar desbloqueo</button></form></main></html>
    """;
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/solicitar/{token}", async (string token) =>
{
    AgentStatus? agente = agents.Values.FirstOrDefault(a => a.RequestToken == token);
    if (agente is null) return Results.NotFound("Enlace de solicitud inválido.");
    DateTimeOffset ahora = DateTimeOffset.UtcNow;
    if (requestLimits.TryGetValue(token, out var ultima) && ahora - ultima < TimeSpan.FromMinutes(1))
        return Results.Content("Solicitud ya enviada. Esperá la respuesta del administrador.", "text/plain; charset=utf-8");
    requestLimits[token] = ahora;
    agente.SolicitudDesbloqueoPendiente = true;
    agente.SolicitudDesbloqueoUtc = ahora;
    await RegistrarEventoAsync(agente.Id, agente.Equipo, "SOLICITUD_DESBLOQUEO",
        "Solicitud enviada desde el portal móvil del equipo.");
    await GuardarAsync();
    return Results.Content("Solicitud enviada correctamente. El administrador ya fue notificado.", "text/plain; charset=utf-8");
});

app.MapPost("/api/agents/{id}/closed", async (string id) =>
{
    if (!agents.TryGetValue(id, out AgentStatus? agente)) return Results.NotFound();
    agente.EstaEnLinea = false;
    await RegistrarEventoAsync(id, agente.Equipo, "AGENTE_CERRADO", "El agente notificó un cierre normal.");
    await GuardarAsync();
    return Results.Ok();
});

app.MapGet("/api/audit", () => audit.OrderByDescending(e => e.FechaUtc).Take(500));

app.MapGet("/api/agents", () =>
{
    DateTimeOffset limite = DateTimeOffset.UtcNow.AddSeconds(-35);
    return agents.Values
        .Select(a => new AgentStatus
        {
            Id = a.Id, Equipo = a.Equipo, Usuario = a.Usuario, Sistema = a.Sistema,
            Version = a.Version, UltimaConexionUtc = a.UltimaConexionUtc,
            EstaEnLinea = a.UltimaConexionUtc >= limite,
            BloqueadoAdministrativamente = a.BloqueadoAdministrativamente,
            SolicitudDesbloqueoPendiente = a.SolicitudDesbloqueoPendiente,
            SolicitudDesbloqueoUtc = a.SolicitudDesbloqueoUtc,
            NombrePersonalizado = a.NombrePersonalizado
        })
        .OrderBy(a => a.Equipo);
});

app.MapDelete("/api/agents", async () =>
{
    int eliminados = agents.Count;
    agents.Clear();
    await RegistrarEventoAsync("SERVER", "Servidor ARES", "LISTA_EQUIPOS_LIMPIADA",
        $"Se eliminaron {eliminados} equipos registrados. Los agentes conectados volverán a registrarse automáticamente.");
    await GuardarAsync();
    return Results.Ok(new { deleted = eliminados });
});

_ = MonitorOfflineAsync(app.Lifetime.ApplicationStopping);
app.Run();

async Task MonitorOfflineAsync(CancellationToken cancelacion)
{
    while (!cancelacion.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), cancelacion);
        DateTimeOffset limite = DateTimeOffset.UtcNow.AddSeconds(-35);
        foreach (AgentStatus agente in agents.Values.Where(a => a.EstaEnLinea && a.UltimaConexionUtc < limite))
        {
            agente.EstaEnLinea = false;
            await RegistrarEventoAsync(agente.Id, agente.Equipo, "AGENTE_DESCONECTADO",
                "El agente dejó de responder; hora estimada por vencimiento del heartbeat.");
            await GuardarAsync();
        }
    }
}

async Task RegistrarEventoAsync(string agentId, string equipo, string tipo, string detalle)
{
    audit.Enqueue(new AgentAuditEvent
    {
        AgentId = agentId,
        Equipo = equipo,
        Tipo = tipo,
        Detalle = detalle,
        FechaUtc = DateTimeOffset.UtcNow
    });
    while (audit.Count > 2000) audit.TryDequeue(out _);
    await GuardarAuditoriaAsync();
}

async Task GuardarAuditoriaAsync()
{
    await saveLock.WaitAsync();
    try
    {
        string temporal = auditPath + ".tmp";
        await File.WriteAllTextAsync(temporal, JsonSerializer.Serialize(audit,
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporal, auditPath, true);
    }
    finally { saveLock.Release(); }
}

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
