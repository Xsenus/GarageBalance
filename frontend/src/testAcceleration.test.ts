// @vitest-environment node
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'
import { nodeTestFiles } from '../scripts/test-environments.mjs'
import { chooseTestParallelism } from '../scripts/test-parallelism.mjs'

describe('frontend test acceleration', () => {
  const packageJson = JSON.parse(readFileSync(resolve(process.cwd(), 'package.json'), 'utf8')) as {
    scripts: Record<string, string>
  }
  const runner = readFileSync(resolve(process.cwd(), 'scripts', 'run-vitest.mjs'), 'utf8').replace(/\r\n/g, '\n')
  const setup = readFileSync(resolve(process.cwd(), 'src', 'test', 'setup.ts'), 'utf8').replace(/\r\n/g, '\n')
  const workflow = readFileSync(resolve(process.cwd(), '..', '.github', 'workflows', 'deploy-staging.yml'), 'utf8')

  it('runs pure tests in Node without the heavier jsdom environment', () => {
    expect(nodeTestFiles).toContain('src/services/financeApi.test.ts')
    expect(nodeTestFiles).toContain('src/shared/validation.test.ts')
    expect(new Set(nodeTestFiles).size).toBe(nodeTestFiles.length)

    for (const testFile of nodeTestFiles) {
      expect(testFile).toMatch(/^src\/.+\.test\.ts$/)
      const source = readFileSync(resolve(process.cwd(), testFile), 'utf8')
      expect(source.startsWith('// @vitest-environment node')).toBe(true)
      if (testFile === 'src/testAcceleration.test.ts') continue

      expect(source).not.toMatch(
        /@testing-library\/react|\bdocument\b|\bwindow\b|localStorage|sessionStorage|\.to(?:BeInTheDocument|HaveClass|HaveAttribute|BeDisabled|BeEnabled|HaveValue|HaveTextContent|BeVisible|BeChecked|HaveAccessibleName|HaveAccessibleDescription)\b/,
      )
    }
  })

  it('loads DOM matchers only for browser tests', () => {
    expect(setup).toContain("if (typeof document !== 'undefined')")
    expect(setup).toContain("await import('@testing-library/jest-dom/vitest')")
    expect(setup).toContain("await import('@testing-library/dom')")
    expect(setup).not.toMatch(/^import .*@testing-library/m)
  })

  it('uses the sharded runner locally and enforces coverage in CI', () => {
    expect(packageJson.scripts.test).toBe('node scripts/run-vitest.mjs')
    expect(packageJson.scripts['test:ci']).toBe('node scripts/run-vitest.mjs --ci')
    expect(packageJson.scripts['test:coverage']).toBe('node scripts/run-vitest.mjs --ci --coverage.enabled=true')
    expect(workflow).toContain('run: npm run test:coverage')
  })

  it('keeps serial diagnostics and related-test development commands available', () => {
    expect(packageJson.scripts['test:dev']).toBe('node scripts/run-vitest.mjs --quick')
    expect(packageJson.scripts['test:dev:related']).toBe('node scripts/run-vitest.mjs --quick --related')
    expect(packageJson.scripts['test:serial']).toContain('--maxWorkers=1')
    expect(packageJson.scripts['test:related']).toBe('node scripts/run-vitest.mjs --related')
    expect(packageJson.scripts['test:watch']).toContain('--exclude src/App.test.tsx')
    expect(packageJson.scripts['test:watch:full']).toBe('vitest')
  })

  it('balances the monolithic App scenarios and always removes generated shards', () => {
    expect(runner).toContain('balanceTests(testStatements, shardCount)')
    expect(runner).toContain('chooseTestParallelism(availableParallelism())')
    expect(runner).toContain('VITEST_APP_SHARDS, workerCount')
    expect(runner).toContain("['it', 'test', 'describe']")
    expect(runner).toContain('Full App workflows are skipped.')
    expect(runner).toContain('Math.min(8, availableParallelism())')
    expect(runner).toContain("'--pool=threads',")
    expect(runner).toContain('finally {\n  cleanupGeneratedShards()')
    expect(runner).toContain('--slowTestThreshold=1000')
  })

  it.each([
    [1, 1],
    [2, 2],
    [3, 3],
    [4, 4],
    [5, 5],
    [6, 6],
    [8, 6],
  ])('uses %i workers as a safe parallelism budget of %i', (availableWorkers, expectedWorkers) => {
    expect(chooseTestParallelism(availableWorkers)).toBe(expectedWorkers)
  })

  it('falls back to one worker for an invalid CPU budget', () => {
    expect(chooseTestParallelism(0)).toBe(1)
    expect(chooseTestParallelism(Number.NaN)).toBe(1)
  })
})
