# ADR — журнал архитектурных решений

Формат: короткие записи, дата в МСК (§20). Правки решений — новой записью, старую не переписывать.

## ADR-0001 — Стек и модель идентичности (2026-09-10 МСК)

**Контекст.** Нужна веб-панель управления и делегирования on-prem AD + Exchange 2019 на Windows, аналог ADManager Plus (management + delegation + automation, без апрув-workflow).

**Решение.**
- Язык/рантайм: **C# / .NET 8**. Причина — нативная работа с AD и хостинг PowerShell в процессе (единственный путь к Exchange).
- UI: **Blazor Server** (один язык, admin-инструмент).
- Данные: **EF Core + SQL Server**.
- Вход техников: **Windows Auth (Kerberos SSO)**.
- Operational identity: **gMSA** по умолчанию, за `IOperationalCredentialProvider`; альтернатива — хранимая УЗ (модель ADManager).
- Топология MVP: один домен, один лес.
- Архитектура: Clean/onion (`Web → Application → Domain`, инфраструктура за интерфейсами).

**Статус.** Принято владельцем (стек согласован формой-опросом 2026-09-10 МСК). Скелет реализован, ожидает приёмки кода.

**Следствия.** Exchange — только remote PS (пул runspace). `Infrastructure.Ad`/`.Exchange` — Windows-only (net8.0-windows). Апрув-workflow исключён; вместо него — Automation.

## ADR-0002 — Выбор СУБД при установке (2026-09-11 МСК)

**Контекст.** В боевом инсталляторе админ должен выбирать СУБД для панели (скорее всего MS SQL, но не только).

**Решение.**
- Доступ к данным — **EF Core с выбираемым провайдером**. `AdManagerDbContext` остаётся провайдер-агностичным; провайдер и строка подключения задаются **при установке/в конфиге** (`AddDbContext` в композиции Web).
- **Дефолт — MS SQL Server.** Другие кандидаты (напр. PostgreSQL) — за тем же `DbContext`, добавляются пакетом провайдера.
- Ограничение: **без провайдер-специфичного SQL** — только EF Core LINQ; миграции держим per-provider.

**Статус.** Принято владельцем (2026-09-11 МСК).

**Следствия.** Инсталлятор получает шаг «выбор БД» (§21: select + строка подключения). Тесты Data-слоя гонять хотя бы на MS SQL; при заявке второго провайдера — прогон на обоих.

## ADR-0003 — UI: макет ADManager Plus + палитра squid-panel (2026-09-11 МСК)

**Контекст.** Владелец предпочитает GUI как у ManageEngine ADManager Plus и цветовую гамму из репозитория `MerlKoryAmber/squid-panel` (см. память [[ui-match-admanager-plus]]).

**Решение.**
- **Макет** — как у ADManager Plus: верхние табы (Management/Reports/Delegation/Automation), левое дерево категорий (User/Group/Computer/OU/Contact Management + Logs), выбор OU сверху, плотные таблицы с чекбоксами (bulk).
- **Палитра** — тёмная navy из squid-panel: bg `#0c121c`, surface `#141c2a`, primary `#0f1b2e`, акцент золото `#c9a96e`; шрифт Inter. `color-scheme: dark` глобально (нативные контролы единообразны).
- UI на английском; §21 действует по механике (select/radio, без браузерных alert/confirm).

**Статус.** Принято владельцем (фидбэк 2026-09-11 МСК).

**Следствия.** Тема в `wwwroot/app.css` (CSS-переменные). Не плодить контролы, выбивающиеся из темы (нативные — приводить к теме).

## ADR-0004 — Провайдер аудита конфигурируем; IIS-лаба на файловом аудите (2026-09-11 МСК)

**Контекст.** LocalDB под IIS-идентичностью (профиль) нестабилен — воркер зависал.

**Решение.** `ADMGR_AUDIT=Ef|File`. Dev/локально — `Ef` (LocalDB). IIS-лаба — `File` (JSONL в `App_Data`), пул на ApplicationPoolIdentity. Прод — реальный SQL Server (ADR-0002), `Ef`.

**Статус.** Принято (инженерное решение для лабы).

**Следствия.** Для прод-хостинга в IIS ставить полноценный SQL Server (не LocalDB) и вернуть `Ef`.

## ADR-0005 — Аутентификация под IIS: IISServerDefaults (2026-09-11 МСК)

**Контекст.** Под IIS in-process Windows Auth делает IIS, не Negotiate-хендлер Kestrel.

**Решение.** `ADMGR_AUTH=Negotiate|IIS|None`. Kestrel — `Negotiate`; IIS — `IISServerDefaults` + Windows Auth на сайте (Anonymous off); `None` — только локальный smoke.

**Статус.** Принято.
