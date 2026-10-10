import { describe, expect, it } from 'vitest'
import {
  buildGarageMeterStartRequests,
  describeGarageMeterStartChanges,
  formatMeterStartValue,
  getGarageMeterStartFieldKey,
  parseMeterStartValue,
  toGarageMeterStartRows,
  validateGarageMeterStartRows,
} from './garageMeterStartValues'

const dto = (overrides = {}) => ({ meterKind: 'water', serviceName: 'Вода', unitName: 'м³', value: 1234.5 as number | null, hasReadings: false, ...overrides })

describe('garage meter start values', () => {
  it('formats and parses Russian number text', () => {
    expect(formatMeterStartValue(null)).toBe('')
    expect(parseMeterStartValue(formatMeterStartValue(119153))).toBe(119153)
    expect(parseMeterStartValue('1 234,567')).toBe(1234.567)
    expect(parseMeterStartValue('')).toBeNull()
    expect(parseMeterStartValue('abc')).toBeNull()
  })

  it('maps service values to editable rows with the loaded value as the baseline', () => {
    const [row] = toGarageMeterStartRows([dto()])
    expect(row).toMatchObject({ meterKind: 'water', label: 'Вода', value: row.initialValue, hasReadings: false })
    expect(getGarageMeterStartFieldKey('water')).toBe('meter:water')
  })

  it('validates only filled values and names the service', () => {
    const rows = toGarageMeterStartRows([dto(), dto({ meterKind: 'service_x', serviceName: 'Коммерческий тариф', value: null })])
    expect(validateGarageMeterStartRows(rows)).toEqual({})
    expect(validateGarageMeterStartRows([{ ...rows[0], value: '-1' }, { ...rows[1], value: '1000000000' }])).toEqual({
      'meter:water': 'Стартовое значение «Вода» должно быть числом от 0 до 999 999 999.',
      'meter:service_x': 'Стартовое значение «Коммерческий тариф» должно быть числом от 0 до 999 999 999.',
    })
    expect(validateGarageMeterStartRows(undefined)).toEqual({})
  })

  it('builds requests and change entries only for edited non-empty values', () => {
    const rows = toGarageMeterStartRows([dto(), dto({ meterKind: 'service_x', serviceName: 'Коммерческий тариф', value: null })])
    expect(buildGarageMeterStartRequests(rows)).toEqual([])
    const edited = [{ ...rows[0], value: '15' }, { ...rows[1], value: '5000' }]
    expect(buildGarageMeterStartRequests(edited)).toEqual([{ meterKind: 'water', value: 15 }, { meterKind: 'service_x', value: 5000 }])
    expect(describeGarageMeterStartChanges(edited)).toEqual([
      { fieldLabel: 'Стартовое значение: Вода', previousValue: rows[0].initialValue, nextValue: '15' },
      { fieldLabel: 'Стартовое значение: Коммерческий тариф', previousValue: '', nextValue: '5000' },
    ])
    expect(buildGarageMeterStartRequests([{ ...rows[0], value: '' }])).toEqual([])
    expect(buildGarageMeterStartRequests(undefined)).toEqual([])
  })
})
