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

export function getFullPaymentOutstandingAmount(row: GarageIncomePrototypeRow) {
  return fromMoneyMinorUnits(Math.max(
    toMoneyMinorUnits(row.debt) - toMoneyMinorUnits(row.advance),
    0,
  ))
}

export function getFullPaymentRows(rows: GarageIncomePrototypeRow[], period: string) {
  const annualRows = new Map<string, { first: GarageIncomePrototypeRow; latest: GarageIncomePrototypeRow }>()
  for (const row of rows) {
    if (!row.annualAccrualId) {
      continue
    }

    const current = annualRows.get(row.annualAccrualId)
    if (!current) {
      annualRows.set(row.annualAccrualId, { first: row, latest: row })
      continue
    }

    annualRows.set(row.annualAccrualId, {
      first: row.month < current.first.month ? row : current.first,
      latest: row.month > current.latest.month ? row : current.latest,
    })
  }

  const candidates = period === 'full'
    ? [
        ...rows.filter((row) => !row.annualAccrualId),
        ...Array.from(annualRows.values()).map(({ first, latest }) => ({
          ...first,
          debt: latest.debt,
          advance: latest.advance,
        })),
      ]
    : rows
        .filter((row) => row.month === period)
        .map((row) => {
          if (!row.annualAccrualId) {
            return row
          }

          const latest = annualRows.get(row.annualAccrualId)?.latest ?? row
          return {
            ...row,
            debt: latest.debt,
            advance: latest.advance,
          }
        })

  return candidates
    .filter((row) => getFullPaymentOutstandingAmount(row) > 0)
    .sort((left, right) => left.month.localeCompare(right.month))
}

export function sumPaymentDebt(rows: GarageIncomePrototypeRow[], openingDebt = 0) {
  const rowDebt = rows.reduce((sum, row) => sum + toMoneyMinorUnits(getFullPaymentOutstandingAmount(row)), 0)
  return fromMoneyMinorUnits(rowDebt + Math.max(toMoneyMinorUnits(openingDebt), 0))
}

export function createFullPaymentAllocations(rows: GarageIncomePrototypeRow[], amount: number) {
  let remainingMinorUnits = Math.max(toMoneyMinorUnits(amount), 0)
  const allocations: FullPaymentAllocation[] = []

  for (const row of rows) {
    if (remainingMinorUnits <= 0) {
      break
    }

    const rowDebtMinorUnits = toMoneyMinorUnits(getFullPaymentOutstandingAmount(row))
    const allocatedMinorUnits = Math.min(rowDebtMinorUnits, remainingMinorUnits)
    if (allocatedMinorUnits <= 0) {
      continue
    }

    allocations.push({ row, amount: fromMoneyMinorUnits(allocatedMinorUnits) })
    remainingMinorUnits -= allocatedMinorUnits
  }

  return allocations
}
