// @vitest-environment node
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

const featureCoverage = [
  ['audit/AuditPanel.tsx', 1],
  ['contractors/ContractorsPanel.tsx', 4],
  ['dictionaries/DictionaryPanel.tsx', 1],
  ['finance/FinancePanel.tsx', 1],
  ['funds/FundsPanel.tsx', 1],
  ['import/ImportPanel.tsx', 4],
  ['meterReadings/MeterReadingsPanel.tsx', 1],
  ['reports/ReportPanel.tsx', 1],
  ['tariffs/TariffsAndFeesPanel.tsx', 3],
  ['users/UserManagementPanel.tsx', 1],
] as const

describe('shared pagination coverage', () => {
  it.each(featureCoverage)('%s keeps every large list on TablePagination', (relativePath, minimumCount) => {
    const source = readFileSync(resolve(process.cwd(), 'src', 'features', relativePath), 'utf8')
    const renderedPaginationCount = source.match(/<TablePagination\b/g)?.length ?? 0

    expect(source).toContain("from '../../shared/TablePagination'")
    expect(renderedPaginationCount).toBeGreaterThanOrEqual(minimumCount)
  })

  it('does not bring back a native page-size select', () => {
    const appCss = readFileSync(resolve(process.cwd(), 'src', 'App.css'), 'utf8')
    const paginationSource = readFileSync(resolve(process.cwd(), 'src', 'shared', 'TablePagination.tsx'), 'utf8')

    expect(appCss).not.toContain('.dictionary-pagination select')
    expect(paginationSource).not.toContain('<select')
    expect(paginationSource).toContain('pageSizeOptions.map')
  })
})
