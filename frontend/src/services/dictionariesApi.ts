import { apiFetch } from './apiFetch'

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
  balance: number
  overdueDebt: number
  ownerPhone: string | null
  version: string
}

export type GarageColumnFilters = {
  number?: string
  peopleCountMin?: number
  peopleCountMax?: number
  floorCountMin?: number
  floorCountMax?: number
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
  debt: number
  chargeServiceSettingId?: string | null
  chargeServiceSettingName?: string | null
  chargeServiceExpenseTypeId?: string | null
  chargeServiceExpenseFundId?: string | null
  chargeServiceExpenseFundName?: string | null
  chargeServiceExpenseFundBalance?: number | null
  expenseFundId?: string | null
  expenseFundName?: string | null
  comment: string | null
  isArchived: boolean
  version: string
}

export type SupplierContactDto = {
  id: string
  supplierId: string
  supplierName: string
  fullName: string
  position: string | null
  phone: string | null
  email: string | null
  status: string
  comment: string | null
  isArchived: boolean
}

export type StaffDepartmentDto = {
  id: string
  name: string
  isArchived: boolean
}

export type StaffMemberDto = {
  id: string
  fullName: string
  departmentId: string
  departmentName: string
  rate: number
  isArchived: boolean
}

export type AccountingTypeDto = {
  id: string
  name: string
  code: string | null
  isSystem: boolean
  isArchived: boolean
  destinationFundId?: string | null
  destinationFundName?: string | null
}

export type CreateOpeningBalanceAdjustmentRequest = {
  effectiveDate: string
  newAmount: number
  reason: string
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
  electricityTiers?: ElectricityTariffTierDto[]
  effectiveFrom: string
  comment: string | null
  isArchived: boolean
  version: string
}

export type IrregularPaymentDto = {
  id: string
  name: string
  amount: number
  isActive: boolean
  isArchived: boolean
  isUsed: boolean
}

export type ChargeServiceSettingDto = {
  id: string
  name: string
  isRegular: boolean
  periodicityMonths: number | null
  accrualStartMonth: number | null
  paymentDueDay: number | null
  paymentDueMonth: number | null
  overdueGraceDays: number
  incomeTypeId: string | null
  expenseTypeId?: string | null
  expenseFundId?: string | null
  tariffId: string | null
  isMetered: boolean
  hasTieredTariff: boolean
  unitName: string | null
  isArchived: boolean
  tariffCalculationBase?: string | null
  version: string
}

export type FeeCampaignDto = {
  id: string
  name: string
  incomeTypeId: string
  incomeTypeName: string
  goal: string | null
  contributionAmount: number
  targetAmount: number
  startsOn: string
  endsOn: string | null
  appliesToAllGarages: boolean
  participantGarageIds: string[]
  overdueGraceDays: number
  isArchived: boolean
  closedAtUtc?: string | null
  isClosedEarly?: boolean
  closureComment?: string | null
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
  version?: string
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
  email?: string | null
  startingBalance: number
  comment?: string
  chargeServiceSettingId?: string | null
  expenseFundId?: string | null
  version?: string
}

export type ElectricityTariffTierDto = {
  id: string
  name: string
  upperBound: number | null
  rate: number
  isCustom: boolean
}

export type UpsertSupplierContactRequest = {
  supplierId: string
  fullName: string
  position?: string
  phone?: string
  email?: string | null
  status: string
  comment?: string
}

export type UpsertStaffDepartmentRequest = {
  name: string
}

export type UpsertStaffMemberRequest = {
  fullName: string
  departmentId: string
  rate: number
}

export type UpsertAccountingTypeRequest = {
  name: string
  code?: string
}

export type UpsertElectricityTariffTierRequest = {
  id?: string
  name: string
  upperBound?: number
  rate: number
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
  electricityTiers?: UpsertElectricityTariffTierRequest[]
  electricityTierChangeReason?: string
  version?: string
}

export type UpsertIrregularPaymentRequest = {
  name: string
  amount: number
  isActive?: boolean
}

export type UpsertChargeServiceSettingRequest = {
  name: string
  isRegular: boolean
  periodicityMonths?: number | null
  accrualStartMonth?: number | null
  paymentDueDay?: number | null
  paymentDueMonth?: number | null
  overdueGraceDays: number
  incomeTypeId?: string | null
  expenseTypeId?: string | null
  expenseFundId?: string | null
  tariffId?: string | null
  isMetered: boolean
  hasTieredTariff: boolean
  unitName?: string | null
  version?: string
}

