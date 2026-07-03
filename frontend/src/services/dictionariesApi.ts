export type OwnerDto = {
  id: string
  lastName: string
  firstName: string
  middleName: string | null
  fullName: string
  phone: string | null
  address: string | null
  meterNotes: string | null
  isArchived: boolean
  garageNumbers?: string[]
}

export type GarageDto = {
  id: string
  number: string
  peopleCount: number
  floorCount: number
  ownerId: string | null
  ownerName: string | null
  startingBalance: number
  initialWaterMeterValue: number | null
  initialElectricityMeterValue: number | null
  comment: string | null
  isArchived: boolean
}

export type SupplierGroupDto = {
  id: string
  name: string
  isSystem: boolean
  isArchived: boolean
}

export type SupplierDto = {
  id: string
  name: string
  groupId: string
  groupName: string
  inn: string | null
  legalAddress: string | null
  contactPerson: string | null
  phone: string | null
  email: string | null
  startingBalance: number
  comment: string | null
  isArchived: boolean
}

export type AccountingTypeDto = {
  id: string
  name: string
  code: string | null
  isSystem: boolean
  isArchived: boolean
}

export type TariffDto = {
  id: string
  name: string
  calculationBase: string
  rate: number
  electricityFirstThreshold: number | null
  electricitySecondThreshold: number | null
  electricityFirstTierName: string | null
  electricitySecondTierName: string | null
  electricityThirdTierName: string | null
  electricityFirstRate: number | null
  electricitySecondRate: number | null
  electricityThirdRate: number | null
  effectiveFrom: string
  comment: string | null
  isArchived: boolean
}

export type IrregularPaymentDto = {
  id: string
  name: string
  amount: number
  isActive: boolean
  isArchived: boolean
  isUsed: boolean
}

export type PagedResult<TItem> = {
  items: TItem[]
  totalCount: number
  offset: number
  limit: number
}

export type UpsertOwnerRequest = {
  lastName: string
  firstName: string
  middleName?: string
  phone?: string
  address?: string
  meterNotes?: string
}

export type UpsertGarageRequest = {
  number: string
  peopleCount: number
  floorCount: number
  ownerId?: string | null
  startingBalance: number
  initialWaterMeterValue?: number | null
  initialElectricityMeterValue?: number | null
  comment?: string
}

export type UpsertSupplierGroupRequest = {
  name: string
}

export type UpsertSupplierRequest = {
  name: string
  groupId: string
  inn?: string
  legalAddress?: string
  contactPerson?: string
  phone?: string
  email?: string
  startingBalance: number
  comment?: string
}

export type UpsertAccountingTypeRequest = {
  name: string
  code?: string
}

export type UpsertTariffRequest = {
  name: string
  calculationBase: string
  rate: number
  effectiveFrom: string
  comment?: string
  electricityFirstThreshold?: number
  electricitySecondThreshold?: number
  electricityFirstTierName?: string
  electricitySecondTierName?: string
  electricityThirdTierName?: string
  electricityFirstRate?: number
  electricitySecondRate?: number
  electricityThirdRate?: number
}

export type UpsertIrregularPaymentRequest = {
  name: string
  amount: number
  isActive?: boolean
}

