# Инвентаризация UI Контролов И Стилей

Документ фиксирует текущую карту контролов GarageBalance после проверки `frontend/src/App.tsx`, `frontend/src/App.css`, `frontend/src/App.test.tsx` и frontend shared-тестов. Он нужен, чтобы единый стиль текущих контролов оставался проверяемым контрактом, а новые экраны добавлялись по списку, а не по визуальным догадкам.

## Источники

- `frontend/src/App.tsx`: основные рабочие экраны, прототипы форм, модальные окна и таблицы.
- `frontend/src/App.css`: общие классы кнопок, форм, таблиц, вкладок, диалогов, состояний и адаптива.
- `frontend/src/App.test.tsx`: пользовательские workflow-тесты по авторизации, главному меню, тарифам, контрагентам, показаниям, платежам, фондам, пользователям, справочникам, отчетам, импорту и истории изменений.
- `frontend/src/accessibleStatus.test.ts`: структурные проверки доступных имен полей, кнопок, icon-only кнопок, сообщений статуса и `detail-dialog`.
- `frontend/src/responsiveLayout.test.ts`: проверка scrollable dialog-layout внутри viewport.
- `frontend/src/shared/focusHooks.ts` и `focusHooks.test.tsx`: единые hooks для Escape, focus-on-open, focus-trap и восстановления фокуса.
- `frontend/src/shared/changePreview.ts` и `changePreview.test.ts`: единый формат `было -> стало` для confirmation изменений.
- `frontend/src/shared/PageNavigator.tsx` и `PageNavigator.test.tsx`: единая нумерованная пагинация со стрелками, многоточием, активной страницей и быстрым переходом для больших списков.

## Семейства Контролов

| Семейство | Текущий общий стиль | Проверенные области | Открытые хвосты |
| --- | --- | --- | --- |
| Primary buttons | `primary-button` | Авторизация, основные CTA там, где нужен главный action | Проверить, что новые прототипы не используют primary там, где действие вторичное |
| Secondary buttons | `secondary-button` | Сохранение, проведение, экспорт, рабочие действия | Продолжить проверку disabled/loading состояний во всех новых разделах |
| Ghost buttons | `ghost-button` | Отмена, нейтральные переходы, показать еще | Проверить единый hover/focus в локальных прототипах |
| Icon-only buttons | `icon-button`, `topbar-back-button` | Sidebar, topbar, закрытие dialogs, поиск, меню строк, возврат в главное меню | У всех новых icon-only actions сохранять `aria-label`, `type` и tooltip/title при неочевидной иконке |
| Destructive buttons | `danger-button` вместе с `secondary-button` или action-specific классом | Архивирование, удаление, отмена финансовых записей, удаление прототипных строк | Проверить визуальное отличие для будущих фондов, rollback импорта и чеков |
| Link-like actions | `link-button` | Быстрые переходы, вспомогательные действия | Не использовать для опасных действий без confirmation |
| Text inputs | Общие правила внутри `dictionary-form`, compact/forms, finance filters | Логин, пользователи, справочники, тарифы, платежи, отчеты, импорт, история | Дальше сверять width/mobile behavior для новых модалок контрагентов и платежей |
| Number inputs | Общие input-правила плюс business validation | Гаражи, тарифы, суммы, показания, фонды, платежи | Проверить money/quantity alignment в таблицах после backend-моделей сборов и фондов |
| Date/month/year controls | `dictionary-form input[type='date']`, `input[type='month']`; год в прототипе показаний | Тарифы, финансы, отчеты, импорт, платежные прототипы, показания | Year-control показаний должен оставаться стилизованным и валидироваться 1900-9999 |
| Select controls | `dictionary-form select`, finance/report filters, tabs with roles | Роли, группы, тарифная база, типы операций, отчеты, история | Проверить человекочитаемые option labels при новых enum/backend-моделях |
| Textarea | Общие правила `dictionary-form textarea`, dialog reason/comment поля | Причины удаления/отмены, комментарии, импорт, платежи, контрагенты | Для обязательных reason сохранять `aria-invalid` и error text |
| Checkbox/toggle | Styled form controls and native checkbox where appropriate | Архивные списки, регулярные платежи, участники сборов, пороговая тарификация | Нужна отдельная визуальная проверка будущих toggles после стабилизации моделей сборов |
| Tabs | Role-based tabs and section tab buttons | Контрагенты, платежи, отчеты, настройки, dashboard navigation | Проверить mobile overflow для всех новых трехвкладочных/многовкладочных экранов |
| Dialogs/modals | `modal-backdrop`, `detail-dialog`, `detail-dialog-header`, `detail-dialog-actions` | Редакторы, confirmations, карточки истории, прототипные формы | Новые dialogs обязаны подключать focus trap, Escape, cancel, accessible title/description и restore focus |
| Tables | Рабочие tables с accessible names, states and row actions | Тарифы, контрагенты, показания, платежи, фонды, справочники, отчеты, история | Для больших backend-таблиц сохранять pagination/server filters или горизонтальный scroll |
| Pagination/filter states | `PageNavigator` для нумерованной навигации, shared pagination helpers, filter labels, status messages | Контрагенты, история, отчеты, справочники, платежи | Новые серверные таблицы должны переиспользовать `PageNavigator`; filters обязаны сбрасывать offset, а навигация — блокироваться во время загрузки |
| Empty/loading/error states | `empty-state`, `form-error`, `role=status`, `role=alert` | Авторизация, справочники, финансы, импорт, отчеты, история | Дальше расширять на будущие backend-модели фондов, сборов и персонала |

