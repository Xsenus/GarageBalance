import { describe, expect, it } from 'vitest'
import { getManualChunkName } from '../vite.config'

describe('production chunking', () => {
  it('keeps shared controls and icons in the runtime chunk', () => {
    expect(getManualChunkName('\0vite/preload-helper.js')).toBe('app-runtime')
    expect(getManualChunkName('C:\\project\\src\\shared\\LocalizedDatePicker.tsx')).toBe('app-runtime')
    expect(getManualChunkName('/project/node_modules/lucide-react/dist/esm/icons/save.js')).toBe('app-runtime')
    expect(getManualChunkName('/project/src/services/apiFetch.ts')).toBe('app-runtime')
    expect(getManualChunkName('C:\\project\\src\\services\\dictionaryResponseCache.ts')).toBe('app-runtime')
    expect(getManualChunkName('/project/src/shared/retryableLazyLoader.ts')).toBe('app-runtime')
  })

  it('keeps independently loaded workspace sections in separate chunks', () => {
    expect(getManualChunkName('C:\\project\\src\\features\\finance\\FinancePanel.tsx')).toBe('financial-operations')
    expect(getManualChunkName('/project/src/features/meterReadings/MeterReadingsPanel.tsx')).toBe('financial-operations')
    expect(getManualChunkName('/project/src/features/funds/FundsPanel.tsx')).toBe('funds')
    expect(getManualChunkName('C:\\project\\src\\features\\contractors\\ContractorsPanel.tsx')).toBe('contractors')
    expect(getManualChunkName('/project/src/features/tariffs/TariffsAndFeesPanel.tsx')).toBe('tariffs')
    expect(getManualChunkName('/project/src/features/reports/ReportPanel.tsx')).toBe('reporting')
    expect(getManualChunkName('/project/src/features/users/UserManagementPanel.tsx')).toBe('app-runtime')
  })

  it('leaves unrelated modules to Rolldown automatic chunking', () => {
    expect(getManualChunkName('/project/src/App.tsx')).toBeUndefined()
  })
})
