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
var persistence = new AresPersistence(builder.Configuration);
await persistence.InitializeAsync();
await persistence.EnsureOwnerAsync(
    builder.Configuration["ARES_OWNER_USER_ID"] ?? Environment.GetEnvironmentVariable("ARES_OWNER_USER_ID"),
    builder.Configuration["ARES_OWNER_NAME"] ?? Environment.GetEnvironmentVariable("ARES_OWNER_NAME"));
await persistence.EnsurePlatformOwnerAsync(
    builder.Configuration["ARES_PLATFORM_ADMIN_USER_ID"] ?? Environment.GetEnvironmentVariable("ARES_PLATFORM_ADMIN_USER_ID") ?? builder.Configuration["ARES_OWNER_USER_ID"] ?? Environment.GetEnvironmentVariable("ARES_OWNER_USER_ID"),
    builder.Configuration["ARES_OWNER_EMAIL"] ?? Environment.GetEnvironmentVariable("ARES_OWNER_EMAIL"),
    builder.Configuration["ARES_OWNER_NAME"] ?? Environment.GetEnvironmentVariable("ARES_OWNER_NAME"));
var authService = new AresAuthService(builder.Configuration, persistence);
var mercadoPago = new MercadoPagoService(builder.Configuration);
string platformAdminUserId = builder.Configuration["ARES_PLATFORM_ADMIN_USER_ID"]
    ?? Environment.GetEnvironmentVariable("ARES_PLATFORM_ADMIN_USER_ID")
    ?? builder.Configuration["ARES_OWNER_USER_ID"]
    ?? Environment.GetEnvironmentVariable("ARES_OWNER_USER_ID")
    ?? "";
var platformStaff = new ConcurrentDictionary<Guid, PlatformStaffMember>((await persistence.GetPlatformStaffAsync()).ToDictionary(x => x.UserId));

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
string latestWindowsControlVersion = "1.6.6";
string latestMacControlVersion = "1.5.4";
Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);

var agents = new ConcurrentDictionary<string, AgentStatus>(StringComparer.OrdinalIgnoreCase);
var audit = new ConcurrentQueue<AgentAuditEvent>();
var saveLock = new SemaphoreSlim(1, 1);
var requestLimits = new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);
var authRateLimits = new ConcurrentDictionary<string, (DateTimeOffset StartedAt, int Attempts)>(StringComparer.Ordinal);
var controlSessions = new ConcurrentDictionary<string, ControlSessionStatus>(StringComparer.OrdinalIgnoreCase);
List<ControlSessionStatus> savedControlSessions = await LoadStateAsync("control-sessions", controlSessionsPath, new List<ControlSessionStatus>());
foreach (ControlSessionStatus session in savedControlSessions)
{
    if (session.OrganizationId == Guid.Empty) session.OrganizationId = AresPersistence.DefaultOrganizationId;
    controlSessions[OrganizationKey(session.OrganizationId, session.Id)] = session;
}
var schedules = new ConcurrentDictionary<Guid, ScheduleState>();
var scheduleHistories = new ConcurrentDictionary<Guid, List<ScheduleRevision>>();
var policiesByOrganization = new ConcurrentDictionary<Guid, List<GroupPolicy>>();
schedules[AresPersistence.DefaultOrganizationId] = await LoadStateAsync("schedule", schedulePath, new ScheduleState());
scheduleHistories[AresPersistence.DefaultOrganizationId] = await LoadStateAsync("schedule-history", historyPath, new List<ScheduleRevision>());
var defaultPolicies = await LoadStateAsync("group-policies", policiesPath, new List<GroupPolicy>());
if (defaultPolicies.Count == 0) defaultPolicies.Add(new GroupPolicy { Grupo = "General" });
policiesByOrganization[AresPersistence.DefaultOrganizationId] = defaultPolicies;
foreach (Guid organizationId in await persistence.GetOrganizationIdsAsync())
{
    if (organizationId == AresPersistence.DefaultOrganizationId) continue;
    schedules[organizationId] = await persistence.LoadAsync<ScheduleState>($"org:{organizationId:N}:schedule") ?? new();
    scheduleHistories[organizationId] = await persistence.LoadAsync<List<ScheduleRevision>>($"org:{organizationId:N}:schedule-history") ?? [];
    List<GroupPolicy> policies = await persistence.LoadAsync<List<GroupPolicy>>($"org:{organizationId:N}:group-policies") ?? [];
    if (policies.Count == 0) policies.Add(new GroupPolicy { Grupo = "General" });
    policiesByOrganization[organizationId] = policies;
}
string latestAgentVersion = builder.Configuration["ARES_LATEST_AGENT_VERSION"] ?? "1.7.3";
string agentUpdateUrl = builder.Configuration["ARES_AGENT_UPDATE_URL"]
    ?? "https://github.com/valenge22/ARES/releases/download/v1.7.3/ARES-Agent-Remoto-Windows-x64.zip";
string latestAgentSetupUrl = builder.Configuration["ARES_AGENT_SETUP_URL"]
    ?? "https://github.com/valenge22/ARES/releases/download/v1.7.3/ARES-Agent-Setup.exe";
string latestControlWindowsUrl = builder.Configuration["ARES_CONTROL_WINDOWS_URL"]
    ?? "https://github.com/valenge22/ARES/releases/download/control-v1.6.6/ARES-Centro-Control-Setup.exe";
string latestControlMacUrl = builder.Configuration["ARES_CONTROL_MAC_URL"]
    ?? "https://github.com/valenge22/ARES/releases/download/mac-v1.5.4/ARES-Centro-Control-macOS-arm64.pkg";
if (File.Exists(updateVersionPath)) latestAgentVersion = File.ReadAllText(updateVersionPath).Trim();
foreach (AgentStatus agent in await LoadStateAsync("agents", dataPath, new List<AgentStatus>()))
{
    if (agent.OrganizationId == Guid.Empty) agent.OrganizationId = AresPersistence.DefaultOrganizationId;
    agents[OrganizationKey(agent.OrganizationId, agent.Id)] = agent;
}
foreach (AgentAuditEvent evento in await LoadStateAsync("audit", auditPath, new List<AgentAuditEvent>()))
{
    if (evento.OrganizationId == Guid.Empty) evento.OrganizationId = AresPersistence.DefaultOrganizationId;
    audit.Enqueue(evento);
}

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Error no controlado en {context.Request.Method} {context.Request.Path}: {exception}");
        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                error = $"ARES no pudo completar la solicitud ({exception.GetType().Name}). Revisá los logs del servidor."
            });
        }
    }
});

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    if (context.Request.IsHttps || context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() == "https")
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

    string path = context.Request.Path.Value ?? "";
    if (context.Request.Method == "POST" && path is "/api/auth/login" or "/api/auth/register" or "/api/auth/recover" or "/api/auth/resend-confirmation" or "/api/auth/mfa/verify" or "/api/auth/mfa/recover")
    {
        int maximum = path == "/api/auth/login" ? 10 : path is "/api/auth/mfa/verify" or "/api/auth/mfa/recover" ? 8 : 5;
        TimeSpan window = path == "/api/auth/register" ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(15);
        string key = $"{ClientIp(context)}:{path}";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var state = authRateLimits.AddOrUpdate(key, _ => (now, 1), (_, previous) => now - previous.StartedAt >= window ? (now, 1) : (previous.StartedAt, previous.Attempts + 1));
        if (state.Attempts > maximum)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = ((int)Math.Ceiling((window - (now - state.StartedAt)).TotalSeconds)).ToString();
            await context.Response.WriteAsJsonAsync(new { error = "Demasiados intentos. Esperá unos minutos antes de volver a intentarlo." });
            return;
        }
    }
    await next();
});

app.Use(async (context, next) =>
{
    bool publicPath = context.Request.Path.Equals("/") ||
        context.Request.Path.Equals("/portal") ||
        context.Request.Path.Equals("/admin-ares") ||
        context.Request.Path.Equals("/admin-mfa.js") ||
        context.Request.Path.Equals("/admin-license.js") ||
        context.Request.Path.Equals("/admin-operations.js") ||
        context.Request.Path.Equals("/portal-billing.js") ||
        context.Request.Path.Equals("/portal-login.js") ||
        context.Request.Path.Equals("/api/downloads") ||
        context.Request.Path.StartsWithSegments("/health") ||
        context.Request.Path.StartsWithSegments("/solicitar") ||
        context.Request.Path.StartsWithSegments("/auth") ||
        context.Request.Path.Equals("/api/billing/mercadopago/webhook") ||
        context.Request.Path.Equals("/api/auth/login") ||
        context.Request.Path.Equals("/api/auth/refresh") ||
        context.Request.Path.Equals("/api/auth/logout") ||
        context.Request.Path.Equals("/api/auth/register") ||
        context.Request.Path.Equals("/api/auth/resend-confirmation") ||
        context.Request.Path.Equals("/api/agents/enroll") ||
        context.Request.Path.Equals("/api/auth/recover") ||
        context.Request.Path.Equals("/api/auth/update-password");
    publicPath = publicPath || context.Request.Path.Equals("/api/auth/mfa/verify") ||
        context.Request.Path.Equals("/api/auth/mfa/recover");
    bool validApiKey = context.Request.Headers.TryGetValue("X-ARES-Key", out var supplied) && supplied == apiKey;
    DeviceIdentity? deviceIdentity = null;
    if (context.Request.Headers.TryGetValue("X-ARES-Device", out var deviceCredential) && !string.IsNullOrWhiteSpace(deviceCredential))
    {
        byte[] deviceHash = HashSecret(deviceCredential.ToString());
        deviceIdentity = await persistence.ValidateDeviceAsync(deviceHash);
        if (deviceIdentity is not null) await persistence.CompleteDeviceRotationAsync(deviceHash);
    }
    AuthenticatedAdmin? admin = null;
    string authorization = context.Request.Headers.Authorization.ToString();
    string accessToken = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..].Trim() : "";
    if (!string.IsNullOrWhiteSpace(accessToken) && !await persistence.IsAuthTokenRevokedAsync(HashToken(accessToken), false))
    {
        admin = await authService.ValidateAsync(accessToken, context.RequestAborted);
        if (admin is not null) await persistence.TouchAuthSessionAsync(HashToken(accessToken));
    }
    if (!publicPath && !validApiKey && deviceIdentity is null && admin is null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Sesión ARES inválida o vencida." });
        return;
    }
    if (admin is not null && !CanAccess(admin.Role, context.Request.Method, context.Request.Path))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "Tu rol no tiene permiso para realizar esta acción." });
        return;
    }
    if (deviceIdentity is not null && admin is null && !validApiKey)
    {
        string ownAgentPrefix = $"/api/agents/{deviceIdentity.DeviceId}";
        bool ownAgentRoute = context.Request.Path.Equals($"{ownAgentPrefix}/closed") ||
            context.Request.Path.Equals($"{ownAgentPrefix}/unlock-request");
        bool allowedDeviceRoute = context.Request.Path.Equals("/api/agents/heartbeat") ||
            ownAgentRoute || context.Request.Path.Equals("/api/agents/enroll/cancel") ||
            context.Request.Path.Equals("/api/update-package/download");
        if (!allowedDeviceRoute)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "La credencial del equipo no permite esta operaciÃ³n." });
            return;
        }
    }
    if (admin is not null)
    {
        context.Items["AresAdmin"] = admin;
        context.Items["AresOrganizationId"] = admin.OrganizationId;
    }
    else if (validApiKey)
        context.Items["AresOrganizationId"] = AresPersistence.DefaultOrganizationId;
    else if (deviceIdentity is not null)
    {
        context.Items["AresOrganizationId"] = deviceIdentity.OrganizationId;
        context.Items["AresDeviceId"] = deviceIdentity.DeviceId;
        context.Items["AresDeviceGroup"] = deviceIdentity.AssignedGroup;
    }
    await next();
});

app.MapGet("/health", () => Results.Ok(new
{
    service = "ARES Server",
    status = "ok",
    storage = persistence.UsesDatabase ? "postgresql" : "json",
    authentication = authService.IsConfigured ? "configured" : "missing"
    ,billing = mercadoPago.IsConfigured ? "mercadopago" : "not-configured"
}));

