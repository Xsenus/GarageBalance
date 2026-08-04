import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('index.html metadata', () => {
  const indexHtml = readFileSync(resolve(process.cwd(), 'index.html'), 'utf8')
  const mainSource = readFileSync(resolve(process.cwd(), 'src/main.tsx'), 'utf8')
  const bootstrapSource = readFileSync(resolve(process.cwd(), 'public/bootstrap.js'), 'utf8')
  const bootstrapStyles = readFileSync(resolve(process.cwd(), 'public/bootstrap.css'), 'utf8')

  it('uses Russian document metadata for the production shell', () => {
    expect(indexHtml).toContain('<html lang="ru">')
    expect(indexHtml).toContain('<meta name="viewport" content="width=device-width, initial-scale=1.0" />')
    expect(indexHtml).toContain('<title>GarageBalance - учет ГСК</title>')
  })

  it('shows a branded connection state before the React bundle is ready', () => {
    expect(indexHtml).toContain('<link rel="stylesheet" href="/bootstrap.css" />')
    expect(indexHtml).toContain('<script src="/bootstrap.js"></script>')
    expect(indexHtml).not.toContain('<style>')
    expect(indexHtml).not.toContain('<script>')
    expect(bootstrapStyles).toContain('.app-bootstrap-loader')
    expect(indexHtml).toContain('class="app-bootstrap-loader"')
    expect(indexHtml).toContain('role="status"')
    expect(indexHtml).toContain('Подключаем GarageBalance…')
    expect(indexHtml).toContain('Для работы GarageBalance необходимо включить JavaScript.')
  })

  it('replaces an endless bootstrap spinner with a retryable error', () => {
    expect(bootstrapSource).toContain("window.setTimeout(showBootstrapError, 20000)")
    expect(bootstrapSource).toContain("window.addEventListener('error'")
    expect(bootstrapSource).toContain("window.addEventListener('unhandledrejection'")
    expect(bootstrapSource).toContain("window.addEventListener('garagebalance:bootstrap-ready'")
    expect(bootstrapSource).toContain('Не удалось загрузить GarageBalance')
    expect(bootstrapSource).toContain('Повторить загрузку')
    expect(bootstrapSource).toContain("nextUrl.searchParams.set('_gb_reload'")
    expect(bootstrapSource).toContain('window.location.replace(nextUrl)')
    expect(mainSource).toContain('<AppBootstrapBoundary>')
    expect(mainSource).toContain('<AppBootstrapReady>')
    expect(mainSource).not.toContain("window.dispatchEvent(new Event('garagebalance:bootstrap-ready'))")
  })
})
