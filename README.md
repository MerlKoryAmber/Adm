# admanager

Веб-панель управления и делегирования on-prem **Active Directory** и **Exchange 2019**.
Аналог ManageEngine ADManager Plus: **управление AD**, **делегирование**, **автоматизация** (без апрув-workflow) и **Exchange**.

## Возможности

- **AD-управление:** пользователи, группы, компьютеры, OU, контакты — создание/переименование/перемещение/удаление,
  enable/disable, сброс пароля, разблокировка, атрибуты, account-options, членство в группах, **bulk**-операции.
- **Делегирование (RBAC):** роли → scope (OU) → назначения (техник/группа); супер-админы и Domain Admins — полный доступ.
- **Automation:** планировщик (cron МСК) + правила, `BackgroundService`.
- **Exchange:** mailbox enable/disable/свойства, distribution-группы (remote PowerShell) — *код-комплит, не верифицирован без сервера*.
- Двухфазный **аудит** всех операций (attempt/result), время в МСК.

## Стек

C# / .NET 8, Blazor Server, EF Core + SQL Server (или файловые сторы для лабы).
AD — `System.DirectoryServices`; Exchange — `System.Management.Automation` (remote PS).
Вход техников — Windows Auth (Kerberos SSO). Operational identity — gMSA или хранимая УЗ.

## Архитектура

Clean/onion: `Web → Application → Domain`; `Infrastructure.*` реализуют интерфейсы `Application`.
Карта — `docs/SKELETON.md`. Решения — `docs/adr/README.md`. Состояние — `docs/handoff/CURRENT.md`.

## Сборка и запуск (dev)

```powershell
winget install Microsoft.DotNet.SDK.8   # если нет
pwsh ./build/init.ps1                     # создать AdManager.sln
dotnet build
dotnet test
# локально (без Windows Auth, файловый аудит):
$env:ADMGR_DEV_NOAUTH='1'; $env:ADMGR_AUDIT='File'
dotnet run --project src/AdManager.Web
```

Переключатели: `ADMGR_AUTH=Negotiate|IIS|None`, `ADMGR_AUDIT=Ef|File`.

## Развёртывание в IIS

`build/install-server.ps1` (IIS + ASP.NET Core Hosting Bundle), затем `build/install-iis-site.ps1` (сайт :8080, Windows Auth).

## Безопасность

Operational identity **не** Domain Admin — точечная делегация. Операции выполняются от operational identity, техник не олицетворяется. Секреты — в `.env` (не в git). Статус: **лаба, не продакшен**.

## Правила разработки

`CLAUDE.md` (22 правила + специфика). Язык проекта — русский (docs/коммиты); UI — английский.
