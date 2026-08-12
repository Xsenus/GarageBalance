import type { SelectControlOption } from './SelectControl'

function normalizeSearchValue(value: string) {
  return value.trim().toLocaleLowerCase('ru-RU')
}

function normalizeLookupValue(value: string) {
  return normalizeSearchValue(value).replace(/[\s.·-]+/gu, '')
}

export function findEditableComboboxMatch(options: SelectControlOption[], value: string) {
  const query = normalizeSearchValue(value)
  if (!query) return 0

  const exactIndex = options.findIndex((option) => normalizeSearchValue(option.label) === query)
  if (exactIndex >= 0) return exactIndex

  const lookupQuery = normalizeLookupValue(query)
  const prefixIndex = options.findIndex((option) => normalizeLookupValue(option.label).startsWith(lookupQuery))
  if (prefixIndex >= 0) return prefixIndex

  return options.findIndex((option) => normalizeLookupValue(option.label).includes(lookupQuery))
}

export function editableComboboxValuesMatch(left: string, right: string) {
  return normalizeSearchValue(left) === normalizeSearchValue(right)
}
