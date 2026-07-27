import { describe, expect, it } from 'vitest'
import { getGarageBalancePresentation, toSignedGarageNetBalance, toSignedGarageSplitBalance } from './garageBalancePresentation'

describe('getGarageBalancePresentation', () => {
  it('classifies a positive total debt with a smaller overdue part without calculating a separate hint', () => {
    expect(getGarageBalancePresentation(1500, 500)).toEqual({
      kind: 'debt',
      label: 'Баланс',
      amount: -1500,
      moneyClassName: 'money-expense',
      overdueRelation: 'partly-overdue',
    })
  })

  it('marks a total debt as fully overdue when the amounts are equal', () => {
    expect(getGarageBalancePresentation(500, 500)).toMatchObject({
      kind: 'debt',
      overdueRelation: 'fully-overdue',
    })
  })

  it('shows an advance as a positive green balance without hiding service-specific overdue debt', () => {
    expect(getGarageBalancePresentation(-750, 200)).toEqual({
      kind: 'advance',
      label: 'Баланс',
      amount: 750,
      moneyClassName: 'money-income',
      overdueRelation: 'service-specific-overdue',
    })
  })

  it('uses a neutral balance for a settled garage without overdue debt', () => {
    expect(getGarageBalancePresentation(0, 0)).toEqual({
      kind: 'settled',
      label: 'Баланс',
      amount: 0,
      moneyClassName: undefined,
      overdueRelation: 'no-overdue',
    })
  })
})

describe('signed garage balances', () => {
  it('converts the API debt convention to a negative UI balance and rounds money', () => {
    expect(toSignedGarageNetBalance(125.555)).toBe(-125.56)
    expect(toSignedGarageNetBalance(-80)).toBe(80)
  })

  it('combines row debt and advance into one signed balance', () => {
    expect(toSignedGarageSplitBalance(125.55, 0)).toBe(-125.55)
    expect(toSignedGarageSplitBalance(0, 80)).toBe(80)
    expect(toSignedGarageSplitBalance(100, 25)).toBe(-75)
  })
})
