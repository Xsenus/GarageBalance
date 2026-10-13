import { describe, expect, it } from 'vitest'
import {
  buildGarageMeterStartRequests,
  describeGarageMeterStartChanges,
  formatMeterStartValue,
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

  it('saves a dated pending connection and distinguishes an explicitly entered zero', () => {
    const [row] = toGarageMeterStartRows([dto({ value: null, requiresConnectionDate: true })])
    expect(getGarageMeterConnectionStatus(row)).toBe('Не подключена')
    const dated = { ...row, connectedOn: '2026-09-10' }
    expect(validateGarageMeterStartRows([dated])).toEqual({})
    expect(buildGarageMeterStartRequests([dated])).toEqual([{ meterKind: 'water', value: null, connectedOn: '2026-09-10', connectionVersion: null }])
    expect(getGarageMeterConnectionStatus(dated)).toBe('Требуется стартовое показание')
    expect(buildGarageMeterStartRequests([{ ...dated, value: '0' }])[0].value).toBe(0)
    expect(getGarageMeterConnectionStatus({ ...dated, value: '0' })).toBe('Первый расчётный месяц: 09.2026')
    expect(describeGarageMeterStartChanges([dated])).toEqual([{ fieldLabel: 'Дата подключения: Вода', previousValue: '', nextValue: '10.09.2026' }])
  })

  it('requires valid dates for new services and prevents removing dates or recorded baselines', () => {
    const [row] = toGarageMeterStartRows([dto({ value: null, requiresConnectionDate: true })])
    expect(validateGarageMeterStartRows([{ ...row, value: '0' }])).toHaveProperty('meter:water:connectedOn')
    for (const connectedOn of ['2026-02-30', '2026-13-01', 'invalid']) {
      expect(validateGarageMeterStartRows([{ ...row, connectedOn }])).toHaveProperty('meter:water:connectedOn')
    }
    expect(validateGarageMeterStartRows([{ ...row, connectedOn: '2028-02-29' }])).toEqual({})
    const [active] = toGarageMeterStartRows([dto({ connectedOn: '2026-09-10', hasReadings: true, connectionVersion: 'v1' })])
    expect(validateGarageMeterStartRows([{ ...active, connectedOn: '' }])).toHaveProperty('meter:water:connectedOn')
    expect(validateGarageMeterStartRows([{ ...active, value: '', reason: 'Акт' }])).toHaveProperty('meter:water')
    expect(getGarageMeterConnectionStatus(toGarageMeterStartRows([dto()])[0])).toBeNull()
  })

  it('requires reasons for existing connections and legacy histories while preserving concurrency versions', () => {
    const [row] = toGarageMeterStartRows([dto({ connectedOn: '2026-09-10', connectionVersion: 'v1' })])
    expect(requiresGarageMeterConnectionReason(row)).toBe(false)
    const changed = { ...row, connectedOn: '2026-09-12', value: '250' }
    expect(validateGarageMeterStartRows([changed])).toHaveProperty('meter:water:reason')
    expect(validateGarageMeterStartRows([{ ...changed, reason: 'a'.repeat(1001) }])).toHaveProperty('meter:water:reason')
    const withReason = { ...changed, reason: '  Акт подключения  ' }
    expect(validateGarageMeterStartRows([withReason])).toEqual({})
    expect(buildGarageMeterStartRequests([withReason])).toEqual([{ meterKind: 'water', value: 250, connectedOn: '2026-09-12', connectionVersion: 'v1', reason: 'Акт подключения' }])
    expect(describeGarageMeterStartChanges([withReason])[0]).toEqual({ fieldLabel: 'Дата подключения: Вода', previousValue: '10.09.2026', nextValue: '12.09.2026' })
    const [legacy] = toGarageMeterStartRows([dto({ hasReadings: true })])
    expect(requiresGarageMeterConnectionReason({ ...legacy, connectedOn: '2026-09-01' })).toBe(true)
    expect(requiresGarageMeterConnectionReason({ ...legacy, value: '250' })).toBe(false)
  })
})
