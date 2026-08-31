export const garageBalanceSignHelp = 'Долг вводится со знаком минус, переплата — со знаком плюс.'
export const garageBalanceWithOverdueHelp = `${garageBalanceSignHelp} Просрочка при нулевом балансе автоматически создаёт такой же долг.`
export const garageOverdueHelp = 'Просрочка входит в общий долг и при нулевом балансе заполняет его автоматически.'

export function toDisplayedGarageStartingBalance(storedBalance: number) {
  return storedBalance === 0 ? 0 : -storedBalance
}

export function toStoredGarageStartingBalance(displayedBalance: number, startingOverdueDebt = 0) {
  if (Number.isNaN(displayedBalance)) {
    return displayedBalance
  }

  const effectiveBalance = displayedBalance || (startingOverdueDebt > 0 ? -startingOverdueDebt : 0)
  return effectiveBalance === 0 ? 0 : -effectiveBalance
}

export function syncDisplayedGarageBalanceWithOverdue(
  displayedBalance: number,
  previousOverdueDebt: number,
  nextOverdueDebt: number,
) {
  if (displayedBalance !== 0 && (previousOverdueDebt <= 0 || displayedBalance !== -previousOverdueDebt)) {
    return displayedBalance
  }

  return nextOverdueDebt > 0 ? -nextOverdueDebt : 0
}
