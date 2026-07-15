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
