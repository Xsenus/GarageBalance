export type GarageBalanceKind = 'debt' | 'advance' | 'settled'

export type GarageBalanceRelation = 'no-overdue' | 'partly-overdue' | 'fully-overdue' | 'service-specific-overdue'

export type GarageBalancePresentation = {
  kind: GarageBalanceKind
  label: 'Баланс'
  amount: number
  moneyClassName?: 'money-expense' | 'money-income'
  overdueRelation: GarageBalanceRelation
}

function roundMoney(value: number) {
  return Math.sign(value) * Math.round((Math.abs(value) + Number.EPSILON) * 100) / 100
}

export function toSignedGarageNetBalance(balance: number) {
  const roundedBalance = roundMoney(balance)
  return roundedBalance === 0 ? 0 : -roundedBalance
}

export function toSignedGarageSplitBalance(debt: number, advance: number) {
  return roundMoney(advance - debt)
}

export function getGarageBalancePresentation(balance: number, overdueDebt: number): GarageBalancePresentation {
  const roundedBalance = roundMoney(balance)
  const roundedOverdueDebt = Math.max(roundMoney(overdueDebt), 0)
  const kind: GarageBalanceKind = roundedBalance > 0 ? 'debt' : roundedBalance < 0 ? 'advance' : 'settled'
  const amount = toSignedGarageNetBalance(roundedBalance)

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
    label: 'Баланс',
    amount,
    moneyClassName: kind === 'debt' ? 'money-expense' : kind === 'advance' ? 'money-income' : undefined,
    overdueRelation,
  }
}
