import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { WorkspaceSectionErrorBoundary } from './Workspace'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

function BrokenSection(): never {
  throw new Error('chunk unavailable')
}

describe('WorkspaceSectionErrorBoundary', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows a recoverable error instead of a blank workspace', () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined)

    render(
      <WorkspaceSectionErrorBoundary>
        <BrokenSection />
      </WorkspaceSectionErrorBoundary>,
    )

    expect(screen.getByRole('alert', { name: 'Не удалось загрузить раздел' })).toHaveTextContent('Проверьте соединение')
    expect(screen.getByRole('button', { name: 'Перезагрузить страницу' })).toBeInTheDocument()
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
