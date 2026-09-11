# CURRENT — текущее состояние

Обновлено: 2026-09-11 МСК.

## Статус

Создан **скелет** проекта admanager. Тела адаптеров AD/Exchange/Data/Automation — заглушки (`NotImplementedException`). **Скелет собран и верифицирован:** `dotnet build` — 0 warning / 0 error; смоук-тесты — 2/2 Passed. Пакеты подняты до 8.0.31 (закрыта NU1903 в Negotiate).

## Окружение (2026-09-11 МСК)

- Машина разработки **в домене `Merl.loc`**; сессия пока под локальной УЗ `desktop-pmqne78\merlkory` (для SSO/gMSA нужен вход доменной УЗ или запуск процесса под доменной).
- **.NET 8 SDK 8.0.425** (user scope, `%LOCALAPPDATA%\Microsoft\dotnet`, в PATH). `git` есть. `winget` в системе отсутствует.
- **PowerShell 7.6.6** — портативно (`%LOCALAPPDATA%\Microsoft\powershell7`).
- **IIS** включён (роль + Windows Authentication, ISAPI, mgmt console), W3SVC Running. **ASP.NET Core 8 Hosting Bundle** — ANCM v2 + runtime 8.0.31.
- **SQL Server 2022 LocalDB** (`MSSQLLocalDB`, v16.0.1000.6) — подключение проверено (`(localdb)\MSSQLLocalDB`).
- Скрипты установки: `build/install-server.ps1` (IIS+Hosting), `build/install-localdb.ps1` (LocalDB). UAC на машине выключен.
- Для лабы разрешено ходить под мощной доменной УЗ; пароль — только в `.env` (не в git).

## Лаба развёрнута (2026-09-11 МСК)

- DC `dc-01.Merl.loc` (192.168.0.175), связь и креды `merl` проверены. Инвентарь — [docs/lab.md](../lab.md).
- Создано `OU=AdManagerLab` (Users/Groups/ServiceAccounts) + `test.user1`, `test.user2` (enabled), группа `HelpDesk-L1`. Скрипт `build/lab-setup.ps1` (идемпотентный).
- **Проверено вживую:** сброс пароля через `System.DirectoryServices` + `AuthenticationTypes.Secure` работает без LDAPS → Фаза 0 (AD) разблокирована.

## Решение по языку (2026-09-11 МСК)

UI приложения — **английский** + полная поддержка Unicode (кириллические УЗ). Чат/docs/коммиты — русский. Зафиксировано в `CLAUDE.md` (специфика).

## Итог за ночь 2026-09-11 МСК

**Развёрнуто и работает в IIS** (`http://localhost:8080`, Windows Auth; логин `Merl.loc\merl`, пароль в `.env`). Тесты 11/11, build 0/0.

Пиллары:
- **AD-управление** (полный набор ADManager): users/groups/computers/OUs/contacts — create/rename/move/delete/enable-disable/attrs/account-options/membership + bulk. Live-проверено (5 интеграционных тестов + браузер).
- **Делегирование (RBAC)** — реальный движок (роли→scope→назначения, супер-админы, Domain Admins bypass), UI `/delegation`, стор `FileRbacStore`. 4 юнит-теста.
- **Automation** — планировщик (BackgroundService + NCrontab, cron МСК), UI `/automation`, задача DisableInactiveUsers, стор `FileAutomationStore`.
- **Exchange** — код-комплит (remote PS), UI `/exchange`. **Не верифицирован** (нет сервера) — см. TODO.

UI: макет ADManager Plus, тёмная тема squid-panel (ADR-0003), `color-scheme: dark`.

Тулчейн на машине: .NET 8 SDK, PS7, IIS+Hosting Bundle, SQL 2022 LocalDB.

Открытое — `docs/handoff/TODO.md` (Exchange-верификация, EF-сторы, group-scope, reports, bulk-CSV, gMSA, DPAPI).

Статус пилларов: РЕАЛИЗОВАНО НО НЕ ПРИНЯТО (приёмка — человеком, §19).

## Сделано

- `CLAUDE.md` (22 правила + специфика admanager).
- Docs: `SKELETON.md`, `architecture.md`, `operations-model.md`, `adr/README.md`, этот файл.
- Слои: Domain (сущности, `Permission`), Application (интерфейсы-контракты), Infrastructure.{Ad,Exchange,Data,Automation} (заглушки), Web (Program.cs, health), Domain.Tests (smoke).
- `Directory.Build.props`, `.editorconfig`, `.gitignore`, `build/init.ps1`.

## Не сделано / следующее

1. ~~SDK + сборка скелета~~ — сделано (2026-09-11 МСК).
2. ~~**Фаза 0 (AD-срез):** реальный `ResetPassword` + аудит, live-проверка~~ — **сделано, ревью (§4) пройдено, major-замечания закрыты/занесены в трекер**. Статус: РЕАЛИЗОВАНО НО НЕ ПРИНЯТО (ждёт приёмки человеком, §19). Отчёт — `docs/agent_reports/phase0/REPORT.md`; хвосты — `docs/handoff/TODO.md`.
3. Согласовать список `Permission` (операции MVP) — можно параллельно.
4. SQL LocalDB для аудита/данных (к моменту, когда пишем в БД).
5. Blazor-компоненты (App/Routes/страницы), EF-миграции, DI-регистрации в Web.

## Заметки

- Git-репозиторий ещё не инициализирован.
- Ветвление/коммиты/push — по §6, по команде человека.
