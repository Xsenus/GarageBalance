import { afterEach, describe, expect, it, vi } from 'vitest'

import { settingsApi } from './settingsApi'

describe('settingsApi', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('loads payment display settings', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      showAllGarageOperationsByDefault: false,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await settingsApi.getPaymentDisplaySettings('token')

    expect(result.showAllGarageOperationsByDefault).toBe(false)
    expect(fetchMock).toHaveBeenCalledWith('/api/settings/payments/display', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('updates payment display settings', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      showAllGarageOperationsByDefault: true,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await settingsApi.updatePaymentDisplaySettings('token', { showAllGarageOperationsByDefault: true })

    expect(result.showAllGarageOperationsByDefault).toBe(true)
    expect(fetchMock).toHaveBeenCalledWith('/api/settings/payments/display', {
      method: 'PUT',
      body: JSON.stringify({ showAllGarageOperationsByDefault: true }),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('maps API problem details to a readable error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      detail: 'Настройка недоступна.',
    }), { status: 403, headers: { 'Content-Type': 'application/problem+json' } })))

    await expect(settingsApi.getPaymentDisplaySettings('token')).rejects.toThrow('Настройка недоступна.')
  })
})
