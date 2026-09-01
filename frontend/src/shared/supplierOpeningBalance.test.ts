// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { syncDisplayedSupplierBalanceWithDebt, toDisplayedSupplierStartingBalance, toStoredSupplierStartingBalance } from './supplierOpeningBalance'

describe('supplier opening balance boundary', () => {
  it('shows supplier debt as a negative balance and advance as a positive balance', () => {
    expect(toDisplayedSupplierStartingBalance(125)).toBe(-125)
    expect(toDisplayedSupplierStartingBalance(-40)).toBe(40)
    expect(toDisplayedSupplierStartingBalance(0)).toBe(0)
  })

  it('includes entered debt in the stored opening balance', () => {
    expect(toStoredSupplierStartingBalance(-125, 125)).toBe(125)
    expect(toStoredSupplierStartingBalance(40, 0)).toBe(-40)
    expect(toStoredSupplierStartingBalance(0, 125)).toBe(125)
    expect(toStoredSupplierStartingBalance(Number.NaN, 0)).toBeNaN()
  })

  it('always derives the displayed debt balance from the entered debt', () => {
    expect(syncDisplayedSupplierBalanceWithDebt(125)).toBe(-125)
    expect(syncDisplayedSupplierBalanceWithDebt(150)).toBe(-150)
    expect(syncDisplayedSupplierBalanceWithDebt(0)).toBe(0)
    expect(syncDisplayedSupplierBalanceWithDebt(-10)).toBe(0)
  })

})
