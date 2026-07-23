export type FundDto = {
  id: string
  name: string
  balance: number
  availableToDistribute: number
  sortOrder: number
  allowOperations: boolean
  isSystem: boolean
  linkedServices: FundLinkedServiceDto[]
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
  reason: string
}

export type UpdateFundOperationRequest = {
  amount: number
  reason: string
}

export type UpsertFundRequest = {
  name: string
}

export type CancelFundOperationRequest = {
  reason: string
}

export type FundsClient = {
  getFunds(accessToken: string, signal?: AbortSignal): Promise<FundDto[]>
  createFund(accessToken: string, request: UpsertFundRequest): Promise<FundDto>
  updateFund(accessToken: string, fundId: string, request: UpsertFundRequest): Promise<FundDto>
  getOperations(accessToken: string, query?: { limit?: number; includeCanceled?: boolean }, signal?: AbortSignal): Promise<FundOperationDto[]>
  getOperationsPage?(accessToken: string, query?: { offset?: number; limit?: number; includeCanceled?: boolean }, signal?: AbortSignal): Promise<FundOperationPageDto>
  createOperation(accessToken: string, fundId: string, request: CreateFundOperationRequest): Promise<FundOperationDto>
  updateOperation(accessToken: string, operationId: string, request: UpdateFundOperationRequest): Promise<FundOperationDto>
  cancelOperation(accessToken: string, operationId: string, request: CancelFundOperationRequest): Promise<FundOperationDto>
  restoreOperation(accessToken: string, operationId: string): Promise<FundOperationDto>
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
    throw new Error(problem?.detail ?? 'Не удалось выполнить операцию фонда.')
  }

  return response.json()
}

export const fundsApi: FundsClient = {
  getFunds(accessToken, signal) {
    return requestJson(accessToken, '/api/funds', { signal })
  },
  createFund(accessToken, request) {
    return requestJson(accessToken, '/api/funds', { method: 'POST', body: JSON.stringify(request) })
  },
  updateFund(accessToken, fundId, request) {
    return requestJson(accessToken, `/api/funds/${fundId}`, { method: 'PUT', body: JSON.stringify(request) })
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
  createOperation(accessToken, fundId, request) {
    return requestJson(accessToken, `/api/funds/${fundId}/operations`, { method: 'POST', body: JSON.stringify(request) })
  },
  updateOperation(accessToken, operationId, request) {
    return requestJson(accessToken, `/api/funds/operations/${operationId}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  cancelOperation(accessToken, operationId, request) {
    return requestJson(accessToken, `/api/funds/operations/${operationId}/cancel`, { method: 'POST', body: JSON.stringify(request) })
  },
  restoreOperation(accessToken, operationId) {
    return requestJson(accessToken, `/api/funds/operations/${operationId}/restore`, { method: 'POST' })
  },
}
