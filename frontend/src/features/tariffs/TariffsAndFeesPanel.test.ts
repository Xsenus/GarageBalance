import { describe, expect, it } from 'vitest'
import { formatTariffDecimal } from './tariffFormatting'

describe('formatTariffDecimal', () => {
  it('formats tariff values with grouped thousands and two decimal places', () => {
    expect(formatTariffDecimal(1000)).toBe('1 000.00')
    expect(formatTariffDecimal(1_000_000)).toBe('1 000 000.00')
    expect(formatTariffDecimal(100.6)).toBe('100.60')
    expect(formatTariffDecimal('10,17')).toBe('10.17')
  })

  it('keeps an unfinished or invalid editable value unchanged', () => {
    expect(formatTariffDecimal('')).toBe('')
    expect(formatTariffDecimal('12.')).toBe('12.')
  })
})
