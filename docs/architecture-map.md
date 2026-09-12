# Архитектурная карта admanager

Составлено: 2026-09-12 МСК. Навигация — по графу Graphify (`graphify-out/graph.json`, 1137 узлов / 2126 рёбер / 56 сообществ; `graphify god-nodes`/`query`). Метки: **[КОД]** — подтверждено кодом; **[RUNTIME?]** — предположение о поведении в рантайме.

Стек: C# / .NET 8, Blazor Server (interactive server), Clean/onion `Web → Application → Domain`, `Infrastructure.*` реализуют интерфейсы Application. UI — английский; чат/доки — русский.

## 1. Точки входа
- **[КОД]** Единственная точка входа — `src/AdManager.Web/Program.cs`: конфигурация DI, аутентификация, `MapRazorComponents<App>().AddInteractiveServerRenderMode()`, `UseAntiforgery`. Компонент-корень `App`/`Routes` в `src/AdManager.Web/Components/`.
- **[КОД]** Решение/проекты: `AdManager.sln` + csproj по слоям. Хостинг-абстракции — `Microsoft.Extensions.Hosting` (тянется из Infrastructure.Automation).
- **[RUNTIME?]** Хост — Kestrel (Negotiate) или IIS (:8080) в зависимости от `ADMGR_AUTH`; endpoint `/health` отвечал в dev (в IIS Anonymous отключён).

## 2. UI / сервисный слой / фон / интеграции
- **[КОД]** UI: Blazor Server, `src/AdManager.Web/Components/`. Каркас — `Layout/MainLayout.razor` (верхние табы Management/Reports/Delegation/Automation + левое дерево). Страницы `Components/Pages/*.razor`: Home, Users, CreateUser, ModifyUser, Templates, TemplateEditor, Groups, Computers, OUs, Contacts, Exchange, GroupPolicy, Reports (+ `Reports.razor.js`), Delegation, Automation, Audit, Search.
- **[КОД]** Сервисный слой (фасады Application) — единственный путь UI→операции: `AdManagementService` (`src/AdManager.Application/AdManagementService.cs`, метод `.Run`: RBAC → аудит(attempt) → операция под operational identity → аудит(result)), `ExchangeManagementService`, `GpoManagementService`. Прямых вызовов `IAdService`/`IExchangeService`/`IGpoService` из UI нет.
- **[КОД]** REST/API-контроллеров НЕТ. Взаимодействие клиента и сервера — через Blazor-circuit. **[RUNTIME?]** транспорт — SignalR/WebSocket, состояние на circuit каждого пользователя.
- **[КОД]** Фоновые задачи: `AutomationScheduler` (`src/AdManager.Infrastructure.Automation/AutomationScheduler.cs`, `BackgroundService` + `ncrontab 3.3.3`), cron в МСК; контракты `Automation.cs` (Application), UI `Automation.razor`, стор `FileAutomationStore`. (Отложено, ветка: `PasswordExpiryNotifier` — рассылка о скором истечении пароля.)
- **[КОД]** Интеграция AD: `System.DirectoryServices` — `AdService` (write), `AdDirectory` (read), `GpoService`/`GpoDirectory` (GPO/gPLink), `PasswordExpiryService` (WIP); единый бинд — `Ldap.Bind` (`src/AdManager.Infrastructure.Ad/Ldap.cs`).
- **[КОД]** Интеграция Exchange 2019: remote PowerShell — `ExchangeService` (`src/AdManager.Infrastructure.Exchange/ExchangeService.cs`, Runspace/PowerShell, параметризованные командлеты). **[RUNTIME? / ИЗВЕСТНО]** живого сервера нет — рантайм не верифицирован.

