import type { GarageMeterStartValueDto, UpsertGarageMeterStartValueRequest } from '../../services/dictionariesApi'

export type GarageMeterStartRow = {
  meterKind: string
  label: string
  unitName: string | null
  value: string
  initialValue: string
  hasReadings: boolean
  connectedOn: string
  initialConnectedOn: string
  requiresConnectionDate: boolean
  requiresStartValue: boolean
  requiresTariff: boolean
  connectionVersion: string | null
  reason: string
}

export const maxMeterStartValue = 999999999

export function getGarageMeterStartFieldKey(meterKind: string) {
  return `meter:${meterKind}`
}

export function formatMeterStartValue(value: number | null | undefined) {
  if (value === null || value === undefined) {
    return ''
  }

  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 3 }).format(value)
}

export function parseMeterStartValue(text: string) {
  const normalized = text.replace(/\s/g, '').replace(',', '.')
  if (!normalized) {
    return null
  }

  const parsed = Number(normalized)
  return Number.isFinite(parsed) ? parsed : null
}

export function toGarageMeterStartRows(values: GarageMeterStartValueDto[]): GarageMeterStartRow[] {
  return values.map((item) => {
    const text = formatMeterStartValue(item.value)
    return {
      meterKind: item.meterKind,
      label: item.serviceName,
      unitName: item.unitName,
      value: text,
      initialValue: text,
      hasReadings: item.hasReadings,
      connectedOn: item.connectedOn ?? '',
      initialConnectedOn: item.connectedOn ?? '',
      requiresConnectionDate: item.requiresConnectionDate ?? false,
      requiresStartValue: item.requiresStartValue ?? false,
      requiresTariff: item.requiresTariff ?? false,
      connectionVersion: item.connectionVersion ?? null,
      reason: '',
    }
  })
}

export function validateGarageMeterStartRows(rows: GarageMeterStartRow[] | undefined) {
  const errors: Record<string, string> = {}
  for (const row of rows ?? []) {
    if (requiresGarageMeterConnectionReason(row) && !row.reason.trim()) {
      errors[`meter:${row.meterKind}:reason`] = `Укажите причину изменения учёта услуги «${row.label}».`
    } else if (row.reason.trim().length > 1000) {
      errors[`meter:${row.meterKind}:reason`] = `Причина изменения учёта услуги «${row.label}» не должна превышать 1000 символов.`
    }
    if (!row.value.trim() && row.initialValue.trim() && row.hasReadings && row.initialConnectedOn) {
      errors[getGarageMeterStartFieldKey(row.meterKind)] = `Нельзя удалить стартовое значение «${row.label}», когда уже есть показания.`
    }
    if (!row.value.trim()) {
      continue
    }

    const parsed = parseMeterStartValue(row.value)
    if (parsed === null || parsed < 0 || parsed > maxMeterStartValue) {
      errors[getGarageMeterStartFieldKey(row.meterKind)] = `Стартовое значение «${row.label}» должно быть числом от 0 до 999 999 999.`
    }
  }

  return errors
}

export function isGarageMeterStartRowChanged(row: GarageMeterStartRow) {
  return row.value.trim() !== row.initialValue.trim()
}

// The start value is the reading at the end of the month before the garage's "Учитывать с"
// month. A connected meter is moved to that month; a new one is placed there by the server.
export function buildGarageMeterStartRequests(rows: GarageMeterStartRow[] | undefined, accountingStartMonth?: string): UpsertGarageMeterStartValueRequest[] {
  const requests: UpsertGarageMeterStartValueRequest[] = []
  for (const row of rows ?? []) {
    const parsed = parseMeterStartValue(row.value)
    if (isGarageMeterStartRowChanged(row) && (parsed !== null || row.initialConnectedOn && !row.value.trim())) {
      const connectedOn = row.initialConnectedOn ? (accountingStartMonth ? `${accountingStartMonth}-01` : row.initialConnectedOn) : null
      requests.push({ meterKind: row.meterKind, value: parsed,
        ...(connectedOn ? { connectedOn, connectionVersion: row.connectionVersion } : {}),
        ...(row.reason.trim() ? { reason: row.reason.trim() } : {}),
      })
    }
  }

  return requests
}

export function describeGarageMeterStartChanges(rows: GarageMeterStartRow[] | undefined) {
  return (rows ?? [])
    .filter(isGarageMeterStartRowChanged)
    .map((row) => ({ fieldLabel: `Стартовое значение: ${row.label}`, previousValue: row.initialValue, nextValue: row.value }))
}

export function getGarageMeterBaselineMonth(accountingStartMonth: string | undefined) {
  if (!accountingStartMonth || !/^\d{4}-\d{2}$/.test(accountingStartMonth)) return null
  const [year, month] = accountingStartMonth.split('-').map(Number)
  const previous = new Date(Date.UTC(year, month - 2, 1))
  return `${String(previous.getUTCMonth() + 1).padStart(2, '0')}.${previous.getUTCFullYear()}`
}

export function getGarageMeterConnectionStatus(row: GarageMeterStartRow, accountingStartMonth?: string) {
  const baselineMonth = getGarageMeterBaselineMonth(accountingStartMonth)
  if (!row.value.trim()) return row.initialConnectedOn ? 'Требуется стартовое показание' : row.requiresConnectionDate ? 'Не подключена' : null
  return baselineMonth ? `Показание на конец ${baselineMonth}` : null
}

export function requiresGarageMeterConnectionReason(row: GarageMeterStartRow) {
  return isGarageMeterStartRowChanged(row) && Boolean(row.initialConnectedOn)
}
