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

  it.each([
    new Headers({ Authorization: 'Bearer stale', 'Content-Type': 'application/problem+json', 'X-Request-Id': 'request-3' }),
    [['authorization', 'Bearer stale'], ['content-type', 'application/problem+json'], ['x-request-id', 'request-3']] as [string, string][],
    { authorization: 'Bearer stale', 'content-type': 'application/problem+json', 'X-Request-Id': 'request-3' },
    { AUTHORIZATION: 'Bearer stale', 'CONTENT-TYPE': 'application/problem+json', 'X-Request-Id': 'request-3' },
  ])('merges header names case-insensitively for every supported HeadersInit: %s', async (headers) => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
    await authenticatedJsonApiFetch('current-token', '/api/headers', { headers })
    const sentHeaders = new Headers(fetchMock.mock.calls[0][1].headers)
    expect(sentHeaders.get('Authorization')).toBe('Bearer current-token')
    expect(sentHeaders.get('Content-Type')).toBe('application/problem+json')
    expect(sentHeaders.get('X-Request-Id')).toBe('request-3')
    expect(new Headers(headers).get('Authorization')).toBe('Bearer stale')
  })

  it('reads a problem detail and falls back for a non-JSON response', async () => {
    await expect(readApiErrorMessage(new Response(JSON.stringify({ detail: 'Точная ошибка' })), 'Общая ошибка')).resolves.toBe('Точная ошибка')
    await expect(readApiErrorMessage(new Response('invalid'), 'Общая ошибка')).resolves.toBe('Общая ошибка')
  })
})
