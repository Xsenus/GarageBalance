// @vitest-environment node
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

  it('loads backup status and creates a manual backup with an audit reason', async () => {
    const status = {
      enabled: true,
      automaticEnabled: true,
      intervalHours: 24,
      retentionCount: 30,
      directory: '/backups',
      isRunning: false,
      lastSuccessfulBackupAtUtc: null,
      lastError: null,
      backups: [],
    }
    const created = {
      fileName: 'garagebalance_manual_20260715_120000_000.pgdump',
      sizeBytes: 1024,
      createdAtUtc: '2026-07-15T12:00:00Z',
      kind: 'manual',
    }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(status), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify(created), { status: 201, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(settingsApi.getDatabaseBackups('token')).resolves.toEqual(status)
    await expect(settingsApi.createDatabaseBackup('token', { reason: 'Перед обновлением' })).resolves.toEqual(created)

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/settings/backups', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/settings/backups', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ reason: 'Перед обновлением' }),
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
  })

  it('loads diagnostic status and downloads the protected package', async () => {
    const status = {
      enabled: true,
      retentionDays: 14,
      packageDays: 7,
      packageMaxSizeMb: 20,
      fileCount: 2,
      totalSizeBytes: 4096,
      lastEntryAtUtc: '2026-07-15T05:00:00Z',
      lastWriteError: null,
    }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(status), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response('zip', { status: 200, headers: { 'Content-Type': 'application/zip' } }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(settingsApi.getDiagnosticLogStatus('token')).resolves.toEqual(status)
    const result = await settingsApi.createDiagnosticPackage('token')
    expect(await result.text()).toBe('zip')
    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/diagnostics/status', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/diagnostics/package', {
      method: 'POST',
      headers: { Authorization: 'Bearer token' },
    })
  })
})
