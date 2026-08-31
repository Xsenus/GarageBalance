// @vitest-environment node
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('frontend bundle budget gate', () => {
  const packageJson = JSON.parse(readFileSync(resolve(process.cwd(), 'package.json'), 'utf8')) as {
    scripts: Record<string, string>
  }
  const budgetScript = readFileSync(resolve(process.cwd(), 'scripts', 'check-bundle-budget.mjs'), 'utf8')
  const viteConfig = readFileSync(resolve(process.cwd(), 'vite.config.ts'), 'utf8')
  const appSource = readFileSync(resolve(process.cwd(), 'src', 'App.tsx'), 'utf8')

  it('exposes an npm script that can run after production build', () => {
    expect(packageJson.scripts['check:bundle']).toBe('node scripts/check-bundle-budget.mjs')
    expect(budgetScript).toContain('Run npm run build first')
  })

  it('keeps explicit gzip budgets for initial JS, largest assets and the total production bundle', () => {
    expect(budgetScript).toContain('mainJsGzipBytes: 180 * 1024')
    expect(budgetScript).toContain('initialJsGzipBytes: 110 * 1024')
    expect(budgetScript).toContain('mainCssGzipBytes: 40 * 1024')
    expect(budgetScript).toContain('totalAssetsGzipBytes: 267 * 1024')
    expect(budgetScript).toContain('gzipSync')
    expect(budgetScript).toContain('collectStaticJsGraph')
    expect(budgetScript).toContain('remaining ${limit - bytes} bytes')
  })

  it('groups the compact release panel with reporting instead of creating a separate tiny chunk', () => {
    expect(viteConfig).toContain('releases[/\\\\]ReleasePanel')
    expect(viteConfig).toContain("return 'reporting'")
  })

  it('keeps independently opened accounting screens in separate lazy chunks', () => {
    expect(viteConfig).toContain('modulePreload: false')
    expect(viteConfig).toContain("return 'financial-operations'")
    expect(viteConfig).toContain("return 'funds'")
    expect(viteConfig).toContain("return 'contractors'")
    expect(viteConfig).toContain("return 'tariffs'")
  })

  it('loads the authenticated workspace only after authentication', () => {
    expect(appSource).toContain("import('./features/workspace/AppShell')")
    expect(appSource).not.toContain("import { AuthenticatedAppShell } from './features/workspace/AppShell'")
  })
})
