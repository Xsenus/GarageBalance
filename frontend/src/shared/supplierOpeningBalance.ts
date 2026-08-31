export const supplierBalanceWithDebtHelp = 'Долг поставщику вводится со знаком минус, аванс поставщику — со знаком плюс. При нулевом балансе введённая задолженность автоматически формирует такой же долг.'
export const supplierStartingDebtHelp = 'Начальная задолженность входит во входящий баланс поставщика и при нулевом балансе заполняет его автоматически.'

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

export function syncDisplayedSupplierBalanceWithDebt(
  displayedBalance: number,
  previousDebt: number,
  nextDebt: number,
) {
  if (displayedBalance !== 0 && (previousDebt <= 0 || displayedBalance !== -previousDebt)) {
    return displayedBalance
  }

  return nextDebt > 0 ? -nextDebt : 0
}
