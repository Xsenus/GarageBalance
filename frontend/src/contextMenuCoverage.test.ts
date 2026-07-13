import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

const contextMenuFeatures = [
  'contractors/ContractorsPanel.tsx',
  'dictionaries/DictionaryPanel.tsx',
  'finance/FinancePanel.tsx',
  'tariffs/TariffsAndFeesPanel.tsx',
  'users/UserManagementPanel.tsx',
] as const

describe('shared context menu coverage', () => {
  it.each(contextMenuFeatures)('%s groups every right-click menu', (relativePath) => {
    const source = readFileSync(resolve(process.cwd(), 'src', 'features', relativePath), 'utf8')
    const menuCount = source.match(/className="context-menu(?: [^"]*)?"/g)?.length ?? 0
    const groupCount = source.match(/className="context-menu-group"/g)?.length ?? 0

    expect(menuCount).toBeGreaterThan(0)
    expect(groupCount).toBeGreaterThanOrEqual(menuCount)
  })

  it('keeps contractor edit actions separate from financial navigation', () => {
    const source = readFileSync(resolve(process.cwd(), 'src', 'features', 'contractors', 'ContractorsPanel.tsx'), 'utf8')

    expect(source).toContain('className="context-menu-separator" role="separator"')
    expect(source).toContain('<span>Финансовый отчет</span>')
    expect(source).not.toContain('<span>Открыть финансовый отчет</span>')
  })

  it('keeps shared group, separator and right-aligned debt styles', () => {
    const appCss = readFileSync(resolve(process.cwd(), 'src', 'App.css'), 'utf8')
    const contractorsSource = readFileSync(resolve(process.cwd(), 'src', 'features', 'contractors', 'ContractorsPanel.tsx'), 'utf8')

    expect(appCss).toContain('.context-menu-group')
    expect(appCss).toContain('.context-menu-separator')
    expect(appCss).toContain('.contractors-directory-cell--right')
    expect(contractorsSource).toContain('`${formatMoney(overdueDebt)} руб.`')
  })
})
