import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('frontend bundle budget gate', () => {
  const packageJson = JSON.parse(readFileSync(resolve(process.cwd(), 'package.json'), 'utf8')) as {
    scripts: Record<string, string>
  }
  const budgetScript = readFileSync(resolve(process.cwd(), 'scripts', 'check-bundle-budget.mjs'), 'utf8')

  it('exposes an npm script that can run after production build', () => {
    expect(packageJson.scripts['check:bundle']).toBe('node scripts/check-bundle-budget.mjs')
    expect(budgetScript).toContain('Run npm run build first')
  })

  it('keeps explicit gzip budgets for JS, CSS and total production assets', () => {
    expect(budgetScript).toContain('mainJsGzipBytes: 180 * 1024')
    expect(budgetScript).toContain('mainCssGzipBytes: 40 * 1024')
    expect(budgetScript).toContain('totalAssetsGzipBytes: 260 * 1024')
    expect(budgetScript).toContain('gzipSync')
  })
})
