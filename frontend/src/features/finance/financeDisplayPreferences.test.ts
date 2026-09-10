import { describe, expect, it, vi } from 'vitest'
import { overdueDebtDetailsPreference, selectedGaragePreference, shouldRestoreSelectedGarageAfterReload } from './financeDisplayPreferences'

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

  it('stores the selected garage per user only for the current browser tab', () => {
    window.sessionStorage.clear()

    expect(selectedGaragePreference('user-1')).toBeNull()
    const garage = { id: 'garage-12', number: '12' }
    expect(selectedGaragePreference('user-1', garage)).toEqual(garage)
    expect(selectedGaragePreference('user-1')).toEqual(garage)
    expect(selectedGaragePreference('user-2')).toBeNull()
    expect(selectedGaragePreference('user-1', null)).toBeNull()
    expect(selectedGaragePreference('user-1')).toBeNull()
  })

  it('falls back safely when selected-garage session storage is unavailable', () => {
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked') })
    expect(selectedGaragePreference('user-1')).toBeNull()
    getItem.mockRestore()

    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked') })
    expect(selectedGaragePreference('user-1', { id: 'garage-12', number: '12' })).toEqual({ id: 'garage-12', number: '12' })
    setItem.mockRestore()
  })

  it('ignores a malformed selected-garage value from an older or damaged browser session', () => {
    window.sessionStorage.setItem('garagebalance.finance.selectedGarage.user-1', 'garage-12')

    expect(selectedGaragePreference('user-1')).toBeNull()
  })

  it('restores a garage only after an actual browser reload', () => {
    const navigationEntries = vi.spyOn(performance, 'getEntriesByType')
    navigationEntries.mockReturnValueOnce([{ type: 'navigate' }] as PerformanceEntry[])
    expect(shouldRestoreSelectedGarageAfterReload()).toBe(false)

    navigationEntries.mockReturnValueOnce([{ type: 'reload' }] as PerformanceEntry[])
    expect(shouldRestoreSelectedGarageAfterReload()).toBe(true)
    navigationEntries.mockRestore()
  })
})
