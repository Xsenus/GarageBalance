export type ExpenseWorksheetBalanceSplit = {
  debt: number
  advance: number
}

export type ExpenseWorksheetCollectedClassName = 'money-income' | 'money-expense'

export function splitExpenseWorksheetBalance(balance: number): ExpenseWorksheetBalanceSplit {
  const roundedBalance = Math.round(balance * 100) / 100
  return {
    debt: roundedBalance > 0 ? roundedBalance : 0,
    advance: roundedBalance < 0 ? -roundedBalance : 0,
  }
}

export function toSignedExpenseWorksheetBalance(debt: number, advance: number): number {
  return Math.round((advance - debt) * 100) / 100
}

export function getExpenseWorksheetCollectedClassName(
  collected: number | string | null | undefined,
  cost: number | string | null | undefined,
): ExpenseWorksheetCollectedClassName | undefined {
  if (typeof collected !== 'number' || typeof cost !== 'number') {
    return undefined
  }

  return collected < cost ? 'money-expense' : 'money-income'
}

export function calculateExpenseWorksheetClosingBalance(
  openingDebt: number,
  openingAdvance: number,
  cost: number,
  paid: number,
): ExpenseWorksheetBalanceSplit {
  return splitExpenseWorksheetBalance(openingDebt - openingAdvance + cost - paid)
}
