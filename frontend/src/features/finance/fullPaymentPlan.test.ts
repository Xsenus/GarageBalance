import { describe, expect, it } from 'vitest'
import { createFullPaymentAllocations, getFullPaymentRows, roundPaymentMoney, sumPaymentDebt } from './fullPaymentPlan'
import type { GarageIncomePrototypeRow } from './garageIncomeWorksheetRows'

function createRow(overrides: Partial<GarageIncomePrototypeRow> = {}): GarageIncomePrototypeRow {
  return {
    id: 'row',
    incomeTypeId: 'income-type',
    month: '2026-08',
    monthLabel: 'авг.26',
    service: 'Услуга',
    annualAccrualId: null,
    feeCampaignId: null,
    irregularPaymentId: null,
    meterKind: null,
    meterReadingId: null,
    meterReadingVersion: null,
    meterReadingDate: null,
    meter: null,
    meterDraft: '',
    meterError: null,
    difference: null,
    payable: 0,
    paymentDraft: '',
    paid: 0,
    advance: 0,
    debt: 0,
    ...overrides,
  }
}

describe('full payment plan', () => {
  it('includes regular, fee campaign and irregular debts in the selected month and full total', () => {
    const rows = [
      createRow({ id: 'regular', debt: 1000 }),
      createRow({ id: 'fee', debt: 300, feeCampaignId: 'fee-campaign' }),
      createRow({ id: 'irregular', debt: 3000, irregularPaymentId: 'irregular-payment' }),
      createRow({ id: 'other-month', month: '2026-07', debt: 200 }),
    ]

    expect(getFullPaymentRows(rows, '2026-08').map((row) => row.id)).toEqual(['regular', 'fee', 'irregular'])
    expect(sumPaymentDebt(getFullPaymentRows(rows, '2026-08'))).toBe(4300)
    expect(sumPaymentDebt(getFullPaymentRows(rows, 'full'))).toBe(4500)
  })

  it('counts a carried annual obligation once in the full total', () => {
    const rows = [
      createRow({ id: 'annual-july', month: '2026-07', debt: 700, annualAccrualId: 'annual' }),
      createRow({ id: 'annual-august', debt: 700, annualAccrualId: 'annual' }),
    ]

    expect(getFullPaymentRows(rows, 'full').map((row) => row.id)).toEqual(['annual-july'])
    expect(sumPaymentDebt(getFullPaymentRows(rows, 'full'))).toBe(700)
  })

  it('allocates fractional amounts without leaving a floating-point kopeck remainder', () => {
    const rows = [
      createRow({ id: 'first', debt: 0.1 }),
      createRow({ id: 'second', debt: 0.2 }),
      createRow({ id: 'third', debt: 300.01 }),
    ]

    expect(sumPaymentDebt(rows)).toBe(300.31)
    expect(createFullPaymentAllocations(rows, 300.31)).toEqual([
      { row: rows[0], amount: 0.1 },
      { row: rows[1], amount: 0.2 },
      { row: rows[2], amount: 300.01 },
    ])
    expect(roundPaymentMoney(0.1 + 0.2)).toBe(0.3)
  })
})
