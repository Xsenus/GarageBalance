import type { AuthResponse } from '../services/authApi'

export const permissions = {
  usersManage: 'users.manage',
  dictionariesRead: 'dictionaries.read',
  dictionariesWrite: 'dictionaries.write',
  tariffsManage: 'tariffs.manage',
  paymentsRead: 'payments.read',
  paymentsWrite: 'payments.write',
  historicalMeterReadingsCorrect: 'payments.meter_readings.historical_correct',
  openingDataAdjust: 'opening_data.adjust',
  reportsRead: 'reports.read',
  importRun: 'import.run',
  auditRead: 'audit.read',
  appReleasesManage: 'app_releases.manage',
} as const

export type Permission = (typeof permissions)[keyof typeof permissions]

const permissionDependencies: Readonly<Partial<Record<Permission, readonly Permission[]>>> = {
  [permissions.dictionariesWrite]: [permissions.dictionariesRead],
  [permissions.tariffsManage]: [permissions.dictionariesRead],
  [permissions.paymentsWrite]: [permissions.paymentsRead, permissions.dictionariesRead],
  [permissions.historicalMeterReadingsCorrect]: [permissions.paymentsWrite],
  [permissions.openingDataAdjust]: [permissions.dictionariesWrite],
  [permissions.reportsRead]: [permissions.dictionariesRead],
}

export function expandPermissionDependencies(selectedPermissions: readonly string[]): string[] {
  const expanded = new Set(selectedPermissions)
  const pending = [...expanded]
  while (pending.length > 0) {
    const permission = pending.shift() as Permission
    for (const dependency of permissionDependencies[permission] ?? []) {
      if (!expanded.has(dependency)) {
        expanded.add(dependency)
        pending.push(dependency)
      }
    }
  }

  return [...expanded].sort()
}

export function isPermissionRequiredBySelection(permission: string, selectedPermissions: readonly string[]): boolean {
  return selectedPermissions.some((selected) => (
    selected !== permission && expandPermissionDependencies([selected]).includes(permission)
  ))
}

export const rolePermissionGroups: ReadonlyArray<{ label: string; permission: Permission }> = [
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
]

export function hasPermission(auth: AuthResponse, permission: string): boolean {
  return auth.user.permissions.includes(permission)
}

export function isAdministrator(auth: AuthResponse): boolean {
  return auth.user.roles.includes('administrator')
}

export function hasAnyPermission(auth: AuthResponse, requiredAny?: readonly string[]): boolean {
  return !requiredAny || requiredAny.some((permission) => hasPermission(auth, permission))
}
