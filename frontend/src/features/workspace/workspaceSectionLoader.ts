import { lazy } from 'react'
import type { WorkspaceSection } from '../../shared/workspaceNavigation'

const loadPasswordPanel = () => import('../settings/PasswordPanel').then((module) => ({ default: module.PasswordPanel }))
const loadFundsPanel = () => import('../funds/FundsPanel').then((module) => ({ default: module.FundsPrototypePanel }))
const loadImportPanel = () => import('../import/ImportPanel').then((module) => ({ default: module.ImportPanel }))
const loadMeterReadingsPanel = () => import('../meterReadings/MeterReadingsPanel').then((module) => ({ default: module.MeterReadingsPrototypePanel }))
const loadAuditPanel = () => import('../audit/AuditPanel').then((module) => ({ default: module.AuditPanel }))
const loadReportPanel = () => import('../reports/ReportPanel').then((module) => ({ default: module.ReportPanel }))
const loadUserManagementPanel = () => import('../users/UserManagementPanel').then((module) => ({ default: module.UserManagementPanel }))
const loadDictionaryPanel = () => import('../dictionaries/DictionaryPanel').then((module) => ({ default: module.DictionaryPanelV2 }))
const loadTariffsPanel = () => import('../tariffs/TariffsAndFeesPanel').then((module) => ({ default: module.TariffsAndFeesPrototypePanel }))
const loadContractorsPanel = () => import('../contractors/ContractorsPanel').then((module) => ({ default: module.ContractorsPrototypePanel }))
const loadFinancePanel = () => import('../finance/FinancePanel').then((module) => ({ default: module.FinancePanel }))
const loadReleasePanel = () => import('../releases/ReleasePanel').then((module) => ({ default: module.ReleasePanel }))

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
