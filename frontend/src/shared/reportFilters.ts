import { formatMonthInputValue } from './formatters'
import { getDateOnlyOrDefault, getRowModeOrDefault, getStringArrayOrDefault, getStringOrDefault, isRecord, readSessionJson, saveSessionJson } from './sessionStorage'
import type { ConsolidatedReportFilters, ExpenseReportFilters, IncomeReportFilters } from './validation'

export const reportFilterStorageKeys = {
  consolidated: 'garagebalance.reports.consolidatedFilters',
  income: 'garagebalance.reports.incomeFilters',
  expense: 'garagebalance.reports.expenseFilters',
} as const

export function createDefaultConsolidatedReportFilters(month: string): ConsolidatedReportFilters {
  return { monthFrom: month, monthTo: month, search: '' }
}

export function createDefaultIncomeReportFilters(month: string, today: string): IncomeReportFilters {
  return { dateFrom: month, dateTo: today, search: '', garageIds: [], ownerIds: [], incomeTypeIds: [], rowMode: 'all' }
}

export function createDefaultExpenseReportFilters(month: string, today: string): ExpenseReportFilters {
  return { dateFrom: month, dateTo: today, search: '', supplierIds: [], expenseTypeIds: [], rowMode: 'all' }
}

export function createDefaultGarageBalanceHistoryFilters(date = new Date()) {
  const to = new Date(date.getFullYear(), date.getMonth(), 1)
  const from = new Date(date.getFullYear(), date.getMonth() - 5, 1)
  return { monthFrom: formatMonthInputValue(from), monthTo: formatMonthInputValue(to) }
}

export function createFullFinancialReportFilters(period: { monthFrom: string; monthTo: string }) {
  return {
    monthFrom: period.monthFrom.slice(0, 7),
    monthTo: period.monthTo.slice(0, 7),
  }
}

export function loadConsolidatedReportFilters(month: string): ConsolidatedReportFilters {
  const parsed = readSessionJson(reportFilterStorageKeys.consolidated)
  if (!isRecord(parsed)) {
    return createDefaultConsolidatedReportFilters(month)
  }

  return {
    monthFrom: getDateOnlyOrDefault(parsed.monthFrom, month),
    monthTo: getDateOnlyOrDefault(parsed.monthTo, month),
    search: getStringOrDefault(parsed.search, ''),
  }
}

export function loadIncomeReportFilters(month: string, today: string): IncomeReportFilters {
  const parsed = readSessionJson(reportFilterStorageKeys.income)
  if (!isRecord(parsed)) {
    return createDefaultIncomeReportFilters(month, today)
  }

  return {
    dateFrom: getDateOnlyOrDefault(parsed.dateFrom, month),
    dateTo: getDateOnlyOrDefault(parsed.dateTo, today),
    search: getStringOrDefault(parsed.search, ''),
    garageIds: getStringArrayOrDefault(parsed.garageIds),
    ownerIds: getStringArrayOrDefault(parsed.ownerIds),
    incomeTypeIds: getStringArrayOrDefault(parsed.incomeTypeIds),
    rowMode: getRowModeOrDefault(parsed.rowMode),
  }
}

export function loadExpenseReportFilters(month: string, today: string): ExpenseReportFilters {
  const parsed = readSessionJson(reportFilterStorageKeys.expense)
  if (!isRecord(parsed)) {
    return createDefaultExpenseReportFilters(month, today)
  }

  return {
    dateFrom: getDateOnlyOrDefault(parsed.dateFrom, month),
    dateTo: getDateOnlyOrDefault(parsed.dateTo, today),
    search: getStringOrDefault(parsed.search, ''),
    supplierIds: getStringArrayOrDefault(parsed.supplierIds),
    expenseTypeIds: getStringArrayOrDefault(parsed.expenseTypeIds),
    rowMode: getRowModeOrDefault(parsed.rowMode),
  }
}

export function saveConsolidatedReportFilters(filters: ConsolidatedReportFilters) {
  saveSessionJson(reportFilterStorageKeys.consolidated, filters)
}

export function saveIncomeReportFilters(filters: IncomeReportFilters) {
  saveSessionJson(reportFilterStorageKeys.income, filters)
}

export function saveExpenseReportFilters(filters: ExpenseReportFilters) {
  saveSessionJson(reportFilterStorageKeys.expense, filters)
}
