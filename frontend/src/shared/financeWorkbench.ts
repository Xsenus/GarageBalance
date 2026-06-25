export type FinanceSectionKey = 'income' | 'expense' | 'accruals' | 'supplierAccruals' | 'meterReadings'
export type FinanceEditorKey = FinanceSectionKey | 'regularAccruals' | 'supplierGroupSalaryAccruals'

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
  | 'periodFilter'
  | 'periodFrom'
  | 'periodTo'
  | 'search'
  | 'searchPlaceholder'
  | 'regularAccruals'
  | 'supplierGroupSalaryAccruals'
  | 'tableArea'
  | 'emptyState'
  | 'pagination'
  | 'rows'
  | 'pageSize'

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
  periodFilter: 'Фильтр периода',
  periodFrom: 'Период с',
  periodTo: 'Период по',
  search: 'Поиск по платежам',
  searchPlaceholder: 'Гараж, владелец, поставщик или документ',
  regularAccruals: 'Регулярные',
  supplierGroupSalaryAccruals: 'Зарплата группы',
  tableArea: 'Рабочая область платежной таблицы',
  emptyState: 'По выбранным условиям записей нет',
  pagination: 'Пагинация платежей',
  rows: 'Строк',
  pageSize: 'Количество строк платежей',
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
