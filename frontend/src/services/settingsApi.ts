import { authenticatedApiFetch, authenticatedJsonApiFetch, readApiErrorMessage } from './authenticatedApiFetch'

export type PaymentDisplaySettingsDto = {
  showAllGarageOperationsByDefault: boolean
  version: string
  showPeriodicityColumn: boolean
  showAccrualMonthColumn: boolean
  tariffTableVersion: string
  showFundName: boolean
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

export type ActionCommentSettingsDto = {
  required: boolean
  version: string
}

export type HistoricalMeterReadingCorrectionSettingsDto = {
  enabled: boolean
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
  getActionCommentSettings(accessToken: string, signal?: AbortSignal): Promise<ActionCommentSettingsDto>
  updateActionCommentSettings(accessToken: string, request: ActionCommentSettingsDto): Promise<ActionCommentSettingsDto>
  getHistoricalMeterReadingCorrectionSettings(accessToken: string, signal?: AbortSignal): Promise<HistoricalMeterReadingCorrectionSettingsDto>
  updateHistoricalMeterReadingCorrectionSettings(accessToken: string, request: HistoricalMeterReadingCorrectionSettingsDto): Promise<HistoricalMeterReadingCorrectionSettingsDto>
  getPaymentDisplaySettings(accessToken: string, signal?: AbortSignal): Promise<PaymentDisplaySettingsDto>
  updatePaymentDisplaySettings(accessToken: string, request: PaymentDisplaySettingsDto): Promise<PaymentDisplaySettingsDto>
  getTariffPanelsLayout(accessToken: string, signal?: AbortSignal): Promise<TariffPanelsLayoutDto>
  updateTariffPanelsLayout(accessToken: string, request: UpdateTariffPanelsLayoutRequest): Promise<TariffPanelsLayoutDto>
  getSalaryAccrualSettings(accessToken: string, signal?: AbortSignal): Promise<SalaryAccrualSettingsDto>
  updateSalaryAccrualSettings(accessToken: string, request: SalaryAccrualSettingsDto): Promise<SalaryAccrualSettingsDto>
  getBusinessDateSettings(accessToken: string, signal?: AbortSignal): Promise<BusinessDateSettingsDto>
  previewBusinessDateChange(accessToken: string, request: { overrideDate: string | null; version?: string }): Promise<BusinessDateChangePreviewDto>
  updateBusinessDateSettings(accessToken: string, request: { overrideDate: string | null; version?: string }): Promise<BusinessDateSettingsDto>
  getCashBankBalances(accessToken: string, signal?: AbortSignal): Promise<CashBankBalanceSettingsDto>
  updateCashBankOpeningBalances(accessToken: string, request: { cashOpeningBalance: number; bankOpeningBalance: number; reason: string }): Promise<CashBankBalanceSettingsDto>
  createCashBankBalanceAdjustment(accessToken: string, request: { account: 'cash' | 'bank'; direction: 'increase' | 'decrease'; operationDate: string; amount: number; reason: string }): Promise<CashBankBalanceSettingsDto>
  getDatabaseBackups(accessToken: string, signal?: AbortSignal): Promise<DatabaseBackupStatusDto>
  createDatabaseBackup(accessToken: string, request: { reason: string }): Promise<DatabaseBackupFileDto>
  downloadDatabaseBackup(accessToken: string, fileName: string): Promise<Blob>
  deleteDatabaseBackup(accessToken: string, fileName: string, request: { reason: string }): Promise<DatabaseBackupFileDto>
  getDiagnosticLogStatus(accessToken: string, signal?: AbortSignal): Promise<DiagnosticLogStatusDto>
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
  getActionCommentSettings(accessToken, signal) {
    return requestJson(accessToken, '/api/settings/action-comments', { signal })
  },
  updateActionCommentSettings(accessToken, request) {
    return requestJson(accessToken, '/api/settings/action-comments', { method: 'PUT', body: JSON.stringify(request) })
  },
  getHistoricalMeterReadingCorrectionSettings(accessToken, signal) {
    return requestJson(accessToken, '/api/settings/meter-readings/historical-corrections', { signal })
  },
  updateHistoricalMeterReadingCorrectionSettings(accessToken, request) {
    return requestJson(accessToken, '/api/settings/meter-readings/historical-corrections', { method: 'PUT', body: JSON.stringify(request) })
  },
  getPaymentDisplaySettings(accessToken, signal) {
    return requestJson(accessToken, '/api/settings/payments/display', { signal })
  },
  updatePaymentDisplaySettings(accessToken, request) {
    return requestJson(accessToken, '/api/settings/payments/display', { method: 'PUT', body: JSON.stringify(request) })
  },
  getTariffPanelsLayout(accessToken, signal) {
    return requestJson(accessToken, '/api/settings/tariffs/layout', { signal })
  },
  updateTariffPanelsLayout(accessToken, request) {
    return requestJson(accessToken, '/api/settings/tariffs/layout', { method: 'PUT', body: JSON.stringify(request) })
  },
  getSalaryAccrualSettings(accessToken, signal) {
    return requestJson(accessToken, '/api/settings/salary-accrual', { signal })
  },
  updateSalaryAccrualSettings(accessToken, request) {
    return requestJson(accessToken, '/api/settings/salary-accrual', { method: 'PUT', body: JSON.stringify(request) })
  },
  getBusinessDateSettings(accessToken, signal) {
    return requestJson(accessToken, '/api/settings/business-date', { signal })
  },
  previewBusinessDateChange(accessToken, request) {
    return requestJson(accessToken, '/api/settings/business-date/preview', { method: 'POST', body: JSON.stringify(request) })
  },
  updateBusinessDateSettings(accessToken, request) {
    return requestJson(accessToken, '/api/settings/business-date', { method: 'PUT', body: JSON.stringify(request) })
  },
  getCashBankBalances(accessToken, signal) {
    return requestJson(accessToken, '/api/settings/cash-bank-balances', { signal })
  },
  updateCashBankOpeningBalances(accessToken, request) {
    return requestJson(accessToken, '/api/settings/cash-bank-balances/opening', { method: 'PUT', body: JSON.stringify(request) })
  },
  createCashBankBalanceAdjustment(accessToken, request) {
    return requestJson(accessToken, '/api/settings/cash-bank-balances/adjustments', { method: 'POST', body: JSON.stringify(request) })
  },
  getDatabaseBackups(accessToken, signal) {
    return requestJson(accessToken, '/api/settings/backups', { signal })
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
  getDiagnosticLogStatus(accessToken, signal) {
    return requestJson(accessToken, '/api/diagnostics/status', { signal })
  },
  createDiagnosticPackage(accessToken) {
    return requestBlob(accessToken, '/api/diagnostics/package', { method: 'POST' })
  },
}
