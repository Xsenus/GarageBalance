import { describe, expect, it } from 'vitest'
import { createSupplierOpeningBalanceEntries } from './contractorFinancialReport'

describe('createSupplierOpeningBalanceEntries', () => {
  it('creates an opening row without adding it to period accruals', () => {
    expect(createSupplierOpeningBalanceEntries('supplier-1', 1250, '2026-06', true)).toEqual([{
      id: 'supplier-opening-balance-supplier-1',
      accountingMonth: '2026-06-01',
      date: '2026-06-01',
      documentNumber: '—',
      description: 'Входящий остаток',
      accrualAmount: 0,
      paymentAmount: 0,
      sortOrder: -1,
    }])
  })

  it('keeps a pure starting balance visibly distinguished from prior activity', () => {
    expect(createSupplierOpeningBalanceEntries('supplier-2', -300, '2026-07', false)).toEqual([
      expect.objectContaining({ description: 'Стартовый баланс', accrualAmount: 0, paymentAmount: 0 }),
    ])
  })

  it('keeps a zero incoming balance row when prior activity offsets fully', () => {
    expect(createSupplierOpeningBalanceEntries('supplier-3', 0, '2026-08', true)).toHaveLength(1)
  })

  it('does not add a report row when there is no opening balance or prior activity', () => {
    expect(createSupplierOpeningBalanceEntries('supplier-4', 0, '2026-08', false)).toEqual([])
  })
})
