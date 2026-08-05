// @vitest-environment node
import { describe, expect, it } from 'vitest'
import type { AuthResponse } from '../services/authApi'
import { expandPermissionDependencies, hasAnyPermission, hasPermission, isAdministrator, isPermissionRequiredBySelection, permissions, rolePermissionGroups } from './accessControl'

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

  it('adds transitive read permissions and identifies locked dependencies', () => {
    expect(expandPermissionDependencies([
      permissions.historicalMeterReadingsCorrect,
      permissions.openingDataAdjust,
      permissions.reportsRead,
    ])).toEqual([
      permissions.dictionariesRead,
      permissions.dictionariesWrite,
      permissions.openingDataAdjust,
      permissions.historicalMeterReadingsCorrect,
      permissions.paymentsRead,
      permissions.paymentsWrite,
      permissions.reportsRead,
    ])
    expect(isPermissionRequiredBySelection(permissions.paymentsRead, [permissions.historicalMeterReadingsCorrect])).toBe(true)
    expect(isPermissionRequiredBySelection(permissions.auditRead, [permissions.historicalMeterReadingsCorrect])).toBe(false)
  })

  it('keeps role permission matrix labels tied to known permissions', () => {
    expect(rolePermissionGroups).toEqual([
      { label: 'Пользователи', permission: permissions.usersManage },
      { label: 'Справочники', permission: permissions.dictionariesWrite },
      { label: 'Тарифы и сборы', permission: permissions.tariffsManage },
      { label: 'Платежи', permission: permissions.paymentsWrite },
      { label: 'Показания вне текущего месяца', permission: permissions.historicalMeterReadingsCorrect },
      { label: 'Корректировка начальных данных', permission: permissions.openingDataAdjust },
      { label: 'Отчеты', permission: permissions.reportsRead },
      { label: 'Импорт', permission: permissions.importRun },
      { label: 'История изменений', permission: permissions.auditRead },
      { label: 'Что нового', permission: permissions.appReleasesManage },
    ])
  })
})
