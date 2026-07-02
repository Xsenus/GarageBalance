export type ManagedRoleDto = {
  code: string
  name: string
  permissions: string[]
}

export type ManagedUserDto = {
  id: string
  email: string
  displayName: string
  isActive: boolean
  createdAtUtc: string
  lastLoginAtUtc: string | null
  roles: string[]
  permissions: string[]
}

export type PagedManagedUsersDto = {
  items: ManagedUserDto[]
  totalCount: number
  offset: number
  limit: number
}

export type CreateManagedUserRequest = {
  email: string
  displayName: string
  password: string
  roleCodes: string[]
  isActive: boolean
}

export type UpdateManagedUserRequest = {
  displayName: string
  roleCodes: string[]
  isActive: boolean
  newPassword?: string | null
  deactivationReason?: string | null
}

export type UserManagementClient = {
  getRoles(accessToken: string): Promise<ManagedRoleDto[]>
  getUsers(accessToken: string, search?: string, limit?: number): Promise<ManagedUserDto[]>
  getUsersPage(accessToken: string, search?: string, offset?: number, limit?: number): Promise<PagedManagedUsersDto>
  createUser(accessToken: string, request: CreateManagedUserRequest): Promise<ManagedUserDto>
  updateUser(accessToken: string, userId: string, request: UpdateManagedUserRequest): Promise<ManagedUserDto>
  restoreUser(accessToken: string, userId: string): Promise<ManagedUserDto>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''
const defaultUserListLimit = 50

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

export const usersApi: UserManagementClient = {
  getRoles(accessToken) {
    return requestJson(accessToken, '/api/users/roles')
  },
  getUsers(accessToken, search, limit = defaultUserListLimit) {
    return requestJson(accessToken, withQuery('/api/users', { search, limit }))
  },
  getUsersPage(accessToken, search, offset = 0, limit = 25) {
    return requestJson(accessToken, withQuery('/api/users/page', { search, offset, limit }))
  },
  createUser(accessToken, request) {
    return requestJson(accessToken, '/api/users', { method: 'POST', body: JSON.stringify(request) })
  },
  updateUser(accessToken, userId, request) {
    return requestJson(accessToken, `/api/users/${userId}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  restoreUser(accessToken, userId) {
    return requestJson(accessToken, `/api/users/${userId}/restore`, { method: 'POST' })
  },
}
