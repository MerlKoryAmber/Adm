# CURRENT — текущее состояние

Обновлено: 2026-09-13 МСК.

## Кратко
- Ветка `feature/ad-full-management` = `main` = **`326671f`**, запушены на GitHub (`MerlKoryAmber/Adm`). Рабочее дерево чистое.
- Задеплоено в IIS: `http://localhost:8080` (Windows Auth, логин `Merl.loc\merl`, пароль в `.env`). Сборка 0/0. Тесты: Domain 2/2, Integration 9/9 (live AD + RBAC + EF-аудит).
- Адаптеры — **не заглушки**: AD/GPO реальные и проверены вживую; Exchange код-комплит, **рантайм не верифицирован** (нет сервера).
- Статус: РЕАЛИЗОВАНО НО НЕ ПРИНЯТО (приёмка человеком, §19). UI-проверки делал автор (не независимый), тесты зелёные, был независимый code-review (находки R1–R8 — часть исправлена, часть в трекере).

## Как запускать/деплоить
- Dev (без auth): `ASPNETCORE_URLS=http://localhost:<port>`, `ADMGR_DEV_NOAUTH=1`, `ADMGR_AUDIT=File`, `dotnet run --no-launch-profile`. Health: `/health`.
- Deploy: `dotnet publish -c Release -o publish` → `.env` в publish → **elevated** `build/install-iis-site.ps1` (гасит пул до копирования — без зависаний).
- Навигация по коду — **Graphify** (`graphify query/explain/god-nodes`), strict-режим + git-хуки активны. Карта — `docs/architecture-map.md`, `docs/SKELETON.md`.

## Сделано в сессии 2026-09-12 (сверх ночи 11-го)
- **Матрица атрибутов пользователя ADUC — покрыта**: General; Account (UAC-флаги: PNE, must-change, reversible-enc, smartcard, not-delegated, DES-only, no-preauth, cannot-change [UAC 0x40, оговорка ниже]) + **Logon Hours** (7×24); Address + **Country** (c/co/countryCode одним селектором); Telephones + **Other… multi-valued**; Organization; Profile + **Home folder** (Local/Connect); **Member Of** + **Primary Group**. Менеджер/managedBy — пикеры по имени. **DN в UI не показываются.**
- **Form templates**: Layout-редактор (`/templates`,`/templates/edit`) — Field Tray, вкладки, per-field default/required/**auto-naming**; подключены к `/users/create` и `/users/modify` (селектор Layout template).
- **Reports** (`/reports`): реальные отчёты + фильтр/пагинация + CSV-экспорт; переключение отчётов только через левое дерево (`?r=<key>`), дубль-колонка кнопок в теле убрана.
- **Group Policy — новый пиллар** (`/gpo`): список GPO + управление линками (gPLink: link/unlink/enforce/enable), RBAC+аудит (`Permission.ManageGpoLinks`).
- **Exchange — права ящиков**: Full Access / Send As / Send on Behalf (`Permission.ManageMailboxPermissions`). Рантайм не верифицирован.
- **Меню/навигация**: контекстное левое дерево по вкладкам + активная вкладка по маршруту; единый паттерн **Modify/Create** для Groups/Computers/OU/Contacts; управление Groups/Computers — на отдельных страницах `/{groups,computers}/modify?dn=` (не инлайн). Фильтры списков унифицированы (без OU-фильтра; в Users — Locked/Disabled/Hide disabled).
- **Delegation**: выбор субъекта **по имени** с авто-резолвом SID (`IAdDirectory.GetSidAsync`, `RoleAssignment.SubjectName`); модель остаётся SID-based. Секции Roles/Scopes/Assignments переключаются по левому дереву (`?s=<section>`), показывается одна за раз (не все три сразу), дефолт — Roles.
- **Пополевые права в ролях** (модель ADManager): роль хранит `CreateUserFields`/`ModifyUserFields` (ключи FieldCatalog; пусто = все поля — обратная совместимость). UI роли — `FieldPicker` под галками CreateUser/ModifyAttributes. Enforcement UI+сервер: `RbacEngine.AllowedFieldsAsync` → `FieldPermission`; `AdManagementService` отклоняет запрещённые поля (attrs, multi-value, logon hours, primary group) с аудитом; формы Create/Modify скрывают запрещённые поля. Тесты: RbacEngineTests 10/10 (вкл. пополевые + round-trip стора).
- **Graphify** настроен для репо (скилл + strict + git-хуки + merge-driver), `docs/architecture-map.md`.

## Сделано в сессии 2026-09-13
- **Пополевые права в ролях** (Delegation): CreateUserFields/ModifyUserFields, FieldPicker, enforcement UI+сервер, 10 тестов. Подписи permission человекочитаемые + группировка/алфавит; «Modify users»/«Create user» как в Management.
- **Settings-пиллар** (новая верхняя вкладка, из `wip/settings-password-notifier` cherry-pick 4 файла + достройка): `/settings` (SMTP + политика), `/password-expiry` (список истекающих + ручной прогон). `PasswordExpiryNotifier` (BackgroundService, ежедневно RunHourMsk МСК). DI в Program.cs. Проверено на живом деплое (AD-запрос expiry отработал). SMTP-рассылка вживую не гонялась (нет тест-адресатов/relay).

## Отложено (WIP-ветки на GitHub, НЕ собираются целиком — доделать)
- `wip/inactive-report` — inactive-отчёт (частично) + экспорт членов группы + member-of viewer.
- **DPAPI для SmtpSettings.Password** — сейчас в App_Data открытым текстом (как StoredCredential); до прода.
Подробности — `docs/handoff/TODO.md`.

## Открытое / блокеры (в TODO)
- **Exchange на живом сервере** и **gMSA на domain-joined хосте** — код есть, проверка за инфраструктурой пользователя.
- EF-миграция файловых сторов (RBAC/Automation/Templates/Settings), RBAC **group-scope**, **R6** серверная LDAP-пагинация (сейчас весь домен в память), DPAPI для StoredCredential/SMTP-пароля, hash-chain для файлового аудита.
- **cannot-change-password**: пишется UAC-битом `0x40`, а в AD это ACL — эффект ограничен; корректный ACL-вариант не сделан.

## Окружение (без изменений с 11-го)
- DC `dc-01.Merl.loc` (192.168.0.175), OU `AdManagerLab` (test.user1/2, группа HelpDesk-L1). Инвентарь — `docs/lab.md`.
- .NET 8 SDK (user scope), IIS + ASP.NET Core 8 Hosting Bundle, SQL 2022 LocalDB. `uv` + `graphifyy` (для Graphify) в `~/.local/bin`.
- Секреты — только в `.env` (gitignored). `App_Data/` (файловые сторы, граф `graphify-out/`) — gitignored.
