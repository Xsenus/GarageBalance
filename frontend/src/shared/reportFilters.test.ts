import { afterEach, describe, expect, it } from 'vitest'
import {
  createDefaultConsolidatedReportFilters,
  createDefaultExpenseReportFilters,
  createDefaultGarageBalanceHistoryFilters,
  createDefaultIncomeReportFilters,
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
