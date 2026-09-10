const overdueDebtDetailsStoragePrefix = 'garagebalance.finance.overdueExpanded'
const selectedGarageStoragePrefix = 'garagebalance.finance.selectedGarage'

export type SelectedGaragePreference = {
  id: string
  number: string
}

export function shouldRestoreSelectedGarageAfterReload() {
  return (performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined)?.type === 'reload'
}

export function overdueDebtDetailsPreference(userId: string, expanded?: boolean) {
  try {
    const key = `${overdueDebtDetailsStoragePrefix}.${userId}`
    if (expanded === undefined) return window.localStorage.getItem(key) !== 'false'
    window.localStorage.setItem(key, String(expanded))
    return expanded
  } catch {
    return true
  }
}

export function selectedGaragePreference(userId: string, garage?: SelectedGaragePreference | null) {
  try {
    const key = `${selectedGarageStoragePrefix}.${userId}`
    if (garage === undefined) {
      const stored = window.sessionStorage.getItem(key)
      if (!stored) return null
      const [id, number, extra] = stored.split('\n')
      return id && number && extra === undefined ? { id, number } : null
    }
    if (garage === null) {
      window.sessionStorage.removeItem(key)
      return null
    }
    window.sessionStorage.setItem(key, `${garage.id}\n${garage.number}`)
    return garage
  } catch {
    return garage === undefined ? null : garage
  }
}
