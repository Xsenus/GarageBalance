// @vitest-environment node
import { afterEach, describe, expect, it, vi } from 'vitest'
import { findTariffGapSourcePeriod, getInlineTariffChangeEffectiveFrom, getServiceMeasurementUnit, getServiceTariffDisplayName, getTariffGapRateStart } from './tariffServicePresentation'
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

  it('changes the tariff period displayed for the working date without creating a new period today', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-12T10:00:00+07:00'))

    expect(getInlineTariffChangeEffectiveFrom('2026-01-01')).toBe('2026-01-01')
    expect(getInlineTariffChangeEffectiveFrom('2026-09-01')).toBe('2026-09-01')
    expect(getInlineTariffChangeEffectiveFrom(null)).toBe('2026-08-12')
  })

})

describe('rate entered in a tariff schedule gap', () => {
  const period = (effectiveFrom: string | null, effectiveTo: string | null) => ({ tariffId: `tariff-${effectiveFrom}`, effectiveFrom, effectiveTo, rate: 100, tariffVersion: 'v1' })

  it('copies the latest period that started before the new rate, or the first future one', () => {
    const periods = [period('2026-11-01', null), period(null, '2025-12-31'), period('2026-01-01', '2026-08-31')]
    expect(findTariffGapSourcePeriod(periods, '2026-10-20')?.effectiveFrom).toBe('2026-01-01')
    expect(findTariffGapSourcePeriod([period('2026-11-01', null)], '2026-10-20')?.effectiveFrom).toBe('2026-11-01')
    expect(findTariffGapSourcePeriod([], '2026-10-20')).toBeUndefined()
  })

  it('starts the rate with the working month unless the previous period ends inside it', () => {
    expect(getTariffGapRateStart('2026-10-20', period('2026-01-01', '2026-08-31'))).toBe('2026-10-01')
    expect(getTariffGapRateStart('2026-10-20', period('2026-11-01', null))).toBe('2026-10-01')
    expect(getTariffGapRateStart('2026-10-20', period('2026-01-01', '2026-10-09'))).toBe('2026-10-10')
    expect(getTariffGapRateStart('2026-10-31', period('2026-01-01', '2026-10-31'))).toBe('2026-10-31')
  })
})
