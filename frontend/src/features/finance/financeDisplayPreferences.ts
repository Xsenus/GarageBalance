const overdueDebtDetailsStoragePrefix = 'garagebalance.finance.overdueExpanded'

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
