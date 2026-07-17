// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { formatTariffDecimal } from './tariffFormatting'

describe('formatTariffDecimal', () => {
  it.each([
    [1000, '1 000.00'],
    [100.6, '100.60'],
    ['100', '100.00'],
    ['100.', '100.00'],
    ['100,', '100.00'],
    ['7.5', '7.50'],
    ['10,17', '10.17'],
    ['1000000', '1 000 000.00'],
    ['1 000 000.25', '1 000 000.25'],
  ])('formats %s with grouped thousands and two decimal places', (value, expected) => {
    expect(formatTariffDecimal(value)).toBe(expected)
  })

  it('keeps an empty or invalid editable value available for correction', () => {
    expect(formatTariffDecimal('')).toBe('')
    expect(formatTariffDecimal('not-a-number')).toBe('not-a-number')
  })
})
