// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { syncDisplayedGarageBalanceWithOverdue, toDisplayedGarageStartingBalance, toStoredGarageStartingBalance } from './garageOpeningBalance'

describe('garage opening balance boundary', () => {
  it('shows stored debt as a negative balance and stored overpayment as a positive balance', () => {
    expect(toDisplayedGarageStartingBalance(125)).toBe(-125)
    expect(toDisplayedGarageStartingBalance(-40)).toBe(40)
    expect(toDisplayedGarageStartingBalance(0)).toBe(0)
  })

  it('converts the displayed accounting sign back to the stored calculation sign', () => {
    expect(toStoredGarageStartingBalance(-125, 125)).toBe(125)
    expect(toStoredGarageStartingBalance(40, 0)).toBe(-40)
    expect(toStoredGarageStartingBalance(0, 125)).toBe(125)
    expect(toStoredGarageStartingBalance(Number.NaN, 0)).toBeNaN()
  })

  it('derives and updates the negative balance only while it follows the overdue amount', () => {
    expect(syncDisplayedGarageBalanceWithOverdue(0, 0, 125)).toBe(-125)
    expect(syncDisplayedGarageBalanceWithOverdue(-125, 125, 150)).toBe(-150)
    expect(syncDisplayedGarageBalanceWithOverdue(-125, 125, 0)).toBe(0)
    expect(syncDisplayedGarageBalanceWithOverdue(-500, 125, 150)).toBe(-500)
  })
})
