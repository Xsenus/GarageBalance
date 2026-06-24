import { readFileSync, readdirSync } from 'node:fs'
import { join, resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('frontend API base URL', () => {
  it('uses same-origin API fallback for deployed builds', () => {
    const servicesDir = resolve(process.cwd(), 'src', 'services')
    const serviceFiles = readdirSync(servicesDir).filter((file) => file.endsWith('Api.ts'))

    expect(serviceFiles.length).toBeGreaterThan(0)

    for (const file of serviceFiles) {
      const content = readFileSync(join(servicesDir, file), 'utf8')

      expect(content).toContain("import.meta.env.VITE_API_BASE_URL ?? ''")
      expect(content).not.toContain('http://127.0.0.1:5080')
      expect(content).not.toContain('http://localhost:5080')
    }
  })
})
