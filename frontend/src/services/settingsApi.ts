import { authenticatedApiFetch, authenticatedJsonApiFetch, readApiErrorMessage } from './authenticatedApiFetch'

export type PaymentDisplaySettingsDto = {
  showAllGarageOperationsByDefault: boolean
  version: string
  showPeriodicityColumn: boolean
  showAccrualMonthColumn: boolean
  tariffTableVersion: string
}

export type TariffPanelsLayoutDto = {
  irregularPaymentsWidthPercent: number
  version: string
}

export type UpdateTariffPanelsLayoutRequest = Pick<TariffPanelsLayoutDto, 'irregularPaymentsWidthPercent'>

export type SalaryAccrualSettingsDto = {
  accrualDay: number
  version: string
}

export type BusinessDateSettingsDto = {
  systemDate: string
  effectiveDate: string
  overrideDate: string | null
  isOverrideActive: boolean
  updatedAtUtc: string | null
  automation: {
    succeeded: boolean
    createdCount: number
    skippedCount: number
    message: string
  } | null
  version: string
}

export type BusinessDateChangePreviewDto = {
  systemDate: string
  currentEffectiveDate: string
  proposedEffectiveDate: string
  overrideDate: string | null
  isChange: boolean
  automation: {
    accountingMonth: string
    activeGarageCount: number
    activeRegularServiceCount: number
    dueRegularServiceCount: number
    activeFeeCampaignCount: number
    maximumGarageChecks: number
    warnings: string[]
  }
  version: string
}

export type CashBankBalanceOperationDto = {
  id: string
  account: 'cash' | 'bank'
  operationKind: 'opening_balance' | 'adjustment'
  direction: 'increase' | 'decrease'
  operationDate: string
  amount: number
  reason: string
  createdAtUtc: string
}

export type CashBankBalanceSettingsDto = {
  cashOpeningBalance: number
  bankOpeningBalance: number
  cashCurrentBalance: number
  bankCurrentBalance: number
  recentOperations: CashBankBalanceOperationDto[]
}

export type DatabaseBackupFileDto = {
  fileName: string
  sizeBytes: number
  createdAtUtc: string
  kind: 'manual' | 'automatic' | 'pre_update'
}

export type DatabaseBackupStatusDto = {
  enabled: boolean
  automaticEnabled: boolean
  intervalHours: number
  retentionCount: number
  directory: string
  isRunning: boolean
  lastSuccessfulBackupAtUtc: string | null
  lastError: string | null
  backups: DatabaseBackupFileDto[]
}

export type DiagnosticLogStatusDto = {
  enabled: boolean
  retentionDays: number
  packageDays: number
  packageMaxSizeMb: number
  fileCount: number
  totalSizeBytes: number
  lastEntryAtUtc: string | null
  lastWriteError: string | null
}

export type ApplicationSettingsClient = {
  getPaymentDisplaySettings(accessToken: string): Promise<PaymentDisplaySettingsDto>
  updatePaymentDisplaySettings(accessToken: string, request: PaymentDisplaySettingsDto): Promise<PaymentDisplaySettingsDto>
  getTariffPanelsLayout(accessToken: string): Promise<TariffPanelsLayoutDto>
  updateTariffPanelsLayout(accessToken: string, request: UpdateTariffPanelsLayoutRequest): Promise<TariffPanelsLayoutDto>
  getSalaryAccrualSettings(accessToken: string): Promise<SalaryAccrualSettingsDto>
  updateSalaryAccrualSettings(accessToken: string, request: SalaryAccrualSettingsDto): Promise<SalaryAccrualSettingsDto>
  getBusinessDateSettings(accessToken: string): Promise<BusinessDateSettingsDto>
  previewBusinessDateChange(accessToken: string, request: { overrideDate: string | null; version?: string }): Promise<BusinessDateChangePreviewDto>
  updateBusinessDateSettings(accessToken: string, request: { overrideDate: string | null; version?: string }): Promise<BusinessDateSettingsDto>
  getCashBankBalances(accessToken: string): Promise<CashBankBalanceSettingsDto>
  updateCashBankOpeningBalances(accessToken: string, request: { cashOpeningBalance: number; bankOpeningBalance: number; reason: string }): Promise<CashBankBalanceSettingsDto>
  createCashBankBalanceAdjustment(accessToken: string, request: { account: 'cash' | 'bank'; direction: 'increase' | 'decrease'; operationDate: string; amount: number; reason: string }): Promise<CashBankBalanceSettingsDto>
  getDatabaseBackups(accessToken: string): Promise<DatabaseBackupStatusDto>
  createDatabaseBackup(accessToken: string, request: { reason: string }): Promise<DatabaseBackupFileDto>
  downloadDatabaseBackup(accessToken: string, fileName: string): Promise<Blob>
  deleteDatabaseBackup(accessToken: string, fileName: string, request: { reason: string }): Promise<DatabaseBackupFileDto>
  getDiagnosticLogStatus(accessToken: string): Promise<DiagnosticLogStatusDto>
  createDiagnosticPackage(accessToken: string): Promise<Blob>
}

