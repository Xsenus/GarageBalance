import { afterEach, describe, expect, it, vi } from 'vitest'

import { importApi } from './importApi'

describe('importApi', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('downloads dry-run report through POST because the backend records an audit event', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await importApi.downloadAccessRunReport('token', 'run-42')

    expect(result.size).toBe(2)
    expect(result.type).toBe('application/json')
    expect(fetchMock).toHaveBeenCalledWith('/api/import/access/runs/run-42/report', {
      method: 'POST',
      headers: {
        Authorization: 'Bearer token',
      },
    })
  })
})
