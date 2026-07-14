import { lazy } from 'react'
import { createRetryableLazyLoader } from '../../shared/retryableLazyLoader'
import type { WorkspaceSection } from '../../shared/workspaceNavigation'

const loadPasswordPanel = createRetryableLazyLoader(() => import('../settings/PasswordPanel').then((module) => ({ default: module.PasswordPanel })))
const loadFundsPanel = createRetryableLazyLoader(() => import('../funds/FundsPanel').then((module) => ({ default: module.FundsPrototypePanel })))
const loadImportPanel = createRetryableLazyLoader(() => import('../import/ImportPanel').then((module) => ({ default: module.ImportPanel })))
const loadMeterReadingsPanel = createRetryableLazyLoader(() => import('../meterReadings/MeterReadingsPanel').then((module) => ({ default: module.MeterReadingsPrototypePanel })))
const loadAuditPanel = createRetryableLazyLoader(() => import('../audit/AuditPanel').then((module) => ({ default: module.AuditPanel })))
const loadReportPanel = createRetryableLazyLoader(() => import('../reports/ReportPanel').then((module) => ({ default: module.ReportPanel })))
const loadUserManagementPanel = createRetryableLazyLoader(() => import('../users/UserManagementPanel').then((module) => ({ default: module.UserManagementPanel })))
const loadDictionaryPanel = createRetryableLazyLoader(() => import('../dictionaries/DictionaryPanel').then((module) => ({ default: module.DictionaryPanelV2 })))
const loadTariffsPanel = createRetryableLazyLoader(() => import('../tariffs/TariffsAndFeesPanel').then((module) => ({ default: module.TariffsAndFeesPrototypePanel })))
const loadContractorsPanel = createRetryableLazyLoader(() => import('../contractors/ContractorsPanel').then((module) => ({ default: module.ContractorsPrototypePanel })))
const loadFinancePanel = createRetryableLazyLoader(() => import('../finance/FinancePanel').then((module) => ({ default: module.FinancePanel })))
const loadReleasePanel = createRetryableLazyLoader(() => import('../releases/ReleasePanel').then((module) => ({ default: module.ReleasePanel })))

export const PasswordPanel = lazy(loadPasswordPanel)
export const FundsPrototypePanel = lazy(loadFundsPanel)
export const ImportPanel = lazy(loadImportPanel)
export const MeterReadingsPrototypePanel = lazy(loadMeterReadingsPanel)
export const AuditPanel = lazy(loadAuditPanel)
export const ReportPanel = lazy(loadReportPanel)
export const UserManagementPanel = lazy(loadUserManagementPanel)
export const DictionaryPanelV2 = lazy(loadDictionaryPanel)
export const TariffsAndFeesPrototypePanel = lazy(loadTariffsPanel)
export const ContractorsPrototypePanel = lazy(loadContractorsPanel)
export const FinancePanel = lazy(loadFinancePanel)
export const ReleasePanel = lazy(loadReleasePanel)

const workspaceSectionPreloaders: Partial<Record<WorkspaceSection, () => Promise<unknown>>> = {
  users: loadUserManagementPanel,
  dictionaries: loadDictionaryPanel,
  contractors: loadContractorsPanel,
  tariffsAndFees: loadTariffsPanel,
  payments: loadFinancePanel,
  meterReadings: loadMeterReadingsPanel,
  funds: loadFundsPanel,
  reports: loadReportPanel,
  import: loadImportPanel,
  audit: loadAuditPanel,
  releases: loadReleasePanel,
  settings: loadPasswordPanel,
}

export function preloadWorkspaceSection(section: WorkspaceSection) {
  void workspaceSectionPreloaders[section]?.().catch(() => undefined)
}
