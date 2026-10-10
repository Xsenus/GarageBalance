import type { GarageMeterStartValueDto, UpsertGarageMeterStartValueRequest } from '../../services/dictionariesApi'

export type GarageMeterStartRow = {
  meterKind: string
  label: string
  unitName: string | null
  value: string
  initialValue: string
  hasReadings: boolean
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
    }
  })
}

export function validateGarageMeterStartRows(rows: GarageMeterStartRow[] | undefined) {
  const errors: Record<string, string> = {}
  for (const row of rows ?? []) {
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
  return row.value.trim() !== '' && row.value.trim() !== row.initialValue.trim()
}

export function buildGarageMeterStartRequests(rows: GarageMeterStartRow[] | undefined): UpsertGarageMeterStartValueRequest[] {
  const requests: UpsertGarageMeterStartValueRequest[] = []
  for (const row of rows ?? []) {
    const parsed = parseMeterStartValue(row.value)
    if (isGarageMeterStartRowChanged(row) && parsed !== null) {
      requests.push({ meterKind: row.meterKind, value: parsed })
    }
  }

  return requests
}

export function describeGarageMeterStartChanges(rows: GarageMeterStartRow[] | undefined) {
  return (rows ?? [])
    .filter(isGarageMeterStartRowChanged)
    .map((row) => ({ fieldLabel: `Стартовое значение: ${row.label}`, previousValue: row.initialValue, nextValue: row.value }))
}
