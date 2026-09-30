import type { ServiceReport, ServiceReportKind } from '../../services/serviceReportsApi'

export type ServiceReportDisplayRow = { key: string; date: string | null; garageNumber: string; amounts: number[]; total: number; dayTotal: boolean }
export const serviceReportRowHeight = 38
export function serviceReportDisplayRows(report: ServiceReport, kind: ServiceReportKind): ServiceReportDisplayRow[] {
  const days = new Map(report.days.map((day) => [day.date, day]))
  return report.rows.flatMap((row, index) => {
    const result: ServiceReportDisplayRow[] = [{ ...row, key: `${row.date}-${row.garageId}`, dayTotal: false }]
    const day = row.date ? days.get(row.date) : undefined
    if (kind === 'payments' && day && row.date !== report.rows[index + 1]?.date && (index < report.rows.length - 1 || report.rows.length === report.rowCount)) result.push({ key: `day-${day.date}`, date: day.date, garageNumber: '', amounts: day.amounts, total: day.total, dayTotal: true })
    return result
  })
}
export function serviceReportWindow(rows: ServiceReportDisplayRow[], scrollTop: number) {
  const start = Math.max(0, Math.min(rows.length - 1, Math.floor(Math.max(scrollTop, 0) / serviceReportRowHeight) - 8))
  const end = Math.min(rows.length, start + 80)
  return { rows: rows.slice(start, end), before: start * serviceReportRowHeight, after: (rows.length - end) * serviceReportRowHeight }
}
