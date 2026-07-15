export function parseMoneyInput(value: string): number {
  if (value.trim() === '') {
    return Number.NaN
  }

  const parsedValue = Number(value.replace(/\s/g, '').replace(',', '.'))
  return Number.isFinite(parsedValue) ? parsedValue : Number.NaN
}

export function formatMoneyInput(value: number): string {
  if (!Number.isFinite(value)) {
    return ''
  }

  return new Intl.NumberFormat('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value).replace(/,/g, ' ')
}

export function formatMoneyTextInput(value: string | number | null | undefined): string {
  if (value === null || value === undefined || (typeof value === 'string' && value.trim() === '')) {
    return ''
  }

  const numericValue = typeof value === 'number' ? value : parseMoneyInput(value)
  if (!Number.isFinite(numericValue)) {
    return typeof value === 'string' ? value : ''
  }

  return formatMoneyInput(numericValue)
}
