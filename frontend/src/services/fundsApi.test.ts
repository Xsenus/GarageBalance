// @vitest-environment node
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearFundsResponseCache, fundsApi } from './fundsApi'

const emptyFundsResponse = () => new Response(JSON.stringify([]), {
  status: 200,
  headers: { 'Content-Type': 'application/json' },
})

describe('fundsApi response cache', () => {
  beforeEach(() => {
    clearFundsResponseCache()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('deduplicates concurrent and repeated fund reads in one authenticated session', async () => {
    const fetchMock = vi.fn(async () => emptyFundsResponse())
    vi.stubGlobal('fetch', fetchMock)

    await Promise.all([
      fundsApi.getFunds('token'),
      fundsApi.getFunds('token'),
    ])
    await fundsApi.getFunds('token')

    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('does not share fund responses between authenticated sessions', async () => {
    const fetchMock = vi.fn(async () => emptyFundsResponse())
    vi.stubGlobal('fetch', fetchMock)

    await fundsApi.getFunds('first-token')
    await fundsApi.getFunds('second-token')

    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('keeps abortable fund reads isolated from the shared cache', async () => {
    const fetchMock = vi.fn(async () => emptyFundsResponse())
    vi.stubGlobal('fetch', fetchMock)
    const firstCancellation = new AbortController()
    const secondCancellation = new AbortController()

    await Promise.all([
      fundsApi.getFunds('token', firstCancellation.signal),
      fundsApi.getFunds('token', secondCancellation.signal),
    ])

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(fetchMock.mock.calls[0][1]?.signal).toBeInstanceOf(AbortSignal)
    expect(fetchMock.mock.calls[1][1]?.signal).toBeInstanceOf(AbortSignal)
    expect(fetchMock.mock.calls[0][1]?.signal).not.toBe(fetchMock.mock.calls[1][1]?.signal)
  })

  it('removes a failed fund response so the next read can retry', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'Временная ошибка' }), { status: 500 }))
      .mockResolvedValueOnce(emptyFundsResponse())
    vi.stubGlobal('fetch', fetchMock)

    await expect(fundsApi.getFunds('token')).rejects.toThrow('Временная ошибка')
    await expect(fundsApi.getFunds('token')).resolves.toEqual([])

    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it.each([
    ['createFund', () => fundsApi.createFund('token', { name: 'Резервный' })],
    ['updateFund', () => fundsApi.updateFund('token', 'fund-1', { name: 'Резервный' })],
    ['deleteFund', () => fundsApi.deleteFund('token', 'fund-1', { reason: 'Дубликат' })],
    ['createOperation', () => fundsApi.createOperation('token', 'fund-1', { operationKind: 'deposit', amount: 10, reason: 'Пополнение' })],
    ['updateOperation', () => fundsApi.updateOperation('token', 'operation-1', { amount: 12, reason: 'Уточнение' })],
    ['cancelOperation', () => fundsApi.cancelOperation('token', 'operation-1', { reason: 'Ошибка' })],
    ['restoreOperation', () => fundsApi.restoreOperation('token', 'operation-1')],
  ])('invalidates cached funds after successful %s', async (_name, mutate) => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(emptyFundsResponse())
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 'result-1' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }))
      .mockResolvedValueOnce(emptyFundsResponse())
    vi.stubGlobal('fetch', fetchMock)

    await fundsApi.getFunds('token')
    await mutate()
    await fundsApi.getFunds('token')

    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('keeps cached funds when a mutation fails', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(emptyFundsResponse())
      .mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'Не сохранено' }), { status: 500 }))
    vi.stubGlobal('fetch', fetchMock)

    await fundsApi.getFunds('token')
    await expect(fundsApi.updateFund('token', 'fund-1', { name: 'Резервный' })).rejects.toThrow('Не сохранено')
    await fundsApi.getFunds('token')

    expect(fetchMock).toHaveBeenCalledTimes(2)
  })
})

describe('fundsApi configuration options', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('loads the non-financial fund catalog from the dedicated endpoint', async () => {
    const options = [{ id: 'fund-water', name: 'Водоснабжение', allowOperations: true }]
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(options), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(fundsApi.getFundOptions('token')).resolves.toEqual(options)

    expect(fetchMock).toHaveBeenCalledWith('/api/funds/options', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
  })

  it('loads the fund reconciliation from the dedicated endpoint', async () => {
    const reconciliation = {
      cashAndBankTotal: 1000,
      namedFundTotal: 700,
      availableToDistribute: 250,
      difference: 50,
      isReconciled: false,
    }
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(reconciliation), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(fundsApi.getReconciliation?.('token')).resolves.toEqual(reconciliation)

    expect(fetchMock).toHaveBeenCalledWith('/api/funds/reconciliation', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
  })

  it('returns the server error for a retryable options load', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ detail: 'Каталог фондов временно недоступен.' }), { status: 503 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(fundsApi.getFundOptions('token')).rejects.toThrow('Каталог фондов временно недоступен.')
  })

  it('forwards caller cancellation to the options request', async () => {
    const controller = new AbortController()
    let receivedSignal: AbortSignal | null = null
    const fetchMock = vi.fn((_input: RequestInfo | URL, init?: RequestInit) => new Promise<Response>((_resolve, reject) => {
      receivedSignal = init?.signal ?? null
      receivedSignal?.addEventListener('abort', () => reject(receivedSignal?.reason), { once: true })
    }))
    vi.stubGlobal('fetch', fetchMock)

    const request = fundsApi.getFundOptions('token', controller.signal)
    await vi.waitFor(() => expect(receivedSignal).toBeInstanceOf(AbortSignal))
    controller.abort()

    await expect(request).rejects.toMatchObject({ name: 'AbortError' })
    expect(receivedSignal?.aborted).toBe(true)
  })
})
