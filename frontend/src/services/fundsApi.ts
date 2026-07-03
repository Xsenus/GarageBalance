export type FundDto = {
  id: string
  name: string
  balance: number
  sortOrder: number
  allowOperations: boolean
  isSystem: boolean
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
}

export type CreateFundOperationRequest = {
  operationKind: 'deposit' | 'withdraw'
  amount: number
  reason: string
}

export type FundsClient = {
  getFunds(accessToken: string): Promise<FundDto[]>
  createOperation(accessToken: string, fundId: string, request: CreateFundOperationRequest): Promise<FundOperationDto>
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
  getFunds(accessToken) {
    return requestJson(accessToken, '/api/funds')
  },
  createOperation(accessToken, fundId, request) {
    return requestJson(accessToken, `/api/funds/${fundId}/operations`, { method: 'POST', body: JSON.stringify(request) })
  },
}
