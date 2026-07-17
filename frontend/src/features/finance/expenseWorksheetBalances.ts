export type ExpenseWorksheetBalanceSplit = {
  debt: number
  advance: number
}

const atomicCashExpenseTypeCodes = new Set([
  'advance',
  'advance_payment',
  'advance_payments',
  'cash_advance',
  'no_receipt',
  'without_receipt',
  'no_check',
  'without_check',
  'cash_no_receipt',
])

const atomicCashExpenseTypeNames = new Set(['авансовые выплаты', 'выплата без чека'])

export function isAtomicCashExpenseType(code: string | null | undefined, name: string): boolean {
  const normalizedCode = code?.trim().toLocaleLowerCase('ru-RU').replaceAll('-', '_').replaceAll(' ', '_') ?? ''
  const normalizedName = name.trim().toLocaleLowerCase('ru-RU').replaceAll('ё', 'е')
  return atomicCashExpenseTypeCodes.has(normalizedCode) || atomicCashExpenseTypeNames.has(normalizedName)
}

export function splitExpenseWorksheetBalance(balance: number): ExpenseWorksheetBalanceSplit {
  const roundedBalance = Math.round(balance * 100) / 100
  return {
    debt: roundedBalance > 0 ? roundedBalance : 0,
    advance: roundedBalance < 0 ? -roundedBalance : 0,
  }
}

export function calculateExpenseWorksheetClosingBalance(
  openingDebt: number,
  openingAdvance: number,
  cost: number,
  paid: number,
): ExpenseWorksheetBalanceSplit {
  return splitExpenseWorksheetBalance(openingDebt - openingAdvance + cost - paid)
}
