import { describe, expect, it } from 'vitest'
import { financeSectionOptions, getFinanceEditorSavingScope, getFinanceEditorSubmitLabel, getFinanceEditorTitle } from './financeWorkbench'
import type { FinanceEditorKey } from './financeWorkbench'

const editorKeys: FinanceEditorKey[] = [
  'income',
  'expense',
  'accruals',
  'regularAccruals',
  'supplierGroupSalaryAccruals',
  'supplierAccruals',
  'meterReadings',
]

describe('finance workbench metadata', () => {
  it('keeps the payment table sections in the expected order', () => {
    expect(financeSectionOptions).toEqual([
      { key: 'income', label: 'Приходы', description: 'Оплаты владельцев' },
      { key: 'expense', label: 'Расходы', description: 'Выплаты поставщикам' },
      { key: 'accruals', label: 'Начисления владельцам', description: 'Долги по гаражам' },
      { key: 'supplierAccruals', label: 'Начисления поставщикам', description: 'Обязательства' },
      { key: 'meterReadings', label: 'Счетчики', description: 'Вода и электричество' },
    ])
  })

  it('returns dialog titles for every payment editor', () => {
    expect(Object.fromEntries(editorKeys.map((key) => [key, getFinanceEditorTitle(key)]))).toEqual({
      income: 'Новое поступление',
      expense: 'Новая выплата',
      accruals: 'Ручное начисление',
      regularAccruals: 'Регулярные начисления',
      supplierGroupSalaryAccruals: 'Зарплата группы',
      supplierAccruals: 'Начисление поставщику',
      meterReadings: 'Показание счетчика',
    })
  })

  it('returns create submit labels for every payment editor', () => {
    expect(Object.fromEntries(editorKeys.map((key) => [key, getFinanceEditorSubmitLabel(key)]))).toEqual({
      income: 'Провести',
      expense: 'Провести',
      accruals: 'Начислить',
      regularAccruals: 'Создать месяц',
      supplierGroupSalaryAccruals: 'Начислить зарплату',
      supplierAccruals: 'Начислить',
      meterReadings: 'Внести',
    })
  })

  it('returns saving scopes for every payment editor', () => {
    expect(Object.fromEntries(editorKeys.map((key) => [key, getFinanceEditorSavingScope(key)]))).toEqual({
      income: 'income',
      expense: 'expense',
      accruals: 'accrual',
      regularAccruals: 'regular-accruals',
      supplierGroupSalaryAccruals: 'salary-accruals',
      supplierAccruals: 'supplier-accrual',
      meterReadings: 'meter-reading',
    })
  })
})
