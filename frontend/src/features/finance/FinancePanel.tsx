import { Fragment, useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent, MouseEvent, ReactNode } from 'react'
import { Award, CircleHelp, FileText, Gavel, History, LoaderCircle, Pencil, RotateCcw, Save, Search, Trash2, UserRound, WalletCards, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { AccountingTypeDto, DictionaryClient, GarageDto, IrregularPaymentDto, StaffMemberDto, SupplierDto, SupplierGroupDto } from '../../services/dictionariesApi'
import type { AccrualDto, CreateAccrualRequest, CreateExpenseOperationRequest, CreateIncomeOperationRequest, CreateMeterReadingRequest, CreateSupplierAccrualRequest, ExpensePaymentSource, ExpensePaymentType, ExpenseWorksheetDto, FinanceClient, FinancePagedResult, FinanceSummaryDto, FinancialOperationDto, GarageFullPaymentQuoteDto, GarageOverdueDebtDto, GenerateSupplierGroupSalaryAccrualsRequest, MeterReadingDto, MissingMeterReadingDto, StaffSalaryAdjustmentType, SupplierAccrualDto } from '../../services/financeApi'
import { FinanceApiError } from '../../services/financeApi'
import type { IntegrationClient } from '../../services/integrationsApi'
import type { ApplicationSettingsClient } from '../../services/settingsApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { AsyncErrorState, BackgroundRefreshStatus, LoadingSkeleton, StatusMessage, TableLoadingState } from '../../shared/AsyncState'
import type { FinanceEditorKey, FinanceSectionKey } from '../../shared/financeWorkbench'
import { financeSectionOptions, formatFinanceGarageLabel, formatFinanceIncomeGarageSearchStatus, formatFinanceOperationCount, formatFinanceVisibleListStatus, getFinanceContextMenuLabel, getFinanceEditorFieldLabel, getFinanceEditorSavingScope, getFinanceEditorSubmitLabel, getFinanceEditorTitle, getFinanceEditorUiLabel, getFinanceEditorValidationTitle, getFinanceFallbackLabel, getFinanceMeterKindLabel, getFinanceOptionalText, getFinancePanelLabel, getFinanceSectionDescription, getFinanceTableHeaders, getFinanceToolbarLabel, getFinanceVisibleListEmptyLabel, getFinanceVisibleListTableHeaders, getFinanceVisibleListTableLabel } from '../../shared/financeWorkbench'
import type { ChangePreview } from '../../shared/changePreview'
import { appendChangePreview, formatChangeDate, formatChangeMoney, formatChangeText } from '../../shared/changePreview'
import { FormError, FormValidationSummary } from '../../shared/formFeedback'
import { FormField } from '../../shared/FormField'
import { formatAccrualSource, formatDateOnly, formatDebtAmount, formatDebtLabel, formatMissingMeterReadings, formatMoney, formatMonth, formatOperationTime, formatPaymentAllocations, getDebtClassName, getCurrentMonthInputValue, getLocalDateInputValue, getPreviousMonthInputValue } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { MoneyInput, MoneyTextInput } from '../../shared/MoneyInput'
import { MeterReadingInput } from '../../shared/MeterReadingInput'
import { SelectControl } from '../../shared/SelectControl'
import { ReportPeriodQuickSelect } from '../../shared/ReportPeriodQuickSelect'
import { TablePagination } from '../../shared/TablePagination'
import { getAccrualValidationErrors, getExpenseValidationErrors, getIncomeValidationErrors, getMeterReadingValidationErrors, getSupplierAccrualValidationErrors, getSupplierGroupSalaryValidationErrors } from '../../shared/validation'
import { formatPaymentMoney, parsePaymentMoney } from './paymentMoneyFormatting'
import { calculateCashAndBankTotal, calculateExpenseWorksheetClosingBalance, toSignedExpenseWorksheetBalance } from './expenseWorksheetBalances'
import { expensePaymentTypeOptions, formatExpensePaymentSource, formatExpensePaymentType } from './expensePaymentTypes'
import { rankGarageSearchResults } from './garageSearchRanking'
import { getGarageBalancePresentation, toSignedGarageNetBalance, toSignedGarageSplitBalance } from './garageBalancePresentation'
import { createGarageIncomeRowsFromWorksheet, getAccrualCalculationSummary } from './garageIncomeWorksheetRows'
import type { GarageIncomePrototypeRow } from './garageIncomeWorksheetRows'
import { createFullPaymentAllocations, getFullPaymentRows, roundPaymentMoney, sumPaymentDebt, toMoneyMinorUnits } from './fullPaymentPlan'
import { getFirstLinkedSupplier, getSupplierAccrualExpenseType } from './supplierAccrualLink'
import { overdueDebtDetailsPreference } from './financeDisplayPreferences'

type AccrualBreakdown =
  | { kind: 'garage'; accrual: AccrualDto }
  | { kind: 'supplier'; accrual: SupplierAccrualDto }

const financeScreenRequestLimit = 50
const financePreviewRequestLimit = 8
const dictionaryScreenRequestLimit = 100
const garageSearchTimeoutMs = 10_000
type FinanceRecord = FinancialOperationDto | AccrualDto | SupplierAccrualDto | MeterReadingDto
type FinancePreviewStatuses = {
  operations: boolean
  accruals: boolean
  supplierAccruals: boolean
  meterReadings: boolean
}

class LatestRequestSequence {
  private currentRequestId = 0

  begin() {
    this.currentRequestId += 1
    return this.currentRequestId
  }

  invalidate() {
    this.currentRequestId += 1
  }

  isLatest(requestId: number) {
    return requestId === this.currentRequestId
  }
}
type CancelFinanceTarget = {
  section: FinanceSectionKey
  record: FinanceRecord
  reason: string
}
type RestoreFinanceTarget = {
  section: FinanceSectionKey
  record: FinanceRecord
}
type PaymentsPrototypeDialogKey = 'bank'

type PaymentPrototypeRow = {
  rowKind?: 'supplier' | 'staff' | string
  supplierId?: string | null
  staffMemberId?: string | null
  expenseTypeId?: string | null
  expenseFundName?: string | null
  item: string
  counterparty?: string
  openingDebt: number
  openingAdvance: number
  closingDebt: number
  closingAdvance: number
  cost: number | string
  baseAccrual?: number
  bonus?: number
  penalty?: number
  paid: number | string
  balance: number | string
  collected: number | string
  difference: number | string
  action: boolean
}

type PaymentsPrototypeGarage = {
  id: string
  number: string
  ownerName: string
  phone: string
  peopleCount: number
  floorCount: number
  balance: number
  overdueDebt: number
}

type GarageIncomeWorksheetPeriodSummary = {
  openingBalance: number
  openingDebt: number
  unrepresentedOpeningDebt: number
  accrualTotal: number
  incomeTotal: number
  advanceTotal: number
  closingBalance: number
  closingDebt: number
}

type GaragePaymentHistoryPrototypeRow = {
  id: string
  date: string
  time: string
  amount: number
  purpose: string
  debtAfter: number
  operation?: FinancialOperationDto
}

type GaragePaymentHistoryEditState = {
  row: GaragePaymentHistoryPrototypeRow
  amount: string
  operationDate: string
  accountingMonth: string
  documentNumber: string
  comment: string
  error: string | null
}

type GaragePaymentHistoryCancelState = {
  row: GaragePaymentHistoryPrototypeRow
  reason: string
  error: string | null
}

type EarlyElectricityPaymentConfirmationState = {
  row: GarageIncomePrototypeRow
  previousPaymentDate: string
  daysSincePreviousPayment: number
}

type HistoricalMeterReadingSaveState = {
  row: GarageIncomePrototypeRow
  reason: string
  error: string | null
}

type GaragePaymentIncomeType = {
  id: string
  code: string | null
}

type FullPaymentPrototypePeriodOption = {
  value: string
  label: string
  debt: number
}

type FullPaymentPrototypeSubmitRequest = {
  period: string
  amount: number
  comment: string
}

type GarageAccrualPrototypeSubmitRequest = {
  basis: string
  amount: number
  accountingMonth: string
  comment: string
}

type PenaltyAccrualPrototypeSubmitRequest = {
  accountingMonth: string
  amount: number
  reason: string
}

type ExpensePrototypeDialogPreset = {
  expensePaymentSource: ExpensePaymentSource
  expenseTypeName?: string
  amount?: number
  rowIndex?: number
}

type StaffPaymentPrototypeDialogPreset = {
  staffMemberName?: string
  amount?: number
  rowIndex?: number
}

type StaffSalaryAdjustmentPrototypeDialogPreset = {
  adjustmentType: StaffSalaryAdjustmentType
  accountingMonth: string
}

type ExpensePrototypeSubmitRequest = {
  supplierId?: string
  counterpartyName: string
  expenseTypeId: string
  expensePaymentType: ExpensePaymentType
  expensePaymentSource: ExpensePaymentSource
  expenseFundId?: string
  confirmNegativeFundBalance: boolean
  operationDate: string
  accountingMonth: string
  amount: number
  documentNumber: string
  comment: string
  rowIndex?: number
}

type ExpenseFundOption = {
  id: string
  name: string
  balance: number
}

function getExpenseFundOptions(suppliers: SupplierDto[]): ExpenseFundOption[] {
  const funds = new Map<string, ExpenseFundOption>()
  suppliers.forEach((supplier) => {
    if (supplier.expenseFundId && supplier.expenseFundName) {
      funds.set(supplier.expenseFundId, {
        id: supplier.expenseFundId,
        name: supplier.expenseFundName,
        balance: supplier.expenseFundBalance ?? 0,
      })
    }
  })
  return Array.from(funds.values()).sort((left, right) => left.name.localeCompare(right.name, 'ru-RU'))
}

type StaffPaymentPrototypeSubmitRequest = {
  staffMemberId: string
  operationDate: string
  accountingMonth: string
  amount: number
  documentNumber: string
  comment: string
  rowIndex?: number
}

type StaffSalaryAdjustmentPrototypeSubmitRequest = {
  staffMemberId: string
  accountingMonth: string
  adjustmentType: StaffSalaryAdjustmentType
  amount: number
  documentNumber: string
  reason: string
}

type SupplierAccrualPrototypeSubmitRequest = {
  supplierId: string
  expenseTypeId: string
  accountingMonth: string
  amount: number
  documentNumber: string
  comment: string
}

function createExpenseRowsFromWorksheet(worksheet: ExpenseWorksheetDto): PaymentPrototypeRow[] {
  return worksheet.rows.map((row) => ({
    rowKind: row.rowKind,
    supplierId: row.supplierId,
    staffMemberId: row.staffMemberId,
    expenseTypeId: row.expenseTypeId,
    expenseFundName: row.expenseFundName,
    counterparty: row.counterpartyName ?? '',
    item: row.expenseTypeName,
    openingDebt: row.openingDebt ?? Math.max(row.openingBalance ?? 0, 0),
    openingAdvance: row.openingAdvance ?? Math.max(-(row.openingBalance ?? 0), 0),
    closingDebt: row.closingDebt ?? Math.max((row.openingBalance ?? 0) + row.accrualAmount - row.expenseAmount, 0),
    closingAdvance: row.closingAdvance ?? Math.max(-((row.openingBalance ?? 0) + row.accrualAmount - row.expenseAmount), 0),
    cost: row.accrualAmount,
    baseAccrual: row.baseAccrualAmount ?? row.accrualAmount,
    bonus: row.bonusAmount ?? 0,
    penalty: row.penaltyAmount ?? 0,
    paid: row.expenseAmount,
    balance: row.closingDebt ?? row.balance,
    collected: row.collectedAmount ?? '',
    difference: row.difference ?? '',
    action: true,
  }))
}

export function FinancePanel({
  auth,
  dictionaryClient,
  financeClient,
  settingsClient,
}: {
  auth: AuthResponse
  dictionaryClient: DictionaryClient
  financeClient: FinanceClient
  integrationClient: IntegrationClient
  settingsClient: ApplicationSettingsClient
}) {
  const today = getLocalDateInputValue()
  const month = `${today.slice(0, 7)}-01`
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [incomeGarageOptions, setIncomeGarageOptions] = useState<GarageDto[]>([])
  const [supplierGroups, setSupplierGroups] = useState<SupplierGroupDto[]>([])
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [staffMembers, setStaffMembers] = useState<StaffMemberDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [irregularPayments, setIrregularPayments] = useState<IrregularPaymentDto[]>([])
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [operations, setOperations] = useState<FinancialOperationDto[]>([])
  const [accruals, setAccruals] = useState<AccrualDto[]>([])
  const [supplierAccruals, setSupplierAccruals] = useState<SupplierAccrualDto[]>([])
  const [meterReadings, setMeterReadings] = useState<MeterReadingDto[]>([])
  const [missingMeterReadings, setMissingMeterReadings] = useState<MissingMeterReadingDto[]>([])
  const [summary, setSummary] = useState<FinanceSummaryDto>({ incomeTotal: 0, expenseTotal: 0, accrualTotal: 0, balance: 0, debt: 0, operationCount: 0, accrualCount: 0, meterReadingCount: 0 })
  const [incomeForm, setIncomeForm] = useState({ garageId: '', incomeTypeId: '', operationDate: today, accountingMonth: month, amount: 0, documentNumber: '', comment: '' })
  const [expenseForm, setExpenseForm] = useState({
    supplierId: '',
    expenseTypeId: '',
    expensePaymentType: 'with_receipt' as ExpensePaymentType,
    expensePaymentSource: 'bank' as ExpensePaymentSource,
    expenseFundId: '',
    operationDate: today,
    accountingMonth: month,
    amount: 0,
    documentNumber: '',
    comment: '',
  })
  const expenseFundOptions = useMemo(() => getExpenseFundOptions(suppliers), [suppliers])
  const selectedExpenseSupplier = suppliers.find((supplier) => supplier.id === expenseForm.supplierId)
  const [accrualForm, setAccrualForm] = useState({ garageId: '', incomeTypeId: '', accountingMonth: month, amount: 0, source: 'manual' as 'manual' | 'regular', comment: '' })
  const [supplierAccrualForm, setSupplierAccrualForm] = useState({ supplierId: '', expenseTypeId: '', accountingMonth: month, amount: 0, source: 'manual' as 'manual' | 'regular', documentNumber: '', comment: '' })
  const [salaryForm, setSalaryForm] = useState({ supplierGroupId: '', accountingMonth: month, amount: 0, documentNumber: '', comment: '' })
  const [salaryStatus, setSalaryStatus] = useState<string | null>(null)
  const [meterForm, setMeterForm] = useState({ garageId: '', meterKind: 'water', accountingMonth: month, readingDate: today, currentValue: 0, comment: '' })
  const [incomeGarageSearch, setIncomeGarageSearch] = useState('')
  const [incomeGarageSearchStatus, setIncomeGarageSearchStatus] = useState<string | null>(null)
  const [activeFinanceSection, setActiveFinanceSection] = useState<FinanceSectionKey>('income')
  const [financeFilter, setFinanceFilter] = useState({ monthFrom: '', monthTo: '', search: '' })
  const [financeSearchInput, setFinanceSearchInput] = useState('')
  const [financeEditor, setFinanceEditor] = useState<{ section: FinanceEditorKey; mode: 'create' | 'edit'; record?: FinanceRecord } | null>(null)
  const [financeEditorInitialSnapshot, setFinanceEditorInitialSnapshot] = useState('')
  const [pendingFinanceEditConfirmation, setPendingFinanceEditConfirmation] = useState<{
    kind: 'income' | 'expense' | 'accrual' | 'supplier-accrual'
    recordId: string
    objectName: string
    request: CreateIncomeOperationRequest | CreateExpenseOperationRequest | CreateAccrualRequest | CreateSupplierAccrualRequest
    changes: ChangePreview[]
  } | null>(null)
  const [financePage, setFinancePage] = useState<FinancePagedResult<FinanceRecord>>({ items: [], totalCount: 0, offset: 0, limit: 25 })
  const [financeSectionCounts, setFinanceSectionCounts] = useState<Record<FinanceSectionKey, number>>({ income: 0, expense: 0, accruals: 0, supplierAccruals: 0, meterReadings: 0 })
  const [financeContextMenu, setFinanceContextMenu] = useState<{ section: FinanceSectionKey; record?: FinanceRecord; x: number; y: number } | null>(null)
  const financeContextMenuTriggerRef = useRef<HTMLElement | null>(null)
  const financeEditorTriggerRef = useRef<HTMLElement | null>(null)
  const cancelFinanceTriggerRef = useRef<HTMLElement | null>(null)
  const restoreFinanceTriggerRef = useRef<HTMLElement | null>(null)
  const [financeWorkbenchRequests] = useState(() => new LatestRequestSequence())
  const financeWorkbenchControllerRef = useRef<AbortController | null>(null)
  const financeSummaryCacheRef = useRef<{ key: string; promise: Promise<FinanceSummaryDto> } | null>(null)
  const financeReferenceBundlePromiseRef = useRef<Promise<void> | null>(null)
  const financeReferenceBundleLoadedRef = useRef(false)
  const financeReferenceBundleGenerationRef = useRef(0)
  const financeReferencesControllerRef = useRef<AbortController | null>(null)
  const financeGarageReferencesPromiseRef = useRef<Promise<GarageDto[] | undefined> | null>(null)
  const financeGarageReferencesRef = useRef<GarageDto[] | null>(null)
  const [paymentsPrototypeDialog, setPaymentsPrototypeDialog] = useState<PaymentsPrototypeDialogKey | null>(null)
  const [paymentsPrototypeRefreshRevision, setPaymentsPrototypeRefreshRevision] = useState(0)
  const paymentsPrototypeTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [financeEditorCloseConfirmation, setFinanceEditorCloseConfirmation] = useState(false)
  const [cancelFinanceTarget, setCancelFinanceTarget] = useState<CancelFinanceTarget | null>(null)
  const [restoreFinanceTarget, setRestoreFinanceTarget] = useState<RestoreFinanceTarget | null>(null)
  const [cancelFinanceReasonError, setCancelFinanceReasonError] = useState<string | null>(null)
  const [incomeValidationErrors, setIncomeValidationErrors] = useState<string[]>([])
  const [expenseValidationErrors, setExpenseValidationErrors] = useState<string[]>([])
  const [accrualValidationErrors, setAccrualValidationErrors] = useState<string[]>([])
  const [supplierAccrualValidationErrors, setSupplierAccrualValidationErrors] = useState<string[]>([])
  const [salaryValidationErrors, setSalaryValidationErrors] = useState<string[]>([])
  const [meterValidationErrors, setMeterValidationErrors] = useState<string[]>([])
  const [accrualBreakdown, setAccrualBreakdown] = useState<AccrualBreakdown | null>(null)
  const [financeReferenceLoading, setFinanceReferenceLoading] = useState(0)
  const [workbenchLoading, setWorkbenchLoading] = useState(false)
  const [workbenchLoaded, setWorkbenchLoaded] = useState(false)
  const [financePreviewLoading, setFinancePreviewLoading] = useState<FinancePreviewStatuses>({ operations: true, accruals: true, supplierAccruals: true, meterReadings: true })
  const [financePreviewFailures, setFinancePreviewFailures] = useState<FinancePreviewStatuses>({ operations: false, accruals: false, supplierAccruals: false, meterReadings: false })
  const [financePreviewReloadRevision, setFinancePreviewReloadRevision] = useState(0)
  const [paymentDisplaySettingsLoaded, setPaymentDisplaySettingsLoaded] = useState(false)
  const [showAllGarageOperations, setShowAllGarageOperations] = useState(false)
  const [paymentDisplaySettingsError, setPaymentDisplaySettingsError] = useState<string | null>(null)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [financeReloadRevision, setFinanceReloadRevision] = useState(0)
  const referencesLoading = financeReferenceLoading !== 0
  const loading = workbenchLoading || !paymentDisplaySettingsLoaded || (showAllGarageOperations && !workbenchLoaded)
  const paymentsPrototypeLoading = referencesLoading
  const closeCancelFinanceDialog = useCallback(() => {
    const trigger = cancelFinanceTriggerRef.current
    setCancelFinanceTarget(null)
    setCancelFinanceReasonError(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      cancelFinanceTriggerRef.current = null
    }, 0)
  }, [setCancelFinanceReasonError, setCancelFinanceTarget])
  const closeRestoreFinanceDialog = useCallback(() => {
    const trigger = restoreFinanceTriggerRef.current
    setRestoreFinanceTarget(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      restoreFinanceTriggerRef.current = null
    }, 0)
  }, [])
  useRestoreFocusOnClose(Boolean(accrualBreakdown))
  useRestoreFocusOnClose(Boolean(financeEditor))
  useRestoreFocusOnClose(Boolean(financeContextMenu))
  useRestoreFocusOnClose(Boolean(pendingFinanceEditConfirmation))
  useRestoreFocusOnClose(Boolean(financeEditorCloseConfirmation))
  useRestoreFocusOnClose(Boolean(cancelFinanceTarget))
  useRestoreFocusOnClose(Boolean(restoreFinanceTarget))
  const accrualBreakdownCloseButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(accrualBreakdown))
  const accrualBreakdownDialogRef = useFocusTrap<HTMLElement>(Boolean(accrualBreakdown))
  const financeEditorCloseButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(financeEditor))
  const financeEditorDialogRef = useFocusTrap<HTMLElement>(Boolean(financeEditor))
  const financeEditConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingFinanceEditConfirmation))
  const financeEditConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingFinanceEditConfirmation))
  const financeEditorCloseConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(financeEditorCloseConfirmation))
  const financeEditorCloseConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(financeEditorCloseConfirmation))
  const cancelFinanceReasonRef = useFocusOnOpen<HTMLTextAreaElement>(Boolean(cancelFinanceTarget))
  const cancelFinanceDialogRef = useFocusTrap<HTMLElement>(Boolean(cancelFinanceTarget))
  const restoreFinanceCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(restoreFinanceTarget))
  const restoreFinanceDialogRef = useFocusTrap<HTMLElement>(Boolean(restoreFinanceTarget))
  const financeContextMenuFirstItemRef = useFocusOnOpen<HTMLButtonElement>(Boolean(financeContextMenu))

  function getFinanceEditorFormSnapshot(section: FinanceEditorKey) {
    if (section === 'income') {
      return JSON.stringify(incomeForm)
    }
    if (section === 'expense') {
      return JSON.stringify(expenseForm)
    }
    if (section === 'accruals') {
      return JSON.stringify(accrualForm)
    }
    if (section === 'supplierGroupSalaryAccruals') {
      return JSON.stringify(salaryForm)
    }
    if (section === 'supplierAccruals') {
      return JSON.stringify(supplierAccrualForm)
    }
    return JSON.stringify(meterForm)
  }

  function hasUnsavedFinanceEditorChanges() {
    return Boolean(financeEditor && financeEditorInitialSnapshot && financeEditorInitialSnapshot !== getFinanceEditorFormSnapshot(financeEditor.section))
  }

  function closeFinanceEditor(options?: { skipConfirmation?: boolean }) {
    if (!financeEditor) {
      return
    }

    if (!options?.skipConfirmation && hasUnsavedFinanceEditorChanges()) {
      setFinanceEditorCloseConfirmation(true)
      return
    }

    setFinanceEditorCloseConfirmation(false)
    setPendingFinanceEditConfirmation(null)
    setFinanceEditorInitialSnapshot('')
    const trigger = financeEditorTriggerRef.current
    setFinanceEditor(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      financeEditorTriggerRef.current = null
    }, 0)
  }

  function confirmCloseFinanceEditor() {
    closeFinanceEditor({ skipConfirmation: true })
  }

  function openPaymentsPrototypeDialog(dialog: PaymentsPrototypeDialogKey, trigger?: HTMLButtonElement | null) {
    paymentsPrototypeTriggerRef.current = trigger ?? null
    setPaymentsPrototypeDialog(dialog)
  }

  function closePaymentsPrototypeDialog() {
    const trigger = paymentsPrototypeTriggerRef.current
    setPaymentsPrototypeDialog(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      paymentsPrototypeTriggerRef.current = null
    }, 0)
  }

  useEscapeKey(Boolean(accrualBreakdown), () => setAccrualBreakdown(null))
  useEscapeKey(Boolean(financeEditor) && !financeEditorCloseConfirmation && !pendingFinanceEditConfirmation, () => closeFinanceEditor())
  useEscapeKey(Boolean(pendingFinanceEditConfirmation), () => setPendingFinanceEditConfirmation(null))
  useEscapeKey(Boolean(financeEditorCloseConfirmation), () => setFinanceEditorCloseConfirmation(false))
  useEscapeKey(Boolean(cancelFinanceTarget) && !saving?.startsWith('cancel'), () => closeCancelFinanceDialog())
  useEscapeKey(Boolean(restoreFinanceTarget) && !saving?.startsWith('restore-finance'), () => closeRestoreFinanceDialog())
  useEscapeKey(Boolean(financeContextMenu), () => setFinanceContextMenu(null))
  useEscapeKey(Boolean(paymentsPrototypeDialog), () => closePaymentsPrototypeDialog())
  const canWritePayments = hasPermission(auth, permissions.paymentsWrite)
  const visibleOperations = operations.slice(0, 8)
  const visibleAccruals = accruals.slice(0, 8)
  const visibleSupplierAccruals = supplierAccruals.slice(0, 8)
  const visibleMeterReadings = meterReadings.slice(0, 8)
  const operationPreviewTotal = summary.incomeCount !== undefined && summary.expenseCount !== undefined
    ? summary.incomeCount + summary.expenseCount
    : summary.operationCount
  const supplierAccrualPreviewTotal = summary.supplierAccrualCount ?? supplierAccruals.length
  const financePreviewPending = {
    operations: showAllGarageOperations && financePreviewLoading.operations,
    accruals: showAllGarageOperations && financePreviewLoading.accruals,
    supplierAccruals: showAllGarageOperations && financePreviewLoading.supplierAccruals,
    meterReadings: showAllGarageOperations && financePreviewLoading.meterReadings,
  }
  const financePreviewsError = Object.values(financePreviewFailures).some(Boolean)

  const ensureFinanceReferenceBundle = useCallback(async () => {
    if (financeReferenceBundleLoadedRef.current) {
      return true
    }

    if (!financeReferenceBundlePromiseRef.current) {
      const generation = financeReferenceBundleGenerationRef.current
      const controller = financeReferencesControllerRef.current!
      setFinanceReferenceLoading((value) => value | 1)
      setError(null)
      financeReferenceBundlePromiseRef.current = Promise.all([
        dictionaryClient.getSupplierGroups(auth.accessToken, undefined, dictionaryScreenRequestLimit, false, controller.signal),
        dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit, false, controller.signal),
        dictionaryClient.getStaffMembers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit, false, controller.signal),
        dictionaryClient.getIncomeTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit, false, controller.signal),
        dictionaryClient.getExpenseTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit, false, controller.signal),
        dictionaryClient.getIrregularPayments(auth.accessToken, undefined, dictionaryScreenRequestLimit, false, controller.signal),
      ]).then(([loadedSupplierGroups, loadedSuppliers, loadedStaffMembers, loadedIncomeTypes, loadedExpenseTypes, loadedIrregularPayments]) => {
        if (controller.signal.aborted || generation !== financeReferenceBundleGenerationRef.current) {
          return
        }

        setSupplierGroups(loadedSupplierGroups)
        setSuppliers(loadedSuppliers)
        setStaffMembers(loadedStaffMembers)
        setIncomeTypes(loadedIncomeTypes)
        setExpenseTypes(loadedExpenseTypes)
        setIrregularPayments(loadedIrregularPayments)
        setIncomeForm((value) => ({ ...value, incomeTypeId: value.incomeTypeId || loadedIncomeTypes[0]?.id || '' }))
        setExpenseForm((value) => {
          const requestedSupplier = loadedSuppliers.find((supplier) => supplier.id === value.supplierId)
          const configuredSuppliers = loadedSuppliers.filter((supplier) => Boolean(
            getSupplierAccrualExpenseType(supplier, loadedExpenseTypes) && supplier.expenseFundId,
          ))
          const linkedSupplier = requestedSupplier && getSupplierAccrualExpenseType(requestedSupplier, loadedExpenseTypes) && requestedSupplier.expenseFundId
            ? requestedSupplier
            : getFirstLinkedSupplier(configuredSuppliers, loadedExpenseTypes)
          return {
            ...value,
            supplierId: linkedSupplier?.id ?? requestedSupplier?.id ?? loadedSuppliers[0]?.id ?? '',
            expenseTypeId: getSupplierAccrualExpenseType(linkedSupplier ?? requestedSupplier ?? loadedSuppliers[0], loadedExpenseTypes)?.id ?? '',
            expenseFundId: (linkedSupplier ?? requestedSupplier ?? loadedSuppliers[0])?.expenseFundId ?? '',
          }
        })
        setAccrualForm((value) => ({ ...value, incomeTypeId: value.incomeTypeId || loadedIncomeTypes[0]?.id || '' }))
        setSupplierAccrualForm((value) => {
          const requestedSupplier = loadedSuppliers.find((supplier) => supplier.id === value.supplierId)
          const linkedSupplier = requestedSupplier && getSupplierAccrualExpenseType(requestedSupplier, loadedExpenseTypes)
            ? requestedSupplier
            : getFirstLinkedSupplier(loadedSuppliers, loadedExpenseTypes)
          return {
            ...value,
            supplierId: linkedSupplier?.id ?? requestedSupplier?.id ?? loadedSuppliers[0]?.id ?? '',
            expenseTypeId: getSupplierAccrualExpenseType(linkedSupplier ?? requestedSupplier ?? loadedSuppliers[0], loadedExpenseTypes)?.id ?? '',
          }
        })
        setSalaryForm((value) => ({ ...value, supplierGroupId: value.supplierGroupId || loadedSupplierGroups[0]?.id || '' }))
        financeReferenceBundleLoadedRef.current = true
      })
    }

    const request = financeReferenceBundlePromiseRef.current
    const requestController = financeReferencesControllerRef.current
    try {
      await request
      return financeReferenceBundleLoadedRef.current
    } catch (caught) {
      if (!requestController?.signal.aborted && request === financeReferenceBundlePromiseRef.current) {
        setError(caught instanceof Error ? caught.message : 'Не удалось загрузить справочники для формы платежа.')
      }
      return false
    } finally {
      if (request === financeReferenceBundlePromiseRef.current) {
        financeReferenceBundlePromiseRef.current = null
        if (!requestController?.signal.aborted) {
          setFinanceReferenceLoading((value) => value & ~1)
        }
      }
    }
  }, [auth.accessToken, dictionaryClient])

  const ensureFinanceGarageReferences = useCallback(async () => {
    if (financeGarageReferencesRef.current) {
      return financeGarageReferencesRef.current
    }
    if (financeGarageReferencesPromiseRef.current) {
      return financeGarageReferencesPromiseRef.current
    }

    const controller = financeReferencesControllerRef.current!
    setFinanceReferenceLoading((value) => value | 2)
    setError(null)
    const request = (async () => {
      try {
        const loadedGarages = await dictionaryClient.getGarages(
          auth.accessToken,
          undefined,
          dictionaryScreenRequestLimit,
          false,
          controller.signal,
        )
        if (controller.signal.aborted) {
          return
        }

        setGarages(loadedGarages)
        setIncomeGarageOptions(loadedGarages)
        financeGarageReferencesRef.current = loadedGarages
        setIncomeForm((value) => ({ ...value, garageId: value.garageId || loadedGarages[0]?.id || '' }))
        setAccrualForm((value) => ({ ...value, garageId: value.garageId || loadedGarages[0]?.id || '' }))
        setMeterForm((value) => ({ ...value, garageId: value.garageId || loadedGarages[0]?.id || '' }))
        return loadedGarages
      } catch (caught) {
        if (!controller.signal.aborted) {
          setError(caught instanceof Error ? caught.message : 'Ошибка.')
        }
      } finally {
        if (!controller.signal.aborted) {
          financeGarageReferencesPromiseRef.current = null
          setFinanceReferenceLoading((value) => value & ~2)
        }
      }
    })()
    financeGarageReferencesPromiseRef.current = request
    return request
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => {
    financeReferenceBundleGenerationRef.current += 1
    const referenceBundleGeneration = financeReferenceBundleGenerationRef.current
    financeReferencesControllerRef.current?.abort()
    financeReferencesControllerRef.current = new AbortController()
    financeReferenceBundleLoadedRef.current = false
    financeReferenceBundlePromiseRef.current = null
    financeGarageReferencesRef.current = null
    financeGarageReferencesPromiseRef.current = null
    queueMicrotask(() => {
      if (referenceBundleGeneration === financeReferenceBundleGenerationRef.current) {
        setFinanceReferenceLoading(
          (financeReferenceBundlePromiseRef.current ? 1 : 0)
          | (financeGarageReferencesPromiseRef.current ? 2 : 0),
        )
      }
    })
    return () => {
      financeReferenceBundleGenerationRef.current += 1
      financeReferencesControllerRef.current?.abort()
    }
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => {
    let ignore = false
    const controller = new AbortController()
    settingsClient.getPaymentDisplaySettings(auth.accessToken, controller.signal)
      .then((settings) => {
        if (!ignore) {
          setShowAllGarageOperations(settings.showAllGarageOperationsByDefault)
          setPaymentDisplaySettingsError(null)
        }
      })
      .catch((caught: unknown) => {
        if (!ignore) {
          setShowAllGarageOperations(false)
          setPaymentDisplaySettingsError(caught instanceof Error ? caught.message : 'Не удалось загрузить настройку отображения платежей.')
        }
      })
      .finally(() => {
        if (!ignore) {
          setPaymentDisplaySettingsLoaded(true)
        }
      })

    return () => {
      ignore = true
      controller.abort()
    }
  }, [auth.accessToken, settingsClient])

  useEffect(() => {
    if (paymentDisplaySettingsLoaded && showAllGarageOperations) {
      void ensureFinanceReferenceBundle()
      void ensureFinanceGarageReferences()
    }
  }, [ensureFinanceGarageReferences, ensureFinanceReferenceBundle, paymentDisplaySettingsLoaded, showAllGarageOperations])

  useEffect(() => {
    if (!paymentDisplaySettingsLoaded || !showAllGarageOperations) {
      return
    }

    let ignore = false
    const controller = new AbortController()
    const handle = window.setTimeout(() => {
      setFinancePreviewLoading({ operations: true, accruals: true, supplierAccruals: true, meterReadings: true })
      setFinancePreviewFailures({ operations: false, accruals: false, supplierAccruals: false, meterReadings: false })

      function loadPreview<T>(
        key: keyof FinancePreviewStatuses,
        request: Promise<T>,
        applyResult: (result: T) => void,
      ) {
        void request
          .then((result) => {
            if (!ignore) {
              applyResult(result)
            }
          })
          .catch(() => {
            if (!ignore) {
              setFinancePreviewFailures((current) => ({ ...current, [key]: true }))
            }
          })
          .finally(() => {
            if (!ignore) {
              setFinancePreviewLoading((current) => ({ ...current, [key]: false }))
            }
          })
      }

      loadPreview('operations', financeClient.getOperations(auth.accessToken, financePreviewRequestLimit, controller.signal), setOperations)
      loadPreview('accruals', financeClient.getAccruals(auth.accessToken, financePreviewRequestLimit, controller.signal), setAccruals)
      loadPreview('supplierAccruals', financeClient.getSupplierAccruals(auth.accessToken, financePreviewRequestLimit, controller.signal), setSupplierAccruals)
      loadPreview('meterReadings', financeClient.getMeterReadings(auth.accessToken, financePreviewRequestLimit, controller.signal), setMeterReadings)
    }, 500)

    return () => {
      ignore = true
      controller.abort()
      window.clearTimeout(handle)
    }
  }, [auth.accessToken, financeClient, financePreviewReloadRevision, paymentDisplaySettingsLoaded, showAllGarageOperations])

  useEffect(() => {
    const handleWindowClick = () => setFinanceContextMenu(null)
    window.addEventListener('click', handleWindowClick)
    return () => window.removeEventListener('click', handleWindowClick)
  }, [])

  useEffect(() => {
    const handle = window.setTimeout(() => {
      setFinanceFilter((value) => (value.search === financeSearchInput ? value : { ...value, search: financeSearchInput }))
    }, 350)

    return () => window.clearTimeout(handle)
  }, [financeSearchInput])

  const loadFinanceWorkbench = useCallback(async (section: FinanceSectionKey, offset: number, limit: number, refreshSummary = false) => {
    financeWorkbenchControllerRef.current?.abort()
    const controller = new AbortController()
    financeWorkbenchControllerRef.current = controller
    const requestId = financeWorkbenchRequests.begin()
    setFinanceContextMenu(null)
    setWorkbenchLoading(true)
    setError(null)
    try {
      const params = {
        monthFrom: financeFilter.monthFrom,
        monthTo: financeFilter.monthTo,
        search: financeFilter.search,
        offset,
        limit,
      }
      const missingMeterMonth = financeFilter.monthFrom || meterForm.accountingMonth
      const activePagePromise: Promise<FinancePagedResult<FinanceRecord>> = section === 'income'
        ? financeClient.getOperationsPage(auth.accessToken, { ...params, operationKind: 'income' }, controller.signal) as Promise<FinancePagedResult<FinanceRecord>>
        : section === 'expense'
          ? financeClient.getOperationsPage(auth.accessToken, { ...params, operationKind: 'expense' }, controller.signal) as Promise<FinancePagedResult<FinanceRecord>>
          : section === 'accruals'
            ? financeClient.getAccrualsPage(auth.accessToken, params, controller.signal) as Promise<FinancePagedResult<FinanceRecord>>
            : section === 'supplierAccruals'
              ? financeClient.getSupplierAccrualsPage(auth.accessToken, params, controller.signal) as Promise<FinancePagedResult<FinanceRecord>>
              : financeClient.getMeterReadingsPage(auth.accessToken, params, controller.signal) as Promise<FinancePagedResult<FinanceRecord>>
      const missingMeterReadingsPromise = section === 'meterReadings'
        ? financeClient.getMissingMeterReadings(auth.accessToken, { accountingMonth: missingMeterMonth, search: financeFilter.search, limit: financeScreenRequestLimit }, controller.signal)
        : Promise.resolve(null)
      const summaryKey = JSON.stringify([financeFilter.monthFrom, financeFilter.monthTo, financeFilter.search])
      let summaryPromise = financeSummaryCacheRef.current?.key === summaryKey && !refreshSummary
        ? financeSummaryCacheRef.current.promise
        : null
      if (!summaryPromise) {
        summaryPromise = financeClient.getSummary(auth.accessToken, { monthFrom: financeFilter.monthFrom, monthTo: financeFilter.monthTo, search: financeFilter.search }, controller.signal)
        financeSummaryCacheRef.current = { key: summaryKey, promise: summaryPromise }
        void summaryPromise.catch(() => {
          if (financeSummaryCacheRef.current?.promise === summaryPromise) {
            financeSummaryCacheRef.current = null
          }
        })
      }
      const secondaryResults = Promise.allSettled([missingMeterReadingsPromise, summaryPromise])
      const activePage = await activePagePromise

      if (!financeWorkbenchRequests.isLatest(requestId)) {
        return
      }

      setFinanceSectionCounts((current) => ({ ...current, [section]: activePage.totalCount }))
      setFinancePage(activePage)
      setWorkbenchLoaded(true)
      setWorkbenchLoading(false)

      const [missingMeterReadingsResult, summaryResult] = await secondaryResults
      if (!financeWorkbenchRequests.isLatest(requestId)) {
        return
      }

      if (summaryResult.status === 'fulfilled') {
        const loadedSummary = summaryResult.value
        setFinanceSectionCounts((current) => ({
          income: loadedSummary.incomeCount ?? (section === 'income' ? activePage.totalCount : current.income),
          expense: loadedSummary.expenseCount ?? (section === 'expense' ? activePage.totalCount : current.expense),
          accruals: loadedSummary.accrualCount,
          supplierAccruals: loadedSummary.supplierAccrualCount ?? (section === 'supplierAccruals' ? activePage.totalCount : current.supplierAccruals),
          meterReadings: loadedSummary.meterReadingCount,
        }))
        setSummary(loadedSummary)
      } else {
        setError(summaryResult.reason instanceof Error ? summaryResult.reason.message : 'Не удалось загрузить сводные показатели платежей.')
      }

      if (missingMeterReadingsResult.status === 'fulfilled') {
        if (missingMeterReadingsResult.value) {
          setMissingMeterReadings(missingMeterReadingsResult.value)
        }
      } else {
        setError(missingMeterReadingsResult.reason instanceof Error ? missingMeterReadingsResult.reason.message : 'Не удалось загрузить список отсутствующих показаний.')
      }
    } catch (caught) {
      if (financeWorkbenchRequests.isLatest(requestId)) {
        setWorkbenchLoaded(true)
        setError(caught instanceof Error ? caught.message : 'Не удалось загрузить страницу платежей.')
      }
    } finally {
      if (financeWorkbenchRequests.isLatest(requestId)) {
        setWorkbenchLoading(false)
      }
    }
  }, [auth.accessToken, financeClient, financeFilter.monthFrom, financeFilter.monthTo, financeFilter.search, financeWorkbenchRequests, meterForm.accountingMonth])

  function refreshFinanceWorkbenchAfterSave(section: FinanceSectionKey, offset = financePage.offset) {
    void loadFinanceWorkbench(section, offset, financePage.limit, true)
    setFinancePreviewReloadRevision((value) => value + 1)
  }

  useEffect(() => {
    if (!paymentDisplaySettingsLoaded || !showAllGarageOperations) {
      financeWorkbenchControllerRef.current?.abort()
      return
    }

    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadFinanceWorkbench(activeFinanceSection, 0, financePage.limit)
    return () => financeWorkbenchControllerRef.current?.abort()
  }, [activeFinanceSection, financePage.limit, financeReloadRevision, loadFinanceWorkbench, paymentDisplaySettingsLoaded, showAllGarageOperations])

  async function searchIncomeGarages() {
    const query = incomeGarageSearch.trim()
    await runSaving('income-garage-search', async () => {
      const controller = financeReferencesControllerRef.current!
      const foundGarages = await dictionaryClient.getGarages(auth.accessToken, query || undefined, dictionaryScreenRequestLimit, false, controller.signal)
      if (controller.signal.aborted) return
      setIncomeGarageOptions(foundGarages)
      setIncomeForm((value) => ({
        ...value,
        garageId: foundGarages.some((garage) => garage.id === value.garageId) ? value.garageId : foundGarages[0]?.id ?? '',
      }))
      setIncomeGarageSearchStatus(formatFinanceIncomeGarageSearchStatus(foundGarages.length, Boolean(query)))
    })
  }

  function getIncomeEditChangePreview(record: FinancialOperationDto, request: CreateIncomeOperationRequest) {
    const changes: ChangePreview[] = []
    const formatIncomeGarage = (garageId: string | null | undefined, fallbackGarageNumber: string | null | undefined) => {
      if (!garageId) {
        return 'пусто'
      }

      if (fallbackGarageNumber) {
        return formatFinanceGarageLabel(fallbackGarageNumber)
      }

      const garage = incomeGarageOptions.find((item) => item.id === garageId)
      return garage ? formatFinanceGarageLabel(garage.number) : formatFinanceGarageLabel(garageId)
    }
    const formatIncomeType = (incomeTypeId: string | null | undefined, fallbackName: string | null | undefined) => {
      if (!incomeTypeId) {
        return 'пусто'
      }

      if (fallbackName) {
        return fallbackName
      }

      return incomeTypes.find((item) => item.id === incomeTypeId)?.name ?? fallbackName ?? incomeTypeId
    }

    appendChangePreview(changes, 'Гараж', formatIncomeGarage(record.garageId, record.garageNumber), formatIncomeGarage(request.garageId, request.garageId === record.garageId ? record.garageNumber : null))
    appendChangePreview(changes, 'Вид поступления', formatIncomeType(record.incomeTypeId, record.incomeTypeName), formatIncomeType(request.incomeTypeId, request.incomeTypeId === record.incomeTypeId ? record.incomeTypeName : null))
    appendChangePreview(changes, 'Дата поступления', formatChangeDate(record.operationDate), formatChangeDate(request.operationDate))
    appendChangePreview(changes, 'Месяц поступления', formatMonth(record.accountingMonth), formatMonth(request.accountingMonth))
    appendChangePreview(changes, 'Сумма', formatChangeMoney(record.amount), formatChangeMoney(request.amount))
    appendChangePreview(changes, 'Документ', formatChangeText(record.documentNumber), formatChangeText(request.documentNumber))
    appendChangePreview(changes, 'Комментарий', formatChangeText(record.comment), formatChangeText(request.comment))
    return changes
  }

  function getExpenseEditChangePreview(record: FinancialOperationDto, request: CreateExpenseOperationRequest) {
    const changes: ChangePreview[] = []
    const formatSupplier = (supplierId: string | null | undefined, fallbackName: string | null | undefined) => {
      if (!supplierId) {
        return 'пусто'
      }

      if (fallbackName) {
        return fallbackName
      }

      return suppliers.find((item) => item.id === supplierId)?.name ?? supplierId
    }
    const formatExpenseType = (expenseTypeId: string | null | undefined, fallbackName: string | null | undefined) => {
      if (!expenseTypeId) {
        return 'пусто'
      }

      if (fallbackName) {
        return fallbackName
      }

      return expenseTypes.find((item) => item.id === expenseTypeId)?.name ?? expenseTypeId
    }

    appendChangePreview(changes, 'Поставщик', formatSupplier(record.supplierId, record.supplierName), formatSupplier(request.supplierId, request.supplierId === record.supplierId ? record.supplierName : null))
    appendChangePreview(changes, 'Услуга', formatExpenseType(record.expenseTypeId, record.expenseTypeName), formatExpenseType(request.expenseTypeId, request.expenseTypeId === record.expenseTypeId ? record.expenseTypeName : null))
    appendChangePreview(changes, 'Источник выплаты', record.expensePaymentSource === 'cash' || (!record.expensePaymentSource && record.expensePaymentType === 'without_receipt') ? 'Касса' : 'Банк', request.expensePaymentSource === 'cash' ? 'Касса' : 'Банк')
    appendChangePreview(changes, 'Тип выплаты', formatExpensePaymentType(record.expensePaymentType), formatExpensePaymentType(request.expensePaymentType))
    appendChangePreview(
      changes,
      'Фонд расходования',
      formatChangeText(record.expenseFundName),
      formatChangeText(expenseFundOptions.find((fund) => fund.id === request.expenseFundId)?.name ?? request.expenseFundId),
    )
    appendChangePreview(changes, 'Дата выплаты', formatChangeDate(record.operationDate), formatChangeDate(request.operationDate))
    appendChangePreview(changes, 'Месяц выплаты', formatMonth(record.accountingMonth), formatMonth(request.accountingMonth))
    appendChangePreview(changes, 'Сумма', formatChangeMoney(record.amount), formatChangeMoney(request.amount))
    appendChangePreview(changes, 'Документ', formatChangeText(record.documentNumber), formatChangeText(request.documentNumber))
    appendChangePreview(changes, 'Комментарий', formatChangeText(record.comment), formatChangeText(request.comment))
    return changes
  }

  function getAccrualEditChangePreview(record: AccrualDto, request: CreateAccrualRequest) {
    const changes: ChangePreview[] = []
    const formatAccrualGarage = (garageId: string | null | undefined, fallbackGarageNumber: string | null | undefined) => {
      if (!garageId) {
        return 'пусто'
      }

      if (fallbackGarageNumber) {
        return formatFinanceGarageLabel(fallbackGarageNumber)
      }

      const garage = incomeGarageOptions.find((item) => item.id === garageId)
      return garage ? formatFinanceGarageLabel(garage.number) : formatFinanceGarageLabel(garageId)
    }
    const formatAccrualIncomeType = (incomeTypeId: string | null | undefined, fallbackName: string | null | undefined) => {
      if (!incomeTypeId) {
        return 'пусто'
      }

      if (fallbackName) {
        return fallbackName
      }

      return incomeTypes.find((item) => item.id === incomeTypeId)?.name ?? incomeTypeId
    }

    appendChangePreview(changes, 'Гараж', formatAccrualGarage(record.garageId, record.garageNumber), formatAccrualGarage(request.garageId, request.garageId === record.garageId ? record.garageNumber : null))
    appendChangePreview(changes, 'Вид начисления', formatAccrualIncomeType(record.incomeTypeId, record.incomeTypeName), formatAccrualIncomeType(request.incomeTypeId, request.incomeTypeId === record.incomeTypeId ? record.incomeTypeName : null))
    appendChangePreview(changes, 'Месяц начисления', formatMonth(record.accountingMonth), formatMonth(request.accountingMonth))
    appendChangePreview(changes, 'Сумма', formatChangeMoney(record.amount), formatChangeMoney(request.amount))
    appendChangePreview(changes, 'Источник', formatAccrualSource(record.source), formatAccrualSource(request.source))
    appendChangePreview(changes, 'Комментарий', formatChangeText(record.comment), formatChangeText(request.comment))
    return changes
  }

  function getSupplierAccrualEditChangePreview(record: SupplierAccrualDto, request: CreateSupplierAccrualRequest) {
    const changes: ChangePreview[] = []
    const formatSupplier = (supplierId: string | null | undefined, fallbackName: string | null | undefined) => {
      if (!supplierId) {
        return 'пусто'
      }

      if (fallbackName) {
        return fallbackName
      }

      return suppliers.find((item) => item.id === supplierId)?.name ?? supplierId
    }
    const formatExpenseType = (expenseTypeId: string | null | undefined, fallbackName: string | null | undefined) => {
      if (!expenseTypeId) {
        return 'пусто'
      }

      if (fallbackName) {
        return fallbackName
      }

      return expenseTypes.find((item) => item.id === expenseTypeId)?.name ?? expenseTypeId
    }

    appendChangePreview(changes, 'Поставщик', formatSupplier(record.supplierId, record.supplierName), formatSupplier(request.supplierId, request.supplierId === record.supplierId ? record.supplierName : null))
    appendChangePreview(changes, 'Вид начисления', formatExpenseType(record.expenseTypeId, record.expenseTypeName), formatExpenseType(request.expenseTypeId, request.expenseTypeId === record.expenseTypeId ? record.expenseTypeName : null))
    appendChangePreview(changes, 'Месяц начисления', formatMonth(record.accountingMonth), formatMonth(request.accountingMonth))
    appendChangePreview(changes, 'Сумма', formatChangeMoney(record.amount), formatChangeMoney(request.amount))
    appendChangePreview(changes, 'Источник', formatAccrualSource(record.source), formatAccrualSource(request.source))
    appendChangePreview(changes, 'Документ', formatChangeText(record.documentNumber), formatChangeText(request.documentNumber))
    appendChangePreview(changes, 'Комментарий', formatChangeText(record.comment), formatChangeText(request.comment))
    return changes
  }

  async function confirmPendingFinanceEdit() {
    if (!pendingFinanceEditConfirmation) {
      return
    }

    const pending = pendingFinanceEditConfirmation
    const saved = await runSaving(pending.kind, async () => {
      if (pending.kind === 'income') {
        await financeClient.updateIncome(auth.accessToken, pending.recordId, pending.request as CreateIncomeOperationRequest)
        setIncomeForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
        refreshFinanceWorkbenchAfterSave('income')
      } else if (pending.kind === 'expense') {
        await financeClient.updateExpense(auth.accessToken, pending.recordId, pending.request as CreateExpenseOperationRequest)
        setExpenseForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
        refreshFinanceWorkbenchAfterSave('expense')
      } else if (pending.kind === 'accrual') {
        await financeClient.updateAccrual(auth.accessToken, pending.recordId, pending.request as CreateAccrualRequest)
        setAccrualForm((value) => ({ ...value, amount: 0, comment: '' }))
        refreshFinanceWorkbenchAfterSave('accruals')
      } else {
        await financeClient.updateSupplierAccrual(auth.accessToken, pending.recordId, pending.request as CreateSupplierAccrualRequest)
        setSupplierAccrualForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
        refreshFinanceWorkbenchAfterSave('supplierAccruals')
      }
    })
    if (saved) {
      setPendingFinanceEditConfirmation(null)
      closeFinanceEditor({ skipConfirmation: true })
    }
  }

  async function saveIncome(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWritePayments) {
      setError('Для записи платежей нужно право payments.write.')
      return
    }

    const request: CreateIncomeOperationRequest = {
      garageId: incomeForm.garageId,
      incomeTypeId: incomeForm.incomeTypeId,
      operationDate: incomeForm.operationDate,
      accountingMonth: incomeForm.accountingMonth,
      amount: incomeForm.amount,
      documentNumber: incomeForm.documentNumber,
      comment: incomeForm.comment,
    }
    const errors = getIncomeValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setIncomeValidationErrors(errors)
      return
    }

    setIncomeValidationErrors([])
    if (financeEditor?.mode === 'edit' && financeEditor.record && 'operationKind' in financeEditor.record) {
      const changes = getIncomeEditChangePreview(financeEditor.record, request)
      if (changes.length === 0) {
        closeFinanceEditor({ skipConfirmation: true })
        return
      }

      setPendingFinanceEditConfirmation({
        kind: 'income',
        recordId: financeEditor.record.id,
        objectName: `${financeEditor.record.incomeTypeName ?? 'Поступление'} · ${formatFinanceGarageLabel(financeEditor.record.garageNumber)} · ${formatChangeMoney(financeEditor.record.amount)}`,
        request,
        changes,
      })
      return
    }

    const saved = await runSaving('income', async () => {
      await financeClient.createIncome(auth.accessToken, request)
      setIncomeForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
      refreshFinanceWorkbenchAfterSave('income')
    })
    if (saved) {
      closeFinanceEditor({ skipConfirmation: true })
    }
  }

  async function saveExpense(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWritePayments) {
      setError('Для записи платежей нужно право payments.write.')
      return
    }

    const request: CreateExpenseOperationRequest = {
      supplierId: expenseForm.supplierId,
      expenseTypeId: expenseForm.expenseTypeId,
      expensePaymentType: expenseForm.expensePaymentType,
      expensePaymentSource: expenseForm.expensePaymentSource,
      expenseFundId: expenseForm.expenseFundId || undefined,
      operationDate: expenseForm.operationDate,
      accountingMonth: expenseForm.accountingMonth,
      amount: expenseForm.amount,
      documentNumber: expenseForm.documentNumber,
      comment: expenseForm.comment,
    }
    const errors = getExpenseValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setExpenseValidationErrors(errors)
      return
    }

    setExpenseValidationErrors([])
    if (financeEditor?.mode === 'edit' && financeEditor.record && 'operationKind' in financeEditor.record) {
      const changes = getExpenseEditChangePreview(financeEditor.record, request)
      if (changes.length === 0) {
        closeFinanceEditor({ skipConfirmation: true })
        return
      }

      setPendingFinanceEditConfirmation({
        kind: 'expense',
        recordId: financeEditor.record.id,
        objectName: `${financeEditor.record.expenseTypeName ?? 'Выплата'} · ${financeEditor.record.supplierName ?? financeEditor.record.counterpartyName ?? 'Получатель не указан'} · ${formatChangeMoney(financeEditor.record.amount)}`,
        request,
        changes,
      })
      return
    }

    const saved = await runSaving('expense', async () => {
      await financeClient.createExpense(auth.accessToken, request)
      setExpenseForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
      refreshFinanceWorkbenchAfterSave('expense')
    })
    if (saved) {
      closeFinanceEditor({ skipConfirmation: true })
    }
  }

  async function saveAccrual(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWritePayments) {
      setError('Для записи платежей нужно право payments.write.')
      return
    }

    const request: CreateAccrualRequest = {
      garageId: accrualForm.garageId,
      incomeTypeId: accrualForm.incomeTypeId,
      accountingMonth: accrualForm.accountingMonth,
      amount: accrualForm.amount,
      source: accrualForm.source,
      comment: accrualForm.comment,
    }
    const errors = getAccrualValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setAccrualValidationErrors(errors)
      return
    }

    setAccrualValidationErrors([])
    if (financeEditor?.mode === 'edit' && financeEditor.record && 'incomeTypeId' in financeEditor.record && !('operationKind' in financeEditor.record)) {
      const changes = getAccrualEditChangePreview(financeEditor.record, request)
      if (changes.length === 0) {
        closeFinanceEditor({ skipConfirmation: true })
        return
      }

      setPendingFinanceEditConfirmation({
        kind: 'accrual',
        recordId: financeEditor.record.id,
        objectName: `${financeEditor.record.incomeTypeName} · ${formatFinanceGarageLabel(financeEditor.record.garageNumber)} · ${formatChangeMoney(financeEditor.record.amount)}`,
        request,
        changes,
      })
      return
    }

    const saved = await runSaving('accrual', async () => {
      await financeClient.createAccrual(auth.accessToken, request)
      setAccrualForm((value) => ({ ...value, amount: 0, comment: '' }))
      refreshFinanceWorkbenchAfterSave('accruals')
    })
    if (saved) {
      closeFinanceEditor({ skipConfirmation: true })
    }
  }

  async function saveSupplierAccrual(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWritePayments) {
      setError('Для записи платежей нужно право payments.write.')
      return
    }

    const request: CreateSupplierAccrualRequest = {
      supplierId: supplierAccrualForm.supplierId,
      expenseTypeId: supplierAccrualForm.expenseTypeId,
      accountingMonth: supplierAccrualForm.accountingMonth,
      amount: supplierAccrualForm.amount,
      source: supplierAccrualForm.source,
      documentNumber: supplierAccrualForm.documentNumber,
      comment: supplierAccrualForm.comment,
    }
    const errors = getSupplierAccrualValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setSupplierAccrualValidationErrors(errors)
      return
    }

    setSupplierAccrualValidationErrors([])
    if (financeEditor?.mode === 'edit' && financeEditor.record && 'supplierId' in financeEditor.record && !('operationKind' in financeEditor.record)) {
      const changes = getSupplierAccrualEditChangePreview(financeEditor.record, request)
      if (changes.length === 0) {
        closeFinanceEditor({ skipConfirmation: true })
        return
      }

      setPendingFinanceEditConfirmation({
        kind: 'supplier-accrual',
        recordId: financeEditor.record.id,
        objectName: `${financeEditor.record.expenseTypeName} · ${financeEditor.record.supplierName} · ${formatChangeMoney(financeEditor.record.amount)}`,
        request,
        changes,
      })
      return
    }

    const saved = await runSaving('supplier-accrual', async () => {
      await financeClient.createSupplierAccrual(auth.accessToken, request)
      setSupplierAccrualForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
      refreshFinanceWorkbenchAfterSave('supplierAccruals')
    })
    if (saved) {
      closeFinanceEditor({ skipConfirmation: true })
    }
  }

  function selectSupplierForAccrual(supplierId: string) {
    const supplier = suppliers.find((item) => item.id === supplierId)
    setSupplierAccrualForm((value) => ({
      ...value,
      supplierId,
      expenseTypeId: getSupplierAccrualExpenseType(supplier, expenseTypes)?.id ?? '',
    }))
    setSupplierAccrualValidationErrors([])
  }

  async function saveSupplierGroupSalaryAccruals(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWritePayments) {
      setError('Для начисления зарплаты нужно право payments.write.')
      return
    }

    const request: GenerateSupplierGroupSalaryAccrualsRequest = {
      supplierGroupId: salaryForm.supplierGroupId,
      accountingMonth: salaryForm.accountingMonth,
      amount: salaryForm.amount,
      documentNumber: salaryForm.documentNumber,
      comment: salaryForm.comment,
    }
    const errors = getSupplierGroupSalaryValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setSalaryValidationErrors(errors)
      return
    }

    setSalaryValidationErrors([])
    const saved = await runSaving('salary-accruals', async () => {
      const result = await financeClient.generateSupplierGroupSalaryAccruals(auth.accessToken, request)
      setSupplierAccruals((items) => [...result.createdAccruals, ...items])
      setSalaryStatus(`Создано ${result.createdCount}, пропущено ${result.skippedCount}`)
      setSalaryForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
      refreshFinanceWorkbenchAfterSave('supplierAccruals', 0)
    })
    if (saved) {
      closeFinanceEditor({ skipConfirmation: true })
      setActiveFinanceSection('supplierAccruals')
    }
  }

  async function saveMeterReading(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWritePayments) {
      setError('Для записи платежей нужно право payments.write.')
      return
    }

    const request: CreateMeterReadingRequest = {
      garageId: meterForm.garageId,
      meterKind: meterForm.meterKind,
      accountingMonth: meterForm.accountingMonth,
      readingDate: meterForm.readingDate,
      currentValue: meterForm.currentValue,
      comment: meterForm.comment,
    }
    const errors = getMeterReadingValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setMeterValidationErrors(errors)
      return
    }

    setMeterValidationErrors([])
    const saved = await runSaving('meter-reading', async () => {
      if (financeEditor?.mode === 'edit' && financeEditor.record && 'meterKind' in financeEditor.record) {
        await financeClient.updateMeterReading(auth.accessToken, financeEditor.record.id, request)
      } else {
        await financeClient.createMeterReading(auth.accessToken, request)
      }
      setMeterForm((value) => ({ ...value, currentValue: 0, comment: '' }))
      refreshFinanceWorkbenchAfterSave('meterReadings')
    })
    if (saved) {
      closeFinanceEditor({ skipConfirmation: true })
    }
  }

  function openAccrualBreakdown(value: AccrualBreakdown) {
    setAccrualBreakdown(value)
  }

  function handleAccrualBreakdownKeyDown(event: KeyboardEvent<HTMLElement>, value: AccrualBreakdown) {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      openAccrualBreakdown(value)
    }
  }

  function openCancelFinanceDialog(section: FinanceSectionKey, record: FinanceRecord, trigger?: HTMLElement | null) {
    if (!canWritePayments) {
      setError('Для отмены платежей, начислений и показаний нужно право payments.write.')
      return
    }

    cancelFinanceTriggerRef.current = trigger ?? null
    setError(null)
    setCancelFinanceReasonError(null)
    setCancelFinanceTarget({ section, record, reason: '' })
  }

  function getCancelFinanceSavingScope(target: CancelFinanceTarget) {
    if (target.section === 'income' || target.section === 'expense') {
      return `cancel-${target.record.id}`
    }
    if (target.section === 'accruals') {
      return `cancel-accrual-${target.record.id}`
    }
    if (target.section === 'supplierAccruals') {
      return `cancel-supplier-accrual-${target.record.id}`
    }
    return `cancel-meter-reading-${target.record.id}`
  }

  function getRestoreFinanceSavingScope(target: RestoreFinanceTarget) {
    if (target.section === 'income' || target.section === 'expense') {
      return `restore-finance-operation-${target.record.id}`
    }
    if (target.section === 'accruals') {
      return `restore-finance-accrual-${target.record.id}`
    }
    if (target.section === 'supplierAccruals') {
      return `restore-finance-supplier-accrual-${target.record.id}`
    }
    return `restore-finance-meter-reading-${target.record.id}`
  }

  function getCancelFinanceTitle(target: CancelFinanceTarget) {
    if (target.section === 'income') {
      return 'Отменить поступление?'
    }
    if (target.section === 'expense') {
      return 'Отменить выплату?'
    }
    if (target.section === 'accruals') {
      return 'Отменить начисление владельцу?'
    }
    if (target.section === 'supplierAccruals') {
      return 'Отменить начисление поставщику?'
    }
    return 'Отменить показание счетчика?'
  }

  function getRestoreFinanceTitle(target: RestoreFinanceTarget) {
    if (target.section === 'income') {
      return 'Вернуть поступление?'
    }
    if (target.section === 'expense') {
      return 'Вернуть выплату?'
    }
    if (target.section === 'accruals') {
      return 'Вернуть начисление владельцу?'
    }
    if (target.section === 'supplierAccruals') {
      return 'Вернуть начисление поставщику?'
    }
    return 'Вернуть показание счетчика?'
  }

  function getCancelFinanceObjectLabel(target: { record: FinanceRecord }) {
    const record = target.record
    if ('operationKind' in record) {
      const name = record.operationKind === 'income' ? record.incomeTypeName : record.expenseTypeName
      const counterparty = record.operationKind === 'income' ? formatFinanceGarageLabel(record.garageNumber) : record.supplierName ?? record.counterpartyName
      return `${name ?? 'Операция'} · ${counterparty ?? 'контрагент не указан'} · ${formatMoney(record.amount)}`
    }
    if ('meterKind' in record) {
      return `${getFinanceMeterKindLabel(record.meterKind)} · ${formatFinanceGarageLabel(record.garageNumber)} · ${formatMonth(record.accountingMonth)}`
    }
    if ('supplierName' in record) {
      const recipient = record.supplierName ?? ('counterpartyName' in record ? record.counterpartyName : null) ?? 'Получатель не указан'
      return `${record.expenseTypeName} · ${recipient} · ${formatMoney(record.amount)}`
    }
    return `${record.basis ?? record.incomeTypeName} · ${formatFinanceGarageLabel(record.garageNumber)} · ${formatMoney(record.amount)}`
  }

  async function confirmRestoreFinanceRecord() {
    if (!restoreFinanceTarget) {
      return
    }

    const target = restoreFinanceTarget
    const saved = await runSaving(getRestoreFinanceSavingScope(target), async () => {
      if (target.section === 'income' || target.section === 'expense') {
        const operation = target.record as FinancialOperationDto
        await financeClient.restoreOperation(auth.accessToken, operation.id)
        refreshFinanceWorkbenchAfterSave(operation.operationKind === 'income' ? 'income' : 'expense')
      } else if (target.section === 'accruals') {
        await financeClient.restoreAccrual(auth.accessToken, target.record.id)
        refreshFinanceWorkbenchAfterSave('accruals')
      } else if (target.section === 'supplierAccruals') {
        await financeClient.restoreSupplierAccrual(auth.accessToken, target.record.id)
        refreshFinanceWorkbenchAfterSave('supplierAccruals')
      } else {
        await financeClient.restoreMeterReading(auth.accessToken, target.record.id)
        refreshFinanceWorkbenchAfterSave('meterReadings')
      }
    })

    if (saved) {
      closeRestoreFinanceDialog()
    }
  }

  async function confirmCancelFinanceRecord() {
    if (!cancelFinanceTarget) {
      return
    }

    const reason = cancelFinanceTarget.reason.trim()
    if (!reason) {
      setCancelFinanceReasonError('Укажите причину отмены.')
      return
    }

    const target = cancelFinanceTarget
    const saved = await runSaving(getCancelFinanceSavingScope(target), async () => {
      if (target.section === 'income' || target.section === 'expense') {
        const operation = target.record as FinancialOperationDto
        await financeClient.cancelOperation(auth.accessToken, operation.id, { reason })
        refreshFinanceWorkbenchAfterSave(operation.operationKind === 'income' ? 'income' : 'expense')
      } else if (target.section === 'accruals') {
        await financeClient.cancelAccrual(auth.accessToken, target.record.id, { reason })
        refreshFinanceWorkbenchAfterSave('accruals')
      } else if (target.section === 'supplierAccruals') {
        await financeClient.cancelSupplierAccrual(auth.accessToken, target.record.id, { reason })
        refreshFinanceWorkbenchAfterSave('supplierAccruals')
      } else {
        await financeClient.cancelMeterReading(auth.accessToken, target.record.id, { reason })
        refreshFinanceWorkbenchAfterSave('meterReadings')
      }
    })

    if (saved) {
      closeCancelFinanceDialog()
    }
  }

  async function runSaving(scope: string, action: () => Promise<void>) {
    setSaving(scope)
    setError(null)
    try {
      await action()
      return true
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось сохранить финансовую операцию.')
      return false
    } finally {
      setSaving(null)
    }
  }

  async function openFinanceEditor(section: FinanceEditorKey, record?: FinanceRecord, trigger?: HTMLElement | null) {
    if (!canWritePayments) {
      setFinanceContextMenu(null)
      setError('Для записи платежей нужно право payments.write.')
      return
    }

    const [bundleReady, garageReferences] = await Promise.all([
      section !== 'meterReadings' ? ensureFinanceReferenceBundle() : true,
      section === 'income' || section === 'accruals' || section === 'meterReadings' ? ensureFinanceGarageReferences() : [],
    ])
    if (!bundleReady || !garageReferences) {
      return
    }
    const defaultGarageId = garageReferences[0]?.id || ''

    financeEditorTriggerRef.current = trigger ?? null
    setError(null)
    setSalaryStatus(null)
    setIncomeValidationErrors([])
    setExpenseValidationErrors([])
    setAccrualValidationErrors([])
    setSupplierAccrualValidationErrors([])
    setSalaryValidationErrors([])
    setMeterValidationErrors([])
    let initialSnapshot = ''
    if (record && section === 'income' && 'operationKind' in record) {
      if (record.garageId) {
        const garageId = record.garageId
        setIncomeGarageOptions((items) => (items.some((garage) => garage.id === garageId)
          ? items
          : [{
              id: garageId,
              version: '',
              number: record.garageNumber ?? 'без номера',
              ownerId: null,
              ownerName: record.ownerName,
              ownerPhone: null,
              peopleCount: 0,
              floorCount: 0,
              startingBalance: 0,
              startingOverdueDebt: 0,
              balance: 0,
              overdueDebt: 0,
              initialWaterMeterValue: null,
              initialElectricityMeterValue: null,
              comment: null,
              isArchived: false,
            }, ...items]))
      }
      const nextForm = {
        garageId: record.garageId ?? '',
        incomeTypeId: record.incomeTypeId ?? '',
        operationDate: record.operationDate,
        accountingMonth: record.accountingMonth,
        amount: record.amount,
        documentNumber: record.documentNumber ?? '',
        comment: record.comment ?? '',
      }
      setIncomeForm(nextForm)
      initialSnapshot = JSON.stringify(nextForm)
    } else if (!record && section === 'income') {
      const nextForm = { ...incomeForm, garageId: incomeForm.garageId || defaultGarageId, amount: 0, documentNumber: '', comment: '' }
      setIncomeForm(nextForm)
      initialSnapshot = JSON.stringify(nextForm)
    } else if (record && section === 'expense' && 'operationKind' in record) {
      const nextForm = {
        supplierId: record.supplierId ?? '',
        expenseTypeId: record.expenseTypeId ?? '',
        expensePaymentType: record.expensePaymentType ?? 'with_receipt',
        expensePaymentSource: record.expensePaymentSource ?? (record.expensePaymentType === 'without_receipt' ? 'cash' : 'bank'),
        expenseFundId: record.expenseFundId ?? suppliers.find((supplier) => supplier.id === record.supplierId)?.expenseFundId ?? '',
        operationDate: record.operationDate,
        accountingMonth: record.accountingMonth,
        amount: record.amount,
        documentNumber: record.documentNumber ?? '',
        comment: record.comment ?? '',
      }
      setExpenseForm(nextForm)
      initialSnapshot = JSON.stringify(nextForm)
    } else if (!record && section === 'expense') {
      const nextForm = { ...expenseForm, amount: 0, documentNumber: '', comment: '' }
      setExpenseForm(nextForm)
      initialSnapshot = JSON.stringify(nextForm)
    } else if (record && section === 'accruals' && 'incomeTypeId' in record && !('operationKind' in record)) {
      const editableSource: 'manual' | 'regular' = record.source === 'regular' ? 'regular' : 'manual'
      const nextForm = {
        garageId: record.garageId,
        incomeTypeId: record.incomeTypeId,
        accountingMonth: record.accountingMonth,
        amount: record.amount,
        source: editableSource,
        comment: record.comment ?? '',
      }
      setAccrualForm(nextForm)
      initialSnapshot = JSON.stringify(nextForm)
    } else if (!record && section === 'accruals') {
      const nextForm = { ...accrualForm, garageId: accrualForm.garageId || defaultGarageId, source: 'manual' as const, amount: 0, comment: '' }
      setAccrualForm(nextForm)
      initialSnapshot = JSON.stringify(nextForm)
    } else if (record && section === 'supplierAccruals' && 'supplierId' in record && !('operationKind' in record)) {
      const linkedExpenseTypeId = getSupplierAccrualExpenseType(
        suppliers.find((supplier) => supplier.id === record.supplierId),
        expenseTypes,
      )?.id ?? ''
      const nextForm = {
        supplierId: record.supplierId,
        expenseTypeId: linkedExpenseTypeId,
        accountingMonth: record.accountingMonth,
        amount: record.amount,
        source: record.source,
        documentNumber: record.documentNumber ?? '',
        comment: record.comment ?? '',
      }
      setSupplierAccrualForm(nextForm)
      initialSnapshot = JSON.stringify(nextForm)
    } else if (!record && section === 'supplierAccruals') {
      const nextForm = { ...supplierAccrualForm, source: 'manual' as const, amount: 0, documentNumber: '', comment: '' }
      setSupplierAccrualForm(nextForm)
      initialSnapshot = JSON.stringify(nextForm)
    } else if (!record && section === 'supplierGroupSalaryAccruals') {
      const nextForm = { ...salaryForm, amount: 0, documentNumber: '', comment: '' }
      setSalaryForm(nextForm)
      initialSnapshot = JSON.stringify(nextForm)
    } else if (record && section === 'meterReadings' && 'meterKind' in record) {
      const nextForm = {
        garageId: record.garageId,
        meterKind: record.meterKind,
        accountingMonth: record.accountingMonth,
        readingDate: record.readingDate,
        currentValue: record.currentValue,
        comment: record.comment ?? '',
      }
      setMeterForm(nextForm)
      initialSnapshot = JSON.stringify(nextForm)
    } else if (!record && section === 'meterReadings') {
      const nextForm = { ...meterForm, garageId: meterForm.garageId || defaultGarageId }
      setMeterForm(nextForm)
      initialSnapshot = JSON.stringify(nextForm)
    }
    setFinanceEditorInitialSnapshot(initialSnapshot || getFinanceEditorFormSnapshot(section))
    setFinanceEditor({ section, mode: record ? 'edit' : 'create', record })
  }

  function openFinanceContextMenu(event: MouseEvent<HTMLElement>, section: FinanceSectionKey, record?: FinanceRecord) {
    event.preventDefault()
    event.stopPropagation()
    if (loading) {
      return
    }

    financeContextMenuTriggerRef.current = record ? event.currentTarget : null
    setFinanceContextMenu({ section, record, x: event.clientX, y: event.clientY })
  }

  function selectFinanceSection(section: FinanceSectionKey) {
    if (section === activeFinanceSection) {
      return
    }

    financeWorkbenchRequests.invalidate()
    setFinanceContextMenu(null)
    setWorkbenchLoading(true)
    setFinancePage((current) => ({ items: [], totalCount: 0, offset: 0, limit: current.limit }))
    setActiveFinanceSection(section)
  }

  function editFinanceRecord(section: FinanceSectionKey, record: FinanceRecord, trigger?: HTMLElement | null) {
    if (loading) {
      return
    }

    setFinanceContextMenu(null)
    financeContextMenuTriggerRef.current = null
    void openFinanceEditor(section, record, trigger)
  }

  function addFinanceRecord(section: FinanceSectionKey) {
    setFinanceContextMenu(null)
    void openFinanceEditor(section)
  }

  function deleteFinanceRecord(section: FinanceSectionKey, record: FinanceRecord) {
    const trigger = financeContextMenuTriggerRef.current
    setFinanceContextMenu(null)
    financeContextMenuTriggerRef.current = null
    openCancelFinanceDialog(section, record, trigger)
  }

  function restoreFinanceRecord(section: FinanceSectionKey, record: FinanceRecord) {
    if (!canWritePayments) {
      setFinanceContextMenu(null)
      setError('Для восстановления платежей, начислений и показаний нужно право payments.write.')
      return
    }

    restoreFinanceTriggerRef.current = financeContextMenuTriggerRef.current
    setFinanceContextMenu(null)
    financeContextMenuTriggerRef.current = null
    setError(null)
    setRestoreFinanceTarget({ section, record })
  }

  function handleFinanceRowKeyDown(event: KeyboardEvent<HTMLElement>, section: FinanceSectionKey, record: FinanceRecord) {
    if (loading) {
      return
    }

    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      editFinanceRecord(section, record, event.currentTarget)
    } else if (event.key === 'ContextMenu' || (event.shiftKey && event.key === 'F10')) {
      event.preventDefault()
      const rect = event.currentTarget.getBoundingClientRect()
      financeContextMenuTriggerRef.current = event.currentTarget
      setFinanceContextMenu({
        section,
        record,
        x: rect.left,
        y: rect.top + rect.height / 2,
      })
    }
  }

  function handleFinanceTableAreaKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (loading || event.target !== event.currentTarget || (event.key !== 'ContextMenu' && !(event.shiftKey && event.key === 'F10'))) {
      return
    }

    event.preventDefault()
    const rect = event.currentTarget.getBoundingClientRect()
    financeContextMenuTriggerRef.current = null
    setFinanceContextMenu({
      section: activeFinanceSection,
      x: rect.left,
      y: rect.top + Math.min(rect.height, 48),
    })
  }

  function handleFinanceContextMenuKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
      return
    }

    const items = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled)'))
    if (items.length === 0) {
      return
    }

    event.preventDefault()
    const currentIndex = items.findIndex((item) => item === document.activeElement)
    if (event.key === 'Home') {
      items[0].focus()
    } else if (event.key === 'End') {
      items[items.length - 1].focus()
    } else if (event.key === 'ArrowDown') {
      items[(currentIndex + 1) % items.length].focus()
    } else {
      items[(currentIndex <= 0 ? items.length : currentIndex) - 1].focus()
    }
  }

  const filteredIncomeOperations = activeFinanceSection === 'income'
    ? (financePage.items as FinancialOperationDto[]).filter((operation) => operation.operationKind === 'income')
    : []
  const filteredExpenseOperations = activeFinanceSection === 'expense'
    ? (financePage.items as FinancialOperationDto[]).filter((operation) => operation.operationKind === 'expense')
    : []
  const filteredAccruals = activeFinanceSection === 'accruals' ? financePage.items as AccrualDto[] : []
  const filteredSupplierAccruals = activeFinanceSection === 'supplierAccruals' ? financePage.items as SupplierAccrualDto[] : []
  const filteredMeterReadings = activeFinanceSection === 'meterReadings' ? financePage.items as MeterReadingDto[] : []

  function getActiveFinanceRowsCount() {
    return financePage.items.length
  }

  function renderFinanceTableHead(section: FinanceSectionKey) {
    return (
      <thead>
        <tr>
          {getFinanceTableHeaders(section).map((header) => (
            <th key={header}>{header}</th>
          ))}
        </tr>
      </thead>
    )
  }

  function renderFinanceTable() {
    if (activeFinanceSection === 'income') {
      return (
        <table className="dictionary-data-table finance-data-table">
          {renderFinanceTableHead('income')}
          <tbody>
            {filteredIncomeOperations.map((operation) => (
              <tr aria-disabled={loading} className="finance-table-row--interactive" key={operation.id} tabIndex={loading ? -1 : 0} onContextMenu={(event) => openFinanceContextMenu(event, 'income', operation)} onClick={(event) => editFinanceRecord('income', operation, event.currentTarget)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'income', operation)}>
                <td>{formatDateOnly(operation.operationDate)}</td>
                <td>{formatMonth(operation.accountingMonth)}</td>
                <td>{formatFinanceGarageLabel(operation.garageNumber)}</td>
                <td>{getFinanceOptionalText(operation.ownerName)}</td>
                <td>{operation.incomeTypeName}</td>
                <td>{getFinanceOptionalText(operation.documentNumber)}</td>
                <td className="money-income">{formatMoney(operation.amount)}</td>
                <td className={operation.garageDebtAfter !== null ? getDebtClassName(operation.garageDebtAfter) : undefined}>{operation.garageDebtAfter !== null ? formatDebtAmount(operation.garageDebtAfter) : getFinanceFallbackLabel('noData')}</td>
                <td>{getFinanceOptionalText(operation.comment, 'noComment')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )
    }

    if (activeFinanceSection === 'expense') {
      return (
        <table className="dictionary-data-table finance-data-table">
          {renderFinanceTableHead('expense')}
          <tbody>
            {filteredExpenseOperations.map((operation) => (
              <tr aria-disabled={loading} className="finance-table-row--interactive" key={operation.id} tabIndex={loading ? -1 : 0} onContextMenu={(event) => openFinanceContextMenu(event, 'expense', operation)} onClick={(event) => editFinanceRecord('expense', operation, event.currentTarget)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'expense', operation)}>
                <td>{formatDateOnly(operation.operationDate)}</td>
                <td>{formatMonth(operation.accountingMonth)}</td>
                <td>{getFinanceOptionalText(operation.supplierName ?? operation.counterpartyName)}</td>
                <td>{operation.expenseTypeName}</td>
                <td>{formatExpensePaymentSource(operation.expensePaymentSource, operation.expensePaymentType)} · {formatExpensePaymentType(operation.expensePaymentType)}</td>
                <td>{getFinanceOptionalText(operation.documentNumber)}</td>
                <td className="money-expense">{formatMoney(operation.amount)}</td>
                <td>{operation.supplierDebtAfter !== null ? formatMoney(operation.supplierDebtAfter) : getFinanceFallbackLabel('noData')}</td>
                <td>{getFinanceOptionalText(operation.comment, 'noComment')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )
    }

    if (activeFinanceSection === 'accruals') {
      return (
        <table className="dictionary-data-table finance-data-table">
          {renderFinanceTableHead('accruals')}
          <tbody>
            {filteredAccruals.map((accrual) => (
              <tr aria-disabled={loading} className="finance-table-row--interactive" key={accrual.id} tabIndex={loading ? -1 : 0} onContextMenu={(event) => openFinanceContextMenu(event, 'accruals', accrual)} onClick={(event) => editFinanceRecord('accruals', accrual, event.currentTarget)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'accruals', accrual)}>
                <td>{formatMonth(accrual.accountingMonth)}</td>
                <td>{accrual.accountingYear ?? '—'}</td>
                <td>{formatFinanceGarageLabel(accrual.garageNumber)}</td>
                <td>{getFinanceOptionalText(accrual.ownerName)}</td>
                <td>{accrual.basis ?? accrual.incomeTypeName}</td>
                <td>{formatAccrualSource(accrual.source)}</td>
                <td className="money-accrual">{formatMoney(accrual.amount)}</td>
                <td>{getFinanceOptionalText(accrual.comment, 'noComment')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )
    }

    if (activeFinanceSection === 'supplierAccruals') {
      return (
        <table className="dictionary-data-table finance-data-table">
          {renderFinanceTableHead('supplierAccruals')}
          <tbody>
            {filteredSupplierAccruals.map((accrual) => (
              <tr aria-disabled={loading} className="finance-table-row--interactive" key={accrual.id} tabIndex={loading ? -1 : 0} onContextMenu={(event) => openFinanceContextMenu(event, 'supplierAccruals', accrual)} onClick={(event) => editFinanceRecord('supplierAccruals', accrual, event.currentTarget)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'supplierAccruals', accrual)}>
                <td>{formatMonth(accrual.accountingMonth)}</td>
                <td>{accrual.supplierName}</td>
                <td>{accrual.expenseTypeName}</td>
                <td>{formatAccrualSource(accrual.source)}</td>
                <td>{getFinanceOptionalText(accrual.documentNumber)}</td>
                <td className="money-expense">{formatMoney(accrual.amount)}</td>
                <td>{getFinanceOptionalText(accrual.comment, 'noComment')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )
    }

    return (
      <>
        {missingMeterReadings.length > 0 ? (
          <p className="empty-state warning-text" role="status" aria-live="polite">
            Нет показаний за {formatMonth(missingMeterReadings[0].accountingMonth)}: {formatMissingMeterReadings(missingMeterReadings)}
          </p>
        ) : null}
        <table className="dictionary-data-table finance-data-table">
          {renderFinanceTableHead('meterReadings')}
          <tbody>
            {filteredMeterReadings.map((reading) => (
              <tr aria-disabled={loading} className="finance-table-row--interactive" key={reading.id} tabIndex={loading ? -1 : 0} onContextMenu={(event) => openFinanceContextMenu(event, 'meterReadings', reading)} onClick={(event) => editFinanceRecord('meterReadings', reading, event.currentTarget)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'meterReadings', reading)}>
                <td>{formatMonth(reading.accountingMonth)}</td>
                <td>{formatDateOnly(reading.readingDate)}</td>
                <td>{formatFinanceGarageLabel(reading.garageNumber)}</td>
                <td>{getFinanceMeterKindLabel(reading.meterKind)}</td>
                <td>{reading.previousValue}</td>
                <td>{reading.currentValue}</td>
                <td>
                  {reading.consumption}
                  {reading.hasGapWarning ? <small className="warning-text">{getFinanceFallbackLabel('meterGapWarning')}</small> : null}
                </td>
                <td>{getFinanceOptionalText(reading.comment, 'noComment')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </>
    )
  }

  function handleFinanceEditorSubmit(event: FormEvent<HTMLFormElement>) {
    if (!financeEditor) {
      event.preventDefault()
      return
    }

    if (financeEditor.section === 'income') {
      void saveIncome(event)
      return
    }
    if (financeEditor.section === 'expense') {
      void saveExpense(event)
      return
    }
    if (financeEditor.section === 'accruals') {
      void saveAccrual(event)
      return
    }
    if (financeEditor.section === 'supplierGroupSalaryAccruals') {
      void saveSupplierGroupSalaryAccruals(event)
      return
    }
    if (financeEditor.section === 'supplierAccruals') {
      void saveSupplierAccrual(event)
      return
    }
    void saveMeterReading(event)
  }

  function renderFinanceEditorFields(section: FinanceEditorKey) {
    const financeField = (key: Parameters<typeof getFinanceEditorFieldLabel>[0], children: ReactNode) => (
      <FormField label={getFinanceEditorFieldLabel(key)}>{children}</FormField>
    )

    if (section === 'income') {
      return (
        <>
          <div className="inline-fields">
            <FormField label={getFinanceEditorFieldLabel('incomeGarageSearch')} className="dictionary-search">
              <span className="field-input-with-icon">
                <Search size={16} aria-hidden="true" />
                <input aria-label={getFinanceToolbarLabel('incomeGarageSearch')} placeholder={getFinanceToolbarLabel('incomeGarageSearchPlaceholder')} value={incomeGarageSearch} onChange={(event) => setIncomeGarageSearch(event.target.value)} />
              </span>
            </FormField>
            <button className="icon-button" type="button" aria-label={getFinanceToolbarLabel('incomeGarageSearchSubmit')} disabled={saving === 'income-garage-search'} onClick={() => void searchIncomeGarages()}>
              <Search size={16} aria-hidden="true" />
            </button>
          </div>
          {incomeGarageSearchStatus ? <p className="form-hint" role="status" aria-live="polite">{incomeGarageSearchStatus}</p> : null}
          {financeField('incomeGarage', (
            <SelectControl aria-label="Гараж для поступления" value={incomeForm.garageId} options={[
              { value: '', label: 'Выберите гараж' },
              ...incomeGarageOptions.map((garage) => ({ value: garage.id, label: garage.ownerName ? `Гараж ${garage.number} - ${garage.ownerName}` : `Гараж ${garage.number}` })),
            ]} onChange={(garageId) => setIncomeForm({ ...incomeForm, garageId })} />
          ))}
          {financeField('incomeType', (
            <SelectControl aria-label="Вид поступления для платежа" value={incomeForm.incomeTypeId} options={[
              { value: '', label: 'Выберите вид' },
              ...incomeTypes.map((item) => ({ value: item.id, label: item.name })),
            ]} onChange={(incomeTypeId) => setIncomeForm({ ...incomeForm, incomeTypeId })} />
          ))}
          <div className="inline-fields">
            {financeField('incomeDate', <LocalizedDatePicker ariaLabel="Дата поступления" mode="date" value={incomeForm.operationDate} onChange={(operationDate) => setIncomeForm({ ...incomeForm, operationDate })} required />)}
            {financeField('incomeMonth', <LocalizedDatePicker ariaLabel="Месяц поступления" mode="month" value={incomeForm.accountingMonth.slice(0, 7)} onChange={(accountingMonth) => setIncomeForm({ ...incomeForm, accountingMonth: `${accountingMonth}-01` })} required />)}
          </div>
          <div className="inline-fields">
            {financeField('incomeAmount', <MoneyInput aria-label="Сумма поступления" min="0.01" value={incomeForm.amount} onValueChange={(amount) => setIncomeForm({ ...incomeForm, amount })} required />)}
            {financeField('incomeDocument', <input aria-label="Документ поступления" placeholder="Номер документа" value={incomeForm.documentNumber} onChange={(event) => setIncomeForm({ ...incomeForm, documentNumber: event.target.value })} />)}
          </div>
          {financeField('incomeComment', <input aria-label="Комментарий поступления" placeholder="Комментарий платежа" value={incomeForm.comment} onChange={(event) => setIncomeForm({ ...incomeForm, comment: event.target.value })} />)}
          <FormValidationSummary title={getFinanceEditorValidationTitle('income')} items={incomeValidationErrors} />
        </>
      )
    }

    if (section === 'expense') {
      return (
        <>
          {financeField('expensePaymentSource', (
            <SelectControl
              aria-label="Источник выплаты"
              value={expenseForm.expensePaymentSource}
              options={[
                { value: 'bank', label: 'Банк · регулярный поставщик' },
                { value: 'cash', label: 'Касса · эпизодическая выплата' },
              ]}
              onChange={(expensePaymentSource) => {
                const source = expensePaymentSource as ExpensePaymentSource
                const supplier = suppliers.find((item) => item.id === expenseForm.supplierId)
                setExpenseForm({
                  ...expenseForm,
                  expensePaymentSource: source,
                  expenseTypeId: getSupplierAccrualExpenseType(supplier, expenseTypes)?.id ?? '',
                  expenseFundId: supplier?.expenseFundId ?? '',
                })
              }} />
          ))}
          {financeField('expenseSupplier', (
            <SelectControl
              aria-label="Поставщик для выплаты"
              value={expenseForm.supplierId}
              options={suppliers
                .filter((supplier) => Boolean(getSupplierAccrualExpenseType(supplier, expenseTypes) && supplier.expenseFundId))
                .map((supplier) => ({ value: supplier.id, label: supplier.name }))}
              onChange={(supplierId) => {
                const supplier = suppliers.find((item) => item.id === supplierId)
                setExpenseForm({
                  ...expenseForm,
                  supplierId,
                  expenseTypeId: getSupplierAccrualExpenseType(supplier, expenseTypes)?.id ?? '',
                  expenseFundId: supplier?.expenseFundId ?? '',
                })
              }} />
          ))}
          {financeField('expenseType', (
            <SelectControl
              aria-label="Услуга выплаты"
              value={expenseForm.expenseTypeId}
              options={expenseForm.expenseTypeId
                ? [{ value: expenseForm.expenseTypeId, label: expenseTypes.find((item) => item.id === expenseForm.expenseTypeId)?.name ?? 'Настроенная услуга' }]
                : [{ value: '', label: 'Услуга не настроена' }]}
              onChange={() => undefined}
              disabled />
          ))}
          {financeField('expensePaymentType', (
            <SelectControl
              aria-label="Тип выплаты"
              value={expenseForm.expensePaymentType}
              options={expensePaymentTypeOptions}
              onChange={(expensePaymentType) => setExpenseForm({ ...expenseForm, expensePaymentType: expensePaymentType as ExpensePaymentType })} />
          ))}
          <p className="form-hint">Фонд расходования: <strong>{selectedExpenseSupplier?.expenseFundName ?? 'не настроен'}</strong>. Источник выплаты: <strong>{expenseForm.expensePaymentSource === 'cash' ? 'касса' : 'банковский счёт'}</strong>.</p>
          <div className="inline-fields">
            {financeField('expenseDate', <LocalizedDatePicker ariaLabel="Дата выплаты" mode="date" value={expenseForm.operationDate} onChange={(operationDate) => setExpenseForm({ ...expenseForm, operationDate })} required />)}
            {financeField('expenseMonth', <LocalizedDatePicker ariaLabel="Месяц выплаты" mode="month" value={expenseForm.accountingMonth.slice(0, 7)} onChange={(accountingMonth) => setExpenseForm({ ...expenseForm, accountingMonth: `${accountingMonth}-01` })} required />)}
          </div>
          <div className="inline-fields">
            {financeField('expenseAmount', <MoneyInput aria-label="Сумма выплаты" min="0.01" value={expenseForm.amount} onValueChange={(amount) => setExpenseForm({ ...expenseForm, amount })} required />)}
            {financeField('expenseDocument', <input aria-label="Документ выплаты" placeholder="Номер документа" value={expenseForm.documentNumber} onChange={(event) => setExpenseForm({ ...expenseForm, documentNumber: event.target.value })} />)}
          </div>
          {financeField('expenseComment', <input aria-label="Комментарий выплаты" placeholder="Комментарий платежа" value={expenseForm.comment} onChange={(event) => setExpenseForm({ ...expenseForm, comment: event.target.value })} />)}
          <FormValidationSummary title={getFinanceEditorValidationTitle('expense')} items={expenseValidationErrors} />
        </>
      )
    }

    if (section === 'accruals') {
      return (
        <>
          {financeField('accrualGarage', (
            <SelectControl aria-label="Гараж для начисления" value={accrualForm.garageId} options={[
              { value: '', label: 'Выберите гараж' },
              ...garages.map((garage) => ({ value: garage.id, label: `Гараж ${garage.number}` })),
            ]} onChange={(garageId) => setAccrualForm({ ...accrualForm, garageId })} />
          ))}
          {financeField('accrualIncomeType', (
            <SelectControl aria-label="Вид начисления" value={accrualForm.incomeTypeId} options={[
              { value: '', label: 'Выберите вид' },
              ...incomeTypes.map((item) => ({ value: item.id, label: item.name })),
            ]} onChange={(incomeTypeId) => setAccrualForm({ ...accrualForm, incomeTypeId })} />
          ))}
          <div className="inline-fields">
          {financeField('accrualMonth', <LocalizedDatePicker ariaLabel="Месяц начисления" mode="month" value={accrualForm.accountingMonth.slice(0, 7)} onChange={(accountingMonth) => setAccrualForm({ ...accrualForm, accountingMonth: `${accountingMonth}-01` })} required />)}
            {financeField('accrualAmount', <MoneyInput aria-label="Сумма начисления" min="0.01" value={accrualForm.amount} onValueChange={(amount) => setAccrualForm({ ...accrualForm, amount })} required />)}
          </div>
          {financeField('accrualSource', <input aria-label="Источник начисления" value={formatAccrualSource(accrualForm.source)} readOnly />)}
          {financeField('accrualComment', <input aria-label="Комментарий к начислению" placeholder="Комментарий" value={accrualForm.comment} onChange={(event) => setAccrualForm({ ...accrualForm, comment: event.target.value })} />)}
          <FormValidationSummary title={getFinanceEditorValidationTitle('accruals')} items={accrualValidationErrors} />
        </>
      )
    }

    if (section === 'supplierAccruals') {
      return (
        <>
          {financeField('supplierAccrualSupplier', (
            <SelectControl aria-label="Поставщик для начисления" value={supplierAccrualForm.supplierId} options={[
              { value: '', label: 'Выберите поставщика' },
              ...suppliers.map((supplier) => ({ value: supplier.id, label: supplier.name })),
            ]} onChange={selectSupplierForAccrual} />
          ))}
          {financeField('supplierAccrualType', (
            <SelectControl aria-label="Услуга начисления поставщику" value={supplierAccrualForm.expenseTypeId} options={supplierAccrualForm.expenseTypeId
              ? [{ value: supplierAccrualForm.expenseTypeId, label: getSupplierAccrualExpenseType(suppliers.find((supplier) => supplier.id === supplierAccrualForm.supplierId), expenseTypes)?.name ?? 'Настроенная услуга' }]
              : [{ value: '', label: 'Для поставщика услуга не настроена' }]} onChange={() => undefined} disabled />
          ))}
          <div className="inline-fields">
          {financeField('supplierAccrualMonth', <LocalizedDatePicker ariaLabel="Месяц начисления поставщику" mode="month" value={supplierAccrualForm.accountingMonth.slice(0, 7)} onChange={(accountingMonth) => setSupplierAccrualForm({ ...supplierAccrualForm, accountingMonth: `${accountingMonth}-01` })} required />)}
            {financeField('supplierAccrualAmount', <MoneyInput aria-label="Сумма начисления поставщику" min="0.01" value={supplierAccrualForm.amount} onValueChange={(amount) => setSupplierAccrualForm({ ...supplierAccrualForm, amount })} required />)}
          </div>
          {financeField('supplierAccrualSource', <input aria-label="Источник начисления поставщику" value={formatAccrualSource(supplierAccrualForm.source)} readOnly />)}
          <div className="inline-fields">
            {financeField('supplierAccrualDocument', <input aria-label="Документ начисления поставщику" placeholder="Номер документа" value={supplierAccrualForm.documentNumber} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, documentNumber: event.target.value })} />)}
            {financeField('supplierAccrualComment', <input aria-label="Комментарий начисления поставщику" placeholder="Комментарий" value={supplierAccrualForm.comment} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, comment: event.target.value })} />)}
          </div>
          <FormValidationSummary title={getFinanceEditorValidationTitle('supplierAccruals')} items={supplierAccrualValidationErrors} />
        </>
      )
    }

    if (section === 'supplierGroupSalaryAccruals') {
      return (
        <>
          {financeField('salaryGroup', (
            <SelectControl aria-label="Группа для зарплаты" value={salaryForm.supplierGroupId} options={[
              { value: '', label: 'Выберите группу' },
              ...supplierGroups.map((group) => ({ value: group.id, label: group.name })),
            ]} onChange={(supplierGroupId) => setSalaryForm({ ...salaryForm, supplierGroupId })} />
          ))}
          <div className="inline-fields">
          {financeField('salaryMonth', <LocalizedDatePicker ariaLabel="Месяц зарплаты" mode="month" value={salaryForm.accountingMonth.slice(0, 7)} onChange={(accountingMonth) => setSalaryForm({ ...salaryForm, accountingMonth: `${accountingMonth}-01` })} required />)}
            {financeField('salaryAmount', <MoneyInput aria-label="Сумма зарплаты" min="0.01" value={salaryForm.amount} onValueChange={(amount) => setSalaryForm({ ...salaryForm, amount })} required />)}
          </div>
          <div className="inline-fields">
            {financeField('salaryDocument', <input aria-label="Документ зарплаты" placeholder="Номер документа" value={salaryForm.documentNumber} onChange={(event) => setSalaryForm({ ...salaryForm, documentNumber: event.target.value })} />)}
            {financeField('salaryComment', <input aria-label="Комментарий зарплаты" placeholder="Комментарий" value={salaryForm.comment} onChange={(event) => setSalaryForm({ ...salaryForm, comment: event.target.value })} />)}
          </div>
          <FormValidationSummary title={getFinanceEditorValidationTitle('supplierGroupSalaryAccruals')} items={salaryValidationErrors} />
          {salaryStatus ? <p className="form-hint">{salaryStatus}</p> : null}
        </>
      )
    }

    return (
      <>
        {financeField('meterGarage', (
          <SelectControl aria-label="Гараж для показания" value={meterForm.garageId} options={[
            { value: '', label: 'Выберите гараж' },
            ...garages.map((garage) => ({ value: garage.id, label: `Гараж ${garage.number}` })),
          ]} onChange={(garageId) => setMeterForm({ ...meterForm, garageId })} />
        ))}
        {financeField('meterKind', (
          <SelectControl aria-label="Тип счетчика" value={meterForm.meterKind} options={[
            { value: 'water', label: 'Вода' },
            { value: 'electricity', label: 'Электричество' },
          ]} onChange={(meterKind) => setMeterForm({ ...meterForm, meterKind })} />
        ))}
        <div className="inline-fields">
          {financeField('meterMonth', <LocalizedDatePicker ariaLabel="Месяц показания" mode="month" value={meterForm.accountingMonth.slice(0, 7)} onChange={(accountingMonth) => setMeterForm({ ...meterForm, accountingMonth: `${accountingMonth}-01` })} required />)}
          {financeField('meterDate', <LocalizedDatePicker ariaLabel="Дата показания" mode="date" value={meterForm.readingDate} onChange={(readingDate) => setMeterForm({ ...meterForm, readingDate })} required />)}
        </div>
        <div className="inline-fields">
          {financeField('meterCurrentValue', <input aria-label="Текущее показание" type="number" min="0" step="0.001" value={meterForm.currentValue} onChange={(event) => setMeterForm({ ...meterForm, currentValue: Number(event.target.value) })} required />)}
          {financeField('meterComment', <input aria-label="Комментарий к показанию" placeholder="Комментарий" value={meterForm.comment} onChange={(event) => setMeterForm({ ...meterForm, comment: event.target.value })} />)}
        </div>
        <FormValidationSummary title={getFinanceEditorValidationTitle('meterReadings')} items={meterValidationErrors} />
      </>
    )
  }

  const financeEditorHasUnsavedChanges = hasUnsavedFinanceEditorChanges()
  const paymentsHeadingStatus = paymentsPrototypeLoading || !paymentDisplaySettingsLoaded
    ? getFinancePanelLabel('loading')
    : showAllGarageOperations
      ? formatFinanceOperationCount(summary.operationCount)
      : null

  return (
    <section className={showAllGarageOperations ? 'finance-panel finance-panel--show-overview' : 'finance-panel'} aria-label={getFinancePanelLabel('section')}>
      <PaymentsPrototypePanel
        auth={auth}
        canWritePayments={canWritePayments}
        dictionaryClient={dictionaryClient}
        expenseTypes={expenseTypes}
        financeClient={financeClient}
        garages={garages}
        incomeTypes={incomeTypes}
        irregularPayments={irregularPayments}
        loading={paymentsPrototypeLoading}
        suppliers={suppliers}
        staffMembers={staffMembers}
        headingStatus={paymentsHeadingStatus}
        headingNotices={(
          <>
            {error ? (
              <AsyncErrorState
                message={error}
                onRetry={() => setFinanceReloadRevision((value) => value + 1)}
                retrying={referencesLoading || workbenchLoading}
              />
            ) : null}
            {paymentDisplaySettingsError ? <FormError>{paymentDisplaySettingsError}</FormError> : null}
            {!canWritePayments ? <p className="form-hint">{getFinancePanelLabel('readOnlyHint')}</p> : null}
          </>
        )}
        onEnsureReferences={ensureFinanceReferenceBundle}
        onOpenDialog={openPaymentsPrototypeDialog}
        refreshRevision={paymentsPrototypeRefreshRevision}
      />

      <div className="summary-strip" aria-label={getFinancePanelLabel('summary')}>
        <div>
          <span>{getFinancePanelLabel('incomeTotal')}</span>
          <strong>{formatMoney(summary.incomeTotal)}</strong>
        </div>
        <div>
          <span>{getFinancePanelLabel('accrualTotal')}</span>
          <strong>{formatMoney(summary.accrualTotal)}</strong>
        </div>
        <div>
          <span>{formatDebtLabel(summary.debt)}</span>
          <strong className={getDebtClassName(summary.debt)}>{formatDebtAmount(summary.debt)}</strong>
        </div>
        <div>
          <span>{getFinancePanelLabel('expenseTotal')}</span>
          <strong>{formatMoney(summary.expenseTotal)}</strong>
        </div>
        <div>
          <span>{getFinancePanelLabel('balance')}</span>
          <strong>{formatMoney(summary.balance)}</strong>
        </div>
        <div>
          <span>{getFinancePanelLabel('meterReadings')}</span>
          <strong>{summary.meterReadingCount}</strong>
        </div>
      </div>

      <div className="finance-workbench">
        <div className="finance-tabs" role="tablist" aria-label={getFinanceToolbarLabel('sectionTabs')}>
          {financeSectionOptions.map((section) => (
            <button
              type="button"
              role="tab"
              aria-selected={activeFinanceSection === section.key}
              className={activeFinanceSection === section.key ? 'is-active' : undefined}
              key={section.key}
              onClick={() => selectFinanceSection(section.key)}
            >
              <span>{section.label}</span>
              <small>{getFinanceSectionDescription(section, financeSectionCounts)}</small>
            </button>
          ))}
        </div>

        <div className="dictionary-toolbar finance-table-toolbar">
          <div className="finance-period-filter" aria-label={getFinanceToolbarLabel('periodFilter')}>
            <LocalizedDatePicker ariaLabel={getFinanceToolbarLabel('periodFrom')} mode="month" value={financeFilter.monthFrom} onChange={(monthFrom) => setFinanceFilter((current) => ({ ...current, monthFrom }))} />
            <LocalizedDatePicker ariaLabel={getFinanceToolbarLabel('periodTo')} mode="month" value={financeFilter.monthTo} onChange={(monthTo) => setFinanceFilter((current) => ({ ...current, monthTo }))} />
          </div>
          <label className="dictionary-search">
            <Search size={16} aria-hidden="true" />
            <input aria-label={getFinanceToolbarLabel('search')} placeholder={getFinanceToolbarLabel('searchPlaceholder')} value={financeSearchInput} onChange={(event) => setFinanceSearchInput(event.target.value)} />
          </label>
          <div className="finance-toolbar-actions">
            {activeFinanceSection === 'supplierAccruals' ? (
              <button className="ghost-button" type="button" disabled={!canWritePayments} onClick={() => void openFinanceEditor('supplierGroupSalaryAccruals')}>
                <span>{getFinanceToolbarLabel('supplierGroupSalaryAccruals')}</span>
              </button>
            ) : null}
          </div>
        </div>

        <div className="dictionary-table-shell">
          <div
            className={`dictionary-table-scroll${loading && getActiveFinanceRowsCount() === 0 ? ' dictionary-table-scroll--loading' : ''}`}
            role="group"
            aria-label={getFinanceToolbarLabel('tableArea')}
            aria-busy={loading}
            tabIndex={getActiveFinanceRowsCount() === 0 ? 0 : -1}
            onContextMenu={(event) => openFinanceContextMenu(event, activeFinanceSection)}
            onKeyDown={handleFinanceTableAreaKeyDown}
          >
            {renderFinanceTable()}
            {loading && getActiveFinanceRowsCount() === 0 ? <TableLoadingState label="Загружаем таблицу платежей" /> : null}
            {loading && getActiveFinanceRowsCount() > 0 ? <BackgroundRefreshStatus label="Обновляем таблицу платежей" /> : null}
            {!loading && getActiveFinanceRowsCount() === 0 ? <StatusMessage>{getFinanceToolbarLabel('emptyState')}</StatusMessage> : null}
          </div>
          <TablePagination
            ariaLabel={getFinanceToolbarLabel('pagination')}
            totalCount={financePage.totalCount}
            offset={financePage.offset}
            limit={financePage.limit}
            visibleCount={getActiveFinanceRowsCount()}
            disabled={loading}
            pageSizeLabel={getFinanceToolbarLabel('pageSize')}
            onPageChange={(page) => void loadFinanceWorkbench(activeFinanceSection, (page - 1) * financePage.limit, financePage.limit)}
            onPageSizeChange={(limit) => void loadFinanceWorkbench(activeFinanceSection, 0, limit)}
          />
        </div>
      </div>

      <div className="finance-grid">
        <form className="dictionary-form" onSubmit={saveIncome}>
          <h3>Новое поступление</h3>
          <div className="inline-fields">
            <label className="dictionary-search">
              <Search size={16} aria-hidden="true" />
              <input aria-label={getFinanceToolbarLabel('incomeGarageSearch')} placeholder={getFinanceToolbarLabel('incomeGarageSearchPlaceholder')} value={incomeGarageSearch} onChange={(event) => setIncomeGarageSearch(event.target.value)} />
            </label>
            <button className="icon-button" type="button" aria-label={getFinanceToolbarLabel('incomeGarageSearchSubmit')} disabled={saving === 'income-garage-search'} onClick={() => void searchIncomeGarages()}>
              <Search size={16} aria-hidden="true" />
            </button>
          </div>
          {incomeGarageSearchStatus ? <p className="form-hint" role="status" aria-live="polite">{incomeGarageSearchStatus}</p> : null}
          <SelectControl aria-label="Гараж для поступления" value={incomeForm.garageId} options={[
            { value: '', label: 'Выберите гараж' },
            ...incomeGarageOptions.map((garage) => ({ value: garage.id, label: garage.ownerName ? `Гараж ${garage.number} - ${garage.ownerName}` : `Гараж ${garage.number}` })),
          ]} onChange={(garageId) => setIncomeForm({ ...incomeForm, garageId })} />
          <SelectControl aria-label="Вид поступления для платежа" value={incomeForm.incomeTypeId} options={[
            { value: '', label: 'Выберите вид' },
            ...incomeTypes.map((item) => ({ value: item.id, label: item.name })),
          ]} onChange={(incomeTypeId) => setIncomeForm({ ...incomeForm, incomeTypeId })} />
          <div className="inline-fields">
            <LocalizedDatePicker ariaLabel="Дата поступления" mode="date" value={incomeForm.operationDate} onChange={(operationDate) => setIncomeForm({ ...incomeForm, operationDate })} required />
            <LocalizedDatePicker ariaLabel="Месяц поступления" mode="month" value={incomeForm.accountingMonth.slice(0, 7)} onChange={(accountingMonth) => setIncomeForm({ ...incomeForm, accountingMonth: `${accountingMonth}-01` })} required />
          </div>
          <div className="inline-fields">
            <MoneyInput aria-label="Сумма поступления" min="0.01" value={incomeForm.amount} onValueChange={(amount) => setIncomeForm({ ...incomeForm, amount })} required />
            <input aria-label="Документ поступления" placeholder="Документ" value={incomeForm.documentNumber} onChange={(event) => setIncomeForm({ ...incomeForm, documentNumber: event.target.value })} />
          </div>
          <input aria-label="Комментарий поступления" placeholder="Комментарий платежа" value={incomeForm.comment} onChange={(event) => setIncomeForm({ ...incomeForm, comment: event.target.value })} />
          <FormValidationSummary title={getFinanceEditorValidationTitle('income')} items={incomeValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'income' || !incomeForm.garageId || !incomeForm.incomeTypeId}>
            <span>Провести</span>
          </button>
        </form>

        <form className="dictionary-form" onSubmit={saveExpense}>
          <h3>Новая выплата</h3>
          <SelectControl
            aria-label="Источник выплаты"
            value={expenseForm.expensePaymentSource}
            options={[
              { value: 'bank', label: 'Банк · регулярный поставщик' },
              { value: 'cash', label: 'Касса · эпизодическая выплата' },
            ]}
            onChange={(expensePaymentSource) => {
              const source = expensePaymentSource as ExpensePaymentSource
              const supplier = suppliers.find((item) => item.id === expenseForm.supplierId)
              setExpenseForm({
                ...expenseForm,
                expensePaymentSource: source,
                expenseTypeId: getSupplierAccrualExpenseType(supplier, expenseTypes)?.id ?? '',
                expenseFundId: supplier?.expenseFundId ?? '',
              })
            }} />
          <SelectControl
            aria-label="Поставщик для выплаты"
            value={expenseForm.supplierId}
            options={suppliers
              .filter((supplier) => Boolean(getSupplierAccrualExpenseType(supplier, expenseTypes) && supplier.expenseFundId))
              .map((supplier) => ({ value: supplier.id, label: supplier.name }))}
            onChange={(supplierId) => {
              const supplier = suppliers.find((item) => item.id === supplierId)
              setExpenseForm({
                ...expenseForm,
                supplierId,
                expenseTypeId: getSupplierAccrualExpenseType(supplier, expenseTypes)?.id ?? '',
                expenseFundId: supplier?.expenseFundId ?? '',
              })
            }} />
          <SelectControl
            aria-label="Услуга выплаты"
            value={expenseForm.expenseTypeId}
            options={expenseForm.expenseTypeId
              ? [{ value: expenseForm.expenseTypeId, label: expenseTypes.find((item) => item.id === expenseForm.expenseTypeId)?.name ?? 'Настроенная услуга' }]
              : [{ value: '', label: 'Услуга не настроена' }]}
            onChange={() => undefined}
            disabled />
          <SelectControl
            aria-label="Тип выплаты"
            value={expenseForm.expensePaymentType}
            options={expensePaymentTypeOptions}
            onChange={(expensePaymentType) => setExpenseForm({ ...expenseForm, expensePaymentType: expensePaymentType as ExpensePaymentType })} />
          <p className="form-hint">Фонд расходования: <strong>{selectedExpenseSupplier?.expenseFundName ?? 'не настроен'}</strong>. Источник выплаты: <strong>{expenseForm.expensePaymentSource === 'cash' ? 'касса' : 'банковский счёт'}</strong>.</p>
          <div className="inline-fields">
            <LocalizedDatePicker ariaLabel="Дата выплаты" mode="date" value={expenseForm.operationDate} onChange={(operationDate) => setExpenseForm({ ...expenseForm, operationDate })} required />
            <LocalizedDatePicker ariaLabel="Месяц выплаты" mode="month" value={expenseForm.accountingMonth.slice(0, 7)} onChange={(accountingMonth) => setExpenseForm({ ...expenseForm, accountingMonth: `${accountingMonth}-01` })} required />
          </div>
          <div className="inline-fields">
            <MoneyInput aria-label="Сумма выплаты" min="0.01" value={expenseForm.amount} onValueChange={(amount) => setExpenseForm({ ...expenseForm, amount })} required />
            <input aria-label="Документ выплаты" placeholder="Документ" value={expenseForm.documentNumber} onChange={(event) => setExpenseForm({ ...expenseForm, documentNumber: event.target.value })} />
          </div>
          <input aria-label="Комментарий выплаты" placeholder="Комментарий платежа" value={expenseForm.comment} onChange={(event) => setExpenseForm({ ...expenseForm, comment: event.target.value })} />
          <FormValidationSummary title={getFinanceEditorValidationTitle('expense')} items={expenseValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'expense' || !expenseForm.supplierId || !expenseForm.expenseTypeId}>
            <span>Провести</span>
          </button>
        </form>

        <form className="dictionary-form" onSubmit={saveAccrual}>
          <h3>Ручное начисление</h3>
          <SelectControl aria-label="Гараж для начисления" value={accrualForm.garageId} options={[
            { value: '', label: 'Выберите гараж' },
            ...garages.map((garage) => ({ value: garage.id, label: `Гараж ${garage.number}` })),
          ]} onChange={(garageId) => setAccrualForm({ ...accrualForm, garageId })} />
          <SelectControl aria-label="Вид начисления" value={accrualForm.incomeTypeId} options={[
            { value: '', label: 'Выберите вид' },
            ...incomeTypes.map((item) => ({ value: item.id, label: item.name })),
          ]} onChange={(incomeTypeId) => setAccrualForm({ ...accrualForm, incomeTypeId })} />
          <div className="inline-fields">
          <LocalizedDatePicker ariaLabel="Месяц начисления" mode="month" value={accrualForm.accountingMonth.slice(0, 7)} onChange={(accountingMonth) => setAccrualForm({ ...accrualForm, accountingMonth: `${accountingMonth}-01` })} required />
            <MoneyInput aria-label="Сумма начисления" min="0.01" value={accrualForm.amount} onValueChange={(amount) => setAccrualForm({ ...accrualForm, amount })} required />
          </div>
          <input aria-label="Комментарий начисления" placeholder="Комментарий (необязательно)" value={accrualForm.comment} onChange={(event) => setAccrualForm({ ...accrualForm, comment: event.target.value })} />
          <FormValidationSummary title={getFinanceEditorValidationTitle('accruals')} items={accrualValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'accrual' || !accrualForm.garageId || !accrualForm.incomeTypeId}>
            <span>Начислить</span>
          </button>
        </form>

        <form className="dictionary-form" onSubmit={saveSupplierAccrual}>
          <h3>Начисление поставщику</h3>
          <SelectControl aria-label="Поставщик для начисления" value={supplierAccrualForm.supplierId} options={[
            { value: '', label: 'Выберите поставщика' },
            ...suppliers.map((supplier) => ({ value: supplier.id, label: supplier.name })),
          ]} onChange={selectSupplierForAccrual} />
          <SelectControl aria-label="Услуга начисления поставщику" value={supplierAccrualForm.expenseTypeId} options={supplierAccrualForm.expenseTypeId
            ? [{ value: supplierAccrualForm.expenseTypeId, label: getSupplierAccrualExpenseType(suppliers.find((supplier) => supplier.id === supplierAccrualForm.supplierId), expenseTypes)?.name ?? 'Настроенная услуга' }]
            : [{ value: '', label: 'Для поставщика услуга не настроена' }]} onChange={() => undefined} disabled />
          <div className="inline-fields">
          <LocalizedDatePicker ariaLabel="Месяц начисления поставщику" mode="month" value={supplierAccrualForm.accountingMonth.slice(0, 7)} onChange={(accountingMonth) => setSupplierAccrualForm({ ...supplierAccrualForm, accountingMonth: `${accountingMonth}-01` })} required />
            <MoneyInput aria-label="Сумма начисления поставщику" min="0.01" value={supplierAccrualForm.amount} onValueChange={(amount) => setSupplierAccrualForm({ ...supplierAccrualForm, amount })} required />
          </div>
          <div className="inline-fields">
            <input aria-label="Документ начисления поставщику" placeholder="Документ" value={supplierAccrualForm.documentNumber} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, documentNumber: event.target.value })} />
            <input aria-label="Комментарий начисления поставщику" placeholder="Комментарий (необязательно)" value={supplierAccrualForm.comment} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, comment: event.target.value })} />
          </div>
          <FormValidationSummary title={getFinanceEditorValidationTitle('supplierAccruals')} items={supplierAccrualValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'supplier-accrual' || !supplierAccrualForm.supplierId || !supplierAccrualForm.expenseTypeId}>
            <span>Начислить</span>
          </button>
        </form>

        <form className="dictionary-form" onSubmit={saveMeterReading}>
          <h3>Показание счетчика</h3>
          <SelectControl aria-label="Гараж для счетчика" value={meterForm.garageId} options={[
            { value: '', label: 'Выберите гараж' },
            ...garages.map((garage) => ({ value: garage.id, label: `Гараж ${garage.number}` })),
          ]} onChange={(garageId) => setMeterForm({ ...meterForm, garageId })} />
          <SelectControl aria-label="Тип счетчика" value={meterForm.meterKind} options={[
            { value: 'water', label: 'Вода' },
            { value: 'electricity', label: 'Электричество' },
          ]} onChange={(meterKind) => setMeterForm({ ...meterForm, meterKind })} />
          <div className="inline-fields">
            <LocalizedDatePicker ariaLabel="Месяц показания" mode="month" value={meterForm.accountingMonth.slice(0, 7)} onChange={(accountingMonth) => setMeterForm({ ...meterForm, accountingMonth: `${accountingMonth}-01` })} required />
            <LocalizedDatePicker ariaLabel="Дата показания" mode="date" value={meterForm.readingDate} onChange={(readingDate) => setMeterForm({ ...meterForm, readingDate })} required />
          </div>
          <div className="inline-fields">
            <input aria-label="Новое показание" type="number" min="0" step="0.001" value={meterForm.currentValue} onChange={(event) => setMeterForm({ ...meterForm, currentValue: Number(event.target.value) })} required />
            <input aria-label="Комментарий счетчика" placeholder="Комментарий" value={meterForm.comment} onChange={(event) => setMeterForm({ ...meterForm, comment: event.target.value })} />
          </div>
          <FormValidationSummary title={getFinanceEditorValidationTitle('meterReadings', 'detailed')} items={meterValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'meter-reading' || !meterForm.garageId}>
            <span>Внести</span>
          </button>
        </form>

        {financePreviewsError ? <FormError>Не удалось загрузить часть последних операций. Основная таблица платежей продолжает работать.</FormError> : null}

        <div className="operation-list" role="table" aria-label={getFinanceVisibleListTableLabel('operations')} aria-busy={financePreviewPending.operations}>
          <div className="operation-row header" role="row">
            {getFinanceVisibleListTableHeaders('operations').map((header) => <span role="columnheader" key={header}>{header}</span>)}
          </div>
          {financePreviewPending.operations && operations.length === 0 ? <TableLoadingState label="Загружаем последние операции" rows={2} columns={3} /> : null}
          {financePreviewPending.operations && operations.length > 0 ? <BackgroundRefreshStatus label="Обновляем последние операции" /> : null}
          {!financePreviewPending.operations && !financePreviewFailures.operations && operations.length === 0 ? <StatusMessage>{getFinanceVisibleListEmptyLabel('operations')}</StatusMessage> : null}
          {operations.length > 0 ? visibleOperations.map((operation) => (
            <div className="operation-row" role="row" key={operation.id}>
              <span role="cell">{formatDateOnly(operation.operationDate)}</span>
              <span role="cell">
                <strong>{operation.operationKind === 'income' ? operation.incomeTypeName : operation.expenseTypeName}</strong>
                <small>{operation.operationKind === 'income' ? `Гараж ${operation.garageNumber}` : operation.supplierName ?? operation.counterpartyName ?? 'Получатель не указан'}</small>
                {operation.operationKind === 'income' && operation.garageDebtBefore !== null && operation.garageDebtAfter !== null ? (
                  <small className="balance-history">Долг: {formatMoney(operation.garageDebtBefore)} → {formatMoney(operation.garageDebtAfter)}</small>
                ) : null}
                {operation.operationKind === 'expense' && operation.supplierDebtBefore !== null && operation.supplierDebtAfter !== null ? (
                  <small className="balance-history">Обязательство: {formatMoney(operation.supplierDebtBefore)} → {formatMoney(operation.supplierDebtAfter)}</small>
                ) : null}
                {operation.paymentAllocations.length > 0 ? (
                  <small className="balance-history">Разбивка: {formatPaymentAllocations(operation.paymentAllocations)}</small>
                ) : null}
              </span>
              <span role="cell" className={`operation-amount ${operation.operationKind === 'income' ? 'money-income' : 'money-expense'}`}>
                {operation.operationKind === 'income' ? '+' : '-'}
                {formatMoney(operation.amount)}
              </span>
            </div>
          )) : null}
          {operations.length > 0 && operationPreviewTotal > visibleOperations.length ? <StatusMessage>{formatFinanceVisibleListStatus(visibleOperations.length, operationPreviewTotal, 'operations')}</StatusMessage> : null}
        </div>

        <div className="operation-list" role="table" aria-label={getFinanceVisibleListTableLabel('accruals')} aria-busy={financePreviewPending.accruals}>
          <div className="operation-row header" role="row">
            {getFinanceVisibleListTableHeaders('accruals').map((header) => <span role="columnheader" key={header}>{header}</span>)}
          </div>
          {financePreviewPending.accruals && accruals.length === 0 ? <TableLoadingState label="Загружаем последние начисления" rows={2} columns={3} /> : null}
          {financePreviewPending.accruals && accruals.length > 0 ? <BackgroundRefreshStatus label="Обновляем последние начисления" /> : null}
          {!financePreviewPending.accruals && !financePreviewFailures.accruals && accruals.length === 0 ? <StatusMessage>{getFinanceVisibleListEmptyLabel('accruals')}</StatusMessage> : null}
          {accruals.length > 0 ? visibleAccruals.map((accrual) => (
            <div
              className="operation-row operation-row--interactive"
              role="row"
              tabIndex={0}
              aria-label={`Разбивка начисления ${accrual.basis ?? accrual.incomeTypeName} гараж ${accrual.garageNumber}`}
              key={accrual.id}
              onDoubleClick={() => openAccrualBreakdown({ kind: 'garage', accrual })}
              onKeyDown={(event) => handleAccrualBreakdownKeyDown(event, { kind: 'garage', accrual })}
            >
              <span role="cell">{formatMonth(accrual.accountingMonth)}</span>
              <span role="cell">
                <strong>{accrual.basis ?? accrual.incomeTypeName}</strong>
                <small>Гараж {accrual.garageNumber}</small>
                {accrual.accountingYear ? <small>Учетный год: {accrual.accountingYear}</small> : null}
                <small>{formatAccrualSource(accrual.source)}</small>
              </span>
              <span role="cell" className="operation-amount money-accrual">
                {formatMoney(accrual.amount)}
              </span>
            </div>
          )) : null}
          {accruals.length > 0 && summary.accrualCount > visibleAccruals.length ? <StatusMessage>{formatFinanceVisibleListStatus(visibleAccruals.length, summary.accrualCount, 'accruals')}</StatusMessage> : null}
        </div>

        <div className="operation-list" role="table" aria-label={getFinanceVisibleListTableLabel('supplierAccruals')} aria-busy={financePreviewPending.supplierAccruals}>
          <div className="operation-row header" role="row">
            {getFinanceVisibleListTableHeaders('supplierAccruals').map((header) => <span role="columnheader" key={header}>{header}</span>)}
          </div>
          {financePreviewPending.supplierAccruals && supplierAccruals.length === 0 ? <TableLoadingState label="Загружаем последние начисления поставщикам" rows={2} columns={3} /> : null}
          {financePreviewPending.supplierAccruals && supplierAccruals.length > 0 ? <BackgroundRefreshStatus label="Обновляем последние начисления поставщикам" /> : null}
          {!financePreviewPending.supplierAccruals && !financePreviewFailures.supplierAccruals && supplierAccruals.length === 0 ? <StatusMessage>{getFinanceVisibleListEmptyLabel('supplierAccruals')}</StatusMessage> : null}
          {supplierAccruals.length > 0 ? visibleSupplierAccruals.map((accrual) => (
            <div
              className="operation-row operation-row--interactive"
              role="row"
              tabIndex={0}
              aria-label={`Разбивка начисления поставщику ${accrual.supplierName}`}
              key={accrual.id}
              onDoubleClick={() => openAccrualBreakdown({ kind: 'supplier', accrual })}
              onKeyDown={(event) => handleAccrualBreakdownKeyDown(event, { kind: 'supplier', accrual })}
            >
              <span role="cell">{formatMonth(accrual.accountingMonth)}</span>
              <span role="cell">
                <strong>{accrual.supplierName}</strong>
                <small>{accrual.expenseTypeName}</small>
                <small>{formatAccrualSource(accrual.source)}</small>
              </span>
              <span role="cell" className="operation-amount money-expense">
                {formatMoney(accrual.amount)}
              </span>
            </div>
          )) : null}
          {supplierAccruals.length > 0 && supplierAccrualPreviewTotal > visibleSupplierAccruals.length ? <StatusMessage>{formatFinanceVisibleListStatus(visibleSupplierAccruals.length, supplierAccrualPreviewTotal, 'supplierAccruals')}</StatusMessage> : null}
        </div>

        <div className="operation-list" role="table" aria-label={getFinanceVisibleListTableLabel('meterReadings')} aria-busy={financePreviewPending.meterReadings}>
          <div className="operation-row header" role="row">
            {getFinanceVisibleListTableHeaders('meterReadings').map((header) => <span role="columnheader" key={header}>{header}</span>)}
          </div>
          {financePreviewPending.meterReadings && meterReadings.length === 0 ? <TableLoadingState label="Загружаем последние показания" rows={2} columns={3} /> : null}
          {financePreviewPending.meterReadings && meterReadings.length > 0 ? <BackgroundRefreshStatus label="Обновляем последние показания" /> : null}
          {!financePreviewPending.meterReadings && !financePreviewFailures.meterReadings && meterReadings.length === 0 ? <StatusMessage>{getFinanceVisibleListEmptyLabel('meterReadings')}</StatusMessage> : null}
          {meterReadings.length > 0 ? visibleMeterReadings.map((reading) => (
            <div className="operation-row" role="row" key={reading.id}>
              <span role="cell">{formatMonth(reading.accountingMonth)}</span>
              <span role="cell">
                <strong>{getFinanceMeterKindLabel(reading.meterKind)}</strong>
                <small>
                  Гараж {reading.garageNumber}: {reading.previousValue} → {reading.currentValue}
                </small>
                {reading.hasGapWarning ? <small className="warning-text">проверьте предыдущий месяц</small> : null}
              </span>
              <span role="cell" className="operation-amount money-accrual">
                {reading.consumption}
              </span>
            </div>
          )) : null}
          {meterReadings.length > 0 && summary.meterReadingCount > visibleMeterReadings.length ? <StatusMessage>{formatFinanceVisibleListStatus(visibleMeterReadings.length, summary.meterReadingCount, 'meterReadings')}</StatusMessage> : null}
        </div>
      </div>
      {financeContextMenu ? (
        <div className="context-menu" style={{ left: financeContextMenu.x, top: financeContextMenu.y }} role="menu" aria-label={getFinanceToolbarLabel('contextMenu')} onClick={(event) => event.stopPropagation()} onKeyDown={handleFinanceContextMenuKeyDown}>
          <div className="context-menu-group" role="group">
            <button ref={financeContextMenuFirstItemRef} type="button" role="menuitem" disabled={!canWritePayments} onClick={() => addFinanceRecord(financeContextMenu.section)}>
              <span>{getFinanceContextMenuLabel('add')}</span>
            </button>
          </div>
          <div className="context-menu-separator" role="separator" />
          <div className="context-menu-group" role="group">
            <button type="button" role="menuitem" disabled={!canWritePayments || !financeContextMenu.record || financeContextMenu.record.isCanceled} onClick={() => financeContextMenu.record ? editFinanceRecord(financeContextMenu.section, financeContextMenu.record, financeContextMenuTriggerRef.current) : undefined}>
              <span>{getFinanceContextMenuLabel('edit')}</span>
            </button>
            <button className="context-menu-danger" type="button" role="menuitem" disabled={!canWritePayments || !financeContextMenu.record || financeContextMenu.record.isCanceled} onClick={() => financeContextMenu.record ? deleteFinanceRecord(financeContextMenu.section, financeContextMenu.record) : undefined}>
              <span>{getFinanceContextMenuLabel('delete')}</span>
            </button>
            <button type="button" role="menuitem" disabled={!canWritePayments || !financeContextMenu.record?.isCanceled} onClick={() => financeContextMenu.record ? restoreFinanceRecord(financeContextMenu.section, financeContextMenu.record) : undefined}>
              <span>{getFinanceContextMenuLabel('restore')}</span>
            </button>
          </div>
        </div>
      ) : null}
      {cancelFinanceTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (saving !== getCancelFinanceSavingScope(cancelFinanceTarget)) {
            closeCancelFinanceDialog()
          }
        }}>
          <section ref={cancelFinanceDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="finance-cancel-title" aria-describedby="finance-cancel-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Отмена записи</p>
                <h3 id="finance-cancel-title">{getCancelFinanceTitle(cancelFinanceTarget)}</h3>
                <p>{getCancelFinanceObjectLabel(cancelFinanceTarget)}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение отмены" onClick={closeCancelFinanceDialog} disabled={saving === getCancelFinanceSavingScope(cancelFinanceTarget)}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p className="confirmation-text" id="finance-cancel-description">Запись будет скрыта из рабочих таблиц как отмененная, но останется в истории изменений и финансовом журнале. Укажите причину, чтобы бухгалтер мог проверить действие позже.</p>
            <FormField label="Причина отмены">
              <textarea
                ref={cancelFinanceReasonRef}
                aria-label="Причина отмены финансовой записи"
                value={cancelFinanceTarget.reason}
                onChange={(event) => {
                  setCancelFinanceReasonError(null)
                  setCancelFinanceTarget((target) => target ? { ...target, reason: event.target.value } : target)
                }}
                placeholder="Например: ошибочный документ, неверная сумма или дубль записи"
                required
              />
            </FormField>
            {cancelFinanceReasonError ? <FormError>{cancelFinanceReasonError}</FormError> : null}
            <div className="detail-dialog-actions">
              <button className="ghost-button" type="button" onClick={closeCancelFinanceDialog} disabled={saving === getCancelFinanceSavingScope(cancelFinanceTarget)}>
                Оставить запись
              </button>
              <button className="secondary-button danger-button" type="button" onClick={() => void confirmCancelFinanceRecord()} disabled={saving === getCancelFinanceSavingScope(cancelFinanceTarget)}>
                <Trash2 size={16} aria-hidden="true" />
                <span>{saving === getCancelFinanceSavingScope(cancelFinanceTarget) ? 'Отменяем...' : 'Отменить запись'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
      {restoreFinanceTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (saving !== getRestoreFinanceSavingScope(restoreFinanceTarget)) {
            closeRestoreFinanceDialog()
          }
        }}>
          <section ref={restoreFinanceDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="finance-restore-title" aria-describedby="finance-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Восстановление записи</p>
                <h3 id="finance-restore-title">{getRestoreFinanceTitle(restoreFinanceTarget)}</h3>
                <p>{getCancelFinanceObjectLabel(restoreFinanceTarget)}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение восстановления" onClick={closeRestoreFinanceDialog} disabled={saving === getRestoreFinanceSavingScope(restoreFinanceTarget)}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p className="confirmation-text" id="finance-restore-description">Запись снова появится в рабочих таблицах, расчетах и отчетах. Действие будет записано в общую историю изменений.</p>
            <div className="detail-dialog-actions">
              <button ref={restoreFinanceCancelRef} className="ghost-button" type="button" onClick={closeRestoreFinanceDialog} disabled={saving === getRestoreFinanceSavingScope(restoreFinanceTarget)}>
                Отмена
              </button>
              <button className="secondary-button" type="button" onClick={() => void confirmRestoreFinanceRecord()} disabled={saving === getRestoreFinanceSavingScope(restoreFinanceTarget)}>
                <RotateCcw size={16} aria-hidden="true" />
                <span>{saving === getRestoreFinanceSavingScope(restoreFinanceTarget) ? 'Возвращаем...' : 'Вернуть запись'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
      {financeEditor ? (
        <div className="modal-backdrop" role="presentation" data-testid="finance-editor-backdrop" onMouseDown={() => closeFinanceEditor()}>
          <section
            ref={financeEditorDialogRef}
            className="detail-dialog finance-editor-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="finance-editor-title"
            aria-describedby={financeEditorHasUnsavedChanges ? 'finance-editor-unsaved-changes' : undefined}
            onMouseDown={(event) => event.stopPropagation()}
          >
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">{financeEditor.mode === 'edit' ? getFinanceEditorUiLabel('editMode') : getFinanceEditorUiLabel('createMode')}</p>
                <h3 id="finance-editor-title">{getFinanceEditorTitle(financeEditor.section)}</h3>
              </div>
              <button ref={financeEditorCloseButtonRef} className="icon-button" type="button" aria-label={getFinanceEditorUiLabel('close')} onClick={() => closeFinanceEditor()}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <form className="dictionary-form finance-editor-form" onSubmit={handleFinanceEditorSubmit}>
              {renderFinanceEditorFields(financeEditor.section)}
              {financeEditorHasUnsavedChanges ? <p className="form-hint" id="finance-editor-unsaved-changes" role="status" aria-live="polite">{getFinanceEditorUiLabel('unsavedHint')}</p> : null}
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={() => closeFinanceEditor()}>
                  {getFinanceEditorUiLabel('cancel')}
                </button>
                <button className="secondary-button" type="submit" disabled={!canWritePayments || Boolean(pendingFinanceEditConfirmation) || saving === getFinanceEditorSavingScope(financeEditor.section)}>
                  <span>{financeEditor.mode === 'edit' ? getFinanceEditorUiLabel('save') : getFinanceEditorSubmitLabel(financeEditor.section)}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
      {pendingFinanceEditConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setPendingFinanceEditConfirmation(null)}>
          <section ref={financeEditConfirmationDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="finance-edit-confirmation-title" aria-describedby="finance-edit-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Проверка изменения</p>
                <h3 id="finance-edit-confirmation-title">Подтвердить изменение платежа?</h3>
                <p>{pendingFinanceEditConfirmation.objectName}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение платежа" onClick={() => setPendingFinanceEditConfirmation(null)}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p className="confirmation-text" id="finance-edit-confirmation-description">Проверьте изменения перед сохранением. После подтверждения backend запишет корректировку в историю платежей.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля платежа">
              {pendingFinanceEditConfirmation.changes.map((change) => (
                <li key={change.field}>
                  <span className="dictionary-change-field">{change.field}</span>
                  <span className="dictionary-change-values">
                    <span className="dictionary-change-value">{change.before}</span>
                    <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                    <span className="dictionary-change-value dictionary-change-value-after">{change.after}</span>
                  </span>
                </li>
              ))}
            </ul>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={financeEditConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setPendingFinanceEditConfirmation(null)}>Отмена</button>
              <button className="secondary-button" type="button" onClick={confirmPendingFinanceEdit} disabled={saving === pendingFinanceEditConfirmation.kind}>
                <Save size={16} aria-hidden="true" />
                <span>{saving === pendingFinanceEditConfirmation.kind ? 'Сохраняем...' : 'Сохранить'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
      {financeEditorCloseConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setFinanceEditorCloseConfirmation(false)}>
          <section ref={financeEditorCloseConfirmationDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="finance-editor-close-confirmation-title" aria-describedby="finance-editor-close-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Черновик</p>
                <h3 id="finance-editor-close-confirmation-title">Закрыть форму без сохранения?</h3>
                <p>{financeEditor ? getFinanceEditorTitle(financeEditor.section) : getFinancePanelLabel('section')}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Остаться в форме платежа" onClick={() => setFinanceEditorCloseConfirmation(false)}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p className="confirmation-text" id="finance-editor-close-confirmation-description">{getFinanceEditorUiLabel('unsavedConfirm')}</p>
            <div className="detail-dialog-actions">
              <button ref={financeEditorCloseConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setFinanceEditorCloseConfirmation(false)}>
                Остаться
              </button>
              <button className="secondary-button danger-button" type="button" onClick={confirmCloseFinanceEditor}>
                <X size={16} aria-hidden="true" />
                <span>Закрыть без сохранения</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
      {accrualBreakdown ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setAccrualBreakdown(null)}>
          <section ref={accrualBreakdownDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="accrual-breakdown-title" aria-describedby="accrual-breakdown-period" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <h3 id="accrual-breakdown-title">
                  {accrualBreakdown.kind === 'garage' ? 'Разбивка начисления' : 'Разбивка начисления поставщику'}
                </h3>
                <p id="accrual-breakdown-period">{formatMonth(accrualBreakdown.accrual.accountingMonth)}</p>
              </div>
              <button ref={accrualBreakdownCloseButtonRef} className="icon-button" type="button" aria-label="Закрыть разбивку" onClick={() => setAccrualBreakdown(null)}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            {accrualBreakdown.kind === 'garage' ? (
              <dl className="detail-grid">
                <div>
                  <dt>Гараж</dt>
                  <dd>{accrualBreakdown.accrual.garageNumber}</dd>
                </div>
                <div>
                  <dt>Владелец</dt>
                  <dd>{accrualBreakdown.accrual.ownerName ?? 'Не указан'}</dd>
                </div>
                <div>
                  <dt>Вид начисления</dt>
                  <dd>{accrualBreakdown.accrual.incomeTypeName}</dd>
                </div>
                {accrualBreakdown.accrual.basis ? (
                  <div>
                    <dt>Основание</dt>
                    <dd>{accrualBreakdown.accrual.basis}</dd>
                  </div>
                ) : null}
                <div>
                  <dt>Источник</dt>
                  <dd>{formatAccrualSource(accrualBreakdown.accrual.source)}</dd>
                </div>
                <div>
                  <dt>Сумма</dt>
                  <dd className="money-accrual">{formatMoney(accrualBreakdown.accrual.amount)}</dd>
                </div>
                <div>
                  <dt>Комментарий</dt>
                  <dd>{accrualBreakdown.accrual.comment ?? 'Нет комментария'}</dd>
                </div>
              </dl>
            ) : (
              <dl className="detail-grid">
                <div>
                  <dt>Поставщик</dt>
                  <dd>{accrualBreakdown.accrual.supplierName}</dd>
                </div>
                <div>
                  <dt>Услуга</dt>
                  <dd>{accrualBreakdown.accrual.expenseTypeName}</dd>
                </div>
                <div>
                  <dt>Источник</dt>
                  <dd>{formatAccrualSource(accrualBreakdown.accrual.source)}</dd>
                </div>
                <div>
                  <dt>Документ</dt>
                  <dd>{accrualBreakdown.accrual.documentNumber ?? 'Не указан'}</dd>
                </div>
                <div>
                  <dt>Сумма</dt>
                  <dd className="money-expense">{formatMoney(accrualBreakdown.accrual.amount)}</dd>
                </div>
                <div>
                  <dt>Комментарий</dt>
                  <dd>{accrualBreakdown.accrual.comment ?? 'Нет комментария'}</dd>
                </div>
              </dl>
            )}
          </section>
        </div>
      ) : null}
      {paymentsPrototypeDialog === 'bank' ? (
        <BankDepositPrototypeDialog
          auth={auth}
          financeClient={financeClient}
          onClose={closePaymentsPrototypeDialog}
          onSaved={() => {
            setPaymentsPrototypeRefreshRevision((value) => value + 1)
            closePaymentsPrototypeDialog()
          }}
        />
      ) : null}
    </section>
  )
}

function formatPaymentPrototypeValue(value: number | string) {
  return formatPaymentMoney(value)
}

function createGaragePaymentHistoryRowsFromOperations(operations: FinancialOperationDto[]): GaragePaymentHistoryPrototypeRow[] {
  return operations
    .filter((operation) => operation.operationKind === 'income' && operation.garageId)
    .map((operation) => ({
      id: operation.id,
      date: formatDateOnly(operation.operationDate),
      time: formatOperationTime(operation.createdAtUtc),
      amount: operation.amount,
      purpose: operation.incomeTypeName ?? operation.comment ?? 'Поступление',
      debtAfter: operation.garageDebtAfter ?? 0,
      operation,
    }))
}

function formatPaymentPrototypeMonthLabel(value: string) {
  const match = /^(\d{4})-(\d{2})(?:-\d{2})?$/.exec(value)
  if (!match) {
    return value
  }

  const monthLabels = ['янв', 'фев', 'мар', 'апр', 'май', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек']
  const monthIndex = Number(match[2]) - 1
  const monthLabel = monthLabels[monthIndex] ?? match[2]
  return `${monthLabel}.${match[1].slice(2)}`
}

function PaymentsPrototypePanel({
  auth,
  canWritePayments,
  dictionaryClient,
  expenseTypes,
  financeClient,
  garages,
  incomeTypes,
  irregularPayments,
  loading,
  suppliers,
  staffMembers,
  headingStatus,
  headingNotices,
  onEnsureReferences,
  onOpenDialog,
  refreshRevision,
}: {
  auth: AuthResponse
  canWritePayments: boolean
  dictionaryClient: DictionaryClient
  expenseTypes: AccountingTypeDto[]
  financeClient: FinanceClient
  garages: GarageDto[]
  incomeTypes: AccountingTypeDto[]
  irregularPayments: IrregularPaymentDto[]
  loading: boolean
  suppliers: SupplierDto[]
  staffMembers: StaffMemberDto[]
  headingStatus: string | null
  headingNotices: ReactNode
  onEnsureReferences: () => Promise<boolean>
  onOpenDialog: (dialog: PaymentsPrototypeDialogKey, trigger?: HTMLButtonElement | null) => void
  refreshRevision: number
}) {
  const [activeTab, setActiveTab] = useState<'income' | 'expense'>('income')
  const [garageSearch, setGarageSearch] = useState('')
  const [garageSearchGarages, setGarageSearchGarages] = useState<GarageDto[]>([])
  const [garageSearchLoading, setGarageSearchLoading] = useState(false)
  const [garageSearchError, setGarageSearchError] = useState<string | null>(null)
  const [garageSearchOpen, setGarageSearchOpen] = useState(false)
  const garageSearchWrapRef = useRef<HTMLDivElement | null>(null)
  const garageSearchRequestSequenceRef = useRef(0)
  const [selectedGarageId, setSelectedGarageId] = useState<string | null>(null)
  const selectedGarageIdRef = useRef<string | null>(null)
  const [incomeWorksheetRequests] = useState(() => new LatestRequestSequence())
  const incomeWorksheetRequestControllerRef = useRef<AbortController | null>(null)
  const [selectedGarage, setSelectedGarage] = useState<PaymentsPrototypeGarage | null>(null)
  const [overdueDebtDetails, setOverdueDebtDetails] = useState<GarageOverdueDebtDto | null>(null)
  const [overdueDebtLoading, setOverdueDebtLoading] = useState(false)
  const [overdueDebtError, setOverdueDebtError] = useState<string | null>(null)
  const [overdueDebtRefresh, setOverdueDebtRefresh] = useState(0)
  const [incomeWorksheetMonthFrom, setIncomeWorksheetMonthFrom] = useState(() => getPreviousMonthInputValue(getCurrentMonthInputValue()))
  const [incomeWorksheetMonthTo, setIncomeWorksheetMonthTo] = useState(() => getCurrentMonthInputValue())
  const [incomeWorksheetAvailableMonthFrom, setIncomeWorksheetAvailableMonthFrom] = useState(() => getPreviousMonthInputValue(getCurrentMonthInputValue()))
  const [incomeWorksheetAvailableMonthTo, setIncomeWorksheetAvailableMonthTo] = useState(() => getCurrentMonthInputValue())
  const [garageRows, setGarageRows] = useState<GarageIncomePrototypeRow[]>([])
  const [calculationDialogRow, setCalculationDialogRow] = useState<GarageIncomePrototypeRow | null>(null)
  const calculationDialogRef = useFocusTrap<HTMLElement>(Boolean(calculationDialogRow))
  useEscapeKey(Boolean(calculationDialogRow), () => setCalculationDialogRow(null))
  useRestoreFocusOnClose(Boolean(calculationDialogRow))
  const calculationDialogInitialFocusRef = useFocusOnOpen<HTMLButtonElement>(Boolean(calculationDialogRow))
  const [historicalMeterReadingSave, setHistoricalMeterReadingSave] = useState<HistoricalMeterReadingSaveState | null>(null)
  const historicalMeterReadingDialogRef = useFocusTrap<HTMLElement>(Boolean(historicalMeterReadingSave))
  useEscapeKey(Boolean(historicalMeterReadingSave), () => setHistoricalMeterReadingSave(null))
  useRestoreFocusOnClose(Boolean(historicalMeterReadingSave))
  const [overdueDebtDetailsExpanded, setOverdueDebtDetailsExpanded] = useState(() => overdueDebtDetailsPreference(auth.user.id))
  const [garageWorksheetSummary, setGarageWorksheetSummary] = useState<GarageIncomeWorksheetPeriodSummary | null>(null)
  const [expenseRows, setExpenseRows] = useState<PaymentPrototypeRow[]>([])
  const [expenseWorksheetMonthFrom, setExpenseWorksheetMonthFrom] = useState(() => getCurrentMonthInputValue())
  const [expenseWorksheetMonthTo, setExpenseWorksheetMonthTo] = useState(() => getCurrentMonthInputValue())
  const [expenseBankAmount, setExpenseBankAmount] = useState(0)
  const [expenseCashAmount, setExpenseCashAmount] = useState(0)
  const [expenseWorksheetRefreshRevision, setExpenseWorksheetRefreshRevision] = useState(0)
  const [historyRows, setHistoryRows] = useState<GaragePaymentHistoryPrototypeRow[]>([])
  const [paymentHistoryOpen, setPaymentHistoryOpen] = useState(false)
  const [paymentHistoryRequests] = useState(() => new LatestRequestSequence())
  const paymentHistoryRequestControllerRef = useRef<AbortController | null>(null)
  const incomePaymentWarningControllerRef = useRef<AbortController | null>(null)
  const overdueDebtRefreshControllerRef = useRef<AbortController | null>(null)
  const paymentHistoryId = useId()
  const [paymentError, setPaymentError] = useState<string | null>(null)
  const [garageWorksheetLoadingId, setGarageWorksheetLoadingId] = useState<string | null>(null)
  const [garagePaymentHistoryLoadingId, setGaragePaymentHistoryLoadingId] = useState<string | null>(null)
  const [expenseWorksheetLoading, setExpenseWorksheetLoading] = useState(false)
  const [savingPaymentRowId, setSavingPaymentRowId] = useState<string | null>(null)
  const [savingMeterRowId, setSavingMeterRowId] = useState<string | null>(null)
  const [fullPaymentDialogOpen, setFullPaymentDialogOpen] = useState(false)
  const [fullPaymentQuote, setFullPaymentQuote] = useState<GarageFullPaymentQuoteDto | null>(null)
  const [fullPaymentQuoteLoading, setFullPaymentQuoteLoading] = useState(false)
  const fullPaymentQuoteRequestControllerRef = useRef<AbortController | null>(null)
  const fullPaymentTriggerRef = useRef<HTMLButtonElement | null>(null)
  const fullPaymentReceiptBatchIdRef = useRef<string | null>(null)
  const [garageAccrualDialogOpen, setGarageAccrualDialogOpen] = useState(false)
  const garageAccrualTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [penaltyAccrualDialogOpen, setPenaltyAccrualDialogOpen] = useState(false)
  const penaltyAccrualTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [supplierAccrualDialogOpen, setSupplierAccrualDialogOpen] = useState(false)
  const supplierAccrualTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [expenseDialogPreset, setExpenseDialogPreset] = useState<ExpensePrototypeDialogPreset | null>(null)
  const expenseTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [staffPaymentDialogPreset, setStaffPaymentDialogPreset] = useState<StaffPaymentPrototypeDialogPreset | null>(null)
  const staffPaymentTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [staffSalaryAdjustmentDialogPreset, setStaffSalaryAdjustmentDialogPreset] = useState<StaffSalaryAdjustmentPrototypeDialogPreset | null>(null)
  const staffSalaryAdjustmentTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [historyEdit, setHistoryEdit] = useState<GaragePaymentHistoryEditState | null>(null)
  const historyEditTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [historyCancel, setHistoryCancel] = useState<GaragePaymentHistoryCancelState | null>(null)
  const historyCancelTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [historyActionSaving, setHistoryActionSaving] = useState(false)
  const [earlyElectricityPaymentConfirmation, setEarlyElectricityPaymentConfirmation] = useState<EarlyElectricityPaymentConfirmationState | null>(null)
  const earlyElectricityPaymentTriggerRef = useRef<HTMLElement | null>(null)
  const availableGarages = useMemo(() => {
    const uniqueGarages = new Map<string, GarageDto>()
    for (const garage of [...garages, ...garageSearchGarages]) {
      uniqueGarages.set(garage.id, garage)
    }
    return Array.from(uniqueGarages.values())
  }, [garageSearchGarages, garages])
  const realGarageIds = useMemo(
    () => new Set([
      ...availableGarages.filter((garage) => !garage.isArchived).map((garage) => garage.id),
      ...(selectedGarage ? [selectedGarage.id] : []),
    ]),
    [availableGarages, selectedGarage],
  )
  const garageOptions = useMemo<PaymentsPrototypeGarage[]>(
    () => availableGarages
      .filter((garage) => !garage.isArchived)
      .map((garage) => ({
        id: garage.id,
        number: garage.number,
        ownerName: garage.ownerName?.trim() || 'Владелец не указан',
        phone: garage.ownerPhone?.trim() || 'Не указан',
        peopleCount: garage.peopleCount,
        floorCount: garage.floorCount,
        balance: garage.balance,
        overdueDebt: garage.overdueDebt,
      })),
    [availableGarages],
  )
  useEffect(() => () => {
    incomeWorksheetRequests.invalidate()
    incomeWorksheetRequestControllerRef.current?.abort()
    paymentHistoryRequests.invalidate()
    paymentHistoryRequestControllerRef.current?.abort()
    fullPaymentQuoteRequestControllerRef.current?.abort()
    incomePaymentWarningControllerRef.current?.abort()
    overdueDebtRefreshControllerRef.current?.abort()
  }, [incomeWorksheetRequests, paymentHistoryRequests])
  const selectedGarageBalance = selectedGarage
    ? getGarageBalancePresentation(selectedGarage.balance, selectedGarage.overdueDebt)
    : null
  const selectedGarageOverdueDebt = selectedGarage?.overdueDebt ?? 0
  const normalizedSearch = garageSearch.trim().toLowerCase()
  const garageSearchResults = rankGarageSearchResults(garageOptions, normalizedSearch)
    .slice(0, 20)
  const shouldShowGarageResults = garageSearchOpen
  const garageSearchListId = useId()
  useEffect(() => {
    const query = garageSearch.trim()
    const requestSequence = ++garageSearchRequestSequenceRef.current
    if (!query && !garageSearchOpen) {
      setGarageSearchGarages([])
      setGarageSearchLoading(false)
      setGarageSearchError(null)
      return
    }

    let requestTimeoutHandle = 0
    let timedOut = false
    const controller = new AbortController()
    const handle = window.setTimeout(() => {
      setGarageSearchLoading(true)
      setGarageSearchError(null)
      const request = dictionaryClient.getGaragesPage
        ? dictionaryClient.getGaragesPage(auth.accessToken, query, 0, 20, false, undefined, undefined, false, {}, controller.signal)
            .then((page) => page.items)
        : dictionaryClient.getGarages(auth.accessToken, query, 20, false, controller.signal)
      const timeout = new Promise<GarageDto[]>((_resolve, reject) => {
        requestTimeoutHandle = window.setTimeout(
          () => {
            timedOut = true
            controller.abort()
            reject(new Error('Поиск гаражей занял слишком много времени. Повторите запрос.'))
          },
          garageSearchTimeoutMs,
        )
      })
      void Promise.race([request, timeout])
        .then((foundGarages) => {
          if (garageSearchRequestSequenceRef.current === requestSequence) {
            setGarageSearchGarages(foundGarages)
          }
        })
        .catch((error: unknown) => {
          if (garageSearchRequestSequenceRef.current === requestSequence) {
            setGarageSearchError(timedOut
              ? 'Поиск гаражей занял слишком много времени. Повторите запрос.'
              : error instanceof Error ? error.message : 'Не удалось выполнить поиск гаражей.')
          }
        })
        .finally(() => {
          window.clearTimeout(requestTimeoutHandle)
          if (garageSearchRequestSequenceRef.current === requestSequence) {
            setGarageSearchLoading(false)
          }
        })
    }, 250)

    return () => {
      window.clearTimeout(handle)
      window.clearTimeout(requestTimeoutHandle)
      controller.abort()
      if (garageSearchRequestSequenceRef.current === requestSequence) {
        garageSearchRequestSequenceRef.current += 1
      }
    }
  }, [auth.accessToken, dictionaryClient, garageSearch, garageSearchOpen])

  useEffect(() => {
    if (!selectedGarageId || selectedGarageOverdueDebt <= 0) {
      setOverdueDebtDetails(null)
      setOverdueDebtLoading(false)
      setOverdueDebtError(null)
      return
    }

    let cancelled = false
    const controller = new AbortController()
    setOverdueDebtDetails(null)
    setOverdueDebtLoading(true)
    setOverdueDebtError(null)
    void financeClient.getGarageOverdueDebt(auth.accessToken, selectedGarageId, controller.signal)
      .then((details) => {
        if (!cancelled) {
          setOverdueDebtDetails(details)
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setOverdueDebtError(error instanceof Error ? error.message : 'Не удалось загрузить расшифровку просрочки.')
        }
      })
      .finally(() => {
        if (!cancelled) {
          setOverdueDebtLoading(false)
        }
      })

    return () => {
      cancelled = true
      controller.abort()
    }
  }, [auth.accessToken, financeClient, overdueDebtRefresh, selectedGarageId, selectedGarageOverdueDebt])

  useEffect(() => {
    if (!garageSearchOpen) {
      return undefined
    }

    const closeGarageSearchOnOutsidePointer = (event: PointerEvent) => {
      if (!garageSearchWrapRef.current?.contains(event.target as Node)) {
        setGarageSearchOpen(false)
      }
    }

    document.addEventListener('pointerdown', closeGarageSearchOnOutsidePointer, true)
    return () => document.removeEventListener('pointerdown', closeGarageSearchOnOutsidePointer, true)
  }, [garageSearchOpen])

  useEffect(() => {
    if (activeTab !== 'expense') {
      return
    }

    if (expenseWorksheetMonthFrom > expenseWorksheetMonthTo) {
      setExpenseRows([])
      setExpenseWorksheetLoading(false)
      setPaymentError('Месяц начала формы выплат не может быть позже месяца окончания.')
      return
    }

    let cancelled = false
    const controller = new AbortController()
    setExpenseWorksheetLoading(true)
    setExpenseRows([])
    setExpenseBankAmount(0)
    setExpenseCashAmount(0)
    setPaymentError(null)
    financeClient
      .getExpenseWorksheet(auth.accessToken, expenseWorksheetMonthFrom === expenseWorksheetMonthTo
        ? { accountingMonth: `${expenseWorksheetMonthTo}-01` }
        : {
            monthFrom: `${expenseWorksheetMonthFrom}-01`,
            monthTo: `${expenseWorksheetMonthTo}-01`,
          }, controller.signal)
      .then((worksheet) => {
        if (!cancelled) {
          setExpenseRows(createExpenseRowsFromWorksheet(worksheet))
          setExpenseBankAmount(worksheet.bankAmount)
          setExpenseCashAmount(worksheet.cashAmount)
          setPaymentError(null)
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setPaymentError(error instanceof Error ? error.message : 'Не удалось загрузить форму выплат.')
        }
      })
      .finally(() => {
        if (!cancelled) {
          setExpenseWorksheetLoading(false)
        }
      })

    return () => {
      cancelled = true
      controller.abort()
    }
  }, [activeTab, auth.accessToken, expenseWorksheetMonthFrom, expenseWorksheetMonthTo, expenseWorksheetRefreshRevision, financeClient, refreshRevision])

  function activateExpenseTab() {
    if (activeTab !== 'expense') {
      setExpenseWorksheetLoading(true)
    }

    setGarageSearchOpen(false)
    setActiveTab('expense')
  }

  function handleExpenseWorksheetMonthFromChange(value: string) {
    if (!/^\d{4}-\d{2}$/.test(value)) {
      return
    }

    setExpenseWorksheetLoading(true)
    setExpenseWorksheetMonthFrom(value)
  }

  function refreshExpenseWorksheetAfterSave(accountingMonth: string) {
    const requestedMonth = accountingMonth.slice(0, 7)
    setExpenseWorksheetLoading(true)
    if (requestedMonth < expenseWorksheetMonthFrom || requestedMonth > expenseWorksheetMonthTo) {
      setExpenseWorksheetMonthFrom(requestedMonth)
      setExpenseWorksheetMonthTo(requestedMonth)
      return
    }

    setExpenseWorksheetRefreshRevision((value) => value + 1)
  }

  function handleExpenseWorksheetMonthToChange(value: string) {
    if (!/^\d{4}-\d{2}$/.test(value)) {
      return
    }

    setExpenseWorksheetLoading(true)
    setExpenseWorksheetMonthTo(value)
  }

  function openDialogFromButton(event: MouseEvent<HTMLButtonElement>, dialog: PaymentsPrototypeDialogKey) {
    event.currentTarget.focus()
    onOpenDialog(dialog, event.currentTarget)
  }

  async function openFullPaymentDialog(event: MouseEvent<HTMLButtonElement>) {
    fullPaymentTriggerRef.current = event.currentTarget
    setPaymentError(null)
    if (!selectedGarage || !realGarageIds.has(selectedGarage.id) || !(await onEnsureReferences())) {
      return
    }
    if (selectedGarageIdRef.current !== selectedGarage.id) {
      return
    }

    fullPaymentQuoteRequestControllerRef.current?.abort()
    const controller = new AbortController()
    fullPaymentQuoteRequestControllerRef.current = controller
    setFullPaymentQuoteLoading(true)
    try {
      const quote = await financeClient.getGarageFullPaymentQuote(auth.accessToken, selectedGarage.id, controller.signal)
      if (controller.signal.aborted || fullPaymentQuoteRequestControllerRef.current !== controller || selectedGarageIdRef.current !== selectedGarage.id) {
        return
      }
      setFullPaymentQuote(quote)
      fullPaymentReceiptBatchIdRef.current = crypto.randomUUID()
      setFullPaymentDialogOpen(true)
    } catch (error) {
      if (!controller.signal.aborted && fullPaymentQuoteRequestControllerRef.current === controller && selectedGarageIdRef.current === selectedGarage.id) {
        setPaymentError(error instanceof Error ? error.message : 'Не удалось точно рассчитать полную оплату.')
      }
    } finally {
      if (fullPaymentQuoteRequestControllerRef.current === controller) {
        setFullPaymentQuoteLoading(false)
      }
    }
  }

  function closeFullPaymentDialog() {
    const trigger = fullPaymentTriggerRef.current
    fullPaymentQuoteRequestControllerRef.current?.abort()
    setFullPaymentDialogOpen(false)
    setFullPaymentQuote(null)
    fullPaymentReceiptBatchIdRef.current = null
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      fullPaymentTriggerRef.current = null
    }, 0)
  }

  async function openGarageAccrualDialog(event: MouseEvent<HTMLButtonElement>) {
    garageAccrualTriggerRef.current = event.currentTarget
    setPaymentError(null)
    if (await onEnsureReferences()) {
      setGarageAccrualDialogOpen(true)
    }
  }

  function closeGarageAccrualDialog() {
    const trigger = garageAccrualTriggerRef.current
    setGarageAccrualDialogOpen(false)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      garageAccrualTriggerRef.current = null
    }, 0)
  }

  async function openSupplierAccrualDialog(event: MouseEvent<HTMLButtonElement>) {
    supplierAccrualTriggerRef.current = event.currentTarget
    setPaymentError(null)
    if (await onEnsureReferences()) {
      setSupplierAccrualDialogOpen(true)
    }
  }

  function closeSupplierAccrualDialog() {
    const trigger = supplierAccrualTriggerRef.current
    setSupplierAccrualDialogOpen(false)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      supplierAccrualTriggerRef.current = null
    }, 0)
  }

  async function openExpenseDialog(event: MouseEvent<HTMLButtonElement>, preset: ExpensePrototypeDialogPreset) {
    expenseTriggerRef.current = event.currentTarget
    setPaymentError(null)
    if (await onEnsureReferences()) {
      setExpenseDialogPreset(preset)
    }
  }

  function closeExpenseDialog() {
    const trigger = expenseTriggerRef.current
    setExpenseDialogPreset(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      expenseTriggerRef.current = null
    }, 0)
  }

  async function openStaffPaymentDialog(event: MouseEvent<HTMLButtonElement>, preset?: StaffPaymentPrototypeDialogPreset) {
    staffPaymentTriggerRef.current = event.currentTarget
    setPaymentError(null)
    if (await onEnsureReferences()) {
      setStaffPaymentDialogPreset(preset ?? {})
    }
  }

  function closeStaffPaymentDialog() {
    const trigger = staffPaymentTriggerRef.current
    setStaffPaymentDialogPreset(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      staffPaymentTriggerRef.current = null
    }, 0)
  }

  async function loadGarageIncomeWorksheet(
    garage: PaymentsPrototypeGarage,
    monthFrom = incomeWorksheetMonthFrom,
    monthTo = incomeWorksheetMonthTo,
    preservedMeter?: Pick<GarageIncomePrototypeRow, 'meterKind' | 'month' | 'meterDraft' | 'meterError'>,
    resolveAvailablePeriod = false,
  ) {
    incomeWorksheetRequestControllerRef.current?.abort()
    const controller = new AbortController()
    incomeWorksheetRequestControllerRef.current = controller
    const requestId = incomeWorksheetRequests.begin()
    setGarageWorksheetLoadingId(garage.id)
    try {
      let resolvedMonthFrom = monthFrom
      let resolvedMonthTo = monthTo
      let resolvedAvailableMonthFrom = incomeWorksheetAvailableMonthFrom
      let resolvedAvailableMonthTo = incomeWorksheetAvailableMonthTo
      if (resolveAvailablePeriod) {
        const period = await financeClient.getFinancialReportPeriod(auth.accessToken, { garageId: garage.id }, controller.signal)
        if (!incomeWorksheetRequests.isLatest(requestId) || selectedGarageIdRef.current !== garage.id) {
          return
        }

        resolvedAvailableMonthFrom = period.monthFrom.slice(0, 7)
        resolvedAvailableMonthTo = period.monthTo.slice(0, 7)
        resolvedMonthFrom = period.defaultMonthFrom?.slice(0, 7) ?? resolvedAvailableMonthFrom
        resolvedMonthTo = period.defaultMonthTo?.slice(0, 7) ?? resolvedAvailableMonthTo
      }

      const worksheetRequest = {
        monthFrom: `${resolvedMonthFrom}-01`,
        monthTo: `${resolvedMonthTo}-01`,
      }
      const worksheet = canWritePayments && financeClient.calculateGarageIncomeWorksheet
        ? await financeClient.calculateGarageIncomeWorksheet(auth.accessToken, garage.id, worksheetRequest, controller.signal)
        : await financeClient.getGarageIncomeWorksheet(auth.accessToken, garage.id, worksheetRequest, controller.signal)
      if (!incomeWorksheetRequests.isLatest(requestId) || selectedGarageIdRef.current !== garage.id) {
        return
      }

      setPaymentError(null)

      if (resolveAvailablePeriod) {
        setIncomeWorksheetAvailableMonthFrom(resolvedAvailableMonthFrom)
        setIncomeWorksheetAvailableMonthTo(resolvedAvailableMonthTo)
        setIncomeWorksheetMonthFrom(resolvedMonthFrom)
        setIncomeWorksheetMonthTo(resolvedMonthTo)
      }
      const rows = createGarageIncomeRowsFromWorksheet(worksheet).map((worksheetRow) => (
        preservedMeter
        && worksheetRow.meterKind === preservedMeter.meterKind
        && worksheetRow.month === preservedMeter.month
          ? { ...worksheetRow, meterDraft: preservedMeter.meterDraft, meterError: preservedMeter.meterError }
          : worksheetRow
      ))
      setGarageRows(rows)
      setGarageWorksheetSummary({
        openingBalance: worksheet.openingBalance,
        openingDebt: worksheet.openingDebt,
        unrepresentedOpeningDebt: worksheet.unrepresentedOpeningDebt ?? 0,
        accrualTotal: worksheet.accrualTotal,
        incomeTotal: worksheet.incomeTotal,
        advanceTotal: worksheet.advanceTotal ?? 0,
        closingBalance: worksheet.closingBalance,
        closingDebt: worksheet.closingDebt,
      })
    } catch (error) {
      if (!controller.signal.aborted && incomeWorksheetRequests.isLatest(requestId) && selectedGarageIdRef.current === garage.id) {
        setPaymentError(error instanceof Error ? error.message : 'Не удалось загрузить форму поступлений гаража.')
      }
    } finally {
      if (!controller.signal.aborted && incomeWorksheetRequests.isLatest(requestId) && selectedGarageIdRef.current === garage.id) {
        setGarageWorksheetLoadingId((currentId) => (currentId === garage.id ? null : currentId))
      }
    }
  }

  async function openStaffSalaryAdjustmentDialog(event: MouseEvent<HTMLButtonElement>, adjustmentType: StaffSalaryAdjustmentType) {
    staffSalaryAdjustmentTriggerRef.current = event.currentTarget
    setPaymentError(null)
    if (await onEnsureReferences()) {
      setStaffSalaryAdjustmentDialogPreset({ adjustmentType, accountingMonth: expenseWorksheetMonthTo })
    }
  }

  function closeStaffSalaryAdjustmentDialog() {
    const trigger = staffSalaryAdjustmentTriggerRef.current
    setStaffSalaryAdjustmentDialogPreset(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      staffSalaryAdjustmentTriggerRef.current = null
    }, 0)
  }

  async function loadGaragePaymentHistory(garage: PaymentsPrototypeGarage) {
    paymentHistoryRequestControllerRef.current?.abort()
    const controller = new AbortController()
    paymentHistoryRequestControllerRef.current = controller
    const requestId = paymentHistoryRequests.begin()
    setGaragePaymentHistoryLoadingId(garage.id)
    try {
      const page = await financeClient.getOperationsPage(auth.accessToken, {
        operationKind: 'income',
        garageId: garage.id,
        limit: 100,
      }, controller.signal)
      if (paymentHistoryRequests.isLatest(requestId) && selectedGarageIdRef.current === garage.id) {
        setHistoryRows(createGaragePaymentHistoryRowsFromOperations(page.items))
      }
    } catch (error) {
      if (!controller.signal.aborted && paymentHistoryRequests.isLatest(requestId) && selectedGarageIdRef.current === garage.id) {
        setPaymentError(error instanceof Error ? error.message : 'Не удалось загрузить историю платежей выбранного гаража.')
      }
    } finally {
      if (!controller.signal.aborted && paymentHistoryRequests.isLatest(requestId) && selectedGarageIdRef.current === garage.id) {
        setGaragePaymentHistoryLoadingId((currentId) => (currentId === garage.id ? null : currentId))
      }
    }
  }

  async function refreshGarageOverdueDebt(garage: PaymentsPrototypeGarage) {
    overdueDebtRefreshControllerRef.current?.abort()
    const controller = overdueDebtRefreshControllerRef.current = new AbortController()
    try {
      const details = await financeClient.getGarageOverdueDebt(auth.accessToken, garage.id, controller.signal)
      if (!controller.signal.aborted && selectedGarageIdRef.current === garage.id) {
        setSelectedGarage((currentGarage) => currentGarage?.id === garage.id
          ? { ...currentGarage, overdueDebt: details.total }
          : currentGarage)
        setOverdueDebtDetails(details.total > 0 ? details : null)
        setOverdueDebtError(null)
      }
      return true
    } catch {
      return controller.signal.aborted
    }
  }

  function refreshGarageAfterIncomeSave(garage: PaymentsPrototypeGarage, overdueDebtErrorMessage: string) {
    void Promise.all([
      refreshGarageOverdueDebt(garage),
      loadGarageIncomeWorksheet(garage),
      paymentHistoryOpen ? loadGaragePaymentHistory(garage) : Promise.resolve(),
    ]).then(([overdueDebtRefreshed]) => {
      if (!overdueDebtRefreshed && selectedGarageIdRef.current === garage.id) {
        setPaymentError(overdueDebtErrorMessage)
      }
    })
  }

  function togglePaymentHistory() {
    if (!selectedGarage) {
      return
    }

    if (paymentHistoryOpen) {
      paymentHistoryRequests.invalidate()
      paymentHistoryRequestControllerRef.current?.abort()
      setPaymentHistoryOpen(false)
      setGaragePaymentHistoryLoadingId(null)
      return
    }

    setPaymentError(null)
    setHistoryRows([])
    setPaymentHistoryOpen(true)
    void loadGaragePaymentHistory(selectedGarage)
  }

  function openHistoryEdit(row: GaragePaymentHistoryPrototypeRow, trigger?: HTMLButtonElement | null) {
    if (!row.operation || !canWritePayments) {
      return
    }

    historyEditTriggerRef.current = trigger ?? null
    setPaymentError(null)
    setHistoryEdit({
      row,
      amount: String(row.operation.amount),
      operationDate: row.operation.operationDate,
      accountingMonth: row.operation.accountingMonth.slice(0, 7),
      documentNumber: row.operation.documentNumber ?? '',
      comment: row.operation.comment ?? '',
      error: null,
    })
  }

  function closeHistoryEditDialog() {
    const trigger = historyEditTriggerRef.current
    setHistoryEdit(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      historyEditTriggerRef.current = null
    }, 0)
  }

  function openHistoryCancel(row: GaragePaymentHistoryPrototypeRow, trigger?: HTMLButtonElement | null) {
    if (!row.operation || !canWritePayments) {
      return
    }

    historyCancelTriggerRef.current = trigger ?? null
    setPaymentError(null)
    setHistoryCancel({ row, reason: '', error: null })
  }

  function closeHistoryCancelDialog() {
    const trigger = historyCancelTriggerRef.current
    setHistoryCancel(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      historyCancelTriggerRef.current = null
    }, 0)
  }

  async function saveHistoryEdit() {
    if (!historyEdit?.row.operation || !selectedGarage) {
      return
    }

    const operation = historyEdit.row.operation
    const amount = parsePaymentMoney(historyEdit.amount)
    if (!operation.garageId || !operation.incomeTypeId) {
      setHistoryEdit((state) => state ? { ...state, error: 'Платеж нельзя изменить: в операции не хватает гаража или вида поступления.' } : state)
      return
    }

    if (!Number.isFinite(amount) || amount <= 0) {
      setHistoryEdit((state) => state ? { ...state, error: 'Укажите сумму платежа больше нуля.' } : state)
      return
    }

    setHistoryActionSaving(true)
    setHistoryEdit((state) => state ? { ...state, error: null } : state)
    try {
      await financeClient.updateIncome(auth.accessToken, operation.id, {
        garageId: operation.garageId,
        incomeTypeId: operation.incomeTypeId,
        operationDate: historyEdit.operationDate,
        accountingMonth: `${historyEdit.accountingMonth}-01`,
        amount,
        documentNumber: historyEdit.documentNumber.trim() || undefined,
        comment: historyEdit.comment.trim() || undefined,
      })
      const balanceDelta = operation.amount - amount
      setSelectedGarage((currentGarage) => currentGarage?.id === selectedGarage.id
        ? { ...currentGarage, balance: roundPaymentMoney(currentGarage.balance + balanceDelta) }
        : currentGarage)
      closeHistoryEditDialog()
      refreshGarageAfterIncomeSave(selectedGarage, 'Платеж изменён, но не удалось обновить просроченную задолженность. Обновите страницу.')
    } catch (error) {
      setHistoryEdit((state) => state ? { ...state, error: error instanceof Error ? error.message : 'Не удалось изменить платеж.' } : state)
    } finally {
      setHistoryActionSaving(false)
    }
  }

  async function confirmHistoryCancel() {
    if (!historyCancel?.row.operation || !selectedGarage) {
      return
    }

    const reason = historyCancel.reason.trim()
    if (!reason) {
      setHistoryCancel((state) => state ? { ...state, error: 'Укажите причину отмены платежа.' } : state)
      return
    }

    setHistoryActionSaving(true)
    setHistoryCancel((state) => state ? { ...state, error: null } : state)
    try {
      const canceledAmount = historyCancel.row.operation.amount
      await financeClient.cancelOperation(auth.accessToken, historyCancel.row.operation.id, { reason })
      setSelectedGarage((currentGarage) => currentGarage?.id === selectedGarage.id
        ? { ...currentGarage, balance: roundPaymentMoney(currentGarage.balance + canceledAmount) }
        : currentGarage)
      closeHistoryCancelDialog()
      refreshGarageAfterIncomeSave(selectedGarage, 'Платеж отменён, но не удалось обновить просроченную задолженность. Обновите страницу.')
    } catch (error) {
      setHistoryCancel((state) => state ? { ...state, error: error instanceof Error ? error.message : 'Не удалось отменить платеж.' } : state)
    } finally {
      setHistoryActionSaving(false)
    }
  }

  function activateGarage(garage: PaymentsPrototypeGarage) {
    const currentMonth = getCurrentMonthInputValue()
    const previousMonth = getPreviousMonthInputValue(currentMonth)
    selectedGarageIdRef.current = garage.id
    incomeWorksheetRequestControllerRef.current?.abort()
    paymentHistoryRequests.invalidate()
    paymentHistoryRequestControllerRef.current?.abort()
    fullPaymentQuoteRequestControllerRef.current?.abort()
    incomePaymentWarningControllerRef.current?.abort()
    overdueDebtRefreshControllerRef.current?.abort()
    setSelectedGarageId(garage.id)
    setSelectedGarage(garage)
    setGarageRows([])
    setGarageWorksheetSummary(null)
    setHistoryRows([])
    setPaymentHistoryOpen(false)
    setGaragePaymentHistoryLoadingId(null)
    setFullPaymentQuoteLoading(false)
    setFullPaymentQuote(null)
    setFullPaymentDialogOpen(false)
    setPaymentError(null)
    setIncomeWorksheetAvailableMonthFrom(previousMonth)
    setIncomeWorksheetAvailableMonthTo(currentMonth)
    setIncomeWorksheetMonthFrom(previousMonth)
    setIncomeWorksheetMonthTo(currentMonth)
    void loadGarageIncomeWorksheet(garage, previousMonth, currentMonth, undefined, true)
  }

  function handleIncomeWorksheetMonthFromChange(value: string) {
    setIncomeWorksheetMonthFrom(value)
    if (selectedGarage) {
      void loadGarageIncomeWorksheet(selectedGarage, value, incomeWorksheetMonthTo)
    }
  }

  function handleIncomeWorksheetMonthToChange(value: string) {
    setIncomeWorksheetMonthTo(value)
    if (selectedGarage) {
      void loadGarageIncomeWorksheet(selectedGarage, incomeWorksheetMonthFrom, value)
    }
  }

  function setIncomeWorksheetPeriod(monthFrom: string, monthTo: string) {
    setIncomeWorksheetMonthFrom(monthFrom)
    setIncomeWorksheetMonthTo(monthTo)
    if (selectedGarage) {
      void loadGarageIncomeWorksheet(selectedGarage, monthFrom, monthTo)
    }
  }

  function handleMeterDraftChange(rowId: string, meterDraft: string) {
    setGarageRows((currentRows) => currentRows.map((row) => row.id === rowId
      ? { ...row, meterDraft, meterError: null }
      : row))
  }

  async function commitGarageMeterReading(row: GarageIncomePrototypeRow, periodOverrideReason?: string) {
    if (
      !selectedGarage
      || !realGarageIds.has(selectedGarage.id)
      || !row.meterKind
      || !financeClient.savePaymentFormMeterReading
    ) {
      return
    }

    const isCurrentMonth = row.month === getCurrentMonthInputValue()
    if (!isCurrentMonth && !hasPermission(auth, permissions.historicalMeterReadingsCorrect)) {
      setGarageRows((currentRows) => currentRows.map((currentRow) => currentRow.id === row.id
        ? { ...currentRow, meterError: 'Для показаний вне текущего месяца требуется право исторической корректировки.' }
        : currentRow))
      return
    }
    if (!isCurrentMonth && periodOverrideReason === undefined) {
      setHistoricalMeterReadingSave({ row, reason: '', error: null })
      return
    }

    const normalizedMeterDraft = row.meterDraft.trim().replace(',', '.')
    if (!normalizedMeterDraft) {
      setGarageRows((currentRows) => currentRows.map((currentRow) => currentRow.id === row.id
        ? { ...currentRow, meterError: 'Введите показание счетчика вручную.' }
        : currentRow))
      return
    }

    const currentValue = Number(normalizedMeterDraft)
    const validationErrors = getMeterReadingValidationErrors({
      garageId: selectedGarage.id,
      meterKind: row.meterKind,
      accountingMonth: `${row.month}-01`,
      readingDate: row.meterReadingDate ?? getLocalDateInputValue(),
      currentValue,
    })
    if (validationErrors.length > 0) {
      setGarageRows((currentRows) => currentRows.map((currentRow) => currentRow.id === row.id
        ? { ...currentRow, meterError: validationErrors[0] }
        : currentRow))
      return
    }

    setSavingMeterRowId(row.id)
    setGarageRows((currentRows) => currentRows.map((currentRow) => currentRow.id === row.id
      ? { ...currentRow, meterError: null }
      : currentRow))
    try {
      const comment = `Показание из формы поступлений: ${row.service} ${row.monthLabel}`
      const savedReading = !isCurrentMonth && row.meterReadingId && row.meterReadingVersion && financeClient.correctHistoricalMeterReading
        ? await financeClient.correctHistoricalMeterReading(auth.accessToken, row.meterReadingId, {
            readingDate: row.meterReadingDate ?? getLocalDateInputValue(),
            currentValue,
            comment,
            reason: periodOverrideReason?.trim() || undefined,
            expectedVersion: row.meterReadingVersion,
          })
        : await financeClient.savePaymentFormMeterReading(auth.accessToken, {
            garageId: selectedGarage.id,
            meterKind: row.meterKind,
            accountingMonth: `${row.month}-01`,
            readingDate: row.meterReadingDate ?? getLocalDateInputValue(),
            currentValue,
            comment,
            meterReadingId: row.meterReadingId ?? undefined,
            expectedVersion: row.meterReadingVersion ?? undefined,
            periodOverrideReason: isCurrentMonth ? undefined : periodOverrideReason?.trim() || undefined,
          })
      if (selectedGarageIdRef.current !== selectedGarage.id) {
        return
      }

      setGarageRows((currentRows) => currentRows.map((currentRow) => currentRow.id === row.id
        ? {
            ...currentRow,
            meter: savedReading.currentValue,
            meterDraft: String(savedReading.currentValue),
            difference: savedReading.consumption,
            meterReadingId: savedReading.id,
            meterReadingVersion: savedReading.version,
            meterReadingDate: savedReading.readingDate,
            meterRequired: false,
            meterError: null,
          }
        : currentRow))
      setHistoricalMeterReadingSave(null)
      void loadGarageIncomeWorksheet(selectedGarage, incomeWorksheetMonthFrom, incomeWorksheetMonthTo)
    } catch (error) {
      if (selectedGarageIdRef.current !== selectedGarage.id) {
        return
      }

      const meterError = error instanceof Error ? error.message : 'Не удалось сохранить показание. Повторите попытку.'
      setGarageRows((currentRows) => currentRows.map((currentRow) => currentRow.id === row.id
        ? { ...currentRow, meterError }
        : currentRow))
      if (error instanceof FinanceApiError && error.code === 'meter_reading_conflict') {
        await loadGarageIncomeWorksheet(selectedGarage, incomeWorksheetMonthFrom, incomeWorksheetMonthTo, {
          meterKind: row.meterKind,
          month: row.month,
          meterDraft: row.meterDraft,
          meterError,
        })
      }
    } finally {
      setSavingMeterRowId((currentRowId) => (currentRowId === row.id ? null : currentRowId))
    }
  }

  async function confirmHistoricalMeterReadingSave() {
    if (!historicalMeterReadingSave) {
      return
    }

    await commitGarageMeterReading(historicalMeterReadingSave.row, '')
  }

  function selectFirstGarageResult() {
    if (garageSearchResults.length > 0) {
      activateGarage(garageSearchResults[0])
      setGarageSearch('')
      setGarageSearchOpen(false)
    }
  }

  function handlePaymentDraftChange(rowId: string, value: string) {
    setPaymentError(null)
    setGarageRows((currentRows) => currentRows.map((row) => row.id === rowId ? { ...row, paymentDraft: value } : row))
  }

  function formatPaymentDraft(rowId: string) {
    setGarageRows((currentRows) => currentRows.map((row) => row.id === rowId
      ? { ...row, paymentDraft: formatPaymentMoney(row.paymentDraft) }
      : row))
  }

  function findIncomeTypeForPayment(
    serviceName: string,
    incomeTypeId?: string | null,
    meterKind?: GarageIncomePrototypeRow['meterKind'],
  ): GaragePaymentIncomeType | null {
    const normalizedService = serviceName.trim().toLocaleLowerCase('ru-RU')
    const activeIncomeTypes = incomeTypes.filter((incomeType) => !incomeType.isArchived)

    return activeIncomeTypes.find((incomeType) => incomeType.id === incomeTypeId)
      ?? activeIncomeTypes.find((incomeType) => incomeType.name.trim().toLocaleLowerCase('ru-RU') === normalizedService)
      ?? activeIncomeTypes.find((incomeType) => {
        const normalizedTypeName = incomeType.name.trim().toLocaleLowerCase('ru-RU')
        return normalizedTypeName.length > 0 && (normalizedTypeName.includes(normalizedService) || normalizedService.includes(normalizedTypeName))
      })
      ?? (incomeTypeId ? { id: incomeTypeId, code: meterKind ?? null } : null)
  }

  async function commitGaragePayment(row: GarageIncomePrototypeRow, warningConfirmed = false) {
    const amount = parsePaymentMoney(row.paymentDraft)
    if (!Number.isFinite(amount) || amount <= 0) {
      return
    }

    if (!selectedGarage || !realGarageIds.has(selectedGarage.id)) {
      setPaymentError('Выберите гараж из справочника, чтобы сохранить платеж в истории операций.')
      return
    }

    const incomeType = findIncomeTypeForPayment(row.service, row.incomeTypeId, row.meterKind)
    if (!incomeType) {
      setPaymentError(`Не найден вид поступления для услуги "${row.service}". Добавьте его в справочник и повторите сохранение.`)
      return
    }

    const appliedAmount = Math.min(amount, row.debt)
    const nextPaid = Math.min(row.paid + appliedAmount, row.payable)
    const nextAdvance = row.advance + Math.max(amount - appliedAmount, 0)
    const nextDebt = Math.max(row.debt - appliedAmount, 0)
    const accountingMonth = row.month.length === 7 ? `${row.month}-01` : row.month
    setSavingPaymentRowId(row.id)
    setPaymentError(null)
    let warningController: AbortController | null = null

    try {
      if (!warningConfirmed && incomeType.code?.trim().toLowerCase() === 'electricity') {
        const warningTrigger = document.activeElement instanceof HTMLElement ? document.activeElement : null
        incomePaymentWarningControllerRef.current?.abort()
        warningController = incomePaymentWarningControllerRef.current = new AbortController()
        const warning = await financeClient.getIncomePaymentWarning(auth.accessToken, {
          garageId: selectedGarage.id,
          incomeTypeId: incomeType.id,
          operationDate: getLocalDateInputValue(),
        }, warningController.signal)
        if (warningController.signal.aborted) return
        if (warning.requiresConfirmation && warning.previousPaymentDate && warning.daysSincePreviousPayment !== null) {
          earlyElectricityPaymentTriggerRef.current = warningTrigger
          setEarlyElectricityPaymentConfirmation({
            row,
            previousPaymentDate: warning.previousPaymentDate,
            daysSincePreviousPayment: warning.daysSincePreviousPayment,
          })
          return
        }
      }

      const operation = await financeClient.createIncome(auth.accessToken, {
        garageId: selectedGarage.id,
        incomeTypeId: incomeType.id,
        operationDate: getLocalDateInputValue(),
        accountingMonth,
        amount,
        feeCampaignId: row.feeCampaignId ?? undefined,
        irregularPaymentId: row.irregularPaymentId ?? undefined,
        comment: `Платеж из формы поступлений: ${row.service} ${row.monthLabel}`,
      })
      const paymentTime = new Date().toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
      const historyDebtAfter = operation.garageDebtAfter ?? nextDebt
      const optimisticGarageDebtAfter = operation.garageDebtAfter ?? selectedGarage.balance - amount

      setGarageRows((currentRows) => currentRows.map((currentRow) => currentRow.id === row.id ? { ...currentRow, paymentDraft: '', paid: nextPaid, advance: nextAdvance, debt: nextDebt } : currentRow))
      setGarageWorksheetSummary((currentSummary) => currentSummary
        ? {
            ...currentSummary,
            incomeTotal: currentSummary.incomeTotal + amount,
            advanceTotal: currentSummary.advanceTotal + Math.max(amount - appliedAmount, 0),
            closingBalance: currentSummary.closingBalance - amount,
            closingDebt: Math.max(currentSummary.closingDebt - appliedAmount, 0),
          }
        : currentSummary)
      setHistoryRows((currentRows) => [
        { id: operation.id, date: formatDateOnly(operation.operationDate), time: formatOperationTime(operation.createdAtUtc) || paymentTime, amount: operation.amount, purpose: operation.incomeTypeName ?? row.service, debtAfter: historyDebtAfter },
        ...currentRows,
      ])
      setSelectedGarage((currentGarage) => currentGarage?.id === selectedGarage.id
        ? { ...currentGarage, balance: optimisticGarageDebtAfter }
        : currentGarage)

      refreshGarageAfterIncomeSave(selectedGarage, 'Платеж сохранён, но не удалось обновить просроченную задолженность. Обновите страницу.')
    } catch (error) {
      if (warningController?.signal.aborted) return
      setPaymentError(error instanceof Error ? error.message : 'Не удалось сохранить платеж.')
    } finally {
      setSavingPaymentRowId(null)
    }
  }

  function closeEarlyElectricityPaymentConfirmation() {
    const trigger = earlyElectricityPaymentTriggerRef.current
    setEarlyElectricityPaymentConfirmation(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      earlyElectricityPaymentTriggerRef.current = null
    }, 0)
  }

  async function openPenaltyAccrualDialog(event: MouseEvent<HTMLButtonElement>) {
    penaltyAccrualTriggerRef.current = event.currentTarget
    setPaymentError(null)
    if (await onEnsureReferences()) {
      setPenaltyAccrualDialogOpen(true)
    }
  }

  function closePenaltyAccrualDialog() {
    const trigger = penaltyAccrualTriggerRef.current
    setPenaltyAccrualDialogOpen(false)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      penaltyAccrualTriggerRef.current = null
    }, 0)
  }

  function confirmEarlyElectricityPayment(pendingPayment: EarlyElectricityPaymentConfirmationState) {
    const trigger = earlyElectricityPaymentTriggerRef.current
    setEarlyElectricityPaymentConfirmation(null)
    earlyElectricityPaymentTriggerRef.current = null
    void commitGaragePayment(pendingPayment.row, true).finally(() => {
      window.setTimeout(() => {
        if (trigger?.isConnected) {
          trigger.focus()
        }
      }, 0)
    })
  }

  function getRowsForFullPayment(period: string) {
    return getFullPaymentRows(garageRows, period)
  }

  function getOpeningDebtForFullPayment(period: string) {
    return period === 'full' ? Math.max(garageWorksheetSummary?.unrepresentedOpeningDebt ?? 0, 0) : 0
  }

  async function commitFullGaragePayment(request: FullPaymentPrototypeSubmitRequest) {
    if (!selectedGarage || !realGarageIds.has(selectedGarage.id)) {
      return 'Выберите гараж из справочника, чтобы сохранить полную оплату в истории операций.'
    }

    const authoritativeQuote = request.period === 'full' && fullPaymentQuote?.garageId === selectedGarage.id
      ? fullPaymentQuote
      : null
    const usesAuthoritativeQuote = authoritativeQuote !== null
    const rowsToPay = usesAuthoritativeQuote ? [] : getRowsForFullPayment(request.period)
    const openingDebtToPay = usesAuthoritativeQuote ? 0 : getOpeningDebtForFullPayment(request.period)
    const totalDebtToPay = usesAuthoritativeQuote
      ? roundPaymentMoney(authoritativeQuote.totalAmount)
      : sumPaymentDebt(rowsToPay, openingDebtToPay)
    if (totalDebtToPay <= 0) {
      return 'По выбранному периоду нет задолженности для оплаты.'
    }
    if (!Number.isFinite(request.amount) || request.amount <= 0 || request.amount > totalDebtToPay) {
      return `Сумма оплаты должна быть больше нуля и не выше долга ${formatPaymentMoney(totalDebtToPay)}.`
    }

    const openingDebtPaymentAmount = usesAuthoritativeQuote ? 0 : Math.min(
      Math.max(toMoneyMinorUnits(openingDebtToPay), 0),
      Math.max(toMoneyMinorUnits(request.amount), 0),
    ) / 100
    const amountAfterOpeningDebt = roundPaymentMoney(request.amount - openingDebtPaymentAmount)
    const paymentPlan: Array<{ row: GarageIncomePrototypeRow; incomeType: GaragePaymentIncomeType; amount: number }> = []
    for (const allocation of createFullPaymentAllocations(rowsToPay, amountAfterOpeningDebt)) {
      const row = allocation.row
      const incomeType = findIncomeTypeForPayment(row.service, row.incomeTypeId, row.meterKind)
      if (!incomeType) {
        return `Не найден вид поступления для услуги "${row.service}". Добавьте его в справочник и повторите сохранение.`
      }

      paymentPlan.push({ row, incomeType, amount: allocation.amount })
    }

    const quotedPaymentPlan: Array<{ line: GarageFullPaymentQuoteDto['lines'][number]; amount: number }> = []
    if (usesAuthoritativeQuote) {
      let remainingAmount = Math.max(toMoneyMinorUnits(request.amount), 0)
      for (const line of authoritativeQuote.lines) {
        if (remainingAmount <= 0) {
          break
        }
        const allocatedAmount = Math.min(toMoneyMinorUnits(line.outstandingAmount), remainingAmount)
        if (allocatedAmount > 0) {
          quotedPaymentPlan.push({ line, amount: allocatedAmount / 100 })
          remainingAmount -= allocatedAmount
        }
      }
    }

    if (paymentPlan.length === 0 && quotedPaymentPlan.length === 0 && openingDebtPaymentAmount <= 0) {
      return 'Укажите сумму полной оплаты больше нуля.'
    }

    const receiptBatchId = fullPaymentReceiptBatchIdRef.current ?? crypto.randomUUID()
    fullPaymentReceiptBatchIdRef.current = receiptBatchId
    const paymentPurposes: string[] = []
    const lines = []
    for (const item of quotedPaymentPlan) {
      const label = item.line.isOpeningDebt ? 'Входящий долг' : item.line.incomeTypeName
      const datedLabel = item.line.isOpeningDebt
        ? label
        : `${label} ${formatPaymentPrototypeMonthLabel(item.line.accountingMonth)}`
      lines.push({
        incomeTypeId: item.line.incomeTypeId ?? undefined,
        accountingMonth: item.line.accountingMonth,
        amount: item.amount,
        isOpeningDebt: item.line.isOpeningDebt,
        feeCampaignId: item.line.feeCampaignId ?? undefined,
        irregularPaymentId: item.line.irregularPaymentId ?? undefined,
        comment: item.line.isOpeningDebt
          ? request.comment.trim() || undefined
          : request.comment.trim()
            ? `Полная оплата ${datedLabel}: ${request.comment.trim()}`
            : `Полная оплата ${datedLabel}`,
      })
      paymentPurposes.push(label)
    }

    if (openingDebtPaymentAmount > 0) {
      lines.push({
        accountingMonth: incomeWorksheetMonthFrom.length === 7 ? `${incomeWorksheetMonthFrom}-01` : incomeWorksheetMonthFrom,
        amount: openingDebtPaymentAmount,
        comment: request.comment.trim() || undefined,
        isOpeningDebt: true,
      })
      paymentPurposes.push('Оплата входящего долга')
    }

    for (const item of paymentPlan) {
      lines.push({
        incomeTypeId: item.incomeType.id,
        accountingMonth: item.row.month.length === 7 ? `${item.row.month}-01` : item.row.month,
        amount: item.amount,
        feeCampaignId: item.row.feeCampaignId ?? undefined,
        irregularPaymentId: item.row.irregularPaymentId ?? undefined,
        comment: request.comment.trim()
          ? `Полная оплата ${item.row.service} ${item.row.monthLabel}: ${request.comment.trim()}`
          : `Полная оплата ${item.row.service} ${item.row.monthLabel}`,
      })
      paymentPurposes.push(item.row.service)
    }

    const batch = await financeClient.createFullGaragePayment(auth.accessToken, {
      garageId: selectedGarage.id,
      operationDate: getLocalDateInputValue(),
      receiptBatchId,
      lines,
    })
    const historyItems = batch.operations.map((operation, index) => ({
      operation,
      purposeFallback: paymentPurposes[index]!,
    }))

    const paidByRowId = new Map(paymentPlan.map((item) => [item.row.id, item.amount]))
    setGarageRows((currentRows) => currentRows.map((row) => {
      const paidAmount = paidByRowId.get(row.id)
      return paidAmount ? {
        ...row,
        paymentDraft: '',
        paid: roundPaymentMoney(row.paid + paidAmount),
        debt: Math.max(roundPaymentMoney(row.debt - paidAmount), 0),
      } : row
    }))

    const paymentTime = new Date().toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
    setHistoryRows((currentRows) => [
      ...historyItems.map((item) => {
        const operation = item.operation
        return {
          id: operation.id,
          date: formatDateOnly(operation.operationDate),
          time: formatOperationTime(operation.createdAtUtc) || paymentTime,
          amount: operation.amount,
          purpose: operation.incomeTypeName ?? item.purposeFallback,
          debtAfter: operation.garageDebtAfter ?? 0,
        }
      }),
      ...currentRows,
    ])

    const optimisticGarageDebtAfter = batch.operations.at(-1)?.garageDebtAfter
      ?? Math.max(selectedGarage.balance - request.amount, 0)
    setSelectedGarage((currentGarage) => currentGarage?.id === selectedGarage.id
      ? { ...currentGarage, balance: optimisticGarageDebtAfter }
      : currentGarage)

    refreshGarageAfterIncomeSave(selectedGarage, 'Полная оплата сохранена, но не удалось обновить просроченную задолженность. Обновите страницу.')

    return null
  }

  async function commitGarageAccrual(request: GarageAccrualPrototypeSubmitRequest) {
    if (!selectedGarage || !realGarageIds.has(selectedGarage.id)) {
      return 'Выберите гараж из справочника, чтобы сохранить начисление в истории операций.'
    }

    const normalizedBasis = request.basis.trim().toLocaleLowerCase('ru-RU')
    const irregularPayment = irregularPayments.find((item) => item.isActive && !item.isArchived && item.name.trim().toLocaleLowerCase('ru-RU') === normalizedBasis) ?? null

    const savedAccrual = await financeClient.createIrregularAccrual(auth.accessToken, {
      garageId: selectedGarage.id,
      irregularPaymentId: irregularPayment?.id,
      basis: request.basis.trim(),
      amount: request.amount,
      accountingMonth: request.accountingMonth,
      comment: request.comment.trim() || undefined,
    })
    const month = savedAccrual.accountingMonth.slice(0, 7)
    const monthLabel = formatPaymentPrototypeMonthLabel(savedAccrual.accountingMonth)

    setGarageRows((currentRows) => {
      const serviceName = savedAccrual.basis ?? savedAccrual.irregularPaymentName ?? request.basis.trim()
      const existingRow = currentRows.find((row) => row.month === month && row.service.trim().toLocaleLowerCase('ru-RU') === serviceName.trim().toLocaleLowerCase('ru-RU'))
      if (existingRow) {
        return currentRows.map((row) => row.id === existingRow.id
          ? { ...row, payable: row.payable + savedAccrual.amount, debt: row.debt + savedAccrual.amount }
          : row)
      }

      return [
        ...currentRows,
        {
          id: `garage-accrual-${savedAccrual.id}`,
          month,
          monthLabel,
          service: serviceName,
          incomeTypeId: savedAccrual.incomeTypeId,
          annualAccrualId: savedAccrual.accountingYear ? savedAccrual.id : null,
          meterKind: null,
          meterReadingId: null,
          meterReadingVersion: null,
          meterReadingDate: null,
          meter: null,
          meterDraft: '',
          meterError: null,
          difference: null,
          payable: savedAccrual.amount,
          paymentDraft: '',
          paid: 0,
          advance: 0,
          debt: savedAccrual.amount,
        },
      ]
    })
    setGarageWorksheetSummary((currentSummary) => currentSummary
      ? {
          ...currentSummary,
          accrualTotal: currentSummary.accrualTotal + savedAccrual.amount,
          closingBalance: currentSummary.closingBalance + savedAccrual.amount,
          closingDebt: currentSummary.closingDebt + savedAccrual.amount,
        }
      : currentSummary)

    return null
  }

  async function commitPenaltyAccrual(request: PenaltyAccrualPrototypeSubmitRequest) {
    if (!selectedGarage || !realGarageIds.has(selectedGarage.id)) {
      return 'Выберите гараж из справочника, чтобы начислить штраф.'
    }

    const penaltyIncomeType = incomeTypes.find((incomeType) => incomeType.code === 'penalty' && incomeType.isSystem && !incomeType.isArchived) ?? null
    if (!penaltyIncomeType) {
      return 'Системный вид поступления «Штраф» не настроен. Обратитесь к администратору.'
    }

    const savedAccrual = await financeClient.createAccrual(auth.accessToken, {
      garageId: selectedGarage.id,
      incomeTypeId: penaltyIncomeType.id,
      accountingMonth: request.accountingMonth,
      amount: request.amount,
      source: 'manual',
      comment: request.reason.trim(),
    })
    const month = savedAccrual.accountingMonth.slice(0, 7)
    const monthLabel = formatPaymentPrototypeMonthLabel(savedAccrual.accountingMonth)
    const serviceName = savedAccrual.incomeTypeName || penaltyIncomeType.name

    setGarageRows((currentRows) => {
      const existingRow = currentRows.find((row) => row.month === month && row.service.trim().toLocaleLowerCase('ru-RU') === serviceName.trim().toLocaleLowerCase('ru-RU'))
      if (existingRow) {
        return currentRows.map((row) => row.id === existingRow.id
          ? { ...row, payable: row.payable + savedAccrual.amount, debt: row.debt + savedAccrual.amount }
          : row)
      }

      return [
        ...currentRows,
        {
          id: `garage-penalty-${savedAccrual.id}`,
          month,
          monthLabel,
          service: serviceName,
          incomeTypeId: savedAccrual.incomeTypeId,
          annualAccrualId: null,
          meterKind: null,
          meterReadingId: null,
          meterReadingVersion: null,
          meterReadingDate: null,
          meter: null,
          meterDraft: '',
          meterError: null,
          difference: null,
          payable: savedAccrual.amount,
          paymentDraft: '',
          paid: 0,
          advance: 0,
          debt: savedAccrual.amount,
          reason: request.reason.trim(),
        },
      ]
    })
    setGarageWorksheetSummary((currentSummary) => currentSummary
      ? {
          ...currentSummary,
          accrualTotal: currentSummary.accrualTotal + savedAccrual.amount,
          closingBalance: currentSummary.closingBalance + savedAccrual.amount,
          closingDebt: currentSummary.closingDebt + savedAccrual.amount,
        }
      : currentSummary)

    return null
  }

  async function commitExpensePayment(request: ExpensePrototypeSubmitRequest) {
    const expenseType = expenseTypes.find((item) => item.id === request.expenseTypeId && !item.isArchived) ?? null
    if (!expenseType) {
      return 'Выберите услугу или статью выплаты.'
    }

    const supplier = request.supplierId
      ? suppliers.find((item) => item.id === request.supplierId && !item.isArchived) ?? null
      : null
    if (request.expensePaymentSource === 'bank') {
      if (!supplier) {
        return 'Для выплаты с банковского счёта выберите поставщика.'
      }
      if (supplier.expenseTypeId !== expenseType.id) {
        return `Поставщику «${supplier.name}» можно провести выплату только по настроенной услуге.`
      }
    }
    await financeClient.createExpense(auth.accessToken, {
      supplierId: supplier?.id,
      counterpartyName: request.expensePaymentSource === 'cash' ? request.counterpartyName.trim() || undefined : undefined,
      expenseTypeId: expenseType.id,
      expensePaymentType: request.expensePaymentType,
      expensePaymentSource: request.expensePaymentSource,
      expenseFundId: request.expenseFundId || undefined,
      confirmNegativeFundBalance: request.confirmNegativeFundBalance,
      operationDate: request.operationDate,
      accountingMonth: request.accountingMonth,
      amount: request.amount,
      documentNumber: request.documentNumber.trim() || undefined,
      comment: request.comment.trim() || undefined,
    })

    refreshExpenseWorksheetAfterSave(request.accountingMonth)

    return null
  }

  async function commitStaffPayment(request: StaffPaymentPrototypeSubmitRequest) {
    const staffMember = staffMembers.find((item) => item.id === request.staffMemberId && !item.isArchived) ?? null
    if (!staffMember) {
      return 'Выберите сотрудника из справочника персонала.'
    }

    await financeClient.createStaffPayment(auth.accessToken, {
      staffMemberId: staffMember.id,
      operationDate: request.operationDate,
      accountingMonth: request.accountingMonth,
      amount: request.amount,
      documentNumber: request.documentNumber.trim() || undefined,
      comment: request.comment.trim() || undefined,
    })

    refreshExpenseWorksheetAfterSave(request.accountingMonth)

    return null
  }

  async function commitStaffSalaryAdjustment(request: StaffSalaryAdjustmentPrototypeSubmitRequest) {
    const staffMember = staffMembers.find((item) => item.id === request.staffMemberId && !item.isArchived) ?? null
    if (!staffMember) {
      return 'Выберите сотрудника из справочника персонала.'
    }

    await financeClient.createStaffSalaryAdjustment(auth.accessToken, {
      staffMemberId: staffMember.id,
      accountingMonth: request.accountingMonth,
      adjustmentType: request.adjustmentType,
      amount: request.amount,
      documentNumber: request.documentNumber.trim() || undefined,
      reason: request.reason.trim(),
    })

    refreshExpenseWorksheetAfterSave(request.accountingMonth)

    return null
  }

  async function commitSupplierAccrual(request: SupplierAccrualPrototypeSubmitRequest) {
    const supplier = suppliers.find((item) => item.id === request.supplierId && !item.isArchived) ?? null
    if (!supplier) {
      return 'Выберите поставщика из справочника.'
    }

    const expenseType = getSupplierAccrualExpenseType(supplier, expenseTypes)
    if (!expenseType) {
      return 'Для выбранного поставщика не настроена услуга начисления.'
    }
    if (expenseType.id !== request.expenseTypeId) {
      return 'Услуга начисления не соответствует выбранному поставщику.'
    }

    const accrual = await financeClient.createSupplierAccrual(auth.accessToken, {
      supplierId: supplier.id,
      expenseTypeId: expenseType.id,
      accountingMonth: request.accountingMonth,
      amount: request.amount,
      source: 'manual',
      documentNumber: request.documentNumber.trim() || undefined,
      comment: request.comment.trim() || undefined,
    })

    setExpenseRows((currentRows) => {
      let updated = false
      const nextRows = currentRows.map((row) => {
        if (row.item.trim().toLocaleLowerCase('ru-RU') !== accrual.expenseTypeName.trim().toLocaleLowerCase('ru-RU')) {
          return row
        }

        updated = true
        const cost = (typeof row.cost === 'number' ? row.cost : 0) + accrual.amount
        const paid = typeof row.paid === 'number' ? row.paid : 0
        const closing = calculateExpenseWorksheetClosingBalance(row.openingDebt, row.openingAdvance, cost, paid)
        return { ...row, cost, balance: closing.debt, closingDebt: closing.debt, closingAdvance: closing.advance }
      })

      if (updated) {
        return nextRows
      }

      return [
        ...nextRows,
        {
          item: accrual.expenseTypeName,
          openingDebt: 0,
          openingAdvance: 0,
          closingDebt: accrual.amount,
          closingAdvance: 0,
          cost: accrual.amount,
          paid: 0,
          balance: accrual.amount,
          collected: '',
          difference: '',
          action: true,
        },
      ]
    })

    return null
  }

  const groupedGarageRows = garageRows.reduce<Array<{ month: string; monthLabel: string; rows: GarageIncomePrototypeRow[] }>>((groups, row) => {
    const existingGroup = groups.find((group) => group.month === row.month)
    if (existingGroup) {
      existingGroup.rows.push(row)
    } else {
      groups.push({ month: row.month, monthLabel: row.monthLabel, rows: [row] })
    }
    return groups
  }, [])

  const paymentTotal = garageWorksheetSummary?.accrualTotal ?? garageRows.reduce((sum, row) => sum + row.payable, 0)
  const paidTotal = garageRows.reduce((sum, row) => sum + row.paid, 0)
  const openingBalanceTotal = toSignedGarageNetBalance(garageWorksheetSummary?.openingBalance ?? 0)
  const closingBalanceTotal = garageWorksheetSummary
    ? toSignedGarageNetBalance(garageWorksheetSummary.closingBalance)
    : toSignedGarageSplitBalance(
        garageRows.reduce((sum, row) => sum + row.debt, 0),
        garageRows.reduce((sum, row) => sum + row.advance, 0),
      )
  const fullPaymentRowsDebt = sumPaymentDebt(getRowsForFullPayment('full'))
  const selectedGarageFullPaymentQuote = fullPaymentQuote && fullPaymentQuote.garageId === selectedGarage?.id
    ? fullPaymentQuote
    : null
  const authoritativeFullPaymentDebt = selectedGarageFullPaymentQuote
    ? roundPaymentMoney(selectedGarageFullPaymentQuote.totalAmount)
    : roundPaymentMoney(fullPaymentRowsDebt + getOpeningDebtForFullPayment('full'))
  const fullPaymentPeriodOptions = [
    { value: 'full', label: 'Полный расчет', debt: authoritativeFullPaymentDebt },
    ...groupedGarageRows.map((group) => ({ value: group.month, label: group.monthLabel, debt: sumPaymentDebt(getRowsForFullPayment(group.month)) })),
  ].filter((option, index, options) => index === 0 || option.debt > 0 || !options.some((existingOption, existingIndex) => existingIndex < index && existingOption.value === option.value))
  const expenseAccrualTotal = expenseRows.reduce((sum, row) => sum + (typeof row.cost === 'number' ? row.cost : 0), 0)
  const expenseOpeningDebtTotal = expenseRows.reduce((sum, row) => sum + row.openingDebt, 0)
  const expenseOpeningAdvanceTotal = expenseRows.reduce((sum, row) => sum + row.openingAdvance, 0)
  const expensePaidTotal = expenseRows.reduce((sum, row) => sum + (typeof row.paid === 'number' ? row.paid : 0), 0)
  const expenseClosingDebtTotal = expenseRows.reduce((sum, row) => sum + row.closingDebt, 0)
  const expenseClosingAdvanceTotal = expenseRows.reduce((sum, row) => sum + row.closingAdvance, 0)
  const expenseOpeningBalanceTotal = toSignedExpenseWorksheetBalance(expenseOpeningDebtTotal, expenseOpeningAdvanceTotal)
  const expenseClosingBalanceTotal = toSignedExpenseWorksheetBalance(expenseClosingDebtTotal, expenseClosingAdvanceTotal)
  const expenseDifferenceTotal = expenseRows.reduce((sum, row) => sum + (typeof row.difference === 'number' ? row.difference : 0), 0)
  const expenseCashAndBankTotal = calculateCashAndBankTotal(expenseBankAmount, expenseCashAmount)
  const isEditableExpenseWorksheetPeriod = expenseWorksheetMonthFrom === expenseWorksheetMonthTo
    && expenseWorksheetMonthTo >= getCurrentMonthInputValue()
  const expensePeriodLabel = expenseWorksheetMonthFrom === expenseWorksheetMonthTo
    ? new Intl.DateTimeFormat('ru-RU', { month: 'long', year: 'numeric', timeZone: 'UTC' })
        .format(new Date(`${expenseWorksheetMonthFrom}-01T00:00:00Z`))
        .replace(/\s+г\.$/u, '')
    : `${formatMonth(`${expenseWorksheetMonthFrom}-01`)} — ${formatMonth(`${expenseWorksheetMonthTo}-01`)}`
  const expenseWorksheetTableLabel = `Форма выплат за ${expensePeriodLabel}`

  return (
    <section className="payments-prototype" aria-label="Форма платежей">
      <div className="payments-prototype-heading">
        <div className="section-heading payments-prototype-section-heading">
          <div>
            <p className="eyebrow">{getFinancePanelLabel('section')}</p>
            <h2>{getFinancePanelLabel('title')}</h2>
          </div>
          {headingStatus ? <span>{headingStatus}</span> : null}
        </div>
      </div>
      {headingNotices}
      {activeTab === 'income' ? <div className="payments-prototype-topline">
        <div ref={garageSearchWrapRef} className="payments-prototype-search-wrap">
          <label className="payments-prototype-search">
            <Search size={18} aria-hidden="true" />
            <input
              aria-label="Поиск номера гаража или ФИО владельца"
              role="combobox"
              aria-expanded={shouldShowGarageResults}
              aria-controls={garageSearchListId}
              placeholder="Введите номер гаража или ФИО владельца"
              value={garageSearch}
              onFocus={() => setGarageSearchOpen(true)}
              onChange={(event) => {
                setGarageSearch(event.target.value)
                setGarageSearchOpen(true)
              }}
              onKeyDown={(event) => {
                if (event.key === 'Escape') {
                  event.preventDefault()
                  setGarageSearchOpen(false)
                  return
                }
                if (event.key === 'Enter') {
                  event.preventDefault()
                  selectFirstGarageResult()
                }
              }}
            />
          </label>
          {shouldShowGarageResults ? (
            <div className="payments-prototype-search-results" id={garageSearchListId} role="listbox" aria-label="Найденные гаражи" aria-busy={garageSearchLoading}>
              {garageSearchLoading && garageSearchResults.length === 0 ? <span className="payments-prototype-search-empty" role="status">Ищем гаражи...</span> : null}
              {garageSearchError ? <span className="payments-prototype-search-empty" role="alert">{garageSearchError}</span> : null}
              {garageSearchResults.length > 0 ? garageSearchResults.map((garage) => (
                <button
                  className="payments-prototype-search-option"
                  key={garage.id}
                  type="button"
                  role="option"
                  aria-label={`Открыть карточку: Гараж ${garage.number} ${garage.ownerName}`}
                  aria-selected={selectedGarageId === garage.id}
                  onClick={() => {
                    activateGarage(garage)
                    setGarageSearch('')
                    setGarageSearchOpen(false)
                  }}
                >
                  <span>
                    <strong>Гараж {garage.number}</strong>
                    <small>{garage.ownerName}</small>
                  </span>
                </button>
              )) : !garageSearchLoading && !garageSearchError ? <span className="payments-prototype-search-empty">Ничего не найдено</span> : null}
            </div>
          ) : null}
        </div>
      </div> : null}
      {paymentError ? <FormError>{paymentError}</FormError> : null}
      {activeTab === 'income' && garageWorksheetLoadingId ? <TableLoadingState className="table-loading-state--compact" label="Загружаем поступления выбранного гаража" /> : null}

      <div className="payments-prototype-toolbar">
        <div className="payments-prototype-tabs" role="tablist" aria-label="Разделы формы платежей">
          <button type="button" role="tab" aria-selected={activeTab === 'income'} className={activeTab === 'income' ? 'is-active' : undefined} onClick={() => setActiveTab('income')}>
            Поступления
          </button>
          <button type="button" role="tab" aria-selected={activeTab === 'expense'} className={activeTab === 'expense' ? 'is-active' : undefined} onClick={activateExpenseTab}>
            Выплаты
          </button>
        </div>
      </div>

      {selectedGarage && activeTab === 'income' ? (
        <section className="payments-prototype-workspace-header" aria-label="Карточка выбранного гаража">
          <div className="payments-prototype-garage-overview" aria-label="Выбранный гараж">
            <section className="payments-prototype-garage-summary" aria-label="Параметры выбранного гаража">
              <section className="payments-prototype-summary-group" aria-label="Гараж">
                <h3>Гараж</h3>
                <dl>
                  <div><dt>Номер</dt><dd>{selectedGarage.number}</dd></div>
                  <div><dt>Люди</dt><dd>{selectedGarage.peopleCount}</dd></div>
                  <div><dt>Этажи</dt><dd>{selectedGarage.floorCount}</dd></div>
                </dl>
              </section>
              <section className="payments-prototype-summary-group" aria-label="Владелец">
                <h3>Владелец</h3>
                <dl>
                  <div><dt>ФИО</dt><dd>{selectedGarage.ownerName}</dd></div>
                  <div><dt>Телефон</dt><dd>{selectedGarage.phone}</dd></div>
                </dl>
              </section>
              <section className="payments-prototype-summary-group payments-prototype-summary-group--finances" aria-label="Финансы">
                <h3>Финансы</h3>
                <dl>
                  <div>
                    <dt>{selectedGarageBalance?.label}</dt>
                    <dd className={selectedGarageBalance?.moneyClassName}>{formatPaymentPrototypeValue(selectedGarageBalance?.amount ?? 0)}</dd>
                  </div>
                  <div>
                    <dt>Просроченная задолженность</dt>
                    <dd className={selectedGarage.overdueDebt > 0 ? 'money-expense' : undefined}>{formatPaymentPrototypeValue(selectedGarage.overdueDebt)}</dd>
                  </div>
                </dl>
              </section>
            </section>
          </div>
          <div className="payments-prototype-actions" aria-label="Действия с гаражом">
            <button className="secondary-button create-action-button payments-prototype-action-button" type="button" aria-label="Добавить начисление гаражу" onClick={openGarageAccrualDialog}>
              <FileText size={16} aria-hidden="true" />
              <span>Добавить начисление</span>
            </button>
            <button className="secondary-button create-action-button payments-prototype-action-button" type="button" onClick={openPenaltyAccrualDialog} disabled={!canWritePayments}>
              <Gavel size={16} aria-hidden="true" />
              <span>Начислить штраф</span>
            </button>
            <button className="secondary-button payments-prototype-action-button" type="button" onClick={openFullPaymentDialog} aria-busy={fullPaymentQuoteLoading} disabled={garageWorksheetLoadingId === selectedGarage.id || fullPaymentQuoteLoading}>
              {fullPaymentQuoteLoading ? <LoaderCircle className="financial-report-button__spinner" size={16} aria-hidden="true" /> : <WalletCards size={16} aria-hidden="true" />}
              <span>{fullPaymentQuoteLoading ? 'Рассчитываем оплату' : 'Полная оплата'}</span>
            </button>
            <button
              className="secondary-button payments-prototype-action-button"
              type="button"
              aria-controls={paymentHistoryId}
              aria-expanded={paymentHistoryOpen}
              onClick={togglePaymentHistory}
            >
              <History size={16} aria-hidden="true" />
              <span>{paymentHistoryOpen ? 'Скрыть историю' : 'История платежей'}</span>
            </button>
          </div>
          {selectedGarageBalance && selectedGarage.overdueDebt > 0 ? (
            <>
              <p className="payments-prototype-balance-explanation" role="note">
                {selectedGarageBalance.overdueRelation === 'partly-overdue'
                  ? <>Общий долг составляет <strong>{formatPaymentPrototypeValue(Math.abs(selectedGarageBalance.amount))}</strong>, из него просрочено <strong>{formatPaymentPrototypeValue(selectedGarage.overdueDebt)}</strong>.</>
                  : selectedGarageBalance.overdueRelation === 'fully-overdue'
                    ? <>Весь общий долг <strong>{formatPaymentPrototypeValue(Math.abs(selectedGarageBalance.amount))}</strong> уже просрочен.</>
                    : <>{selectedGarageBalance.label} <strong>{formatPaymentPrototypeValue(selectedGarageBalance.amount)}</strong> и просрочка <strong>{formatPaymentPrototypeValue(selectedGarage.overdueDebt)}</strong> относятся к разным услугам. Ниже показано, по каким услугам остался просроченный долг.</>}
              </p>
              <section className={`payments-prototype-overdue-details${overdueDebtDetailsExpanded ? ' payments-prototype-overdue-details--expanded' : ''}`} aria-label="Расшифровка просроченной задолженности">
                <div className="payments-prototype-overdue-heading">
                  <span className="payments-prototype-overdue-title">Расшифровка просроченной задолженности</span>
                  <span className="payments-prototype-overdue-controls">
                    <strong>{formatPaymentPrototypeValue(overdueDebtDetails?.total ?? selectedGarage.overdueDebt)}</strong>
                    <button
                      type="button"
                      className="icon-button"
                      aria-label={`${overdueDebtDetailsExpanded ? 'Скрыть' : 'Показать'} расшифровку просроченной задолженности`}
                      onClick={() => setOverdueDebtDetailsExpanded((current) => {
                        const next = !current
                        overdueDebtDetailsPreference(auth.user.id, next)
                        return next
                      })}
                    >
                      {overdueDebtDetailsExpanded
                        ? <X size={17} aria-hidden="true" />
                        : <CircleHelp size={17} aria-hidden="true" />}
                    </button>
                  </span>
                </div>
                {overdueDebtDetailsExpanded && (overdueDebtLoading ? (
                  <LoadingSkeleton label="Загрузка расшифровки просроченной задолженности" rows={3} columns={4} />
                ) : overdueDebtError ? (
                  <div className="form-error payments-prototype-overdue-error" role="alert">
                    <span>{overdueDebtError}</span>
                    <button className="secondary-button" type="button" onClick={() => setOverdueDebtRefresh((value) => value + 1)}>Повторить</button>
                  </div>
                ) : overdueDebtDetails && overdueDebtDetails.rows.length > 0 ? (
                  <div className="table-scroll">
                    <table aria-label="Расшифровка просроченной задолженности">
                      <thead>
                        <tr>
                          <th>Услуга</th>
                          <th>Месяц начисления</th>
                          <th>Срок оплаты</th>
                          <th>Просрочено с</th>
                          <th>Начислено</th>
                          <th>Оплачено</th>
                          <th>Остаток</th>
                        </tr>
                      </thead>
                      <tbody>
                        {overdueDebtDetails.rows.map((row, index) => (
                          <tr key={`${row.rowKind}-${row.incomeTypeId ?? 'opening'}-${row.accountingMonth ?? index}`}>
                            <td>{row.incomeTypeName}</td>
                            <td>{row.accountingMonth ? formatMonth(row.accountingMonth) : '—'}</td>
                            <td>{row.dueDate ? formatDateOnly(row.dueDate) : '—'}</td>
                            <td>{row.overdueFromDate ? formatDateOnly(row.overdueFromDate) : '—'}</td>
                            <td>{formatPaymentPrototypeValue(row.originalAmount)}</td>
                            <td>{formatPaymentPrototypeValue(row.paidAmount)}</td>
                            <td className="money-expense">{formatPaymentPrototypeValue(row.outstandingAmount)}</td>
                          </tr>
                        ))}
                      </tbody>
                      <tfoot>
                        <tr>
                          <th colSpan={6}>Итого на {formatDateOnly(overdueDebtDetails.asOfDate)}</th>
                          <th>{formatPaymentPrototypeValue(overdueDebtDetails.total)}</th>
                        </tr>
                      </tfoot>
                    </table>
                  </div>
                ) : (
                  <p className="empty-state empty-state--spacious" role="status" aria-live="polite">Просроченных начислений не найдено.</p>
                ))}
              </section>
            </>
          ) : null}
        </section>
      ) : null}

      {activeTab === 'income' ? !selectedGarage ? (
        loading
          ? <TableLoadingState label="Загружаем раздел платежей" />
          : <p className="empty-state" role="status">Выберите гараж через поиск, чтобы увидеть карточку, поступления, историю платежей и задолженность.</p>
      ) : (
        <>
          {paymentHistoryOpen ? <section id={paymentHistoryId} className="payments-prototype-card payments-prototype-card--history" aria-label="История платежей гаража">
            <table className="payments-prototype-mini-table" aria-label="История платежей гаража">
              <thead>
                <tr>
                  <th scope="col">Дата</th>
                  <th scope="col">Время</th>
                  <th scope="col">Сумма платежа</th>
                  <th scope="col">Назначение платежа</th>
                  <th scope="col">Остаток долга после платежа</th>
                  <th scope="col">Действия</th>
                </tr>
              </thead>
              <tbody>
                {garagePaymentHistoryLoadingId === selectedGarage.id ? (
                  <tr>
                    <td colSpan={6}><TableLoadingState label="Загружаем историю платежей" /></td>
                  </tr>
                ) : historyRows.length > 0 ? historyRows.map((row) => {
                  return (
                  <tr key={row.id}>
                    <td>{row.date}</td>
                    <td>{row.time}</td>
                    <td>{formatPaymentMoney(row.amount)}</td>
                    <td>{row.purpose}</td>
                    <td>{formatPaymentPrototypeValue(row.debtAfter)}</td>
                    <td>
                      {row.operation && canWritePayments ? (
                        <div className="table-action-row payments-prototype-history-actions">
                          <button className="icon-button" type="button" title="Изменить платеж" aria-label={`Изменить платеж ${row.purpose}`} onClick={(event) => openHistoryEdit(row, event.currentTarget)}>
                            <Pencil size={16} aria-hidden="true" />
                          </button>
                          <button className="icon-button danger-icon-button" type="button" title="Отменить платеж" aria-label={`Отменить платеж ${row.purpose}`} onClick={(event) => openHistoryCancel(row, event.currentTarget)}>
                            <Trash2 size={16} aria-hidden="true" />
                          </button>
                        </div>
                      ) : '—'}
                    </td>
                  </tr>
                  )
                }) : (
                  <tr>
                    <td colSpan={6}>Платежей по выбранному гаражу пока нет.</td>
                  </tr>
                )}
              </tbody>
            </table>
          </section> : null}

          <div className="payments-prototype-sheet">
            <div className="payments-prototype-period-row">
              <label>
                <span>Месяц с</span>
                <LocalizedDatePicker ariaLabel="Месяц поступлений с" mode="month" value={incomeWorksheetMonthFrom} onChange={handleIncomeWorksheetMonthFromChange} />
              </label>
              <label>
                <span>Месяц по</span>
                <LocalizedDatePicker ariaLabel="Месяц поступлений по" mode="month" value={incomeWorksheetMonthTo} onChange={handleIncomeWorksheetMonthToChange} />
              </label>
              <ReportPeriodQuickSelect
                mode="month"
                valueFrom={incomeWorksheetMonthFrom}
                valueTo={incomeWorksheetMonthTo}
                onSelect={(range) => setIncomeWorksheetPeriod(range.monthFrom, range.monthTo)}
              />
            </div>
            {garageWorksheetSummary ? (
              <div className="payments-prototype-period-summary" aria-label="Итоги периода поступлений">
                <div>
                  <span>Баланс на начало</span>
                  <strong className={openingBalanceTotal < 0 ? 'money-expense' : openingBalanceTotal > 0 ? 'money-income' : undefined}>{formatPaymentPrototypeValue(openingBalanceTotal)}</strong>
                </div>
                <div>
                  <span>Начислено</span>
                  <strong>{formatPaymentPrototypeValue(garageWorksheetSummary.accrualTotal)}</strong>
                </div>
                <div>
                  <span>Внесено</span>
                  <strong>{formatPaymentPrototypeValue(garageWorksheetSummary.incomeTotal)}</strong>
                </div>
                <div>
                  <span>Баланс на конец</span>
                  <strong className={closingBalanceTotal < 0 ? 'money-expense' : closingBalanceTotal > 0 ? 'money-income' : undefined}>{formatPaymentPrototypeValue(closingBalanceTotal)}</strong>
                </div>
              </div>
            ) : null}
            <div className="payments-prototype-table-scroll">
              <table className="payments-prototype-table payments-prototype-table--garage" aria-label={`Поступления гаража ${selectedGarage.number}`}>
                <thead>
                  <tr>
                    <th scope="col">Месяц</th>
                    <th scope="col">Услуга</th>
                    <th scope="col">Счётчик</th>
                    <th scope="col">Разница</th>
                    <th scope="col">К оплате</th>
                    <th scope="col">Платёж</th>
                    <th scope="col">Оплачено</th>
                    <th scope="col">Баланс</th>
                  </tr>
                </thead>
                <tbody>
                  {groupedGarageRows.map((group) => {
                    const groupPayable = group.rows.reduce((sum, row) => sum + row.payable, 0)
                    const groupPaid = group.rows.reduce((sum, row) => sum + row.paid, 0)
                    const groupAdvance = group.rows.reduce((sum, row) => sum + row.advance, 0)
                    const groupDebt = group.rows.reduce((sum, row) => sum + row.debt, 0)
                    const groupBalance = toSignedGarageSplitBalance(groupDebt, groupAdvance)
                    return (
                      <Fragment key={group.month}>
                        <tr className="payments-prototype-month-total">
                          <td>{group.monthLabel}</td>
                          <td>ИТОГО</td>
                          <td />
                          <td />
                          <td>{formatPaymentMoney(groupPayable)}</td>
                          <td />
                          <td>{formatPaymentMoney(groupPaid)}</td>
                          <td className={groupBalance < 0 ? 'money-expense' : groupBalance > 0 ? 'money-income' : undefined}>{formatPaymentMoney(groupBalance)}</td>
                        </tr>
                        {group.rows.map((row) => {
                          return (
                          <Fragment key={row.id}>
                          <tr>
                            <td />
                            <td>
                              <span>{row.service}</span>
                              {row.reason ? <small className="payments-prototype-row-reason">Причина: {row.reason}</small> : null}
                            </td>
                            <td className={row.meterRequired && row.meter === null ? 'payments-prototype-required-cell' : undefined}>
                              {row.meterKind && canWritePayments && financeClient.savePaymentFormMeterReading && (
                                row.month === getCurrentMonthInputValue()
                                || hasPermission(auth, permissions.historicalMeterReadingsCorrect)
                              ) ? (
                                <div className="payments-prototype-meter-editor">
                                  <MeterReadingInput
                                    className="payments-prototype-meter-input"
                                    aria-label={`Показание ${row.service} ${row.monthLabel}`}
                                    aria-describedby={row.meterRequired && row.meter === null ? `required-meter-${row.id}` : undefined}
                                    aria-invalid={row.meterError || (row.meterRequired && row.meter === null) ? 'true' : undefined}
                                    disabled={savingMeterRowId === row.id}
                                    value={row.meterDraft}
                                    onChange={(event) => handleMeterDraftChange(row.id, event.target.value)}
                                    onKeyDown={(event) => {
                                      if (event.key === 'Enter') {
                                        event.preventDefault()
                                        void commitGarageMeterReading(row)
                                      }
                                    }}
                                  />
                                  <button
                                    type="button"
                                    className="icon-button payments-prototype-meter-save"
                                    aria-label={savingMeterRowId === row.id ? `Сохраняется показание ${row.service} ${row.monthLabel}` : `Сохранить показание ${row.service} ${row.monthLabel}`}
                                    title={savingMeterRowId === row.id ? 'Сохраняется показание' : 'Сохранить показание'}
                                    disabled={savingMeterRowId === row.id}
                                    onClick={() => void commitGarageMeterReading(row)}
                                  >
                                    {savingMeterRowId === row.id
                                      ? <LoaderCircle className="payments-prototype-meter-spinner" size={14} aria-hidden="true" />
                                      : <Save size={14} aria-hidden="true" />}
                                  </button>
                                  {savingMeterRowId === row.id
                                    ? <span className="payments-prototype-meter-status" role="status" aria-live="polite">Сохраняем показание…</span>
                                    : row.meterError ? <span className="payments-prototype-meter-error" role="alert">{row.meterError}</span> : null}
                                </div>
                              ) : row.meter === null ? '' : row.meter.toLocaleString('ru-RU', { maximumFractionDigits: 3 })}
                              {row.meterRequired && row.meter === null ? (
                                <span className="payments-prototype-meter-required-hint" id={`required-meter-${row.id}`}>
                                  Введите обязательное показание
                                </span>
                              ) : null}
                            </td>
                            <td>{formatPaymentMoney(row.difference ?? '')}</td>
                            <td>
                              <div className="payments-prototype-payable">
                                {row.calculationDetails || row.payable > 0 ? (
                                  <span className="field-help">
                                    <button
                                      type="button"
                                      className="icon-button payments-prototype-calculation-toggle"
                                      aria-label={`Показать расчёт суммы ${row.service} ${row.monthLabel}`}
                                      onClick={() => setCalculationDialogRow(row)}
                                    >
                                      <CircleHelp size={15} aria-hidden="true" />
                                    </button>
                                    <span className="field-help__tooltip payments-prototype-calculation-tooltip">
                                      {getAccrualCalculationSummary(
                                        row.calculationDetails,
                                        `Сохранённое начисление: ${formatPaymentMoney(row.payable)}`,
                                      )}
                                    </span>
                                  </span>
                                ) : null}
                                <span className="payments-prototype-payable-amount">{formatPaymentMoney(row.payable)}</span>
                              </div>
                            </td>
                            <td>
                              <div className="payments-prototype-payment-editor">
                                <MoneyTextInput
                                  className="payments-prototype-payment-input"
                                  aria-label={`Платеж ${row.service} ${row.monthLabel}`}
                                  disabled={savingPaymentRowId === row.id}
                                  value={row.paymentDraft}
                                  onValueChange={(paymentDraft) => handlePaymentDraftChange(row.id, paymentDraft)}
                                  onBlur={() => formatPaymentDraft(row.id)}
                                  onKeyDown={(event) => {
                                    if (event.key === 'Enter') {
                                      event.preventDefault()
                                      void commitGaragePayment(row)
                                    }
                                  }}
                                />
                                <button
                                  type="button"
                                  className="icon-button payments-prototype-payment-save"
                                  aria-label={savingPaymentRowId === row.id ? `Сохраняется платеж ${row.service} ${row.monthLabel}` : `Сохранить платеж ${row.service} ${row.monthLabel}`}
                                  title={savingPaymentRowId === row.id ? 'Сохраняется платёж' : 'Сохранить платёж'}
                                  disabled={savingPaymentRowId === row.id || !Number.isFinite(parsePaymentMoney(row.paymentDraft)) || parsePaymentMoney(row.paymentDraft) <= 0}
                                  onClick={() => void commitGaragePayment(row)}
                                >
                                  {savingPaymentRowId === row.id
                                    ? <LoaderCircle className="payments-prototype-meter-spinner" size={14} aria-hidden="true" />
                                    : <Save size={14} aria-hidden="true" />}
                                </button>
                                {savingPaymentRowId === row.id
                                  ? <span className="payments-prototype-payment-status" role="status" aria-live="polite">Сохраняем платёж…</span>
                                  : null}
                              </div>
                            </td>
                            <td>{formatPaymentMoney(row.paid)}</td>
                            {(() => {
                              const balance = toSignedGarageSplitBalance(row.debt, row.advance)
                              return <td className={balance < 0 ? 'money-expense' : balance > 0 ? 'money-income' : undefined}>{formatPaymentMoney(balance)}</td>
                            })()}
                          </tr>
                          </Fragment>
                          )
                        })}
                      </Fragment>
                    )
                  })}
                  {groupedGarageRows.length === 0 ? (
                    <tr>
                      <td colSpan={8}>{garageWorksheetLoadingId === selectedGarage.id ? <TableLoadingState label="Загружаем начисления и поступления" /> : 'Начислений и поступлений за выбранный период пока нет.'}</td>
                    </tr>
                  ) : null}
                  <tr className="payments-prototype-total-row">
                    <td />
                    <td>ИТОГО</td>
                    <td />
                    <td />
                    <td>{formatPaymentMoney(paymentTotal)}</td>
                    <td />
                    <td>{formatPaymentMoney(paidTotal)}</td>
                    <td className={closingBalanceTotal < 0 ? 'money-expense' : closingBalanceTotal > 0 ? 'money-income' : undefined}>{formatPaymentMoney(closingBalanceTotal)}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </>
      ) : (
        <>
          <div className="payments-prototype-actions payments-prototype-actions--sheet">
            <button className="secondary-button create-action-button" type="button" onClick={(event) => openSupplierAccrualDialog(event)}>
              <FileText size={16} aria-hidden="true" />
              <span>Добавить начисление</span>
            </button>
            <button className="secondary-button create-action-button" type="button" disabled={!canWritePayments} onClick={(event) => openExpenseDialog(event, { expensePaymentSource: 'bank' })}>
              <WalletCards size={16} aria-hidden="true" />
              <span>Добавить выплату</span>
            </button>
            <button className="secondary-button create-action-button" type="button" disabled={!canWritePayments} onClick={(event) => openStaffPaymentDialog(event)}>
              <UserRound size={16} aria-hidden="true" />
              <span>Выплатить оклад</span>
            </button>
            <button className="secondary-button create-action-button" type="button" disabled={!canWritePayments} onClick={(event) => openStaffSalaryAdjustmentDialog(event, 'bonus')}>
              <Award size={16} aria-hidden="true" />
              <span>Начислить премию</span>
            </button>
            <button className="secondary-button create-action-button" type="button" disabled={!canWritePayments} onClick={(event) => openStaffSalaryAdjustmentDialog(event, 'penalty')}>
              <Gavel size={16} aria-hidden="true" />
              <span>Начислить штраф</span>
            </button>
          </div>

          <div className="payments-prototype-sheet">
            <div className="payments-prototype-period-row">
              <label>
                <span>Месяц с</span>
                <LocalizedDatePicker
                  ariaLabel="Месяц выплат с"
                  mode="month"
                  value={expenseWorksheetMonthFrom}
                  onChange={handleExpenseWorksheetMonthFromChange}
                />
              </label>
              <label>
                <span>Месяц по</span>
                <LocalizedDatePicker
                  ariaLabel="Месяц выплат по"
                  mode="month"
                  value={expenseWorksheetMonthTo}
                  onChange={handleExpenseWorksheetMonthToChange}
                />
              </label>
              <ReportPeriodQuickSelect
                mode="month"
                valueFrom={expenseWorksheetMonthFrom}
                valueTo={expenseWorksheetMonthTo}
                onSelect={({ monthFrom, monthTo }) => {
                  setExpenseWorksheetMonthFrom(monthFrom)
                  setExpenseWorksheetMonthTo(monthTo)
                }}
              />
            </div>
            <div className="payments-prototype-table-scroll">
              <table className="payments-prototype-table" aria-label={expenseWorksheetTableLabel}>
                <thead>
                  <tr>
                    <th scope="col">Поставщик</th>
                    <th scope="col">Услуга</th>
                    <th scope="col">Входящий баланс</th>
                    <th scope="col">Стоимость</th>
                    <th scope="col">Оплачено</th>
                    <th scope="col">Исходящий баланс</th>
                    {isEditableExpenseWorksheetPeriod ? (
                      <>
                        <th scope="col">Текущий размер фонда</th>
                        <th scope="col">Действие</th>
                      </>
                    ) : null}
                  </tr>
                </thead>
                <tbody>
                  {expenseRows.map((row, index) => {
                    const supplier = row.counterparty ?? ''
                    const isStaffPaymentRow = row.rowKind === 'staff'
                    const suggestedAmount = row.closingDebt > 0 ? row.closingDebt : typeof row.cost === 'number' ? row.cost : undefined
                    const openingBalance = toSignedExpenseWorksheetBalance(row.openingDebt, row.openingAdvance)
                    const closingBalance = toSignedExpenseWorksheetBalance(row.closingDebt, row.closingAdvance)
                    return (
                      <tr key={`${row.item}-${index}`}>
                        <td>{supplier}</td>
                        <td>
                          <span>{row.item}</span>
                          {row.rowKind === 'supplier' && row.expenseFundName ? (
                            <small className="payments-prototype-cell-note">Фонд: {row.expenseFundName}</small>
                          ) : null}
                          {isStaffPaymentRow && ((row.bonus ?? 0) > 0 || (row.penalty ?? 0) > 0) ? (
                            <small className="payments-prototype-cell-note">
                              Оклад {formatPaymentMoney(row.baseAccrual ?? row.cost)}
                              {(row.bonus ?? 0) > 0 ? ` · премия ${formatPaymentMoney(row.bonus ?? 0)}` : ''}
                              {(row.penalty ?? 0) > 0 ? ` · штраф ${formatPaymentMoney(row.penalty ?? 0)}` : ''}
                            </small>
                          ) : null}
                        </td>
                        <td>{formatPaymentMoney(openingBalance)}</td>
                        <td>{formatPaymentMoney(row.cost)}</td>
                        <td>{formatPaymentMoney(row.paid)}</td>
                        <td>{formatPaymentMoney(closingBalance)}</td>
                        {isEditableExpenseWorksheetPeriod ? (
                          <>
                            <td className={typeof row.difference === 'number' && row.difference < 0 ? 'money-expense' : typeof row.difference === 'number' && row.difference > 0 ? 'money-income' : undefined}>
                              {formatPaymentMoney(row.difference)}
                            </td>
                            <td>
                              {row.action ? (
                                <button className="link-button" type="button" onClick={(event) => {
                                  if (isStaffPaymentRow) {
                                    openStaffPaymentDialog(event, { staffMemberName: supplier, amount: suggestedAmount, rowIndex: index })
                                    return
                                  }

                                  openExpenseDialog(event, { expensePaymentSource: 'bank', expenseTypeName: row.item, amount: suggestedAmount, rowIndex: index })
                                }} aria-label={isStaffPaymentRow ? `Оплатить сотрудника ${supplier}` : `Оплатить ${row.item}`}>
                                  Оплатить
                                </button>
                              ) : null}
                            </td>
                          </>
                        ) : null}
                      </tr>
                    )
                  })}
                  {expenseRows.length === 0 ? (
                    <tr>
                      <td colSpan={isEditableExpenseWorksheetPeriod ? 8 : 6}>{expenseWorksheetLoading ? <TableLoadingState label="Загружаем форму выплат" /> : 'Начислений и выплат за выбранный период пока нет.'}</td>
                    </tr>
                  ) : null}
                  <tr className="payments-prototype-total-row">
                    <td>ИТОГО</td>
                    <td />
                    <td>{formatPaymentMoney(expenseOpeningBalanceTotal)}</td>
                    <td>{formatPaymentMoney(expenseAccrualTotal)}</td>
                    <td>{formatPaymentMoney(expensePaidTotal)}</td>
                    <td>{formatPaymentMoney(expenseClosingBalanceTotal)}</td>
                    {isEditableExpenseWorksheetPeriod ? (
                      <>
                        <td className={expenseDifferenceTotal < 0 ? 'money-expense' : expenseDifferenceTotal > 0 ? 'money-income' : undefined}>
                          {formatPaymentMoney(expenseDifferenceTotal)}
                        </td>
                        <td />
                      </>
                    ) : null}
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          <div className="payments-prototype-footer" aria-label="Итоги кассы и банка">
            <div>
              <span>Сумма в банке</span>
              <strong>{formatPaymentPrototypeValue(expenseBankAmount)}</strong>
            </div>
            <div>
              <span>Касса</span>
              <strong>{formatPaymentPrototypeValue(expenseCashAmount)}</strong>
            </div>
            <div>
              <span>Касса + банк</span>
              <strong>{formatPaymentPrototypeValue(expenseCashAndBankTotal)}</strong>
            </div>
            <button className="secondary-button" type="button" onClick={(event) => openDialogFromButton(event, 'bank')}>
              Сдать кассу в банк
            </button>
          </div>
        </>
      )}
      {calculationDialogRow ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setCalculationDialogRow(null)}>
          <section
            ref={calculationDialogRef}
            className="detail-dialog payments-prototype-calculation-dialog"
            role="dialog"
            aria-modal="true"
            aria-label={`Расчёт суммы: ${calculationDialogRow.service}, ${calculationDialogRow.monthLabel}`}
            onMouseDown={(event) => event.stopPropagation()}
          >
            <div className="detail-dialog-header">
              <h3>Расчёт суммы: {calculationDialogRow.service}, {calculationDialogRow.monthLabel}</h3>
              <button ref={calculationDialogInitialFocusRef} className="icon-button" type="button" aria-label="Закрыть расчёт суммы" onClick={() => setCalculationDialogRow(null)}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            {calculationDialogRow.calculationDetails ? (
              <div className="payments-prototype-calculation">
                <div className="payments-prototype-calculation-heading">
                  <strong>Как рассчитано {formatPaymentMoney(calculationDialogRow.calculationDetails.totalAmount)}</strong>
                  {calculationDialogRow.calculationDetails.requiresMeter ? (
                    <span>Показания: {calculationDialogRow.calculationDetails.previousMeterValue?.toLocaleString('ru-RU') ?? '—'} → {calculationDialogRow.calculationDetails.currentMeterValue?.toLocaleString('ru-RU') ?? '—'}; расход {calculationDialogRow.calculationDetails.meterConsumption?.toLocaleString('ru-RU') ?? '—'}</span>
                  ) : null}
                </div>
                {calculationDialogRow.calculationDetails.volumeAllocationRule ? <p>{calculationDialogRow.calculationDetails.volumeAllocationRule}</p> : null}
                <div className="payments-prototype-calculation-lines">
                  {calculationDialogRow.calculationDetails.lines.map((line, lineIndex) => (
                  <div className="payments-prototype-calculation-line" key={`${line.effectiveFrom}-${line.effectiveTo}-${lineIndex}`}>
                    <span>{formatDateOnly(line.effectiveFrom)}–{formatDateOnly(line.effectiveTo)}</span>
                    <span>{line.calculationMode === 'fixed' ? 'Фиксированный' : line.calculationMode === 'people' ? 'По количеству людей' : line.calculationMode === 'metered' ? 'По счётчику' : line.calculationMode === 'metered_tiered' ? 'По счётчику, пороги' : 'Без тарифа'}</span>
                    <span>{line.formula}</span>
                    {line.tiers.length > 0 ? (
                      <ul>
                        {line.tiers.map((tier, tierIndex) => (
                          <li key={`${tier.from}-${tier.to ?? 'max'}-${tierIndex}`}>
                            {tier.from.toLocaleString('ru-RU')}–{tier.to?.toLocaleString('ru-RU') ?? 'без верхней границы'} {line.unitName}: {tier.quantity.toLocaleString('ru-RU')} × {formatPaymentMoney(tier.rate)} = {formatPaymentMoney(tier.amount)}
                          </li>
                        ))}
                      </ul>
                    ) : null}
                  </div>
                  ))}
                </div>
              </div>
            ) : (
              <div className="payments-prototype-calculation payments-prototype-calculation--historical" role="note">
                <div className="payments-prototype-calculation-heading">
                  <strong>Сумма сохранённого начисления: {formatPaymentMoney(calculationDialogRow.payable)}</strong>
                  {calculationDialogRow.difference !== null ? <span>Зафиксированный расход: {calculationDialogRow.difference.toLocaleString('ru-RU')}</span> : null}
                </div>
                <p>Для этой ранее созданной записи подробная тарифная формула не сохранялась. Сумма показана без пересчёта и не изменяет начисление, оплату или задолженность.</p>
              </div>
            )}
          </section>
        </div>
      ) : null}
      {historicalMeterReadingSave ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (savingMeterRowId !== historicalMeterReadingSave.row.id) setHistoricalMeterReadingSave(null)
        }}>
          <section
            ref={historicalMeterReadingDialogRef}
            className="detail-dialog compact-dialog"
            role="dialog"
            aria-modal="true"
            aria-label={`Сохранить показание ${historicalMeterReadingSave.row.service} за ${historicalMeterReadingSave.row.monthLabel}`}
            onMouseDown={(event) => event.stopPropagation()}
          >
            <div className="detail-dialog-header">
              <h3>Показание вне текущего месяца</h3>
              <button
                className="icon-button"
                type="button"
                aria-label="Закрыть ввод причины показания"
                disabled={savingMeterRowId === historicalMeterReadingSave.row.id}
                onClick={() => setHistoricalMeterReadingSave(null)}
              >
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <div className="dialog-body">
              <p className="form-hint">
                Показание за {historicalMeterReadingSave.row.monthLabel} изменит неоплаченный расчёт. Система автоматически сохранит это действие в истории изменений.
              </p>
              {historicalMeterReadingSave.error ? <FormError>{historicalMeterReadingSave.error}</FormError> : null}
            </div>
            <div className="dialog-actions">
              <button
                className="primary-button"
                type="button"
                disabled={savingMeterRowId === historicalMeterReadingSave.row.id}
                onClick={() => void confirmHistoricalMeterReadingSave()}
              >
                <Save size={16} aria-hidden="true" />
                {savingMeterRowId === historicalMeterReadingSave.row.id ? 'Сохраняем…' : 'Сохранить показание'}
              </button>
              <button
                className="secondary-button"
                type="button"
                disabled={savingMeterRowId === historicalMeterReadingSave.row.id}
                onClick={() => setHistoricalMeterReadingSave(null)}
              >
                Отмена
              </button>
            </div>
          </section>
        </div>
      ) : null}
      {fullPaymentDialogOpen ? (
        <FullPaymentPrototypeDialog
          periodOptions={fullPaymentPeriodOptions}
          onClose={closeFullPaymentDialog}
          onSubmit={commitFullGaragePayment}
        />
      ) : null}
      {garageAccrualDialogOpen ? (
        <GarageAccrualPrototypeDialog
          irregularPayments={irregularPayments.filter((payment) => payment.isActive && !payment.isArchived)}
          onClose={closeGarageAccrualDialog}
          onSubmit={commitGarageAccrual}
        />
      ) : null}
      {expenseDialogPreset ? (
        <NewExpensePrototypeDialog
          availableAmounts={[expenseBankAmount, expenseCashAmount]}
          expenseTypes={expenseTypes.filter((expenseType) => !expenseType.isArchived)}
          preset={expenseDialogPreset}
          suppliers={suppliers.filter((supplier) => !supplier.isArchived)}
          onClose={closeExpenseDialog}
          onSubmit={commitExpensePayment}
        />
      ) : null}
      {staffPaymentDialogPreset ? (
        <StaffPaymentPrototypeDialog
          availableCashAmount={expenseCashAmount}
          preset={staffPaymentDialogPreset}
          staffMembers={staffMembers.filter((staffMember) => !staffMember.isArchived)}
          onClose={closeStaffPaymentDialog}
          onSubmit={commitStaffPayment}
        />
      ) : null}
      {supplierAccrualDialogOpen ? (
        <NewAccrualPrototypeDialog
          expenseTypes={expenseTypes.filter((expenseType) => !expenseType.isArchived)}
          suppliers={suppliers.filter((supplier) => !supplier.isArchived)}
          onClose={closeSupplierAccrualDialog}
          onSubmit={commitSupplierAccrual}
        />
      ) : null}
      {historyEdit ? (
        <GaragePaymentHistoryEditDialog
          state={historyEdit}
          saving={historyActionSaving}
          onChange={(patch) => setHistoryEdit((value) => value ? { ...value, ...patch, error: null } : value)}
          onClose={closeHistoryEditDialog}
          onSubmit={saveHistoryEdit}
        />
      ) : null}
      {historyCancel ? (
        <GaragePaymentHistoryCancelDialog
          state={historyCancel}
          saving={historyActionSaving}
          onChange={(patch) => setHistoryCancel((value) => value ? { ...value, ...patch, error: null } : value)}
          onClose={closeHistoryCancelDialog}
          onConfirm={confirmHistoryCancel}
        />
      ) : null}
      {staffSalaryAdjustmentDialogPreset ? (
        <StaffSalaryAdjustmentPrototypeDialog
          preset={staffSalaryAdjustmentDialogPreset}
          staffMembers={staffMembers.filter((staffMember) => !staffMember.isArchived)}
          onClose={closeStaffSalaryAdjustmentDialog}
          onSubmit={commitStaffSalaryAdjustment}
        />
      ) : null}
      {penaltyAccrualDialogOpen ? (
        <PenaltyAccrualPrototypeDialog
          onClose={closePenaltyAccrualDialog}
          onSubmit={commitPenaltyAccrual}
        />
      ) : null}
      {earlyElectricityPaymentConfirmation ? (
        <EarlyElectricityPaymentConfirmationDialog
          state={earlyElectricityPaymentConfirmation}
          onClose={closeEarlyElectricityPaymentConfirmation}
          onConfirm={() => confirmEarlyElectricityPayment(earlyElectricityPaymentConfirmation)}
        />
      ) : null}
    </section>
  )
}

