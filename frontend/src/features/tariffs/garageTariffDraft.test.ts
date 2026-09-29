import { describe, expect, it } from 'vitest'
import { appendGarageTariffTier, createGarageTariffDraft, removeGarageTariffTier, validateGarageTariffDraft, validateGarageTariffReason } from './garageTariffDraft'

const valid = { ...createGarageTariffDraft('2026-09-01', 100, []), reason: ' Проверка ' }
describe('individual tariff draft', () => {
  it('normalizes Russian decimals, optional values and reasons without rounding away precision', () => {
    expect(validateGarageTariffDraft({ ...valid, rate: '1 234,5678', comment: ' Текст ' }, false, true)).toEqual({ terms: {
      effectiveFrom: '2026-09-01', effectiveTo: null, rate: 1234.5678, tiers: null, comment: 'Текст', reason: 'Проверка',
    } })
    expect(validateGarageTariffReason('', false)).toBeNull()
    expect(validateGarageTariffReason('', true)).not.toBeNull()
  })
  it.each([
    { from: '2026-02-30' }, { from: '1999-01-01' }, { from: '9999-01-01' }, { to: '2026-08-31' },
    { rate: '0' }, { rate: '-1' }, { rate: 'Infinity' }, { rate: '1e2' }, { rate: '1.12345' }, { rate: '1000000000' },
    { reason: ' ' }, { reason: 'x'.repeat(1001) }, { comment: 'x'.repeat(2001) },
  ])('rejects invalid date, amount or text: %j', (change) => expect(validateGarageTariffDraft({ ...valid, ...change }, false, true).error).toBeTruthy())
  it('supports tiered meter rates, inherited defaults without general-tier ids and safe tier removal', () => {
    const draft = { ...createGarageTariffDraft('2026-09-01', 2, [
      { id: 'general-legacy-1', name: 'Первая', upperBound: 100, rate: 1.2345, isCustom: false },
      { id: 'general-legacy-2', name: 'Последняя', upperBound: null, rate: 3, isCustom: false },
    ]), reason: 'Ставка' }
    expect(draft.tiers[0].id).toBeUndefined()
    expect(validateGarageTariffDraft(draft, true, true).terms?.tiers).toEqual([
      { id: undefined, name: 'Первая', upperBound: 100, rate: 1.2345 }, { id: undefined, name: 'Последняя', upperBound: undefined, rate: 3 },
    ])
    expect(validateGarageTariffDraft(draft, false, true).error).toBeTruthy()
    const added = appendGarageTariffTier(draft)
    expect(added.tiers).toHaveLength(3)
    expect(removeGarageTariffTier(added, 2).tiers.at(-1)?.upper).toBe('')
    expect(removeGarageTariffTier(draft, 0)).toBe(draft)
    expect(removeGarageTariffTier(added, 99)).toBe(added)
    expect(appendGarageTariffTier({ ...draft, tiers: Array.from({ length: 20 }, () => draft.tiers[0]) }).tiers).toHaveLength(20)
    expect(validateGarageTariffDraft({ ...draft, tiers: [{ ...draft.tiers[0], upper: '0.0001' }, draft.tiers[1]] }, true, true).error).toBeTruthy()
    expect(validateGarageTariffDraft({ ...draft, tiers: [{ ...draft.tiers[0], name: ' ' }, draft.tiers[1]] }, true, true).error).toBeTruthy()
    expect(validateGarageTariffDraft({ ...draft, tiers: [draft.tiers[0], { ...draft.tiers[1], upper: '200' }] }, true, true).error).toBeTruthy()
  })
})
