// @vitest-environment node
import { afterEach, describe, expect, it, vi } from 'vitest'

import { FinanceApiError, financeApi } from './financeApi'

describe('financeApi', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('passes counterparty filters to finance page endpoints', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
      items: [],
      totalCount: 0,
      offset: 0,
      limit: 25,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)

    await financeApi.getOperationsPage('token', {
      operationKind: 'income',
      garageId: 'garage-77',
      supplierId: 'supplier-77',
      staffMemberId: 'staff-77',
      limit: 25,
    })
    await financeApi.getSupplierAccrualsPage('token', {
      supplierId: 'supplier-77',
      limit: 25,
    })
    await financeApi.getSupplierOpeningBalance('token', 'supplier-77', '2026-06')
    await financeApi.getFinancialReportPeriod('token', { supplierId: 'supplier-77' })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/finance/operations/page?operationKind=income&garageId=garage-77&supplierId=supplier-77&staffMemberId=staff-77&limit=25', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/finance/supplier-accruals/page?supplierId=supplier-77&limit=25', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/finance/suppliers/supplier-77/opening-balance?monthFrom=2026-06-01', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/api/finance/financial-report-period?supplierId=supplier-77', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('posts regular catalog accrual generation to the catalog endpoint', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
      accountingMonth: '2026-06-01',
      serviceCount: 1,
      createdCount: 1,
      skippedCount: 0,
      totalAmount: 300,
      serviceResults: [],
      skippedServices: [],
    }), { status: 201, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)

    await financeApi.generateRegularCatalogAccruals('token', {
      accountingMonth: '2026-06-01',
      comment: 'Каталог',
    })

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/accruals/generate-regular-catalog', {
      method: 'POST',
      body: JSON.stringify({
        accountingMonth: '2026-06-01',
        comment: 'Каталог',
      }),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('posts opening debt payment to the debt payment endpoint', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
      id: 'operation-debt-payment',
      operationKind: 'income',
      operationDate: '2026-06-19',
      accountingMonth: '2026-06-01',
      amount: 900,
      paymentAllocations: [],
      isCanceled: false,
    }), { status: 201, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)

    await financeApi.createGarageDebtPayment('token', {
      garageId: 'garage-88',
      operationDate: '2026-06-19',
      accountingMonth: '2026-06-01',
      amount: 900,
      comment: 'Закрываем долг',
    })

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/income/debt-payment', {
      method: 'POST',
      body: JSON.stringify({
        garageId: 'garage-88',
        operationDate: '2026-06-19',
        accountingMonth: '2026-06-01',
        amount: 900,
        comment: 'Закрываем долг',
      }),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('previews the early electricity payment warning with an optional edited operation', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      isElectricityPayment: true,
      previousPaymentDate: '2026-06-01',
      daysSincePreviousPayment: 29,
      requiresConfirmation: true,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const request = {
      garageId: 'garage-88',
      incomeTypeId: 'income-electricity',
      operationDate: '2026-06-30',
      excludedOperationId: 'operation-edited',
    }

    const warning = await financeApi.getIncomePaymentWarning('token', request)

    expect(warning).toMatchObject({ daysSincePreviousPayment: 29, requiresConfirmation: true })
    expect(fetchMock).toHaveBeenCalledWith('/api/finance/income/payment-warning', {
      method: 'POST',
      body: JSON.stringify(request),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('saves a versioned meter reading through the payment form endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: 'meter-reading-1',
      version: 'version-2',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const request = {
      garageId: 'garage-12',
      meterKind: 'water' as const,
      accountingMonth: '2026-06-01',
      readingDate: '2026-06-20',
      currentValue: 18,
      comment: 'Из формы оплаты',
      meterReadingId: 'meter-reading-1',
      expectedVersion: 'version-1',
    }

    await financeApi.savePaymentFormMeterReading('token', request)

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/payment-form/meter-reading', {
      method: 'PUT',
      body: JSON.stringify(request),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('preserves the server error code and status for recoverable finance conflicts', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      code: 'meter_reading_conflict',
      detail: 'Показание уже изменено другим пользователем.',
    }), { status: 409, headers: { 'Content-Type': 'application/problem+json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(financeApi.savePaymentFormMeterReading('token', {
      garageId: 'garage-12',
      meterKind: 'electricity',
      accountingMonth: '2026-06-01',
      readingDate: '2026-06-20',
      currentValue: 18,
      meterReadingId: 'meter-reading-1',
      expectedVersion: 'version-stale',
    })).rejects.toEqual(expect.objectContaining<Partial<FinanceApiError>>({
      name: 'FinanceApiError',
      code: 'meter_reading_conflict',
      status: 409,
      message: 'Показание уже изменено другим пользователем.',
    }))
  })

  it('sends an audited historical meter reading correction to the dedicated endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: 'meter-reading-1',
      version: 'version-2',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const request = {
      readingDate: '2026-06-21',
      currentValue: 18,
      comment: 'После сверки',
      reason: 'Сверка с бумажным журналом',
      expectedVersion: 'version-1',
    }

    await financeApi.correctHistoricalMeterReading!('token', 'meter-reading-1', request)

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/meter-readings/meter-reading-1/historical-correction', {
      method: 'PUT',
      body: JSON.stringify(request),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('loads the overdue debt breakdown for the selected garage', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
      garageId: 'garage-88',
      garageNumber: '88',
      asOfDate: '2026-07-17',
      total: 500,
      rows: [],
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)

    const result = await financeApi.getGarageOverdueDebt('token', 'garage-88')

    expect(result.total).toBe(500)
    expect(fetchMock).toHaveBeenCalledWith('/api/finance/garages/garage-88/overdue-debt', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('loads the paged historical accrual due-date reconciliation report', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      items: [],
      totalCount: 0,
      offset: 25,
      limit: 25,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await financeApi.getAccrualDueDateReviewPage!('token', { offset: 25, limit: 25 })

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/accruals/due-date-review?offset=25&limit=25', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })
})
