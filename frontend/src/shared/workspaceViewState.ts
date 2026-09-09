export const workspaceViewStorageKeys = {
  section: 'garagebalance.workspace.section',
  contractorsSection: 'garagebalance.workspace.contractors.section',
  dictionariesSection: 'garagebalance.workspace.dictionaries.section',
  financeSection: 'garagebalance.workspace.finance.section',
  paymentsTab: 'garagebalance.workspace.payments.tab',
  reportsTab: 'garagebalance.workspace.reports.tab',
  importTab: 'garagebalance.workspace.import.tab',
  tariffsView: 'garagebalance.workspace.tariffs.view',
  settingsTab: 'garagebalance.workspace.settings.tab',
} as const

export function loadStoredWorkspaceView<T extends string>(key: string, allowedValues: readonly T[], fallback: T): T {
  try {
    const storedValue = window.sessionStorage.getItem(key)
    return storedValue && allowedValues.includes(storedValue as T)
      ? storedValue as T
      : fallback
  } catch {
    return fallback
  }
}

export function saveStoredWorkspaceView(key: string, value: string) {
  try {
    window.sessionStorage.setItem(key, value)
  } catch {
    // Navigation persistence is progressive enhancement; the workspace remains usable without storage.
  }
}
