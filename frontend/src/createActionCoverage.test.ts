// @vitest-environment node
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

const createActionFeatures = [
  ['contractors/ContractorsPanel.tsx', 6],
  ['dictionaries/DictionaryPanel.tsx', 7],
  ['finance/FinancePanel.tsx', 5],
  ['funds/FundsPanel.tsx', 1],
  ['releases/ReleasePanel.tsx', 2],
  ['tariffs/TariffsAndFeesPanel.tsx', 4],
  ['users/UserManagementPanel.tsx', 1],
] as const

describe('shared create action coverage', () => {
  it.each(createActionFeatures)('%s uses the shared create-button interaction', (relativePath, minimumCount) => {
    const source = readFileSync(resolve(process.cwd(), 'src', 'features', relativePath), 'utf8')
    const sharedClassCount = source.match(/create-action-button/g)?.length ?? 0

    expect(sharedClassCount).toBeGreaterThanOrEqual(minimumCount)
  })

  it.each(createActionFeatures.filter(([relativePath]) => relativePath !== 'funds/FundsPanel.tsx'))('%s does not use a generic plus icon for creation', (relativePath) => {
    const source = readFileSync(resolve(process.cwd(), 'src', 'features', relativePath), 'utf8')

    expect(source).not.toMatch(/<Plus\b/)
  })

  it('keeps hover, keyboard focus, active and reduced-motion behavior in one shared style', () => {
    const css = readFileSync(resolve(process.cwd(), 'src', 'App.css'), 'utf8')

    expect(css).toContain('.create-action-button:hover:not(:disabled)')
    expect(css).toContain('.create-action-button:focus-visible')
    expect(css).toContain('.create-action-button:active:not(:disabled)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
  })
})