app.MapGet("/", () => Results.File(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html"), "text/html; charset=utf-8"));
app.MapGet("/portal", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    string html = File.ReadAllText(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "portal.html"));
    html = html.Replace("onsubmit=\"login(event)\"", "onsubmit=\"submitLogin(event)\"")
        .Replace("</body>", "<script src=\"/portal-login.js\"></script></body>");
    return Results.Content(html, "text/html; charset=utf-8");
});
app.MapGet("/portal-login.js", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    return Results.File(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "portal-login.js"), "application/javascript; charset=utf-8");
});
app.MapGet("/api/downloads", () => Results.Ok(new
{
    controlWindows = new { version = latestWindowsControlVersion, url = latestControlWindowsUrl },
    controlMac = new { version = latestMacControlVersion, url = latestControlMacUrl },
    agentWindows = new { version = latestAgentVersion, url = latestAgentSetupUrl }
}));
app.MapGet("/admin-ares", () => Results.File(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "admin-ares.html"), "text/html; charset=utf-8"));
app.MapGet("/admin-mfa.js", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    return Results.File(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "admin-mfa.js"), "application/javascript; charset=utf-8");
});
app.MapGet("/admin-license.js", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    return Results.File(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "admin-license.js"), "application/javascript; charset=utf-8");
});
app.MapGet("/admin-operations.js", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.File(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "admin-operations.js"), "application/javascript; charset=utf-8");
});
app.MapGet("/portal-billing.js", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    return Results.File(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "portal-billing.js"), "application/javascript; charset=utf-8");
});

app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext context, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        return Results.BadRequest(new { error = "Ingresá correo y contraseña." });
    AuthResult? result = await authService.LoginAsync(request.Email.Trim(), request.Password, cancellationToken);
    string client = ClientName(context); string ip = ClientIp(context);
    AdminUser? knownUser = result is null ? await persistence.GetAdminByEmailAsync(request.Email.Trim()) : null;
    await persistence.RecordLoginAsync(result?.User.UserId ?? knownUser?.UserId, result?.User.OrganizationId ?? knownUser?.OrganizationId, request.Email.Trim(), client, ip, result is not null);
    if (result is null) return Results.Json(new { error = "Correo, contraseña o permisos inválidos." }, statusCode: 401);
    if (!result.MfaRequired)
        await persistence.RegisterAuthSessionAsync(result.User.UserId, result.User.OrganizationId, HashToken(result.AccessToken), HashToken(result.RefreshToken), client, ip);
    return IsWebAuthClient(context) && !result.MfaRequired ? WebAuthResult(context, result) : Results.Ok(result);
});

app.MapPost("/api/auth/refresh", async (RefreshRequest? request, HttpContext context, CancellationToken cancellationToken) =>
{
    string refreshToken = IsWebAuthClient(context) ? context.Request.Cookies["ares_refresh"] ?? "" : request?.RefreshToken ?? "";
    if (string.IsNullOrWhiteSpace(refreshToken) || await persistence.IsAuthTokenRevokedAsync(HashToken(refreshToken), true))
        return Results.Json(new { error = "Esta sesión fue cerrada remotamente." }, statusCode: 401);
    AuthResult? result = await authService.RefreshAsync(refreshToken, cancellationToken);
    if (result is null) return Results.Json(new { error = "La sesión venció. Iniciá sesión nuevamente." }, statusCode: 401);
    await persistence.RegisterAuthSessionAsync(result.User.UserId, result.User.OrganizationId, HashToken(result.AccessToken), HashToken(result.RefreshToken), ClientName(context), ClientIp(context));
    return IsWebAuthClient(context) ? WebAuthResult(context, result) : Results.Ok(result);
});

