import { useEffect, useId, useRef, useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { FileSpreadsheet, FileText, LoaderCircle, Search, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { AccountingTypeDto, DictionaryClient, GarageDto, StaffMemberDto, SupplierDto } from '../../services/dictionariesApi'
import type { BankDepositReportDto, CashPaymentReportDto, ConsolidatedReportDto, ExpenseReportDto, FeeReportDto, FundChangeReportDto, GarageDetailReportDto, IncomeReportDto, ReportClient } from '../../services/reportsApi'
import { EmptyState, TableLoadingState } from '../../shared/AsyncState'
import { buildReportFileName, buildSnapshotReportFileName, downloadBlob } from '../../shared/fileExports'
import { FormError } from '../../shared/formFeedback'
import { formatMoney, formatMonth, formatOperationTime, getCurrentMonthInputValue, getLocalDateInputValue, getPreviousMonthInputValue } from '../../shared/formatters'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { createClientPage } from '../../shared/pagination'
import { advanceReportSort } from '../../shared/reportSorting'
import type { ReportSort } from '../../shared/reportSorting'
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

const dictionaryScreenRequestLimit = 100

type ReportFilterOption = {
  value: string
  label: string
}

type ReportColumn = {
  label: string
  sortField?: string
}

function ReportMultiSelect({
  label,
  ariaLabel,
  allLabel,
  options,
  selectedValues,
  onChange,
}: {
  label: string
  ariaLabel: string
  allLabel: string
  options: ReportFilterOption[]
  selectedValues: string[]
  onChange: (values: string[]) => void
}) {
  const selectId = useId()
  const statusId = useId()
  const isAllSelected = selectedValues.length === 0

  return (
    <div className="report-workbook-filter-wide report-workbook-multi-select">
      <div className="report-workbook-multi-select-heading">
        <label htmlFor={selectId}>{label}</label>
        <button
          className="link-button"
          type="button"
          aria-label={`${allLabel}: сбросить выбор`}
          aria-pressed={isAllSelected}
          onClick={() => onChange([])}
        >
          Все
        </button>
      </div>
      <select
        id={selectId}
        multiple
        aria-label={ariaLabel}
        aria-describedby={statusId}
        value={selectedValues}
        onChange={(event) => onChange(Array.from(event.currentTarget.selectedOptions, (option) => option.value))}
      >
        {options.map((option) => <option value={option.value} key={option.value}>{option.label}</option>)}
      </select>
      <span className="report-workbook-multi-select-status" id={statusId} role="status" aria-live="polite">
        {isAllSelected ? allLabel : `Выбрано: ${selectedValues.length}`}
      </span>
    </div>
  )
}

function ReportGarageMultiSelect({
  label,
  allLabel,
  options,
  selectedValues,
  onChange,
}: {
  label: string
  allLabel: string
  options: ReportFilterOption[]
  selectedValues: string[]
  onChange: (values: string[]) => void
}) {
  const searchId = useId()
  const listId = useId()
  const statusId = useId()
  const wrapRef = useRef<HTMLDivElement | null>(null)
  const [search, setSearch] = useState('')
  const [searchOpen, setSearchOpen] = useState(false)
  const normalizedSearch = search.trim().toLocaleLowerCase('ru-RU')
  const filteredOptions = normalizedSearch
    ? options.filter((option) => option.label.toLocaleLowerCase('ru-RU').includes(normalizedSearch)).slice(0, 20)
    : []
  const selectedOptions = selectedValues
    .map((value) => options.find((option) => option.value === value))
    .filter((option): option is ReportFilterOption => Boolean(option))
  const shouldShowResults = searchOpen && normalizedSearch.length > 0

  useEffect(() => {
    function handlePointerDown(event: PointerEvent) {
      if (wrapRef.current && !wrapRef.current.contains(event.target as Node)) {
        setSearchOpen(false)
      }
    }

    document.addEventListener('pointerdown', handlePointerDown)
    return () => document.removeEventListener('pointerdown', handlePointerDown)
  }, [])

  function toggleSelection(value: string) {
    onChange(selectedValues.includes(value)
      ? selectedValues.filter((selectedValue) => selectedValue !== value)
      : [...selectedValues, value])
  }

  function selectFirstResult() {
    const firstOption = filteredOptions[0]
    if (firstOption) {
      toggleSelection(firstOption.value)
    }
  }

  return (
    <div className="report-workbook-filter-wide report-garage-picker">
      <label htmlFor={searchId}>{label}</label>
      <div ref={wrapRef} className="payments-prototype-search-wrap">
        <label className="payments-prototype-search">
          <Search size={18} aria-hidden="true" />
          <input
            id={searchId}
            role="combobox"
            aria-label={label}
            aria-expanded={shouldShowResults}
            aria-controls={listId}
            aria-describedby={statusId}
            placeholder="Введите номер гаража или ФИО владельца"
            value={search}
            onFocus={() => setSearchOpen(search.trim().length > 0)}
            onChange={(event) => {
              setSearch(event.target.value)
              setSearchOpen(event.target.value.trim().length > 0)
            }}
            onKeyDown={(event) => {
              if (event.key === 'Escape') {
                event.preventDefault()
                setSearchOpen(false)
              } else if (event.key === 'Enter') {
                event.preventDefault()
                selectFirstResult()
              }
            }}
          />
        </label>
        {shouldShowResults ? (
          <div className="payments-prototype-search-results" id={listId} role="listbox" aria-label="Найденные гаражи отчёта">
            {filteredOptions.length > 0 ? filteredOptions.map((option) => {
              const [title, description] = option.label.split(' · ', 2)
              return (
                <label className="payments-prototype-search-option" key={option.value} role="option" aria-selected={selectedValues.includes(option.value)}>
                  <input
                    type="checkbox"
                    aria-label={`Выбрать ${title.toLocaleLowerCase('ru-RU')}, ${description}`}
                    checked={selectedValues.includes(option.value)}
                    onChange={() => toggleSelection(option.value)}
                  />
                  <span>
                    <strong>{title}</strong>
                    <small>{description}</small>
                  </span>
                </label>
              )
            }) : <span className="payments-prototype-search-empty">Ничего не найдено</span>}
          </div>
        ) : null}
      </div>
      {selectedValues.length === 0 ? (
        <span className="report-workbook-multi-select-status" id={statusId} role="status" aria-live="polite">{allLabel}</span>
      ) : null}
      {selectedOptions.length > 0 ? (
        <div className="payments-prototype-selected-garages report-garage-picker-selected" aria-label="Выбранные гаражи отчёта">
          <div className="payments-prototype-selected-heading">
            <span id={statusId} role="status" aria-live="polite">Выбрано: {selectedOptions.length}</span>
            <button className="ghost-button" type="button" onClick={() => onChange([])}>Очистить</button>
          </div>
          <div className="payments-prototype-selected-list">
            {selectedOptions.map((option) => {
              const [title, description] = option.label.split(' · ', 2)
              return (
                <div className="report-garage-picker-selected-item" key={option.value}>
                  <span>
                    <strong>{title}</strong>
                    <small>{description}</small>
                  </span>
                  <button
                    className="icon-button payments-prototype-selected-remove"
                    type="button"
                    aria-label={`Убрать ${title.toLocaleLowerCase('ru-RU')} из выбранных`}
                    title="Убрать из выбранных"
                    onClick={() => toggleSelection(option.value)}
                  >
                    <X size={14} aria-hidden="true" />
                  </button>
                </div>
              )
            })}
          </div>
        </div>
      ) : null}
    </div>
  )
}

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

export function ReportPanel({ auth, dictionaryClient, reportClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient; reportClient: ReportClient }) {
  const today = getLocalDateInputValue()
  const currentMonth = getCurrentMonthInputValue(today)
  const previousMonth = getPreviousMonthInputValue(currentMonth)
  const feeOptionsId = useId()
  const [activeReportTab, setActiveReportTab] = useState<ReportWorkbookTab>('consolidated')
  const [reportSorts, setReportSorts] = useState<Partial<Record<ReportWorkbookTab, ReportSort>>>({})
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
  const [selectedGarageIds, setSelectedGarageIds] = useState<string[]>([])
  const [selectedCounterpartyKeys, setSelectedCounterpartyKeys] = useState<string[]>([])
  const [selectedIncomeGarageIds, setSelectedIncomeGarageIds] = useState<string[]>([])
  const [feeVariationFilter, setFeeVariationFilter] = useState('Сбор на ворота')
  const [appliedFeeVariationFilter, setAppliedFeeVariationFilter] = useState('Сбор на ворота')
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [staffMembers, setStaffMembers] = useState<StaffMemberDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const loadedReportDictionaries = useRef({ garages: false, suppliers: false, staffMembers: false, incomeTypes: false })
  const [reportDataSettled, setReportDataSettled] = useState<Partial<Record<ReportWorkbookTab, boolean>>>({})
  const [dictionaryError, setDictionaryError] = useState<string | null>(null)
  const [consolidatedReport, setConsolidatedReport] = useState<ConsolidatedReportDto | null>(null)
  const [consolidatedReportLoading, setConsolidatedReportLoading] = useState(true)
  const [consolidatedReportError, setConsolidatedReportError] = useState<string | null>(null)
  const [consolidatedPageNumber, setConsolidatedPageNumber] = useState(1)
  const [consolidatedPageSize, setConsolidatedPageSize] = useState(25)
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
  const [feeReportError, setFeeReportError] = useState<string | null>(null)
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
    if (feeVariationFilter === appliedFeeVariationFilter) {
      return undefined
    }
    const handle = window.setTimeout(() => {
      setAppliedFeeVariationFilter(feeVariationFilter)
      setFeeSummaryPageNumber(1)
      setFeeDetailPageNumber(1)
    }, 350)
    return () => window.clearTimeout(handle)
  }, [appliedFeeVariationFilter, feeVariationFilter])

  useEffect(() => {
    if ((activeReportTab === 'garages' || activeReportTab === 'payouts' || activeReportTab === 'income' || activeReportTab === 'fees')
      && !reportDataSettled[activeReportTab]) {
      return
    }

    let ignore = false

    async function loadReportDictionaries() {
      const needsGarages = activeReportTab === 'garages' || activeReportTab === 'income'
      const needsSuppliers = activeReportTab === 'payouts'
      const needsStaffMembers = activeReportTab === 'payouts'
      const needsIncomeTypes = activeReportTab === 'fees'
      const requests: Promise<void>[] = []

      if (needsGarages && !loadedReportDictionaries.current.garages) {
        requests.push(dictionaryClient.getGarages(auth.accessToken, undefined, dictionaryScreenRequestLimit).then((loaded) => {
          if (!ignore) {
            setGarages(loaded.filter((garage) => !garage.isArchived))
            loadedReportDictionaries.current.garages = true
          }
        }))
      }
      if (needsSuppliers && !loadedReportDictionaries.current.suppliers) {
        requests.push(dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit).then((loaded) => {
          if (!ignore) {
            setSuppliers(loaded.filter((supplier) => !supplier.isArchived))
            loadedReportDictionaries.current.suppliers = true
          }
        }))
      }
      if (needsStaffMembers && !loadedReportDictionaries.current.staffMembers) {
        requests.push(dictionaryClient.getStaffMembers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit).then((loaded) => {
          if (!ignore) {
            setStaffMembers(loaded.filter((member) => !member.isArchived))
            loadedReportDictionaries.current.staffMembers = true
          }
        }))
      }
      if (needsIncomeTypes && !loadedReportDictionaries.current.incomeTypes) {
        requests.push(dictionaryClient.getIncomeTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit).then((loaded) => {
          if (!ignore) {
            setIncomeTypes(loaded.filter((item) => !item.isArchived))
            loadedReportDictionaries.current.incomeTypes = true
          }
        }))
      }
      if (requests.length === 0) {
        return
      }

      setDictionaryError(null)
      try {
        await Promise.all(requests)
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
  }, [activeReportTab, auth.accessToken, dictionaryClient, reportDataSettled])

  useEffect(() => {
    let ignore = false

    async function loadConsolidatedReport() {
      setConsolidatedReportLoading(true)
      setConsolidatedReport(null)
      setConsolidatedReportError(null)
      try {
        const consolidatedFilter = monthlyFilters.consolidated
        const monthFrom = getReportMonthStart(consolidatedFilter.monthFrom)
        const monthTo = getReportMonthStart(consolidatedFilter.monthTo)
        const sort = reportSorts.consolidated
        const loadedConsolidated = await reportClient.getConsolidatedReport(auth.accessToken, {
          monthFrom,
          monthTo,
          limit: 100,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
        })

        if (ignore) {
          return
        }

        setConsolidatedReport(loadedConsolidated)
        setConsolidatedReportLoading(false)
      } catch (caught) {
        if (!ignore) {
          setConsolidatedReportError(caught instanceof Error ? caught.message : 'Не удалось загрузить расчетные данные отчетов.')
          setConsolidatedReportLoading(false)
        }
      }
    }

    void loadConsolidatedReport()

    return () => {
      ignore = true
    }
  }, [auth.accessToken, monthlyFilters.consolidated, reportClient, reportSorts.consolidated])

  useEffect(() => {
    if (activeReportTab !== 'fees') {
      return
    }

    let ignore = false
    async function loadFeeReport() {
      setReportDataSettled((current) => ({ ...current, fees: false }))
      setFeeReportLoading(true)
      setFeeReport(null)
      setFeeReportError(null)
      try {
        const sort = reportSorts.fees
        const report = await reportClient.getFeeReport(auth.accessToken, {
          variation: appliedFeeVariationFilter.trim() || undefined,
          limit: 100,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
        })
        if (!ignore) {
          setFeeReport(report)
        }
      } catch (caught) {
        if (!ignore) {
          setFeeReportError(caught instanceof Error ? caught.message : 'Не удалось загрузить отчет по сборам.')
        }
      } finally {
        if (!ignore) {
          setReportDataSettled((current) => ({ ...current, fees: true }))
          setFeeReportLoading(false)
        }
      }
    }

    void loadFeeReport()
    return () => {
      ignore = true
    }
  }, [activeReportTab, appliedFeeVariationFilter, auth.accessToken, reportClient, reportSorts.fees])

  useEffect(() => {
    if (activeReportTab !== 'garages') {
      return
    }

    let ignore = false

    async function loadGarageReport() {
      setReportDataSettled((current) => ({ ...current, garages: false }))
      setGarageReportLoading(true)
      setGarageReport(null)
      setGarageReportError(null)
      try {
        const filter = monthlyFilters.garages
        const sort = reportSorts.garages
        const report = await reportClient.getGarageReport(auth.accessToken, {
          monthFrom: getReportMonthStart(filter.monthFrom),
          monthTo: getReportMonthStart(filter.monthTo),
          garageIds: selectedGarageIds,
          groupAccruals: garageAccrualsGrouped,
          offset: garagePageRequest.offset,
          limit: garagePageRequest.limit,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
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
          setReportDataSettled((current) => ({ ...current, garages: true }))
          setGarageReportLoading(false)
        }
      }
    }

    void loadGarageReport()

    return () => {
      ignore = true
    }
  }, [activeReportTab, auth.accessToken, garageAccrualsGrouped, garagePageRequest.limit, garagePageRequest.offset, monthlyFilters.garages, reportClient, reportSorts.garages, selectedGarageIds])

  useEffect(() => {
    if (activeReportTab !== 'payouts') {
      return
    }

    let ignore = false

    async function loadPayoutReport() {
      setReportDataSettled((current) => ({ ...current, payouts: false }))
      setPayoutReportLoading(true)
      setPayoutReport(null)
      setPayoutReportError(null)
      try {
        const filter = monthlyFilters.payouts
        const supplierIds = selectedCounterpartyKeys.filter((key) => key.startsWith('supplier:')).map((key) => key.slice('supplier:'.length))
        const staffMemberIds = selectedCounterpartyKeys.filter((key) => key.startsWith('staff:')).map((key) => key.slice('staff:'.length))
        const sort = reportSorts.payouts
        const report = await reportClient.getExpenseReport(auth.accessToken, {
          dateFrom: getReportMonthStart(filter.monthFrom),
          dateTo: getReportMonthEnd(filter.monthTo),
          supplierIds,
          staffMemberIds,
          offset: payoutPageRequest.offset,
          limit: payoutPageRequest.limit,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
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
          setReportDataSettled((current) => ({ ...current, payouts: true }))
          setPayoutReportLoading(false)
        }
      }
    }

    void loadPayoutReport()

    return () => {
      ignore = true
    }
  }, [activeReportTab, auth.accessToken, monthlyFilters.payouts, payoutPageRequest.limit, payoutPageRequest.offset, reportClient, reportSorts.payouts, selectedCounterpartyKeys])

  useEffect(() => {
    if (activeReportTab !== 'income') {
      return
    }

    let ignore = false

    async function loadIncomeReport() {
      setReportDataSettled((current) => ({ ...current, income: false }))
      setIncomeReportLoading(true)
      setIncomeReport(null)
      setIncomeReportError(null)
      try {
        const filter = dateFilters.income
        const sort = reportSorts.income
        const report = await reportClient.getIncomeReport(auth.accessToken, {
          dateFrom: filter.dateFrom,
          dateTo: filter.dateTo,
          garageIds: selectedIncomeGarageIds,
          rowMode: 'payments',
          offset: incomePageRequest.offset,
          limit: incomePageRequest.limit,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
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
          setReportDataSettled((current) => ({ ...current, income: true }))
          setIncomeReportLoading(false)
        }
      }
    }

    void loadIncomeReport()

    return () => {
      ignore = true
    }
  }, [activeReportTab, auth.accessToken, dateFilters.income, incomePageRequest.limit, incomePageRequest.offset, reportClient, reportSorts.income, selectedIncomeGarageIds])

  useEffect(() => {
    if (activeReportTab !== 'cashPayments') {
      return
    }

    let ignore = false

    async function loadCashPayments() {
      setCashPaymentReportLoading(true)
      setCashPaymentReport(null)
      setCashPaymentReportError(null)
      try {
        const filter = dateFilters.cashPayments
        const sort = reportSorts.cashPayments
        const report = await reportClient.getCashPaymentReport(auth.accessToken, {
          dateFrom: filter.dateFrom,
          dateTo: filter.dateTo,
          offset: cashPaymentPageRequest.offset,
          limit: cashPaymentPageRequest.limit,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
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
  }, [activeReportTab, auth.accessToken, cashPaymentPageRequest.limit, cashPaymentPageRequest.offset, dateFilters.cashPayments, reportClient, reportSorts.cashPayments])

  useEffect(() => {
    if (activeReportTab !== 'bankDeposits') {
      return
    }

    let ignore = false

    async function loadBankDeposits() {
      setBankDepositReportLoading(true)
      setBankDepositReport(null)
      setBankDepositReportError(null)
      try {
        const filter = dateFilters.bankDeposits
        const sort = reportSorts.bankDeposits
        const report = await reportClient.getBankDepositReport(auth.accessToken, {
          dateFrom: filter.dateFrom,
          dateTo: filter.dateTo,
          offset: bankDepositPageRequest.offset,
          limit: bankDepositPageRequest.limit,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
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
  }, [activeReportTab, auth.accessToken, bankDepositPageRequest.limit, bankDepositPageRequest.offset, dateFilters.bankDeposits, reportClient, reportSorts.bankDeposits])

  useEffect(() => {
    if (activeReportTab !== 'funds') {
      return
    }

    let ignore = false

    async function loadFundChanges() {
      setFundChangeReportLoading(true)
      setFundChangeReport(null)
      setFundChangeReportError(null)
      try {
        const filter = dateFilters.funds
        const sort = reportSorts.funds
        const report = await reportClient.getFundChangeReport(auth.accessToken, {
          dateFrom: filter.dateFrom,
          dateTo: filter.dateTo,
          offset: fundChangePageRequest.offset,
          limit: fundChangePageRequest.limit,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
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
  }, [activeReportTab, auth.accessToken, dateFilters.funds, fundChangePageRequest.limit, fundChangePageRequest.offset, reportClient, reportSorts.funds])

  const selectedTab = reportWorkbookTabs.find((tab) => tab.key === activeReportTab) ?? reportWorkbookTabs[0]
  const counterpartyOptions = [
    ...suppliers.map((supplier) => ({ value: `supplier:${supplier.id}`, label: supplier.name })),
    ...staffMembers.map((member) => ({ value: `staff:${member.id}`, label: member.fullName })),
  ]
  const feeVariationLabel = feeVariationFilter.trim() || 'Все сборы'
  const feeOptions = Array.from(new Set(['Сбор на ворота', 'Вступительный взнос', 'Целевой взнос', ...incomeTypes.map((item) => item.name)]))

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

  async function downloadConsolidatedReport(extension: 'xlsx' | 'pdf') {
    const filter = monthlyFilters.consolidated
    const params = {
      monthFrom: getReportMonthStart(filter.monthFrom),
      monthTo: getReportMonthStart(filter.monthTo),
      sortBy: reportSorts.consolidated?.field,
      sortDirection: reportSorts.consolidated?.direction,
    }
    const exportKey = `consolidated-${extension}`
    setReportExporting(exportKey)
    setReportExportMessage(null)
    setReportDataError(null)
    try {
      const blob = extension === 'xlsx'
        ? await reportClient.exportConsolidatedReportXlsx(auth.accessToken, params)
        : await reportClient.exportConsolidatedReportPdf(auth.accessToken, params)
      downloadBlob(blob, buildReportFileName('consolidated', params.monthFrom, params.monthTo, extension))
      setReportExportMessage(extension === 'xlsx' ? 'Отчет XLSX готов.' : 'Отчет PDF готов.')
    } catch (caught) {
      setReportDataError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчет.')
    } finally {
      setReportExporting(null)
    }
  }

  async function downloadGarageReport(extension: 'xlsx' | 'pdf') {
    const filter = monthlyFilters.garages
    const params = {
      monthFrom: getReportMonthStart(filter.monthFrom),
      monthTo: getReportMonthStart(filter.monthTo),
      garageIds: selectedGarageIds,
      groupAccruals: garageAccrualsGrouped,
      sortBy: reportSorts.garages?.field,
      sortDirection: reportSorts.garages?.direction,
    }
    const exportKey = `garages-${extension}`
    setReportExporting(exportKey)
    setReportExportMessage(null)
    setReportDataError(null)
    try {
      const blob = extension === 'xlsx'
        ? await reportClient.exportGarageReportXlsx(auth.accessToken, params)
        : await reportClient.exportGarageReportPdf(auth.accessToken, params)
      downloadBlob(blob, buildReportFileName('garages', params.monthFrom, params.monthTo, extension))
      setReportExportMessage(extension === 'xlsx' ? 'Отчет XLSX готов.' : 'Отчет PDF готов.')
    } catch (caught) {
      setReportDataError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчет.')
    } finally {
      setReportExporting(null)
    }
  }

  async function downloadPayoutReport(extension: 'xlsx' | 'pdf') {
    const filter = monthlyFilters.payouts
    const supplierIds = selectedCounterpartyKeys.filter((key) => key.startsWith('supplier:')).map((key) => key.slice('supplier:'.length))
    const staffMemberIds = selectedCounterpartyKeys.filter((key) => key.startsWith('staff:')).map((key) => key.slice('staff:'.length))
    const params = {
      dateFrom: getReportMonthStart(filter.monthFrom),
      dateTo: getReportMonthEnd(filter.monthTo),
      supplierIds,
      staffMemberIds,
      sortBy: reportSorts.payouts?.field,
      sortDirection: reportSorts.payouts?.direction,
    }
    const exportKey = `payouts-${extension}`
    setReportExporting(exportKey)
    setReportExportMessage(null)
    setReportDataError(null)
    try {
      const blob = extension === 'xlsx'
        ? await reportClient.exportExpenseReportXlsx(auth.accessToken, params)
        : await reportClient.exportExpenseReportPdf(auth.accessToken, params)
      downloadBlob(blob, buildReportFileName('expense', params.dateFrom, params.dateTo, extension))
      setReportExportMessage(extension === 'xlsx' ? 'Отчет XLSX готов.' : 'Отчет PDF готов.')
    } catch (caught) {
      setReportDataError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчет.')
    } finally {
      setReportExporting(null)
    }
  }

  async function downloadIncomeReport(extension: 'xlsx' | 'pdf') {
    const filter = dateFilters.income
    const params = {
      ...filter,
      garageIds: selectedIncomeGarageIds,
      sortBy: reportSorts.income?.field,
      sortDirection: reportSorts.income?.direction,
    }
    const exportKey = `income-${extension}`
    setReportExporting(exportKey)
    setReportExportMessage(null)
    setReportDataError(null)
    try {
      const blob = extension === 'xlsx'
        ? await reportClient.exportIncomeReportXlsx(auth.accessToken, params)
        : await reportClient.exportIncomeReportPdf(auth.accessToken, params)
      downloadBlob(blob, buildReportFileName('income', filter.dateFrom, filter.dateTo, extension))
      setReportExportMessage(extension === 'xlsx' ? 'Отчет XLSX готов.' : 'Отчет PDF готов.')
    } catch (caught) {
      setReportDataError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчет.')
    } finally {
      setReportExporting(null)
    }
  }

  async function downloadCashOrBankReport(type: 'cashPayments' | 'bankDeposits', extension: 'xlsx' | 'pdf') {
    const filter = dateFilters[type]
    const sort = reportSorts[type]
    const params = { ...filter, sortBy: sort?.field, sortDirection: sort?.direction }
    const exportKey = `${type}-${extension}`
    setReportExporting(exportKey)
    setReportExportMessage(null)
    setReportDataError(null)
    try {
      const blob = type === 'cashPayments'
        ? extension === 'xlsx'
          ? await reportClient.exportCashPaymentReportXlsx(auth.accessToken, params)
          : await reportClient.exportCashPaymentReportPdf(auth.accessToken, params)
        : extension === 'xlsx'
          ? await reportClient.exportBankDepositReportXlsx(auth.accessToken, params)
          : await reportClient.exportBankDepositReportPdf(auth.accessToken, params)
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
    const variation = appliedFeeVariationFilter.trim() || undefined
    const params = { variation, sortBy: reportSorts.fees?.field, sortDirection: reportSorts.fees?.direction }
    setReportExporting(exportKey)
    setReportExportMessage(null)
    setReportDataError(null)
    try {
      const blob = extension === 'xlsx'
        ? await reportClient.exportFeeReportXlsx(auth.accessToken, params)
        : await reportClient.exportFeeReportPdf(auth.accessToken, params)
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
    const params = { ...filter, sortBy: reportSorts.funds?.field, sortDirection: reportSorts.funds?.direction }
    const exportKey = `funds-${extension}`
    setReportExporting(exportKey)
    setReportExportMessage(null)
    setReportDataError(null)
    try {
      const blob = extension === 'xlsx'
        ? await reportClient.exportFundChangeReportXlsx(auth.accessToken, params)
        : await reportClient.exportFundChangeReportPdf(auth.accessToken, params)
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
    const Icon = extension === 'xlsx' ? FileSpreadsheet : FileText
    const isExporting = reportExporting === exportKey

    return (
      <button className={`secondary-button report-export-button report-export-button--${extension}`} type="button" aria-label={label} aria-busy={isExporting} title={label} data-tooltip={label} disabled={reportExporting !== null} onClick={onClick}>
        {isExporting ? <LoaderCircle className="report-export-button__spinner" size={19} aria-hidden="true" /> : <Icon size={19} strokeWidth={2.1} aria-hidden="true" />}
      </button>
    )
  }

  function renderMonthlyFilter(key: ReportMonthlyFilterKey, labels: { from: string; to: string; extra?: ReactNode; actions?: ReactNode }) {
    const filter = monthlyFilters[key]
    return (
      <div className="report-workbook-filter" aria-label={`Фильтры отчета ${labels.from}`}>
        <div className="report-workbook-filter__fields">
          <label>
            <span>{labels.from}</span>
            <LocalizedDatePicker ariaLabel={labels.from} mode="month" value={filter.monthFrom} onChange={(value) => updateMonthlyFilter(key, 'monthFrom', value)} required />
          </label>
          <label>
            <span>{labels.to}</span>
            <LocalizedDatePicker ariaLabel={labels.to} mode="month" value={filter.monthTo} onChange={(value) => updateMonthlyFilter(key, 'monthTo', value)} required />
          </label>
          <button className="link-button report-period-button" type="button" onClick={() => applyPreviousMonth(key)}>Предыдущий</button>
        </div>
        {labels.actions ? <div className="report-workbook-filter__actions" role="group" aria-label="Действия с отчетом">{labels.actions}</div> : null}
        {labels.extra ? <div className="report-workbook-filter__extra">{labels.extra}</div> : null}
      </div>
    )
  }

  function renderDateFilter(key: ReportDateFilterKey, labels: { from: string; to: string; extra?: ReactNode; actions?: ReactNode }) {
    const filter = dateFilters[key]
    return (
      <div className="report-workbook-filter" aria-label={`Фильтры отчета ${labels.from}`}>
        <div className="report-workbook-filter__fields">
          <label>
            <span>{labels.from}</span>
            <LocalizedDatePicker ariaLabel={labels.from} mode="date" value={filter.dateFrom} onChange={(value) => updateDateFilter(key, 'dateFrom', value)} required />
          </label>
          <label>
            <span>{labels.to}</span>
            <LocalizedDatePicker ariaLabel={labels.to} mode="date" value={filter.dateTo} onChange={(value) => updateDateFilter(key, 'dateTo', value)} required />
          </label>
          <button className="link-button report-period-button" type="button" onClick={() => applyToday(key)}>Сегодня</button>
        </div>
        {labels.actions ? <div className="report-workbook-filter__actions" role="group" aria-label="Действия с отчетом">{labels.actions}</div> : null}
        {labels.extra ? <div className="report-workbook-filter__extra">{labels.extra}</div> : null}
      </div>
    )
  }

  function resetReportPage(tab: ReportWorkbookTab) {
    if (tab === 'consolidated') {
      setConsolidatedPageNumber(1)
    } else if (tab === 'garages') {
      setGaragePageRequest((current) => ({ ...current, offset: 0 }))
    } else if (tab === 'payouts') {
      setPayoutPageRequest((current) => ({ ...current, offset: 0 }))
    } else if (tab === 'income') {
      setIncomePageRequest((current) => ({ ...current, offset: 0 }))
    } else if (tab === 'cashPayments') {
      setCashPaymentPageRequest((current) => ({ ...current, offset: 0 }))
    } else if (tab === 'bankDeposits') {
      setBankDepositPageRequest((current) => ({ ...current, offset: 0 }))
    } else if (tab === 'fees') {
      setFeeDetailPageNumber(1)
    } else {
      setFundChangePageRequest((current) => ({ ...current, offset: 0 }))
    }
  }

  function updateReportSort(tab: ReportWorkbookTab, field: string) {
    setReportSorts((current) => ({ ...current, [tab]: advanceReportSort(current[tab], field) ?? undefined }))
    resetReportPage(tab)
  }

  function clearReportSort(tab: ReportWorkbookTab) {
    setReportSorts((current) => ({ ...current, [tab]: undefined }))
    resetReportPage(tab)
  }

  function renderReportTable(
    ariaLabel: string,
    columns: Array<string | ReportColumn>,
    rows: Array<Array<ReactNode>>,
    footer?: Array<ReactNode>,
    sortOptions?: { tab: ReportWorkbookTab; disabled?: boolean },
    emptyMessage?: string,
  ) {
    const normalizedColumns = columns.map((column) => typeof column === 'string' ? { label: column } : column)
    const sort = sortOptions ? reportSorts[sortOptions.tab] : undefined
    const sortLabel = normalizedColumns.find((column) => column.sortField === sort?.field)?.label
    return (
      <>
        {sortOptions ? (
          <div className="report-sort-status">
            <span role="status" aria-live="polite">{sort && sortLabel ? `Сортировка: ${sortLabel}, ${sort.direction === 'asc' ? 'по возрастанию' : 'по убыванию'}` : 'Сортировка: по умолчанию'}</span>
            <button className="link-button" type="button" disabled={!sort || sortOptions.disabled} onClick={() => clearReportSort(sortOptions.tab)}>Сбросить сортировку</button>
          </div>
        ) : null}
        <div className="report-workbook-table" role="table" aria-label={ariaLabel}>
          <div className="report-workbook-row report-workbook-row--header" role="row" style={{ '--report-columns': normalizedColumns.length } as CSSProperties}>
            {normalizedColumns.map((column, columnIndex) => {
              const isActive = Boolean(column.sortField && sort?.field === column.sortField)
              const ariaSort = column.sortField ? isActive ? sort?.direction === 'asc' ? 'ascending' : 'descending' : 'none' : undefined
              return (
                <span role="columnheader" aria-sort={ariaSort} key={`${column.label}-${columnIndex}`}>
                  {column.sortField && sortOptions ? (
                    <button
                      className="link-button report-sort-button"
                      type="button"
                      disabled={sortOptions.disabled}
                      aria-label={`Сортировать ${column.label}: ${isActive ? sort?.direction === 'asc' ? 'сейчас по возрастанию' : 'сейчас по убыванию' : 'сейчас по умолчанию'}`}
                      onClick={() => updateReportSort(sortOptions.tab, column.sortField!)}
                    >
                      <span>{column.label}</span>
                      <span className="report-sort-direction" aria-hidden="true">{isActive ? sort?.direction === 'asc' ? '↑' : '↓' : '↕'}</span>
                    </button>
                  ) : column.label}
                </span>
              )
            })}
          </div>
          {rows.map((row, rowIndex) => (
            <div className="report-workbook-row" role="row" style={{ '--report-columns': normalizedColumns.length } as CSSProperties} key={`${ariaLabel}-${rowIndex}`}>
              {row.map((cell, cellIndex) => <span role="cell" key={`${ariaLabel}-${rowIndex}-${cellIndex}`}>{cell}</span>)}
            </div>
          ))}
          {rows.length === 0 && emptyMessage ? (
            <div className="report-workbook-row report-workbook-row--empty" role="row" style={{ '--report-columns': normalizedColumns.length } as CSSProperties}>
              <div className="report-workbook-empty-cell" role="cell">
                <EmptyState className="report-workbook-empty-state">{emptyMessage}</EmptyState>
              </div>
            </div>
          ) : null}
          {footer ? (
            <div className="report-workbook-row report-workbook-row--footer" role="row" style={{ '--report-columns': normalizedColumns.length } as CSSProperties}>
              {footer.map((cell, cellIndex) => <span role="cell" key={`${ariaLabel}-footer-${cellIndex}`}>{cell}</span>)}
            </div>
          ) : null}
        </div>
      </>
    )
  }

  function renderActiveReport() {
    if (activeReportTab === 'consolidated') {
      const consolidatedPage = createClientPage(consolidatedReport?.monthlyRows ?? [], consolidatedPageNumber, consolidatedPageSize)
      const reportRows = consolidatedPage.items.flatMap((month) => {
        const incomeRows = month.incomeBreakdown ?? []
        const expenseRows = month.expenseBreakdown ?? []
        const detailCount = Math.max(incomeRows.length, expenseRows.length)
        const details = Array.from({ length: detailCount }, (_, index) => {
          const incomeRow = incomeRows[index]
          const expenseRow = expenseRows[index]
          return [
            index === 0 ? formatMonth(month.accountingMonth) : '',
            incomeRow?.name ?? '',
            incomeRow ? formatMoney(incomeRow.amount) : '',
            expenseRow?.name ?? '',
            expenseRow ? formatMoney(expenseRow.amount) : '',
            '',
            '',
            '',
          ]
        })
        return [
          ...details,
          [
            detailCount === 0 ? formatMonth(month.accountingMonth) : '',
            'ИТОГО',
            formatMoney(month.incomeTotal),
            'ИТОГО',
            formatMoney(month.expenseTotal),
            formatMoney(month.incomeTotal - month.expenseTotal),
            formatMoney(month.bankBalanceOpening),
            formatMoney(month.bankBalanceClosing),
          ],
        ]
      })
      return (
        <ReportWorkbookSheet title="Консолидированный отчёт">
          {renderMonthlyFilter('consolidated', {
            from: 'Месяц с',
            to: 'Месяц по',
            actions: <>{renderReportExportButton('xlsx', 'consolidated-xlsx', () => void downloadConsolidatedReport('xlsx'))}{renderReportExportButton('pdf', 'consolidated-pdf', () => void downloadConsolidatedReport('pdf'))}</>,
          })}
          {consolidatedReportLoading ? <TableLoadingState label="Загружаем сводный отчёт" /> : null}
          {consolidatedReportError ? <FormError>{consolidatedReportError}</FormError> : null}
          {renderReportTable(
            'Консолидированный отчет',
            [{ label: 'Месяц', sortField: 'accountingMonth' }, 'Наименование поступления', 'Поступления', 'Наименование выплаты', 'Выплаты', 'Разница', 'Остаток по счёту — На начало месяца', 'Остаток по счёту — На конец месяца'],
            consolidatedReportLoading ? [] : reportRows,
            undefined,
            { tab: 'consolidated', disabled: consolidatedReportLoading },
            consolidatedReportLoading || consolidatedReportError ? undefined : 'Данных за период нет',
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
      const garageReportColumns: ReportColumn[] = garageAccrualsGrouped
        ? [{ label: 'Месяц', sortField: 'accountingMonth' }, { label: 'Гараж', sortField: 'garageNumber' }, { label: 'Начисления', sortField: 'accrualAmount' }, { label: 'Поступления', sortField: 'incomeAmount' }, { label: 'Разница', sortField: 'difference' }]
        : [{ label: 'Месяц', sortField: 'accountingMonth' }, { label: 'Гараж', sortField: 'garageNumber' }, { label: 'Услуга', sortField: 'incomeTypeName' }, { label: 'Начисления', sortField: 'accrualAmount' }, { label: 'Поступления', sortField: 'incomeAmount' }, { label: 'Разница', sortField: 'difference' }]
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
          row.incomeTypeName,
          formatMoney(row.accrualAmount),
          formatMoney(row.incomeAmount),
          formatMoney(row.difference),
        ]) ?? []
      const garageReportFooter = garageReport
        ? garageAccrualsGrouped
          ? ['ИТОГО', '', formatMoney(garageReport.accrualTotal), formatMoney(garageReport.incomeTotal), formatMoney(garageReport.difference)]
          : ['ИТОГО', '', '', formatMoney(garageReport.accrualTotal), formatMoney(garageReport.incomeTotal), formatMoney(garageReport.difference)]
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
            actions: (
              <>
                <button
                  className="secondary-button report-group-button"
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
                {renderReportExportButton('xlsx', 'garages-xlsx', () => void downloadGarageReport('xlsx'))}
                {renderReportExportButton('pdf', 'garages-pdf', () => void downloadGarageReport('pdf'))}
              </>
            ),
            extra: (
              <ReportGarageMultiSelect
                label="Гаражи"
                allLabel="Все гаражи"
                options={garages.map((garage) => ({ value: garage.id, label: `Гараж ${garage.number} · ${garage.ownerName ?? 'без владельца'}` }))}
                selectedValues={selectedGarageIds}
                onChange={(values) => {
                  setGaragePageRequest((current) => current.offset === 0 ? current : { ...current, offset: 0 })
                  setSelectedGarageIds(values)
                }}
              />
            ),
          })}
          <p className="report-workbook-comment" role="note">
            Начисления и поступления сопоставлены по месяцу, гаражу и услуге. Разница = начисления − поступления. Группировка объединяет услуги в одну строку по гаражу и месяцу.
          </p>
          {garageReportLoading ? <TableLoadingState label="Загружаем отчет по гаражам..." /> : null}
          {garageReportError ? <FormError>{garageReportError}</FormError> : null}
          <div className="report-workbook-summary-row">
            <strong>ИТОГО начислений</strong>
            <strong>ИТОГО поступлений</strong>
            <strong>Разница</strong>
          </div>
          {renderReportTable(
            'Отчет по гаражам',
            garageReportColumns,
            garageReportLoading ? [] : reportRows,
            garageReportLoading ? undefined : garageReportFooter,
            { tab: 'garages', disabled: garageReportLoading },
            garageReportLoading || garageReportError ? undefined : 'Данных за период нет',
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
            actions: <>{renderReportExportButton('xlsx', 'payouts-xlsx', () => void downloadPayoutReport('xlsx'))}{renderReportExportButton('pdf', 'payouts-pdf', () => void downloadPayoutReport('pdf'))}</>,
            extra: (
              <ReportMultiSelect
                label="Поставщики/сотрудники"
                ariaLabel="Поставщики или сотрудники"
                allLabel="Все поставщики и сотрудники"
                options={counterpartyOptions}
                selectedValues={selectedCounterpartyKeys}
                onChange={(values) => {
                  setPayoutPageRequest((current) => current.offset === 0 ? current : { ...current, offset: 0 })
                  setSelectedCounterpartyKeys(values)
                }}
              />
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
            [{ label: 'Месяц', sortField: 'accountingMonth' }, { label: 'Услуга', sortField: 'expenseTypeName' }, { label: 'Поставщик/сотрудник', sortField: 'supplierName' }, { label: 'Начисления', sortField: 'accrualAmount' }, { label: 'Выплаты', sortField: 'expenseAmount' }, { label: 'Разница', sortField: 'difference' }],
            payoutReportLoading ? [] : reportRows,
            !payoutReportLoading && payoutReport ? ['ИТОГО', '', '', formatMoney(payoutReport.accrualTotal), formatMoney(payoutReport.expenseTotal), formatMoney(payoutReport.difference)] : undefined,
            { tab: 'payouts', disabled: payoutReportLoading },
            payoutReportLoading || payoutReportError ? undefined : 'Данных за период нет',
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
            actions: <>{renderReportExportButton('xlsx', 'income-xlsx', () => void downloadIncomeReport('xlsx'))}{renderReportExportButton('pdf', 'income-pdf', () => void downloadIncomeReport('pdf'))}</>,
            extra: (
              <ReportMultiSelect
                label="Гаражи"
                ariaLabel="Гаражи по поступлениям"
                allLabel="Все гаражи"
                options={garages.map((garage) => ({ value: garage.id, label: `Гараж ${garage.number} · ${garage.ownerName ?? 'без владельца'}` }))}
                selectedValues={selectedIncomeGarageIds}
                onChange={(values) => {
                  setIncomePageRequest((current) => current.offset === 0 ? current : { ...current, offset: 0 })
                  setSelectedIncomeGarageIds(values)
                }}
              />
            ),
          })}
          {incomeReportLoading ? <TableLoadingState label="Загружаем поступления..." /> : null}
          {incomeReportError ? <FormError>{incomeReportError}</FormError> : null}
          <div className="report-workbook-summary-row report-workbook-summary-row--single"><strong>ИТОГО</strong></div>
          {renderReportTable(
            'Отчет по поступлениям',
            [{ label: 'Гараж', sortField: 'garageNumber' }, { label: 'Дата', sortField: 'date' }, 'Время', { label: 'Сумма платежа', sortField: 'incomeAmount' }, { label: 'Назначение платежа', sortField: 'incomeTypeName' }, { label: 'Остаток долга после платежа', sortField: 'debt' }],
            incomeReportLoading ? [] : incomeRows,
            !incomeReportLoading && incomeReport ? ['ИТОГО', '', '', formatMoney(incomeReport.incomeTotal), '', ''] : undefined,
            { tab: 'income', disabled: incomeReportLoading },
            incomeReportLoading || incomeReportError ? undefined : 'Данных за период нет',
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
          {renderDateFilter('cashPayments', { from: 'С', to: 'По', actions: <>{renderReportExportButton('xlsx', 'cashPayments-xlsx', () => void downloadCashOrBankReport('cashPayments', 'xlsx'))}{renderReportExportButton('pdf', 'cashPayments-pdf', () => void downloadCashOrBankReport('cashPayments', 'pdf'))}</> })}
          {cashPaymentReportLoading ? <TableLoadingState label="Загружаем оплаты из кассы..." /> : null}
          {cashPaymentReportError ? <FormError>{cashPaymentReportError}</FormError> : null}
          <div className="report-workbook-summary-row report-workbook-summary-row--single"><strong>ИТОГО</strong></div>
          {renderReportTable(
            'Отчет по оплатам из кассы',
            [{ label: 'Дата', sortField: 'date' }, { label: 'Сумма', sortField: 'amount' }, { label: 'Наличие чека', sortField: 'hasReceipt' }, { label: 'Назначение', sortField: 'purpose' }, 'Комментарий'],
            cashPaymentReportLoading ? [] : cashRows,
            !cashPaymentReportLoading && cashPaymentReport ? ['ИТОГО', formatMoney(cashPaymentReport.total), '', '', `${cashPaymentReport.rowCount} операций`] : undefined,
            { tab: 'cashPayments', disabled: cashPaymentReportLoading },
            cashPaymentReportLoading || cashPaymentReportError ? undefined : 'Операций за период нет',
          )}
          <TablePagination ariaLabel="Пагинация отчета по оплатам из кассы" totalCount={cashPaymentPage.totalCount} offset={cashPaymentPage.offset} limit={cashPaymentPage.limit} visibleCount={cashPaymentPage.items.length} disabled={cashPaymentReportLoading} pageSizeLabel="Количество строк отчета по оплатам из кассы" onPageChange={(page) => setCashPaymentPageRequest((current) => ({ ...current, offset: (page - 1) * current.limit }))} onPageSizeChange={(limit) => setCashPaymentPageRequest({ offset: 0, limit })} />
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'bankDeposits') {
      const bankRows = bankDepositReport?.rows.map((row) => [
        row.date,
        formatMoney(row.amount),
        row.comment || '',
      ]) ?? []
      const bankDepositPage = {
        items: bankDepositReport?.rows ?? [],
        totalCount: bankDepositReport?.rowCount ?? 0,
        offset: bankDepositReport?.offset ?? bankDepositPageRequest.offset,
        limit: bankDepositReport?.limit ?? bankDepositPageRequest.limit,
      }
      return (
        <ReportWorkbookSheet title="Отчёт по сдаче кассы в банк">
          {renderDateFilter('bankDeposits', { from: 'С', to: 'По', actions: <>{renderReportExportButton('xlsx', 'bankDeposits-xlsx', () => void downloadCashOrBankReport('bankDeposits', 'xlsx'))}{renderReportExportButton('pdf', 'bankDeposits-pdf', () => void downloadCashOrBankReport('bankDeposits', 'pdf'))}</> })}
          {bankDepositReportLoading ? <TableLoadingState label="Загружаем сдачу кассы в банк..." /> : null}
          {bankDepositReportError ? <FormError>{bankDepositReportError}</FormError> : null}
          <div className="report-workbook-summary-row report-workbook-summary-row--single"><strong>ИТОГО</strong></div>
          {renderReportTable(
            'Отчет по сдаче кассы в банк',
            [{ label: 'Дата', sortField: 'date' }, { label: 'Сумма', sortField: 'amount' }, { label: 'Комментарий', sortField: 'comment' }],
            bankDepositReportLoading ? [] : bankRows,
            !bankDepositReportLoading && bankDepositReport ? ['ИТОГО', formatMoney(bankDepositReport.total), `${bankDepositReport.rowCount} операций`] : undefined,
            { tab: 'bankDeposits', disabled: bankDepositReportLoading },
            bankDepositReportLoading || bankDepositReportError ? undefined : 'Операций за период нет',
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
      const feeDetailTableName = feeDetailMode === 'debtors' ? 'Должники по сбору' : 'Гаражи по сбору'
      return (
          <ReportWorkbookSheet title="Отчёт по сборам">
          {feeReportLoading ? <TableLoadingState label="Загружаем отчёт по сборам" /> : null}
          {feeReportError ? <FormError>{feeReportError}</FormError> : null}
          <div className="report-workbook-filter report-workbook-filter--single" aria-label="Фильтры отчета по сборам">
            <div className="report-workbook-filter__fields">
              <label className="report-workbook-filter-wide">
                <span>Вариация сбора</span>
                <input aria-label="Вариация сбора" list={feeOptionsId} value={feeVariationFilter} onChange={(event) => setFeeVariationFilter(event.target.value)} placeholder="Название сбора" />
              </label>
            </div>
            <div className="report-workbook-filter__actions" role="group" aria-label="Действия с отчетом">
              {renderReportExportButton('xlsx', 'fees-xlsx', () => void downloadFeeReport('xlsx'))}
              {renderReportExportButton('pdf', 'fees-pdf', () => void downloadFeeReport('pdf'))}
            </div>
          </div>
          <div className="report-workbook-split">
            {renderReportTable(
              'Отчет по сборам',
              ['Наименование', 'Цель', 'Сумма сбора', 'Собрано'],
              summaryRows,
              feeReport ? ['ИТОГО', '', formatMoney(feeReport.accruedTotal), formatMoney(feeReport.collectedTotal)] : undefined,
              undefined,
              feeReportLoading || feeReportError ? undefined : 'Данных по сбору нет',
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
                    [{ label: 'Гараж', sortField: 'garageNumber' }, { label: 'Владелец', sortField: 'ownerName' }, { label: 'Начислено', sortField: 'accrued' }, { label: 'Оплачено', sortField: 'paid' }, { label: 'Дата', sortField: 'lastPaymentDate' }, { label: 'Задолженность', sortField: 'debt' }],
                    feeDetailTableRows,
                    undefined,
                    { tab: 'fees', disabled: feeReportLoading },
                    feeReportLoading || feeReportError ? undefined : feeDetailMode === 'debtors' ? 'Должников нет' : 'Данных по гаражам нет',
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
    const fundPage = {
      items: fundChangeReport?.rows ?? [],
      totalCount: fundChangeReport?.rowCount ?? 0,
      offset: fundChangeReport?.offset ?? fundChangePageRequest.offset,
      limit: fundChangeReport?.limit ?? fundChangePageRequest.limit,
    }

    return (
      <ReportWorkbookSheet title="Отчёт по изменению фондов">
        {renderDateFilter('funds', { from: 'С', to: 'По', actions: <>{renderReportExportButton('xlsx', 'funds-xlsx', () => void downloadFundChangeReport('xlsx'))}{renderReportExportButton('pdf', 'funds-pdf', () => void downloadFundChangeReport('pdf'))}</> })}
        {fundChangeReportLoading ? <TableLoadingState label="Загружаем изменения фондов..." /> : null}
        {fundChangeReportError ? <FormError>{fundChangeReportError}</FormError> : null}
        {fundChangeReport ? (
          <div className="report-workbook-summary-row">
            <strong>Пополнено: {formatMoney(fundChangeReport.depositTotal)}</strong>
            <strong>Изъято: {formatMoney(fundChangeReport.withdrawalTotal)}</strong>
            <strong>Операций: {fundChangeReport.rowCount}</strong>
          </div>
        ) : null}
        {renderReportTable(
          'Отчет по изменению фондов',
          [{ label: 'Фонд', sortField: 'fundName' }, { label: 'Дата', sortField: 'date' }, { label: 'Изменение', sortField: 'changeName' }, { label: 'Сумма', sortField: 'amount' }, { label: 'Сумма до', sortField: 'balanceBefore' }, { label: 'Сумма после', sortField: 'balanceAfter' }, { label: 'Пользователь', sortField: 'actorDisplayName' }, { label: 'Комментарий', sortField: 'reason' }],
          fundChangeReportLoading ? [] : fundRows,
          undefined,
          { tab: 'funds', disabled: fundChangeReportLoading },
          fundChangeReportLoading || fundChangeReportError ? undefined : 'Операций за период нет',
        )}
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
            onClick={() => {
              if (tab.key !== activeReportTab && (tab.key === 'garages' || tab.key === 'payouts' || tab.key === 'income' || tab.key === 'fees')) {
                setReportDataSettled((current) => ({ ...current, [tab.key]: false }))
              }
              setActiveReportTab(tab.key)
            }}
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
