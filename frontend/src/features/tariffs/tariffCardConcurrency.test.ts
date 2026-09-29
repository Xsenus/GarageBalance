import { expect, it } from 'vitest'
import type { ChargeServiceSettingDto } from '../../services/dictionariesApi'
import { sameTariffScheduleTerms, sameTariffServiceSettings } from './tariffCardConcurrency'

it('allows a version-only retry but never overwrites someone else’s garage scope or service edits', () => {
  const original = { name: 'Охрана', version: 'v1' } as ChargeServiceSettingDto
  expect(sameTariffServiceSettings(original, { ...original, version: 'v2', appliesToSelectedGarages: false, garageIds: [] })).toBe(true)
  expect(sameTariffServiceSettings(original, { ...original, appliesToSelectedGarages: true, garageIds: ['85'] })).toBe(false)
  expect(sameTariffServiceSettings(original, { ...original, paymentDueDay: 27 })).toBe(false)
  expect(sameTariffServiceSettings(original, { ...original, isArchived: true })).toBe(false)
  const restricted = { ...original, appliesToSelectedGarages: true, garageIds: ['85', '86'] }
  expect(sameTariffServiceSettings(restricted, { ...restricted, garageIds: ['86', '85'], version: 'v2' })).toBe(true)
  expect(sameTariffServiceSettings(restricted, { ...restricted, garageIds: ['85'] })).toBe(false)
})

it('ignores version and array order but protects tariff prices, dates and deleted periods', () => {
  const first = { tariffId: '1', effectiveFrom: null, effectiveTo: '2026-08-31', rate: 350, tariffVersion: 'v1' }
  const second = { ...first, tariffId: '2', effectiveFrom: '2026-09-01', effectiveTo: null }
  expect(sameTariffScheduleTerms([first, second], [{ ...second, tariffVersion: 'v2' }, first])).toBe(true)
  expect(sameTariffScheduleTerms([first], [{ ...first, rate: 400 }])).toBe(false)
  expect(sameTariffScheduleTerms([first], [{ ...first, effectiveTo: null }])).toBe(false)
  expect(sameTariffScheduleTerms([first, second], [first])).toBe(false)
  expect(sameTariffScheduleTerms([], [])).toBe(true)
})
