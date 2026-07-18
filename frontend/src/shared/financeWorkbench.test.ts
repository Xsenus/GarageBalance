// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { financeSectionOptions, formatFinanceGarageLabel, formatFinanceIncomeGarageSearchStatus, formatFinanceOperationCount, formatFinanceVisibleListStatus, formatFinanceVisibleRange, getFinanceContextMenuLabel, getFinanceEditorFieldLabel, getFinanceEditorSavingScope, getFinanceEditorSubmitLabel, getFinanceEditorTitle, getFinanceEditorUiLabel, getFinanceEditorValidationTitle, getFinanceFallbackLabel, getFinanceMeterKindLabel, getFinanceOptionalText, getFinancePanelLabel, getFinanceSectionDescription, getFinanceTableHeaders, getFinanceToolbarLabel, getFinanceVisibleListEmptyLabel, getFinanceVisibleListTableHeaders, getFinanceVisibleListTableLabel } from './financeWorkbench'
import type { FinanceContextMenuAction, FinanceEditorFieldLabelKey, FinanceEditorKey, FinanceEditorUiLabelKey, FinanceFallbackLabelKey, FinancePanelLabelKey, FinanceSectionKey, FinanceToolbarLabelKey, FinanceVisibleListStatusKind } from './financeWorkbench'

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
const visibleListKeys: FinanceVisibleListStatusKind[] = ['operations', 'accruals', 'supplierAccruals', 'meterReadings']
const fallbackKeys: FinanceFallbackLabelKey[] = ['missingValue', 'noData', 'noComment', 'meterGapWarning']
const toolbarLabelKeys: FinanceToolbarLabelKey[] = [
  'sectionTabs',
  'periodFilter',
  'periodFrom',
  'periodTo',
  'search',
  'searchPlaceholder',
  'incomeGarageSearch',
  'incomeGarageSearchPlaceholder',
  'incomeGarageSearchSubmit',
  'regularAccruals',
  'supplierGroupSalaryAccruals',
  'tableArea',
  'emptyState',
  'pagination',
  'rows',
  'pageSize',
  'previousPage',
  'nextPage',
  'contextMenu',
]
const editorUiLabelKeys: FinanceEditorUiLabelKey[] = ['createMode', 'editMode', 'close', 'cancel', 'save', 'unsavedHint', 'unsavedConfirm']
const panelLabelKeys: FinancePanelLabelKey[] = [
  'section',
  'title',
  'loading',
  'readOnlyHint',
  'summary',
  'incomeTotal',
  'accrualTotal',
  'expenseTotal',
  'balance',
  'meterReadings',
]
const editorFieldLabelKeys: FinanceEditorFieldLabelKey[] = [
  'incomeGarageSearch',
  'incomeGarage',
  'incomeType',
  'incomeDate',
  'incomeMonth',
  'incomeAmount',
  'incomeDocument',
  'incomeComment',
  'expenseSupplier',
  'expenseType',
  'expenseDate',
  'expenseMonth',
  'expenseAmount',
  'expenseDocument',
  'expenseComment',
  'accrualGarage',
  'accrualIncomeType',
  'accrualMonth',
  'accrualAmount',
  'accrualSource',
  'accrualComment',
  'regularIncomeType',
  'regularTariff',
  'regularMonth',
  'regularComment',
  'supplierAccrualSupplier',
  'supplierAccrualType',
  'supplierAccrualMonth',
  'supplierAccrualAmount',
  'supplierAccrualSource',
  'supplierAccrualDocument',
  'supplierAccrualComment',
  'salaryGroup',
  'salaryMonth',
  'salaryAmount',
  'salaryDocument',
  'salaryComment',
  'meterGarage',
  'meterKind',
  'meterMonth',
  'meterDate',
  'meterCurrentValue',
  'meterComment',
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

  it('returns validation titles for every payment editor', () => {
    expect(Object.fromEntries(editorKeys.map((key) => [key, getFinanceEditorValidationTitle(key)]))).toEqual({
      income: 'Проверьте поступление',
      expense: 'Проверьте выплату',
      accruals: 'Проверьте начисление',
      regularAccruals: 'Проверьте регулярное начисление',
      supplierGroupSalaryAccruals: 'Проверьте начисление зарплаты',
      supplierAccruals: 'Проверьте начисление поставщику',
      meterReadings: 'Проверьте показание',
    })
    expect(getFinanceEditorValidationTitle('regularAccruals', 'batch')).toBe('Проверьте регулярные начисления')
    expect(getFinanceEditorValidationTitle('meterReadings', 'detailed')).toBe('Проверьте показание счетчика')
  })

  it('returns common labels for the payment editor dialog', () => {
    expect(Object.fromEntries(editorUiLabelKeys.map((key) => [key, getFinanceEditorUiLabel(key)]))).toEqual({
      createMode: 'Платежи',
      editMode: 'Изменение',
      close: 'Закрыть форму платежа',
      cancel: 'Отмена',
      save: 'Сохранить',
      unsavedHint: 'Есть несохраненные изменения формы платежа.',
      unsavedConfirm: 'Закрыть форму платежа без сохранения изменений?',
    })
  })

  it('returns visible labels for payment editor fields', () => {
    expect(Object.fromEntries(editorFieldLabelKeys.map((key) => [key, getFinanceEditorFieldLabel(key)]))).toMatchObject({
      incomeGarageSearch: 'Поиск гаража или владельца',
      incomeGarage: 'Гараж',
      incomeMonth: 'Месяц учета',
      incomeDocument: 'Документ',
      expenseSupplier: 'Поставщик',
      accrualSource: 'Источник',
      supplierAccrualDocument: 'Документ',
      meterCurrentValue: 'Текущее показание',
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
      accruals: ['Месяц', 'Учетный год', 'Гараж', 'Владелец', 'Вид оплаты', 'Источник', 'Начислено', 'Комментарий'],
      supplierAccruals: ['Месяц', 'Поставщик', 'Вид выплаты', 'Источник', 'Документ', 'Начислено', 'Комментарий'],
      meterReadings: ['Месяц', 'Дата', 'Гараж', 'Счетчик', 'Пред. знач.', 'Нов. знач.', 'Разница', 'Комментарий'],
    })
  })

  it('returns fallback labels for empty payment table values', () => {
    expect(Object.fromEntries(fallbackKeys.map((key) => [key, getFinanceFallbackLabel(key)]))).toEqual({
      missingValue: 'Не указан',
      noData: 'Нет данных',
      noComment: 'Нет комментария',
      meterGapWarning: 'проверьте месяц',
    })
    expect(getFinanceOptionalText(null)).toBe('Не указан')
    expect(getFinanceOptionalText(undefined, 'noComment')).toBe('Нет комментария')
    expect(getFinanceOptionalText('Документ 7')).toBe('Документ 7')
  })

  it('formats table labels for garages and meter kinds', () => {
    expect(formatFinanceGarageLabel(42)).toBe('Гараж 42')
    expect(formatFinanceGarageLabel(null)).toBe('Гараж ')
    expect(getFinanceMeterKindLabel('water')).toBe('Вода')
    expect(getFinanceMeterKindLabel('electricity')).toBe('Электричество')
  })

  it('returns toolbar labels for payment filters and pagination', () => {
    expect(Object.fromEntries(toolbarLabelKeys.map((key) => [key, getFinanceToolbarLabel(key)]))).toEqual({
      sectionTabs: 'Разделы платежей',
      periodFilter: 'Фильтр периода',
      periodFrom: 'Период с',
      periodTo: 'Период по',
      search: 'Поиск по платежам',
      searchPlaceholder: 'Гараж, владелец, поставщик или документ',
      incomeGarageSearch: 'Поиск гаража для поступления',
      incomeGarageSearchPlaceholder: 'Гараж или владелец',
      incomeGarageSearchSubmit: 'Найти гараж для поступления',
      regularAccruals: 'Регулярные',
      supplierGroupSalaryAccruals: 'Зарплата группы',
      tableArea: 'Рабочая область платежной таблицы',
      emptyState: 'По выбранным условиям записей нет',
      pagination: 'Пагинация платежей',
      rows: 'Строк',
      pageSize: 'Количество строк платежей',
      previousPage: 'Назад',
      nextPage: 'Вперед',
      contextMenu: 'Операции с платежами',
    })
  })

  it('returns panel and summary labels for the payment workbench', () => {
    expect(Object.fromEntries(panelLabelKeys.map((key) => [key, getFinancePanelLabel(key)]))).toEqual({
      section: 'Платежи',
      title: 'Поступления владельцев и выплаты поставщикам',
      loading: 'Загрузка...',
      readOnlyHint: 'Режим просмотра: для записи платежей, начислений и показаний нужно право payments.write.',
      summary: 'Итоги платежей',
      incomeTotal: 'Поступления',
      accrualTotal: 'Начислено',
      expenseTotal: 'Выплаты',
      balance: 'Баланс',
      meterReadings: 'Счетчики',
    })
    expect(formatFinanceOperationCount(7)).toBe('7 операций')
  })

  it('formats payment pagination status text', () => {
    expect(formatFinanceVisibleRange({ from: 1, to: 25 }, 80)).toBe('Показано 1-25 из 80')
    expect(formatFinanceVisibleRange({ from: 0, to: 0 }, 0)).toBe('Показано 0-0 из 0')
  })

  it('formats visible finance list status text', () => {
    expect({
      operations: formatFinanceVisibleListStatus(8, 12, 'operations'),
      accruals: formatFinanceVisibleListStatus(8, 15, 'accruals'),
      supplierAccruals: formatFinanceVisibleListStatus(8, 20, 'supplierAccruals'),
      meterReadings: formatFinanceVisibleListStatus(8, 25, 'meterReadings'),
    }).toEqual({
      operations: 'Показано 8 из 12 операций',
      accruals: 'Показано 8 из 15 начислений',
      supplierAccruals: 'Показано 8 из 20 начислений поставщикам',
      meterReadings: 'Показано 8 из 25 показаний',
    })
  })

  it('returns empty labels for short finance lists', () => {
    expect({
      operations: getFinanceVisibleListEmptyLabel('operations'),
      accruals: getFinanceVisibleListEmptyLabel('accruals'),
      supplierAccruals: getFinanceVisibleListEmptyLabel('supplierAccruals'),
      meterReadings: getFinanceVisibleListEmptyLabel('meterReadings'),
    }).toEqual({
      operations: 'Операций пока нет',
      accruals: 'Начислений пока нет',
      supplierAccruals: 'Начислений поставщикам пока нет',
      meterReadings: 'Показаний пока нет',
    })
  })

  it('returns table metadata for short finance lists', () => {
    expect(Object.fromEntries(visibleListKeys.map((key) => [key, getFinanceVisibleListTableLabel(key)]))).toEqual({
      operations: 'Последние платежи',
      accruals: 'Последние начисления',
      supplierAccruals: 'Последние начисления поставщикам',
      meterReadings: 'Последние показания',
    })
    expect(Object.fromEntries(visibleListKeys.map((key) => [key, getFinanceVisibleListTableHeaders(key)]))).toEqual({
      operations: ['Дата', 'Операция', 'Сумма'],
      accruals: ['Месяц', 'Начисление', 'Сумма'],
      supplierAccruals: ['Месяц', 'Поставщик', 'Сумма'],
      meterReadings: ['Месяц', 'Счетчик', 'Расход'],
    })
  })

  it('formats income garage search status text', () => {
    expect(formatFinanceIncomeGarageSearchStatus(3, true)).toBe('Найдено гаражей: 3')
    expect(formatFinanceIncomeGarageSearchStatus(12, false)).toBe('Показаны все гаражи: 12')
  })
})
