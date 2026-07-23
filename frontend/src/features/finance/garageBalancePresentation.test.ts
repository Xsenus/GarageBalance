import { describe, expect, it } from 'vitest'
import { getGarageBalancePresentation } from './garageBalancePresentation'

describe('getGarageBalancePresentation', () => {
  it('separates overdue and not-yet-overdue portions of a positive total debt', () => {
    expect(getGarageBalancePresentation(1500, 500)).toEqual({
      kind: 'debt',
      label: 'Общий долг',
      amount: 1500,
      moneyClassName: 'money-expense',
      overdueRelation: 'partly-overdue',
      notOverdueDebt: 1000,
    })
  })

  it('marks a total debt as fully overdue when the amounts are equal', () => {
    expect(getGarageBalancePresentation(500, 500)).toMatchObject({
      kind: 'debt',
      overdueRelation: 'fully-overdue',
      notOverdueDebt: 0,
    })
  })

  it('shows an advance as a positive green amount without hiding service-specific overdue debt', () => {
    expect(getGarageBalancePresentation(-750, 200)).toEqual({
      kind: 'advance',
      label: 'Аванс',
      amount: 750,
      moneyClassName: 'money-income',
      overdueRelation: 'service-specific-overdue',
      notOverdueDebt: 0,
    })
  })

  it('uses a neutral balance for a settled garage without overdue debt', () => {
    expect(getGarageBalancePresentation(0, 0)).toEqual({
      kind: 'settled',
      label: 'Баланс',
      amount: 0,
      moneyClassName: undefined,
      overdueRelation: 'no-overdue',
      notOverdueDebt: 0,
    })
  })
})
