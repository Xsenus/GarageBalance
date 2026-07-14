import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearDictionaryResponseCache, dictionariesApi } from './dictionariesApi'

describe('dictionariesApi response cache', () => {
  beforeEach(() => {
    clearDictionaryResponseCache()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('deduplicates concurrent and repeated dictionary reads', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify([]), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await Promise.all([
      dictionariesApi.getGarages('token', undefined, 100),
      dictionariesApi.getGarages('token', undefined, 100),
    ])
    await dictionariesApi.getGarages('token', undefined, 100)

    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('does not share responses between authenticated sessions', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify([]), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await dictionariesApi.getGarages('first-token', undefined, 100)
    await dictionariesApi.getGarages('second-token', undefined, 100)

    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('invalidates cached reads after a dictionary mutation', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 'owner-1' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await dictionariesApi.getGarages('token', undefined, 100)
    await dictionariesApi.createOwner('token', { lastName: 'Иванов', firstName: 'Иван' })
    await dictionariesApi.getGarages('token', undefined, 100)

    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('removes a failed response so the next read can retry', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'Ошибка' }), { status: 500 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(dictionariesApi.getGarages('token', undefined, 100)).rejects.toThrow('Ошибка')
    await expect(dictionariesApi.getGarages('token', undefined, 100)).resolves.toEqual([])

    expect(fetchMock).toHaveBeenCalledTimes(2)
  })
})
