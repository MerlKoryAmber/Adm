# CURRENT — текущее состояние

Обновлено: 2026-09-27 МСК.

## Сделано в сессии 2026-09-27 (Exchange + OU-дерево + Settings)
- **OU-дерево вместо плоских select** во всех формах Create (user/group/computer/contact/OU/bulk) и Move (Users bulk, Group/Computer/OU modify): новый переиспользуемый `Components/Shared/OuPicker.razor` — компактное выпадающее дерево из `IOuTreeProvider` (кэш + фильтр системных контейнеров). CSS `.oupick*` в app.css. `Indent()`/`ous` в этих формах местами остались неиспользуемыми (варнинги, не ошибки).
- **PFX — загрузка файлом** (Settings): `InputFile` («Choose PFX file…», кнопка в стиле панели `.uploadbtn`) вместо ручного ввода пути; файл сохраняется в `App_Data/certs`, путь подставляется сам. Лимит 512 КБ, только .pfx/.p12.
- **Settings → раздел Administration** (левое меню): верхняя вкладка Settings убрана; Administration = Panel users (ManageDelegation) + Settings-подменю (Email & HTTPS / Exchange cache / Operational identity / Secret encryption / Audit logs), якоря `#…`. `MainLayout`: Section `settings`→Administration, `CanAdministration`/`AdministrationHome`.
- **Вкладка Exchange в Create/Modify user** (`Components/Shared/MailboxTab.razor`):
  - Create: галка **Create mailbox** над полями (иначе раздел игнорируется) + выбор mailbox database + **персональный архив** с выбором базы. После создания юзера → `EnableMailboxAsync(MailboxProvisioning)`.
  - Modify: email-адреса (proxyAddresses, primary), делегация (Full Access/Send As/Send on Behalf), скрытие из GAL, переадресация, **мобильные устройства** (refresh/wipe/remove). Блоки — по правам роли.
  - Показ вкладки: `UserTemplate.IncludeMailbox` (тумблер в редакторе шаблонов) + право актора.
- **Права ролей**: новая группа «Exchange — Mailbox» (ManageMailboxPermissions=делегация, EmailAddresses, Forwarding, AddressBook, Mobile).
- **Кэш Exchange**: `IExchangeDataCache`/`ExchangeDataCache` (Web singleton) + `ExchangeDataRefresher` (BackgroundService) — список баз, интервал из `AppSettings.ExchangeCache.DatabaseListRefreshHours` (24ч; mailbox-атрибуты — 6ч). В Settings — интервалы + «Force refresh now».
- **Delete/Disable mailbox** — кнопки временно убраны (Exchange.razor, bulk + single), методы оставлены в коде.
- Задеплоено на IIS `http://localhost:8080`, проверено вживую (OU-дерево, вкладка Exchange, Settings в Administration, PFX-кнопка, Exchange cache). Exchange-операции рантайм не верифицированы (нет сервера). Сборка 0 ошибок. **Не закоммичено** на момент записи.

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

## Сделано в сессии 2026-09-26
- **UI Delegation → New role: группы прав вынесены во вкладки** (Users/Groups/Computers/OU/Contacts/Objects/Exchange/Group Policy/Administration), по одной колонке на активную вкладку + бейдж-счётчик выбранных. Убрана простыня и перекрытие с левым деревом. Галка Super admin вынесена под Name (одна, не на каждой вкладке); при её включении блок прав скрывается. Стиль `.subtabs/.subtab` в app.css. Проверено на IIS.
- **ADR-0007 — локальные УЗ панели + смешанная аутентификация.**
  - **Сущность `LocalUser`** (Application/LocalUsers.cs): UserName, PasswordHash, DisplayName, Disabled, IsSuperAdmin, синтетический SID `LOCAL:<guid>`. Интерфейсы `ILocalUserStore`, `ILocalAuthService`.
  - **`EfLocalUserStore`** (документ `localusers` в AppState) + **`LocalAuthService`** (PBKDF2 Rfc2898, SHA-256, 100k, формат `pbkdf2:v1:iter:salt:hash`; verify FixedTimeEquals; сид admin/admin как супер-админ при пустой таблице).
  - **Смешанный вход**: Windows Auth (домен SSO) + cookie-схема `AdmgrCookie`. `/login` (Login.razor, LoginLayout, AllowAnonymous, статический POST) → `/auth/login` (endpoint: Validate + SignIn cookie) → `/logout`. Endpoint отделён от страницы (`/auth/login`, не `/login`) — иначе AmbiguousMatch с `@page`. FallbackPolicy принимает ОБЕ схемы (winScheme + cookie) — иначе cookie-принципал не виден. DefaultChallengeScheme=cookie → неаутентифицированный уходит на форму.
  - **RBAC**: `TechnicianContext.IsSuperAdmin` (новый флаг), локальный вход кладёт claim `admgr:super`; `RbacEngine.IsSuperAdmin(actor)` учитывает флаг — один метод покрыл все 4 вызова. Локальный супер-админ = полный доступ; локальный без назначений = только Dashboard (проверено).
  - **Вкладка Administration** (`/administration`, Permission.ManageDelegation) → Panel users: список локальных УЗ (Disable/Reset password/Delete + inline-подтверждение), создание локальной УЗ (галка Super admin), список доменных субъектов с назначениями. Logout-кнопка в topright MainLayout.
  - **IIS**: install-скрипт теперь ставит Anonymous+Windows ОБЕ (смешанный вход; `/login` нужен анонимный доступ). **ВРЕМЕННО** — финально будет exe-инсталлятор (ставит IIS/БД/keyring); логика «сайт: Anonymous+Windows» переедет туда.
  - **Целевое (в бэклог)**: единая форма логин/пароль с выбором Local/Domain внизу (сейчас домен — через Windows SSO, локальные — через форму).
  - ADR-0007 в docs/adr/README.md. Тесты LocalAuth 6/6 (хэш/verify/case-insensitive/disabled/сид admin/SID). Полный прогон: Domain 2/2, Integration 35/35. Сборка 0/0.
  - **Верифицировано на IIS**: сид admin в БД (PBKDF2, не plaintext); вход admin/admin через форму → Dashboard, все вкладки, who=admin; создание operator1 → вход под ним → только Dashboard (RBAC для локальных работает); logout.
