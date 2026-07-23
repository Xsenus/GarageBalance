// @vitest-environment node
import { afterEach, describe, expect, it, vi } from 'vitest'

import { auditApi } from './auditApi'
import { authApi } from './authApi'
import { fundsApi } from './fundsApi'
import { usersApi } from './usersApi'

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' },
  })
}

describe('core API clients', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('sends every authentication request with the expected method, body and authorization header', async () => {
    const authResponse = {
      accessToken: 'access-token',
      expiresAtUtc: '2026-07-16T00:00:00Z',
      user: { id: 'user-1', email: 'admin@example.test', displayName: 'Администратор', roles: ['administrator'], permissions: ['users.manage'] },
    }
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse(authResponse)))
    vi.stubGlobal('fetch', fetchMock)

    await authApi.bootstrapAdmin({ email: 'admin@example.test', password: 'StrongPass123', displayName: 'Администратор' })
    await authApi.login({ email: 'admin@example.test', password: 'StrongPass123' })
    await authApi.changeOwnPassword('access-token', { currentPassword: 'StrongPass123', newPassword: 'NewStrongPass123' })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/auth/bootstrap-admin', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ email: 'admin@example.test', password: 'StrongPass123', displayName: 'Администратор' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/auth/login', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ email: 'admin@example.test', password: 'StrongPass123' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/auth/me/password', expect.objectContaining({
      method: 'PUT',
      headers: expect.objectContaining({ Authorization: 'Bearer access-token' }),
      body: JSON.stringify({ currentPassword: 'StrongPass123', newPassword: 'NewStrongPass123' }),
    }))
  })

  it('maps authentication problem details and invalid error bodies to stable messages', async () => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(jsonResponse({ detail: 'Неверный пароль.' }, 401))
      .mockResolvedValueOnce(new Response('broken', { status: 500 })))

    await expect(authApi.login({ email: 'admin@example.test', password: 'bad' })).rejects.toThrow('Неверный пароль.')
    await expect(authApi.changeOwnPassword('token', { currentPassword: 'bad', newPassword: 'NewStrongPass123' })).rejects.toThrow('Не удалось выполнить действие.')
  })

  it('builds all audit list, page, detail and export requests without dropping filters', async () => {
    const fetchMock = vi.fn().mockImplementation((url: string) => Promise.resolve(
      url.includes('/export')
        ? new Response('audit', { status: 200 })
        : jsonResponse(url.includes('/page') ? { items: [], totalCount: 0, offset: 20, limit: 25 } : []),
    ))
    vi.stubGlobal('fetch', fetchMock)
    const filters = {
      dateFrom: '2026-07-01', dateTo: '2026-07-31', action: 'updated', search: 'гараж', offset: 20, limit: 25,
      section: 'payments', actionKind: 'edit', entityType: 'garage', actorUserId: 'actor-1', quickFilter: 'today',
      relatedGarage: '15', relatedAccountingMonth: '2026-07', relatedCounterparty: 'Водоканал', relatedDocument: 'PKO-1',
    }

    await auditApi.getEvents('token', filters)
    await auditApi.getEventsPage('token', filters)
    await auditApi.getEvent('token', 'event/1')
    await auditApi.exportEvents('token', filters)
    await auditApi.exportEventsXlsx('token', filters)

    const urls = fetchMock.mock.calls.map(([url]) => String(url))
    expect(urls[0]).toContain('/api/audit/events?')
    expect(urls[0]).toContain('offset=20')
    expect(urls[0]).toContain('relatedDocument=PKO-1')
    expect(urls[1]).toContain('/api/audit/events/page?')
    expect(urls[2]).toBe('/api/audit/events/event%2F1')
    expect(urls[3]).toContain('/api/audit/events/export?')
    expect(urls[4]).toContain('/api/audit/events/export/xlsx?')
    for (const [, init] of fetchMock.mock.calls) {
      expect(init?.headers).toEqual({ Authorization: 'Bearer token' })
    }
  })

  it('maps audit JSON and export failures to readable messages', async () => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(jsonResponse({ detail: 'История недоступна.' }, 403))
      .mockResolvedValueOnce(new Response('broken', { status: 503 })))

    await expect(auditApi.getEvents('token')).rejects.toThrow('История недоступна.')
    await expect(auditApi.exportEvents('token')).rejects.toThrow('Не удалось скачать историю изменений.')
  })

  it('sends every fund query and mutation with bounded paging and exact payloads', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse([])))
    vi.stubGlobal('fetch', fetchMock)

    await fundsApi.getFunds('token')
    await fundsApi.createFund('token', { name: 'Резервный фонд' })
    await fundsApi.updateFund('token', 'fund/1', { name: 'Новый резерв' })
    await fundsApi.getOperations('token', { limit: 10, includeCanceled: true })
    await fundsApi.getOperationsPage?.('token', { offset: 25, limit: 25, includeCanceled: true })
    await fundsApi.createOperation('token', 'fund/1', { operationKind: 'deposit', amount: 1000, reason: 'Пополнение' })
    await fundsApi.updateOperation('token', 'operation/1', { amount: 900, reason: 'Исправление' })
    await fundsApi.cancelOperation('token', 'operation/1', { reason: 'Ошибка' })
    await fundsApi.restoreOperation('token', 'operation/1')

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      '/api/funds',
      '/api/funds',
      '/api/funds/fund/1',
      '/api/funds/operations?limit=10&includeCanceled=true',
      '/api/funds/operations/page?offset=25&limit=25&includeCanceled=true',
      '/api/funds/fund/1/operations',
      '/api/funds/operations/operation/1',
      '/api/funds/operations/operation/1/cancel',
      '/api/funds/operations/operation/1/restore',
    ])
    expect(fetchMock.mock.calls[1][1]).toEqual(expect.objectContaining({ method: 'POST', body: JSON.stringify({ name: 'Резервный фонд' }) }))
    expect(fetchMock.mock.calls[2][1]).toEqual(expect.objectContaining({ method: 'PUT', body: JSON.stringify({ name: 'Новый резерв' }) }))
    expect(fetchMock.mock.calls[5][1]).toEqual(expect.objectContaining({ method: 'POST', body: JSON.stringify({ operationKind: 'deposit', amount: 1000, reason: 'Пополнение' }) }))
    expect(fetchMock.mock.calls[6][1]).toEqual(expect.objectContaining({ method: 'PUT', body: JSON.stringify({ amount: 900, reason: 'Исправление' }) }))
    expect(fetchMock.mock.calls[7][1]).toEqual(expect.objectContaining({ method: 'POST', body: JSON.stringify({ reason: 'Ошибка' }) }))
    expect(fetchMock.mock.calls[8][1]).toEqual(expect.objectContaining({ method: 'POST' }))
  })

  it('maps fund failures to their server detail or stable fallback', async () => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(jsonResponse({ detail: 'Недостаточно средств фонда.' }, 409))
      .mockResolvedValueOnce(new Response('broken', { status: 500 })))

    await expect(fundsApi.getFunds('token')).rejects.toThrow('Недостаточно средств фонда.')
    await expect(fundsApi.restoreOperation('token', 'operation-1')).rejects.toThrow('Не удалось выполнить операцию фонда.')
  })

  it('sends every user and role request with paging, encoding and mutation payloads', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse([])))
    vi.stubGlobal('fetch', fetchMock)
    const createRequest = { email: 'user@example.test', displayName: 'Оператор', password: 'StrongPass123', roleCodes: ['operator'], isActive: true }
    const updateRequest = { displayName: 'Бухгалтер', roleCodes: ['accountant'], isActive: true, newPassword: null }

    await usersApi.getRoles('token')
    await usersApi.getUsers('token', 'Иван', 100)
    await usersApi.getUsersPage('token', 'Иван', 25, 25)
    await usersApi.createUser('token', createRequest)
    await usersApi.updateUser('token', 'user/1', updateRequest)
    await usersApi.restoreUser('token', 'user/1')
    await usersApi.updateRolePermissions('token', 'reports viewer', { permissions: ['reports.read'] })

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      '/api/users/roles',
      '/api/users?search=%D0%98%D0%B2%D0%B0%D0%BD&limit=100',
      '/api/users/page?search=%D0%98%D0%B2%D0%B0%D0%BD&offset=25&limit=25',
      '/api/users',
      '/api/users/user/1',
      '/api/users/user/1/restore',
      '/api/users/roles/reports%20viewer/permissions',
    ])
    expect(fetchMock.mock.calls[3][1]).toEqual(expect.objectContaining({ method: 'POST', body: JSON.stringify(createRequest) }))
    expect(fetchMock.mock.calls[4][1]).toEqual(expect.objectContaining({ method: 'PUT', body: JSON.stringify(updateRequest) }))
    expect(fetchMock.mock.calls[6][1]).toEqual(expect.objectContaining({ method: 'PUT', body: JSON.stringify({ permissions: ['reports.read'] }) }))
  })

  it('maps user-management failures without exposing invalid response bodies', async () => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(jsonResponse({ detail: 'Недостаточно прав.' }, 403))
      .mockResolvedValueOnce(new Response('broken', { status: 500 })))

    await expect(usersApi.getRoles('token')).rejects.toThrow('Недостаточно прав.')
    await expect(usersApi.restoreUser('token', 'user-1')).rejects.toThrow('Не удалось выполнить запрос.')
  })
})
