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

  it('requests Access import rollback with a required reason', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'run-42', status: 'rollback_requested' }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await importApi.requestAccessImportRollback('token', 'run-42', 'Выбран неверный файл')

    expect(result.status).toBe('rollback_requested')
    expect(fetchMock).toHaveBeenCalledWith('/api/import/access/runs/run-42/rollback', {
      method: 'POST',
      headers: {
        Authorization: 'Bearer token',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ reason: 'Выбран неверный файл' }),
    })
  })

  it('requests Access import apply with reason and backup confirmation', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'run-42', status: 'import_requested' }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await importApi.requestAccessImportApply('token', 'run-42', 'Dry-run проверен', true)

    expect(result.status).toBe('import_requested')
    expect(fetchMock).toHaveBeenCalledWith('/api/import/access/runs/run-42/apply', {
      method: 'POST',
      headers: {
        Authorization: 'Bearer token',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ reason: 'Dry-run проверен', backupConfirmed: true }),
    })
  })
})
