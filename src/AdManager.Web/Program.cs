using AdManager.Application;
using AdManager.Application.Abstractions;
using AdManager.Infrastructure.Ad;
using AdManager.Infrastructure.Automation;
using AdManager.Infrastructure.Data;
using AdManager.Infrastructure.Exchange;
using AdManager.Web;
using AdManager.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Секреты лабы из .env (не в git).
DotEnv.Overlay(builder);

// Опции.
var adOpt = builder.Configuration.GetSection("Ad").Get<AdConnectionOptions>() ?? new AdConnectionOptions();
var opOpt = builder.Configuration.GetSection("Operational").Get<OperationalCredentialOptions>() ?? new OperationalCredentialOptions();
var uiOpt = builder.Configuration.GetSection("Ui").Get<UiOptions>() ?? new UiOptions();
builder.Services.AddSingleton(adOpt);
builder.Services.AddSingleton(opOpt);
builder.Services.AddSingleton(uiOpt);

// Инфраструктура.
// Op-identity: хранимая УЗ читается из БД (settings-стор), .env — bootstrap-fallback (ADR-0006).
builder.Services.AddSingleton<IOperationalCredentialProvider>(sp =>
    new ConfiguredCredentialProvider(
        sp.GetRequiredService<OperationalCredentialOptions>(),
        sp.GetService<ISettingsStore>()));
builder.Services.AddScoped<IAdService, AdService>();
builder.Services.AddScoped<IAdDirectory, AdDirectory>();
// Движок отчётов (эталон ADManager Plus): единая точка для UI и автоматизаций.
builder.Services.AddSingleton(sp => new UiBaseDn(sp.GetRequiredService<UiOptions>().BaseDn));
builder.Services.AddScoped<IReportService, ReportService>();
// Кэш дерева OU (singleton) + фоновое обновление раз в час. Держит полное дерево,
// фильтрует системные контейнеры (Domain Controllers и т.п.).
builder.Services.AddSingleton<IOuTreeProvider>(sp =>
    new OuTreeCache(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<UiOptions>().BaseDn,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<OuTreeCache>()));
builder.Services.AddHostedService<OuTreeRefresher>();
// Retention аудита: раз в сутки чистит записи старше AuditRetentionDays (Settings).
builder.Services.AddHostedService<AuditRetentionService>();
// Провайдер аудита: ADMGR_AUDIT=Ef (SQL, по умолчанию) | File (JSONL, без БД).
var auditProvider = Environment.GetEnvironmentVariable("ADMGR_AUDIT")
                    ?? builder.Configuration["Audit:Provider"] ?? "Ef";
var useEfAudit = auditProvider.Equals("Ef", StringComparison.OrdinalIgnoreCase);

// Провайдер состояния (RBAC/Settings/Automation/Templates): ADMGR_STORE=Ef (SQL, по умолчанию) | File (App_Data/*.json).
// ADR-0006: состояние — в БД. File остаётся для dev/no-auth и как fallback.
var storeProvider = Environment.GetEnvironmentVariable("ADMGR_STORE")
                    ?? builder.Configuration["Store:Provider"] ?? "Ef";
var useEfStore = storeProvider.Equals("Ef", StringComparison.OrdinalIgnoreCase);

var needsDb = useEfAudit || useEfStore;
if (needsDb)
{
    // Фабрика (для документных сторов из любого контекста, в т.ч. singleton BackgroundService)
    // + scoped-контекст (для EfAuditLog).
    builder.Services.AddDbContextFactory<AdManagerDbContext>(o =>
        o.UseSqlServer(builder.Configuration.GetConnectionString("AdManagerDb")));
    builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AdManagerDbContext>>().CreateDbContext());
}

if (useEfAudit)
{
    builder.Services.AddScoped<IAuditLog, EfAuditLog>();
}
else
{
    var auditPath = builder.Configuration["Audit:FilePath"]
                    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "audit.jsonl");
    builder.Services.AddSingleton<IAuditLog>(new FileAuditLog(auditPath));
}
builder.Services.AddScoped<AdManagementService>();

