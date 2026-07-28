// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { expensePaymentTypeOptions, formatExpensePaymentSource, formatExpensePaymentType } from './expensePaymentTypes'

describe('expense payment types', () => {
  it('keeps the operation type independent from an expense article', () => {
    expect(expensePaymentTypeOptions).toEqual([
      { value: 'with_receipt', label: 'С чеком' },
      { value: 'without_receipt', label: 'Без чека' },
    ])
    expect(formatExpensePaymentType('with_receipt')).toBe('С чеком')
    expect(formatExpensePaymentType('without_receipt')).toBe('Без чека')
    expect(formatExpensePaymentType(null)).toBe('С чеком')
    expect(formatExpensePaymentSource('bank', 'without_receipt')).toBe('Банк')
    expect(formatExpensePaymentSource('cash', 'with_receipt')).toBe('Касса')
    expect(formatExpensePaymentSource(null, 'without_receipt')).toBe('Касса')
    expect(formatExpensePaymentSource(null, 'with_receipt')).toBe('Банк')
  })
})
