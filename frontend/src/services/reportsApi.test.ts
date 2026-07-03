import { afterEach, describe, expect, it, vi } from 'vitest'

import { reportsApi } from './reportsApi'

describe('reportsApi', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('downloads report exports through POST because the backend records audit events', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response('report', { status: 200, headers: { 'Content-Type': 'application/octet-stream' } })))
    vi.stubGlobal('fetch', fetchMock)

    await reportsApi.exportConsolidatedReportXlsx('token', { monthFrom: '2026-06-01', monthTo: '2026-06-01', search: '12' })
    await reportsApi.exportConsolidatedReportPdf('token', { monthFrom: '2026-06-01', monthTo: '2026-06-01', search: '12' })
    await reportsApi.exportIncomeReportXlsx('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', garageIds: ['garage-1'], ownerIds: ['owner-1'], incomeTypeIds: ['income-1'], rowMode: 'all' })
    await reportsApi.exportIncomeReportPdf('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', garageIds: ['garage-1'], ownerIds: ['owner-1'], incomeTypeIds: ['income-1'], rowMode: 'all' })
    await reportsApi.exportExpenseReportXlsx('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', supplierIds: ['supplier-1'], expenseTypeIds: ['expense-1'], rowMode: 'all' })
    await reportsApi.exportExpenseReportPdf('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', supplierIds: ['supplier-1'], expenseTypeIds: ['expense-1'], rowMode: 'all' })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/reports/consolidated/export/xlsx?monthFrom=2026-06-01&monthTo=2026-06-01&search=12', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/reports/consolidated/export/pdf?monthFrom=2026-06-01&monthTo=2026-06-01&search=12', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/reports/income/export/xlsx?dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&garageIds=garage-1&ownerIds=owner-1&incomeTypeIds=income-1', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/api/reports/income/export/pdf?dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&garageIds=garage-1&ownerIds=owner-1&incomeTypeIds=income-1', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(5, '/api/reports/expense/export/xlsx?dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&supplierIds=supplier-1&expenseTypeIds=expense-1', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(6, '/api/reports/expense/export/pdf?dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&supplierIds=supplier-1&expenseTypeIds=expense-1', postRequest())
  })
})

function postRequest() {
  return {
    method: 'POST',
    headers: {
      Authorization: 'Bearer token',
    },
  }
}