app.MapPost("/api/auth/logout", (HttpContext context) =>
{
    context.Response.Cookies.Delete("ares_refresh", new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/api/auth" });
    return Results.Ok(new { loggedOut = true });
});

app.MapGet("/api/auth/me", (HttpContext context) => Results.Ok((AuthenticatedAdmin)context.Items["AresAdmin"]!));

app.MapGet("/api/onboarding", async (HttpContext context) =>
{
    Guid organizationId = CurrentOrganization(context);
    OrganizationSetupInfo setup = await persistence.GetOrganizationSetupAsync(organizationId)
        ?? new OrganizationSetupInfo(organizationId, "Organización ARES", $"org-{organizationId.ToString("N")[..8]}", false);
    return Results.Ok(setup);
});
app.MapPost("/api/onboarding/complete", async (HttpContext context) =>
{
    if (!IsOwner(context)) return Results.Forbid();
    await persistence.CompleteOrganizationSetupAsync(CurrentOrganization(context));
    return Results.Ok(new { completed = true });
});
app.MapPut("/api/organization", async (UpdateOrganizationRequest request, HttpContext context) =>
{
    if (!IsOwner(context)) return Results.Forbid();
    string name = request.Name?.Trim() ?? "";
    if (name.Length is < 2 or > 120) return Results.BadRequest(new { error = "El nombre debe tener entre 2 y 120 caracteres." });
    await persistence.UpdateOrganizationNameAsync(CurrentOrganization(context), name);
    return Results.Ok(new { updated = true, name });
});
app.MapGet("/api/license", async (HttpContext context) =>
{
    BillingSubscription? billing = await persistence.GetBillingSubscriptionAsync(CurrentOrganization(context));
    if (billing is not null && mercadoPago.IsConfigured &&
        (billing.Status is "pending" or "authorized" or "paused" ||
         (!string.IsNullOrWhiteSpace(billing.LastPaymentStatus) && billing.LastPaymentStatus != "approved")) &&
        (!billing.PaidUntil.HasValue || billing.PaidUntil.Value <= DateTimeOffset.UtcNow.AddDays(1)))
        await ReconcileBillingAsync(billing, mercadoPago, persistence, CancellationToken.None);
    LicenseInfo? license = await persistence.GetLicenseAsync(CurrentOrganization(context));
    return license is null ? Results.NotFound() : Results.Ok(new { license, canManagePlatform = IsPlatformAdmin(context) });
});
app.MapGet("/api/billing", async (HttpContext context, CancellationToken cancellationToken) =>
{
    BillingSubscription? subscription = await persistence.GetBillingSubscriptionAsync(CurrentOrganization(context));
    if (subscription is not null && (subscription.Status == "pending" ||
        (subscription.Status is "cancelled" or "canceled" && !subscription.PaidUntil.HasValue)))
        subscription = await ReconcileBillingAsync(subscription, mercadoPago, persistence, cancellationToken);
    return Results.Json(new { configured = mercadoPago.IsConfigured, usdArsRate = mercadoPago.UsdArsRate, subscription });
});
app.MapGet("/api/billing/history", async (HttpContext context, CancellationToken cancellationToken) =>
{
    if (!IsOwner(context)) return Results.Forbid();
    Guid organizationId = CurrentOrganization(context);
    List<BillingPayment> payments = await persistence.GetBillingPaymentsAsync(organizationId);
    if (payments.Count == 0)
    {
        BillingSubscription? subscription = await persistence.GetBillingSubscriptionAsync(organizationId);
        if (subscription is not null && mercadoPago.IsConfigured)
        {
            await ReconcileBillingAsync(subscription, mercadoPago, persistence, cancellationToken);
            payments = await persistence.GetBillingPaymentsAsync(organizationId);
        }
    }
    return Results.Ok(payments);
});
app.MapPost("/api/billing/checkout", async (BillingCheckoutRequest request, HttpContext context, CancellationToken cancellationToken) =>
{
    if (!IsOwner(context)) return Results.Forbid();
    if (!mercadoPago.IsConfigured) return Results.BadRequest(new { error = "Mercado Pago todavía no está configurado." });
    string plan = NormalizePlan(request.Plan); if (plan is "" or "Trial") return Results.BadRequest(new { error = "Elegí un plan pago válido." });
    if (request.AdditionalDevices is < 0 or > 100000 || request.AdditionalPanelUsers is < 0 or > 10000)
        return Results.BadRequest(new { error = "Los adicionales no son válidos." });
    BillingSubscription? existing = await persistence.GetBillingSubscriptionAsync(CurrentOrganization(context));
    if (existing is not null && existing.Status == "authorized")
        return Results.BadRequest(new { error = "Ya existe una suscripción activa. La modificación de planes se habilitará desde Administrar suscripción." });
    if (existing is not null && existing.Status == "pending" && !string.IsNullOrWhiteSpace(existing.ProviderSubscriptionId))
        await mercadoPago.CancelSubscriptionOrPlanAsync(existing.ProviderSubscriptionId, cancellationToken);
    PlanDefinition definition = PlanDetails(plan);
    decimal usd = definition.MonthlyPriceUsd + request.AdditionalDevices * definition.AdditionalDeviceUsd + request.AdditionalPanelUsers * definition.AdditionalPanelUserUsd;
    decimal ars = decimal.Round(usd * mercadoPago.UsdArsRate, 2);
    string origin = $"{context.Request.Scheme}://{context.Request.Host}";
    MercadoPagoCreateResult creation = await mercadoPago.CreateSubscriptionPlanAsync(CurrentOrganization(context),
        $"ARES {definition.DisplayName}", ars, $"{origin}/portal", cancellationToken);
    MercadoPagoSubscription? created = creation.Subscription;
    if (created is null || string.IsNullOrWhiteSpace(created.CheckoutUrl))
        return Results.BadRequest(new { error = string.IsNullOrWhiteSpace(creation.Error) ? "Mercado Pago no pudo crear la suscripción." : creation.Error });
    await persistence.UpsertBillingSubscriptionAsync(new(CurrentOrganization(context), created.Id, plan, request.AdditionalDevices,
        request.AdditionalPanelUsers, ars, "pending", "", null));
    return Results.Ok(new { checkoutUrl = created.CheckoutUrl, amountUsd = usd, amountArs = ars });
});
app.MapPost("/api/billing/cancel", async (HttpContext context, CancellationToken cancellationToken) =>
{
    if (!IsOwner(context)) return Results.Forbid();
    BillingSubscription? subscription = await persistence.GetBillingSubscriptionAsync(CurrentOrganization(context));
    if (subscription is null || string.IsNullOrWhiteSpace(subscription.ProviderSubscriptionId)) return Results.NotFound();
    bool canceledRemotely = await mercadoPago.CancelSubscriptionOrPlanAsync(subscription.ProviderSubscriptionId, cancellationToken);
    if (!canceledRemotely && subscription.Status == "authorized")
        return Results.BadRequest(new { error = "Mercado Pago no pudo cancelar la suscripción activa." });
    await persistence.UpsertBillingSubscriptionAsync(subscription with { Status = "cancelled" });
    if (!subscription.PaidUntil.HasValue)
    {
        PlanDefinition definition = PlanDetails(subscription.RequestedPlan);
        await persistence.UpdateLicenseAsync(subscription.OrganizationId, subscription.RequestedPlan, "Canceled", definition.IncludedDevices,
            subscription.AdditionalDevices, definition.IncludedPanelUsers, subscription.AdditionalPanelUsers, 0, null, 3);
    }
    return Results.Ok(new { canceled = true, canceledRemotely, accessUntil = subscription.PaidUntil });
});

app.MapPost("/api/billing/reconcile-payment", async (BillingPaymentReconcileRequest request, HttpContext context, CancellationToken cancellationToken) =>
{
    if (!IsOwner(context)) return Results.Forbid();
    string paymentId = request.PaymentId?.Trim() ?? "";
    if (paymentId.Length is < 6 or > 30 || paymentId.Any(x => !char.IsDigit(x)))
        return Results.BadRequest(new { error = "El número de operación no es válido." });
    string stage = "consultar el pago";
    try
    {
        MercadoPagoAuthorizedPayment? payment = await mercadoPago.FindAuthorizedPaymentByPaymentIdAsync(paymentId, cancellationToken);
        if (payment is null || payment.PaymentStatus != "approved")
            return Results.BadRequest(new { error = "Mercado Pago no devolvió una factura aprobada para esa operación." });
        stage = "consultar la suscripción";
        MercadoPagoSubscription? remote = await mercadoPago.GetSubscriptionAsync(payment.SubscriptionId, cancellationToken);
        stage = "validar la organización";
        BillingSubscription? stored = await persistence.GetBillingSubscriptionAsync(CurrentOrganization(context));
        if (remote is null || stored is null ||
            (remote.Id != stored.ProviderSubscriptionId && remote.PlanId != stored.ProviderSubscriptionId &&
             (!Guid.TryParse(remote.ExternalReference, out Guid reference) || reference != stored.OrganizationId)))
            return Results.BadRequest(new { error = "La operación no pertenece a esta organización ARES." });
        stage = "guardar la acreditación";
        BillingSubscription updated = await ApplyAuthorizedPaymentAsync(stored, remote, payment, mercadoPago, persistence, cancellationToken);
        return Results.Ok(new { reconciled = true, subscription = updated });
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Error al reconciliar pago {paymentId} durante '{stage}': {exception}");
        return Results.BadRequest(new { error = $"No se pudo {stage}: {exception.GetType().Name}." });
    }
});

app.MapPost("/api/billing/mercadopago/webhook", async (HttpContext context, JsonElement payload, CancellationToken cancellationToken) =>
{
    string dataId = context.Request.Query["data.id"].FirstOrDefault() ??
        (payload.TryGetProperty("data", out JsonElement data) && data.TryGetProperty("id", out JsonElement idValue) ? idValue.ToString() : "");
    string type = context.Request.Query["type"].FirstOrDefault() ?? (payload.TryGetProperty("type", out JsonElement typeValue) ? typeValue.GetString() ?? "" : "");
    string requestId = context.Request.Headers["x-request-id"].ToString(); string signature = context.Request.Headers["x-signature"].ToString();
    if (string.IsNullOrWhiteSpace(dataId)) return Results.BadRequest();
    if (type != "subscription_authorized_payment" && !mercadoPago.ValidateWebhook(dataId, requestId, signature)) return Results.Unauthorized();
    if (type == "subscription_preapproval")
    {
        MercadoPagoSubscription? remote = await mercadoPago.GetSubscriptionAsync(dataId, cancellationToken);
        if (remote is null) return Results.Ok();
        BillingSubscription? stored = await persistence.GetBillingSubscriptionByProviderIdAsync(remote.Id);
        if (stored is null && !string.IsNullOrWhiteSpace(remote.PlanId))
            stored = await persistence.GetBillingSubscriptionByProviderIdAsync(remote.PlanId);
        if (stored is null && Guid.TryParse(remote.ExternalReference, out Guid referencedOrganization))
            stored = await persistence.GetBillingSubscriptionAsync(referencedOrganization);
        if (stored is null) return Results.Ok();
        Guid organizationId = stored.OrganizationId;
        await persistence.UpsertBillingSubscriptionAsync(stored with { ProviderSubscriptionId = remote.Id, Status = remote.Status });
        if (!string.IsNullOrWhiteSpace(remote.PlanId))
            await mercadoPago.CancelSubscriptionOrPlanAsync(remote.PlanId, cancellationToken);
        if (remote.Status is "cancelled" or "canceled" && !stored.PaidUntil.HasValue)
            await persistence.UpdateLicenseAsync(organizationId, stored.RequestedPlan, "Canceled", PlanDetails(stored.RequestedPlan).IncludedDevices,
                stored.AdditionalDevices, PlanDetails(stored.RequestedPlan).IncludedPanelUsers, stored.AdditionalPanelUsers, 0, null, 3);
    }
    else if (type == "payment")
    {
        MercadoPagoAuthorizedPayment? payment = await mercadoPago.FindAuthorizedPaymentByPaymentIdAsync(dataId, cancellationToken);
        if (payment is null) return Results.Ok();
        MercadoPagoSubscription? remote = await mercadoPago.GetSubscriptionAsync(payment.SubscriptionId, cancellationToken);
        if (remote is null) return Results.Ok();
        BillingSubscription? stored = await persistence.GetBillingSubscriptionByProviderIdAsync(remote.Id);
        if (stored is null && !string.IsNullOrWhiteSpace(remote.PlanId)) stored = await persistence.GetBillingSubscriptionByProviderIdAsync(remote.PlanId);
        if (stored is null && Guid.TryParse(remote.ExternalReference, out Guid reference)) stored = await persistence.GetBillingSubscriptionAsync(reference);
        if (stored is not null) await ApplyAuthorizedPaymentAsync(stored, remote, payment, mercadoPago, persistence, cancellationToken);
    }
    else if (type == "subscription_authorized_payment")
    {
        MercadoPagoAuthorizedPayment? payment = await mercadoPago.GetAuthorizedPaymentAsync(dataId, cancellationToken); if (payment is null) return Results.Ok();
        BillingSubscription? stored = await persistence.GetBillingSubscriptionByProviderIdAsync(payment.SubscriptionId); if (stored is null) return Results.Ok();
        MercadoPagoSubscription? remote = await mercadoPago.GetSubscriptionAsync(payment.SubscriptionId, cancellationToken); if (remote is null) return Results.Ok();
        await ApplyAuthorizedPaymentAsync(stored, remote, payment, mercadoPago, persistence, cancellationToken);
    }
    return Results.Ok();
});
app.MapGet("/api/platform/organizations", async (HttpContext context) =>
    IsPlatformAdmin(context) ? Results.Ok(await persistence.GetLicensesAsync()) : Results.Forbid());
app.MapGet("/api/platform/overview", async (HttpContext context) =>
{
    if (!IsPlatformAdmin(context)) return Results.Forbid();
    List<LicenseInfo> licenses = await persistence.GetLicensesAsync();
    DateTimeOffset onlineCutoff = DateTimeOffset.UtcNow.AddSeconds(-35);
    var alerts = licenses.Where(x => x.AccessStatus is "Expired" or "PastDue" ||
            (x.AccessEndsAt.HasValue && x.AccessEndsAt.Value <= DateTimeOffset.UtcNow.AddDays(7)))
        .Select(x => new { x.OrganizationId, OrganizationName = x.OrganizationName, Type = x.AccessStatus is "Expired" or "PastDue" ? "Licencia" : "Vencimiento próximo", x.AccessStatus, x.AccessEndsAt }).ToList();
    foreach (LicenseInfo license in licenses)
    {
        long connected = agents.Values.LongCount(a => a.OrganizationId == license.OrganizationId && a.UltimaConexionUtc >= onlineCutoff);
        if (license.UsedDevices > 0 && connected == 0)
            alerts.Add(new { license.OrganizationId, OrganizationName = license.OrganizationName, Type = "Sin equipos conectados", AccessStatus = "Atención", AccessEndsAt = (DateTimeOffset?)null });
    }
    return Results.Ok(new { alerts, audit = await persistence.GetPlatformAuditAsync() });
});
app.MapGet("/api/platform/staff", (HttpContext context) =>
    IsPlatformOwner(context) ? Results.Ok(platformStaff.Values.OrderBy(x => x.DisplayName)) : Results.Forbid());
app.MapPut("/api/platform/staff", async (PlatformStaffRequest request, HttpContext context) =>
{
    if (!IsPlatformOwner(context)) return Results.Forbid();
    string email = request.Email?.Trim() ?? "", role = request.Role?.Trim() ?? "";
    if (!email.Contains('@') || role is not ("Owner" or "Support" or "Sales")) return Results.BadRequest(new { error = "Correo o rol inválido." });
    if (!await persistence.SetPlatformStaffByEmailAsync(email, role, request.Enabled)) return Results.BadRequest(new { error = "La cuenta debe iniciar sesión en ARES antes de poder agregarla." });
    platformStaff.Clear(); foreach (PlatformStaffMember staff in await persistence.GetPlatformStaffAsync()) platformStaff[staff.UserId] = staff;
    AuthenticatedAdmin actor = CurrentAdmin(context); await persistence.AddPlatformAuditAsync(actor.UserId, actor.DisplayName, null, "EQUIPO_INTERNO_ACTUALIZADO", $"{email}: {role}, {(request.Enabled ? "habilitado" : "deshabilitado")}.");
    return Results.Ok(new { updated = true });
});
app.MapGet("/api/platform/tickets", async (HttpContext context) =>
    IsPlatformAdmin(context) ? Results.Ok(await persistence.GetSupportTicketsAsync()) : Results.Forbid());
app.MapPost("/api/platform/tickets", async (CreateSupportTicketRequest request, HttpContext context) =>
{
    if (!HasPlatformRole(context, "Owner", "Support")) return Results.Forbid();
    string subject = request.Subject?.Trim() ?? "", detail = request.Detail?.Trim() ?? "", priority = request.Priority?.Trim() ?? "Normal";
    if (subject.Length is < 3 or > 160 || detail.Length is < 3 or > 5000 || priority is not ("Low" or "Normal" or "High")) return Results.BadRequest(new { error = "Revisá los datos del ticket." });
    AuthenticatedAdmin actor = CurrentAdmin(context); await persistence.CreateSupportTicketAsync(request.OrganizationId, subject, detail, priority, actor.UserId); await persistence.AddPlatformAuditAsync(actor.UserId, actor.DisplayName, request.OrganizationId, "TICKET_CREADO", subject);
    return Results.Ok(new { created = true });
});
app.MapPut("/api/platform/tickets/{id:guid}", async (Guid id, UpdateSupportTicketRequest request, HttpContext context) =>
{
    if (!IsPlatformAdmin(context) || request.Status is not ("Open" or "InProgress" or "Resolved" or "Closed")) return Results.Forbid();
    return await persistence.UpdateSupportTicketAsync(id, request.Status) ? Results.Ok(new { updated = true }) : Results.NotFound();
});
app.MapGet("/api/platform/organizations/{id:guid}/support", async (Guid id, HttpContext context) =>
{
    if (!HasPlatformRole(context, "Owner", "Support")) return Results.Forbid();
    DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddSeconds(-35);
    var devices = agents.Values.Where(a => a.OrganizationId == id).Select(a => new { a.Id, a.Equipo, a.Usuario, a.Version, Online = a.UltimaConexionUtc >= cutoff, a.UltimaConexionUtc, a.BloqueadoAdministrativamente, a.SolicitudDesbloqueoPendiente }).OrderBy(x => x.Equipo).ToList();
    var events = audit.Where(x => x.OrganizationId == id).OrderByDescending(x => x.FechaUtc).Take(30).ToList();
    return Results.Ok(new { devices, events });
});
app.MapPost("/api/platform/organizations/{organizationId:guid}/devices/{deviceId}/revoke", async (Guid organizationId, string deviceId, HttpContext context) =>
{
    if (!HasPlatformRole(context, "Owner", "Support")) return Results.Forbid();
    bool revoked = await persistence.RevokeDeviceAsync(organizationId, deviceId);
    if (!revoked) return Results.NotFound(new { error = "No se encontró la credencial del equipo." });
    AgentStatus? live = agents.Values.FirstOrDefault(a => a.OrganizationId == organizationId && a.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
    if (live is not null) { live.EstaEnLinea = false; live.BloqueadoAdministrativamente = true; }
    AuthenticatedAdmin actor = CurrentAdmin(context);
    await persistence.AddPlatformAuditAsync(actor.UserId, actor.DisplayName, organizationId, "EQUIPO_REVOCADO", $"Se revocó la credencial del equipo {deviceId}.");
    return Results.Ok(new { revoked = true });
});
app.MapGet("/api/platform/billing/history", async (HttpContext context) =>
    IsPlatformAdmin(context) ? Results.Ok(await persistence.GetAllBillingPaymentsAsync()) : Results.Forbid());
app.MapPut("/api/platform/organizations/{id:guid}/license", async (Guid id, UpdateLicenseRequest request, HttpContext context) =>
{
    if (!HasPlatformRole(context, "Owner", "Sales")) return Results.Forbid();
    string plan = NormalizePlan(request.Plan); string status = NormalizeLicenseStatus(request.Status);
    if (string.IsNullOrEmpty(plan) || string.IsNullOrEmpty(status) || request.AdditionalDevices is < 0 or > 100000 ||
        request.AdditionalPanelUsers is < 0 or > 10000 || request.GraceDays is < 0 or > 30)
        return Results.BadRequest(new { error = "Configuración de licencia inválida." });
    PlanDefinition definition = PlanDetails(plan);
    int includedDevices = plan == "Enterprise" && request.MaxDevices > 0 ? request.MaxDevices : definition.IncludedDevices;
    int includedUsers = plan == "Enterprise" && request.MaxPanelUsers > 0 ? request.MaxPanelUsers : definition.IncludedPanelUsers;
    decimal monthlyPrice = definition.MonthlyPriceUsd + request.AdditionalDevices * definition.AdditionalDeviceUsd +
        request.AdditionalPanelUsers * definition.AdditionalPanelUserUsd;
    await persistence.UpdateLicenseAsync(id, plan, status, includedDevices, request.AdditionalDevices, includedUsers,
        request.AdditionalPanelUsers, monthlyPrice, request.ExpiresAt, request.GraceDays);
    AuthenticatedAdmin actor = CurrentAdmin(context);
    await persistence.AddPlatformAuditAsync(actor.UserId, actor.DisplayName, id, "LICENCIA_ACTUALIZADA", $"Plan {plan}; estado {status}; equipos {includedDevices + request.AdditionalDevices}; usuarios {includedUsers + request.AdditionalPanelUsers}.");
    return Results.Ok(new { updated = true });
});
app.MapDelete("/api/platform/organizations/{id:guid}", async (Guid id, HttpContext context) =>
{
    if (!IsPlatformOwner(context)) return Results.Forbid();
    if (id == AresPersistence.DefaultOrganizationId || id == CurrentOrganization(context))
        return Results.BadRequest(new { error = "No se puede eliminar la organización principal o la organización de tu sesión." });
    await persistence.ArchiveOrganizationAsync(id);
    AuthenticatedAdmin actor = CurrentAdmin(context);
    await persistence.AddPlatformAuditAsync(actor.UserId, actor.DisplayName, id, "ORGANIZACION_ARCHIVADA", "La organización fue archivada y sus accesos revocados.");
    return Results.Ok(new { archived = true });
});

app.MapPost("/api/auth/register", async (RegisterRequest request, HttpRequest httpRequest, CancellationToken cancellationToken) =>
{
    string name = request.DisplayName?.Trim() ?? ""; string email = request.Email?.Trim() ?? ""; string password = request.Password ?? "";
    string invitationCode = request.InvitationCode?.Trim() ?? ""; string organizationName = request.OrganizationName?.Trim() ?? "";
    if (name.Length is < 2 or > 80 || !email.Contains('@') || password.Length < 8)
        return Results.BadRequest(new { error = "Revisá el nombre, correo y contraseña (mínimo 8 caracteres)." });
    bool createOrganization = string.IsNullOrWhiteSpace(invitationCode);
    if (createOrganization && organizationName.Length is < 2 or > 120)
        return Results.BadRequest(new { error = "Ingresá el nombre de la empresa u organización." });
    InvitationGrant? invitation = null;
    if (!createOrganization)
    {
        invitation = await persistence.ConsumeInvitationAsync(HashInvitationCode(invitationCode));
        if (invitation is null) return Results.Json(new { error = "Código de invitación inválido." }, statusCode: 403);
    }
    string origin = $"{httpRequest.Scheme}://{httpRequest.Host}";
    SignUpResult signUp = await authService.SignUpAsync(email, password, name, $"{origin}/auth/confirmed", cancellationToken);
    if (!signUp.UserId.HasValue)
    {
        if (invitation is not null) await persistence.RestoreInvitationUseAsync(invitation.InvitationId);
        string error = signUp.ErrorCode switch
        {
            "user_already_exists" or "email_exists" => "Ese correo todavía figura registrado en Supabase.",
            "over_email_send_rate_limit" => "Supabase alcanzó temporalmente el límite de correos. Esperá unos minutos e intentá nuevamente.",
            "signup_disabled" => "El registro de usuarios está deshabilitado en Supabase.",
            "weak_password" => "Supabase rechazó la contraseña por considerarla demasiado débil.",
            _ => $"Supabase rechazó el registro: {signUp.ErrorMessage}"
        };
        return Results.BadRequest(new { error, code = signUp.ErrorCode });
    }
    if (createOrganization)
    {
        await persistence.CreateOrganizationOwnerAsync(signUp.UserId.Value, email, name, organizationName);
        return Results.Ok(new { created = true, pendingApproval = false, message = "Organización creada. Confirmá tu correo y luego iniciá sesión como propietario." });
    }
    await persistence.RegisterPendingAsync(signUp.UserId.Value, invitation!.OrganizationId, invitation.InvitedRole, email, name);
    return Results.Ok(new { created = true, pendingApproval = true, message = "Revisá tu correo y esperá la aprobación del propietario." });
});

app.MapPost("/api/auth/recover", async (RecoverRequest request, HttpRequest httpRequest, CancellationToken cancellationToken) =>
{
    string configuredOrigin = builder.Configuration["ARES_PUBLIC_URL"] ?? Environment.GetEnvironmentVariable("ARES_PUBLIC_URL") ?? "";
    string forwardedScheme = httpRequest.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? httpRequest.Scheme;
    string origin = string.IsNullOrWhiteSpace(configuredOrigin)
        ? $"{forwardedScheme}://{httpRequest.Host}"
        : configuredOrigin.TrimEnd('/');
    await authService.RecoverAsync(request.Email.Trim(), $"{origin}/auth/reset", cancellationToken);
    return Results.Ok(new { sent = true });
});

app.MapPost("/api/auth/resend-confirmation", async (RecoverRequest request, HttpRequest httpRequest, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@')) return Results.BadRequest(new { error = "Ingresá un correo válido." });
    string configuredOrigin = builder.Configuration["ARES_PUBLIC_URL"] ?? Environment.GetEnvironmentVariable("ARES_PUBLIC_URL") ?? "";
    string forwardedScheme = httpRequest.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? httpRequest.Scheme;
    string origin = string.IsNullOrWhiteSpace(configuredOrigin) ? $"{forwardedScheme}://{httpRequest.Host}" : configuredOrigin.TrimEnd('/');
    bool sent = await authService.ResendConfirmationAsync(request.Email.Trim(), $"{origin}/auth/confirmed", cancellationToken);
    return sent ? Results.Ok(new { sent = true }) : Results.BadRequest(new { error = "Supabase no pudo reenviar el correo. Esperá unos minutos e intentá nuevamente." });
});

app.MapPost("/api/auth/update-password", async (UpdatePasswordRequest request, CancellationToken cancellationToken) =>
{
    if (request.Password.Length < 8) return Results.BadRequest(new { error = "La contraseña debe tener al menos 8 caracteres." });
    return await authService.UpdatePasswordAsync(request.AccessToken, request.Password, cancellationToken) ? Results.Ok(new { updated = true }) : Results.BadRequest(new { error = "El enlace venció o no es válido." });
});
app.MapPut("/api/account/password", async (ChangePasswordRequest request, HttpContext context, CancellationToken cancellationToken) =>
{
    if (request.Password.Length < 8) return Results.BadRequest(new { error = "La contraseña debe tener al menos 8 caracteres." });
    string token = context.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
    return await authService.UpdatePasswordAsync(token, request.Password, cancellationToken)
        ? Results.Ok(new { updated = true }) : Results.BadRequest(new { error = "No se pudo actualizar la contraseña." });
});
app.MapGet("/api/account/sessions", async (HttpContext context) => Results.Ok(await persistence.GetAuthSessionsAsync(CurrentAdmin(context).UserId)));
app.MapDelete("/api/account/sessions/{id:guid}", async (Guid id, HttpContext context) =>
{
    await persistence.RevokeAuthSessionAsync(CurrentAdmin(context).UserId, id); return Results.Ok(new { revoked = true });
});
app.MapGet("/api/account/login-events", async (HttpContext context) => Results.Json(await persistence.GetLoginEventsAsync(CurrentAdmin(context).UserId)));
app.MapPost("/api/auth/mfa/verify", async (MfaVerifyRequest request, HttpContext context, CancellationToken cancellationToken) =>
{
    AuthResult? result = await authService.VerifyMfaAsync(request.AccessToken, request.FactorId, request.Code, cancellationToken);
    if (result is null) return Results.BadRequest(new { error = "El código de verificación no es válido." });
    await persistence.RegisterAuthSessionAsync(result.User.UserId, result.User.OrganizationId, HashToken(result.AccessToken), HashToken(result.RefreshToken), ClientName(context), ClientIp(context));
    return IsWebAuthClient(context) ? WebAuthResult(context, result) : Results.Ok(result);
});
app.MapPost("/api/auth/mfa/recover", async (MfaRecoveryRequest request, HttpContext context, CancellationToken cancellationToken) =>
{
    if (!authService.RecoveryConfigured)
        return Results.BadRequest(new { error = "La recuperación 2FA todavía no está configurada en el servidor." });
    var identity = await authService.IdentifyForRecoveryAsync(request.AccessToken, cancellationToken);
    if (identity is null) return Results.Json(new { error = "La sesión de recuperación no es válida." }, statusCode: 401);
    byte[] codeHash = HashRecoveryCode(identity.Value.UserId, request.RecoveryCode);
    if (!await persistence.IsMfaRecoveryCodeValidAsync(identity.Value.UserId, codeHash))
        return Results.BadRequest(new { error = "El código de recuperación es inválido o ya fue utilizado." });
    if (!await authService.RemoveMfaFactorAsAdminAsync(identity.Value.UserId, request.FactorId, cancellationToken))
        return Results.BadRequest(new { error = "Supabase no permitió retirar el segundo factor." });
    if (!await persistence.ConsumeMfaRecoveryCodeAsync(identity.Value.UserId, codeHash))
        return Results.BadRequest(new { error = "El código de recuperación ya fue utilizado." });
    var result = new AuthResult(request.AccessToken, request.RefreshToken, 0, identity.Value.Admin, false, "");
    await persistence.RegisterAuthSessionAsync(result.User.UserId, result.User.OrganizationId, HashToken(result.AccessToken), HashToken(result.RefreshToken), ClientName(context), ClientIp(context));
    return IsWebAuthClient(context) ? WebAuthResult(context, result) : Results.Ok(result);
});

app.MapGet("/api/account/mfa", async (HttpContext context, CancellationToken cancellationToken) =>
{
    string token = BearerToken(context); JsonElement? result = await authService.ListMfaAsync(token, cancellationToken);
    return result.HasValue ? Results.Ok(ExtractMfaFactors(result.Value)) : Results.BadRequest(new { error = "No se pudo consultar el segundo factor." });
});
app.MapPost("/api/account/mfa/recovery-codes", async (HttpContext context) =>
{
    Guid userId = CurrentAdmin(context).UserId;
    List<string> recoveryCodes = GenerateRecoveryCodes();
    await persistence.ReplaceMfaRecoveryCodesAsync(userId, recoveryCodes.Select(x => HashRecoveryCode(userId, x)).ToList());
    return Results.Ok(new { recoveryCodes });
});
app.MapPost("/api/account/mfa/enroll", async (HttpContext context, CancellationToken cancellationToken) =>
{
    string token = BearerToken(context);
    JsonElement? current = await authService.ListMfaAsync(token, cancellationToken);
    if (current.HasValue)
    {
        foreach (JsonElement factor in ExtractMfaFactors(current.Value))
        {
            string status = factor.TryGetProperty("status", out JsonElement statusValue) ? statusValue.GetString() ?? "" : "";
            string type = factor.TryGetProperty("factor_type", out JsonElement typeValue) ? typeValue.GetString() ?? "" : "";
            string factorId = factor.TryGetProperty("id", out JsonElement factorIdValue) ? factorIdValue.GetString() ?? "" : "";
            if (type == "totp" && status != "verified" && !string.IsNullOrWhiteSpace(factorId))
                await authService.UnenrollMfaAsync(token, factorId, cancellationToken);
        }
    }
    JsonElement? result = await authService.EnrollMfaAsync(token, cancellationToken);
    if (!result.HasValue) return Results.BadRequest(new { error = "No se pudo iniciar la configuración 2FA." });
    JsonElement value = result.Value;
    string id = value.TryGetProperty("id", out JsonElement idValue) ? idValue.GetString() ?? "" : "";
    JsonElement totp = value.GetProperty("totp");
    string qr = totp.TryGetProperty("qr_code", out JsonElement qrValue) ? qrValue.GetString() ?? "" : "";
    string image = qr;
    string svgMarkup = qr;
    if (qr.StartsWith("data:image/svg+xml", StringComparison.OrdinalIgnoreCase) && qr.IndexOf(',') is int comma && comma >= 0)
        svgMarkup = Uri.UnescapeDataString(qr[(comma + 1)..]);
    int svgStart = svgMarkup.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
    if (svgStart >= 0)
    {
        string svg = svgMarkup[svgStart..];
        svgMarkup = svg;
        image = $"data:image/svg+xml;charset=utf-8,{Uri.EscapeDataString(svg)}";
    }
    else if (qr.StartsWith("data:image/svg+xml;utf-8,", StringComparison.OrdinalIgnoreCase))
    {
        image = "data:image/svg+xml;charset=utf-8," + qr["data:image/svg+xml;utf-8,".Length..];
    }
    return Results.Ok(new { id, totp = new {
        qr_code = image,
        svg = svgStart >= 0 ? svgMarkup : "",
        secret = totp.TryGetProperty("secret", out JsonElement secret) ? secret.GetString() ?? "" : "",
        uri = totp.TryGetProperty("uri", out JsonElement uri) ? uri.GetString() ?? "" : ""
    }});
});
app.MapPost("/api/account/mfa/verify", async (MfaVerifyRequest request, HttpContext context, CancellationToken cancellationToken) =>
{
    AuthResult? result = await authService.VerifyMfaAsync(BearerToken(context), request.FactorId, request.Code, cancellationToken);
    if (result is null) return Results.BadRequest(new { error = "El código de verificación no es válido." });
    await persistence.RegisterAuthSessionAsync(result.User.UserId, result.User.OrganizationId, HashToken(result.AccessToken), HashToken(result.RefreshToken), ClientName(context), ClientIp(context));
    List<string> recoveryCodes = GenerateRecoveryCodes();
    await persistence.ReplaceMfaRecoveryCodesAsync(result.User.UserId, recoveryCodes.Select(x => HashRecoveryCode(result.User.UserId, x)).ToList());
    if (IsWebAuthClient(context))
    {
        context.Response.Cookies.Append("ares_refresh", result.RefreshToken, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/api/auth", MaxAge = TimeSpan.FromDays(30), IsEssential = true });
        return Results.Ok(new { result.AccessToken, result.ExpiresIn, result.User, result.MfaRequired, result.FactorId, recoveryCodes });
    }
    return Results.Ok(new { result.AccessToken, result.RefreshToken, result.ExpiresIn, result.User, result.MfaRequired, result.FactorId, recoveryCodes });
});
app.MapDelete("/api/account/mfa/{factorId}", async (string factorId, HttpContext context, CancellationToken cancellationToken) =>
    await authService.UnenrollMfaAsync(BearerToken(context), factorId, cancellationToken) ? Results.Ok(new { removed = true }) : Results.BadRequest(new { error = "No se pudo desactivar 2FA." }));

app.MapGet("/auth/confirmed", () => Results.Content("""
    <!doctype html><html lang="es"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>ARES</title>
    <style>body{font:16px Segoe UI,Arial;background:#0f172a;color:white;display:grid;place-items:center;min-height:100vh}main{background:#172554;padding:36px;border-radius:18px;text-align:center}h1{color:#38bdf8}</style>
    <main><h1>ARES</h1><h2>Correo confirmado</h2><p>Ya podés volver al Centro de Control e iniciar sesión. Si ingresaste con una invitación, tu acceso puede requerir aprobación.</p></main></html>
    """, "text/html; charset=utf-8"));

app.MapGet("/auth/reset", () => Results.Content("""
    <!doctype html><html lang="es"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>ARES · Nueva contraseña</title>
    <style>body{font:16px Segoe UI,Arial;background:#0f172a;color:white;display:grid;place-items:center;min-height:100vh}main{width:min(380px,85vw);background:#172554;padding:32px;border-radius:18px}h1{color:#38bdf8}input,button{box-sizing:border-box;width:100%;padding:12px;margin:8px 0;border-radius:8px;border:0}button{background:#2563eb;color:white;font-weight:bold}</style>
    <main><h1>ARES</h1><h2>Nueva contraseña</h2><input id="p" type="password" minlength="8" placeholder="Mínimo 8 caracteres"><button onclick="save()">Guardar contraseña</button><p id="m"></p></main>
    <script>async function save(){const h=new URLSearchParams(location.hash.substring(1));const token=h.get('access_token');const p=document.getElementById('p').value;const r=await fetch('/api/auth/update-password',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({accessToken:token,password:p})});document.getElementById('m').textContent=r.ok?'Contraseña actualizada. Ya podés volver a ARES.':'El enlace venció o la contraseña no es válida.'}</script></html>
    """, "text/html; charset=utf-8"));

app.MapGet("/api/admin/registrations", async (HttpContext context) =>
    IsOwner(context) ? Results.Ok(await persistence.GetRegistrationsAsync(CurrentAdmin(context).OrganizationId)) : Results.Forbid());
app.MapGet("/api/admin/users", async (HttpContext context) =>
    IsOwner(context) ? Results.Ok(await persistence.GetAdminsAsync(CurrentAdmin(context).OrganizationId)) : Results.Forbid());
app.MapPost("/api/admin/registrations/{id:guid}/approve", async (Guid id, ApproveRegistrationRequest request, HttpContext context) =>
{
    if (!IsOwner(context)) return Results.Forbid();
    if (!ValidRole(request.Role) || request.Role == "Owner") return Results.BadRequest(new { error = "Rol inválido." });
    AuthenticatedAdmin admin = CurrentAdmin(context);
    LicenseInfo? license = await persistence.GetLicenseAsync(admin.OrganizationId);
    if (license is null || !license.AllowsNewResources || license.UsedPanelUsers >= license.TotalPanelUsers)
        return Results.Json(new { error = "No hay cupos disponibles para nuevos usuarios del panel." }, statusCode: 402);
    return await persistence.ApproveAsync(id, admin.OrganizationId, request.Role, admin.UserId) ? Results.Ok(new { approved = true }) : Results.NotFound();
});
app.MapPost("/api/admin/registrations/{id:guid}/reject", async (Guid id, HttpContext context) =>
{
    if (!IsOwner(context)) return Results.Forbid(); AuthenticatedAdmin admin = CurrentAdmin(context); await persistence.ReviewRegistrationAsync(id, admin.OrganizationId, "Rejected", admin.UserId); return Results.Ok(new { rejected = true });
});
app.MapPut("/api/admin/users/{id:guid}", async (Guid id, UpdateAdminRequest request, HttpContext context) =>
{
    if (!IsOwner(context)) return Results.Forbid();
    if (!ValidRole(request.Role) || request.Role == "Owner") return Results.BadRequest(new { error = "Rol inválido." });
    Guid organizationId = CurrentAdmin(context).OrganizationId;
    AdminUser? target = (await persistence.GetAdminsAsync(organizationId)).FirstOrDefault(x => x.UserId == id);
    if (request.Enabled && target is not null && !target.Enabled)
    {
        LicenseInfo? license = await persistence.GetLicenseAsync(organizationId);
        if (license is null || !license.AllowsNewResources || license.UsedPanelUsers >= license.TotalPanelUsers)
            return Results.Json(new { error = "No hay cupos disponibles para reactivar este usuario." }, statusCode: 402);
    }
    return await persistence.UpdateAdminAsync(id, organizationId, request.Role, request.Enabled) ? Results.Ok(new { updated = true }) : Results.NotFound();
});
app.MapDelete("/api/admin/users/{id:guid}", async (Guid id, HttpContext context) =>
{
    if (!IsOwner(context)) return Results.Forbid();
    return await persistence.RemoveAdminAsync(id, CurrentAdmin(context).OrganizationId) ? Results.Ok(new { removed = true }) : Results.NotFound();
});
app.MapGet("/api/admin/invitations", async (HttpContext context) =>
    IsOwner(context) ? Results.Ok(await persistence.GetInvitationsAsync(CurrentAdmin(context).OrganizationId)) : Results.Forbid());
app.MapPost("/api/admin/invitations", async (CreateInvitationRequest request, HttpContext context) =>
{
    if (!IsOwner(context)) return Results.Forbid();
    if (request.MaxUses is < 1 or > 1000 || request.DurationHours is < 1 or > 720)
        return Results.BadRequest(new { error = "Los usos deben ser 1-1000 y la duración 1-720 horas." });
    if (!ValidRole(request.Role) || request.Role == "Owner") return Results.BadRequest(new { error = "Rol inválido." });
    LicenseInfo? license = await persistence.GetLicenseAsync(CurrentOrganization(context));
    long availableSeats = license?.AllowsNewResources == true ? license.TotalPanelUsers - license.UsedPanelUsers : 0;
    if (availableSeats < 1 || request.MaxUses > availableSeats)
        return Results.Json(new { error = $"La licencia tiene {Math.Max(0, availableSeats)} cupos disponibles para usuarios del panel." }, statusCode: 402);
    string raw = RandomNumberGenerator.GetHexString(12);
    string code = $"ARES-{raw[..4]}-{raw[4..8]}-{raw[8..]}";
    AuthenticatedAdmin admin = CurrentAdmin(context);
    InvitationInfo info = await persistence.CreateInvitationAsync(admin.OrganizationId, request.Role, HashInvitationCode(code), code[..9], request.MaxUses,
        DateTimeOffset.UtcNow.AddHours(request.DurationHours), admin.UserId);
    return Results.Ok(new { code, invitation = info });
});
app.MapDelete("/api/admin/invitations/{id:guid}", async (Guid id, HttpContext context) =>
{
    if (!IsOwner(context)) return Results.Forbid(); await persistence.RevokeInvitationAsync(id, CurrentAdmin(context).OrganizationId); return Results.Ok(new { revoked = true });
});

app.MapGet("/api/admin/device-enrollments", async (HttpContext context) =>
    IsAdministrator(context) ? Results.Ok(await persistence.GetDeviceEnrollmentsAsync(CurrentOrganization(context))) : Results.Forbid());
app.MapPost("/api/admin/device-enrollments", async (CreateDeviceEnrollmentRequest request, HttpContext context) =>
{
    if (!IsAdministrator(context)) return Results.Forbid();
    LicenseInfo? license = await persistence.GetLicenseAsync(CurrentOrganization(context));
    if (license is null || !license.AllowsNewResources)
        return Results.Json(new { error = "La licencia está vencida o suspendida." }, statusCode: 402);
    if (license.UsedDevices >= license.TotalDevices)
        return Results.Json(new { error = $"Se alcanzó el límite de {license.TotalDevices} equipos de la licencia." }, statusCode: 402);
    string requestedGroup = request.Group?.Trim() ?? "";
    if (request.MaxUses is < 1 or > 1000 || request.DurationHours is < 1 or > 720 ||
        !GetPolicies(CurrentOrganization(context)).Any(x => x.Grupo.Equals(requestedGroup, StringComparison.OrdinalIgnoreCase)))
        return Results.BadRequest(new { error = "Parámetros de vinculación o grupo inválidos." });
    string raw = RandomNumberGenerator.GetHexString(12);
    string code = $"ARES-PC-{raw[..4]}-{raw[4..8]}-{raw[8..]}";
    AuthenticatedAdmin admin = CurrentAdmin(context);
    DeviceEnrollmentInfo info = await persistence.CreateDeviceEnrollmentAsync(admin.OrganizationId, HashSecret(code), code[..12], requestedGroup,
        request.MaxUses, DateTimeOffset.UtcNow.AddHours(request.DurationHours), admin.UserId);
    return Results.Ok(new { code, enrollment = info });
});
app.MapDelete("/api/admin/device-enrollments/{id:guid}", async (Guid id, HttpContext context) =>
{
    if (!IsAdministrator(context)) return Results.Forbid();
    await persistence.RevokeDeviceEnrollmentAsync(id, CurrentOrganization(context)); return Results.Ok(new { revoked = true });
});
app.MapPost("/api/admin/devices/{id}/rotate", async (string id, HttpContext context) =>
{
    if (!IsAdministrator(context)) return Results.Forbid();
    bool requested = await persistence.RequestDeviceRotationAsync(CurrentOrganization(context), id);
    if (!requested) return Results.NotFound(new { error = "El equipo no está vinculado o ya fue revocado." });
    await RegistrarEventoAsync(id, id, "CREDENCIAL_RENOVACION_SOLICITADA", "La renovación se aplicará cuando el servicio del equipo se conecte.", CurrentOrganization(context));
    return Results.Ok(new { requested = true });
});
app.MapDelete("/api/admin/devices/{id}", async (string id, HttpContext context) =>
{
    if (!IsAdministrator(context)) return Results.Forbid();
    bool revoked = await persistence.RevokeDeviceAsync(CurrentOrganization(context), id);
    if (!revoked) return Results.NotFound(new { error = "El equipo no está vinculado o ya fue revocado." });
    if (FindAgent(context, id) is AgentStatus agent) agent.EstaEnLinea = false;
    await RegistrarEventoAsync(id, id, "CREDENCIAL_REVOCADA", "El servidor dejó de aceptar la credencial del equipo.", CurrentOrganization(context));
    await GuardarAsync();
    return Results.Ok(new { revoked = true });
});

app.MapPost("/api/agents/enroll", async (EnrollDeviceRequest request) =>
{
    string deviceId = request.DeviceId?.Trim() ?? ""; string machine = request.MachineName?.Trim() ?? "";
    if (deviceId.Length is < 8 or > 64 || machine.Length is < 1 or > 100) return Results.BadRequest(new { error = "Identidad del equipo inválida." });
    string credential = RandomNumberGenerator.GetHexString(32);
    DeviceEnrollmentGrant? grant = await persistence.EnrollDeviceAsync(HashSecret(request.Code), deviceId, machine, HashSecret(credential));
    if (grant is null) return Results.Json(new { error = "Código de vinculación inválido, vencido o sin usos disponibles." }, statusCode: 403);
    return Results.Ok(new { credential, organizationId = grant.OrganizationId, group = grant.AssignedGroup });
});

app.MapPost("/api/agents/enroll/cancel", async (HttpContext context) =>
{
    if (context.Items["AresDeviceId"] is not string deviceId ||
        !context.Request.Headers.TryGetValue("X-ARES-Device", out var credential)) return Results.Unauthorized();
    await persistence.CancelDeviceEnrollmentAsync(CurrentOrganization(context), deviceId, HashSecret(credential.ToString()));
    return Results.Ok(new { cancelled = true });
});

app.MapPost("/api/control-sessions/heartbeat", async (ControlSessionHeartbeat heartbeat, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(heartbeat.Id)) return Results.BadRequest();
    Guid organizationId = CurrentOrganization(context);
    string sessionKey = OrganizationKey(organizationId, heartbeat.Id);
    DateTimeOffset now = DateTimeOffset.UtcNow;
    controlSessions.AddOrUpdate(sessionKey,
        _ => new ControlSessionStatus { Id = heartbeat.Id, OrganizationId = organizationId, Usuario = heartbeat.Usuario, Equipo = heartbeat.Equipo,
            Plataforma = heartbeat.Plataforma, Version = heartbeat.Version, Nombre = string.IsNullOrWhiteSpace(heartbeat.Nombre) ? $"{heartbeat.Usuario} @ {heartbeat.Equipo}" : heartbeat.Nombre,
            EstadoActualizacion = heartbeat.EstadoActualizacion, UltimaConexionUtc = now, Activa = true },
        (_, current) => { current.Usuario = heartbeat.Usuario; current.Equipo = heartbeat.Equipo; current.Plataforma = heartbeat.Plataforma;
            current.Version = heartbeat.Version;
            string expected = heartbeat.Plataforma.Contains("mac", StringComparison.OrdinalIgnoreCase) ? latestMacControlVersion : latestWindowsControlVersion;
            if (Version.TryParse(heartbeat.Version, out var installed) && Version.TryParse(expected, out var latest) && installed >= latest) current.EstadoActualizacion = "Actualizado";
            else if (heartbeat.EstadoActualizacion is "Descargando" or "Instalando" or "Error") current.EstadoActualizacion = heartbeat.EstadoActualizacion;
            current.UltimaConexionUtc = now; current.Activa = true; return current; });
    int count = controlSessions.Values.Count(x => x.OrganizationId == organizationId && x.UltimaConexionUtc >= now.AddSeconds(-35));
    ControlSessionStatus currentSession = controlSessions[sessionKey];
    bool isMac = heartbeat.Plataforma.Contains("mac", StringComparison.OrdinalIgnoreCase);
    string version = isMac ? latestMacControlVersion : latestWindowsControlVersion;
    string packagePath = isMac ? controlMacPackagePath : controlWindowsPackagePath;
    bool updateNow = currentSession.ActualizacionSolicitada && File.Exists(packagePath);
    if (updateNow) { currentSession.ActualizacionSolicitada = false; currentSession.EstadoActualizacion = "Descargando"; }
    await GuardarSesionesPanelAsync();
    return Results.Ok(new ControlSessionHeartbeatResponse { Activas = count, ActualizarAhora = updateNow,
        Version = version, Url = File.Exists(packagePath) ? $"{context.Request.Scheme}://{context.Request.Host}/api/control-update/download/{(isMac ? "macos" : "windows")}" : "" });
});

app.MapGet("/api/control-sessions", (HttpContext context) =>
{
    Guid organizationId = CurrentOrganization(context);
    DateTimeOffset limit = DateTimeOffset.UtcNow.AddSeconds(-35);
    return controlSessions.Values.Where(x => x.OrganizationId == organizationId).Select(x => new ControlSessionStatus
    {
        Id = x.Id, OrganizationId = x.OrganizationId, Usuario = x.Usuario, Equipo = x.Equipo, Plataforma = x.Plataforma,
        Version = x.Version, Nombre = x.Nombre, EstadoActualizacion = x.EstadoActualizacion,
        UltimaConexionUtc = x.UltimaConexionUtc, Activa = x.UltimaConexionUtc >= limit,
        ActualizacionSolicitada = x.ActualizacionSolicitada,
        UltimaVersion = x.Plataforma.Contains("mac", StringComparison.OrdinalIgnoreCase) ? latestMacControlVersion : latestWindowsControlVersion,
        ActualizacionDisponible = Version.TryParse(x.Plataforma.Contains("mac", StringComparison.OrdinalIgnoreCase) ? latestMacControlVersion : latestWindowsControlVersion, out var latest)
            && Version.TryParse(x.Version, out var installed) && latest > installed
    }).Where(x => x.Activa).OrderBy(x => x.Usuario);
});

app.MapPut("/api/control-sessions/{id}/name", async (string id, RenameAgentRequest request, HttpContext context) =>
{
    if (!controlSessions.TryGetValue(OrganizationKey(CurrentOrganization(context), id), out ControlSessionStatus? session)) return Results.NotFound();
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

app.MapPost("/api/control-update/request", async (ControlUpdateRequest request, HttpContext context) =>
{
    Guid organizationId = CurrentOrganization(context);
    int count = 0;
    foreach (string id in request.SessionIds.Distinct(StringComparer.OrdinalIgnoreCase))
        if (controlSessions.TryGetValue(OrganizationKey(organizationId, id), out ControlSessionStatus? session))
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

app.MapPost("/api/agents/heartbeat", async (AgentHeartbeat heartbeat, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(heartbeat.Id) || string.IsNullOrWhiteSpace(heartbeat.Equipo))
        return Results.BadRequest(new { error = "Identidad de agente incompleta." });
    if (context.Items["AresDeviceId"] is string authorizedDevice && !authorizedDevice.Equals(heartbeat.Id, StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "La credencial no pertenece a este equipo." }, statusCode: 403);

    Guid organizationId = CurrentOrganization(context);
    string agentKey = OrganizationKey(organizationId, heartbeat.Id);
    bool estabaEnLinea = agents.TryGetValue(agentKey, out AgentStatus? anterior) && anterior.EstaEnLinea;
    DateTimeOffset ahora = DateTimeOffset.UtcNow;
    AgentStatus agenteActual = agents.AddOrUpdate(agentKey,
        _ => new AgentStatus
        {
            Id = heartbeat.Id, OrganizationId = organizationId, Equipo = heartbeat.Equipo, Usuario = heartbeat.Usuario,
            Sistema = heartbeat.Sistema, Version = heartbeat.Version,
            UltimaConexionUtc = ahora, EstaEnLinea = true,
            // El estado local puede provenir del horario cacheado; no debe convertirse
            // en un bloqueo manual permanente cuando el servidor pierde su almacenamiento.
            BloqueadoAdministrativamente = false,
            RequestToken = heartbeat.RequestToken
            , Grupo = context.Items["AresDeviceGroup"] as string ?? GetPolicies(organizationId).First().Grupo
            , CredencialIndividual = context.Items["AresDeviceId"] is string
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
            existente.CredencialIndividual = context.Items["AresDeviceId"] is string;
            if (!string.IsNullOrWhiteSpace(heartbeat.RequestToken))
                existente.RequestToken = heartbeat.RequestToken;
            return existente;
        });
    if (!estabaEnLinea)
        await RegistrarEventoAsync(heartbeat.Id, heartbeat.Equipo, "AGENTE_CONECTADO", "ARES Agent inició o recuperó la conexión.", organizationId);
    await GuardarAsync();
    ScheduleState organizationSchedule = GetSchedule(organizationId);
    GroupPolicy policy = GetPolicies(organizationId).FirstOrDefault(p => p.Grupo == agenteActual.Grupo) ?? new();
    bool actualizarAhora = agenteActual.ActualizacionSolicitada && heartbeat.EsServicioSistema;
    if (actualizarAhora) agenteActual.ActualizacionSolicitada = false;
    string nuevaCredencial = "";
    if (heartbeat.EsServicioSistema && context.Request.Headers.TryGetValue("X-ARES-Device", out var credencialActual))
    {
        string candidata = RandomNumberGenerator.GetHexString(32);
        if (await persistence.RotateDeviceCredentialAsync(organizationId, heartbeat.Id,
            HashSecret(credencialActual.ToString()), HashSecret(candidata)))
        {
            nuevaCredencial = candidata;
            await RegistrarEventoAsync(heartbeat.Id, heartbeat.Equipo, "CREDENCIAL_RENOVADA", "La credencial única del equipo fue renovada.", organizationId);
        }
    }
    return Results.Ok(new HeartbeatResponse
    {
        Accepted = true,
        ServerTimeUtc = DateTimeOffset.UtcNow,
        BloqueadoAdministrativamente = agenteActual.BloqueadoAdministrativamente
        ,HorarioVersion = organizationSchedule.Version
        ,Horarios = organizationSchedule.Horarios.Where(h => h.AgentId.Equals(heartbeat.Id, StringComparison.OrdinalIgnoreCase)).ToList()
        ,ExcepcionHastaUtc = agenteActual.ExcepcionHastaUtc
        ,ExcepcionPermitirUso = agenteActual.ExcepcionPermitirUso
        ,MargenEntradaMinutos = policy.MargenEntradaMinutos
        ,MargenSalidaMinutos = policy.MargenSalidaMinutos
        ,UltimaVersion = latestAgentVersion
        ,UrlActualizacion = File.Exists(updatePackagePath)
            ? $"{context.Request.Scheme}://{context.Request.Host}/api/update-package/download"
            : agentUpdateUrl
        ,ActualizarAhora = actualizarAhora
        ,NuevaCredencialDispositivo = nuevaCredencial
    });
});

app.MapPut("/api/agents/{id}/group", async (string id, GroupRequest request, HttpContext context) =>
{
    AgentStatus? agente = FindAgent(context, id); if (agente is null) return Results.NotFound();
    string group = request.Grupo?.Trim() ?? "";
    if (!GetPolicies(CurrentOrganization(context)).Any(x => x.Grupo.Equals(group, StringComparison.OrdinalIgnoreCase)))
        return Results.BadRequest(new { error = "El grupo no existe en esta organización." });
    agente.Grupo = group;
    await GuardarAsync();
    return Results.Ok(new { updated = true });
});

app.MapGet("/api/schedule", (HttpContext context) => GetSchedule(CurrentOrganization(context)));
app.MapGet("/api/schedule/history", (HttpContext context) => GetScheduleHistory(CurrentOrganization(context)).OrderByDescending(x => x.FechaUtc).Take(20));
app.MapGet("/api/group-policies", (HttpContext context) => GetPolicies(CurrentOrganization(context)));

app.MapPut("/api/group-policies", async (GroupPoliciesRequest request, HttpContext context) =>
{
    List<GroupPolicy> policies = request.Grupos.Select(x => new GroupPolicy { Grupo = x.Grupo?.Trim() ?? "", MargenEntradaMinutos = x.MargenEntradaMinutos, MargenSalidaMinutos = x.MargenSalidaMinutos }).ToList();
    if (policies.Count is < 1 or > 50 || policies.Any(x => x.Grupo.Length is < 1 or > 60 || x.MargenEntradaMinutos is < 0 or > 180 || x.MargenSalidaMinutos is < 0 or > 180) ||
        policies.Select(x => x.Grupo).Distinct(StringComparer.OrdinalIgnoreCase).Count() != policies.Count)
        return Results.BadRequest(new { error = "Definí entre 1 y 50 grupos con nombres únicos; los márgenes deben estar entre 0 y 180 minutos." });
    Guid organizationId = CurrentOrganization(context);
    string[] removedInUse = agents.Values.Where(x => x.OrganizationId == organizationId)
        .Select(x => x.Grupo).Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(x => !policies.Any(p => p.Grupo.Equals(x, StringComparison.OrdinalIgnoreCase))).ToArray();
    if (removedInUse.Length > 0) return Results.BadRequest(new { error = $"No podés eliminar grupos con equipos asignados: {string.Join(", ", removedInUse)}." });
    policiesByOrganization[organizationId] = policies;
    await SaveOrganizationStateAsync("group-policies", organizationId, policiesPath, policies);
    await RegistrarEventoAsync("SERVER", "Servidor ARES", "POLITICAS_GRUPO_ACTUALIZADAS", "Se actualizaron los margenes de entrada y salida.", organizationId);
    return Results.Ok(policies);
});

app.MapPut("/api/schedule", async (SchedulePublication publication, HttpContext context) =>
{
    Guid organizationId = CurrentOrganization(context);
    ScheduleState schedule = GetSchedule(organizationId);
    List<ScheduleRevision> scheduleHistory = GetScheduleHistory(organizationId);
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
    schedules[organizationId] = schedule;
    await GuardarHorariosAsync(organizationId);
    await RegistrarEventoAsync("SERVER", "Servidor ARES", "HORARIOS_PUBLICADOS",
        $"Se publicaron {schedule.Horarios.Count} turnos para {schedule.Mes:00}/{schedule.Anio}.", organizationId);
    return Results.Ok(schedule);
});

app.MapPost("/api/schedule/rollback", async (RollbackScheduleRequest request, HttpContext context) =>
{
    Guid organizationId = CurrentOrganization(context);
    ScheduleState schedule = GetSchedule(organizationId);
    List<ScheduleRevision> scheduleHistory = GetScheduleHistory(organizationId);
    ScheduleRevision? revision = scheduleHistory.FirstOrDefault(x => x.Id == request.RevisionId);
    if (revision is null) return Results.NotFound(new { error = "Revision no encontrada." });
    scheduleHistory.Add(new ScheduleRevision { FechaUtc = DateTimeOffset.UtcNow, Accion = "Antes de restaurar", Estado = ClonarHorario(schedule) });
    schedule = ClonarHorario(revision.Estado); schedule.Version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); schedule.PublicadoUtc = DateTimeOffset.UtcNow;
    schedules[organizationId] = schedule;
    await GuardarHorariosAsync(organizationId);
    await RegistrarEventoAsync("SERVER", "Servidor ARES", "HORARIOS_RESTAURADOS", $"Se restauro la revision {revision.Id}.", organizationId);
    return Results.Ok(schedule);
});

app.MapPut("/api/agents/{id}/override", async (string id, TemporaryOverrideRequest request, HttpContext context) =>
{
    AgentStatus? agente = FindAgent(context, id); if (agente is null) return Results.NotFound();
    if (request.HastaUtc <= DateTimeOffset.UtcNow || request.HastaUtc > DateTimeOffset.UtcNow.AddDays(31))
        return Results.BadRequest(new { error = "La excepcion debe vencer en el futuro y dentro de 31 dias." });
    agente.ExcepcionPermitirUso = request.PermitirUso; agente.ExcepcionHastaUtc = request.HastaUtc;
    await RegistrarEventoAsync(id, agente.Equipo, request.PermitirUso ? "EXCEPCION_DESBLOQUEO" : "EXCEPCION_BLOQUEO",
        $"{request.Motivo}. Vigente hasta {request.HastaUtc:u}.", agente.OrganizationId);
    await GuardarAsync(); return Results.Ok();
});

app.MapPost("/api/agents/{id}/update", async (string id, HttpContext context) =>
{
    AgentStatus? agente = FindAgent(context, id); if (agente is null) return Results.NotFound();
    agente.ActualizacionSolicitada = true;
    await RegistrarEventoAsync(id, agente.Equipo, "ACTUALIZACION_SOLICITADA", $"Se solicito actualizar ARES Agent a {latestAgentVersion}.", agente.OrganizationId);
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

app.MapDelete("/api/agents/{id}/override", async (string id, HttpContext context) =>
{
    AgentStatus? agente = FindAgent(context, id); if (agente is null) return Results.NotFound();
    agente.ExcepcionPermitirUso = null; agente.ExcepcionHastaUtc = null; await GuardarAsync(); return Results.Ok();
});

app.MapPut("/api/agents/{id}/restriction", async (string id, RestrictionRequest request, HttpContext context) =>
{
    AgentStatus? agente = FindAgent(context, id);
    if (agente is null)
        return Results.NotFound(new { error = "El agente no está registrado." });

    agente.BloqueadoAdministrativamente = request.Bloqueado;
    agente.SolicitudDesbloqueoPendiente = false;
    agente.SolicitudDesbloqueoUtc = null;
    await RegistrarEventoAsync(id, agente.Equipo,
        request.Bloqueado ? "USUARIO_BLOQUEADO" : "USUARIO_DESBLOQUEADO",
        request.Bloqueado ? "Restricción activada desde la consola ARES." : "Restricción retirada desde la consola ARES.", agente.OrganizationId);
    await GuardarAsync();
    return Results.Ok(new { updated = true, bloqueado = request.Bloqueado });
});

app.MapPost("/api/agents/{id}/unlock-request", async (string id, HttpContext context) =>
{
    AgentStatus? agente = FindAgent(context, id);
    if (agente is null)
        return Results.NotFound(new { error = "El agente no está registrado." });
    if (!agente.BloqueadoAdministrativamente && !agente.BloqueadoLocalmente && CalcularMotivo(agente) is not "Fuera del horario" and not "Excepcion: bloqueo temporal")
        return Results.Conflict(new { error = "El equipo no está bloqueado." });

    if (!agente.SolicitudDesbloqueoPendiente)
    {
        agente.SolicitudDesbloqueoPendiente = true;
        agente.SolicitudDesbloqueoUtc = DateTimeOffset.UtcNow;
        await RegistrarEventoAsync(id, agente.Equipo, "SOLICITUD_DESBLOQUEO",
            "El usuario solicitó al administrador que retire la restricción.", agente.OrganizationId);
        await GuardarAsync();
    }
    return Results.Ok(new { received = true, requestedAtUtc = agente.SolicitudDesbloqueoUtc });
});

app.MapPut("/api/agents/{id}/name", async (string id, RenameAgentRequest request, HttpContext context) =>
{
    AgentStatus? agente = FindAgent(context, id);
    if (agente is null)
        return Results.NotFound(new { error = "El agente no está registrado." });
    string nombre = request.Nombre.Trim();
    if (nombre.Length is < 1 or > 50)
        return Results.BadRequest(new { error = "El nombre debe tener entre 1 y 50 caracteres." });
    agente.NombrePersonalizado = nombre;
    await RegistrarEventoAsync(id, nombre, "EQUIPO_RENOMBRADO", $"Nombre real: {agente.Equipo}.", agente.OrganizationId);
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
        "Solicitud enviada desde el portal móvil del equipo.", agente.OrganizationId);
    await GuardarAsync();
    return Results.Content("Solicitud enviada correctamente. El administrador ya fue notificado.", "text/plain; charset=utf-8");
});

app.MapPost("/api/agents/{id}/closed", async (string id, HttpContext context) =>
{
    AgentStatus? agente = FindAgent(context, id); if (agente is null) return Results.NotFound();
    agente.EstaEnLinea = false;
    await RegistrarEventoAsync(id, agente.Equipo, "AGENTE_CERRADO", "El agente notificó un cierre normal.", agente.OrganizationId);
    await GuardarAsync();
    return Results.Ok();
});

app.MapGet("/api/audit", (HttpContext context) => audit.Where(e => e.OrganizationId == CurrentOrganization(context)).OrderByDescending(e => e.FechaUtc).Take(500));

app.MapGet("/api/agents", (HttpContext context) =>
{
    Guid organizationId = CurrentOrganization(context);
    DateTimeOffset limite = DateTimeOffset.UtcNow.AddSeconds(-35);
    return agents.Values.Where(a => a.OrganizationId == organizationId)
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
            ,ProximoCambioUtc = CalcularProximoCambio(a)
            ,ActualizacionDisponible = Version.TryParse(latestAgentVersion, out var latest) && Version.TryParse(a.Version, out var current) && latest > current
            ,UltimaVersion = latestAgentVersion
            ,HorarioPendienteSincronizar = HorarioPendiente(a)
            ,CredencialIndividual = a.CredencialIndividual
        })
        .OrderBy(a => a.Equipo);
});

app.MapDelete("/api/agents", async (HttpContext context) =>
{
    Guid organizationId = CurrentOrganization(context);
    string[] keys = agents.Where(x => x.Value.OrganizationId == organizationId).Select(x => x.Key).ToArray();
    int eliminados = 0;
    foreach (string key in keys) if (agents.TryRemove(key, out _)) eliminados++;
    await RegistrarEventoAsync("SERVER", "Servidor ARES", "LISTA_EQUIPOS_LIMPIADA",
        $"Se eliminaron {eliminados} equipos registrados. Los agentes conectados volverán a registrarse automáticamente.", organizationId);
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
                "El agente dejó de responder; hora estimada por vencimiento del heartbeat.", agente.OrganizationId);
            await GuardarAsync();
        }
    }
}

async Task RegistrarEventoAsync(string agentId, string equipo, string tipo, string detalle, Guid? organizationId = null)
{
    audit.Enqueue(new AgentAuditEvent
    {
        AgentId = agentId,
        OrganizationId = organizationId ?? AresPersistence.DefaultOrganizationId,
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
        await SaveStateAsync("audit", auditPath, audit.ToArray());
    }
    finally { saveLock.Release(); }
}

async Task GuardarAsync()
{
    await saveLock.WaitAsync();
    try
    {
        await SaveStateAsync("agents", dataPath, agents.Values.ToArray());
    }
    finally { saveLock.Release(); }
}

async Task GuardarHorariosAsync(Guid organizationId)
{
    await SaveOrganizationStateAsync("schedule", organizationId, schedulePath, GetSchedule(organizationId));
    await SaveOrganizationStateAsync("schedule-history", organizationId, historyPath, GetScheduleHistory(organizationId));
}

Task SaveOrganizationStateAsync<T>(string key, Guid organizationId, string legacyPath, T value) =>
    SaveStateAsync(organizationId == AresPersistence.DefaultOrganizationId ? key : $"org:{organizationId:N}:{key}", legacyPath, value);

async Task GuardarSesionesPanelAsync()
{
    await saveLock.WaitAsync();
    try { await SaveStateAsync("control-sessions", controlSessionsPath, controlSessions.Values.ToArray()); }
    finally { saveLock.Release(); }
}

async Task<T> LoadStateAsync<T>(string key, string legacyPath, T fallback)
{
    T? databaseValue = await persistence.LoadAsync<T>(key);
    if (databaseValue is not null) return databaseValue;

    T value = File.Exists(legacyPath)
        ? JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(legacyPath)) ?? fallback
        : fallback;
    if (persistence.UsesDatabase) await persistence.SaveAsync(key, value);
    return value;
}

async Task SaveStateAsync<T>(string key, string legacyPath, T value)
{
    if (persistence.UsesDatabase)
    {
        await persistence.SaveAsync(key, value);
        return;
    }

    string temporal = legacyPath + ".tmp";
    await File.WriteAllTextAsync(temporal, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    File.Move(temporal, legacyPath, true);
}

ScheduleState ClonarHorario(ScheduleState value) => JsonSerializer.Deserialize<ScheduleState>(JsonSerializer.Serialize(value)) ?? new();

string CalcularMotivo(AgentStatus agent)
{
    DateTimeOffset now = DateTimeOffset.UtcNow;
    if (agent.ExcepcionHastaUtc > now && agent.ExcepcionPermitirUso.HasValue)
        return agent.ExcepcionPermitirUso.Value ? "Excepcion: uso permitido" : "Excepcion: bloqueo temporal";
    if (agent.BloqueadoAdministrativamente) return "Bloqueo manual";
    ScheduleState schedule = GetSchedule(agent.OrganizationId);
    List<ScheduleInterval> own = schedule.Horarios.Where(x => x.AgentId.Equals(agent.Id, StringComparison.OrdinalIgnoreCase)).ToList();
    if (own.Count == 0) return "Sin horario asignado";
    GroupPolicy policy = GetPolicies(agent.OrganizationId).FirstOrDefault(x => x.Grupo == agent.Grupo) ?? new();
    bool inside = own.Any(x => now >= x.InicioUtc.AddMinutes(-policy.MargenEntradaMinutos) && now < x.FinUtc.AddMinutes(policy.MargenSalidaMinutos));
    return inside ? "Dentro del turno" : "Fuera del horario";
}

DateTimeOffset? CalcularProximoCambio(AgentStatus agent)
{
    DateTimeOffset now = DateTimeOffset.UtcNow;
    return GetSchedule(agent.OrganizationId).Horarios.Where(x => x.AgentId.Equals(agent.Id, StringComparison.OrdinalIgnoreCase))
        .SelectMany(x => new[] { x.InicioUtc, x.FinUtc }).Where(x => x > now).OrderBy(x => x).Cast<DateTimeOffset?>().FirstOrDefault();
}

bool HorarioPendiente(AgentStatus agent)
{
    ScheduleState schedule = GetSchedule(agent.OrganizationId);
    return schedule.Horarios.Any(x => x.AgentId.Equals(agent.Id, StringComparison.OrdinalIgnoreCase)) && agent.HorarioVersionAplicada < schedule.Version;
}

AuthenticatedAdmin CurrentAdmin(HttpContext context) => (AuthenticatedAdmin)context.Items["AresAdmin"]!;
Guid CurrentOrganization(HttpContext context) => context.Items["AresOrganizationId"] is Guid id ? id : AresPersistence.DefaultOrganizationId;
string OrganizationKey(Guid organizationId, string id) => $"{organizationId:N}:{id}";
AgentStatus? FindAgent(HttpContext context, string id) => agents.TryGetValue(OrganizationKey(CurrentOrganization(context), id), out AgentStatus? agent) ? agent : null;
ScheduleState GetSchedule(Guid organizationId) => schedules.GetOrAdd(organizationId, _ => new ScheduleState());
List<ScheduleRevision> GetScheduleHistory(Guid organizationId) => scheduleHistories.GetOrAdd(organizationId, _ => []);
List<GroupPolicy> GetPolicies(Guid organizationId)
{
    List<GroupPolicy> policies = policiesByOrganization.GetOrAdd(organizationId, _ => [new() { Grupo = "General" }]);
    foreach (string assignedGroup in agents.Values.Where(x => x.OrganizationId == organizationId && !string.IsNullOrWhiteSpace(x.Grupo))
                 .Select(x => x.Grupo.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        if (!policies.Any(x => x.Grupo.Equals(assignedGroup, StringComparison.OrdinalIgnoreCase)))
            policies.Add(new GroupPolicy { Grupo = assignedGroup });
    return policies;
}
bool IsOwner(HttpContext context) => context.Items["AresAdmin"] is AuthenticatedAdmin admin && admin.Role == "Owner";
bool IsAdministrator(HttpContext context) => context.Items["AresAdmin"] is AuthenticatedAdmin admin && admin.Role is "Owner" or "Administrator";
bool IsPlatformAdmin(HttpContext context) => context.Items["AresAdmin"] is AuthenticatedAdmin admin &&
    admin.MfaVerified && (platformStaff.TryGetValue(admin.UserId, out PlatformStaffMember? staff) && staff.Enabled ||
        !string.IsNullOrWhiteSpace(platformAdminUserId) && admin.UserId.ToString().Equals(platformAdminUserId, StringComparison.OrdinalIgnoreCase));
bool IsPlatformOwner(HttpContext context) => context.Items["AresAdmin"] is AuthenticatedAdmin admin && admin.MfaVerified &&
    (platformStaff.TryGetValue(admin.UserId, out PlatformStaffMember? staff) && staff.Enabled && staff.Role == "Owner" ||
        !string.IsNullOrWhiteSpace(platformAdminUserId) && admin.UserId.ToString().Equals(platformAdminUserId, StringComparison.OrdinalIgnoreCase));
bool HasPlatformRole(HttpContext context, params string[] roles) => context.Items["AresAdmin"] is AuthenticatedAdmin admin && admin.MfaVerified &&
    ((platformStaff.TryGetValue(admin.UserId, out PlatformStaffMember? staff) && staff.Enabled && roles.Contains(staff.Role)) ||
     (!string.IsNullOrWhiteSpace(platformAdminUserId) && admin.UserId.ToString().Equals(platformAdminUserId, StringComparison.OrdinalIgnoreCase) && roles.Contains("Owner")));
bool IsWebAuthClient(HttpContext context) => context.Request.Headers["X-ARES-Web"].FirstOrDefault() == "1";
IResult WebAuthResult(HttpContext context, AuthResult result)
{
    context.Response.Cookies.Append("ares_refresh", result.RefreshToken, new CookieOptions
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/api/auth", MaxAge = TimeSpan.FromDays(30), IsEssential = true
    });
    return Results.Ok(new { result.AccessToken, result.ExpiresIn, result.User, result.MfaRequired, result.FactorId });
}
bool ValidRole(string role) => role is "Owner" or "Administrator" or "Operator" or "Viewer";
byte[] HashInvitationCode(string? code) => SHA256.HashData(Encoding.UTF8.GetBytes((code ?? "").Trim().ToUpperInvariant()));
byte[] HashSecret(string? value) => SHA256.HashData(Encoding.UTF8.GetBytes((value ?? "").Trim().ToUpperInvariant()));
byte[] HashRecoveryCode(Guid userId, string? value) => SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{(value ?? "").Trim().ToUpperInvariant()}"));
List<string> GenerateRecoveryCodes()
{
    const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    var codes = new List<string>();
    for (int item = 0; item < 10; item++)
    {
        byte[] random = RandomNumberGenerator.GetBytes(8);
        string value = new(random.Select(x => alphabet[x % alphabet.Length]).ToArray());
        codes.Add($"ARES-{value[..4]}-{value[4..]}");
    }
    return codes;
}
List<JsonElement> ExtractMfaFactors(JsonElement root)
{
    var result = new List<JsonElement>();
    void Visit(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out _)) result.Add(item.Clone());
                else Visit(item);
            }
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (string property in new[] { "factors", "all", "totp", "phone" })
            if (value.TryGetProperty(property, out JsonElement nested)) Visit(nested);
    }
    Visit(root);
    return result.GroupBy(x => x.TryGetProperty("id", out JsonElement id) ? id.GetString() : null)
        .Select(x => x.First()).ToList();
}

