import { requestFinanceJson } from './financeApi'
import { invalidateFundsResponseCache } from './fundsApi'

export type ExpenseBatchPreviewRequest = { accountingMonth: string; operationDate: string }
export type ExpenseBatchPayment = {
  recipientKind: 'supplier' | 'staff'
  recipientId: string
  expenseTypeId: string
  fundId: string | null
  accountingMonth: string
  amount: number
  paymentSource: 'bank' | 'cash'
}
export type ExpenseBatchPreview = ExpenseBatchPreviewRequest & {
  items: { payment: ExpenseBatchPayment; recipientName: string; expenseTypeName: string }[]
  bankAmount: number
  cashAmount: number
  availableBankAmount: number
  availableCashAmount: number
  funds: { fundId: string; name: string; amount: number; availableAmount: number }[]
  issues: string[]
  requiresNegativeFundConfirmation: boolean
  fingerprint: string
  canSubmit: boolean
}
export type ExpenseBatchPaymentRequest = ExpenseBatchPreviewRequest & {
  requestId: string
  fingerprint: string
  confirmNegativeFundBalance: boolean
  comment: string | null
}
export type ExpenseBatchPaymentResult = { requestId: string; operationIds: string[] }

export const expenseBatchesApi = {
  preview(accessToken: string, request: ExpenseBatchPreviewRequest, signal?: AbortSignal) {
    return requestFinanceJson<ExpenseBatchPreview>(accessToken, '/api/finance/expense-batches/preview', {
      method: 'POST', body: JSON.stringify(request), signal,
    })
  },
  async pay(accessToken: string, request: ExpenseBatchPaymentRequest, signal?: AbortSignal) {
    const result = await requestFinanceJson<ExpenseBatchPaymentResult>(accessToken, '/api/finance/expense-batches', {
      method: 'POST', body: JSON.stringify(request), signal,
    })
    invalidateFundsResponseCache(accessToken)
    return result
  },
}
