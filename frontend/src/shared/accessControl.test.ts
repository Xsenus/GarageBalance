// @vitest-environment node
import { describe, expect, it } from 'vitest'
import type { AuthResponse } from '../services/authApi'
import { hasAnyPermission, hasPermission, isAdministrator, permissions, rolePermissionGroups } from './accessControl'

function createAuthResponse(permissionList: string[]): AuthResponse {
  return {
    accessToken: 'token',
    expiresAtUtc: '2030-01-01T00:00:00Z',
    user: {
      id: 'user-1',
      email: 'admin@example.com',
      displayName: 'Администратор',
      roles: ['administrator'],
      permissions: permissionList,
    },
  }
}

describe('accessControl', () => {
  it('checks a single permission', () => {
    const auth = createAuthResponse([permissions.paymentsRead])

    expect(hasPermission(auth, permissions.paymentsRead)).toBe(true)
    expect(hasPermission(auth, permissions.paymentsWrite)).toBe(false)
  })

  it('checks optional any-permission requirements', () => {
    const auth = createAuthResponse([permissions.reportsRead])

    expect(hasAnyPermission(auth)).toBe(true)
    expect(hasAnyPermission(auth, [])).toBe(false)
    expect(hasAnyPermission(auth, [permissions.usersManage, permissions.reportsRead])).toBe(true)
    expect(hasAnyPermission(auth, [permissions.usersManage, permissions.auditRead])).toBe(false)
  })

  it('checks the administrator role independently from granted permissions', () => {
    expect(isAdministrator(createAuthResponse([]))).toBe(true)
    expect(isAdministrator({
      ...createAuthResponse([permissions.usersManage]),
      user: { ...createAuthResponse([permissions.usersManage]).user, roles: ['operator'] },
    })).toBe(false)
  })

  it('keeps role permission matrix labels tied to known permissions', () => {
    expect(rolePermissionGroups).toEqual([
      { label: 'Пользователи', permission: permissions.usersManage },
      { label: 'Справочники', permission: permissions.dictionariesWrite },
      { label: 'Тарифы', permission: permissions.tariffsManage },
      { label: 'Платежи', permission: permissions.paymentsWrite },
      { label: 'Исторические показания', permission: permissions.historicalMeterReadingsCorrect },
      { label: 'Отчеты', permission: permissions.reportsRead },
      { label: 'Импорт', permission: permissions.importRun },
      { label: 'История изменений', permission: permissions.auditRead },
      { label: 'Что нового', permission: permissions.appReleasesManage },
    ])
  })
})
