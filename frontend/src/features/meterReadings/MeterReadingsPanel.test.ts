import { describe, expect, it } from 'vitest'
import { isFutureMeterReadingMonth } from './meterReadingPeriod'

describe('meter reading month availability', () => {
  it('allows past and current months but rejects future months', () => {
    expect(isFutureMeterReadingMonth('2026', '06', '2026-07')).toBe(false)
    expect(isFutureMeterReadingMonth('2026', '07', '2026-07')).toBe(false)
    expect(isFutureMeterReadingMonth('2026', '08', '2026-07')).toBe(true)
    expect(isFutureMeterReadingMonth('2027', '01', '2026-07')).toBe(true)
  })
})
