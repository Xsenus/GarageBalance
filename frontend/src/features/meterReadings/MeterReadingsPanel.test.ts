import { describe, expect, it } from 'vitest'
import { getMeterReadingDateForMonth, isFutureMeterReadingMonth, isOutsideCurrentMeterReadingMonth } from './meterReadingPeriod'

describe('meter reading month availability', () => {
  it('allows past and current months but rejects future months', () => {
    expect(isFutureMeterReadingMonth('2026', '06', '2026-07')).toBe(false)
    expect(isFutureMeterReadingMonth('2026', '07', '2026-07')).toBe(false)
    expect(isFutureMeterReadingMonth('2026', '08', '2026-07')).toBe(true)
    expect(isFutureMeterReadingMonth('2027', '01', '2026-07')).toBe(true)
  })

  it('identifies every month outside the current period and chooses an in-period reading date', () => {
    expect(isOutsideCurrentMeterReadingMonth('2026', '06', '2026-07')).toBe(true)
    expect(isOutsideCurrentMeterReadingMonth('2026', '07', '2026-07')).toBe(false)
    expect(isOutsideCurrentMeterReadingMonth('2026', '08', '2026-07')).toBe(true)
    expect(getMeterReadingDateForMonth('2026', '07', '2026-07', '2026-07-17')).toBe('2026-07-17')
    expect(getMeterReadingDateForMonth('2026', '06', '2026-07', '2026-07-17')).toBe('2026-06-01')
    expect(getMeterReadingDateForMonth('2028', '02', '2026-07', '2026-07-17')).toBe('2028-02-01')
  })
})
