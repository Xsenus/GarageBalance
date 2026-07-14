import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('frontend test acceleration', () => {
  const packageJson = JSON.parse(readFileSync(resolve(process.cwd(), 'package.json'), 'utf8')) as {
    scripts: Record<string, string>
  }
  const runner = readFileSync(resolve(process.cwd(), 'scripts', 'run-vitest.mjs'), 'utf8')
  const workflow = readFileSync(resolve(process.cwd(), '..', '.github', 'workflows', 'deploy-staging.yml'), 'utf8')

  it('uses the sharded runner locally and in CI', () => {
    expect(packageJson.scripts.test).toBe('node scripts/run-vitest.mjs')
    expect(packageJson.scripts['test:ci']).toBe('node scripts/run-vitest.mjs --ci')
    expect(workflow).toContain('run: npm run test:ci')
  })

  it('keeps serial diagnostics and related-test development commands available', () => {
    expect(packageJson.scripts['test:serial']).toContain('--maxWorkers=1')
    expect(packageJson.scripts['test:related']).toBe('node scripts/run-vitest.mjs --related')
  })

  it('balances the monolithic App scenarios and always removes generated shards', () => {
    expect(runner).toContain('balanceTests(testStatements, shardCount)')
    expect(runner).toContain("['it', 'test', 'describe']")
    expect(runner).toContain('finally {\n  cleanupGeneratedShards()')
    expect(runner).toContain('--slowTestThreshold=1000')
  })
})