byte[] HashToken(string? value) => SHA256.HashData(Encoding.UTF8.GetBytes(value ?? ""));
string ClientIp(HttpContext context) => context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
    ?? context.Connection.RemoteIpAddress?.ToString() ?? "Desconocida";
string ClientName(HttpContext context) => string.IsNullOrWhiteSpace(context.Request.Headers.UserAgent)
    ? "ARES Centro de Control" : context.Request.Headers.UserAgent.ToString();
string BearerToken(HttpContext context) => context.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
bool CanAccess(string role, string method, PathString path)
{
    if (role is "Owner" or "Administrator") return true;
    if (HttpMethods.IsGet(method)) return true;
    if (path.Equals("/api/control-sessions/heartbeat")) return true;
    if (role == "Viewer") return false;
    // Operador: operación cotidiana de equipos, sin administración global,
    // publicación de horarios, limpieza ni distribución de software.
    string value = path.Value ?? "";
    return role == "Operator" && value.StartsWith("/api/agents/", StringComparison.OrdinalIgnoreCase) &&
        (value.EndsWith("/restriction", StringComparison.OrdinalIgnoreCase) ||
         value.EndsWith("/override", StringComparison.OrdinalIgnoreCase) ||
         value.EndsWith("/name", StringComparison.OrdinalIgnoreCase) ||
         value.EndsWith("/group", StringComparison.OrdinalIgnoreCase));
}

