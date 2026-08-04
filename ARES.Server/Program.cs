using ARES.Shared.Modelos;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using System.IO.Compression;
using System.Net;
using System.Net.Mail;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 100 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 110 * 1024 * 1024);
var app = builder.Build();

string apiKey = builder.Configuration["ARES_API_KEY"]
    ?? Environment.GetEnvironmentVariable("ARES_API_KEY")
    ?? "CAMBIAR-ESTA-CLAVE";
string dataPath = Path.Combine(AppContext.BaseDirectory, "data", "agents.json");
string auditPath = Path.Combine(AppContext.BaseDirectory, "data", "audit.json");
string schedulePath = Path.Combine(AppContext.BaseDirectory, "data", "schedule.json");
string historyPath = Path.Combine(AppContext.BaseDirectory, "data", "schedule-history.json");
string policiesPath = Path.Combine(AppContext.BaseDirectory, "data", "group-policies.json");
string updatePackagePath = Path.Combine(AppContext.BaseDirectory, "data", "agent-update.zip");
string updateVersionPath = Path.Combine(AppContext.BaseDirectory, "data", "agent-update-version.txt");
string controlSessionsPath = Path.Combine(AppContext.BaseDirectory, "data", "control-sessions.json");
string controlWindowsPackagePath = Path.Combine(AppContext.BaseDirectory, "data", "control-windows-update.zip");
string controlMacPackagePath = Path.Combine(AppContext.BaseDirectory, "data", "control-macos-update.pkg");
string latestWindowsControlVersion = "1.2.3";
string latestMacControlVersion = "1.1.3";
Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);

var agents = new ConcurrentDictionary<string, AgentStatus>(StringComparer.OrdinalIgnoreCase);
var audit = new ConcurrentQueue<AgentAuditEvent>();
var saveLock = new SemaphoreSlim(1, 1);
var requestLimits = new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);
var controlSessions = new ConcurrentDictionary<string, ControlSessionStatus>(StringComparer.OrdinalIgnoreCase);
if (File.Exists(controlSessionsPath))
    foreach (ControlSessionStatus session in JsonSerializer.Deserialize<List<ControlSessionStatus>>(File.ReadAllText(controlSessionsPath)) ?? [])
        controlSessions[session.Id] = session;
ScheduleState schedule = File.Exists(schedulePath)
    ? JsonSerializer.Deserialize<ScheduleState>(File.ReadAllText(schedulePath)) ?? new()
    : new();
var scheduleHistory = File.Exists(historyPath)
    ? JsonSerializer.Deserialize<List<ScheduleRevision>>(File.ReadAllText(historyPath)) ?? []
    : new List<ScheduleRevision>();
var groupPolicies = File.Exists(policiesPath)
    ? JsonSerializer.Deserialize<List<GroupPolicy>>(File.ReadAllText(policiesPath)) ?? []
    : new List<GroupPolicy>();
foreach (string group in new[] { "Grupo 1", "Grupo 2", "Grupo 3" })
    if (!groupPolicies.Any(p => p.Grupo == group)) groupPolicies.Add(new GroupPolicy { Grupo = group });
string latestAgentVersion = builder.Configuration["ARES_LATEST_AGENT_VERSION"] ?? "1.6.1";
string agentUpdateUrl = builder.Configuration["ARES_AGENT_UPDATE_URL"]
    ?? "https://github.com/valenge22/ARES/releases/download/v1.6.1/ARES-Agent-Windows-x64.zip";
if (File.Exists(updateVersionPath)) latestAgentVersion = File.ReadAllText(updateVersionPath).Trim();
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