function EarlyElectricityPaymentConfirmationDialog({
  state,
  onClose,
  onConfirm,
}: {
  state: EarlyElectricityPaymentConfirmationState
  onClose: () => void
  onConfirm: () => void
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  useEscapeKey(true, onClose)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog payments-prototype-dialog--wide" role="dialog" aria-modal="true" aria-labelledby="early-electricity-payment-title" aria-describedby="early-electricity-payment-description" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <p className="eyebrow">Проверка интервала оплаты</p>
            <h3 id="early-electricity-payment-title">Оплата электроэнергии раньше 30 дней</h3>
            <p>{state.row.service} · {formatPaymentPrototypeValue(parsePaymentMoney(state.row.paymentDraft))}</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть предупреждение ранней оплаты" onClick={onClose}>
            <X size={18} aria-hidden="true" />
          </button>
        </div>
        <div className="dictionary-modal-form payments-prototype-modal-form">
          <p className="confirmation-text" id="early-electricity-payment-description">
            Предыдущая оплата была {formatDateOnly(state.previousPaymentDate)} — прошло {state.daysSincePreviousPayment} календ. дн. Проверьте платеж или подтвердите продолжение.
          </p>
          <p className="form-hint">Подтверждение не меняет сумму и не требует комментария.</p>
          <div className="detail-dialog-actions">
            <button ref={cancelRef} className="ghost-button" type="button" onClick={onClose}>Вернуться к платежу</button>
            <button className="secondary-button" type="button" onClick={onConfirm}>Все равно провести</button>
          </div>
        </div>
      </section>
    </div>
  )
}

