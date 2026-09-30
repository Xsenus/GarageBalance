import { expect, it } from 'vitest'
import type { ServiceReport } from '../../services/serviceReportsApi'
import { serviceReportDisplayRows, serviceReportWindow } from './serviceReportRows'
const report: ServiceReport = { dateFrom: null, dateTo: '2026-09-30', columns: [], rows: [{ date: '2026-09-30', garageId: 'g2', garageNumber: '2', amounts: [10], total: 10 }], days: [{ date: '2026-09-30', amounts: [20], total: 20 }], totals: [20], total: 20, rowCount: 2, offset: 0, limit: 50 }
it('does not insert a premature day total at an unfinished page boundary', () => {
  expect(serviceReportDisplayRows(report, 'payments')).toHaveLength(1)
  const complete = { ...report, rows: [...report.rows, { ...report.rows[0], garageId: 'g10', garageNumber: '10' }] }
  expect(serviceReportDisplayRows(complete, 'payments').map((row) => [row.key, row.total, row.dayTotal])).toEqual([['2026-09-30-g2', 10, false], ['2026-09-30-g10', 10, false], ['day-2026-09-30', 20, true]])
  expect(serviceReportDisplayRows(complete, 'debt')).toHaveLength(2)
})
it('renders only a bounded window with accurate spacers and handles empty and negative scroll', () => {
  const rows = Array.from({ length: 1000 }, (_, index) => ({ key: String(index), date: null, garageNumber: String(index), amounts: [1], total: 1, dayTotal: false }))
  expect(serviceReportWindow(rows, 0).rows).toHaveLength(80)
  const window = serviceReportWindow(rows, 3800)
  expect(window.rows[0].key).toBe('92'); expect(window.before).toBe(3496); expect(window.after).toBe(31464)
  expect(serviceReportWindow(rows, -20).before).toBe(0)
  expect(serviceReportWindow([], 0)).toEqual({ rows: [], before: 0, after: 0 })
  expect(serviceReportWindow(rows, 1000000).rows).toHaveLength(1)
})
