# DEAD-CODE — неиспользуемые запчасти (зачистить по окончании разработки)

Заведено: 2026-09-27 МСК. Здесь копятся куски кода, оставленные временно
(после рефактора UI или намеренно скрытого функционала). НЕ удалять по ходу
разработки — зачистка отдельным проходом (`chore(cleanup): …`) на приёмке.

Правило: перед удалением каждого пункта — убедиться, что он реально не вызывается
(в т.ч. из .razor-разметки), пересобрать, прогнать тесты.

## 1. `Indent(int)` в формах после перехода на `OuPicker`

После замены плоских `<select>` целевого OU на `OuPicker` (выпадающее дерево)
хелпер отступов `Indent` больше не вызывается из разметки. Удалить объявления:

- [ ] `src/AdManager.Web/Components/Pages/GroupCreate.razor` — `private static string Indent(int d)` (~L81)
- [ ] `src/AdManager.Web/Components/Pages/ComputerCreate.razor` — `Indent` (~L60)
- [ ] `src/AdManager.Web/Components/Pages/ContactCreate.razor` — `Indent` (~L64)
- [ ] `src/AdManager.Web/Components/Pages/OuCreate.razor` — `Indent` (~L63)
- [ ] `src/AdManager.Web/Components/Pages/BulkCreate.razor` — `Indent` (~L89)
- [ ] `src/AdManager.Web/Components/Pages/Users.razor` — `Indent` (~L202)
- [ ] `src/AdManager.Web/Components/Pages/GroupModify.razor` — `Indent` (~L194)
- [ ] `src/AdManager.Web/Components/Pages/ComputerModify.razor` — `Indent` (~L174)
- [ ] `src/AdManager.Web/Components/Pages/OrganizationalUnits.razor` — `Indent` (вариант с «└ », ~L83)

Примечание: поле `List<AdOuNode>? ous` в Create-формах **не мёртвое** — используется
для выбора OU по умолчанию (`ous.Any(o => o.Dn == …)`). Не трогать, пока default-OU
не переедет в сам `OuPicker`/кэш. (Кандидат на будущее упрощение: убрать `ListAllOusAsync`
из этих форм и брать дефолт из `IOuTreeProvider`.)

## 2. Скрытый функционал «Disable/Delete mailbox» (Exchange.razor)

Кнопки «Disable mailbox» (bulk + single) временно убраны из разметки по требованию
(«кнопку удалить почтовый ящик пока вообще убираем»), методы оставлены в коде на
случай возврата:

- [ ] `src/AdManager.Web/Components/Pages/Exchange.razor` — `private async Task BulkDisable()` (~L217)
- [ ] `src/AdManager.Web/Components/Pages/Exchange.razor` — `private async Task DisableMailbox()` (~L231)

Решение по этим двум — при приёмке: либо вернуть кнопки, либо удалить методы
(и, если больше нигде не нужен, `IExchangeService.DisableMailboxAsync` +
`ExchangeManagementService.DisableMailboxAsync` — проверить перед удалением).
