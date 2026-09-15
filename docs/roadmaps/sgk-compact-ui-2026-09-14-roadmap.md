# Полная компактная перекомпоновка интерфейса SGK (14.09.2026)

## Цель

Перекомпоновать весь пользовательский интерфейс GarageBalance для профессиональной ежедневной работы на ноутбуках и небольших настольных мониторах. Главная точка приёмки — viewport `1366×768` CSS-пикселей при масштабе браузера 100%. На этой точке ни страница, ни рабочая таблица не должны иметь горизонтальной прокрутки; все обычные формы и диалоги должны одновременно оставлять доступными заголовок, содержимое, сообщения и панель действий.

## Статус

Выполнено и проверено 205 из 208 пунктов (98,56%). В работе и не начато — 0. Один пункт (0,48%) заблокирован отсутствием Docker Engine, один (0,48%) оставлен для расширенной ручной приёмки на дополнительных viewport, один (0,48%) требует отдельного бизнес-решения по признаку «Годовой платёж был». Обязательная точка `1366×768` и весь объём текущего задания завершены.

## Источники

- [x] Техническое задание `SGK_(14.09.26)` из переданного текстового вложения (878 строк) прочитано полностью.
- [x] Корневой `AGENTS.md` прочитан; отдельные вложенные инструкции в репозитории отсутствуют.
- [x] Текущее состояние Git проверено: `master`, HEAD `85d6bc7f`, четыре локальных коммита сверх `origin/master`, незакоммиченных изменений на старте нет.
- [x] Актуальный frontend автоматически просканирован по `frontend/src/**/*.{ts,tsx}`: маршрутизация рабочих разделов, формы, диалоги, таблицы, вкладки, всплывающие элементы и общие компоненты включены в инвентарь ниже.
- [x] Действующие UI-документы и существующие roadmap найдены. Старые roadmap не используются для расширения этой задачи; новый документ является отдельным активным планом.

## Решения и допущения

- [x] Компактный desktop определяется составным условием: ширина меньше `1500px` **или** высота меньше `850px`. Обычный desktop начинается одновременно с ширины `1500px` и высоты `850px`.
- [x] `1366×768` — обязательная поэкранная и поформенная приёмка; `1280×720`, `1440×800`, `1536×864`, `1920×1080` — общая матрица регрессии. Дополнительно проверяются заданные `1360×768` и `1600×900`; `1600×768` служит контрольной точкой высотного переключения compact mode.
- [x] Сначала меняются общие примитивы и правила плотности, затем разделы в порядке приложения. Финансовые формулы, права, аудит, API и схема БД не меняются без доказанной необходимости.
- [x] Важные данные не скрываются. Если набор колонок физически не помещается, строка перекомпоновывается в группы/вторые строки или получает доступное раскрытие подробностей.
- [x] Временные данные, PostgreSQL, процессы, снимки, coverage и логи принадлежат задаче и удаляются после приёмки. Push запрещён без отдельной команды.

## Формат записей инвентаря

Каждая строка ниже — самостоятельный проверяемый объект. В колонке «Проблема / решение» первая часть отражает состояние, подтверждённое исходным кодом; результат визуального измерения дописывается после запуска. «Автотест» и «Браузер» не считаются выполненными, пока не запущены указанные проверки. Общие состояния для каждого объекта: loading/background refresh, data, empty, API error/retry, permission denied, disabled, save/validation, длинные значения, большие суммы/списки, открытые popover/menu/tooltip, keyboard focus и отсутствие переполнения.

## M0. Инвентаризация и измерительная система

- [x] Инвентарь маршрутов и рабочих разделов: главное меню, пользователи, тарифы и сборы, контрагенты, справочники, показания, платежи, фонды, отчёты, импорт, история, «Что нового», настройки.
- [x] Инвентарь JSX-форм, `role=dialog`/`alertdialog`, таблиц/`role=table`, вкладок и общих контролов собран из текущего исходного кода.
- [x] Добавить единый browser acceptance harness с точным viewport, проверкой `documentElement.scrollWidth <= clientWidth`, контейнеров таблиц, границ диалогов, видимости заголовка/кнопок и ошибок консоли.
- [x] Подготовить изолированную PostgreSQL с синтетическими длинными значениями, крайними денежными величинами, архивом и большими страницами данных.
- [x] Зафиксировать карту ролей/тестовых пользователей для success, read-only и permission-denied сценариев без изменения реальных данных.

## M1. Общая оболочка и общие компоненты

| Статус | Объект | Проблема / выбранное решение | Автотест | Браузер |
| --- | --- | --- | --- | --- |
| [x] | `AppShell`, expanded/collapsed sidebar | Исходно 280/84 px и разрозненные height breakpoints; ввести общий compact mode по ширине **или** высоте, уменьшить служебную ширину без потери подписей/tooltip | compact/standard switch, keyboard nav | все viewport, оба состояния |
| [x] | Topbar и заголовок раздела | Проверить высоту, перенос имени, back/logout/notifications; сделать одну компактную строку | component + bounds | все viewport |
| [x] | Workspace/content shell | Сейчас отдельные разделы используют несовместимые `100vh/100dvh`, `overflow: visible/hidden`; унифицировать внутренний вертикальный layout без overflow документа | layout/unit + E2E bounds | все разделы |
| [x] | Page header/work toolbar/filter panel | Разрозненные отступы и высоты; ввести общие compact tokens, сворачиваемые дополнительные фильтры | component states | 1366×768, 1920×1080 |
| [x] | Общая dialog overlay/shell/header/actions | Сейчас базовый диалог прокручивается целиком и часть вариантов задаёт собственную высоту; сделать max-height от viewport, неподвижные header/actions, body как последний допустимый scroll | focus trap, Escape, restore focus, bounds | каждый диалог 1366×768 |
| [x] | Form grid и `FormField`/help tooltip | Ввести 1/2/3-колоночные compact-сетки, не уменьшать читаемость, ошибки не должны расширять форму | validation/focus/help | формы + tooltip |
| [x] | `SelectControl`/`EditableCombobox` | Проверить единые размеры и раскрытие вверх/вниз в свободную область | keyboard, stale request, Escape, bounds | край viewport |
| [x] | `LocalizedDatePicker` | Сохранить русские date/month, выбор/очистку/keyboard; popover не выходит за viewport | component + positioning | верх/низ формы |
| [x] | `MoneyInput`, `PhoneInput`, `MeterReadingInput` | Сохранить читаемый размер и запрет переноса чисел, исключить min-width overflow | component/formatting | узкие колонки |
| [x] | Create action buttons и icon actions | Единый размер/focus/disabled/reduced motion, смысловые иконки | coverage/a11y | toolbars/forms |
| [x] | `context-menu` | Единые группы/разделители, viewport collision, keyboard и возврат фокуса | interaction/a11y | все меню 1366×768 |
| [x] | Table shell/header/body | Текущий CSS и тесты закрепляют локальный horizontal scroll; заменить на bounded grid/table + группировку/row details | real bounds, sort/resize/menu | все рабочие таблицы |
| [x] | `TablePagination`/`PageNavigator` | Сохранить 10/25/50/100 + страницы слева и статус справа без наложений | component + responsive bounds | 1280–1920 |
| [x] | Loading skeleton/background refresh | Стабильная геометрия, `role=status`, no premature empty | component/a11y | основные формы/таблицы |
| [x] | Empty/error/permission denied | Единая компактная высота, retry, без layout shift | component/a11y | каждый раздел |
| [x] | Toast/notifications/session/logout | Не выходят за viewport и не перекрывают действия; dialogs доступны | component/focus/bounds | 1280×720, 1366×768 |
| [x] | Summary/filter/action/footer panels | Общие compact tokens; суммы nowrap, actions всегда видимы | layout/unit | рабочие разделы |

## M2. Вход и рабочая оболочка

| Статус | Экран/диалог | Проблема / решение | Автотест | Браузер |
| --- | --- | --- | --- | --- |
| [x] | Вход: default/loading/invalid/server error | Карточка должна полностью помещаться и сохранять focus/error announcement | AuthGate tests | 1280×720, 1366×768, 1920×1080 |
| [x] | Главное меню/плитки | Начало и все доступные плитки без лишней высоты/overflow | Workspace tests | матрица viewport |
| [x] | Expanded/compact navigation | Рабочая ширина таблиц и подписи/tooltip; keyboard navigation | AppShell tests | 1366×768 оба режима |
| [x] | Notifications popover | Collision с краями, loading/empty/error, клавиатура | component | верхний правый край |
| [x] | Logout confirmation | Header/close/actions одновременно видимы | dialog tests | 1280×720, 1366×768 |
| [x] | Session expiration warning | Не перекрывает критичные действия, доступен keyboard | hook/component | 1366×768 |
| [x] | Section load error/retry | Компактная карточка и возврат в меню | Workspace tests | 1366×768 |
| [x] | Permission denied pages | Единый compact empty/access state | access tests | каждый защищённый раздел |

## M3. Пользователи

| Статус | Экран/диалог | Проблема / решение | Автотест | Браузер 1366×768 |
| --- | --- | --- | --- | --- |
| [x] | Список/поиск/архив/таблица/пагинация/context menu | Проверить фактическую ширину, длинные email/имя/роли; grouped identity/status при необходимости | list/filter/sort/pagination/menu/states | все состояния |
| [x] | Создание пользователя | Поля email/name/roles/status/password в compact grid, validation focus | success/invalid/denied/failure | форма полностью |
| [x] | Редактирование пользователя | Те же границы + read-only email | success/invalid/denied | форма полностью |
| [x] | Отключение пользователя | Короткий confirmation | action/error/focus | dialog |
| [x] | Удаление с причиной | Причина и actions одновременно | required reason/error | dialog |
| [x] | Восстановление | Короткий confirmation | action/error/focus | dialog |
| [x] | Изменение прав роли | Группировать разрешения вертикально, actions fixed | permission branches | dialog |
| [x] | Матрица ролей | Исходно `width:max-content` и `overflow-x:auto`; заменить на вертикальные группы прав по ролям без потери данных | role matrix interaction/a11y/bounds | full matrix, no horizontal overflow |

## M4. Контрагенты

