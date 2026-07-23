// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { calculateExpenseWorksheetClosingBalance, getExpenseWorksheetCollectedClassName, splitExpenseWorksheetBalance, toSignedExpenseWorksheetBalance } from './expenseWorksheetBalances'

describe('expense worksheet balances', () => {
  it('separates debt, advance and zero without ambiguous negative values', () => {
    expect(splitExpenseWorksheetBalance(125.5)).toEqual({ debt: 125.5, advance: 0 })
    expect(splitExpenseWorksheetBalance(-40.25)).toEqual({ debt: 0, advance: 40.25 })
    expect(splitExpenseWorksheetBalance(0)).toEqual({ debt: 0, advance: 0 })
  })

  it('carries the incoming balance through current cost and payment', () => {
    expect(calculateExpenseWorksheetClosingBalance(100, 0, 50, 20)).toEqual({ debt: 130, advance: 0 })
    expect(calculateExpenseWorksheetClosingBalance(0, 100, 20, 50)).toEqual({ debt: 0, advance: 130 })
    expect(calculateExpenseWorksheetClosingBalance(20, 0, 30, 50)).toEqual({ debt: 0, advance: 0 })
  })

  it('shows debt as a negative balance and advance as a positive balance', () => {
    expect(toSignedExpenseWorksheetBalance(125.5, 0)).toBe(-125.5)
    expect(toSignedExpenseWorksheetBalance(0, 40.25)).toBe(40.25)
    expect(toSignedExpenseWorksheetBalance(25.111, 10.555)).toBe(-14.56)
    expect(toSignedExpenseWorksheetBalance(0, 0)).toBe(0)
  })

  it('colors only available collections against the monthly service cost', () => {
    expect(getExpenseWorksheetCollectedClassName(99.99, 100)).toBe('money-expense')
    expect(getExpenseWorksheetCollectedClassName(100, 100)).toBe('money-income')
    expect(getExpenseWorksheetCollectedClassName(125, 100)).toBe('money-income')
    expect(getExpenseWorksheetCollectedClassName(0, 0)).toBe('money-income')
    expect(getExpenseWorksheetCollectedClassName(null, 100)).toBeUndefined()
    expect(getExpenseWorksheetCollectedClassName(100, null)).toBeUndefined()
    expect(getExpenseWorksheetCollectedClassName('', 100)).toBeUndefined()
    expect(getExpenseWorksheetCollectedClassName(100, '')).toBeUndefined()
  })

})
