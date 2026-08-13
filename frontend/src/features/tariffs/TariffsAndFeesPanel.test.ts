// @vitest-environment node
import { afterEach, describe, expect, it, vi } from 'vitest'
import { getInlineTariffChangeEffectiveFrom, getServiceMeasurementUnit, getServiceTariffDisplayName } from './tariffServicePresentation'
import { formatTariffDecimal } from './tariffFormatting'

afterEach(() => {
  vi.useRealTimers()
})

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

describe('tariff service presentation', () => {
  it('prefers the unit configured in the service card and hides generated mode suffixes', () => {
    expect(getServiceMeasurementUnit({ unitName: 'м³' }, { calculationBase: 'meter_electricity' })).toBe('м³')
    expect(getServiceMeasurementUnit({ unitName: null }, { calculationBase: 'meter_electricity' })).toBe('кВт·ч')
    expect(getServiceTariffDisplayName('Вода — по счетчику', 'Вода')).toBe('Вода')
    expect(getServiceTariffDisplayName('Вода — по счетчику, 12.08.2026, abcdef12', 'Вода')).toBe('Вода')
    expect(getServiceTariffDisplayName('Вода — по счетчику', 'ВОДАКА')).toBe('ВОДАКА')
    expect(getServiceTariffDisplayName('Льготный тариф', 'Вода')).toBe('Льготный тариф')
  })

  it('starts an inline tariff correction on the current calendar date', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-12T10:00:00+07:00'))

    expect(getInlineTariffChangeEffectiveFrom('2026-01-01')).toBe('2026-08-12')
    expect(getInlineTariffChangeEffectiveFrom('2026-09-01')).toBe('2026-09-01')
  })
})
