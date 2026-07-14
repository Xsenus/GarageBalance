export type AppReleaseItemDto = {
  type: string
  text: string
}

export type AppReleaseDto = {
  releaseId: string
  version: string
  publishedAt: string
  title: string
  summary: string
  items: AppReleaseItemDto[]
  isPublished?: boolean | null
}

export type AppReleasePageDto = {
  items: AppReleaseDto[]
  totalCount: number
  offset: number
  limit: number
  hasMore: boolean
}

export type UpsertAppReleaseRequest = {
  releaseId?: string | null
  version: string
  publishedAt?: string | null
  title: string
  summary: string
  items: AppReleaseItemDto[]
  isPublished?: boolean
}

export type ReleaseClient = {
  getReleases(accessToken: string, offset?: number, limit?: number): Promise<AppReleasePageDto>
  getManageableReleases(accessToken: string, offset?: number, limit?: number): Promise<AppReleasePageDto>
  createRelease(accessToken: string, request: UpsertAppReleaseRequest): Promise<AppReleaseDto>
  updateRelease(accessToken: string, releaseId: string, request: UpsertAppReleaseRequest): Promise<AppReleaseDto>
  publishRelease(accessToken: string, releaseId: string): Promise<AppReleaseDto>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

async function requestJson<TResponse>(accessToken: string, path: string, init: RequestInit = {}): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
      ...init.headers,
    },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? 'Не удалось загрузить историю обновлений.')
  }

  return response.json()
}

export const releasesApi: ReleaseClient = {
  getReleases(accessToken, offset = 0, limit = 9) {
    const searchParams = new URLSearchParams()
    searchParams.set('offset', offset.toString())
    searchParams.set('limit', limit.toString())
    return requestJson(accessToken, `/api/app-releases?${searchParams}`)
  },
  getManageableReleases(accessToken, offset = 0, limit = 9) {
    const searchParams = new URLSearchParams()
    searchParams.set('offset', offset.toString())
    searchParams.set('limit', limit.toString())
    return requestJson(accessToken, `/api/app-releases/manage?${searchParams}`)
  },
  createRelease(accessToken, request) {
    return requestJson(accessToken, '/api/app-releases', {
      method: 'POST',
      body: JSON.stringify(request),
    })
  },
  updateRelease(accessToken, releaseId, request) {
    return requestJson(accessToken, `/api/app-releases/${encodeURIComponent(releaseId)}`, {
      method: 'PUT',
      body: JSON.stringify(request),
    })
  },
  publishRelease(accessToken, releaseId) {
    return requestJson(accessToken, `/api/app-releases/${encodeURIComponent(releaseId)}/publish`, {
      method: 'POST',
    })
  },
}
