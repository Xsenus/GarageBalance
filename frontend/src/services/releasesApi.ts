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
}

export type ReleaseClient = {
  getReleases(accessToken: string, limit?: number): Promise<AppReleaseDto[]>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

async function requestJson<TResponse>(accessToken: string, path: string): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
    },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? 'Не удалось загрузить историю обновлений.')
  }

  return response.json()
}

export const releasesApi: ReleaseClient = {
  getReleases(accessToken, limit = 10) {
    const searchParams = new URLSearchParams()
    searchParams.set('limit', limit.toString())
    return requestJson(accessToken, `/api/app-releases?${searchParams}`)
  },
}
