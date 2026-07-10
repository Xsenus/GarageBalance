import { afterEach, describe, expect, it, vi } from 'vitest'

import { integrationsApi } from './integrationsApi'

describe('integrationsApi', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requests 1C Fresh sync retry through the retry endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      auditEventId: 'audit-1',
      provider: 'OneCFresh',
      status: 'pending_adapter',
      statusMessage: 'Повтор зарегистрирован.',
      requestedAtUtc: '2026-07-10T00:00:00Z',
      isRetry: true,
      canRetry: true,
      hasConflict: false,
      errorCode: null,
      externalRunId: null,
      recoveryAction: 'retry',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await integrationsApi.retryOneCFreshSync('token', { comment: 'Повтор после ошибки' })

    expect(result.statusMessage).toBe('Повтор зарегистрирован.')
    expect(result.canRetry).toBe(true)
    expect(result.hasConflict).toBe(false)
    expect(result.recoveryAction).toBe('retry')
    expect(fetchMock).toHaveBeenCalledWith('/api/integrations/one-c-fresh/sync-runs/retry', {
      method: 'POST',
      body: JSON.stringify({ comment: 'Повтор после ошибки' }),
      headers: expect.any(Headers),
    })
    const headers = fetchMock.mock.calls[0][1].headers as Headers
    expect(headers.get('Authorization')).toBe('Bearer token')
    expect(headers.get('Content-Type')).toBe('application/json')
  })
})
