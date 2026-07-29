import { describe, expect, it, vi } from 'vitest'
import type { FeeReportDto } from '../../services/reportsApi'
import { loadAllFeeReportPages, loadAllReportPages, ReportPageLoadCancelledError } from './loadAllReportPages'

type TestReport = {
  rows: number[]
  rowCount: number
  offset: number
  limit: number
  total: number
}

describe('loadAllReportPages', () => {
  it('loads every server-bounded page and returns one complete report', async () => {
    const loadPage = vi.fn(async (offset: number, limit: number): Promise<TestReport> => ({
      rows: [1, 2, 3, 4, 5].slice(offset, offset + limit),
      rowCount: 5,
      offset,
      limit,
      total: 15,
    }))

    await expect(loadAllReportPages(loadPage, { pageSize: 2 })).resolves.toEqual({
      rows: [1, 2, 3, 4, 5],
      rowCount: 5,
      offset: 0,
      limit: 5,
      total: 15,
    })
    expect(loadPage.mock.calls).toEqual([[0, 2], [2, 2], [4, 2]])
  })

  it('stops an obsolete load before requesting its next page', async () => {
    let cancelled = false
    const loadPage = vi.fn(async (offset: number, limit: number): Promise<TestReport> => {
      cancelled = true
      return { rows: [1, 2], rowCount: 4, offset, limit, total: 10 }
    })

    await expect(loadAllReportPages(loadPage, {
      pageSize: 2,
      isCancelled: () => cancelled,
    })).rejects.toBeInstanceOf(ReportPageLoadCancelledError)
    expect(loadPage).toHaveBeenCalledTimes(1)
  })

  it('rejects a non-progressing incomplete server page', async () => {
    const loadPage = vi.fn(async (offset: number, limit: number): Promise<TestReport> => ({
      rows: offset === 0 ? [1, 2] : [],
      rowCount: 3,
      offset,
      limit,
      total: 6,
    }))

    await expect(loadAllReportPages(loadPage, { pageSize: 2 })).rejects.toThrow('Сервер вернул неполный отчёт')
  })

  it('loads all fee garage rows while keeping the complete summary', async () => {
    const garageRows: FeeReportDto['garageRows'] = [1, 2, 3].map((index) => ({
      garageId: `garage-${index}`,
      garageNumber: String(index),
      ownerName: null,
      incomeTypeId: 'fee-1',
      feeName: 'Целевой сбор',
      accrued: 100,
      paid: index === 1 ? 100 : 0,
      lastPaymentDate: null,
      debt: index === 1 ? 0 : 100,
    }))
    const summaryRows: FeeReportDto['summaryRows'] = [{
      incomeTypeId: 'fee-1',
      name: 'Целевой сбор',
      goal: 'Ремонт',
      feeAmount: 300,
      collected: 100,
    }]
    const loadPage = vi.fn(async (offset: number, limit: number): Promise<FeeReportDto> => {
      const pageRows = garageRows.slice(offset, offset + limit)
      return {
        variation: 'Все сборы',
        accruedTotal: 300,
        collectedTotal: 100,
        debtTotal: 200,
        rowCount: summaryRows.length + garageRows.length,
        summaryRows,
        garageRows: pageRows,
        debtorRows: pageRows.filter((row) => row.debt > 0).map((row) => ({
          garageId: row.garageId,
          garageNumber: row.garageNumber,
          ownerName: row.ownerName,
          incomeTypeId: row.incomeTypeId,
          feeName: row.feeName,
          paid: row.paid,
          lastPaymentDate: row.lastPaymentDate,
          debt: row.debt,
        })),
      }
    })

    const report = await loadAllFeeReportPages(loadPage, { pageSize: 2 })

    expect(report.summaryRows).toEqual(summaryRows)
    expect(report.garageRows).toEqual(garageRows)
    expect(report.debtorRows.map((row) => row.garageId)).toEqual(['garage-2', 'garage-3'])
    expect(loadPage.mock.calls).toEqual([[0, 2], [2, 2]])
  })
})
