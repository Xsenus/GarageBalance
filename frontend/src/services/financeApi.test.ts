import { afterEach, describe, expect, it, vi } from 'vitest'

import { financeApi } from './financeApi'

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
})
