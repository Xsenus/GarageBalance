import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AccessImportRunDto } from '../services/importApi'
import { buildAuditExportFileName, buildImportReportFileName, buildReportFileName, downloadBlob, getFormValues } from './fileExports'

describe('shared file export helpers', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    document.body.innerHTML = ''
  })

  it('builds stable report file names from selected periods', () => {
    expect(buildReportFileName('consolidated', '2026-01-01', '2026-12-31', 'xlsx')).toBe('garagebalance-consolidated-20260101-20261231.xlsx')
    expect(buildReportFileName('income', '2026-06-01', '2026-06-30', 'pdf')).toBe('garagebalance-income-20260601-20260630.pdf')
    expect(buildAuditExportFileName(new Date('2026-06-25T10:00:00Z'))).toBe('garagebalance-audit-20260625.csv')
  })

  it('builds import dry-run report file names from run metadata', () => {
    expect(buildImportReportFileName(createImportRun({ originalFileName: 'Old Base.accdb', startedAtUtc: '2026-06-25T01:02:03Z' }))).toBe('garagebalance-access-dry-run-old-base-20260625-010203.json')
  })

  it('reads multi-select form values and skips empty entries', () => {
    const form = new FormData()
    form.append('garageIds', 'garage-1')
    form.append('garageIds', '')
    form.append('garageIds', 'garage-2')

    expect(getFormValues(form, 'garageIds')).toEqual(['garage-1', 'garage-2'])
  })

  it('downloads blobs through an ephemeral link and revokes the object URL', () => {
    const createObjectUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:garagebalance-export')
    const revokeObjectUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined)
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)

    downloadBlob(new Blob(['report'], { type: 'text/plain' }), 'report.csv')

    expect(createObjectUrl).toHaveBeenCalledTimes(1)
    expect(click).toHaveBeenCalledTimes(1)
    expect(revokeObjectUrl).toHaveBeenCalledWith('blob:garagebalance-export')
    expect(document.body.querySelector('a')).toBeNull()
  })

  it('does nothing when object URLs are unavailable', () => {
    const originalCreateObjectUrl = URL.createObjectURL
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)

    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: undefined })
    try {
      downloadBlob(new Blob(['report']), 'report.csv')
    } finally {
      Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: originalCreateObjectUrl })
    }

    expect(click).not.toHaveBeenCalled()
    expect(document.body.querySelector('a')).toBeNull()
  })
})

function createImportRun(overrides: Partial<AccessImportRunDto> = {}): AccessImportRunDto {
  return {
    id: 'run-1',
    mode: 'dry_run',
    status: 'completed',
    originalFileName: 'old.accdb',
    fileExtension: '.accdb',
    fileSizeBytes: 1024,
    contentSha256: 'hash',
    startedAtUtc: '2026-06-25T01:02:03Z',
    finishedAtUtc: '2026-06-25T01:02:04Z',
    totalChecks: 4,
    passedChecks: 4,
    warningCount: 0,
    errorCount: 0,
    summary: 'Готово',
    checks: [],
    ...overrides,
  }
}
