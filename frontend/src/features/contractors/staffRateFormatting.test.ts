import { describe, expect, it } from 'vitest'
import { formatStaffRate, parseStaffRate } from './staffRateFormatting'

describe('staff rate formatting', () => {
  it('shows grouped rates with a decimal point and two digits', () => {
    expect(formatStaffRate(100_000_000)).toBe('100 000 000.00')
    expect(formatStaffRate('42131')).toBe('42 131.00')
    expect(formatStaffRate('42 131,5')).toBe('42 131.50')
  })

  it('keeps an empty rate empty', () => {
    expect(formatStaffRate('')).toBe('')
    expect(formatStaffRate(null)).toBe('')
  })

  it('parses a formatted rate before sending it to the API', () => {
    expect(parseStaffRate('100 000 000.00')).toBe(100_000_000)
  })
})
