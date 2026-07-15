const recoveryStorageKey = 'garagebalance.lazy-chunk-reload-at'
const recoveryCooldownMs = 2 * 60 * 1000

export function isLazyChunkLoadError(error: unknown): boolean {
  if (!(error instanceof Error)) {
    return false
  }

  const message = `${error.name} ${error.message}`.toLowerCase()
  return error.name === 'ChunkLoadError'
    || message.includes('failed to fetch dynamically imported module')
    || message.includes('importing a module script failed')
    || /loading (?:css )?chunk [^ ]+ failed/.test(message)
    || message.includes('error loading dynamically imported module')
}

type RecoveryOptions = {
  storage?: Pick<Storage, 'getItem' | 'setItem'>
  reload?: () => void
  now?: () => number
}

export function recoverFromLazyChunkError(error: unknown, options: RecoveryOptions = {}): boolean {
  if (!isLazyChunkLoadError(error)) {
    return false
  }

  const storage = options.storage ?? window.sessionStorage
  const reload = options.reload ?? (() => window.location.reload())
  const now = (options.now ?? Date.now)()

  try {
    const previousReload = Number(storage.getItem(recoveryStorageKey))
    if (Number.isFinite(previousReload) && previousReload > 0 && now - previousReload < recoveryCooldownMs) {
      return false
    }

    storage.setItem(recoveryStorageKey, String(now))
  } catch {
    // If storage is unavailable, keep the visible recovery screen instead of risking a reload loop.
    return false
  }

  reload()
  return true
}