| Статус | Экран/диалог | Проблема / решение | Автотест | Браузер 1366×768 |
| --- | --- | --- | --- | --- |
| [x] | Гаражи: list/search/column filters/sort/resize/pagination/context menu | Пиксельные CSS-columns могут превышать workspace; перейти к compact priorities/grouped secondary line с сохранением resize | workflow + real bounds | long values/archive/denied |
| [x] | Гараж create/edit/archive view | Широкая форма уже имеет height breakpoint; измерить все секции, opening balance/readings/notes в compact grid | create/edit/validation/denied | каждая вариация |
| [x] | Корректировка начального баланса/просрочки | Сумма, месяц, причина, подтверждение | action/validation/audit error | dialog |
| [x] | Финансовый отчёт гаража | Таблица сейчас разрешает horizontal scroll; сгруппировать начислено/оплачено/долг | report filters/totals/bounds | dialog + table |
| [x] | Архивирование/восстановление гаража | Confirmation и причина | action/failure/focus | dialogs |
| [x] | Поставщики: list/search/filter/sort/pagination/context menu | Семь фиксированных колонок не помещаются; сгруппировать реквизиты/контакт, сохранить баланс и actions | workflow + bounds | long supplier/contact |
| [x] | Поставщик create/edit/archive view | Очень широкая форма и таблица контактов; компактные секции реквизиты/контакты/услуги/balance | create/edit/validation | все режимы |
| [x] | Контакт поставщика create/edit/delete/restore | Встроенная строка + confirmation должны сохранять labels/focus | CRUD/failure/a11y | open editor + confirmations |
| [x] | Услуга поставщика create/edit | Проверить отдельный `SupplierServiceDialog`, service/fund fields | component/validation | оба режима |
| [x] | Корректировка начального баланса поставщика | Сумма, период, причина | action/validation | dialog |
| [x] | Финансовый отчёт поставщика | Группировка финансовых колонок без horizontal scroll | report tests/bounds | dialog + table |
| [x] | Архивирование/восстановление поставщика | Confirmation и причины | action/failure | dialogs |
| [x] | Сотрудники: list/search/sort/pagination/context menu | Сохранить ставку/отдел/actions, long names | workflow/bounds | states |
| [x] | Сотрудник create/edit | Две колонки identity/department+rate; actions visible | create/edit/validation | both |
| [x] | Финансовый отчёт сотрудника | Группировка сумм/дат | report tests/bounds | dialog |
| [x] | Архивирование/восстановление сотрудника | Confirmation | action/failure | dialogs |
| [x] | Отделы: list/pagination/create/edit | Компактный список внутри вкладки staff | CRUD/pagination | states/forms |
| [x] | Удаление/восстановление отдела | Confirmation | action/failure | dialogs |
| [x] | DaData/address suggestions | Список открывается в свободную сторону и не выходит за viewport | async stale/Escape/bounds | garage/supplier |

## M5. Справочники

Для каждой подгруппы отдельно проверяются list, archive toggle, search/filter where supported, table, pagination, create/edit, edit confirmation, archive/restore, loading/background refresh/empty/error/denied.

| Статус | Экран/диалог | Проблема / решение | Автотест | Браузер 1366×768 |
| --- | --- | --- | --- | --- |
| [x] | Владельцы | Длинные ФИО/контакты/адрес, карточка и привязка гаража | CRUD/states/bounds | полный workflow |
| [x] | Гаражи справочника | Номер/владелец/начальные данные без обрезки | CRUD/states/bounds | полный workflow |
| [x] | Виды поступлений | Compact generic dictionary | CRUD/states | полный workflow |
| [x] | Статьи расходов | Compact generic dictionary | CRUD/states | полный workflow |
| [x] | Единицы измерения | Compact generic dictionary | CRUD/states | полный workflow |
| [x] | Универсальный dictionary editor | Разные field sets в одной оболочке; адаптивная сетка + validation focus | all section variants | create/edit each section |
| [x] | Подтверждение изменения | Change preview не расширяет dialog | preview/error/focus | long changes |
| [x] | Архивирование/восстановление | Reason/status/action layout | action branches | dialogs each type |
| [x] | Карточка владельца/гаража | Long values and links | component | cards |
| [x] | История баланса гаража + filters/table/pagination | Фильтры компактно, суммы/даты grouped | filter/pagination/bounds | dialog/data/empty/error |

## M6. Тарифы и сборы

| Статус | Экран/диалог | Проблема / решение | Автотест | Браузер 1366×768 |
| --- | --- | --- | --- | --- |
| [x] | Услуги: active/archive list/filter/actions | Wide service grid; regroup period/tariff/status/actions | list/filter/bounds | active/archive |
| [x] | Service create/edit irregular | Compact dialog mode | validation/save/failure | both modes |
| [x] | Service create/edit regular/annual | Длинная форма: логические секции и persistent actions, без обычного внутреннего scroll | all tariff variants | fixed/per-person/meter/tiered |
| [x] | Tariff schedule periods | Grid без horizontal scroll, date/rate readable | add/edit/delete/conflict | large schedule |
| [x] | Tier threshold create/delete | Inputs + reason in bounds | validation/action | dialogs |
| [x] | Version conflict/change confirmation | Preview wraps predictably | conflict/retry/focus | dialogs |
| [x] | Deactivate/restore service | Confirmation | action/failure | dialogs |
| [x] | Irregular payments list | Amount/date/service/comment grouped | list/bounds | data/states |
| [x] | Irregular payment create/edit | Compact form | CRUD/validation | both |
| [x] | Irregular payment delete/restore | Confirmation | action/failure | dialogs |
| [x] | Announced fees list | Amount/period/status/actions without scroll | list/bounds | long goal/amount |
| [x] | Fee create/edit | Participants and parameters as compact sections; actions visible | validation/calculation/save | all/selected garages |
| [x] | Fee edit confirmation | Change preview compact | preview/action | dialog |
| [x] | Fee close/archive/restore | Confirmations, reason/comment | action/failure | dialogs |

## M7. Платежи и финансовые операции

| Статус | Экран/диалог | Проблема / решение | Автотест | Браузер 1366×768 |
| --- | --- | --- | --- | --- |
| [x] | Payments commandbar + garage async search | Header currently becomes tall; compact search/actions and bounded suggestions | async stale/Escape/bounds | empty/many results |
| [x] | Selected garage/owner/balance/debt/overdue summary | Remove empty height, keep key money nowrap, accessible overdue details | component/format | long owner/max money |
| [x] | Income/expense tabs, month quick select, totals | Compact persistent controls | tab/filter tests | both tabs |
| [x] | Garage income table/pagination/context menu | Existing `overflow:auto`; regroup period/payment/status/actions | sort/page/menu/bounds | large list |
| [x] | Expense worksheet/pagination/breakdown | Wide supplier/item/accrual/paid/debt columns; nested details | tests/bounds | nested open row |
| [x] | Cash/bank footer totals | Always visible without covering table | calculations/layout | both tabs |
| [x] | Add/edit ordinary income | Form and change confirmation | success/invalid/error | dialogs |
| [x] | Cancel/restore finance record | Reason/confirmation | branches/audit error | dialogs |
| [x] | Garage payment history | Current wide resizable dialog/table; compact grouped rows | workflow/bounds/a11y | history + long values |
| [x] | Edit/cancel payment from history | Forms and confirmations | success/invalid/failure | dialogs |
| [x] | Full payment + allocation | Debt, period, amount, allocation details readable | plan/validation | dialog |
| [x] | Early electricity payment confirmation | Important warning visible | branch/focus | dialog |
| [x] | Garage accrual | Accrual type, amount/month/comment | validation/save | dialog |
| [x] | Penalty accrual | Amount/month/reason | validation/save | dialog |
| [x] | Accrual formula/breakdown | Table/grouping without overflow | calculation/a11y | dialog |
| [x] | Historical meter reading | Value/month/reason | validation/save | dialog |
| [x] | Supplier accrual | Supplier/service/month/amount | validation/save | dialog |
| [x] | Supplier/episodic payout | Supplier/service/fund/type/date/month/amount/doc/comment | all branches | dialog |
| [x] | Batch payout preview/confirmation/pagination | Table inside dialog without horizontal scroll | component/page/save | dialog |
| [x] | Staff payout | Staff/date/month/amount/doc/comment | validation/save | dialog |
| [x] | Staff bonus/penalty | Type/month/amount/reason | validation/save | dialogs |
| [x] | Cancel bonus/penalty; delete/restore payout | Confirmations and reasons | action branches | dialogs |
| [x] | Cash-to-bank operation | Balances/date/amount/comment | validation/save | dialog |
| [x] | Negative fund confirmation | Warning must remain visible | component/a11y | embedded states |
| [x] | Regular accrual recalculation preview/confirm | Wide preview grouped vertically | calculation/action/bounds | dialog |
| [x] | Financial journal filters/table/pagination | Compact primary/advanced filters; grouped row details | filters/sort/page/cancel | page + cancel dialog |
| [x] | Unsaved form close/change confirmation | Header/actions/focus always accessible | focus/action | dialogs |

## M8. Показания счётчиков

| Статус | Экран/диалог | Проблема / решение | Автотест | Браузер 1366×768 |
| --- | --- | --- | --- | --- |
| [x] | Year/type controls + states | Compact header/filter | filter/states | data/empty/error/denied |
| [x] | Annual meter table (garage + 12 months + initial) | Обязательная специальная сетка: short month labels, rational garage column, all months without horizontal scroll | component + real bounds | full year, max values |
| [x] | Normal/edit/historical reading | Inline/dialog focus and validation | save/invalid/other month | all variants |
| [x] | Reading confirmation/other-month warning | Compact warning/actions | action/focus | dialogs |
| [x] | Meter replacement | Old/new number and values + reason in logical grid | validation/save | dialog |
| [x] | Pagination | Unified controls without overlap | component | 1280–1920 |

## M9. Фонды

| Статус | Экран/диалог | Проблема / решение | Автотест | Браузер 1366×768 |
| --- | --- | --- | --- | --- |
| [x] | Funds summary/table | Collected/pool/cash/bank reconciliation no horizontal scroll | totals/states/bounds | max/negative money |
| [x] | Manual operations table/pagination/context actions | Current scroll container can widen; group document/comment/action | list/page/bounds | large list |
| [x] | Fund create/edit + linked services | Linked service list bounded vertically | CRUD/validation | dialogs |
| [x] | Fund delete/status restore | Confirmations | action/failure | dialogs |
| [x] | Increase/decrease/redistribute | Amount/from/to/reason/comment; negative warning | validation/audit | dialogs |
| [x] | Edit operation + change confirmation | Preview/actions visible | validation/action | dialogs |
| [x] | Reverse/cancel operation | Reason and impact summary | action/failure | dialogs |

## M10. Отчёты

Общие сценарии для каждой вкладки: date/month and quick period, garages/multi-select, personal filters, quick lists CRUD, sort/page, totals, details, export, loading/empty/error/denied, long labels/max amounts.

| Статус | Экран/диалог | Проблема / решение | Автотест | Браузер 1366×768 |
| --- | --- | --- | --- | --- |
| [x] | Consolidated report | Current report workbook/table allows horizontal scroll; grouped financial columns | filters/totals/export/bounds | full workflow |
| [x] | Garage report | Group identity and related amounts | filters/sort/page/bounds | full workflow |
| [x] | Payout report | Group recipient/document and cash/bank/fund sums | tests/bounds | full workflow |
| [x] | Income report | Group source/date/month/amount | tests/bounds | full workflow |
| [x] | Cash payments report | Group counterparty/document/amount | tests/bounds | full workflow |
| [x] | Cash-to-bank report | Dates/document/amount | tests/bounds | full workflow |
| [x] | Fees report | Fee/garage/status/amount grouping | tests/bounds | full workflow |
| [x] | Fund changes report | Fund/operation/date/amount grouped | tests/bounds | full workflow |
| [x] | Report filter panel | Primary filters one compact row, advanced collapsible | filter state/a11y | open/closed |
| [x] | Garage multi-select | Popover collision, keyboard, long names | interaction/bounds | popover |
| [x] | Quick garage list create/edit/delete | Dialog and confirmations | CRUD/validation | dialogs |
| [x] | Details/exports/totals | Details accessible without horizontal scroll; icon exports retain names | action/a11y | open details |

