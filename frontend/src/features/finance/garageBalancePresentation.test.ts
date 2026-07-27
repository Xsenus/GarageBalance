import { describe, expect, it } from 'vitest'
import { getGarageBalancePresentation } from './garageBalancePresentation'

describe('getGarageBalancePresentation', () => {
  it('classifies a positive total debt with a smaller overdue part without calculating a separate hint', () => {
    expect(getGarageBalancePresentation(1500, 500)).toEqual({
      kind: 'debt',
      label: 'Общий долг',
      amount: 1500,
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

  it('shows an advance as a positive green amount without hiding service-specific overdue debt', () => {
    expect(getGarageBalancePresentation(-750, 200)).toEqual({
      kind: 'advance',
      label: 'Аванс',
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
