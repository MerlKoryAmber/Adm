using AdManager.Application;
using AdManager.Application.Abstractions;
using AdManager.Infrastructure.Ad;
using AdManager.Infrastructure.Automation;
using AdManager.Infrastructure.Data;
using AdManager.Infrastructure.Exchange;
using AdManager.Web;
using AdManager.Web.Components;
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
builder.Services.AddSingleton<IOperationalCredentialProvider, ConfiguredCredentialProvider>();
builder.Services.AddScoped<IAdService, AdService>();
builder.Services.AddScoped<IAdDirectory, AdDirectory>();
// Провайдер аудита: ADMGR_AUDIT=Ef (LocalDB/SQL, по умолчанию) | File (JSONL, без БД — для IIS-лабы).
var auditProvider = Environment.GetEnvironmentVariable("ADMGR_AUDIT")
                    ?? builder.Configuration["Audit:Provider"] ?? "Ef";
var useEfAudit = auditProvider.Equals("Ef", StringComparison.OrdinalIgnoreCase);
if (useEfAudit)
{
    builder.Services.AddDbContext<AdManagerDbContext>(o =>
        o.UseSqlServer(builder.Configuration.GetConnectionString("AdManagerDb")));
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

if (authMode == "IIS")
{
    builder.Services.AddAuthentication(Microsoft.AspNetCore.Server.IIS.IISServerDefaults.AuthenticationScheme);
}
else if (authMode == "Negotiate")
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
}

if (requireAuth)
{
    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    });
}
else
{
    builder.Services.AddAuthorization();
}
builder.Services.AddCascadingAuthenticationState();

// RBAC (делегирование): под auth — реальный движок; в dev/no-auth — allow-all.
var rbacOpt = builder.Configuration.GetSection("Rbac").Get<RbacOptions>() ?? new RbacOptions();
builder.Services.AddSingleton(rbacOpt);
var rbacPath = builder.Configuration["Rbac:FilePath"]
               ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "rbac.json");
builder.Services.AddSingleton<IRbacStore>(new FileRbacStore(rbacPath));
if (requireAuth)
    builder.Services.AddScoped<IRbacEngine, RbacEngine>();
else
    builder.Services.AddScoped<IRbacEngine, AllowAllRbacEngine>();

// Automation (планировщик + правила, без апрувов).
var autoPath = builder.Configuration["Automation:FilePath"]
               ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "automation.json");
builder.Services.AddSingleton<IAutomationStore>(new FileAutomationStore(autoPath));

// Шаблоны формы пользователя (Layout View).
var tplPath = builder.Configuration["Templates:FilePath"]
              ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "user-templates.json");
builder.Services.AddSingleton<IUserTemplateStore>(new FileUserTemplateStore(tplPath));

builder.Services.AddSingleton<AutomationScheduler>();
builder.Services.AddSingleton<IAutomationScheduler>(sp => sp.GetRequiredService<AutomationScheduler>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<AutomationScheduler>());

// Settings + напоминатель истечения пароля (SMTP).
var settingsPath = builder.Configuration["Settings:FilePath"]
                   ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "app-settings.json");
builder.Services.AddSingleton<ISettingsStore>(new FileSettingsStore(settingsPath));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IPasswordExpiryService, PasswordExpiryService>();
builder.Services.AddSingleton<PasswordExpiryNotifier>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PasswordExpiryNotifier>());

// Blazor Server.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

// Создать схему аудита (EnsureCreated; миграции — позже). Только для EF-провайдера.
if (useEfAudit)
{
    try
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AdManagerDbContext>().Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "EnsureCreated failed");
    }
}

app.UseStaticFiles();
if (requireAuth)
{
    app.UseAuthentication();
}
app.UseAuthorization();
app.UseAntiforgery();
app.MapGet("/health", () => Results.Ok(new { status = "ok", ts = DateTimeOffset.UtcNow })).AllowAnonymous();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
