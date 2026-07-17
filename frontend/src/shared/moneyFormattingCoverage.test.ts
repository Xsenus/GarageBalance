// @vitest-environment node
import { readFileSync, readdirSync } from 'node:fs'
import { extname, join, relative, resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

function collectSourceFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) {
      return collectSourceFiles(path)
    }

    return ['.ts', '.tsx'].includes(extname(entry.name)) && !entry.name.includes('.test.') ? [path] : []
  })
}

const sourceRoot = resolve(process.cwd(), 'src')
const sources = collectSourceFiles(sourceRoot).map((path) => ({
  path: relative(sourceRoot, path).replace(/\\/g, '/'),
  source: readFileSync(path, 'utf8'),
}))

describe('unified money formatting coverage', () => {
  it('keeps decimal money inputs inside the shared control', () => {
    const offenders = sources
      .filter(({ path }) => !['shared/MoneyInput.tsx', 'features/meterReadings/MeterReadingsPanel.tsx'].includes(path))
      .filter(({ source }) => /inputMode="decimal"|step="0\.01"/.test(source))
      .map(({ path }) => path)

    expect(offenders).toEqual([])
  })

  it('keeps two-decimal grouping implementation in one shared formatter', () => {
    const formatterOwners = sources
      .filter(({ source }) => /minimumFractionDigits:\s*2/.test(source))
      .map(({ path }) => path)

    expect(formatterOwners).toEqual(['shared/moneyInputFormatting.ts'])
  })

  it('covers every working section that accepts money', () => {
    const expectedControls: Record<string, string> = {
      'features/contractors/ContractorsPanel.tsx': 'MoneyTextInput',
      'features/dictionaries/DictionaryPanel.tsx': 'MoneyInput',
      'features/finance/FinancePanel.tsx': 'MoneyTextInput',
      'features/funds/FundsPanel.tsx': 'MoneyTextInput',
      'features/tariffs/TariffsAndFeesPanel.tsx': 'MoneyTextInput',
    }

    Object.entries(expectedControls).forEach(([path, control]) => {
      expect(sources.find((source) => source.path === path)?.source).toContain(`<${control}`)
    })
  })
})
