import { authenticatedApiFetch, authenticatedJsonApiFetch, readApiErrorMessage } from './authenticatedApiFetch'

export type FundDto = {
  id: string
  name: string
  balance: number
  availableToDistribute: number
  sortOrder: number
  allowOperations: boolean
  isSystem: boolean
  linkedServices: FundLinkedServiceDto[]
  version: string
}

export type FundOptionDto = {
  id: string
  name: string
  allowOperations: boolean
}

export type FundLinkedServiceDto = {
  id: string
  name: string
}

export type FundOperationDto = {
  id: string
  fundId: string
  fundName: string
  operationKind: 'deposit' | 'withdraw'
  amount: number
  balanceBefore: number
  balanceAfter: number
  reason: string
  createdAtUtc: string
  isCanceled: boolean
  isAutomaticIncomeAssignment?: boolean
}

export type FundOperationPageDto = {
  items: FundOperationDto[]
  totalCount: number
  offset: number
  limit: number
}

export type CreateFundOperationRequest = {
  operationKind: 'deposit' | 'withdraw'
  amount: number
  reason?: string
}

export type UpdateFundOperationRequest = {
  amount: number
  reason: string
}

export type UpsertFundRequest = {
  name: string
  version?: string
}

export type DeleteFundRequest = {
  reason: string
}

export type CancelFundOperationRequest = {
  reason: string
}

export type FundsClient = {
  getFunds(accessToken: string, signal?: AbortSignal): Promise<FundDto[]>
  getFundOptions(accessToken: string, signal?: AbortSignal): Promise<FundOptionDto[]>
  createFund(accessToken: string, request: UpsertFundRequest): Promise<FundDto>
  updateFund(accessToken: string, fundId: string, request: UpsertFundRequest): Promise<FundDto>
  deleteFund(accessToken: string, fundId: string, request: DeleteFundRequest): Promise<void>
  getOperations(accessToken: string, query?: { limit?: number; includeCanceled?: boolean }, signal?: AbortSignal): Promise<FundOperationDto[]>
  getOperationsPage?(accessToken: string, query?: { offset?: number; limit?: number; includeCanceled?: boolean }, signal?: AbortSignal): Promise<FundOperationPageDto>
  createOperation(accessToken: string, fundId: string, request: CreateFundOperationRequest): Promise<FundOperationDto>
  updateOperation(accessToken: string, operationId: string, request: UpdateFundOperationRequest): Promise<FundOperationDto>
  cancelOperation(accessToken: string, operationId: string, request: CancelFundOperationRequest): Promise<FundOperationDto>
  restoreOperation(accessToken: string, operationId: string): Promise<FundOperationDto>
}

const fundsResponseCacheLifetimeMs = 60_000

type FundsCacheEntry = {
  expiresAt: number
  response: Promise<FundDto[]>
}

const fundsResponseCache = new Map<string, FundsCacheEntry>()
const fundsCacheVersions = new Map<string, number>()

export function clearFundsResponseCache() {
  fundsResponseCache.clear()
  fundsCacheVersions.clear()
}

function getFundsCacheVersion(accessToken: string): number {
  return fundsCacheVersions.get(accessToken) ?? 0
}

function invalidateFundsResponseCache(accessToken: string) {
  fundsCacheVersions.set(accessToken, getFundsCacheVersion(accessToken) + 1)
  for (const cacheKey of fundsResponseCache.keys()) {
    if (cacheKey.startsWith(`${accessToken}\n`)) {
      fundsResponseCache.delete(cacheKey)
    }
  }
}

function getCachedFunds(accessToken: string): Promise<FundDto[]> {
  const cacheKey = `${accessToken}\n${getFundsCacheVersion(accessToken)}`
  const cached = fundsResponseCache.get(cacheKey)
  if (cached && cached.expiresAt > Date.now()) {
    return cached.response
  }
  if (cached) {
    fundsResponseCache.delete(cacheKey)
  }

  const response = requestJson<FundDto[]>(accessToken, '/api/funds')
  fundsResponseCache.set(cacheKey, {
    expiresAt: Date.now() + fundsResponseCacheLifetimeMs,
    response,
  })
  response.catch(() => {
    if (fundsResponseCache.get(cacheKey)?.response === response) {
      fundsResponseCache.delete(cacheKey)
    }
  })
  return response
}

