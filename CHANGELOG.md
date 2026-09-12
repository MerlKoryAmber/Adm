# CHANGELOG

Формат — по датам (МСК). Conventional Commits в истории git.

## 2026-09-12 — UI-редизайн раздела Users + редактор шаблонов формы

### Добавлено
- **Контекстное левое меню по вкладкам**: активная верхняя вкладка определяется по маршруту; у каждой вкладки своё дерево — Management (объекты), Reports (список отчётов + Audit log), Delegation (Roles/Scopes/Assignments), Automation (Policies). Раньше дерево Management висело на всех вкладках, а «Management» всегда была active.
- **Delegation — выбор субъекта по имени** вместо ручного ввода SID: поиск пользователя/группы → авто-резолв SID и типа (Technician/AdGroup); в назначении сохраняется читаемое имя (`RoleAssignment.SubjectName`). Модель по-прежнему SID-based (`IAdDirectory.GetSidAsync`). Отчёты открываются по `?r=<key>` из бокового дерева.
- **Единая структура меню для всех разделов объектов**: как в Users, теперь у Groups/Computers/OU/Contacts — отдельные пункты «Modify …» (грид+управление) и «Create …» (отдельная страница). Формы создания вынесены на `/groups/create`, `/computers/create`, `/ous/create`, `/contacts/create`. Пункт «Users» переименован в «Modify users».
- **Консистентность фильтров списков**: убран OU-фильтр из Groups/Computers/Contacts/Exchange (как в Users — список по всему домену + поиск). В Users добавлена галка «Hide disabled» (рядом с Locked only / Disabled only).
- **Country / Home folder / Other… multi-valued** (batch 4, завершает матрицу ADUC): единый селектор Country/region (пишет c/co/countryCode связанно, `Countries`); Home folder с ADUC-логикой Local/Connect(drive+UNC); списковые редакторы «Other…» (otherTelephone/otherHomePhone/otherPager/otherMobile/otherFax/otherIpPhone/url) на общем multi-value движке (`IAdDirectory.GetMultiValueAsync` / `AdService.SetMultiValueAsync`). Все — в каталоге шаблонов. Round-trip проверен на живом AD.
- **Logon Hours** (batch 3): редактор-сетка 7×24 (`logonHours`, 21 байт, UTC) на вкладке Account — allow/deny по часам, Allow all/Deny all/Save/Clear. `IAdDirectory.GetLogonHoursAsync` + `AdService.SetLogonHoursAsync`; поле `__logonHours` в каталоге шаблонов. Round-trip проверен на живом AD (UTC; ADUC показывает со сдвигом в локальное время).
- **Member Of на карточке пользователя** (batch 2): вкладка со списком групп (`memberOf`, читаемые имена), add/remove через group.member, **Set Primary Group** (`primaryGroupID` по RID). Новый read `IAdDirectory.ListUserGroupsAsync`, `AdService.SetPrimaryGroupAsync`; категория «Member Of» в каталоге шаблонов.
- **Убраны DN из UI карточки пользователя**: менеджер — пикер по имени (поиск), группы — по имени (CN), подзаголовок — displayName/sam + контейнер. DN больше не показываются/не вводятся вручную.
- **Account (UAC-флаги ADUC)** на карточке пользователя: reversible encryption, smart card required, sensitive/not-delegated, DES-only, no-preauth, cannot-change-password — чекбоксы с чтением из `userAccountControl` и записью через `SetAccountOptions` (`AccountOptions` расширен). Прим.: cannot-change-password пишется UAC-битом 0x40 (в AD реально — ACL; ACL-вариант отдельным шагом). Первый батч закрытия матрицы атрибутов ADUC (см. `docs/architecture-map.md`).
- **Group Policy — новый пиллар** (`/gpo`): чтение GPO (groupPolicyContainer: имя, версия, статус user/computer settings), управление линками через `gPLink` по scope (домен/OU) — link/unlink/enforce/enable, счётчик линков и отметка Unlinked. Без GPMC, чистый LDAP. Контракты `IGpoDirectory`/`IGpoService`, `GpoManagementService` (RBAC+аудит, `Permission.ManageGpoLinks`). Редактирование самих настроек GPO — вне объёма. Backlog фич — `docs/feature-backlog.md`.
- **Reports** — реальные отчёты вместо заглушки (All/Disabled/Locked-out/Without email/Without manager/Password never expires) + фильтр/пагинация + CSV-экспорт (JS-interop, RFC 4180).
- **Exchange — права доступа к ящикам**: Full Access (`Add/Remove-MailboxPermission`), Send As (`Add/Remove-ADPermission "Send As"`), Send on Behalf (`Set-Mailbox -GrantSendOnBehalfTo`). Секция Permissions в панели ящика. `Permission.ManageMailboxPermissions`, RBAC+аудит. **Рантайм не верифицирован** (нет сервера).
- **Редактор шаблонов формы пользователя** (аналог ADManager «User Creation/Modification Templates»): страница `/templates` (список: edit/copy/delete) и Layout-редактор `/templates/edit` — Field Tray с полями по категориям, раскладка полей по вкладкам, переименование/добавление/перемещение вкладок, порядок полей (↑↓). Модель `UserTemplate`/`TemplateTab`/`FieldCatalog`, стор `IUserTemplateStore` → `FileUserTemplateStore` (`App_Data/user-templates.json`).
- **Create user** переведён в табовую форму (General/Account/Address/Telephones/Organization/Profile), как правка.
- **Шаблоны подключены к формам**: на `/users/create` и `/users/modify` — селектор «Layout template»; выбранный шаблон задаёт вкладки, набор и порядок полей (спец-контролы `__password`/`__enabled`/`__mustChange`/`__pwdNeverExpires`/`sAMAccountName`). Без выбора — дефолтная раскладка (`TemplateDefaults`). Create-форма показывает Create-шаблоны, Modify — Modify-шаблоны.
- **Шаблоны — prefill/именование/обязательные поля**: per-field default value, авто-именование logon/UPN/display (`NamingRules`: First+Last, first.last, f+Last и т.д. — live на форме create), пометка required + валидация. Каталог полей расширен (Organization: employeeNumber/Type, division; Exchange: proxyAddresses, mailNickname, targetAddress; Profile: userWorkstations).

