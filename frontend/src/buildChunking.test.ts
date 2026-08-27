import { describe, expect, it } from 'vitest'
import { getManualChunkName } from '../vite.config'

describe('production chunking', () => {
  it('keeps shared controls and icons in the runtime chunk', () => {
    expect(getManualChunkName('C:\\project\\src\\shared\\LocalizedDatePicker.tsx')).toBe('app-runtime')
    expect(getManualChunkName('/project/node_modules/lucide-react/dist/esm/icons/save.js')).toBe('app-runtime')
    expect(getManualChunkName('/project/src/services/apiFetch.ts')).toBe('app-runtime')
    expect(getManualChunkName('C:\\project\\src\\services\\dictionaryResponseCache.ts')).toBe('app-runtime')
  })

  it('keeps feature groups isolated on Windows and Unix paths', () => {
    expect(getManualChunkName('C:\\project\\src\\features\\finance\\FinancePanel.tsx')).toBe('financial-operations')
    expect(getManualChunkName('/project/src/features/tariffs/TariffsAndFeesPanel.tsx')).toBe('cooperative-setup')
    expect(getManualChunkName('/project/src/features/reports/ReportPanel.tsx')).toBe('reporting')
    expect(getManualChunkName('/project/src/features/users/UserManagementPanel.tsx')).toBe('app-runtime')
  })

  it('leaves unrelated modules to Rolldown automatic chunking', () => {
    expect(getManualChunkName('/project/src/App.tsx')).toBeUndefined()
  })
})
