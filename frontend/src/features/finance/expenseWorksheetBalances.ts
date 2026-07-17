export type ExpenseWorksheetBalanceSplit = {
  debt: number
  advance: number
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
