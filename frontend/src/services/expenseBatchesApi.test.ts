// @vitest-environment node
import { afterEach, expect, it, vi } from 'vitest'
import { expenseBatchesApi } from './expenseBatchesApi'
import { clearFundsResponseCache, fundsApi } from './fundsApi'

const dates = { accountingMonth: '2026-08-01', operationDate: '2026-08-30' }
const payment = { ...dates, requestId: 'request-1', fingerprint: 'a'.repeat(64), confirmNegativeFundBalance: false, comment: 'Проверка' }
afterEach(() => { vi.unstubAllGlobals(); clearFundsResponseCache() })

it('posts preview and payment with the same caller-supplied idempotency data', async () => {
  const fetch = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({ requestId: payment.requestId, operationIds: ['operation-1'] }))))
  vi.stubGlobal('fetch', fetch)
  await expenseBatchesApi.preview('token', dates)
  await expenseBatchesApi.pay('token', payment)
  await expenseBatchesApi.pay('token', payment)
  expect(fetch.mock.calls.map(([url]) => url)).toEqual(['/api/finance/expense-batches/preview', '/api/finance/expense-batches', '/api/finance/expense-batches'])
  expect(fetch.mock.calls[0][1].body).toBe(JSON.stringify(dates))
  expect(fetch.mock.calls[1][1].body).toBe(JSON.stringify(payment))
  expect(fetch.mock.calls[2][1].body).toBe(JSON.stringify(payment))
  expect(fetch.mock.calls[1][1].headers.Authorization).toBe('Bearer token')
})

it.each([true, false])('invalidates only this user’s funds after success: %s', async (success) => {
  const fetch = vi.fn().mockImplementation((url: string) => Promise.resolve(url === '/api/finance/expense-batches'
    ? new Response(JSON.stringify(success ? { requestId: 'request-1', operationIds: [] } : { code: 'expense_batch_preview_changed', detail: 'Обновите расчёт' }), { status: success ? 200 : 409 })
    : new Response('[]')))
  vi.stubGlobal('fetch', fetch)
  await fundsApi.getFunds('one')
  await fundsApi.getFunds('two')
  if (success) await expenseBatchesApi.pay('one', payment)
  else await expect(expenseBatchesApi.pay('one', payment)).rejects.toMatchObject({ code: 'expense_batch_preview_changed', status: 409 })
  await fundsApi.getFunds('one')
  await fundsApi.getFunds('two')
  expect(fetch).toHaveBeenCalledTimes(success ? 4 : 3)
})

it('preserves cancellation and does not retry a failed payment automatically', async () => {
  const fetch = vi.fn().mockRejectedValue(new Error('Connection lost'))
  vi.stubGlobal('fetch', fetch)
  await expect(expenseBatchesApi.pay('token', payment)).rejects.toThrow('Connection lost')
  expect(fetch).toHaveBeenCalledTimes(1)
  const controller = new AbortController()
  controller.abort()
  await expect(expenseBatchesApi.preview('token', dates, controller.signal)).rejects.toMatchObject({ name: 'AbortError' })
  expect(fetch).toHaveBeenCalledTimes(1)
})

it('uses the shared financial error for an unreadable server response', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('unavailable', { status: 503 })))
  await expect(expenseBatchesApi.preview('token', dates)).rejects.toMatchObject({ code: 'finance_request_failed', status: 503 })
})
