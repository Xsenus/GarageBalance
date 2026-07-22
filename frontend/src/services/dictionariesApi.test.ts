// @vitest-environment node
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

  it('creates a regular service and its tariff through one request', async () => {
    const response = { service: { id: 'service-1', tariffId: 'tariff-1' }, tariff: { id: 'tariff-1', rate: 1750 } }
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(response), {
      status: 201,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)
    const request = {
      service: {
        name: 'Охрана',
        isRegular: true,
        periodicityMonths: 1,
        accrualStartMonth: 1,
        paymentDueDay: 20,
        paymentDueMonth: null,
        overdueGraceDays: 15,
        incomeTypeId: 'income-security',
        tariffId: 'tariff-template',
        isMetered: false,
        hasTieredTariff: false,
        unitName: 'руб.',
      },
      rate: 1750,
      effectiveFrom: '2026-07-23',
    }

    await expect(dictionariesApi.createChargeServiceWithTariff('token', request)).resolves.toEqual(response)

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/dictionaries/charge-services/with-tariff',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify(request),
        headers: expect.objectContaining({ Authorization: 'Bearer token' }),
      }),
    )
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

  it('passes the overdue debtor mode to the garage page endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      items: [],
      totalCount: 0,
      offset: 25,
      limit: 25,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await dictionariesApi.getGaragesPage('token', undefined, 25, 25, true, 'overdueDebt', 'desc', true)

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/dictionaries/garages/page?offset=25&limit=25&includeArchived=true&sortBy=overdueDebt&sortDirection=desc&debtorsOnly=true',
      expect.objectContaining({ headers: expect.objectContaining({ Authorization: 'Bearer token' }) }),
    )
  })

  it('passes garage green-column filters to the page endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ items: [], totalCount: 0, offset: 0, limit: 25 }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await dictionariesApi.getGaragesPage('token', undefined, 0, 25, true, 'number', 'asc', true, {
      number: 'А-', peopleCountMin: 2, peopleCountMax: 4, floorCountMin: 1, floorCountMax: 2,
    })

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/dictionaries/garages/page?offset=0&limit=25&includeArchived=true&sortBy=number&sortDirection=asc&debtorsOnly=true&number=%D0%90-&peopleCountMin=2&peopleCountMax=4&floorCountMin=1&floorCountMax=2',
      expect.objectContaining({ headers: expect.objectContaining({ Authorization: 'Bearer token' }) }),
    )
  })
})
