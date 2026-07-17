// @vitest-environment node
import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

describe('static assets', () => {
  it('ships the favicon referenced by index.html', () => {
    const faviconPath = resolve(process.cwd(), 'public', 'favicon.svg')

    expect(existsSync(faviconPath)).toBe(true)
    expect(readFileSync(faviconPath, 'utf8')).toContain('<svg')
  })
})
