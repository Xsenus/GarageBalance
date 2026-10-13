import { describe, expect, it } from 'vitest'
import { getMeterReadingStartDisplay, getVisibleMeterReadingMonthKeys } from './meterYearColumns'

describe('visible meter months and separate baseline', () => {
  it('uses the later garage or connection start and retains future active months', () => {
    expect(getVisibleMeterReadingMonthKeys('2026', [{ id: 'a', number: '1', accountingStartMonth: '2026-09-01', connectedOn: '2026-10-10' }], new Set())).toEqual(['10', '11', '12'])
  })
  it('keeps only months through closure and returns no active months outside the year', () => {
    const garages = [{ id: 'a', number: '1', connectedOn: '2026-09-10', closedOn: '2026-10-04' }]
    expect(getVisibleMeterReadingMonthKeys('2026', garages, new Set())).toEqual(['09', '10'])
    expect(getVisibleMeterReadingMonthKeys('2025', garages, new Set())).toEqual([])
    expect(getVisibleMeterReadingMonthKeys('2027', garages, new Set())).toEqual([])
  })
  it('shows the union for a mixed page and always retains saved history', () => {
    expect(getVisibleMeterReadingMonthKeys('2026', [
      { id: 'a', number: '1', accountingStartMonth: '2026-12-01' },
      { id: 'b', number: '2', connectedOn: '2026-09-10', closedOn: '2026-10-04' },
    ], new Set(['2026-03', '2025-02']))).toEqual(['03', '09', '10', '12'])
  })
  it('hides the legacy base month including a known zero and handles a December base', () => {
    expect(getVisibleMeterReadingMonthKeys('2026', [{ id: 'a', number: '1', initialReadingMonth: '2026-08-01', initialReadingValue: 0 }], new Set())).toEqual(['09', '10', '11', '12'])
    expect(getVisibleMeterReadingMonthKeys('2026', [{ id: 'a', number: '1', initialReadingMonth: '2026-12-01', initialReadingValue: 7 }], new Set())).toEqual([])
    expect(getVisibleMeterReadingMonthKeys('2027', [{ id: 'a', number: '1', initialReadingMonth: '2026-12-01', initialReadingValue: 7 }], new Set())).toHaveLength(12)
  })
  it('uses an actual device date for custom services without legacy columns', () => {
    expect(getVisibleMeterReadingMonthKeys('2026', [{ id: 'a', number: '1', startValueSource: 'device', startValueDate: '2026-12-15', startValue: 0 }], new Set())).toEqual(['12'])
    expect(getVisibleMeterReadingMonthKeys('2026', [{ id: 'a', number: '1', initialReadingMonth: '2026-05-01', initialReadingValue: 100, startValueSource: 'device', startValueDate: '2026-10-15', startValue: 25 }], new Set())).toEqual(['10', '11', '12'])
    expect(getVisibleMeterReadingMonthKeys('9999', [{ id: 'a', number: '1', initialReadingMonth: '9999-12-01', initialReadingValue: 100 }], new Set())).toEqual([])
  })
  it('does not invent a period for an unconnected service, but keeps its saved history', () => {
    const garages = [{ id: 'a', number: '1', requiresConnectionDate: true, requiresStartValue: true }]
    expect(getVisibleMeterReadingMonthKeys('2026', garages, new Set())).toEqual([])
    expect(getVisibleMeterReadingMonthKeys('2026', garages, new Set(['2026-09']))).toEqual(['09'])
  })
  it('preserves the safe all-month fallback for legacy records with unknown dates', () => {
    expect(getVisibleMeterReadingMonthKeys('2026', [{ id: 'a', number: '1', initialReadingValue: 0 }], new Set())).toHaveLength(12)
    expect(getVisibleMeterReadingMonthKeys('2026', [], new Set())).toEqual([])
    expect(getVisibleMeterReadingMonthKeys('invalid', [], new Set())).toEqual([])
  })
  it('distinguishes a zero legacy value, unknown value and an unknown month', () => {
    expect(getMeterReadingStartDisplay({ id: 'a', number: '1', initialReadingMonth: '2026-08-01', initialReadingValue: 0 })).toEqual({ value: 0, description: 'База за 08.2026' })
    expect(getMeterReadingStartDisplay({ id: 'a', number: '1', initialReadingValue: 7 })).toEqual({ value: 7, description: 'Месяц базы неизвестен' })
    expect(getMeterReadingStartDisplay({ id: 'a', number: '1' })).toEqual({ value: null, description: 'База не заполнена' })
  })
  it('uses the explicit device or connection base instead of a stale garage value', () => {
    expect(getMeterReadingStartDisplay({ id: 'a', number: '1', initialReadingValue: 9, startValue: 100, startValueDate: '2026-08-31', startValueSource: 'device', startValueDeviceSerial: 'ORIGINAL' })).toEqual({ value: 100, description: 'Прибор ORIGINAL · 31.08.2026' })
    expect(getMeterReadingStartDisplay({ id: 'a', number: '1', initialReadingValue: 999, startValue: 0, startValueSource: 'connection', startValueDate: '2026-09-10' })).toEqual({ value: 0, description: 'Подключение 10.09.2026' })
    expect(getMeterReadingStartDisplay({ id: 'a', number: '1', initialReadingValue: 999, startValueSource: 'unknown', connectedOn: '2026-09-10' })).toEqual({ value: null, description: 'Подключено 10.09.2026' })
  })
})
