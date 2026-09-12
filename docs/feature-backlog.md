# Feature backlog — фичи панелей уровня ADManager Plus

Обновлено: 2026-09-12 МСК. Источник — исследование фич ManageEngine ADManager Plus / RecoveryManager Plus / Exchange Admin Center / Netwrix GPO (см. ссылки в конце). Приоритет: feasible-in-lab и по архитектуре проекта (Blazor Server + S.DS для AD, remote PowerShell для Exchange).

## Реализуемо в лабе (S.DS/LDAP, проверяемо)

### Group Policy (новый пиллар)
- [x] **Список GPO** (groupPolicyContainer): имя, версия, статус настроек (user/computer disabled). — сделано
- [x] **Линки GPO по scope** (gPLink): показать порядок, enforced, enabled; link/unlink/enforce/enable. — сделано
- [x] **Отчёт linked/unlinked** (по всем OU+домену). — сделано (счётчик линков в списке)
- [ ] Отчёт «orphaned» (в CN=Policies есть, но нет SYSVOL, или наоборот), «disabled links».
- [ ] Порядок линков (precedence) — reorder ↑/↓ в gPLink.
- [ ] Security filtering (member of + Apply Group Policy ACE) — чтение/правка.
- [ ] GPO backup/restore и создание/удаление GPO — **нужен GPMC/PowerShell-модуль GroupPolicy** (как Exchange, отдельным адаптером).

### AD management (дополнения)
- [ ] **Last logon / inactive users** (`lastLogonTimestamp`) — отчёт + фильтр; база под automation DisableInactiveUsers.
- [ ] **Вложенное (эффективное) членство** пользователя (`memberOf` транзитивно, matching rule 1.2.840.113556.1.4.1941).
- [ ] **User → его группы** на странице modify; **экспорт членов группы** в CSV.
- [ ] **Move home directory** при перемещении/переименовании.
- [ ] **Password expiry** отчёт (msDS-UserPasswordExpiryTimeComputed).
- [ ] **Bulk modify из CSV** (сейчас только bulk create).

## Требует инфраструктуры (код можно, верификация — за пользователем)

### Exchange (remote PowerShell; сервер в лабе не поднят)
- [ ] **Права на ящик**: Full Access (`Add/Remove-MailboxPermission`), Send As (`Add/Remove-ADPermission -ExtendedRights "Send As"`), Send on Behalf (`Set-Mailbox -GrantSendOnBehalfTo`). — следующий батч
- [ ] **Shared mailbox** (создать/конвертировать), **resource mailbox** (room/equipment).
- [ ] **Листинги** Get-Mailbox / Get-DistributionGroup в UI; владельцы distribution-группы (ManagedBy).
- [ ] Mailbox policies (ActiveSync/OWA/retention) — как в ADManager.
- [ ] Пул runspace-ов (сейчас разовое подключение).

### Прочее (из TODO)
- [ ] RBAC group-scope; EF-миграция файловых сторов; DPAPI для StoredCredential; серверная LDAP-пагинация (R6); gMSA на domain-joined хосте.

## Источники (research 2026-09-12)
- [ADManager Plus — AD management tool](https://www.manageengine.com/products/ad-manager/active-directory-tool.html)
- [ADManager Plus — Group Policy management](https://www.manageengine.com/products/ad-manager/windows-ad-group-policy-management.html)
- [ADManager Plus — GPO reports](https://www.manageengine.com/products/ad-manager/windows-active-directory-gpo-reports.html)
- [RecoveryManager Plus — GPO backup/restore](https://www.manageengine.com/ad-recovery-manager/active-directory-group-policy-object-backup-recovery.html)
- [Netwrix — Group Policy management guide](https://netwrix.com/en/resources/blog/group-policy-management/)
- [Microsoft Learn — Exchange mailbox permissions](https://learn.microsoft.com/en-us/exchange/recipients/mailbox-permissions)
- [Microsoft Learn — Exchange Admin Center](https://learn.microsoft.com/en-us/Exchange/architecture/client-access/exchange-admin-center)
- [Microsoft Learn — distribution groups](https://learn.microsoft.com/en-us/exchange/recipients-in-exchange-online/manage-distribution-groups/manage-distribution-groups)
