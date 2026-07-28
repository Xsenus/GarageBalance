import { afterEach, describe, expect, it } from 'vitest'
import {
  createDefaultConsolidatedReportFilters,
  createDefaultExpenseReportFilters,
  createDefaultGarageBalanceHistoryFilters,
  createFullFinancialReportFilters,
  createDefaultIncomeReportFilters,
  filterAndRankReportOptions,
  getReportQuickPeriodRange,
  loadConsolidatedReportFilters,
  loadExpenseReportFilters,
  loadIncomeReportFilters,
  reportFilterStorageKeys,
  saveConsolidatedReportFilters,
  saveExpenseReportFilters,
  saveIncomeReportFilters,
} from './reportFilters'

describe('report filter storage helpers', () => {
  afterEach(() => {
    window.sessionStorage.clear()
  })

  it('creates default filters for all report tabs', () => {
    expect(createDefaultConsolidatedReportFilters('2026-06-01')).toEqual({
      monthFrom: '2026-06-01',
      monthTo: '2026-06-01',
      search: '',
    })

    expect(createDefaultIncomeReportFilters('2026-06-01', '2026-06-25')).toEqual({
      dateFrom: '2026-06-01',
      dateTo: '2026-06-25',
      search: '',
      garageIds: [],
      ownerIds: [],
      incomeTypeIds: [],
      rowMode: 'all',
    })

    expect(createDefaultExpenseReportFilters('2026-06-01', '2026-06-25')).toEqual({
      dateFrom: '2026-06-01',
      dateTo: '2026-06-25',
      search: '',
      supplierIds: [],
      expenseTypeIds: [],
      rowMode: 'all',
    })
  })

  it('creates a six-month garage balance history period ending at the selected month', () => {
    expect(createDefaultGarageBalanceHistoryFilters(new Date(2026, 5, 25))).toEqual({
      monthFrom: '2026-01',
      monthTo: '2026-06',
    })

    expect(createDefaultGarageBalanceHistoryFilters(new Date(2026, 1, 10))).toEqual({
      monthFrom: '2025-09',
      monthTo: '2026-02',
    })
  })

  it('ranks garage options by exact number, prefix, other match and natural number order', () => {
    const options = [
      { value: 'garage-121', label: 'Гараж 121', description: 'Петров', rankingValue: '121' },
      { value: 'garage-21-owner', label: 'Гараж 2', description: 'Владелец 21', rankingValue: '2' },
      { value: 'garage-21-a10', label: 'Гараж 21А10', description: 'Сидоров', rankingValue: '21А10' },
      { value: 'garage-21', label: 'Гараж 21', description: 'Иванов', rankingValue: '21' },
      { value: 'garage-21-a2', label: 'Гараж 21А2', description: 'Орлов', rankingValue: '21А2' },
    ]

    expect(filterAndRankReportOptions(options, '21').map((option) => option.value)).toEqual([
      'garage-21',
      'garage-21-a2',
      'garage-21-a10',
      'garage-121',
      'garage-21-owner',
    ])
    expect(filterAndRankReportOptions([
      { value: '105', label: 'Гараж 105', rankingValue: '105' },
      { value: '12', label: 'Гараж 12', rankingValue: '12' },
      { value: '2', label: 'Гараж 2', rankingValue: '2' },
      { value: '21', label: 'Гараж 21', rankingValue: '21' },
      { value: '10', label: 'Гараж 10', rankingValue: '10' },
    ], '').map((option) => option.value)).toEqual(['2', '10', '12', '21', '105'])
  })

  it('keeps the source order for report options without a garage ranking value', () => {
    const options = [
      { value: 'supplier-b', label: 'Бета' },
      { value: 'supplier-a', label: 'Альфа' },
    ]

    expect(filterAndRankReportOptions(options, '')).toEqual(options)
  })

  it('creates a full financial report period from server month boundaries', () => {
    expect(createFullFinancialReportFilters({ monthFrom: '2023-02-01', monthTo: '2026-07-01' })).toEqual({
      monthFrom: '2023-02',
      monthTo: '2026-07',
    })
  })

  it('creates quick report periods for the current month and adjacent years', () => {
    expect(getReportQuickPeriodRange('currentMonth', '2026-07-28')).toEqual({
      monthFrom: '2026-07',
      monthTo: '2026-07',
      dateFrom: '2026-07-01',
      dateTo: '2026-07-31',
    })
    expect(getReportQuickPeriodRange('currentYear', '2026-07-28')).toEqual({
      monthFrom: '2026-01',
      monthTo: '2026-12',
      dateFrom: '2026-01-01',
      dateTo: '2026-12-31',
    })
    expect(getReportQuickPeriodRange('previousYear', '2026-01-01')).toEqual({
      monthFrom: '2025-01',
      monthTo: '2025-12',
      dateFrom: '2025-01-01',
      dateTo: '2025-12-31',
    })
  })

  it('uses the real last day for a quick current-month period', () => {
    expect(getReportQuickPeriodRange('currentMonth', '2024-02-10').dateTo).toBe('2024-02-29')
    expect(getReportQuickPeriodRange('currentMonth', '2025-02-10').dateTo).toBe('2025-02-28')
  })

  it('loads saved report filters and normalizes unsafe values', () => {
    window.sessionStorage.setItem(reportFilterStorageKeys.consolidated, JSON.stringify({
      monthFrom: '2026-01-01',
      monthTo: 'broken',
      search: 'Гараж 7',
    }))
    window.sessionStorage.setItem(reportFilterStorageKeys.income, JSON.stringify({
      dateFrom: '2026-02-01',
      dateTo: 'not-date',
      search: 5,
      garageIds: ['garage-1', '', 2, 'garage-2'],
      ownerIds: ['owner-1'],
      incomeTypeIds: 'income-1',
      rowMode: 'payments',
    }))
    window.sessionStorage.setItem(reportFilterStorageKeys.expense, JSON.stringify({
      dateFrom: false,
      dateTo: '2026-06-20',
      search: 'банк',
      supplierIds: ['supplier-1'],
      expenseTypeIds: ['expense-1', null],
      rowMode: 'unexpected',
    }))

    expect(loadConsolidatedReportFilters('2026-06-01')).toEqual({
      monthFrom: '2026-01-01',
      monthTo: '2026-06-01',
      search: 'Гараж 7',
    })
    expect(loadIncomeReportFilters('2026-06-01', '2026-06-25')).toEqual({
      dateFrom: '2026-02-01',
      dateTo: '2026-06-25',
      search: '',
      garageIds: ['garage-1', 'garage-2'],
      ownerIds: ['owner-1'],
      incomeTypeIds: [],
      rowMode: 'payments',
    })
    expect(loadExpenseReportFilters('2026-06-01', '2026-06-25')).toEqual({
      dateFrom: '2026-06-01',
      dateTo: '2026-06-20',
      search: 'банк',
      supplierIds: ['supplier-1'],
      expenseTypeIds: ['expense-1'],
      rowMode: 'all',
    })
  })

  it('falls back to defaults for missing or malformed saved filters', () => {
    window.sessionStorage.setItem(reportFilterStorageKeys.income, '{')

    expect(loadConsolidatedReportFilters('2026-06-01')).toEqual(createDefaultConsolidatedReportFilters('2026-06-01'))
    expect(loadIncomeReportFilters('2026-06-01', '2026-06-25')).toEqual(createDefaultIncomeReportFilters('2026-06-01', '2026-06-25'))
    expect(loadExpenseReportFilters('2026-06-01', '2026-06-25')).toEqual(createDefaultExpenseReportFilters('2026-06-01', '2026-06-25'))
  })

  it('saves report filters under stable session storage keys', () => {
    saveConsolidatedReportFilters({ monthFrom: '2026-05-01', monthTo: '2026-06-01', search: 'гараж' })
    saveIncomeReportFilters({ dateFrom: '2026-06-01', dateTo: '2026-06-25', search: 'иванов', garageIds: ['g1'], ownerIds: ['o1'], incomeTypeIds: ['i1'], rowMode: 'accruals' })
    saveExpenseReportFilters({ dateFrom: '2026-06-01', dateTo: '2026-06-25', search: 'банк', supplierIds: ['s1'], expenseTypeIds: ['e1'], rowMode: 'payments' })

    expect(loadConsolidatedReportFilters('2026-01-01')).toEqual({ monthFrom: '2026-05-01', monthTo: '2026-06-01', search: 'гараж' })
    expect(loadIncomeReportFilters('2026-01-01', '2026-01-31')).toEqual({ dateFrom: '2026-06-01', dateTo: '2026-06-25', search: 'иванов', garageIds: ['g1'], ownerIds: ['o1'], incomeTypeIds: ['i1'], rowMode: 'accruals' })
    expect(loadExpenseReportFilters('2026-01-01', '2026-01-31')).toEqual({ dateFrom: '2026-06-01', dateTo: '2026-06-25', search: 'банк', supplierIds: ['s1'], expenseTypeIds: ['e1'], rowMode: 'payments' })
  })
})
