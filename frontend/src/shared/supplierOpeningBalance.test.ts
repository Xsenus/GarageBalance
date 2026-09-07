// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { supplierDebtSortDirection, toDisplayedSupplierBalance, toStoredSupplierStartingBalance } from './supplierOpeningBalance'

describe('supplier balance boundary', () => {
  it('shows supplier debt as a negative balance and advance as a positive balance', () => {
    expect(toDisplayedSupplierBalance(125)).toBe(-125)
    expect(toDisplayedSupplierBalance(-40)).toBe(40)
    expect(toDisplayedSupplierBalance(0)).toBe(0)
  })

  it('reverses stored debt order to match visible balance order', () => {
    expect(supplierDebtSortDirection('asc')).toBe('desc')
    expect(supplierDebtSortDirection('desc')).toBe('asc')
  })

  it('includes entered debt in the stored opening balance', () => {
    expect(toStoredSupplierStartingBalance(-125, 125)).toBe(125)
    expect(toStoredSupplierStartingBalance(40, 0)).toBe(-40)
    expect(toStoredSupplierStartingBalance(0, 125)).toBe(125)
    expect(toStoredSupplierStartingBalance(Number.NaN, 0)).toBeNaN()
  })

  it('preserves an explicit advance and returns positive zero at both boundaries', () => {
    expect(toStoredSupplierStartingBalance(40, 125)).toBe(-40)
    expect(Object.is(toStoredSupplierStartingBalance(0), -0)).toBe(false)
    expect(Object.is(toDisplayedSupplierBalance(0), -0)).toBe(false)
  })

})