app.MapPost("/api/control-sessions/heartbeat", async (ControlSessionHeartbeat heartbeat, HttpRequest httpRequest) =>
{
    if (string.IsNullOrWhiteSpace(heartbeat.Id)) return Results.BadRequest();
    DateTimeOffset now = DateTimeOffset.UtcNow;
    controlSessions.AddOrUpdate(heartbeat.Id,
        _ => new ControlSessionStatus { Id = heartbeat.Id, Usuario = heartbeat.Usuario, Equipo = heartbeat.Equipo,
            Plataforma = heartbeat.Plataforma, Version = heartbeat.Version, Nombre = string.IsNullOrWhiteSpace(heartbeat.Nombre) ? $"{heartbeat.Usuario} @ {heartbeat.Equipo}" : heartbeat.Nombre,
            EstadoActualizacion = heartbeat.EstadoActualizacion, UltimaConexionUtc = now, Activa = true },
        (_, current) => { current.Usuario = heartbeat.Usuario; current.Equipo = heartbeat.Equipo; current.Plataforma = heartbeat.Plataforma;
            current.Version = heartbeat.Version;
            string expected = heartbeat.Plataforma.Contains("mac", StringComparison.OrdinalIgnoreCase) ? latestMacControlVersion : latestWindowsControlVersion;
            if (Version.TryParse(heartbeat.Version, out var installed) && Version.TryParse(expected, out var latest) && installed >= latest) current.EstadoActualizacion = "Actualizado";
            else if (heartbeat.EstadoActualizacion is "Descargando" or "Instalando" or "Error") current.EstadoActualizacion = heartbeat.EstadoActualizacion;
            current.UltimaConexionUtc = now; current.Activa = true; return current; });
    int count = controlSessions.Values.Count(x => x.UltimaConexionUtc >= now.AddSeconds(-35));
    ControlSessionStatus currentSession = controlSessions[heartbeat.Id];
    bool isMac = heartbeat.Plataforma.Contains("mac", StringComparison.OrdinalIgnoreCase);
    string version = isMac ? latestMacControlVersion : latestWindowsControlVersion;
    string packagePath = isMac ? controlMacPackagePath : controlWindowsPackagePath;
    bool updateNow = currentSession.ActualizacionSolicitada && File.Exists(packagePath);
    if (updateNow) { currentSession.ActualizacionSolicitada = false; currentSession.EstadoActualizacion = "Descargando"; }
    await GuardarSesionesPanelAsync();
    return Results.Ok(new ControlSessionHeartbeatResponse { Activas = count, ActualizarAhora = updateNow,
        Version = version, Url = File.Exists(packagePath) ? $"{httpRequest.Scheme}://{httpRequest.Host}/api/control-update/download/{(isMac ? "macos" : "windows")}" : "" });
});

app.MapGet("/api/control-sessions", () =>
{
    DateTimeOffset limit = DateTimeOffset.UtcNow.AddSeconds(-35);
    return controlSessions.Values.Select(x => new ControlSessionStatus
    {
        Id = x.Id, Usuario = x.Usuario, Equipo = x.Equipo, Plataforma = x.Plataforma,
        Version = x.Version, Nombre = x.Nombre, EstadoActualizacion = x.EstadoActualizacion,
        UltimaConexionUtc = x.UltimaConexionUtc, Activa = x.UltimaConexionUtc >= limit,
        ActualizacionSolicitada = x.ActualizacionSolicitada,
        UltimaVersion = x.Plataforma.Contains("mac", StringComparison.OrdinalIgnoreCase) ? latestMacControlVersion : latestWindowsControlVersion,
        ActualizacionDisponible = Version.TryParse(x.Plataforma.Contains("mac", StringComparison.OrdinalIgnoreCase) ? latestMacControlVersion : latestWindowsControlVersion, out var latest)
            && Version.TryParse(x.Version, out var installed) && latest > installed
    }).Where(x => x.Activa).OrderBy(x => x.Usuario);
});

app.MapPut("/api/control-sessions/{id}/name", async (string id, RenameAgentRequest request) =>
{
    if (!controlSessions.TryGetValue(id, out ControlSessionStatus? session)) return Results.NotFound();
    string name = request.Nombre.Trim();
    if (name.Length is < 1 or > 60) return Results.BadRequest(new { error = "El nombre debe tener entre 1 y 60 caracteres." });
    session.Nombre = name; await GuardarSesionesPanelAsync(); return Results.Ok(new { updated = true, nombre = name });
});

