// @vitest-environment node
import { afterEach, describe, expect, it, vi } from 'vitest'
import { authenticatedApiFetch, authenticatedJsonApiFetch, readApiErrorMessage } from './authenticatedApiFetch'

describe('authenticatedApiFetch', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('adds the bearer token and preserves request options', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await authenticatedApiFetch('token-1', '/api/test', {
      method: 'POST',
      headers: { 'X-Request-Id': 'request-1' },
    })

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    const headers = new Headers(init.headers)
    expect(url).toBe('/api/test')
    expect(init.method).toBe('POST')
    expect(headers.get('Authorization')).toBe('Bearer token-1')
    expect(headers.get('X-Request-Id')).toBe('request-1')
  })

  it('adds JSON content type without replacing an explicit content type', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await authenticatedJsonApiFetch('token-2', '/api/json')
    await authenticatedJsonApiFetch('token-2', '/api/custom', { headers: { 'Content-Type': 'application/problem+json' } })

    expect(new Headers(fetchMock.mock.calls[0][1].headers).get('Content-Type')).toBe('application/json')
    expect(new Headers(fetchMock.mock.calls[1][1].headers).get('Content-Type')).toBe('application/problem+json')
  })

  it('reads a problem detail and falls back for a non-JSON response', async () => {
    await expect(readApiErrorMessage(new Response(JSON.stringify({ detail: 'Точная ошибка' })), 'Общая ошибка')).resolves.toBe('Точная ошибка')
    await expect(readApiErrorMessage(new Response('invalid'), 'Общая ошибка')).resolves.toBe('Общая ошибка')
  })
})
