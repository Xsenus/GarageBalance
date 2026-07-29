import type { FeeReportDto } from '../../services/reportsApi'

export type PagedReport<TRow> = {
  rows: TRow[]
  rowCount: number
  offset: number
  limit: number
}

export class ReportPageLoadCancelledError extends Error {
  constructor() {
    super('Загрузка отчёта отменена.')
    this.name = 'ReportPageLoadCancelledError'
  }
}

export async function loadAllReportPages<TRow, TReport extends PagedReport<TRow>>(
  loadPage: (offset: number, limit: number) => Promise<TReport>,
  options: {
    pageSize?: number
    isCancelled?: () => boolean
  } = {},
): Promise<TReport> {
  const pageSize = options.pageSize ?? 500
  const isCancelled = options.isCancelled ?? (() => false)
  let firstPage: TReport | null = null
  const rows: TRow[] = []

  while (firstPage === null || rows.length < firstPage.rowCount) {
    if (isCancelled()) {
      throw new ReportPageLoadCancelledError()
    }

    const page = await loadPage(rows.length, pageSize)
    if (isCancelled()) {
      throw new ReportPageLoadCancelledError()
    }

    firstPage ??= page
    if (page.rows.length === 0) {
      if (rows.length < firstPage.rowCount) {
        throw new Error('Сервер вернул неполный отчёт. Повторите загрузку.')
      }
      break
    }

    rows.push(...page.rows)
  }

  if (firstPage === null) {
    throw new Error('Сервер не вернул данные отчёта.')
  }

  return {
    ...firstPage,
    rows,
    offset: 0,
    limit: Math.max(rows.length, 1),
  }
}

export async function loadAllFeeReportPages(
  loadPage: (offset: number, limit: number) => Promise<FeeReportDto>,
  options: {
    pageSize?: number
    isCancelled?: () => boolean
  } = {},
): Promise<FeeReportDto> {
  const pageSize = options.pageSize ?? 500
  const isCancelled = options.isCancelled ?? (() => false)
  let firstPage: FeeReportDto | null = null
  const garageRows: FeeReportDto['garageRows'] = []
  const debtorRows: FeeReportDto['debtorRows'] = []

  while (firstPage === null || garageRows.length < Math.max(firstPage.rowCount - firstPage.summaryRows.length, 0)) {
    if (isCancelled()) {
      throw new ReportPageLoadCancelledError()
    }

    const page = await loadPage(garageRows.length, pageSize)
    if (isCancelled()) {
      throw new ReportPageLoadCancelledError()
    }

    firstPage ??= page
    if (page.garageRows.length === 0) {
      const expectedGarageRows = Math.max(firstPage.rowCount - firstPage.summaryRows.length, 0)
      if (garageRows.length < expectedGarageRows) {
        throw new Error('Сервер вернул неполный отчёт по сборам. Повторите загрузку.')
      }
      break
    }

    garageRows.push(...page.garageRows)
    debtorRows.push(...page.debtorRows)
  }

  if (firstPage === null) {
    throw new Error('Сервер не вернул данные отчёта по сборам.')
  }

  return {
    ...firstPage,
    garageRows,
    debtorRows,
  }
}
