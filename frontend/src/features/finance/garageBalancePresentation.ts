export type GarageBalanceKind = 'debt' | 'advance' | 'settled'

export type GarageBalanceRelation = 'no-overdue' | 'partly-overdue' | 'fully-overdue' | 'service-specific-overdue'

export type GarageBalancePresentation = {
  kind: GarageBalanceKind
  label: 'Общий долг' | 'Аванс' | 'Баланс'
  amount: number
  moneyClassName?: 'money-expense' | 'money-income'
  overdueRelation: GarageBalanceRelation
  notOverdueDebt: number
}

function roundMoney(value: number) {
  return Math.round((value + Number.EPSILON) * 100) / 100
}

export function getGarageBalancePresentation(balance: number, overdueDebt: number): GarageBalancePresentation {
  const roundedBalance = roundMoney(balance)
  const roundedOverdueDebt = Math.max(roundMoney(overdueDebt), 0)
  const kind: GarageBalanceKind = roundedBalance > 0 ? 'debt' : roundedBalance < 0 ? 'advance' : 'settled'
  const amount = Math.abs(roundedBalance)
  const notOverdueDebt = kind === 'debt'
    ? Math.max(roundMoney(roundedBalance - roundedOverdueDebt), 0)
    : 0

  let overdueRelation: GarageBalanceRelation = 'no-overdue'
  if (roundedOverdueDebt > 0) {
    overdueRelation = kind === 'debt' && roundedBalance > roundedOverdueDebt
      ? 'partly-overdue'
      : kind === 'debt' && roundedBalance === roundedOverdueDebt
        ? 'fully-overdue'
        : 'service-specific-overdue'
  }

  return {
    kind,
    label: kind === 'debt' ? 'Общий долг' : kind === 'advance' ? 'Аванс' : 'Баланс',
    amount,
    moneyClassName: kind === 'debt' ? 'money-expense' : kind === 'advance' ? 'money-income' : undefined,
    overdueRelation,
    notOverdueDebt,
  }
}
