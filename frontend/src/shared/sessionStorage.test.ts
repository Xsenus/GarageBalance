import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AuthResponse } from '../services/authApi'
import {
  clearStoredAuthSession,
  getDateOnlyOrDefault,
  getRowModeOrDefault,
  getStringArrayOrDefault,
  getStringOrDefault,
  isStoredAuthResponse,
  loadStoredAuthSession,
  readSessionJson,
  removeSessionJson,
  saveSessionJson,
  saveStoredAuthSession,
} from './sessionStorage'

const authKey = 'garagebalance.test.auth'

describe('shared session storage helpers', () => {
  afterEach(() => {
    window.sessionStorage.clear()
    vi.restoreAllMocks()
  })

  it('reads and writes JSON values safely', () => {
    saveSessionJson('filters', { search: 'Гараж 12' })

    expect(readSessionJson('filters')).toEqual({ search: 'Гараж 12' })
  })

  it('returns null for missing or malformed JSON values', () => {
    window.sessionStorage.setItem('broken', '{')

    expect(readSessionJson('missing')).toBeNull()
    expect(readSessionJson('broken')).toBeNull()
  })

  it('does not throw when browser storage rejects writes or removes', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('quota')
    })
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(() => saveSessionJson('filters', { search: 'x' })).not.toThrow()
    expect(() => removeSessionJson('filters')).not.toThrow()
  })

  it('loads valid auth session and clears invalid or expired values', () => {
    const auth = createAuthResponse()
    saveStoredAuthSession(authKey, auth)

    expect(loadStoredAuthSession(authKey)).toEqual(auth)

    window.sessionStorage.setItem(authKey, JSON.stringify({ accessToken: 'broken' }))
    expect(loadStoredAuthSession(authKey)).toBeNull()
    expect(window.sessionStorage.getItem(authKey)).toBeNull()

    saveStoredAuthSession(authKey, createAuthResponse({ expiresAtUtc: new Date(Date.now() - 60_000).toISOString() }))
    expect(loadStoredAuthSession(authKey)).toBeNull()
    expect(window.sessionStorage.getItem(authKey)).toBeNull()
  })

  it('clears stored auth session explicitly', () => {
    saveStoredAuthSession(authKey, createAuthResponse())

    clearStoredAuthSession(authKey)

    expect(window.sessionStorage.getItem(authKey)).toBeNull()
  })

  it('validates stored auth response shape', () => {
    expect(isStoredAuthResponse(createAuthResponse())).toBe(true)
    expect(isStoredAuthResponse({ ...createAuthResponse(), expiresAtUtc: 'not-a-date' })).toBe(false)
    expect(isStoredAuthResponse({ ...createAuthResponse(), user: { id: '1', email: 'a@b.ru', displayName: 'Admin', roles: ['administrator'], permissions: [1] } })).toBe(false)
  })

  it('normalizes primitive values from saved filter JSON', () => {
    expect(getStringOrDefault('поиск', '')).toBe('поиск')
    expect(getStringOrDefault(5, '')).toBe('')
    expect(getDateOnlyOrDefault('2026-06-25', '2026-01-01')).toBe('2026-06-25')
    expect(getDateOnlyOrDefault('25.06.2026', '2026-01-01')).toBe('2026-01-01')
    expect(getStringArrayOrDefault(['a', '', 1, 'b'])).toEqual(['a', 'b'])
    expect(getStringArrayOrDefault('a')).toEqual([])
    expect(getRowModeOrDefault('payments')).toBe('payments')
    expect(getRowModeOrDefault('accruals')).toBe('accruals')
    expect(getRowModeOrDefault('unexpected')).toBe('all')
  })
})

function createAuthResponse(overrides: Partial<AuthResponse> = {}): AuthResponse {
  return {
    accessToken: 'token',
    expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
    user: {
      id: 'user-1',
      email: 'admin@example.com',
      displayName: 'Администратор',
      roles: ['administrator'],
      permissions: ['reports.read'],
    },
    ...overrides,
  }
}
