import type { AccessImportRunDto } from '../services/importApi'

export function buildReportFileName(type: 'consolidated' | 'garages' | 'income' | 'expense' | 'cash-payments' | 'bank-deposits' | 'fund-changes', dateFrom: string, dateTo: string, extension: 'xlsx' | 'pdf'): string {
  return `garagebalance-${type}-${dateFrom.replaceAll('-', '')}-${dateTo.replaceAll('-', '')}.${extension}`
}

export function buildSnapshotReportFileName(type: 'fees', extension: 'xlsx' | 'pdf'): string {
  return `garagebalance-${type}.${extension}`
}

export function buildImportReportFileName(run: AccessImportRunDto): string {
  const startedAt = run.startedAtUtc.slice(0, 19).replaceAll('-', '').replaceAll(':', '').replace('T', '-')
  const sourceName = run.originalFileName.replace(/\.[^.]+$/, '').replaceAll(' ', '-').toLowerCase()
  return `garagebalance-access-dry-run-${sourceName}-${startedAt}.json`
}

export function buildAuditExportFileName(date = new Date(), extension: 'csv' | 'xlsx' = 'csv'): string {
  return `garagebalance-audit-${date.toISOString().slice(0, 10).replaceAll('-', '')}.${extension}`
}

export function getFormValues(form: FormData, name: string): string[] {
  return form
    .getAll(name)
    .map((value) => String(value))
    .filter(Boolean)
}

export function downloadBlob(blob: Blob, fileName: string) {
  if (typeof URL.createObjectURL !== 'function') {
    return
  }

  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.append(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
