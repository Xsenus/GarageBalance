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

export type DictionaryClient = {
  getOwners(accessToken: string, search?: string): Promise<OwnerDto[]>
  createOwner(accessToken: string, request: UpsertOwnerRequest): Promise<OwnerDto>
  getGarages(accessToken: string, search?: string): Promise<GarageDto[]>
  createGarage(accessToken: string, request: UpsertGarageRequest): Promise<GarageDto>
  getSupplierGroups(accessToken: string): Promise<SupplierGroupDto[]>
  createSupplierGroup(accessToken: string, request: UpsertSupplierGroupRequest): Promise<SupplierGroupDto>
  getSuppliers(accessToken: string, groupId?: string, search?: string): Promise<SupplierDto[]>
  createSupplier(accessToken: string, request: UpsertSupplierRequest): Promise<SupplierDto>
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
  getGarages(accessToken, search) {
    return requestJson(accessToken, withQuery('/api/dictionaries/garages', { search }))
  },
  createGarage(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/garages', { method: 'POST', body: JSON.stringify(request) })
  },
  getSupplierGroups(accessToken) {
    return requestJson(accessToken, '/api/dictionaries/supplier-groups')
  },
  createSupplierGroup(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/supplier-groups', { method: 'POST', body: JSON.stringify(request) })
  },
  getSuppliers(accessToken, groupId, search) {
    return requestJson(accessToken, withQuery('/api/dictionaries/suppliers', { groupId, search }))
  },
  createSupplier(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/suppliers', { method: 'POST', body: JSON.stringify(request) })
  },
}
