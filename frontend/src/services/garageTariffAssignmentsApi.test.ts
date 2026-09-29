// @vitest-environment node
import { afterEach, expect, it, vi } from 'vitest'
import { garageTariffAssignmentsApi as api } from './garageTariffAssignmentsApi'
import { clearDictionaryResponseCache } from './dictionaryResponseCache'

afterEach(() => { vi.unstubAllGlobals(); clearDictionaryResponseCache() })
const terms = { effectiveFrom: '2026-09-01', effectiveTo: null, rate: 200, tiers: null, comment: null, reason: 'Проверка' }
it('uses scoped routes, JSON versions, authorization and cancellation for every operation', async () => {
  const fetch = vi.fn().mockResolvedValue(new Response('{}'))
  // Each request needs its own body stream.
  fetch.mockImplementation(() => Promise.resolve(new Response('{}')))
  vi.stubGlobal('fetch', fetch)
  const controller = new AbortController()
  await api.getPage('token', 'service/id', { garageId: 'garage', includeArchived: true, offset: 25, limit: 25 }, controller.signal)
  await api.create('token', 'service', { ...terms, garageIds: ['garage'], serviceVersion: 'service-version' }, controller.signal)
  await api.update('token', 'service', 'assignment', { ...terms, version: 'version', serviceVersion: 'service-version' }, controller.signal)
  await api.archive('token', 'service', 'assignment', { version: 'version', reason: 'Отмена' }, controller.signal)
  expect(fetch.mock.calls.map(([url]) => url)).toEqual([
    '/api/dictionaries/charge-services/service%2Fid/garage-tariffs?garageId=garage&includeArchived=true&offset=25&limit=25',
    '/api/dictionaries/charge-services/service/garage-tariffs',
    '/api/dictionaries/charge-services/service/garage-tariffs/assignment',
    '/api/dictionaries/charge-services/service/garage-tariffs/assignment',
  ])
  expect(fetch.mock.calls.map(([, init]) => init.method)).toEqual([undefined, 'POST', 'PUT', 'DELETE'])
  for (const [, init] of fetch.mock.calls) { expect(init.headers.Authorization).toBe('Bearer token'); expect(init.signal).toBeInstanceOf(AbortSignal) }
  expect(JSON.parse(fetch.mock.calls[2][1].body)).toEqual({ ...terms, version: 'version', serviceVersion: 'service-version' })
})
it('preserves conflicts and never automatically resubmits a mutation', async () => {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'concurrent_write_conflict', detail: 'Обновите карточку' }), { status: 409 }))
  vi.stubGlobal('fetch', fetch)
  await expect(api.archive('token', 'service', 'id', { version: 'old', reason: null })).rejects.toMatchObject({ status: 409, code: 'concurrent_write_conflict' })
  expect(fetch).toHaveBeenCalledTimes(1)
})
it('preserves cancellation without sending an already aborted request', async () => {
  const fetch = vi.fn()
  vi.stubGlobal('fetch', fetch)
  const controller = new AbortController(); controller.abort()
  await expect(api.getPage('token', 'service', {}, controller.signal)).rejects.toMatchObject({ name: 'AbortError' })
  expect(fetch).not.toHaveBeenCalled()
})
it('propagates caller cancellation to the shared transport signal during a mutation', async () => {
  const fetch = vi.fn((_url: string, init: RequestInit) => new Promise<Response>((_resolve, reject) => {
    init.signal!.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true })
  }))
  vi.stubGlobal('fetch', fetch)
  const controller = new AbortController()
  const pending = api.update('token', 'service', 'id', { ...terms, version: 'version', serviceVersion: 'service-version' }, controller.signal)
  controller.abort()
  await expect(pending).rejects.toMatchObject({ name: 'AbortError' })
  expect(fetch.mock.calls[0][1].signal?.aborted).toBe(true)
  expect(fetch).toHaveBeenCalledTimes(1)
})
