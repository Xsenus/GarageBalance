// @vitest-environment node
import { describe, expect, it } from 'vitest'
import type { AuthResponse } from '../services/authApi'
import { permissions } from './accessControl'
import { canAccessWorkspaceSection } from './workspaceNavigation'
import type { WorkspaceSection } from './workspaceNavigation'

describe('workspace navigation access', () => {
  it.each<WorkspaceSection>(['dashboard', 'releases', 'settings'])('keeps %s available without a dedicated permission', (section) => {
    expect(canAccessWorkspaceSection(createAuthResponse([]), section)).toBe(true)
  })

  it.each<[WorkspaceSection, string]>([
    ['users', permissions.usersManage],
    ['contractors', permissions.dictionariesRead],
    ['tariffsAndFees', permissions.dictionariesRead],
    ['dictionaries', permissions.dictionariesRead],
    ['meterReadings', permissions.paymentsRead],
    ['funds', permissions.reportsRead],
    ['import', permissions.importRun],
    ['audit', permissions.auditRead],
  ])('requires the section permission for %s', (section, permission) => {
    expect(canAccessWorkspaceSection(createAuthResponse([]), section)).toBe(false)
    expect(canAccessWorkspaceSection(createAuthResponse([permissions.appReleasesManage]), section)).toBe(false)
    expect(canAccessWorkspaceSection(createAuthResponse([permission]), section)).toBe(true)
  })

  it.each<WorkspaceSection>(['payments', 'reports'])('requires both financial and dictionary access for %s', (section) => {
    const financialPermission = section === 'payments' ? permissions.paymentsRead : permissions.reportsRead

    expect(canAccessWorkspaceSection(createAuthResponse([financialPermission]), section)).toBe(false)
    expect(canAccessWorkspaceSection(createAuthResponse([permissions.dictionariesRead]), section)).toBe(false)
    expect(canAccessWorkspaceSection(createAuthResponse([financialPermission, permissions.dictionariesRead]), section)).toBe(true)
  })
})

function createAuthResponse(permissionList: string[]): AuthResponse {
  return {
    accessToken: 'token',
    expiresAtUtc: '2030-01-01T00:00:00Z',
    user: {
      id: 'user-1',
      email: 'user@example.com',
      displayName: 'Пользователь',
      roles: ['operator'],
      permissions: permissionList,
    },
  }
}
