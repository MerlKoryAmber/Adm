# REPORT — создание скелета admanager

Дата: 2026-09-10 МСК. Роль: строитель. Статус: РЕАЛИЗОВАНО НО НЕ ПРИНЯТО.

## Задача

Собрать скелет проекта (чтобы не шерстить код каждый раз, §22), применить правила из CLAUDE.md (repo radiusproxy), заложить модель делегирования ADManager, идентичность/Exchange, автоматизацию вместо апрув-workflow.

## Что сделано

- Адаптирован `CLAUDE.md` под admanager (22 правила сохранены, добавлена специфика).
- Скелет .NET 8, Clean-архитектура: Domain, Application (контракты), 4× Infrastructure (заглушки), Web (Blazor Server host + health), Domain.Tests.
- Docs: SKELETON, architecture, operations-model, ADR-0001, handoff/CURRENT.
- Инфра сборки: Directory.Build.props, .editorconfig, .gitignore, build/init.ps1.

## Решения

- Operational identity = gMSA по умолчанию, за `IOperationalCredentialProvider` (альтернатива — модель ADManager с хранимой УЗ). Обоснование — `operations-model.md`.
- Exchange — только remote PowerShell; проекты Ad/Exchange = net8.0-windows.
- Апрув-workflow исключён; заложен `IAutomationScheduler`.

## Не проверено (нужна верификация, §4)

- **Сборка не запускалась** — в окружении нет .NET SDK. Гейт зелёный не подтверждён.
- Версии NuGet-пакетов в .csproj проставлены ориентировочно — проверить при первом restore.

## Следующие шаги

См. `docs/handoff/CURRENT.md`. Ближайшее — установить SDK и собрать; затем согласовать список `Permission` и Фазу 0.
