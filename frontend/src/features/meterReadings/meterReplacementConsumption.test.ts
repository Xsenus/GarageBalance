import { describe, expect, it } from 'vitest'
import { getMeterReplacementConsumption } from './meterReplacementConsumption'

describe('getMeterReplacementConsumption', () => {
  it('splits the month consumption between the removed and the new meter', () => {
    expect(getMeterReplacementConsumption(119153, 124626, 0, 10)).toEqual({ oldMeter: 5473, newMeter: 10 })
  })

  it('reports negative parts instead of hiding invalid input and rounds to three decimals', () => {
    expect(getMeterReplacementConsumption(100, 99.5, 5, 4)).toEqual({ oldMeter: -0.5, newMeter: -1 })
    expect(getMeterReplacementConsumption(0.1, 0.3, 0, 0.2)).toEqual({ oldMeter: 0.2, newMeter: 0.2 })
  })

  it('stays silent until every value is known', () => {
    expect(getMeterReplacementConsumption(null, 1, 0, 1)).toBeNull()
    expect(getMeterReplacementConsumption(0, null, 0, 1)).toBeNull()
    expect(getMeterReplacementConsumption(0, 1, null, 1)).toBeNull()
    expect(getMeterReplacementConsumption(0, 1, 0, null)).toBeNull()
  })
})
