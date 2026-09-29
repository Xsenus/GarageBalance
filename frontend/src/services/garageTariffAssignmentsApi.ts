import { requestDictionaryJson } from './dictionariesApi'
import type { ElectricityTariffTierDto, PagedResult, UpsertElectricityTariffTierRequest } from './dictionariesApi'
import { invalidateDictionaryResponseCache } from './dictionaryResponseCache'

export type GarageTariffAssignment = {
  id: string; garageId: string; garageNumber: string; serviceId: string; tariffId: string
  calculationBase: string; rate: number; tiers: ElectricityTariffTierDto[]
  effectiveFrom: string; effectiveTo: string | null; comment: string | null; isArchived: boolean; version: string
}
export type GarageTariffTerms = {
  effectiveFrom: string; effectiveTo: string | null; rate: number
  tiers: UpsertElectricityTariffTierRequest[] | null; comment: string | null; reason: string | null
}
export type CreateGarageTariffs = GarageTariffTerms & { garageIds: string[]; serviceVersion: string }
export type UpdateGarageTariff = GarageTariffTerms & { version: string; serviceVersion: string }
export type GarageTariffQuery = { garageId?: string; includeArchived?: boolean; offset?: number; limit?: number }
const path = (serviceId: string) => `/api/dictionaries/charge-services/${encodeURIComponent(serviceId)}/garage-tariffs`
async function mutate<T>(token: string, url: string, method: string, body: unknown, signal?: AbortSignal): Promise<T> {
  const result = await requestDictionaryJson<T>(token, url, { method, body: JSON.stringify(body), signal })
  invalidateDictionaryResponseCache(token, 'garages')
  return result
}
export const garageTariffAssignmentsApi = {
  getPage(token: string, serviceId: string, params: GarageTariffQuery = {}, signal?: AbortSignal) {
    const query = new URLSearchParams()
    for (const [key, value] of Object.entries(params)) if (value !== undefined && value !== '') query.set(key, String(value))
    return requestDictionaryJson<PagedResult<GarageTariffAssignment>>(token, `${path(serviceId)}${query.size ? `?${query}` : ''}`, { signal })
  },
  create(token: string, serviceId: string, request: CreateGarageTariffs, signal?: AbortSignal) {
    return mutate<GarageTariffAssignment[]>(token, path(serviceId), 'POST', request, signal)
  },
  update(token: string, serviceId: string, id: string, request: UpdateGarageTariff, signal?: AbortSignal) {
    return mutate<GarageTariffAssignment>(token, `${path(serviceId)}/${encodeURIComponent(id)}`, 'PUT', request, signal)
  },
  archive(token: string, serviceId: string, id: string, request: { version: string; reason: string | null }, signal?: AbortSignal) {
    return mutate<GarageTariffAssignment>(token, `${path(serviceId)}/${encodeURIComponent(id)}`, 'DELETE', request, signal)
  },
}
export type GarageTariffAssignmentsClient = typeof garageTariffAssignmentsApi
