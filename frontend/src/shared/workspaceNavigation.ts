import type { AuthResponse } from '../services/authApi'
import { hasPermission, permissions } from './accessControl'

export type WorkspaceSection = 'dashboard' | 'users' | 'contractors' | 'tariffsAndFees' | 'dictionaries' | 'meterReadings' | 'payments' | 'funds' | 'reports' | 'import' | 'audit' | 'releases' | 'settings'

const requiredPermissionsBySection: Partial<Record<WorkspaceSection, readonly string[]>> = {
  users: [permissions.usersManage],
  contractors: [permissions.dictionariesRead],
  tariffsAndFees: [permissions.dictionariesRead],
  dictionaries: [permissions.dictionariesRead],
  meterReadings: [permissions.paymentsRead],
  payments: [permissions.paymentsRead, permissions.dictionariesRead],
  funds: [permissions.reportsRead],
  reports: [permissions.reportsRead, permissions.dictionariesRead],
  import: [permissions.importRun],
  audit: [permissions.auditRead],
}

export function canAccessWorkspaceSection(auth: AuthResponse, section: WorkspaceSection): boolean {
  return requiredPermissionsBySection[section]?.every((permission) => hasPermission(auth, permission)) ?? true
}

export type AuditPanelPreset = {
  section?: string
  entityType?: string
  relatedCounterparty?: string
}

export type ContractorOpenTarget = {
  section: 'garages' | 'suppliers' | 'staff'
  entityId?: string | null
  displayName?: string | null
  garageNumber?: string | null
}

export type WorkspaceOpenContext = {
  contractorTarget?: ContractorOpenTarget
}
