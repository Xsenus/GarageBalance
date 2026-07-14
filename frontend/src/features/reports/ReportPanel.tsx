import { useEffect, useId, useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { FileSpreadsheet, FileText } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { AccountingTypeDto, DictionaryClient, GarageDto, SupplierDto } from '../../services/dictionariesApi'
import type { BankDepositReportDto, CashPaymentReportDto, ConsolidatedReportDto, ExpenseReportDto, FeeReportDto, FundChangeReportDto, GarageDetailReportDto, IncomeReportDto, ReportClient } from '../../services/reportsApi'
import { TableLoadingState } from '../../shared/AsyncState'
import { buildReportFileName, buildSnapshotReportFileName, downloadBlob } from '../../shared/fileExports'
import { FormError } from '../../shared/formFeedback'
import { formatMoney, formatMonth, formatOperationTime, getCurrentMonthInputValue, getLocalDateInputValue, getPreviousMonthInputValue } from '../../shared/formatters'
import { createClientPage } from '../../shared/pagination'
import { TablePagination } from '../../shared/TablePagination'

type ReportWorkbookTab = 'consolidated' | 'garages' | 'payouts' | 'income' | 'cashPayments' | 'bankDeposits' | 'fees' | 'funds'
type ReportMonthlyFilterKey = 'consolidated' | 'garages' | 'payouts'
type ReportDateFilterKey = 'income' | 'cashPayments' | 'bankDeposits' | 'funds'

type ReportMonthRange = {
  monthFrom: string
  monthTo: string
}

type ReportDateRange = {
  dateFrom: string
  dateTo: string
}

type ReportNamedAmountRow = {
  name: string
  amount: number
}

const dictionaryScreenRequestLimit = 100

const reportWorkbookTabs: Array<{ key: ReportWorkbookTab; label: string; meta: string }> = [
  { key: 'consolidated', label: 'Консолидированный', meta: 'месяцы' },
  { key: 'garages', label: 'По гаражам', meta: 'гаражи' },
  { key: 'payouts', label: 'По выплатам', meta: 'поставщики и сотрудники' },
  { key: 'income', label: 'Поступления', meta: 'касса' },
  { key: 'cashPayments', label: 'Оплаты из кассы', meta: 'расход' },
  { key: 'bankDeposits', label: 'Сдача кассы в банк', meta: 'банк' },
  { key: 'fees', label: 'Сборы', meta: 'вариации' },
  { key: 'funds', label: 'Изменение фондов', meta: 'фонды' },
]

function getReportMonthStart(monthValue: string) {
  return `${monthValue}-01`
}

function getReportMonthEnd(monthValue: string) {
  const [yearText, monthText] = monthValue.split('-')
  const endDate = new Date(Number(yearText), Number(monthText), 0)
  return `${endDate.getFullYear()}-${String(endDate.getMonth() + 1).padStart(2, '0')}-${String(endDate.getDate()).padStart(2, '0')}`
}

function aggregateIncomePaymentsByName(report: IncomeReportDto | null): ReportNamedAmountRow[] {
  const totals = new Map<string, number>()
  report?.rows
    .filter((row) => row.incomeAmount !== 0)
    .forEach((row) => {
      totals.set(row.incomeTypeName, (totals.get(row.incomeTypeName) ?? 0) + row.incomeAmount)
    })
  return Array.from(totals.entries())
    .map(([name, amount]) => ({ name, amount }))
    .sort((left, right) => left.name.localeCompare(right.name, 'ru'))
}

function aggregateExpensePaymentsByName(report: ExpenseReportDto | null): ReportNamedAmountRow[] {
  const totals = new Map<string, number>()
  report?.rows
    .filter((row) => row.expenseAmount !== 0)
    .forEach((row) => {
      totals.set(row.expenseTypeName, (totals.get(row.expenseTypeName) ?? 0) + row.expenseAmount)
    })
  return Array.from(totals.entries())
    .map(([name, amount]) => ({ name, amount }))
    .sort((left, right) => left.name.localeCompare(right.name, 'ru'))
}

export function ReportPanel({ auth, dictionaryClient, reportClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient; reportClient: ReportClient }) {
  const today = getLocalDateInputValue()
  const currentMonth = getCurrentMonthInputValue(today)
  const previousMonth = getPreviousMonthInputValue(currentMonth)
  const garageOptionsId = useId()
  const supplierOptionsId = useId()
  const feeOptionsId = useId()
  const [activeReportTab, setActiveReportTab] = useState<ReportWorkbookTab>('consolidated')
  const [monthlyFilters, setMonthlyFilters] = useState<Record<ReportMonthlyFilterKey, ReportMonthRange>>({
    consolidated: { monthFrom: currentMonth, monthTo: currentMonth },
    garages: { monthFrom: currentMonth, monthTo: currentMonth },
    payouts: { monthFrom: currentMonth, monthTo: currentMonth },
  })
  const [dateFilters, setDateFilters] = useState<Record<ReportDateFilterKey, ReportDateRange>>({
    income: { dateFrom: today, dateTo: today },
    cashPayments: { dateFrom: today, dateTo: today },
    bankDeposits: { dateFrom: today, dateTo: today },
    funds: { dateFrom: today, dateTo: today },
  })
  const [garageFilter, setGarageFilter] = useState('')
  const [counterpartyFilter, setCounterpartyFilter] = useState('')
  const [incomeGarageFilter, setIncomeGarageFilter] = useState('')
  const [feeVariationFilter, setFeeVariationFilter] = useState('Сбор на ворота')
  const [appliedGarageFilter, setAppliedGarageFilter] = useState('')
  const [appliedCounterpartyFilter, setAppliedCounterpartyFilter] = useState('')
  const [appliedIncomeGarageFilter, setAppliedIncomeGarageFilter] = useState('')
  const [appliedFeeVariationFilter, setAppliedFeeVariationFilter] = useState('Сбор на ворота')
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [dictionaryError, setDictionaryError] = useState<string | null>(null)
  const [consolidatedReport, setConsolidatedReport] = useState<ConsolidatedReportDto | null>(null)
  const [consolidatedReportLoading, setConsolidatedReportLoading] = useState(true)
  const [consolidatedPageNumber, setConsolidatedPageNumber] = useState(1)
  const [consolidatedPageSize, setConsolidatedPageSize] = useState(25)
  const [consolidatedIncomeBreakdown, setConsolidatedIncomeBreakdown] = useState<IncomeReportDto | null>(null)
  const [consolidatedExpenseBreakdown, setConsolidatedExpenseBreakdown] = useState<ExpenseReportDto | null>(null)
  const [garageReport, setGarageReport] = useState<GarageDetailReportDto | null>(null)
  const [garagePageRequest, setGaragePageRequest] = useState({ offset: 0, limit: 25 })
  const [garageReportLoading, setGarageReportLoading] = useState(false)
  const [garageReportError, setGarageReportError] = useState<string | null>(null)
  const [payoutReport, setPayoutReport] = useState<ExpenseReportDto | null>(null)
  const [payoutPageRequest, setPayoutPageRequest] = useState({ offset: 0, limit: 25 })
  const [payoutReportLoading, setPayoutReportLoading] = useState(false)
  const [payoutReportError, setPayoutReportError] = useState<string | null>(null)
  const [incomeReport, setIncomeReport] = useState<IncomeReportDto | null>(null)
  const [incomePageRequest, setIncomePageRequest] = useState({ offset: 0, limit: 25 })
  const [incomeReportLoading, setIncomeReportLoading] = useState(false)
  const [incomeReportError, setIncomeReportError] = useState<string | null>(null)
  const [cashPaymentReport, setCashPaymentReport] = useState<CashPaymentReportDto | null>(null)
  const [cashPaymentPageRequest, setCashPaymentPageRequest] = useState({ offset: 0, limit: 25 })
  const [cashPaymentReportLoading, setCashPaymentReportLoading] = useState(false)
  const [cashPaymentReportError, setCashPaymentReportError] = useState<string | null>(null)
  const [bankDepositReport, setBankDepositReport] = useState<BankDepositReportDto | null>(null)
  const [bankDepositPageRequest, setBankDepositPageRequest] = useState({ offset: 0, limit: 25 })
  const [bankDepositReportLoading, setBankDepositReportLoading] = useState(false)
  const [bankDepositReportError, setBankDepositReportError] = useState<string | null>(null)
  const [feeReport, setFeeReport] = useState<FeeReportDto | null>(null)
  const [feeReportLoading, setFeeReportLoading] = useState(false)
  const [feeSummaryPageNumber, setFeeSummaryPageNumber] = useState(1)
  const [feeSummaryPageSize, setFeeSummaryPageSize] = useState(25)
  const [feeDetailPageNumber, setFeeDetailPageNumber] = useState(1)
  const [feeDetailPageSize, setFeeDetailPageSize] = useState(25)
  const [feeDebtorsVisible, setFeeDebtorsVisible] = useState(false)
  const [feeDetailMode, setFeeDetailMode] = useState<'debtors' | 'all'>('debtors')
  const [garageAccrualsGrouped, setGarageAccrualsGrouped] = useState(false)
  const [reportDataError, setReportDataError] = useState<string | null>(null)
  const [reportExporting, setReportExporting] = useState<string | null>(null)
  const [reportExportMessage, setReportExportMessage] = useState<string | null>(null)
  const [fundChangeReport, setFundChangeReport] = useState<FundChangeReportDto | null>(null)
  const [fundChangePageRequest, setFundChangePageRequest] = useState({ offset: 0, limit: 25 })
  const [fundChangeReportLoading, setFundChangeReportLoading] = useState(false)
  const [fundChangeReportError, setFundChangeReportError] = useState<string | null>(null)

  useEffect(() => {
    const handle = window.setTimeout(() => {
      setAppliedGarageFilter(garageFilter)
      setGaragePageRequest((current) => current.offset === 0 ? current : { ...current, offset: 0 })
    }, 350)
    return () => window.clearTimeout(handle)
  }, [garageFilter])

  useEffect(() => {
    const handle = window.setTimeout(() => {
      setAppliedCounterpartyFilter(counterpartyFilter)
      setPayoutPageRequest((current) => current.offset === 0 ? current : { ...current, offset: 0 })
    }, 350)
    return () => window.clearTimeout(handle)
  }, [counterpartyFilter])

  useEffect(() => {
    const handle = window.setTimeout(() => {
      setAppliedIncomeGarageFilter(incomeGarageFilter)
      setIncomePageRequest((current) => current.offset === 0 ? current : { ...current, offset: 0 })
    }, 350)
    return () => window.clearTimeout(handle)
  }, [incomeGarageFilter])

  useEffect(() => {
    const handle = window.setTimeout(() => {
      setAppliedFeeVariationFilter(feeVariationFilter)
      setFeeSummaryPageNumber(1)
      setFeeDetailPageNumber(1)
    }, 350)
    return () => window.clearTimeout(handle)
  }, [feeVariationFilter])

  useEffect(() => {
    let ignore = false

    async function loadReportDictionaries() {
      setDictionaryError(null)
      try {
        const [loadedGarages, loadedSuppliers, loadedIncomeTypes, loadedExpenseTypes] = await Promise.all([
          dictionaryClient.getGarages(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getIncomeTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getExpenseTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit),
        ])

        if (ignore) {
          return
        }

        setGarages(loadedGarages.filter((garage) => !garage.isArchived))
        setSuppliers(loadedSuppliers.filter((supplier) => !supplier.isArchived))
        setIncomeTypes(loadedIncomeTypes.filter((item) => !item.isArchived))
        setExpenseTypes(loadedExpenseTypes.filter((item) => !item.isArchived))
      } catch (caught) {
        if (!ignore) {
          setDictionaryError(caught instanceof Error ? caught.message : 'Не удалось загрузить подсказки для фильтров отчетов.')
        }
      }
    }

    void loadReportDictionaries()

    return () => {
      ignore = true
    }
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => {
    let ignore = false

    async function loadConsolidatedReport() {
      setConsolidatedReportLoading(true)
      setReportDataError(null)
      try {
        const consolidatedFilter = monthlyFilters.consolidated
        const monthFrom = getReportMonthStart(consolidatedFilter.monthFrom)
        const monthTo = getReportMonthStart(consolidatedFilter.monthTo)
        const loadedConsolidated = await reportClient.getConsolidatedReport(auth.accessToken, {
          monthFrom,
          monthTo,
          limit: 100,
        })

        if (ignore) {
          return
        }

        setConsolidatedReport(loadedConsolidated)
        setConsolidatedReportLoading(false)

        const [loadedConsolidatedIncome, loadedConsolidatedExpenses] = await Promise.all([
          reportClient.getIncomeReport(auth.accessToken, {
            dateFrom: monthFrom,
            dateTo: getReportMonthEnd(consolidatedFilter.monthTo),
            rowMode: 'payments',
            limit: 500,
          }),
          reportClient.getExpenseReport(auth.accessToken, {
            dateFrom: monthFrom,
            dateTo: getReportMonthEnd(consolidatedFilter.monthTo),
            rowMode: 'payments',
            limit: 500,
          }),
        ])

        if (ignore) {
          return
        }

        setConsolidatedIncomeBreakdown(loadedConsolidatedIncome)
        setConsolidatedExpenseBreakdown(loadedConsolidatedExpenses)
      } catch (caught) {
        if (!ignore) {
          setReportDataError(caught instanceof Error ? caught.message : 'Не удалось загрузить расчетные данные отчетов.')
          setConsolidatedReportLoading(false)
        }
      }
    }

    void loadConsolidatedReport()

    return () => {
      ignore = true
    }
  }, [auth.accessToken, monthlyFilters.consolidated, reportClient])

  useEffect(() => {
    if (activeReportTab !== 'fees') {
      return
    }

    let ignore = false
    async function loadFeeReport() {
      setFeeReportLoading(true)
      setReportDataError(null)
      try {
        const report = await reportClient.getFeeReport(auth.accessToken, {
          variation: appliedFeeVariationFilter.trim() || undefined,
          limit: 100,
        })
        if (!ignore) {
          setFeeReport(report)
        }
      } catch (caught) {
        if (!ignore) {
          setReportDataError(caught instanceof Error ? caught.message : 'Не удалось загрузить отчет по сборам.')
        }
      } finally {
        if (!ignore) {
          setFeeReportLoading(false)
        }
      }
    }

    void loadFeeReport()
    return () => {
      ignore = true
    }
  }, [activeReportTab, appliedFeeVariationFilter, auth.accessToken, reportClient])

  useEffect(() => {
    if (activeReportTab !== 'garages') {
      return
    }

    let ignore = false

    async function loadGarageReport() {
      setGarageReportLoading(true)
      setGarageReportError(null)
      try {
        const filter = monthlyFilters.garages
        const report = await reportClient.getGarageReport(auth.accessToken, {
          monthFrom: getReportMonthStart(filter.monthFrom),
          monthTo: getReportMonthStart(filter.monthTo),
          search: appliedGarageFilter.trim() || undefined,
          groupAccruals: garageAccrualsGrouped,
          offset: garagePageRequest.offset,
          limit: garagePageRequest.limit,
        })
        if (!ignore) {
          setGarageReport(report)
        }
      } catch (caught) {
        if (!ignore) {
          setGarageReportError(caught instanceof Error ? caught.message : 'Не удалось загрузить отчет по гаражам.')
        }
      } finally {
        if (!ignore) {
          setGarageReportLoading(false)
        }
      }
    }

    void loadGarageReport()

    return () => {
      ignore = true
    }
  }, [activeReportTab, appliedGarageFilter, auth.accessToken, garageAccrualsGrouped, garagePageRequest.limit, garagePageRequest.offset, monthlyFilters.garages, reportClient])

  useEffect(() => {
    if (activeReportTab !== 'payouts') {
      return
    }

    let ignore = false

    async function loadPayoutReport() {
      setPayoutReportLoading(true)
      setPayoutReportError(null)
      try {
        const filter = monthlyFilters.payouts
        const report = await reportClient.getExpenseReport(auth.accessToken, {
          dateFrom: getReportMonthStart(filter.monthFrom),
          dateTo: getReportMonthEnd(filter.monthTo),
          search: appliedCounterpartyFilter.trim() || undefined,
          offset: payoutPageRequest.offset,
          limit: payoutPageRequest.limit,
        })
        if (!ignore) {
          setPayoutReport(report)
        }
      } catch (caught) {
        if (!ignore) {
          setPayoutReportError(caught instanceof Error ? caught.message : 'Не удалось загрузить отчет по выплатам.')
        }
      } finally {
        if (!ignore) {
          setPayoutReportLoading(false)
        }
      }
    }

    void loadPayoutReport()

    return () => {
      ignore = true
    }
  }, [activeReportTab, appliedCounterpartyFilter, auth.accessToken, monthlyFilters.payouts, payoutPageRequest.limit, payoutPageRequest.offset, reportClient])

  useEffect(() => {
    if (activeReportTab !== 'income') {
      return
    }

    let ignore = false

    async function loadIncomeReport() {
      setIncomeReportLoading(true)
      setIncomeReportError(null)
      try {
        const filter = dateFilters.income
        const report = await reportClient.getIncomeReport(auth.accessToken, {
          dateFrom: filter.dateFrom,
          dateTo: filter.dateTo,
          search: appliedIncomeGarageFilter.trim() || undefined,
          rowMode: 'payments',
          offset: incomePageRequest.offset,
          limit: incomePageRequest.limit,
        })
        if (!ignore) {
          setIncomeReport(report)
        }
      } catch (caught) {
        if (!ignore) {
          setIncomeReportError(caught instanceof Error ? caught.message : 'Не удалось загрузить отчет по поступлениям.')
        }
      } finally {
        if (!ignore) {
          setIncomeReportLoading(false)
        }
      }
    }

    void loadIncomeReport()

    return () => {
      ignore = true
    }
  }, [activeReportTab, appliedIncomeGarageFilter, auth.accessToken, dateFilters.income, incomePageRequest.limit, incomePageRequest.offset, reportClient])

  useEffect(() => {
    if (activeReportTab !== 'cashPayments') {
      return
    }

    let ignore = false

    async function loadCashPayments() {
      setCashPaymentReportLoading(true)
      setCashPaymentReportError(null)
      try {
        const filter = dateFilters.cashPayments
        const report = await reportClient.getCashPaymentReport(auth.accessToken, {
          dateFrom: filter.dateFrom,
          dateTo: filter.dateTo,
          offset: cashPaymentPageRequest.offset,
          limit: cashPaymentPageRequest.limit,
        })
        if (!ignore) {
          setCashPaymentReport(report)
        }
      } catch (caught) {
        if (!ignore) {
          setCashPaymentReportError(caught instanceof Error ? caught.message : 'Не удалось загрузить отчет по оплатам из кассы.')
        }
      } finally {
        if (!ignore) {
          setCashPaymentReportLoading(false)
        }
      }
    }

    void loadCashPayments()

    return () => {
      ignore = true
    }
  }, [activeReportTab, auth.accessToken, cashPaymentPageRequest.limit, cashPaymentPageRequest.offset, dateFilters.cashPayments, reportClient])

  useEffect(() => {
    if (activeReportTab !== 'bankDeposits') {
      return
    }

    let ignore = false

    async function loadBankDeposits() {
      setBankDepositReportLoading(true)
      setBankDepositReportError(null)
      try {
        const filter = dateFilters.bankDeposits
        const report = await reportClient.getBankDepositReport(auth.accessToken, {
          dateFrom: filter.dateFrom,
          dateTo: filter.dateTo,
          offset: bankDepositPageRequest.offset,
          limit: bankDepositPageRequest.limit,
        })
        if (!ignore) {
          setBankDepositReport(report)
        }
      } catch (caught) {
        if (!ignore) {
          setBankDepositReportError(caught instanceof Error ? caught.message : 'Не удалось загрузить отчет по сдаче кассы в банк.')
        }
      } finally {
        if (!ignore) {
          setBankDepositReportLoading(false)
        }
      }
    }

    void loadBankDeposits()

    return () => {
      ignore = true
    }
  }, [activeReportTab, auth.accessToken, bankDepositPageRequest.limit, bankDepositPageRequest.offset, dateFilters.bankDeposits, reportClient])

  useEffect(() => {
    if (activeReportTab !== 'funds') {
      return
    }

    let ignore = false

    async function loadFundChanges() {
      setFundChangeReportLoading(true)
      setFundChangeReportError(null)
      try {
        const filter = dateFilters.funds
        const report = await reportClient.getFundChangeReport(auth.accessToken, {
          dateFrom: filter.dateFrom,
          dateTo: filter.dateTo,
          offset: fundChangePageRequest.offset,
          limit: fundChangePageRequest.limit,
        })
        if (!ignore) {
          setFundChangeReport(report)
        }
      } catch (caught) {
        if (!ignore) {
          setFundChangeReportError(caught instanceof Error ? caught.message : 'Не удалось загрузить отчет по изменению фондов.')
        }
      } finally {
        if (!ignore) {
          setFundChangeReportLoading(false)
        }
      }
    }

    void loadFundChanges()

    return () => {
      ignore = true
    }
  }, [activeReportTab, auth.accessToken, dateFilters.funds, fundChangePageRequest.limit, fundChangePageRequest.offset, reportClient])

  const selectedTab = reportWorkbookTabs.find((tab) => tab.key === activeReportTab) ?? reportWorkbookTabs[0]
  const garageFilterLabel = garageFilter.trim() || 'Все гаражи'
  const incomeGarageFilterLabel = incomeGarageFilter.trim() || 'Все гаражи'
  const counterpartyFilterLabel = counterpartyFilter.trim() || 'Все поставщики и сотрудники'
  const feeVariationLabel = feeVariationFilter.trim() || 'Все сборы'
  const feeOptions = Array.from(new Set(['Сбор на ворота', 'Вступительный взнос', 'Целевой взнос', ...incomeTypes.map((item) => item.name)]))
  const counterpartyOptions = Array.from(new Set([...suppliers.map((supplier) => supplier.name), ...expenseTypes.map((item) => item.name), 'Электрик', 'Председатель', 'Бухгалтерия']))

  function updateMonthlyFilter(key: ReportMonthlyFilterKey, field: keyof ReportMonthRange, value: string) {
    if (key === 'garages') {
      setGaragePageRequest((current) => ({ ...current, offset: 0 }))
    } else if (key === 'payouts') {
      setPayoutPageRequest((current) => ({ ...current, offset: 0 }))
    }
    setMonthlyFilters((current) => ({
      ...current,
      [key]: {
        ...current[key],
        [field]: value,
      },
    }))
  }

  function updateDateFilter(key: ReportDateFilterKey, field: keyof ReportDateRange, value: string) {
    if (key === 'income') {
      setIncomePageRequest((current) => ({ ...current, offset: 0 }))
    } else if (key === 'cashPayments') {
      setCashPaymentPageRequest((current) => ({ ...current, offset: 0 }))
    } else if (key === 'bankDeposits') {
      setBankDepositPageRequest((current) => ({ ...current, offset: 0 }))
    } else if (key === 'funds') {
      setFundChangePageRequest((current) => ({ ...current, offset: 0 }))
    }
    setDateFilters((current) => ({
      ...current,
      [key]: {
        ...current[key],
        [field]: value,
      },
    }))
  }

  function applyPreviousMonth(key: ReportMonthlyFilterKey) {
    if (key === 'garages') {
      setGaragePageRequest((current) => ({ ...current, offset: 0 }))
    } else if (key === 'payouts') {
      setPayoutPageRequest((current) => ({ ...current, offset: 0 }))
    }
    setMonthlyFilters((current) => ({
      ...current,
      [key]: { monthFrom: previousMonth, monthTo: previousMonth },
    }))
  }

  function applyToday(key: ReportDateFilterKey) {
    if (key === 'income') {
      setIncomePageRequest((current) => ({ ...current, offset: 0 }))
    } else if (key === 'cashPayments') {
      setCashPaymentPageRequest((current) => ({ ...current, offset: 0 }))
    } else if (key === 'bankDeposits') {
      setBankDepositPageRequest((current) => ({ ...current, offset: 0 }))
    } else if (key === 'funds') {
      setFundChangePageRequest((current) => ({ ...current, offset: 0 }))
    }
    setDateFilters((current) => ({
      ...current,
      [key]: { dateFrom: today, dateTo: today },
    }))
  }

  async function downloadCashOrBankReport(type: 'cashPayments' | 'bankDeposits', extension: 'xlsx' | 'pdf') {
    const filter = dateFilters[type]
    const exportKey = `${type}-${extension}`
    setReportExporting(exportKey)
    setReportExportMessage(null)
    setReportDataError(null)
    try {
      const blob = type === 'cashPayments'
        ? extension === 'xlsx'
          ? await reportClient.exportCashPaymentReportXlsx(auth.accessToken, filter)
          : await reportClient.exportCashPaymentReportPdf(auth.accessToken, filter)
        : extension === 'xlsx'
          ? await reportClient.exportBankDepositReportXlsx(auth.accessToken, filter)
          : await reportClient.exportBankDepositReportPdf(auth.accessToken, filter)
      const reportType = type === 'cashPayments' ? 'cash-payments' : 'bank-deposits'
      downloadBlob(blob, buildReportFileName(reportType, filter.dateFrom, filter.dateTo, extension))
      setReportExportMessage(extension === 'xlsx' ? 'Отчет XLSX готов.' : 'Отчет PDF готов.')
    } catch (caught) {
      setReportDataError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчет.')
    } finally {
      setReportExporting(null)
    }
  }

  async function downloadFeeReport(extension: 'xlsx' | 'pdf') {
    const exportKey = `fees-${extension}`
    const variation = feeVariationFilter.trim() || undefined
    setReportExporting(exportKey)
    setReportExportMessage(null)
    setReportDataError(null)
    try {
      const blob = extension === 'xlsx'
        ? await reportClient.exportFeeReportXlsx(auth.accessToken, { variation })
        : await reportClient.exportFeeReportPdf(auth.accessToken, { variation })
      downloadBlob(blob, buildSnapshotReportFileName('fees', extension))
      setReportExportMessage(extension === 'xlsx' ? 'Отчет XLSX готов.' : 'Отчет PDF готов.')
    } catch (caught) {
      setReportDataError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчет.')
    } finally {
      setReportExporting(null)
    }
  }

  async function downloadFundChangeReport(extension: 'xlsx' | 'pdf') {
    const filter = dateFilters.funds
    const exportKey = `funds-${extension}`
    setReportExporting(exportKey)
    setReportExportMessage(null)
    setReportDataError(null)
    try {
      const blob = extension === 'xlsx'
        ? await reportClient.exportFundChangeReportXlsx(auth.accessToken, filter)
        : await reportClient.exportFundChangeReportPdf(auth.accessToken, filter)
      downloadBlob(blob, buildReportFileName('fund-changes', filter.dateFrom, filter.dateTo, extension))
      setReportExportMessage(extension === 'xlsx' ? 'Отчет XLSX готов.' : 'Отчет PDF готов.')
    } catch (caught) {
      setReportDataError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчет.')
    } finally {
      setReportExporting(null)
    }
  }

  function renderReportExportButton(extension: 'xlsx' | 'pdf', exportKey: string, onClick: () => void) {
    const label = extension === 'xlsx' ? 'Скачать XLSX' : 'Скачать PDF'
    const loadingLabel = extension === 'xlsx' ? 'Готовим XLSX...' : 'Готовим PDF...'
    const Icon = extension === 'xlsx' ? FileSpreadsheet : FileText

    return (
      <button className="secondary-button report-export-button" type="button" aria-label={label} title={label} data-tooltip={label} disabled={reportExporting !== null} onClick={onClick}>
        <Icon size={16} aria-hidden="true" />
        <span>{reportExporting === exportKey ? loadingLabel : label}</span>
      </button>
    )
  }

  function renderMonthlyFilter(key: ReportMonthlyFilterKey, labels: { from: string; to: string; extra?: ReactNode }) {
    const filter = monthlyFilters[key]
    return (
      <div className="report-workbook-filter" aria-label={`Фильтры отчета ${labels.from}`}>
        <label>
          <span>{labels.from}</span>
          <input aria-label={labels.from} type="month" value={filter.monthFrom} onChange={(event) => updateMonthlyFilter(key, 'monthFrom', event.target.value)} />
        </label>
        <label>
          <span>{labels.to}</span>
          <input aria-label={labels.to} type="month" value={filter.monthTo} onChange={(event) => updateMonthlyFilter(key, 'monthTo', event.target.value)} />
        </label>
        <button className="link-button report-period-button" type="button" onClick={() => applyPreviousMonth(key)}>Предыдущий</button>
        {labels.extra}
      </div>
    )
  }

  function renderDateFilter(key: ReportDateFilterKey, labels: { from: string; to: string; extra?: ReactNode }) {
    const filter = dateFilters[key]
    return (
      <div className="report-workbook-filter" aria-label={`Фильтры отчета ${labels.from}`}>
        <label>
          <span>{labels.from}</span>
          <input aria-label={labels.from} type="date" value={filter.dateFrom} onChange={(event) => updateDateFilter(key, 'dateFrom', event.target.value)} />
        </label>
        <label>
          <span>{labels.to}</span>
          <input aria-label={labels.to} type="date" value={filter.dateTo} onChange={(event) => updateDateFilter(key, 'dateTo', event.target.value)} />
        </label>
        <button className="link-button report-period-button" type="button" onClick={() => applyToday(key)}>Сегодня</button>
        {labels.extra}
      </div>
    )
  }

  function renderReportTable(ariaLabel: string, columns: string[], rows: Array<Array<ReactNode>>, footer?: Array<ReactNode>) {
    return (
      <div className="report-workbook-table" role="table" aria-label={ariaLabel}>
        <div className="report-workbook-row report-workbook-row--header" role="row" style={{ '--report-columns': columns.length } as CSSProperties}>
          {columns.map((column, columnIndex) => <span role="columnheader" key={`${column}-${columnIndex}`}>{column}</span>)}
        </div>
        {rows.map((row, rowIndex) => (
          <div className="report-workbook-row" role="row" style={{ '--report-columns': columns.length } as CSSProperties} key={`${ariaLabel}-${rowIndex}`}>
            {row.map((cell, cellIndex) => <span role="cell" key={`${ariaLabel}-${rowIndex}-${cellIndex}`}>{cell}</span>)}
          </div>
        ))}
        {footer ? (
          <div className="report-workbook-row report-workbook-row--footer" role="row" style={{ '--report-columns': columns.length } as CSSProperties}>
            {footer.map((cell, cellIndex) => <span role="cell" key={`${ariaLabel}-footer-${cellIndex}`}>{cell}</span>)}
          </div>
        ) : null}
      </div>
    )
  }

  function renderActiveReport() {
    if (activeReportTab === 'consolidated') {
      const incomeBreakdown = aggregateIncomePaymentsByName(consolidatedIncomeBreakdown)
      const expenseBreakdown = aggregateExpensePaymentsByName(consolidatedExpenseBreakdown)
      const rowCount = Math.max(incomeBreakdown.length, expenseBreakdown.length, 1)
      const periodLabel = monthlyFilters.consolidated.monthFrom === monthlyFilters.consolidated.monthTo
        ? formatMonth(getReportMonthStart(monthlyFilters.consolidated.monthFrom))
        : `${formatMonth(getReportMonthStart(monthlyFilters.consolidated.monthFrom))} - ${formatMonth(getReportMonthStart(monthlyFilters.consolidated.monthTo))}`
      const totalDifference = consolidatedReport ? consolidatedReport.incomeTotal - consolidatedReport.expenseTotal : 0
      const endingBalance = consolidatedReport?.balance ?? 0
      const openingBalance = endingBalance - totalDifference
      const reportRows = Array.from({ length: rowCount }, (_, index) => {
        const incomeRow = incomeBreakdown[index]
        const expenseRow = expenseBreakdown[index]
        return [
          index === 0 ? periodLabel : '',
          incomeRow?.name ?? '',
          incomeRow ? formatMoney(incomeRow.amount) : '',
          expenseRow?.name ?? '',
          expenseRow ? formatMoney(expenseRow.amount) : '',
          index === 0 ? formatMoney(totalDifference) : '',
          index === 0 ? formatMoney(openingBalance) : '',
          index === 0 ? formatMoney(endingBalance) : '',
        ]
      })
      const consolidatedPage = createClientPage(reportRows, consolidatedPageNumber, consolidatedPageSize)
      return (
        <ReportWorkbookSheet title="Консолидированный отчёт">
          {renderMonthlyFilter('consolidated', { from: 'Месяц с', to: 'Месяц по' })}
          {consolidatedReportLoading ? <TableLoadingState label="Загружаем сводный отчёт" /> : null}
          {renderReportTable(
            'Консолидированный отчет',
            ['Месяц', 'Наименование', 'Поступления', 'Наименование', 'Выплаты', 'Разница', 'На начало месяца', 'На конец месяца'],
            consolidatedPage.items,
            consolidatedReport ? ['ИТОГО', '', formatMoney(consolidatedReport.incomeTotal), '', formatMoney(consolidatedReport.expenseTotal), formatMoney(consolidatedReport.balance), formatMoney(consolidatedReport.debt), formatMoney(consolidatedReport.balance)] : undefined,
          )}
          <TablePagination
            ariaLabel="Пагинация консолидированного отчета"
            totalCount={consolidatedPage.totalCount}
            offset={consolidatedPage.offset}
            limit={consolidatedPage.limit}
            visibleCount={consolidatedPage.items.length}
            pageSizeLabel="Количество строк консолидированного отчета"
            onPageChange={setConsolidatedPageNumber}
            onPageSizeChange={(limit) => {
              setConsolidatedPageNumber(1)
              setConsolidatedPageSize(limit)
            }}
          />
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'garages') {
      const garageReportColumns = garageAccrualsGrouped
        ? ['Месяц', 'Гараж', 'Начисления', 'Поступления', 'Разница']
        : ['Месяц', 'Гараж', 'Начисления', 'Услуга', 'Поступления', 'Разница']
      const reportRows = garageReport?.rows.map((row) => garageAccrualsGrouped
        ? [
          formatMonth(row.accountingMonth),
          row.garageNumber,
          formatMoney(row.accrualAmount),
          formatMoney(row.incomeAmount),
          formatMoney(row.difference),
        ]
        : [
          formatMonth(row.accountingMonth),
          row.garageNumber,
          formatMoney(row.accrualAmount),
          row.incomeTypeName,
          formatMoney(row.incomeAmount),
          formatMoney(row.difference),
        ]) ?? []
      const emptyGarageRow = garageAccrualsGrouped
        ? ['', garageFilterLabel, '', 'Данных за период нет', '']
        : ['', garageFilterLabel, '', 'Данных за период нет', '', '']
      const garageReportFooter = garageReport
        ? garageAccrualsGrouped
          ? ['ИТОГО', '', formatMoney(garageReport.accrualTotal), formatMoney(garageReport.incomeTotal), formatMoney(garageReport.difference)]
          : ['ИТОГО', '', formatMoney(garageReport.accrualTotal), '', formatMoney(garageReport.incomeTotal), formatMoney(garageReport.difference)]
        : undefined
      const garagePage = {
        items: garageReport?.rows ?? [],
        totalCount: garageReport?.rowCount ?? 0,
        offset: garageReport?.offset ?? garagePageRequest.offset,
        limit: garageReport?.limit ?? garagePageRequest.limit,
      }
      return (
        <ReportWorkbookSheet title="Отчёт по гаражам">
          {renderMonthlyFilter('garages', {
            from: 'Месяц с',
            to: 'Месяц по',
            extra: (
              <label className="report-workbook-filter-wide">
                <span>Гаражи</span>
                <input
                  aria-label="Гаражи"
                  list={garageOptionsId}
                  value={garageFilter}
                  onChange={(event) => setGarageFilter(event.target.value)}
                  placeholder="Гараж или номер"
                />
              </label>
            ),
          })}
          {garageReportLoading ? <TableLoadingState label="Загружаем отчет по гаражам..." /> : null}
          {garageReportError ? <FormError>{garageReportError}</FormError> : null}
          <div className="report-workbook-summary-row">
            <strong>ИТОГО начислений</strong>
            <strong>ИТОГО поступлений</strong>
            <strong>Разница</strong>
          </div>
          <div className="report-workbook-toolbar" role="group" aria-label="Группировка отчета по гаражам">
            <button
              className="secondary-button"
              type="button"
              aria-pressed={garageAccrualsGrouped}
              disabled={garageReportLoading}
              onClick={() => {
                setGaragePageRequest((current) => ({ ...current, offset: 0 }))
                setGarageAccrualsGrouped((current) => !current)
              }}
            >
              {garageAccrualsGrouped ? 'Разгруппировать начисления' : 'Сгруппировать начисления'}
            </button>
          </div>
          {renderReportTable(
            'Отчет по гаражам',
            garageReportColumns,
            garageReportLoading ? [] : reportRows.length > 0 ? reportRows : [emptyGarageRow],
            garageReportLoading ? undefined : garageReportFooter,
          )}
          <TablePagination ariaLabel="Пагинация отчета по гаражам" totalCount={garagePage.totalCount} offset={garagePage.offset} limit={garagePage.limit} visibleCount={garagePage.items.length} disabled={garageReportLoading} pageSizeLabel="Количество строк отчета по гаражам" onPageChange={(page) => setGaragePageRequest((current) => ({ ...current, offset: (page - 1) * current.limit }))} onPageSizeChange={(limit) => setGaragePageRequest({ offset: 0, limit })} />
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'payouts') {
      const reportRows = payoutReport?.rows.map((row) => [
        formatMonth(row.accountingMonth),
        row.expenseTypeName,
        row.supplierName,
        formatMoney(row.accrualAmount),
        formatMoney(row.expenseAmount),
        formatMoney(row.difference),
      ]) ?? []
      const payoutPage = {
        items: payoutReport?.rows ?? [],
        totalCount: payoutReport?.rowCount ?? 0,
        offset: payoutReport?.offset ?? payoutPageRequest.offset,
        limit: payoutReport?.limit ?? payoutPageRequest.limit,
      }
      return (
        <ReportWorkbookSheet title="Отчёт по выплатам">
          {renderMonthlyFilter('payouts', {
            from: 'Месяц с',
            to: 'Месяц по',
            extra: (
              <label className="report-workbook-filter-wide">
                <span>Поставщики/сотрудники</span>
                <input
                  aria-label="Поставщики или сотрудники"
                  list={supplierOptionsId}
                  value={counterpartyFilter}
                  onChange={(event) => setCounterpartyFilter(event.target.value)}
                  placeholder="Поставщик или сотрудник"
                />
              </label>
            ),
          })}
          {payoutReportLoading ? <TableLoadingState label="Загружаем выплаты..." /> : null}
          {payoutReportError ? <FormError>{payoutReportError}</FormError> : null}
          <div className="report-workbook-summary-row">
            <strong>ИТОГО начислений</strong>
            <strong>ИТОГО выплат</strong>
            <strong>Разница</strong>
          </div>
          {renderReportTable(
            'Отчет по выплатам',
            ['Месяц', 'Услуга', 'Поставщик/сотрудник', 'Начисления', 'Выплаты', 'Разница'],
            payoutReportLoading ? [] : reportRows.length > 0 ? reportRows : [['', '', counterpartyFilterLabel, 'Данных за период нет', '', '']],
            !payoutReportLoading && payoutReport ? ['ИТОГО', '', '', formatMoney(payoutReport.accrualTotal), formatMoney(payoutReport.expenseTotal), formatMoney(payoutReport.difference)] : undefined,
          )}
          <TablePagination ariaLabel="Пагинация отчета по выплатам" totalCount={payoutPage.totalCount} offset={payoutPage.offset} limit={payoutPage.limit} visibleCount={payoutPage.items.length} disabled={payoutReportLoading} pageSizeLabel="Количество строк отчета по выплатам" onPageChange={(page) => setPayoutPageRequest((current) => ({ ...current, offset: (page - 1) * current.limit }))} onPageSizeChange={(limit) => setPayoutPageRequest({ offset: 0, limit })} />
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'income') {
      const incomeRows = incomeReport?.rows.filter((row) => row.rowType === 'payments').map((row) => [
        row.garageNumber,
        row.date,
        formatOperationTime(row.createdAtUtc),
        formatMoney(row.incomeAmount),
        row.incomeTypeName,
        row.debtAfterPayment === null || row.debtAfterPayment === undefined ? '' : formatMoney(row.debtAfterPayment),
      ]) ?? []
      const incomePage = {
        items: incomeReport?.rows ?? [],
        totalCount: incomeReport?.rowCount ?? 0,
        offset: incomeReport?.offset ?? incomePageRequest.offset,
        limit: incomeReport?.limit ?? incomePageRequest.limit,
      }
      return (
        <ReportWorkbookSheet title="Отчет по поступлениям">
          {renderDateFilter('income', {
            from: 'С',
            to: 'По',
            extra: (
              <label className="report-workbook-filter-wide">
                <span>Гаражи</span>
                <input
                  aria-label="Гаражи по поступлениям"
                  list={garageOptionsId}
                  value={incomeGarageFilter}
                  onChange={(event) => setIncomeGarageFilter(event.target.value)}
                  placeholder="Гараж или номер"
                />
              </label>
            ),
          })}
          {incomeReportLoading ? <TableLoadingState label="Загружаем поступления..." /> : null}
          {incomeReportError ? <FormError>{incomeReportError}</FormError> : null}
          <div className="report-workbook-summary-row report-workbook-summary-row--single"><strong>ИТОГО</strong></div>
          {renderReportTable(
            'Отчет по поступлениям',
            ['Гараж', 'Дата', 'Время', 'Сумма платежа', 'Назначение платежа', 'Остаток долга после платежа'],
            incomeReportLoading ? [] : incomeRows.length > 0 ? incomeRows : [[incomeGarageFilterLabel, '', '', 'Данных за период нет', '', '']],
            !incomeReportLoading && incomeReport ? ['ИТОГО', '', '', formatMoney(incomeReport.incomeTotal), '', ''] : undefined,
          )}
          <TablePagination ariaLabel="Пагинация отчета по поступлениям" totalCount={incomePage.totalCount} offset={incomePage.offset} limit={incomePage.limit} visibleCount={incomePage.items.length} disabled={incomeReportLoading} pageSizeLabel="Количество строк отчета по поступлениям" onPageChange={(page) => setIncomePageRequest((current) => ({ ...current, offset: (page - 1) * current.limit }))} onPageSizeChange={(limit) => setIncomePageRequest({ offset: 0, limit })} />
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'cashPayments') {
      const cashRows = cashPaymentReport?.rows.map((row) => [
        row.date,
        formatMoney(row.amount),
        row.hasReceipt ? 'Да' : 'Нет',
        row.purpose,
        row.comment ?? '',
      ]) ?? []
      const cashPaymentPage = {
        items: cashPaymentReport?.rows ?? [],
        totalCount: cashPaymentReport?.rowCount ?? 0,
        offset: cashPaymentReport?.offset ?? cashPaymentPageRequest.offset,
        limit: cashPaymentReport?.limit ?? cashPaymentPageRequest.limit,
      }
      return (
        <ReportWorkbookSheet title="Отчёт по оплатам из кассы">
          {renderDateFilter('cashPayments', { from: 'С', to: 'По' })}
          {cashPaymentReportLoading ? <TableLoadingState label="Загружаем оплаты из кассы..." /> : null}
          {cashPaymentReportError ? <FormError>{cashPaymentReportError}</FormError> : null}
          <div className="report-workbook-toolbar" role="group" aria-label="Выгрузка отчета по оплатам из кассы">
            {renderReportExportButton('xlsx', 'cashPayments-xlsx', () => void downloadCashOrBankReport('cashPayments', 'xlsx'))}
            {renderReportExportButton('pdf', 'cashPayments-pdf', () => void downloadCashOrBankReport('cashPayments', 'pdf'))}
          </div>
          <div className="report-workbook-summary-row report-workbook-summary-row--single"><strong>ИТОГО</strong></div>
          {renderReportTable(
            'Отчет по оплатам из кассы',
            ['Дата', 'Сумма', 'Наличие чека', 'Назначение', 'Комментарий'],
            cashPaymentReportLoading ? [] : cashRows.length > 0 ? cashRows : [['', 'Операций за период нет', '', '', '']],
            !cashPaymentReportLoading && cashPaymentReport ? ['ИТОГО', formatMoney(cashPaymentReport.total), '', '', `${cashPaymentReport.rowCount} операций`] : undefined,
          )}
          <TablePagination ariaLabel="Пагинация отчета по оплатам из кассы" totalCount={cashPaymentPage.totalCount} offset={cashPaymentPage.offset} limit={cashPaymentPage.limit} visibleCount={cashPaymentPage.items.length} disabled={cashPaymentReportLoading} pageSizeLabel="Количество строк отчета по оплатам из кассы" onPageChange={(page) => setCashPaymentPageRequest((current) => ({ ...current, offset: (page - 1) * current.limit }))} onPageSizeChange={(limit) => setCashPaymentPageRequest({ offset: 0, limit })} />
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'bankDeposits') {
      const bankRows = bankDepositReport?.rows.map((row) => [
        row.date,
        formatMoney(row.amount),
        row.comment || row.fundName || '',
      ]) ?? []
      const bankDepositPage = {
        items: bankDepositReport?.rows ?? [],
        totalCount: bankDepositReport?.rowCount ?? 0,
        offset: bankDepositReport?.offset ?? bankDepositPageRequest.offset,
        limit: bankDepositReport?.limit ?? bankDepositPageRequest.limit,
      }
      return (
        <ReportWorkbookSheet title="Отчёт по сдаче кассы в банк">
          {renderDateFilter('bankDeposits', { from: 'С', to: 'По' })}
          {bankDepositReportLoading ? <TableLoadingState label="Загружаем сдачу кассы в банк..." /> : null}
          {bankDepositReportError ? <FormError>{bankDepositReportError}</FormError> : null}
          <div className="report-workbook-toolbar" role="group" aria-label="Выгрузка отчета по сдаче кассы в банк">
            {renderReportExportButton('xlsx', 'bankDeposits-xlsx', () => void downloadCashOrBankReport('bankDeposits', 'xlsx'))}
            {renderReportExportButton('pdf', 'bankDeposits-pdf', () => void downloadCashOrBankReport('bankDeposits', 'pdf'))}
          </div>
          <div className="report-workbook-summary-row report-workbook-summary-row--single"><strong>ИТОГО</strong></div>
          {renderReportTable(
            'Отчет по сдаче кассы в банк',
            ['Дата', 'Сумма', 'Комментарий'],
            bankDepositReportLoading ? [] : bankRows.length > 0 ? bankRows : [['', 'Операций за период нет', '']],
            !bankDepositReportLoading && bankDepositReport ? ['ИТОГО', formatMoney(bankDepositReport.total), `${bankDepositReport.rowCount} операций`] : undefined,
          )}
          <TablePagination ariaLabel="Пагинация отчета по сдаче кассы в банк" totalCount={bankDepositPage.totalCount} offset={bankDepositPage.offset} limit={bankDepositPage.limit} visibleCount={bankDepositPage.items.length} disabled={bankDepositReportLoading} pageSizeLabel="Количество строк отчета по сдаче кассы в банк" onPageChange={(page) => setBankDepositPageRequest((current) => ({ ...current, offset: (page - 1) * current.limit }))} onPageSizeChange={(limit) => setBankDepositPageRequest({ offset: 0, limit })} />
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'fees') {
      const feeSummaryPage = createClientPage(feeReport?.summaryRows ?? [], feeSummaryPageNumber, feeSummaryPageSize)
      const summaryRows = feeSummaryPage.items.map((row) => [
        <button
          className="link-button"
          type="button"
          aria-label={`Открыть детализацию сбора ${row.name}`}
          onClick={() => {
            setFeeVariationFilter(row.name)
            setAppliedFeeVariationFilter(row.name)
            setFeeSummaryPageNumber(1)
            setFeeDetailPageNumber(1)
            setFeeDebtorsVisible(true)
            setFeeDetailMode('all')
          }}
        >
          {row.name}
        </button>,
        row.goal,
        formatMoney(row.feeAmount),
        formatMoney(row.collected),
      ])
      const feeDetailRows = (feeDetailMode === 'debtors'
        ? feeReport?.garageRows.filter((row) => row.debt > 0)
        : feeReport?.garageRows) ?? []
      const feeDetailPage = createClientPage(feeDetailRows, feeDetailPageNumber, feeDetailPageSize)
      const feeDetailTableRows = feeDetailPage.items.map((row) => [
        row.garageNumber,
        row.ownerName ?? '',
        formatMoney(row.accrued),
        formatMoney(row.paid),
        row.lastPaymentDate ?? '',
        formatMoney(row.debt),
      ])
      const feeDetailEmptyRow = feeDetailMode === 'debtors'
        ? ['', '', '', '', '', 'Должников нет']
        : ['', '', '', '', '', 'Данных по гаражам нет']
      const feeDetailTableName = feeDetailMode === 'debtors' ? 'Должники по сбору' : 'Гаражи по сбору'
      return (
        <ReportWorkbookSheet title="Отчёт по сборам">
          {feeReportLoading ? <TableLoadingState label="Загружаем отчёт по сборам" /> : null}
          <div className="report-workbook-filter report-workbook-filter--single" aria-label="Фильтры отчета по сборам">
            <label className="report-workbook-filter-wide">
              <span>Вариация сбора</span>
              <input aria-label="Вариация сбора" list={feeOptionsId} value={feeVariationFilter} onChange={(event) => setFeeVariationFilter(event.target.value)} placeholder="Название сбора" />
            </label>
          </div>
          <div className="report-workbook-toolbar" role="group" aria-label="Выгрузка отчета по сборам">
            {renderReportExportButton('xlsx', 'fees-xlsx', () => void downloadFeeReport('xlsx'))}
            {renderReportExportButton('pdf', 'fees-pdf', () => void downloadFeeReport('pdf'))}
          </div>
          <div className="report-workbook-split">
            {renderReportTable(
              'Отчет по сборам',
              ['Наименование', 'Цель', 'Сумма сбора', 'Собрано'],
              summaryRows.length > 0 ? summaryRows : [[feeVariationLabel, 'Данных по сбору нет', '', '']],
              feeReport ? ['ИТОГО', '', formatMoney(feeReport.accruedTotal), formatMoney(feeReport.collectedTotal)] : undefined,
            )}
            <TablePagination
              ariaLabel="Пагинация отчета по сборам"
              totalCount={feeSummaryPage.totalCount}
              offset={feeSummaryPage.offset}
              limit={feeSummaryPage.limit}
              visibleCount={feeSummaryPage.items.length}
              pageSizeLabel="Количество строк отчета по сборам"
              onPageChange={setFeeSummaryPageNumber}
              onPageSizeChange={(limit) => {
                setFeeSummaryPageNumber(1)
                setFeeSummaryPageSize(limit)
              }}
            />
            <div className="report-workbook-side-summary" aria-label="Детализация сбора">
              <dl>
                <div><dt>{feeReport?.variation ?? feeVariationLabel}</dt><dd>{formatMoney(feeReport?.accruedTotal ?? 0)}</dd></div>
                <div><dt>Собрано</dt><dd>{formatMoney(feeReport?.collectedTotal ?? 0)}</dd></div>
                <div><dt>Задолженность</dt><dd>{formatMoney(feeReport?.debtTotal ?? 0)}</dd></div>
              </dl>
              <button
                aria-controls="fee-debtors-report"
                aria-expanded={feeDebtorsVisible}
                className="link-button"
                type="button"
                onClick={() => setFeeDebtorsVisible((value) => !value)}
              >
                {feeDebtorsVisible ? 'Скрыть должников' : 'Показать должников'}
              </button>
              {feeDebtorsVisible ? (
                <div id="fee-debtors-report">
                  <div className="report-workbook-toolbar" role="group" aria-label="Режим детализации сбора">
                    <button className="secondary-button" type="button" aria-pressed={feeDetailMode === 'debtors'} onClick={() => setFeeDetailMode('debtors')}>
                      Только должники
                    </button>
                    <button className="secondary-button" type="button" aria-pressed={feeDetailMode === 'all'} onClick={() => setFeeDetailMode('all')}>
                      Все гаражи
                    </button>
                  </div>
                  {renderReportTable(
                    feeDetailTableName,
                    ['Гараж', 'Владелец', 'Начислено', 'Оплачено', 'Дата', 'Задолженность'],
                    feeDetailTableRows.length > 0 ? feeDetailTableRows : [feeDetailEmptyRow],
                  )}
                  <TablePagination
                    ariaLabel="Пагинация детализации сбора"
                    totalCount={feeDetailPage.totalCount}
                    offset={feeDetailPage.offset}
                    limit={feeDetailPage.limit}
                    visibleCount={feeDetailPage.items.length}
                    pageSizeLabel="Количество строк детализации сбора"
                    onPageChange={setFeeDetailPageNumber}
                    onPageSizeChange={(limit) => {
                      setFeeDetailPageNumber(1)
                      setFeeDetailPageSize(limit)
                    }}
                  />
                </div>
              ) : null}
            </div>
          </div>
        </ReportWorkbookSheet>
      )
    }

    const fundRows = fundChangeReport?.rows.map((row) => [
      row.fundName,
      row.date,
      row.changeName,
      formatMoney(row.amount),
      formatMoney(row.balanceBefore),
      formatMoney(row.balanceAfter),
      row.actorDisplayName ?? '',
      row.reason,
    ]) ?? []
    const visibleFundRows = fundRows.length > 0
      ? fundRows
      : [['', '', 'Операций за период нет', '', '', '', '', '']]
    const fundPage = {
      items: fundChangeReport?.rows ?? [],
      totalCount: fundChangeReport?.rowCount ?? 0,
      offset: fundChangeReport?.offset ?? fundChangePageRequest.offset,
      limit: fundChangeReport?.limit ?? fundChangePageRequest.limit,
    }

    return (
      <ReportWorkbookSheet title="Отчёт по изменению фондов">
        {renderDateFilter('funds', { from: 'С', to: 'По' })}
        <div className="report-workbook-toolbar" role="group" aria-label="Выгрузка отчета по изменению фондов">
          {renderReportExportButton('xlsx', 'funds-xlsx', () => void downloadFundChangeReport('xlsx'))}
          {renderReportExportButton('pdf', 'funds-pdf', () => void downloadFundChangeReport('pdf'))}
        </div>
        {fundChangeReportLoading ? <TableLoadingState label="Загружаем изменения фондов..." /> : null}
        {fundChangeReportError ? <FormError>{fundChangeReportError}</FormError> : null}
        {fundChangeReport ? (
          <div className="report-workbook-summary-row">
            <strong>Пополнено: {formatMoney(fundChangeReport.depositTotal)}</strong>
            <strong>Изъято: {formatMoney(fundChangeReport.withdrawalTotal)}</strong>
            <strong>Операций: {fundChangeReport.rowCount}</strong>
          </div>
        ) : null}
        {renderReportTable('Отчет по изменению фондов', ['Фонд', 'Дата', 'Изменение', 'Сумма', 'Сумма до', 'Сумма после', 'Пользователь', 'Комментарий'], fundChangeReportLoading ? [] : visibleFundRows)}
        <TablePagination ariaLabel="Пагинация отчета по изменению фондов" totalCount={fundPage.totalCount} offset={fundPage.offset} limit={fundPage.limit} visibleCount={fundPage.items.length} disabled={fundChangeReportLoading} pageSizeLabel="Количество строк отчета по изменению фондов" onPageChange={(page) => setFundChangePageRequest((current) => ({ ...current, offset: (page - 1) * current.limit }))} onPageSizeChange={(limit) => setFundChangePageRequest({ offset: 0, limit })} />
      </ReportWorkbookSheet>
    )
  }

  return (
    <section className="dictionary-panel reports-panel reports-workbook-panel" aria-label="Отчеты">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Отчеты</p>
          <h2>Отчетность ГСК</h2>
        </div>
        <span>{selectedTab.meta}</span>
      </div>

      {dictionaryError ? <FormError>{dictionaryError}</FormError> : null}
      {reportDataError ? <FormError>{reportDataError}</FormError> : null}
      {reportExportMessage ? <p className="form-success">{reportExportMessage}</p> : null}

      <datalist id={garageOptionsId}>
        {garages.map((garage) => <option value={`Гараж ${garage.number}`} key={garage.id} />)}
      </datalist>
      <datalist id={supplierOptionsId}>
        {counterpartyOptions.map((item) => <option value={item} key={item} />)}
      </datalist>
      <datalist id={feeOptionsId}>
        {feeOptions.map((item) => <option value={item} key={item} />)}
      </datalist>

      <div className="report-tabs report-tabs--workbook" role="tablist" aria-label="Разделы отчетов">
        {reportWorkbookTabs.map((tab) => (
          <button
            type="button"
            role="tab"
            id={`report-tab-${tab.key}`}
            aria-selected={activeReportTab === tab.key}
            aria-controls={`report-panel-${tab.key}`}
            className={activeReportTab === tab.key ? 'is-active' : undefined}
            onClick={() => setActiveReportTab(tab.key)}
            key={tab.key}
          >
            <span>{tab.label}</span>
            <small>{tab.meta}</small>
          </button>
        ))}
      </div>

      <div className="report-tab-panel" role="tabpanel" id={`report-panel-${activeReportTab}`} aria-labelledby={`report-tab-${activeReportTab}`}>
        {renderActiveReport()}
      </div>
    </section>
  )
}

function ReportWorkbookSheet({ children, title }: { children: ReactNode; title: string }) {
  return (
    <div className="report-workbook-sheet">
      <h3>{title}</h3>
      {children}
    </div>
  )
}
