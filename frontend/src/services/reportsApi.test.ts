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
    await reportsApi.exportCashPaymentReportXlsx('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'чек' })
    await reportsApi.exportCashPaymentReportPdf('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'чек' })
    await reportsApi.exportBankDepositReportXlsx('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'банк' })
    await reportsApi.exportBankDepositReportPdf('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'банк' })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/reports/consolidated/export/xlsx?monthFrom=2026-06-01&monthTo=2026-06-01&search=12', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/reports/consolidated/export/pdf?monthFrom=2026-06-01&monthTo=2026-06-01&search=12', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/reports/income/export/xlsx?dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&garageIds=garage-1&ownerIds=owner-1&incomeTypeIds=income-1', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/api/reports/income/export/pdf?dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&garageIds=garage-1&ownerIds=owner-1&incomeTypeIds=income-1', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(5, '/api/reports/expense/export/xlsx?dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&supplierIds=supplier-1&expenseTypeIds=expense-1', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(6, '/api/reports/expense/export/pdf?dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&supplierIds=supplier-1&expenseTypeIds=expense-1', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(7, '/api/reports/cash-payments/export/xlsx?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D1%87%D0%B5%D0%BA', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(8, '/api/reports/cash-payments/export/pdf?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D1%87%D0%B5%D0%BA', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(9, '/api/reports/bank-deposits/export/xlsx?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D0%B1%D0%B0%D0%BD%D0%BA', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(10, '/api/reports/bank-deposits/export/pdf?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D0%B1%D0%B0%D0%BD%D0%BA', postRequest())
  })

  it('loads cash, bank and fee reports through dedicated filtered endpoints', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)

    await reportsApi.getCashPaymentReport('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'чек', limit: 20 })
    await reportsApi.getBankDepositReport('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'банк', limit: 20 })
    await reportsApi.getFeeReport('token', { variation: 'Сбор на ворота', limit: 20 })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/reports/cash-payments?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D1%87%D0%B5%D0%BA&limit=20', getRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/reports/bank-deposits?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D0%B1%D0%B0%D0%BD%D0%BA&limit=20', getRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/reports/fees?variation=%D0%A1%D0%B1%D0%BE%D1%80+%D0%BD%D0%B0+%D0%B2%D0%BE%D1%80%D0%BE%D1%82%D0%B0&limit=20', getRequest())
  })
})

function getRequest() {
  return {
    headers: {
      Authorization: 'Bearer token',
    },
  }
}

function postRequest() {
  return {
    method: 'POST',
    headers: {
      Authorization: 'Bearer token',
    },
  }
}
