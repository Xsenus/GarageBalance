// @vitest-environment node
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
    await reportsApi.exportIncomeReportXlsx('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', garageIds: ['garage-1'], ownerIds: ['owner-1'], incomeTypeIds: ['income-1'], rowMode: 'all', groupPayments: true })
    await reportsApi.exportIncomeReportPdf('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', garageIds: ['garage-1'], ownerIds: ['owner-1'], incomeTypeIds: ['income-1'], rowMode: 'all', groupPayments: true })
    await reportsApi.exportExpenseReportXlsx('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', supplierIds: ['supplier-1'], expenseTypeIds: ['expense-1'], rowMode: 'all' })
    await reportsApi.exportExpenseReportPdf('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', supplierIds: ['supplier-1'], staffMemberIds: ['staff-1'], expenseTypeIds: ['expense-1'], rowMode: 'all' })
    await reportsApi.exportCashPaymentReportXlsx('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'чек' })
    await reportsApi.exportCashPaymentReportPdf('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'чек' })
    await reportsApi.exportBankDepositReportXlsx('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'банк' })
    await reportsApi.exportBankDepositReportPdf('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'банк' })
    await reportsApi.exportFeeReportXlsx('token', { variation: 'Сбор на ворота' })
    await reportsApi.exportFeeReportPdf('token', { variation: 'Сбор на ворота' })
    await reportsApi.exportFundChangeReportXlsx('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'фонд' })
    await reportsApi.exportFundChangeReportPdf('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'фонд' })
    await reportsApi.exportGarageReportXlsx('token', { monthFrom: '2026-06-01', monthTo: '2026-07-01', garageIds: ['garage-1'], groupAccruals: true })
    await reportsApi.exportGarageReportPdf('token', { monthFrom: '2026-06-01', monthTo: '2026-07-01', garageIds: ['garage-1'], groupAccruals: true })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/reports/consolidated/export/xlsx?monthFrom=2026-06-01&monthTo=2026-06-01&search=12', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/reports/consolidated/export/pdf?monthFrom=2026-06-01&monthTo=2026-06-01&search=12', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/reports/income/export/xlsx?dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&groupPayments=true&garageIds=garage-1&ownerIds=owner-1&incomeTypeIds=income-1', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/api/reports/income/export/pdf?dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&groupPayments=true&garageIds=garage-1&ownerIds=owner-1&incomeTypeIds=income-1', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(5, '/api/reports/expense/export/xlsx?dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&supplierIds=supplier-1&expenseTypeIds=expense-1', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(6, '/api/reports/expense/export/pdf?dateFrom=2026-06-01&dateTo=2026-06-30&rowMode=all&supplierIds=supplier-1&staffMemberIds=staff-1&expenseTypeIds=expense-1', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(7, '/api/reports/cash-payments/export/xlsx?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D1%87%D0%B5%D0%BA', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(8, '/api/reports/cash-payments/export/pdf?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D1%87%D0%B5%D0%BA', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(9, '/api/reports/bank-deposits/export/xlsx?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D0%B1%D0%B0%D0%BD%D0%BA', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(10, '/api/reports/bank-deposits/export/pdf?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D0%B1%D0%B0%D0%BD%D0%BA', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(11, '/api/reports/fees/export/xlsx?variation=%D0%A1%D0%B1%D0%BE%D1%80+%D0%BD%D0%B0+%D0%B2%D0%BE%D1%80%D0%BE%D1%82%D0%B0', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(12, '/api/reports/fees/export/pdf?variation=%D0%A1%D0%B1%D0%BE%D1%80+%D0%BD%D0%B0+%D0%B2%D0%BE%D1%80%D0%BE%D1%82%D0%B0', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(13, '/api/reports/fund-changes/export/xlsx?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D1%84%D0%BE%D0%BD%D0%B4', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(14, '/api/reports/fund-changes/export/pdf?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D1%84%D0%BE%D0%BD%D0%B4', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(15, '/api/reports/garages/export/xlsx?monthFrom=2026-06-01&monthTo=2026-07-01&groupAccruals=true&garageIds=garage-1', postRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(16, '/api/reports/garages/export/pdf?monthFrom=2026-06-01&monthTo=2026-07-01&groupAccruals=true&garageIds=garage-1', postRequest())
  })

  it('loads paged garage, income, expense, cash, bank and fund reports with other dedicated filtered endpoints', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)

    await reportsApi.getIncomeReport('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: '12', rowMode: 'payments', groupPayments: true, offset: 20, limit: 20 })
    await reportsApi.getExpenseReport('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'Водоканал', supplierIds: ['supplier-1', 'supplier-2'], staffMemberIds: ['staff-1', 'staff-2'], expenseTypeIds: ['expense-1'], rowMode: 'payments', offset: 20, limit: 20 })
    await reportsApi.getCashPaymentReport('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'чек', offset: 20, limit: 20 })
    await reportsApi.getBankDepositReport('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', search: 'банк', offset: 20, limit: 20 })
    await reportsApi.getFeeReport('token', { variation: 'Сбор на ворота', limit: 20 })
    await reportsApi.getFundChangeReport('token', { dateFrom: '2026-06-01', dateTo: '2026-06-30', offset: 20, limit: 20 })
    await reportsApi.getGarageReport('token', { monthFrom: '2026-06-01', monthTo: '2026-07-01', search: '12', garageIds: ['garage-1', 'garage-2'], ownerIds: ['owner-1'], incomeTypeIds: ['income-1'], groupAccruals: true, offset: 20, limit: 20 })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/reports/income?dateFrom=2026-06-01&dateTo=2026-06-30&search=12&rowMode=payments&groupPayments=true&limit=20&offset=20', getRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/reports/expense?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D0%92%D0%BE%D0%B4%D0%BE%D0%BA%D0%B0%D0%BD%D0%B0%D0%BB&rowMode=payments&limit=20&offset=20&supplierIds=supplier-1&supplierIds=supplier-2&staffMemberIds=staff-1&staffMemberIds=staff-2&expenseTypeIds=expense-1', getRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/reports/cash-payments?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D1%87%D0%B5%D0%BA&offset=20&limit=20', getRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/api/reports/bank-deposits?dateFrom=2026-06-01&dateTo=2026-06-30&search=%D0%B1%D0%B0%D0%BD%D0%BA&offset=20&limit=20', getRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(5, '/api/reports/fees?variation=%D0%A1%D0%B1%D0%BE%D1%80+%D0%BD%D0%B0+%D0%B2%D0%BE%D1%80%D0%BE%D1%82%D0%B0&limit=20', getRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(6, '/api/reports/fund-changes?dateFrom=2026-06-01&dateTo=2026-06-30&offset=20&limit=20', getRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(7, '/api/reports/garages?monthFrom=2026-06-01&monthTo=2026-07-01&search=12&groupAccruals=true&offset=20&limit=20&garageIds=garage-1&garageIds=garage-2&ownerIds=owner-1&incomeTypeIds=income-1', getRequest())
  })

  it('forwards caller cancellation to report and quick-list reads', async () => {
    const controller = new AbortController()
    const observedSignals: Array<AbortSignal | null | undefined> = []
    const fetchMock = vi.fn().mockImplementation((_input, init) => {
      observedSignals.push(init?.signal)
      return Promise.resolve(new Response('{}', {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
      }))
    })
    vi.stubGlobal('fetch', fetchMock)

    await reportsApi.getGarageReport('token', { limit: 500 }, controller.signal)
    await reportsApi.getGarageReportQuickLists('token', controller.signal)

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/reports/garages?limit=500', getRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/reports/garage-quick-lists', getRequest())
    expect(observedSignals).toHaveLength(2)
    expect(observedSignals.every((signal) => signal instanceof AbortSignal)).toBe(true)
  })

  it('forwards the same report sorting to screen and export requests', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)

    await reportsApi.getConsolidatedReport('token', { sortBy: 'balance', sortDirection: 'asc' })
    await reportsApi.exportConsolidatedReportXlsx('token', { sortBy: 'balance', sortDirection: 'asc' })
    await reportsApi.getGarageReport('token', { sortBy: 'garageNumber', sortDirection: 'desc' })
    await reportsApi.exportGarageReportXlsx('token', { sortBy: 'garageNumber', sortDirection: 'desc' })
    await reportsApi.getIncomeReport('token', { sortBy: 'incomeAmount', sortDirection: 'asc' })
    await reportsApi.exportIncomeReportXlsx('token', { sortBy: 'incomeAmount', sortDirection: 'asc' })
    await reportsApi.getExpenseReport('token', { sortBy: 'expenseAmount', sortDirection: 'desc' })
    await reportsApi.exportExpenseReportXlsx('token', { sortBy: 'expenseAmount', sortDirection: 'desc' })
    await reportsApi.getCashPaymentReport('token', { sortBy: 'hasReceipt', sortDirection: 'asc' })
    await reportsApi.exportCashPaymentReportXlsx('token', { sortBy: 'hasReceipt', sortDirection: 'asc' })
    await reportsApi.getBankDepositReport('token', { sortBy: 'comment', sortDirection: 'desc' })
    await reportsApi.exportBankDepositReportXlsx('token', { sortBy: 'comment', sortDirection: 'desc' })
    await reportsApi.getFeeReport('token', { sortBy: 'debt', sortDirection: 'asc' })
    await reportsApi.exportFeeReportXlsx('token', { sortBy: 'debt', sortDirection: 'asc' })
    await reportsApi.getFundChangeReport('token', { sortBy: 'actorDisplayName', sortDirection: 'desc' })
    await reportsApi.exportFundChangeReportXlsx('token', { sortBy: 'actorDisplayName', sortDirection: 'desc' })

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      '/api/reports/consolidated?sortBy=balance&sortDirection=asc',
      '/api/reports/consolidated/export/xlsx?sortBy=balance&sortDirection=asc',
      '/api/reports/garages?sortBy=garageNumber&sortDirection=desc',
      '/api/reports/garages/export/xlsx?sortBy=garageNumber&sortDirection=desc',
      '/api/reports/income?sortBy=incomeAmount&sortDirection=asc',
      '/api/reports/income/export/xlsx?sortBy=incomeAmount&sortDirection=asc',
      '/api/reports/expense?sortBy=expenseAmount&sortDirection=desc',
      '/api/reports/expense/export/xlsx?sortBy=expenseAmount&sortDirection=desc',
      '/api/reports/cash-payments?sortBy=hasReceipt&sortDirection=asc',
      '/api/reports/cash-payments/export/xlsx?sortBy=hasReceipt&sortDirection=asc',
      '/api/reports/bank-deposits?sortBy=comment&sortDirection=desc',
      '/api/reports/bank-deposits/export/xlsx?sortBy=comment&sortDirection=desc',
      '/api/reports/fees?sortBy=debt&sortDirection=asc',
      '/api/reports/fees/export/xlsx?sortBy=debt&sortDirection=asc',
      '/api/reports/fund-changes?sortBy=actorDisplayName&sortDirection=desc',
      '/api/reports/fund-changes/export/xlsx?sortBy=actorDisplayName&sortDirection=desc',
    ])
  })

  it('loads and changes garage report quick lists with authenticated JSON requests', async () => {
    const list = {
      id: 'list/1',
      name: 'Северный ряд',
      garages: [{ garageId: 'garage-1', garageNumber: '12', ownerName: 'Иванов И.И.' }],
      updatedAtUtc: '2026-07-28T02:00:00Z',
      updatedByUserId: 'user-1',
    }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify([list]), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify(list), { status: 201, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify(list), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await reportsApi.getGarageReportQuickLists('token')
    await reportsApi.createGarageReportQuickList('token', { name: 'Северный ряд', garageIds: ['garage-1'] })
    await reportsApi.updateGarageReportQuickList('token', 'list/1', { name: 'Северные гаражи', garageIds: ['garage-1'] })
    await reportsApi.deleteGarageReportQuickList('token', 'list/1', 'Список больше не используется')

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/reports/garage-quick-lists', getRequest())
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/reports/garage-quick-lists', jsonRequest('POST', {
      name: 'Северный ряд',
      garageIds: ['garage-1'],
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/reports/garage-quick-lists/list%2F1', jsonRequest('PUT', {
      name: 'Северные гаражи',
      garageIds: ['garage-1'],
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/api/reports/garage-quick-lists/list%2F1', jsonRequest('DELETE', {
      reason: 'Список больше не используется',
    }))
  })
})

function getRequest(signal?: AbortSignal) {
  return {
    headers: {
      Authorization: 'Bearer token',
    },
    ...(signal ? { signal } : {}),
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

function jsonRequest(method: string, body: object) {
  return {
    method,
    body: JSON.stringify(body),
    headers: {
      'Content-Type': 'application/json',
      Authorization: 'Bearer token',
    },
  }
}
