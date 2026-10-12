import { describe, expect, it } from 'vitest'
import { getMeterReplacementConsumption, parseReplacementValue } from './meterReplacementConsumption'

describe('getMeterReplacementConsumption', () => {
  it.each(['', '-1', '1.1234', '1000000000', 'NaN', '1e3'])('rejects an invalid replacement value %s', (value) => {
    expect(parseReplacementValue(value)).toBeNull()
  })

  it.each([[' 1,125 ', 1.125], ['0', 0], ['999999999', 999999999]] as const)('parses replacement value %s', (value, expected) => {
    expect(parseReplacementValue(value)).toBe(expected)
  })

  it('splits the month consumption between the removed and the new meter', () => {
    expect(getMeterReplacementConsumption(1000, 1200, 0, 10)).toEqual({ oldMeter: 200, newMeter: 10, total: 210 })
  })

  it('reports negative parts instead of hiding invalid input and rounds to three decimals', () => {
    expect(getMeterReplacementConsumption(100, 99.5, 5, 4)).toEqual({ oldMeter: -0.5, newMeter: -1, total: -1.5 })
    expect(getMeterReplacementConsumption(0.1, 0.3, 0, 0.2)).toEqual({ oldMeter: 0.2, newMeter: 0.2, total: 0.4 })
  })

  it('includes all earlier devices and rounds the month total without floating-point residue', () => {
    expect(getMeterReplacementConsumption(0.1, 0.3, 0, 0.2, 1.001)?.total).toBe(1.401)
    expect(getMeterReplacementConsumption(5, 9, 20, 23, 10)).toEqual({ oldMeter: 4, newMeter: 3, total: 17 })
  })

  it('stays silent until every value is known', () => {
    expect(getMeterReplacementConsumption(null, 1, 0, 1)).toBeNull()
    expect(getMeterReplacementConsumption(0, null, 0, 1)).toBeNull()
    expect(getMeterReplacementConsumption(0, 1, null, 1)).toBeNull()
    expect(getMeterReplacementConsumption(0, 1, 0, null)).toBeNull()
  })
})