// Group Policy (чтение GPO + управление линками gPLink через LDAP; без GPMC).
builder.Services.AddScoped<IGpoDirectory, GpoDirectory>();
builder.Services.AddScoped<IGpoService, GpoService>();
builder.Services.AddScoped<GpoManagementService>();

// Exchange (remote PowerShell; сервер в лабе не поднят — операции вернут ошибку, пока не сконфигурирован).
var exOpt = builder.Configuration.GetSection("Exchange").Get<ExchangeOptions>() ?? new ExchangeOptions();
builder.Services.AddSingleton(exOpt);
builder.Services.AddScoped<IExchangeService, ExchangeService>();
builder.Services.AddScoped<ExchangeManagementService>();

// Аутентификация техников. Режим:
//   ADMGR_AUTH=Negotiate (по умолчанию, Kestrel/Windows Auth Kerberos SSO)
//   ADMGR_AUTH=IIS       (хостинг в IIS — Windows Auth делает IIS)
//   ADMGR_AUTH=None      (только локальный smoke; = ADMGR_DEV_NOAUTH=1)
var authMode = Environment.GetEnvironmentVariable("ADMGR_AUTH");
if (Environment.GetEnvironmentVariable("ADMGR_DEV_NOAUTH") == "1") authMode = "None";
authMode ??= "Negotiate";
var requireAuth = authMode is "IIS" or "Negotiate";

// Смешанная аутентификация (ADR-0007): Windows Auth (доменные SSO) + cookie (локальные УЗ).
// Cookie-схема регистрируется во всех защищённых режимах — по ней входят локальные УЗ через /login.
const string CookieScheme = "AdmgrCookie";
if (authMode == "IIS")
{
    // Default scheme (Windows/IIS) authenticates domain SSO; unauthenticated requests
    // are challenged on the cookie scheme -> redirected to /login for local accounts.
    builder.Services.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = Microsoft.AspNetCore.Server.IIS.IISServerDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = CookieScheme;
        })
        .AddCookie(CookieScheme, o => { o.LoginPath = "/login"; o.LogoutPath = "/logout"; o.AccessDeniedPath = "/login"; });
}
else if (authMode == "Negotiate")
{
    builder.Services.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = NegotiateDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = CookieScheme;
        })
        .AddNegotiate()
        .AddCookie(CookieScheme, o => { o.LoginPath = "/login"; o.LogoutPath = "/logout"; o.AccessDeniedPath = "/login"; });
}

if (requireAuth)
{
    // Windows-схема зависит от режима хостинга.
    var winScheme = authMode == "IIS"
        ? Microsoft.AspNetCore.Server.IIS.IISServerDefaults.AuthenticationScheme
        : NegotiateDefaults.AuthenticationScheme;
    builder.Services.AddAuthorization(options =>
    {
        // Аутентифицирован ЛЮБОЙ из схем: Windows SSO ИЛИ локальный cookie (ADR-0007).
        // Обе схемы явно в политике — иначе cookie-принципал не виден (проверяется только дефолтная).
        options.FallbackPolicy = new AuthorizationPolicyBuilder(winScheme, CookieScheme)
            .RequireAuthenticatedUser()
            .Build();
    });
}
else
{
    builder.Services.AddAuthorization();
}
builder.Services.AddCascadingAuthenticationState();

// Локальные УЗ панели (ADR-0007): стор + auth-сервис. Только в Ef-режиме (в БД).
if (useEfStore)
{
    builder.Services.AddSingleton<ILocalUserStore, EfLocalUserStore>();
    builder.Services.AddSingleton<ILocalAuthService, LocalAuthService>();
}

// RBAC (делегирование): под auth — реальный движок; в dev/no-auth — allow-all.
var rbacOpt = builder.Configuration.GetSection("Rbac").Get<RbacOptions>() ?? new RbacOptions();
builder.Services.AddSingleton(rbacOpt);
var rbacPath = builder.Configuration["Rbac:FilePath"]
               ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "rbac.json");
if (useEfStore)
    builder.Services.AddSingleton<IRbacStore, EfRbacStore>();
else
    builder.Services.AddSingleton<IRbacStore>(new FileRbacStore(rbacPath));
if (requireAuth)
    builder.Services.AddScoped<IRbacEngine, RbacEngine>();
else
    builder.Services.AddScoped<IRbacEngine, AllowAllRbacEngine>();