function GaragePaymentHistoryEditDialog({
  state,
  saving,
  onChange,
  onClose,
  onSubmit,
}: {
  state: GaragePaymentHistoryEditState
  saving: boolean
  onChange: (patch: Partial<Omit<GaragePaymentHistoryEditState, 'row'>>) => void
  onClose: () => void
  onSubmit: () => void
}) {
  const [pendingChanges, setPendingChanges] = useState<ChangePreview[] | null>(null)
  const dialogRef = useFocusTrap<HTMLElement>(!pendingChanges)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const confirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingChanges))
  const confirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingChanges))
  useEscapeKey(!saving && !pendingChanges, onClose)
  useEscapeKey(Boolean(pendingChanges), () => setPendingChanges(null))

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const operation = state.row.operation
    if (!operation) {
      onChange({ error: 'Платеж нельзя изменить: операция не найдена.' })
      return
    }

    const amount = parsePaymentMoney(state.amount)
    if (!Number.isFinite(amount) || amount <= 0) {
      onChange({ error: 'Укажите сумму платежа больше нуля.' })
      return
    }

    const changes: ChangePreview[] = []
    appendChangePreview(changes, 'Сумма', formatChangeMoney(operation.amount), formatChangeMoney(amount))
    appendChangePreview(changes, 'Дата поступления', formatChangeDate(operation.operationDate), formatChangeDate(state.operationDate))
    appendChangePreview(changes, 'Месяц поступления', formatMonth(operation.accountingMonth), formatMonth(`${state.accountingMonth}-01`))
    appendChangePreview(changes, 'Документ', formatChangeText(operation.documentNumber), formatChangeText(state.documentNumber))
    appendChangePreview(changes, 'Комментарий', formatChangeText(operation.comment), formatChangeText(state.comment))
    if (changes.length === 0) {
      onClose()
      return
    }

    onChange({ error: null })
    if (changes.length === 1 && changes[0].field === 'Комментарий') {
      onSubmit()
      return
    }

    setPendingChanges(changes)
  }

  function confirmSubmit() {
    setPendingChanges(null)
    onSubmit()
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={() => {
      if (!saving && !pendingChanges) {
        onClose()
      }
    }}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog payments-prototype-dialog--wide" role="dialog" aria-modal="true" aria-labelledby="garage-payment-edit-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <p className="eyebrow">Платеж гаража</p>
            <h3 id="garage-payment-edit-title">Изменить платеж</h3>
            <p>{state.row.purpose}</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть изменение платежа" onClick={onClose} disabled={saving}>
            <X size={18} aria-hidden="true" />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={handleSubmit}>
          <FormField label="Сумма">
            <MoneyTextInput aria-label="Сумма изменяемого платежа" value={state.amount} onValueChange={(amount) => onChange({ amount })} disabled={saving} />
          </FormField>
          <FormField label="Дата">
            <LocalizedDatePicker ariaLabel="Дата изменяемого платежа" mode="date" value={state.operationDate} onChange={(operationDate) => onChange({ operationDate })} disabled={saving} />
          </FormField>
          <FormField label="Месяц">
            <LocalizedDatePicker ariaLabel="Месяц изменяемого платежа" mode="month" value={state.accountingMonth} onChange={(accountingMonth) => onChange({ accountingMonth })} disabled={saving} />
          </FormField>
          <FormField label="Документ">
            <input aria-label="Документ изменяемого платежа" value={state.documentNumber} onChange={(event) => onChange({ documentNumber: event.target.value })} disabled={saving} />
          </FormField>
          <FormField label="Комментарий">
            <textarea aria-label="Комментарий к изменяемому платежу" rows={4} value={state.comment} onChange={(event) => onChange({ comment: event.target.value })} disabled={saving} />
          </FormField>
          {state.error ? <FormError>{state.error}</FormError> : null}
          <div className="detail-dialog-actions">
            <button ref={cancelRef} className="ghost-button" type="button" onClick={onClose} disabled={saving}>Отмена</button>
            <button className="secondary-button" type="submit" disabled={saving}>
              <Save size={16} aria-hidden="true" />
              <span>{saving ? 'Сохраняем...' : 'Сохранить'}</span>
            </button>
          </div>
        </form>
      </section>
      {pendingChanges ? (
        <section ref={confirmationDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="garage-payment-edit-confirmation-title" aria-describedby="garage-payment-edit-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <div>
              <p className="eyebrow">Проверка изменения</p>
              <h3 id="garage-payment-edit-confirmation-title">Подтвердить изменение платежа?</h3>
              <p>{state.row.purpose}</p>
            </div>
            <button className="icon-button" type="button" aria-label="Закрыть подтверждение платежа" onClick={() => setPendingChanges(null)} disabled={saving}>
              <X size={18} aria-hidden="true" />
            </button>
          </div>
          <p className="confirmation-text" id="garage-payment-edit-confirmation-description">Проверьте изменения перед сохранением. После подтверждения backend запишет корректировку в историю платежей.</p>
          <ul className="dictionary-change-list" aria-label="Изменяемые поля платежа">
            {pendingChanges.map((change) => (
              <li key={change.field}>
                <span className="dictionary-change-field">{change.field}</span>
                <span className="dictionary-change-values">
                  <span className="dictionary-change-value">{change.before}</span>
                  <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                  <span className="dictionary-change-value dictionary-change-value-after">{change.after}</span>
                </span>
              </li>
            ))}
          </ul>
          <div className="detail-dialog-actions contractors-dialog-actions">
            <button ref={confirmationCancelRef} className="ghost-button" type="button" onClick={() => setPendingChanges(null)} disabled={saving}>Отмена</button>
            <button className="secondary-button" type="button" onClick={confirmSubmit} disabled={saving}>
              <Save size={16} aria-hidden="true" />
              <span>{saving ? 'Сохраняем...' : 'Сохранить'}</span>
            </button>
          </div>
        </section>
      ) : null}
    </div>
  )
}

