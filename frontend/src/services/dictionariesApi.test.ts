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

  it('deduplicates the complete tariff reference bundle across repeated section loads', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify([]), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)
    const loadTariffReferences = () => Promise.all([
      dictionariesApi.getTariffs('token'),
      dictionariesApi.getChargeServiceSettings('token'),
      dictionariesApi.getIncomeTypes('token'),
      dictionariesApi.getExpenseTypes('token'),
      dictionariesApi.getMeasurementUnitsPage('token'),
      dictionariesApi.getIrregularPayments('token'),
      dictionariesApi.getFeeCampaigns('token'),
    ])

    await Promise.all([loadTariffReferences(), loadTariffReferences()])
    await loadTariffReferences()

    expect(fetchMock).toHaveBeenCalledTimes(7)
    expect(fetchMock.mock.calls.map(([path]) => path)).toEqual(expect.arrayContaining([
      '/api/dictionaries/tariffs?limit=100',
      '/api/dictionaries/charge-services?limit=100',
      '/api/dictionaries/income-types?limit=100',
      '/api/dictionaries/expense-types?limit=100',
      '/api/dictionaries/measurement-units/page?offset=0&limit=100',
      '/api/dictionaries/irregular-payments?limit=100',
      '/api/dictionaries/fee-campaigns?limit=100',
    ]))
  })

  it('loads and mutates the measurement-unit dictionary through its dedicated endpoints', async () => {
    const unit = { id: 'unit-1', name: 'комплект', isArchived: false }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ items: [unit], totalCount: 1, offset: 0, limit: 100 }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(unit), { status: 201 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...unit, name: 'упаковка' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(unit), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await dictionariesApi.getMeasurementUnitsPage('token')
    await dictionariesApi.createMeasurementUnit('token', { name: 'комплект' })
    await dictionariesApi.updateMeasurementUnit('token', unit.id, { name: 'упаковка' })
    await dictionariesApi.archiveMeasurementUnit('token', unit.id, 'Дубликат')
    await dictionariesApi.restoreMeasurementUnit('token', unit.id)

    expect(fetchMock.mock.calls.map(([path]) => path)).toEqual([
      '/api/dictionaries/measurement-units/page?offset=0&limit=100',
      '/api/dictionaries/measurement-units',
      '/api/dictionaries/measurement-units/unit-1',
      '/api/dictionaries/measurement-units/unit-1',
      '/api/dictionaries/measurement-units/unit-1/restore',
    ])
  })

  it('requests only user-facing tariff templates for the tariff dictionary', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ items: [], totalCount: 0, offset: 0, limit: 25 }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await dictionariesApi.getTariffsPage('token', undefined, 0, 25, false, true)

    expect(fetchMock).toHaveBeenCalledWith('/api/dictionaries/tariffs/page?offset=0&limit=25&templatesOnly=true', expect.any(Object))
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

  it('creates opening-balance adjustment documents', async () => {
    const adjustment = { id: 'adjustment-1', targetKind: 'garage', targetId: 'garage-1', effectiveDate: '2026-07-01', previousAmount: 100, newAmount: 120, reason: 'Сверка' }
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(adjustment), { status: 201 }))
    vi.stubGlobal('fetch', fetchMock)

    const created = await dictionariesApi.adjustGarageOpeningBalance?.('token', 'garage-1', { effectiveDate: '2026-07-01', newAmount: 120, reason: 'Сверка' })

    expect(created).toEqual(adjustment)
    expect(fetchMock).toHaveBeenCalledWith('/api/dictionaries/garages/garage-1/opening-balance-adjustments', expect.objectContaining({ method: 'POST', body: JSON.stringify({ effectiveDate: '2026-07-01', newAmount: 120, reason: 'Сверка' }) }))
  })

  it('keeps unrelated cached dictionaries after a successful mutation', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 'owner-1' }), { status: 201 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await dictionariesApi.getGarages('token', undefined, 100)
    await dictionariesApi.getSuppliers('token', undefined, undefined, 100)
    await dictionariesApi.createOwner('token', { lastName: 'Иванов', firstName: 'Иван' })
    await dictionariesApi.getGarages('token', undefined, 100)
    await dictionariesApi.getSuppliers('token', undefined, undefined, 100)

    expect(fetchMock).toHaveBeenCalledTimes(4)
  })

  it('keeps cached reads when a mutation fails', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'Не сохранено' }), { status: 500 }))
    vi.stubGlobal('fetch', fetchMock)

    await dictionariesApi.getGarages('token', undefined, 100)
    await expect(dictionariesApi.createOwner('token', { lastName: 'Иванов', firstName: 'Иван' })).rejects.toThrow('Не сохранено')
    await dictionariesApi.getGarages('token', undefined, 100)

    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('invalidates dependent supplier lists when a supplier group changes', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 'group-1' }), { status: 201 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await dictionariesApi.getSupplierGroups('token', undefined, 100)
    await dictionariesApi.getSuppliers('token', undefined, undefined, 100)
    await dictionariesApi.createSupplierGroup('token', { name: 'Коммунальные услуги' })
    await dictionariesApi.getSupplierGroups('token', undefined, 100)
    await dictionariesApi.getSuppliers('token', undefined, undefined, 100)

    expect(fetchMock).toHaveBeenCalledTimes(5)
  })

  it('invalidates a dictionary after a successful no-content archive response', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await dictionariesApi.getOwners('token', undefined, 100)
    await dictionariesApi.archiveOwner('token', 'owner-1', 'Дубликат')
    await dictionariesApi.getOwners('token', undefined, 100)

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

  it('updates a regular service and its tariff through one request', async () => {
    const response = { service: { id: 'service-1', tariffId: 'tariff-1' }, tariff: { id: 'tariff-1', rate: 100.8 } }
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(response), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)
    const request = {
      service: {
        name: 'Вода',
        isRegular: true,
        periodicityMonths: 1,
        accrualStartMonth: 1,
        paymentDueDay: 30,
        paymentDueMonth: null,
        overdueGraceDays: 30,
        incomeTypeId: 'income-water',
        expenseTypeId: 'expense-water',
        expenseFundId: 'fund-water',
        tariffId: 'tariff-1',
        isMetered: true,
        hasTieredTariff: false,
        unitName: 'м³',
      },
      rate: 100.8,
      tariffMode: 'metered' as const,
      effectiveFrom: '2026-08-01',
      changeReason: 'Смена режима',
      calculationBase: 'meter_water',
    }

    await expect(dictionariesApi.updateChargeServiceWithTariff('token', 'service-1', request)).resolves.toEqual(response)

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/dictionaries/charge-services/service-1/with-tariff',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify(request),
        headers: expect.objectContaining({ Authorization: 'Bearer token' }),
      }),
    )
  })

  it('closes a fee campaign with an optional closure comment', async () => {
    const response = {
      id: 'fee-campaign-1',
      closedAtUtc: '2026-07-27T13:00:00Z',
      isClosedEarly: true,
      closureComment: 'Решение правления',
    }
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(response), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(dictionariesApi.closeFeeCampaign('token', 'fee-campaign-1', {
      comment: 'Решение правления',
    })).resolves.toEqual(response)

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/dictionaries/fee-campaigns/fee-campaign-1/close',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ comment: 'Решение правления' }),
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

  it('forwards cancellation and bypasses the shared response cache for an abortable read', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify({
      items: [],
      totalCount: 0,
      offset: 0,
      limit: 25,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()

    await dictionariesApi.getGaragesPage('token', undefined, 0, 25, false, undefined, undefined, false, {}, controller.signal)
    await dictionariesApi.getGaragesPage('token', undefined, 0, 25, false, undefined, undefined, false, {}, controller.signal)

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(fetchMock.mock.calls[0][0]).toBe('/api/dictionaries/garages/page?offset=0&limit=25')
    expect(fetchMock.mock.calls[0][1]?.signal).toBeInstanceOf(AbortSignal)
  })
})
