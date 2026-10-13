import { describe, expect, it } from 'vitest'
import { buildGarageAccountingStartRequest, validateGarageAccountingMonth } from './garageAccountingStart'

describe('garage accounting month', () => {
  it.each(['1900-01', '2026-09', '2026-12', '2027-01', '9999-12'])('accepts %s', (value) => expect(validateGarageAccountingMonth(value)).toBeUndefined())
  it.each(['', '1899-12', '2026-13', '2026-00', '2026-09-01', '09.2026'])('rejects %s', (value) => expect(validateGarageAccountingMonth(value)).toContain('месяц начала учёта'))
  it('preserves compatibility with absent fields', () => expect(validateGarageAccountingMonth(undefined)).toBeUndefined())
  it('omits an unchanged month and normal edits', () => {
    expect(buildGarageAccountingStartRequest('2026-10', '2026-10')).toEqual({})
    expect(buildGarageAccountingStartRequest(undefined, undefined)).toEqual({})
  })
  it('sends the first day and trimmed correction reason', () => {
    expect(buildGarageAccountingStartRequest('2026-09', '2026-10', ' Исправление регистрации ')).toEqual({ accountingStartMonth: '2026-09-01', accountingStartReason: 'Исправление регистрации' })
    expect(buildGarageAccountingStartRequest('2026-12', '2026-10')).toEqual({ accountingStartMonth: '2026-12-01' })
  })
})
