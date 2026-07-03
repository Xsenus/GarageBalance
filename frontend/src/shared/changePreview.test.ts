import { describe, expect, it } from 'vitest'
import type { ChangePreview } from './changePreview'
import {
  appendChangePreview,
  appendFormattedChangePreview,
  createChangePreview,
  createFormattedChangePreview,
  formatChangeDate,
  formatChangeMoney,
  formatChangeNumber,
  formatChangeText,
  formatSensitiveChange,
} from './changePreview'

describe('change preview helpers', () => {
  it('formats empty and filled values for confirmation dialogs', () => {
    expect(formatChangeText('  Иванов  ')).toBe('Иванов')
    expect(formatChangeText('   ')).toBe('пусто')
    expect(formatChangeText(null)).toBe('пусто')

    expect(formatChangeNumber(12)).toBe('12')
    expect(formatChangeNumber(Number.NaN)).toBe('пусто')
    expect(formatChangeNumber(undefined)).toBe('пусто')

    expect(formatChangeMoney(1200.5)).toBe('1\u00a0200,50')
    expect(formatChangeMoney(null)).toBe('пусто')

    expect(formatChangeDate('2026-07-03')).toBe('03.07.2026')
    expect(formatChangeDate('')).toBe('пусто')
  })

  it('does not expose sensitive values in change previews', () => {
    expect(formatSensitiveChange('secret-before')).toBe('изменено')
    expect(formatSensitiveChange('')).toBe('пусто')
    expect(formatSensitiveChange(null)).toBe('пусто')
  })

  it('creates only real before-after changes', () => {
    expect(createChangePreview('Телефон', 'пусто', 'пусто')).toBeNull()
    expect(createChangePreview('Телефон', 'пусто', '+7')).toEqual({
      field: 'Телефон',
      before: 'пусто',
      after: '+7',
    })
  })

  it('appends plain and formatted changes to an existing list', () => {
    const changes: ChangePreview[] = []

    appendChangePreview(changes, 'Адрес', 'пусто', 'пусто')
    appendChangePreview(changes, 'Адрес', 'пусто', 'ул. Ленина')
    appendFormattedChangePreview(changes, 'Сумма', 0, 1500, formatChangeMoney)

    expect(changes).toEqual([
      { field: 'Адрес', before: 'пусто', after: 'ул. Ленина' },
      { field: 'Сумма', before: '0,00', after: '1\u00a0500,00' },
    ])
  })

  it('supports custom formatting for select-like values', () => {
    const labels: Record<string, string> = {
      owner1: 'Иванов Иван',
      owner2: 'Петров Петр',
    }

    expect(createFormattedChangePreview('Владелец', 'owner1', 'owner2', (value) => labels[value] ?? value)).toEqual({
      field: 'Владелец',
      before: 'Иванов Иван',
      after: 'Петров Петр',
    })
  })
})