function GaragePaymentHistoryCancelDialog({
  state,
  saving,
  onChange,
  onClose,
  onConfirm,
}: {
  state: GaragePaymentHistoryCancelState
  saving: boolean
  onChange: (patch: Partial<Omit<GaragePaymentHistoryCancelState, 'row'>>) => void
  onClose: () => void
  onConfirm: () => void
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  useEscapeKey(!saving, onClose)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={() => {
      if (!saving) {
        onClose()
      }
    }}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog payments-prototype-dialog--wide" role="dialog" aria-modal="true" aria-labelledby="garage-payment-cancel-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <p className="eyebrow">Отмена платежа</p>
            <h3 id="garage-payment-cancel-title">Отменить платеж?</h3>
            <p>{state.row.purpose} · {formatPaymentPrototypeValue(state.row.amount)}</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть отмену платежа" onClick={onClose} disabled={saving}>
            <X size={18} aria-hidden="true" />
          </button>
        </div>
        <div className="dictionary-modal-form payments-prototype-modal-form">
          <FormField label="Причина отмены">
            <textarea aria-label="Причина отмены платежа" rows={4} value={state.reason} onChange={(event) => onChange({ reason: event.target.value })} disabled={saving} />
          </FormField>
          {state.error ? <FormError>{state.error}</FormError> : null}
          <div className="detail-dialog-actions">
            <button ref={cancelRef} className="ghost-button" type="button" onClick={onClose} disabled={saving}>Отмена</button>
            <button className="secondary-button danger-button" type="button" onClick={onConfirm} disabled={saving}>
              <Trash2 size={16} aria-hidden="true" />
              <span>{saving ? 'Отменяем...' : 'Отменить платеж'}</span>
            </button>
          </div>
        </div>
      </section>
    </div>
  )
}