async Task<BillingSubscription> ReconcileBillingAsync(BillingSubscription stored, MercadoPagoService provider,
    AresPersistence database, CancellationToken cancellationToken)
{
    bool cancellationRequested = stored.Status is "cancelled" or "canceled";
    MercadoPagoSubscription? remote = await provider.GetSubscriptionAsync(stored.ProviderSubscriptionId, cancellationToken);
    remote ??= await provider.FindSubscriptionByPlanAsync(stored.ProviderSubscriptionId, cancellationToken);
    if (remote is null || string.IsNullOrWhiteSpace(remote.Id)) return stored;

    MercadoPagoAuthorizedPayment? payment = await provider.FindLatestAuthorizedPaymentAsync(remote.Id, cancellationToken);
    bool approved = payment?.PaymentStatus == "approved";
    DateTimeOffset? paidUntil = approved ? (payment?.DebitDate ?? DateTimeOffset.UtcNow).AddMonths(1).ToUniversalTime() : stored.PaidUntil;
    if (cancellationRequested) await provider.CancelSubscriptionAsync(remote.Id, cancellationToken);
    var updated = stored with
    {
        ProviderSubscriptionId = remote.Id,
        Status = cancellationRequested ? "cancelled" : approved ? "authorized" : remote.Status,
        LastPaymentStatus = payment?.PaymentStatus ?? stored.LastPaymentStatus,
        PaidUntil = paidUntil
    };
    await database.UpsertBillingSubscriptionAsync(updated);
    if (payment is not null && !string.IsNullOrWhiteSpace(payment.PaymentId))
        await database.UpsertBillingPaymentAsync(new(Guid.NewGuid(), stored.OrganizationId, payment.PaymentId, remote.Id,
            stored.RequestedPlan, stored.AmountArs, payment.PaymentStatus, payment.DebitDate, approved ? paidUntil : null,
            $"https://www.mercadopago.com.ar/activities/detail/{Uri.EscapeDataString(payment.PaymentId)}", payment.DebitDate ?? DateTimeOffset.UtcNow));
    if (!string.IsNullOrWhiteSpace(remote.PlanId))
        await provider.CancelSubscriptionOrPlanAsync(remote.PlanId, cancellationToken);

    if (approved)
    {
        PlanDefinition definition = PlanDetails(stored.RequestedPlan);
        decimal usd = definition.MonthlyPriceUsd + stored.AdditionalDevices * definition.AdditionalDeviceUsd +
            stored.AdditionalPanelUsers * definition.AdditionalPanelUserUsd;
        await database.UpdateLicenseAsync(stored.OrganizationId, stored.RequestedPlan, "Active", definition.IncludedDevices,
            stored.AdditionalDevices, definition.IncludedPanelUsers, stored.AdditionalPanelUsers, usd, paidUntil, 3);
    }
    return updated;
}

