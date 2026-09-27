# Installation guide — admanager

Установка панели admanager на Windows Server в домене AD.

> Статус: пред-требования (SQL, IIS) ставит администратор **вручную** по этой инструкции.
> Будущий .exe-установщик (трекер F3) будет только **проверять** пред-требования и
> разворачивать панель; SQL/IIS он ставить не будет. Домен настраивается **в самой панели**
> после установки (сейчас — через конфиг, см. §5; перенос в UI — трекер F2).

## 1. Требования

| Компонент | Требование |
|-----------|-----------|
| ОС | Windows Server 2019/2022, **в домене** (member server) |
| Права установки | Локальный администратор сервера |
| .NET | **ASP.NET Core 8 Hosting Bundle** (для IIS) |
| Веб-сервер | IIS с Windows Authentication |
| БД | SQL Server 2019+ или **SQL Server Express** (локально или на отдельном хосте) |
| Operational identity | gMSA (рекомендуется) или доменная УЗ — **не** Domain Admin; точечная делегация прав |

## 2. Установить SQL Server (вручную)

Вариант А — **SQL Express локально** (лаба/малый прод):
- Скачать SQL Server Express, установить инстанс `SQLEXPRESS`.
- Убедиться, что служба запущена и доступна как `.\SQLEXPRESS`.

Вариант Б — существующий SQL Server: получить строку подключения к нему.

БД создаётся приложением автоматически при первом старте (EF `EnsureCreated`/миграции) —
достаточно, чтобы у пула IIS был доступ на создание БД `AdManager` (или создать её заранее
и дать права на неё).

## 3. Установить IIS + .NET Hosting Bundle (вручную)

Включить роль IIS с компонентами (в т.ч. **Windows Authentication**) и поставить
**ASP.NET Core 8 Hosting Bundle**. Быстрый путь — готовый скрипт репозитория (elevated):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build\install-server.ps1
```

Он включает нужные IIS-features и ставит Hosting Bundle. После — `iisreset`.

Проверка: `dotnet --list-runtimes` показывает `Microsoft.AspNetCore.App 8.x`, а в IIS Manager
есть модуль **AspNetCoreModuleV2**.

## 4. Развернуть панель

1. Собрать publish:
   ```powershell
   dotnet publish src\AdManager.Web\AdManager.Web.csproj -c Release -o publish
   ```
2. Развернуть в IIS-сайт (elevated) — создаёт сайт `admanager` на :8080, пул
   `ApplicationPoolIdentity`, включает Windows + Anonymous (смешанный вход, ADR-0007),
   ставит ACL на `App_Data` и keyring:
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File build\install-iis-site.ps1
   ```

Переменные окружения пула (ставятся скриптом): `ADMGR_AUTH=IIS`, `ADMGR_STORE=Ef`,
`ADMGR_AUDIT=Ef`, `ASPNETCORE_ENVIRONMENT=Production`.

## 5. Конфигурация подключений

`appsettings.json` (или переменные окружения) на сервере:

- **Строка подключения БД** — `ConnectionStrings:AdManagerDb`. По умолчанию:
  ```
  Server=.\SQLEXPRESS;Database=AdManager;Trusted_Connection=True;TrustServerCertificate=True
  ```
  Для внешнего SQL заменить `Server=…` и, при необходимости, способ аутентификации.
- **Домен / AD** — пока берётся из конфига (`Ad:*` / `.env`). **Перенос в UI-настройки —
  трекер F2**; после него домен и operational identity задаются в панели
  (Administration → Settings), как в ADManager, без правки конфига.
- **Exchange** — `Exchange:ConnectionUri` (remote PowerShell). Без него mailbox-операции
  вернут ошибку «Exchange is not configured».

Секреты (SMTP/PFX/operational пароль) шифруются envelope-шифрованием (ADR-0006); ключ —
`C:\ProgramData\AdManager\keyring`. **Бэкапить ключ отдельно от БД** (docs/backup-dr.md).

## 6. Первый вход

- Открыть `http://<сервер>:8080` (или ваш биндинг). Доменные техники входят через
  Windows SSO; локальные УЗ панели — через `/login`.
- Встроенная локальная УЗ на свежей установке: **admin / admin** — сменить сразу в
  Administration → Panel users.
- Дальше: настроить роли/scope (Delegation), operational identity и прочее — в панели.

## 7. Обновление

Пересобрать publish и повторно прогнать `build\install-iis-site.ps1` (он гасит пул перед
копированием). Данные (БД, App_Data, keyring) сохраняются.

## Связанные документы

- `docs/backup-dr.md` — бэкап БД и ключа шифрования, восстановление.
- `docs/SKELETON.md` — структура и конфигурация.
- ADR: `docs/adr/` (0004 SQL-аудит, 0006 секреты, 0007 локальные УЗ + смешанный вход).