function BankDepositPrototypeDialog({
  auth,
  financeClient,
  onClose,
  onSaved,
}: {
  auth: AuthResponse
  financeClient: FinanceClient
  onClose: () => void
  onSaved: () => void
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const [operationDate, setOperationDate] = useState(getLocalDateInputValue())
  const [amount, setAmount] = useState('')
  const [comment, setComment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEscapeKey(true, onClose)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedAmount = parsePaymentMoney(amount)
    if (!operationDate) {
      setError('Укажите дату сдачи кассы.')
      return
    }
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError('Укажите сумму сдачи больше нуля.')
      return
    }
    setSaving(true)
    setError(null)
    try {
      await financeClient.createCashBankTransfer(auth.accessToken, {
        transferDate: operationDate,
        amount: parsedAmount,
        comment: comment.trim() || undefined,
      })
      onSaved()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Не удалось сохранить сдачу кассы в банк.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog bank-deposit-dialog" role="dialog" aria-modal="true" aria-labelledby="bank-deposit-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="bank-deposit-title">Учет суммы на счете в банке</h3>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть учет суммы в банке" onClick={onClose}>
            <X size={18} />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form bank-deposit-form" onSubmit={handleSubmit}>
          <FormField
            className="bank-deposit-form__amount"
            label="Сумма"
            hint="Сумма будет списана из кассы и зачислена на банковский счет."
          >
            <MoneyTextInput aria-label="Сумма в банке" value={amount} onValueChange={(nextAmount) => {
              setAmount(nextAmount)
              setError(null)
            }} disabled={saving} />
          </FormField>
          <FormField className="bank-deposit-form__date" label="Дата">
            <LocalizedDatePicker ariaLabel="Дата учета суммы в банке" mode="date" value={operationDate} disabled={saving} onChange={(nextOperationDate) => {
              setOperationDate(nextOperationDate)
              setError(null)
            }} />
          </FormField>
          <FormField className="bank-deposit-form__comment" label="Комментарий">
            <textarea aria-label="Комментарий к сумме в банке" rows={4} value={comment} onChange={(event) => setComment(event.target.value)} disabled={saving} />
          </FormField>
          {error ? <FormError>{error}</FormError> : null}
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit" disabled={saving}><Save size={17} aria-hidden="true" /><span>{saving ? 'Сохраняем...' : 'Сохранить'}</span></button>
            <button ref={cancelRef} className="ghost-button" type="button" onClick={onClose} disabled={saving}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function NewExpensePrototypeDialog({
  availableAmounts,
  expenseTypes,
  preset,
  suppliers,
  onClose,
  onSubmit,
}: {
  availableAmounts: [number, number]
  expenseTypes: AccountingTypeDto[]
  preset: ExpensePrototypeDialogPreset
  suppliers: SupplierDto[]
  onClose: () => void
  onSubmit: (request: ExpensePrototypeSubmitRequest) => Promise<string | null>
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const presetExpenseType = preset.expenseTypeName
    ? expenseTypes.find((expenseType) => expenseType.name.trim().toLocaleLowerCase('ru-RU') === preset.expenseTypeName?.trim().toLocaleLowerCase('ru-RU'))
    : null
  const availableSuppliers = suppliers.filter((supplier) => Boolean(
    getSupplierAccrualExpenseType(supplier, expenseTypes) && supplier.expenseFundId,
  ))
  const initialSupplier = availableSuppliers.find((supplier) => supplier.expenseTypeId === presetExpenseType?.id)
    ?? getFirstLinkedSupplier(availableSuppliers, expenseTypes)
  const [expensePaymentSource, setExpensePaymentSource] = useState<ExpensePaymentSource>(preset.expensePaymentSource)
  const isCashExpense = expensePaymentSource === 'cash'
  const [supplierId, setSupplierId] = useState(initialSupplier?.id ?? '')
  const [expenseTypeId, setExpenseTypeId] = useState(
    preset.expensePaymentSource === 'cash'
      ? (presetExpenseType ?? expenseTypes.find((expenseType) => !expenseType.isArchived))?.id ?? ''
      : getSupplierAccrualExpenseType(initialSupplier, expenseTypes)?.id ?? '',
  )
  const [expenseFundId, setExpenseFundId] = useState(initialSupplier?.expenseFundId ?? '')
  const [expensePaymentType, setExpensePaymentType] = useState<ExpensePaymentType>('with_receipt')
  const [counterpartyName, setCounterpartyName] = useState('')
  const [confirmNegativeFundBalance, setConfirmNegativeFundBalance] = useState(false)
  const [operationDate, setOperationDate] = useState(getLocalDateInputValue())
  const [accountingMonth, setAccountingMonth] = useState(getLocalDateInputValue().slice(0, 7))
  const [amount, setAmount] = useState(preset.amount ? String(preset.amount) : '')
  const [documentNumber, setDocumentNumber] = useState('')
  const [comment, setComment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const selectedSupplier = suppliers.find((supplier) => supplier.id === supplierId)
  useEscapeKey(true, onClose)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedAmount = parsePaymentMoney(amount)
    if (!isCashExpense && !supplierId) {
      setError('Для выплаты с банковского счёта выберите поставщика.')
      return
    }
    if (!expenseTypeId) {
      setError('Выберите услугу или статью выплаты.')
      return
    }
    if (!isCashExpense && !selectedSupplier?.expenseFundId) {
      setError('Для услуги поставщика должен быть настроен фонд расходования.')
      return
    }
    if (!operationDate) {
      setError('Укажите дату выплаты.')
      return
    }
    if (!/^\d{4}-\d{2}$/.test(accountingMonth)) {
      setError('Укажите месяц выплаты.')
      return
    }
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError('Укажите сумму выплаты больше нуля.')
      return
    }
    const availableFundBalance = selectedSupplier?.expenseFundBalance ?? 0

    setSaving(true)
    setError(null)
    try {
      const submitError = await onSubmit({
        supplierId: isCashExpense ? undefined : supplierId,
        counterpartyName,
        expenseTypeId,
        expensePaymentType,
        expensePaymentSource,
        expenseFundId: isCashExpense ? undefined : expenseFundId,
        confirmNegativeFundBalance: !isCashExpense && parsedAmount > availableFundBalance && confirmNegativeFundBalance,
        operationDate,
        accountingMonth: `${accountingMonth}-01`,
        amount: parsedAmount,
        documentNumber,
        comment,
        rowIndex: preset.rowIndex,
      })
      if (submitError) {
        setError(submitError)
        return
      }
      onClose()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Не удалось провести выплату. Повторите попытку позже.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog--wide" role="dialog" aria-modal="true" aria-labelledby="new-expense-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="new-expense-title">Добавить выплату</h3>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть новую выплату" onClick={onClose}>
            <X size={18} />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form expense-form" onSubmit={handleSubmit}>
          <FormField className="full-payment-field" label="Источник и вид выплаты">
            <SelectControl
              aria-label="Источник выплаты"
              value={expensePaymentSource}
              options={[
                { value: 'bank', label: 'Банк · поставщику' },
                { value: 'cash', label: 'Касса · эпизодическая' },
              ]}
              disabled={saving}
              onChange={(nextSource) => {
                const source = nextSource as ExpensePaymentSource
                setExpensePaymentSource(source)
                setConfirmNegativeFundBalance(false)
                if (source === 'bank') {
                  const nextSupplier = suppliers.find((supplier) => supplier.id === supplierId) ?? initialSupplier
                  setSupplierId(nextSupplier?.id ?? '')
                  setExpenseTypeId(getSupplierAccrualExpenseType(nextSupplier, expenseTypes)?.id ?? '')
                  setExpenseFundId(nextSupplier?.expenseFundId ?? '')
                } else {
                  setExpenseTypeId(expenseTypes.find((expenseType) => !expenseType.isArchived)?.id ?? '')
                  setExpenseFundId('')
                }
                setError(null)
              }} />
          </FormField>
          <p className="form-hint full-payment-field" role="status">
            Доступно в {isCashExpense ? 'кассе' : 'банке'}: {formatMoney(availableAmounts[Number(isCashExpense)])}.
          </p>
          {!isCashExpense ? <FormField label="Поставщик">
            <SelectControl
              aria-label="Поставщик выплаты"
              value={supplierId}
              options={availableSuppliers.length > 0
                ? availableSuppliers.map((supplier) => ({ value: supplier.id, label: supplier.name }))
                : [{ value: '', label: 'Нет поставщиков' }]}
              disabled={saving}
              onChange={(nextSupplierId) => {
                setSupplierId(nextSupplierId)
                const nextSupplier = suppliers.find((supplier) => supplier.id === nextSupplierId)
                setExpenseTypeId(getSupplierAccrualExpenseType(nextSupplier, expenseTypes)?.id ?? '')
                setExpenseFundId(nextSupplier?.expenseFundId ?? '')
                setConfirmNegativeFundBalance(false)
                setError(null)
              }} />
          </FormField> : (
            <FormField label="Получатель (необязательно)" help="Можно указать имя или организацию без создания карточки поставщика.">
              <input aria-label="Получатель эпизодической выплаты" maxLength={200} value={counterpartyName} onChange={(event) => setCounterpartyName(event.target.value)} disabled={saving} />
            </FormField>
          )}
          <FormField label={isCashExpense ? 'Услуга или статья' : 'Услуга'}>
            <SelectControl
              aria-label="Услуга выплаты поставщику"
              value={expenseTypeId}
              options={isCashExpense
                ? expenseTypes.filter((expenseType) => !expenseType.isArchived).map((expenseType) => ({ value: expenseType.id, label: expenseType.name }))
                : expenseTypeId
                  ? [{ value: expenseTypeId, label: expenseTypes.find((expenseType) => expenseType.id === expenseTypeId)?.name ?? 'Настроенная услуга' }]
                  : [{ value: '', label: 'Услуга не настроена' }]}
              onChange={(nextExpenseTypeId) => {
                setExpenseTypeId(nextExpenseTypeId)
                setError(null)
              }}
              disabled={!isCashExpense || saving} />
          </FormField>
          {!isCashExpense ? <p className="form-hint">
            Фонд расходования: {selectedSupplier?.expenseFundName ?? 'не настроен'}
            {selectedSupplier?.expenseFundId
              ? ` · доступно в фонде ${formatMoney(selectedSupplier.expenseFundBalance ?? 0)}`
              : ''}
          </p> : null}
          {isCashExpense ? (
            <FormField className="full-payment-field" label="Тип выплаты">
              <SelectControl
                aria-label="Тип выплаты"
                value={expensePaymentType}
                options={expensePaymentTypeOptions}
                disabled={saving}
                onChange={(nextExpensePaymentType) => {
                  setExpensePaymentType(nextExpensePaymentType as ExpensePaymentType)
                  setError(null)
                }} />
            </FormField>
          ) : null}
          <FormField label="Дата">
            <LocalizedDatePicker ariaLabel="Дата выплаты" mode="date" value={operationDate} disabled={saving} onChange={(nextOperationDate) => {
              setOperationDate(nextOperationDate)
              setError(null)
            }} />
          </FormField>
          <FormField label="Месяц">
            <LocalizedDatePicker ariaLabel="Месяц выплаты" mode="month" value={accountingMonth} disabled={saving} onChange={(nextAccountingMonth) => {
              setAccountingMonth(nextAccountingMonth)
              setError(null)
            }} />
          </FormField>
          <FormField label="Сумма">
            <MoneyTextInput aria-label="Сумма выплаты" value={amount} onValueChange={(nextAmount) => {
              setAmount(nextAmount)
              setConfirmNegativeFundBalance(false)
              setError(null)
            }} />
          </FormField>
          {!isCashExpense && (parsePaymentMoney(amount) ?? 0) > (selectedSupplier?.expenseFundBalance ?? 0) ? (
            <label className="payments-negative-fund-confirmation">
              <input
                type="checkbox"
                aria-label="Подтвердить отрицательный остаток фонда"
                checked={confirmNegativeFundBalance}
                onChange={(event) => {
                  setConfirmNegativeFundBalance(event.target.checked)
                  setError(null)
                }}
                disabled={saving}
              />
              <span>
                <strong>После выплаты фонд станет отрицательным.</strong>
                <small>Банк будет проверен отдельно. Подтверждение сохранится в истории изменений.</small>
              </span>
            </label>
          ) : null}
          <FormField label="Документ">
            <input aria-label="Документ выплаты" value={documentNumber} onChange={(event) => setDocumentNumber(event.target.value)} />
          </FormField>
          <FormField className="full-payment-field" label="Комментарий">
            <textarea aria-label="Комментарий к выплате" rows={4} value={comment} onChange={(event) => setComment(event.target.value)} />
          </FormField>
          {error ? <FormError>{error}</FormError> : null}
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit" disabled={saving}>{saving ? 'Сохраняем...' : 'Провести'}</button>
            <button ref={cancelRef} className="secondary-button" type="button" onClick={onClose} disabled={saving}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function StaffPaymentPrototypeDialog({
  availableCashAmount,
  preset,
  staffMembers,
  onClose,
  onSubmit,
}: {
  availableCashAmount: number
  preset: StaffPaymentPrototypeDialogPreset
  staffMembers: StaffMemberDto[]
  onClose: () => void
  onSubmit: (request: StaffPaymentPrototypeSubmitRequest) => Promise<string | null>
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const normalizedPresetName = preset.staffMemberName?.trim().toLocaleLowerCase('ru-RU') ?? ''
  const presetStaffMember = normalizedPresetName
    ? staffMembers.find((member) => member.fullName.trim().toLocaleLowerCase('ru-RU').includes(normalizedPresetName))
    : null
  const [staffMemberId, setStaffMemberId] = useState(presetStaffMember?.id ?? staffMembers[0]?.id ?? '')
  const [operationDate, setOperationDate] = useState(getLocalDateInputValue())
  const [accountingMonth, setAccountingMonth] = useState(getLocalDateInputValue().slice(0, 7))
  const [amount, setAmount] = useState(preset.amount ? String(preset.amount) : '')
  const [documentNumber, setDocumentNumber] = useState('')
  const [comment, setComment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEscapeKey(true, onClose)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedAmount = parsePaymentMoney(amount)
    if (!staffMemberId) {
      setError('Выберите сотрудника из справочника персонала.')
      return
    }
    if (!operationDate) {
      setError('Укажите дату выплаты сотруднику.')
      return
    }
    if (!/^\d{4}-\d{2}$/.test(accountingMonth)) {
      setError('Укажите месяц выплаты сотруднику.')
      return
    }
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError('Укажите сумму выплаты сотруднику больше нуля.')
      return
    }
    if (parsedAmount > availableCashAmount) {
      setError(`В кассе недостаточно средств. Доступно ${formatMoney(availableCashAmount)}.`)
      return
    }

    setSaving(true)
    setError(null)
    try {
      const submitError = await onSubmit({
        staffMemberId,
        operationDate,
        accountingMonth: `${accountingMonth}-01`,
        amount: parsedAmount,
        documentNumber,
        comment,
        rowIndex: preset.rowIndex,
      })
      if (submitError) {
        setError(submitError)
        return
      }
      onClose()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Не удалось провести выплату сотруднику. Повторите попытку позже.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog" role="dialog" aria-modal="true" aria-labelledby="staff-payment-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="staff-payment-title">Выплата сотруднику</h3>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть выплату сотруднику" onClick={onClose}>
            <X size={18} />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={handleSubmit}>
          <p className="form-hint">Источник выплаты: <strong>касса</strong>. Доступно {formatMoney(availableCashAmount)}. Фонд расходования не требуется.</p>
          {error ? <FormError>{error}</FormError> : null}
          <FormField label="Сотрудник">
            <SelectControl
              aria-label="Сотрудник выплаты"
              value={staffMemberId}
              options={staffMembers.length > 0
                ? staffMembers.map((member) => ({ value: member.id, label: `${member.fullName} · ${member.departmentName}` }))
                : [{ value: '', label: 'Нет сотрудников' }]}
              disabled={saving}
              onChange={(nextStaffMemberId) => {
                setStaffMemberId(nextStaffMemberId)
                setError(null)
              }} />
          </FormField>
          <FormField label="Дата">
            <LocalizedDatePicker ariaLabel="Дата выплаты сотруднику" mode="date" value={operationDate} disabled={saving} onChange={(nextOperationDate) => {
              setOperationDate(nextOperationDate)
              setError(null)
            }} />
          </FormField>
          <FormField label="Месяц">
            <LocalizedDatePicker ariaLabel="Месяц выплаты сотруднику" mode="month" value={accountingMonth} disabled={saving} onChange={(nextAccountingMonth) => {
              setAccountingMonth(nextAccountingMonth)
              setError(null)
            }} />
          </FormField>
          <FormField label="Сумма">
            <MoneyTextInput aria-label="Сумма выплаты сотруднику" value={amount} onValueChange={(nextAmount) => {
              setAmount(nextAmount)
              setError(null)
            }} />
          </FormField>
          <FormField label="Документ">
            <input aria-label="Документ выплаты сотруднику" value={documentNumber} onChange={(event) => setDocumentNumber(event.target.value)} />
          </FormField>
          <FormField label="Комментарий">
            <textarea aria-label="Комментарий к выплате сотруднику" rows={4} value={comment} onChange={(event) => setComment(event.target.value)} />
          </FormField>
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit" disabled={saving}>{saving ? 'Сохраняем...' : 'Провести'}</button>
            <button ref={cancelRef} className="secondary-button" type="button" onClick={onClose} disabled={saving}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function StaffSalaryAdjustmentPrototypeDialog({
  preset,
  staffMembers,
  onClose,
  onSubmit,
}: {
  preset: StaffSalaryAdjustmentPrototypeDialogPreset
  staffMembers: StaffMemberDto[]
  onClose: () => void
  onSubmit: (request: StaffSalaryAdjustmentPrototypeSubmitRequest) => Promise<string | null>
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const [staffMemberId, setStaffMemberId] = useState(staffMembers[0]?.id ?? '')
  const [accountingMonth, setAccountingMonth] = useState(preset.accountingMonth)
  const [amount, setAmount] = useState('')
  const [documentNumber, setDocumentNumber] = useState('')
  const [reason, setReason] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const isBonus = preset.adjustmentType === 'bonus'
  const actionName = isBonus ? 'премию' : 'штраф'
  const selectedStaffMember = staffMembers.find((member) => member.id === staffMemberId)
  useEscapeKey(true, onClose)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedAmount = parsePaymentMoney(amount)
    if (!staffMemberId) {
      setError('Выберите сотрудника из справочника персонала.')
      return
    }
    if (!/^\d{4}-\d{2}$/.test(accountingMonth)) {
      setError(`Укажите месяц, за который начисляется ${actionName}.`)
      return
    }
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError(`Укажите сумму ${isBonus ? 'премии' : 'штрафа'} больше нуля.`)
      return
    }
    if (!reason.trim()) {
      setError(`Укажите основание для ${isBonus ? 'премии' : 'штрафа'}.`)
      return
    }

    setSaving(true)
    setError(null)
    try {
      const submitError = await onSubmit({
        staffMemberId,
        accountingMonth: `${accountingMonth}-01`,
        adjustmentType: preset.adjustmentType,
        amount: parsedAmount,
        documentNumber,
        reason,
      })
      if (submitError) {
        setError(submitError)
        return
      }
      onClose()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : `Не удалось начислить ${actionName}. Повторите попытку позже.`)
    } finally {
      setSaving(false)
    }
  }

  const title = isBonus ? 'Начислить премию сотруднику' : 'Начислить штраф сотруднику'
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog" role="dialog" aria-modal="true" aria-labelledby="staff-salary-adjustment-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="staff-salary-adjustment-title">{title}</h3>
          </div>
          <button className="icon-button" type="button" aria-label={`Закрыть: ${title.toLocaleLowerCase('ru-RU')}`} onClick={onClose}>
            <X size={18} />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={handleSubmit}>
          <FormField
            label="Сотрудник"
            hint={selectedStaffMember ? `Месячный оклад: ${formatPaymentMoney(selectedStaffMember.rate)}` : undefined}>
            <SelectControl
              aria-label={`Сотрудник для ${isBonus ? 'премии' : 'штрафа'}`}
              value={staffMemberId}
              options={staffMembers.length > 0
                ? staffMembers.map((member) => ({ value: member.id, label: `${member.fullName} · ${member.departmentName}` }))
                : [{ value: '', label: 'Нет сотрудников' }]}
              disabled={saving}
              onChange={(nextStaffMemberId) => {
                setStaffMemberId(nextStaffMemberId)
                setError(null)
              }} />
          </FormField>
          <FormField label="Месяц">
            <LocalizedDatePicker ariaLabel={`Месяц ${isBonus ? 'премии' : 'штрафа'}`} mode="month" value={accountingMonth} disabled={saving} onChange={(nextAccountingMonth) => {
              setAccountingMonth(nextAccountingMonth)
              setError(null)
            }} />
          </FormField>
          <FormField label="Сумма">
            <MoneyTextInput aria-label={`Сумма ${isBonus ? 'премии' : 'штрафа'}`} value={amount} onValueChange={(nextAmount) => {
              setAmount(nextAmount)
              setError(null)
            }} />
          </FormField>
          <FormField label="Документ">
            <input aria-label={`Документ ${isBonus ? 'премии' : 'штрафа'}`} value={documentNumber} disabled={saving} onChange={(event) => setDocumentNumber(event.target.value)} />
          </FormField>
          <FormField label="Основание">
            <textarea aria-label={`Основание ${isBonus ? 'премии' : 'штрафа'}`} rows={4} value={reason} disabled={saving} onChange={(event) => {
              setReason(event.target.value)
              setError(null)
            }} />
          </FormField>
          {error ? <FormError>{error}</FormError> : null}
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit" disabled={saving}>{saving ? 'Сохраняем...' : title}</button>
            <button ref={cancelRef} className="secondary-button" type="button" onClick={onClose} disabled={saving}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function NewAccrualPrototypeDialog({
  expenseTypes,
  suppliers,
  onClose,
  onSubmit,
}: {
  expenseTypes: AccountingTypeDto[]
  suppliers: SupplierDto[]
  onClose: () => void
  onSubmit: (request: SupplierAccrualPrototypeSubmitRequest) => Promise<string | null>
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const initialSupplier = getFirstLinkedSupplier(suppliers, expenseTypes) ?? suppliers[0]
  const [supplierId, setSupplierId] = useState(initialSupplier?.id ?? '')
  const [expenseTypeId, setExpenseTypeId] = useState(getSupplierAccrualExpenseType(initialSupplier, expenseTypes)?.id ?? '')
  const [accountingMonth, setAccountingMonth] = useState(getLocalDateInputValue().slice(0, 7))
  const [amount, setAmount] = useState('')
  const [documentNumber, setDocumentNumber] = useState('')
  const [comment, setComment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const selectedSupplier = suppliers.find((supplier) => supplier.id === supplierId)
  useEscapeKey(true, onClose)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedAmount = parsePaymentMoney(amount)
    if (!supplierId) {
      setError('Выберите поставщика из справочника.')
      return
    }
    if (!expenseTypeId) {
      setError('Для выбранного поставщика не настроена услуга начисления.')
      return
    }
    if (!selectedSupplier?.expenseFundId) {
      setError('Для услуги поставщика должен быть настроен фонд расходования.')
      return
    }
    if (!/^\d{4}-\d{2}$/.test(accountingMonth)) {
      setError('Укажите месяц начисления.')
      return
    }
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError('Укажите сумму начисления больше нуля.')
      return
    }

    setSaving(true)
    setError(null)
    try {
      const submitError = await onSubmit({
        supplierId,
        expenseTypeId,
        accountingMonth: `${accountingMonth}-01`,
        amount: parsedAmount,
        documentNumber,
        comment,
      })
      if (submitError) {
        setError(submitError)
        return
      }
      onClose()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Не удалось сохранить начисление. Повторите попытку позже.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog payments-prototype-dialog--wide" role="dialog" aria-modal="true" aria-labelledby="new-accrual-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="new-accrual-title">Новое начисление</h3>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть новое начисление" onClick={onClose}>
            <X size={18} />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={handleSubmit}>
          <FormField label="Поставщик">
            <SelectControl
              aria-label="Поставщик начисления"
              value={supplierId}
              options={suppliers.length > 0
                ? suppliers.map((supplier) => ({ value: supplier.id, label: supplier.name }))
                : [{ value: '', label: 'Нет поставщиков' }]}
              disabled={saving}
              onChange={(nextSupplierId) => {
                setSupplierId(nextSupplierId)
                setExpenseTypeId(getSupplierAccrualExpenseType(
                  suppliers.find((supplier) => supplier.id === nextSupplierId),
                  expenseTypes,
                )?.id ?? '')
                setError(null)
              }} />
          </FormField>
          <FormField
            label="Услуга"
            hint={`Фонд расходования: ${selectedSupplier?.expenseFundName ?? 'не настроен'}`}>
            <SelectControl
              aria-label="Услуга начисления поставщику"
              value={expenseTypeId}
              options={expenseTypeId
                ? [{
                    value: expenseTypeId,
                    label: getSupplierAccrualExpenseType(
                      suppliers.find((supplier) => supplier.id === supplierId),
                      expenseTypes,
                    )?.name ?? 'Связанная услуга',
                  }]
                : [{ value: '', label: 'Для поставщика услуга не настроена' }]}
              onChange={() => undefined}
              disabled />
          </FormField>
          <FormField label="Месяц">
            <LocalizedDatePicker ariaLabel="Месяц начисления поставщику" mode="month" value={accountingMonth} disabled={saving} onChange={(nextAccountingMonth) => {
              setAccountingMonth(nextAccountingMonth)
              setError(null)
            }} />
          </FormField>
          <FormField label="Сумма">
            <MoneyTextInput aria-label="Сумма начисления поставщику" value={amount} onValueChange={(nextAmount) => {
              setAmount(nextAmount)
              setError(null)
            }} />
          </FormField>
          <FormField label="Документ">
            <input aria-label="Документ начисления поставщику" value={documentNumber} onChange={(event) => setDocumentNumber(event.target.value)} />
          </FormField>
          <FormField label="Комментарий">
            <textarea aria-label="Комментарий начисления поставщику" rows={5} value={comment} onChange={(event) => setComment(event.target.value)} />
          </FormField>
          {error ? <FormError>{error}</FormError> : null}
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit" disabled={saving}>{saving ? 'Сохраняем...' : 'Ок'}</button>
            <button ref={cancelRef} className="secondary-button" type="button" onClick={onClose} disabled={saving}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function PenaltyAccrualPrototypeDialog({
  onClose,
  onSubmit,
}: {
  onClose: () => void
  onSubmit: (request: PenaltyAccrualPrototypeSubmitRequest) => Promise<string | null>
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const [amount, setAmount] = useState('')
  const [accountingMonth, setAccountingMonth] = useState(getLocalDateInputValue().slice(0, 7))
  const [reason, setReason] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEscapeKey(true, onClose)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedAmount = parsePaymentMoney(amount)
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError('Укажите сумму штрафа больше нуля.')
      return
    }
    if (!/^\d{4}-\d{2}$/.test(accountingMonth)) {
      setError('Укажите месяц начисления штрафа.')
      return
    }
    if (!reason.trim()) {
      setError('Укажите причину начисления штрафа.')
      return
    }

    setSaving(true)
    setError(null)
    try {
      const submitError = await onSubmit({
        amount: parsedAmount,
        accountingMonth: `${accountingMonth}-01`,
        reason: reason.trim(),
      })
      if (submitError) {
        setError(submitError)
        return
      }
      onClose()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Не удалось начислить штраф. Повторите попытку позже.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog" role="dialog" aria-modal="true" aria-labelledby="penalty-accrual-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="penalty-accrual-title">Начислить штраф</h3>
            <p>Сумма может быть произвольной. Причина сохранится в истории изменений.</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть начисление штрафа" onClick={onClose}>
            <X size={18} aria-hidden="true" />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={handleSubmit}>
          <FormField label="Сумма штрафа">
            <MoneyTextInput aria-label="Сумма штрафа" value={amount} onValueChange={(nextAmount) => {
              setAmount(nextAmount)
              setError(null)
            }} />
          </FormField>
          <FormField label="Месяц">
            <LocalizedDatePicker ariaLabel="Месяц начисления штрафа" mode="month" value={accountingMonth} onChange={(nextAccountingMonth) => {
              setAccountingMonth(nextAccountingMonth)
              setError(null)
            }} />
          </FormField>
          <FormField label="Причина">
            <textarea aria-label="Причина начисления штрафа" rows={5} value={reason} onChange={(event) => {
              setReason(event.target.value)
              setError(null)
            }} />
          </FormField>
          {error ? <FormError>{error}</FormError> : null}
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit" disabled={saving}>{saving ? 'Начисляем...' : 'Начислить'}</button>
            <button ref={cancelRef} className="secondary-button" type="button" onClick={onClose} disabled={saving}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function GarageAccrualPrototypeDialog({
  irregularPayments,
  onClose,
  onSubmit,
}: {
  irregularPayments: IrregularPaymentDto[]
  onClose: () => void
  onSubmit: (request: GarageAccrualPrototypeSubmitRequest) => Promise<string | null>
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const basisOptionsId = useId()
  const [basis, setBasis] = useState(irregularPayments[0]?.name ?? '')
  const [amount, setAmount] = useState(irregularPayments[0] ? String(irregularPayments[0].amount) : '')
  const [accountingMonth, setAccountingMonth] = useState(getLocalDateInputValue().slice(0, 7))
  const [comment, setComment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEscapeKey(true, onClose)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!basis.trim()) {
      setError('Укажите основание начисления.')
      return
    }
    const parsedAmount = parsePaymentMoney(amount)
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError('Укажите сумму начисления больше нуля.')
      return
    }
    if (!/^\d{4}-\d{2}$/.test(accountingMonth)) {
      setError('Укажите месяц начисления.')
      return
    }
    setSaving(true)
    setError(null)
    try {
      const submitError = await onSubmit({
        basis: basis.trim(),
        amount: parsedAmount,
        accountingMonth: `${accountingMonth}-01`,
        comment,
      })
      if (submitError) {
        setError(submitError)
        return
      }
      onClose()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Не удалось сохранить начисление. Повторите попытку позже.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog payments-prototype-dialog--wide" role="dialog" aria-modal="true" aria-labelledby="garage-accrual-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="garage-accrual-title">Новое начисление</h3>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть начисление гаража" onClick={onClose}>
            <X size={18} aria-hidden="true" />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={handleSubmit}>
          <FormField label="Основание">
            <input
              aria-label="Основание начисления гаража"
              list={basisOptionsId}
              maxLength={200}
              value={basis}
              placeholder="Выберите готовое основание или введите своё"
              onChange={(event) => {
                const nextBasis = event.target.value
                const matchedPayment = irregularPayments.find((payment) => payment.name.trim().toLocaleLowerCase('ru-RU') === nextBasis.trim().toLocaleLowerCase('ru-RU'))
                setBasis(nextBasis)
                if (matchedPayment) {
                  setAmount(String(matchedPayment.amount))
                }
                setError(null)
              }}
            />
            <datalist id={basisOptionsId}>
              {irregularPayments.map((payment) => <option key={payment.id} value={payment.name}>{formatPaymentPrototypeValue(payment.amount)}</option>)}
            </datalist>
          </FormField>
          <FormField label="Сумма">
            <MoneyTextInput
              aria-label="Сумма нерегулярного начисления гаража"
              value={amount}
              onValueChange={(nextAmount) => {
                setAmount(nextAmount)
                setError(null)
              }}
            />
          </FormField>
          <FormField label="Месяц">
            <LocalizedDatePicker ariaLabel="Месяц начисления гаража" mode="month" value={accountingMonth} onChange={(nextAccountingMonth) => {
              setAccountingMonth(nextAccountingMonth)
              setError(null)
            }} />
          </FormField>
          <FormField label="Комментарий">
            <textarea aria-label="Комментарий к начислению гаража" rows={5} value={comment} onChange={(event) => setComment(event.target.value)} />
          </FormField>
          {error ? <FormError>{error}</FormError> : null}
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit" disabled={saving}>{saving ? 'Сохраняем...' : 'Ок'}</button>
            <button ref={cancelRef} className="secondary-button" type="button" onClick={onClose} disabled={saving}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function FullPaymentPrototypeDialog({
  periodOptions,
  onClose,
  onSubmit,
}: {
  periodOptions: FullPaymentPrototypePeriodOption[]
  onClose: () => void
  onSubmit: (request: FullPaymentPrototypeSubmitRequest) => Promise<string | null>
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const [period, setPeriod] = useState(periodOptions[0]?.value ?? 'full')
  const [amount, setAmount] = useState(() => periodOptions[0]?.debt ?? 0)
  const [comment, setComment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEscapeKey(true, onClose)

  const selectedDebt = periodOptions.find((option) => option.value === period)?.debt ?? 0
  const hasDebt = periodOptions.some((option) => option.debt > 0)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!Number.isFinite(amount) || amount <= 0) {
      setError('Укажите сумму полной оплаты больше нуля.')
      return
    }
    if (amount > selectedDebt) {
      setError(`Сумма оплаты не может превышать долг ${formatPaymentMoney(selectedDebt)}.`)
      return
    }

    setSaving(true)
    setError(null)
    try {
      const submitError = await onSubmit({ period, amount, comment })
      if (submitError) {
        setError(submitError)
        return
      }
      onClose()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Не удалось провести полную оплату. Повторите попытку позже.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog full-payment-dialog" role="dialog" aria-modal="true" aria-labelledby="full-payment-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="full-payment-title">Полная оплата</h3>
            <p>Выберите расчетный период и укажите сумму оплаты задолженности.</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть полную оплату" onClick={onClose}>
            <X size={18} aria-hidden="true" />
          </button>
        </div>
        {!hasDebt ? (
          <>
            <div className="full-payment-empty" role="status" aria-live="polite">
              <span className="full-payment-empty__icon" aria-hidden="true"><WalletCards size={22} /></span>
              <div>
                <strong>Задолженности для оплаты нет</strong>
                <p>По выбранному гаражу нет долга за загруженный расчетный период.</p>
              </div>
            </div>
            <div className="detail-dialog-actions">
              <button ref={cancelRef} className="ghost-button" type="button" onClick={onClose}>Закрыть</button>
            </div>
          </>
        ) : (
        <form className="dictionary-modal-form payments-prototype-modal-form full-payment-form" onSubmit={handleSubmit}>
          <div className="full-payment-fields">
            <FormField className="full-payment-field" label="Расчетный период">
              <SelectControl
                aria-label="Период полной оплаты"
                value={period}
                options={periodOptions.filter((option) => option.debt > 0).map((option) => ({ value: option.value, label: option.label }))}
                disabled={saving}
                onChange={(nextPeriod) => {
                  const nextDebt = periodOptions.find((option) => option.value === nextPeriod)?.debt ?? 0
                  setPeriod(nextPeriod)
                  setAmount(nextDebt)
                  setError(null)
                }}
              />
            </FormField>
            <FormField className="full-payment-field full-payment-amount" label="Сумма оплаты" hint={`Доступный долг: ${formatPaymentMoney(selectedDebt)} руб.`}>
              <MoneyInput aria-label="Сумма полной оплаты" value={amount} onValueChange={(nextAmount) => {
                setAmount(nextAmount)
                setError(null)
              }} disabled={saving} />
            </FormField>
          </div>
          <FormField className="full-payment-field" label="Комментарий" hint="Необязательно">
            <textarea aria-label="Комментарий к полной оплате" rows={3} value={comment} onChange={(event) => setComment(event.target.value)} disabled={saving} />
          </FormField>
          {error ? <FormError>{error}</FormError> : null}
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit" disabled={saving}>{saving ? 'Сохраняем...' : 'Провести оплату'}</button>
            <button ref={cancelRef} className="ghost-button" type="button" onClick={onClose} disabled={saving}>Отмена</button>
          </div>
        </form>
        )}
      </section>
    </div>
  )
}
