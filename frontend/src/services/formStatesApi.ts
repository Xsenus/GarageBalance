export type FormStateDto<TPayload = unknown> = {
  scope: string
  payload: TPayload
  updatedAtUtc: string
  updatedByUserId: string | null
}

export type UpsertFormStateRequest<TPayload = unknown> = {
  payload: TPayload
  summary?: string
}

export type FormStateClient = {
  getState<TPayload>(accessToken: string, scope: string): Promise<FormStateDto<TPayload> | null>
  saveState<TPayload>(accessToken: string, scope: string, request: UpsertFormStateRequest<TPayload>): Promise<FormStateDto<TPayload>>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

export class FormStateApiError extends Error {
  readonly code: string | null
  readonly status: number

  constructor(code: string | null, message: string, status: number) {
    super(message)
    this.name = 'FormStateApiError'
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
    throw new FormStateApiError(code, problem?.detail ?? 'Не удалось сохранить состояние формы.', response.status)
  }

  if (response.status === 204) {
    return null as TResponse
  }

  return response.json()
}

export const formStatesApi: FormStateClient = {
  getState(accessToken, scope) {
    return requestJson(accessToken, `/api/form-states/${encodeURIComponent(scope)}`)
  },
  saveState(accessToken, scope, request) {
    return requestJson(accessToken, `/api/form-states/${encodeURIComponent(scope)}`, { method: 'PUT', body: JSON.stringify(request) })
  },
}
