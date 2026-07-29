import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiRequestTimeoutError, apiFetch } from './apiFetch'

describe('apiFetch', () => {
  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('returns a successful response without retrying', async () => {
    const response = new Response('{}', { status: 200 })
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(response)

    await expect(apiFetch('/api/health')).resolves.toBe(response)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0][1]?.signal).toBeInstanceOf(AbortSignal)
  })

  it('retries one transient GET network failure', async () => {
    const response = new Response('{}', { status: 200 })
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockRejectedValueOnce(new TypeError('network unavailable'))
      .mockResolvedValueOnce(response)

    await expect(apiFetch('/api/finance/summary')).resolves.toBe(response)
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('coalesces concurrent identical safe reads and gives each caller its own response body', async () => {
    let resolveFetch!: (response: Response) => void
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(() => new Promise<Response>((resolve) => {
      resolveFetch = resolve
    }))

    const firstRequest = apiFetch('/api/finance/summary', {
      headers: { Authorization: 'Bearer session-token' },
    })
    const secondRequest = apiFetch('/api/finance/summary', {
      headers: { Authorization: 'Bearer session-token' },
    })
    resolveFetch(new Response('{"total":42}', { status: 200 }))

    const [firstResponse, secondResponse] = await Promise.all([firstRequest, secondRequest])

    expect(fetchMock).toHaveBeenCalledTimes(1)
    await expect(firstResponse.json()).resolves.toEqual({ total: 42 })
    await expect(secondResponse.json()).resolves.toEqual({ total: 42 })
  })

  it('does not coalesce reads from different authenticated sessions', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response('{"user":1}', { status: 200 }))
      .mockResolvedValueOnce(new Response('{"user":2}', { status: 200 }))

    await Promise.all([
      apiFetch('/api/auth/me', { headers: { Authorization: 'Bearer first-session' } }),
      apiFetch('/api/auth/me', { headers: { Authorization: 'Bearer second-session' } }),
    ])

    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('removes a failed safe read from the in-flight registry', async () => {
    const response = new Response('{}', { status: 200 })
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockRejectedValueOnce(new TypeError('first attempt failed'))
      .mockRejectedValueOnce(new TypeError('retry failed'))
      .mockResolvedValueOnce(response)

    await expect(apiFetch('/api/finance/summary')).rejects.toThrow('retry failed')
    await expect(apiFetch('/api/finance/summary')).resolves.toBe(response)

    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('aborts timed-out GET attempts and reports a clear error after the retry', async () => {
    vi.useFakeTimers()
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((_input, init) => new Promise((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')), { once: true })
    }))

    const request = apiFetch('/api/dictionaries/garages')
    const assertion = expect(request).rejects.toBeInstanceOf(ApiRequestTimeoutError)
    await vi.advanceTimersByTimeAsync(15_000)
    await vi.advanceTimersByTimeAsync(15_000)

    await assertion
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('does not retry write requests because the server may have accepted them', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('connection closed'))

    await expect(apiFetch('/api/finance/income', { method: 'POST' })).rejects.toThrow('connection closed')

    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('uses the method from a Request object and never retries its write', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('connection closed'))
    const request = new Request('https://garagebalance.test/api/finance/income', { method: 'POST' })

    await expect(apiFetch(request)).rejects.toThrow('connection closed')

    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('aborts a timed-out write without retrying it', async () => {
    vi.useFakeTimers()
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((_input, init) => new Promise((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')), { once: true })
    }))

    const request = apiFetch('/api/finance/income', { method: 'POST' })
    const assertion = expect(request).rejects.toBeInstanceOf(ApiRequestTimeoutError)
    await vi.advanceTimersByTimeAsync(60_000)

    await assertion
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('propagates caller cancellation without retrying', async () => {
    const controller = new AbortController()
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((_input, init) => new Promise((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')), { once: true })
    }))

    const request = apiFetch('/api/funds', { signal: controller.signal })
    controller.abort()

    await expect(request).rejects.toMatchObject({ name: 'AbortError' })
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('does not coalesce reads with caller-owned cancellation', async () => {
    const firstController = new AbortController()
    const secondController = new AbortController()
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((_input, init) => new Promise<Response>((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')), { once: true })
    }))

    const firstRequest = apiFetch('/api/reports/income', { signal: firstController.signal })
    const secondRequest = apiFetch('/api/reports/income', { signal: secondController.signal })

    expect(fetchMock).toHaveBeenCalledTimes(2)
    firstController.abort()
    secondController.abort()
    await Promise.allSettled([firstRequest, secondRequest])
  })
})
