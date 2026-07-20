import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('index.html metadata', () => {
  const indexHtml = readFileSync(resolve(process.cwd(), 'index.html'), 'utf8')
  const mainSource = readFileSync(resolve(process.cwd(), 'src/main.tsx'), 'utf8')

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

  it('replaces an endless bootstrap spinner with a retryable error', () => {
    expect(indexHtml).toContain("window.setTimeout(showBootstrapError, 20000)")
    expect(indexHtml).toContain("window.addEventListener('error'")
    expect(indexHtml).toContain("window.addEventListener('unhandledrejection'")
    expect(indexHtml).toContain("window.addEventListener('garagebalance:bootstrap-ready'")
    expect(indexHtml).toContain('Не удалось загрузить GarageBalance')
    expect(indexHtml).toContain('Повторить загрузку')
    expect(indexHtml).toContain('window.location.reload()')
    expect(mainSource).toContain("window.dispatchEvent(new Event('garagebalance:bootstrap-ready'))")
  })
})
