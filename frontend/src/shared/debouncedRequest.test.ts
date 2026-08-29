import { vi } from 'vitest'
import { scheduleDebouncedRequest } from './debouncedRequest'

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
})
