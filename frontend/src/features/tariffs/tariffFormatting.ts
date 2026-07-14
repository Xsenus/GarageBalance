export function formatTariffDecimal(value: number | string) {
  const normalized = String(value).replace(/[\s\u00a0]+/g, '').replace(',', '.').trim()
  if (!normalized || !/^\d+(?:\.\d+)?$/.test(normalized)) {
    return String(value)
  }

  const parsed = Number(normalized)
  if (!Number.isFinite(parsed)) {
    return String(value)
  }

  const [integerPart, decimalPart] = parsed.toFixed(2).split('.')
  return `${integerPart.replace(/\B(?=(\d{3})+(?!\d))/g, ' ')}.${decimalPart}`
}
