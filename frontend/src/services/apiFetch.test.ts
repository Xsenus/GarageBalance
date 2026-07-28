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
})
