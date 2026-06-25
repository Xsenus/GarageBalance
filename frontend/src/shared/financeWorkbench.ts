export type FinanceSectionKey = 'income' | 'expense' | 'accruals' | 'supplierAccruals' | 'meterReadings'
export type FinanceEditorKey = FinanceSectionKey | 'regularAccruals' | 'supplierGroupSalaryAccruals'
export type FinanceVisibleListStatusKind = 'operations' | 'accruals' | 'supplierAccruals' | 'meterReadings'

export type FinanceSectionOption = {
  key: FinanceSectionKey
  label: string
  description: string
}

export type FinanceSectionCounts = Record<FinanceSectionKey, number>

export type FinanceEditorSavingScope =
  | 'income'
  | 'expense'
  | 'accrual'
  | 'regular-accruals'
  | 'salary-accruals'
  | 'supplier-accrual'
  | 'meter-reading'

export type FinanceContextMenuAction = 'add' | 'edit' | 'delete'
export type FinanceFallbackLabelKey = 'missingValue' | 'noData' | 'noComment' | 'meterGapWarning'
export type FinanceToolbarLabelKey =
  | 'sectionTabs'
  | 'periodFilter'
  | 'periodFrom'
  | 'periodTo'
  | 'search'
  | 'searchPlaceholder'
  | 'incomeGarageSearch'
  | 'incomeGarageSearchPlaceholder'
  | 'incomeGarageSearchSubmit'
  | 'regularAccruals'
  | 'supplierGroupSalaryAccruals'
  | 'tableArea'
  | 'emptyState'
  | 'pagination'
  | 'rows'
  | 'pageSize'
  | 'previousPage'
  | 'nextPage'
  | 'contextMenu'
export type FinanceEditorUiLabelKey =
  | 'createMode'
  | 'editMode'
  | 'close'
  | 'cancel'
  | 'save'
  | 'unsavedHint'
  | 'unsavedConfirm'
export type FinanceEditorValidationTitleVariant = 'default' | 'batch' | 'detailed'
export type FinancePanelLabelKey =
  | 'section'
  | 'title'
  | 'loading'
  | 'readOnlyHint'
  | 'summary'
  | 'incomeTotal'
  | 'accrualTotal'
  | 'expenseTotal'
  | 'balance'
  | 'meterReadings'

export const financeSectionOptions: FinanceSectionOption[] = [
  { key: 'income', label: 'Приходы', description: 'Оплаты владельцев' },
  { key: 'expense', label: 'Расходы', description: 'Выплаты поставщикам' },
  { key: 'accruals', label: 'Начисления владельцам', description: 'Долги по гаражам' },
  { key: 'supplierAccruals', label: 'Начисления поставщикам', description: 'Обязательства' },
  { key: 'meterReadings', label: 'Счетчики', description: 'Вода и электричество' },
]

const financeEditorTitles: Record<FinanceEditorKey, string> = {
  income: 'Новое поступление',
  expense: 'Новая выплата',
  accruals: 'Ручное начисление',
  regularAccruals: 'Регулярные начисления',
  supplierGroupSalaryAccruals: 'Зарплата группы',
  supplierAccruals: 'Начисление поставщику',
  meterReadings: 'Показание счетчика',
}

const financeEditorSubmitLabels: Record<FinanceEditorKey, string> = {
  income: 'Провести',
  expense: 'Провести',
  accruals: 'Начислить',
  regularAccruals: 'Создать месяц',
  supplierGroupSalaryAccruals: 'Начислить зарплату',
  supplierAccruals: 'Начислить',
  meterReadings: 'Внести',
}

const financeEditorSavingScopes: Record<FinanceEditorKey, FinanceEditorSavingScope> = {
  income: 'income',
  expense: 'expense',
  accruals: 'accrual',
  regularAccruals: 'regular-accruals',
  supplierGroupSalaryAccruals: 'salary-accruals',
  supplierAccruals: 'supplier-accrual',
  meterReadings: 'meter-reading',
}

const financeContextMenuLabels: Record<FinanceContextMenuAction, string> = {
  add: 'Добавить',
  edit: 'Изменить',
  delete: 'Удалить',
}