export type DictionaryClient = {
  getOwners(accessToken: string, search?: string, limit?: number, includeArchived?: boolean): Promise<OwnerDto[]>
  getOwnersPage?(accessToken: string, search?: string, offset?: number, limit?: number, includeArchived?: boolean): Promise<PagedResult<OwnerDto>>
  createOwner(accessToken: string, request: UpsertOwnerRequest): Promise<OwnerDto>
  updateOwner(accessToken: string, id: string, request: UpsertOwnerRequest): Promise<OwnerDto>
  archiveOwner(accessToken: string, id: string, reason: string): Promise<void>
  restoreOwner(accessToken: string, id: string): Promise<OwnerDto>
  getGarages(accessToken: string, search?: string, limit?: number, includeArchived?: boolean): Promise<GarageDto[]>
  getGaragesPage?(accessToken: string, search?: string, offset?: number, limit?: number, includeArchived?: boolean): Promise<PagedResult<GarageDto>>
  createGarage(accessToken: string, request: UpsertGarageRequest): Promise<GarageDto>
  updateGarage(accessToken: string, id: string, request: UpsertGarageRequest): Promise<GarageDto>
  archiveGarage(accessToken: string, id: string, reason: string): Promise<void>
  restoreGarage(accessToken: string, id: string): Promise<GarageDto>
  getSupplierGroups(accessToken: string, limit?: number, includeArchived?: boolean): Promise<SupplierGroupDto[]>
  getSupplierGroupsPage?(accessToken: string, offset?: number, limit?: number, includeArchived?: boolean): Promise<PagedResult<SupplierGroupDto>>
  createSupplierGroup(accessToken: string, request: UpsertSupplierGroupRequest): Promise<SupplierGroupDto>
  updateSupplierGroup?(accessToken: string, id: string, request: UpsertSupplierGroupRequest): Promise<SupplierGroupDto>
  archiveSupplierGroup(accessToken: string, id: string, reason: string): Promise<void>
  restoreSupplierGroup(accessToken: string, id: string): Promise<SupplierGroupDto>
  getSuppliers(accessToken: string, groupId?: string, search?: string, limit?: number, includeArchived?: boolean): Promise<SupplierDto[]>
  getSuppliersPage?(accessToken: string, groupId?: string, search?: string, offset?: number, limit?: number, includeArchived?: boolean): Promise<PagedResult<SupplierDto>>
  createSupplier(accessToken: string, request: UpsertSupplierRequest): Promise<SupplierDto>
  updateSupplier(accessToken: string, id: string, request: UpsertSupplierRequest): Promise<SupplierDto>
  archiveSupplier(accessToken: string, id: string, reason: string): Promise<void>
  restoreSupplier(accessToken: string, id: string): Promise<SupplierDto>
  getIncomeTypes(accessToken: string, limit?: number, includeArchived?: boolean): Promise<AccountingTypeDto[]>
  getIncomeTypesPage?(accessToken: string, offset?: number, limit?: number, includeArchived?: boolean): Promise<PagedResult<AccountingTypeDto>>
  createIncomeType(accessToken: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  updateIncomeType?(accessToken: string, id: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  archiveIncomeType(accessToken: string, id: string, reason: string): Promise<void>
  restoreIncomeType(accessToken: string, id: string): Promise<AccountingTypeDto>
  getExpenseTypes(accessToken: string, limit?: number, includeArchived?: boolean): Promise<AccountingTypeDto[]>
  getExpenseTypesPage?(accessToken: string, offset?: number, limit?: number, includeArchived?: boolean): Promise<PagedResult<AccountingTypeDto>>
  createExpenseType(accessToken: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  updateExpenseType?(accessToken: string, id: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  archiveExpenseType(accessToken: string, id: string, reason: string): Promise<void>
  restoreExpenseType(accessToken: string, id: string): Promise<AccountingTypeDto>
  getTariffs(accessToken: string, search?: string, limit?: number, includeArchived?: boolean): Promise<TariffDto[]>
  getTariffsPage?(accessToken: string, search?: string, offset?: number, limit?: number, includeArchived?: boolean): Promise<PagedResult<TariffDto>>
  createTariff(accessToken: string, request: UpsertTariffRequest): Promise<TariffDto>
  updateTariff(accessToken: string, id: string, request: UpsertTariffRequest): Promise<TariffDto>
  archiveTariff(accessToken: string, id: string, reason: string): Promise<void>
  restoreTariff(accessToken: string, id: string): Promise<TariffDto>
  getIrregularPayments(accessToken: string, search?: string, limit?: number, includeArchived?: boolean): Promise<IrregularPaymentDto[]>
  createIrregularPayment(accessToken: string, request: UpsertIrregularPaymentRequest): Promise<IrregularPaymentDto>
  updateIrregularPayment(accessToken: string, id: string, request: UpsertIrregularPaymentRequest): Promise<IrregularPaymentDto>
  setIrregularPaymentStatus(accessToken: string, id: string, request: { isActive: boolean; reason?: string }): Promise<IrregularPaymentDto>
  archiveIrregularPayment(accessToken: string, id: string, reason: string): Promise<void>
  restoreIrregularPayment(accessToken: string, id: string): Promise<IrregularPaymentDto>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''
const defaultDictionaryListLimit = 100

export class DictionaryApiError extends Error {
  readonly code: string | null
  readonly status: number

  constructor(code: string | null, message: string, status: number) {
    super(message)
    this.name = 'DictionaryApiError'
    this.code = code
    this.status = status
  }
}

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
    const code = typeof problem?.code === 'string' ? problem.code : typeof problem?.title === 'string' ? problem.title : null
    throw new DictionaryApiError(code, problem?.detail ?? 'Не удалось выполнить запрос.', response.status)
  }

  if (response.status === 204) {
    return undefined as TResponse
  }

  return response.json()
}

function withQuery(path: string, params: Record<string, string | number | boolean | undefined>): string {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== '') {
      query.set(key, String(value))
    }
  }

  const queryString = query.toString()
  return queryString ? `${path}?${queryString}` : path
}

export const dictionariesApi: DictionaryClient = {
  getOwners(accessToken, search, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/owners', { search, limit, includeArchived: includeArchived || undefined }))
  },
  getOwnersPage(accessToken, search, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/owners/page', { search, offset, limit, includeArchived: includeArchived || undefined }))
  },
  createOwner(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/owners', { method: 'POST', body: JSON.stringify(request) })
  },
  updateOwner(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/owners/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveOwner(accessToken, id, reason) {
    return requestJson(accessToken, `/api/dictionaries/owners/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  restoreOwner(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/owners/${id}/restore`, { method: 'POST' })
  },
  getGarages(accessToken, search, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/garages', { search, limit, includeArchived: includeArchived || undefined }))
  },
  getGaragesPage(accessToken, search, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/garages/page', { search, offset, limit, includeArchived: includeArchived || undefined }))
  },
  createGarage(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/garages', { method: 'POST', body: JSON.stringify(request) })
  },
  updateGarage(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/garages/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveGarage(accessToken, id, reason) {
    return requestJson(accessToken, `/api/dictionaries/garages/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  restoreGarage(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/garages/${id}/restore`, { method: 'POST' })
  },
  getSupplierGroups(accessToken, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/supplier-groups', { limit, includeArchived: includeArchived || undefined }))
  },
  getSupplierGroupsPage(accessToken, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/supplier-groups/page', { offset, limit, includeArchived: includeArchived || undefined }))
  },
  createSupplierGroup(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/supplier-groups', { method: 'POST', body: JSON.stringify(request) })
  },
  updateSupplierGroup(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/supplier-groups/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveSupplierGroup(accessToken, id, reason) {
    return requestJson(accessToken, `/api/dictionaries/supplier-groups/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  restoreSupplierGroup(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/supplier-groups/${id}/restore`, { method: 'POST' })
  },
  getSuppliers(accessToken, groupId, search, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/suppliers', { groupId, search, limit, includeArchived: includeArchived || undefined }))
  },
  getSuppliersPage(accessToken, groupId, search, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/suppliers/page', { groupId, search, offset, limit, includeArchived: includeArchived || undefined }))
  },
  createSupplier(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/suppliers', { method: 'POST', body: JSON.stringify(request) })
  },
  updateSupplier(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/suppliers/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveSupplier(accessToken, id, reason) {
    return requestJson(accessToken, `/api/dictionaries/suppliers/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  restoreSupplier(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/suppliers/${id}/restore`, { method: 'POST' })
  },
  getIncomeTypes(accessToken, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/income-types', { limit, includeArchived: includeArchived || undefined }))
  },
  getIncomeTypesPage(accessToken, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/income-types/page', { offset, limit, includeArchived: includeArchived || undefined }))
  },
  createIncomeType(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/income-types', { method: 'POST', body: JSON.stringify(request) })
  },
  updateIncomeType(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/income-types/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveIncomeType(accessToken, id, reason) {
    return requestJson(accessToken, `/api/dictionaries/income-types/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  restoreIncomeType(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/income-types/${id}/restore`, { method: 'POST' })
  },
  getExpenseTypes(accessToken, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/expense-types', { limit, includeArchived: includeArchived || undefined }))
  },
  getExpenseTypesPage(accessToken, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/expense-types/page', { offset, limit, includeArchived: includeArchived || undefined }))
  },
  createExpenseType(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/expense-types', { method: 'POST', body: JSON.stringify(request) })
  },
  updateExpenseType(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/expense-types/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveExpenseType(accessToken, id, reason) {
    return requestJson(accessToken, `/api/dictionaries/expense-types/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  restoreExpenseType(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/expense-types/${id}/restore`, { method: 'POST' })
  },
  getTariffs(accessToken, search, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/tariffs', { search, limit, includeArchived: includeArchived || undefined }))
  },
  getTariffsPage(accessToken, search, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/tariffs/page', { search, offset, limit, includeArchived: includeArchived || undefined }))
  },
  createTariff(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/tariffs', { method: 'POST', body: JSON.stringify(request) })
  },
  updateTariff(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/tariffs/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveTariff(accessToken, id, reason) {
    return requestJson(accessToken, `/api/dictionaries/tariffs/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  restoreTariff(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/tariffs/${id}/restore`, { method: 'POST' })
  },
  getIrregularPayments(accessToken, search, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/irregular-payments', { search, limit, includeArchived: includeArchived || undefined }))
  },
  createIrregularPayment(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/irregular-payments', { method: 'POST', body: JSON.stringify(request) })
  },
  updateIrregularPayment(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/irregular-payments/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  setIrregularPaymentStatus(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/irregular-payments/${id}/status`, { method: 'POST', body: JSON.stringify(request) })
  },
  archiveIrregularPayment(accessToken, id, reason) {
    return requestJson(accessToken, `/api/dictionaries/irregular-payments/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  restoreIrregularPayment(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/irregular-payments/${id}/restore`, { method: 'POST' })
  },
}
