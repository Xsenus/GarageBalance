import { vi } from 'vitest'
import { scheduleDebouncedRequest, scheduleDelayedAction } from './debouncedRequest'

describe('scheduleDelayedAction', () => {
  it('runs an action after the default delay', async () => {
    vi.useFakeTimers()
    const action = vi.fn()
    scheduleDelayedAction(action)

    await vi.advanceTimersByTimeAsync(349)
    expect(action).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    expect(action).toHaveBeenCalledOnce()
    vi.useRealTimers()
  })

  it('cancels a pending action', async () => {
    vi.useFakeTimers()
    const action = vi.fn()
    const cancel = scheduleDelayedAction(action, 100)

    cancel()
    await vi.runAllTimersAsync()
    expect(action).not.toHaveBeenCalled()
    vi.useRealTimers()
  })
})

describe('scheduleDebouncedRequest', () => {
  it('starts after the delay and delivers the successful result', async () => {
    vi.useFakeTimers()
    const onStart = vi.fn()
    const onSuccess = vi.fn()
    const request = vi.fn(async () => ['result'])

    scheduleDebouncedRequest({ request, onStart, onSuccess, onError: vi.fn() })
    await vi.advanceTimersByTimeAsync(349)
    expect(request).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(1)
    expect(onStart).toHaveBeenCalledOnce()
    expect(onSuccess).toHaveBeenCalledWith(['result'])
    vi.useRealTimers()
  })

  it('cancels a request before the delay expires', async () => {
    vi.useFakeTimers()
    const request = vi.fn(async () => 'result')
    const cancel = scheduleDebouncedRequest({ request, onStart: vi.fn(), onSuccess: vi.fn(), onError: vi.fn() })

    cancel()
    await vi.runAllTimersAsync()

    expect(request).not.toHaveBeenCalled()
    vi.useRealTimers()
  })

  it('ignores a result from a request cancelled after it started', async () => {
    vi.useFakeTimers()
    let resolveRequest: (value: string) => void = () => undefined
    const request = vi.fn(() => new Promise<string>((resolve) => { resolveRequest = resolve }))
    const onSuccess = vi.fn()
    const cancel = scheduleDebouncedRequest({ delay: 0, request, onStart: vi.fn(), onSuccess, onError: vi.fn() })
    await vi.runAllTimersAsync()

    cancel()
    resolveRequest('stale')
    await Promise.resolve()

    expect(onSuccess).not.toHaveBeenCalled()
    vi.useRealTimers()
  })

  it('reports an active request failure and ignores a cancelled one', async () => {
    vi.useFakeTimers()
    const activeError = new Error('active')
    const onError = vi.fn()
    scheduleDebouncedRequest({ delay: 0, request: async () => { throw activeError }, onStart: vi.fn(), onSuccess: vi.fn(), onError })
    await vi.runAllTimersAsync()
    expect(onError).toHaveBeenCalledWith(activeError)

    let rejectRequest: (error: Error) => void = () => undefined
    const cancel = scheduleDebouncedRequest({ delay: 0, request: () => new Promise((_, reject) => { rejectRequest = reject }), onStart: vi.fn(), onSuccess: vi.fn(), onError })
    await vi.runAllTimersAsync()
    cancel()
    rejectRequest(new Error('cancelled'))
    await Promise.resolve()
    expect(onError).toHaveBeenCalledTimes(1)
    vi.useRealTimers()
  })

  it('aborts a stalled request at its hard timeout and ignores its late result', async () => {
    vi.useFakeTimers()
    let resolveRequest: (value: string) => void = () => undefined
    let requestSignal: AbortSignal | undefined
    const timeoutError = new Error('timed out')
    const onSuccess = vi.fn()
    const onError = vi.fn()
    scheduleDebouncedRequest({
      delay: 0,
      requestTimeout: 1_000,
      timeoutError,
      request: (signal) => {
        requestSignal = signal
        return new Promise((resolve) => { resolveRequest = resolve })
      },
      onStart: vi.fn(),
      onSuccess,
      onError,
    })

    await vi.advanceTimersByTimeAsync(999)
    expect(onError).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    expect(requestSignal?.aborted).toBe(true)
    expect(onError).toHaveBeenCalledWith(timeoutError)

    resolveRequest('late')
    await Promise.resolve()
    expect(onSuccess).not.toHaveBeenCalled()
    vi.useRealTimers()
  })

  it('clears the hard timeout after success or cancellation', async () => {
    vi.useFakeTimers()
    const successfulError = vi.fn()
    scheduleDebouncedRequest({
      delay: 0,
      requestTimeout: 1_000,
      timeoutError: new Error('success timeout'),
      request: async () => 'result',
      onStart: vi.fn(),
      onSuccess: vi.fn(),
      onError: successfulError,
    })
    await vi.advanceTimersByTimeAsync(1_000)
    expect(successfulError).not.toHaveBeenCalled()

    const cancelledError = vi.fn()
    const cancel = scheduleDebouncedRequest({
      delay: 0,
      requestTimeout: 1_000,
      timeoutError: new Error('cancelled timeout'),
      request: () => new Promise(() => undefined),
      onStart: vi.fn(),
      onSuccess: vi.fn(),
      onError: cancelledError,
    })
    await vi.advanceTimersByTimeAsync(0)
    cancel()
    await vi.advanceTimersByTimeAsync(1_000)
    expect(cancelledError).not.toHaveBeenCalled()
    vi.useRealTimers()
  })

  it('reports a synchronous request failure', async () => {
    vi.useFakeTimers()
    const requestError = new Error('synchronous')
    const onError = vi.fn()
    scheduleDebouncedRequest({
      delay: 0,
      request: () => { throw requestError },
      onStart: vi.fn(),
      onSuccess: vi.fn(),
      onError,
    })

    await vi.runAllTimersAsync()
    expect(onError).toHaveBeenCalledWith(requestError)
    vi.useRealTimers()
  })
})