async Task<BillingSubscription> ApplyAuthorizedPaymentAsync(BillingSubscription stored, MercadoPagoSubscription remote,
    MercadoPagoAuthorizedPayment payment, MercadoPagoService provider, AresPersistence database, CancellationToken cancellationToken)
{
    bool cancellationRequested = stored.Status is "cancelled" or "canceled";
    DateTimeOffset? paidUntil = payment.PaymentStatus == "approved"
        ? (payment.DebitDate ?? DateTimeOffset.UtcNow).AddMonths(1).ToUniversalTime()
        : stored.PaidUntil;
    if (cancellationRequested) await provider.CancelSubscriptionAsync(remote.Id, cancellationToken);
    if (!string.IsNullOrWhiteSpace(remote.PlanId)) await provider.CancelSubscriptionOrPlanAsync(remote.PlanId, cancellationToken);
    var updated = stored with
    {
        ProviderSubscriptionId = remote.Id,
        Status = cancellationRequested ? "cancelled" : payment.PaymentStatus == "approved" ? "authorized" : remote.Status,
        LastPaymentStatus = payment.PaymentStatus,
        PaidUntil = paidUntil
    };
    await database.UpsertBillingSubscriptionAsync(updated);
    if (!string.IsNullOrWhiteSpace(payment.PaymentId))
        await database.UpsertBillingPaymentAsync(new(Guid.NewGuid(), stored.OrganizationId, payment.PaymentId, remote.Id,
            stored.RequestedPlan, stored.AmountArs, payment.PaymentStatus, payment.DebitDate,
            payment.PaymentStatus == "approved" ? paidUntil : null,
            $"https://www.mercadopago.com.ar/activities/detail/{Uri.EscapeDataString(payment.PaymentId)}", payment.DebitDate ?? DateTimeOffset.UtcNow));
    if (payment.PaymentStatus == "approved")
    {
        PlanDefinition definition = PlanDetails(stored.RequestedPlan);
        decimal usd = definition.MonthlyPriceUsd + stored.AdditionalDevices * definition.AdditionalDeviceUsd +
            stored.AdditionalPanelUsers * definition.AdditionalPanelUserUsd;
        await database.UpdateLicenseAsync(stored.OrganizationId, stored.RequestedPlan, "Active", definition.IncludedDevices,
            stored.AdditionalDevices, definition.IncludedPanelUsers, stored.AdditionalPanelUsers, usd, paidUntil, 3);
    }
    return updated;
}

