import { useEffect, useRef, useState } from 'react'
import { expenseBatchesApi, type ExpenseBatchPaymentRequest, type ExpenseBatchPaymentResult, type ExpenseBatchPreview } from '../../services/expenseBatchesApi'
import { FinanceApiError } from '../../services/financeApi'

export function useExpenseBatchPayment({ accessToken, open, accountingMonth, operationDate, canPay, client = expenseBatchesApi }: {
  accessToken: string
  open: boolean
  accountingMonth: string
  operationDate: string
  canPay: boolean
  client?: typeof expenseBatchesApi
}) {
  const [preview, setPreview] = useState<ExpenseBatchPreview | null>(null)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ExpenseBatchPaymentResult | null>(null)
  const [uncertain, setUncertain] = useState(false)
  const [revision, setRevision] = useState(0)
  // Retain the exact request while this workflow is mounted, including a closed dialog.
  // No payment details or access tokens are persisted in browser storage.
  const attempt = useRef<ExpenseBatchPaymentRequest | null>(null)
  const busy = useRef(false)
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => { mounted.current = false }
  }, [])

  useEffect(() => {
    if (!open || !canPay || attempt.current) return
    const controller = new AbortController()
    async function load() {
      setLoading(true)
      setPreview(null)
      setResult(null)
      setError(null)
      try {
        const next = await client.preview(accessToken, { accountingMonth, operationDate }, controller.signal)
        if (!controller.signal.aborted) setPreview(next)
      } catch (failure) {
        if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : 'Не удалось рассчитать выплаты.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [accessToken, open, canPay, accountingMonth, operationDate, client, revision])

  async function pay(comment: string, required: boolean, confirmed: boolean) {
    if (busy.current || !canPay || loading || result) return null
    if (!attempt.current) {
      if (!preview?.canSubmit) return null
      const normalized = comment.trim()
      if ((required && !normalized) || (normalized.length > 0 && normalized.length < 3) || normalized.length > 1000) {
        setError('Введите комментарий от 3 до 1000 символов.')
        return null
      }
      if (preview.requiresNegativeFundConfirmation && !confirmed) {
        setError('Подтвердите выплату сверх остатка фонда.')
        return null
      }
      attempt.current = {
        requestId: crypto.randomUUID(), accountingMonth: preview.accountingMonth,
        operationDate: preview.operationDate, fingerprint: preview.fingerprint,
        confirmNegativeFundBalance: confirmed, comment: normalized || null,
      }
    }
    busy.current = true
    setSaving(true)
    setError(null)
    try {
      const paid = await client.pay(accessToken, attempt.current)
      attempt.current = null
      if (mounted.current) {
        setResult(paid)
        setUncertain(false)
      }
      return paid
    } catch (failure) {
      // Only an explicit business/authorization rejection proves that this attempt failed.
      // Server errors, broken connections and timeouts can hide a committed transaction.
      const rejected = failure instanceof FinanceApiError && [400, 401, 403, 404, 405, 409, 422].includes(failure.status)
      if (rejected) attempt.current = null
      if (mounted.current) {
        setUncertain(!rejected)
        if (rejected) setPreview(null)
        setError(failure instanceof Error ? failure.message : 'Не удалось получить результат выплаты.')
      }
      return null
    } finally {
      busy.current = false
      if (mounted.current) setSaving(false)
    }
  }

  return { preview, loading, saving, error, result, uncertain, pay, reload: () => setRevision((value) => value + 1) }
}