- **Статус ADR-0007**: РЕАЛИЗОВАНО НО НЕ ПРИНЯТО (§19).

## Сделано в сессии 2026-09-25
- **ADR-0006 — хвосты закрыты (шифрование секретов + бэкап/DR + сидер).**
  - **Envelope-шифрование секретов**: `ISecretProtector`/`IKeyring` (Application), `DpapiKeyring` + `EnvelopeSecretProtector` (AES-256-GCM, `Infrastructure.Data/SecretProtection.cs`). Ключ — файл `key.dat` **вне БД**, дефолт `C:\ProgramData\AdManager\keyring\` (переопр. `ADMGR_KEYRING`), сам файл под DPAPI machine-scope. Токен `enc:v1:base64(nonce|tag|cipher)`; Unprotect понимает legacy-plain (обратная совместимость миграции).
  - **Декоратор `EncryptedSettingsStore`** поверх Ef/File settings-стора: шифрует `Smtp.Password`, `Https.PfxPassword`, `OperationalCredential.Password` на Save, расшифровывает на Load. Одна точка на оба стора; не мутирует объект вызывающего.
  - **Op-identity (StoredCredential) перенесён в БД**: `AppSettings.OperationalCredential` (`StoredCredentialSettings`), `ConfiguredCredentialProvider` читает из settings-стор, `.env` — bootstrap-fallback. UI в `/settings` (Mode gMSA|StoredCredential + поля).
  - **UI**: панель Operational identity + панель Secret encryption (Key fingerprint + кнопка Rotate с inline-подтверждением как в Groups; ротация читает старым→Rotate→пишет новым = пере-шифровка). Устаревшие подписи «App_Data/DPAPI TODO» → «encrypted (envelope)».
  - **Сидер `StateSeeder`**: при Ef-режиме и пустом целевом сторе + непустом `App_Data/*.json` — одноразовый импорт (RBAC/Automation/Templates/Settings), идемпотентно; Settings через декоратор (секреты шифруются при импорте). Вызов в Program.cs после EnsureCreated.
  - **Доки**: `docs/backup-dr.md` — бэкап БД + отдельный бэкап ключа, restore (тот же хост / перенос), ротация, потеря ключа.
  - Тесты: `SecretProtectionTests` 11/11 (round-trip, разный шифротекст, legacy passthrough, идемпотентность, null/empty, персист ключа, ротация+пере-шифровка, декоратор). Полный прогон: Domain 2/2, Integration 29/29. Сборка 0/0.
  - **Инфра**: переустановлен **.NET 8 SDK 8.0.425** в `C:\Program Files\dotnet` (был только runtime; SDK пропал с прошлой сессии). Собирать через `"C:\Program Files\dotnet\dotnet.exe"`.
- **Верифицировано на живом IIS-деплое** (localhost:8080, ApplicationPoolIdentity):
  - Keyring создан в `C:\ProgramData\AdManager\keyring\key.dat` (262 б) под пулом. ACL на keyring-папку теперь ставит сам `build/install-iis-site.ps1` (шаг 3a, `IIS_IUSRS:(OI)(CI)M`). Отпечаток в UI: `2828487BF3CC9368`.
  - SMTP-пароль сохранён через UI → в SQL `AppState[settings].Json` лежит `enc:v1:...` (plaintext `SuperSecret123!` в БД ОТСУТСТВУЕТ), при Load расшифровывается обратно.
  - Ротация ключа через UI (inline-подтверждение): отпечаток сменился `2828487BF3CC9368`→`945F18BA520EFFFB`, токен секрета в БД пере-шифрован (новый ≠ старый), plaintext нет, форма читается.
- **Статус ADR-0006**: РЕАЛИЗОВАНО НО НЕ ПРИНЯТО (приёмка человеком, §19). Осталось проверить вживую: op-identity StoredCredential из БД (реальные операции под хранимой УЗ), сидер миграции (на стенде с непустыми App_Data).

## Сделано в сессии 2026-09-24
- **Всё состояние → SQL (ADR-0006)**: установлен **SQL Server 2025 Express** (служба, `build/install-sqlexpress.ps1`, БД `AdManager`, грант `IIS APPPOOL\admanager`). Все 4 стора (RBAC/Settings/Automation/Templates) + аудит — в БД: `AdManagerDbContext.AppState` (JSON-документы) + `EfAppStateStore<T>` через `IDbContextFactory`; EF-реализации `EfRbacStore`/`EfSettingsStore`/`EfAutomationStore`/`EfUserTemplateStore`. Переключатель `ADMGR_STORE=Ef|File` (дефолт Ef), IIS ставит `ADMGR_AUDIT=Ef` + `ADMGR_STORE=Ef`. Connection string → `.\SQLEXPRESS`. Проверено: роль пишется в SQL (таблица AppState). **Осталось по ADR-0006**: шифрование секретов (AES-ключ вне БД), бэкап/DR-процедура, сидер миграции с файлов.
- **Super-admin через роль**: `HelpDeskRole.IsSuperAdmin` (галка в UI, бейдж, `RbacEngine.HasSuperAdminRole`). Bootstrap `RbacOptions.SuperAdmins` = merl + MerlKory (только когда БД пуста). Тесты RbacEngine 13/13.
- **RBAC-фильтрация UI по правам**: техник видит только делегированные разделы. `RbacEngine.EffectiveAccessAsync` → `EffectiveAccess`; меню (`MainLayout`, грузится в OnAfterRender — AuthState на пререндере пуст) и все чувствительные страницы (`RequireAccess`-гард, 25 страниц) фильтруются. Новые Permission на разделы (ViewReports/ManageDelegation/ManageAutomation/ManageSettings/ManagePasswordExpiry, группа Administration). Админ-разделы теперь делегируются ролью, не хардкодом. Bootstrap-супер-админы: merl + **MerlKory** (локальный админ стенда). Тесты RbacEngine 12/12.
- **Отключено браузерное кеширование** (middleware no-cache на все ответы).
- **Delegation: убрано пустое боковое меню на Dashboard.**
- **Пополевой темплейт в assignment**: `RoleAssignment.CreateTemplateId`/`ModifyTemplateId` — принудительный Form-template (Create/Modify) из наших шаблонов. UI: два селектора в New assignment + колонка Templates в таблице. Enforcement: `RbacEngine.EnforcedTemplateAsync`; формы Create/Modify лочат селектор шаблона на заданный (техник не переключает). Тесты RbacEngine 11/11. Дашборд без бокового меню.

## Сделано в сессии 2026-09-13
- **Пополевые права в ролях** (Delegation): CreateUserFields/ModifyUserFields, FieldPicker, enforcement UI+сервер, 10 тестов. Подписи permission человекочитаемые + группировка/алфавит; «Modify users»/«Create user» как в Management.
- **Settings-пиллар** (из `wip/settings-password-notifier` cherry-pick 4 файла + достройка): `/settings` (SMTP + **HTTPS/сертификаты**: RequireHttps/HSTS/источник IIS|PFX|Store/порт). `PasswordExpiryNotifier` (BackgroundService, ежедневно RunHourMsk МСК). DI в Program.cs.
- **Password Expiry** — вынесен в **отдельную верхнюю вкладку** `/password-expiry`: политика + **триггеры** (`ExpiryTrigger[]`, несколько порогов дней, свой Subject/Body на каждый) + preview-список + ручной прогон. Проверено вживую (AD expiry-запрос ок). SMTP-рассылка вживую не гонялась (нет relay).
- **Dashboard** (`/`, `/dashboard`) — новая стартовая вкладка по образцу ADManager: KPI-плитки (users/disabled/locked/expiring≤7d/PNE/without email/groups/computers/OU, кликабельны на Reports/страницы) + истекающие пароли + недавний аудит. Home.razor удалён (заменён дашбордом).
- **UX-аудит**: выровнены заголовки h1 под пункты меню (Modify users/groups/computers/OUs/contacts — раньше были «X Management»/«Users»). Убраны показы DN из UI (правило «DN не показываются»): колонка/деталь на /ous, деталь на /exchange (→ sAMAccountName).

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
