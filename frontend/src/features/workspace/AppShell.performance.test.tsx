import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AuthResponse } from '../../services/authApi'
import { permissions } from '../../shared/accessControl'
import { intentPreloadDelayMs } from '../../shared/useIntentPreload'

const workspaceRenderSpy = vi.hoisted(() => vi.fn())
const workspacePropsSpy = vi.hoisted(() => vi.fn())

vi.mock('./Workspace', async () => {
  const { memo } = await import('react')
  return {
    Workspace: memo(function WorkspaceProbe(props: { activeSection: string; [key: string]: unknown }) {
      const { activeSection } = props
      workspaceRenderSpy(activeSection)
      workspacePropsSpy(props)
      return <div role="region" aria-label={`workspace-${activeSection}`} />
    }),
  }
})

vi.mock('./workspaceSectionLoader', () => ({
  preloadWorkspaceSection: vi.fn(),
}))

import { AuthenticatedAppShell } from './AppShell'
import { preloadWorkspaceSection } from './workspaceSectionLoader'

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
  const settingsClient = {
    getActionCommentSettings: () => new Promise<never>(() => undefined),
    updateActionCommentSettings: async (_accessToken: string, request: { required: boolean; version: string }) => request,
  } as never
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
      settingsClient={settingsClient}
      userClient={emptyClient}
      onLogout={vi.fn()}
    />,
  )
}

describe('AuthenticatedAppShell performance', () => {
  afterEach(() => {
    workspaceRenderSpy.mockReset()
    workspacePropsSpy.mockReset()
    vi.mocked(preloadWorkspaceSection).mockReset()
    vi.useRealTimers()
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

  it('preloads sidebar sections only after pointer intent and immediately on focus', () => {
    vi.useFakeTimers()
    renderShell()
    const usersButton = screen.getByRole('button', { name: 'Пользователи' })

    fireEvent.pointerEnter(usersButton)
    act(() => vi.advanceTimersByTime(intentPreloadDelayMs - 1))
    expect(preloadWorkspaceSection).not.toHaveBeenCalled()
    fireEvent.pointerLeave(usersButton)
    act(() => vi.runAllTimers())
    expect(preloadWorkspaceSection).not.toHaveBeenCalled()

    fireEvent.pointerEnter(usersButton)
    act(() => vi.advanceTimersByTime(intentPreloadDelayMs))
    expect(preloadWorkspaceSection).toHaveBeenCalledWith('users')

    const tariffsButton = screen.getByRole('button', { name: 'Тарифы и сборы' })
    fireEvent.pointerEnter(tariffsButton)
    fireEvent.focus(tariffsButton)
    expect(preloadWorkspaceSection).toHaveBeenCalledWith('tariffsAndFees')
    act(() => vi.runAllTimers())
    expect(preloadWorkspaceSection).toHaveBeenCalledTimes(2)
  })

  it('provides production API clients when callers do not inject test clients', async () => {
    render(<AuthenticatedAppShell auth={auth} authClient={{} as never} onLogout={vi.fn()} />)
    await act(async () => undefined)

    const props = workspacePropsSpy.mock.calls[0]?.[0] as Record<string, unknown>
    for (const clientName of ['auditClient', 'dictionaryClient', 'financeClient', 'fundsClient', 'importClient', 'integrationClient', 'reportClient', 'releaseClient', 'settingsClient', 'userClient']) {
      expect(props[clientName]).toBeTruthy()
    }
  })
})

function withinNavigationButtons(navigation: HTMLElement) {
  return [...navigation.querySelectorAll('button')]
}
