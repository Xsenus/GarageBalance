export const supplierBalanceWithDebtHelp = 'Укажите баланс на начало учёта: долг перед поставщиком — со знаком минус, аванс поставщику — со знаком плюс. При отсутствии долга и аванса оставьте ноль.'

export function toDisplayedSupplierBalance(storedBalance: number) {
  return storedBalance === 0 ? 0 : -storedBalance
}

export function toStoredSupplierStartingBalance(displayedBalance: number, startingDebt = 0) {
  if (Number.isNaN(displayedBalance)) {
    return displayedBalance
  }

  const effectiveBalance = displayedBalance || (startingDebt > 0 ? -startingDebt : 0)
  return toDisplayedSupplierBalance(effectiveBalance)
}

export function supplierDebtSortDirection(direction: 'asc' | 'desc') {
  return direction === 'asc' ? 'desc' : 'asc'
}
