# Access To PostgreSQL Mapping Checklist

Этот документ фиксирует безопасную карту соответствия старой Access-БД и целевой PostgreSQL-модели GarageBalance.
Карта является предварительной: она опирается на текущую EF Core модель и бинарные ориентиры из `docs/source-analysis.md`, но не считается финальной field-level mapping без приватного schema inventory старой базы.

## Предусловия Для Финального Закрытия

- [ ] Есть приватная рабочая копия `.accdb`/`.mdb`; оригинал не изменяется.
- [ ] Есть рабочий reader Access или согласованная конвертация.
- [ ] Снят schema inventory с таблицами, колонками, типами, ключами, relationships, indexes и row counts.
- [ ] Снят pre-import baseline с row counts и checksums по ключевым таблицам.
- [ ] Mapping составляется без raw rows, ФИО, адресов, телефонов, номеров документов и платежных строк по конкретным владельцам.

## Известные Access Ориентиры

Из `docs/source-analysis.md` доступны только неполные технические имена:

- таблицы/объекты: `garage`, `Vladelci`, `vidoplati`, `VidViplat`;
- поля/идентификаторы: `GARAGENUMBER`, `OwnerID`, `FAMILIA`, `IMYA`, `OTCHESTVO`, `TELEFHONE`, `ADRES`, `CHELOVEK`, `ETAZHI`, `PREDZNACHENIE`, `NOVZNACHENIE`, `RAZNICA`, `OPLACHENO`, `SUMMA`, `DATAPLATEZ`, `DATAVIPLATI`, `CENAPLATEZ`, `KOPLATE`, `ANOTACIA`.

Этого достаточно для предварительной матрицы, но недостаточно для закрытия roadmap-пункта как `[x]`.

## Предварительная Матрица Соответствия

| Access источник | Access поля-ориентиры | PostgreSQL target | Правило переноса | Статус |
| --- | --- | --- | --- | --- |
| `Vladelci` | `OwnerID`, `FAMILIA`, `IMYA`, `OTCHESTVO`, `TELEFHONE`, `ADRES` | `owners` | ФИО разделяется на `LastName`, `FirstName`, `MiddleName`; телефон и адрес переносятся как чувствительные поля; `OwnerID` используется только как external id/fingerprint, не как публичный GUID. | Требует schema inventory |
| `garage` | `GARAGENUMBER`, `OwnerID`, `CHELOVEK`, `ETAZHI`, `ANOTACIA` | `garages` | Номер гаража -> `Number`; связь `OwnerID` -> `OwnerId`; количество людей -> `PeopleCount`; этажи -> `FloorCount`; примечание -> `Comment`; пустые/дублирующиеся номера уходят в quarantine. | Требует schema inventory |
| Access виды оплат | `vidoplati`, `KOPLATE`, `CENAPLATEZ`, `ANOTACIA` | `income_types`, `tariffs`, `charge_service_settings`, `irregular_payments` | Виды поступлений нормализуются: справочник типа дохода отдельно от тарифа, регулярности и признака счетчика; ставки и пороги требуют проверки дат действия. | Требует бизнес-сверку |
| Access виды выплат | `VidViplat`, `ANOTACIA` | `expense_types`, `supplier_groups`, `suppliers` | Тип выплаты отделяется от контрагента; если в Access контрагенты смешаны с видом выплаты, строка требует rule/quarantine. | Требует schema inventory |
| Access платежи владельцев | `DATAPLATEZ`, `OPLACHENO`, `SUMMA`, `GARAGENUMBER`/`OwnerID`, `KOPLATE`, `ANOTACIA` | `financial_operations`, `accruals` | Фактическое поступление -> `financial_operations`; начисление/долг -> `accruals`; связь идет через гараж и тип дохода; суммы переносятся decimal с явным округлением. | Требует source rows вне Git |
| Access выплаты поставщикам/персоналу | `DATAVIPLATI`, `SUMMA`, `ANOTACIA` | `financial_operations`, `supplier_accruals`, `staff_members` | Выплата поставщику/персоналу требует определить counterparty type; неизвестные получатели и документы уходят в quarantine. | Требует schema inventory |
| Access счетчики | `PREDZNACHENIE`, `NOVZNACHENIE`, `RAZNICA`, `GARAGENUMBER`, `DATAPLATEZ`/месяц | `meter_readings` | Предыдущее/новое значение -> `PreviousValue`/`CurrentValue`; разница сверяется с `Consumption`; отрицательная или пропущенная последовательность дает warning/quarantine. | Требует row-level dry-run |
| Access начальные остатки | `SUMMA`, `OPLACHENO` и исторические долги | `garages.StartingBalance`, `suppliers.StartingBalance`, `funds` | Начальные остатки нельзя выводить из отдельных строк без согласованной даты среза; использовать только после baseline и сверки отчетов. | Требует решение |
| Import service metadata | source table, external id, row hash | `access_import_row_fingerprints`, `access_import_created_records`, `access_import_quarantine_items` | Каждая перенесенная строка получает source entity type, optional external id, row hash, target entity и audit trail; дубли блокируются fingerprint-ключом. | Готово как целевая модель |

## Целевые Таблицы, Которые Не Импортируются Из Access Напрямую

- `app_users`, `app_roles`, `app_user_roles`: создаются в новой системе через механизм пользователей и ролей.
- `audit_events`: создается новой системой при изменениях и импорте.
- `access_import_runs`, `access_import_run_log_entries`: создаются процессом импорта.
- `form_states`: состояние UI новой системы.
- `integration_secret_settings`: будущие защищенные настройки интеграций.
- release notes из `backend/GarageBalance.Api/AppReleases/releases.json`: справочная история версии продукта, не legacy data.

## Что Должна Содержать Финальная Карта

- [ ] Для каждой Access-таблицы указан PostgreSQL target или причина исключения.
- [ ] Для каждой переносимой колонки указан target field, тип, nullable-правило и transform.
- [ ] Для каждой связи указан lookup key и поведение при отсутствии target entity.
- [ ] Для каждой финансовой суммы указано направление, знак, валюта/единица, округление и accounting month.
- [ ] Для каждой даты указано правило преобразования в `DateOnly`, `DateTimeOffset` или accounting month.
- [ ] Для каждой строки указан idempotency key: external id или deterministic row hash.
- [ ] Для каждой неоднозначности указан quarantine reason.
- [ ] Для исключенных tables/forms/queries указана причина: UI-only, report-only, temp/system, duplicate или requires decision.
- [ ] Итоговый файл содержит `rawRowsExported=false` и не содержит персональных/финансовых строк.

## Когда Roadmap-Пункт Можно Закрыть

- [ ] Финальный field-level mapping составлен на основе приватного schema inventory.
- [ ] Mapping покрывает гаражи, владельцев, виды оплат/выплат, тарифы, начисления, платежи, счетчики, поставщиков и начальные остатки.
- [ ] Все unknown tables/columns имеют решение: import, skip, quarantine или business decision.
- [ ] Mapping сверяется с pre-import baseline counts/checksums.
- [ ] Privacy-check подтверждает, что Access-файлы, raw exports и приватные данные не попадут в Git.
- [ ] Guard `AccessPostgreSqlMappingRoadmapItemRemainsBlockedUntilFieldLevelInventoryMappingExists` можно удалить только вместе с реальным mapping evidence.
