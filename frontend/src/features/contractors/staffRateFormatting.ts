export function parseStaffRate(value: string) {
  const normalized = value.replace(/\s/g, '').replace(',', '.')
  const numericPart = normalized.match(/-?\d+(?:\.\d+)?/)?.[0]
  const parsed = Number(numericPart)

  return Number.isFinite(parsed) ? parsed : Number.NaN
}

export function formatStaffRate(value: string | number | null | undefined) {
  if (value === null || value === undefined || (typeof value === 'string' && value.trim() === '')) {
    return ''
  }

  const numericValue = typeof value === 'number' ? value : parseStaffRate(value)
  if (!Number.isFinite(numericValue)) {
    return ''
  }

  return new Intl.NumberFormat('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(numericValue).replace(/,/g, ' ')
}
