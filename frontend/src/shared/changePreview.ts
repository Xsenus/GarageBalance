import { formatDateOnly, formatMoney } from './formatters'

export type ChangePreview = {
  field: string
  before: string
  after: string
}

export function formatChangeText(value: string | null | undefined) {
  return value?.trim() ? value.trim() : 'пусто'
}

export function formatChangeNumber(value: number | null | undefined) {
  return value == null || Number.isNaN(value) ? 'пусто' : String(value)
}

export function formatChangeMoney(value: number | null | undefined) {
  return value == null || Number.isNaN(value) ? 'пусто' : formatMoney(value)
}

export function formatChangeDate(value: string | null | undefined) {
  return value ? formatDateOnly(value) : 'пусто'
}

export function formatSensitiveChange(value: string | null | undefined) {
  return value == null || value === '' ? 'пусто' : 'изменено'
}

export function createChangePreview(field: string, before: string, after: string): ChangePreview | null {
  return before === after ? null : { field, before, after }
}

export function appendChangePreview(changes: ChangePreview[], field: string, before: string, after: string) {
  const change = createChangePreview(field, before, after)
  if (change) {
    changes.push(change)
  }
}

export function createFormattedChangePreview<T>(field: string, before: T, after: T, formatter: (value: T) => string): ChangePreview | null {
  return createChangePreview(field, formatter(before), formatter(after))
}

export function appendFormattedChangePreview<T>(changes: ChangePreview[], field: string, before: T, after: T, formatter: (value: T) => string) {
  const change = createFormattedChangePreview(field, before, after, formatter)
  if (change) {
    changes.push(change)
  }
}
