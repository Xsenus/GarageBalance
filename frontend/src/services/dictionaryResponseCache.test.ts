// @vitest-environment node
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  clearDictionaryResponseCache,
  getDictionaryCacheContext,
  invalidateDictionaryResponseCache,
  storeDictionaryResponse,
} from './dictionaryResponseCache'

describe('dictionaryResponseCache', () => {
  beforeEach(() => {
    vi.useRealTimers()
    clearDictionaryResponseCache()
  })

  it('stores a successful response for the matching session and path', async () => {
    const initial = getDictionaryCacheContext<unknown[]>('token-1', '/api/dictionaries/owners', true)
    const response = Promise.resolve([{ id: 'owner-1' }])

    storeDictionaryResponse('token-1', initial.cacheTag!, initial.cacheKey, response)

    await expect(getDictionaryCacheContext<unknown[]>('token-1', '/api/dictionaries/owners', true).cachedResponse).resolves.toEqual([{ id: 'owner-1' }])
    expect(getDictionaryCacheContext('token-2', '/api/dictionaries/owners', true).cachedResponse).toBeNull()
  })

  it('does not read cache when a request explicitly bypasses it', () => {
    const initial = getDictionaryCacheContext('token', '/api/dictionaries/owners', true)
    storeDictionaryResponse('token', initial.cacheTag!, initial.cacheKey, Promise.resolve([]))

    expect(getDictionaryCacheContext('token', '/api/dictionaries/owners', false).cachedResponse).toBeNull()
  })

  it('expires entries and removes rejected responses', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-27T00:00:00Z'))
    const expiring = getDictionaryCacheContext('token', '/api/dictionaries/owners', true)
    storeDictionaryResponse('token', expiring.cacheTag!, expiring.cacheKey, Promise.resolve([]))
    vi.advanceTimersByTime(60_001)
    expect(getDictionaryCacheContext('token', '/api/dictionaries/owners', true).cachedResponse).toBeNull()

    const rejected = getDictionaryCacheContext('token', '/api/dictionaries/suppliers', true)
    const failure = Promise.reject(new Error('network'))
    storeDictionaryResponse('token', rejected.cacheTag!, rejected.cacheKey, failure)
    await failure.catch(() => undefined)
    expect(getDictionaryCacheContext('token', '/api/dictionaries/suppliers', true).cachedResponse).toBeNull()
  })

  it('invalidates dependent dictionaries without clearing unrelated data', async () => {
    for (const path of ['owners', 'garages', 'suppliers']) {
      const context = getDictionaryCacheContext('token', `/api/dictionaries/${path}`, true)
      storeDictionaryResponse('token', context.cacheTag!, context.cacheKey, Promise.resolve([path]))
    }

    invalidateDictionaryResponseCache('token', 'owners')

    expect(getDictionaryCacheContext('token', '/api/dictionaries/owners', true).cachedResponse).toBeNull()
    expect(getDictionaryCacheContext('token', '/api/dictionaries/garages', true).cachedResponse).toBeNull()
    await expect(getDictionaryCacheContext<string[]>('token', '/api/dictionaries/suppliers', true).cachedResponse).resolves.toEqual(['suppliers'])
  })

  it('clears all sessions and ignores non-dictionary paths', () => {
    const context = getDictionaryCacheContext('token', '/api/dictionaries/owners', true)
    storeDictionaryResponse('token', context.cacheTag!, context.cacheKey, Promise.resolve([]))
    clearDictionaryResponseCache()

    expect(getDictionaryCacheContext('token', '/api/dictionaries/owners', true).cachedResponse).toBeNull()
    expect(getDictionaryCacheContext('token', '/api/finance/operations', true).cacheTag).toBeNull()
  })
})
