# Операционная модель: идентичность и права

## Как это делает ADManager (справочно)

- Windows-служба ADManager Plus по умолчанию стартует под Local System — это только хост процесса.
- Реальные операции AD/Exchange идут под **настроенной в продукте доменной УЗ** (Domain Settings), хранимой зашифрованно.
- Exchange достигается через **remote PowerShell** с явной передачей этих кредов (`New-PSSession -Credential`), а не под Local System (= учётка компьютера `DOMAIN\SERVER$`).

Вывод: run-as процесса и operational identity — разные вещи. Работу делает настроенный сервисный аккаунт с делегированными правами.

## Наш выбор

| | Вариант A — «как ADManager» | **Вариант B — gMSA (по умолчанию)** |
|---|---|---|
| Процесс | Local System / любой | app pool = gMSA |
| Auth в AD/Exchange | хранимая зашифрованная УЗ, передаётся явно | identity процесса, Kerberos |
| Пароль | хранить + ротировать | AD ротирует сам |
| Плюс | несколько доменов | безопаснее, проще для 1 домена |

Оба скрыты за `IOperationalCredentialProvider`. По умолчанию — B. A включаем, если появятся другие домены/леса.
Double-hop не возникает: операции всегда от фиксированной operational identity, не от техника.

## Наименьшие привилегии (один домен)

**AD** — делегировать на целевых OU: extended right `Reset Password`, запись `lockoutTime`/`userAccountControl`, `Create/Delete Child (user)`, запись `member` на группах. **Не** Domain Admin.

**Exchange 2019** — назначить operational identity кастомную **RBAC** management-role, суженную до нужных командлетов и recipient scope (напр. на базе Recipient Management).

## Матрица прав (черновик, заполнять по мере реализации)

| Операция (Permission) | Право AD / роль Exchange |
|-----------------------|--------------------------|
| ResetPassword | extended right Reset Password на OU |
| UnlockAccount | запись `lockoutTime` |
| EnableDisableUser | запись `userAccountControl` |
| CreateUser / DeleteUser | Create/Delete Child (user) на OU |
| ModifyAttributes | запись выбранных атрибутов |
| ManageGroupMembership | запись `member` |
| EnableMailbox / SetMailboxProps | Exchange RBAC (Recipient Management scope) |
| ManageDistribution | Exchange RBAC (Distribution Groups) |
