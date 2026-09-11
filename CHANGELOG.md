# CHANGELOG

Формат — по датам (МСК). Conventional Commits в истории git.

## 2026-09-12 — UI-редизайн раздела Users + редактор шаблонов формы

### Добавлено
- **Редактор шаблонов формы пользователя** (аналог ADManager «User Creation/Modification Templates»): страница `/templates` (список: edit/copy/delete) и Layout-редактор `/templates/edit` — Field Tray с полями по категориям, раскладка полей по вкладкам, переименование/добавление/перемещение вкладок, порядок полей (↑↓). Модель `UserTemplate`/`TemplateTab`/`FieldCatalog`, стор `IUserTemplateStore` → `FileUserTemplateStore` (`App_Data/user-templates.json`).
- **Create user** переведён в табовую форму (General/Account/Address/Telephones/Organization/Profile), как правка.
- **Шаблоны подключены к формам**: на `/users/create` и `/users/modify` — селектор «Layout template»; выбранный шаблон задаёт вкладки, набор и порядок полей (спец-контролы `__password`/`__enabled`/`__mustChange`/`__pwdNeverExpires`/`sAMAccountName`). Без выбора — дефолтная раскладка (`TemplateDefaults`). Create-форма показывает Create-шаблоны, Modify — Modify-шаблоны.

### Изменено (UI)
- Фиксированные топбар и дерево — скроллится только контент (вкладки не «уезжают»).
- **Users**: полноширинный грид всех пользователей домена вместо списка с боковой панелью; поиск, фильтры «Locked only»/«Disabled only», пагинация 50/стр, bulk-тулбар; правка — на отдельной странице `/users/modify`. Убран фильтр по OU (целевой OU остался в create и bulk-move).
- Единое имя раздела «Users» (меню = заголовок).

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
