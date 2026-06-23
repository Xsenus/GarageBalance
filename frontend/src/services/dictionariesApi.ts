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
  effectiveFrom: string
  comment: string | null
  isArchived: boolean
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
}

export type DictionaryClient = {
  getOwners(accessToken: string, search?: string): Promise<OwnerDto[]>
  createOwner(accessToken: string, request: UpsertOwnerRequest): Promise<OwnerDto>
  archiveOwner(accessToken: string, id: string): Promise<void>
  getGarages(accessToken: string, search?: string): Promise<GarageDto[]>
  createGarage(accessToken: string, request: UpsertGarageRequest): Promise<GarageDto>
  archiveGarage(accessToken: string, id: string): Promise<void>
  getSupplierGroups(accessToken: string): Promise<SupplierGroupDto[]>
  createSupplierGroup(accessToken: string, request: UpsertSupplierGroupRequest): Promise<SupplierGroupDto>
  archiveSupplierGroup(accessToken: string, id: string): Promise<void>
  getSuppliers(accessToken: string, groupId?: string, search?: string): Promise<SupplierDto[]>
  createSupplier(accessToken: string, request: UpsertSupplierRequest): Promise<SupplierDto>
  archiveSupplier(accessToken: string, id: string): Promise<void>
  getIncomeTypes(accessToken: string): Promise<AccountingTypeDto[]>
  createIncomeType(accessToken: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  archiveIncomeType(accessToken: string, id: string): Promise<void>
  getExpenseTypes(accessToken: string): Promise<AccountingTypeDto[]>
  createExpenseType(accessToken: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  archiveExpenseType(accessToken: string, id: string): Promise<void>
  getTariffs(accessToken: string, search?: string): Promise<TariffDto[]>
  createTariff(accessToken: string, request: UpsertTariffRequest): Promise<TariffDto>
  archiveTariff(accessToken: string, id: string): Promise<void>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://127.0.0.1:5080'

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

function withQuery(path: string, params: Record<string, string | undefined>): string {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value) {
      query.set(key, value)
    }
  }

  const queryString = query.toString()
  return queryString ? `${path}?${queryString}` : path
}

export const dictionariesApi: DictionaryClient = {
  getOwners(accessToken, search) {
    return requestJson(accessToken, withQuery('/api/dictionaries/owners', { search }))
  },
  createOwner(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/owners', { method: 'POST', body: JSON.stringify(request) })
  },
  archiveOwner(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/owners/${id}`, { method: 'DELETE' })
  },
  getGarages(accessToken, search) {
    return requestJson(accessToken, withQuery('/api/dictionaries/garages', { search }))
  },
  createGarage(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/garages', { method: 'POST', body: JSON.stringify(request) })
  },
  archiveGarage(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/garages/${id}`, { method: 'DELETE' })
  },
  getSupplierGroups(accessToken) {
    return requestJson(accessToken, '/api/dictionaries/supplier-groups')
  },
  createSupplierGroup(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/supplier-groups', { method: 'POST', body: JSON.stringify(request) })
  },
  archiveSupplierGroup(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/supplier-groups/${id}`, { method: 'DELETE' })
  },
  getSuppliers(accessToken, groupId, search) {
    return requestJson(accessToken, withQuery('/api/dictionaries/suppliers', { groupId, search }))
  },
  createSupplier(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/suppliers', { method: 'POST', body: JSON.stringify(request) })
  },
  archiveSupplier(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/suppliers/${id}`, { method: 'DELETE' })
  },
  getIncomeTypes(accessToken) {
    return requestJson(accessToken, '/api/dictionaries/income-types')
  },
  createIncomeType(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/income-types', { method: 'POST', body: JSON.stringify(request) })
  },
  archiveIncomeType(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/income-types/${id}`, { method: 'DELETE' })
  },
  getExpenseTypes(accessToken) {
    return requestJson(accessToken, '/api/dictionaries/expense-types')
  },
  createExpenseType(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/expense-types', { method: 'POST', body: JSON.stringify(request) })
  },
  archiveExpenseType(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/expense-types/${id}`, { method: 'DELETE' })
  },
  getTariffs(accessToken, search) {
    return requestJson(accessToken, withQuery('/api/dictionaries/tariffs', { search }))
  },
  createTariff(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/tariffs', { method: 'POST', body: JSON.stringify(request) })
  },
  archiveTariff(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/tariffs/${id}`, { method: 'DELETE' })
  },
}
