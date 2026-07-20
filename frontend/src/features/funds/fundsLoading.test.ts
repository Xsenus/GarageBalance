import { afterEach, describe, expect, it, vi } from 'vitest'

import { fundsLoadAttemptTimeoutMs, loadFundsRequest } from './fundsLoading'

describe('funds loading boundary', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('retries a request once after the first attempt times out', async () => {
    vi.useFakeTimers()
    const requestFactory = vi.fn()
      .mockImplementationOnce((signal: AbortSignal) => new Promise<string>(() => {
        expect(signal.aborted).toBe(false)
      }))
      .mockResolvedValueOnce('loaded')

    const result = loadFundsRequest(requestFactory, 'Фонды загружаются слишком долго.')
    await vi.advanceTimersByTimeAsync(fundsLoadAttemptTimeoutMs)

    await expect(result).resolves.toBe('loaded')
    expect(requestFactory).toHaveBeenCalledTimes(2)
    expect(requestFactory.mock.calls[0][0].aborted).toBe(true)
    expect(requestFactory.mock.calls[1][0].aborted).toBe(false)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('stops waiting after two timed-out attempts and returns a readable error', async () => {
    vi.useFakeTimers()
    const requestFactory = vi.fn(() => new Promise<string>(() => undefined))

    const result = loadFundsRequest(requestFactory, 'Фонды загружаются слишком долго.')
    const rejection = expect(result).rejects.toThrow('Фонды загружаются слишком долго.')
    await vi.advanceTimersByTimeAsync(fundsLoadAttemptTimeoutMs * 2)

    await rejection
    expect(requestFactory).toHaveBeenCalledTimes(2)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('does not retry an immediate API error', async () => {
    const requestFactory = vi.fn().mockRejectedValue(new Error('Доступ к фондам запрещен.'))

    await expect(loadFundsRequest(requestFactory, 'Фонды загружаются слишком долго.')).rejects.toThrow('Доступ к фондам запрещен.')
    expect(requestFactory).toHaveBeenCalledTimes(1)
  })

  it('aborts an obsolete load without waiting for a timeout or starting a retry', async () => {
    vi.useFakeTimers()
    const cancellationController = new AbortController()
    const requestFactory = vi.fn((signal: AbortSignal) => new Promise<string>(() => {
      expect(signal.aborted).toBe(false)
    }))

    const result = loadFundsRequest(requestFactory, 'Фонды загружаются слишком долго.', cancellationController.signal)
    const rejection = expect(result).rejects.toMatchObject({ name: 'AbortError' })
    await Promise.resolve()
    cancellationController.abort()

    await rejection
    expect(requestFactory).toHaveBeenCalledTimes(1)
    expect(requestFactory.mock.calls[0][0].aborted).toBe(true)
    expect(vi.getTimerCount()).toBe(0)
  })
})
