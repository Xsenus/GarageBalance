import { apiFetch } from './apiFetch'

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

function toHeaderRecord(headers?: HeadersInit): Record<string, string> {
  if (!headers) {
    return {}
  }

  if (headers instanceof Headers || Array.isArray(headers)) {
    return Object.fromEntries(new Headers(headers).entries())
  }

  return headers
}

export function authenticatedApiFetch(accessToken: string, path: string, init?: RequestInit): Promise<Response> {
  return apiFetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      ...toHeaderRecord(init?.headers),
      Authorization: `Bearer ${accessToken}`,
    },
  })
}

export function authenticatedJsonApiFetch(accessToken: string, path: string, init?: RequestInit): Promise<Response> {
  return authenticatedApiFetch(accessToken, path, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...toHeaderRecord(init?.headers),
    },
  })
}

export function authenticatedJsonBodyApiFetch(accessToken: string, path: string, init?: RequestInit): Promise<Response> {
  return init?.body
    ? authenticatedJsonApiFetch(accessToken, path, init)
    : authenticatedApiFetch(accessToken, path, init)
}

export async function readApiErrorMessage(response: Response, fallbackMessage: string): Promise<string> {
  const problem = await response.json().catch(() => null)
  return typeof problem?.detail === 'string' ? problem.detail : fallbackMessage
}
