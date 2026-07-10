# Access Forms And Queries Decision Checklist

Этот checklist фиксирует, как согласовать старые Access forms/queries перед импортом и переносом бизнес-логики в GarageBalance.
Пункт нельзя закрывать как `[x]`, пока нет приватного schema inventory и ручного бизнес-решения по найденным объектам.

## Предусловия

- [ ] Есть приватная рабочая копия `.accdb`/`.mdb`; оригинал не изменяется.
- [ ] Есть рабочий Access reader или согласованная конвертация.
- [ ] Снят schema inventory со списком queries/forms/reports только как технических имен.
- [ ] Не выгружаются screenshots, SQL/query text, embedded macros или raw rows с персональными/финансовыми данными.
- [ ] Есть участник со стороны заказчика, который может подтвердить назначение старых форм и отчетов.

## Классификация Объектов

Каждый найденный Access form/query/report должен получить один статус:

- `ui_only`: старый экран ввода/просмотра без самостоятельной бизнес-логики; переносится как UX/reference, но не как импортируемая таблица.
- `report_only`: старый отчет/запрос только для вывода данных; сопоставляется с новым отчетом или остается historical reference.
- `business_rule`: объект содержит расчет, фильтр, начисление, распределение, округление, дату/период или проверку, которую нужно перенести в backend/domain logic.
- `import_helper`: объект использовался для подготовки/очистки/связки данных; правило нужно учесть в import dry-run/quarantine.
- `duplicate_or_obsolete`: объект больше не используется или дублирует другой; требует подтверждения перед исключением.
- `unknown_requires_decision`: назначения недостаточно ясны; нельзя переносить автоматически.

## Минимальная Safe Matrix

| Access object | Type | Decision | GarageBalance target | Evidence required | Privacy notes |
| --- | --- | --- | --- | --- | --- |
| `<technical name>` | form/query/report | ui_only/report_only/business_rule/import_helper/duplicate_or_obsolete/unknown_requires_decision | UI screen, report, domain service, import rule, skip, quarantine | owner confirmation, schema inventory, anonymized description | no SQL text/raw rows/screenshots |

## Что Нужно Согласовать

- [ ] Какие forms являются только UI и не требуют отдельного backend-rule.
- [ ] Какие queries являются отчетами и должны быть покрыты новыми report endpoints.
- [ ] Какие queries/forms содержат расчеты начислений, долгов, переплат, пеней, счетчиков, тарифов или периодов.
- [ ] Какие objects влияют на import mapping, deduplication, quarantine или начальные остатки.
- [ ] Какие old reports нужно сверить на реальных данных при acceptance.
- [ ] Какие objects можно исключить как duplicate/obsolete.
- [ ] Какие unknown objects остаются `[decision]` до демонстрации заказчику.

## Что Нельзя Сохранять В Git

- ФИО, адреса, телефоны, паспортные данные.
- Номера платежных документов и строки платежей по конкретным владельцам.
- Raw SQL/query text, если он содержит чувствительные значения или embedded credentials.
- Screenshots старых форм/отчетов с персональными или финансовыми данными.
- Сам `.accdb/.mdb`, exports, dumps, backups и private import folders.

## Когда Roadmap-Пункт Можно Закрыть

- [ ] Schema inventory содержит список forms/queries/reports без raw sensitive content.
- [ ] По каждому объекту есть decision из списка классификации.
- [ ] Все `business_rule` и `import_helper` объекты связаны с backend/domain/import задачами или уже реализованными правилами.
- [ ] Все `report_only` объекты связаны с новыми отчетами или acceptance-сверкой.
- [ ] Все `unknown_requires_decision` устранены или явно вынесены в отдельные `[decision]` пункты.
- [ ] Privacy-check подтверждает, что Access-файлы, screenshots, raw SQL и private exports не попадут в Git.
- [ ] Guard `AccessFormsQueriesDecisionRoadmapItemRequiresInventoryAndBusinessApproval` можно удалить только вместе с реальным decision evidence.
