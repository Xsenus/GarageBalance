import { afterEach, describe, expect, it, vi } from 'vitest'

import { releasesApi } from './releasesApi'

describe('releasesApi', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requests published release pages by offset and nine items by default', async () => {
    const fetchMock = vi.fn().mockResolvedValue(pageResponse())
    vi.stubGlobal('fetch', fetchMock)

    const result = await releasesApi.getReleases('token', 9)

    expect(result.totalCount).toBe(18)
    expect(fetchMock).toHaveBeenCalledWith('/api/app-releases?offset=9&limit=9', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
  })

  it('requests manageable release pages with explicit bounds', async () => {
    const fetchMock = vi.fn().mockResolvedValue(pageResponse())
    vi.stubGlobal('fetch', fetchMock)

    await releasesApi.getManageableReleases('token', 18, 9)

    expect(fetchMock).toHaveBeenCalledWith('/api/app-releases/manage?offset=18&limit=9', expect.any(Object))
  })
})

function pageResponse() {
  return new Response(JSON.stringify({
    items: [],
    totalCount: 18,
    offset: 9,
    limit: 9,
    hasMore: true,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
