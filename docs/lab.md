# Лабная среда (merl.loc)

Обновлено: 2026-09-11 МСК. Только для разработки/тестов.

## Домен

- DC: **dc-01.Merl.loc** — IP **192.168.0.175**, LDAP 389 доступен.
- Корень: **DC=Merl,DC=loc**. Уровень домена — 2016.
- Рабочая УЗ (dev operational identity): `Merl.loc\merl` (Domain/Enterprise/Schema Admins).
  Креды — в `C:\Code\admanager\.env` (не в git). Пароль в чат/логи не выводим.
- Ограничения владельца: не ронять домен; **никаких write-действий над учёткой `Administrator`**.

## Созданные объекты (build/lab-setup.ps1, идемпотентно)

```
OU=AdManagerLab,DC=Merl,DC=loc
├─ OU=Users
│   ├─ CN=Test User1  (sam=test.user1, enabled)
│   └─ CN=Test User2  (sam=test.user2, enabled)
├─ OU=Groups
│   └─ CN=HelpDesk-L1 (global security)
└─ OU=ServiceAccounts   (пусто; svc-УЗ + делегирование — Фаза 2)
```

Пароль тест-юзеров — в `.env` (`LAB_TEST_PWD`).

## Пересоздать / догнать

```powershell
& C:\Code\admanager\build\lab-setup.ps1
```

## Заметки для реализации (проверено на живом DC)

- **Сброс пароля работает через `System.DirectoryServices` + `AuthenticationTypes.Secure`** (Kerberos/NTLM sealing), LDAPS не обязателен. Это же использует `IAdService.ResetPasswordAsync`.
- Проверка существования объекта: не `NativeObject`/`.Guid` (ложный true), а `DirectoryEntry.RefreshCache()` — форсит round-trip.
- `.ps1` для Windows PowerShell 5.1 держать **ASCII-only** (5.1 читает как ANSI, кириллица в скрипте ломает парсер). UTF-8 — только с BOM.
