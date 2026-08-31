// @vitest-environment node
import { afterEach, describe, expect, it, vi } from 'vitest'

import { normalizeAccrualReasonDisplayMode, settingsApi } from './settingsApi'

describe('settingsApi', () => {
  it('normalizes missing and unknown reason modes to the safe default', () => {
    expect(normalizeAccrualReasonDisplayMode(undefined)).toBe('penalties_only')
    expect(normalizeAccrualReasonDisplayMode('unexpected')).toBe('penalties_only')
    expect(normalizeAccrualReasonDisplayMode('all')).toBe('all')
    expect(normalizeAccrualReasonDisplayMode('hidden')).toBe('hidden')
  })

  it('loads and updates the global action comment requirement', async () => {
    const current = { required: false, version: 'comments-v1' }
    const request = { required: true, version: 'comments-v1' }
    const updated = { required: true, version: 'comments-v2' }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(current), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify(updated), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(settingsApi.getActionCommentSettings('token')).resolves.toEqual(current)
    await expect(settingsApi.updateActionCommentSettings('token', request)).resolves.toEqual(updated)

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/settings/action-comments', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/settings/action-comments', expect.objectContaining({
      method: 'PUT',
      body: JSON.stringify(request),
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
  })

  it('loads and updates the historical meter reading correction switch', async () => {
    const current = { enabled: false, version: 'meter-correction-v1' }
    const request = { enabled: true, version: 'meter-correction-v1' }
    const updated = { enabled: true, version: 'meter-correction-v2' }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(current), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify(updated), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(settingsApi.getHistoricalMeterReadingCorrectionSettings('token')).resolves.toEqual(current)
    await expect(settingsApi.updateHistoricalMeterReadingCorrectionSettings('token', request)).resolves.toEqual(updated)

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/settings/meter-readings/historical-corrections', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/settings/meter-readings/historical-corrections', expect.objectContaining({
      method: 'PUT',
      body: JSON.stringify(request),
    }))
  })

  it('loads and saves the authenticated tariff panel layout', async () => {
    const fetchMock = vi.fn()
    fetchMock
      .mockResolvedValueOnce(new Response(JSON.stringify({ irregularPaymentsWidthPercent: 32, version: 'layout-v1' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ irregularPaymentsWidthPercent: 28, version: 'layout-v2' }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(settingsApi.getTariffPanelsLayout('token')).resolves.toEqual({ irregularPaymentsWidthPercent: 32, version: 'layout-v1' })
    await expect(settingsApi.updateTariffPanelsLayout('token', { irregularPaymentsWidthPercent: 28 })).resolves.toEqual({ irregularPaymentsWidthPercent: 28, version: 'layout-v2' })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/settings/tariffs/layout', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/settings/tariffs/layout', expect.objectContaining({
      method: 'PUT',
      body: JSON.stringify({ irregularPaymentsWidthPercent: 28 }),
    }))
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('loads payment display settings', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      showAllGarageOperationsByDefault: false,
      showFundName: false,
      accrualReasonDisplayMode: 'penalties_only',
      accrualReasonDisplayVersion: 'reason-v1',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await settingsApi.getPaymentDisplaySettings('token')

    expect(result.showAllGarageOperationsByDefault).toBe(false)
    expect(result.showFundName).toBe(false)
    expect(result.accrualReasonDisplayMode).toBe('penalties_only')
    expect(result.accrualReasonDisplayVersion).toBe('reason-v1')
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
      showFundName: true,
      accrualReasonDisplayMode: 'all',
      accrualReasonDisplayVersion: 'reason-v2',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    const request = { showAllGarageOperationsByDefault: true, version: 'payment-version', showPeriodicityColumn: true, showAccrualMonthColumn: false, tariffTableVersion: 'tariff-version', showFundName: true, accrualReasonDisplayMode: 'all' as const, accrualReasonDisplayVersion: 'reason-v1' }
    const result = await settingsApi.updatePaymentDisplaySettings('token', request)

    expect(result.showAllGarageOperationsByDefault).toBe(true)
    expect(result.showFundName).toBe(true)
    expect(result.accrualReasonDisplayMode).toBe('all')
    expect(result.accrualReasonDisplayVersion).toBe('reason-v2')
    expect(fetchMock).toHaveBeenCalledWith('/api/settings/payments/display', {
      method: 'PUT',
      body: JSON.stringify(request),
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

  it('forwards cancellation for tariff display settings', async () => {
    const fetchSignals: AbortSignal[] = []
    const fetchMock = vi.fn().mockImplementation((_path: string, init: RequestInit) => new Promise<Response>((_resolve, reject) => {
      const signal = init.signal
      if (signal) {
        fetchSignals.push(signal)
        signal.addEventListener('abort', () => reject(signal.reason), { once: true })
      }
    }))
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()

    const request = Promise.all([
      settingsApi.getPaymentDisplaySettings('token', controller.signal),
      settingsApi.getTariffPanelsLayout('token', controller.signal),
      settingsApi.getHistoricalMeterReadingCorrectionSettings('token', controller.signal),
    ])
    await vi.waitFor(() => expect(fetchSignals).toHaveLength(3))
    controller.abort()

    await expect(request).rejects.toMatchObject({ name: 'AbortError' })
    expect(fetchSignals.every((signal) => signal.aborted)).toBe(true)
    expect(fetchMock.mock.calls.map(([path]) => path)).toEqual([
      '/api/settings/payments/display',
      '/api/settings/tariffs/layout',
      '/api/settings/meter-readings/historical-corrections',
    ])
  })

  it('forwards cancellation for settings workspace reads', async () => {
    const fetchSignals: AbortSignal[] = []
    const fetchMock = vi.fn().mockImplementation((_path: string, init: RequestInit) => new Promise<Response>((_resolve, reject) => {
      const signal = init.signal
      if (signal) {
        fetchSignals.push(signal)
        signal.addEventListener('abort', () => reject(signal.reason), { once: true })
      }
    }))
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()

    const request = Promise.all([
      settingsApi.getBusinessDateSettings('token', controller.signal),
      settingsApi.getSalaryAccrualSettings('token', controller.signal),
      settingsApi.getCashBankBalances('token', controller.signal),
      settingsApi.getDatabaseBackups('token', controller.signal),
      settingsApi.getDiagnosticLogStatus('token', controller.signal),
    ])
    await vi.waitFor(() => expect(fetchSignals).toHaveLength(5))
    controller.abort()

    await expect(request).rejects.toMatchObject({ name: 'AbortError' })
    expect(fetchSignals.every((signal) => signal.aborted)).toBe(true)
    expect(fetchMock.mock.calls.map(([path]) => path)).toEqual([
      '/api/settings/business-date',
      '/api/settings/salary-accrual',
      '/api/settings/cash-bank-balances',
      '/api/settings/backups',
      '/api/diagnostics/status',
    ])
  })

  it('loads and updates the automatic salary accrual day', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ accrualDay: 10 }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ accrualDay: 15 }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(settingsApi.getSalaryAccrualSettings('token')).resolves.toEqual({ accrualDay: 10 })
    await expect(settingsApi.updateSalaryAccrualSettings('token', { accrualDay: 15 })).resolves.toEqual({ accrualDay: 15 })
    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/settings/salary-accrual', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/settings/salary-accrual', expect.objectContaining({
      method: 'PUT',
      body: JSON.stringify({ accrualDay: 15 }),
    }))
  })

  it('loads and updates the administrator business date', async () => {
    const current = {
      systemDate: '2026-07-21', effectiveDate: '2026-07-21', overrideDate: null,
      isOverrideActive: false, updatedAtUtc: null, automation: null,
    }
    const updated = {
      ...current,
      effectiveDate: '2026-08-05', overrideDate: '2026-08-05', isOverrideActive: true,
      updatedAtUtc: '2026-07-21T09:00:00Z',
      automation: { succeeded: true, createdCount: 2, skippedCount: 3, message: 'Готово' },
    }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(current), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify(updated), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(settingsApi.getBusinessDateSettings('token')).resolves.toEqual(current)
    await expect(settingsApi.updateBusinessDateSettings('token', { overrideDate: '2026-08-05' })).resolves.toEqual(updated)

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/settings/business-date', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/settings/business-date', expect.objectContaining({
      method: 'PUT',
      body: JSON.stringify({ overrideDate: '2026-08-05' }),
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
  })

  it('loads, creates, downloads, and deletes a backup through protected endpoints', async () => {
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
      .mockResolvedValueOnce(new Response('dump', { status: 200, headers: { 'Content-Type': 'application/octet-stream' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify(created), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(settingsApi.getDatabaseBackups('token')).resolves.toEqual(status)
    await expect(settingsApi.createDatabaseBackup('token', { reason: 'Перед обновлением' })).resolves.toEqual(created)
    const download = await settingsApi.downloadDatabaseBackup('token', created.fileName)
    expect(await download.text()).toBe('dump')
    await expect(settingsApi.deleteDatabaseBackup('token', created.fileName, { reason: 'Копия больше не нужна' })).resolves.toEqual(created)

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/settings/backups', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/settings/backups', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ reason: 'Перед обновлением' }),
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(3, `/api/settings/backups/${created.fileName}/download`, {
      headers: { Authorization: 'Bearer token' },
    })
    expect(fetchMock).toHaveBeenNthCalledWith(4, `/api/settings/backups/${created.fileName}`, expect.objectContaining({
      method: 'DELETE',
      body: JSON.stringify({ reason: 'Копия больше не нужна' }),
      headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    }))
  })

  it('loads balances, updates opening values, and creates an adjustment', async () => {
    const balances = {
      cashOpeningBalance: 1000,
      bankOpeningBalance: 5000,
      cashCurrentBalance: 1200,
      bankCurrentBalance: 4800,
      recentOperations: [],
    }
    const openingRequest = {
      cashOpeningBalance: 1500,
      bankOpeningBalance: 7000,
      reason: 'Остатки на дату запуска',
    }
    const adjustmentRequest = {
      account: 'cash' as const,
      direction: 'increase' as const,
      operationDate: '2026-07-27',
      amount: 250,
      reason: 'Размен кассы',
    }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(balances), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify(balances), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify(balances), { status: 201, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(settingsApi.getCashBankBalances('token')).resolves.toEqual(balances)
    await expect(settingsApi.updateCashBankOpeningBalances('token', openingRequest)).resolves.toEqual(balances)
    await expect(settingsApi.createCashBankBalanceAdjustment('token', adjustmentRequest)).resolves.toEqual(balances)

    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/settings/cash-bank-balances/opening', expect.objectContaining({
      method: 'PUT',
      body: JSON.stringify(openingRequest),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/settings/cash-bank-balances/adjustments', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify(adjustmentRequest),
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