async function requestJson<TResponse>(accessToken: string, path: string, init?: RequestInit): Promise<TResponse> {
  const response = await authenticatedJsonApiFetch(accessToken, path, init)

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, 'Не удалось выполнить операцию фонда.'))
  }

  return response.json()
}

async function requestVoid(accessToken: string, path: string, init?: RequestInit): Promise<void> {
  const response = await authenticatedApiFetch(accessToken, path, init)

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, 'Не удалось выполнить операцию фонда.'))
  }
}

export const fundsApi: FundsClient = {
  getFunds(accessToken, signal) {
    return signal
      ? requestJson(accessToken, '/api/funds', { signal })
      : getCachedFunds(accessToken)
  },
  getFundOptions(accessToken, signal) {
    return requestJson<FundOptionDto[]>(accessToken, '/api/funds/options', signal ? { signal } : undefined)
  },
  async createFund(accessToken, request) {
    const result = await requestJson<FundDto>(accessToken, '/api/funds', { method: 'POST', body: JSON.stringify(request) })
    invalidateFundsResponseCache(accessToken)
    return result
  },
  async updateFund(accessToken, fundId, request) {
    const result = await requestJson<FundDto>(accessToken, `/api/funds/${fundId}`, { method: 'PUT', body: JSON.stringify(request) })
    invalidateFundsResponseCache(accessToken)
    return result
  },
  async deleteFund(accessToken, fundId, request) {
    await requestVoid(accessToken, `/api/funds/${fundId}`, {
      method: 'DELETE',
      body: JSON.stringify(request),
      headers: { 'Content-Type': 'application/json' },
    })
    invalidateFundsResponseCache(accessToken)
  },
  getOperations(accessToken, query = {}, signal) {
    const search = new URLSearchParams()
    if (query.limit !== undefined) {
      search.set('limit', String(query.limit))
    }
    if (query.includeCanceled !== undefined) {
      search.set('includeCanceled', String(query.includeCanceled))
    }
    const queryString = search.toString()
    const suffix = queryString ? `?${queryString}` : ''
    return requestJson(accessToken, `/api/funds/operations${suffix}`, { signal })
  },
  getOperationsPage(accessToken, query = {}, signal) {
    const search = new URLSearchParams()
    search.set('offset', String(query.offset ?? 0))
    search.set('limit', String(query.limit ?? 25))
    search.set('includeCanceled', String(query.includeCanceled ?? false))
    return requestJson(accessToken, `/api/funds/operations/page?${search.toString()}`, { signal })
  },
  async createOperation(accessToken, fundId, request) {
    const result = await requestJson<FundOperationDto>(accessToken, `/api/funds/${fundId}/operations`, { method: 'POST', body: JSON.stringify(request) })
    invalidateFundsResponseCache(accessToken)
    return result
  },
  async updateOperation(accessToken, operationId, request) {
    const result = await requestJson<FundOperationDto>(accessToken, `/api/funds/operations/${operationId}`, { method: 'PUT', body: JSON.stringify(request) })
    invalidateFundsResponseCache(accessToken)
    return result
  },
  async cancelOperation(accessToken, operationId, request) {
    const result = await requestJson<FundOperationDto>(accessToken, `/api/funds/operations/${operationId}/cancel`, { method: 'POST', body: JSON.stringify(request) })
    invalidateFundsResponseCache(accessToken)
    return result
  },
  async restoreOperation(accessToken, operationId) {
    const result = await requestJson<FundOperationDto>(accessToken, `/api/funds/operations/${operationId}/restore`, { method: 'POST' })
    invalidateFundsResponseCache(accessToken)
    return result
  },
}