const financeTableHeaders: Record<FinanceSectionKey, string[]> = {
  income: ['Дата', 'Месяц', 'Гараж', 'Владелец', 'Вид оплаты', 'Документ', 'Оплачено', 'Долг после', 'Комментарий'],
  expense: ['Дата', 'Месяц', 'Поставщик', 'Вид выплаты', 'Документ', 'Выплачено', 'Обязательство после', 'Комментарий'],
  accruals: ['Месяц', 'Гараж', 'Владелец', 'Вид оплаты', 'Источник', 'Начислено', 'Комментарий'],
  supplierAccruals: ['Месяц', 'Поставщик', 'Вид выплаты', 'Источник', 'Документ', 'Начислено', 'Комментарий'],
  meterReadings: ['Месяц', 'Дата', 'Гараж', 'Счетчик', 'Пред. знач.', 'Нов. знач.', 'Разница', 'Комментарий'],
}

const financeFallbackLabels: Record<FinanceFallbackLabelKey, string> = {
  missingValue: 'Не указан',
  noData: 'Нет данных',
  noComment: 'Нет комментария',
  meterGapWarning: 'проверьте месяц',
}

const financeToolbarLabels: Record<FinanceToolbarLabelKey, string> = {
  sectionTabs: 'Разделы платежей',
  periodFilter: 'Фильтр периода',
  periodFrom: 'Период с',
  periodTo: 'Период по',
  search: 'Поиск по платежам',
  searchPlaceholder: 'Гараж, владелец, поставщик или документ',
  incomeGarageSearch: 'Поиск гаража для поступления',
  incomeGarageSearchPlaceholder: 'Гараж или владелец',
  incomeGarageSearchSubmit: 'Найти гараж для поступления',
  regularAccruals: 'Регулярные',
  supplierGroupSalaryAccruals: 'Зарплата группы',
  tableArea: 'Рабочая область платежной таблицы',
  emptyState: 'По выбранным условиям записей нет',
  pagination: 'Пагинация платежей',
  rows: 'Строк',
  pageSize: 'Количество строк платежей',
  previousPage: 'Назад',
  nextPage: 'Вперед',
  contextMenu: 'Операции с платежами',
}

const financeEditorValidationTitles: Record<FinanceEditorKey, string> = {
  income: 'Проверьте поступление',
  expense: 'Проверьте выплату',
  accruals: 'Проверьте начисление',
  regularAccruals: 'Проверьте регулярное начисление',
  supplierGroupSalaryAccruals: 'Проверьте начисление зарплаты',
  supplierAccruals: 'Проверьте начисление поставщику',
  meterReadings: 'Проверьте показание',
}

const financeEditorValidationTitleVariants: Partial<Record<FinanceEditorKey, Partial<Record<FinanceEditorValidationTitleVariant, string>>>> = {
  regularAccruals: {
    batch: 'Проверьте регулярные начисления',
  },
  meterReadings: {
    detailed: 'Проверьте показание счетчика',
  },
}

const financeEditorUiLabels: Record<FinanceEditorUiLabelKey, string> = {
  createMode: 'Платежи',
  editMode: 'Изменение',
  close: 'Закрыть форму платежа',
  cancel: 'Отмена',
  save: 'Сохранить',
  unsavedHint: 'Есть несохраненные изменения формы платежа.',
  unsavedConfirm: 'Закрыть форму платежа без сохранения изменений?',
}

const financePanelLabels: Record<FinancePanelLabelKey, string> = {
  section: 'Платежи',
  title: 'Поступления владельцев и выплаты поставщикам',
  loading: 'Загрузка...',
  readOnlyHint: 'Режим просмотра: для записи платежей, начислений и показаний нужно право payments.write.',
  summary: 'Итоги платежей',
  incomeTotal: 'Поступления',
  accrualTotal: 'Начислено',
  expenseTotal: 'Выплаты',
  balance: 'Баланс',
  meterReadings: 'Счетчики',
}

const financeVisibleListStatusSuffixes: Record<FinanceVisibleListStatusKind, string> = {
  operations: 'операций',
  accruals: 'начислений',
  supplierAccruals: 'начислений поставщикам',
  meterReadings: 'показаний',
}

