import type { ExpensePaymentSource, ExpensePaymentType } from '../../services/financeApi'

export const expensePaymentTypeOptions = [
  { value: 'with_receipt', label: 'С чеком' },
  { value: 'without_receipt', label: 'Без чека' },
] satisfies Array<{ value: ExpensePaymentType; label: string }>

export function formatExpensePaymentType(value: ExpensePaymentType | null | undefined) {
  return value === 'without_receipt' ? 'Без чека' : 'С чеком'
}

export function formatExpensePaymentSource(
  value: ExpensePaymentSource | null | undefined,
  legacyPaymentType?: ExpensePaymentType | null,
) {
  return value === 'cash' || (!value && legacyPaymentType === 'without_receipt') ? 'Касса' : 'Банк'
}
