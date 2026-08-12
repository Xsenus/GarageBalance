import { describe, expect, it, vi } from 'vitest'
import { overdueDebtDetailsPreference } from './financeDisplayPreferences'

describe('finance display preferences', () => {
  it('shows overdue details by default and stores a separate choice for each user', () => {
    window.localStorage.clear()

    expect(overdueDebtDetailsPreference('user-1')).toBe(true)
    overdueDebtDetailsPreference('user-1', false)

    expect(overdueDebtDetailsPreference('user-1')).toBe(false)
    expect(overdueDebtDetailsPreference('user-2')).toBe(true)
  })

  it('keeps the payments screen usable when browser storage is unavailable', () => {
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked') })

    expect(overdueDebtDetailsPreference('user-1')).toBe(true)
    getItem.mockRestore()
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked') })
    expect(() => overdueDebtDetailsPreference('user-1', false)).not.toThrow()
    setItem.mockRestore()
  })
})
