import type { AuthResponse } from '../services/authApi'

export const permissions = {
  usersManage: 'users.manage',
  dictionariesRead: 'dictionaries.read',
  dictionariesWrite: 'dictionaries.write',
  tariffsManage: 'tariffs.manage',
  paymentsRead: 'payments.read',
  paymentsWrite: 'payments.write',
  historicalMeterReadingsCorrect: 'payments.meter_readings.historical_correct',
  reportsRead: 'reports.read',
  importRun: 'import.run',
  auditRead: 'audit.read',
  appReleasesManage: 'app_releases.manage',
} as const

export type Permission = (typeof permissions)[keyof typeof permissions]

export const rolePermissionGroups: ReadonlyArray<{ label: string; permission: Permission }> = [
  { label: 'Пользователи', permission: permissions.usersManage },
  { label: 'Справочники', permission: permissions.dictionariesWrite },
  { label: 'Тарифы', permission: permissions.tariffsManage },
  { label: 'Платежи', permission: permissions.paymentsWrite },
  { label: 'Исторические показания', permission: permissions.historicalMeterReadingsCorrect },
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
