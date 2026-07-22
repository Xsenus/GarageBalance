export type ContractorFinancialReportEntry = {
  id: string
  accountingMonth: string
  date: string
  documentNumber: string
  description: string
  accrualAmount: number
  paymentAmount: number
}

export function createSupplierStartingBalanceEntries(supplierId: string, startingBalance: number, monthFrom: string): ContractorFinancialReportEntry[] {
  if (startingBalance === 0) {
    return []
  }

  const periodStart = `${monthFrom}-01`
  return [{
    id: `supplier-starting-balance-${supplierId}`,
    accountingMonth: periodStart,
    date: periodStart,
    documentNumber: '—',
    description: 'Стартовый баланс',
    accrualAmount: startingBalance,
    paymentAmount: 0,
  }]
}
