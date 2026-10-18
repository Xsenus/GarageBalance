import { describe, expect, it } from 'vitest'
import {
  buildGarageMeterStartRequests,
  describeGarageMeterStartChanges,
  formatMeterStartValue,
  getGarageMeterBaselineMonth,
  getGarageMeterStartFieldKey,
  getGarageMeterConnectionStatus,
  requiresGarageMeterConnectionReason,
  parseMeterStartValue,
  toGarageMeterStartRows,
  validateGarageMeterStartRows,
} from './garageMeterStartValues'

const dto = (overrides = {}) => ({ meterKind: 'water', serviceName: 'Вода', unitName: 'м³', value: 1234.5 as number | null, hasReadings: false, ...overrides })

describe('garage meter start values', () => {
  it('formats and parses Russian number text', () => {
    expect(formatMeterStartValue(null)).toBe('')
    expect(parseMeterStartValue(formatMeterStartValue(1000))).toBe(1000)
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

  it('places a new start value before "Учитывать с" without a separate connection date', () => {
    const [row] = toGarageMeterStartRows([dto({ value: null, requiresConnectionDate: true })])
    expect(getGarageMeterConnectionStatus(row, '2026-09')).toBe('Не подключена')
    const entered = { ...row, value: '320' }
    expect(validateGarageMeterStartRows([entered])).toEqual({})
    expect(buildGarageMeterStartRequests([entered], '2026-09')).toEqual([{ meterKind: 'water', value: 320 }])
    expect(getGarageMeterConnectionStatus(entered, '2026-09')).toBe('Показание на конец 08.2026')
    expect(getGarageMeterConnectionStatus(entered, '2027-01')).toBe('Показание на конец 12.2026')
    expect(getGarageMeterConnectionStatus(entered)).toBeNull()
    expect(getGarageMeterBaselineMonth('invalid')).toBeNull()
    expect(buildGarageMeterStartRequests([{ ...row, value: '0' }])[0].value).toBe(0)
    expect(requiresGarageMeterConnectionReason(entered)).toBe(false)
  })

  it('moves an existing connection to the garage start and keeps its version and reason', () => {
    const [row] = toGarageMeterStartRows([dto({ connectedOn: '2026-10-18', connectionVersion: 'v1', value: 320 })])
    expect(requiresGarageMeterConnectionReason(row)).toBe(false)
    const changed = { ...row, value: '330' }
    expect(validateGarageMeterStartRows([changed])).toHaveProperty('meter:water:reason')
    expect(validateGarageMeterStartRows([{ ...changed, reason: 'a'.repeat(1001) }])).toHaveProperty('meter:water:reason')
    const withReason = { ...changed, reason: '  Акт  ' }
    expect(validateGarageMeterStartRows([withReason])).toEqual({})
    expect(buildGarageMeterStartRequests([withReason], '2026-09')).toEqual([{ meterKind: 'water', value: 330, connectedOn: '2026-09-01', connectionVersion: 'v1', reason: 'Акт' }])
    expect(buildGarageMeterStartRequests([withReason])).toEqual([{ meterKind: 'water', value: 330, connectedOn: '2026-10-18', connectionVersion: 'v1', reason: 'Акт' }])
    expect(getGarageMeterConnectionStatus({ ...row, value: '' })).toBe('Требуется стартовое показание')
    expect(validateGarageMeterStartRows([{ ...row, hasReadings: true, value: '', reason: 'Акт' }])).toHaveProperty('meter:water')
    expect(describeGarageMeterStartChanges([withReason])).toEqual([{ fieldLabel: 'Стартовое значение: Вода', previousValue: '320', nextValue: '330' }])
    const [legacy] = toGarageMeterStartRows([dto({ hasReadings: true })])
    expect(requiresGarageMeterConnectionReason({ ...legacy, value: '250' })).toBe(false)
  })
})
