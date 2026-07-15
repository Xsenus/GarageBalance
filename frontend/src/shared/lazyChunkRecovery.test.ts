import { describe, expect, it, vi } from 'vitest'
import { isLazyChunkLoadError, recoverFromLazyChunkError } from './lazyChunkRecovery'

function createStorage() {
  const values = new Map<string, string>()
  return {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => values.set(key, value),
  }
}

describe('lazyChunkRecovery', () => {
  it.each([
    Object.assign(new Error('Loading chunk 781 failed.'), { name: 'ChunkLoadError' }),
    new TypeError('Failed to fetch dynamically imported module: /assets/FinancePanel-old.js'),
    new TypeError('Importing a module script failed.'),
    new Error('Error loading dynamically imported module'),
  ])('recognizes a stale lazy module error', (error) => {
    expect(isLazyChunkLoadError(error)).toBe(true)
  })

  it('does not classify an ordinary render error as a lazy module failure', () => {
    expect(isLazyChunkLoadError(new Error('Cannot read properties of undefined'))).toBe(false)
  })

  it('reloads once and blocks a repeated automatic reload during the cooldown', () => {
    const storage = createStorage()
    const reload = vi.fn()
    const error = new TypeError('Failed to fetch dynamically imported module: /assets/FinancePanel-old.js')

    expect(recoverFromLazyChunkError(error, { storage, reload, now: () => 1_000_000 })).toBe(true)
    expect(recoverFromLazyChunkError(error, { storage, reload, now: () => 1_001_000 })).toBe(false)
    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('keeps the fallback visible when session storage is unavailable', () => {
    const reload = vi.fn()
    const storage = {
      getItem: () => { throw new Error('blocked') },
      setItem: () => undefined,
    }

    expect(recoverFromLazyChunkError(new Error('Loading chunk 1 failed.'), { storage, reload })).toBe(false)
    expect(reload).not.toHaveBeenCalled()
  })
})
