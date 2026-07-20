import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('index.html metadata', () => {
  const indexHtml = readFileSync(resolve(process.cwd(), 'index.html'), 'utf8')

  it('uses Russian document metadata for the production shell', () => {
    expect(indexHtml).toContain('<html lang="ru">')
    expect(indexHtml).toContain('<meta name="viewport" content="width=device-width, initial-scale=1.0" />')
    expect(indexHtml).toContain('<title>GarageBalance - учет ГСК</title>')
  })

  it('shows a branded connection state before the React bundle is ready', () => {
    expect(indexHtml).toContain('class="app-bootstrap-loader"')
    expect(indexHtml).toContain('role="status"')
    expect(indexHtml).toContain('Подключаем GarageBalance…')
    expect(indexHtml).toContain('Для работы GarageBalance необходимо включить JavaScript.')
  })
})