app.MapPost("/api/control-update/package/{platform}", async (string platform, HttpRequest request) =>
{
    IFormCollection form = await request.ReadFormAsync(); IFormFile? file = form.Files.FirstOrDefault();
    if (file is null || file.Length is < 1 or > 100_000_000) return Results.BadRequest(new { error = "Paquete invalido." });
    bool mac = platform.Equals("macos", StringComparison.OrdinalIgnoreCase);
    string target = mac ? controlMacPackagePath : controlWindowsPackagePath;
    if (mac && !file.FileName.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "Selecciona el .pkg de macOS." });
    if (!mac && !file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "Selecciona el ZIP de Windows." });
    await using (FileStream output = File.Create(target)) await file.CopyToAsync(output);
    if (!mac)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(target);
            if (!archive.Entries.Any(x => x.FullName.Replace('\\', '/').Equals("app/ARES.ControlCenter.exe", StringComparison.OrdinalIgnoreCase)))
            { File.Delete(target); return Results.BadRequest(new { error = "El ZIP no contiene app/ARES.ControlCenter.exe." }); }
        }
        catch (InvalidDataException) { File.Delete(target); return Results.BadRequest(new { error = "ZIP invalido." }); }
    }
    return Results.Ok(new { platform, bytes = file.Length });
}).DisableAntiforgery();

app.MapPost("/api/control-update/request", async (ControlUpdateRequest request) =>
{
    int count = 0;
    foreach (string id in request.SessionIds.Distinct(StringComparer.OrdinalIgnoreCase))
        if (controlSessions.TryGetValue(id, out ControlSessionStatus? session))
        { session.ActualizacionSolicitada = true; session.EstadoActualizacion = "Pendiente"; count++; }
    await GuardarSesionesPanelAsync();
    await RegistrarEventoAsync("SERVER", "Centro de Control", "ACTUALIZACION_PANELES_SOLICITADA", $"Se enviaron {count} ordenes de actualizacion.");
    return Results.Ok(new { requested = count });
});

app.MapGet("/api/control-update/download/{platform}", (string platform) =>
{
    bool mac = platform.Equals("macos", StringComparison.OrdinalIgnoreCase);
    string path = mac ? controlMacPackagePath : controlWindowsPackagePath;
    return File.Exists(path) ? Results.File(path, "application/octet-stream", mac ? "ARES-Control.pkg" : "ARES-Control.zip") : Results.NotFound();
});

