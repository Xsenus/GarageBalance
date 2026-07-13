import { Fragment, useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent, MouseEvent, ReactNode } from 'react'
import { FileText, Pencil, Plus, RotateCcw, Save, Search, Trash2, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { AccountingTypeDto, DictionaryClient, GarageDto, StaffMemberDto, SupplierDto, SupplierGroupDto, TariffDto } from '../../services/dictionariesApi'
import type { AccrualDto, CreateAccrualRequest, CreateExpenseOperationRequest, CreateIncomeOperationRequest, CreateMeterReadingRequest, CreateSupplierAccrualRequest, ExpenseWorksheetDto, FinanceClient, FinancePagedResult, FinanceSummaryDto, FinancialOperationDto, GarageIncomeWorksheetDto, GenerateRegularAccrualsRequest, GenerateSupplierGroupSalaryAccrualsRequest, MeterReadingDto, MissingMeterReadingDto, SupplierAccrualDto } from '../../services/financeApi'
import type { FundDto, FundsClient } from '../../services/fundsApi'
import type { FormStateClient } from '../../services/formStatesApi'
import type { IntegrationClient, ReceiptPrintingActionKind } from '../../services/integrationsApi'
import type { ApplicationSettingsClient } from '../../services/settingsApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import type { FinanceEditorKey, FinanceSectionKey } from '../../shared/financeWorkbench'
import { financeSectionOptions, formatFinanceGarageLabel, formatFinanceIncomeGarageSearchStatus, formatFinanceOperationCount, formatFinanceVisibleListStatus, getFinanceContextMenuLabel, getFinanceEditorFieldLabel, getFinanceEditorSavingScope, getFinanceEditorSubmitLabel, getFinanceEditorTitle, getFinanceEditorUiLabel, getFinanceEditorValidationTitle, getFinanceFallbackLabel, getFinanceMeterKindLabel, getFinanceOptionalText, getFinancePanelLabel, getFinanceSectionDescription, getFinanceTableHeaders, getFinanceToolbarLabel, getFinanceVisibleListEmptyLabel, getFinanceVisibleListTableHeaders, getFinanceVisibleListTableLabel } from '../../shared/financeWorkbench'
import type { ChangePreview } from '../../shared/changePreview'
import { appendChangePreview, formatChangeDate, formatChangeMoney, formatChangeText } from '../../shared/changePreview'
import { FormError, FormValidationSummary } from '../../shared/formFeedback'
import { FormField } from '../../shared/FormField'
import { formatAccrualSource, formatDateOnly, formatDebtAmount, formatDebtLabel, formatMissingMeterReadings, formatMoney, formatMonth, formatOperationTime, formatPaymentAllocations, getDebtClassName, getCurrentMonthInputValue, getLocalDateInputValue, getPreviousMonthInputValue } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { TablePagination } from '../../shared/TablePagination'
import { chooseRegularTariffId, getAccrualValidationErrors, getCompatibleRegularTariffs, getExpenseValidationErrors, getIncomeValidationErrors, getMeterReadingValidationErrors, getRegularAccrualValidationErrorsForCatalog, getSupplierAccrualValidationErrors, getSupplierGroupSalaryValidationErrors } from '../../shared/validation'

type AccrualBreakdown =
  | { kind: 'garage'; accrual: AccrualDto }
  | { kind: 'supplier'; accrual: SupplierAccrualDto }

const financeScreenRequestLimit = 50
const dictionaryScreenRequestLimit = 100
const paymentsFormStateScope = 'payments-prototype'

type FinanceRecord = FinancialOperationDto | AccrualDto | SupplierAccrualDto | MeterReadingDto
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
  item: string
  counterparty?: string
  cost: number | string
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

type GarageIncomePrototypeRow = {
  id: string
  month: string
  monthLabel: string
  service: string
  meter: number | null
  difference: number | null
  payable: number
  paymentDraft: string
  paid: number
  debt: number
  meterRequired?: boolean
}

type GarageIncomeWorksheetPeriodSummary = {
  openingDebt: number
  accrualTotal: number
  incomeTotal: number
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

type GaragePaymentReceiptActionState = {
  row: GaragePaymentHistoryPrototypeRow
  action: ReceiptPrintingActionKind
  reason: string
  error: string | null
}

const receiptPrintingActionLabels: Record<ReceiptPrintingActionKind, { title: string; button: string; saving: string; description: string }> = {
  print: {
    title: 'Сформировать квитанцию?',
    button: 'Сформировать квитанцию',
    saving: 'Формируем...',
    description: 'Действие будет записано в общую историю. Фактическая отправка на печатающее устройство включится после подключения адаптера.',
  },
  cancel: {
    title: 'Отменить печать квитанции?',
    button: 'Отменить печать',
    saving: 'Отменяем...',
    description: 'Отмена печати сохранится в общей истории изменений. Укажите причину, чтобы бухгалтер мог сверить действие позже.',
  },
  reprint: {
    title: 'Напечатать копию квитанции?',
    button: 'Напечатать копию',
    saving: 'Регистрируем...',
    description: 'Повторная печать будет зафиксирована как копия квитанции с отдельной отметкой в истории изменений. Укажите причину, например потерю квитанции или исправление печати.',
  },
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

type DebtTransferPrototypePeriodOption = {
  value: string
  label: string
  debt: number
  defaultTargetMonth: string
}

type DebtTransferPrototypeSubmitRequest = {
  sourceMonth: string
  targetMonth: string
  amount: number
  comment: string
}

type GarageAccrualPrototypeSubmitRequest = {
  incomeTypeId: string
  accountingMonth: string
  amount: number
  comment: string
}

type ExpensePrototypeDialogPreset = {
  expenseTypeName?: string
  amount?: number
  rowIndex?: number
}

type StaffPaymentPrototypeDialogPreset = {
  staffMemberName?: string
  amount?: number
  rowIndex?: number
}

type ExpensePrototypeSubmitRequest = {
  supplierId: string
  expenseTypeId: string
  operationDate: string
  accountingMonth: string
  amount: number
  documentNumber: string
  comment: string
  rowIndex?: number
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

type SupplierAccrualPrototypeSubmitRequest = {
  supplierId: string
  expenseTypeId: string
  accountingMonth: string
  amount: number
  documentNumber: string
  comment: string
}

type SalaryAccrualPrototypeSubmitRequest = {
  supplierGroupId: string
  accountingMonth: string
  amount: number
  documentNumber: string
  comment: string
}

type RegularAccrualPrototypeSubmitRequest = {
  accountingMonth: string
  comment: string
}

type PaymentsPrototypeSavedState = {
  selectedGarageId: string | null
  selectedGarageIds?: string[]
  garageSearch: string
  incomeWorksheetMonthFrom?: string
  incomeWorksheetMonthTo?: string
  garageRows: GarageIncomePrototypeRow[]
  historyRows: GaragePaymentHistoryPrototypeRow[]
}

function createGarageIncomeRowsFromWorksheet(worksheet: GarageIncomeWorksheetDto): GarageIncomePrototypeRow[] {
  return worksheet.rows.map((row) => {
    const month = row.accountingMonth.slice(0, 7)
    const rowKey = row.incomeTypeId ?? row.incomeTypeName.toLocaleLowerCase('ru-RU').replace(/\s+/g, '-')
    return {
      id: `garage-${worksheet.garageId}-${month}-${rowKey}`,
      month,
      monthLabel: formatPaymentPrototypeMonthLabel(row.accountingMonth),
      service: row.incomeTypeName,
      meter: row.meterValue,
      difference: row.meterConsumption,
      payable: row.accrualAmount,
      paymentDraft: '',
      paid: row.incomeAmount,
      debt: row.debt,
      meterRequired: row.meterKind !== null && row.meterValue === null,
    }
  })
}

function createExpenseRowsFromWorksheet(worksheet: ExpenseWorksheetDto): PaymentPrototypeRow[] {
  return worksheet.rows.map((row) => ({
    rowKind: row.rowKind,
    supplierId: row.supplierId,
    staffMemberId: row.staffMemberId,
    counterparty: row.counterpartyName ?? '',
    item: row.expenseTypeName,
    cost: row.accrualAmount,
    paid: row.expenseAmount,
    balance: row.balance,
    collected: row.collectedAmount ?? '',
    difference: row.difference ?? '',
    action: true,
  }))
}

export function FinancePanel({
  auth,
  dictionaryClient,
  financeClient,
  fundsClient,
  formStateClient,
  integrationClient,
  settingsClient,
}: {
  auth: AuthResponse
  dictionaryClient: DictionaryClient
  financeClient: FinanceClient
  fundsClient: FundsClient
  formStateClient: FormStateClient
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
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [tariffs, setTariffs] = useState<TariffDto[]>([])
  const [operations, setOperations] = useState<FinancialOperationDto[]>([])
  const [accruals, setAccruals] = useState<AccrualDto[]>([])
  const [supplierAccruals, setSupplierAccruals] = useState<SupplierAccrualDto[]>([])
  const [meterReadings, setMeterReadings] = useState<MeterReadingDto[]>([])
  const [missingMeterReadings, setMissingMeterReadings] = useState<MissingMeterReadingDto[]>([])
  const [summary, setSummary] = useState<FinanceSummaryDto>({ incomeTotal: 0, expenseTotal: 0, accrualTotal: 0, balance: 0, debt: 0, operationCount: 0, accrualCount: 0, meterReadingCount: 0 })
  const [incomeForm, setIncomeForm] = useState({ garageId: '', incomeTypeId: '', operationDate: today, accountingMonth: month, amount: 0, documentNumber: '', comment: '' })
  const [expenseForm, setExpenseForm] = useState({ supplierId: '', expenseTypeId: '', operationDate: today, accountingMonth: month, amount: 0, documentNumber: '', comment: '' })
  const [accrualForm, setAccrualForm] = useState({ garageId: '', incomeTypeId: '', accountingMonth: month, amount: 0, source: 'manual' as 'manual' | 'regular', comment: '' })
  const [supplierAccrualForm, setSupplierAccrualForm] = useState({ supplierId: '', expenseTypeId: '', accountingMonth: month, amount: 0, source: 'manual' as 'manual' | 'regular', documentNumber: '', comment: '' })
  const [regularForm, setRegularForm] = useState({ incomeTypeId: '', tariffId: '', accountingMonth: month, comment: '' })
  const [regularStatus, setRegularStatus] = useState<string | null>(null)
  const [salaryForm, setSalaryForm] = useState({ supplierGroupId: '', accountingMonth: month, amount: 0, documentNumber: '', comment: '' })
  const [salaryStatus, setSalaryStatus] = useState<string | null>(null)
  const [meterForm, setMeterForm] = useState({ garageId: '', meterKind: 'water' as 'water' | 'electricity', accountingMonth: month, readingDate: today, currentValue: 0, comment: '' })
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
  const [paymentsPrototypeDialog, setPaymentsPrototypeDialog] = useState<PaymentsPrototypeDialogKey | null>(null)
  const paymentsPrototypeTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [financeEditorCloseConfirmation, setFinanceEditorCloseConfirmation] = useState(false)
  const [cancelFinanceTarget, setCancelFinanceTarget] = useState<CancelFinanceTarget | null>(null)
  const [restoreFinanceTarget, setRestoreFinanceTarget] = useState<RestoreFinanceTarget | null>(null)
  const [cancelFinanceReasonError, setCancelFinanceReasonError] = useState<string | null>(null)
  const [incomeValidationErrors, setIncomeValidationErrors] = useState<string[]>([])
  const [expenseValidationErrors, setExpenseValidationErrors] = useState<string[]>([])
  const [accrualValidationErrors, setAccrualValidationErrors] = useState<string[]>([])
  const [supplierAccrualValidationErrors, setSupplierAccrualValidationErrors] = useState<string[]>([])
  const [regularValidationErrors, setRegularValidationErrors] = useState<string[]>([])
  const [salaryValidationErrors, setSalaryValidationErrors] = useState<string[]>([])
  const [meterValidationErrors, setMeterValidationErrors] = useState<string[]>([])
  const [accrualBreakdown, setAccrualBreakdown] = useState<AccrualBreakdown | null>(null)
  const [loading, setLoading] = useState(true)
  const [paymentDisplaySettingsLoaded, setPaymentDisplaySettingsLoaded] = useState(false)
  const [showAllGarageOperations, setShowAllGarageOperations] = useState(false)
  const [paymentDisplaySettingsError, setPaymentDisplaySettingsError] = useState<string | null>(null)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
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
  }, [])
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
    if (section === 'regularAccruals') {
      return JSON.stringify(regularForm)
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
  const compatibleRegularTariffs = getCompatibleRegularTariffs(regularForm.incomeTypeId, incomeTypes, tariffs)

  useEffect(() => {
    let ignore = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const [loadedGarages, loadedSupplierGroups, loadedSuppliers, loadedStaffMembers, loadedIncomeTypes, loadedExpenseTypes, loadedTariffs] = await Promise.all([
          dictionaryClient.getGarages(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getSupplierGroups(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getStaffMembers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getIncomeTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getExpenseTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getTariffs(auth.accessToken, undefined, dictionaryScreenRequestLimit),
        ])
        if (!ignore) {
          setGarages(loadedGarages)
          setIncomeGarageOptions(loadedGarages)
          setSupplierGroups(loadedSupplierGroups)
          setSuppliers(loadedSuppliers)
          setStaffMembers(loadedStaffMembers)
          setIncomeTypes(loadedIncomeTypes)
          setExpenseTypes(loadedExpenseTypes)
          setTariffs(loadedTariffs)
          setIncomeForm((value) => ({ ...value, garageId: value.garageId || loadedGarages[0]?.id || '', incomeTypeId: value.incomeTypeId || loadedIncomeTypes[0]?.id || '' }))
          setExpenseForm((value) => ({ ...value, supplierId: value.supplierId || loadedSuppliers[0]?.id || '', expenseTypeId: value.expenseTypeId || loadedExpenseTypes[0]?.id || '' }))
          setAccrualForm((value) => ({ ...value, garageId: value.garageId || loadedGarages[0]?.id || '', incomeTypeId: value.incomeTypeId || loadedIncomeTypes[0]?.id || '' }))
          setSupplierAccrualForm((value) => ({ ...value, supplierId: value.supplierId || loadedSuppliers[0]?.id || '', expenseTypeId: value.expenseTypeId || loadedExpenseTypes[0]?.id || '' }))
          setSalaryForm((value) => ({ ...value, supplierGroupId: value.supplierGroupId || loadedSupplierGroups[0]?.id || '' }))
          setRegularForm((value) => {
            const incomeTypeId = value.incomeTypeId || loadedIncomeTypes[0]?.id || ''
            return {
              ...value,
              incomeTypeId,
              tariffId: chooseRegularTariffId(incomeTypeId, value.tariffId, loadedIncomeTypes, loadedTariffs),
            }
          })
          setMeterForm((value) => ({ ...value, garageId: value.garageId || loadedGarages[0]?.id || '' }))
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить платежи.')
        }
      } finally {
        if (!ignore) {
          setLoading(false)
        }
      }
    }

    void load()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, dictionaryClient, financeClient, month])

  useEffect(() => {
    let ignore = false
    settingsClient.getPaymentDisplaySettings(auth.accessToken)
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
    }
  }, [auth.accessToken, settingsClient])

  useEffect(() => {
    if (!paymentDisplaySettingsLoaded || !showAllGarageOperations) {
      return
    }

    let ignore = false
    const handle = window.setTimeout(() => {
      void Promise.all([
        financeClient.getOperations(auth.accessToken, financeScreenRequestLimit),
        financeClient.getAccruals(auth.accessToken, financeScreenRequestLimit),
        financeClient.getSupplierAccruals(auth.accessToken, financeScreenRequestLimit),
        financeClient.getMeterReadings(auth.accessToken, financeScreenRequestLimit),
      ]).then(([loadedOperations, loadedAccruals, loadedSupplierAccruals, loadedMeterReadings]) => {
        if (!ignore) {
          setOperations(loadedOperations)
          setAccruals(loadedAccruals)
          setSupplierAccruals(loadedSupplierAccruals)
          setMeterReadings(loadedMeterReadings)
        }
      }).catch(() => {
        // Фоновые превью не должны задерживать или ломать основную форму платежей.
      })
    }, 500)

    return () => {
      ignore = true
      window.clearTimeout(handle)
    }
  }, [auth.accessToken, financeClient, paymentDisplaySettingsLoaded, showAllGarageOperations])

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

  const loadFinanceWorkbench = useCallback(async (section: FinanceSectionKey, offset: number, limit: number) => {
    setFinanceContextMenu(null)
    setLoading(true)
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
      const [incomePage, expensePage, accrualsPage, supplierAccrualsPage, meterReadingsPage, loadedMissingMeterReadings, loadedSummary] = await Promise.all([
        financeClient.getOperationsPage(auth.accessToken, { ...params, operationKind: 'income', limit: section === 'income' ? limit : 1, offset: section === 'income' ? offset : 0 }),
        financeClient.getOperationsPage(auth.accessToken, { ...params, operationKind: 'expense', limit: section === 'expense' ? limit : 1, offset: section === 'expense' ? offset : 0 }),
        financeClient.getAccrualsPage(auth.accessToken, { ...params, limit: section === 'accruals' ? limit : 1, offset: section === 'accruals' ? offset : 0 }),
        financeClient.getSupplierAccrualsPage(auth.accessToken, { ...params, limit: section === 'supplierAccruals' ? limit : 1, offset: section === 'supplierAccruals' ? offset : 0 }),
        financeClient.getMeterReadingsPage(auth.accessToken, { ...params, limit: section === 'meterReadings' ? limit : 1, offset: section === 'meterReadings' ? offset : 0 }),
        financeClient.getMissingMeterReadings(auth.accessToken, { accountingMonth: missingMeterMonth, search: financeFilter.search, limit: financeScreenRequestLimit }),
        financeClient.getSummary(auth.accessToken, { monthFrom: financeFilter.monthFrom, monthTo: financeFilter.monthTo, search: financeFilter.search }),
      ])

      setFinanceSectionCounts({
        income: incomePage.totalCount,
        expense: expensePage.totalCount,
        accruals: accrualsPage.totalCount,
        supplierAccruals: supplierAccrualsPage.totalCount,
        meterReadings: meterReadingsPage.totalCount,
      })
      setSummary(loadedSummary)
      if (section === 'income') {
        setOperations(incomePage.items)
        setFinancePage(incomePage as FinancePagedResult<FinanceRecord>)
      } else if (section === 'expense') {
        setOperations(expensePage.items)
        setFinancePage(expensePage as FinancePagedResult<FinanceRecord>)
      } else if (section === 'accruals') {
        setAccruals(accrualsPage.items)
        setFinancePage(accrualsPage as FinancePagedResult<FinanceRecord>)
      } else if (section === 'supplierAccruals') {
        setSupplierAccruals(supplierAccrualsPage.items)
        setFinancePage(supplierAccrualsPage as FinancePagedResult<FinanceRecord>)
      } else {
        setMeterReadings(meterReadingsPage.items)
        setFinancePage(meterReadingsPage as FinancePagedResult<FinanceRecord>)
      }
      setMissingMeterReadings(loadedMissingMeterReadings)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось загрузить страницу платежей.')
    } finally {
      setLoading(false)
    }
  }, [auth.accessToken, financeClient, financeFilter.monthFrom, financeFilter.monthTo, financeFilter.search, meterForm.accountingMonth])

  useEffect(() => {
    if (!paymentDisplaySettingsLoaded || !showAllGarageOperations) {
      return
    }

    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadFinanceWorkbench(activeFinanceSection, 0, financePage.limit)
  }, [activeFinanceSection, financePage.limit, loadFinanceWorkbench, paymentDisplaySettingsLoaded, showAllGarageOperations])

  async function searchIncomeGarages() {
    const query = incomeGarageSearch.trim()
    await runSaving('income-garage-search', async () => {
      const foundGarages = await dictionaryClient.getGarages(auth.accessToken, query || undefined, dictionaryScreenRequestLimit)
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
    appendChangePreview(changes, 'Вид выплаты', formatExpenseType(record.expenseTypeId, record.expenseTypeName), formatExpenseType(request.expenseTypeId, request.expenseTypeId === record.expenseTypeId ? record.expenseTypeName : null))
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
        await loadFinanceWorkbench('income', financePage.offset, financePage.limit)
        setIncomeForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
      } else if (pending.kind === 'expense') {
        await financeClient.updateExpense(auth.accessToken, pending.recordId, pending.request as CreateExpenseOperationRequest)
        await loadFinanceWorkbench('expense', financePage.offset, financePage.limit)
        setExpenseForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
      } else if (pending.kind === 'accrual') {
        await financeClient.updateAccrual(auth.accessToken, pending.recordId, pending.request as CreateAccrualRequest)
        await loadFinanceWorkbench('accruals', financePage.offset, financePage.limit)
        setAccrualForm((value) => ({ ...value, amount: 0, comment: '' }))
      } else {
        await financeClient.updateSupplierAccrual(auth.accessToken, pending.recordId, pending.request as CreateSupplierAccrualRequest)
        await loadFinanceWorkbench('supplierAccruals', financePage.offset, financePage.limit)
        setSupplierAccrualForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
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
      await loadFinanceWorkbench('income', financePage.offset, financePage.limit)
      setIncomeForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
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
        objectName: `${financeEditor.record.expenseTypeName ?? 'Выплата'} · ${financeEditor.record.supplierName ?? 'Поставщик'} · ${formatChangeMoney(financeEditor.record.amount)}`,
        request,
        changes,
      })
      return
    }

    const saved = await runSaving('expense', async () => {
      await financeClient.createExpense(auth.accessToken, request)
      await loadFinanceWorkbench('expense', financePage.offset, financePage.limit)
      setExpenseForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
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
      await loadFinanceWorkbench('accruals', financePage.offset, financePage.limit)
      setAccrualForm((value) => ({ ...value, amount: 0, comment: '' }))
    })
    if (saved) {
      closeFinanceEditor({ skipConfirmation: true })
    }
  }

  async function saveRegularAccruals(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWritePayments) {
      setError('Для записи платежей нужно право payments.write.')
      return
    }

    const request: GenerateRegularAccrualsRequest = {
      incomeTypeId: regularForm.incomeTypeId,
      tariffId: regularForm.tariffId,
      accountingMonth: regularForm.accountingMonth,
      comment: regularForm.comment,
    }
    const errors = getRegularAccrualValidationErrorsForCatalog(request, incomeTypes, tariffs)
    if (errors.length > 0) {
      setError(null)
      setRegularValidationErrors(errors)
      return
    }

    setRegularValidationErrors([])
    const saved = await runSaving('regular-accruals', async () => {
      const result = await financeClient.generateRegularAccruals(auth.accessToken, request)
      setAccruals((items) => [...result.createdAccruals, ...items])
      setSummary((value) => ({
        ...value,
        accrualTotal: value.accrualTotal + result.totalAmount,
        debt: value.debt + result.totalAmount,
        accrualCount: value.accrualCount + result.createdCount,
      }))
      setRegularStatus(`Создано ${result.createdCount}, пропущено ${result.skippedCount}`)
      setRegularForm((value) => ({ ...value, comment: '' }))
    })
    if (saved) {
      closeFinanceEditor({ skipConfirmation: true })
      setActiveFinanceSection('accruals')
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
      await loadFinanceWorkbench('supplierAccruals', financePage.offset, financePage.limit)
      setSupplierAccrualForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
    })
    if (saved) {
      closeFinanceEditor({ skipConfirmation: true })
    }
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
      await loadFinanceWorkbench('supplierAccruals', 0, financePage.limit)
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
      await loadFinanceWorkbench('meterReadings', financePage.offset, financePage.limit)
      setMeterForm((value) => ({ ...value, currentValue: 0, comment: '' }))
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
      const counterparty = record.operationKind === 'income' ? formatFinanceGarageLabel(record.garageNumber) : record.supplierName
      return `${name ?? 'Операция'} · ${counterparty ?? 'контрагент не указан'} · ${formatMoney(record.amount)}`
    }
    if ('meterKind' in record) {
      return `${getFinanceMeterKindLabel(record.meterKind)} · ${formatFinanceGarageLabel(record.garageNumber)} · ${formatMonth(record.accountingMonth)}`
    }
    if ('supplierName' in record) {
      return `${record.expenseTypeName} · ${record.supplierName} · ${formatMoney(record.amount)}`
    }
    return `${record.incomeTypeName} · ${formatFinanceGarageLabel(record.garageNumber)} · ${formatMoney(record.amount)}`
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
        await loadFinanceWorkbench(operation.operationKind === 'income' ? 'income' : 'expense', financePage.offset, financePage.limit)
      } else if (target.section === 'accruals') {
        await financeClient.restoreAccrual(auth.accessToken, target.record.id)
        await loadFinanceWorkbench('accruals', financePage.offset, financePage.limit)
      } else if (target.section === 'supplierAccruals') {
        await financeClient.restoreSupplierAccrual(auth.accessToken, target.record.id)
        await loadFinanceWorkbench('supplierAccruals', financePage.offset, financePage.limit)
      } else {
        await financeClient.restoreMeterReading(auth.accessToken, target.record.id)
        await loadFinanceWorkbench('meterReadings', financePage.offset, financePage.limit)
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
        await loadFinanceWorkbench(operation.operationKind === 'income' ? 'income' : 'expense', financePage.offset, financePage.limit)
      } else if (target.section === 'accruals') {
        await financeClient.cancelAccrual(auth.accessToken, target.record.id, { reason })
        await loadFinanceWorkbench('accruals', financePage.offset, financePage.limit)
      } else if (target.section === 'supplierAccruals') {
        await financeClient.cancelSupplierAccrual(auth.accessToken, target.record.id, { reason })
        await loadFinanceWorkbench('supplierAccruals', financePage.offset, financePage.limit)
      } else {
        await financeClient.cancelMeterReading(auth.accessToken, target.record.id, { reason })
        await loadFinanceWorkbench('meterReadings', financePage.offset, financePage.limit)
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

  function openFinanceEditor(section: FinanceEditorKey, record?: FinanceRecord, trigger?: HTMLElement | null) {
    if (!canWritePayments) {
      setFinanceContextMenu(null)
      setError('Для записи платежей нужно право payments.write.')
      return
    }

    financeEditorTriggerRef.current = trigger ?? null
    setError(null)
    setRegularStatus(null)
    setSalaryStatus(null)
    setIncomeValidationErrors([])
    setExpenseValidationErrors([])
    setAccrualValidationErrors([])
    setSupplierAccrualValidationErrors([])
    setRegularValidationErrors([])
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
              number: record.garageNumber ?? 'без номера',
              ownerId: null,
              ownerName: record.ownerName,
              peopleCount: 0,
              floorCount: 0,
              startingBalance: 0,
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
      const nextForm = { ...incomeForm, amount: 0, documentNumber: '', comment: '' }
      setIncomeForm(nextForm)
      initialSnapshot = JSON.stringify(nextForm)
    } else if (record && section === 'expense' && 'operationKind' in record) {
      const nextForm = {
        supplierId: record.supplierId ?? '',
        expenseTypeId: record.expenseTypeId ?? '',
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
      const nextForm = { ...accrualForm, source: 'manual' as const, amount: 0, comment: '' }
      setAccrualForm(nextForm)
      initialSnapshot = JSON.stringify(nextForm)
    } else if (!record && section === 'regularAccruals') {
      initialSnapshot = JSON.stringify(regularForm)
    } else if (record && section === 'supplierAccruals' && 'supplierId' in record && !('operationKind' in record)) {
      const nextForm = {
        supplierId: record.supplierId,
        expenseTypeId: record.expenseTypeId,
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
      initialSnapshot = JSON.stringify(meterForm)
    }
    setFinanceEditorInitialSnapshot(initialSnapshot || getFinanceEditorFormSnapshot(section))
    setFinanceEditor({ section, mode: record ? 'edit' : 'create', record })
  }

  function openFinanceContextMenu(event: MouseEvent<HTMLElement>, section: FinanceSectionKey, record?: FinanceRecord) {
    event.preventDefault()
    event.stopPropagation()
    financeContextMenuTriggerRef.current = record ? event.currentTarget : null
    setFinanceContextMenu({ section, record, x: event.clientX, y: event.clientY })
  }

  function selectFinanceSection(section: FinanceSectionKey) {
    setFinanceContextMenu(null)
    setActiveFinanceSection(section)
  }

  function editFinanceRecord(section: FinanceSectionKey, record: FinanceRecord, trigger?: HTMLElement | null) {
    setFinanceContextMenu(null)
    financeContextMenuTriggerRef.current = null
    openFinanceEditor(section, record, trigger)
  }

  function addFinanceRecord(section: FinanceSectionKey) {
    setFinanceContextMenu(null)
    openFinanceEditor(section)
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
    if (event.target !== event.currentTarget || (event.key !== 'ContextMenu' && !(event.shiftKey && event.key === 'F10'))) {
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

  const filteredIncomeOperations = operations.filter((operation) => operation.operationKind === 'income')
  const filteredExpenseOperations = operations.filter((operation) => operation.operationKind === 'expense')
  const filteredAccruals = accruals
  const filteredSupplierAccruals = supplierAccruals
  const filteredMeterReadings = meterReadings

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
              <tr className="finance-table-row--interactive" key={operation.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'income', operation)} onClick={(event) => editFinanceRecord('income', operation, event.currentTarget)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'income', operation)}>
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
              <tr className="finance-table-row--interactive" key={operation.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'expense', operation)} onClick={(event) => editFinanceRecord('expense', operation, event.currentTarget)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'expense', operation)}>
                <td>{formatDateOnly(operation.operationDate)}</td>
                <td>{formatMonth(operation.accountingMonth)}</td>
                <td>{getFinanceOptionalText(operation.supplierName)}</td>
                <td>{operation.expenseTypeName}</td>
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
              <tr className="finance-table-row--interactive" key={accrual.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'accruals', accrual)} onClick={(event) => editFinanceRecord('accruals', accrual, event.currentTarget)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'accruals', accrual)}>
                <td>{formatMonth(accrual.accountingMonth)}</td>
                <td>{formatFinanceGarageLabel(accrual.garageNumber)}</td>
                <td>{getFinanceOptionalText(accrual.ownerName)}</td>
                <td>{accrual.incomeTypeName}</td>
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
              <tr className="finance-table-row--interactive" key={accrual.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'supplierAccruals', accrual)} onClick={(event) => editFinanceRecord('supplierAccruals', accrual, event.currentTarget)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'supplierAccruals', accrual)}>
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
              <tr className="finance-table-row--interactive" key={reading.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'meterReadings', reading)} onClick={(event) => editFinanceRecord('meterReadings', reading, event.currentTarget)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'meterReadings', reading)}>
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
    if (financeEditor.section === 'regularAccruals') {
      void saveRegularAccruals(event)
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
            <select aria-label="Гараж для поступления" value={incomeForm.garageId} onChange={(event) => setIncomeForm({ ...incomeForm, garageId: event.target.value })} required>
              <option value="" disabled>
                Выберите гараж
              </option>
              {incomeGarageOptions.map((garage) => (
                <option value={garage.id} key={garage.id}>
                  {garage.ownerName ? `Гараж ${garage.number} - ${garage.ownerName}` : `Гараж ${garage.number}`}
                </option>
              ))}
            </select>
          ))}
          {financeField('incomeType', (
            <select aria-label="Вид поступления для платежа" value={incomeForm.incomeTypeId} onChange={(event) => setIncomeForm({ ...incomeForm, incomeTypeId: event.target.value })} required>
              <option value="" disabled>
                Выберите вид
              </option>
              {incomeTypes.map((item) => (
                <option value={item.id} key={item.id}>
                  {item.name}
                </option>
              ))}
            </select>
          ))}
          <div className="inline-fields">
            {financeField('incomeDate', <input aria-label="Дата поступления" type="date" value={incomeForm.operationDate} onChange={(event) => setIncomeForm({ ...incomeForm, operationDate: event.target.value })} required />)}
            {financeField('incomeMonth', <input aria-label="Месяц поступления" type="month" value={incomeForm.accountingMonth.slice(0, 7)} onChange={(event) => setIncomeForm({ ...incomeForm, accountingMonth: `${event.target.value}-01` })} required />)}
          </div>
          <div className="inline-fields">
            {financeField('incomeAmount', <input aria-label="Сумма поступления" type="number" min="0.01" step="0.01" value={incomeForm.amount} onChange={(event) => setIncomeForm({ ...incomeForm, amount: Number(event.target.value) })} required />)}
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
          {financeField('expenseSupplier', (
            <select aria-label="Поставщик для выплаты" value={expenseForm.supplierId} onChange={(event) => setExpenseForm({ ...expenseForm, supplierId: event.target.value })} required>
              <option value="" disabled>
                Выберите поставщика
              </option>
              {suppliers.map((supplier) => (
                <option value={supplier.id} key={supplier.id}>
                  {supplier.name}
                </option>
              ))}
            </select>
          ))}
          {financeField('expenseType', (
            <select aria-label="Вид выплаты" value={expenseForm.expenseTypeId} onChange={(event) => setExpenseForm({ ...expenseForm, expenseTypeId: event.target.value })} required>
              <option value="" disabled>
                Выберите вид
              </option>
              {expenseTypes.map((item) => (
                <option value={item.id} key={item.id}>
                  {item.name}
                </option>
              ))}
            </select>
          ))}
          <div className="inline-fields">
            {financeField('expenseDate', <input aria-label="Дата выплаты" type="date" value={expenseForm.operationDate} onChange={(event) => setExpenseForm({ ...expenseForm, operationDate: event.target.value })} required />)}
            {financeField('expenseMonth', <input aria-label="Месяц выплаты" type="month" value={expenseForm.accountingMonth.slice(0, 7)} onChange={(event) => setExpenseForm({ ...expenseForm, accountingMonth: `${event.target.value}-01` })} required />)}
          </div>
          <div className="inline-fields">
            {financeField('expenseAmount', <input aria-label="Сумма выплаты" type="number" min="0.01" step="0.01" value={expenseForm.amount} onChange={(event) => setExpenseForm({ ...expenseForm, amount: Number(event.target.value) })} required />)}
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
            <select aria-label="Гараж для начисления" value={accrualForm.garageId} onChange={(event) => setAccrualForm({ ...accrualForm, garageId: event.target.value })} required>
              <option value="" disabled>
                Выберите гараж
              </option>
              {garages.map((garage) => (
                <option value={garage.id} key={garage.id}>
                  Гараж {garage.number}
                </option>
              ))}
            </select>
          ))}
          {financeField('accrualIncomeType', (
            <select aria-label="Вид начисления" value={accrualForm.incomeTypeId} onChange={(event) => setAccrualForm({ ...accrualForm, incomeTypeId: event.target.value })} required>
              <option value="" disabled>
                Выберите вид
              </option>
              {incomeTypes.map((item) => (
                <option value={item.id} key={item.id}>
                  {item.name}
                </option>
              ))}
            </select>
          ))}
          <div className="inline-fields">
            {financeField('accrualMonth', <input aria-label="Месяц начисления" type="month" value={accrualForm.accountingMonth.slice(0, 7)} onChange={(event) => setAccrualForm({ ...accrualForm, accountingMonth: `${event.target.value}-01` })} required />)}
            {financeField('accrualAmount', <input aria-label="Сумма начисления" type="number" min="0.01" step="0.01" value={accrualForm.amount} onChange={(event) => setAccrualForm({ ...accrualForm, amount: Number(event.target.value) })} required />)}
          </div>
          {financeField('accrualSource', <input aria-label="Источник начисления" value={formatAccrualSource(accrualForm.source)} readOnly />)}
          {financeField('accrualComment', <input aria-label="Комментарий к начислению" placeholder="Комментарий" value={accrualForm.comment} onChange={(event) => setAccrualForm({ ...accrualForm, comment: event.target.value })} />)}
          <FormValidationSummary title={getFinanceEditorValidationTitle('accruals')} items={accrualValidationErrors} />
        </>
      )
    }

    if (section === 'regularAccruals') {
      return (
        <>
          {financeField('regularIncomeType', (
            <select
              aria-label="Вид регулярного начисления"
              value={regularForm.incomeTypeId}
              onChange={(event) => {
                const incomeTypeId = event.target.value
                setRegularForm({ ...regularForm, incomeTypeId, tariffId: chooseRegularTariffId(incomeTypeId, regularForm.tariffId, incomeTypes, tariffs) })
              }}
              required
            >
              <option value="" disabled>
                Выберите вид
              </option>
              {incomeTypes.map((item) => (
                <option value={item.id} key={item.id}>
                  {item.name}
                </option>
              ))}
            </select>
          ))}
          {financeField('regularTariff', (
            <select aria-label="Тариф для регулярного начисления" value={regularForm.tariffId} onChange={(event) => setRegularForm({ ...regularForm, tariffId: event.target.value })} required>
              <option value="" disabled>
                Выберите тариф
              </option>
              {compatibleRegularTariffs.map((tariff) => (
                <option value={tariff.id} key={tariff.id}>
                  {tariff.name}
                </option>
              ))}
            </select>
          ))}
          {financeField('regularMonth', <input aria-label="Месяц регулярного начисления" type="month" value={regularForm.accountingMonth.slice(0, 7)} onChange={(event) => setRegularForm({ ...regularForm, accountingMonth: `${event.target.value}-01` })} required />)}
          {financeField('regularComment', <input aria-label="Комментарий к регулярному начислению" placeholder="Комментарий" value={regularForm.comment} onChange={(event) => setRegularForm({ ...regularForm, comment: event.target.value })} />)}
          <FormValidationSummary title={getFinanceEditorValidationTitle('regularAccruals')} items={regularValidationErrors} />
          {regularStatus ? <p className="form-hint">{regularStatus}</p> : null}
        </>
      )
    }

    if (section === 'supplierAccruals') {
      return (
        <>
          {financeField('supplierAccrualSupplier', (
            <select aria-label="Поставщик для начисления" value={supplierAccrualForm.supplierId} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, supplierId: event.target.value })} required>
              <option value="" disabled>
                Выберите поставщика
              </option>
              {suppliers.map((supplier) => (
                <option value={supplier.id} key={supplier.id}>
                  {supplier.name}
                </option>
              ))}
            </select>
          ))}
          {financeField('supplierAccrualType', (
            <select aria-label="Вид начисления поставщику" value={supplierAccrualForm.expenseTypeId} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, expenseTypeId: event.target.value })} required>
              <option value="" disabled>
                Выберите вид
              </option>
              {expenseTypes.map((item) => (
                <option value={item.id} key={item.id}>
                  {item.name}
                </option>
              ))}
            </select>
          ))}
          <div className="inline-fields">
            {financeField('supplierAccrualMonth', <input aria-label="Месяц начисления поставщику" type="month" value={supplierAccrualForm.accountingMonth.slice(0, 7)} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, accountingMonth: `${event.target.value}-01` })} required />)}
            {financeField('supplierAccrualAmount', <input aria-label="Сумма начисления поставщику" type="number" min="0.01" step="0.01" value={supplierAccrualForm.amount} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, amount: Number(event.target.value) })} required />)}
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
            <select aria-label="Группа для зарплаты" value={salaryForm.supplierGroupId} onChange={(event) => setSalaryForm({ ...salaryForm, supplierGroupId: event.target.value })} required>
              <option value="" disabled>
                Выберите группу
              </option>
              {supplierGroups.map((group) => (
                <option value={group.id} key={group.id}>
                  {group.name}
                </option>
              ))}
            </select>
          ))}
          <div className="inline-fields">
            {financeField('salaryMonth', <input aria-label="Месяц зарплаты" type="month" value={salaryForm.accountingMonth.slice(0, 7)} onChange={(event) => setSalaryForm({ ...salaryForm, accountingMonth: `${event.target.value}-01` })} required />)}
            {financeField('salaryAmount', <input aria-label="Сумма зарплаты" type="number" min="0.01" step="0.01" value={salaryForm.amount} onChange={(event) => setSalaryForm({ ...salaryForm, amount: Number(event.target.value) })} required />)}
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
          <select aria-label="Гараж для показания" value={meterForm.garageId} onChange={(event) => setMeterForm({ ...meterForm, garageId: event.target.value })} required>
            <option value="" disabled>
              Выберите гараж
            </option>
            {garages.map((garage) => (
              <option value={garage.id} key={garage.id}>
                Гараж {garage.number}
              </option>
            ))}
          </select>
        ))}
        {financeField('meterKind', (
          <select aria-label="Тип счетчика" value={meterForm.meterKind} onChange={(event) => setMeterForm({ ...meterForm, meterKind: event.target.value as 'water' | 'electricity' })} required>
            <option value="water">Вода</option>
            <option value="electricity">Электричество</option>
          </select>
        ))}
        <div className="inline-fields">
          {financeField('meterMonth', <input aria-label="Месяц показания" type="month" value={meterForm.accountingMonth.slice(0, 7)} onChange={(event) => setMeterForm({ ...meterForm, accountingMonth: `${event.target.value}-01` })} required />)}
          {financeField('meterDate', <input aria-label="Дата показания" type="date" value={meterForm.readingDate} onChange={(event) => setMeterForm({ ...meterForm, readingDate: event.target.value })} required />)}
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

  return (
    <section className={showAllGarageOperations ? 'finance-panel finance-panel--show-overview' : 'finance-panel'} aria-label={getFinancePanelLabel('section')}>
      <div className="section-heading">
        <div>
          <p className="eyebrow">{getFinancePanelLabel('section')}</p>
          <h2>{getFinancePanelLabel('title')}</h2>
        </div>
        <span>{loading || !paymentDisplaySettingsLoaded ? getFinancePanelLabel('loading') : showAllGarageOperations ? formatFinanceOperationCount(summary.operationCount) : 'Поиск по гаражу'}</span>
      </div>

      {error ? <FormError>{error}</FormError> : null}
      {paymentDisplaySettingsError ? <FormError>{paymentDisplaySettingsError}</FormError> : null}
      {!canWritePayments ? <p className="form-hint">{getFinancePanelLabel('readOnlyHint')}</p> : null}

      <PaymentsPrototypePanel
        auth={auth}
        canWritePayments={canWritePayments}
        dictionaryClient={dictionaryClient}
        expenseTypes={expenseTypes}
        financeClient={financeClient}
        formStateClient={formStateClient}
        garages={garages}
        incomeTypes={incomeTypes}
        integrationClient={integrationClient}
        supplierGroups={supplierGroups}
        suppliers={suppliers}
        staffMembers={staffMembers}
        onOpenDialog={openPaymentsPrototypeDialog}
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
            <input aria-label={getFinanceToolbarLabel('periodFrom')} type="month" value={financeFilter.monthFrom} onChange={(event) => setFinanceFilter((value) => ({ ...value, monthFrom: event.target.value }))} />
            <input aria-label={getFinanceToolbarLabel('periodTo')} type="month" value={financeFilter.monthTo} onChange={(event) => setFinanceFilter((value) => ({ ...value, monthTo: event.target.value }))} />
          </div>
          <label className="dictionary-search">
            <Search size={16} aria-hidden="true" />
            <input aria-label={getFinanceToolbarLabel('search')} placeholder={getFinanceToolbarLabel('searchPlaceholder')} value={financeSearchInput} onChange={(event) => setFinanceSearchInput(event.target.value)} />
          </label>
          <div className="finance-toolbar-actions">
            {activeFinanceSection === 'accruals' ? (
              <button className="ghost-button" type="button" disabled={!canWritePayments} onClick={() => openFinanceEditor('regularAccruals')}>
                <span>{getFinanceToolbarLabel('regularAccruals')}</span>
              </button>
            ) : null}
            {activeFinanceSection === 'supplierAccruals' ? (
              <button className="ghost-button" type="button" disabled={!canWritePayments} onClick={() => openFinanceEditor('supplierGroupSalaryAccruals')}>
                <span>{getFinanceToolbarLabel('supplierGroupSalaryAccruals')}</span>
              </button>
            ) : null}
          </div>
        </div>

        <div className="dictionary-table-shell">
          <div
            className="dictionary-table-scroll"
            role="group"
            aria-label={getFinanceToolbarLabel('tableArea')}
            tabIndex={getActiveFinanceRowsCount() === 0 ? 0 : -1}
            onContextMenu={(event) => openFinanceContextMenu(event, activeFinanceSection)}
            onKeyDown={handleFinanceTableAreaKeyDown}
          >
            {renderFinanceTable()}
            {getActiveFinanceRowsCount() === 0 ? <p className="empty-state" role="status" aria-live="polite">{getFinanceToolbarLabel('emptyState')}</p> : null}
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
          <select aria-label="Гараж для поступления" value={incomeForm.garageId} onChange={(event) => setIncomeForm({ ...incomeForm, garageId: event.target.value })} required>
            <option value="" disabled>
              Выберите гараж
            </option>
            {incomeGarageOptions.map((garage) => (
              <option value={garage.id} key={garage.id}>
                {garage.ownerName ? `Гараж ${garage.number} - ${garage.ownerName}` : `Гараж ${garage.number}`}
              </option>
            ))}
          </select>
          <select aria-label="Вид поступления для платежа" value={incomeForm.incomeTypeId} onChange={(event) => setIncomeForm({ ...incomeForm, incomeTypeId: event.target.value })} required>
            <option value="" disabled>
              Выберите вид
            </option>
            {incomeTypes.map((item) => (
              <option value={item.id} key={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          <div className="inline-fields">
            <input aria-label="Дата поступления" type="date" value={incomeForm.operationDate} onChange={(event) => setIncomeForm({ ...incomeForm, operationDate: event.target.value })} required />
            <input aria-label="Месяц поступления" type="month" value={incomeForm.accountingMonth.slice(0, 7)} onChange={(event) => setIncomeForm({ ...incomeForm, accountingMonth: `${event.target.value}-01` })} required />
          </div>
          <div className="inline-fields">
            <input aria-label="Сумма поступления" type="number" min="0.01" step="0.01" value={incomeForm.amount} onChange={(event) => setIncomeForm({ ...incomeForm, amount: Number(event.target.value) })} required />
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
          <select aria-label="Поставщик для выплаты" value={expenseForm.supplierId} onChange={(event) => setExpenseForm({ ...expenseForm, supplierId: event.target.value })} required>
            <option value="" disabled>
              Выберите поставщика
            </option>
            {suppliers.map((supplier) => (
              <option value={supplier.id} key={supplier.id}>
                {supplier.name}
              </option>
            ))}
          </select>
          <select aria-label="Вид выплаты для платежа" value={expenseForm.expenseTypeId} onChange={(event) => setExpenseForm({ ...expenseForm, expenseTypeId: event.target.value })} required>
            <option value="" disabled>
              Выберите вид
            </option>
            {expenseTypes.map((item) => (
              <option value={item.id} key={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          <div className="inline-fields">
            <input aria-label="Дата выплаты" type="date" value={expenseForm.operationDate} onChange={(event) => setExpenseForm({ ...expenseForm, operationDate: event.target.value })} required />
            <input aria-label="Месяц выплаты" type="month" value={expenseForm.accountingMonth.slice(0, 7)} onChange={(event) => setExpenseForm({ ...expenseForm, accountingMonth: `${event.target.value}-01` })} required />
          </div>
          <div className="inline-fields">
            <input aria-label="Сумма выплаты" type="number" min="0.01" step="0.01" value={expenseForm.amount} onChange={(event) => setExpenseForm({ ...expenseForm, amount: Number(event.target.value) })} required />
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
          <select aria-label="Гараж для начисления" value={accrualForm.garageId} onChange={(event) => setAccrualForm({ ...accrualForm, garageId: event.target.value })} required>
            <option value="" disabled>
              Выберите гараж
            </option>
            {garages.map((garage) => (
              <option value={garage.id} key={garage.id}>
                Гараж {garage.number}
              </option>
            ))}
          </select>
          <select aria-label="Вид начисления" value={accrualForm.incomeTypeId} onChange={(event) => setAccrualForm({ ...accrualForm, incomeTypeId: event.target.value })} required>
            <option value="" disabled>
              Выберите вид
            </option>
            {incomeTypes.map((item) => (
              <option value={item.id} key={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          <div className="inline-fields">
            <input aria-label="Месяц начисления" type="month" value={accrualForm.accountingMonth.slice(0, 7)} onChange={(event) => setAccrualForm({ ...accrualForm, accountingMonth: `${event.target.value}-01` })} required />
            <input aria-label="Сумма начисления" type="number" min="0.01" step="0.01" value={accrualForm.amount} onChange={(event) => setAccrualForm({ ...accrualForm, amount: Number(event.target.value) })} required />
          </div>
          <input aria-label="Комментарий начисления" placeholder="Комментарий" value={accrualForm.comment} onChange={(event) => setAccrualForm({ ...accrualForm, comment: event.target.value })} required />
          <FormValidationSummary title={getFinanceEditorValidationTitle('accruals')} items={accrualValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'accrual' || !accrualForm.garageId || !accrualForm.incomeTypeId}>
            <span>Начислить</span>
          </button>
        </form>

        <form className="dictionary-form" onSubmit={saveSupplierAccrual}>
          <h3>Начисление поставщику</h3>
          <select aria-label="Поставщик для начисления" value={supplierAccrualForm.supplierId} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, supplierId: event.target.value })} required>
            <option value="" disabled>
              Выберите поставщика
            </option>
            {suppliers.map((supplier) => (
              <option value={supplier.id} key={supplier.id}>
                {supplier.name}
              </option>
            ))}
          </select>
          <select aria-label="Вид начисления поставщику" value={supplierAccrualForm.expenseTypeId} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, expenseTypeId: event.target.value })} required>
            <option value="" disabled>
              Выберите вид
            </option>
            {expenseTypes.map((item) => (
              <option value={item.id} key={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          <div className="inline-fields">
            <input aria-label="Месяц начисления поставщику" type="month" value={supplierAccrualForm.accountingMonth.slice(0, 7)} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, accountingMonth: `${event.target.value}-01` })} required />
            <input aria-label="Сумма начисления поставщику" type="number" min="0.01" step="0.01" value={supplierAccrualForm.amount} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, amount: Number(event.target.value) })} required />
          </div>
          <div className="inline-fields">
            <input aria-label="Документ начисления поставщику" placeholder="Документ" value={supplierAccrualForm.documentNumber} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, documentNumber: event.target.value })} />
            <input aria-label="Комментарий начисления поставщику" placeholder="Комментарий" value={supplierAccrualForm.comment} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, comment: event.target.value })} required />
          </div>
          <FormValidationSummary title={getFinanceEditorValidationTitle('supplierAccruals')} items={supplierAccrualValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'supplier-accrual' || !supplierAccrualForm.supplierId || !supplierAccrualForm.expenseTypeId}>
            <span>Начислить</span>
          </button>
        </form>

        <form className="dictionary-form" onSubmit={saveRegularAccruals}>
          <h3>Регулярные начисления</h3>
          <select
            aria-label="Вид регулярного начисления"
            value={regularForm.incomeTypeId}
            onChange={(event) => {
              const incomeTypeId = event.target.value
              setRegularForm({ ...regularForm, incomeTypeId, tariffId: chooseRegularTariffId(incomeTypeId, regularForm.tariffId, incomeTypes, tariffs) })
            }}
            required
          >
            <option value="" disabled>
              Выберите вид
            </option>
            {incomeTypes.map((item) => (
              <option value={item.id} key={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          <select aria-label="Тариф регулярного начисления" value={regularForm.tariffId} onChange={(event) => setRegularForm({ ...regularForm, tariffId: event.target.value })} required>
            <option value="" disabled>
              Выберите тариф
            </option>
            {compatibleRegularTariffs.map((tariff) => (
              <option value={tariff.id} key={tariff.id}>
                {tariff.name} · {formatMoney(tariff.rate)}
              </option>
            ))}
          </select>
          <input aria-label="Месяц регулярных начислений" type="month" value={regularForm.accountingMonth.slice(0, 7)} onChange={(event) => setRegularForm({ ...regularForm, accountingMonth: `${event.target.value}-01` })} required />
          <input aria-label="Комментарий регулярных начислений" placeholder="Комментарий" value={regularForm.comment} onChange={(event) => setRegularForm({ ...regularForm, comment: event.target.value })} />
          <FormValidationSummary title={getFinanceEditorValidationTitle('regularAccruals', 'batch')} items={regularValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'regular-accruals' || !regularForm.incomeTypeId || !regularForm.tariffId}>
            <span>{getFinanceEditorSubmitLabel('regularAccruals')}</span>
          </button>
          {regularStatus ? <p className="empty-state" role="status" aria-live="polite">{regularStatus}</p> : null}
        </form>

        <form className="dictionary-form" onSubmit={saveMeterReading}>
          <h3>Показание счетчика</h3>
          <select aria-label="Гараж для счетчика" value={meterForm.garageId} onChange={(event) => setMeterForm({ ...meterForm, garageId: event.target.value })} required>
            <option value="" disabled>
              Выберите гараж
            </option>
            {garages.map((garage) => (
              <option value={garage.id} key={garage.id}>
                Гараж {garage.number}
              </option>
            ))}
          </select>
          <select aria-label="Тип счетчика" value={meterForm.meterKind} onChange={(event) => setMeterForm({ ...meterForm, meterKind: event.target.value as 'water' | 'electricity' })} required>
            <option value="water">Вода</option>
            <option value="electricity">Электричество</option>
          </select>
          <div className="inline-fields">
            <input aria-label="Месяц показания" type="month" value={meterForm.accountingMonth.slice(0, 7)} onChange={(event) => setMeterForm({ ...meterForm, accountingMonth: `${event.target.value}-01` })} required />
            <input aria-label="Дата показания" type="date" value={meterForm.readingDate} onChange={(event) => setMeterForm({ ...meterForm, readingDate: event.target.value })} required />
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

        <div className="operation-list" role="table" aria-label={getFinanceVisibleListTableLabel('operations')}>
          <div className="operation-row header" role="row">
            {getFinanceVisibleListTableHeaders('operations').map((header) => <span role="columnheader" key={header}>{header}</span>)}
          </div>
          {operations.length === 0 ? <p className="empty-state" role="status" aria-live="polite">{getFinanceVisibleListEmptyLabel('operations')}</p> : null}
          {visibleOperations.map((operation) => (
            <div className="operation-row" role="row" key={operation.id}>
              <span role="cell">{formatDateOnly(operation.operationDate)}</span>
              <span role="cell">
                <strong>{operation.operationKind === 'income' ? operation.incomeTypeName : operation.expenseTypeName}</strong>
                <small>{operation.operationKind === 'income' ? `Гараж ${operation.garageNumber}` : operation.supplierName}</small>
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
          ))}
          {operations.length > visibleOperations.length ? <p className="empty-state" role="status" aria-live="polite">{formatFinanceVisibleListStatus(visibleOperations.length, operations.length, 'operations')}</p> : null}
        </div>

        <div className="operation-list" role="table" aria-label={getFinanceVisibleListTableLabel('accruals')}>
          <div className="operation-row header" role="row">
            {getFinanceVisibleListTableHeaders('accruals').map((header) => <span role="columnheader" key={header}>{header}</span>)}
          </div>
          {accruals.length === 0 ? <p className="empty-state" role="status" aria-live="polite">{getFinanceVisibleListEmptyLabel('accruals')}</p> : null}
          {visibleAccruals.map((accrual) => (
            <div
              className="operation-row operation-row--interactive"
              role="row"
              tabIndex={0}
              aria-label={`Разбивка начисления ${accrual.incomeTypeName} гараж ${accrual.garageNumber}`}
              key={accrual.id}
              onDoubleClick={() => openAccrualBreakdown({ kind: 'garage', accrual })}
              onKeyDown={(event) => handleAccrualBreakdownKeyDown(event, { kind: 'garage', accrual })}
            >
              <span role="cell">{formatMonth(accrual.accountingMonth)}</span>
              <span role="cell">
                <strong>{accrual.incomeTypeName}</strong>
                <small>Гараж {accrual.garageNumber}</small>
                <small>{formatAccrualSource(accrual.source)}</small>
              </span>
              <span role="cell" className="operation-amount money-accrual">
                {formatMoney(accrual.amount)}
              </span>
            </div>
          ))}
          {accruals.length > visibleAccruals.length ? <p className="empty-state" role="status" aria-live="polite">{formatFinanceVisibleListStatus(visibleAccruals.length, accruals.length, 'accruals')}</p> : null}
        </div>

        <div className="operation-list" role="table" aria-label={getFinanceVisibleListTableLabel('supplierAccruals')}>
          <div className="operation-row header" role="row">
            {getFinanceVisibleListTableHeaders('supplierAccruals').map((header) => <span role="columnheader" key={header}>{header}</span>)}
          </div>
          {supplierAccruals.length === 0 ? <p className="empty-state" role="status" aria-live="polite">{getFinanceVisibleListEmptyLabel('supplierAccruals')}</p> : null}
          {visibleSupplierAccruals.map((accrual) => (
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
          ))}
          {supplierAccruals.length > visibleSupplierAccruals.length ? <p className="empty-state" role="status" aria-live="polite">{formatFinanceVisibleListStatus(visibleSupplierAccruals.length, supplierAccruals.length, 'supplierAccruals')}</p> : null}
        </div>

        <div className="operation-list" role="table" aria-label={getFinanceVisibleListTableLabel('meterReadings')}>
          <div className="operation-row header" role="row">
            {getFinanceVisibleListTableHeaders('meterReadings').map((header) => <span role="columnheader" key={header}>{header}</span>)}
          </div>
          {meterReadings.length === 0 ? <p className="empty-state" role="status" aria-live="polite">{getFinanceVisibleListEmptyLabel('meterReadings')}</p> : null}
          {visibleMeterReadings.map((reading) => (
            <div className="operation-row" role="row" key={reading.id}>
              <span role="cell">{formatMonth(reading.accountingMonth)}</span>
              <span role="cell">
                <strong>{reading.meterKind === 'water' ? 'Вода' : 'Электричество'}</strong>
                <small>
                  Гараж {reading.garageNumber}: {reading.previousValue} → {reading.currentValue}
                </small>
                {reading.hasGapWarning ? <small className="warning-text">проверьте предыдущий месяц</small> : null}
              </span>
              <span role="cell" className="operation-amount money-accrual">
                {reading.consumption}
              </span>
            </div>
          ))}
          {meterReadings.length > visibleMeterReadings.length ? <p className="empty-state" role="status" aria-live="polite">{formatFinanceVisibleListStatus(visibleMeterReadings.length, meterReadings.length, 'meterReadings')}</p> : null}
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
                  <dt>Вид выплаты</dt>
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
      {paymentsPrototypeDialog === 'bank' ? <BankDepositPrototypeDialog auth={auth} fundsClient={fundsClient} onClose={closePaymentsPrototypeDialog} /> : null}
    </section>
  )
}

function formatPaymentPrototypeValue(value: number | string) {
  return typeof value === 'number' ? value.toLocaleString('ru-RU') : value
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

function addPaymentPrototypeMonths(value: string, offset: number) {
  const match = /^(\d{4})-(\d{2})/.exec(value)
  if (!match) {
    return value
  }

  const date = new Date(Number(match[1]), Number(match[2]) - 1 + offset, 1)
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  return `${year}-${month}`
}

function createPaymentPrototypeMonthOptions(currentMonth = getCurrentMonthInputValue(), extraMonths: string[] = []) {
  const values = [
    ...Array.from({ length: 4 }, (_, index) => addPaymentPrototypeMonths(currentMonth, -index)),
    ...extraMonths,
  ].filter((value, index, source) => value && source.indexOf(value) === index)

  return values.map((value) => {
    return { value, label: formatMonth(`${value}-01`) }
  })
}

function PaymentsPrototypePanel({
  auth,
  canWritePayments,
  dictionaryClient,
  expenseTypes,
  financeClient,
  formStateClient,
  garages,
  incomeTypes,
  integrationClient,
  supplierGroups,
  suppliers,
  staffMembers,
  onOpenDialog,
}: {
  auth: AuthResponse
  canWritePayments: boolean
  dictionaryClient: DictionaryClient
  expenseTypes: AccountingTypeDto[]
  financeClient: FinanceClient
  formStateClient: FormStateClient
  garages: GarageDto[]
  incomeTypes: AccountingTypeDto[]
  integrationClient: IntegrationClient
  supplierGroups: SupplierGroupDto[]
  suppliers: SupplierDto[]
  staffMembers: StaffMemberDto[]
  onOpenDialog: (dialog: PaymentsPrototypeDialogKey, trigger?: HTMLButtonElement | null) => void
}) {
  const [activeTab, setActiveTab] = useState<'income' | 'expense'>('income')
  const [garageSearch, setGarageSearch] = useState('')
  const [garageSearchGarages, setGarageSearchGarages] = useState<GarageDto[]>([])
  const [garageSearchLoading, setGarageSearchLoading] = useState(false)
  const [garageSearchError, setGarageSearchError] = useState<string | null>(null)
  const [selectedGarageId, setSelectedGarageId] = useState<string | null>(null)
  const [selectedGarageIds, setSelectedGarageIds] = useState<string[]>([])
  const [incomeWorksheetMonthFrom, setIncomeWorksheetMonthFrom] = useState(() => getPreviousMonthInputValue(getCurrentMonthInputValue()))
  const [incomeWorksheetMonthTo, setIncomeWorksheetMonthTo] = useState(() => getCurrentMonthInputValue())
  const [garageRows, setGarageRows] = useState<GarageIncomePrototypeRow[]>([])
  const [garageWorksheetSummary, setGarageWorksheetSummary] = useState<GarageIncomeWorksheetPeriodSummary | null>(null)
  const [expenseRows, setExpenseRows] = useState<PaymentPrototypeRow[]>([])
  const [expenseWorksheetMonth, setExpenseWorksheetMonth] = useState('2026-06')
  const [expenseBankAmount, setExpenseBankAmount] = useState(0)
  const [historyRows, setHistoryRows] = useState<GaragePaymentHistoryPrototypeRow[]>([])
  const [formStateLoaded, setFormStateLoaded] = useState(false)
  const [formStateError, setFormStateError] = useState<string | null>(null)
  const [paymentError, setPaymentError] = useState<string | null>(null)
  const [garageWorksheetLoadingId, setGarageWorksheetLoadingId] = useState<string | null>(null)
  const [garagePaymentHistoryLoadingId, setGaragePaymentHistoryLoadingId] = useState<string | null>(null)
  const [expenseWorksheetLoading, setExpenseWorksheetLoading] = useState(false)
  const [savingPaymentRowId, setSavingPaymentRowId] = useState<string | null>(null)
  const [fullPaymentDialogOpen, setFullPaymentDialogOpen] = useState(false)
  const fullPaymentTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [debtTransferDialogOpen, setDebtTransferDialogOpen] = useState(false)
  const debtTransferTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [garageAccrualDialogOpen, setGarageAccrualDialogOpen] = useState(false)
  const garageAccrualTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [regularAccrualDialogOpen, setRegularAccrualDialogOpen] = useState(false)
  const regularAccrualTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [supplierAccrualDialogOpen, setSupplierAccrualDialogOpen] = useState(false)
  const supplierAccrualTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [salaryDialogOpen, setSalaryDialogOpen] = useState(false)
  const salaryTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [expenseDialogPreset, setExpenseDialogPreset] = useState<ExpensePrototypeDialogPreset | null>(null)
  const expenseTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [staffPaymentDialogPreset, setStaffPaymentDialogPreset] = useState<StaffPaymentPrototypeDialogPreset | null>(null)
  const staffPaymentTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [historyEdit, setHistoryEdit] = useState<GaragePaymentHistoryEditState | null>(null)
  const historyEditTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [historyCancel, setHistoryCancel] = useState<GaragePaymentHistoryCancelState | null>(null)
  const historyCancelTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [receiptAction, setReceiptAction] = useState<GaragePaymentReceiptActionState | null>(null)
  const receiptActionTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [historyActionSaving, setHistoryActionSaving] = useState(false)
  const [receiptActionSaving, setReceiptActionSaving] = useState(false)
  const [receiptActionStatus, setReceiptActionStatus] = useState<string | null>(null)
  const availableGarages = useMemo(() => {
    const uniqueGarages = new Map<string, GarageDto>()
    for (const garage of [...garages, ...garageSearchGarages]) {
      uniqueGarages.set(garage.id, garage)
    }
    return Array.from(uniqueGarages.values())
  }, [garageSearchGarages, garages])
  const realGarageIds = useMemo(() => new Set(availableGarages.filter((garage) => !garage.isArchived).map((garage) => garage.id)), [availableGarages])
  const garageOptions = useMemo<PaymentsPrototypeGarage[]>(
    () => availableGarages
      .filter((garage) => !garage.isArchived)
      .map((garage) => ({
        id: garage.id,
        number: garage.number,
        ownerName: garage.ownerName?.trim() || 'Владелец не указан',
        phone: '',
        peopleCount: garage.peopleCount,
        floorCount: garage.floorCount,
        balance: garage.balance,
        overdueDebt: garage.overdueDebt,
      })),
    [availableGarages],
  )
  const selectedGarage = garageOptions.find((garage) => garage.id === selectedGarageId) ?? null
  const selectedGarages = selectedGarageIds
    .map((garageId) => garageOptions.find((garage) => garage.id === garageId))
    .filter((garage): garage is PaymentsPrototypeGarage => garage !== undefined)
  const normalizedSearch = garageSearch.trim().toLowerCase()
  const garageSearchResults = garageOptions
    .filter((garage) => !normalizedSearch || garage.number.toLowerCase().includes(normalizedSearch) || garage.ownerName.toLowerCase().includes(normalizedSearch))
    .slice(0, 20)
  const shouldShowGarageResults = garageSearch.trim().length > 0
  const garageSearchListId = useId()
  const incomeWorksheetMonthOptions = useMemo(
    () => createPaymentPrototypeMonthOptions(getCurrentMonthInputValue(), [incomeWorksheetMonthFrom, incomeWorksheetMonthTo]),
    [incomeWorksheetMonthFrom, incomeWorksheetMonthTo],
  )

  useEffect(() => {
    const query = garageSearch.trim()
    if (!query) {
      setGarageSearchGarages([])
      setGarageSearchLoading(false)
      setGarageSearchError(null)
      return
    }

    let cancelled = false
    const handle = window.setTimeout(() => {
      setGarageSearchLoading(true)
      setGarageSearchError(null)
      const request = dictionaryClient.getGaragesPage
        ? dictionaryClient.getGaragesPage(auth.accessToken, query, 0, 20)
            .then((page) => page.items)
        : dictionaryClient.getGarages(auth.accessToken, query, 20)
      void request
        .then((foundGarages) => {
          if (!cancelled) {
            setGarageSearchGarages(foundGarages)
          }
        })
        .catch((error: unknown) => {
          if (!cancelled) {
            setGarageSearchError(error instanceof Error ? error.message : 'Не удалось выполнить поиск гаражей.')
          }
        })
        .finally(() => {
          if (!cancelled) {
            setGarageSearchLoading(false)
          }
        })
    }, 250)

    return () => {
      cancelled = true
      window.clearTimeout(handle)
    }
  }, [auth.accessToken, dictionaryClient, garageSearch])

  useEffect(() => {
    if (formStateLoaded || garageOptions.length === 0) {
      return
    }

    let cancelled = false
    formStateClient
      .getState<PaymentsPrototypeSavedState>(auth.accessToken, paymentsFormStateScope)
      .then((state) => {
        if (cancelled) {
          return
        }

        if (state?.payload) {
          const restoredGarageIds = (state.payload.selectedGarageIds?.length
            ? state.payload.selectedGarageIds
            : state.payload.selectedGarageId ? [state.payload.selectedGarageId] : [])
            .filter((garageId) => realGarageIds.has(garageId))
          const restoredGarageId = restoredGarageIds.includes(state.payload.selectedGarageId ?? '')
            ? state.payload.selectedGarageId
            : restoredGarageIds.at(-1) ?? null
          const restoredMonthFrom = state.payload.incomeWorksheetMonthFrom ?? getPreviousMonthInputValue(getCurrentMonthInputValue())
          const restoredMonthTo = state.payload.incomeWorksheetMonthTo ?? getCurrentMonthInputValue()
          const isRestoredRealGarage = restoredGarageId !== null && realGarageIds.has(restoredGarageId)
          const restoredRealGarage = isRestoredRealGarage ? garageOptions.find((garage) => garage.id === restoredGarageId) ?? null : null
          setSelectedGarageId(isRestoredRealGarage ? restoredGarageId : null)
          setSelectedGarageIds(restoredGarageIds)
          setGarageSearch(state.payload.garageSearch ?? '')
          setIncomeWorksheetMonthFrom(restoredMonthFrom)
          setIncomeWorksheetMonthTo(restoredMonthTo)
          setGarageRows([])
          setHistoryRows([])
          if (restoredRealGarage) {
            setGarageWorksheetLoadingId(restoredRealGarage.id)
            void financeClient
              .getGarageIncomeWorksheet(auth.accessToken, restoredRealGarage.id, {
                monthFrom: `${restoredMonthFrom}-01`,
                monthTo: `${restoredMonthTo}-01`,
              })
              .then((worksheet) => {
                if (cancelled) {
                  return
                }

                setGarageRows(createGarageIncomeRowsFromWorksheet(worksheet))
                setGarageWorksheetSummary({
                  openingDebt: worksheet.openingDebt,
                  accrualTotal: worksheet.accrualTotal,
                  incomeTotal: worksheet.incomeTotal,
                  closingDebt: worksheet.closingDebt,
                })
              })
              .catch((error: unknown) => {
                if (!cancelled) {
                  setPaymentError(error instanceof Error ? error.message : 'Не удалось загрузить форму поступлений гаража.')
                }
              })
              .finally(() => {
                if (!cancelled) {
                  setGarageWorksheetLoadingId((currentId) => (currentId === restoredRealGarage.id ? null : currentId))
                }
              })
            setGaragePaymentHistoryLoadingId(restoredRealGarage.id)
            void financeClient
              .getOperationsPage(auth.accessToken, {
                operationKind: 'income',
                garageId: restoredRealGarage.id,
                limit: 100,
              })
              .then((page) => {
                if (!cancelled) {
                  setHistoryRows(createGaragePaymentHistoryRowsFromOperations(page.items))
                }
              })
              .catch((error: unknown) => {
                if (!cancelled) {
                  setPaymentError(error instanceof Error ? error.message : 'Не удалось загрузить историю платежей выбранного гаража.')
                }
              })
              .finally(() => {
                if (!cancelled) {
                  setGaragePaymentHistoryLoadingId((currentId) => (currentId === restoredRealGarage.id ? null : currentId))
                }
              })
          }
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить сохраненное состояние платежей.')
        }
      })
      .finally(() => {
        if (!cancelled) {
          setFormStateLoaded(true)
        }
      })

    return () => {
      cancelled = true
    }
  }, [auth.accessToken, financeClient, formStateClient, formStateLoaded, garageOptions, realGarageIds])

  useEffect(() => {
    if (!formStateLoaded) {
      return
    }

    const handle = window.setTimeout(() => {
      void formStateClient
        .saveState<PaymentsPrototypeSavedState>(auth.accessToken, paymentsFormStateScope, {
          payload: { selectedGarageId, selectedGarageIds, garageSearch, incomeWorksheetMonthFrom, incomeWorksheetMonthTo, garageRows, historyRows },
          summary: 'Сохранены выбранные гаражи и период формы платежей.'
        })
        .catch((error: unknown) => setFormStateError(error instanceof Error ? error.message : 'Не удалось сохранить состояние платежей.'))
    }, 400)

    return () => window.clearTimeout(handle)
  }, [auth.accessToken, formStateClient, formStateLoaded, garageRows, garageSearch, historyRows, incomeWorksheetMonthFrom, incomeWorksheetMonthTo, selectedGarageId, selectedGarageIds])

  useEffect(() => {
    if (activeTab !== 'expense') {
      return
    }

    let cancelled = false
    setExpenseWorksheetLoading(true)
    setExpenseRows([])
    setExpenseBankAmount(0)
    setPaymentError(null)
    financeClient
      .getExpenseWorksheet(auth.accessToken, { accountingMonth: `${expenseWorksheetMonth}-01` })
      .then((worksheet) => {
        if (!cancelled) {
          setExpenseRows(createExpenseRowsFromWorksheet(worksheet))
          setExpenseBankAmount(worksheet.bankAmount)
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
    }
  }, [activeTab, auth.accessToken, expenseWorksheetMonth, financeClient])

  function activateExpenseTab() {
    if (activeTab !== 'expense') {
      setExpenseWorksheetLoading(true)
    }

    setActiveTab('expense')
  }

  function handleExpenseWorksheetMonthChange(value: string) {
    setExpenseWorksheetLoading(true)
    setExpenseWorksheetMonth(value)
  }

  function openDialogFromButton(event: MouseEvent<HTMLButtonElement>, dialog: PaymentsPrototypeDialogKey) {
    event.currentTarget.focus()
    onOpenDialog(dialog, event.currentTarget)
  }

  function openFullPaymentDialog(event: MouseEvent<HTMLButtonElement>) {
    fullPaymentTriggerRef.current = event.currentTarget
    setPaymentError(null)
    setFullPaymentDialogOpen(true)
  }

  function closeFullPaymentDialog() {
    const trigger = fullPaymentTriggerRef.current
    setFullPaymentDialogOpen(false)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      fullPaymentTriggerRef.current = null
    }, 0)
  }

  function openDebtTransferDialog(event: MouseEvent<HTMLButtonElement>) {
    debtTransferTriggerRef.current = event.currentTarget
    setPaymentError(null)
    setDebtTransferDialogOpen(true)
  }

  function closeDebtTransferDialog() {
    const trigger = debtTransferTriggerRef.current
    setDebtTransferDialogOpen(false)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      debtTransferTriggerRef.current = null
    }, 0)
  }

  function openGarageAccrualDialog(event: MouseEvent<HTMLButtonElement>) {
    garageAccrualTriggerRef.current = event.currentTarget
    setPaymentError(null)
    setGarageAccrualDialogOpen(true)
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

  function openRegularAccrualDialog(event: MouseEvent<HTMLButtonElement>) {
    regularAccrualTriggerRef.current = event.currentTarget
    setPaymentError(null)
    setRegularAccrualDialogOpen(true)
  }

  function closeRegularAccrualDialog() {
    const trigger = regularAccrualTriggerRef.current
    setRegularAccrualDialogOpen(false)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      regularAccrualTriggerRef.current = null
    }, 0)
  }

  function openSupplierAccrualDialog(event: MouseEvent<HTMLButtonElement>) {
    supplierAccrualTriggerRef.current = event.currentTarget
    setPaymentError(null)
    setSupplierAccrualDialogOpen(true)
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

  function openSalaryDialog(event: MouseEvent<HTMLButtonElement>) {
    salaryTriggerRef.current = event.currentTarget
    setPaymentError(null)
    setSalaryDialogOpen(true)
  }

  function closeSalaryDialog() {
    const trigger = salaryTriggerRef.current
    setSalaryDialogOpen(false)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      salaryTriggerRef.current = null
    }, 0)
  }

  function openExpenseDialog(event: MouseEvent<HTMLButtonElement>, preset?: ExpensePrototypeDialogPreset) {
    expenseTriggerRef.current = event.currentTarget
    setPaymentError(null)
    setExpenseDialogPreset(preset ?? {})
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

  function openStaffPaymentDialog(event: MouseEvent<HTMLButtonElement>, preset?: StaffPaymentPrototypeDialogPreset) {
    staffPaymentTriggerRef.current = event.currentTarget
    setPaymentError(null)
    setStaffPaymentDialogPreset(preset ?? {})
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
  ) {
    setGarageWorksheetLoadingId(garage.id)
    try {
      const worksheet = await financeClient.getGarageIncomeWorksheet(auth.accessToken, garage.id, {
        monthFrom: `${monthFrom}-01`,
        monthTo: `${monthTo}-01`,
      })
      const rows = createGarageIncomeRowsFromWorksheet(worksheet)
      setGarageRows(rows)
      setGarageWorksheetSummary({
        openingDebt: worksheet.openingDebt,
        accrualTotal: worksheet.accrualTotal,
        incomeTotal: worksheet.incomeTotal,
        closingDebt: worksheet.closingDebt,
      })
    } catch (error) {
      setPaymentError(error instanceof Error ? error.message : 'Не удалось загрузить форму поступлений гаража.')
    } finally {
      setGarageWorksheetLoadingId((currentId) => (currentId === garage.id ? null : currentId))
    }
  }

  async function loadGaragePaymentHistory(garage: PaymentsPrototypeGarage) {
    setGaragePaymentHistoryLoadingId(garage.id)
    try {
      const page = await financeClient.getOperationsPage(auth.accessToken, {
        operationKind: 'income',
        garageId: garage.id,
        limit: 100,
      })
      setHistoryRows(createGaragePaymentHistoryRowsFromOperations(page.items))
    } catch (error) {
      setPaymentError(error instanceof Error ? error.message : 'Не удалось загрузить историю платежей выбранного гаража.')
    } finally {
      setGaragePaymentHistoryLoadingId((currentId) => (currentId === garage.id ? null : currentId))
    }
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

  function openReceiptAction(row: GaragePaymentHistoryPrototypeRow, action: ReceiptPrintingActionKind, trigger?: HTMLButtonElement | null) {
    if (!row.operation || !canWritePayments || row.operation.isCanceled) {
      return
    }

    receiptActionTriggerRef.current = trigger ?? null
    setPaymentError(null)
    setReceiptActionStatus(null)
    setReceiptAction({ row, action, reason: '', error: null })
  }

  function closeReceiptActionDialog() {
    const trigger = receiptActionTriggerRef.current
    setReceiptAction(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      receiptActionTriggerRef.current = null
    }, 0)
  }

  async function saveHistoryEdit() {
    if (!historyEdit?.row.operation || !selectedGarage) {
      return
    }

    const operation = historyEdit.row.operation
    const amount = Number(historyEdit.amount.trim().replace(',', '.'))
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
      closeHistoryEditDialog()
      await Promise.all([
        loadGaragePaymentHistory(selectedGarage),
        loadGarageIncomeWorksheet(selectedGarage),
      ])
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
      await financeClient.cancelOperation(auth.accessToken, historyCancel.row.operation.id, { reason })
      closeHistoryCancelDialog()
      await Promise.all([
        loadGaragePaymentHistory(selectedGarage),
        loadGarageIncomeWorksheet(selectedGarage),
      ])
    } catch (error) {
      setHistoryCancel((state) => state ? { ...state, error: error instanceof Error ? error.message : 'Не удалось отменить платеж.' } : state)
    } finally {
      setHistoryActionSaving(false)
    }
  }

  async function confirmReceiptAction() {
    if (!receiptAction?.row.operation) {
      return
    }

    const reason = receiptAction.reason.trim()
    if (receiptAction.action !== 'print' && !reason) {
      setReceiptAction((state) => state ? { ...state, error: 'Укажите причину для отмены или повторной печати квитанции.' } : state)
      return
    }

    setReceiptActionSaving(true)
    setReceiptAction((state) => state ? { ...state, error: null } : state)
    try {
      const result = await integrationClient.registerReceiptPrintingAction(auth.accessToken, receiptAction.row.operation.id, {
        action: receiptAction.action,
        reason: reason || undefined,
      })
      closeReceiptActionDialog()
      setReceiptActionStatus(result.isCopy && result.copyMark ? `${result.statusMessage} Отметка: ${result.copyMark}.` : result.statusMessage)
    } catch (error) {
      setReceiptAction((state) => state ? { ...state, error: error instanceof Error ? error.message : 'Не удалось зарегистрировать действие квитанции.' } : state)
    } finally {
      setReceiptActionSaving(false)
    }
  }

  function activateGarage(garage: PaymentsPrototypeGarage) {
    setSelectedGarageId(garage.id)
    setGarageRows([])
    setGarageWorksheetSummary(null)
    setHistoryRows([])
    setPaymentError(null)
    setReceiptActionStatus(null)
    void loadGarageIncomeWorksheet(garage)
    void loadGaragePaymentHistory(garage)
  }

  function toggleGarageSelection(garage: PaymentsPrototypeGarage) {
    if (selectedGarageIds.includes(garage.id)) {
      const remainingGarageIds = selectedGarageIds.filter((garageId) => garageId !== garage.id)
      setSelectedGarageIds(remainingGarageIds)
      if (selectedGarageId === garage.id) {
        const nextGarage = garageOptions.find((option) => option.id === remainingGarageIds.at(-1)) ?? null
        if (nextGarage) {
          activateGarage(nextGarage)
        } else {
          setSelectedGarageId(null)
          setGarageRows([])
          setGarageWorksheetSummary(null)
          setHistoryRows([])
        }
      }
      return
    }

    setSelectedGarageIds((garageIds) => [...garageIds, garage.id])
    activateGarage(garage)
  }

  function clearGarageSelection() {
    setSelectedGarageIds([])
    setSelectedGarageId(null)
    setGarageRows([])
    setGarageWorksheetSummary(null)
    setHistoryRows([])
    setPaymentError(null)
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

  function setCurrentIncomeWorksheetMonth() {
    const currentMonth = getCurrentMonthInputValue()
    setIncomeWorksheetMonthFrom(currentMonth)
    setIncomeWorksheetMonthTo(currentMonth)
    if (selectedGarage) {
      void loadGarageIncomeWorksheet(selectedGarage, currentMonth, currentMonth)
    }
  }

  function selectFirstGarageResult() {
    if (garageSearchResults.length > 0) {
      toggleGarageSelection(garageSearchResults[0])
    }
  }

  function handlePaymentDraftChange(rowId: string, value: string) {
    setPaymentError(null)
    setGarageRows((currentRows) => currentRows.map((row) => row.id === rowId ? { ...row, paymentDraft: value } : row))
  }

  function findIncomeTypeForPayment(serviceName: string) {
    const normalizedService = serviceName.trim().toLocaleLowerCase('ru-RU')
    const activeIncomeTypes = incomeTypes.filter((incomeType) => !incomeType.isArchived)

    return activeIncomeTypes.find((incomeType) => incomeType.name.trim().toLocaleLowerCase('ru-RU') === normalizedService)
      ?? activeIncomeTypes.find((incomeType) => {
        const normalizedTypeName = incomeType.name.trim().toLocaleLowerCase('ru-RU')
        return normalizedTypeName.length > 0 && (normalizedTypeName.includes(normalizedService) || normalizedService.includes(normalizedTypeName))
      })
      ?? null
  }

  async function commitGaragePayment(row: GarageIncomePrototypeRow) {
    const amount = Number(row.paymentDraft.trim().replace(',', '.'))
    if (!Number.isFinite(amount) || amount <= 0) {
      return
    }

    if (!selectedGarage || !realGarageIds.has(selectedGarage.id)) {
      setPaymentError('Выберите гараж из справочника, чтобы сохранить платеж в истории операций.')
      return
    }

    const incomeType = findIncomeTypeForPayment(row.service)
    if (!incomeType) {
      setPaymentError(`Не найден вид поступления для услуги "${row.service}". Добавьте его в справочник и повторите сохранение.`)
      return
    }

    const nextPaid = row.paid + amount
    const nextDebt = Math.max(row.payable - nextPaid, 0)
    const accountingMonth = row.month.length === 7 ? `${row.month}-01` : row.month
    setSavingPaymentRowId(row.id)
    setPaymentError(null)

    try {
      const operation = await financeClient.createIncome(auth.accessToken, {
        garageId: selectedGarage.id,
        incomeTypeId: incomeType.id,
        operationDate: getLocalDateInputValue(),
        accountingMonth,
        amount,
        comment: `Платеж из формы поступлений: ${row.service} ${row.monthLabel}`,
      })
      const paymentTime = new Date().toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
      const historyDebtAfter = operation.garageDebtAfter ?? nextDebt

      setGarageRows((currentRows) => currentRows.map((currentRow) => currentRow.id === row.id ? { ...currentRow, paymentDraft: '', paid: nextPaid, debt: nextDebt } : currentRow))
      setHistoryRows((currentRows) => [
        { id: operation.id, date: formatDateOnly(operation.operationDate), time: formatOperationTime(operation.createdAtUtc) || paymentTime, amount: operation.amount, purpose: operation.incomeTypeName ?? row.service, debtAfter: historyDebtAfter },
        ...currentRows,
      ])
      void loadGaragePaymentHistory(selectedGarage)
    } catch (error) {
      setPaymentError(error instanceof Error ? error.message : 'Не удалось сохранить платеж. Повторите попытку позже.')
    } finally {
      setSavingPaymentRowId(null)
    }
  }

  function getRowsForFullPayment(period: string) {
    return garageRows.filter((row) => row.debt > 0 && (period === 'full' || row.month === period))
  }

  function getOpeningDebtForFullPayment(period: string) {
    return period === 'full' ? Math.max(garageWorksheetSummary?.openingDebt ?? 0, 0) : 0
  }

  async function commitFullGaragePayment(request: FullPaymentPrototypeSubmitRequest) {
    if (!selectedGarage || !realGarageIds.has(selectedGarage.id)) {
      return 'Выберите гараж из справочника, чтобы сохранить полную оплату в истории операций.'
    }

    const rowsToPay = getRowsForFullPayment(request.period)
    const rowsDebtToPay = rowsToPay.reduce((sum, row) => sum + row.debt, 0)
    const openingDebtToPay = getOpeningDebtForFullPayment(request.period)
    const totalDebtToPay = rowsDebtToPay + openingDebtToPay
    if (totalDebtToPay <= 0) {
      return 'По выбранному периоду нет задолженности для оплаты.'
    }
    let remainingAmount = rowsDebtToPay
    const paymentPlan: Array<{ row: GarageIncomePrototypeRow; incomeType: AccountingTypeDto; amount: number }> = []
    for (const row of rowsToPay) {
      if (remainingAmount <= 0) {
        break
      }

      const incomeType = findIncomeTypeForPayment(row.service)
      if (!incomeType) {
        return `Не найден вид поступления для услуги "${row.service}". Добавьте его в справочник и повторите сохранение.`
      }

      const rowAmount = Math.min(row.debt, remainingAmount)
      paymentPlan.push({ row, incomeType, amount: rowAmount })
      remainingAmount -= rowAmount
    }

    if (paymentPlan.length === 0 && openingDebtToPay <= 0) {
      return 'Укажите сумму полной оплаты больше нуля.'
    }

    const historyItems: Array<{ operation: FinancialOperationDto; purposeFallback: string; debtAfterFallback: number }> = []
    if (openingDebtToPay > 0) {
      const operation = await financeClient.createGarageDebtPayment(auth.accessToken, {
        garageId: selectedGarage.id,
        operationDate: getLocalDateInputValue(),
        accountingMonth: incomeWorksheetMonthFrom.length === 7 ? `${incomeWorksheetMonthFrom}-01` : incomeWorksheetMonthFrom,
        amount: openingDebtToPay,
        comment: request.comment.trim() || undefined,
      })
      historyItems.push({
        operation,
        purposeFallback: 'Оплата входящего долга',
        debtAfterFallback: Math.max(totalDebtToPay - openingDebtToPay, 0),
      })
    }

    for (const item of paymentPlan) {
      const accountingMonth = item.row.month.length === 7 ? `${item.row.month}-01` : item.row.month
      const operation = await financeClient.createIncome(auth.accessToken, {
        garageId: selectedGarage.id,
        incomeTypeId: item.incomeType.id,
        operationDate: getLocalDateInputValue(),
        accountingMonth,
        amount: item.amount,
        comment: request.comment.trim()
          ? `Полная оплата ${item.row.service} ${item.row.monthLabel}: ${request.comment.trim()}`
          : `Полная оплата ${item.row.service} ${item.row.monthLabel}`,
      })
      historyItems.push({
        operation,
        purposeFallback: item.row.service,
        debtAfterFallback: Math.max(item.row.debt - item.amount, 0),
      })
    }

    const paidByRowId = new Map(paymentPlan.map((item) => [item.row.id, item.amount]))
    setGarageRows((currentRows) => currentRows.map((row) => {
      const paidAmount = paidByRowId.get(row.id)
      return paidAmount ? { ...row, paymentDraft: '', paid: row.paid + paidAmount, debt: Math.max(row.debt - paidAmount, 0) } : row
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
          debtAfter: operation.garageDebtAfter ?? item.debtAfterFallback,
        }
      }),
      ...currentRows,
    ])

    void loadGarageIncomeWorksheet(selectedGarage)
    void loadGaragePaymentHistory(selectedGarage)

    return null
  }

  async function commitDebtTransfer(request: DebtTransferPrototypeSubmitRequest) {
    if (!selectedGarage || !realGarageIds.has(selectedGarage.id)) {
      return 'Выберите гараж из справочника, чтобы перенести задолженность между месяцами.'
    }

    if (request.sourceMonth === request.targetMonth) {
      return 'Месяц переноса должен отличаться от исходного месяца.'
    }

    const sourceRows = garageRows.filter((row) => row.month === request.sourceMonth && row.debt > 0)
    const availableDebt = sourceRows.reduce((sum, row) => sum + row.debt, 0)
    if (availableDebt <= 0) {
      return 'В исходном месяце нет задолженности для переноса.'
    }

    if (request.amount <= 0 || request.amount > availableDebt) {
      return `Сумма переноса должна быть больше нуля и не выше долга ${formatPaymentPrototypeValue(availableDebt)}.`
    }

    const transferDate = getLocalDateInputValue()
    const transferTime = new Date().toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
    const sourceLabel = formatPaymentPrototypeMonthLabel(request.sourceMonth)
    const targetLabel = formatPaymentPrototypeMonthLabel(request.targetMonth)
    await financeClient.createDebtTransfer(auth.accessToken, {
      garageId: selectedGarage.id,
      sourceMonth: `${request.sourceMonth}-01`,
      targetMonth: `${request.targetMonth}-01`,
      amount: request.amount,
      comment: request.comment.trim() || undefined,
    })
    const allocations: Array<{ sourceRowId: string; service: string; amount: number }> = []
    let remainingAmount = request.amount

    sourceRows.forEach((row) => {
      if (remainingAmount <= 0) {
        return
      }

      const amount = Math.min(row.debt, remainingAmount)
      allocations.push({ sourceRowId: row.id, service: row.service, amount })
      remainingAmount -= amount
    })

    setGarageRows((currentRows) => {
      let nextRows = currentRows.map((row) => {
        const allocation = allocations.find((item) => item.sourceRowId === row.id)
        return allocation ? { ...row, debt: Math.max(row.debt - allocation.amount, 0) } : row
      })

      allocations.forEach((allocation) => {
        const transferService = `Перенос задолженности: ${allocation.service}`
        const existingTransfer = nextRows.find((row) => row.month === request.targetMonth && row.service === transferService)
        if (existingTransfer) {
          nextRows = nextRows.map((row) => row.id === existingTransfer.id
            ? { ...row, payable: row.payable + allocation.amount, debt: row.debt + allocation.amount }
            : row)
          return
        }

        nextRows = [
          ...nextRows,
          {
            id: `garage-transfer-${selectedGarage.id}-${request.sourceMonth}-${request.targetMonth}-${allocation.sourceRowId}`,
            month: request.targetMonth,
            monthLabel: targetLabel,
            service: transferService,
            meter: null,
            difference: null,
            payable: allocation.amount,
            paymentDraft: '',
            paid: 0,
            debt: allocation.amount,
          },
        ]
      })

      return nextRows
    })

    setHistoryRows((currentRows) => [
      {
        id: `debt-transfer-${selectedGarage.id}-${request.sourceMonth}-${request.targetMonth}-${transferDate}-${transferTime}`,
        date: formatDateOnly(transferDate),
        time: transferTime,
        amount: request.amount,
        purpose: request.comment.trim()
          ? `Перенос задолженности ${sourceLabel} -> ${targetLabel}: ${request.comment.trim()}`
          : `Перенос задолженности ${sourceLabel} -> ${targetLabel}`,
        debtAfter: debtTotal,
      },
      ...currentRows,
    ])

    return null
  }

  async function commitGarageAccrual(request: GarageAccrualPrototypeSubmitRequest) {
    if (!selectedGarage || !realGarageIds.has(selectedGarage.id)) {
      return 'Выберите гараж из справочника, чтобы сохранить начисление в истории операций.'
    }

    const incomeType = incomeTypes.find((item) => item.id === request.incomeTypeId && !item.isArchived) ?? null
    if (!incomeType) {
      return 'Выберите вид начисления из справочника поступлений.'
    }

    const savedAccrual = await financeClient.createAccrual(auth.accessToken, {
      garageId: selectedGarage.id,
      incomeTypeId: incomeType.id,
      accountingMonth: request.accountingMonth,
      amount: request.amount,
      source: 'manual',
      comment: request.comment.trim() || undefined,
    })
    const month = savedAccrual.accountingMonth.slice(0, 7)
    const monthLabel = formatPaymentPrototypeMonthLabel(savedAccrual.accountingMonth)

    setGarageRows((currentRows) => {
      const existingRow = currentRows.find((row) => row.month === month && row.service.trim().toLocaleLowerCase('ru-RU') === savedAccrual.incomeTypeName.trim().toLocaleLowerCase('ru-RU'))
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
          service: savedAccrual.incomeTypeName,
          meter: null,
          difference: null,
          payable: savedAccrual.amount,
          paymentDraft: '',
          paid: 0,
          debt: savedAccrual.amount,
        },
      ]
    })

    return null
  }

  async function commitRegularAccruals(request: RegularAccrualPrototypeSubmitRequest) {
    const result = await financeClient.generateRegularCatalogAccruals(auth.accessToken, {
      accountingMonth: request.accountingMonth,
      comment: request.comment.trim() || undefined,
    })
    const createdAccruals = result.serviceResults.flatMap((serviceResult) => serviceResult.createdAccruals)

    const selectedGarageAccruals = selectedGarage
      ? createdAccruals.filter((accrual) => accrual.garageId === selectedGarage.id)
      : createdAccruals

    setGarageRows((currentRows) => {
      let nextRows = currentRows
      selectedGarageAccruals.forEach((accrual) => {
        const month = accrual.accountingMonth.slice(0, 7)
        const existingRowId = `garage-${accrual.garageId}-${month}-${accrual.incomeTypeId}`
        let updated = false
        nextRows = nextRows.map((row) => {
          const matchesRow = row.id === existingRowId || (row.month === month && row.service.trim().toLocaleLowerCase('ru-RU') === accrual.incomeTypeName.trim().toLocaleLowerCase('ru-RU'))
          if (!matchesRow) {
            return row
          }

          updated = true
          return { ...row, payable: row.payable + accrual.amount, debt: row.debt + accrual.amount }
        })

        if (!updated) {
          nextRows = [
            ...nextRows,
            {
              id: existingRowId,
              month,
              monthLabel: formatPaymentPrototypeMonthLabel(accrual.accountingMonth),
              service: accrual.incomeTypeName,
              meter: null,
              difference: null,
              payable: accrual.amount,
              paymentDraft: '',
              paid: 0,
              debt: accrual.amount,
            },
          ]
        }
      })

      return nextRows
    })

    return null
  }

  async function commitExpensePayment(request: ExpensePrototypeSubmitRequest) {
    const supplier = suppliers.find((item) => item.id === request.supplierId && !item.isArchived) ?? null
    if (!supplier) {
      return 'Выберите поставщика из справочника.'
    }

    const expenseType = expenseTypes.find((item) => item.id === request.expenseTypeId && !item.isArchived) ?? null
    if (!expenseType) {
      return 'Выберите вид выплаты из справочника.'
    }

    const operation = await financeClient.createExpense(auth.accessToken, {
      supplierId: supplier.id,
      expenseTypeId: expenseType.id,
      operationDate: request.operationDate,
      accountingMonth: request.accountingMonth,
      amount: request.amount,
      documentNumber: request.documentNumber.trim() || undefined,
      comment: request.comment.trim() || undefined,
    })

    setExpenseRows((currentRows) => currentRows.map((row, index) => {
      const shouldUpdate = request.rowIndex === index
        || (request.rowIndex === undefined && row.item.trim().toLocaleLowerCase('ru-RU') === (operation.expenseTypeName ?? expenseType.name).trim().toLocaleLowerCase('ru-RU'))
      if (!shouldUpdate) {
        return row
      }

      const paid = typeof row.paid === 'number' ? row.paid + operation.amount : operation.amount
      const cost = typeof row.cost === 'number' ? row.cost : operation.amount
      const balance = Math.max(cost - paid, 0)
      return { ...row, paid, balance }
    }))

    return null
  }

  async function commitStaffPayment(request: StaffPaymentPrototypeSubmitRequest) {
    const staffMember = staffMembers.find((item) => item.id === request.staffMemberId && !item.isArchived) ?? null
    if (!staffMember) {
      return 'Выберите сотрудника из справочника персонала.'
    }

    const operation = await financeClient.createStaffPayment(auth.accessToken, {
      staffMemberId: staffMember.id,
      operationDate: request.operationDate,
      accountingMonth: request.accountingMonth,
      amount: request.amount,
      documentNumber: request.documentNumber.trim() || undefined,
      comment: request.comment.trim() || undefined,
    })

    setExpenseRows((currentRows) => currentRows.map((row, index) => {
      const normalizedCounterparty = row.counterparty?.trim().toLocaleLowerCase('ru-RU') ?? ''
      const normalizedStaffName = (operation.staffMemberName ?? staffMember.fullName).trim().toLocaleLowerCase('ru-RU')
      const shouldUpdate = request.rowIndex === index
        || (request.rowIndex === undefined && normalizedCounterparty.length > 0 && normalizedStaffName.includes(normalizedCounterparty))
      if (!shouldUpdate) {
        return row
      }

      const paid = (typeof row.paid === 'number' ? row.paid : 0) + operation.amount
      const cost = typeof row.cost === 'number' ? row.cost : staffMember.rate
      const balance = Math.max(cost - paid, 0)
      return { ...row, paid, balance }
    }))

    return null
  }

  async function commitSupplierAccrual(request: SupplierAccrualPrototypeSubmitRequest) {
    const supplier = suppliers.find((item) => item.id === request.supplierId && !item.isArchived) ?? null
    if (!supplier) {
      return 'Выберите поставщика из справочника.'
    }

    const expenseType = expenseTypes.find((item) => item.id === request.expenseTypeId && !item.isArchived) ?? null
    if (!expenseType) {
      return 'Выберите вид начисления из справочника выплат.'
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
        return { ...row, cost, balance: Math.max(cost - paid, 0) }
      })

      if (updated) {
        return nextRows
      }

      return [
        ...nextRows,
        {
          item: accrual.expenseTypeName,
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

  async function commitSalaryAccruals(request: SalaryAccrualPrototypeSubmitRequest) {
    const supplierGroup = supplierGroups.find((item) => item.id === request.supplierGroupId && !item.isArchived) ?? null
    if (!supplierGroup) {
      return 'Выберите группу сотрудников или поставщиков.'
    }

    const result = await financeClient.generateSupplierGroupSalaryAccruals(auth.accessToken, {
      supplierGroupId: supplierGroup.id,
      accountingMonth: request.accountingMonth,
      amount: request.amount,
      documentNumber: request.documentNumber.trim() || undefined,
      comment: request.comment.trim() || undefined,
    })

    setExpenseRows((currentRows) => {
      let nextRows = currentRows
      result.createdAccruals.forEach((accrual) => {
        let updated = false
        nextRows = nextRows.map((row) => {
          const normalizedCounterparty = row.counterparty?.trim().toLocaleLowerCase('ru-RU') ?? ''
          const normalizedSupplier = accrual.supplierName.trim().toLocaleLowerCase('ru-RU')
          const matchesCounterparty = normalizedCounterparty.length > 0 && normalizedSupplier.includes(normalizedCounterparty)
          const matchesService = row.item.trim().toLocaleLowerCase('ru-RU') === accrual.expenseTypeName.trim().toLocaleLowerCase('ru-RU')
          if (!matchesCounterparty && !matchesService) {
            return row
          }

          updated = true
          const cost = (typeof row.cost === 'number' ? row.cost : 0) + accrual.amount
          const paid = typeof row.paid === 'number' ? row.paid : 0
          return { ...row, cost, balance: Math.max(cost - paid, 0) }
        })

        if (!updated) {
          nextRows = [
            ...nextRows,
            {
              item: accrual.expenseTypeName,
              counterparty: accrual.supplierName,
              cost: accrual.amount,
              paid: 0,
              balance: accrual.amount,
              collected: '',
              difference: '',
              action: true,
            },
          ]
        }
      })

      return nextRows
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

  const paymentTotal = garageRows.reduce((sum, row) => sum + row.payable, 0)
  const paidTotal = garageRows.reduce((sum, row) => sum + row.paid, 0)
  const debtTotal = garageRows.reduce((sum, row) => sum + row.debt, 0)
  const fullPaymentPeriodOptions = [
    { value: 'full', label: 'Полный расчет', debt: debtTotal + getOpeningDebtForFullPayment('full') },
    ...groupedGarageRows.map((group) => ({ value: group.month, label: group.monthLabel, debt: group.rows.reduce((sum, row) => sum + row.debt, 0) })),
  ].filter((option, index, options) => index === 0 || option.debt > 0 || !options.some((existingOption, existingIndex) => existingIndex < index && existingOption.value === option.value))
  const debtTransferSourceOptions: DebtTransferPrototypePeriodOption[] = groupedGarageRows
    .map((group) => ({
      value: group.month,
      label: group.monthLabel,
      debt: group.rows.reduce((sum, row) => sum + row.debt, 0),
      defaultTargetMonth: addPaymentPrototypeMonths(group.month, 1),
    }))
    .filter((option) => option.debt > 0)
  const debtTransferTargetMonths = Array.from(new Set([
    ...groupedGarageRows.map((group) => group.month),
    ...debtTransferSourceOptions.map((option) => option.defaultTargetMonth),
  ]))
    .sort()
    .map((month) => ({ value: month, label: formatPaymentPrototypeMonthLabel(month) }))
  const expenseAccrualTotal = expenseRows.reduce((sum, row) => sum + (typeof row.cost === 'number' ? row.cost : 0), 0)
  const expensePaidTotal = expenseRows.reduce((sum, row) => sum + (typeof row.paid === 'number' ? row.paid : 0), 0)
  const expenseBalanceTotal = expenseRows.reduce((sum, row) => sum + (typeof row.balance === 'number' ? row.balance : 0), 0)
  const expenseCollectedTotal = expenseRows.reduce((sum, row) => sum + (typeof row.collected === 'number' ? row.collected : 0), 0)
  const expenseDifferenceTotal = expenseCollectedTotal - expenseAccrualTotal
  const expenseCashTotal = expenseCollectedTotal - expensePaidTotal
  const expenseMonthLabel = expenseWorksheetMonth === '2026-06'
    ? 'июнь 2026'
    : expenseWorksheetMonth === '2026-05'
      ? 'май 2026'
      : expenseWorksheetMonth === '2026-04'
        ? 'апрель 2026'
        : formatMonth(`${expenseWorksheetMonth}-01`)

  return (
    <section className="payments-prototype" aria-label="Форма платежей">
      <div className="payments-prototype-topline">
        <div className="payments-prototype-search-wrap">
          <label className="payments-prototype-search">
            <Search size={18} aria-hidden="true" />
            <input
              aria-label="Поиск номера гаража или ФИО владельца"
              role="combobox"
              aria-expanded={shouldShowGarageResults}
              aria-controls={garageSearchListId}
              placeholder="Введите номер гаража или ФИО владельца"
              value={garageSearch}
              onChange={(event) => setGarageSearch(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault()
                  selectFirstGarageResult()
                }
              }}
            />
          </label>
          {shouldShowGarageResults ? (
            <div className="payments-prototype-search-results" id={garageSearchListId} role="listbox" aria-label="Найденные гаражи">
              {garageSearchLoading ? <span className="payments-prototype-search-empty" role="status">Ищем гаражи...</span> : null}
              {garageSearchError ? <span className="payments-prototype-search-empty" role="alert">{garageSearchError}</span> : null}
              {!garageSearchLoading && garageSearchResults.length > 0 ? garageSearchResults.map((garage) => (
                <label className="payments-prototype-search-option" key={garage.id} role="option" aria-selected={selectedGarageIds.includes(garage.id)}>
                  <input
                    type="checkbox"
                    aria-label={`Выбрать гараж ${garage.number}, ${garage.ownerName}`}
                    checked={selectedGarageIds.includes(garage.id)}
                    onChange={() => toggleGarageSelection(garage)}
                  />
                  <span>
                    <strong>Гараж {garage.number}</strong>
                    <small>{garage.ownerName}</small>
                  </span>
                </label>
              )) : !garageSearchLoading && !garageSearchError ? <span className="payments-prototype-search-empty">Ничего не найдено</span> : null}
            </div>
          ) : null}
          {selectedGarages.length > 0 ? (
            <div className="payments-prototype-selected-garages" aria-label="Выбранные гаражи">
              <div className="payments-prototype-selected-heading">
                <span>Выбрано: {selectedGarages.length}</span>
                <button className="ghost-button" type="button" onClick={clearGarageSelection}>Очистить</button>
              </div>
              <div className="payments-prototype-selected-list">
                {selectedGarages.map((garage) => (
                  <button
                    className={`ghost-button${garage.id === selectedGarageId ? ' is-active' : ''}`}
                    key={garage.id}
                    type="button"
                    aria-pressed={garage.id === selectedGarageId}
                    onClick={() => activateGarage(garage)}
                  >
                    Гараж {garage.number}
                  </button>
                ))}
              </div>
            </div>
          ) : null}
        </div>

        {selectedGarage ? (
          <section className="payments-prototype-garage-summary" aria-label="Параметры выбранного гаража">
            <div><span>Люди</span><strong>{selectedGarage.peopleCount}</strong></div>
            <div><span>Баланс</span><strong className={selectedGarage.balance < 0 ? 'money-expense' : undefined}>{formatPaymentPrototypeValue(Math.abs(selectedGarage.balance))}</strong></div>
            <div><span>Этажи</span><strong>{selectedGarage.floorCount}</strong></div>
            <div><span>Просроченная задолженность</span><strong className={selectedGarage.overdueDebt > 0 ? 'money-expense' : undefined}>{formatPaymentPrototypeValue(selectedGarage.overdueDebt)}</strong></div>
          </section>
        ) : null}
      </div>
      {formStateError ? <FormError>{formStateError}</FormError> : null}
      {paymentError ? <FormError>{paymentError}</FormError> : null}
      {receiptActionStatus ? <p className="form-status" role="status">{receiptActionStatus}</p> : null}
      {garageWorksheetLoadingId ? <p className="form-status" role="status">Загружаем поступления выбранного гаража...</p> : null}

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

      {!selectedGarage ? (
        <p className="empty-state" role="status">Выберите гараж через поиск, чтобы увидеть карточку, поступления, историю платежей и задолженность.</p>
      ) : activeTab === 'income' ? (
        <>
          <div className="payments-prototype-owner-row" aria-label="Выбранный гараж">
            <div><span>Гараж</span><strong>{selectedGarage.number}</strong></div>
            <div><span>Владелец</span><strong>{selectedGarage.ownerName}</strong></div>
            <div><span>Телефон</span><strong>{selectedGarage.phone}</strong></div>
            <div className="payments-prototype-actions payments-prototype-actions--stacked">
              <button className="secondary-button" type="button" aria-label="Добавить начисление гаражу" onClick={openGarageAccrualDialog}>
                <Plus size={16} aria-hidden="true" />
                <span>Добавить начисление</span>
              </button>
              <button className="secondary-button" type="button" onClick={openRegularAccrualDialog}>
                <Plus size={16} aria-hidden="true" />
                <span>Сформировать начисления</span>
              </button>
              <button className="secondary-button" type="button" onClick={openDebtTransferDialog}>
                <span>Перенести задолженность</span>
              </button>
              <button className="secondary-button" type="button" onClick={openFullPaymentDialog}>
                <span>Полная оплата</span>
              </button>
            </div>
          </div>

          <section className="payments-prototype-card payments-prototype-card--history" aria-label="История платежей гаража">
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
                    <td colSpan={6}>Загружаем историю платежей...</td>
                  </tr>
                ) : historyRows.length > 0 ? historyRows.map((row) => (
                  <tr key={row.id}>
                    <td>{row.date}</td>
                    <td>{row.time}</td>
                    <td>{formatPaymentPrototypeValue(row.amount)}</td>
                    <td>{row.purpose}</td>
                    <td>{formatPaymentPrototypeValue(row.debtAfter)}</td>
                    <td>
                      {row.operation && canWritePayments ? (
                        <div className="table-action-row">
                          <button className="icon-button" type="button" title="Изменить платеж" aria-label={`Изменить платеж ${row.purpose}`} onClick={(event) => openHistoryEdit(row, event.currentTarget)}>
                            <Pencil size={16} aria-hidden="true" />
                          </button>
                          <button className="icon-button danger-icon-button" type="button" title="Отменить платеж" aria-label={`Отменить платеж ${row.purpose}`} onClick={(event) => openHistoryCancel(row, event.currentTarget)}>
                            <Trash2 size={16} aria-hidden="true" />
                          </button>
                          {!row.operation.isCanceled ? (
                            <>
                              <button className="icon-button" type="button" title="Сформировать квитанцию" aria-label={`Сформировать квитанцию платежа ${row.purpose}`} onClick={(event) => openReceiptAction(row, 'print', event.currentTarget)}>
                                <FileText size={16} aria-hidden="true" />
                              </button>
                              <button className="icon-button danger-icon-button" type="button" title="Отменить печать квитанции" aria-label={`Отменить печать квитанции платежа ${row.purpose}`} onClick={(event) => openReceiptAction(row, 'cancel', event.currentTarget)}>
                                <Trash2 size={16} aria-hidden="true" />
                              </button>
                              <button className="icon-button" type="button" title="Напечатать копию квитанции" aria-label={`Напечатать копию квитанции платежа ${row.purpose}`} onClick={(event) => openReceiptAction(row, 'reprint', event.currentTarget)}>
                                <RotateCcw size={16} aria-hidden="true" />
                              </button>
                            </>
                          ) : null}
                        </div>
                      ) : '—'}
                    </td>
                  </tr>
                )) : (
                  <tr>
                    <td colSpan={6}>Платежей по выбранному гаражу пока нет.</td>
                  </tr>
                )}
              </tbody>
            </table>
          </section>

          <div className="payments-prototype-sheet">
            <div className="payments-prototype-period-row">
              <label>
                <span>Месяц с</span>
                <select aria-label="Месяц поступлений с" value={incomeWorksheetMonthFrom} onChange={(event) => handleIncomeWorksheetMonthFromChange(event.target.value)}>
                  {incomeWorksheetMonthOptions.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
              </label>
              <label>
                <span>Месяц по</span>
                <select aria-label="Месяц поступлений по" value={incomeWorksheetMonthTo} onChange={(event) => handleIncomeWorksheetMonthToChange(event.target.value)}>
                  {incomeWorksheetMonthOptions.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
              </label>
              <button className="link-button" type="button" onClick={setCurrentIncomeWorksheetMonth}>Текущий</button>
            </div>
            {garageWorksheetSummary ? (
              <div className="payments-prototype-period-summary" aria-label="Итоги периода поступлений">
                <div>
                  <span>Долг на начало</span>
                  <strong className={garageWorksheetSummary.openingDebt > 0 ? 'money-expense' : undefined}>{formatPaymentPrototypeValue(garageWorksheetSummary.openingDebt)}</strong>
                </div>
                <div>
                  <span>Начислено</span>
                  <strong>{formatPaymentPrototypeValue(garageWorksheetSummary.accrualTotal)}</strong>
                </div>
                <div>
                  <span>Оплачено</span>
                  <strong>{formatPaymentPrototypeValue(garageWorksheetSummary.incomeTotal)}</strong>
                </div>
                <div>
                  <span>Долг на конец</span>
                  <strong className={garageWorksheetSummary.closingDebt > 0 ? 'money-expense' : undefined}>{formatPaymentPrototypeValue(garageWorksheetSummary.closingDebt)}</strong>
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
                    <th scope="col">Задолженность</th>
                  </tr>
                </thead>
                <tbody>
                  {groupedGarageRows.map((group) => {
                    const groupPayable = group.rows.reduce((sum, row) => sum + row.payable, 0)
                    const groupPaid = group.rows.reduce((sum, row) => sum + row.paid, 0)
                    const groupDebt = group.rows.reduce((sum, row) => sum + row.debt, 0)
                    return (
                      <Fragment key={group.month}>
                        <tr className="payments-prototype-month-total">
                          <td>{group.monthLabel}</td>
                          <td>ИТОГО</td>
                          <td />
                          <td />
                          <td>{formatPaymentPrototypeValue(groupPayable)}</td>
                          <td />
                          <td>{formatPaymentPrototypeValue(groupPaid)}</td>
                          <td className={groupDebt > 0 ? 'money-expense' : undefined}>{formatPaymentPrototypeValue(groupDebt)}</td>
                        </tr>
                        {group.rows.map((row) => (
                          <tr key={row.id}>
                            <td />
                            <td>{row.service}</td>
                            <td className={row.meterRequired && row.meter === null ? 'payments-prototype-required-cell' : undefined}>{formatPaymentPrototypeValue(row.meter ?? '')}</td>
                            <td>{formatPaymentPrototypeValue(row.difference ?? '')}</td>
                            <td>{formatPaymentPrototypeValue(row.payable)}</td>
                            <td>
                              <input
                                className="payments-prototype-payment-input"
                                aria-label={`Платеж ${row.service} ${row.monthLabel}`}
                                inputMode="decimal"
                                disabled={savingPaymentRowId === row.id}
                                value={row.paymentDraft}
                                onChange={(event) => handlePaymentDraftChange(row.id, event.target.value)}
                                onKeyDown={(event) => {
                                  if (event.key === 'Enter') {
                                    event.preventDefault()
                                    void commitGaragePayment(row)
                                  }
                                }}
                              />
                            </td>
                            <td>{formatPaymentPrototypeValue(row.paid)}</td>
                            <td className={row.debt > 0 ? 'money-expense' : undefined}>{formatPaymentPrototypeValue(row.debt)}</td>
                          </tr>
                        ))}
                      </Fragment>
                    )
                  })}
                  {groupedGarageRows.length === 0 ? (
                    <tr>
                      <td colSpan={8}>{garageWorksheetLoadingId === selectedGarage.id ? 'Загружаем поступления...' : 'Начислений и поступлений за выбранный период пока нет.'}</td>
                    </tr>
                  ) : null}
                  <tr className="payments-prototype-total-row">
                    <td />
                    <td>ИТОГО</td>
                    <td />
                    <td />
                    <td>{formatPaymentPrototypeValue(paymentTotal)}</td>
                    <td />
                    <td>{formatPaymentPrototypeValue(paidTotal)}</td>
                    <td className={debtTotal > 0 ? 'money-expense' : undefined}>{formatPaymentPrototypeValue(debtTotal)}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </>
      ) : (
        <>
          <div className="payments-prototype-actions payments-prototype-actions--sheet">
            <button className="secondary-button" type="button" onClick={(event) => openSupplierAccrualDialog(event)}>
              <Plus size={16} aria-hidden="true" />
              <span>Добавить начисление</span>
            </button>
            <button className="secondary-button" type="button" onClick={(event) => openExpenseDialog(event)}>
              <Plus size={16} aria-hidden="true" />
              <span>Добавить выплату</span>
            </button>
            <button className="secondary-button" type="button" onClick={(event) => openSalaryDialog(event)}>
              <Plus size={16} aria-hidden="true" />
              <span>Начислить зарплату</span>
            </button>
          </div>

          <div className="payments-prototype-sheet">
            <div className="payments-prototype-period-row">
              <label>
                <span>Месяц</span>
                <select aria-label="Месяц выплат" value={expenseWorksheetMonth} onChange={(event) => handleExpenseWorksheetMonthChange(event.target.value)}>
                  <option value="2026-06">июнь 2026</option>
                  <option value="2026-05">май 2026</option>
                  <option value="2026-04">апрель 2026</option>
                </select>
              </label>
            </div>
            {expenseWorksheetLoading ? <p className="form-status" role="status">Загружаем форму выплат...</p> : null}
            <div className="payments-prototype-table-scroll">
              <table className="payments-prototype-table" aria-label={`Форма выплат за ${expenseMonthLabel}`}>
                <thead>
                  <tr>
                    <th scope="col">Поставщик</th>
                    <th scope="col">Услуга</th>
                    <th scope="col">Стоимость</th>
                    <th scope="col">Оплачено</th>
                    <th scope="col">Остаток</th>
                    <th scope="col">Собрано</th>
                    <th scope="col">Разница</th>
                    <th scope="col">Действие</th>
                  </tr>
                </thead>
                <tbody>
                  {expenseRows.map((row, index) => {
                    const supplier = row.counterparty ?? ''
                    const isStaffPaymentRow = row.rowKind === 'staff'
                    const suggestedAmount = typeof row.balance === 'number' && row.balance > 0 ? row.balance : typeof row.cost === 'number' ? row.cost : undefined
                    return (
                      <tr key={`${row.item}-${index}`}>
                        <td>{supplier}</td>
                        <td>{row.item}</td>
                        <td className={row.cost ? 'money-income' : undefined}>{formatPaymentPrototypeValue(row.cost)}</td>
                        <td>{formatPaymentPrototypeValue(row.paid)}</td>
                        <td>{formatPaymentPrototypeValue(row.balance)}</td>
                        <td>{formatPaymentPrototypeValue(row.collected)}</td>
                        <td className={typeof row.difference === 'number' ? row.difference >= 0 ? 'money-income' : 'money-expense' : undefined}>
                          {formatPaymentPrototypeValue(row.difference)}
                        </td>
                        <td>
                          {row.action && row.item !== 'Авансовые выплаты' && row.item !== 'Выплата без чека' ? (
                            <button className="link-button" type="button" onClick={(event) => {
                              if (isStaffPaymentRow) {
                                openStaffPaymentDialog(event, { staffMemberName: supplier, amount: suggestedAmount, rowIndex: index })
                                return
                              }

                              openExpenseDialog(event, { expenseTypeName: row.item, amount: suggestedAmount, rowIndex: index })
                            }} aria-label={isStaffPaymentRow ? `Оплатить сотрудника ${supplier}` : `Оплатить ${row.item}`}>
                              Оплатить
                            </button>
                          ) : null}
                        </td>
                      </tr>
                    )
                  })}
                  {expenseRows.length === 0 ? (
                    <tr>
                      <td colSpan={8}>{expenseWorksheetLoading ? 'Загружаем форму выплат...' : 'Начислений и выплат за выбранный месяц пока нет.'}</td>
                    </tr>
                  ) : null}
                  <tr className="payments-prototype-total-row">
                    <td>ИТОГО</td>
                    <td />
                    <td>{formatPaymentPrototypeValue(expenseAccrualTotal)}</td>
                    <td>{formatPaymentPrototypeValue(expensePaidTotal)}</td>
                    <td>{formatPaymentPrototypeValue(expenseBalanceTotal)}</td>
                    <td>{formatPaymentPrototypeValue(expenseCollectedTotal)}</td>
                    <td className={expenseDifferenceTotal >= 0 ? 'money-income' : 'money-expense'}>{formatPaymentPrototypeValue(expenseDifferenceTotal)}</td>
                    <td />
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
              <strong>{formatPaymentPrototypeValue(expenseCashTotal)}</strong>
            </div>
            <div>
              <span>ИТОГО</span>
              <strong className={expenseCollectedTotal >= 0 ? 'money-income' : 'money-expense'}>{formatPaymentPrototypeValue(expenseCollectedTotal)}</strong>
            </div>
            <button className="secondary-button" type="button" onClick={(event) => openDialogFromButton(event, 'bank')}>
              Сдать кассу в банк
            </button>
          </div>
        </>
      )}
      {fullPaymentDialogOpen ? (
        <FullPaymentPrototypeDialog
          periodOptions={fullPaymentPeriodOptions}
          onClose={closeFullPaymentDialog}
          onSubmit={commitFullGaragePayment}
        />
      ) : null}
      {debtTransferDialogOpen ? (
        <DebtTransferPrototypeDialog
          sourceOptions={debtTransferSourceOptions}
          targetOptions={debtTransferTargetMonths}
          onClose={closeDebtTransferDialog}
          onSubmit={commitDebtTransfer}
        />
      ) : null}
      {garageAccrualDialogOpen ? (
        <GarageAccrualPrototypeDialog
          incomeTypes={incomeTypes.filter((incomeType) => !incomeType.isArchived)}
          onClose={closeGarageAccrualDialog}
          onSubmit={commitGarageAccrual}
        />
      ) : null}
      {regularAccrualDialogOpen ? (
        <RegularAccrualPrototypeDialog
          onClose={closeRegularAccrualDialog}
          onSubmit={commitRegularAccruals}
        />
      ) : null}
      {expenseDialogPreset ? (
        <NewExpensePrototypeDialog
          expenseTypes={expenseTypes.filter((expenseType) => !expenseType.isArchived)}
          preset={expenseDialogPreset}
          suppliers={suppliers.filter((supplier) => !supplier.isArchived)}
          onClose={closeExpenseDialog}
          onSubmit={commitExpensePayment}
        />
      ) : null}
      {staffPaymentDialogPreset ? (
        <StaffPaymentPrototypeDialog
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
      {salaryDialogOpen ? (
        <SalaryAccrualPrototypeDialog
          supplierGroups={supplierGroups.filter((group) => !group.isArchived)}
          onClose={closeSalaryDialog}
          onSubmit={commitSalaryAccruals}
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
      {receiptAction ? (
        <GaragePaymentReceiptActionDialog
          state={receiptAction}
          saving={receiptActionSaving}
          onChange={(patch) => setReceiptAction((value) => value ? { ...value, ...patch, error: null } : value)}
          onClose={closeReceiptActionDialog}
          onConfirm={confirmReceiptAction}
        />
      ) : null}
    </section>
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

    const amount = Number(state.amount.trim().replace(',', '.'))
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
            <input aria-label="Сумма изменяемого платежа" inputMode="decimal" value={state.amount} onChange={(event) => onChange({ amount: event.target.value })} disabled={saving} />
          </FormField>
          <FormField label="Дата">
            <input aria-label="Дата изменяемого платежа" type="date" value={state.operationDate} onChange={(event) => onChange({ operationDate: event.target.value })} disabled={saving} />
          </FormField>
          <FormField label="Месяц">
            <input aria-label="Месяц изменяемого платежа" type="month" value={state.accountingMonth} onChange={(event) => onChange({ accountingMonth: event.target.value })} disabled={saving} />
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

function GaragePaymentReceiptActionDialog({
  state,
  saving,
  onChange,
  onClose,
  onConfirm,
}: {
  state: GaragePaymentReceiptActionState
  saving: boolean
  onChange: (patch: Partial<Omit<GaragePaymentReceiptActionState, 'row' | 'action'>>) => void
  onClose: () => void
  onConfirm: () => void
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const labels = receiptPrintingActionLabels[state.action]
  const needsReason = state.action !== 'print'
  useEscapeKey(!saving, onClose)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={() => {
      if (!saving) {
        onClose()
      }
    }}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog payments-prototype-dialog--wide" role="dialog" aria-modal="true" aria-labelledby="garage-payment-receipt-action-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <p className="eyebrow">Квитанция платежа</p>
            <h3 id="garage-payment-receipt-action-title">{labels.title}</h3>
            <p>{state.row.purpose} · {formatPaymentPrototypeValue(state.row.amount)}</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть действие квитанции" onClick={onClose} disabled={saving}>
            <X size={18} aria-hidden="true" />
          </button>
        </div>
        <div className="dictionary-modal-form payments-prototype-modal-form">
          <p className="confirmation-text">{labels.description}</p>
          {state.row.operation?.documentNumber ? <p className="form-hint">Документ: {state.row.operation.documentNumber}</p> : null}
          {needsReason ? (
            <FormField label="Причина">
              <textarea aria-label="Причина действия с квитанцией" rows={4} value={state.reason} onChange={(event) => onChange({ reason: event.target.value })} disabled={saving} />
            </FormField>
          ) : null}
          {state.error ? <FormError>{state.error}</FormError> : null}
          <div className="detail-dialog-actions">
            <button ref={cancelRef} className="ghost-button" type="button" onClick={onClose} disabled={saving}>Отмена</button>
            <button className={`secondary-button${state.action === 'cancel' ? ' danger-button' : ''}`} type="button" onClick={onConfirm} disabled={saving}>
              {state.action === 'reprint' ? <RotateCcw size={16} aria-hidden="true" /> : state.action === 'cancel' ? <Trash2 size={16} aria-hidden="true" /> : <FileText size={16} aria-hidden="true" />}
              <span>{saving ? labels.saving : labels.button}</span>
            </button>
          </div>
        </div>
      </section>
    </div>
  )
}

function BankDepositPrototypeDialog({
  auth,
  fundsClient,
  onClose,
}: {
  auth: AuthResponse
  fundsClient: FundsClient
  onClose: () => void
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const [funds, setFunds] = useState<FundDto[]>([])
  const [fundId, setFundId] = useState('')
  const [operationDate, setOperationDate] = useState(getLocalDateInputValue())
  const [amount, setAmount] = useState('')
  const [comment, setComment] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const availableToDistribute = funds.length > 0 ? funds[0].availableToDistribute : null
  useEscapeKey(true, onClose)

  useEffect(() => {
    let active = true

    async function loadFunds() {
      setLoading(true)
      setError(null)
      try {
        const loadedFunds = await fundsClient.getFunds(auth.accessToken)
        if (!active) {
          return
        }

        const allowedFunds = loadedFunds.filter((fund) => fund.allowOperations)
        setFunds(allowedFunds)
        setFundId((current) => current || allowedFunds[0]?.id || '')
      } catch (loadError) {
        if (active) {
          setError(loadError instanceof Error ? loadError.message : 'Не удалось загрузить фонды.')
        }
      } finally {
        if (active) {
          setLoading(false)
        }
      }
    }

    void loadFunds()

    return () => {
      active = false
    }
  }, [auth.accessToken, fundsClient])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedAmount = Number(amount.trim().replace(/\s/g, '').replace(',', '.'))
    if (!fundId) {
      setError('Выберите фонд для сдачи кассы в банк.')
      return
    }
    if (!operationDate) {
      setError('Укажите дату сдачи кассы.')
      return
    }
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError('Укажите сумму сдачи больше нуля.')
      return
    }
    if (availableToDistribute !== null && parsedAmount > availableToDistribute) {
      setError(`Сумма сдачи не может превышать доступную к распределению сумму ${formatMoney(availableToDistribute)} руб.`)
      return
    }

    const reason = comment.trim()
      ? `Сдача кассы в банк ${operationDate}: ${comment.trim()}`
      : `Сдача кассы в банк ${operationDate}`

    setSaving(true)
    setError(null)
    try {
      await fundsClient.createOperation(auth.accessToken, fundId, {
        operationKind: 'deposit',
        amount: parsedAmount,
        reason,
      })
      onClose()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Не удалось сохранить сдачу кассы в банк.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog payments-prototype-dialog--wide" role="dialog" aria-modal="true" aria-labelledby="bank-deposit-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="bank-deposit-title">Учет суммы на счете в банке</h3>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть учет суммы в банке" onClick={onClose}>
            <X size={18} />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={handleSubmit}>
          <FormField label="Фонд">
            <select aria-label="Фонд для сдачи кассы" value={fundId} onChange={(event) => {
              setFundId(event.target.value)
              setError(null)
            }} disabled={loading || saving}>
              {funds.length > 0 ? funds.map((fund) => (
                <option key={fund.id} value={fund.id}>{fund.name}</option>
              )) : <option value="">Нет доступных фондов</option>}
            </select>
          </FormField>
          <FormField label="Сумма">
            <input aria-label="Сумма в банке" inputMode="decimal" value={amount} onChange={(event) => {
              setAmount(event.target.value)
              setError(null)
            }} disabled={saving} />
          </FormField>
          {availableToDistribute !== null ? <p className="form-hint">Доступно к распределению: {formatMoney(availableToDistribute)} руб.</p> : null}
          <FormField label="Дата">
            <input aria-label="Дата учета суммы в банке" type="date" value={operationDate} onChange={(event) => {
              setOperationDate(event.target.value)
              setError(null)
            }} disabled={saving} />
          </FormField>
          <FormField label="Комментарий">
            <textarea aria-label="Комментарий к сумме в банке" rows={5} value={comment} onChange={(event) => setComment(event.target.value)} disabled={saving} />
          </FormField>
          {loading ? <p className="form-hint" role="status">Загружаем фонды...</p> : null}
          {error ? <FormError>{error}</FormError> : null}
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit" disabled={loading || saving}>{saving ? 'Сохраняем...' : 'Ок'}</button>
            <button ref={cancelRef} className="secondary-button" type="button" onClick={onClose} disabled={saving}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function NewExpensePrototypeDialog({
  expenseTypes,
  preset,
  suppliers,
  onClose,
  onSubmit,
}: {
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
  const [supplierId, setSupplierId] = useState(suppliers[0]?.id ?? '')
  const [expenseTypeId, setExpenseTypeId] = useState(presetExpenseType?.id ?? expenseTypes[0]?.id ?? '')
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
    const parsedAmount = Number(amount.trim().replace(/\s/g, '').replace(',', '.'))
    if (!supplierId) {
      setError('Выберите поставщика из справочника.')
      return
    }
    if (!expenseTypeId) {
      setError('Выберите вид выплаты из справочника.')
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

    setSaving(true)
    setError(null)
    try {
      const submitError = await onSubmit({
        supplierId,
        expenseTypeId,
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
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog" role="dialog" aria-modal="true" aria-labelledby="new-expense-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="new-expense-title">Новая выплата</h3>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть новую выплату" onClick={onClose}>
            <X size={18} />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={handleSubmit}>
          <FormField label="Поставщик">
            <select aria-label="Поставщик выплаты" value={supplierId} onChange={(event) => {
              setSupplierId(event.target.value)
              setError(null)
            }}>
              {suppliers.length > 0 ? suppliers.map((supplier) => (
                <option key={supplier.id} value={supplier.id}>{supplier.name}</option>
              )) : <option value="">Нет поставщиков</option>}
            </select>
          </FormField>
          <FormField label="Вид выплаты">
            <select aria-label="Вид выплаты" value={expenseTypeId} onChange={(event) => {
              setExpenseTypeId(event.target.value)
              setError(null)
            }}>
              {expenseTypes.length > 0 ? expenseTypes.map((expenseType) => (
                <option key={expenseType.id} value={expenseType.id}>{expenseType.name}</option>
              )) : <option value="">Нет видов выплат</option>}
            </select>
          </FormField>
          <FormField label="Дата">
            <input aria-label="Дата выплаты" type="date" value={operationDate} onChange={(event) => {
              setOperationDate(event.target.value)
              setError(null)
            }} />
          </FormField>
          <FormField label="Месяц">
            <input aria-label="Месяц выплаты" type="month" value={accountingMonth} onChange={(event) => {
              setAccountingMonth(event.target.value)
              setError(null)
            }} />
          </FormField>
          <FormField label="Сумма">
            <input aria-label="Сумма выплаты" inputMode="decimal" value={amount} onChange={(event) => {
              setAmount(event.target.value)
              setError(null)
            }} />
          </FormField>
          <FormField label="Документ">
            <input aria-label="Документ выплаты" value={documentNumber} onChange={(event) => setDocumentNumber(event.target.value)} />
          </FormField>
          <FormField label="Комментарий">
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
  preset,
  staffMembers,
  onClose,
  onSubmit,
}: {
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
    const parsedAmount = Number(amount.trim().replace(/\s/g, '').replace(',', '.'))
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
          <FormField label="Сотрудник">
            <select aria-label="Сотрудник выплаты" value={staffMemberId} onChange={(event) => {
              setStaffMemberId(event.target.value)
              setError(null)
            }}>
              {staffMembers.length > 0 ? staffMembers.map((member) => (
                <option key={member.id} value={member.id}>{member.fullName} · {member.departmentName}</option>
              )) : <option value="">Нет сотрудников</option>}
            </select>
          </FormField>
          <FormField label="Дата">
            <input aria-label="Дата выплаты сотруднику" type="date" value={operationDate} onChange={(event) => {
              setOperationDate(event.target.value)
              setError(null)
            }} />
          </FormField>
          <FormField label="Месяц">
            <input aria-label="Месяц выплаты сотруднику" type="month" value={accountingMonth} onChange={(event) => {
              setAccountingMonth(event.target.value)
              setError(null)
            }} />
          </FormField>
          <FormField label="Сумма">
            <input aria-label="Сумма выплаты сотруднику" inputMode="decimal" value={amount} onChange={(event) => {
              setAmount(event.target.value)
              setError(null)
            }} />
          </FormField>
          <FormField label="Документ">
            <input aria-label="Документ выплаты сотруднику" value={documentNumber} onChange={(event) => setDocumentNumber(event.target.value)} />
          </FormField>
          <FormField label="Комментарий">
            <textarea aria-label="Комментарий к выплате сотруднику" rows={4} value={comment} onChange={(event) => setComment(event.target.value)} />
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
  const [supplierId, setSupplierId] = useState(suppliers[0]?.id ?? '')
  const [expenseTypeId, setExpenseTypeId] = useState(expenseTypes[0]?.id ?? '')
  const [accountingMonth, setAccountingMonth] = useState(getLocalDateInputValue().slice(0, 7))
  const [amount, setAmount] = useState('')
  const [documentNumber, setDocumentNumber] = useState('')
  const [comment, setComment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEscapeKey(true, onClose)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedAmount = Number(amount.trim().replace(/\s/g, '').replace(',', '.'))
    if (!supplierId) {
      setError('Выберите поставщика из справочника.')
      return
    }
    if (!expenseTypeId) {
      setError('Выберите вид начисления из справочника выплат.')
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
            <select aria-label="Поставщик начисления" value={supplierId} onChange={(event) => {
              setSupplierId(event.target.value)
              setError(null)
            }}>
              {suppliers.length > 0 ? suppliers.map((supplier) => (
                <option key={supplier.id} value={supplier.id}>{supplier.name}</option>
              )) : <option value="">Нет поставщиков</option>}
            </select>
          </FormField>
          <FormField label="Вид начисления">
            <select aria-label="Вид начисления поставщику" value={expenseTypeId} onChange={(event) => {
              setExpenseTypeId(event.target.value)
              setError(null)
            }}>
              {expenseTypes.length > 0 ? expenseTypes.map((expenseType) => (
                <option key={expenseType.id} value={expenseType.id}>{expenseType.name}</option>
              )) : <option value="">Нет видов выплат</option>}
            </select>
          </FormField>
          <FormField label="Месяц">
            <input aria-label="Месяц начисления поставщику" type="month" value={accountingMonth} onChange={(event) => {
              setAccountingMonth(event.target.value)
              setError(null)
            }} />
          </FormField>
          <FormField label="Сумма">
            <input aria-label="Сумма начисления поставщику" inputMode="decimal" value={amount} onChange={(event) => {
              setAmount(event.target.value)
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

function SalaryAccrualPrototypeDialog({
  supplierGroups,
  onClose,
  onSubmit,
}: {
  supplierGroups: SupplierGroupDto[]
  onClose: () => void
  onSubmit: (request: SalaryAccrualPrototypeSubmitRequest) => Promise<string | null>
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const [supplierGroupId, setSupplierGroupId] = useState(supplierGroups[0]?.id ?? '')
  const [accountingMonth, setAccountingMonth] = useState(getLocalDateInputValue().slice(0, 7))
  const [amount, setAmount] = useState('')
  const [documentNumber, setDocumentNumber] = useState('')
  const [comment, setComment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEscapeKey(true, onClose)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedAmount = Number(amount.trim().replace(/\s/g, '').replace(',', '.'))
    if (!supplierGroupId) {
      setError('Выберите группу сотрудников или поставщиков.')
      return
    }
    if (!/^\d{4}-\d{2}$/.test(accountingMonth)) {
      setError('Укажите месяц зарплаты.')
      return
    }
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError('Укажите сумму зарплаты больше нуля.')
      return
    }

    setSaving(true)
    setError(null)
    try {
      const submitError = await onSubmit({
        supplierGroupId,
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
      setError(submitError instanceof Error ? submitError.message : 'Не удалось начислить зарплату. Повторите попытку позже.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog payments-prototype-dialog--wide" role="dialog" aria-modal="true" aria-labelledby="salary-accrual-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="salary-accrual-title">Начислить зарплату</h3>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть начисление зарплаты" onClick={onClose}>
            <X size={18} />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={handleSubmit}>
          <FormField label="Группа">
            <select aria-label="Группа для начисления зарплаты" value={supplierGroupId} onChange={(event) => {
              setSupplierGroupId(event.target.value)
              setError(null)
            }}>
              {supplierGroups.length > 0 ? supplierGroups.map((group) => (
                <option key={group.id} value={group.id}>{group.name}</option>
              )) : <option value="">Нет групп</option>}
            </select>
          </FormField>
          <FormField label="Месяц">
            <input aria-label="Месяц начисления зарплаты" type="month" value={accountingMonth} onChange={(event) => {
              setAccountingMonth(event.target.value)
              setError(null)
            }} />
          </FormField>
          <FormField label="Сумма">
            <input aria-label="Сумма начисления зарплаты" inputMode="decimal" value={amount} onChange={(event) => {
              setAmount(event.target.value)
              setError(null)
            }} />
          </FormField>
          <FormField label="Документ">
            <input aria-label="Документ начисления зарплаты" value={documentNumber} onChange={(event) => setDocumentNumber(event.target.value)} />
          </FormField>
          <FormField label="Комментарий">
            <textarea aria-label="Комментарий начисления зарплаты" rows={5} value={comment} onChange={(event) => setComment(event.target.value)} />
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

function RegularAccrualPrototypeDialog({
  onClose,
  onSubmit,
}: {
  onClose: () => void
  onSubmit: (request: RegularAccrualPrototypeSubmitRequest) => Promise<string | null>
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const [accountingMonth, setAccountingMonth] = useState(getLocalDateInputValue().slice(0, 7))
  const [comment, setComment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEscapeKey(true, onClose)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!/^\d{4}-\d{2}$/.test(accountingMonth)) {
      setError('Укажите месяц начисления.')
      return
    }

    setSaving(true)
    setError(null)
    try {
      const submitError = await onSubmit({
        accountingMonth: `${accountingMonth}-01`,
        comment,
      })
      if (submitError) {
        setError(submitError)
        return
      }
      onClose()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Не удалось сформировать начисления. Повторите попытку позже.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog payments-prototype-dialog--wide" role="dialog" aria-modal="true" aria-labelledby="regular-accrual-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="regular-accrual-title">Сформировать начисления</h3>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть формирование начислений" onClick={onClose}>
            <X size={18} />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={handleSubmit}>
          <FormField label="Месяц">
            <input aria-label="Месяц регулярного начисления" type="month" value={accountingMonth} onChange={(event) => {
              setAccountingMonth(event.target.value)
              setError(null)
            }} />
          </FormField>
          <FormField label="Комментарий">
            <textarea aria-label="Комментарий регулярного начисления" rows={5} value={comment} onChange={(event) => setComment(event.target.value)} />
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

function GarageAccrualPrototypeDialog({
  incomeTypes,
  onClose,
  onSubmit,
}: {
  incomeTypes: AccountingTypeDto[]
  onClose: () => void
  onSubmit: (request: GarageAccrualPrototypeSubmitRequest) => Promise<string | null>
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const [incomeTypeId, setIncomeTypeId] = useState(incomeTypes[0]?.id ?? '')
  const [accountingMonth, setAccountingMonth] = useState(getLocalDateInputValue().slice(0, 7))
  const [amount, setAmount] = useState('')
  const [comment, setComment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEscapeKey(true, onClose)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedAmount = Number(amount.trim().replace(/\s/g, '').replace(',', '.'))
    if (!incomeTypeId) {
      setError('Выберите вид начисления из справочника поступлений.')
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
        incomeTypeId,
        accountingMonth: `${accountingMonth}-01`,
        amount: parsedAmount,
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
          <FormField label="Вид начисления">
            <select aria-label="Вид начисления гаража" value={incomeTypeId} onChange={(event) => {
              setIncomeTypeId(event.target.value)
              setError(null)
            }}>
              {incomeTypes.length > 0 ? incomeTypes.map((incomeType) => (
                <option key={incomeType.id} value={incomeType.id}>{incomeType.name}</option>
              )) : <option value="">Нет видов поступлений</option>}
            </select>
          </FormField>
          <FormField label="Сумма">
            <input aria-label="Сумма начисления гаража" inputMode="decimal" value={amount} onChange={(event) => {
              setAmount(event.target.value)
              setError(null)
            }} />
          </FormField>
          <FormField label="Месяц">
            <input aria-label="Месяц начисления гаража" type="month" value={accountingMonth} onChange={(event) => {
              setAccountingMonth(event.target.value)
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

function DebtTransferPrototypeDialog({
  sourceOptions,
  targetOptions,
  onClose,
  onSubmit,
}: {
  sourceOptions: DebtTransferPrototypePeriodOption[]
  targetOptions: Array<{ value: string; label: string }>
  onClose: () => void
  onSubmit: (request: DebtTransferPrototypeSubmitRequest) => Promise<string | null>
}) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  const initialSource = sourceOptions[0] ?? null
  const [sourceMonth, setSourceMonth] = useState(initialSource?.value ?? '')
  const [targetMonth, setTargetMonth] = useState(initialSource?.defaultTargetMonth ?? targetOptions[0]?.value ?? '')
  const [amount, setAmount] = useState(() => String(initialSource?.debt ?? 0))
  const [comment, setComment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(sourceOptions.length === 0 ? 'Нет задолженности для переноса.' : null)
  useEscapeKey(true, onClose)

  function handleSourceChange(value: string) {
    const nextSource = sourceOptions.find((option) => option.value === value) ?? null
    setSourceMonth(value)
    setAmount(String(nextSource?.debt ?? 0))
    setTargetMonth(nextSource?.defaultTargetMonth ?? targetOptions.find((option) => option.value !== value)?.value ?? '')
    setError(null)
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedAmount = Number(amount.trim().replace(/\s/g, '').replace(',', '.'))
    if (!sourceMonth || !targetMonth) {
      setError('Выберите исходный и целевой месяц переноса.')
      return
    }

    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError('Укажите сумму переноса больше нуля.')
      return
    }

    setSaving(true)
    setError(null)
    try {
      const submitError = await onSubmit({ sourceMonth, targetMonth, amount: parsedAmount, comment })
      if (submitError) {
        setError(submitError)
        return
      }
      onClose()
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Не удалось перенести задолженность. Повторите попытку позже.')
    } finally {
      setSaving(false)
    }
  }

  const availableTargets = targetOptions.filter((option) => option.value !== sourceMonth)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog" role="dialog" aria-modal="true" aria-labelledby="debt-transfer-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="debt-transfer-title">Перенести задолженность</h3>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть перенос задолженности" onClick={onClose}>
            <X size={18} aria-hidden="true" />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={handleSubmit}>
          <div className="form-grid two-columns">
            <FormField label="Месяц с">
              <select aria-label="Исходный месяц переноса задолженности" value={sourceMonth} onChange={(event) => handleSourceChange(event.target.value)} disabled={sourceOptions.length === 0}>
                {sourceOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label} · долг {formatPaymentPrototypeValue(option.debt)}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Месяц по">
              <select aria-label="Целевой месяц переноса задолженности" value={targetMonth} onChange={(event) => {
                setTargetMonth(event.target.value)
                setError(null)
              }} disabled={availableTargets.length === 0}>
                {availableTargets.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </FormField>
          </div>
          <FormField label="Сумма">
            <input aria-label="Сумма переноса задолженности" inputMode="decimal" value={amount} onChange={(event) => {
              setAmount(event.target.value)
              setError(null)
            }} />
          </FormField>
          <FormField label="Комментарий">
            <textarea aria-label="Комментарий к переносу задолженности" rows={4} value={comment} onChange={(event) => setComment(event.target.value)} />
          </FormField>
          {error ? <FormError>{error}</FormError> : null}
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit" disabled={saving || sourceOptions.length === 0}>{saving ? 'Сохраняем...' : 'Принять'}</button>
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
  const [amount, setAmount] = useState(() => String(periodOptions[0]?.debt ?? 0))
  const [comment, setComment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEscapeKey(true, onClose)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedAmount = Number(amount.trim().replace(/\s/g, '').replace(',', '.'))
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError('Укажите сумму полной оплаты больше нуля.')
      return
    }

    setSaving(true)
    setError(null)
    try {
      const submitError = await onSubmit({ period, amount: parsedAmount, comment })
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
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog" role="dialog" aria-modal="true" aria-labelledby="full-payment-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="full-payment-title">Полная оплата</h3>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть полную оплату" onClick={onClose}>
            <X size={18} aria-hidden="true" />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={handleSubmit}>
          <FormField label="Период">
            <select aria-label="Период полной оплаты" value={period} onChange={(event) => {
              const nextPeriod = event.target.value
              setPeriod(nextPeriod)
              setAmount(String(periodOptions.find((option) => option.value === nextPeriod)?.debt ?? 0))
              setError(null)
            }}>
              {periodOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Сумма">
            <input aria-label="Сумма полной оплаты" inputMode="decimal" value={amount} readOnly />
          </FormField>
          <FormField label="Комментарий">
            <textarea aria-label="Комментарий к полной оплате" rows={4} value={comment} onChange={(event) => setComment(event.target.value)} />
          </FormField>
          {error ? <FormError>{error}</FormError> : null}
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit" disabled={saving}>{saving ? 'Сохраняем...' : 'Принять'}</button>
            <button ref={cancelRef} className="secondary-button" type="button" onClick={onClose} disabled={saving}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}
