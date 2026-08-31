import { useCallback, useEffect, useId, useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { FileSpreadsheet, FileText, LoaderCircle, Pencil, Search, Trash2, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { DictionaryClient } from '../../services/dictionariesApi'
import type { BankDepositReportDto, CashPaymentReportDto, ConsolidatedReportDto, ExpenseReportDto, FeeReportDto, FundChangeReportDto, GarageDetailReportDto, GarageReportQuickListDto, IncomeReportDto, ReportClient } from '../../services/reportsApi'
import { AsyncErrorState, BackgroundRefreshStatus, EmptyState, TableLoadingState } from '../../shared/AsyncState'
import { scheduleDebouncedRequest } from '../../shared/debouncedRequest'
import { buildReportFileName, buildSnapshotReportFileName, downloadBlob } from '../../shared/fileExports'
import { ForegroundDialogError, FormError } from '../../shared/formFeedback'
import { useCloseOnOutsidePointer, useEscapeKey, useFocusOnOpen, useFocusTrap } from '../../shared/focusHooks'
import { formatCount, formatDateOnly, formatMoney, formatMonth, formatOperationTime, getCurrentMonthInputValue, getLocalDateInputValue } from '../../shared/formatters'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { ReportPeriodQuickSelect } from '../../shared/ReportPeriodQuickSelect'
import { filterAndRankReportOptions } from '../../shared/reportFilters'
import type { RankableReportFilterOption, ReportQuickPeriodRange } from '../../shared/reportFilters'
import { advanceReportSort } from '../../shared/reportSorting'
import type { ReportSort } from '../../shared/reportSorting'
import { SelectControl } from '../../shared/SelectControl'
import { useActionCommentSettings } from '../../shared/ActionCommentSettings'

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

const reportFullViewLimit = 5000
const reportDictionarySearchLimit = 20
const reportGarageBrowseLimit = 100
const reportGarageFilterPanelDefaultSize = { width: 760, height: 480 }

function getReportExportSuccessMessage(extension: 'xlsx' | 'pdf') {
  return `Отчет ${extension.toUpperCase()} готов.`
}

type ReportGarageFilterPanelSize = typeof reportGarageFilterPanelDefaultSize

type ReportFilterOption = RankableReportFilterOption

type ReportColumn = {
  label: string
  sortField?: string
}

type GarageQuickListEditor = {
  id: string | null
  name: string
}

function ReportCheckboxMultiSelect({
  label,
  ariaLabel,
  allLabel,
  placeholder,
  resultsAriaLabel,
  selectedAriaLabel,
  options,
  selectedValues,
  openOnFocus = false,
  loadOptions,
  onChange,
}: {
  label: string
  ariaLabel: string
  allLabel: string
  placeholder: string
  resultsAriaLabel: string
  selectedAriaLabel: string
  options: ReportFilterOption[]
  selectedValues: string[]
  openOnFocus?: boolean
  loadOptions?: (search: string, signal: AbortSignal) => Promise<ReportFilterOption[]>
  onChange: (values: string[]) => void
}) {
  const searchId = useId()
  const listId = useId()
  const statusId = useId()
  const [search, setSearch] = useState('')
  const [searchOpen, setSearchOpen] = useState(false)
  const wrapRef = useCloseOnOutsidePointer<HTMLDivElement>(searchOpen, setSearchOpen)
  const [remoteOptions, setRemoteOptions] = useState<ReportFilterOption[]>([])
  const [remoteLoading, setRemoteLoading] = useState(false)
  const [remoteError, setRemoteError] = useState<string | null>(null)
  const normalizedSearch = search.trim().toLocaleLowerCase('ru-RU')
  const availableOptions = Array.from(new Map([...options, ...remoteOptions].map((option) => [option.value, option])).values())
  const filteredOptions = normalizedSearch
    ? filterAndRankReportOptions(availableOptions, normalizedSearch).slice(0, 20)
    : openOnFocus ? filterAndRankReportOptions(availableOptions, '') : []
  const selectedOptions = selectedValues
    .map((value) => availableOptions.find((option) => option.value === value))
    .filter((option): option is ReportFilterOption => Boolean(option))
  const shouldShowResults = searchOpen && (openOnFocus || normalizedSearch.length > 0)

  useEffect(() => {
    if (!loadOptions || !searchOpen || (!openOnFocus && normalizedSearch.length === 0)) {
      return undefined
    }

    return scheduleDebouncedRequest({
      delay: 250,
      request: (signal) => loadOptions(normalizedSearch, signal),
      onStart: () => {
        setRemoteLoading(true)
        setRemoteError(null)
      },
      onSuccess: (loaded) => {
        setRemoteOptions((current) => Array.from(new Map([...current, ...loaded].map((option) => [option.value, option])).values()))
        setRemoteLoading(false)
      },
      onError: (error) => {
        setRemoteError(error instanceof Error ? error.message : 'Не удалось выполнить поиск.')
        setRemoteLoading(false)
      },
    })
  }, [loadOptions, normalizedSearch, openOnFocus, searchOpen])

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
    <div className="report-workbook-filter-wide report-checkbox-picker">
      <label htmlFor={searchId}>{label}</label>
      <div ref={wrapRef} className="payments-prototype-search-wrap">
        <label className="payments-prototype-search">
          <Search size={18} aria-hidden="true" />
          <input
            id={searchId}
            role="combobox"
            aria-label={ariaLabel}
            aria-expanded={shouldShowResults}
            aria-controls={listId}
            aria-describedby={statusId}
            placeholder={placeholder}
            value={search}
            onFocus={() => setSearchOpen(openOnFocus || search.trim().length > 0)}
            onChange={(event) => {
              setSearch(event.target.value)
              setSearchOpen(openOnFocus || event.target.value.trim().length > 0)
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
          <div className="payments-prototype-search-results" id={listId} role="listbox" aria-label={resultsAriaLabel}>
            {filteredOptions.length > 0 ? filteredOptions.map((option) => (
              <label className="payments-prototype-search-option" key={option.value} role="option" aria-selected={selectedValues.includes(option.value)}>
                <input
                  type="checkbox"
                  aria-label={`Выбрать ${option.label.toLocaleLowerCase('ru-RU')}${option.description ? `, ${option.description.toLocaleLowerCase('ru-RU')}` : ''}`}
                  checked={selectedValues.includes(option.value)}
                  onChange={() => toggleSelection(option.value)}
                />
                <span>
                  <strong>{option.label}</strong>
                  {option.description ? <small>{option.description}</small> : null}
                </span>
              </label>
            )) : remoteLoading ? null : <span className="payments-prototype-search-empty">Ничего не найдено</span>}
          </div>
        ) : null}
      </div>
      {selectedValues.length === 0 ? (
        <span className="report-workbook-multi-select-status" id={statusId} role="status" aria-live="polite">{allLabel}</span>
      ) : null}
      {remoteLoading ? <span className="report-filter-search-status" role="status">Ищем…</span> : null}
      {remoteError ? <ForegroundDialogError><span className="report-filter-search-error" role="alert">{remoteError}</span></ForegroundDialogError> : null}
      {selectedOptions.length > 0 ? (
        <div className="payments-prototype-selected-garages report-checkbox-picker-selected" aria-label={selectedAriaLabel}>
          <div className="payments-prototype-selected-heading">
            <span id={statusId} role="status" aria-live="polite">Выбрано: {selectedOptions.length}</span>
            <button className="ghost-button" type="button" onClick={() => onChange([])}>Очистить</button>
          </div>
          <div className="payments-prototype-selected-list">
            {selectedOptions.map((option) => (
              <div className="report-checkbox-picker-selected-item" key={option.value}>
                <span>
                  <strong>{option.label}</strong>
                  {option.description ? <small>{option.description}</small> : null}
                </span>
                <button
                  className="icon-button payments-prototype-selected-remove"
                  type="button"
                  aria-label={`Убрать ${option.label.toLocaleLowerCase('ru-RU')} из выбранных`}
                  title="Убрать из выбранных"
                  onClick={() => toggleSelection(option.value)}
                >
                  <X size={14} aria-hidden="true" />
                </button>
              </div>
            ))}
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

function getReportView<T extends object>(report: T | null, loading: boolean, error: string | null, reportQueries: WeakMap<object, string>, currentQuery: string) {
  const data = report && reportQueries.get(report) === currentQuery ? report : null
  return [data, !data && !error, loading && !!data] as const
}

function renderReportLoadingState(primaryLoading: boolean, refreshing: boolean) {
  if (refreshing) {
    return <BackgroundRefreshStatus label="Обновляем отчёт" />
  }

  return primaryLoading ? <TableLoadingState label="Загружаем отчёт" /> : null
}

export function ReportPanel({ auth, dictionaryClient, reportClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient; reportClient: ReportClient }) {
  const [actionCommentsRequired] = useActionCommentSettings()
  const today = getLocalDateInputValue()
  const currentMonth = getCurrentMonthInputValue(today)
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
  const garageFilterPanelStorageKey = `garagebalance.reports.garageFilterPanelSize.${auth.user.id}`
  const [garageFilterPanelSize] = useState<ReportGarageFilterPanelSize>(() => {
    try {
      return { ...reportGarageFilterPanelDefaultSize, ...JSON.parse(window.localStorage.getItem(garageFilterPanelStorageKey) ?? '{}') }
    } catch {
      return reportGarageFilterPanelDefaultSize
    }
  })
  const [garageQuickLists, setGarageQuickLists] = useState<GarageReportQuickListDto[]>([])
  const [selectedGarageQuickListId, setSelectedGarageQuickListId] = useState('')
  const [garageQuickListsLoading, setGarageQuickListsLoading] = useState(false)
  const [garageQuickListError, setGarageQuickListError] = useState<string | null>(null)
  const [garageQuickListMessage, setGarageQuickListMessage] = useState<string | null>(null)
  const [garageQuickListEditor, setGarageQuickListEditor] = useState<GarageQuickListEditor | null>(null)
  const [garageQuickListDeleteTarget, setGarageQuickListDeleteTarget] = useState<GarageReportQuickListDto | null>(null)
  const [garageQuickListDeleteReason, setGarageQuickListDeleteReason] = useState('')
  const [garageQuickListSaving, setGarageQuickListSaving] = useState(false)
  const [selectedCounterpartyKeys, setSelectedCounterpartyKeys] = useState<string[]>([])
  const [selectedIncomeGarageIds, setSelectedIncomeGarageIds] = useState<string[]>([])
  const [selectedFeeEntryIds, setSelectedFeeEntryIds] = useState<string[]>([])
  const [feeFilterOptions, setFeeFilterOptions] = useState<ReportFilterOption[]>([])
  const [garageFilterOptions, setGarageFilterOptions] = useState<ReportFilterOption[]>([])
  const [counterpartyFilterOptions, setCounterpartyFilterOptions] = useState<ReportFilterOption[]>([])
  const [consolidatedReport, setConsolidatedReport] = useState<ConsolidatedReportDto | null>(null)
  const [consolidatedReportLoading, setConsolidatedReportLoading] = useState(true)
  const [consolidatedReportError, setConsolidatedReportError] = useState<string | null>(null)
  const [garageReport, setGarageReport] = useState<GarageDetailReportDto | null>(null)
  const [garageReportLoading, setGarageReportLoading] = useState(false)
  const [garageReportError, setGarageReportError] = useState<string | null>(null)
  const [payoutReport, setPayoutReport] = useState<ExpenseReportDto | null>(null)
  const [payoutReportLoading, setPayoutReportLoading] = useState(false)
  const [payoutReportError, setPayoutReportError] = useState<string | null>(null)
  const [incomeReport, setIncomeReport] = useState<IncomeReportDto | null>(null)
  const [incomeReportLoading, setIncomeReportLoading] = useState(false)
  const [incomeReportError, setIncomeReportError] = useState<string | null>(null)
  const [cashPaymentReport, setCashPaymentReport] = useState<CashPaymentReportDto | null>(null)
  const [cashPaymentReportLoading, setCashPaymentReportLoading] = useState(false)
  const [cashPaymentReportError, setCashPaymentReportError] = useState<string | null>(null)
  const [bankDepositReport, setBankDepositReport] = useState<BankDepositReportDto | null>(null)
  const [bankDepositReportLoading, setBankDepositReportLoading] = useState(false)
  const [bankDepositReportError, setBankDepositReportError] = useState<string | null>(null)
  const [feeReport, setFeeReport] = useState<FeeReportDto | null>(null)
  const [feeReportLoading, setFeeReportLoading] = useState(false)
  const [feeReportError, setFeeReportError] = useState<string | null>(null)
  const [feeDebtorsVisible, setFeeDebtorsVisible] = useState(false)
  const [feeDetailMode, setFeeDetailMode] = useState<'debtors' | 'all'>('debtors')
  const [garageAccrualsGrouped, setGarageAccrualsGrouped] = useState(false)
  const [incomePaymentsGrouped, setIncomePaymentsGrouped] = useState(true)
  const [reportDataError, setReportDataError] = useState<string | null>(null)
  const [reportExporting, setReportExporting] = useState<string | null>(null)
  const [reportExportMessage, setReportExportMessage] = useState<string | null>(null)
  const [fundChangeReport, setFundChangeReport] = useState<FundChangeReportDto | null>(null)
  const [fundChangeReportLoading, setFundChangeReportLoading] = useState(false)
  const [fundChangeReportError, setFundChangeReportError] = useState<string | null>(null)
  const [reportReloadRevision, setReportReloadRevision] = useState(0)
  const [reportQueries] = useState(() => new WeakMap<object, string>())
  const garageQuickListDialogRef = useFocusTrap<HTMLElement>(garageQuickListEditor !== null)
  const garageQuickListNameRef = useFocusOnOpen<HTMLInputElement>(garageQuickListEditor !== null)
  const garageQuickListDeleteDialogRef = useFocusTrap<HTMLElement>(garageQuickListDeleteTarget !== null)
  const garageQuickListDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(garageQuickListDeleteTarget !== null)
  useEscapeKey(garageQuickListEditor !== null && !garageQuickListSaving, () => setGarageQuickListEditor(null))
  useEscapeKey(garageQuickListDeleteTarget !== null && !garageQuickListSaving, () => setGarageQuickListDeleteTarget(null))

  const activeReportIndex = reportWorkbookTabs.findIndex((tab) => tab.key === activeReportTab)
  const reportQueryCriteria = [
    [monthlyFilters.consolidated, reportSorts.consolidated],
    [monthlyFilters.garages, selectedGarageIds, garageAccrualsGrouped, reportSorts.garages],
    [monthlyFilters.payouts, selectedCounterpartyKeys, reportSorts.payouts],
    [dateFilters.income, selectedIncomeGarageIds, incomePaymentsGrouped, reportSorts.income],
    [dateFilters.cashPayments, reportSorts.cashPayments],
    [dateFilters.bankDeposits, reportSorts.bankDeposits],
    [selectedFeeEntryIds, reportSorts.fees],
    [dateFilters.funds, reportSorts.funds],
  ][activeReportIndex]
  const currentReportQuery = JSON.stringify([auth.accessToken, reportQueryCriteria])

  const loadGarageFilterOptions = useCallback(async (search: string, signal: AbortSignal) => {
    const resultLimit = search ? reportDictionarySearchLimit : reportGarageBrowseLimit
    const garages = dictionaryClient.getGaragesPage
      ? (await dictionaryClient.getGaragesPage(auth.accessToken, search || undefined, 0, resultLimit, false, 'number', 'asc', false, {}, signal)).items
      : await dictionaryClient.getGarages(auth.accessToken, search || undefined, resultLimit, false, signal)
    const options = garages
      .filter((garage) => !garage.isArchived)
      .map((garage) => ({ value: garage.id, label: `Гараж ${garage.number}`, description: garage.ownerName ?? 'Без владельца', rankingValue: garage.number }))
    if (!signal.aborted) {
      setGarageFilterOptions((current) => Array.from(new Map([...current, ...options].map((option) => [option.value, option])).values()))
    }
    return options
  }, [auth.accessToken, dictionaryClient])

  const loadCounterpartyFilterOptions = useCallback(async (search: string, signal: AbortSignal) => {
    const [suppliers, staffMembers] = await Promise.all([
      dictionaryClient.getSuppliersPage
        ? dictionaryClient.getSuppliersPage(auth.accessToken, undefined, search || undefined, 0, reportDictionarySearchLimit, false, 'name', 'asc', signal).then((page) => page.items)
        : dictionaryClient.getSuppliers(auth.accessToken, undefined, search || undefined, reportDictionarySearchLimit, false, signal),
      dictionaryClient.getStaffMembersPage
        ? dictionaryClient.getStaffMembersPage(auth.accessToken, undefined, search || undefined, 0, reportDictionarySearchLimit, false, 'fullName', 'asc', signal).then((page) => page.items)
        : dictionaryClient.getStaffMembers(auth.accessToken, undefined, search || undefined, reportDictionarySearchLimit, false, signal),
    ])
    const options = [
      ...suppliers.filter((supplier) => !supplier.isArchived).map((supplier) => ({ value: `supplier:${supplier.id}`, label: supplier.name, description: 'Поставщик' })),
      ...staffMembers.filter((member) => !member.isArchived).map((member) => ({ value: `staff:${member.id}`, label: member.fullName, description: 'Сотрудник' })),
    ]
    if (!signal.aborted) {
      setCounterpartyFilterOptions((current) => Array.from(new Map([...current, ...options].map((option) => [option.value, option])).values()))
    }
    return options
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => {
    if (activeReportTab !== 'garages') {
      return undefined
    }

    return scheduleDebouncedRequest({
      delay: 0,
      request: (signal) => reportClient.getGarageReportQuickLists(auth.accessToken, signal),
      onStart: () => {
        setGarageQuickListsLoading(true)
        setGarageQuickListError(null)
      },
      onSuccess: (items) => {
        setGarageQuickLists(items)
        setGarageQuickListsLoading(false)
      },
      onError: (error) => {
        setGarageQuickListError(error instanceof Error ? error.message : 'Не удалось загрузить быстрые списки гаражей.')
        setGarageQuickListsLoading(false)
      },
    })
  }, [activeReportTab, auth.accessToken, reportClient])

  useEffect(() => {
    if (activeReportTab !== 'consolidated') {
      return
    }

    let ignore = false
    const controller = new AbortController()
    const queryKey = currentReportQuery

    async function loadConsolidatedReport() {
      setConsolidatedReportLoading(true)
      setConsolidatedReportError(null)
      try {
        const consolidatedFilter = monthlyFilters.consolidated
        const monthFrom = getReportMonthStart(consolidatedFilter.monthFrom)
        const monthTo = getReportMonthStart(consolidatedFilter.monthTo)
        const sort = reportSorts.consolidated
        const loadedConsolidated = await reportClient.getConsolidatedReport(auth.accessToken, {
          monthFrom,
          monthTo,
          limit: 1,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
        }, controller.signal)

        if (ignore) {
          return
        }

        reportQueries.set(loadedConsolidated, queryKey)
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
      controller.abort()
    }
  }, [activeReportTab, auth.accessToken, currentReportQuery, monthlyFilters.consolidated, reportClient, reportQueries, reportReloadRevision, reportSorts.consolidated])

  useEffect(() => {
    if (activeReportTab !== 'fees') {
      return
    }

    let ignore = false
    const controller = new AbortController()
    const queryKey = currentReportQuery
    async function loadFeeReport() {
      setFeeReportLoading(true)
      setFeeReportError(null)
      try {
        const sort = reportSorts.fees
        const report = await reportClient.getFeeReport(auth.accessToken, {
          feeEntryIds: selectedFeeEntryIds.length > 0 ? selectedFeeEntryIds : undefined,
          offset: 0,
          limit: reportFullViewLimit,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
        }, controller.signal)
        if (!ignore) {
          reportQueries.set(report, queryKey)
          setFeeReport(report)
          setFeeFilterOptions((current) => Array.from(new Map([
            ...current,
            ...report.summaryRows.map((row) => ({ value: row.incomeTypeId, label: row.name, description: row.goal })),
          ].map((option) => [option.value, option])).values()))
        }
      } catch (caught) {
        if (!ignore) {
          setFeeReportError(caught instanceof Error ? caught.message : 'Не удалось загрузить отчет по сборам.')
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
      controller.abort()
    }
  }, [activeReportTab, auth.accessToken, currentReportQuery, reportClient, reportQueries, reportReloadRevision, reportSorts.fees, selectedFeeEntryIds])

  useEffect(() => {
    if (activeReportTab !== 'garages') {
      return
    }

    let ignore = false
    const controller = new AbortController()
    const queryKey = currentReportQuery

    async function loadGarageReport() {
      setGarageReportLoading(true)
      setGarageReportError(null)
      try {
        const filter = monthlyFilters.garages
        const sort = reportSorts.garages
        const report = await reportClient.getGarageReport(auth.accessToken, {
          monthFrom: getReportMonthStart(filter.monthFrom),
          monthTo: getReportMonthStart(filter.monthTo),
          garageIds: selectedGarageIds,
          groupAccruals: garageAccrualsGrouped,
          offset: 0,
          limit: reportFullViewLimit,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
        }, controller.signal)
        if (!ignore) {
          reportQueries.set(report, queryKey)
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
      controller.abort()
    }
  }, [activeReportTab, auth.accessToken, currentReportQuery, garageAccrualsGrouped, monthlyFilters.garages, reportClient, reportQueries, reportReloadRevision, reportSorts.garages, selectedGarageIds])

  useEffect(() => {
    if (activeReportTab !== 'payouts') {
      return
    }

    let ignore = false
    const controller = new AbortController()
    const queryKey = currentReportQuery

    async function loadPayoutReport() {
      setPayoutReportLoading(true)
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
          offset: 0,
          limit: reportFullViewLimit,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
        }, controller.signal)
        if (!ignore) {
          reportQueries.set(report, queryKey)
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
      controller.abort()
    }
  }, [activeReportTab, auth.accessToken, currentReportQuery, monthlyFilters.payouts, reportClient, reportQueries, reportReloadRevision, reportSorts.payouts, selectedCounterpartyKeys])

  useEffect(() => {
    if (activeReportTab !== 'income') {
      return
    }

    let ignore = false
    const controller = new AbortController()
    const queryKey = currentReportQuery

    async function loadIncomeReport() {
      setIncomeReportLoading(true)
      setIncomeReportError(null)
      try {
        const filter = dateFilters.income
        const sort = reportSorts.income
        const report = await reportClient.getIncomeReport(auth.accessToken, {
          dateFrom: filter.dateFrom,
          dateTo: filter.dateTo,
          garageIds: selectedIncomeGarageIds,
          rowMode: 'payments',
          groupPayments: incomePaymentsGrouped,
          offset: 0,
          limit: reportFullViewLimit,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
        }, controller.signal)
        if (!ignore) {
          reportQueries.set(report, queryKey)
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
      controller.abort()
    }
  }, [activeReportTab, auth.accessToken, currentReportQuery, dateFilters.income, incomePaymentsGrouped, reportClient, reportQueries, reportReloadRevision, reportSorts.income, selectedIncomeGarageIds])

  useEffect(() => {
    if (activeReportTab !== 'cashPayments') {
      return
    }

    let ignore = false
    const controller = new AbortController()
    const queryKey = currentReportQuery

    async function loadCashPayments() {
      setCashPaymentReportLoading(true)
      setCashPaymentReportError(null)
      try {
        const filter = dateFilters.cashPayments
        const sort = reportSorts.cashPayments
        const report = await reportClient.getCashPaymentReport(auth.accessToken, {
          dateFrom: filter.dateFrom,
          dateTo: filter.dateTo,
          offset: 0,
          limit: reportFullViewLimit,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
        }, controller.signal)
        if (!ignore) {
          reportQueries.set(report, queryKey)
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
      controller.abort()
    }
  }, [activeReportTab, auth.accessToken, currentReportQuery, dateFilters.cashPayments, reportClient, reportQueries, reportReloadRevision, reportSorts.cashPayments])

  useEffect(() => {
    if (activeReportTab !== 'bankDeposits') {
      return
    }

    let ignore = false
    const controller = new AbortController()
    const queryKey = currentReportQuery

    async function loadBankDeposits() {
      setBankDepositReportLoading(true)
      setBankDepositReportError(null)
      try {
        const filter = dateFilters.bankDeposits
        const sort = reportSorts.bankDeposits
        const report = await reportClient.getBankDepositReport(auth.accessToken, {
          dateFrom: filter.dateFrom,
          dateTo: filter.dateTo,
          offset: 0,
          limit: reportFullViewLimit,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
        }, controller.signal)
        if (!ignore) {
          reportQueries.set(report, queryKey)
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
      controller.abort()
    }
  }, [activeReportTab, auth.accessToken, currentReportQuery, dateFilters.bankDeposits, reportClient, reportQueries, reportReloadRevision, reportSorts.bankDeposits])

  useEffect(() => {
    if (activeReportTab !== 'funds') {
      return
    }

    let ignore = false
    const controller = new AbortController()
    const queryKey = currentReportQuery

    async function loadFundChanges() {
      setFundChangeReportLoading(true)
      setFundChangeReportError(null)
      try {
        const filter = dateFilters.funds
        const sort = reportSorts.funds
        const report = await reportClient.getFundChangeReport(auth.accessToken, {
          dateFrom: filter.dateFrom,
          dateTo: filter.dateTo,
          offset: 0,
          limit: reportFullViewLimit,
          sortBy: sort?.field,
          sortDirection: sort?.direction,
        }, controller.signal)
        if (!ignore) {
          reportQueries.set(report, queryKey)
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
      controller.abort()
    }
  }, [activeReportTab, auth.accessToken, currentReportQuery, dateFilters.funds, reportClient, reportQueries, reportReloadRevision, reportSorts.funds])

  const selectedTab = reportWorkbookTabs[activeReportIndex]
  const feeVariationLabel = selectedFeeEntryIds.length === 0 ? 'Все сборы' : `Выбрано сборов: ${selectedFeeEntryIds.length}`

  function updateMonthlyFilter(key: ReportMonthlyFilterKey, field: keyof ReportMonthRange, value: string) {
    setMonthlyFilters((current) => ({
      ...current,
      [key]: {
        ...current[key],
        [field]: value,
      },
    }))
  }

  function updateDateFilter(key: ReportDateFilterKey, field: keyof ReportDateRange, value: string) {
    setDateFilters((current) => ({
      ...current,
      [key]: {
        ...current[key],
        [field]: value,
      },
    }))
  }

  function applyMonthlyQuickPeriod(key: ReportMonthlyFilterKey, range: ReportQuickPeriodRange) {
    setMonthlyFilters((current) => ({
      ...current,
      [key]: { monthFrom: range.monthFrom, monthTo: range.monthTo },
    }))
  }

  function applyDateQuickPeriod(key: ReportDateFilterKey, range: ReportQuickPeriodRange) {
    setDateFilters((current) => ({
      ...current,
      [key]: { dateFrom: range.dateFrom, dateTo: range.dateTo },
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
      setReportExportMessage(getReportExportSuccessMessage(extension))
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
      setReportExportMessage(getReportExportSuccessMessage(extension))
    } catch (caught) {
      setReportDataError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчет.')
    } finally {
      setReportExporting(null)
    }
  }

  function applyGarageQuickList(id: string) {
    setGarageQuickListError(null)
    setGarageQuickListMessage(null)
    setSelectedGarageQuickListId(id)
    if (!id) {
      setSelectedGarageIds([])
      return
    }

    const quickList = garageQuickLists.find((item) => item.id === id)
    if (!quickList) {
      setSelectedGarageQuickListId('')
      setSelectedGarageIds([])
      setGarageQuickListError('Выбранный быстрый список больше не существует.')
      return
    }

    const activeGarageIds = quickList.garages.filter((garage) => !garage.isArchived).map((garage) => garage.garageId)
    setGarageFilterOptions((current) => Array.from(new Map([
      ...current,
      ...quickList.garages.filter((garage) => !garage.isArchived).map((garage) => ({
        value: garage.garageId,
        label: `Гараж ${garage.garageNumber}`,
        description: garage.ownerName ?? 'Без владельца',
        rankingValue: garage.garageNumber,
      })),
    ].map((option) => [option.value, option])).values()))
    setSelectedGarageIds(activeGarageIds)
    if (activeGarageIds.length !== quickList.garages.length) {
      setGarageQuickListMessage('Удалённые гаражи пропущены при применении списка.')
    }
  }

  function openGarageQuickListCreate() {
    setGarageQuickListError(null)
    setGarageQuickListMessage(null)
    setGarageQuickListEditor({ id: null, name: '' })
  }

  function openGarageQuickListEdit() {
    const quickList = garageQuickLists.find((item) => item.id === selectedGarageQuickListId)
    if (!quickList) {
      return
    }

    setGarageQuickListError(null)
    setGarageQuickListMessage(null)
    setGarageQuickListEditor({ id: quickList.id, name: quickList.name })
  }

  async function saveGarageQuickList() {
    if (!garageQuickListEditor) {
      return
    }

    const name = garageQuickListEditor.name.trim()
    if (!name) {
      setGarageQuickListError('Укажите название быстрого списка.')
      return
    }
    if (selectedGarageIds.length === 0) {
      setGarageQuickListError('Выберите хотя бы один гараж.')
      return
    }

    setGarageQuickListSaving(true)
    setGarageQuickListError(null)
    setGarageQuickListMessage(null)
    try {
      const request = { name, garageIds: selectedGarageIds }
      const saved = garageQuickListEditor.id
        ? await reportClient.updateGarageReportQuickList(auth.accessToken, garageQuickListEditor.id, request)
        : await reportClient.createGarageReportQuickList(auth.accessToken, request)
      setGarageQuickLists((current) => [...current.filter((item) => item.id !== saved.id), saved]
        .sort((left, right) => left.name.localeCompare(right.name, 'ru-RU')))
      setSelectedGarageQuickListId(saved.id)
      setGarageQuickListEditor(null)
      setGarageQuickListMessage(garageQuickListEditor.id ? 'Быстрый список обновлён.' : 'Быстрый список создан.')
    } catch (error) {
      setGarageQuickListError(error instanceof Error ? error.message : 'Не удалось сохранить быстрый список.')
    } finally {
      setGarageQuickListSaving(false)
    }
  }

  async function deleteGarageQuickList() {
    if (!garageQuickListDeleteTarget) {
      return
    }
    const reason = garageQuickListDeleteReason.trim()
    if (actionCommentsRequired && !reason) {
      setGarageQuickListError('Укажите причину удаления быстрого списка.')
      return
    }

    setGarageQuickListSaving(true)
    setGarageQuickListError(null)
    setGarageQuickListMessage(null)
    try {
      await reportClient.deleteGarageReportQuickList(auth.accessToken, garageQuickListDeleteTarget.id, reason)
      setGarageQuickLists((current) => current.filter((item) => item.id !== garageQuickListDeleteTarget.id))
      if (selectedGarageQuickListId === garageQuickListDeleteTarget.id) {
        setSelectedGarageQuickListId('')
        setSelectedGarageIds([])
      }
      setGarageQuickListDeleteTarget(null)
      setGarageQuickListDeleteReason('')
      setGarageQuickListMessage('Быстрый список удалён.')
    } catch (error) {
      setGarageQuickListError(error instanceof Error ? error.message : 'Не удалось удалить быстрый список.')
    } finally {
      setGarageQuickListSaving(false)
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
      setReportExportMessage(getReportExportSuccessMessage(extension))
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
      rowMode: 'payments',
      groupPayments: incomePaymentsGrouped,
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
      setReportExportMessage(getReportExportSuccessMessage(extension))
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
      setReportExportMessage(getReportExportSuccessMessage(extension))
    } catch (caught) {
      setReportDataError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчет.')
    } finally {
      setReportExporting(null)
    }
  }

  async function downloadFeeReport(extension: 'xlsx' | 'pdf') {
    const exportKey = `fees-${extension}`
    const params = { feeEntryIds: selectedFeeEntryIds.length > 0 ? selectedFeeEntryIds : undefined, sortBy: reportSorts.fees?.field, sortDirection: reportSorts.fees?.direction }
    setReportExporting(exportKey)
    setReportExportMessage(null)
    setReportDataError(null)
    try {
      const blob = extension === 'xlsx'
        ? await reportClient.exportFeeReportXlsx(auth.accessToken, params)
        : await reportClient.exportFeeReportPdf(auth.accessToken, params)
      downloadBlob(blob, buildSnapshotReportFileName('fees', extension))
      setReportExportMessage(getReportExportSuccessMessage(extension))
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
      setReportExportMessage(getReportExportSuccessMessage(extension))
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
      <button className={`secondary-button report-export-button report-export-button--${extension}`} type="button" aria-label={label} aria-busy={isExporting} title={label} disabled={reportExporting !== null} onClick={onClick}>
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
          <ReportPeriodQuickSelect
            mode="month"
            valueFrom={filter.monthFrom}
            valueTo={filter.monthTo}
            referenceDate={today}
            onSelect={(range) => applyMonthlyQuickPeriod(key, range)}
          />
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
          <ReportPeriodQuickSelect
            mode="date"
            valueFrom={filter.dateFrom}
            valueTo={filter.dateTo}
            referenceDate={today}
            onSelect={(range) => applyDateQuickPeriod(key, range)}
          />
        </div>
        {labels.actions ? <div className="report-workbook-filter__actions" role="group" aria-label="Действия с отчетом">{labels.actions}</div> : null}
        {labels.extra ? <div className="report-workbook-filter__extra">{labels.extra}</div> : null}
      </div>
    )
  }

  function updateReportSort(tab: ReportWorkbookTab, field: string) {
    setReportSorts((current) => ({ ...current, [tab]: advanceReportSort(current[tab], field) ?? undefined }))
  }

  function clearReportSort(tab: ReportWorkbookTab) {
    setReportSorts((current) => ({ ...current, [tab]: undefined }))
  }

  function renderReportTable(
    ariaLabel: string,
    columns: Array<string | ReportColumn>,
    rows: Array<Array<ReactNode>>,
    footer?: Array<ReactNode>,
    sortOptions?: { tab: ReportWorkbookTab; disabled?: boolean; totalCount?: number },
    emptyMessage?: string,
  ) {
    const normalizedColumns = columns.map((column) => typeof column === 'string' ? { label: column } : column)
    const sort = sortOptions ? reportSorts[sortOptions.tab] : undefined
    const sortLabel = normalizedColumns.find((column) => column.sortField === sort?.field)?.label
    return (
      <>
        {sortOptions?.totalCount !== undefined && sortOptions.totalCount > rows.length ? (
          <p className="report-workbook-limit-notice" role="status">
            Показаны первые {rows.length} из {sortOptions.totalCount} строк. Уточните период или фильтр, чтобы увидеть весь результат без перегрузки страницы.
          </p>
        ) : null}
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
      const [report, primaryLoading, refreshing] = getReportView(consolidatedReport, consolidatedReportLoading, consolidatedReportError, reportQueries, currentReportQuery)
      const reportRows = (report?.monthlyRows ?? []).flatMap((month) => {
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
          {renderReportLoadingState(primaryLoading, refreshing)}
          {consolidatedReportError ? <AsyncErrorState message={consolidatedReportError} onRetry={() => setReportReloadRevision((value) => value + 1)} retrying={consolidatedReportLoading} /> : null}
          {renderReportTable(
            'Консолидированный отчет',
            [{ label: 'Месяц', sortField: 'accountingMonth' }, 'Наименование поступления', 'Поступления', 'Наименование выплаты', 'Выплаты', 'Разница', 'Остаток по счёту — На начало месяца', 'Остаток по счёту — На конец месяца'],
            reportRows,
            undefined,
            { tab: 'consolidated', disabled: consolidatedReportLoading },
            primaryLoading || consolidatedReportError ? undefined : 'Данных за период нет',
          )}
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'garages') {
      const [report, primaryLoading, refreshing] = getReportView(garageReport, garageReportLoading, garageReportError, reportQueries, currentReportQuery)
      const garageReportColumns: ReportColumn[] = garageAccrualsGrouped
        ? [{ label: 'Месяц', sortField: 'accountingMonth' }, { label: 'Гараж', sortField: 'garageNumber' }, { label: 'Начисления', sortField: 'accrualAmount' }, { label: 'Поступления', sortField: 'incomeAmount' }, { label: 'Разница', sortField: 'difference' }]
        : [{ label: 'Месяц', sortField: 'accountingMonth' }, { label: 'Гараж', sortField: 'garageNumber' }, { label: 'Услуга', sortField: 'incomeTypeName' }, { label: 'Начисления', sortField: 'accrualAmount' }, { label: 'Поступления', sortField: 'incomeAmount' }, { label: 'Разница', sortField: 'difference' }]
      const reportRows = report?.rows.map((row) => garageAccrualsGrouped
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
      const garageReportFooter = report
        ? garageAccrualsGrouped
          ? ['ИТОГО', '', formatMoney(report.accrualTotal), formatMoney(report.incomeTotal), formatMoney(report.difference)]
          : ['ИТОГО', '', '', formatMoney(report.accrualTotal), formatMoney(report.incomeTotal), formatMoney(report.difference)]
        : undefined
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
              <details className="report-garage-filter-disclosure">
                <summary
                  className="ghost-button report-garage-filter-toggle"
                  role="button"
                  aria-controls="garage-report-personal-filters"
                >
                  Гаражи и личные фильтры
                </summary>
                <div
                  id="garage-report-personal-filters"
                  className="localized-date-picker__popover report-garage-filter-panel"
                  role="region"
                  aria-label="Гаражи и личные фильтры отчёта"
                  onPointerUp={({ currentTarget }) => {
                    try {
                      window.localStorage.setItem(garageFilterPanelStorageKey, JSON.stringify({ width: currentTarget.offsetWidth, height: currentTarget.offsetHeight }))
                    } catch { /* Локальная настройка не влияет на отчёт. */ }
                  }}
                  style={{
                    '--report-garage-filter-panel-width': `${garageFilterPanelSize.width}px`,
                    '--report-garage-filter-panel-height': `${garageFilterPanelSize.height}px`,
                  } as CSSProperties}
                >
                    <div className="report-garage-quick-lists" aria-label="Быстрые списки гаражей">
                      <label>
                        <span>Личный список</span>
                        <SelectControl
                          aria-label="Быстрый список гаражей"
                          value={selectedGarageQuickListId}
                          options={[
                            { value: '', label: 'Все гаражи' },
                            ...garageQuickLists.map((item) => ({
                              value: item.id,
                              label: `${item.name} (${item.garages.filter((garage) => !garage.isArchived).length})`,
                            })),
                          ]}
                          disabled={garageQuickListsLoading}
                          maxVisibleOptions={8}
                          onChange={applyGarageQuickList}
                        />
                      </label>
                      <div className="report-garage-quick-list-actions" role="group" aria-label="Действия с быстрыми списками гаражей">
                        <button
                          className="secondary-button"
                          type="button"
                          aria-pressed={selectedGarageIds.length === 0}
                          onClick={() => applyGarageQuickList('')}
                        >
                          Все
                        </button>
                        <button
                          className="secondary-button create-action-button"
                          type="button"
                          disabled={selectedGarageIds.length === 0 || garageQuickListsLoading}
                          title={selectedGarageIds.length === 0 ? 'Сначала выберите гаражи' : undefined}
                          onClick={openGarageQuickListCreate}
                        >
                          <FileSpreadsheet size={16} aria-hidden="true" />
                          Создать список
                        </button>
                        {selectedGarageQuickListId ? (
                          <>
                            <button className="ghost-button" type="button" onClick={openGarageQuickListEdit}>
                              <Pencil size={15} aria-hidden="true" />
                              Изменить
                            </button>
                            <button
                              className="ghost-button danger-quiet-button"
                              type="button"
                              onClick={() => {
                                setGarageQuickListDeleteReason('')
                                setGarageQuickListError(null)
                                setGarageQuickListDeleteTarget(garageQuickLists.find((item) => item.id === selectedGarageQuickListId) ?? null)
                              }}
                            >
                              <Trash2 size={15} aria-hidden="true" />
                              Удалить
                            </button>
                          </>
                        ) : null}
                      </div>
                      {garageQuickListMessage ? <p className="form-success" role="status">{garageQuickListMessage}</p> : null}
                      {garageQuickListError
                        && garageQuickListError !== garageReportError
                        && !garageQuickListEditor
                        && !garageQuickListDeleteTarget
                        ? <FormError>{garageQuickListError}</FormError>
                        : null}
                    </div>
                    <ReportCheckboxMultiSelect
                      key="garage-report-filter"
                      label="Гаражи"
                      ariaLabel="Гаражи"
                      allLabel="Все гаражи"
                      placeholder="Выберите гаражи или начните вводить номер либо ФИО"
                      resultsAriaLabel="Найденные гаражи отчёта"
                      selectedAriaLabel="Выбранные гаражи отчёта"
                      options={garageFilterOptions}
                      loadOptions={loadGarageFilterOptions}
                      selectedValues={selectedGarageIds}
                      openOnFocus
                      onChange={(values) => {
                        setSelectedGarageQuickListId('')
                        setGarageQuickListMessage(null)
                        setGarageQuickListError(null)
                        setSelectedGarageIds(values)
                      }}
                    />
                </div>
              </details>
            ),
          })}
          <p className="report-workbook-comment" role="note">
            Начисления и поступления сопоставлены по месяцу, гаражу и услуге. Разница = начисления − поступления. Группировка объединяет услуги в одну строку по гаражу и месяцу.
          </p>
          {renderReportLoadingState(primaryLoading, refreshing)}
          {garageReportError ? <AsyncErrorState message={garageReportError} onRetry={() => setReportReloadRevision((value) => value + 1)} retrying={garageReportLoading} /> : null}
          <div className="report-workbook-summary-row">
            <span><strong>ИТОГО начислений</strong><b>{report ? formatMoney(report.accrualTotal) : '—'}</b></span>
            <span><strong>ИТОГО поступлений</strong><b>{report ? formatMoney(report.incomeTotal) : '—'}</b></span>
            <span><strong>Разница</strong><b>{report ? formatMoney(report.difference) : '—'}</b></span>
          </div>
          {renderReportTable(
            'Отчет по гаражам',
            garageReportColumns,
            reportRows,
            garageReportFooter,
            { tab: 'garages', disabled: garageReportLoading, totalCount: report?.rowCount },
            primaryLoading || garageReportError ? undefined : 'Данных за период нет',
          )}
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'payouts') {
      const [report, primaryLoading, refreshing] = getReportView(payoutReport, payoutReportLoading, payoutReportError, reportQueries, currentReportQuery)
      const reportRows = report?.rows.map((row) => [
        formatMonth(row.accountingMonth),
        row.expenseTypeName,
        row.supplierName,
        formatMoney(row.accrualAmount),
        formatMoney(row.expenseAmount),
        formatMoney(row.difference),
      ]) ?? []
      return (
        <ReportWorkbookSheet title="Отчёт по выплатам">
          {renderMonthlyFilter('payouts', {
            from: 'Месяц с',
            to: 'Месяц по',
            actions: <>{renderReportExportButton('xlsx', 'payouts-xlsx', () => void downloadPayoutReport('xlsx'))}{renderReportExportButton('pdf', 'payouts-pdf', () => void downloadPayoutReport('pdf'))}</>,
            extra: (
              <ReportCheckboxMultiSelect
                key="payout-report-filter"
                label="Поставщики/сотрудники"
                ariaLabel="Поставщики или сотрудники"
                allLabel="Все поставщики и сотрудники"
                placeholder="Введите поставщика или сотрудника"
                resultsAriaLabel="Найденные поставщики и сотрудники отчёта"
                selectedAriaLabel="Выбранные поставщики и сотрудники отчёта"
                options={counterpartyFilterOptions}
                loadOptions={loadCounterpartyFilterOptions}
                selectedValues={selectedCounterpartyKeys}
                onChange={setSelectedCounterpartyKeys}
              />
            ),
          })}
          {renderReportLoadingState(primaryLoading, refreshing)}
          {payoutReportError ? <AsyncErrorState message={payoutReportError} onRetry={() => setReportReloadRevision((value) => value + 1)} retrying={payoutReportLoading} /> : null}
          <div className="report-workbook-summary-row">
            <span><strong>ИТОГО начислений</strong><b>{report ? formatMoney(report.accrualTotal) : '—'}</b></span>
            <span><strong>ИТОГО выплат</strong><b>{report ? formatMoney(report.expenseTotal) : '—'}</b></span>
            <span><strong>Разница</strong><b>{report ? formatMoney(report.difference) : '—'}</b></span>
          </div>
          {renderReportTable(
            'Отчет по выплатам',
            [{ label: 'Месяц', sortField: 'accountingMonth' }, { label: 'Услуга', sortField: 'expenseTypeName' }, { label: 'Поставщик/сотрудник', sortField: 'supplierName' }, { label: 'Начисления', sortField: 'accrualAmount' }, { label: 'Выплаты', sortField: 'expenseAmount' }, { label: 'Разница', sortField: 'difference' }],
            reportRows,
            report ? ['ИТОГО', '', '', formatMoney(report.accrualTotal), formatMoney(report.expenseTotal), formatMoney(report.difference)] : undefined,
            { tab: 'payouts', disabled: payoutReportLoading, totalCount: report?.rowCount },
            primaryLoading || payoutReportError ? undefined : 'Данных за период нет',
          )}
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'income') {
      const [report, primaryLoading, refreshing] = getReportView(incomeReport, incomeReportLoading, incomeReportError, reportQueries, currentReportQuery)
      const incomeRows = report?.rows.filter((row) => row.rowType === 'payments').map((row) => [
        row.garageNumber,
        formatDateOnly(row.date),
        formatOperationTime(row.createdAtUtc),
        formatMoney(row.incomeAmount),
        row.incomeTypeName,
        row.debtAfterPayment === null || row.debtAfterPayment === undefined ? '' : formatMoney(row.debtAfterPayment),
      ]) ?? []
      return (
        <ReportWorkbookSheet title="Отчет по поступлениям">
          {renderDateFilter('income', {
            from: 'С',
            to: 'По',
            actions: (
              <>
                <button
                  className="secondary-button report-group-button"
                  type="button"
                  aria-pressed={incomePaymentsGrouped}
                  disabled={incomeReportLoading}
                  onClick={() => {
                    setIncomePaymentsGrouped((current) => !current)
                  }}
                >
                  {incomePaymentsGrouped ? 'Показать отдельные платежи' : 'Сгруппировать платежи'}
                </button>
                {renderReportExportButton('xlsx', 'income-xlsx', () => void downloadIncomeReport('xlsx'))}
                {renderReportExportButton('pdf', 'income-pdf', () => void downloadIncomeReport('pdf'))}
              </>
            ),
            extra: (
              <ReportCheckboxMultiSelect
                key="income-report-filter"
                label="Гаражи"
                ariaLabel="Гаражи по поступлениям"
                allLabel="Все гаражи"
                placeholder="Введите номер гаража или ФИО владельца"
                resultsAriaLabel="Найденные гаражи отчёта по поступлениям"
                selectedAriaLabel="Выбранные гаражи отчёта по поступлениям"
                options={garageFilterOptions}
                loadOptions={loadGarageFilterOptions}
                selectedValues={selectedIncomeGarageIds}
                onChange={setSelectedIncomeGarageIds}
              />
            ),
          })}
          <p className="report-workbook-comment" role="note">
            По умолчанию части одной квитанции или полной оплаты, в том числе сохранённой ранее, объединены. В режиме отдельных платежей каждая операция показана собственной строкой.
          </p>
          {renderReportLoadingState(primaryLoading, refreshing)}
          {incomeReportError ? <AsyncErrorState message={incomeReportError} onRetry={() => setReportReloadRevision((value) => value + 1)} retrying={incomeReportLoading} /> : null}
          <div className="report-workbook-summary-row report-workbook-summary-row--single">
            <span><strong>ИТОГО поступлений</strong><b>{report ? formatMoney(report.incomeTotal) : '—'}</b></span>
          </div>
          {renderReportTable(
            'Отчет по поступлениям',
            [{ label: 'Гараж', sortField: 'garageNumber' }, { label: 'Дата', sortField: 'date' }, 'Время', { label: 'Сумма платежа', sortField: 'incomeAmount' }, { label: 'Назначение платежа', sortField: 'incomeTypeName' }, { label: 'Остаток долга после платежа', sortField: 'debt' }],
            incomeRows,
            report ? ['ИТОГО', '', '', formatMoney(report.incomeTotal), '', ''] : undefined,
            { tab: 'income', disabled: incomeReportLoading, totalCount: report?.rowCount },
            primaryLoading || incomeReportError ? undefined : 'Данных за период нет',
          )}
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'cashPayments') {
      const [report, primaryLoading, refreshing] = getReportView(cashPaymentReport, cashPaymentReportLoading, cashPaymentReportError, reportQueries, currentReportQuery)
      const cashRows = report?.rows.map((row) => [
        formatDateOnly(row.date),
        formatMoney(row.amount),
        row.hasReceipt ? 'Да' : 'Нет',
        row.purpose,
        row.comment ?? '',
      ]) ?? []
      return (
        <ReportWorkbookSheet title="Отчёт по оплатам из кассы">
          {renderDateFilter('cashPayments', { from: 'С', to: 'По', actions: <>{renderReportExportButton('xlsx', 'cashPayments-xlsx', () => void downloadCashOrBankReport('cashPayments', 'xlsx'))}{renderReportExportButton('pdf', 'cashPayments-pdf', () => void downloadCashOrBankReport('cashPayments', 'pdf'))}</> })}
          {renderReportLoadingState(primaryLoading, refreshing)}
          {cashPaymentReportError ? <AsyncErrorState message={cashPaymentReportError} onRetry={() => setReportReloadRevision((value) => value + 1)} retrying={cashPaymentReportLoading} /> : null}
          <div className="report-workbook-summary-row report-workbook-summary-row--single">
            <span><strong>ИТОГО оплачено</strong><b>{report ? formatMoney(report.total) : '—'}</b></span>
          </div>
          {renderReportTable(
            'Отчет по оплатам из кассы',
            [{ label: 'Дата', sortField: 'date' }, { label: 'Сумма', sortField: 'amount' }, { label: 'Наличие чека', sortField: 'hasReceipt' }, { label: 'Назначение', sortField: 'purpose' }, 'Комментарий'],
            cashRows,
            report ? ['ИТОГО', formatMoney(report.total), '', '', formatCount(report.rowCount, 'операция', 'операции', 'операций')] : undefined,
            { tab: 'cashPayments', disabled: cashPaymentReportLoading, totalCount: report?.rowCount },
            primaryLoading || cashPaymentReportError ? undefined : 'Операций за период нет',
          )}
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'bankDeposits') {
      const [report, primaryLoading, refreshing] = getReportView(bankDepositReport, bankDepositReportLoading, bankDepositReportError, reportQueries, currentReportQuery)
      const bankRows = report?.rows.map((row) => [
        formatDateOnly(row.date),
        formatMoney(row.amount),
        row.comment || '',
      ]) ?? []
      return (
        <ReportWorkbookSheet title="Отчёт по сдаче кассы в банк">
          {renderDateFilter('bankDeposits', { from: 'С', to: 'По', actions: <>{renderReportExportButton('xlsx', 'bankDeposits-xlsx', () => void downloadCashOrBankReport('bankDeposits', 'xlsx'))}{renderReportExportButton('pdf', 'bankDeposits-pdf', () => void downloadCashOrBankReport('bankDeposits', 'pdf'))}</> })}
          {renderReportLoadingState(primaryLoading, refreshing)}
          {bankDepositReportError ? <AsyncErrorState message={bankDepositReportError} onRetry={() => setReportReloadRevision((value) => value + 1)} retrying={bankDepositReportLoading} /> : null}
          <div className="report-workbook-summary-row report-workbook-summary-row--single">
            <span><strong>ИТОГО сдано в банк</strong><b>{report ? formatMoney(report.total) : '—'}</b></span>
          </div>
          {renderReportTable(
            'Отчет по сдаче кассы в банк',
            [{ label: 'Дата', sortField: 'date' }, { label: 'Сумма', sortField: 'amount' }, { label: 'Комментарий', sortField: 'comment' }],
            bankRows,
            report ? ['ИТОГО', formatMoney(report.total), formatCount(report.rowCount, 'операция', 'операции', 'операций')] : undefined,
            { tab: 'bankDeposits', disabled: bankDepositReportLoading, totalCount: report?.rowCount },
            primaryLoading || bankDepositReportError ? undefined : 'Операций за период нет',
          )}
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'fees') {
      const [report, primaryLoading, refreshing] = getReportView(feeReport, feeReportLoading, feeReportError, reportQueries, currentReportQuery)
      const summaryRows = (report?.summaryRows ?? []).map((row) => [
        <button
          className="link-button"
          type="button"
          aria-label={`Открыть детализацию сбора ${row.name}`}
          onClick={() => {
            setSelectedFeeEntryIds([row.incomeTypeId])
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
        ? report?.garageRows.filter((row) => row.debt > 0)
        : report?.garageRows) ?? []
      const feeDetailTableRows = feeDetailRows.map((row) => [
        row.garageNumber,
        row.ownerName ?? '',
        formatMoney(row.accrued),
        formatMoney(row.paid),
        row.lastPaymentDate ? formatDateOnly(row.lastPaymentDate) : '',
        formatMoney(row.debt),
      ])
      const feeDetailTableName = feeDetailMode === 'debtors' ? 'Должники по сбору' : 'Гаражи по сбору'
      return (
          <ReportWorkbookSheet title="Отчёт по сборам">
          {renderReportLoadingState(primaryLoading, refreshing)}
          {feeReportError ? <AsyncErrorState message={feeReportError} onRetry={() => setReportReloadRevision((value) => value + 1)} retrying={feeReportLoading} /> : null}
          <div className="report-workbook-filter report-workbook-filter--single" aria-label="Фильтры отчета по сборам">
            <div className="report-workbook-filter__fields">
              <ReportCheckboxMultiSelect
                key="fee-report-filter"
                label="Вариации сборов"
                ariaLabel="Вариации сборов"
                allLabel="Все сборы"
                placeholder="Выберите один или несколько сборов"
                resultsAriaLabel="Доступные вариации сборов"
                selectedAriaLabel="Выбранные вариации сборов"
                options={feeFilterOptions}
                selectedValues={selectedFeeEntryIds}
                openOnFocus
                onChange={setSelectedFeeEntryIds}
              />
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
              report ? ['ИТОГО', '', formatMoney(report.accruedTotal), formatMoney(report.collectedTotal)] : undefined,
              undefined,
              primaryLoading || feeReportError ? undefined : 'Данных по сбору нет',
            )}
            <div className="report-workbook-side-summary" aria-label="Детализация сбора">
              <dl>
                <div><dt>{report?.variation ?? feeVariationLabel}</dt><dd>{formatMoney(report?.accruedTotal ?? 0)}</dd></div>
                <div><dt>Собрано</dt><dd>{formatMoney(report?.collectedTotal ?? 0)}</dd></div>
                <div><dt>Задолженность</dt><dd>{formatMoney(report?.debtTotal ?? 0)}</dd></div>
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
                    {
                      tab: 'fees',
                      disabled: feeReportLoading,
                      totalCount: report
                        ? feeDetailMode === 'debtors'
                          ? feeDetailTableRows.length
                          : Math.max(feeDetailTableRows.length, report.rowCount - report.summaryRows.length)
                        : undefined,
                    },
                    primaryLoading || feeReportError ? undefined : feeDetailMode === 'debtors' ? 'Должников нет' : 'Данных по гаражам нет',
                  )}
                </div>
              ) : null}
            </div>
          </div>
        </ReportWorkbookSheet>
      )
    }

    const [report, primaryLoading, refreshing] = getReportView(fundChangeReport, fundChangeReportLoading, fundChangeReportError, reportQueries, currentReportQuery)
    const fundRows = report?.rows.map((row) => [
      row.fundName,
      formatDateOnly(row.date),
      row.changeName,
      formatMoney(row.amount),
      formatMoney(row.balanceBefore),
      formatMoney(row.balanceAfter),
      row.actorDisplayName ?? '',
      row.reason,
    ]) ?? []
    return (
      <ReportWorkbookSheet title="Отчёт по изменению фондов">
        {renderDateFilter('funds', { from: 'С', to: 'По', actions: <>{renderReportExportButton('xlsx', 'funds-xlsx', () => void downloadFundChangeReport('xlsx'))}{renderReportExportButton('pdf', 'funds-pdf', () => void downloadFundChangeReport('pdf'))}</> })}
        {renderReportLoadingState(primaryLoading, refreshing)}
        {fundChangeReportError ? <AsyncErrorState message={fundChangeReportError} onRetry={() => setReportReloadRevision((value) => value + 1)} retrying={fundChangeReportLoading} /> : null}
        {report ? (
          <div className="report-workbook-summary-row">
            <strong>Пополнено: {formatMoney(report.depositTotal)}</strong>
            <strong>Изъято: {formatMoney(report.withdrawalTotal)}</strong>
            <strong>Операций: {report.rowCount}</strong>
          </div>
        ) : null}
        {renderReportTable(
          'Отчет по изменению фондов',
          [{ label: 'Фонд', sortField: 'fundName' }, { label: 'Дата', sortField: 'date' }, { label: 'Изменение', sortField: 'changeName' }, { label: 'Изменение, руб.', sortField: 'amount' }, { label: 'Сумма до', sortField: 'balanceBefore' }, { label: 'Сумма после', sortField: 'balanceAfter' }, { label: 'Пользователь', sortField: 'actorDisplayName' }, { label: 'Комментарий', sortField: 'reason' }],
          fundRows,
          undefined,
          { tab: 'funds', disabled: fundChangeReportLoading, totalCount: report?.rowCount },
          primaryLoading || fundChangeReportError ? undefined : 'Операций за период нет',
        )}
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

      {reportDataError ? <FormError>{reportDataError}</FormError> : null}
      {reportExportMessage ? <p className="form-success">{reportExportMessage}</p> : null}


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

      {garageQuickListEditor ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!garageQuickListSaving) setGarageQuickListEditor(null)
        }}>
          <section ref={garageQuickListDialogRef} className="detail-dialog dictionary-confirmation-dialog report-garage-quick-list-dialog" role="dialog" aria-modal="true" aria-labelledby="garage-quick-list-editor-title" aria-describedby="garage-quick-list-editor-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Отчёт по гаражам</p>
                <h3 id="garage-quick-list-editor-title">{garageQuickListEditor.id ? 'Изменить быстрый список' : 'Создать быстрый список'}</h3>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть форму быстрого списка" disabled={garageQuickListSaving} onClick={() => setGarageQuickListEditor(null)}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p id="garage-quick-list-editor-description">В список войдут выбранные сейчас гаражи: {selectedGarageIds.length}. Список будет доступен всем пользователям отчётов.</p>
            <form onSubmit={(event) => {
              event.preventDefault()
              void saveGarageQuickList()
            }}>
              <label>
                <span>Название списка</span>
                <input
                  ref={garageQuickListNameRef}
                  aria-label="Название быстрого списка"
                  value={garageQuickListEditor.name}
                  maxLength={100}
                  required
                  disabled={garageQuickListSaving}
                  onChange={(event) => {
                    setGarageQuickListEditor({ ...garageQuickListEditor, name: event.target.value })
                    setGarageQuickListError(null)
                  }}
                />
              </label>
              {garageQuickListError ? <FormError>{garageQuickListError}</FormError> : null}
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" disabled={garageQuickListSaving} onClick={() => setGarageQuickListEditor(null)}>Отмена</button>
                <button className="primary-button" type="submit" disabled={garageQuickListSaving}>
                  {garageQuickListSaving ? <LoaderCircle className="button-spinner" size={16} aria-hidden="true" /> : null}
                  {garageQuickListSaving ? 'Сохраняем...' : garageQuickListEditor.id ? 'Сохранить список' : 'Создать список'}
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}

      {garageQuickListDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!garageQuickListSaving) setGarageQuickListDeleteTarget(null)
        }}>
          <section ref={garageQuickListDeleteDialogRef} className="detail-dialog dictionary-confirmation-dialog report-garage-quick-list-dialog" role="alertdialog" aria-modal="true" aria-labelledby="garage-quick-list-delete-title" aria-describedby="garage-quick-list-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление списка</p>
                <h3 id="garage-quick-list-delete-title">Удалить список «{garageQuickListDeleteTarget.name}»?</h3>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления списка" disabled={garageQuickListSaving} onClick={() => setGarageQuickListDeleteTarget(null)}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p id="garage-quick-list-delete-description">Удалится только быстрый список. Гаражи, операции и отчёты не изменятся; действие сохранится в истории.</p>
            <label>
              <span>Причина удаления</span>
              <textarea
                aria-label="Причина удаления быстрого списка"
                value={garageQuickListDeleteReason}
                maxLength={1000}
                required={actionCommentsRequired}
                disabled={garageQuickListSaving}
                onChange={(event) => {
                  setGarageQuickListDeleteReason(event.target.value)
                  setGarageQuickListError(null)
                }}
              />
            </label>
            {garageQuickListError ? <FormError>{garageQuickListError}</FormError> : null}
            <div className="detail-dialog-actions">
              <button ref={garageQuickListDeleteCancelRef} className="ghost-button" type="button" disabled={garageQuickListSaving} onClick={() => setGarageQuickListDeleteTarget(null)}>Отмена</button>
              <button className="ghost-button danger-button" type="button" disabled={garageQuickListSaving} onClick={() => void deleteGarageQuickList()}>
                {garageQuickListSaving ? <LoaderCircle className="button-spinner" size={16} aria-hidden="true" /> : <Trash2 size={16} aria-hidden="true" />}
                {garageQuickListSaving ? 'Удаляем...' : 'Удалить список'}
              </button>
            </div>
          </section>
        </div>
      ) : null}
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