app.MapPost("/api/agents/heartbeat", async (AgentHeartbeat heartbeat, HttpRequest httpRequest) =>
{
    if (string.IsNullOrWhiteSpace(heartbeat.Id) || string.IsNullOrWhiteSpace(heartbeat.Equipo))
        return Results.BadRequest(new { error = "Identidad de agente incompleta." });

    bool estabaEnLinea = agents.TryGetValue(heartbeat.Id, out AgentStatus? anterior) && anterior.EstaEnLinea;
    DateTimeOffset ahora = DateTimeOffset.UtcNow;
    AgentStatus agenteActual = agents.AddOrUpdate(heartbeat.Id,
        _ => new AgentStatus
        {
            Id = heartbeat.Id, Equipo = heartbeat.Equipo, Usuario = heartbeat.Usuario,
            Sistema = heartbeat.Sistema, Version = heartbeat.Version,
            UltimaConexionUtc = ahora, EstaEnLinea = true,
            // El estado local puede provenir del horario cacheado; no debe convertirse
            // en un bloqueo manual permanente cuando el servidor pierde su almacenamiento.
            BloqueadoAdministrativamente = false,
            RequestToken = heartbeat.RequestToken
        },
        (_, existente) =>
        {
            // Se actualiza el mismo objeto para no sobrescribir una solicitud,
            // un bloqueo o un alias modificados por otra petición concurrente.
            existente.Equipo = heartbeat.Equipo;
            existente.Usuario = heartbeat.Usuario;
            existente.Sistema = heartbeat.Sistema;
            existente.Version = heartbeat.Version;
            existente.MotivoEstadoLocal = heartbeat.MotivoEstadoLocal;
            existente.HorarioVersionAplicada = heartbeat.HorarioVersionAplicada;
            existente.BloqueadoLocalmente = heartbeat.BloqueadoLocalmente;
            existente.UltimaConexionUtc = ahora;
            existente.EstaEnLinea = true;
            if (!string.IsNullOrWhiteSpace(heartbeat.RequestToken))
                existente.RequestToken = heartbeat.RequestToken;
            return existente;
        });
    if (!estabaEnLinea)
        await RegistrarEventoAsync(heartbeat.Id, heartbeat.Equipo, "AGENTE_CONECTADO", "ARES Agent inició o recuperó la conexión.");
    await GuardarAsync();
    GroupPolicy policy = groupPolicies.FirstOrDefault(p => p.Grupo == agenteActual.Grupo) ?? new();
    bool actualizarAhora = agenteActual.ActualizacionSolicitada && heartbeat.EsServicioSistema;
    if (actualizarAhora) agenteActual.ActualizacionSolicitada = false;
    return Results.Ok(new HeartbeatResponse
    {
        Accepted = true,
        ServerTimeUtc = DateTimeOffset.UtcNow,
        BloqueadoAdministrativamente = agenteActual.BloqueadoAdministrativamente
        ,HorarioVersion = schedule.Version
        ,Horarios = schedule.Horarios.Where(h => h.AgentId.Equals(heartbeat.Id, StringComparison.OrdinalIgnoreCase)).ToList()
        ,ExcepcionHastaUtc = agenteActual.ExcepcionHastaUtc
        ,ExcepcionPermitirUso = agenteActual.ExcepcionPermitirUso
        ,MargenEntradaMinutos = policy.MargenEntradaMinutos
        ,MargenSalidaMinutos = policy.MargenSalidaMinutos
        ,UltimaVersion = latestAgentVersion
        ,UrlActualizacion = File.Exists(updatePackagePath)
            ? $"{httpRequest.Scheme}://{httpRequest.Host}/api/update-package/download"
            : agentUpdateUrl
        ,ActualizarAhora = actualizarAhora
    });
});

app.MapPut("/api/agents/{id}/group", async (string id, GroupRequest request) =>
{
    if (!agents.TryGetValue(id, out AgentStatus? agente)) return Results.NotFound();
    string[] validos = ["Grupo 1", "Grupo 2", "Grupo 3"];
    if (!validos.Contains(request.Grupo)) return Results.BadRequest(new { error = "Grupo invalido." });
    agente.Grupo = request.Grupo;
    await GuardarAsync();
    return Results.Ok(new { updated = true });
});

app.MapGet("/api/schedule", () => schedule);
app.MapGet("/api/schedule/history", () => scheduleHistory.OrderByDescending(x => x.FechaUtc).Take(20));
app.MapGet("/api/group-policies", () => groupPolicies);

app.MapPut("/api/group-policies", async (GroupPoliciesRequest request) =>
{
    if (request.Grupos.Any(x => x.MargenEntradaMinutos is < 0 or > 180 || x.MargenSalidaMinutos is < 0 or > 180))
        return Results.BadRequest(new { error = "Los margenes deben estar entre 0 y 180 minutos." });
    groupPolicies = request.Grupos.Where(x => new[] { "Grupo 1", "Grupo 2", "Grupo 3" }.Contains(x.Grupo)).ToList();
    await File.WriteAllTextAsync(policiesPath, JsonSerializer.Serialize(groupPolicies, new JsonSerializerOptions { WriteIndented = true }));
    await RegistrarEventoAsync("SERVER", "Servidor ARES", "POLITICAS_GRUPO_ACTUALIZADAS", "Se actualizaron los margenes de entrada y salida.");
    return Results.Ok(groupPolicies);
});

