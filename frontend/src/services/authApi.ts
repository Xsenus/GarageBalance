export type CurrentUserDto = {
  id: string
  email: string
  displayName: string
  roles: string[]
  permissions: string[]
}

export type AuthResponse = {
  accessToken: string
  expiresAtUtc: string
  user: CurrentUserDto
}

export type BootstrapAdminRequest = {
  email: string
  password: string
  displayName: string
}

export type LoginRequest = {
  email: string
  password: string
}

export type ChangeOwnPasswordRequest = {
  currentPassword: string
  newPassword: string
}

export type AuthClient = {
  bootstrapAdmin(request: BootstrapAdminRequest): Promise<AuthResponse>
  login(request: LoginRequest): Promise<AuthResponse>
  changeOwnPassword(accessToken: string, request: ChangeOwnPasswordRequest): Promise<CurrentUserDto>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://127.0.0.1:5080'

async function postAuth<TRequest>(path: string, request: TRequest): Promise<AuthResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? 'Не удалось выполнить вход.')
  }

  return response.json()
}

async function putAuthorized<TRequest, TResponse>(path: string, accessToken: string, request: TRequest): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method: 'PUT',
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? 'Не удалось выполнить действие.')
  }

  return response.json()
}

export const authApi: AuthClient = {
  bootstrapAdmin(request) {
    return postAuth('/api/auth/bootstrap-admin', request)
  },
  login(request) {
    return postAuth('/api/auth/login', request)
  },
  changeOwnPassword(accessToken, request) {
    return putAuthorized('/api/auth/me/password', accessToken, request)
  },
}
