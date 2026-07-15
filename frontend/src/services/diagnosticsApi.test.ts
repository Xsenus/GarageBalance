import { afterEach, describe, expect, it, vi } from 'vitest'

import { diagnosticsApi } from './diagnosticsApi'

describe('diagnosticsApi', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('reports a bounded browser error with authentication', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 202 }))
    vi.stubGlobal('fetch', fetchMock)

    await diagnosticsApi.reportClientError('access-token', {
      clientErrorId: 'browser-error-123',
      errorName: 'TypeError',
      message: 'Rendering failed',
      componentStack: 'at PaymentsPanel',
      route: '/payments',
    })

    expect(fetchMock).toHaveBeenCalledWith('/api/diagnostics/client-errors', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: 'Bearer access-token' },
      body: JSON.stringify({
        clientErrorId: 'browser-error-123',
        errorName: 'TypeError',
        message: 'Rendering failed',
        componentStack: 'at PaymentsPanel',
        route: '/payments',
      }),
    })
  })

  it('does not expose the server response when reporting fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('secret diagnostic detail', { status: 500 })))

    await expect(diagnosticsApi.reportClientError('token', {
      clientErrorId: 'browser-error-456',
      errorName: 'Error',
      message: 'Failure',
    })).rejects.toThrow('Не удалось передать диагностический отчет.')
  })
})
