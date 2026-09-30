import { expect, it, vi } from 'vitest'
import { serviceReportsApi } from './serviceReportsApi'
import { authenticatedApiFetch, authenticatedJsonApiFetch } from './authenticatedApiFetch'
vi.mock('./authenticatedApiFetch', () => ({ authenticatedApiFetch: vi.fn(), authenticatedJsonApiFetch: vi.fn(), readApiErrorMessage: vi.fn(async () => 'Ошибка сервера') }))
it('sends inclusive dates and filters, excludes empty dates and keeps cancellation', async () => {
  vi.mocked(authenticatedApiFetch).mockResolvedValue(new Response(JSON.stringify({ total: 30 })))
  const controller = new AbortController()
  expect(await serviceReportsApi.getReport('token', 'debt', { dateFrom: '', dateTo: '2026-09-30', garageId: 'g2', overdueOnly: true, offset: 50, limit: 50 }, controller.signal)).toEqual({ total: 30 })
  expect(authenticatedApiFetch).toHaveBeenCalledWith('token', '/api/reports/services/debt?dateTo=2026-09-30&garageId=g2&overdueOnly=true&offset=50&limit=50', { signal: controller.signal })
})
it('loads and saves columns with JSON version and cancellation', async () => {
  vi.mocked(authenticatedApiFetch).mockResolvedValue(new Response(JSON.stringify({ version: 'v' })))
  vi.mocked(authenticatedJsonApiFetch).mockResolvedValue(new Response(JSON.stringify({ version: 'next' })))
  expect(await serviceReportsApi.getColumns('token')).toEqual({ version: 'v' })
  expect(await serviceReportsApi.saveColumns('token', { version: 'v', columns: [] })).toEqual({ version: 'next' })
  expect(authenticatedJsonApiFetch).toHaveBeenCalledWith('token', '/api/reports/services/columns', { method: 'PUT', body: JSON.stringify({ version: 'v', columns: [] }), signal: undefined })
})
it.each(['xlsx', 'pdf'] as const)('exports %s with the full filter rather than the loaded page', async (format) => {
  vi.mocked(authenticatedApiFetch).mockResolvedValue(new Response('export'))
  const blob = await serviceReportsApi.exportReport('token', 'payments', { dateTo: '2026-09-30', offset: 50, limit: 50 }, format)
  expect(await blob.text()).toBe('export')
  expect(authenticatedApiFetch).toHaveBeenLastCalledWith('token', `/api/reports/services/payments/export/${format}?dateTo=2026-09-30`, { method: 'POST', signal: undefined })
})
it('propagates server errors for query, settings and export', async () => {
  vi.mocked(authenticatedApiFetch).mockImplementation(async () => new Response('{}', { status: 403 }))
  vi.mocked(authenticatedJsonApiFetch).mockImplementation(async () => new Response('{}', { status: 409 }))
  await expect(serviceReportsApi.getColumns('token')).rejects.toThrow('Ошибка сервера')
  await expect(serviceReportsApi.getReport('token', 'payments', {})).rejects.toThrow('Ошибка сервера')
  await expect(serviceReportsApi.saveColumns('token', { version: 'v', columns: [] })).rejects.toThrow('Ошибка сервера')
  await expect(serviceReportsApi.exportReport('token', 'debt', {}, 'xlsx')).rejects.toThrow('Ошибка сервера')
})