## 3. БД / очереди / кэш / внешние сервисы
- **[КОД]** БД: SQL Server / LocalDB через EF Core — `src/AdManager.Infrastructure.Data/AdManagerDbContext.cs`, пакет `Microsoft.EntityFrameworkCore.SqlServer 8.0.31`. Используется **только для аудита** (`EfAuditLog`). Провайдер аудита переключается: `ADMGR_AUDIT=Ef` (LocalDB/SQL) | `File` (JSONL `App_Data/audit.jsonl`, `FileAuditLog`) — выбор в `Program.cs`.
- **[КОД]** Файловые сторы конфигурации (не БД, `App_Data/`, gitignored): `FileRbacStore` (rbac.json), `FileAutomationStore` (automation.json), `FileUserTemplateStore` (user-templates.json), `FileSettingsStore` (WIP). Все — singletons с `SemaphoreSlim`; запись целиком (без temp+rename — риск R7).
- **[КОД]** Очередей/брокеров и кэша (Redis и т.п.) НЕТ — соответствующих зависимостей в графе/csproj нет.
- **[КОД]** Внешние сервисы: Active Directory (LDAP/Kerberos), Exchange (remote PowerShell), SMTP (WIP-нотифаер). **[RUNTIME?]** фактические соединения зависят от окружения (DC/Exchange/SMTP).