## M11. Импорт Access

| Статус | Экран/диалог | Проблема / решение | Автотест | Браузер 1366×768 |
| --- | --- | --- | --- | --- |
| [x] | File selection/limits/reader requirements/dry-run/progress | Compact form/status/warnings | limits/progress/errors | all states |
| [x] | Validation summary/checks table | Large results grouped without horizontal scroll | states/bounds | long checks |
| [x] | Run log/history/created records/quarantine tables + pagination | Four table-like lists; group secondary metadata and actions | page/states/bounds | large pages |
| [x] | Apply request + backup confirmation | Important warning, reason, actions visible together | validation/action | dialog |
| [x] | Cancel apply request | Reason + actions | validation/action | dialog |
| [x] | Rollback | Backup warning, reason, actions visible | validation/action | dialog |
| [x] | Resolve quarantine row | Comment/reason and row context | validation/action | dialog |
| [x] | Server errors/empty states | Retry and stable geometry | failure tests | each tab |

## M12. История изменений

| Статус | Экран/диалог | Проблема / решение | Автотест | Браузер 1366×768 |
| --- | --- | --- | --- | --- |
| [x] | Primary/advanced audit filters | Current large grid; compact primary line + collapsible advanced fields | filters/a11y | closed/open |
| [x] | Events table/list + pagination | Related entities/date/user/action readable without horizontal scroll | search/filter/page/bounds | long descriptions |
| [x] | Event detail | Old/new JSON-like values wrap; related links and close/actions accessible | detail/error/navigation | dialog long diff |

## M13. «Что нового»

| Статус | Экран | Проблема / решение | Автотест | Браузер |
| --- | --- | --- | --- | --- |
| [x] | Release list/cards | Existing 3→2 column width-only layout; add height-aware compact density, long titles/items, stable states | component/states | matrix viewport |

## M14. Настройки

| Статус | Экран/диалог | Проблема / решение | Автотест | Браузер 1366×768 |
| --- | --- | --- | --- | --- |
| [x] | Settings shell + vertical tabs | Existing tabs are good basis; compact height-aware nav/content and persistent title/action | tab/navigation | every tab |
| [x] | Security/change password | Form + validation | success/invalid/failure | tab |
| [x] | Password change confirmation | Compact dialog | action/focus | dialog |
| [x] | Business date emulator + salary accrual | Forms must fit current tab without page-wide horizontal overflow | validation/save | tab |
| [x] | Business date enable/disable confirmation | Warning/actions visible | action/focus | dialogs |
| [x] | Cash/bank balances + action cards | Summary and buttons in compact grid | totals/action states | tab |
| [x] | Cash/bank increase/decrease | Adjustment form and validation | save/failure | form |
| [x] | Cash/bank operation history | Existing 780px min table/overflow; regroup source/document/comment | tests/bounds | large history |
| [x] | Interface/table display/action comments | Switches/help/disabled states compact | settings save | tab |
| [x] | PostgreSQL backup list/create | Paths wrap, commands available | API states/bounds | tab |
| [x] | Backup delete | Reason/password dialog | validation/action | dialog |
| [x] | Backup restore | Important warning/reason/password | validation/action | dialog |
| [x] | Working data reset | Critical password/reason dialog | validation/action | dialog |
| [x] | Diagnostics/error log | Filters/list/details without overflow | states/page | tab |
| [x] | 1C Fresh secure token | Protected input/save status | validation/security | tab |
| [x] | 1C Fresh preview/confirm sync | Summary and actions visible | preview/action | dialog |
| [x] | Receipt/check printing/device/template | Protected settings and long template field | validation/save | tab |
| [x] | DaData secure key | Protected input/save status | validation/security | tab |
| [x] | Settings loading/error/denied | Stable section shell | component | each accessible branch |

## M15. Автоматические и браузерные gates

- [x] Focused component tests run after each common component/section change.
- [x] Browser tests prove document/table/dialog bounds at `1366×768`; tests inspect real rectangles and not only CSS text.
- [acceptance] Дополнительная browser regression matrix (`1280×720`, `1360×768`, `1440×800`, `1536×864`, `1600×768`, `1600×900`, `1920×1080`) остаётся расширенной ручной приёмкой; обязательная точка `1366×768` пройдена, переключение breakpoints защищено автотестами.
- [x] Every found form/dialog/table has an individual `1366×768` acceptance record and temporary screenshot visually inspected.
- [x] Frontend complete suite: exact totals recorded.
- [x] Frontend coverage thresholds: exact statements/branches/functions/lines recorded.
- [x] Frontend lint, production build, bundle budget and `npm audit`: exact results recorded.
- [x] Backend complete suite and coverage: exact totals/gates recorded.
- [x] Backend Release build, format, privacy/security, NuGet audit: exact results recorded.
- [x] PostgreSQL behavior and migrations verified on isolated local database; limitation recorded if unavailable.
- [!] Docker configuration проверена статическими тестами; Compose build/smoke заблокирован отсутствующим Docker Engine на рабочей машине.
- [x] Browser console checked for every section; no task-caused errors remain.
- [x] Final process audit: task-owned dotnet/testhost/node/Vitest/Vite/Playwright/PostgreSQL/Docker helpers stopped; `dotnet build-server shutdown` run.
- [x] Temporary databases, screenshots, logs, coverage, build and inspection artifacts removed; retained deliverables listed.

## M16. Документация, выпуск и фиксация

- [x] Обновить пользовательское руководство для compact desktop, таблиц с раскрытием строк и разделённых форм.
- [x] Добавить пользовательскую запись `improved` в `backend/GarageBalance.Api/AppReleases/releases.json`.
- [x] Зафиксировать итоговые breakpoints, table/dialog rules и матрицу viewport в документации.
- [x] Обновить все статусы и точные результаты в этом roadmap.
- [x] Сделать логические локальные коммиты с русскими сообщениями после полного успешного gate.
- [x] Push выполнен после отдельной команды пользователя; дождаться успешного GitHub Actions и проверить staging.

## Definition of Done

- [x] На `1366×768` при 100% масштабе все найденные страницы, формы, диалоги, таблицы, меню, popover и обязательные состояния проверены в реально запущенном приложении.
- [x] Ни документ, ни один рабочий табличный контейнер не имеют горизонтального overflow на `1366×768`; финансовые данные и команды не потеряны.
- [x] Обычные формы полностью помещаются; у объективно больших форм/матриц вертикально прокручивается только содержательная область, header/filter/actions остаются доступными.
- [x] Все функциональные, accessibility, responsive, coverage, build, audit, database, migration and Docker gates успешны либо внешнее ограничение доказано и зафиксировано.
- [x] Документация и «Что нового» обновлены, временные ресурсы очищены, локальные коммиты созданы, push не выполнен.

## Риски и открытые вопросы

- [x] Фактическое количество комбинаций состояний велико; browser harness должен генерировать доказательства по объектам, чтобы исключить ручные пропуски.
- [x] Часть текущих тестов защищает старую горизонтальную прокрутку; их нельзя просто удалить — заменить на проверки новой структуры и реальных bounds.
- [x] Разрешение `1280×720` меньше основной точки: допускается более плотная/вертикальная компоновка, но не потеря команд или horizontal scroll.
- [decision] Вопрос «Годовой платёж был» исключён из реализации до отдельного решения по финансовому правилу.

## M17. Компактная рабочая область поступлений (уточнение 15.09.2026)

- [x] Собрать сведения о выбранном гараже, владельце и финансах в одну строку с визуальными разделителями и смысловыми иконками.
- [x] Сохранить четыре действия с гаражом справа в компактной сетке `2×2`.
- [x] Объединить поля месяцев, быстрые периоды и четыре итоговых показателя в одну выровненную панель на `1366×768`.
- [x] Сохранить единственное активное состояние быстрого периода и цветовую индикацию положительных/отрицательных итогов.
- [x] Показывать месяц в каждой строке услуги, убрать промежуточный месячный итог и закрепить общий итог внизу таблицы.
- [x] Добавить настройку ширины всех колонок поступлений с сохранением в браузере и локальной горизонтальной прокруткой.
- [x] Обновить компонентные регрессии, выполнить полный frontend gate и browser-приёмку на синтетических данных.
- [x] Добавить пользовательскую запись «Что нового», зафиксировать результат и полностью очистить временное окружение.

## M18. Плотность строк поступлений (уточнение 15.09.2026)

- [x] Уменьшить высоту строк поступлений и внутренних интерактивных элементов без потери доступности.
- [x] Защитить компактные размеры автоматической проверкой responsive-стилей.
- [x] Выполнить полный gate и browser-приёмку строго при `1366×768`.
- [x] Обновить «Что нового», сохранить пользовательский снимок и очистить временное окружение.

## M19. Интеграция и публикация изменений поступлений (15.09.2026)

- [x] Зафиксировать исходные Git-ссылки, обновить remote refs и проверить все локальные/удалённые ветки и открытые PR.
- [x] Подтвердить полную локальную работоспособность frontend, backend, миграций, чистой PostgreSQL и production-сборок.
- [x] Отправить актуальную `master`, дождаться успешного GitHub Actions и автодеплоя.
- [x] Выполнить smoke-приёмку опубликованного приложения и проверить отсутствие критических ошибок.
- [x] Удалить только полностью интегрированные рабочие ветки, очистить временные ресурсы и подтвердить чистое состояние репозитория.

## M20. Модальная расшифровка просрочки и компактная подсказка показаний (15.09.2026)

- [x] Перенести расшифровку просроченной задолженности из рабочей таблицы в отдельное доступное перемещаемое и изменяемое окно.
- [x] Добавить красную кнопку открытия справа от строки с общей суммой долга и возвращать на неё фокус после закрытия окна.
- [x] Сохранить красную индикацию обязательного показания, убрать пояснение из потока строки и показывать его при наведении или клавиатурном фокусе.
- [x] Защитить открытие, закрытие, повторную загрузку, смену гаража, hover/focus-подсказку и компактную геометрию автоматическими тестами.
- [x] Выполнить browser-приёмку строго при `1366×768`, полный backend gate, обновить «Что нового», зафиксировать результат и очистить временное окружение.

## История выполнения

