import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { WorkspaceSectionErrorBoundary } from './Workspace'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

function BrokenSection(): never {
  throw new Error('chunk unavailable')
}

function StaleChunkSection(): never {
  throw new TypeError('Failed to fetch dynamically imported module: /assets/FinancePanel-old.js')
}

describe('WorkspaceSectionErrorBoundary', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    window.sessionStorage.clear()
  })

  it('explains a stale application version and offers an explicit update', () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined)
    window.sessionStorage.setItem('garagebalance.lazy-chunk-reload-at', String(Date.now()))

    render(
      <WorkspaceSectionErrorBoundary onReturn={vi.fn()}>
        <StaleChunkSection />
      </WorkspaceSectionErrorBoundary>,
    )

    const alert = screen.getByRole('alert', { name: 'Не удалось загрузить раздел' })
    expect(alert).toHaveTextContent('Приложение было обновлено')
    expect(alert).toHaveTextContent('Открыта предыдущая версия страницы')
    expect(screen.getByRole('button', { name: 'Обновить приложение' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'На главную' })).toBeInTheDocument()
  })

  it('shows a recoverable error instead of a blank workspace', () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined)

    render(
      <WorkspaceSectionErrorBoundary>
        <BrokenSection />
      </WorkspaceSectionErrorBoundary>,
    )

    expect(screen.getByRole('alert', { name: 'Не удалось загрузить раздел' })).toHaveTextContent('Возникла временная ошибка')
    expect(screen.getByRole('button', { name: 'Повторить загрузку' })).toBeInTheDocument()
  })

  it('keeps heavy sections lazy and preloads dashboard intent', () => {
    const source = readFileSync(resolve(process.cwd(), 'src', 'features', 'workspace', 'Workspace.tsx'), 'utf8')

    const loader = readFileSync(resolve(process.cwd(), 'src', 'features', 'workspace', 'workspaceSectionLoader.ts'), 'utf8')

    expect(loader.match(/export const \w+(?:Panel|PanelV2) = lazy\(/g)?.length).toBe(12)
    expect(loader).toContain('workspaceSectionPreloaders')
    expect(loader).toContain('createRetryableLazyLoader')
    expect(source).toContain('onFocus={() => preloadWorkspaceSection(tile.section)}')
    expect(source).toContain('onPointerEnter={() => preloadWorkspaceSection(tile.section)}')
    expect(source).toContain('fallback={<TableLoadingState label="Загружаем выбранный раздел" />}')
  })
})
