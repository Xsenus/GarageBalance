import { describe, expect, it } from 'vitest'
import type { GarageIncomeWorksheetDto } from '../../services/financeApi'
import { createGarageIncomeRowsFromWorksheet } from './garageIncomeWorksheetRows'

function worksheet(rows: GarageIncomeWorksheetDto['rows']): GarageIncomeWorksheetDto {
  return {
    garageId: 'garage-1',
    garageNumber: '103',
    ownerName: 'Test Owner',
    monthFrom: '2026-08-01',
    monthTo: '2026-08-01',
    openingBalance: 0,
    openingDebt: 0,
    accrualTotal: 1000,
    incomeTotal: 400,
    debtTotal: 600,
    closingBalance: 600,
    closingDebt: 600,
    rows,
  }
}

describe('irregular payment worksheet mapping', () => {
  it('keeps the unpaid remainder linked to its irregular payment', () => {
    const rows = createGarageIncomeRowsFromWorksheet(worksheet([{
      accountingMonth: '2026-08-01',
      incomeTypeId: 'other-payments',
      incomeTypeName: 'Access card',
      meterKind: null,
      meterValue: null,
      meterConsumption: null,
      accrualAmount: 1000,
      incomeAmount: 400,
      debt: 600,
      irregularPaymentId: 'irregular-access-card',
      irregularPaymentRemainingAmount: 600,
    }]))

    expect(rows).toHaveLength(1)
    expect(rows[0]).toMatchObject({
      id: 'garage-garage-1-2026-08-irregular-access-card',
      irregularPaymentId: 'irregular-access-card',
      irregularPaymentRemainingAmount: 600,
      paid: 400,
      debt: 600,
    })
  })

  it('removes the row when a refreshed worksheet no longer returns the paid or inactive item', () => {
    expect(createGarageIncomeRowsFromWorksheet(worksheet([]))).toEqual([])
  })
})
