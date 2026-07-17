// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { calculateExpenseWorksheetClosingBalance, splitExpenseWorksheetBalance } from './expenseWorksheetBalances'

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
})
