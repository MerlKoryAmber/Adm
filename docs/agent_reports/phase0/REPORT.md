# REPORT — Фаза 0 (AD-срез: сброс пароля)

Дата: 2026-09-11 МСК. Роль: строитель. Статус: РЕАЛИЗОВАНО НО НЕ ПРИНЯТО.

## Задача

Первый вертикальный срез: реальный сброс пароля пользователя AD через весь конвейер, с аудитом, проверенный вживую на лабе.

## Сделано

- `ResetPasswordUseCase`: RBAC (заглушка allow) → `IAdService` → `IAuditLog`.
- `AdService.ResetPasswordAsync`: `System.DirectoryServices` + `AuthenticationTypes.Secure`, `Invoke("SetPassword")` — без LDAPS.
- `ConfiguredCredentialProvider`: gMSA (процесс) или StoredCredential (креды из конфига/`.env`).
- `FileAuditLog`: временный JSONL-аудит (`artifacts/audit.jsonl`).
- Интеграционный тест `ResetPasswordSliceTests`: сброс на `test.user1` + вход новым паролем + проверка записи аудита.

## Верификация (автор)

- `dotnet build` — 0 warning / 0 error.
- `dotnet test` (Domain) — 2/2; (Integration, live) — 1/1 Passed.
- Live-подтверждение: `artifacts/audit.jsonl` содержит запись `ResetPassword Success=true` с МСК-таймстампом; тест прошёл только из-за успешного входа `test.user1` новым паролем.

## Не сделано / замечания

- Enum `Operation` в аудите сериализуется числом (0) — сделать строкой (`JsonStringEnumConverter`).
- Тест меняет пароль `test.user1` при каждом прогоне (`.env LAB_TEST_PWD` перестаёт совпадать) — ок для лабы, учесть.
- RBAC — заглушка allow-all (Фаза 2). Аудит — файловый (заменить на EF/SQL). gMSA — не тестировался (dev под StoredCredential).
- Требуется **независимая проверка (§4):** ревью кода + аудит безопасности (обработка пароля/кредов, корректность S.DS, целостность аудита).

## Независимое ревью (§4) — проведено 2026-09-11 МСК

Ревьюер (субагент): critical-блокеров нет; срез корректен и безопасен по обращению с паролем/идентичностью. 3 major + 6 minor. Итог по исправлениям:

- **Исправлено:** ложный «зелёный» теста → `SkippableFact` (реальный Skipped); окно неаудируемого изменения → двухфазный аудит (Attempt до / Result после); падение `QueryAsync` на битой строке → пропуск; enum в аудите → строкой.
- **В трекер** (`docs/handoff/TODO.md`): открытый текст `StoredCredential` (DPAPI до прода), неизменяемость файлового аудита (hash-chain в EF/SQL), `CancellationToken`, `Before/After`, восстановление пароля в тесте, валидация DN.

Пересобрано после правок: build 0/0, тесты 3/3 (Domain 2 + Integration live 1).

## Следующее

Приёмка человеком (§19). Далее — остальные AD-операции (unlock/enable/create/attrs/группы) или Web-UI-срез.