string NormalizePlan(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
{
    "trial" or "prueba" => "Trial",
    "basic" or "esencial" => "Basic",
    "professional" or "profesional" => "Professional",
    "business" or "empresa" => "Business",
    "enterprise" or "corporativo" => "Enterprise",
    _ => ""
};
string NormalizeLicenseStatus(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
{
    "active" or "activa" => "Active",
    "suspended" or "suspendida" => "Suspended",
    "expired" or "vencida" => "Expired",
    "canceled" or "cancelada" => "Canceled",
    "pastdue" or "pago pendiente" => "PastDue",
    _ => ""
};

PlanDefinition PlanDetails(string plan) => plan switch
{
    "Trial" => new("Prueba", 5, 1, 0m, 0m, 0m),
    "Basic" => new("Esencial", 10, 2, 25m, 3m, 4m),
    "Professional" => new("Profesional", 30, 10, 65m, 2.5m, 3m),
    "Business" => new("Empresa", 100, 25, 149m, 2m, 2m),
    "Enterprise" => new("Corporativo", 100, 25, 249m, 2m, 2m),
    _ => new("Prueba", 5, 1, 0m, 0m, 0m)
};

internal sealed record PlatformStaffRequest(string? Email, string? Role, bool Enabled = true);
internal sealed record CreateSupportTicketRequest(Guid OrganizationId, string? Subject, string? Detail, string? Priority);
internal sealed record UpdateSupportTicketRequest(string Status);