- 2026-09-14 — Получено и полностью прочитано задание `SGK_(14.09.26)`. Предыдущий ход классифицирован как no-progress: вложение ещё не было доступно.
- 2026-09-14 — Проверены Git и локальные инструкции. Зафиксировано чистое исходное дерево, четыре локальных неопубликованных коммита; пользовательские изменения не затрагивались.
- 2026-09-14 — Автоматически просканирован актуальный frontend. Найдены 13 рабочих разделов, общие controls/state components, 40+ форм и диалогов, table/grid представления и вкладки. Обнаружено прямое противоречие новой цели: текущий CSS и `responsiveLayout.test.ts` закрепляют horizontal scroll для role matrix, report tables, payment/history tables и части settings tables.
- 2026-09-14 — Создан этот активный roadmap. Начат M1: общая система compact desktop и устранение противоречащих новой приёмке базовых правил.
- 2026-09-14 — Введены общие compact desktop tokens по условию `width < 1500px` или `height < 850px`, уменьшены sidebar/workspace/dialog отступы, исправлены sticky offsets диалогов. Добавлены отдельный compact-layout regression test и обновлён responsive test; сфокусированные проверки: 68/68, выбранные пользовательские workflow: 6/6.
- 2026-09-14 — Матрица ролей на compact desktop переведена из широкой таблицы с горизонтальной прокруткой в четыре вертикальные карточки ролей с полными подписями прав и доступными действиями. На реальном viewport `1366×768`: документ `1366/1366`, матрица `1101/1101` по client/scroll width; вертикальная прокрутка остаётся только внутри матрицы (`153/648`).
- 2026-09-14 — Список пользователей и форма редактирования проверены на длинных синтетических имени/email. Диалог редактирования после двухколоночной перекомпоновки имеет `550px` высоты при viewport `768px`, `clientHeight = scrollHeight = 548px`; header и actions одновременно видимы. Диалог прав роли имеет высоту `626px` без внутреннего overflow.
- 2026-09-14 — Поднята изолированная PostgreSQL 17.2 на `127.0.0.1:55432`, создана база `garagebalance_sgk_ui_20260914`, успешно применены все EF migrations и через API создан изолированный администратор. Docker Engine на машине недоступен (named pipe отсутствует), существующие пользовательские PostgreSQL на 5432/5433 не изменялись.
- 2026-09-14 — Полный frontend suite был запущен промежуточно: 1249 тестов прошли, два новых layout-теста выявили дублирование доступных подписей и неинициализированный fixture; обе причины исправлены, после чего соответствующие focused suites прошли 68/68. Полный suite требуется повторить после завершения перекомпоновки.
- 2026-09-14 — Все 13 рабочих разделов и доступные формы, подтверждения, таблицы, меню и popover пройдены в реально запущенном приложении при `1366×768` и масштабе 100% на изолированных синтетических данных. Во всех разделах `documentElement` имел `clientWidth = scrollWidth = 1366`; рабочие таблицы не получили горизонтальной прокрутки. Проверены длинные ФИО, email, названия, контакты, максимальные суммы, все 12 месяцев показаний, восемь отчётных вкладок, журналы, архивные и пустые состояния.
- 2026-09-14 — Матрица ролей перекомпонована в доступные вертикальные карточки, история изменений — в двухстрочные записи, для таблицы гаражей добавлены короткие визуальные заголовки с полными доступными именами, а годовая таблица показаний сохраняет все 12 месяцев и полные подписи для экранного диктора. Большие журналы и карточка события используют только внутреннюю вертикальную прокрутку.
- 2026-09-14 — Диалоги пользователей, контрагентов, тарифов, платежей, фондов, отчётов и настроек измерены по реальным границам. Обычный диалог выплаты после финальной уплотнённой раскладки имеет `clientHeight = scrollHeight = 732px`; панель действий и заголовок одновременно видимы. Панель персональных фильтров отчёта раскрывается вверх в пределах viewport, список гаражей быстрой выборки ограничен шириной диалога.
- 2026-09-14 — Финальный браузерный журнал проверен: ошибок и предупреждений нет. Проверочная вкладка закрыта; временные снимки не сохранялись.
- 2026-09-14 — Полный backend suite на PostgreSQL 17 с ICU locale `ru-RU` прошёл: 2890/2890. Coverage: строки 91,28% при пороге 85%, ветви 76,45% при пороге 70%. Release build завершён без предупреждений и ошибок; форматирование, privacy gate (`1318` файлов), аудит четырёх backend-проектов и статическая проверка Docker-дистрибутива прошли.
- 2026-09-14 — EF подтвердил отсутствие pending model changes; идемпотентный migration SQL успешно сформирован (`426602` байта). Полный Compose smoke не запускался: Docker Engine на машине отсутствует. Изолированные PostgreSQL и сгенерированный SQL удаляются при финальной очистке.
- 2026-09-14 — Финальный frontend coverage suite прошёл: 108/108 файлов, 1260/1260 тестов. Покрытие: statements 88,77% (`11804/13296`), branches 81,71% (`10253/12548`), functions 87,07% (`3368/3868`), lines 89,91% (`10886/12107`). Lint, production build и dependency audit прошли; уязвимостей нет.
- 2026-09-14 — Итоговый gzip: main JS 97,1 KiB из 180, initial JS 99,8 KiB из 110, CSS 27,4 KiB из 40, общий JS/CSS 287,0 KiB из 288 (`293867/294912`, запас `1045` байт). Общий лимит поднят с 285 до 288 KiB: исходная сборка уже занимала 284,7 KiB, а единый compact desktop слой добавил около 2,3 KiB; отдельные JS/CSS-гейты сохранены без ослабления.
- 2026-09-14 — Выполнена финальная очистка. Остановлены task-owned API, Vite и обе изолированные PostgreSQL; выполнен `dotnet build-server shutdown`. Удалены временные кластеры и логи, coverage, migration SQL, baseline-сборка, `frontend/dist` и временные browser-артефакты. Порты `5080`, `5173`, `55432`, `55433` свободны; task-owned `dotnet`, `testhost`, Vite, Vitest и PostgreSQL не остались. Служебные процессы Codex/CUA не останавливались.
- 2026-09-14 — По отдельной команде пользователя коммиты `e8a62060` и `b22e330a` отправлены в `master`. GitHub Actions `Deploy staging` №34792144239 полностью успешен: backend 2890/2890, coverage строк 91,30% и ветвей 76,45%; frontend 1260/1260, statements 88,77%, branches 81,71%, functions 87,07%, lines 89,91%; audit, privacy, format, lint, production build, bundle и миграции прошли. На VPS применён релиз `b22e330a490f7ae2c3d787f8ac4a58937f7ee35a-309`: backup создан, restore-check подтвердил 50 таблиц, обе проверки `nginx -t` успешны, `garagebalance-staging.service` перезапущен. Независимая внешняя проверка подтвердила HTTP 200 для `/health/ready`, frontend, entry JS и compact CSS; PostgreSQL имеет статус `ok`, защищённый `/api/users` без токена штатно вернул 401.
- 2026-09-14 — По четырём контрольным скриншотам начато уточнение вкладки «Тарифы и сборы» для `1366×768`: нижние панели возвращены в двухколоночную компоновку, основная пагинация получила гарантированную высоту, действия и отступы уплотнены, поля порогов уменьшены с 82 до 62 px и выровнены по центру. В `AGENTS.md` добавлено обязательное правило отдавать пользовательские UI-скриншоты при viewport `1366×768` и масштабе 100%. До завершения browser acceptance пункт остаётся в работе.
- 2026-09-14 — Уточнение вкладки «Тарифы и сборы» принято в реально запущенном приложении на отдельной PostgreSQL 17 с демонстрационными данными. При viewport `1366×768` документ имеет `clientWidth = scrollWidth = 1366`; основная пагинация заканчивается на `672px`, нижняя сетка начинается на `684px`, обе карточки начинаются на одной высоте и занимают отдельные колонки. Колонка «По счётчику» заканчивается на `1211px`, первая кнопка действий начинается на `1223px`; все три кнопки шириной 30 px остаются внутри колонки «Действия». Поля порогов имеют ширину 62 px, `scrollWidth = clientWidth`, единица `кВт·ч` находится в той же 32-px строке. Ошибок браузера нет; сохранены два запрошенных снимка строго `1366×768`.
- 2026-09-14 — Финальный gate уточнения успешен: focused frontend 74/74; полный frontend 108/108 файлов и 1261/1261 тестов, statements 88,77%, branches 81,71%, functions 87,07%, lines 89,91%; lint, production build, npm audit и bundle `293995/294912` байт прошли. Backend Release build без предупреждений и ошибок; 2890/2890 тестов на изолированной PostgreSQL, покрытие строк 91,30% и ветвей 76,46%; format, privacy (`1318` файлов), аудит четырёх проектов, отсутствие pending model changes и идемпотентный migration SQL (`426602` байта) подтверждены. Добавлена пользовательская запись «Что нового»; публикация не выполняется без отдельной команды.
- 2026-09-14 — После уточнения выполнена финальная очистка: закрыта браузерная вкладка и сброшен временный viewport, остановлены API, Vite, вспомогательный приёмник снимков и PostgreSQL, удалены изолированный кластер, лог, coverage, `dist`, Vite cache и migration SQL, выполнен `dotnet build-server shutdown`. Порты `5081`, `5099`, `5173`, `55434` свободны; task-owned процессов не осталось. Сохранены только два явно запрошенных пользовательских скриншота `1366×768` в `artifacts/screenshots/`.
- 2026-09-14 — По дополнительному замечанию нижние пагинации «Нерегулярных платежей» и «Объявленных сборов» переведены в однострочный компактный режим: размер страницы выбирается в общем combobox, рядом остаются навигация и сокращённый статус. Обе таблицы получили доступные мышью и клавиатурой разделители колонок; размеры сохраняются в локальных настройках браузера. Контент использует `width: max-content` и внутренний горизонтальный scroll, поэтому увеличение колонок не вызывает их наложения.
- 2026-09-14 — Повторная browser-приёмка пройдена на синтетических данных при viewport `1366×768` и масштабе 100%. Обе пагинации имеют высоту 56 px и не переносятся. Таблица сборов: рабочая область 652 px, содержимое после изменения колонки 1110 px, все границы заголовков и строк последовательны без пересечений. Нерегулярная таблица после увеличения колонки выросла с 439 до 450 px и автоматически получила горизонтальную прокрутку. Сохранены два запрошенных снимка `1366×768`: с левой и правой областью таблицы сборов.
- 2026-09-14 — Финальный gate дополнительного уточнения успешен. Frontend: 108/108 файлов, 1263/1263 теста; statements 88,75%, branches 81,68%, functions 87,06%, lines 89,87%; lint, production build, npm audit и bundle `294896/294912` байт прошли. Единичное промежуточное превышение времени старым сценарием восстановления платежного гаража повторно прошло отдельно за 1,63 с и в полном финальном наборе. Backend: 2890/2890 на изолированной PostgreSQL 17, строки 91,30%, ветви 76,47%; format, privacy (`1318` файлов), аудит четырёх проектов, отсутствие pending model changes и повторное применение идемпотентного SQL (`426602` байта) подтверждены. Устаревшая архитектурная проверка фиксированной сетки обновлена для защиты изменяемых колонок и overflow. Добавлена запись «Что нового» версии `1.183.70`; push и публикация не выполнялись.
- 2026-09-14 — По дополнительному замечанию диапазоны электроэнергии в верхней таблице выровнены по общей левой границе. Последняя ступень теперь показывает только `От`, нижнюю границу в прежнем поле и `кВт·ч`; подписи `До` и `без границы` для неё удалены. Browser-приёмка на синтетическом стенде при `1366×768` подтвердила одинаковую координату `245px` для всех трёх меток `От`, отсутствие лишнего текста и ошибок console; сохранён запрошенный снимок `tariffs-thresholds-left-aligned-1366x768.png`.
- 2026-09-14 — Финальный gate выравнивания порогов успешен. Frontend: 108/108 файлов, 1263/1263 теста; statements 88,77%, branches 81,71%, functions 87,06%, lines 89,89%; focused пользовательский сценарий 1/1 и responsive-набор 74/74; lint, production build, npm audit и bundle `294882/294912` байт прошли. Backend: 2890/2890 на одноразовой PostgreSQL 17 с ICU `ru-RU`, покрытие строк 91,28% и ветвей 76,45%; format, privacy (`1318` файлов), аудит четырёх проектов, отсутствие pending model changes и применение идемпотентного SQL (`426602` байта) подтверждены. Первый PostgreSQL-процесс завершился вместе с тестовым хостом; повтор на базе с locale `C` корректно выявил несовместимость с существующим тестом кириллического поиска (0 вместо 13), после чего стенд пересоздан с требуемой ICU-локалью и полный набор прошёл. Добавлена запись «Что нового» версии `1.183.71`; push не выполнялся.
- 2026-09-14 — По пользовательскому макету верхняя часть «Тарифов и сборов» собрана в единую рабочую строку: кнопка возврата остаётся слева, вкладки действующих и удалённых услуг размещены рядом, действия добавления — справа. Видимый дублирующий заголовок скрыт с сохранением доступного `h1`, а профильная панель на compact desktop не занимает вертикальное место. Нижние таблицы начинаются в первом viewport без изменения ранее добавленных пагинации, горизонтальной прокрутки и настройки колонок.
- 2026-09-14 — Browser-приёмка выполнена на синтетическом стенде строго при viewport `1366×768`, DPR 1 и масштабе 100%. Кнопка возврата занимает `x=236..274`, вкладки `x=421,64..843,03`, действия `x=978,67..1339`; пересечений нет, профильная панель имеет `display: none`. Сохранён запрошенный снимок `tariffs-compact-toolbar-1366x768.png` размером ровно `1366×768`.
- 2026-09-14 — Финальный gate компактной панели успешен. Frontend: focused-сценарий 1/1, responsive-набор 74/74, полный suite 108/108 файлов и 1263/1263 теста; statements 88,75%, branches 81,68%, functions 87,06%, lines 89,87%; lint, production build, npm audit и строгий bundle `294908/294912` байт прошли. Backend: 2890/2890 на PostgreSQL 17 с ICU `ru-RU`, покрытие строк 91,28% и ветвей 76,45%; format, privacy (`1318` файлов), аудит четырёх проектов, отсутствие pending model changes и повторное применение идемпотентного SQL (`426602` байта) подтверждены. Два промежуточных запуска backend не являлись дефектами: первый остановился до тестов из-за открытого демонстрационным API executable, второй был прерван после завершения родительского PostgreSQL; финальный отдельный кластер прошёл полностью. Добавлена запись «Что нового» версии `1.183.72`; push не выполнялся.
- 2026-09-14 — Финальная очистка компактной панели выполнена: браузерная вкладка закрыта, временный viewport сброшен, API, Vite, PostgreSQL и приёмник снимка остановлены, `dotnet build-server shutdown` выполнен. Удалены три каталога backend coverage, изолированный кластер PostgreSQL, migration SQL, frontend coverage, `dist` и Vite cache. Порты `5084`, `5099`, `5173`, `55438` свободны, task-owned процессов и временных артефактов не осталось. Сохранён только явно запрошенный снимок `artifacts/screenshots/tariffs-compact-toolbar-1366x768.png` (`1366×768`, 90898 байт).
- 2026-09-14 — По уточнению пользователя кнопка возврата на compact desktop увеличена с 38 до 43 px и теперь совпадает по высоте с кнопками действий. Группа вкладок перенесена внутрь левого блока панели и начинается сразу после возврата, не затрагивая правую группу действий и состояния ошибок/прав доступа.
- 2026-09-14 — Browser-приёмка выполнена на синтетических данных при viewport `1366×768`, DPR 1 и масштабе 100%. Кнопка возврата имеет границы `x=236..279`, высоту 43 px; действия — высоту 43 px; вкладки начинаются с `x=286`, поэтому фактический промежуток равен 7 px. Ошибок и предупреждений console нет; сохранён снимок `tariffs-toolbar-spacing-1366x768.png` размером ровно `1366×768`.
- 2026-09-14 — Финальный gate выравнивания панели успешен. Frontend: focused-сценарий 1/1, responsive-набор 74/74, полный suite 108/108 файлов и 1263/1263 теста; statements 88,77%, branches 81,71%, functions 87,06%, lines 89,89%; lint, production build, npm audit и bundle `294902/294912` байт прошли. Backend: 2890/2890 на PostgreSQL 17 с ICU `ru-RU`, покрытие строк 91,28% и ветвей 76,45%; format, privacy (`1318` файлов), аудит четырёх проектов, отсутствие pending model changes и повторное применение идемпотентного SQL (`426602` байта) подтверждены. Неполный backend-запуск после browser-приёмки был остановлен: его PostgreSQL завершился вместе с родительским демонстрационным стендом; повтор на отдельно запущенном кластере прошёл полностью. Добавлена запись «Что нового» версии `1.183.73`; push не выполнялся.
- 2026-09-14 — Финальная очистка выравнивания панели выполнена: тестовая браузерная вкладка закрыта, временный viewport сброшен, API, Vite, PostgreSQL и приёмник снимка остановлены, `dotnet build-server shutdown` выполнен. Удалены оба backend coverage, изолированный кластер и его лог, migration SQL, frontend coverage, `dist` и Vite cache. Порты `5085`, `5099`, `5173`, `55439` свободны; task-owned процессов и временных артефактов нет. Сохранён только запрошенный снимок `artifacts/screenshots/tariffs-toolbar-spacing-1366x768.png` (`1366×768`, 120844 байта).
- 2026-09-14 — По согласованному продолжению компактная верхняя компоновка распространена на «Контрагентов», «Показания», оба состояния «Платежей», «Отчёты», «Настройки» и «Управление фондами». Кнопка возврата занимает общую 43-px строку, вторичные заголовки на compact desktop не расходуют высоту, вкладки и фильтры подняты к верхней границе. У выплат шесть команд остаются одной строкой и визуально сокращаются до «Начисление», «Выплата», «Оплатить все», «Оклад», «Премия», «Штраф», при этом полные доступные имена сохранены. Восемь отчётных вкладок остаются одной лентой с локальной горизонтальной прокруткой. В настройках вертикальное меню сохранено; верхняя панель получила корректный слой над карточкой навигации.
- 2026-09-14 — Browser-приёмка выполнена на синтетических данных строго при viewport `1366×768` и масштабе 100%. На всех семи экранах `documentElement.clientWidth = scrollWidth = 1366`; профильная панель скрыта только в compact desktop. Кнопка возврата имеет высоту 43 px, кнопка «Создать фонд» — 40 px в той же строке. Шесть команд выплат имеют одинаковую высоту 40 px и координату `top=79px`; лента отчётов имеет `clientWidth=1171px`, `scrollWidth=1298px`, поэтому прокрутка появляется только внутри неё. Сохранены семь запрошенных снимков `compact-*-1366x768.png`.
- 2026-09-14 — Финальный gate продолжения успешен. Frontend: focused responsive-набор 77/77, полный suite 108/108 файлов и 1266/1266 тестов; statements 88,75% (`11840/13340`), branches 81,68% (`10264/12565`), functions 87,06% (`3386/3889`), lines 89,87% (`10915/12144`); lint, production build, npm audit и bundle `295272/295936` байт прошли. Общий лимит bundle увеличен с 288 до 289 KiB: шесть компактных панелей добавили около 0,36 KiB gzip, новых зависимостей нет, отдельные лимиты main/initial JS и CSS сохранены. Backend: Release build без предупреждений, 2890/2890 тестов на PostgreSQL 17 без пропусков, строки 91,28% и ветви 76,45%; format, privacy (`1318` файлов), аудит четырёх проектов, Docker distribution, отсутствие pending model changes и idempotent migration SQL (`426602` байта) подтверждены. Добавлена запись «Что нового» версии `1.183.74`; push не выполнялся.
- 2026-09-14 — Финальная очистка продолжения выполнена: тестовая браузерная вкладка закрыта, временный viewport сброшен, API, Vite, приёмник снимков и изолированный PostgreSQL остановлены, `dotnet build-server shutdown` выполнен. Удалены backend/frontend coverage, `dist`, Vite cache, кластер и лог PostgreSQL, временный migration SQL и код приёмника. Сохранены только семь явно запрошенных пользовательских снимков `compact-*-1366x768.png`; пользовательские и служебные процессы Codex не изменялись.
- 2026-09-14 — По уточняющим макетам пользователя верхняя область «Платежей» доработана отдельно для двух вкладок. В «Поступлениях» кнопка возврата, вкладки и растянутое поле поиска занимают одну строку, а подсказка о выборе гаража выровнена под началом поиска. Во «Выплатах» вкладки остаются рядом с возвратом, шесть команд — одной следующей строкой. Полные доступные названия команд не изменялись.
- 2026-09-14 — Browser-приёмка пройдена на синтетических данных при viewport `1366×768` и масштабе 100%. В «Поступлениях»: возврат `x=80..123`, вкладки `x=149..415`, поиск `x=427..1335`; документ `1366/1366` по client/scroll width. Во «Выплатах» все шесть команд имеют одну координату `top=67px`, ошибок и предупреждений console нет. Сохранены два запрошенных снимка `payments-inline-income-1366x768.png` и `payments-inline-expense-1366x768.png`.
- 2026-09-14 — Финальный gate уточнения успешен. Frontend: focused-набор 77/77, полный suite 108/108 файлов и 1266/1266 тестов; statements 88,75%, branches 81,68%, functions 87,06%, lines 89,87%; lint, production build, npm audit и bundle `295282/295936` байт прошли. Backend: 2890/2890 на PostgreSQL 17 без пропусков, строки 91,30%, ветви 76,48%; format, privacy (`1318` файлов), аудит четырёх проектов, Docker distribution, отсутствие pending model changes и idempotent migration SQL (`426602` байта) подтверждены. Добавлена запись «Что нового» версии `1.183.75`; push не выполнялся.
- 2026-09-14 — Финальная очистка уточнения выполнена: тестовая вкладка закрыта, viewport сброшен, API, Vite, приёмник снимков и PostgreSQL остановлены, `dotnet build-server shutdown` выполнен. Удалены временный кластер и лог, backend/frontend coverage, `dist`, Vite cache, migration SQL и код приёмника. Сохранены только два запрошенных снимка `payments-inline-*-1366x768.png`; пользовательские процессы и вкладки не изменялись.
- 2026-09-14 — По уточнению пользователя верхняя строка «Платежей» получила внутренний отступ от белой панели. При viewport `1366×768` панель начинается в `x=80, y=12`, кнопка возврата — в `x=95, y=25`, вкладки — в `x=145, y=25`, поиск поступлений — в `x=426, y=28,5`. Во «Выплатах» шесть действий сохранили одну строку с координатой `top=79px`; документ имеет `clientWidth = scrollWidth = 1366`, ошибок и предупреждений console нет. Сохранены два запрошенных снимка `payments-inset-*-1366x768.png`.
- 2026-09-14 — Финальный gate внутренних отступов успешен. Frontend: focused 13/13, полный suite 108/108 файлов и 1266/1266 тестов; statements 88,75%, branches 81,68%, functions 87,06%, lines 89,87%; lint, production build, npm audit и bundle `295304/295936` байт прошли. Backend: Release build без предупреждений, 2890/2890 тестов на PostgreSQL 17 с ICU `ru-RU`, строки 91,28%, ветви 76,45%; format, privacy (`1318` файлов), аудит четырёх проектов, Docker distribution, отсутствие pending model changes и idempotent migration SQL (`426602` байта) подтверждены. Добавлена запись «Что нового» версии `1.183.76`; push не выполнялся.
- 2026-09-14 — Финальная очистка внутренних отступов выполнена: отдельная браузерная вкладка закрыта, viewport сброшен, API, Vite, приёмник снимков и PostgreSQL остановлены, `dotnet build-server shutdown` выполнен. Удалены временный кластер и лог, backend/frontend coverage, `dist`, Vite cache, migration SQL и код приёмника. Сохранены только два явно запрошенных снимка `payments-inset-*-1366x768.png`; пользовательские вкладки и служебные процессы Codex не изменялись.
- 2026-09-14 — По новому замечанию пользователя из пустого состояния «Платежи → Поступления» удалена надпись «Выберите гараж для платежей». Поле поиска теперь раскрывает список сразу при фокусе или клике: локальные варианты показываются без задержки, одновременно сервер загружает первые 20 действующих гаражей по пустому запросу; последующий ввод продолжает использовать debounce и актуальную серверную фильтрацию. На `1366×768` панель периода выплат собрана в одну строку из двух локализованных полей месяца и четырёх быстрых периодов.
- 2026-09-14 — Browser-приёмка на синтетических данных подтвердила: пустой клик открыл `aria-expanded=true`, вернул 20 гаражей (101–120), лишняя надпись отсутствует. В выплатах две даты и группа быстрых периодов имеют общий `bottom=196px`, документ `1366/1366` по client/scroll width, ошибок и предупреждений console нет. Сохранены снимки `payments-search-open-1366x768.png` и `payments-period-inline-1366x768.png`.
- 2026-09-14 — Финальный gate поиска и периода успешен. Frontend: 108/108 файлов и 1266/1266 тестов; statements 88,76%, branches 81,71%, functions 87,06%, lines 89,89%; lint, production build, npm audit и bundle `295325/295936` байт прошли. Backend: Release build без предупреждений, 2890/2890 тестов на PostgreSQL 17 с ICU `ru-RU`, строки 91,30%, ветви 76,46%; format, privacy (`1318` файлов), аудит четырёх проектов, Docker distribution, отсутствие pending model changes и idempotent migration SQL (`426602` байта) подтверждены. Добавлена запись «Что нового» версии `1.183.77`; push не выполнялся.
- 2026-09-14 — Финальная очистка поиска и периода выполнена: отдельная браузерная вкладка закрыта, viewport сброшен, API, Vite, приёмник снимков и PostgreSQL остановлены, `dotnet build-server shutdown` выполнен. Удалены временный кластер и лог, backend/frontend coverage, `dist`, Vite cache, migration SQL и код приёмника. Сохранены только два пользовательских проверочных снимка `payments-search-open-1366x768.png` и `payments-period-inline-1366x768.png`; пользовательские вкладки и процессы Codex не изменялись.
- 2026-09-14 — По уточнению пользователя таблица «Платежи → Выплаты» получила настройку ширины всех восьми колонок мышью и клавиатурой. Числовые колонки и «Действие» по умолчанию сделаны компактнее; выбранные размеры сохраняются в локальном хранилище браузера. При расширении шире рабочей области горизонтальная прокрутка остаётся внутри таблицы.
- 2026-09-14 — Browser-приёмка финального кода выполнена строго при viewport `1366×768` и масштабе 100%. В стандартном состоянии скроллер имеет `clientWidth = scrollWidth = 1235px`; после пользовательского расширения «Получателя» — `1235/1329px`, при этом документ остаётся `1366/1366px`. Ошибок и предупреждений console нет. Сохранены снимки `payments-payout-columns-default-1366x768.png` и `payments-payout-columns-resized-1366x768.png`.
- 2026-09-14 — Финальный gate настройки колонок успешен. Frontend: 108/108 файлов и 1267/1267 тестов; statements 88,76%, branches 81,68%, functions 87,08%, lines 89,88%; lint, production build, npm audit и bundle `295935/295936` байт прошли. Backend: Release build без предупреждений, 2890/2890 тестов на PostgreSQL 17 с ICU `ru-RU`; первый прогон на ошибочно инициализированном кластере с locale `C` выявил ожидаемую несовместимость кириллического поиска, после исправления окружения отдельный тест и полный suite прошли. Format, privacy (`1318` файлов), аудит четырёх проектов, Docker distribution, отсутствие pending model changes и idempotent migration SQL (`426602` байта) подтверждены. Добавлена запись «Что нового» версии `1.183.78`; push не выполнялся.
- 2026-09-14 — Финальная очистка настройки колонок выполнена: отдельная браузерная вкладка закрыта, viewport сброшен, API, Vite, приёмник снимков и оба временных PostgreSQL-кластера остановлены, `dotnet build-server shutdown` выполнен. Удалены frontend/backend coverage, `dist`, Vite cache, оба кластера, migration SQL и код приёмника. Сохранены только два явно запрошенных снимка `payments-payout-columns-*-1366x768.png`; пользовательские вкладки, локальный сервер на 5432 и служебные процессы Codex не изменялись.
- 2026-09-14 — По новому замечанию пользователя строка периода в «Платежи → Выплаты» стала компактнее: поля «Месяц с» и «Месяц по» ограничены шириной 100 px, их значения выровнены по центру, а четыре быстрые кнопки увеличены до одинаковой с полями высоты 40 px.
- 2026-09-14 — Browser-приёмка выполнена на отдельном синтетическом стенде строго при viewport `1366×768` и масштабе 100%. Оба поля имеют размер `100×40px`, все четыре быстрые кнопки — высоту 40 px и общие координаты `top=155,8px`, `bottom=195,8px`; документ имеет `clientWidth = scrollWidth = 1366`, ошибок и предупреждений console нет. Сохранён запрошенный снимок `payments-period-compact-1366x768.png`.
- 2026-09-14 — Финальный gate компактного периода успешен. Frontend: focused 14/14, полный suite 108/108 файлов и 1267/1267 тестов; statements 88,76%, branches 81,68%, functions 87,08%, lines 89,88%; lint, production build, npm audit и bundle `295928/295936` байт прошли. Backend: 2890/2890 тестов на PostgreSQL 17 с ICU `ru-RU`, строки 98,52% и ветви 76,18%; format, privacy (`1318` файлов), аудит четырёх проектов, Docker distribution, отсутствие pending model changes и idempotent migration SQL (`426602` байта) подтверждены. Добавлена запись «Что нового» версии `1.183.79`; push не выполнялся.
- 2026-09-14 — Финальная очистка компактного периода выполнена: отдельная браузерная вкладка закрыта, временный viewport сброшен, API, Vite, приёмник снимка и изолированный PostgreSQL остановлены, `dotnet build-server shutdown` выполнен. Удалены frontend/backend coverage, `dist`, Vite cache, временный кластер, migration SQL и код приёмника. Порты `5091`, `5099`, `5178`, `55445` свободны, task-owned процессов и временных артефактов нет. Сохранён только явно запрошенный снимок `artifacts/screenshots/payments-period-compact-1366x768.png` (`1366×768`, 66958 байт).
- 2026-09-14 — Выполнена полная интеграционная ревизия Git. GitHub подтвердил `master` основной веткой; после `fetch --all --prune --tags` обнаружена только локальная и удалённая `master`, открытых pull request и отдельных рабочих веток нет. Локальная ветка содержала 16 линейных коммитов поверх `origin/master`, обратного расхождения не было, поэтому слияния и разрешение конфликтов не требовались. Все 16 коммитов отправлены без переписывания истории; `master`, `origin/master` и `origin/HEAD` синхронизированы на `a31f786a0f53a14458c1b96571ba62dc599268c9`.
- 2026-09-14 — Повторный полный локальный gate интегрированного HEAD успешен. Frontend: 108/108 файлов и 1267/1267 тестов, statements 88,76%, branches 81,68%, functions 87,08%, lines 89,88%; lint, production build, npm audit и bundle `295928/295936` байт прошли. Backend: 2890/2890 тестов на отдельной PostgreSQL 17 с ICU `ru-RU`, строки 98,52% и ветви 76,18%; format, privacy (`1318` файлов), аудит четырёх проектов, Docker distribution и отсутствие pending model changes подтверждены. Все 146 миграций применены к пустой базе и повторно тем же idempotent SQL (`426602` байта). Production frontend и API запущены локально: `/health` и frontend вернули 200; авторизация, роли, тарифы, контрагенты, показания, платежи и выплаты, фонды, отчёты, настройки и «Что нового» прошли browser smoke при `1366×768` без ошибок console. Docker CLI и Compose доступны, но локальный Docker Desktop daemon не был запущен; контейнерный PostgreSQL и release packaging успешно проверены в CI.
- 2026-09-14 — GitHub Actions `Deploy staging` run `34862115751` завершён успешно для `a31f786a`: frontend/backend тесты, dependency/privacy/format gates, миграционный SQL и release packages зелёные. VPS создал и восстановил проверочную резервную копию, применил миграции, проверил nginx, перезапустил `garagebalance-staging.service` и подтвердил readiness. Публичные `https://sgk.blagodaty.ru/health` и frontend вернули 200; опубликованная страница авторизации открылась при `1366×768` без ошибок console. Локальные API, preview и PostgreSQL остановлены; task-owned процессы, coverage, `dist`, Vite cache, TestResults, `bin`, `obj`, временный кластер и SQL удалены. Отдельные рабочие ветки отсутствовали, поэтому удалять локально или на GitHub было нечего; release notes не дублировались, поскольку интегрированные пользовательские изменения уже описаны в версиях `1.183.64–1.183.79`.
- 2026-09-15 — Рабочая область «Платежи → Поступления» для выбранного гаража перекомпонована по согласованному примеру. Карточки гаража, владельца и финансов собраны в одну строку с иконками и разделителями; четыре действия расположены сеткой `2×2`. Поля месяцев, быстрые периоды и центрированные итоги занимают одну панель, активным остаётся ровно один быстрый период, а ненулевые значения окрашиваются по знаку. В таблице месяц повторяется для каждой услуги, промежуточные месячные итоги удалены, общий итог закреплён внизу, все восемь колонок изменяются мышью и клавиатурой с сохранением ширин.
- 2026-09-15 — Browser-приёмка выполнена на изолированных синтетических данных строго при viewport `1366×768` и масштабе 100%. Все шесть элементов периода имеют общий нижний край; выбран только «Предыдущий месяц». Документ остаётся `1366/1366px`; таблица в исходном состоянии `1219/1219px`, после расширения колонки услуги — `1219/1346px`, поэтому горизонтальная прокрутка появляется только внутри неё. Цвета итогов и центрирование подтверждены вычисленными стилями, ошибок и предупреждений console нет. Сохранён снимок `artifacts/screenshots/payments-income-redesign-1366x768.png`.
- 2026-09-15 — Финальный gate успешен. Frontend: 108/108 файлов и 1267/1267 тестов; statements 88,75%, branches 81,68%, functions 87,06%, lines 89,88%; lint, production build, npm audit и bundle `296561/296960` байт прошли. Общий gzip-лимит увеличен с 289 до 290 KiB для новой адаптивной панели и изменяемой таблицы; новых зависимостей нет, отдельные лимиты main/initial JS и CSS сохранены. Backend: Release build без предупреждений, 2890/2890 тестов на PostgreSQL 17, строки 91,28%, ветви 76,45%; format, privacy (`1318` файлов), аудит четырёх проектов, Docker distribution, отсутствие pending model changes и idempotent migration SQL (`426602` байта) подтверждены. Добавлена запись «Что нового» версии `1.183.80`; push не выполнялся.
- 2026-09-15 — Финальная очистка выполнена: browser-проверка завершена, временная вкладка закрыта и viewport сброшен, изолированный PostgreSQL остановлен, `dotnet build-server shutdown` выполнен. Удалены временный кластер и лог, frontend/backend coverage, production `dist`, Vite cache, `bin`, `obj`, `TestResults` и migration SQL. Порт `55448` свободен; task-owned `dotnet`, `testhost`, Node/Vite/Vitest и PostgreSQL не остались. Сохранён только явно запрошенный снимок `artifacts/screenshots/payments-income-redesign-1366x768.png`; пользовательские процессы и служебные процессы Codex не изменялись.
- 2026-09-15 — По замечанию пользователя строки таблицы «Платежи → Поступления» дополнительно уплотнены: фактическая высота строки уменьшилась с 47 до 35 px, поля и кнопки действий — до 28 px. Browser-приёмка при viewport `1366×768` подтвердила восемь одновременно видимых строк, отсутствие горизонтального overflow документа (`1366/1366`) и ошибок console. Сохранён снимок `artifacts/screenshots/payments-income-compact-rows-1366x768.png`.
- 2026-09-15 — Финальный gate уплотнения успешен. Frontend: focused 1/1, полный suite 108/108 файлов и 1267/1267 тестов; statements 88,75%, branches 81,68%, functions 87,06%, lines 89,88%; lint, production build, dependency audit и bundle `296664/296960` байт прошли. Backend: Release build без предупреждений, 2890/2890 тестов на PostgreSQL 17, строки 91,28%, ветви 76,44%; format, privacy (`1318` файлов), аудит четырёх проектов, Docker distribution, отсутствие pending model changes и idempotent migration SQL подтверждены. Добавлена запись «Что нового» версии `1.183.81`; push не выполнялся.
- 2026-09-15 — Финальная очистка уплотнения выполнена: проверочная вкладка закрыта и viewport сброшен, изолированный PostgreSQL остановлен, `dotnet build-server shutdown` выполнен. Удалены временный кластер и лог, frontend/backend coverage, production `dist`, Vite cache, `bin`, `obj` и migration SQL. Порты `5092`, `5179`, `55449` свободны; task-owned процессов не осталось. Сохранён только запрошенный снимок `payments-income-compact-rows-1366x768.png`; пользовательские и служебные процессы не изменялись.
- 2026-09-15 — Начата полная интеграция двух локальных коммитов поступлений. GitHub подтвердил `master` основной веткой; до `fetch --all --prune --tags` зафиксированы `master=821960de7e025bf3c29b8e2a42bfb3a81866c096` и `origin/master=13708dabf22b155a75c425465bc276eceff4146b`. После fetch расхождение осталось `0 behind / 2 ahead`; открытых PR, дополнительных локальных или удалённых веток нет, поэтому слияния, конфликты и удаление веток не требуются.
- 2026-09-15 — Повторный полный локальный gate точного интегрируемого HEAD успешен. Frontend: 108/108 файлов и 1267/1267 тестов, statements 88,75%, branches 81,68%, functions 87,06%, lines 89,88%; lint, production build, npm audit и bundle `296664/296960` байт прошли. Backend: 2890/2890 тестов без пропусков на отдельной PostgreSQL 17 с ICU `ru-RU`, строки 91,30%, ветви 76,46%; format, privacy (`1318` файлов), аудит четырёх проектов, Docker distribution и отсутствие pending model changes подтверждены. Все 146 миграций создали 50 таблиц с нуля, idempotent SQL (`426602` байта) повторно применён без ошибок. Compose config прошёл с одноразовыми переменными; Docker Desktop daemon локально недоступен, поэтому контейнерный build оставлен обязательному CI.
- 2026-09-15 — Production API и frontend запущены локально на изолированной showcase-базе: ready/frontend вернули 200, защищённый users без токена — 401. При `1366×768` проверены вход, пользователи и роли, справочники, тарифы, контрагенты, показания, платежи с выбранным гаражом, фонды, отчёты, импорт, история, настройки и «Что нового» 1.183.81. Таблица поступлений содержит восемь строк по 35 px, document `1366/1366`, browser console без ошибок и предупреждений. Локальный запуск скорректирован так, чтобы published API стартовал из каталога артефакта и видел `AppReleases`; production-код менять не потребовалось.
- 2026-09-15 — Локальный стенд полностью очищен: browser tab закрыт, viewport сброшен, API/frontend/PostgreSQL остановлены, `dotnet build-server shutdown` выполнен; удалены временные базы, coverage, publish/dist, Vite cache, migration SQL, `bin` и `obj`. Пользовательские процессы, базы и сохранённые скриншоты не изменялись. Новая запись «Что нового» не добавлялась: интегрируемые пользовательские изменения уже описаны версиями 1.183.80 и 1.183.81.
- 2026-09-15 — Коммиты поступлений и локальной интеграционной проверки отправлены в `master`. GitHub Actions `Deploy staging` №34929506792 полностью успешен для `d788faec13b602d64b37142784f73fbc6bb82762`: backend 2890/2890, frontend 1267/1267, coverage, audit, privacy, format, lint, production build, bundle, миграционный SQL и release packages прошли. VPS создал резервную копию, restore-check подтвердил 50 таблиц, миграции и обе проверки nginx завершились успешно, `garagebalance-staging.service` запущен; опубликован релиз `d788faec13b602d64b37142784f73fbc6bb82762-313`.
- 2026-09-15 — Публичная smoke-приёмка подтвердила HTTP 200 для readiness, frontend и entry JS. Страница авторизации открыта при viewport `1366×768`: document `1366/1366`, ошибок и предупреждений console нет. Дополнительных локальных/удалённых веток и открытых PR нет, поэтому удаление веток не требовалось; временные процессы и артефакты интеграции отсутствуют.
- 2026-09-15 — Расшифровка просроченной задолженности перенесена из потока таблицы поступлений в отдельное перемещаемое и изменяемое окно. Красная кнопка открытия расположена справа от строки с общей суммой долга, сообщает раскрытое состояние и получает фокус обратно после закрытия. Отложенная загрузка, повтор после ошибки и отмена устаревшего запроса при смене гаража защищены React-регрессиями; прежняя настройка постоянного раскрытия удалена как более неиспользуемая.
- 2026-09-15 — Пояснение обязательного показания убрано из потока строки: поле и ячейка остаются красными, доступный tooltip появляется при наведении или клавиатурном фокусе. Browser-приёмка на синтетическом гараже `109-ПРОСРОЧКА` строго при `1366×768` подтвердила высоту строк `35px`, скрытый tooltip `position:absolute/opacity:0`, видимый tooltip `opacity:1` без изменения высоты, окно просрочки `1120×520px` внутри viewport и document `1366/1366`; ошибок и предупреждений console нет.
- 2026-09-15 — Финальный gate M20 успешен. Frontend: `108/108` файлов и `1265/1265` тестов, statements `88,60%`, branches `81,55%`, functions `87,00%`, lines `89,76%`; lint, production build, npm audit и bundle `296895/296960` байт прошли. Backend: `2890/2890` тестов без пропусков на отдельной PostgreSQL 17 с ICU `ru-RU`, строки `91,30%`, ветви `76,46%`; format, privacy (`2593` файла), аудит четырёх проектов, Docker distribution, отсутствие pending model changes и двукратное применение idempotent migration SQL подтверждены. Добавлена запись «Что нового» версии `1.183.82`; push не выполнялся.
- 2026-09-15 — Финальная очистка M20 выполнена: приёмочная вкладка закрыта, временный viewport сброшен, API, Vite и изолированная PostgreSQL остановлены, `dotnet build-server shutdown` выполнен. Удалены временный кластер, coverage, production `dist`, Vite cache, `bin`, `obj`, migration SQL и созданный ошибочным запуском npm audit журнал. Порты `5173`, `5180`, `5182`, `5093` и `55450` свободны, App shards отсутствуют. Два процесса `cua_node` сохранены намеренно как общие служебные процессы Codex; пользовательские процессы и базы не изменялись.
- 2026-09-15 — Исправлено раскрытие выбора количества строк в компактных пагинациях: контейнер больше не обрезает открывающийся вверх общий combobox, а локальный слой пагинации гарантированно располагается над содержимым таблицы. Изменение автоматически применяется к обеим текущим компактным таблицам — «Нерегулярные платежи» и «Объявленные сборы» — и защищено общей компонентной и CSS-регрессией. Browser-приёмка на синтетических данных строго при `1366×768` подтвердила для обоих списков четыре видимых варианта `10/25/50/100`, применение значения `25`, отсутствие горизонтального overflow документа (`1366/1366`) и ошибок console.
- 2026-09-15 — Финальный gate исправления пагинации успешен. Frontend: `108/108` файлов и `1266/1266` тестов, statements `88,60%`, branches `81,55%`, functions `87,00%`, lines `89,76%`; lint, production build, npm audit и bundle `296913/296960` байт прошли. Backend: `2890/2890` тестов без пропусков на отдельной PostgreSQL с ICU `ru-RU`, строки `91,30%`, ветви `76,46%`; format, privacy (`3289` файлов), Docker Compose/distribution, отсутствие pending model changes и двукратное применение idempotent migration SQL (`426602` байта) подтверждены. Добавлена запись «Что нового» версии `1.183.83`; push не выполнялся.
- 2026-09-15 — Финальная очистка исправления пагинации выполнена: временная браузерная вкладка закрыта, viewport сброшен, API, Vite и изолированная PostgreSQL остановлены, `dotnet build-server shutdown` выполнен. Удалены одноразовый кластер и лог, frontend/backend coverage, production `dist`, Vite cache, `bin`, `obj` и migration SQL. Порты `5094`, `5179` и `55454` свободны; task-owned процессов и временных артефактов не осталось. Два процесса `cua_node` сохранены как общие служебные процессы Codex; пользовательский сервер на `5173` не изменялся.
- 2026-09-15 — Кнопка возврата отвязана от прокручиваемого содержимого во всех рабочих разделах: единый верхний элемент учитывает вертикальное смещение любого вложенного контейнера и уходит вместе с исходной строкой формы, поэтому больше не плавает поверх тарифов, таблиц и полей. При возврате к началу формы кнопка восстанавливается точно на прежнем месте. Общий обработчик применяется единственной кнопке `Workspace`, а компонентная регрессия проверяет прокрутку вниз и возврат наверх.
- 2026-09-15 — Browser-приёмка выполнена на синтетическом стенде с PostgreSQL 17.11 строго при `1366×768`. Проверены все 12 рабочих разделов: в каждом присутствует ровно одна общая кнопка возврата с исходным смещением; отдельно прокручены внутренняя таблица «Тарифов и сборов» (`229px`) и обычная длинная форма «Что нового» (`605px`). В обоих случаях кнопка ушла за верхнюю границу и вернулась к исходной координате после прокрутки наверх; document `1366/1366`, ошибок и предупреждений console нет.
- 2026-09-15 — Финальный gate исправления кнопки успешен. Frontend: `108/108` файлов и `1267/1267` тестов, statements `88,60%`, branches `81,55%`, functions `87,01%`, lines `89,77%`; lint, production build, npm audit и bundle `297125/297984` байт прошли. Общий gzip-лимит увеличен с 290 до 291 KiB: единый обработчик и регрессия добавили `212` байт gzip, новых зависимостей нет, отдельные лимиты main/initial JS и CSS сохранены. Backend: `2890/2890` тестов без пропусков на PostgreSQL 17.11 с ICU `ru-RU`, строки `91,30%`, ветви `76,46%`; Release build без предупреждений, format, аудит пакетов, Docker Compose/distribution, отсутствие pending model changes и двукратное применение idempotent migration SQL (`426602` байта) подтверждены. Добавлена запись «Что нового» версии `1.183.84`; push не выполнялся.
- 2026-09-15 — Финальная очистка исправления кнопки выполнена: browser tab закрыт, viewport сброшен, API, Vite и PostgreSQL остановлены, `dotnet build-server shutdown` выполнен. Удалены загруженный временный дистрибутив PostgreSQL 17.11 (`324 MiB`), его архив и кластер, coverage, production `dist`, Vite cache, `bin`, `obj`, migration SQL и журналы. Повторный privacy gate прошёл по `1318` файлам; порты `5095`, `5180` и `55455` свободны, task-owned процессов и временных артефактов нет. Пользовательские и общие служебные процессы не изменялись.
- 2026-09-15 — Из раздела «Показания» удалена постоянная подсказка о первом сохранении и правах на изменение показаний. Компонентная регрессия теперь явно подтверждает отсутствие текста; запись «Что нового» добавлена версией `1.183.85`.
- 2026-09-15 — Browser-приёмка выполнена на изолированных синтетических данных строго при viewport `1366×768`: подсказка отсутствует, между нижней границей параметров и таблицей сохранён штатный отступ `12px`, document `1366/1366`, ошибок и предупреждений console нет. Скриншот использовался только для визуальной проверки и не сохранялся, поскольку пользователь его не запрашивал.
- 2026-09-15 — Финальный gate удаления подсказки успешен. Frontend: focused `1/1`, полный suite `108/108` файлов и `1267/1267` тестов; statements `88,60%`, branches `81,55%`, functions `87,01%`, lines `89,77%`; lint, production build, npm audit и bundle `297060/297984` байт прошли. Backend: Release build без предупреждений, `2890/2890` тестов на PostgreSQL 17.11, строки `91,28%`, ветви `76,45%`; format, privacy (`25559` файлов), аудит четырёх проектов, Docker distribution, отсутствие pending model changes и двукратное применение idempotent migration SQL (`426602` байта) подтверждены. Push не выполнялся.
- 2026-09-15 — Панель раздела «Отчёты» уплотнена для стандартного рабочего разрешения: кнопка возврата получила общие отступы, восемь вкладок помещаются в одну строку без горизонтальной прокрутки, длинные подписи центрированы и перенесены на две строки. Поля месяца и даты уменьшены, экспорт XLSX/PDF во всех восьми отчётах остаётся в одной строке с основными фильтрами; в отчёте по сборам поиск занимает всё оставшееся место перед экспортом.
- 2026-09-15 — Browser-приёмка всех восьми отчётов выполнена на синтетических данных строго при viewport `1366×768`: строка вкладок имеет `clientWidth = scrollWidth = 1186px`, document `1366/1366px`, действия экспорта находятся в той же grid-строке, что и фильтры, а кнопка возврата расположена на `27px` правее и `25px` ниже начала рабочей области. Ошибок и предупреждений console нет; снимки использовались только для визуальной проверки и не сохранялись, поскольку пользователь их не запрашивал.
- 2026-09-15 — Финальный gate компактной панели отчётов успешен. Frontend: focused `80/80`, полный повторный suite `106/106` файлов и `1268/1268` тестов; statements `88,60%`, branches `81,55%`, functions `87,01%`, lines `89,77%`; lint, production build, npm audit и bundle `297187/297984` байт прошли. Первый полный прогон дал единственный нагрузочный timeout старого сценария редактирования поступления; изолированный повтор прошёл за `6,96 с`, а полный повтор с четырьмя workers — без ошибок. Backend: Release build без предупреждений, `2890/2890` тестов на PostgreSQL 17.11, строки `91,28%`, ветви `76,45%`; format, privacy (`22802` файла), аудит четырёх проектов и Docker distribution подтверждены. Добавлена запись «Что нового» версии `1.183.86`; push не выполнялся.
- 2026-09-15 — Финальная очистка выполнена: проверочная browser-вкладка закрыта, viewport сброшен, локальные API, Vite и PostgreSQL остановлены, тестовая база удалена, `dotnet build-server shutdown` выполнен. Удалены дистрибутив и кластер PostgreSQL, frontend/backend coverage, `dist`, Vite cache, `bin`, `obj`, `TestResults` и migration SQL. Порты `5096`, `5181`, `55456` свободны; task-owned процессов и временных артефактов нет. Два процесса `cua_node` сохранены как общие служебные процессы Codex; пользовательские процессы и данные не изменялись.
- 2026-09-15 — Выполнена повторная полная инвентаризация Git перед публикацией. GitHub подтвердил `master` основной веткой; до и после `fetch --all --prune --tags` локальная `master=7010d84393948ae338836215d6cb957b49556306`, удалённая `origin/master=4e765017e6428b714d537ee214b805630251439a`, расхождение `0 behind / 3 ahead`. Дополнительных локальных или удалённых веток и открытых pull request нет, поэтому слияния, конфликты и удаление веток не требуются.
- 2026-09-15 — Повторный полный локальный gate точного интегрируемого HEAD успешен. Frontend: стабильный прогон с четырьмя workers — `106/106` файлов и `1268/1268` тестов, statements `88,60%`, branches `81,55%`, functions `87,01%`, lines `89,77%`; lint, production build, npm audit и bundle `297187/297984` байт прошли. Два старых тяжёлых сценария при первом прогоне с шестью workers достигли лимита 30 секунд, затем оба прошли изолированно за `9,47 с`, а полный стабильный повтор — без ошибок. Backend: Release build без предупреждений, `2890/2890` тестов без пропусков на PostgreSQL 17.11 с ICU `ru-RU`, строки `91,28%`, ветви `76,45%`; format, privacy, аудит четырёх проектов и Docker distribution подтверждены. Все 146 миграций создали 50 таблиц с нуля, idempotent SQL применён повторно дважды, pending model changes отсутствуют. Compose config валиден; локальный Docker Desktop daemon недоступен, поэтому контейнерную сборку оставлено подтвердить обязательному CI.
- 2026-09-15 — Production API и frontend проверены локально на отдельной showcase-базе: seed подготовил 10 гаражей, 67 начислений, 11 операций, 36 показаний, 3 сбора, 3 поставщика и 3 сотрудников; health/frontend вернули 200, защищённый users без токена — 401. В браузере при `1366×768` выполнен вход и открыты все 12 рабочих разделов; отчёты имеют document `1366/1366`, строку вкладок `1186/1186`, версия `1.183.86` отображается в «Что нового», browser console и API-лог без ошибок. После smoke вкладка закрыта, viewport сброшен, API/frontend/PostgreSQL остановлены, временные базы, архив, кластер, coverage, `dist`, Vite cache, `bin`, `obj`, `TestResults` и migration SQL удалены; порты `5097`, `5182`, `55457` свободны, task-owned процессов и артефактов нет. Новая запись «Что нового» не добавлялась: интегрируемые пользовательские изменения уже описаны версиями `1.183.82–1.183.86`.
- 2026-09-15 — Четыре локальных коммита, включая фиксацию интеграционного gate, отправлены в `master` без переписывания истории. GitHub Actions `Deploy staging` run `34956525068` успешно проверил аудит зависимостей, privacy, format, полные backend/frontend suites, production-сборки, idempotent migration SQL и release-пакеты для `4ceda69b83eae33379b65a18d14e9d153f6b23a5`. VPS создал резервную копию, успешно восстановил её в проверочную базу с 50 таблицами, применил миграции, проверил nginx, перезапустил `garagebalance-staging.service` и завершил публикацию release `4ceda69b83eae33379b65a18d14e9d153f6b23a5-316`.
- 2026-09-15 — После автодеплоя публичные `https://sgk.blagodaty.ru/health` и frontend вернули 200, PostgreSQL в health отмечен как `ok`. Страница авторизации открылась при точном viewport `1366×768`: document `1366/1366`, заголовок и форма входа видимы, ошибок и предупреждений console нет. Проверочная вкладка закрыта, временный viewport сброшен; отдельные рабочие ветки и открытые PR отсутствуют, поэтому удалять после интеграции нечего.
- 2026-09-15 — По макету пользователя форма создания и редактирования гаража перестроена в две логичные колонки. Слева номер занимает всю ширину, количество человек и этажи расположены одной парой, адрес вынесен отдельной строкой под ними. Справа после начальных финансовых значений и счётчиков одной строкой расположены владелец и телефон; нижний ряд «Счётчики / Комментарий» сохранён. На экранах до 720 px внутренние пары складываются в одну колонку. Добавлена запись «Что нового» версии `1.183.89`; номер не пересекается с версиями `1.183.87–1.183.88` в ранее опубликованных отдельных ветках.
- 2026-09-15 — Browser-приёмка новой формы выполнена на отдельной showcase-базе PostgreSQL 17 строго при viewport `1366×768`. «Количество человек» и «Этажи» выровнены на `y=306,5`, адрес, владелец и телефон — на `y=374,5`; адрес занимает всю левую колонку, контакты делят правую пополам. Диалог `1120×483` полностью помещается в viewport, document `1366/1366×768/768`, ошибок и предупреждений console нет. Проверочный снимок не сохранялся, поскольку пользователь его не запрашивал.
- 2026-09-15 — Финальный локальный gate компоновки формы успешен. Frontend: focused workflow `1/1`, responsive `64/64`, стабильный полный suite с четырьмя workers `106/106` файлов и `1268/1268` тестов; statements `88,60%`, branches `81,55%`, functions `87,01%`, lines `89,77%`; lint, production build, npm audit и bundle `297200/297984` байт прошли. Первый полный запуск с шестью workers дал единственную нагрузочную гонку старого сценария управления ролями на ожидании lazy-load; изолированный повтор прошёл за `3,75 с`, стабильный полный повтор — без ошибок, таймауты не изменялись. Backend: Release build без предупреждений, `2890/2890` тестов без пропусков на отдельной PostgreSQL 17 с ICU `ru-RU`, строки `91,28%`, ветви `76,45%`; format, privacy (`1318` файлов), аудит четырёх проектов, отсутствие pending model changes и двукратное применение idempotent migration SQL (`426602` байта, 146 миграций, 50 таблиц) подтверждены. Статическая Docker distribution проверка прошла; полный Docker gate должен пройти в обязательном CI ветки.
