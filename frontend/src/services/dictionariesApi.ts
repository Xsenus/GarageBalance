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
  electricityFirstRate: number | null
  electricitySecondRate: number | null
  electricityThirdRate: number | null
  effectiveFrom: string
  comment: string | null
  isArchived: boolean
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
  electricityFirstRate?: number
  electricitySecondRate?: number
  electricityThirdRate?: number
}

export type DictionaryClient = {
  getOwners(accessToken: string, search?: string, limit?: number): Promise<OwnerDto[]>
  getOwnersPage?(accessToken: string, search?: string, offset?: number, limit?: number): Promise<PagedResult<OwnerDto>>
  createOwner(accessToken: string, request: UpsertOwnerRequest): Promise<OwnerDto>
  updateOwner(accessToken: string, id: string, request: UpsertOwnerRequest): Promise<OwnerDto>
  archiveOwner(accessToken: string, id: string): Promise<void>
  restoreOwner(accessToken: string, id: string): Promise<OwnerDto>
  getGarages(accessToken: string, search?: string, limit?: number): Promise<GarageDto[]>
  getGaragesPage?(accessToken: string, search?: string, offset?: number, limit?: number): Promise<PagedResult<GarageDto>>
  createGarage(accessToken: string, request: UpsertGarageRequest): Promise<GarageDto>
  updateGarage(accessToken: string, id: string, request: UpsertGarageRequest): Promise<GarageDto>
  archiveGarage(accessToken: string, id: string): Promise<void>
  restoreGarage(accessToken: string, id: string): Promise<GarageDto>
  getSupplierGroups(accessToken: string, limit?: number): Promise<SupplierGroupDto[]>
  getSupplierGroupsPage?(accessToken: string, offset?: number, limit?: number): Promise<PagedResult<SupplierGroupDto>>
  createSupplierGroup(accessToken: string, request: UpsertSupplierGroupRequest): Promise<SupplierGroupDto>
  updateSupplierGroup?(accessToken: string, id: string, request: UpsertSupplierGroupRequest): Promise<SupplierGroupDto>
  archiveSupplierGroup(accessToken: string, id: string): Promise<void>
  restoreSupplierGroup(accessToken: string, id: string): Promise<SupplierGroupDto>
  getSuppliers(accessToken: string, groupId?: string, search?: string, limit?: number): Promise<SupplierDto[]>
  getSuppliersPage?(accessToken: string, groupId?: string, search?: string, offset?: number, limit?: number): Promise<PagedResult<SupplierDto>>
  createSupplier(accessToken: string, request: UpsertSupplierRequest): Promise<SupplierDto>
  updateSupplier(accessToken: string, id: string, request: UpsertSupplierRequest): Promise<SupplierDto>
  archiveSupplier(accessToken: string, id: string): Promise<void>
  restoreSupplier(accessToken: string, id: string): Promise<SupplierDto>
  getIncomeTypes(accessToken: string, limit?: number): Promise<AccountingTypeDto[]>
  getIncomeTypesPage?(accessToken: string, offset?: number, limit?: number): Promise<PagedResult<AccountingTypeDto>>
  createIncomeType(accessToken: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  updateIncomeType?(accessToken: string, id: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  archiveIncomeType(accessToken: string, id: string): Promise<void>
  restoreIncomeType(accessToken: string, id: string): Promise<AccountingTypeDto>
  getExpenseTypes(accessToken: string, limit?: number): Promise<AccountingTypeDto[]>
  getExpenseTypesPage?(accessToken: string, offset?: number, limit?: number): Promise<PagedResult<AccountingTypeDto>>
  createExpenseType(accessToken: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  updateExpenseType?(accessToken: string, id: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  archiveExpenseType(accessToken: string, id: string): Promise<void>
  restoreExpenseType(accessToken: string, id: string): Promise<AccountingTypeDto>
  getTariffs(accessToken: string, search?: string, limit?: number): Promise<TariffDto[]>
  getTariffsPage?(accessToken: string, search?: string, offset?: number, limit?: number): Promise<PagedResult<TariffDto>>
  createTariff(accessToken: string, request: UpsertTariffRequest): Promise<TariffDto>
  updateTariff(accessToken: string, id: string, request: UpsertTariffRequest): Promise<TariffDto>
  archiveTariff(accessToken: string, id: string): Promise<void>
  restoreTariff(accessToken: string, id: string): Promise<TariffDto>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''
const defaultDictionaryListLimit = 100

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
    throw new Error(problem?.detail ?? 'Не удалось выполнить запрос.')
  }

  if (response.status === 204) {
    return undefined as TResponse
  }

  return response.json()
}

function withQuery(path: string, params: Record<string, string | number | undefined>): string {
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
  getOwners(accessToken, search, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/owners', { search, limit }))
  },
  getOwnersPage(accessToken, search, offset = 0, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/owners/page', { search, offset, limit }))
  },
  createOwner(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/owners', { method: 'POST', body: JSON.stringify(request) })
  },
  updateOwner(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/owners/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveOwner(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/owners/${id}`, { method: 'DELETE' })
  },
  restoreOwner(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/owners/${id}/restore`, { method: 'POST' })
  },
  getGarages(accessToken, search, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/garages', { search, limit }))
  },
  getGaragesPage(accessToken, search, offset = 0, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/garages/page', { search, offset, limit }))
  },
  createGarage(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/garages', { method: 'POST', body: JSON.stringify(request) })
  },
  updateGarage(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/garages/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveGarage(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/garages/${id}`, { method: 'DELETE' })
  },
  restoreGarage(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/garages/${id}/restore`, { method: 'POST' })
  },
  getSupplierGroups(accessToken, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/supplier-groups', { limit }))
  },
  getSupplierGroupsPage(accessToken, offset = 0, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/supplier-groups/page', { offset, limit }))
  },
  createSupplierGroup(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/supplier-groups', { method: 'POST', body: JSON.stringify(request) })
  },
  updateSupplierGroup(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/supplier-groups/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveSupplierGroup(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/supplier-groups/${id}`, { method: 'DELETE' })
  },
  restoreSupplierGroup(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/supplier-groups/${id}/restore`, { method: 'POST' })
  },
  getSuppliers(accessToken, groupId, search, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/suppliers', { groupId, search, limit }))
  },
  getSuppliersPage(accessToken, groupId, search, offset = 0, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/suppliers/page', { groupId, search, offset, limit }))
  },
  createSupplier(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/suppliers', { method: 'POST', body: JSON.stringify(request) })
  },
  updateSupplier(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/suppliers/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveSupplier(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/suppliers/${id}`, { method: 'DELETE' })
  },
  restoreSupplier(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/suppliers/${id}/restore`, { method: 'POST' })
  },
  getIncomeTypes(accessToken, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/income-types', { limit }))
  },
  getIncomeTypesPage(accessToken, offset = 0, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/income-types/page', { offset, limit }))
  },
  createIncomeType(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/income-types', { method: 'POST', body: JSON.stringify(request) })
  },
  updateIncomeType(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/income-types/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveIncomeType(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/income-types/${id}`, { method: 'DELETE' })
  },
  restoreIncomeType(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/income-types/${id}/restore`, { method: 'POST' })
  },
  getExpenseTypes(accessToken, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/expense-types', { limit }))
  },
  getExpenseTypesPage(accessToken, offset = 0, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/expense-types/page', { offset, limit }))
  },
  createExpenseType(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/expense-types', { method: 'POST', body: JSON.stringify(request) })
  },
  updateExpenseType(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/expense-types/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveExpenseType(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/expense-types/${id}`, { method: 'DELETE' })
  },
  restoreExpenseType(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/expense-types/${id}/restore`, { method: 'POST' })
  },
  getTariffs(accessToken, search, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/tariffs', { search, limit }))
  },
  getTariffsPage(accessToken, search, offset = 0, limit = defaultDictionaryListLimit) {
    return requestJson(accessToken, withQuery('/api/dictionaries/tariffs/page', { search, offset, limit }))
  },
  createTariff(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/tariffs', { method: 'POST', body: JSON.stringify(request) })
  },
  updateTariff(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/tariffs/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveTariff(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/tariffs/${id}`, { method: 'DELETE' })
  },
  restoreTariff(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/tariffs/${id}/restore`, { method: 'POST' })
  },
}