// Automation (планировщик + правила, без апрувов).
var autoPath = builder.Configuration["Automation:FilePath"]
               ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "automation.json");
if (useEfStore)
    builder.Services.AddSingleton<IAutomationStore, EfAutomationStore>();
else
    builder.Services.AddSingleton<IAutomationStore>(new FileAutomationStore(autoPath));

// Шаблоны формы пользователя (Layout View).
var tplPath = builder.Configuration["Templates:FilePath"]
              ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "user-templates.json");
if (useEfStore)
    builder.Services.AddSingleton<IUserTemplateStore, EfUserTemplateStore>();
else
    builder.Services.AddSingleton<IUserTemplateStore>(new FileUserTemplateStore(tplPath));

// Каталог custom-атрибутов (глобальный).
var caPath = builder.Configuration["CustomAttrs:FilePath"]
             ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "custom-attributes.json");
if (useEfStore)
    builder.Services.AddSingleton<ICustomAttributeStore, EfCustomAttributeStore>();
else
    builder.Services.AddSingleton<ICustomAttributeStore>(new FileCustomAttributeStore(caPath));

builder.Services.AddSingleton<AutomationScheduler>();
builder.Services.AddSingleton<IAutomationScheduler>(sp => sp.GetRequiredService<AutomationScheduler>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<AutomationScheduler>());

// Шифрование секретов (ADR-0006, envelope): keyring вне БД + protector.
// Путь keyring: ADMGR_KEYRING | конфиг | дефолт C:\ProgramData\AdManager\keyring.
// Ключ НЕ в каталоге сайта: переживает деплой, не попадает в publish.
var keyringDir = Environment.GetEnvironmentVariable("ADMGR_KEYRING")
                 ?? builder.Configuration["Secrets:KeyringDir"]
                 ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "AdManager", "keyring");
var keyring = new DpapiKeyring(keyringDir);
builder.Services.AddSingleton<DpapiKeyring>(keyring);
builder.Services.AddSingleton<IKeyring>(keyring);
builder.Services.AddSingleton<ISecretProtector>(new EnvelopeSecretProtector(keyring));

// Settings + напоминатель истечения пароля (SMTP).
// ISettingsStore оборачивается EncryptedSettingsStore — секреты шифруются на входе в стор.
var settingsPath = builder.Configuration["Settings:FilePath"]
                   ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "app-settings.json");
if (useEfStore)
    builder.Services.AddSingleton<ISettingsStore>(sp =>
        new EncryptedSettingsStore(
            ActivatorUtilities.CreateInstance<EfSettingsStore>(sp),
            sp.GetRequiredService<ISecretProtector>()));
else
    builder.Services.AddSingleton<ISettingsStore>(sp =>
        new EncryptedSettingsStore(
            new FileSettingsStore(settingsPath),
            sp.GetRequiredService<ISecretProtector>()));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IPasswordExpiryService, PasswordExpiryService>();
builder.Services.AddSingleton<PasswordExpiryNotifier>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PasswordExpiryNotifier>());

// Blazor Server.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

// Создать схему БД (EnsureCreated; миграции — позже). Для любого EF-провайдера (аудит или сторы).
if (needsDb)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdManagerDbContext>();
        db.Database.EnsureCreated();
        // EnsureCreated не мигрирует существующую схему. Новые колонки аудита
        // (Kind/Feature/Instance/Action/Host/Method — отчёты Delegation) добавляем idempotent ALTER.
        foreach (var col in new[]
        {
            "Kind INT NOT NULL DEFAULT 0",
            "Feature NVARCHAR(MAX) NULL",
            "Instance NVARCHAR(MAX) NULL",
            "Action NVARCHAR(MAX) NULL",
            "Host NVARCHAR(MAX) NULL",
            "Method NVARCHAR(MAX) NULL",
        })
        {
            var name = col.Split(' ')[0];
            try
            {
                db.Database.ExecuteSqlRaw(
                    $"IF COL_LENGTH('AuditEntries', '{name}') IS NULL ALTER TABLE [AuditEntries] ADD {col};");
            }
            catch (Exception ex) { app.Logger.LogWarning(ex, "Audit column {Col} add skipped", name); }
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "EnsureCreated failed");
    }
}