### Изменено (UI)
- Фиксированные топбар и дерево — скроллится только контент (вкладки не «уезжают»).
- **Users**: полноширинный грид всех пользователей домена вместо списка с боковой панелью; поиск, фильтры «Locked only»/«Disabled only», пагинация 50/стр, bulk-тулбар; правка — на отдельной странице `/users/modify`. Убран фильтр по OU (целевой OU остался в create и bulk-move).
- Единое имя раздела «Users» (меню = заголовок).
- **Groups/Computers/Contacts/Exchange** приведены к тому же полноширинному гриду, что и Users: поиск, пагинация (50/стр), чекбоксы + bulk-тулбар, per-row «Manage ▸/Mailbox ▸», форма создания и панель управления — под гридом. Все прежние операции сохранены.

## 2026-09-11 — начальный импорт

### Добавлено
- Каркас .NET 8 (Clean/onion): Domain, Application, Infrastructure.{Ad,Exchange,Data,Automation}, Web (Blazor Server), тесты.
- **AD-управление** (набор ManageEngine ADManager Plus): пользователи/группы/компьютеры/OU/контакты —
  create/rename/move/delete/enable-disable/атрибуты/account-options/членство + bulk-операции. Реализация на `System.DirectoryServices`.
- **Делегирование (RBAC):** роли (наборы операций) → scope (OU) → назначения (SID техника/группы); супер-админы и Domain Admins bypass. UI `/delegation`.
- **Automation:** планировщик `BackgroundService` + NCrontab (cron МСК), задача DisableInactiveUsers. UI `/automation`.
- **Exchange** (on-prem 2019) через remote PowerShell: mailbox enable/disable/props, distribution-группы. **Рантайм не верифицирован** (нет сервера).
- Двухфазный аудит (attempt/result), провайдеры: EF (LocalDB/SQL) и файловый (JSONL).
- Аутентификация техников — Windows Auth (Negotiate / IIS). Operational identity — gMSA или хранимая УЗ.
- UI: макет ADManager Plus, тёмная тема (палитра squid-panel), английские подписи, Unicode.
- Скрипты установки/деплоя (`build/`), развёртывание в IIS (:8080).

### Известные ограничения
- Exchange не проверен на живом сервере; сторы RBAC/Automation — файловые (не EF); group-scope в RBAC не реализован; отчёты и bulk-CSV — нет. См. `docs/handoff/TODO.md`.
