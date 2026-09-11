# Покрытие AD-функций ADManager Plus

Легенда: ✅ готово · 🟡 частично · ⬜ нет. Ветка `feature/ad-full-management`.

## User Management
- ✅ Create user (OU-выбор, пароль, enabled, must-change)
- ✅ **Bulk create** из CSV (`/users/bulk`)
- ⬜ Create user из шаблона
- ✅ **Modify user** — полный табовый редактор: General / Account / Address / Telephones / Organization / Profile (`/users/modify`)
- ⬜ Bulk modify из CSV
- ✅ Reset password / Unlock / Enable / Disable / Delete / Move / Rename
- ✅ Account options: password never expires, must change, account expires
- ⬜ Cannot change password (ACL), store pwd reversible, smartcard
- ⬜ Logon hours / Log on to (workstations)
- 🟡 Manager (атрибут) / ⬜ direct reports
- ⬜ Photo (thumbnailPhoto)
- ⬜ Restore (AD Recycle Bin)
- ✅ Member Of (через Groups)

## Group Management
- ✅ Create group (scope/type)
- ✅ **Modify group** (description, mail, managedBy, notes)
- ✅ Add/remove members / Delete / Move / Rename
- ⬜ Bulk membership из CSV
- ⬜ Convert scope/type
- 🟡 managedBy (атрибут) / ⬜ «manager can update membership»

## Computer Management
- ✅ Create / Enable / Disable / Delete / Move / Rename
- ✅ **Reset computer account**
- ✅ **Modify attributes** (description, location, managedBy)
- ⬜ Group membership

## Contact Management
- ✅ Create / Delete
- ⬜ Modify (редактор атрибутов) / Move / Rename в UI

## OU Management
- ✅ Create / Rename / Move / Delete
- ⬜ Modify (managedBy, block inheritance)

## Общее
- ✅ **Advanced search** (`/search`) — по cn/sam/displayName/mail по поддереву
- 🟡 Навигация по OU (dropdown) → ⬜ полноценное дерево AD
- ⬜ Templates (create/modify)
- ⬜ last logon / lastLogonTimestamp в списках
- ✅ Bulk (enable/disable/unlock/move/delete) для users

## Осталось (приоритет)
1. Дерево AD. 2. Bulk modify из CSV + шаблоны. 3. Contact/OU редакторы. 4. Computer group membership; group convert scope/type. 5. Recycle Bin restore. 6. Logon hours / workstations / photo. 7. lastLogon в списках.
