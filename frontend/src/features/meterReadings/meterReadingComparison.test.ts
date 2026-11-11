import { describe, expect, it } from 'vitest'
import type { MeterReadingYearGarageDto, MeterReadingYearValueDto } from '../../services/financeApi'
import { getMeterReadingComparisonValue } from './meterReadingComparison'

const garage: MeterReadingYearGarageDto = {
  id: 'garage', number: '1', accountingStartMonth: '2020-01-01',
  startValueSource: 'device', startValueDate: '2026-10-15', startValue: 0,
}

function reading(month: string, value: number, device?: string | null, extra: Partial<MeterReadingYearValueDto> = {}): MeterReadingYearValueDto {
  return { id: month, garageId: garage.id, accountingMonth: `${month}-01`, currentValue: value, version: 'v1', meterDeviceId: device, ...extra }
}

describe('meter comparison within a device chain', () => {
  it.each([0, 30])('uses the restarted baseline %s instead of removed-device history, even in the baseline cell', (value) => {
    expect(getMeterReadingComparisonValue({ ...garage, startValue: value }, '2026-10', [reading('2026-08', 100, 'old'), reading('2026-09', 110, 'old')])).toBe(value)
  })

  it.each(['new', null, undefined])('uses the latest reading after restart for a new month, device %s', (device) => {
    expect(getMeterReadingComparisonValue(garage, '2026-12', [reading('2026-10', 5, device), reading('2026-08', 100, 'old'), reading('2026-11', 7, device)])).toBe(7)
  })

  it.each([['old', 'new'], [null, 'new'], ['old', null]])('does not compare an existing %s reading with device %s', (previousDevice, currentDevice) => {
    expect(getMeterReadingComparisonValue({ ...garage, connectedOn: '2026-07-01' }, '2026-10', [reading('2026-09', 100, previousDevice), reading('2026-10', 5, currentDevice)])).toBeNull()
  })

  it.each(['same', null, undefined])('retains decreases within the same device, including legacy identity %s', (device) => {
    expect(getMeterReadingComparisonValue(garage, '2026-11', [reading('2026-10', 5, device), reading('2026-11', 6, device)])).toBe(5)
  })

  it('uses a historical removed device without applying a later restart baseline', () => {
    expect(getMeterReadingComparisonValue({ ...garage, startValue: 1000 }, '2026-08', [reading('2026-07', 50, 'removed'), reading('2026-08', 60, 'removed')])).toBe(50)
    expect(getMeterReadingComparisonValue(garage, '2026-08', [reading('2026-08', 60, 'removed')])).toBeNull()
  })

  it('preserves a replacement boundary when legacy payloads omit both device IDs', () => {
    expect(getMeterReadingComparisonValue({ ...garage, connectedOn: '2026-07-01' }, '2026-10', [reading('2026-09', 100), reading('2026-10', 5, undefined, { isMeterReplacement: true })])).toBeNull()
  })

  it('uses the new device initial value from its replacement marker', () => {
    const current = reading('2026-10', 5, 'new', {
      isMeterReplacement: true,
      meterReplacement: { deviceId: 'new', date: '2026-10-15', oldSerial: 'OLD', newSerial: 'NEW', oldFinalValue: 100, newInitialValue: 2, currentValue: 5, removedDevicesConsumption: 0, totalConsumption: 3 },
    })
    expect(getMeterReadingComparisonValue({ ...garage, connectedOn: '2026-07-01' }, '2026-10', [reading('2026-09', 100, 'old'), current])).toBe(2)
    expect(getMeterReadingComparisonValue(garage, '2026-10', [{ ...current, meterDeviceId: 'different' }])).toBeNull()
  })

  it('does not compare across a known restart when old payloads omit identity', () => {
    expect(getMeterReadingComparisonValue(garage, '2026-10', [reading('2026-08', 100), reading('2026-10', 5)])).toBe(0)
  })

  it('does not assume an unmatched saved device shares the garage connection baseline', () => {
    expect(getMeterReadingComparisonValue({ ...garage, connectedOn: '2026-07-01', startValueSource: 'connection', startValue: 1000 }, '2026-11', [reading('2026-11', 5, 'new')])).toBeNull()
  })

  it('keeps legacy previous readings without a known baseline and ignores other garages', () => {
    expect(getMeterReadingComparisonValue({ id: 'garage', number: '1' }, '2026-10', [reading('2026-08', 100), reading('2026-09', 999, null, { garageId: 'other' })])).toBe(100)
  })

  it('does not invent a missing or out-of-year baseline', () => {
    expect(getMeterReadingComparisonValue({ ...garage, startValue: null }, '2026-10', [])).toBeNull()
    expect(getMeterReadingComparisonValue(garage, '2027-01', [])).toBeNull()
    expect(getMeterReadingComparisonValue({ id: 'garage', number: '1' }, '2026-10', [])).toBeNull()
  })
})