app.MapPut("/api/schedule", async (SchedulePublication publication) =>
{
    if (publication.Mes is < 1 or > 12 || publication.Anio is < 2020 or > 2200)
        return Results.BadRequest(new { error = "Mes o anio invalido." });
    if (publication.Horarios.Any(h => h.FinUtc <= h.InicioUtc || string.IsNullOrWhiteSpace(h.AgentId)))
        return Results.BadRequest(new { error = "Hay turnos invalidos o sin equipo asignado." });
    if (schedule.Version > 0)
        scheduleHistory.Add(new ScheduleRevision { FechaUtc = DateTimeOffset.UtcNow, Accion = "Reemplazada", Estado = ClonarHorario(schedule) });
    schedule = new ScheduleState
    {
        Mes = publication.Mes, Anio = publication.Anio,
        ZonaHoraria = "America/Argentina/Buenos_Aires",
        Horarios = publication.Horarios, Version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        PublicadoUtc = DateTimeOffset.UtcNow
    };
    scheduleHistory.Add(new ScheduleRevision { FechaUtc = DateTimeOffset.UtcNow, Accion = "Publicada", Estado = ClonarHorario(schedule) });
    while (scheduleHistory.Count > 30) scheduleHistory.RemoveAt(0);
    await GuardarHorariosAsync();
    await RegistrarEventoAsync("SERVER", "Servidor ARES", "HORARIOS_PUBLICADOS",
        $"Se publicaron {schedule.Horarios.Count} turnos para {schedule.Mes:00}/{schedule.Anio}.");
    return Results.Ok(schedule);
});

app.MapPost("/api/schedule/rollback", async (RollbackScheduleRequest request) =>
{
    ScheduleRevision? revision = scheduleHistory.FirstOrDefault(x => x.Id == request.RevisionId);
    if (revision is null) return Results.NotFound(new { error = "Revision no encontrada." });
    scheduleHistory.Add(new ScheduleRevision { FechaUtc = DateTimeOffset.UtcNow, Accion = "Antes de restaurar", Estado = ClonarHorario(schedule) });
    schedule = ClonarHorario(revision.Estado); schedule.Version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); schedule.PublicadoUtc = DateTimeOffset.UtcNow;
    await GuardarHorariosAsync();
    await RegistrarEventoAsync("SERVER", "Servidor ARES", "HORARIOS_RESTAURADOS", $"Se restauro la revision {revision.Id}.");
    return Results.Ok(schedule);
});

app.MapPut("/api/agents/{id}/override", async (string id, TemporaryOverrideRequest request) =>
{
    if (!agents.TryGetValue(id, out AgentStatus? agente)) return Results.NotFound();
    if (request.HastaUtc <= DateTimeOffset.UtcNow || request.HastaUtc > DateTimeOffset.UtcNow.AddDays(31))
        return Results.BadRequest(new { error = "La excepcion debe vencer en el futuro y dentro de 31 dias." });
    agente.ExcepcionPermitirUso = request.PermitirUso; agente.ExcepcionHastaUtc = request.HastaUtc;
    await RegistrarEventoAsync(id, agente.Equipo, request.PermitirUso ? "EXCEPCION_DESBLOQUEO" : "EXCEPCION_BLOQUEO",
        $"{request.Motivo}. Vigente hasta {request.HastaUtc:u}.");
    await GuardarAsync(); return Results.Ok();
});

app.MapPost("/api/agents/{id}/update", async (string id) =>
{
    if (!agents.TryGetValue(id, out AgentStatus? agente)) return Results.NotFound();
    agente.ActualizacionSolicitada = true;
    await RegistrarEventoAsync(id, agente.Equipo, "ACTUALIZACION_SOLICITADA", $"Se solicito actualizar ARES Agent a {latestAgentVersion}.");
    await GuardarAsync(); return Results.Ok();
});

