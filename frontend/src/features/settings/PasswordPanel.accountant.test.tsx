import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AuthClient, AuthResponse } from '../../services/authApi'
import type { IntegrationClient } from '../../services/integrationsApi'
import type { ApplicationSettingsClient, CashBankBalanceSettingsDto } from '../../services/settingsApi'
import { permissions } from '../../shared/accessControl'
import { PasswordPanel } from './PasswordPanel'

const admin: AuthResponse = {
  accessToken: 'token', expiresAtUtc: '2030-01-01T00:00:00Z',
  user: { id: 'test-user', email: 'staff@example.test', displayName: 'Сотрудник', roles: ['administrator'], permissions: Object.values(permissions) },
}

describe('accountant settings permission changes', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
    window.sessionStorage.clear()
  })

  it('cancels a cash read and discards its stale result when financial access is removed', async () => {
    const user = userEvent.setup()
    let signal: AbortSignal | undefined
    let complete: (value: CashBankBalanceSettingsDto) => void = () => undefined
    const settingsClient = {
      getCashBankBalances: vi.fn((_token: string, pendingSignal?: AbortSignal) => {
        signal = pendingSignal
        return new Promise<CashBankBalanceSettingsDto>((resolve) => { complete = resolve })
      }),
    } as unknown as ApplicationSettingsClient
    const props = { authClient: {} as AuthClient, integrationClient: {} as IntegrationClient, settingsClient, onSessionRevoked: vi.fn() }
    const view = render(<PasswordPanel {...props} auth={admin} />)
    await user.click(screen.getByRole('tab', { name: 'Касса и счёт' }))
    await waitFor(() => expect(signal).toBeDefined())
    expect(signal?.aborted).toBe(false)
    view.rerender(<PasswordPanel {...props} auth={{ ...admin, user: { ...admin.user, roles: ['accountant'], permissions: [] } }} />)
    expect(signal?.aborted).toBe(true)
    expect(screen.getByRole('tab', { name: 'Безопасность' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.queryByRole('tab', { name: 'Касса и счёт' })).not.toBeInTheDocument()
    await act(async () => complete({ cashOpeningBalance: 0, bankOpeningBalance: 0, cashCurrentBalance: 99, bankCurrentBalance: 0, recentOperations: [] }))
    expect(screen.queryByLabelText('Текущие остатки')).not.toBeInTheDocument()
  })

  it('closes diagnostics and cancels the pending request when an administrator becomes an accountant', async () => {
    const user = userEvent.setup()
    let signal: AbortSignal | undefined
    const settingsClient = {
      getDiagnosticLogStatus: vi.fn((_token: string, pendingSignal?: AbortSignal) => {
        signal = pendingSignal
        return new Promise<never>(() => undefined)
      }),
    } as unknown as ApplicationSettingsClient
    const props = { authClient: {} as AuthClient, integrationClient: {} as IntegrationClient, settingsClient, onSessionRevoked: vi.fn() }
    const view = render(<PasswordPanel {...props} auth={admin} />)
    await user.click(screen.getByRole('tab', { name: 'Диагностика' }))
    await waitFor(() => expect(signal).toBeDefined())
    view.rerender(<PasswordPanel {...props} auth={{ ...admin, user: { ...admin.user, roles: ['accountant'] } }} />)
    expect(signal?.aborted).toBe(true)
    expect(screen.getByRole('tab', { name: 'Безопасность' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.queryByRole('tab', { name: 'Диагностика' })).not.toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Интеграции' })).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Диагностика ошибок приложения' })).not.toBeInTheDocument()
  })
})
