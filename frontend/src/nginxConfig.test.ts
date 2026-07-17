// @vitest-environment node
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('frontend nginx config', () => {
  const nginxConfig = readFileSync(resolve(process.cwd(), 'nginx.conf'), 'utf8')

  it('serves the SPA shell without stale index.html caching', () => {
    expect(nginxConfig).toContain('location = /index.html')
    expect(nginxConfig).toContain('etag off;')
    expect(nginxConfig).toContain('if_modified_since off;')
    expect(nginxConfig).toContain('Cache-Control "no-store, no-cache, must-revalidate, max-age=0" always')
    expect(nginxConfig).toContain('try_files $uri $uri/ /index.html;')
  })

  it('keeps hashed assets cacheable for production performance', () => {
    expect(nginxConfig).toContain('location /assets/')
    expect(nginxConfig).toContain('expires 30d;')
    expect(nginxConfig).toContain('Cache-Control "public, max-age=2592000, immutable"')
  })
})
