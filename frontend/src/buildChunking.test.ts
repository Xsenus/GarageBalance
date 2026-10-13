// @vitest-environment node
import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { describe, expect, it } from 'vitest'
import { getManualChunkName } from '../vite.config'

const runNode = promisify(execFile)
type BuildChunkInfo = { isEntry: boolean; fileName: string; name: string; imports: string[]; modules: string[]; codeBytes: number }

describe('production chunking', () => {
  it('keeps shared controls and icons in the runtime chunk', () => {
    expect(getManualChunkName('\0vite/preload-helper.js')).toBe('app-runtime')
    expect(getManualChunkName('C:\\project\\src\\shared\\LocalizedDatePicker.tsx')).toBe('app-runtime')
    expect(getManualChunkName('/project/src/shared/PhoneListInput.tsx')).toBe('app-runtime')
    expect(getManualChunkName('/project/src/shared/DecimalTextInput.tsx')).toBe('app-runtime')
    expect(getManualChunkName('/project/node_modules/lucide-react/dist/esm/icons/save.js')).toBe('app-runtime')
    expect(getManualChunkName('/project/src/services/apiFetch.ts')).toBe('app-runtime')
    expect(getManualChunkName('C:\\project\\src\\services\\dictionaryResponseCache.ts')).toBe('app-runtime')
    expect(getManualChunkName('/project/src/shared/retryableLazyLoader.ts')).toBe('app-runtime')
    expect(getManualChunkName('/project/src/shared/lazyChunkRecovery.ts')).toBe('app-runtime')
    expect(getManualChunkName('/project/src/services/diagnosticsApi.ts')).toBe('app-runtime')
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
    expect(getManualChunkName('/project/src/features/workspace/AppShell.tsx')).toBe('workspace-finance')
    expect(getManualChunkName('/project/src/features/dictionaries/DictionaryPanel.tsx')).toBe('workspace-operations')
    expect(getManualChunkName('/project/src/shared/dictionaryWorkbench.ts')).toBe('workspace-operations')
    expect(getManualChunkName('/project/src/features/reports/ReportPanel.tsx')).toBe('workspace-finance')
    expect(getManualChunkName('C:\\project\\src\\features\\reports\\ReportPanel.tsx')).toBe('workspace-finance')
    expect(getManualChunkName('/project/src/features/users/UserManagementPanel.tsx')).toBe('workspace-operations')
  })

  it('leaves unrelated modules to Rolldown automatic chunking', () => {
    expect(getManualChunkName('/project/src/App.tsx')).toBeUndefined()
  })

  it('keeps accounting code out of the actual login import graph', async () => {
    // An isolated process uses the same React/JSX production mode as npm run build
    // without changing NODE_ENV for component tests running at the same time.
    const { stdout } = await runNode(process.execPath, ['--input-type=module', '-e', `
      import { build, loadConfigFromFile } from 'vite';
      const loaded = await loadConfigFromFile({ command: 'build', mode: 'production' });
      const config = loaded.config;
      const result = await build({ ...config, configFile: false, logLevel: 'silent', build: { ...config.build, write: false } });
      if (Array.isArray(result) || !('output' in result)) throw new Error('Expected one completed application build');
      const chunks = result.output.filter(output => output.type === 'chunk').map(chunk => ({
        isEntry: chunk.isEntry, fileName: chunk.fileName, name: chunk.name, imports: chunk.imports,
        modules: Object.keys(chunk.modules), codeBytes: Buffer.byteLength(chunk.code, 'utf8')
      }));
      process.stdout.write(JSON.stringify(chunks));
    `], { env: { ...process.env, NODE_ENV: 'production' }, maxBuffer: 2 * 1024 * 1024 })
    const chunks = JSON.parse(stdout) as BuildChunkInfo[]
    const entry = chunks.find((chunk) => chunk.isEntry)
    const workspaces = chunks.filter((chunk) => chunk.name.startsWith('workspace-'))
    const financeWorkspace = chunks.find((chunk) => chunk.name === 'workspace-finance')
    const operationsWorkspace = chunks.find((chunk) => chunk.name === 'workspace-operations')
    expect(entry).toBeDefined()
    expect(financeWorkspace).toBeDefined()
    expect(operationsWorkspace).toBeDefined()
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
    for (const workspace of workspaces) {
      expect(visited).not.toContain(workspace.fileName)
      expect(workspace.codeBytes).toBeLessThanOrEqual(500_000)
    }
    expect(financeWorkspace!.modules.some((id) => /[/\\]features[/\\]workspace[/\\]AppShell\./.test(id))).toBe(true)
    expect(financeWorkspace!.modules.some((id) => /[/\\]features[/\\]funds[/\\]FundsPanel\./.test(id))).toBe(true)
    expect(financeWorkspace!.modules.some((id) => /[/\\]features[/\\]import[/\\]ImportPanel\./.test(id))).toBe(true)
    expect(financeWorkspace!.modules.some((id) => /[/\\]features[/\\]reports[/\\]ReportPanel\./.test(id))).toBe(true)
    expect(operationsWorkspace!.modules.some((id) => /[/\\]features[/\\]tariffs[/\\]TariffsAndFeesPanel\./.test(id))).toBe(true)
  })
})
