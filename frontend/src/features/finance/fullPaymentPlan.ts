import type { GarageIncomePrototypeRow } from './garageIncomeWorksheetRows'

export type FullPaymentAllocation = {
  row: GarageIncomePrototypeRow
  amount: number
}

export function toMoneyMinorUnits(value: number) {
  return Math.round(Number((value * 100).toFixed(6)))
}

export function fromMoneyMinorUnits(value: number) {
  return value / 100
}

export function roundPaymentMoney(value: number) {
  return fromMoneyMinorUnits(toMoneyMinorUnits(value))
}

export function getFullPaymentRows(rows: GarageIncomePrototypeRow[], period: string) {
  const payableRows = rows
    .filter((row) => toMoneyMinorUnits(row.debt) > 0 && (period === 'full' || row.month === period))

  if (period !== 'full') {
    return payableRows
  }

  const seenAnnualAccruals = new Set<string>()
  return payableRows
    .sort((left, right) => left.month.localeCompare(right.month))
    .filter((row) => {
      if (!row.annualAccrualId) {
        return true
      }
      if (seenAnnualAccruals.has(row.annualAccrualId)) {
        return false
      }
      seenAnnualAccruals.add(row.annualAccrualId)
      return true
    })
}

export function sumPaymentDebt(rows: GarageIncomePrototypeRow[], openingDebt = 0) {
  const rowDebt = rows.reduce((sum, row) => sum + toMoneyMinorUnits(row.debt), 0)
  return fromMoneyMinorUnits(rowDebt + Math.max(toMoneyMinorUnits(openingDebt), 0))
}

export function createFullPaymentAllocations(rows: GarageIncomePrototypeRow[], amount: number) {
  let remainingMinorUnits = Math.max(toMoneyMinorUnits(amount), 0)
  const allocations: FullPaymentAllocation[] = []

  for (const row of rows) {
    if (remainingMinorUnits <= 0) {
      break
    }

    const rowDebtMinorUnits = Math.max(toMoneyMinorUnits(row.debt), 0)
    const allocatedMinorUnits = Math.min(rowDebtMinorUnits, remainingMinorUnits)
    if (allocatedMinorUnits <= 0) {
      continue
    }

    allocations.push({ row, amount: fromMoneyMinorUnits(allocatedMinorUnits) })
    remainingMinorUnits -= allocatedMinorUnits
  }

  return allocations
}
