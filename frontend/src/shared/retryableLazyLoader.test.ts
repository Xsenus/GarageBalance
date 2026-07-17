// @vitest-environment node
import { describe, expect, it, vi } from 'vitest'
import { createRetryableLazyLoader } from './retryableLazyLoader'

describe('createRetryableLazyLoader', () => {
  it('shares one in-flight request and keeps the successful result cached', async () => {
    let resolveRequest!: (value: string) => void
    const loader = vi.fn(() => new Promise<string>((resolve) => { resolveRequest = resolve }))
    const load = createRetryableLazyLoader(loader)

    const first = load()
    const second = load()

    expect(second).toBe(first)
    expect(loader).toHaveBeenCalledTimes(1)
    resolveRequest('loaded')
    await expect(first).resolves.toBe('loaded')
    await expect(load()).resolves.toBe('loaded')
    expect(loader).toHaveBeenCalledTimes(1)
  })

  it('clears a rejected request so the next attempt can recover', async () => {
    const loader = vi.fn<() => Promise<string>>()
      .mockRejectedValueOnce(new Error('temporary network error'))
      .mockResolvedValueOnce('recovered')
    const load = createRetryableLazyLoader(loader)

    await expect(load()).rejects.toThrow('temporary network error')
    await expect(load()).resolves.toBe('recovered')
    expect(loader).toHaveBeenCalledTimes(2)
  })

  it('allows retry when a loader throws before returning a promise', async () => {
    const loader = vi.fn<() => Promise<string>>()
      .mockImplementationOnce(() => { throw new Error('synchronous loader error') })
      .mockResolvedValueOnce('recovered')
    const load = createRetryableLazyLoader(loader)

    await expect(load()).rejects.toThrow('synchronous loader error')
    await expect(load()).resolves.toBe('recovered')
    expect(loader).toHaveBeenCalledTimes(2)
  })
})
