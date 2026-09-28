# TODO / трекер хвостов

Обновлено: 2026-09-29 МСК. Заводится по §3 (≥2 пунктов — в трекер) и по итогам ревью.

## Фичи (запланировано)

| # | Пункт | Статус | Заметки |
|---|-------|--------|---------|
| F1 | **Template editor: визуальная 2-колоночная раскладка перетаскиванием (drag-n-drop)** | открыт | Сейчас форма Create/Modify рендерит поля в 2 колонки авто-потоком (слева-направо), а редактор шаблона — линейный список (Field Tray клик + ↑↓). Хотим как в ADManager Plus: таскать чипы полей мышкой в 2-колоночную сетку с явными позициями (колонка/строка). **Технология НЕ мешает: мы уже на HTML5, drag-n-drop (HTML5 DnD API) доступен в Blazor как есть — вопрос только объёма (обработчики + модель позиций `Column`/`Order` в `TemplateField`).** Паттерн DnD в проекте уже есть — переиспользовать. |
| F4 | **Дистрибутив с Windows-службой (без IIS)** | открыт | Self-contained publish (`--self-contained -r win-x64`, свой .NET рантайм) + Kestrel как Windows-служба (`sc create`/`New-Service`), без IIS/Hosting Bundle — самодостаточный дистрибутив как у ADManager Plus. Установщик тогда: развернуть папку + create service + создать БД. **Риск/проверить:** Windows Auth (Kerberos SSO техников) на голом Kestrel — сложнее чем ANCM в IIS; смешанный вход (домен SSO + локальные УЗ, ADR-0007) надо верифицировать на Kestrel. Прим.: Tomcat не рассматриваем — это Java-контейнер, наш стек .NET; «переезд на Tomcat» = переписать проект на Java. |
| F2 | **Настройка домена/подключения в самой панели** (как в ADManager) — **мульти-домен** | **Фаза 1 сделана** | Реестр доменов в БД (`ManagedDomain`/`IDomainRegistry`, EF+шифрование пароля), UI Administration→Settings→Domains. Активный домен (IsDefault) питает AdConnectionOptions/креды/BaseDn; пустой реестр → bootstrap из конфига. Активен один домен, смена — после recycle пула. |
| F2.2 | **Мульти-домен Фаза 2** | открыт | Переключатель домена в шапке (сейчас domain-chip статичный) + прокидывание domainId в страницы/операции без recycle; per-domain кэши (OU-дерево, Exchange DB list — сейчас один); тегирование домена в аудите/делегировании (scope в домене X ≠ Y); фабрика сервисов по domainId вместо фиксации активного на старте. Межлесные доверия — вне кода (инфраструктура). Перенос текущего merl.loc в реестр обкатан вживую (2026-09-27): после recycle панель работает из реестра, не из конфига. |
| F2.3 | **gMSA: назначение на App Pool из панели** | открыт | Сейчас `install-iis-site.ps1` ставит пул под **доменной УЗ** (`Merl.loc\merl` из .env, identityType=SpecificUser) — сделано для Kerberos к Exchange (см. EX1). Для домена с `Mode=gMSA` панель (или установщик) должна выставлять Identity пула = `DOMAIN\gmsa$` (identityType=3, без пароля; gMSA заводится/Install-ADServiceAccount админом заранее, привязку к пулу делает панель). Требует привилегированной операции над IIS. Для чужих лесов gMSA неприменима — там StoredCredential. |
| A1 | **Вход ТОЛЬКО через форму логин/пароль для всех (без Windows SSO)** | открыт | Требование пользователя (2026-09-29): единая форма входа для всех, никакого Kerberos/Windows SSO. Одна форма `/login`: доменные юзеры проверяются **LDAP-bind'ом логина/пароля к AD** (bind уже работает, DNS/Kerberos для него не нужен), из AD берём SID/группы для RBAC; локальные УЗ панели — как сейчас. **Убрать:** Windows Authentication из IIS/ANCM (сейчас ADMGR_AUTH=IIS + смешанный вход ADR-0007), доменный пул для SSO не нужен. **Плюс:** резко упрощает F4 (Kestrel-служба без IIS — главный блокер был именно SSO). Важно: NB — я (ассистент) ошибочно навязал требование SSO ранее; пользователь SSO не просил. |
| EX1 | **Exchange: перевести код на Basic/HTTPS (верифицировано вживую)** | **открыт (код), инфра готова** | Exchange 2019 поднят на exchange.merl.loc (192.168.0.156). **Вживую подтверждено (2026-09-29):** Basic-auth поверх HTTPS к `https://exchange.merl.loc/PowerShell/` + AllowRedirection + SkipCACheck работает — `Get-MailboxDatabase`/`Get-Mailbox` вернули реальные данные (DB `Mailbox Database 1456599688`, Administrator@merl.loc). На Exchange включён Basic на PowerShell vdir (Set-PowerShellVirtualDirectory -BasicAuthentication:$true). **Осталось в КОДЕ:** `ExchangeService.OpenRunspace` перевести на Basic+HTTPS+AllowRedirection+SkipCACheck (сейчас только Kerberos/HTTP из `ExchangeOptions`); секция `Exchange` в appsettings уже добавлена (ServerFqdn/ConnectionUri/Authentication). Почему Kerberos не пошёл: DNS-порт 53 до DC закрыт → SRV `_kerberos._tcp.merl.loc` недоступны → KDC не находится (инфраструктура, не код). hosts: добавлены exchange.merl.loc, dc-01.merl.loc, merl.loc → 192.168.0.175/156. Прод: либо чинить DNS/Kerberos, либо Basic/HTTPS с валидным сертификатом. |
| F3 | **.exe установщик** | отложено (не в этой итерации) | SQL и IIS ставит пользователь заранее по инструкции (см. docs/install-guide.md). Установщик потом: только проверка пред-требований (.NET Hosting Bundle, IIS, SQL-доступ), publish + IIS-сайт + seed. Домен НЕ настраивает — это делается в панели (F2). Формат — стандартный .exe (напр. Inno Setup/WiX bootstrapper). |

## Хвосты Фазы 0 (из независимого ревью)

| # | Пункт | Серьёзность | Статус | Куда |
|---|-------|-------------|--------|------|
| 1 | Тест давал ложный «зелёный» без `.env` (skip как pass) | major | **исправлено** (SkippableFact) | — |
| 2 | Окно неаудируемого изменения (аудит только post-op) | major | **исправлено** (двухфазный аудит Attempt/Result) | — |
| 3 | `StoredCredential.Password` — открытый текст | major | **исправлено** | Шифруется envelope (ADR-0006) через `EncryptedSettingsStore` вместе с Smtp/Pfx-паролями; ключ — DpapiKeyring. Осталось до прода: перенос ключа на DPAPI-NG/секрет-хранилище на проде. |
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
| R6 | Весь домен грузится в память на /users,/groups,/computers,/contacts,/exchange; дропдауны «Add member» рендерят всех юзеров | major | **исправлено** (VLV-пагинация в гридах + PrincipalPicker autocomplete; /reports — см. ниже) |
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
