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

  it('cancels Access import apply request with a required reason', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'run-42', status: 'import_request_cancelled' }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await importApi.cancelAccessImportApplyRequest('token', 'run-42', 'Нужно перепроверить backup')

    expect(result.status).toBe('import_request_cancelled')
    expect(fetchMock).toHaveBeenCalledWith('/api/import/access/runs/run-42/apply/cancel', {
      method: 'POST',
      headers: {
        Authorization: 'Bearer token',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ reason: 'Нужно перепроверить backup' }),
    })
  })

  it('loads created records for Access import run with a bounded limit', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify([{ id: 'created-1', targetEntityType: 'garage' }]), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await importApi.getAccessCreatedRecords('token', 'run-42', 25)

    expect(result).toHaveLength(1)
    expect(fetchMock).toHaveBeenCalledWith('/api/import/access/runs/run-42/created-records?limit=25', {
      headers: {
        Authorization: 'Bearer token',
      },
    })
  })
})