## Экраны И Текущий Статус

| Экран | Контролы | Статус |
| --- | --- | --- |
| Авторизация | Email/login input, password input, one submit button | Minimal GPT-style реализован, лишние Google/admin buttons убраны |
| Главное меню | `dashboard-tile`, `topbar-back-button`, collapsed sidebar | Есть hover/focus/shadow, текст кнопок не должен выходить за рамки |
| Sidebar/topbar | Icon-only buttons, active states, back button, user actions | Требуется ручная visual-проверка скриншотами после каждого крупного UI-среза |
| Пользователи | Таблица, editor dialog, role select, password input, delete/restore confirmations | Confirmation diff и restore focus покрыты тестами |
| Справочники | Forms, archive/restore dialogs, table/list actions, date/select/money controls | Общий editor и confirmation flow покрыт, отдельные будущие модели еще открыты |
| Тарифы и сборы-прототип | Editable cells, add service/fee dialogs, threshold action, one-time delete/restore | Стиль приведен к общим dialogs/buttons; backend-модель сборов остается открытой |
| Контрагенты-прототип | Tabs `Гаражи`/`Поставщики`/`Персонал`, dialogs per section, delete/restore confirmations, нумерованная server pagination | Все три таблицы используют единый `PageNavigator`; карточка гаража разделяет основные и финансовые поля, сохраняет сведения о счётчиках, а удаление доступно только в таблице; backend-модели контактов/персонала еще открыты |
| Показания-прототип | Year input, type select, editable table cells, confirmation dialog | Нужна backend-модель истории и сохранения показаний по гаражам |
| Платежи-прототип | Payments table, add accrual/payment/bank dialogs, full payment dialog | Требуется дальнейшее удаление лишней информации и привязка к backend-логике |
| Управление фондами-прототип | Fund table, deposit/withdraw dialogs | Confirmation UI есть, backend/audit для фондов остается открытым |
| Финансы/backend экран | Forms, month/date controls, tables, cancel confirmations | Текущие create/update/cancel workflows покрыты тестами |
| Отчеты | Tabs, filters, date/month controls, export buttons, tables | Есть workflow tests, export tests и общий style contract; новые отчеты должны переиспользовать эти controls |
| Импорт Access | File input, dry-run button, quarantine table, export actions | File picker, dry-run, JSON export, quarantine resolve и close confirmation actions имеют иконки/подсказки и workflow tests; rollback confirmation остается открытым, потому что фактический rollback еще не реализован |
| История изменений | Filters, pagination, export buttons, detail dialog | Общий экран есть; отдельный доступ к истории внутри рабочих разделов не нужен |
| "Что нового" | Read-only release list | Admin UI управления release notes еще не реализован |

## Уже Закреплено Тестами

- Все `<button>` в `App.tsx` должны иметь явный `type`.
- Все `icon-button` должны иметь accessible name.
- Все `input`, `select` и `textarea` должны иметь `aria-label` или `aria-labelledby`.
- `detail-dialog` должен быть `role="dialog"`, `aria-modal="true"` и иметь `aria-labelledby`.
- Высокие dialogs должны прокручиваться внутри viewport, а header/actions оставаться доступными.
- `focusHooks` покрывают Escape, focus-on-open, focus-trap и восстановление фокуса.
- `PageNavigator` покрыт тестами диапазонов страниц, стрелок, нумерованных кнопок, быстрого перехода и disabled-состояния; workflow контрагентов проверяет запрос второй серверной страницы.
- Основные workflow-тесты открывают и проверяют dialogs тарифов, контрагентов, показаний, платежей, фондов, пользователей, справочников, отчетов, импорта и истории.

## Открытые Хвосты

- Закрепить единые классы для всех новых primary/secondary/ghost/icon/destructive/link buttons по мере переноса прототипов на backend-модели.
- Для новых backend-моделей и интеграционных экранов добавлять строки coverage по тем же семействам controls до закрытия их roadmap-пунктов.
- Добавлять visual/DOM regression tests для экранов, где появятся новые реальные backend-данные сборов, персонала, фондов и чеков.
- Сделать скриншоты ключевых экранов и получить ручное подтверждение заказчика после завершения UI style audit.

## Связь С Roadmap

- Закрывает Milestone 0: `Найти все кнопки, input, textarea, select, date/month controls, tabs, dialogs и проверить стили`.
- Закрывает Milestone 8 по текущему UI: инвентарь, единые классы, destructive states, date/month/year, select, text inputs, textarea, checkbox/toggle, tabs, tables, dialogs, sidebar/topbar/dashboard, palette и DOM regression.
- Не закрывает ручную acceptance-приемку скриншотов: она остается отдельным пунктом roadmap.
