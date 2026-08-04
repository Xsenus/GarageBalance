import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AuthResponse } from '../../services/authApi'
import { permissions } from '../../shared/accessControl'

const workspaceRenderSpy = vi.hoisted(() => vi.fn())

vi.mock('./Workspace', async () => {
  const { memo } = await import('react')
  return {
    Workspace: memo(function WorkspaceProbe({ activeSection }: { activeSection: string }) {
      workspaceRenderSpy(activeSection)
      return <div role="region" aria-label={`workspace-${activeSection}`} />
    }),
  }
})

vi.mock('./workspaceSectionLoader', () => ({
  preloadWorkspaceSection: vi.fn(),
}))

import { AuthenticatedAppShell } from './AppShell'

const auth: AuthResponse = {
  accessToken: 'token',
  expiresAtUtc: '2026-08-01T00:00:00Z',
  user: {
    id: 'admin-1',
    email: 'admin@example.test',
    displayName: 'Администратор',
    roles: ['administrator'],
    permissions: Object.values(permissions),
  },
}

function renderShell() {
  const emptyClient = {} as never
  return render(
    <AuthenticatedAppShell
      auth={auth}
      authClient={emptyClient}
      auditClient={emptyClient}
      dictionaryClient={emptyClient}
      financeClient={emptyClient}
      fundsClient={emptyClient}
      importClient={emptyClient}
      integrationClient={emptyClient}
      reportClient={emptyClient}
      releaseClient={emptyClient}
      settingsClient={emptyClient}
      userClient={emptyClient}
      onLogout={vi.fn()}
    />,
  )
}

describe('AuthenticatedAppShell performance', () => {
  afterEach(() => {
    workspaceRenderSpy.mockReset()
    window.localStorage.clear()
  })

  it('keeps the workspace memoized when only the sidebar state changes', () => {
    renderShell()

    expect(workspaceRenderSpy).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getByRole('button', { name: 'Развернуть панель' }))

    expect(screen.getByRole('button', { name: 'Свернуть панель' })).toBeInTheDocument()
    expect(workspaceRenderSpy).toHaveBeenCalledTimes(1)
  })

  it('opens every section and commits only the final section during a rapid navigation burst', () => {
    renderShell()
    const navigation = screen.getByRole('navigation', { name: 'Основные разделы' })
    const sectionLabels = [
      'Пользователи',
      'Тарифы и сборы',
      'Контрагенты',
      'Справочники',
      'Показания',
      'Платежи',
      'Фонды',
      'Отчеты',
      'Импорт',
      'История изменений',
      'Что нового',
      'Настройки',
    ]

    for (const label of sectionLabels) {
      fireEvent.click(screen.getByRole('button', { name: label }))
      expect(screen.getByRole('region', { name: /^workspace-/ })).toBeInTheDocument()
    }

    fireEvent.click(screen.getByRole('button', { name: 'Главное меню' }))
    expect(screen.getByRole('region', { name: 'workspace-dashboard' })).toBeInTheDocument()
    const renderCountBeforeBurst = workspaceRenderSpy.mock.calls.length
    act(() => {
      for (const label of sectionLabels) {
        fireEvent.click(screen.getByRole('button', { name: label }))
      }
    })

    expect(screen.getByRole('region', { name: 'workspace-settings' })).toBeInTheDocument()
    expect(workspaceRenderSpy.mock.calls.length - renderCountBeforeBurst).toBe(1)
    expect(withinNavigationButtons(navigation)).toHaveLength(13)
  })
})

function withinNavigationButtons(navigation: HTMLElement) {
  return [...navigation.querySelectorAll('button')]
}
