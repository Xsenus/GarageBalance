export type ReportSortDirection = 'asc' | 'desc'

export type ReportSort = {
  field: string
  direction: ReportSortDirection
}

export function advanceReportSort(current: ReportSort | null | undefined, field: string): ReportSort | null {
  if (current?.field !== field) {
    return { field, direction: 'asc' }
  }

  if (current.direction === 'asc') {
    return { field, direction: 'desc' }
  }

  return null
}
