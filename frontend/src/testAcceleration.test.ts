import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('frontend test acceleration', () => {
  const packageJson = JSON.parse(readFileSync(resolve(process.cwd(), 'package.json'), 'utf8')) as {
    scripts: Record<string, string>
  }
  const runner = readFileSync(resolve(process.cwd(), 'scripts', 'run-vitest.mjs'), 'utf8').replace(/\r\n/g, '\n')
  const workflow = readFileSync(resolve(process.cwd(), '..', '.github', 'workflows', 'deploy-staging.yml'), 'utf8')

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
    expect(runner).toContain("['it', 'test', 'describe']")
    expect(runner).toContain('Full App workflows are skipped.')
    expect(runner).toContain('Math.min(8, availableParallelism())')
    expect(runner).toContain('finally {\n  cleanupGeneratedShards()')
    expect(runner).toContain('--slowTestThreshold=1000')
  })
})
