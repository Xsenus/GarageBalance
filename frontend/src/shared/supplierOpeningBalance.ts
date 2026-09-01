export const supplierBalanceWithDebtHelp = 'Долг вводится со знаком минус, аванс — со знаком плюс. Баланс долга и задолженность совпадают.'
export const supplierStartingDebtHelp = 'Введите долг без минуса: система задаст равный отрицательный баланс. При авансе — ноль.'

export function toDisplayedSupplierStartingBalance(storedBalance: number) {
  return storedBalance === 0 ? 0 : -storedBalance
}

export function toStoredSupplierStartingBalance(displayedBalance: number, startingDebt = 0) {
  if (Number.isNaN(displayedBalance)) {
    return displayedBalance
  }

  const effectiveBalance = displayedBalance || (startingDebt > 0 ? -startingDebt : 0)
  return effectiveBalance === 0 ? 0 : -effectiveBalance
}

export function syncDisplayedSupplierBalanceWithDebt(nextDebt: number) {
  return nextDebt > 0 ? -nextDebt : 0
}
