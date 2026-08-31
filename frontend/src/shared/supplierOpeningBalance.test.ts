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

  it('keeps the displayed balance synchronized only while it follows the debt', () => {
    expect(syncDisplayedSupplierBalanceWithDebt(0, 0, 125)).toBe(-125)
    expect(syncDisplayedSupplierBalanceWithDebt(-125, 125, 150)).toBe(-150)
    expect(syncDisplayedSupplierBalanceWithDebt(-125, 125, 0)).toBe(0)
    expect(syncDisplayedSupplierBalanceWithDebt(-500, 125, 150)).toBe(-500)
  })
})