// Одноразовый сидер: если Ef-сторы и в БД ещё нет состояния, а на диске лежат
// непустые App_Data/*.json (прежний файловый режим) — импортировать их в БД (ADR-0006).
// Идемпотентно: импорт только когда целевой стор пуст.
if (useEfStore)
{
    try
    {
        var appData = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
        await StateSeeder.SeedFromFilesAsync(app.Services, appData, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "State seeding from files failed");
    }
}

// Сид встроенного admin + роли Administrators при установке (ADR-0007):
// admin (builtin) состоит во встроенной роли Administrators (super-admin, builtin).
if (useEfStore)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<ILocalAuthService>().EnsureSeedAdminAsync();
        await AdManager.Web.BootstrapAdmin.EnsureAsync(sp);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Local admin seeding failed");
    }
}

// Полное отключение браузерного кеша для всех ответов (статика + страницы).
// Ставится первым, чтобы заголовки попали и на статику, и на Blazor-ответы.
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        h["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        h["Pragma"] = "no-cache";
        h["Expires"] = "0";
        return Task.CompletedTask;
    });
    await next();
});

app.UseStaticFiles();
if (requireAuth)
{
    app.UseAuthentication();
}
app.UseAuthorization();
app.UseAntiforgery();

// Запись доменного SSO-входа (Technician Logon Report): у Windows Auth нет явного
// login-шага, поэтому пишем при первом аутентифицированном запросе на SID (дедуп по окну).
if (requireAuth && useEfStore)
{
    app.Use(async (ctx, next) =>
    {
        var u = ctx.User;
        if (u?.Identity?.IsAuthenticated == true
            && u.Identity.AuthenticationType != "AdmgrCookie") // cookie-вход уже записан в /auth/login
        {
            var actor = AdManager.Web.CurrentUser.From(u);
            if (!actor.Sid.StartsWith("LOCAL:", StringComparison.Ordinal)
                && AdManager.Web.SsoLogonTracker.ShouldLog(actor.Sid))
            {
                var audit = ctx.RequestServices.GetRequiredService<IAuditLog>();
                var host = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
                await AdManager.Web.LogonAudit.WriteAsync(audit, actor.Sid, actor.DisplayName, host, "SSO", success: true, "Success");
            }
        }
        await next();
    });
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", ts = DateTimeOffset.UtcNow })).AllowAnonymous();

// Локальный вход (ADR-0007): POST /login проверяет логин/пароль и ставит cookie.
// GET /login отдаёт форму (Blazor-страница Login.razor, анонимна).
if (useEfStore && requireAuth)
{
    app.MapPost("/auth/login", async (HttpContext ctx, ILocalAuthService auth, IAuditLog audit) =>
    {
        var form = await ctx.Request.ReadFormAsync();
        var user = form["user"].ToString();
        var pass = form["pass"].ToString();
        var returnUrl = form["returnUrl"].ToString();
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/')) returnUrl = "/";

        var host = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
        var local = await auth.ValidateAsync(user, pass);
        if (local is null)
        {
            // неуспешный вход тоже в журнал (Technician Logon Report)
            await AdManager.Web.LogonAudit.WriteAsync(audit, user, user, host, "Password", success: false, "Invalid user name or password");
            return Results.Redirect("/login?error=1&returnUrl=" + Uri.EscapeDataString(returnUrl));
        }

        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.Name, local.UserName),
            new("http://schemas.microsoft.com/ws/2008/06/identity/claims/primarysid", local.Sid),
        };
        if (local.IsSuperAdmin) claims.Add(new(AdManager.Web.CurrentUser.SuperAdminClaim, "true"));
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "AdmgrCookie");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        await ctx.SignInAsync("AdmgrCookie", principal);
        await AdManager.Web.LogonAudit.WriteAsync(audit, local.Sid, local.UserName, host, "Password", success: true, "Success");
        return Results.Redirect(returnUrl);
    }).AllowAnonymous().DisableAntiforgery();

    app.MapPost("/logout", async (HttpContext ctx) =>
    {
        await ctx.SignOutAsync("AdmgrCookie");
        return Results.Redirect("/login");
    });
}

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
