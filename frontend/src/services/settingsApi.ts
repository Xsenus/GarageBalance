export type PaymentDisplaySettingsDto = {
  showAllGarageOperationsByDefault: boolean
}

export type SalaryAccrualSettingsDto = {
  accrualDay: number
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
  getSalaryAccrualSettings(accessToken: string): Promise<SalaryAccrualSettingsDto>
  updateSalaryAccrualSettings(accessToken: string, request: SalaryAccrualSettingsDto): Promise<SalaryAccrualSettingsDto>
  getBusinessDateSettings(accessToken: string): Promise<BusinessDateSettingsDto>
  updateBusinessDateSettings(accessToken: string, request: { overrideDate: string | null }): Promise<BusinessDateSettingsDto>
  getCashBankBalances(accessToken: string): Promise<CashBankBalanceSettingsDto>
  updateCashBankOpeningBalances(accessToken: string, request: { cashOpeningBalance: number; bankOpeningBalance: number; reason: string }): Promise<CashBankBalanceSettingsDto>
  createCashBankBalanceAdjustment(accessToken: string, request: { account: 'cash' | 'bank'; direction: 'increase' | 'decrease'; operationDate: string; amount: number; reason: string }): Promise<CashBankBalanceSettingsDto>
  getDatabaseBackups(accessToken: string): Promise<DatabaseBackupStatusDto>
  createDatabaseBackup(accessToken: string, request: { reason: string }): Promise<DatabaseBackupFileDto>
  getDiagnosticLogStatus(accessToken: string): Promise<DiagnosticLogStatusDto>
  createDiagnosticPackage(accessToken: string): Promise<Blob>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

async function requestJson<TResponse>(accessToken: string, path: string, init?: RequestInit): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
      ...init?.headers,
    },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? 'Не удалось загрузить настройки отображения.')
  }

  return response.json()
}

async function requestBlob(accessToken: string, path: string, init?: RequestInit): Promise<Blob> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: { Authorization: `Bearer ${accessToken}`, ...init?.headers },
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? 'Не удалось сформировать диагностический пакет.')
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
  getSalaryAccrualSettings(accessToken) {
    return requestJson(accessToken, '/api/settings/salary-accrual')
  },
  updateSalaryAccrualSettings(accessToken, request) {
    return requestJson(accessToken, '/api/settings/salary-accrual', { method: 'PUT', body: JSON.stringify(request) })
  },
  getBusinessDateSettings(accessToken) {
    return requestJson(accessToken, '/api/settings/business-date')
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
  getDiagnosticLogStatus(accessToken) {
    return requestJson(accessToken, '/api/diagnostics/status')
  },
  createDiagnosticPackage(accessToken) {
    return requestBlob(accessToken, '/api/diagnostics/package', { method: 'POST' })
  },
}
