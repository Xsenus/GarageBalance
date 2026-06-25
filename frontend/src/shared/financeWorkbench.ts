export type FinanceSectionKey = 'income' | 'expense' | 'accruals' | 'supplierAccruals' | 'meterReadings'
export type FinanceEditorKey = FinanceSectionKey | 'regularAccruals' | 'supplierGroupSalaryAccruals'

export type FinanceSectionOption = {
  key: FinanceSectionKey
  label: string
  description: string
}

export type FinanceEditorSavingScope =
  | 'income'
  | 'expense'
  | 'accrual'
  | 'regular-accruals'
  | 'salary-accruals'
  | 'supplier-accrual'
  | 'meter-reading'

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

export function getFinanceEditorTitle(section: FinanceEditorKey) {
  return financeEditorTitles[section]
}

export function getFinanceEditorSubmitLabel(section: FinanceEditorKey) {
  return financeEditorSubmitLabels[section]
}

export function getFinanceEditorSavingScope(section: FinanceEditorKey) {
  return financeEditorSavingScopes[section]
}
