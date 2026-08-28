// @vitest-environment node
import { describe, expect, it } from 'vitest'

import type { GarageDto } from '../../services/dictionariesApi'
import { formatFinanceGarageReference, formatFinanceReference } from './financeChangePreview'

describe('finance change preview references', () => {
  it('uses an existing label, a loaded reference and the identifier fallback in that order', () => {
    const references = [{ id: 'income-membership', name: 'Членский взнос' }]

    expect(formatFinanceReference(null, null, references)).toBe('пусто')
    expect(formatFinanceReference('income-membership', 'Название из записи', references)).toBe('Название из записи')
    expect(formatFinanceReference('income-membership', null, references)).toBe('Членский взнос')
    expect(formatFinanceReference('income-unknown', null, references)).toBe('income-unknown')
  })

  it('formats garage labels from an existing number, a loaded garage and the identifier fallback', () => {
    const garages = [{ id: 'garage-12', number: '12' }] as GarageDto[]

    expect(formatFinanceGarageReference(undefined, null, garages)).toBe('пусто')
    expect(formatFinanceGarageReference('garage-12', '77', garages)).toBe('Гараж 77')
    expect(formatFinanceGarageReference('garage-12', null, garages)).toBe('Гараж 12')
    expect(formatFinanceGarageReference('garage-unknown', null, garages)).toBe('Гараж garage-unknown')
  })
})
