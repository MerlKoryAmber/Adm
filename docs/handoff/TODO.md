# TODO / трекер хвостов

Обновлено: 2026-09-13 МСК. Заводится по §3 (≥2 пунктов — в трекер) и по итогам ревью.

## Хвосты Фазы 0 (из независимого ревью)

| # | Пункт | Серьёзность | Статус | Куда |
|---|-------|-------------|--------|------|
| 1 | Тест давал ложный «зелёный» без `.env` (skip как pass) | major | **исправлено** (SkippableFact) | — |
| 2 | Окно неаудируемого изменения (аудит только post-op) | major | **исправлено** (двухфазный аудит Attempt/Result) | — |
| 3 | `StoredCredential.Password` — открытый текст | major | открыт | DPAPI/DPAPI-NG при хранении; **до прода** |
| 4 | Файловый аудит не защищён от правки (нет hash-chain) | minor | открыт | закрывается EF/SQL-аудитом + цепочка хэшей |
| 5 | `CancellationToken` не учитывается в IO/`Invoke` | minor | открыт | при доработке AD/Exchange-адаптеров |
| 6 | `Before/After` в аудите не заполняются | minor | открыт | зафиксировать `pwdLastSet`/mustChange, где осмысленно |
| 7 | `QueryAsync` мог упасть на битой строке | minor | **исправлено** (пропуск битых строк) | — |
| 8 | Live-тест не восстанавливает пароль `test.user1` | minor | принято как есть | отмечено в docs/lab.md |
| 9 | DN/Server в LDAP-пути без валидации | minor | открыт | при появлении внешнего ввода — валидация/экранирование |

## Хвосты из ревью сессии 2026-09-12 (шаблоны/гриды/Reports)

| # | Пункт | Серьёзность | Статус |
|---|-------|-------------|--------|
| R1 | Modify: тихая перезапись нетронутых атрибутов (схлопывание multi-valued) | major | **исправлено** (dirty-tracking, пишем только изменённое) |
| R2 | Create: account-options по неэкранированному собранному DN (mustChange/PNE молча не ставились) | major | **исправлено** (DN ищется по sAMAccountName; ошибка follow-up видна) |
| R3 | `proxyAddresses` (multi-valued) в текстовой раскладке схлопывался | major | **исправлено** (убран из FieldCatalog) |
| R4 | Create: enabled без пароля → AD отклоняет | minor | **исправлено** (создаём disabled + предупреждение) |
| R5 | ModifyUser.OnInitialized без try/catch | minor | **исправлено** |
| R6 | Весь домен грузится в память на /users,/groups,/computers,/contacts,/exchange,/reports; дропдауны «Add member» рендерят всех юзеров | major | **открыт** — серверная LDAP-пагинация + autocomplete вместо клиентской |
| R7 | Файловые сторы: запись без temp+rename, нет межпроцессной блокировки (web-garden) | minor | открыт — закрывается EF/SQL-миграцией |
| R8 | Modify без оптимистичной блокировки (два техника затирают друг друга) | minor | открыт |

## Отложено — WIP-ветки на GitHub (не собираются, доделать)

| Задача | Статус | Ветка | Готово / Осталось |
|--------|--------|-------|--------------------|
| **Settings-пиллар + напоминатель истечения пароля по почте** | **СДЕЛАНО** (сессия 2026-09-13) | merged в `feature/ad-full-management` | Cherry-pick 4 файлов из wip + дописаны `PasswordExpiryNotifier`, `Settings.razor`, `PasswordExpiry.razor`, табы, DI. Задеплоено. Осталось до прода: DPAPI для SMTP-пароля; SMTP-рассылка вживую не верифицирована (нет relay/адресатов). Ветку `wip/settings-password-notifier` можно удалить (устарела). |
| **AD quick-wins: inactive-отчёт / экспорт членов группы / member-of** | ОТЛОЖЕНО | `wip/inactive-report` | Готово частично: метод `IAdDirectory`+`AdDirectory` для `lastLogonTimestamp`, начало отчёта в `Reports.razor`. Осталось: доделать отчёт (ReportDef/catalog/CSV), + Export members CSV на `Groups.razor`, + «Member of» на `ModifyUser.razor`. **Не завершено/не собрано.** |

Backlog-фичи целиком — в `docs/feature-backlog.md`.

## UI (по фидбэку)

- Приблизить GUI к ManageEngine ADManager Plus (сделано: топ-табы, левое дерево, bulk) — продолжать сверять.
- **Забрать цветовую гамму из репозитория `MerlKoryAmber/squid-panel`** и применить к теме admanager.

## Пиллары — открытое (после ночи 2026-09-11)

- **Exchange:** реализовано по докам MS/ADManager, но **не верифицировано** (нет сервера). Поднять Exchange 2019, задать `Exchange:ConnectionUri`, прогнать enable/disable/props/distribution. Добавить пул runspace-ов; листинги Get-Mailbox/Get-DistributionGroup в UI.
- **RBAC:** перевести `FileRbacStore` → EF/SQL; добавить group-based scope (target ∈ группы), не только OU.
- **Automation:** перевести `FileAutomationStore` → EF/SQL; больше типов задач (move stale, cleanup groups, provision из CSV); проверить исполнение на реальных «неактивных» данных.
- **UI:** активное состояние верхних табов по странице (сейчас Management всегда active); скрывать левое дерево на Delegation/Automation/Reports.
- **Reports** — пиллар-заглушка (ADManager: 200+ отчётов).
- **Bulk CSV** импорт/модификация (ADManager).

## Общие (до прода)

- ~~Заменить `AllowAllRbacEngine` на реальный RBAC~~ — сделано (`RbacEngine`, под auth). AllowAll остаётся только для dev/no-auth.
- Заменить файловые сторы (`FileAuditLog`/`FileRbacStore`/`FileAutomationStore`) на EF/SQL (ADR-0002); прод-IIS — на полноценный SQL Server (ADR-0004).
- gMSA-режим `IOperationalCredentialProvider` протестировать на domain-joined хосте.
- DPAPI-шифрование `StoredCredential.Password`.
- `CancellationToken`/отмена в IO; валидация DN при внешнем вводе; `Before/After` в аудите.
