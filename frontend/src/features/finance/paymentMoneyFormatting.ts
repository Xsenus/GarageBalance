export function parsePaymentMoney(value: string) {
  return Number(value.trim().replace(/\s/g, '').replace(',', '.'))
}

export function formatPaymentMoney(value: number | string) {
  if (typeof value === 'string' && value.trim() === '') {
    return ''
  }

  const numericValue = typeof value === 'number' ? value : parsePaymentMoney(value)
  if (!Number.isFinite(numericValue)) {
    return typeof value === 'string' ? value : ''
  }

  return new Intl.NumberFormat('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(numericValue).replace(/,/g, ' ')
}
