export type ContractorFinancialReportEntry = {
  id: string
  accountingMonth: string
  date: string
  documentNumber: string
  description: string
  accrualAmount: number
  paymentAmount: number
  sortOrder?: number
}

export function createSupplierOpeningBalanceEntries(
  supplierId: string,
  openingBalance: number,
  monthFrom: string,
  hasPriorActivity: boolean,
): ContractorFinancialReportEntry[] {
  if (openingBalance === 0 && !hasPriorActivity) {
    return []
  }

  const periodStart = `${monthFrom}-01`
  return [{
    id: `supplier-opening-balance-${supplierId}`,
    accountingMonth: periodStart,
    date: periodStart,
    documentNumber: '—',
    description: hasPriorActivity ? 'Входящий остаток' : 'Стартовый баланс',
    accrualAmount: 0,
    paymentAmount: 0,
    sortOrder: -1,
  }]
}
