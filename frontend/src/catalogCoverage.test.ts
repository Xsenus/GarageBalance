// @vitest-environment node
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

import { catalogCoverageEntries, profileCatalogEntries } from './shared/catalogCoverage'

describe('dictionary API interface coverage', () => {
  const controllerSource = readFileSync(
    resolve(process.cwd(), '..', 'backend', 'GarageBalance.Api', 'Controllers', 'DictionariesController.cs'),
    'utf8',
  )
  const dictionaryPanelSource = readFileSync(
    resolve(process.cwd(), 'src', 'features', 'dictionaries', 'DictionaryPanel.tsx'),
    'utf8',
  )
  const workspaceSource = readFileSync(
    resolve(process.cwd(), 'src', 'features', 'workspace', 'Workspace.tsx'),
    'utf8',
  )

  it('maps every DictionariesController resource family to a visible workspace', () => {
    const controllerRoutes = Array.from(
      controllerSource.matchAll(/\[Http(?:Get|Post|Put|Delete)\("([a-z-]+)(?:\/[^"{]+|\/{[^"]+)?"\)\]/g),
      (match) => match[1],
    )
    const uniqueControllerRoutes = [...new Set(controllerRoutes)].sort()
    const coveredRoutes = catalogCoverageEntries.map((entry) => entry.apiRoute).sort()

    expect(uniqueControllerRoutes).toEqual(coveredRoutes)
    expect(catalogCoverageEntries.every((entry) => entry.workspaceLabel.trim().length > 0)).toBe(true)
  })

  it('keeps reusable master data either directly editable or linked to its profile editor', () => {
    const masterDataEntries = catalogCoverageEntries.filter((entry) => entry.kind === 'master-data')

    expect(masterDataEntries.every((entry) => entry.workspaceSection !== 'dictionaries' || Boolean(entry.dictionarySection))).toBe(true)
    expect(profileCatalogEntries.map((entry) => entry.apiRoute)).toEqual([
      'supplier-groups',
      'suppliers',
      'supplier-contacts',
      'staff-departments',
      'staff-members',
      'tariffs',
      'charge-services',
      'irregular-payments',
    ])
    expect(dictionaryPanelSource).toContain('profileCatalogEntries.map((entry) => (')
    expect(dictionaryPanelSource).toContain('onOpenWorkspaceSection?.(')
    expect(dictionaryPanelSource).toContain("contractorTarget: { section: entry.apiRoute.startsWith('staff-') ? 'staff' : 'suppliers' }")
    expect(workspaceSource).toContain('onOpenWorkspaceSection={onOpenSection}')
  })

  it('classifies campaigns as an operational register instead of duplicating them as a dictionary', () => {
    expect(catalogCoverageEntries.find((entry) => entry.apiRoute === 'fee-campaigns')).toMatchObject({
      kind: 'operation-register',
      workspaceSection: 'tariffsAndFees',
    })
  })
})
