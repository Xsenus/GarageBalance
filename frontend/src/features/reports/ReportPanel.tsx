import { useEffect, useId, useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { FileSpreadsheet, FileText } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { AccountingTypeDto, DictionaryClient, GarageDto, SupplierDto } from '../../services/dictionariesApi'
import type { BankDepositReportDto, CashPaymentReportDto, ConsolidatedReportDto, ExpenseReportDto, FeeReportDto, FundChangeReportDto, GarageDetailReportDto, IncomeReportDto, ReportClient } from '../../services/reportsApi'
import { buildReportFileName, buildSnapshotReportFileName, downloadBlob } from '../../shared/fileExports'
import { FormError } from '../../shared/formFeedback'
import { formatMoney, formatMonth, formatOperationTime, getCurrentMonthInputValue, getLocalDateInputValue, getPreviousMonthInputValue } from '../../shared/formatters'
import { getPageNavigation, getPageVisibleRange, pageSizeOptions } from '../../shared/pagination'

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
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [dictionaryError, setDictionaryError] = useState<string | null>(null)
  const [consolidatedReport, setConsolidatedReport] = useState<ConsolidatedReportDto | null>(null)
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

    async function loadWorkbookReports() {
      setReportDataError(null)
      try {
        const consolidatedFilter = monthlyFilters.consolidated
        const [loadedConsolidated, loadedConsolidatedIncome, loadedConsolidatedExpenses, loadedFees] = await Promise.all([
          reportClient.getConsolidatedReport(auth.accessToken, {
            monthFrom: getReportMonthStart(consolidatedFilter.monthFrom),
            monthTo: getReportMonthStart(consolidatedFilter.monthTo),
            limit: 100,
          }),
          reportClient.getIncomeReport(auth.accessToken, {
            dateFrom: getReportMonthStart(consolidatedFilter.monthFrom),
            dateTo: getReportMonthEnd(consolidatedFilter.monthTo),
            rowMode: 'payments',
            limit: 500,
          }),
          reportClient.getExpenseReport(auth.accessToken, {
            dateFrom: getReportMonthStart(consolidatedFilter.monthFrom),
            dateTo: getReportMonthEnd(consolidatedFilter.monthTo),
            rowMode: 'payments',
            limit: 500,
          }),
          reportClient.getFeeReport(auth.accessToken, {
            variation: feeVariationFilter.trim() || undefined,
            limit: 100,
          }),
        ])

        if (ignore) {
          return
        }

        setConsolidatedReport(loadedConsolidated)
        setConsolidatedIncomeBreakdown(loadedConsolidatedIncome)
        setConsolidatedExpenseBreakdown(loadedConsolidatedExpenses)
        setFeeReport(loadedFees)
      } catch (caught) {
        if (!ignore) {
          setReportDataError(caught instanceof Error ? caught.message : 'Не удалось загрузить расчетные данные отчетов.')
        }
      }
    }

    void loadWorkbookReports()

    return () => {
      ignore = true
    }
  }, [auth.accessToken, feeVariationFilter, monthlyFilters.consolidated, reportClient])

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
          search: garageFilter.trim() || undefined,
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
  }, [activeReportTab, auth.accessToken, garageAccrualsGrouped, garageFilter, garagePageRequest.limit, garagePageRequest.offset, monthlyFilters.garages, reportClient])

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
          search: counterpartyFilter.trim() || undefined,
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
  }, [activeReportTab, auth.accessToken, counterpartyFilter, monthlyFilters.payouts, payoutPageRequest.limit, payoutPageRequest.offset, reportClient])

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
          search: incomeGarageFilter.trim() || undefined,
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
  }, [activeReportTab, auth.accessToken, dateFilters.income, incomeGarageFilter, incomePageRequest.limit, incomePageRequest.offset, reportClient])

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
      return (
        <ReportWorkbookSheet title="Консолидированный отчёт">
          {renderMonthlyFilter('consolidated', { from: 'Месяц с', to: 'Месяц по' })}
          {renderReportTable(
            'Консолидированный отчет',
            ['Месяц', 'Наименование', 'Поступления', 'Наименование', 'Выплаты', 'Разница', 'На начало месяца', 'На конец месяца'],
            reportRows,
            consolidatedReport ? ['ИТОГО', '', formatMoney(consolidatedReport.incomeTotal), '', formatMoney(consolidatedReport.expenseTotal), formatMoney(consolidatedReport.balance), formatMoney(consolidatedReport.debt), formatMoney(consolidatedReport.balance)] : undefined,
          )}
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
      const garageVisibleRange = getPageVisibleRange(garagePage)
      const garageNavigation = getPageNavigation(garagePage)
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
                  onChange={(event) => {
                    setGaragePageRequest((current) => ({ ...current, offset: 0 }))
                    setGarageFilter(event.target.value)
                  }}
                  placeholder="Гараж или номер"
                />
              </label>
            ),
          })}
          {garageReportLoading ? <p className="prototype-status" role="status">Загружаем отчет по гаражам...</p> : null}
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
            reportRows.length > 0 ? reportRows : [emptyGarageRow],
            garageReportFooter,
          )}
          <div className="dictionary-pagination" role="navigation" aria-label="Пагинация отчета по гаражам">
            <span role="status" aria-live="polite">Показано {garageVisibleRange.from}-{garageVisibleRange.to} из {garagePage.totalCount}</span>
            <label>
              Строк на странице
              <select
                aria-label="Строк на странице отчета по гаражам"
                value={garagePage.limit}
                disabled={garageReportLoading}
                onChange={(event) => setGaragePageRequest({ offset: 0, limit: Number(event.target.value) })}
              >
                {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
              </select>
            </label>
            <button className="ghost-button" type="button" disabled={!garageNavigation.canGoPrevious || garageReportLoading} onClick={() => setGaragePageRequest((current) => ({ ...current, offset: garageNavigation.previousOffset }))}>Назад</button>
            <button className="ghost-button" type="button" disabled={!garageNavigation.canGoNext || garageReportLoading} onClick={() => setGaragePageRequest((current) => ({ ...current, offset: garageNavigation.nextOffset }))}>Вперед</button>
          </div>
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
      const payoutVisibleRange = getPageVisibleRange(payoutPage)
      const payoutNavigation = getPageNavigation(payoutPage)
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
                  onChange={(event) => {
                    setPayoutPageRequest((current) => ({ ...current, offset: 0 }))
                    setCounterpartyFilter(event.target.value)
                  }}
                  placeholder="Поставщик или сотрудник"
                />
              </label>
            ),
          })}
          {payoutReportLoading ? <p className="prototype-status" role="status">Загружаем выплаты...</p> : null}
          {payoutReportError ? <FormError>{payoutReportError}</FormError> : null}
          <div className="report-workbook-summary-row">
            <strong>ИТОГО начислений</strong>
            <strong>ИТОГО выплат</strong>
            <strong>Разница</strong>
          </div>
          {renderReportTable(
            'Отчет по выплатам',
            ['Месяц', 'Услуга', 'Поставщик/сотрудник', 'Начисления', 'Выплаты', 'Разница'],
            reportRows.length > 0 ? reportRows : [['', '', counterpartyFilterLabel, 'Данных за период нет', '', '']],
            payoutReport ? ['ИТОГО', '', '', formatMoney(payoutReport.accrualTotal), formatMoney(payoutReport.expenseTotal), formatMoney(payoutReport.difference)] : undefined,
          )}
          <div className="dictionary-pagination" role="navigation" aria-label="Пагинация отчета по выплатам">
            <span role="status" aria-live="polite">Показано {payoutVisibleRange.from}-{payoutVisibleRange.to} из {payoutPage.totalCount}</span>
            <label>
              Строк на странице
              <select
                aria-label="Строк на странице отчета по выплатам"
                value={payoutPage.limit}
                disabled={payoutReportLoading}
                onChange={(event) => setPayoutPageRequest({ offset: 0, limit: Number(event.target.value) })}
              >
                {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
              </select>
            </label>
            <button className="ghost-button" type="button" disabled={!payoutNavigation.canGoPrevious || payoutReportLoading} onClick={() => setPayoutPageRequest((current) => ({ ...current, offset: payoutNavigation.previousOffset }))}>Назад</button>
            <button className="ghost-button" type="button" disabled={!payoutNavigation.canGoNext || payoutReportLoading} onClick={() => setPayoutPageRequest((current) => ({ ...current, offset: payoutNavigation.nextOffset }))}>Вперед</button>
          </div>
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
      const incomeVisibleRange = getPageVisibleRange(incomePage)
      const incomeNavigation = getPageNavigation(incomePage)
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
                  onChange={(event) => {
                    setIncomePageRequest((current) => ({ ...current, offset: 0 }))
                    setIncomeGarageFilter(event.target.value)
                  }}
                  placeholder="Гараж или номер"
                />
              </label>
            ),
          })}
          {incomeReportLoading ? <p className="prototype-status" role="status">Загружаем поступления...</p> : null}
          {incomeReportError ? <FormError>{incomeReportError}</FormError> : null}
          <div className="report-workbook-summary-row report-workbook-summary-row--single"><strong>ИТОГО</strong></div>
          {renderReportTable(
            'Отчет по поступлениям',
            ['Гараж', 'Дата', 'Время', 'Сумма платежа', 'Назначение платежа', 'Остаток долга после платежа'],
            incomeRows.length > 0 ? incomeRows : [[incomeGarageFilterLabel, '', '', 'Данных за период нет', '', '']],
            incomeReport ? ['ИТОГО', '', '', formatMoney(incomeReport.incomeTotal), '', ''] : undefined,
          )}
          <div className="dictionary-pagination" role="navigation" aria-label="Пагинация отчета по поступлениям">
            <span role="status" aria-live="polite">Показано {incomeVisibleRange.from}-{incomeVisibleRange.to} из {incomePage.totalCount}</span>
            <label>
              Строк на странице
              <select
                aria-label="Строк на странице отчета по поступлениям"
                value={incomePage.limit}
                disabled={incomeReportLoading}
                onChange={(event) => setIncomePageRequest({ offset: 0, limit: Number(event.target.value) })}
              >
                {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
              </select>
            </label>
            <button className="ghost-button" type="button" disabled={!incomeNavigation.canGoPrevious || incomeReportLoading} onClick={() => setIncomePageRequest((current) => ({ ...current, offset: incomeNavigation.previousOffset }))}>Назад</button>
            <button className="ghost-button" type="button" disabled={!incomeNavigation.canGoNext || incomeReportLoading} onClick={() => setIncomePageRequest((current) => ({ ...current, offset: incomeNavigation.nextOffset }))}>Вперед</button>
          </div>
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
      const cashPaymentVisibleRange = getPageVisibleRange(cashPaymentPage)
      const cashPaymentNavigation = getPageNavigation(cashPaymentPage)
      return (
        <ReportWorkbookSheet title="Отчёт по оплатам из кассы">
          {renderDateFilter('cashPayments', { from: 'С', to: 'По' })}
          {cashPaymentReportLoading ? <p className="prototype-status" role="status">Загружаем оплаты из кассы...</p> : null}
          {cashPaymentReportError ? <FormError>{cashPaymentReportError}</FormError> : null}
          <div className="report-workbook-toolbar" role="group" aria-label="Выгрузка отчета по оплатам из кассы">
            {renderReportExportButton('xlsx', 'cashPayments-xlsx', () => void downloadCashOrBankReport('cashPayments', 'xlsx'))}
            {renderReportExportButton('pdf', 'cashPayments-pdf', () => void downloadCashOrBankReport('cashPayments', 'pdf'))}
          </div>
          <div className="report-workbook-summary-row report-workbook-summary-row--single"><strong>ИТОГО</strong></div>
          {renderReportTable(
            'Отчет по оплатам из кассы',
            ['Дата', 'Сумма', 'Наличие чека', 'Назначение', 'Комментарий'],
            cashRows.length > 0 ? cashRows : [['', 'Операций за период нет', '', '', '']],
            cashPaymentReport ? ['ИТОГО', formatMoney(cashPaymentReport.total), '', '', `${cashPaymentReport.rowCount} операций`] : undefined,
          )}
          <div className="dictionary-pagination" role="navigation" aria-label="Пагинация отчета по оплатам из кассы">
            <span role="status" aria-live="polite">Показано {cashPaymentVisibleRange.from}-{cashPaymentVisibleRange.to} из {cashPaymentPage.totalCount}</span>
            <label>
              Строк на странице
              <select
                aria-label="Строк на странице отчета по оплатам из кассы"
                value={cashPaymentPage.limit}
                disabled={cashPaymentReportLoading}
                onChange={(event) => setCashPaymentPageRequest({ offset: 0, limit: Number(event.target.value) })}
              >
                {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
              </select>
            </label>
            <button className="ghost-button" type="button" disabled={!cashPaymentNavigation.canGoPrevious || cashPaymentReportLoading} onClick={() => setCashPaymentPageRequest((current) => ({ ...current, offset: cashPaymentNavigation.previousOffset }))}>Назад</button>
            <button className="ghost-button" type="button" disabled={!cashPaymentNavigation.canGoNext || cashPaymentReportLoading} onClick={() => setCashPaymentPageRequest((current) => ({ ...current, offset: cashPaymentNavigation.nextOffset }))}>Вперед</button>
          </div>
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
      const bankDepositVisibleRange = getPageVisibleRange(bankDepositPage)
      const bankDepositNavigation = getPageNavigation(bankDepositPage)
      return (
        <ReportWorkbookSheet title="Отчёт по сдаче кассы в банк">
          {renderDateFilter('bankDeposits', { from: 'С', to: 'По' })}
          {bankDepositReportLoading ? <p className="prototype-status" role="status">Загружаем сдачу кассы в банк...</p> : null}
          {bankDepositReportError ? <FormError>{bankDepositReportError}</FormError> : null}
          <div className="report-workbook-toolbar" role="group" aria-label="Выгрузка отчета по сдаче кассы в банк">
            {renderReportExportButton('xlsx', 'bankDeposits-xlsx', () => void downloadCashOrBankReport('bankDeposits', 'xlsx'))}
            {renderReportExportButton('pdf', 'bankDeposits-pdf', () => void downloadCashOrBankReport('bankDeposits', 'pdf'))}
          </div>
          <div className="report-workbook-summary-row report-workbook-summary-row--single"><strong>ИТОГО</strong></div>
          {renderReportTable(
            'Отчет по сдаче кассы в банк',
            ['Дата', 'Сумма', 'Комментарий'],
            bankRows.length > 0 ? bankRows : [['', 'Операций за период нет', '']],
            bankDepositReport ? ['ИТОГО', formatMoney(bankDepositReport.total), `${bankDepositReport.rowCount} операций`] : undefined,
          )}
          <div className="dictionary-pagination" role="navigation" aria-label="Пагинация отчета по сдаче кассы в банк">
            <span role="status" aria-live="polite">Показано {bankDepositVisibleRange.from}-{bankDepositVisibleRange.to} из {bankDepositPage.totalCount}</span>
            <label>
              Строк на странице
              <select
                aria-label="Строк на странице отчета по сдаче кассы в банк"
                value={bankDepositPage.limit}
                disabled={bankDepositReportLoading}
                onChange={(event) => setBankDepositPageRequest({ offset: 0, limit: Number(event.target.value) })}
              >
                {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
              </select>
            </label>
            <button className="ghost-button" type="button" disabled={!bankDepositNavigation.canGoPrevious || bankDepositReportLoading} onClick={() => setBankDepositPageRequest((current) => ({ ...current, offset: bankDepositNavigation.previousOffset }))}>Назад</button>
            <button className="ghost-button" type="button" disabled={!bankDepositNavigation.canGoNext || bankDepositReportLoading} onClick={() => setBankDepositPageRequest((current) => ({ ...current, offset: bankDepositNavigation.nextOffset }))}>Вперед</button>
          </div>
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'fees') {
      const summaryRows = feeReport?.summaryRows.map((row) => [
        <button
          className="link-button"
          type="button"
          aria-label={`Открыть детализацию сбора ${row.name}`}
          onClick={() => {
            setFeeVariationFilter(row.name)
            setFeeDebtorsVisible(true)
            setFeeDetailMode('all')
          }}
        >
          {row.name}
        </button>,
        row.goal,
        formatMoney(row.feeAmount),
        formatMoney(row.collected),
      ]) ?? []
      const feeDetailRows = (feeDetailMode === 'debtors'
        ? feeReport?.garageRows.filter((row) => row.debt > 0)
        : feeReport?.garageRows) ?? []
      const feeDetailTableRows = feeDetailRows.map((row) => [
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
    const fundVisibleRange = getPageVisibleRange(fundPage)
    const fundNavigation = getPageNavigation(fundPage)

    return (
      <ReportWorkbookSheet title="Отчёт по изменению фондов">
        {renderDateFilter('funds', { from: 'С', to: 'По' })}
        <div className="report-workbook-toolbar" role="group" aria-label="Выгрузка отчета по изменению фондов">
          {renderReportExportButton('xlsx', 'funds-xlsx', () => void downloadFundChangeReport('xlsx'))}
          {renderReportExportButton('pdf', 'funds-pdf', () => void downloadFundChangeReport('pdf'))}
        </div>
        {fundChangeReportLoading ? <p className="prototype-status" role="status">Загружаем изменения фондов...</p> : null}
        {fundChangeReportError ? <FormError>{fundChangeReportError}</FormError> : null}
        {fundChangeReport ? (
          <div className="report-workbook-summary-row">
            <strong>Пополнено: {formatMoney(fundChangeReport.depositTotal)}</strong>
            <strong>Изъято: {formatMoney(fundChangeReport.withdrawalTotal)}</strong>
            <strong>Операций: {fundChangeReport.rowCount}</strong>
          </div>
        ) : null}
        {renderReportTable('Отчет по изменению фондов', ['Фонд', 'Дата', 'Изменение', 'Сумма', 'Сумма до', 'Сумма после', 'Пользователь', 'Комментарий'], visibleFundRows)}
        <div className="dictionary-pagination" role="navigation" aria-label="Пагинация отчета по изменению фондов">
          <span role="status" aria-live="polite">Показано {fundVisibleRange.from}-{fundVisibleRange.to} из {fundPage.totalCount}</span>
          <label>
            Строк на странице
            <select
              aria-label="Строк на странице отчета по изменению фондов"
              value={fundPage.limit}
              disabled={fundChangeReportLoading}
              onChange={(event) => setFundChangePageRequest({ offset: 0, limit: Number(event.target.value) })}
            >
              {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
            </select>
          </label>
          <button className="ghost-button" type="button" disabled={!fundNavigation.canGoPrevious || fundChangeReportLoading} onClick={() => setFundChangePageRequest((current) => ({ ...current, offset: fundNavigation.previousOffset }))}>Назад</button>
          <button className="ghost-button" type="button" disabled={!fundNavigation.canGoNext || fundChangeReportLoading} onClick={() => setFundChangePageRequest((current) => ({ ...current, offset: fundNavigation.nextOffset }))}>Вперед</button>
        </div>
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
