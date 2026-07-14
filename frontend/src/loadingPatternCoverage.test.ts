import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

const loadingFeatures = [
  ['audit/AuditPanel.tsx', 1],
  ['contractors/ContractorsPanel.tsx', 3],
  ['dictionaries/DictionaryPanel.tsx', 1],
  ['finance/FinancePanel.tsx', 5],
  ['funds/FundsPanel.tsx', 3],
  ['import/ImportPanel.tsx', 3],
  ['meterReadings/MeterReadingsPanel.tsx', 1],
  ['releases/ReleasePanel.tsx', 1],
  ['reports/ReportPanel.tsx', 6],
  ['settings/PasswordPanel.tsx', 3],
  ['tariffs/TariffsAndFeesPanel.tsx', 3],
  ['users/UserManagementPanel.tsx', 1],
] as const

const removedBareLoadingMessages = [
  'Загрузка гаражей...',
  'Загрузка поставщиков...',
  'Загрузка персонала...',
  'Загрузка лога...',
  'Загрузка созданных записей...',
  '>Загружаем пользователей...</p>',
  '>Загружаем историю платежей...</td>',
  '>Загружаем форму выплат...</p>',
] as const

describe('shared loading-state coverage', () => {
  it.each(loadingFeatures)('%s uses skeletons for initial content loading', (relativePath, minimumCount) => {
    const source = readFileSync(resolve(process.cwd(), 'src', 'features', relativePath), 'utf8')
    const skeletonCount = source.match(/<LoadingSkeleton\b/g)?.length ?? 0

    expect(source).toContain("from '../../shared/AsyncState'")
    expect(skeletonCount).toBeGreaterThanOrEqual(minimumCount)
  })

  it('does not restore the replaced bare table and form loading messages', () => {
    const source = loadingFeatures
      .map(([relativePath]) => readFileSync(resolve(process.cwd(), 'src', 'features', relativePath), 'utf8'))
      .join('\n')

    removedBareLoadingMessages.forEach((message) => expect(source).not.toContain(message))
  })

  it('keeps the shared skeleton accessible and motion-safe', () => {
    const component = readFileSync(resolve(process.cwd(), 'src', 'shared', 'AsyncState.tsx'), 'utf8')
    const css = readFileSync(resolve(process.cwd(), 'src', 'App.css'), 'utf8')

    expect(component).toContain('role="status"')
    expect(component).toContain('aria-live="polite"')
    expect(component).toContain('aria-hidden="true"')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
  })

  it('loads workspace sections on demand behind the shared skeleton', () => {
    const workspace = readFileSync(resolve(process.cwd(), 'src', 'features', 'workspace', 'Workspace.tsx'), 'utf8')
    const loader = readFileSync(resolve(process.cwd(), 'src', 'features', 'workspace', 'workspaceSectionLoader.ts'), 'utf8')
    const appShell = readFileSync(resolve(process.cwd(), 'src', 'features', 'workspace', 'AppShell.tsx'), 'utf8')
    const lazySectionModules = [
      ['loadPasswordPanel', 'settings/PasswordPanel'],
      ['loadFundsPanel', 'funds/FundsPanel'],
      ['loadImportPanel', 'import/ImportPanel'],
      ['loadMeterReadingsPanel', 'meterReadings/MeterReadingsPanel'],
      ['loadAuditPanel', 'audit/AuditPanel'],
      ['loadReportPanel', 'reports/ReportPanel'],
      ['loadUserManagementPanel', 'users/UserManagementPanel'],
      ['loadDictionaryPanel', 'dictionaries/DictionaryPanel'],
      ['loadTariffsPanel', 'tariffs/TariffsAndFeesPanel'],
      ['loadContractorsPanel', 'contractors/ContractorsPanel'],
      ['loadFinancePanel', 'finance/FinancePanel'],
      ['loadReleasePanel', 'releases/ReleasePanel'],
    ]

    expect(workspace).toContain("import { Suspense, useState } from 'react'")
    expect(workspace).toContain('<Suspense fallback={<LoadingSkeleton label="Загружаем раздел" rows={5} columns={4} />}>')
    expect(workspace).toContain('<AsyncErrorBoundary')
    expect(workspace).toContain('onPointerEnter={() => preloadWorkspaceSection(tile.section)}')
    expect(workspace).toContain('onFocus={() => preloadWorkspaceSection(tile.section)}')
    expect(appShell).toContain('onPointerEnter={() => preloadWorkspaceSection(item.section)}')
    expect(appShell).toContain('onFocus={() => preloadWorkspaceSection(item.section)}')
    for (const [loaderName, modulePath] of lazySectionModules) {
      expect(loader).toContain(`const ${loaderName} = createRetryableLazyLoader(() => import('../${modulePath}')`)
      expect(loader).toContain(`lazy(${loaderName})`)
      expect(loader).not.toMatch(new RegExp(`^import .* from '../${modulePath.replace('/', '\\/')}'`, 'm'))
    }
  })
})
