import { render, screen, waitFor } from '@testing-library/react'
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
    expect(screen.getByRole('alert', { name: 'Не удалось загрузить раздел' })).toHaveTextContent(/Код ошибки: .+Сообщите его администратору/)
    expect(screen.getByRole('button', { name: 'Перезагрузить страницу' })).toBeInTheDocument()
  })

  it('reports the same visible error code to the protected diagnostic endpoint', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 202 }))
    vi.stubGlobal('fetch', fetchMock)

    render(
      <WorkspaceSectionErrorBoundary accessToken="access-token">
        <BrokenSection />
      </WorkspaceSectionErrorBoundary>,
    )

    const alert = screen.getByRole('alert', { name: 'Не удалось загрузить раздел' })
    const visibleErrorId = alert.querySelector('strong')?.textContent
    expect(visibleErrorId).toBeTruthy()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    const request = fetchMock.mock.calls[0]
    const body = JSON.parse(String(request[1]?.body))
    expect(request[0]).toBe('/api/diagnostics/client-errors')
    expect(request[1]?.headers).toEqual({ 'Content-Type': 'application/json', Authorization: 'Bearer access-token' })
    expect(body).toEqual(expect.objectContaining({
      clientErrorId: visibleErrorId,
      errorName: 'Error',
      message: 'Ошибка интерфейса; подробности определяются по коду и стеку вызовов.',
    }))
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
