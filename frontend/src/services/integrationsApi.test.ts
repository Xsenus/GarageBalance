import { afterEach, describe, expect, it, vi } from 'vitest'

import { integrationsApi } from './integrationsApi'

describe('integrationsApi', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requests 1C Fresh sync preview through the preview endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      auditEventId: 'audit-preview',
      provider: 'OneCFresh',
      mode: 'preview',
      direction: 'pending_decision',
      status: 'draft_preview',
      statusMessage: 'Предпросмотр подготовлен.',
      requestedAtUtc: '2026-07-11T00:00:00Z',
      periodSummary: 'Период не выбран.',
      snapshotHash: 'snapshot',
      canApply: false,
      counts: [{ objectType: 'payment', operation: 'export', count: 0 }],
      warnings: [{ code: 'decision_required', message: 'Нужно решение.' }],
      conflicts: [],
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await integrationsApi.previewOneCFreshSync('token', { comment: 'Проверить до отправки' })

    expect(result.mode).toBe('preview')
    expect(result.status).toBe('draft_preview')
    expect(result.canApply).toBe(false)
    expect(result.warnings).toHaveLength(1)
    expect(fetchMock).toHaveBeenCalledWith('/api/integrations/one-c-fresh/sync-runs/preview', {
      method: 'POST',
      body: JSON.stringify({ comment: 'Проверить до отправки' }),
      headers: expect.any(Headers),
    })
    const headers = fetchMock.mock.calls[0][1].headers as Headers
    expect(headers.get('Authorization')).toBe('Bearer token')
    expect(headers.get('Content-Type')).toBe('application/json')
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

  it('registers receipt printing actions through the operation action endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      auditEventId: 'audit-receipt',
      financialOperationId: 'operation-77',
      action: 'reprint',
      status: 'printed',
      statusMessage: 'Копия квитанции отправлена на печать.',
      documentNumber: 'PKO-77',
      registeredAtUtc: '2026-07-11T00:00:00Z',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await integrationsApi.registerReceiptPrintingAction('token', 'operation/77', {
      action: 'reprint',
      reason: 'Повторная выдача',
    })

    expect(result.status).toBe('printed')
    expect(result.documentNumber).toBe('PKO-77')
    expect(fetchMock).toHaveBeenCalledWith('/api/integrations/receipt-printing/operations/operation%2F77/actions', {
      method: 'POST',
      body: JSON.stringify({ action: 'reprint', reason: 'Повторная выдача' }),
      headers: expect.any(Headers),
    })
    const headers = fetchMock.mock.calls[0][1].headers as Headers
    expect(headers.get('Authorization')).toBe('Bearer token')
    expect(headers.get('Content-Type')).toBe('application/json')
  })
})