async function requestJson<TResponse>(accessToken: string, path: string, init?: RequestInit): Promise<TResponse> {
  const response = await authenticatedJsonApiFetch(accessToken, path, init)

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, 'Не удалось загрузить настройки отображения.'))
  }

  return response.json()
}

async function requestBlob(
  accessToken: string,
  path: string,
  init?: RequestInit,
  fallbackMessage = 'Не удалось сформировать диагностический пакет.',
): Promise<Blob> {
  const response = await authenticatedApiFetch(accessToken, path, init)
  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, fallbackMessage))
  }
  return response.blob()
}

export const settingsApi: ApplicationSettingsClient = {
  getPaymentDisplaySettings(accessToken) {
    return requestJson(accessToken, '/api/settings/payments/display')
  },
  updatePaymentDisplaySettings(accessToken, request) {
    return requestJson(accessToken, '/api/settings/payments/display', { method: 'PUT', body: JSON.stringify(request) })
  },
  getTariffPanelsLayout(accessToken) {
    return requestJson(accessToken, '/api/settings/tariffs/layout')
  },
  updateTariffPanelsLayout(accessToken, request) {
    return requestJson(accessToken, '/api/settings/tariffs/layout', { method: 'PUT', body: JSON.stringify(request) })
  },
  getSalaryAccrualSettings(accessToken) {
    return requestJson(accessToken, '/api/settings/salary-accrual')
  },
  updateSalaryAccrualSettings(accessToken, request) {
    return requestJson(accessToken, '/api/settings/salary-accrual', { method: 'PUT', body: JSON.stringify(request) })
  },
  getBusinessDateSettings(accessToken) {
    return requestJson(accessToken, '/api/settings/business-date')
  },
  previewBusinessDateChange(accessToken, request) {
    return requestJson(accessToken, '/api/settings/business-date/preview', { method: 'POST', body: JSON.stringify(request) })
  },
  updateBusinessDateSettings(accessToken, request) {
    return requestJson(accessToken, '/api/settings/business-date', { method: 'PUT', body: JSON.stringify(request) })
  },
  getCashBankBalances(accessToken) {
    return requestJson(accessToken, '/api/settings/cash-bank-balances')
  },
  updateCashBankOpeningBalances(accessToken, request) {
    return requestJson(accessToken, '/api/settings/cash-bank-balances/opening', { method: 'PUT', body: JSON.stringify(request) })
  },
  createCashBankBalanceAdjustment(accessToken, request) {
    return requestJson(accessToken, '/api/settings/cash-bank-balances/adjustments', { method: 'POST', body: JSON.stringify(request) })
  },
  getDatabaseBackups(accessToken) {
    return requestJson(accessToken, '/api/settings/backups')
  },
  createDatabaseBackup(accessToken, request) {
    return requestJson(accessToken, '/api/settings/backups', { method: 'POST', body: JSON.stringify(request) })
  },
  downloadDatabaseBackup(accessToken, fileName) {
    return requestBlob(
      accessToken,
      `/api/settings/backups/${encodeURIComponent(fileName)}/download`,
      undefined,
      'Не удалось скачать резервную копию.',
    )
  },
  deleteDatabaseBackup(accessToken, fileName, request) {
    return requestJson(accessToken, `/api/settings/backups/${encodeURIComponent(fileName)}`, { method: 'DELETE', body: JSON.stringify(request) })
  },
  getDiagnosticLogStatus(accessToken) {
    return requestJson(accessToken, '/api/diagnostics/status')
  },
  createDiagnosticPackage(accessToken) {
    return requestBlob(accessToken, '/api/diagnostics/package', { method: 'POST' })
  },
}
