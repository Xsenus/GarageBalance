import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { ExpenseBatchPreview } from '../../services/expenseBatchesApi'
import { FinanceApiError } from '../../services/financeApi'
import { useExpenseBatchPayment } from './useExpenseBatchPayment'

const preview: ExpenseBatchPreview = {
  accountingMonth: '2026-09-01', operationDate: '2026-09-06', items: [],
  bankAmount: 20, cashAmount: 0, availableBankAmount: 50, availableCashAmount: 0,
  funds: [], issues: [], requiresNegativeFundConfirmation: false, fingerprint: 'a'.repeat(64), canSubmit: true,
}
const props = { accessToken: 'test', open: true, accountingMonth: preview.accountingMonth, operationDate: preview.operationDate, canPay: true }
function client() {
  return { preview: vi.fn().mockResolvedValue(preview), pay: vi.fn().mockResolvedValue({ requestId: 'paid', operationIds: ['operation'] }) }
}

describe('useExpenseBatchPayment', () => {
  it('discards stale preview responses and aborts closed or unmounted loads', async () => {
    const api = client()
    let first!: (value: ExpenseBatchPreview) => void
    api.preview.mockImplementationOnce(() => new Promise((resolve) => { first = resolve }))
    const { result, rerender, unmount } = renderHook((options) => useExpenseBatchPayment({ ...options, client: api }), { initialProps: props })
    expect(result.current.loading).toBe(true)
    rerender({ ...props, operationDate: '2026-09-07' })
    await waitFor(() => expect(result.current.preview).toBe(preview))
    expect(api.preview.mock.calls[0][2].aborted).toBe(true)
    await act(async () => first({ ...preview, fingerprint: 'stale' }))
    expect(result.current.preview?.fingerprint).toBe(preview.fingerprint)
    rerender({ ...props, open: false })
    expect(api.preview.mock.calls[1][2].aborted).toBe(true)
    unmount()
  })

  it('validates comment and fund confirmation before creating one request, then prevents duplicate submits', async () => {
    const api = client()
    api.preview.mockResolvedValue({ ...preview, requiresNegativeFundConfirmation: true })
    let finish!: (value: { requestId: string; operationIds: string[] }) => void
    api.pay.mockImplementation(() => new Promise((resolve) => { finish = resolve }))
    const { result } = renderHook(() => useExpenseBatchPayment({ ...props, client: api }))
    await waitFor(() => expect(result.current.preview).not.toBeNull())
    for (const comment of ['', 'ab', 'x'.repeat(1001)]) {
      await act(async () => { await result.current.pay(comment, true, true) })
      expect(result.current.error).toMatch('комментарий')
    }
    await act(async () => { await result.current.pay('Оплата', true, false) })
    expect(result.current.error).toMatch('Подтвердите')
    expect(api.pay).not.toHaveBeenCalled()
    let payment!: ReturnType<typeof result.current.pay>
    act(() => { payment = result.current.pay(' Оплата ', true, true) })
    await act(async () => { await result.current.pay('Другой', true, true) })
    expect(api.pay).toHaveBeenCalledTimes(1)
    expect(api.pay.mock.calls[0][1]).toMatchObject({ ...propsToRequest(), comment: 'Оплата', confirmNegativeFundBalance: true })
    await act(async () => { finish({ requestId: 'paid', operationIds: ['operation'] }); await payment })
    expect(result.current.result?.operationIds).toEqual(['operation'])
    await act(async () => { await result.current.pay('Оплата', true, true) })
    expect(api.pay).toHaveBeenCalledTimes(1)
  })

  it.each([new TypeError('Связь потеряна'), new FinanceApiError('server_error', 'Нет ответа', 503), new FinanceApiError('timeout', 'Время ожидания истекло', 408)])('retains an uncertain request through close/reopen and retries its identical payload: %s', async (failure) => {
    const api = client()
    api.pay.mockRejectedValueOnce(failure)
    const { result, rerender } = renderHook((options) => useExpenseBatchPayment({ ...options, client: api }), { initialProps: props })
    await waitFor(() => expect(result.current.preview).not.toBeNull())
    await act(async () => { await result.current.pay('', false, false) })
    expect(result.current.uncertain).toBe(true)
    const request = api.pay.mock.calls[0][1]
    expect(request.comment).toBeNull()
    rerender({ ...props, open: false })
    rerender({ ...props, accountingMonth: '2026-10-01' })
    expect(api.preview).toHaveBeenCalledTimes(1)
    expect(api.pay).toHaveBeenCalledTimes(1)
    await act(async () => { await result.current.pay('Changed input', true, true) })
    expect(api.pay.mock.calls[1][1]).toBe(request)
    expect(result.current.uncertain).toBe(false)
  })

  it('requires a new preview after an explicit conflict and permits retrying a failed preview', async () => {
    const api = client()
    api.preview.mockRejectedValueOnce(new Error('Расчёт недоступен'))
    api.pay.mockRejectedValueOnce(new FinanceApiError('expense_batch_preview_changed', 'Остатки изменились', 409))
    const { result } = renderHook(() => useExpenseBatchPayment({ ...props, client: api }))
    await waitFor(() => expect(result.current.error).toBe('Расчёт недоступен'))
    act(() => result.current.reload())
    await waitFor(() => expect(result.current.preview).not.toBeNull())
    await act(async () => { await result.current.pay('Оплата', true, false) })
    expect(result.current.preview).toBeNull()
    expect(result.current.uncertain).toBe(false)
    await act(async () => { await result.current.pay('Оплата', true, false) })
    expect(api.pay).toHaveBeenCalledTimes(1)
    act(() => result.current.reload())
    await waitFor(() => expect(result.current.preview).not.toBeNull())
    await act(async () => { await result.current.pay('Оплата', true, false) })
    expect(api.pay.mock.calls[1][1].requestId).not.toBe(api.pay.mock.calls[0][1].requestId)
  })

  it('does not load without permission or submit an empty or blocked plan', async () => {
    const api = client()
    api.preview.mockResolvedValue({ ...preview, canSubmit: false })
    const { result, rerender } = renderHook((options) => useExpenseBatchPayment({ ...options, client: api }), { initialProps: { ...props, canPay: false } })
    await act(async () => { await result.current.pay('Оплата', true, false) })
    expect(api.preview).not.toHaveBeenCalled()
    rerender(props)
    await waitFor(() => expect(result.current.preview).not.toBeNull())
    await act(async () => { await result.current.pay('Оплата', true, false) })
    expect(api.pay).not.toHaveBeenCalled()
  })

  it('does not submit while loading and settles an in-flight payment after unmount without a second call', async () => {
    const api = client()
    let load!: (value: ExpenseBatchPreview) => void
    let complete!: (value: { requestId: string; operationIds: string[] }) => void
    api.preview.mockImplementationOnce(() => new Promise((resolve) => { load = resolve }))
    api.pay.mockImplementationOnce(() => new Promise((resolve) => { complete = resolve }))
    const { result, unmount } = renderHook(() => useExpenseBatchPayment({ ...props, client: api }))
    await act(async () => { await result.current.pay('Оплата', true, false) })
    expect(api.pay).not.toHaveBeenCalled()
    await act(async () => load(preview))
    let payment!: ReturnType<typeof result.current.pay>
    act(() => { payment = result.current.pay('Оплата', true, false) })
    unmount()
    const paid = { requestId: 'paid', operationIds: ['operation'] }
    await act(async () => { complete(paid); expect(await payment).toEqual(paid) })
    expect(api.pay).toHaveBeenCalledOnce()
  })

  it('uses readable fallback errors for non-Error failures and keeps an uncertain payment', async () => {
    const api = client()
    api.preview.mockRejectedValueOnce(null)
    api.pay.mockRejectedValueOnce(null)
    const { result } = renderHook(() => useExpenseBatchPayment({ ...props, client: api }))
    await waitFor(() => expect(result.current.error).toBe('Не удалось рассчитать выплаты.'))
    act(() => result.current.reload())
    await waitFor(() => expect(result.current.preview).not.toBeNull())
    await act(async () => { await result.current.pay('Оплата', true, false) })
    expect(result.current.error).toBe('Не удалось получить результат выплаты.')
    expect(result.current.uncertain).toBe(true)
  })
})

function propsToRequest() {
  return { accountingMonth: props.accountingMonth, operationDate: props.operationDate, fingerprint: preview.fingerprint }
}