app.MapPost("/api/update-package", async (HttpRequest request) =>
{
    IFormCollection form = await request.ReadFormAsync();
    IFormFile? file = form.Files.FirstOrDefault();
    string version = form["version"].ToString().Trim();
    if (file is null || file.Length is < 1 or > 100_000_000 || !file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Selecciona el ZIP oficial de ARES Agent." });
    if (!Version.TryParse(version, out _)) return Results.BadRequest(new { error = "Version invalida." });
    await using (FileStream output = File.Create(updatePackagePath)) await file.CopyToAsync(output);
    try
    {
        using ZipArchive archive = ZipFile.OpenRead(updatePackagePath);
        if (!archive.Entries.Any(x => x.FullName.Replace('\\', '/').Equals("app/ARES.Agent.exe", StringComparison.OrdinalIgnoreCase)))
        { File.Delete(updatePackagePath); return Results.BadRequest(new { error = "El ZIP no contiene app/ARES.Agent.exe." }); }
    }
    catch (InvalidDataException) { File.Delete(updatePackagePath); return Results.BadRequest(new { error = "El archivo no es un ZIP valido." }); }
    latestAgentVersion = version; await File.WriteAllTextAsync(updateVersionPath, version);
    await RegistrarEventoAsync("SERVER", "Servidor ARES", "PAQUETE_ACTUALIZACION_CARGADO", $"Paquete ARES Agent {version} disponible para despliegue remoto.");
    return Results.Ok(new { version, bytes = file.Length });
}).DisableAntiforgery();

app.MapGet("/api/update-package/download", () => File.Exists(updatePackagePath)
    ? Results.File(updatePackagePath, "application/zip", "ARES-Agent-Update.zip")
    : Results.NotFound());

app.MapDelete("/api/agents/{id}/override", async (string id) =>
{
    if (!agents.TryGetValue(id, out AgentStatus? agente)) return Results.NotFound();
    agente.ExcepcionPermitirUso = null; agente.ExcepcionHastaUtc = null; await GuardarAsync(); return Results.Ok();
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
    if (!agente.BloqueadoAdministrativamente && !agente.BloqueadoLocalmente && CalcularMotivo(agente) is not "Fuera del horario" and not "Excepcion: bloqueo temporal")
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
            BloqueadoLocalmente = a.BloqueadoLocalmente, MotivoEstadoLocal = a.MotivoEstadoLocal,
            HorarioVersionAplicada = a.HorarioVersionAplicada,
            EstaEnLinea = a.UltimaConexionUtc >= limite,
            BloqueadoAdministrativamente = a.BloqueadoAdministrativamente,
            SolicitudDesbloqueoPendiente = a.SolicitudDesbloqueoPendiente,
            SolicitudDesbloqueoUtc = a.SolicitudDesbloqueoUtc,
            NombrePersonalizado = a.NombrePersonalizado
            ,Grupo = a.Grupo
            ,ExcepcionHastaUtc = a.ExcepcionHastaUtc
            ,ExcepcionPermitirUso = a.ExcepcionPermitirUso
            ,MotivoBloqueo = CalcularMotivo(a)
            ,ProximoCambioUtc = CalcularProximoCambio(a.Id)
            ,ActualizacionDisponible = Version.TryParse(latestAgentVersion, out var latest) && Version.TryParse(a.Version, out var current) && latest > current
            ,UltimaVersion = latestAgentVersion
            ,HorarioPendienteSincronizar = schedule.Horarios.Any(x => x.AgentId.Equals(a.Id, StringComparison.OrdinalIgnoreCase)) && a.HorarioVersionAplicada < schedule.Version
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
    if (tipo is "SOLICITUD_DESBLOQUEO" or "AGENTE_DESCONECTADO" or "ACTUALIZACION_SOLICITADA")
        await EnviarAlertaExternaAsync(equipo, tipo, detalle);
}

async Task EnviarAlertaExternaAsync(string equipo, string tipo, string detalle)
{
    string message = $"ARES - {tipo}\nEquipo: {equipo}\n{detalle}";
    try
    {
        string? token = Environment.GetEnvironmentVariable("ARES_TELEGRAM_BOT_TOKEN");
        string? chat = Environment.GetEnvironmentVariable("ARES_TELEGRAM_CHAT_ID");
        if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(chat))
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                await http.PostAsJsonAsync($"https://api.telegram.org/bot{token}/sendMessage", new { chat_id = chat, text = message });
    }
    catch { }
    try
    {
        string? host = Environment.GetEnvironmentVariable("ARES_SMTP_HOST");
        string? to = Environment.GetEnvironmentVariable("ARES_ALERT_EMAIL_TO");
        string? from = Environment.GetEnvironmentVariable("ARES_SMTP_FROM");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(from)) return;
        int.TryParse(Environment.GetEnvironmentVariable("ARES_SMTP_PORT"), out int port); if (port == 0) port = 587;
        using var smtp = new SmtpClient(host, port) { EnableSsl = true };
        string? user = Environment.GetEnvironmentVariable("ARES_SMTP_USER"); string? password = Environment.GetEnvironmentVariable("ARES_SMTP_PASSWORD");
        if (!string.IsNullOrWhiteSpace(user)) smtp.Credentials = new NetworkCredential(user, password);
        using var mail = new MailMessage(from, to, $"ARES: {tipo} - {equipo}", message); await smtp.SendMailAsync(mail);
    }
    catch { }
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

async Task GuardarHorariosAsync()
{
    var options = new JsonSerializerOptions { WriteIndented = true };
    await File.WriteAllTextAsync(schedulePath, JsonSerializer.Serialize(schedule, options));
    await File.WriteAllTextAsync(historyPath, JsonSerializer.Serialize(scheduleHistory, options));
}

async Task GuardarSesionesPanelAsync()
{
    await saveLock.WaitAsync();
    try { await File.WriteAllTextAsync(controlSessionsPath, JsonSerializer.Serialize(controlSessions.Values, new JsonSerializerOptions { WriteIndented = true })); }
    finally { saveLock.Release(); }
}

ScheduleState ClonarHorario(ScheduleState value) => JsonSerializer.Deserialize<ScheduleState>(JsonSerializer.Serialize(value)) ?? new();

string CalcularMotivo(AgentStatus agent)
{
    DateTimeOffset now = DateTimeOffset.UtcNow;
    if (agent.ExcepcionHastaUtc > now && agent.ExcepcionPermitirUso.HasValue)
        return agent.ExcepcionPermitirUso.Value ? "Excepcion: uso permitido" : "Excepcion: bloqueo temporal";
    if (agent.BloqueadoAdministrativamente) return "Bloqueo manual";
    List<ScheduleInterval> own = schedule.Horarios.Where(x => x.AgentId.Equals(agent.Id, StringComparison.OrdinalIgnoreCase)).ToList();
    if (own.Count == 0) return "Sin horario asignado";
    GroupPolicy policy = groupPolicies.FirstOrDefault(x => x.Grupo == agent.Grupo) ?? new();
    bool inside = own.Any(x => now >= x.InicioUtc.AddMinutes(-policy.MargenEntradaMinutos) && now < x.FinUtc.AddMinutes(policy.MargenSalidaMinutos));
    return inside ? "Dentro del turno" : "Fuera del horario";
}

DateTimeOffset? CalcularProximoCambio(string agentId)
{
    DateTimeOffset now = DateTimeOffset.UtcNow;
    return schedule.Horarios.Where(x => x.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase))
        .SelectMany(x => new[] { x.InicioUtc, x.FinUtc }).Where(x => x > now).OrderBy(x => x).Cast<DateTimeOffset?>().FirstOrDefault();
}
