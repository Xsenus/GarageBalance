import type { AuthResponse } from '../services/authApi'

export function readSessionJson(key: string): unknown {
  try {
    const value = window.sessionStorage.getItem(key)
    return value ? JSON.parse(value) : null
  } catch {
    return null
  }
}

export function saveSessionJson(key: string, value: unknown) {
  try {
    window.sessionStorage.setItem(key, JSON.stringify(value))
  } catch {
    // Session storage is a convenience only; the UI must still work without it.
  }
}

export function removeSessionJson(key: string) {
  try {
    window.sessionStorage.removeItem(key)
  } catch {
    // Session storage cleanup must not block logout or filter reset flows.
  }
}

export function loadStoredAuthSession(key: string): AuthResponse | null {
  const parsed = readSessionJson(key)
  if (!isStoredAuthResponse(parsed)) {
    clearStoredAuthSession(key)
    return null
  }

  if (Date.parse(parsed.expiresAtUtc) <= Date.now()) {
    clearStoredAuthSession(key)
    return null
  }

  return parsed
}

export function saveStoredAuthSession(key: string, auth: AuthResponse) {
  saveSessionJson(key, auth)
}

export function clearStoredAuthSession(key: string) {
  removeSessionJson(key)
}

export function isStoredAuthResponse(value: unknown): value is AuthResponse {
  if (!isRecord(value) || typeof value.accessToken !== 'string' || typeof value.expiresAtUtc !== 'string' || !isRecord(value.user)) {
    return false
  }

  if (!Number.isFinite(Date.parse(value.expiresAtUtc))) {
    return false
  }

  return (
    typeof value.user.id === 'string' &&
    typeof value.user.email === 'string' &&
    typeof value.user.displayName === 'string' &&
    Array.isArray(value.user.roles) &&
    value.user.roles.every((role) => typeof role === 'string') &&
    Array.isArray(value.user.permissions) &&
    value.user.permissions.every((permission) => typeof permission === 'string')
  )
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function getStringOrDefault(value: unknown, fallback: string): string {
  return typeof value === 'string' ? value : fallback
}

export function getDateOnlyOrDefault(value: unknown, fallback: string): string {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : fallback
}

export function getStringArrayOrDefault(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string' && item.length > 0) : []
}

export function getRowModeOrDefault(value: unknown): string {
  return value === 'accruals' || value === 'payments' ? value : 'all'
}