const financeVisibleListEmptyLabels: Record<FinanceVisibleListStatusKind, string> = {
  operations: 'Операций пока нет',
  accruals: 'Начислений пока нет',
  supplierAccruals: 'Начислений поставщикам пока нет',
  meterReadings: 'Показаний пока нет',
}

const financeVisibleListTableLabels: Record<FinanceVisibleListStatusKind, string> = {
  operations: 'Последние платежи',
  accruals: 'Последние начисления',
  supplierAccruals: 'Последние начисления поставщикам',
  meterReadings: 'Последние показания',
}

const financeVisibleListTableHeaders: Record<FinanceVisibleListStatusKind, string[]> = {
  operations: ['Дата', 'Операция', 'Сумма'],
  accruals: ['Месяц', 'Начисление', 'Сумма'],
  supplierAccruals: ['Месяц', 'Поставщик', 'Сумма'],
  meterReadings: ['Месяц', 'Счетчик', 'Расход'],
}

export function getFinanceEditorTitle(section: FinanceEditorKey) {
  return financeEditorTitles[section]
}

export function getFinanceEditorSubmitLabel(section: FinanceEditorKey) {
  return financeEditorSubmitLabels[section]
}

export function getFinanceEditorSavingScope(section: FinanceEditorKey) {
  return financeEditorSavingScopes[section]
}

export function getFinanceSectionDescription(section: FinanceSectionOption, counts: FinanceSectionCounts) {
  return `${section.description} · ${counts[section.key]}`
}

export function getFinanceContextMenuLabel(action: FinanceContextMenuAction) {
  return financeContextMenuLabels[action]
}

export function getFinanceTableHeaders(section: FinanceSectionKey) {
  return financeTableHeaders[section]
}

export function getFinanceFallbackLabel(key: FinanceFallbackLabelKey) {
  return financeFallbackLabels[key]
}

export function getFinanceOptionalText(value: string | null | undefined, fallback: FinanceFallbackLabelKey = 'missingValue') {
  return value ?? getFinanceFallbackLabel(fallback)
}

export function formatFinanceGarageLabel(garageNumber: string | number | null | undefined) {
  return `Гараж ${garageNumber ?? ''}`
}

export function getFinanceMeterKindLabel(kind: string) {
  return kind === 'water' ? 'Вода' : 'Электричество'
}

export function getFinanceToolbarLabel(key: FinanceToolbarLabelKey) {
  return financeToolbarLabels[key]
}

export function getFinanceEditorValidationTitle(section: FinanceEditorKey, variant: FinanceEditorValidationTitleVariant = 'default') {
  return financeEditorValidationTitleVariants[section]?.[variant] ?? financeEditorValidationTitles[section]
}

export function getFinanceEditorUiLabel(key: FinanceEditorUiLabelKey) {
  return financeEditorUiLabels[key]
}

export function getFinancePanelLabel(key: FinancePanelLabelKey) {
  return financePanelLabels[key]
}

export function formatFinanceOperationCount(count: number) {
  return `${count} операций`
}

export function formatFinanceVisibleRange(range: { from: number; to: number }, totalCount: number) {
  return `Показано ${range.from}-${range.to} из ${totalCount}`
}

export function formatFinanceVisibleListStatus(visibleCount: number, totalCount: number, kind: FinanceVisibleListStatusKind) {
  return `Показано ${visibleCount} из ${totalCount} ${financeVisibleListStatusSuffixes[kind]}`
}

export function getFinanceVisibleListEmptyLabel(kind: FinanceVisibleListStatusKind) {
  return financeVisibleListEmptyLabels[kind]
}

export function getFinanceVisibleListTableLabel(kind: FinanceVisibleListStatusKind) {
  return financeVisibleListTableLabels[kind]
}

export function getFinanceVisibleListTableHeaders(kind: FinanceVisibleListStatusKind) {
  return financeVisibleListTableHeaders[kind]
}

export function formatFinanceIncomeGarageSearchStatus(foundCount: number, hasSearch: boolean) {
  return hasSearch ? `Найдено гаражей: ${foundCount}` : `Показаны все гаражи: ${foundCount}`
}
