// @vitest-environment node
import { execFileSync } from 'node:child_process'
import { beforeAll, expect, it } from 'vitest'
import { build } from 'vite'

let moduleUrl: string
beforeAll(async () => {
  const result = await build({ configFile: false, logLevel: 'silent', build: { write: false, minify: false, lib: { entry: 'src/services/auditApi.ts', formats: ['es'] } } })
  const builds = Array.isArray(result) ? result : [result]
  expect(builds).toHaveLength(1)
  if (!('output' in builds[0])) throw new Error('Expected a completed audit client build')
  const chunks = builds[0].output.filter((item) => item.type === 'chunk')
  expect(chunks).toHaveLength(1)
  moduleUrl = `data:text/javascript;base64,${Buffer.from(chunks[0].code).toString('base64')}`
})

it.each([
  ['Asia/Novosibirsk', '2026-09-06', '2026-09-05T17:00:00.000Z', '2026-09-06T16:59:59.999999Z'],
  ['America/New_York', '2026-09-06', '2026-09-06T04:00:00.000Z', '2026-09-07T03:59:59.999999Z'],
  ['America/New_York', '2026-03-08', '2026-03-08T05:00:00.000Z', '2026-03-09T03:59:59.999999Z'],
  ['America/New_York', '2026-11-01', '2026-11-01T04:00:00.000Z', '2026-11-02T04:59:59.999999Z'],
])('uses complete local days for every audit request in %s on %s', (zone, day, from, to) => {
  // Each process initializes its real time zone; changing TZ inside a Vitest thread cannot do that.
  const script = `
    const { auditApi } = await import(${JSON.stringify(moduleUrl)});
    const urls = [];
    globalThis.fetch = async (url) => { urls.push(String(url)); return new Response('[]', { status: 200 }); };
    for (const request of [auditApi.getEvents, auditApi.getEventsPage, auditApi.exportEvents, auditApi.exportEventsXlsx]) {
      await request('token', { dateFrom: ${JSON.stringify(day)}, dateTo: ${JSON.stringify(day)} });
    }
    await auditApi.getEvents('token', { dateFrom: ${JSON.stringify(from)}, dateTo: ${JSON.stringify(to)} });
    await auditApi.getEvents('token', { dateFrom: '', dateTo: '' });
    process.stdout.write(JSON.stringify(urls));
  `
  const urls = JSON.parse(execFileSync(process.execPath, ['--input-type=module', '-'], { input: script, encoding: 'utf8', env: { ...process.env, TZ: zone } })) as string[]
  expect(urls).toHaveLength(6)
  for (const url of urls.slice(0, 5)) {
    const query = new URL(url, 'http://localhost').searchParams
    expect(query.get('dateFrom')).toBe(from)
    expect(query.get('dateTo')).toBe(to)
  }
  expect(urls[5]).toBe('/api/audit/events')
})
