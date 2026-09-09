import { describe, expect, it } from 'vitest'
import { build } from 'vite'
import config, { getManualChunkName } from '../vite.config'

describe('production chunking', () => {
  it('keeps shared controls and icons in the runtime chunk', () => {
    expect(getManualChunkName('\0vite/preload-helper.js')).toBe('app-runtime')
    expect(getManualChunkName('C:\\project\\src\\shared\\LocalizedDatePicker.tsx')).toBe('app-runtime')
    expect(getManualChunkName('/project/node_modules/lucide-react/dist/esm/icons/save.js')).toBe('app-runtime')
    expect(getManualChunkName('/project/src/services/apiFetch.ts')).toBe('app-runtime')
    expect(getManualChunkName('C:\\project\\src\\services\\dictionaryResponseCache.ts')).toBe('app-runtime')
    expect(getManualChunkName('/project/src/shared/retryableLazyLoader.ts')).toBe('app-runtime')
    expect(getManualChunkName('/project/node_modules/react/index.js')).toBe('app-runtime')
    expect(getManualChunkName('/project/node_modules/react-dom/client.js')).toBe('app-runtime')
    expect(getManualChunkName('/project/node_modules/scheduler/index.js')).toBe('app-runtime')
    expect(getManualChunkName('/project/node_modules/react-router/index.js')).toBeUndefined()
  })

  it('splits authenticated accounting sections into bounded business chunks', () => {
    expect(getManualChunkName('C:\\project\\src\\features\\finance\\FinancePanel.tsx')).toBe('workspace-finance')
    expect(getManualChunkName('/project/src/features/funds/FundsPanel.tsx')).toBe('workspace-finance')
    expect(getManualChunkName('/project/src/features/import/ImportPanel.tsx')).toBe('workspace-finance')
    expect(getManualChunkName('/project/src/features/meterReadings/MeterReadingsPanel.tsx')).toBe('workspace-operations')
    expect(getManualChunkName('C:\\project\\src\\features\\contractors\\ContractorsPanel.tsx')).toBe('workspace-operations')
    expect(getManualChunkName('/project/src/features/tariffs/TariffsAndFeesPanel.tsx')).toBe('workspace-operations')
    expect(getManualChunkName('/project/src/features/reports/ReportPanel.tsx')).toBe('workspace-operations')
    expect(getManualChunkName('/project/src/features/users/UserManagementPanel.tsx')).toBe('app-runtime')
  })

  it('leaves unrelated modules to Rolldown automatic chunking', () => {
    expect(getManualChunkName('/project/src/App.tsx')).toBeUndefined()
  })

  it('keeps accounting code out of the actual login import graph', async () => {
    const result = await build({ ...config, configFile: false, logLevel: 'silent', build: { ...config.build, write: false } })
    if (Array.isArray(result) || !('output' in result)) throw new Error('Expected one completed application build')
    const chunks = result.output.filter((output) => output.type === 'chunk')
    const entry = chunks.find((chunk) => chunk.isEntry)
    const workspaces = chunks.filter((chunk) => chunk.name.startsWith('workspace-'))
    const financeWorkspace = chunks.find((chunk) => chunk.name === 'workspace-finance')
    const shell = chunks.find((chunk) => chunk.name === 'AppShell')
    expect(entry).toBeDefined()
    expect(financeWorkspace).toBeDefined()
    expect(workspaces.map((chunk) => chunk.name).sort()).toEqual([
      'workspace-finance',
      'workspace-operations',
    ])
    const visited = new Set<string>()
    const pending = [entry!.fileName]
    while (pending.length) {
      const name = pending.pop()!
      if (visited.has(name)) continue
      visited.add(name)
      pending.push(...(chunks.find((chunk) => chunk.fileName === name)?.imports ?? []))
    }
    for (const workspace of workspaces) expect(visited).not.toContain(workspace.fileName)
    expect(visited).not.toContain(shell!.fileName)
    expect(Object.keys(financeWorkspace!.modules).some((id) => /[/\\]features[/\\]funds[/\\]FundsPanel\./.test(id))).toBe(true)
    expect(Object.keys(financeWorkspace!.modules).some((id) => /[/\\]features[/\\]import[/\\]ImportPanel\./.test(id))).toBe(true)
  })
})
