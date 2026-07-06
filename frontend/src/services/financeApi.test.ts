import { afterEach, describe, expect, it, vi } from 'vitest'

import { financeApi } from './financeApi'

describe('financeApi', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('passes garageId to the operations page endpoint', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
      items: [],
      totalCount: 0,
      offset: 0,
      limit: 25,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)

    await financeApi.getOperationsPage('token', {
      operationKind: 'income',
      garageId: 'garage-77',
      limit: 25,
    })

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/operations/page?operationKind=income&garageId=garage-77&limit=25', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })
})
