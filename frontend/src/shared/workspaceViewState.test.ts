import { beforeEach, describe, expect, it, vi } from 'vitest'
import { loadStoredWorkspaceView, saveStoredWorkspaceView, workspaceViewStorageKeys } from './workspaceViewState'

describe('workspace view state', () => {
  beforeEach(() => {
    window.sessionStorage.clear()
  })

  it('stores and restores only an allowed workspace view', () => {
    saveStoredWorkspaceView(workspaceViewStorageKeys.section, 'reports')

    expect(loadStoredWorkspaceView(workspaceViewStorageKeys.section, ['dashboard', 'reports'], 'dashboard')).toBe('reports')
    expect(loadStoredWorkspaceView(workspaceViewStorageKeys.section, ['dashboard', 'payments'], 'dashboard')).toBe('dashboard')
  })

  it('falls back when session storage is unavailable', () => {
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('storage unavailable')
    })
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('storage unavailable')
    })

    expect(loadStoredWorkspaceView(workspaceViewStorageKeys.section, ['dashboard', 'reports'], 'dashboard')).toBe('dashboard')
    expect(() => saveStoredWorkspaceView(workspaceViewStorageKeys.section, 'reports')).not.toThrow()

    getItem.mockRestore()
    setItem.mockRestore()
  })
})
