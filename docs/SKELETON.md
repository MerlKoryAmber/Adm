# SKELETON.md — структурная карта admanager

Обновлено: 2026-09-12 МСК. Читать перед задачей, чинить перед push (§22).

Веб-панель управления и делегирования on-prem AD + Exchange 2019. Аналог ManageEngine ADManager Plus.
UI — английский, тёмная тема (палитра squid-panel), макет ADManager Plus (см. ADR-0003).

## Проекты и зависимости

```
Web ─► Application ─► Domain
Infrastructure.{Ad,Exchange,Data,Automation} ─► Application (реализуют интерфейсы)
Web ─► Infrastructure.* (DI-композиция)
```

| Проект | TFM | Роль |
|--------|-----|------|
| `AdManager.Domain` | net8.0 | Сущности, enum (`Permission`, `AuditPhase`, `SubjectType`, `HelpDeskRole`, `DelegationScope`, `RoleAssignment`, `AuditEntry`, `OperationResult`). |
| `AdManager.Application` | net8.0 | Контракты + DTO + сервисы приложения (см. ниже). |
| `AdManager.Infrastructure.Ad` | net8.0-windows | `AdService` (write-операции S.DS), `AdDirectory` (чтение), `Ldap` (bind). |
| `AdManager.Infrastructure.Exchange` | net8.0-windows | `ExchangeService` (remote PowerShell). **Рантайм не верифицирован** (нет сервера). |
| `AdManager.Infrastructure.Data` | net8.0 | EF `AdManagerDbContext` + `EfAuditLog`; файловые сторы `FileAuditLog`, `FileRbacStore`, `FileAutomationStore`, `FileUserTemplateStore`. |
| `AdManager.Infrastructure.Automation` | net8.0 | `AutomationScheduler` (BackgroundService + NCrontab). |
| `AdManager.Web` | net8.0-windows | Blazor Server, Windows Auth, DI, страницы. |
| `tests/AdManager.Domain.Tests` | net8.0 | xUnit (smoke). |
| `tests/AdManager.Integration.Tests` | net8.0-windows | Live AD (лаба) + EF-аудит + RBAC-движок. |

## Контракты (Application)

- `IAdService` — write-операции AD: reset password, unlock, enable/disable, create/delete user, attrs, move, rename, group membership, account options, create group/computer/contact/OU.
- `IAdDirectory` — чтение: users/groups/computers/contacts/OU, all-OUs, объект по атрибутам, члены группы.
- `IExchangeService` — mailbox enable/disable/props, distribution create/membership.
- `IRbacEngine` (+ `RbacEngine`, `AllowAllRbacEngine`) — авторизация операции.
- `IRbacStore` (+ `FileRbacStore`) — роли/scope/назначения.
- `IAuditLog` (+ `EfAuditLog`, `FileAuditLog`) — неизменяемый аудит (двухфазный).
- `IOperationalCredentialProvider` (+ `ConfiguredCredentialProvider`) — gMSA / StoredCredential.
- `IAutomationScheduler` (+ `AutomationScheduler`), `IAutomationStore` (+ `FileAutomationStore`).
- `IUserTemplateStore` (+ `FileUserTemplateStore`) — шаблоны формы пользователя (Layout View). Модель: `UserTemplate` (Name/Kind/Description/`Tabs`), `TemplateTab` (Title + список ключей полей), каталог `FieldCatalog` (`FieldDef` Key/Label/Category). `Templates.cs`.

Сервисы-обёртки (RBAC + двухфазный аудит вокруг каждой операции):
- `AdManagementService` — все AD-операции (актор-aware).
- `ExchangeManagementService` — все Exchange-операции.

## Страницы (Web/Components/Pages)

| Route | Назначение |
|-------|------------|
| `/` Home | обзор. |
| `/users` | Users: полноширинный грид всех пользователей домена, поиск + фильтры Locked/Disabled only + пагинация (50/стр), **bulk** (enable/disable/unlock/reset pwd/move/delete), «Modify ▸» на строке. |
| `/users/modify` | правка атрибутов (табы) + account options; **селектор Layout template** (Modify-шаблоны), иначе `TemplateDefaults.Modify()`. |
| `/users/create` | создание пользователя, табовая форма + целевой OU; **селектор Layout template** (Create-шаблоны), иначе `TemplateDefaults.Create()`. |
| `/users/bulk` | массовое создание из CSV. |
| `/templates`, `/templates/edit` | **Form templates**: список (edit/copy/delete) + Layout-редактор (Field Tray → вкладки, переименование/добавление/перемещение вкладок, ↑↓ полей). Store — `App_Data/user-templates.json`. |
| `/search` | Advanced search по атрибутам. |
| `/groups` | Group Management: create group, membership, rename/move/delete. |
| `/computers` | Computer Management: create, enable/disable, rename/move/delete. |
| `/ous` | OU Management: create/rename/move/delete. |
| `/contacts` | Contact Management: create/delete. |
| `/exchange` | Mailboxes (enable/disable/props) + distribution groups. |
| `/delegation` | Roles / Scopes / Assignments (реальный RBAC). |
| `/automation` | Автоматизации (cron МСК, create/enable/trigger/delete). |
| `/audit` | Журнал операций (МСК, attempt+result). |
| `/reports` | заглушка. |

## Хостинг / конфигурация

- Blazor Server, `MapRazorComponents<App>().AddInteractiveServerRenderMode()`, `UseAntiforgery`.
- Auth: `ADMGR_AUTH=Negotiate|IIS|None` (ADR-0005). Аудит: `ADMGR_AUDIT=Ef|File` (ADR-0004).
- Секреты лабы — `.env` (оверлей в конфиг через `DotEnv`), не в git.
- IIS-лаба: сайт `admanager` :8080, ApplicationPoolIdentity, Windows Auth, `ADMGR_AUTH=IIS`, `ADMGR_AUDIT=File` (`App_Data/`).
- Скрипты: `build/init.ps1`, `build/lab-setup.ps1`, `build/install-server.ps1` (IIS+Hosting), `build/install-localdb.ps1`, `build/install-iis-site.ps1`.

## Не сделано / открытые хвосты

См. `docs/handoff/TODO.md`: RBAC/Automation в EF (сейчас файлы); group-scope в RBAC; пул runspace Exchange + верификация на живом сервере; gMSA; отчёты; bulk-CSV; DPAPI для StoredCredential.
