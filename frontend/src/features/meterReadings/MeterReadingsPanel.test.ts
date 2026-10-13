import { describe, expect, it } from 'vitest'
import { getMeterReadingConnectionStatus, getMeterReadingDateForMonth, isFutureMeterReadingMonth, isOutsideCurrentMeterReadingMonth } from './meterReadingPeriod'

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

  it('keeps readings within individual connection dates even in a historical first month', () => {
    expect(getMeterReadingDateForMonth('2026', '09', '2026-10', '2026-10-13', '2026-09-10')).toBe('2026-09-10')
    expect(getMeterReadingDateForMonth('2026', '10', '2026-10', '2026-10-13', '2026-09-10')).toBe('2026-10-13')
    expect(getMeterReadingDateForMonth('2026', '10', '2026-10', '2026-10-13', '2026-10-20')).toBe('2026-10-20')
    expect(getMeterReadingDateForMonth('2026', '10', '2026-10', '2026-10-13', '2026-09-10', '2026-10-05')).toBe('2026-10-05')
  })

  it('distinguishes unconnected months, missing starts, closed service and active or legacy periods', () => {
    expect(getMeterReadingConnectionStatus({ requiresConnectionDate: true }, '2026-10')).toBe('Не подключена')
    expect(getMeterReadingConnectionStatus({ connectedOn: '2026-09-10', requiresStartValue: true }, '2026-08')).toBe('Не подключена')
    expect(getMeterReadingConnectionStatus({ connectedOn: '2026-09-10', requiresStartValue: true }, '2026-09')).toBe('Требуется стартовое показание')
    expect(getMeterReadingConnectionStatus({ connectedOn: '2026-09-10' }, '2026-09')).toBeNull()
    expect(getMeterReadingConnectionStatus({ connectedOn: '2026-09-10', closedOn: '2026-10-05' }, '2026-10')).toBeNull()
    expect(getMeterReadingConnectionStatus({ closedOn: '2026-10-05' }, '2026-11')).toBe('Услуга отключена')
    expect(getMeterReadingConnectionStatus({}, '2026-01')).toBeNull()
  })

  it('checks the garage accounting month before the individual connection and permits the first month', () => {
    expect(getMeterReadingConnectionStatus({ accountingStartMonth: '2026-09-01' }, '2026-08')).toBe('До начала учёта гаража')
    expect(getMeterReadingConnectionStatus({ accountingStartMonth: '2026-09-01' }, '2026-09')).toBeNull()
    expect(getMeterReadingConnectionStatus({ accountingStartMonth: '2026-09-01', connectedOn: '2026-10-15' }, '2026-09')).toBe('Не подключена')
    expect(getMeterReadingConnectionStatus({ accountingStartMonth: '2026-09-01', connectedOn: '2026-10-15' }, '2026-10')).toBeNull()
    expect(getMeterReadingConnectionStatus({ accountingStartMonth: '2027-01-01', requiresConnectionDate: true }, '2026-12')).toBe('До начала учёта гаража')
  })
})
