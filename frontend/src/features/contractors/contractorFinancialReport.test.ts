import { describe, expect, it } from 'vitest'
import { createSupplierStartingBalanceEntries } from './contractorFinancialReport'

describe('createSupplierStartingBalanceEntries', () => {
  it('creates an opening obligation row for a positive starting balance', () => {
    expect(createSupplierStartingBalanceEntries('supplier-1', 1250, '2026-06')).toEqual([{
      id: 'supplier-starting-balance-supplier-1',
      accountingMonth: '2026-06-01',
      date: '2026-06-01',
      documentNumber: '—',
      description: 'Стартовый баланс',
      accrualAmount: 1250,
      paymentAmount: 0,
    }])
  })

  it('keeps a negative starting balance as an advance that reduces the debt', () => {
    expect(createSupplierStartingBalanceEntries('supplier-2', -300, '2026-07')).toEqual([
      expect.objectContaining({ accrualAmount: -300, paymentAmount: 0 }),
    ])
  })

  it('does not add a report row when the starting balance is zero', () => {
    expect(createSupplierStartingBalanceEntries('supplier-3', 0, '2026-08')).toEqual([])
  })
})