## 4. Аутентификация / авторизация / секреты
- **[КОД]** AuthN техников — Windows Auth. Режим `ADMGR_AUTH`: `Negotiate` (Kestrel `AddNegotiate`, Kerberos SSO) | `IIS` (`IISServerDefaults`) | `None` (=`ADMGR_DEV_NOAUTH=1`, локальный smoke). Под auth — `FallbackPolicy RequireAuthenticatedUser` (`Program.cs`).
- **[КОД]** AuthZ — RBAC: `IRbacEngine` → `RbacEngine` (`src/AdManager.Application/Rbac.cs`, `.AuthorizeAsync`): супер-админы (`RbacOptions.SuperAdmins`, Domain Admins RID 512) + назначения «субъект (SID техника/группы) → роль (набор `Permission`) → scope (OU)». В dev/no-auth — `AllowAllRbacEngine` (`Configuration.cs`). Каждая операция авторизуется в `*ManagementService.Run` (в т.ч. поштучно в bulk).
- **[КОД]** Домен делегирования: `HelpDeskRole`, `DelegationScope` (OU + subtree), `RoleAssignment` (`src/AdManager.Domain/Entities.cs`); UI `Delegation.razor`. **[ИЗВЕСТНО]** group-scope не реализован (только OU).
- **[КОД]** Operational identity (операции выполняет служебная УЗ, не техник — нет double-hop): `IOperationalCredentialProvider` → `ConfiguredCredentialProvider.GetIdentity` (`Configuration.cs`): режим `gMSA` (без пароля) | `StoredCredential` (хранимая доменная УЗ). Бинд к AD использует эту identity (`Ldap.Bind`).
- **[КОД]** Секреты и передача: `.env` (gitignored) оверлеится в конфиг через `Overlay`/`DotEnv` (`src/AdManager.Web/Options.cs`); плюс `appsettings.json` и env-vars `ADMGR_*`. **[КОД / РИСК]** `OperationalCredentialOptions.Password` (StoredCredential) и SMTP-пароль (WIP) хранятся **открытым текстом** — DPAPI в TODO (R3/#3).
- **[КОД]** Аудит: неизменяемый двухфазный журнал (Attempt/Result, кто/операция/цель/было-стало) — `IAuditLog` (`EfAuditLog`/`FileAuditLog`), `AuditEntry`/`AuditPhase` (`Entities.cs`), пишется из `.Run`. **[ИЗВЕСТНО]** файловый аудит без hash-chain (R4).

## 5. Конфигурации / CI-CD / инфраструктура
- **[КОД]** Конфиг: `src/AdManager.Web/appsettings.json` + `.env` (оверлей) + env-vars (`ADMGR_AUTH`, `ADMGR_AUDIT`, `ADMGR_DEV_NOAUTH`). Опции: `AdConnectionOptions`, `ExchangeOptions`, `OperationalCredentialOptions`, `RbacOptions` (`Configuration.cs`), `UiOptions` (`Web/Options.cs`), `Templates:FilePath`/`Rbac:FilePath`/`Automation:FilePath` (Program.cs).
- **[КОД]** CI/CD: **отсутствует** — нет `.github/workflows`, нет `Dockerfile`/compose. Сборка/деплой — вручную.
- **[КОД]** Инфраструктурный код: `build/*.ps1` — `install-server.ps1` (IIS + .NET Hosting Bundle), `install-iis-site.ps1` (сайт `admanager` :8080, пул, Windows Auth; гасит пул до копирования), `install-localdb.ps1` (LocalDB для EF-аудита), `lab-setup.ps1`, `init.ps1`, `redeploy.ps1`.
- **[КОД]** Документация как «память проекта»: `docs/SKELETON.md` (карта файлов/контрактов/роутов), `docs/architecture.md`, `docs/feature-backlog.md`, `docs/handoff/` (CURRENT/TODO), `docs/adr/README.md`.
- **[КОД]** Graphify: `.claude/skills/graphify/`, `.claude/settings.json` (PreToolUse-хуки, strict), git-хуки post-commit/post-checkout, merge-driver для `graphify-out/graph.json`.

## 6. Критичные узлы (много зависимостей — по `graphify god-nodes`)
| Узел | Рёбер | Файл | Роль |
|------|------:|------|------|
| `OperationResult` | 94 | `Domain/Entities.cs` | результат ВСЕХ операций — [КОД] правка ломает почти всё |
| `TechnicianContext` | 44 | `Application/Abstractions.cs` | актор во всех операциях/аудите |
| `Permission` | 34 | `Domain/Enums/Permission.cs` | enum прав — точка RBAC |
| `AuditEntry` | 28 | `Domain/Entities.cs` | запись аудита |
| `AdManagementService` | 25 | `Application/AdManagementService.cs` | центральный фасад AD |
| `Abstractions` (ns) | 24 | `Application/Abstractions*.cs` | контракты слоя |
| `AdService` / `AdDirectory` | 23 / 23 | `Infrastructure.Ad/AdService.cs`, `AdDirectory.cs` | write/read S.DS |
| `UserTemplate` | 20 | `Application/Templates.cs` | модель шаблонов формы |
| `GpoDirectory` | 19 | `Infrastructure.Ad/GpoDirectory.cs` | чтение GPO/линков |
| `IAdService` | 18 | `Application/Abstractions.cs` | контракт AD-операций |
| `ExchangeService` / `ExchangeManagementService` | 18 / 16 | `Infrastructure.Exchange/*`, `Application/*` | Exchange |

**Вывод [КОД, структурно]:** ядро сходится на `OperationResult` + `TechnicianContext` + `Permission` + `AuditEntry` (сообщества `OperationResult`/`AuditEntry` в графе) — это несущий контур «операция + актор + право + аудит». Любая правка их сигнатур каскадит по всему решению; менять с регрессом.

## Отдельно — предположения о runtime (не подтверждено кодом)
- **[RUNTIME?]** Blazor Server держит состояние страниц (списки пользователей/групп) в памяти circuit на каждого пользователя; на больших доменах — деградация памяти/латентности (R6: сейчас грузится весь домен, клиентская пагинация).
- **[RUNTIME? / ИЗВЕСТНО]** Exchange-операции и gMSA-режим не проверялись на живом сервере/domain-joined хосте.
- **[RUNTIME?]** `/health` анонимен на Kestrel; в IIS Anonymous отключён — доступ к `/health` там зависит от настройки сайта.
- **[RUNTIME?]** Файловые сторы без межпроцессной блокировки — при IIS web-garden/нескольких воркерах возможна потеря записей (R7).
