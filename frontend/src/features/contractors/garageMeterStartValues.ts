import type { GarageMeterStartValueDto, UpsertGarageMeterStartValueRequest } from '../../services/dictionariesApi'
import { formatDateOnly } from '../../shared/formatters'

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

export function getGarageMeterConnectionFieldKey(meterKind: string) {
  return `meter:${meterKind}:connectedOn`
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
    if (row.initialConnectedOn && !row.connectedOn || row.requiresConnectionDate && row.value.trim() && !row.connectedOn) {
      errors[getGarageMeterConnectionFieldKey(row.meterKind)] = `Укажите дату подключения услуги «${row.label}».`
    }
    const parsedDate = new Date(`${row.connectedOn}T00:00:00Z`)
    if (row.connectedOn && (!/^\d{4}-\d{2}-\d{2}$/.test(row.connectedOn) || !Number.isFinite(parsedDate.getTime()) || parsedDate.toISOString().slice(0, 10) !== row.connectedOn)) {
      errors[getGarageMeterConnectionFieldKey(row.meterKind)] = `Укажите корректную дату подключения услуги «${row.label}».`
    }
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
  return row.connectedOn !== row.initialConnectedOn || (Boolean(row.connectedOn) || row.value.trim() !== '') && row.value.trim() !== row.initialValue.trim()
}

export function buildGarageMeterStartRequests(rows: GarageMeterStartRow[] | undefined): UpsertGarageMeterStartValueRequest[] {
  const requests: UpsertGarageMeterStartValueRequest[] = []
  for (const row of rows ?? []) {
    const parsed = parseMeterStartValue(row.value)
    if (isGarageMeterStartRowChanged(row) && (parsed !== null || row.connectedOn && !row.value.trim())) {
      requests.push({ meterKind: row.meterKind, value: parsed,
        ...(row.connectedOn ? { connectedOn: row.connectedOn, connectionVersion: row.connectionVersion } : {}),
        ...(row.reason.trim() ? { reason: row.reason.trim() } : {}),
      })
    }
  }

  return requests
}

export function describeGarageMeterStartChanges(rows: GarageMeterStartRow[] | undefined) {
  return (rows ?? [])
    .filter(isGarageMeterStartRowChanged)
    .flatMap((row) => [
      ...(row.connectedOn !== row.initialConnectedOn ? [{ fieldLabel: `Дата подключения: ${row.label}`, previousValue: row.initialConnectedOn ? formatDateOnly(row.initialConnectedOn) : '', nextValue: row.connectedOn ? formatDateOnly(row.connectedOn) : '' }] : []),
      ...(row.value.trim() !== row.initialValue.trim() ? [{ fieldLabel: `Стартовое значение: ${row.label}`, previousValue: row.initialValue, nextValue: row.value }] : []),
    ])
}

export function getGarageMeterConnectionStatus(row: GarageMeterStartRow) {
  if (!row.connectedOn) return row.requiresConnectionDate ? 'Не подключена' : null
  if (!row.value.trim()) return 'Требуется стартовое показание'
  return `Первый расчётный месяц: ${row.connectedOn.slice(5, 7)}.${row.connectedOn.slice(0, 4)}`
}

export function requiresGarageMeterConnectionReason(row: GarageMeterStartRow) {
  return isGarageMeterStartRowChanged(row) && Boolean(row.connectedOn) && (Boolean(row.initialConnectedOn) || row.initialValue.trim() !== '' || row.hasReadings)
}
