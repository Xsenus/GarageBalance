export type WorkspaceSection = 'dashboard' | 'users' | 'contractors' | 'tariffsAndFees' | 'dictionaries' | 'meterReadings' | 'payments' | 'funds' | 'reports' | 'import' | 'audit' | 'releases' | 'settings'

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
