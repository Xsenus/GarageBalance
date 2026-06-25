import { describe, expect, it } from 'vitest'
import { financeSectionOptions, getFinanceContextMenuLabel, getFinanceEditorSavingScope, getFinanceEditorSubmitLabel, getFinanceEditorTitle, getFinanceSectionDescription, getFinanceTableHeaders } from './financeWorkbench'
import type { FinanceContextMenuAction, FinanceEditorKey, FinanceSectionKey } from './financeWorkbench'

const editorKeys: FinanceEditorKey[] = [
  'income',
  'expense',
  'accruals',
  'regularAccruals',
  'supplierGroupSalaryAccruals',
  'supplierAccruals',
  'meterReadings',
]

const contextMenuActions: FinanceContextMenuAction[] = ['add', 'edit', 'delete']
const sectionKeys: FinanceSectionKey[] = ['income', 'expense', 'accruals', 'supplierAccruals', 'meterReadings']

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

  it('returns section descriptions with server counts', () => {
    expect(getFinanceSectionDescription(financeSectionOptions[0], {
      income: 12,
      expense: 3,
      accruals: 25,
      supplierAccruals: 7,
      meterReadings: 40,
    })).toBe('Оплаты владельцев · 12')
  })

  it('returns context menu labels for payment table CRUD actions', () => {
    expect(Object.fromEntries(contextMenuActions.map((action) => [action, getFinanceContextMenuLabel(action)]))).toEqual({
      add: 'Добавить',
      edit: 'Изменить',
      delete: 'Удалить',
    })
  })

  it('returns table headers for every payment section', () => {
    expect(Object.fromEntries(sectionKeys.map((section) => [section, getFinanceTableHeaders(section)]))).toEqual({
      income: ['Дата', 'Месяц', 'Гараж', 'Владелец', 'Вид оплаты', 'Документ', 'Оплачено', 'Долг после', 'Комментарий'],
      expense: ['Дата', 'Месяц', 'Поставщик', 'Вид выплаты', 'Документ', 'Выплачено', 'Обязательство после', 'Комментарий'],
      accruals: ['Месяц', 'Гараж', 'Владелец', 'Вид оплаты', 'Источник', 'Начислено', 'Комментарий'],
      supplierAccruals: ['Месяц', 'Поставщик', 'Вид выплаты', 'Источник', 'Документ', 'Начислено', 'Комментарий'],
      meterReadings: ['Месяц', 'Дата', 'Гараж', 'Счетчик', 'Пред. знач.', 'Нов. знач.', 'Разница', 'Комментарий'],
    })
  })
})