export type CreateChargeServiceWithTariffRequest = {
  service: UpsertChargeServiceSettingRequest
  rate: number
  effectiveFrom: string
}

export type CreatedChargeServiceWithTariffDto = {
  service: ChargeServiceSettingDto
  tariff: TariffDto
}

export type UpdateChargeServiceWithTariffRequest = {
  service: UpsertChargeServiceSettingRequest
  rate: number
  tariffMode?: 'regular' | 'metered' | 'metered_tiered' | null
  effectiveFrom?: string | null
  electricityTiers?: UpsertElectricityTariffTierRequest[] | null
  changeReason?: string | null
  calculationBase?: string | null
  tariffVersion?: string
}

export type UpdatedChargeServiceWithTariffDto = {
  service: ChargeServiceSettingDto
  tariff: TariffDto
}

export type UpsertFeeCampaignRequest = {
  name: string
  incomeTypeId: string
  goal?: string | null
  contributionAmount: number
  targetAmount: number
  startsOn: string
  endsOn?: string | null
  appliesToAllGarages: boolean
  participantGarageIds?: string[] | null
  overdueGraceDays: number
}

export type DictionaryClient = {
  getOwners(accessToken: string, search?: string, limit?: number, includeArchived?: boolean, signal?: AbortSignal): Promise<OwnerDto[]>
  getOwnersPage?(accessToken: string, search?: string, offset?: number, limit?: number, includeArchived?: boolean, signal?: AbortSignal): Promise<PagedResult<OwnerDto>>
  createOwner(accessToken: string, request: UpsertOwnerRequest): Promise<OwnerDto>
  updateOwner(accessToken: string, id: string, request: UpsertOwnerRequest): Promise<OwnerDto>
  archiveOwner(accessToken: string, id: string, reason: string): Promise<void>
  restoreOwner(accessToken: string, id: string): Promise<OwnerDto>
  getGarages(accessToken: string, search?: string, limit?: number, includeArchived?: boolean, signal?: AbortSignal): Promise<GarageDto[]>
  getGaragesPage?(accessToken: string, search?: string, offset?: number, limit?: number, includeArchived?: boolean, sortBy?: string, sortDirection?: string, debtorsOnly?: boolean, filters?: GarageColumnFilters, signal?: AbortSignal): Promise<PagedResult<GarageDto>>
  createGarage(accessToken: string, request: UpsertGarageRequest): Promise<GarageDto>
  updateGarage(accessToken: string, id: string, request: UpsertGarageRequest): Promise<GarageDto>
  archiveGarage(accessToken: string, id: string, reason: string): Promise<void>
  restoreGarage(accessToken: string, id: string): Promise<GarageDto>
  adjustGarageOpeningBalance?(accessToken: string, id: string, request: CreateOpeningBalanceAdjustmentRequest): Promise<unknown>
  getSupplierGroups(accessToken: string, search?: string, limit?: number, includeArchived?: boolean, signal?: AbortSignal): Promise<SupplierGroupDto[]>
  getSupplierGroupsPage?(accessToken: string, search?: string, offset?: number, limit?: number, includeArchived?: boolean, signal?: AbortSignal): Promise<PagedResult<SupplierGroupDto>>
  createSupplierGroup(accessToken: string, request: UpsertSupplierGroupRequest): Promise<SupplierGroupDto>
  updateSupplierGroup(accessToken: string, id: string, request: UpsertSupplierGroupRequest): Promise<SupplierGroupDto>
  archiveSupplierGroup(accessToken: string, id: string, reason: string): Promise<void>
  restoreSupplierGroup(accessToken: string, id: string): Promise<SupplierGroupDto>
  getSuppliers(accessToken: string, groupId?: string, search?: string, limit?: number, includeArchived?: boolean, signal?: AbortSignal): Promise<SupplierDto[]>
  getSuppliersPage?(accessToken: string, groupId?: string, search?: string, offset?: number, limit?: number, includeArchived?: boolean, sortBy?: string, sortDirection?: string, signal?: AbortSignal): Promise<PagedResult<SupplierDto>>
  createSupplier(accessToken: string, request: UpsertSupplierRequest): Promise<SupplierDto>
  updateSupplier(accessToken: string, id: string, request: UpsertSupplierRequest): Promise<SupplierDto>
  archiveSupplier(accessToken: string, id: string, reason: string): Promise<void>
  restoreSupplier(accessToken: string, id: string): Promise<SupplierDto>
  adjustSupplierOpeningBalance?(accessToken: string, id: string, request: CreateOpeningBalanceAdjustmentRequest): Promise<unknown>
  getSupplierContacts(accessToken: string, supplierId?: string, search?: string, limit?: number, includeArchived?: boolean): Promise<SupplierContactDto[]>
  createSupplierContact(accessToken: string, request: UpsertSupplierContactRequest): Promise<SupplierContactDto>
  updateSupplierContact(accessToken: string, id: string, request: UpsertSupplierContactRequest): Promise<SupplierContactDto>
  archiveSupplierContact(accessToken: string, id: string, reason: string): Promise<void>
  restoreSupplierContact(accessToken: string, id: string): Promise<SupplierContactDto>
  getStaffDepartments(accessToken: string, limit?: number, includeArchived?: boolean): Promise<StaffDepartmentDto[]>
  createStaffDepartment(accessToken: string, request: UpsertStaffDepartmentRequest): Promise<StaffDepartmentDto>
  updateStaffDepartment(accessToken: string, id: string, request: UpsertStaffDepartmentRequest): Promise<StaffDepartmentDto>
  archiveStaffDepartment(accessToken: string, id: string, reason: string): Promise<void>
  restoreStaffDepartment(accessToken: string, id: string): Promise<StaffDepartmentDto>
  getStaffMembers(accessToken: string, departmentId?: string, search?: string, limit?: number, includeArchived?: boolean, signal?: AbortSignal): Promise<StaffMemberDto[]>
  getStaffMembersPage?(accessToken: string, departmentId?: string, search?: string, offset?: number, limit?: number, includeArchived?: boolean, sortBy?: string, sortDirection?: string, signal?: AbortSignal): Promise<PagedResult<StaffMemberDto>>
  createStaffMember(accessToken: string, request: UpsertStaffMemberRequest): Promise<StaffMemberDto>
  updateStaffMember(accessToken: string, id: string, request: UpsertStaffMemberRequest): Promise<StaffMemberDto>
  archiveStaffMember(accessToken: string, id: string, reason: string): Promise<void>
  restoreStaffMember(accessToken: string, id: string): Promise<StaffMemberDto>
  getIncomeTypes(accessToken: string, search?: string, limit?: number, includeArchived?: boolean, signal?: AbortSignal): Promise<AccountingTypeDto[]>
  getIncomeTypesPage?(accessToken: string, search?: string, offset?: number, limit?: number, includeArchived?: boolean, signal?: AbortSignal): Promise<PagedResult<AccountingTypeDto>>
  createIncomeType(accessToken: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  updateIncomeType(accessToken: string, id: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  archiveIncomeType(accessToken: string, id: string, reason: string): Promise<void>
  restoreIncomeType(accessToken: string, id: string): Promise<AccountingTypeDto>
  getExpenseTypes(accessToken: string, search?: string, limit?: number, includeArchived?: boolean, signal?: AbortSignal): Promise<AccountingTypeDto[]>
  getExpenseTypesPage?(accessToken: string, search?: string, offset?: number, limit?: number, includeArchived?: boolean, signal?: AbortSignal): Promise<PagedResult<AccountingTypeDto>>
  createExpenseType(accessToken: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  updateExpenseType(accessToken: string, id: string, request: UpsertAccountingTypeRequest): Promise<AccountingTypeDto>
  archiveExpenseType(accessToken: string, id: string, reason: string): Promise<void>
  restoreExpenseType(accessToken: string, id: string): Promise<AccountingTypeDto>
  getTariffs(accessToken: string, search?: string, limit?: number, includeArchived?: boolean, signal?: AbortSignal): Promise<TariffDto[]>
  getTariffsPage?(accessToken: string, search?: string, offset?: number, limit?: number, includeArchived?: boolean, signal?: AbortSignal): Promise<PagedResult<TariffDto>>
  createTariff(accessToken: string, request: UpsertTariffRequest): Promise<TariffDto>
  updateTariff(accessToken: string, id: string, request: UpsertTariffRequest): Promise<TariffDto>
  archiveTariff(accessToken: string, id: string, reason: string): Promise<void>
  restoreTariff(accessToken: string, id: string): Promise<TariffDto>
  getChargeServiceSettings(accessToken: string, search?: string, limit?: number, includeArchived?: boolean): Promise<ChargeServiceSettingDto[]>
  createChargeServiceWithTariff(accessToken: string, request: CreateChargeServiceWithTariffRequest): Promise<CreatedChargeServiceWithTariffDto>
  createChargeServiceSetting(accessToken: string, request: UpsertChargeServiceSettingRequest): Promise<ChargeServiceSettingDto>
  updateChargeServiceSetting(accessToken: string, id: string, request: UpsertChargeServiceSettingRequest): Promise<ChargeServiceSettingDto>
  updateChargeServiceWithTariff(accessToken: string, id: string, request: UpdateChargeServiceWithTariffRequest): Promise<UpdatedChargeServiceWithTariffDto>
  archiveChargeServiceSetting(accessToken: string, id: string, reason: string): Promise<void>
  restoreChargeServiceSetting(accessToken: string, id: string): Promise<ChargeServiceSettingDto>
  getFeeCampaigns(accessToken: string, search?: string, limit?: number, includeArchived?: boolean): Promise<FeeCampaignDto[]>
  createFeeCampaign(accessToken: string, request: UpsertFeeCampaignRequest): Promise<FeeCampaignDto>
  updateFeeCampaign(accessToken: string, id: string, request: UpsertFeeCampaignRequest): Promise<FeeCampaignDto>
  closeFeeCampaign(accessToken: string, id: string, request: { comment?: string | null }): Promise<FeeCampaignDto>
  archiveFeeCampaign(accessToken: string, id: string, reason: string): Promise<void>
  restoreFeeCampaign(accessToken: string, id: string): Promise<FeeCampaignDto>
  getIrregularPayments(accessToken: string, search?: string, limit?: number, includeArchived?: boolean): Promise<IrregularPaymentDto[]>
  createIrregularPayment(accessToken: string, request: UpsertIrregularPaymentRequest): Promise<IrregularPaymentDto>
  updateIrregularPayment(accessToken: string, id: string, request: UpsertIrregularPaymentRequest): Promise<IrregularPaymentDto>
  setIrregularPaymentStatus(accessToken: string, id: string, request: { isActive: boolean; reason?: string }): Promise<IrregularPaymentDto>
  archiveIrregularPayment(accessToken: string, id: string, reason: string): Promise<void>
  restoreIrregularPayment(accessToken: string, id: string): Promise<IrregularPaymentDto>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''
const defaultDictionaryListLimit = 100
const dictionaryResponseCacheLifetimeMs = 60_000

type DictionaryCacheEntry = {
  accessToken: string
  expiresAt: number
  response: Promise<unknown>
  tag: string
}

const dictionaryResponseCache = new Map<string, DictionaryCacheEntry>()
const dictionaryCacheVersions = new Map<string, number>()

const dictionaryCacheDependencies: Record<string, string[]> = {
  owners: ['owners', 'garages'],
  garages: ['garages'],
  'supplier-groups': ['supplier-groups', 'suppliers'],
  suppliers: ['suppliers', 'supplier-contacts'],
  'supplier-contacts': ['supplier-contacts'],
  'staff-departments': ['staff-departments', 'staff-members'],
  'staff-members': ['staff-members'],
  'income-types': ['income-types', 'charge-services', 'fee-campaigns'],
  'expense-types': ['expense-types', 'charge-services', 'suppliers'],
  tariffs: ['tariffs', 'charge-services'],
  'charge-services': ['charge-services', 'tariffs', 'suppliers'],
  'fee-campaigns': ['fee-campaigns'],
  'irregular-payments': ['irregular-payments'],
}

export function clearDictionaryResponseCache() {
  dictionaryResponseCache.clear()
  dictionaryCacheVersions.clear()
}

function getDictionaryCacheTag(path: string): string | null {
  const match = /^\/api\/dictionaries\/([^/?]+)/.exec(path)
  return match?.[1] ?? null
}

function getDictionaryCacheVersion(accessToken: string, tag: string): number {
  return dictionaryCacheVersions.get(`${accessToken}\n${tag}`) ?? 0
}

function invalidateDictionaryResponseCache(accessToken: string, mutationTag: string | null) {
  if (!mutationTag) {
    return
  }

  const invalidatedTags = dictionaryCacheDependencies[mutationTag] ?? [mutationTag]
  for (const tag of invalidatedTags) {
    const versionKey = `${accessToken}\n${tag}`
    dictionaryCacheVersions.set(versionKey, getDictionaryCacheVersion(accessToken, tag) + 1)
  }

  for (const [cacheKey, entry] of dictionaryResponseCache) {
    if (entry.accessToken === accessToken && invalidatedTags.includes(entry.tag)) {
      dictionaryResponseCache.delete(cacheKey)
    }
  }
}

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
  const method = init?.method?.toUpperCase() ?? 'GET'
  const cacheTag = getDictionaryCacheTag(path)
  const cacheVersion = cacheTag ? getDictionaryCacheVersion(accessToken, cacheTag) : 0
  const cacheKey = `${accessToken}\n${cacheTag ?? ''}\n${cacheVersion}\n${path}`
  if (method === 'GET' && cacheTag && !init?.signal) {
    const cached = dictionaryResponseCache.get(cacheKey)
    if (cached && cached.expiresAt > Date.now()) {
      return cached.response as Promise<TResponse>
    }
    if (cached) {
      dictionaryResponseCache.delete(cacheKey)
    }
  }

  const responsePromise = apiFetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
      ...init?.headers,
    },
  }).then(async (response) => {
    if (!response.ok) {
      const problem = await response.json().catch(() => null)
      const code = typeof problem?.code === 'string' ? problem.code : typeof problem?.title === 'string' ? problem.title : null
      throw new DictionaryApiError(code, problem?.detail ?? 'Не удалось выполнить запрос.', response.status)
    }

    if (response.status === 204) {
      if (method !== 'GET') {
        invalidateDictionaryResponseCache(accessToken, cacheTag)
      }
      return undefined as TResponse
    }

    const result = await response.json() as TResponse
    if (method !== 'GET') {
      invalidateDictionaryResponseCache(accessToken, cacheTag)
    }
    return result
  })

  if (method === 'GET' && cacheTag && !init?.signal) {
    dictionaryResponseCache.set(cacheKey, {
      accessToken,
      expiresAt: Date.now() + dictionaryResponseCacheLifetimeMs,
      response: responsePromise,
      tag: cacheTag,
    })
    responsePromise.catch(() => {
      if (dictionaryResponseCache.get(cacheKey)?.response === responsePromise) {
        dictionaryResponseCache.delete(cacheKey)
      }
    })
  }

  return responsePromise
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
  getOwners(accessToken, search, limit = defaultDictionaryListLimit, includeArchived = false, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/owners', { search, limit, includeArchived: includeArchived || undefined }), { signal })
  },
  getOwnersPage(accessToken, search, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/owners/page', { search, offset, limit, includeArchived: includeArchived || undefined }), { signal })
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
  getGarages(accessToken, search, limit = defaultDictionaryListLimit, includeArchived = false, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/garages', { search, limit, includeArchived: includeArchived || undefined }), { signal })
  },
  getGaragesPage(accessToken, search, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false, sortBy, sortDirection, debtorsOnly = false, filters = {}, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/garages/page', { search, offset, limit, includeArchived: includeArchived || undefined, sortBy, sortDirection, debtorsOnly: debtorsOnly || undefined, ...filters }), { signal })
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
  adjustGarageOpeningBalance(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/garages/${id}/opening-balance-adjustments`, { method: 'POST', body: JSON.stringify(request) })
  },
  getSupplierGroups(accessToken, search, limit = defaultDictionaryListLimit, includeArchived = false, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/supplier-groups', { search, limit, includeArchived: includeArchived || undefined }), { signal })
  },
  getSupplierGroupsPage(accessToken, search, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/supplier-groups/page', { search, offset, limit, includeArchived: includeArchived || undefined }), { signal })
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
  getSuppliers(accessToken, groupId, search, limit = defaultDictionaryListLimit, includeArchived = false, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/suppliers', { groupId, search, limit, includeArchived: includeArchived || undefined }), { signal })
  },
  getSuppliersPage(accessToken, groupId, search, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false, sortBy, sortDirection, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/suppliers/page', { groupId, search, offset, limit, includeArchived: includeArchived || undefined, sortBy, sortDirection }), { signal })
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
  adjustSupplierOpeningBalance(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/suppliers/${id}/opening-balance-adjustments`, { method: 'POST', body: JSON.stringify(request) })
  },
  getSupplierContacts(accessToken, supplierId, search, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/supplier-contacts', { supplierId, search, limit, includeArchived: includeArchived || undefined }))
  },
  createSupplierContact(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/supplier-contacts', { method: 'POST', body: JSON.stringify(request) })
  },
  updateSupplierContact(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/supplier-contacts/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveSupplierContact(accessToken, id, reason) {
    return requestJson(accessToken, `/api/dictionaries/supplier-contacts/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  restoreSupplierContact(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/supplier-contacts/${id}/restore`, { method: 'POST' })
  },
  getStaffDepartments(accessToken, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/staff-departments', { limit, includeArchived: includeArchived || undefined }))
  },
  createStaffDepartment(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/staff-departments', { method: 'POST', body: JSON.stringify(request) })
  },
  updateStaffDepartment(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/staff-departments/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveStaffDepartment(accessToken, id, reason) {
    return requestJson(accessToken, `/api/dictionaries/staff-departments/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  restoreStaffDepartment(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/staff-departments/${id}/restore`, { method: 'POST' })
  },
  getStaffMembers(accessToken, departmentId, search, limit = defaultDictionaryListLimit, includeArchived = false, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/staff-members', { departmentId, search, limit, includeArchived: includeArchived || undefined }), { signal })
  },
  getStaffMembersPage(accessToken, departmentId, search, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false, sortBy, sortDirection, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/staff-members/page', { departmentId, search, offset, limit, includeArchived: includeArchived || undefined, sortBy, sortDirection }), { signal })
  },
  createStaffMember(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/staff-members', { method: 'POST', body: JSON.stringify(request) })
  },
  updateStaffMember(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/staff-members/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveStaffMember(accessToken, id, reason) {
    return requestJson(accessToken, `/api/dictionaries/staff-members/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  restoreStaffMember(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/staff-members/${id}/restore`, { method: 'POST' })
  },
  getIncomeTypes(accessToken, search, limit = defaultDictionaryListLimit, includeArchived = false, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/income-types', { search, limit, includeArchived: includeArchived || undefined }), { signal })
  },
  getIncomeTypesPage(accessToken, search, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/income-types/page', { search, offset, limit, includeArchived: includeArchived || undefined }), { signal })
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
  getExpenseTypes(accessToken, search, limit = defaultDictionaryListLimit, includeArchived = false, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/expense-types', { search, limit, includeArchived: includeArchived || undefined }), { signal })
  },
  getExpenseTypesPage(accessToken, search, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/expense-types/page', { search, offset, limit, includeArchived: includeArchived || undefined }), { signal })
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
  getTariffs(accessToken, search, limit = defaultDictionaryListLimit, includeArchived = false, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/tariffs', { search, limit, includeArchived: includeArchived || undefined }), { signal })
  },
  getTariffsPage(accessToken, search, offset = 0, limit = defaultDictionaryListLimit, includeArchived = false, signal) {
    return requestJson(accessToken, withQuery('/api/dictionaries/tariffs/page', { search, offset, limit, includeArchived: includeArchived || undefined }), { signal })
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
  getChargeServiceSettings(accessToken, search, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/charge-services', { search, limit, includeArchived: includeArchived || undefined }))
  },
  createChargeServiceWithTariff(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/charge-services/with-tariff', { method: 'POST', body: JSON.stringify(request) })
  },
  createChargeServiceSetting(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/charge-services', { method: 'POST', body: JSON.stringify(request) })
  },
  updateChargeServiceSetting(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/charge-services/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  updateChargeServiceWithTariff(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/charge-services/${id}/with-tariff`, { method: 'PUT', body: JSON.stringify(request) })
  },
  archiveChargeServiceSetting(accessToken, id, reason) {
    return requestJson(accessToken, `/api/dictionaries/charge-services/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  restoreChargeServiceSetting(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/charge-services/${id}/restore`, { method: 'POST' })
  },
  getFeeCampaigns(accessToken, search, limit = defaultDictionaryListLimit, includeArchived = false) {
    return requestJson(accessToken, withQuery('/api/dictionaries/fee-campaigns', { search, limit, includeArchived: includeArchived || undefined }))
  },
  createFeeCampaign(accessToken, request) {
    return requestJson(accessToken, '/api/dictionaries/fee-campaigns', { method: 'POST', body: JSON.stringify(request) })
  },
  updateFeeCampaign(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/fee-campaigns/${id}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  closeFeeCampaign(accessToken, id, request) {
    return requestJson(accessToken, `/api/dictionaries/fee-campaigns/${id}/close`, { method: 'POST', body: JSON.stringify(request) })
  },
  archiveFeeCampaign(accessToken, id, reason) {
    return requestJson(accessToken, `/api/dictionaries/fee-campaigns/${id}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  restoreFeeCampaign(accessToken, id) {
    return requestJson(accessToken, `/api/dictionaries/fee-campaigns/${id}/restore`, { method: 'POST' })
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
