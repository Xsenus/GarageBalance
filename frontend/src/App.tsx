import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent, MouseEvent, ReactNode } from 'react'
import {
  Bell,
  BookOpenCheck,
  CircleDollarSign,
  DatabaseZap,
  FileText,
  FileSpreadsheet,
  Gauge,
  LockKeyhole,
  LogOut,
  Plus,
  Save,
  Search,
  ShieldCheck,
  Trash2,
  UsersRound,
  WalletCards,
  X,
} from 'lucide-react'
import { authApi } from './services/authApi'
import type { AuthClient, AuthResponse, CurrentUserDto } from './services/authApi'
import { auditApi } from './services/auditApi'
import type { AuditClient, AuditEventDto } from './services/auditApi'
import { dictionariesApi } from './services/dictionariesApi'
import type { AccountingTypeDto, DictionaryClient, GarageDto, OwnerDto, PagedResult, SupplierDto, SupplierGroupDto, TariffDto, UpsertAccountingTypeRequest, UpsertGarageRequest, UpsertOwnerRequest, UpsertSupplierGroupRequest, UpsertSupplierRequest, UpsertTariffRequest } from './services/dictionariesApi'
import { financeApi } from './services/financeApi'
import type { AccrualDto, CreateAccrualRequest, CreateExpenseOperationRequest, CreateIncomeOperationRequest, CreateMeterReadingRequest, CreateSupplierAccrualRequest, FinanceClient, FinancePagedResult, FinanceSummaryDto, FinancialOperationDto, GarageBalanceHistoryDto, GenerateRegularAccrualsRequest, GenerateSupplierGroupSalaryAccrualsRequest, MeterReadingDto, MissingMeterReadingDto, PaymentAllocationDto, SupplierAccrualDto } from './services/financeApi'
import { importApi } from './services/importApi'
import type { AccessImportCheckDto, AccessImportQuarantineItemDto, AccessImportRunDto, AccessImportRunLogEntryDto, ImportClient } from './services/importApi'
import { reportsApi } from './services/reportsApi'
import type { ConsolidatedReportDto, ExpenseReportDto, IncomeReportDto, ReportClient } from './services/reportsApi'
import { releasesApi } from './services/releasesApi'
import type { AppReleaseDto, ReleaseClient } from './services/releasesApi'
import { usersApi } from './services/usersApi'
import type { CreateManagedUserRequest, ManagedRoleDto, ManagedUserDto, PagedManagedUsersDto, UpdateManagedUserRequest, UserManagementClient } from './services/usersApi'
import './App.css'

type AppProps = {
  authClient?: AuthClient
  auditClient?: AuditClient
  dictionaryClient?: DictionaryClient
  financeClient?: FinanceClient
  importClient?: ImportClient
  reportClient?: ReportClient
  releaseClient?: ReleaseClient
  userClient?: UserManagementClient
}

function FormError({ children }: { children: ReactNode }) {
  return (
    <div className="form-error" role="alert">
      {children}
    </div>
  )
}

function FormValidationSummary({ title, items }: { title: string; items: string[] }) {
  if (items.length === 0) {
    return null
  }

  return (
    <div className="form-error validation-summary" role="alert" aria-label={title}>
      <strong>{title}</strong>
      <ul>
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  )
}

function useEscapeKey(enabled: boolean, onEscape: () => void) {
  useEffect(() => {
    if (!enabled) {
      return undefined
    }

    function handleKeyDown(event: globalThis.KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault()
        onEscape()
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [enabled, onEscape])
}

function useFocusOnOpen<TElement extends HTMLElement>(enabled: boolean) {
  const ref = useRef<TElement | null>(null)

  useEffect(() => {
    if (enabled) {
      ref.current?.focus()
    }
  }, [enabled])

  return ref
}

function useRestoreFocusOnClose(enabled: boolean) {
  const previousFocusRef = useRef<HTMLElement | null>(null)

  useEffect(() => {
    if (!enabled) {
      return undefined
    }

    previousFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null

    return () => {
      const previousFocus = previousFocusRef.current
      previousFocusRef.current = null
      if (previousFocus?.isConnected) {
        previousFocus.focus()
      }
    }
  }, [enabled])
}

function useFocusTrap<TElement extends HTMLElement>(enabled: boolean) {
  const ref = useRef<TElement | null>(null)

  useEffect(() => {
    if (!enabled) {
      return undefined
    }

    function getFocusableElements() {
      const container = ref.current
      if (!container) {
        return []
      }

      return Array.from(
        container.querySelectorAll<HTMLElement>('button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'),
      )
    }

    function handleKeyDown(event: globalThis.KeyboardEvent) {
      if (event.key !== 'Tab') {
        return
      }

      const focusableElements = getFocusableElements()
      if (focusableElements.length === 0) {
        event.preventDefault()
        return
      }

      const firstElement = focusableElements[0]
      const lastElement = focusableElements[focusableElements.length - 1]

      if (event.shiftKey && document.activeElement === firstElement) {
        event.preventDefault()
        lastElement.focus()
        return
      }

      if (!event.shiftKey && document.activeElement === lastElement) {
        event.preventDefault()
        firstElement.focus()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [enabled])

  return ref
}

function getPasswordPolicyErrors(password: string, emptyMessage = 'Укажите пароль.') {
  const errors: string[] = []
  if (!password) {
    errors.push(emptyMessage)
  } else {
    if (password.length < 8) {
      errors.push('Пароль должен быть не короче 8 символов.')
    }

    if (!/[A-ZА-ЯЁ]/.test(password)) {
      errors.push('Добавьте заглавную букву в пароль.')
    }

    if (!/[a-zа-яё]/.test(password)) {
      errors.push('Добавьте строчную букву в пароль.')
    }

    if (!/\d/.test(password)) {
      errors.push('Добавьте хотя бы одну цифру в пароль.')
    }
  }

  return errors
}

function getAuthValidationErrors(mode: 'bootstrap' | 'login', email: string, displayName: string, password: string) {
  const errors: string[] = []
  const trimmedEmail = email.trim()

  if (!trimmedEmail) {
    errors.push('Укажите email.')
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)) {
    errors.push('Проверьте формат email.')
  }

  if (mode === 'bootstrap' && !displayName.trim()) {
    errors.push('Укажите имя пользователя.')
  }

  errors.push(...getPasswordPolicyErrors(password))

  return errors
}

function getPasswordChangeValidationErrors(currentPassword: string, newPassword: string, repeatPassword: string) {
  const errors: string[] = []

  if (!currentPassword) {
    errors.push('Укажите текущий пароль.')
  }

  errors.push(...getPasswordPolicyErrors(newPassword, 'Укажите новый пароль.'))

  if (!repeatPassword) {
    errors.push('Повторите новый пароль.')
  } else if (newPassword !== repeatPassword) {
    errors.push('Новый пароль и повтор пароля не совпадают.')
  }

  return errors
}

function getManagedUserValidationErrors(email: string, displayName: string, password: string, roleCode: string) {
  const errors: string[] = []
  const trimmedEmail = email.trim()

  if (!trimmedEmail) {
    errors.push('Укажите email пользователя.')
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)) {
    errors.push('Проверьте формат email пользователя.')
  }

  if (!displayName.trim()) {
    errors.push('Укажите имя пользователя.')
  }

  errors.push(...getPasswordPolicyErrors(password, 'Укажите пароль пользователя.'))

  if (!roleCode) {
    errors.push('Выберите роль пользователя.')
  }

  return errors
}

function getOwnerValidationErrors(form: UpsertOwnerRequest) {
  const errors: string[] = []

  if (!form.lastName.trim()) {
    errors.push('Укажите фамилию владельца.')
  }

  if (!form.firstName.trim()) {
    errors.push('Укажите имя владельца.')
  }

  if (form.phone?.trim() && form.phone.trim().length < 5) {
    errors.push('Проверьте телефон владельца.')
  }

  return errors
}

function getOwnerGarageLinkValidationErrors(form: OwnerGarageLinkForm) {
  const errors: string[] = []
  const hasNewGarageDetails =
    Boolean(form.newGarageNumber.trim()) ||
    form.peopleCount !== 1 ||
    form.floorCount !== 1 ||
    form.startingBalance !== 0 ||
    form.initialWaterMeterValue.trim() !== '' ||
    form.initialElectricityMeterValue.trim() !== '' ||
    form.comment.trim() !== ''

  if (hasNewGarageDetails && !form.newGarageNumber.trim()) {
    errors.push('Укажите номер нового гаража или очистите поля создания гаража.')
  }

  if (form.newGarageNumber.trim()) {
    errors.push(...getGarageValidationErrors({
      number: form.newGarageNumber,
      peopleCount: form.peopleCount,
      floorCount: form.floorCount,
      ownerId: null,
      startingBalance: form.startingBalance,
      initialWaterMeterValue: form.initialWaterMeterValue === '' ? null : Number(form.initialWaterMeterValue),
      initialElectricityMeterValue: form.initialElectricityMeterValue === '' ? null : Number(form.initialElectricityMeterValue),
      comment: form.comment.trim() || undefined,
    }))
  }

  return errors
}

function getGarageValidationErrors(form: UpsertGarageRequest) {
  const errors: string[] = []

  if (!form.number.trim()) {
    errors.push('Укажите номер гаража.')
  }

  if (!Number.isInteger(form.peopleCount) || form.peopleCount < 0) {
    errors.push('Количество людей должно быть целым числом 0 или больше.')
  }

  if (!Number.isInteger(form.floorCount) || form.floorCount < 0) {
    errors.push('Количество этажей должно быть целым числом 0 или больше.')
  }

  if (!Number.isFinite(form.startingBalance)) {
    errors.push('Укажите корректный стартовый баланс гаража.')
  }

  if (form.initialWaterMeterValue != null && (!Number.isFinite(form.initialWaterMeterValue) || form.initialWaterMeterValue < 0)) {
    errors.push('Стартовый счетчик воды должен быть 0 или больше.')
  }

  if (form.initialElectricityMeterValue != null && (!Number.isFinite(form.initialElectricityMeterValue) || form.initialElectricityMeterValue < 0)) {
    errors.push('Стартовый счетчик электричества должен быть 0 или больше.')
  }

  return errors
}

function getSupplierGroupValidationErrors(form: UpsertSupplierGroupRequest) {
  const errors: string[] = []

  if (!form.name.trim()) {
    errors.push('Укажите группу поставщиков.')
  }

  return errors
}

function getSupplierValidationErrors(form: UpsertSupplierRequest) {
  const errors: string[] = []
  const trimmedInn = form.inn?.trim()

  if (!form.name.trim()) {
    errors.push('Укажите название поставщика.')
  }

  if (!form.groupId) {
    errors.push('Выберите группу поставщика.')
  }

  if (trimmedInn && !/^\d{10}(\d{2})?$/.test(trimmedInn)) {
    errors.push('ИНН поставщика должен содержать 10 или 12 цифр.')
  }

  if (!Number.isFinite(form.startingBalance)) {
    errors.push('Укажите корректный стартовый баланс поставщика.')
  }

  return errors
}

function getAccountingTypeValidationErrors(form: UpsertAccountingTypeRequest, title: string) {
  const errors: string[] = []
  const code = form.code?.trim()

  if (!form.name.trim()) {
    errors.push(`Укажите название ${title}.`)
  }

  if (code && !/^[a-z0-9_-]+$/i.test(code)) {
    errors.push(`Код ${title} должен содержать только латиницу, цифры, дефис или подчеркивание.`)
  }

  return errors
}

function getTariffValidationErrors(form: UpsertTariffRequest) {
  const errors: string[] = []

  if (!form.name.trim()) {
    errors.push('Укажите название тарифа.')
  }

  if (!['fixed', 'people', 'meter_water', 'meter_electricity'].includes(form.calculationBase)) {
    errors.push('Выберите базу расчета тарифа.')
  }

  if (!Number.isFinite(form.rate) || form.rate <= 0) {
    errors.push('Ставка тарифа должна быть больше 0.')
  }

  if (form.calculationBase === 'meter_electricity') {
    const tierValues = [
      form.electricityFirstThreshold,
      form.electricitySecondThreshold,
      form.electricityFirstRate,
      form.electricitySecondRate,
      form.electricityThirdRate,
    ]
    const hasAnyTierValue = tierValues.some((value) => value !== undefined)

    if (hasAnyTierValue) {
      if (tierValues.some((value) => value === undefined)) {
        errors.push('Для трехтарифной электроэнергии заполните два порога и три ставки.')
      } else if (tierValues.some((value) => !Number.isFinite(Number(value)) || Number(value) <= 0)) {
        errors.push('Пороги и ставки электроэнергии должны быть больше 0.')
      } else if (Number(form.electricitySecondThreshold) <= Number(form.electricityFirstThreshold)) {
        errors.push('Второй порог электроэнергии должен быть больше первого.')
      }
    }
  }

  if (!form.effectiveFrom || Number.isNaN(Date.parse(form.effectiveFrom))) {
    errors.push('Укажите дату начала тарифа.')
  }

  return errors
}

function isDateInputValue(value: string) {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(Date.parse(value))
}

function parseOptionalNumberInput(value: string): number | undefined {
  return value === '' ? undefined : Number(value)
}

function createTariffFormFromDto(tariff: TariffDto): UpsertTariffRequest {
  const form: UpsertTariffRequest = {
    name: tariff.name,
    calculationBase: tariff.calculationBase,
    rate: tariff.rate,
    effectiveFrom: tariff.effectiveFrom,
    comment: tariff.comment ?? '',
  }

  const hasElectricityTiers = tariff.electricityFirstThreshold !== null
    && tariff.electricitySecondThreshold !== null
    && tariff.electricityFirstRate !== null
    && tariff.electricitySecondRate !== null
    && tariff.electricityThirdRate !== null

  return hasElectricityTiers
    ? {
        ...form,
        electricityFirstThreshold: tariff.electricityFirstThreshold!,
        electricitySecondThreshold: tariff.electricitySecondThreshold!,
        electricityFirstRate: tariff.electricityFirstRate!,
        electricitySecondRate: tariff.electricitySecondRate!,
        electricityThirdRate: tariff.electricityThirdRate!,
      }
    : form
}

function withoutElectricityTierFields(form: UpsertTariffRequest): UpsertTariffRequest {
  return {
    name: form.name,
    calculationBase: form.calculationBase,
    rate: form.rate,
    effectiveFrom: form.effectiveFrom,
    comment: form.comment,
  }
}

function updateTariffCalculationBase(form: UpsertTariffRequest, calculationBase: string): UpsertTariffRequest {
  const nextForm = { ...form, calculationBase }
  return calculationBase === 'meter_electricity' ? nextForm : withoutElectricityTierFields(nextForm)
}

function isAccountingMonthValue(value: string) {
  return /^\d{4}-\d{2}-01$/.test(value) && !Number.isNaN(Date.parse(value))
}

function addPositiveAmountValidation(errors: string[], amount: number, label: string) {
  if (!Number.isFinite(amount) || amount <= 0) {
    errors.push(`${label} должна быть больше 0.`)
  }
}

function getIncomeValidationErrors(form: CreateIncomeOperationRequest) {
  const errors: string[] = []

  if (!form.garageId) {
    errors.push('Выберите гараж для поступления.')
  }

  if (!form.incomeTypeId) {
    errors.push('Выберите вид поступления.')
  }

  if (!isDateInputValue(form.operationDate)) {
    errors.push('Укажите дату поступления.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц поступления.')
  }

  addPositiveAmountValidation(errors, form.amount, 'Сумма поступления')

  return errors
}

function getExpenseValidationErrors(form: CreateExpenseOperationRequest) {
  const errors: string[] = []

  if (!form.supplierId) {
    errors.push('Выберите поставщика для выплаты.')
  }

  if (!form.expenseTypeId) {
    errors.push('Выберите вид выплаты.')
  }

  if (!isDateInputValue(form.operationDate)) {
    errors.push('Укажите дату выплаты.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц выплаты.')
  }

  addPositiveAmountValidation(errors, form.amount, 'Сумма выплаты')

  return errors
}

function getAccrualValidationErrors(form: CreateAccrualRequest) {
  const errors: string[] = []

  if (!form.garageId) {
    errors.push('Выберите гараж для начисления.')
  }

  if (!form.incomeTypeId) {
    errors.push('Выберите вид начисления.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц начисления.')
  }

  addPositiveAmountValidation(errors, form.amount, 'Сумма начисления')

  if (!form.comment?.trim()) {
    errors.push('Укажите комментарий начисления.')
  }

  return errors
}

function getSupplierAccrualValidationErrors(form: CreateSupplierAccrualRequest) {
  const errors: string[] = []

  if (!form.supplierId) {
    errors.push('Выберите поставщика для начисления.')
  }

  if (!form.expenseTypeId) {
    errors.push('Выберите вид начисления поставщику.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц начисления поставщику.')
  }

  addPositiveAmountValidation(errors, form.amount, 'Сумма начисления поставщику')

  if (!form.comment?.trim()) {
    errors.push('Укажите комментарий начисления поставщику.')
  }

  return errors
}

function getSupplierGroupSalaryValidationErrors(form: GenerateSupplierGroupSalaryAccrualsRequest) {
  const errors: string[] = []

  if (!form.supplierGroupId) {
    errors.push('Выберите группу персонала.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц зарплаты.')
  }

  addPositiveAmountValidation(errors, form.amount, 'Сумма зарплаты')

  return errors
}

function getRegularAccrualValidationErrors(form: GenerateRegularAccrualsRequest) {
  const errors: string[] = []

  if (!form.incomeTypeId) {
    errors.push('Выберите вид регулярного начисления.')
  }

  if (!form.tariffId) {
    errors.push('Выберите тариф регулярного начисления.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц регулярных начислений.')
  }

  return errors
}

function getRegularAccrualValidationErrorsForCatalog(
  form: GenerateRegularAccrualsRequest,
  incomeTypes: AccountingTypeDto[],
  tariffs: TariffDto[],
) {
  const errors = getRegularAccrualValidationErrors(form)
  const incomeType = incomeTypes.find((item) => item.id === form.incomeTypeId)
  const tariff = tariffs.find((item) => item.id === form.tariffId)

  if (incomeType && tariff && !isTariffCompatibleWithRegularIncomeType(incomeType, tariff)) {
    errors.push('Выбранный тариф не подходит для этого вида регулярного начисления.')
  }

  return errors
}

function getRegularIncomeTypeCalculationBase(incomeType?: AccountingTypeDto | null) {
  switch (incomeType?.code?.trim().toLowerCase()) {
    case 'water':
      return 'meter_water'
    case 'trash':
      return 'people'
    case 'electricity':
      return 'meter_electricity'
    case 'membership':
    case 'target':
    case 'entry':
    case 'connection':
      return 'fixed'
    default:
      return null
  }
}

function isTariffCompatibleWithRegularIncomeType(incomeType: AccountingTypeDto, tariff: TariffDto) {
  const calculationBase = getRegularIncomeTypeCalculationBase(incomeType)
  return calculationBase === null || tariff.calculationBase === calculationBase
}

function getCompatibleRegularTariffs(incomeTypeId: string, incomeTypes: AccountingTypeDto[], tariffs: TariffDto[]) {
  const incomeType = incomeTypes.find((item) => item.id === incomeTypeId)
  return incomeType ? tariffs.filter((tariff) => isTariffCompatibleWithRegularIncomeType(incomeType, tariff)) : tariffs
}

function chooseRegularTariffId(incomeTypeId: string, currentTariffId: string, incomeTypes: AccountingTypeDto[], tariffs: TariffDto[]) {
  const compatibleTariffs = getCompatibleRegularTariffs(incomeTypeId, incomeTypes, tariffs)
  return compatibleTariffs.some((tariff) => tariff.id === currentTariffId) ? currentTariffId : compatibleTariffs[0]?.id ?? ''
}

function getMeterReadingValidationErrors(form: CreateMeterReadingRequest) {
  const errors: string[] = []

  if (!form.garageId) {
    errors.push('Выберите гараж для счетчика.')
  }

  if (!['water', 'electricity'].includes(form.meterKind)) {
    errors.push('Выберите тип счетчика.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц показания.')
  }

  if (!isDateInputValue(form.readingDate)) {
    errors.push('Укажите дату показания.')
  }

  if (!Number.isFinite(form.currentValue) || form.currentValue < 0) {
    errors.push('Новое показание должно быть 0 или больше.')
  }

  return errors
}

type AccrualBreakdown =
  | { kind: 'garage'; accrual: AccrualDto }
  | { kind: 'supplier'; accrual: SupplierAccrualDto }

type IncomeReportFilters = {
  dateFrom: string
  dateTo: string
  search: string
  garageIds: string[]
  ownerIds: string[]
  incomeTypeIds: string[]
  rowMode: string
}

type ConsolidatedReportFilters = {
  monthFrom: string
  monthTo: string
  search: string
}

type ExpenseReportFilters = {
  dateFrom: string
  dateTo: string
  search: string
  supplierIds: string[]
  expenseTypeIds: string[]
  rowMode: string
}

function getReportMonthRangeValidationErrors(filters: ConsolidatedReportFilters) {
  const errors: string[] = []

  if (!isAccountingMonthValue(filters.monthFrom)) {
    errors.push('Укажите начало периода отчета.')
  }

  if (!isAccountingMonthValue(filters.monthTo)) {
    errors.push('Укажите конец периода отчета.')
  }

  if (isAccountingMonthValue(filters.monthFrom) && isAccountingMonthValue(filters.monthTo) && filters.monthFrom > filters.monthTo) {
    errors.push('Начало периода отчета не может быть позже конца.')
  }

  return errors
}

function getReportDateRangeValidationErrors(dateFrom: string, dateTo: string, label: string) {
  const errors: string[] = []

  if (!isDateInputValue(dateFrom)) {
    errors.push(`Укажите начало ${label}.`)
  }

  if (!isDateInputValue(dateTo)) {
    errors.push(`Укажите конец ${label}.`)
  }

  if (isDateInputValue(dateFrom) && isDateInputValue(dateTo) && dateFrom > dateTo) {
    errors.push(`Начало ${label} не может быть позже конца.`)
  }

  return errors
}

function getIncomeReportValidationErrors(filters: IncomeReportFilters) {
  return getReportDateRangeValidationErrors(filters.dateFrom, filters.dateTo, 'отчета по поступлениям')
}

function getExpenseReportValidationErrors(filters: ExpenseReportFilters) {
  return getReportDateRangeValidationErrors(filters.dateFrom, filters.dateTo, 'отчета по выплатам')
}

const reportFilterStorageKeys = {
  consolidated: 'garagebalance.reports.consolidatedFilters',
  income: 'garagebalance.reports.incomeFilters',
  expense: 'garagebalance.reports.expenseFilters',
} as const
const authSessionStorageKey = 'garagebalance.auth.session'
const garageReportScreenRowLimit = 12
const reportScreenRowLimit = 16
const auditScreenRequestLimit = 50
const financeScreenRequestLimit = 50
const dictionaryScreenRequestLimit = 100
const importQuarantineScreenRequestLimit = 50

type NavigationItem = {
  section: WorkspaceSection
  label: string
  icon: typeof Gauge
  requiredAny?: readonly string[]
}

type WorkspaceSection = 'dashboard' | 'users' | 'dictionaries' | 'payments' | 'reports' | 'import' | 'audit' | 'releases'
type ReportTab = 'consolidated' | 'income' | 'expense'
type ImportTab = 'checks' | 'log' | 'history' | 'quarantine'

const permissions = {
  usersManage: 'users.manage',
  dictionariesRead: 'dictionaries.read',
  dictionariesWrite: 'dictionaries.write',
  paymentsRead: 'payments.read',
  paymentsWrite: 'payments.write',
  reportsRead: 'reports.read',
  importRun: 'import.run',
  auditRead: 'audit.read',
  tariffsManage: 'tariffs.manage',
  appReleasesManage: 'app_releases.manage',
} as const

const rolePermissionGroups = [
  { label: 'Пользователи', permission: permissions.usersManage },
  { label: 'Справочники', permission: permissions.dictionariesWrite },
  { label: 'Тарифы', permission: permissions.tariffsManage },
  { label: 'Платежи', permission: permissions.paymentsWrite },
  { label: 'Отчеты', permission: permissions.reportsRead },
  { label: 'Импорт', permission: permissions.importRun },
  { label: 'Audit', permission: permissions.auditRead },
  { label: 'Что нового', permission: permissions.appReleasesManage },
] as const

const navigation: NavigationItem[] = [
  { section: 'dashboard', label: 'Панель', icon: Gauge },
  { section: 'users', label: 'Пользователи', icon: ShieldCheck, requiredAny: [permissions.usersManage] },
  { section: 'dictionaries', label: 'Справочники', icon: UsersRound, requiredAny: [permissions.dictionariesRead] },
  { section: 'payments', label: 'Платежи', icon: WalletCards, requiredAny: [permissions.paymentsRead] },
  { section: 'reports', label: 'Отчеты', icon: FileSpreadsheet, requiredAny: [permissions.reportsRead] },
  { section: 'import', label: 'Импорт', icon: DatabaseZap, requiredAny: [permissions.importRun] },
  { section: 'audit', label: 'Audit', icon: FileText, requiredAny: [permissions.auditRead] },
  { section: 'releases', label: 'Что нового', icon: BookOpenCheck },
]

const roadmap = [
  {
    title: 'Пользователи и права',
    text: 'Вход только для разрешенных сотрудников, роли, журнал действий и защита финансовых разделов.',
    icon: ShieldCheck,
  },
  {
    title: 'Справочники',
    text: 'Гаражи, владельцы и поставщики доступны в защищенной рабочей области с проверками дублей и связей.',
    icon: BookOpenCheck,
  },
  {
    title: 'Импорт Access',
    text: 'Следующий шаг: сопоставление таблиц старой базы и перенос без ручного набора данных.',
    icon: DatabaseZap,
  },
  {
    title: 'Платежи и начисления',
    text: 'Помесячные строки, счетчики, регулярные начисления, корректировки и задолженность.',
    icon: CircleDollarSign,
  },
]

function App({ authClient = authApi, auditClient = auditApi, dictionaryClient = dictionariesApi, financeClient = financeApi, importClient = importApi, reportClient = reportsApi, releaseClient = releasesApi, userClient = usersApi }: AppProps) {
  const [auth, setAuth] = useState<AuthResponse | null>(() => loadStoredAuth())
  const [activeSection, setActiveSection] = useState<WorkspaceSection>('dashboard')

  function handleAuthenticated(nextAuth: AuthResponse) {
    saveStoredAuth(nextAuth)
    setAuth(nextAuth)
  }

  function handleUserChanged(user: CurrentUserDto) {
    setAuth((current) => {
      if (!current) {
        return current
      }

      const nextAuth = { ...current, user }
      saveStoredAuth(nextAuth)
      return nextAuth
    })
  }

  function handleLogout() {
    clearStoredAuth()
    setAuth(null)
    setActiveSection('dashboard')
  }

  if (!auth) {
    return (
      <main className="auth-entry">
        <AuthGate authClient={authClient} onAuthenticated={handleAuthenticated} />
      </main>
    )
  }

  const activeNavigationItem = navigation.find((entry) => entry.section === activeSection)
  const effectiveActiveSection = activeNavigationItem && hasAnyPermission(auth, activeNavigationItem.requiredAny) ? activeSection : 'dashboard'

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark">G</div>
          <div>
            <strong>GarageBalance</strong>
            <span>учет гаражного кооператива</span>
          </div>
        </div>

        <nav className="nav-list" aria-label="Основные разделы">
          {navigation.map((item) => {
            const Icon = item.icon
            const canOpen = hasAnyPermission(auth, item.requiredAny)
            const isActive = effectiveActiveSection === item.section
            return (
              <button
                className={isActive ? 'nav-item active' : 'nav-item'}
                type="button"
                key={item.section}
                disabled={!canOpen}
                aria-current={isActive ? 'page' : undefined}
                onClick={() => setActiveSection(item.section)}
              >
                <Icon size={18} />
                <span>{item.label}</span>
              </button>
            )
          })}
        </nav>

        <div className="sidebar-footer">
          <LockKeyhole size={18} />
          <div>
            <strong>Безопасный старт</strong>
            <span>первый этап начинается с ролей и доступа</span>
          </div>
        </div>
      </aside>

      <section className="workspace">
        <Workspace activeSection={effectiveActiveSection} auth={auth} authClient={authClient} auditClient={auditClient} dictionaryClient={dictionaryClient} financeClient={financeClient} importClient={importClient} reportClient={reportClient} releaseClient={releaseClient} userClient={userClient} onUserChanged={handleUserChanged} onLogout={handleLogout} />
      </section>
    </main>
  )
}

function AuthGate({ authClient, onAuthenticated }: { authClient: AuthClient; onAuthenticated: (auth: AuthResponse) => void }) {
  const [mode, setMode] = useState<'bootstrap' | 'login'>('bootstrap')
  const [email, setEmail] = useState('admin@example.com')
  const [displayName, setDisplayName] = useState('Администратор')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  const [loading, setLoading] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    const errors = getAuthValidationErrors(mode, email, displayName, password)
    if (errors.length > 0) {
      setValidationErrors(errors)
      return
    }

    setValidationErrors([])
    setLoading(true)

    try {
      const response =
        mode === 'bootstrap'
          ? await authClient.bootstrapAdmin({ email, displayName, password })
          : await authClient.login({ email, password })
      setPassword('')
      onAuthenticated(response)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось выполнить вход.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <section className="auth-layout" aria-label="Вход в систему">
      <div className="auth-copy">
        <p className="eyebrow">Безопасность</p>
        <h1>Сначала вход и права, потом деньги и импорт</h1>
        <p className="lead">
          Система не открывает рабочие разделы без пользователя. Первый запуск создает администратора, дальше вход идет по учетной записи.
        </p>
      </div>

      <form className="auth-card" onSubmit={handleSubmit}>
        <div className="auth-tabs" role="tablist" aria-label="Режим входа">
          <button type="button" className={mode === 'bootstrap' ? 'active' : ''} onClick={() => setMode('bootstrap')}>
            Первый администратор
          </button>
          <button type="button" className={mode === 'login' ? 'active' : ''} onClick={() => setMode('login')}>
            Вход
          </button>
        </div>

        <label>
          Email
          <input aria-label="Email" value={email} onChange={(event) => setEmail(event.target.value)} type="email" required />
        </label>

        {mode === 'bootstrap' ? (
          <label>
            Имя пользователя
            <input aria-label="Имя пользователя" value={displayName} onChange={(event) => setDisplayName(event.target.value)} required />
          </label>
        ) : null}

        <label>
          Пароль
          <input aria-label="Пароль" aria-describedby="auth-password-policy-hint" value={password} onChange={(event) => setPassword(event.target.value)} type="password" minLength={8} required />
        </label>
        <p className="form-hint" id="auth-password-policy-hint">Минимум 8 символов: заглавная буква, строчная буква и цифра.</p>

        <FormValidationSummary title="Проверьте форму входа" items={validationErrors} />
        {error ? <FormError>{error}</FormError> : null}

        <button className="primary-button" type="submit" disabled={loading}>
          {loading ? 'Проверяем...' : mode === 'bootstrap' ? 'Создать администратора' : 'Войти'}
        </button>
      </form>
    </section>
  )
}

function Workspace({
  activeSection,
  auth,
  authClient,
  auditClient,
  dictionaryClient,
  financeClient,
  importClient,
  reportClient,
  releaseClient,
  userClient,
  onUserChanged,
  onLogout,
}: {
  activeSection: WorkspaceSection
  auth: AuthResponse
  authClient: AuthClient
  auditClient: AuditClient
  dictionaryClient: DictionaryClient
  financeClient: FinanceClient
  importClient: ImportClient
  reportClient: ReportClient
  releaseClient: ReleaseClient
  userClient: UserManagementClient
  onUserChanged: (user: CurrentUserDto) => void
  onLogout: () => void
}) {
  const canManageUsers = hasPermission(auth, permissions.usersManage)
  const canReadDictionaries = hasPermission(auth, permissions.dictionariesRead)
  const canReadPayments = hasPermission(auth, permissions.paymentsRead)
  const canRunImport = hasPermission(auth, permissions.importRun)
  const canReadReports = hasPermission(auth, permissions.reportsRead)
  const canReadAudit = hasPermission(auth, permissions.auditRead)

  function renderActiveSection() {
    switch (activeSection) {
      case 'dashboard':
        return (
          <>
            <section className="hero-panel" aria-label="Панель">
              <div>
                <p className="eyebrow">Старт проекта</p>
                <h1>Финансовый учет ГСК без ручного переноса старой базы</h1>
                <p className="lead">
                  Основа проекта уже разложена под пользователей, справочники, тарифы, платежи, отчеты и импорт Access.
                </p>
              </div>
              <div className="status-stack" aria-label="Ключевые статусы">
                <div>
                  <span>Этап 1</span>
                  <strong>ядро учета</strong>
                </div>
                <div>
                  <span>Права</span>
                  <strong>{auth.user.permissions.length} доступов</strong>
                </div>
                <div>
                  <span>Docker</span>
                  <strong>готовится сразу</strong>
                </div>
              </div>
            </section>

            <PasswordPanel auth={auth} authClient={authClient} onUserChanged={onUserChanged} />

            <section className="roadmap-grid" aria-label="Ближайшая очередь">
              {roadmap.map((item) => {
                const Icon = item.icon
                return (
                  <article className="work-card" key={item.title}>
                    <Icon size={22} />
                    <h2>{item.title}</h2>
                    <p>{item.text}</p>
                  </article>
                )
              })}
            </section>
          </>
        )
      case 'users':
        return canManageUsers ? (
          <UserManagementPanel auth={auth} userClient={userClient} />
        ) : (
          <AccessNotice label="Пользователи недоступны" title="Пользователи" permission={permissions.usersManage} description="Управлять сотрудниками и ролями может только пользователь с правом администрирования." />
        )
      case 'dictionaries':
        return canReadDictionaries ? (
          <DictionaryPanelV2 auth={auth} dictionaryClient={dictionaryClient} financeClient={financeClient} initialSection="owners" />
        ) : (
          <AccessNotice label="Справочники недоступны" title="Справочники" permission={permissions.dictionariesRead} description="Для просмотра гаражей, владельцев и поставщиков нужно право на чтение справочников." />
        )
      case 'payments':
        return canReadPayments && canReadDictionaries ? (
          <FinancePanel auth={auth} dictionaryClient={dictionaryClient} financeClient={financeClient} />
        ) : (
          <AccessNotice label="Платежи недоступны" title="Платежи" permission={permissions.paymentsRead} description="Для платежей нужны права на просмотр финансовых операций и справочников." />
        )
      case 'reports':
        return canReadReports && canReadDictionaries ? (
          <ReportPanel auth={auth} dictionaryClient={dictionaryClient} reportClient={reportClient} />
        ) : (
          <AccessNotice
            label="Отчеты недоступны"
            title="Отчеты"
            permission={canReadReports ? permissions.dictionariesRead : permissions.reportsRead}
            description={canReadReports ? 'Для фильтров отчетов нужно право чтения справочников.' : 'Для отчетов нужно право просмотра отчетности; справочники используются только для фильтров.'}
          />
        )
      case 'import':
        return canRunImport ? (
          <ImportPanel auth={auth} importClient={importClient} />
        ) : (
          <AccessNotice label="Импорт недоступен" title="Импорт Access" permission={permissions.importRun} description="Запускать проверку и перенос старой базы может только пользователь с правом импорта." />
        )
      case 'audit':
        return canReadAudit ? (
          <AuditPanel auth={auth} auditClient={auditClient} />
        ) : (
          <AccessNotice label="Аудит недоступен" title="Аудит" permission={permissions.auditRead} description="Журнал действий доступен только пользователям с правом просмотра audit-событий." />
        )
      case 'releases':
        return <ReleasePanel auth={auth} releaseClient={releaseClient} />
      default:
        return null
    }
  }

  return (
    <>
      <header className="topbar">
        <div className="search">
          <Search size={18} />
          <span>Поиск по гаражу, владельцу или поставщику</span>
        </div>
        <div className="user-panel">
          <div>
            <strong>{auth.user.displayName}</strong>
            <span>{auth.user.roles.join(', ')}</span>
          </div>
          <button className="icon-button" type="button" aria-label="Уведомления">
            <Bell size={19} />
          </button>
          <button className="icon-button" type="button" aria-label="Выйти" onClick={onLogout}>
            <LogOut size={19} />
          </button>
        </div>
      </header>
      {renderActiveSection()}
    </>
  )
}

function PasswordPanel({ auth, authClient, onUserChanged }: { auth: AuthResponse; authClient: AuthClient; onUserChanged: (user: CurrentUserDto) => void }) {
  const [form, setForm] = useState({ currentPassword: '', newPassword: '', repeatPassword: '' })
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  const [saving, setSaving] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setMessage(null)

    const errors = getPasswordChangeValidationErrors(form.currentPassword, form.newPassword, form.repeatPassword)
    if (errors.length > 0) {
      setValidationErrors(errors)
      return
    }

    setValidationErrors([])
    setSaving(true)
    try {
      const user = await authClient.changeOwnPassword(auth.accessToken, {
        currentPassword: form.currentPassword,
        newPassword: form.newPassword,
      })
      onUserChanged(user)
      setForm({ currentPassword: '', newPassword: '', repeatPassword: '' })
      setMessage('Пароль изменен. Используйте новый пароль при следующем входе.')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось изменить пароль.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="password-panel" aria-label="Безопасность аккаунта">
      <div>
        <p className="eyebrow">Безопасность</p>
        <h2>Смена пароля</h2>
        <p>Пользователь может обновить свой пароль без участия администратора. Текущий пароль нужен для подтверждения действия.</p>
      </div>
      <form className="dictionary-form" onSubmit={handleSubmit}>
        <label>
          Текущий пароль
          <input aria-label="Текущий пароль" type="password" value={form.currentPassword} onChange={(event) => setForm({ ...form, currentPassword: event.target.value })} minLength={8} required />
        </label>
        <div className="inline-fields">
          <label>
            Новый пароль
            <input aria-label="Новый пароль" aria-describedby="own-password-policy-hint" type="password" value={form.newPassword} onChange={(event) => setForm({ ...form, newPassword: event.target.value })} minLength={8} required />
          </label>
          <label>
            Повтор нового пароля
            <input aria-label="Повтор нового пароля" aria-describedby="own-password-policy-hint" type="password" value={form.repeatPassword} onChange={(event) => setForm({ ...form, repeatPassword: event.target.value })} minLength={8} required />
          </label>
        </div>
        <p className="form-hint" id="own-password-policy-hint">Минимум 8 символов: заглавная буква, строчная буква и цифра.</p>
        <FormValidationSummary title="Проверьте смену пароля" items={validationErrors} />
        {error ? <FormError>{error}</FormError> : null}
        {message ? <div className="form-success" role="status" aria-live="polite">{message}</div> : null}
        <button className="secondary-button" type="submit" disabled={saving}>
          <ShieldCheck size={16} />
          <span>{saving ? 'Сохраняем...' : 'Изменить пароль'}</span>
        </button>
      </form>
    </section>
  )
}

function AccessNotice({ label, title, permission, description }: { label: string; title: string; permission: string; description: string }) {
  return (
    <section className="access-notice" aria-label={label}>
      <LockKeyhole size={20} />
      <div>
        <p className="eyebrow">Раздел недоступен</p>
        <h2>{title}</h2>
        <p>{description}</p>
        <small>Требуется право: {permission}</small>
      </div>
    </section>
  )
}

function hasPermission(auth: AuthResponse, permission: string): boolean {
  return auth.user.permissions.includes(permission)
}

function hasAnyPermission(auth: AuthResponse, requiredAny?: readonly string[]): boolean {
  return !requiredAny || requiredAny.some((permission) => hasPermission(auth, permission))
}

function ReleasePanel({ auth, releaseClient }: { auth: AuthResponse; releaseClient: ReleaseClient }) {
  const [releases, setReleases] = useState<AppReleaseDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let ignore = false

    async function loadReleases() {
      setLoading(true)
      setError(null)

      try {
        const nextReleases = await releaseClient.getReleases(auth.accessToken, 10)
        if (!ignore) {
          setReleases(nextReleases)
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить историю обновлений.')
        }
      } finally {
        if (!ignore) {
          setLoading(false)
        }
      }
    }

    void loadReleases()

    return () => {
      ignore = true
    }
  }, [auth.accessToken, releaseClient])

  return (
    <section className="release-panel" aria-label="Что нового">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">Что нового</p>
          <h2>История обновлений</h2>
        </div>
        <span>{releases.length} версий</span>
      </div>

      {loading ? <p className="muted" role="status" aria-live="polite">Загружаем историю обновлений...</p> : null}
      {error ? <FormError>{error}</FormError> : null}
      {!loading && !error && releases.length === 0 ? <p className="muted" role="status" aria-live="polite">Пока нет опубликованных изменений.</p> : null}

      {!loading && !error && releases.length > 0 ? (
        <div className="release-list">
          {releases.map((release) => (
            <article className="release-entry" key={release.releaseId}>
              <div className="release-entry__header">
                <div>
                  <h3>{release.title}</h3>
                  <p>{release.summary}</p>
                </div>
                <span>
                  v{release.version} · {formatReleaseDate(release.publishedAt)}
                </span>
              </div>
              <ul>
                {release.items.map((item) => (
                  <li className={`release-item release-item--${item.type}`} key={`${release.releaseId}-${item.type}-${item.text}`}>
                    {item.text}
                  </li>
                ))}
              </ul>
            </article>
          ))}
        </div>
      ) : null}
    </section>
  )
}

type FinanceSectionKey = 'income' | 'expense' | 'accruals' | 'supplierAccruals' | 'meterReadings'
type FinanceEditorKey = FinanceSectionKey | 'regularAccruals' | 'supplierGroupSalaryAccruals'
type FinanceRecord = FinancialOperationDto | AccrualDto | SupplierAccrualDto | MeterReadingDto

const financeSectionOptions: Array<{ key: FinanceSectionKey; label: string; description: string }> = [
  { key: 'income', label: 'Приходы', description: 'Оплаты владельцев' },
  { key: 'expense', label: 'Расходы', description: 'Выплаты поставщикам' },
  { key: 'accruals', label: 'Начисления владельцам', description: 'Долги по гаражам' },
  { key: 'supplierAccruals', label: 'Начисления поставщикам', description: 'Обязательства' },
  { key: 'meterReadings', label: 'Счетчики', description: 'Вода и электричество' },
]

function FinancePanel({
  auth,
  dictionaryClient,
  financeClient,
}: {
  auth: AuthResponse
  dictionaryClient: DictionaryClient
  financeClient: FinanceClient
}) {
  const today = getLocalDateInputValue()
  const month = `${today.slice(0, 7)}-01`
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [incomeGarageOptions, setIncomeGarageOptions] = useState<GarageDto[]>([])
  const [supplierGroups, setSupplierGroups] = useState<SupplierGroupDto[]>([])
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
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
  const [financePage, setFinancePage] = useState<FinancePagedResult<FinanceRecord>>({ items: [], totalCount: 0, offset: 0, limit: 25 })
  const [financeSectionCounts, setFinanceSectionCounts] = useState<Record<FinanceSectionKey, number>>({ income: 0, expense: 0, accruals: 0, supplierAccruals: 0, meterReadings: 0 })
  const [financeContextMenu, setFinanceContextMenu] = useState<{ section: FinanceSectionKey; record?: FinanceRecord; x: number; y: number } | null>(null)
  const [incomeValidationErrors, setIncomeValidationErrors] = useState<string[]>([])
  const [expenseValidationErrors, setExpenseValidationErrors] = useState<string[]>([])
  const [accrualValidationErrors, setAccrualValidationErrors] = useState<string[]>([])
  const [supplierAccrualValidationErrors, setSupplierAccrualValidationErrors] = useState<string[]>([])
  const [regularValidationErrors, setRegularValidationErrors] = useState<string[]>([])
  const [salaryValidationErrors, setSalaryValidationErrors] = useState<string[]>([])
  const [meterValidationErrors, setMeterValidationErrors] = useState<string[]>([])
  const [accrualBreakdown, setAccrualBreakdown] = useState<AccrualBreakdown | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  useRestoreFocusOnClose(Boolean(accrualBreakdown))
  useRestoreFocusOnClose(Boolean(financeEditor))
  const accrualBreakdownCloseButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(accrualBreakdown))
  const accrualBreakdownDialogRef = useFocusTrap<HTMLElement>(Boolean(accrualBreakdown))
  const financeEditorCloseButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(financeEditor))
  const financeEditorDialogRef = useFocusTrap<HTMLElement>(Boolean(financeEditor))

  useEscapeKey(Boolean(accrualBreakdown), () => setAccrualBreakdown(null))
  useEscapeKey(Boolean(financeEditor), () => setFinanceEditor(null))
  useEscapeKey(Boolean(financeContextMenu), () => setFinanceContextMenu(null))
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
        const [loadedGarages, loadedSupplierGroups, loadedSuppliers, loadedIncomeTypes, loadedExpenseTypes, loadedTariffs, loadedOperations, loadedAccruals, loadedSupplierAccruals, loadedMeterReadings, loadedMissingMeterReadings, loadedSummary] = await Promise.all([
          dictionaryClient.getGarages(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getSupplierGroups(auth.accessToken, dictionaryScreenRequestLimit),
          dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getIncomeTypes(auth.accessToken, dictionaryScreenRequestLimit),
          dictionaryClient.getExpenseTypes(auth.accessToken, dictionaryScreenRequestLimit),
          dictionaryClient.getTariffs(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          financeClient.getOperations(auth.accessToken, financeScreenRequestLimit),
          financeClient.getAccruals(auth.accessToken, financeScreenRequestLimit),
          financeClient.getSupplierAccruals(auth.accessToken, financeScreenRequestLimit),
          financeClient.getMeterReadings(auth.accessToken, financeScreenRequestLimit),
          financeClient.getMissingMeterReadings(auth.accessToken, { accountingMonth: month, limit: financeScreenRequestLimit }),
          financeClient.getSummary(auth.accessToken),
        ])
        if (!ignore) {
          setGarages(loadedGarages)
          setIncomeGarageOptions(loadedGarages)
          setSupplierGroups(loadedSupplierGroups)
          setSuppliers(loadedSuppliers)
          setIncomeTypes(loadedIncomeTypes)
          setExpenseTypes(loadedExpenseTypes)
          setTariffs(loadedTariffs)
          setOperations(loadedOperations)
          setAccruals(loadedAccruals)
          setSupplierAccruals(loadedSupplierAccruals)
          setMeterReadings(loadedMeterReadings)
          setMissingMeterReadings(loadedMissingMeterReadings)
          setSummary(loadedSummary)
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
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadFinanceWorkbench(activeFinanceSection, 0, financePage.limit)
  }, [activeFinanceSection, financePage.limit, loadFinanceWorkbench])

  async function searchIncomeGarages() {
    const query = incomeGarageSearch.trim()
    await runSaving('income-garage-search', async () => {
      const foundGarages = await dictionaryClient.getGarages(auth.accessToken, query || undefined, dictionaryScreenRequestLimit)
      setIncomeGarageOptions(foundGarages)
      setIncomeForm((value) => ({
        ...value,
        garageId: foundGarages.some((garage) => garage.id === value.garageId) ? value.garageId : foundGarages[0]?.id ?? '',
      }))
      setIncomeGarageSearchStatus(query ? `Найдено гаражей: ${foundGarages.length}` : `Показаны все гаражи: ${foundGarages.length}`)
    })
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
    const saved = await runSaving('income', async () => {
      if (financeEditor?.mode === 'edit' && financeEditor.record && 'operationKind' in financeEditor.record) {
        await financeClient.updateIncome(auth.accessToken, financeEditor.record.id, request)
      } else {
        await financeClient.createIncome(auth.accessToken, request)
      }
      await loadFinanceWorkbench('income', financePage.offset, financePage.limit)
      setIncomeForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
    })
    if (saved) {
      setFinanceEditor(null)
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
    const saved = await runSaving('expense', async () => {
      if (financeEditor?.mode === 'edit' && financeEditor.record && 'operationKind' in financeEditor.record) {
        await financeClient.updateExpense(auth.accessToken, financeEditor.record.id, request)
      } else {
        await financeClient.createExpense(auth.accessToken, request)
      }
      await loadFinanceWorkbench('expense', financePage.offset, financePage.limit)
      setExpenseForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
    })
    if (saved) {
      setFinanceEditor(null)
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
    const saved = await runSaving('accrual', async () => {
      if (financeEditor?.mode === 'edit' && financeEditor.record && 'incomeTypeId' in financeEditor.record && !('operationKind' in financeEditor.record)) {
        await financeClient.updateAccrual(auth.accessToken, financeEditor.record.id, request)
      } else {
        await financeClient.createAccrual(auth.accessToken, request)
      }
      await loadFinanceWorkbench('accruals', financePage.offset, financePage.limit)
      setAccrualForm((value) => ({ ...value, amount: 0, comment: '' }))
    })
    if (saved) {
      setFinanceEditor(null)
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
      setFinanceEditor(null)
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
    const saved = await runSaving('supplier-accrual', async () => {
      if (financeEditor?.mode === 'edit' && financeEditor.record && 'supplierId' in financeEditor.record && !('operationKind' in financeEditor.record)) {
        await financeClient.updateSupplierAccrual(auth.accessToken, financeEditor.record.id, request)
      } else {
        await financeClient.createSupplierAccrual(auth.accessToken, request)
      }
      await loadFinanceWorkbench('supplierAccruals', financePage.offset, financePage.limit)
      setSupplierAccrualForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
    })
    if (saved) {
      setFinanceEditor(null)
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
      setFinanceEditor(null)
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
      setFinanceEditor(null)
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

  async function cancelOperation(operation: FinancialOperationDto) {
    if (!canWritePayments) {
      setError('Для отмены платежей нужно право payments.write.')
      return
    }

    const reason = window.prompt('Укажите причину отмены операции')
    if (!reason?.trim()) {
      setError('Для отмены операции нужна причина.')
      return
    }

    await runSaving(`cancel-${operation.id}`, async () => {
      await financeClient.cancelOperation(auth.accessToken, operation.id, { reason: reason.trim() })
      await loadFinanceWorkbench(operation.operationKind === 'income' ? 'income' : 'expense', financePage.offset, financePage.limit)
    })
  }

  async function cancelAccrual(accrual: AccrualDto) {
    if (!canWritePayments) {
      setError('Для отмены начислений нужно право payments.write.')
      return
    }

    const reason = window.prompt('Укажите причину отмены начисления')
    if (!reason?.trim()) {
      setError('Для отмены начисления нужна причина.')
      return
    }

    await runSaving(`cancel-accrual-${accrual.id}`, async () => {
      await financeClient.cancelAccrual(auth.accessToken, accrual.id, { reason: reason.trim() })
      await loadFinanceWorkbench('accruals', financePage.offset, financePage.limit)
    })
  }

  async function cancelSupplierAccrual(accrual: SupplierAccrualDto) {
    if (!canWritePayments) {
      setError('Для отмены начислений поставщикам нужно право payments.write.')
      return
    }

    const reason = window.prompt('Укажите причину отмены начисления поставщику')
    if (!reason?.trim()) {
      setError('Для отмены начисления поставщику нужна причина.')
      return
    }

    await runSaving(`cancel-supplier-accrual-${accrual.id}`, async () => {
      await financeClient.cancelSupplierAccrual(auth.accessToken, accrual.id, { reason: reason.trim() })
      await loadFinanceWorkbench('supplierAccruals', financePage.offset, financePage.limit)
    })
  }

  async function cancelMeterReading(reading: MeterReadingDto) {
    if (!canWritePayments) {
      setError('Для отмены показаний нужно право payments.write.')
      return
    }

    const reason = window.prompt('Укажите причину отмены показания')
    if (!reason?.trim()) {
      setError('Для отмены показания нужна причина.')
      return
    }

    await runSaving(`cancel-meter-reading-${reading.id}`, async () => {
      await financeClient.cancelMeterReading(auth.accessToken, reading.id, { reason: reason.trim() })
      await loadFinanceWorkbench('meterReadings', financePage.offset, financePage.limit)
    })
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

  function openFinanceEditor(section: FinanceEditorKey, record?: FinanceRecord) {
    if (!canWritePayments) {
      setFinanceContextMenu(null)
      setError('Для записи платежей нужно право payments.write.')
      return
    }

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
              initialWaterMeterValue: null,
              initialElectricityMeterValue: null,
              comment: null,
              isArchived: false,
            }, ...items]))
      }
      setIncomeForm({
        garageId: record.garageId ?? '',
        incomeTypeId: record.incomeTypeId ?? '',
        operationDate: record.operationDate,
        accountingMonth: record.accountingMonth,
        amount: record.amount,
        documentNumber: record.documentNumber ?? '',
        comment: record.comment ?? '',
      })
    } else if (!record && section === 'income') {
      setIncomeForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
    } else if (record && section === 'expense' && 'operationKind' in record) {
      setExpenseForm({
        supplierId: record.supplierId ?? '',
        expenseTypeId: record.expenseTypeId ?? '',
        operationDate: record.operationDate,
        accountingMonth: record.accountingMonth,
        amount: record.amount,
        documentNumber: record.documentNumber ?? '',
        comment: record.comment ?? '',
      })
    } else if (!record && section === 'expense') {
      setExpenseForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
    } else if (record && section === 'accruals' && 'incomeTypeId' in record && !('operationKind' in record)) {
      setAccrualForm({
        garageId: record.garageId,
        incomeTypeId: record.incomeTypeId,
        accountingMonth: record.accountingMonth,
        amount: record.amount,
        source: record.source,
        comment: record.comment ?? '',
      })
    } else if (!record && section === 'accruals') {
      setAccrualForm((value) => ({ ...value, source: 'manual', amount: 0, comment: '' }))
    } else if (record && section === 'supplierAccruals' && 'supplierId' in record && !('operationKind' in record)) {
      setSupplierAccrualForm({
        supplierId: record.supplierId,
        expenseTypeId: record.expenseTypeId,
        accountingMonth: record.accountingMonth,
        amount: record.amount,
        source: record.source,
        documentNumber: record.documentNumber ?? '',
        comment: record.comment ?? '',
      })
    } else if (!record && section === 'supplierAccruals') {
      setSupplierAccrualForm((value) => ({ ...value, source: 'manual', amount: 0, documentNumber: '', comment: '' }))
    } else if (!record && section === 'supplierGroupSalaryAccruals') {
      setSalaryForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
    } else if (record && section === 'meterReadings' && 'meterKind' in record) {
      setMeterForm({
        garageId: record.garageId,
        meterKind: record.meterKind,
        accountingMonth: record.accountingMonth,
        readingDate: record.readingDate,
        currentValue: record.currentValue,
        comment: record.comment ?? '',
      })
    }
    setFinanceEditor({ section, mode: record ? 'edit' : 'create', record })
  }

  function openFinanceContextMenu(event: MouseEvent<HTMLElement>, section: FinanceSectionKey, record?: FinanceRecord) {
    event.preventDefault()
    event.stopPropagation()
    setFinanceContextMenu({ section, record, x: event.clientX, y: event.clientY })
  }

  function editFinanceRecord(section: FinanceSectionKey, record: FinanceRecord) {
    setFinanceContextMenu(null)
    openFinanceEditor(section, record)
  }

  function addFinanceRecord(section: FinanceSectionKey) {
    setFinanceContextMenu(null)
    openFinanceEditor(section)
  }

  function deleteFinanceRecord(section: FinanceSectionKey, record: FinanceRecord) {
    setFinanceContextMenu(null)
    if (section === 'income' || section === 'expense') {
      void cancelOperation(record as FinancialOperationDto)
    } else if (section === 'accruals') {
      void cancelAccrual(record as AccrualDto)
    } else if (section === 'supplierAccruals') {
      void cancelSupplierAccrual(record as SupplierAccrualDto)
    } else {
      void cancelMeterReading(record as MeterReadingDto)
    }
  }

  function handleFinanceRowKeyDown(event: KeyboardEvent<HTMLElement>, section: FinanceSectionKey, record: FinanceRecord) {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      editFinanceRecord(section, record)
    }
  }

  function getFinanceSectionCount(section: FinanceSectionKey) {
    return financeSectionCounts[section]
  }

  const filteredIncomeOperations = operations.filter((operation) => operation.operationKind === 'income')
  const filteredExpenseOperations = operations.filter((operation) => operation.operationKind === 'expense')
  const filteredAccruals = accruals
  const filteredSupplierAccruals = supplierAccruals
  const filteredMeterReadings = meterReadings

  function getActiveFinanceRowsCount() {
    return financePage.items.length
  }

  function getFinanceAddLabel() {
    if (activeFinanceSection === 'income') {
      return 'Провести поступление'
    }
    if (activeFinanceSection === 'expense') {
      return 'Провести выплату'
    }
    if (activeFinanceSection === 'accruals') {
      return 'Начислить'
    }
    if (activeFinanceSection === 'supplierAccruals') {
      return 'Начислить поставщику'
    }
    return 'Внести показание'
  }

  function renderFinanceTable() {
    if (activeFinanceSection === 'income') {
      return (
        <table className="dictionary-data-table finance-data-table">
          <thead>
            <tr>
              <th>Дата</th>
              <th>Месяц</th>
              <th>Гараж</th>
              <th>Владелец</th>
              <th>Вид оплаты</th>
              <th>Документ</th>
              <th>Оплачено</th>
              <th>Долг после</th>
              <th>Комментарий</th>
            </tr>
          </thead>
          <tbody>
            {filteredIncomeOperations.map((operation) => (
              <tr className="finance-table-row--interactive" key={operation.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'income', operation)} onClick={() => editFinanceRecord('income', operation)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'income', operation)}>
                <td>{formatDateOnly(operation.operationDate)}</td>
                <td>{formatMonth(operation.accountingMonth)}</td>
                <td>Гараж {operation.garageNumber}</td>
                <td>{operation.ownerName ?? 'Не указан'}</td>
                <td>{operation.incomeTypeName}</td>
                <td>{operation.documentNumber ?? 'Не указан'}</td>
                <td className="money-income">{formatMoney(operation.amount)}</td>
                <td className={operation.garageDebtAfter !== null ? getDebtClassName(operation.garageDebtAfter) : undefined}>{operation.garageDebtAfter !== null ? formatDebtAmount(operation.garageDebtAfter) : 'Нет данных'}</td>
                <td>{operation.comment ?? 'Нет комментария'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )
    }

    if (activeFinanceSection === 'expense') {
      return (
        <table className="dictionary-data-table finance-data-table">
          <thead>
            <tr>
              <th>Дата</th>
              <th>Месяц</th>
              <th>Поставщик</th>
              <th>Вид выплаты</th>
              <th>Документ</th>
              <th>Выплачено</th>
              <th>Обязательство после</th>
              <th>Комментарий</th>
            </tr>
          </thead>
          <tbody>
            {filteredExpenseOperations.map((operation) => (
              <tr className="finance-table-row--interactive" key={operation.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'expense', operation)} onClick={() => editFinanceRecord('expense', operation)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'expense', operation)}>
                <td>{formatDateOnly(operation.operationDate)}</td>
                <td>{formatMonth(operation.accountingMonth)}</td>
                <td>{operation.supplierName ?? 'Не указан'}</td>
                <td>{operation.expenseTypeName}</td>
                <td>{operation.documentNumber ?? 'Не указан'}</td>
                <td className="money-expense">{formatMoney(operation.amount)}</td>
                <td>{operation.supplierDebtAfter !== null ? formatMoney(operation.supplierDebtAfter) : 'Нет данных'}</td>
                <td>{operation.comment ?? 'Нет комментария'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )
    }

    if (activeFinanceSection === 'accruals') {
      return (
        <table className="dictionary-data-table finance-data-table">
          <thead>
            <tr>
              <th>Месяц</th>
              <th>Гараж</th>
              <th>Владелец</th>
              <th>Вид оплаты</th>
              <th>Источник</th>
              <th>Начислено</th>
              <th>Комментарий</th>
            </tr>
          </thead>
          <tbody>
            {filteredAccruals.map((accrual) => (
              <tr className="finance-table-row--interactive" key={accrual.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'accruals', accrual)} onClick={() => editFinanceRecord('accruals', accrual)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'accruals', accrual)}>
                <td>{formatMonth(accrual.accountingMonth)}</td>
                <td>Гараж {accrual.garageNumber}</td>
                <td>{accrual.ownerName ?? 'Не указан'}</td>
                <td>{accrual.incomeTypeName}</td>
                <td>{formatAccrualSource(accrual.source)}</td>
                <td className="money-accrual">{formatMoney(accrual.amount)}</td>
                <td>{accrual.comment ?? 'Нет комментария'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )
    }

    if (activeFinanceSection === 'supplierAccruals') {
      return (
        <table className="dictionary-data-table finance-data-table">
          <thead>
            <tr>
              <th>Месяц</th>
              <th>Поставщик</th>
              <th>Вид выплаты</th>
              <th>Источник</th>
              <th>Документ</th>
              <th>Начислено</th>
              <th>Комментарий</th>
            </tr>
          </thead>
          <tbody>
            {filteredSupplierAccruals.map((accrual) => (
              <tr className="finance-table-row--interactive" key={accrual.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'supplierAccruals', accrual)} onClick={() => editFinanceRecord('supplierAccruals', accrual)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'supplierAccruals', accrual)}>
                <td>{formatMonth(accrual.accountingMonth)}</td>
                <td>{accrual.supplierName}</td>
                <td>{accrual.expenseTypeName}</td>
                <td>{formatAccrualSource(accrual.source)}</td>
                <td>{accrual.documentNumber ?? 'Не указан'}</td>
                <td className="money-expense">{formatMoney(accrual.amount)}</td>
                <td>{accrual.comment ?? 'Нет комментария'}</td>
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
          <thead>
            <tr>
              <th>Месяц</th>
              <th>Дата</th>
              <th>Гараж</th>
              <th>Счетчик</th>
              <th>Пред. знач.</th>
              <th>Нов. знач.</th>
              <th>Разница</th>
              <th>Комментарий</th>
            </tr>
          </thead>
          <tbody>
            {filteredMeterReadings.map((reading) => (
              <tr className="finance-table-row--interactive" key={reading.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'meterReadings', reading)} onClick={() => editFinanceRecord('meterReadings', reading)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'meterReadings', reading)}>
                <td>{formatMonth(reading.accountingMonth)}</td>
                <td>{formatDateOnly(reading.readingDate)}</td>
                <td>Гараж {reading.garageNumber}</td>
                <td>{reading.meterKind === 'water' ? 'Вода' : 'Электричество'}</td>
                <td>{reading.previousValue}</td>
                <td>{reading.currentValue}</td>
                <td>
                  {reading.consumption}
                  {reading.hasGapWarning ? <small className="warning-text">проверьте месяц</small> : null}
                </td>
                <td>{reading.comment ?? 'Нет комментария'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </>
    )
  }

  function getFinanceEditorTitle(section: FinanceEditorKey) {
    if (section === 'income') {
      return 'Новое поступление'
    }
    if (section === 'expense') {
      return 'Новая выплата'
    }
    if (section === 'accruals') {
      return 'Ручное начисление'
    }
    if (section === 'regularAccruals') {
      return 'Регулярные начисления'
    }
    if (section === 'supplierGroupSalaryAccruals') {
      return 'Зарплата группы'
    }
    if (section === 'supplierAccruals') {
      return 'Начисление поставщику'
    }
    return 'Показание счетчика'
  }

  function getFinanceEditorSubmitLabel(section: FinanceEditorKey) {
    if (section === 'income' || section === 'expense') {
      return 'Провести'
    }
    if (section === 'meterReadings') {
      return 'Внести'
    }
    if (section === 'regularAccruals') {
      return 'Создать месяц'
    }
    if (section === 'supplierGroupSalaryAccruals') {
      return 'Начислить зарплату'
    }
    return 'Начислить'
  }

  function getFinanceEditorSavingScope(section: FinanceEditorKey) {
    if (section === 'income') {
      return 'income'
    }
    if (section === 'expense') {
      return 'expense'
    }
    if (section === 'accruals') {
      return 'accrual'
    }
    if (section === 'regularAccruals') {
      return 'regular-accruals'
    }
    if (section === 'supplierGroupSalaryAccruals') {
      return 'salary-accruals'
    }
    if (section === 'supplierAccruals') {
      return 'supplier-accrual'
    }
    return 'meter-reading'
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
    if (section === 'income') {
      return (
        <>
          <div className="inline-fields">
            <label className="dictionary-search">
              <Search size={16} aria-hidden="true" />
              <input aria-label="Поиск гаража для поступления" placeholder="Гараж или владелец" value={incomeGarageSearch} onChange={(event) => setIncomeGarageSearch(event.target.value)} />
            </label>
            <button className="icon-button" type="button" aria-label="Найти гараж для поступления" disabled={saving === 'income-garage-search'} onClick={() => void searchIncomeGarages()}>
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
          <FormValidationSummary title="Проверьте поступление" items={incomeValidationErrors} />
        </>
      )
    }

    if (section === 'expense') {
      return (
        <>
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
          <div className="inline-fields">
            <input aria-label="Дата выплаты" type="date" value={expenseForm.operationDate} onChange={(event) => setExpenseForm({ ...expenseForm, operationDate: event.target.value })} required />
            <input aria-label="Месяц выплаты" type="month" value={expenseForm.accountingMonth.slice(0, 7)} onChange={(event) => setExpenseForm({ ...expenseForm, accountingMonth: `${event.target.value}-01` })} required />
          </div>
          <div className="inline-fields">
            <input aria-label="Сумма выплаты" type="number" min="0.01" step="0.01" value={expenseForm.amount} onChange={(event) => setExpenseForm({ ...expenseForm, amount: Number(event.target.value) })} required />
            <input aria-label="Документ выплаты" placeholder="Документ" value={expenseForm.documentNumber} onChange={(event) => setExpenseForm({ ...expenseForm, documentNumber: event.target.value })} />
          </div>
          <input aria-label="Комментарий выплаты" placeholder="Комментарий платежа" value={expenseForm.comment} onChange={(event) => setExpenseForm({ ...expenseForm, comment: event.target.value })} />
          <FormValidationSummary title="Проверьте выплату" items={expenseValidationErrors} />
        </>
      )
    }

    if (section === 'accruals') {
      return (
        <>
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
          <input aria-label="Источник начисления" value={formatAccrualSource(accrualForm.source)} readOnly />
          <input aria-label="Комментарий к начислению" placeholder="Комментарий" value={accrualForm.comment} onChange={(event) => setAccrualForm({ ...accrualForm, comment: event.target.value })} />
          <FormValidationSummary title="Проверьте начисление" items={accrualValidationErrors} />
        </>
      )
    }

    if (section === 'regularAccruals') {
      return (
        <>
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
          <input aria-label="Месяц регулярного начисления" type="month" value={regularForm.accountingMonth.slice(0, 7)} onChange={(event) => setRegularForm({ ...regularForm, accountingMonth: `${event.target.value}-01` })} required />
          <input aria-label="Комментарий к регулярному начислению" placeholder="Комментарий" value={regularForm.comment} onChange={(event) => setRegularForm({ ...regularForm, comment: event.target.value })} />
          <FormValidationSummary title="Проверьте регулярное начисление" items={regularValidationErrors} />
          {regularStatus ? <p className="form-hint">{regularStatus}</p> : null}
        </>
      )
    }

    if (section === 'supplierAccruals') {
      return (
        <>
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
          <input aria-label="Источник начисления поставщику" value={formatAccrualSource(supplierAccrualForm.source)} readOnly />
          <div className="inline-fields">
            <input aria-label="Документ начисления поставщику" placeholder="Документ" value={supplierAccrualForm.documentNumber} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, documentNumber: event.target.value })} />
            <input aria-label="Комментарий начисления поставщику" placeholder="Комментарий" value={supplierAccrualForm.comment} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, comment: event.target.value })} />
          </div>
          <FormValidationSummary title="Проверьте начисление поставщику" items={supplierAccrualValidationErrors} />
        </>
      )
    }

    if (section === 'supplierGroupSalaryAccruals') {
      return (
        <>
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
          <div className="inline-fields">
            <input aria-label="Месяц зарплаты" type="month" value={salaryForm.accountingMonth.slice(0, 7)} onChange={(event) => setSalaryForm({ ...salaryForm, accountingMonth: `${event.target.value}-01` })} required />
            <input aria-label="Сумма зарплаты" type="number" min="0.01" step="0.01" value={salaryForm.amount} onChange={(event) => setSalaryForm({ ...salaryForm, amount: Number(event.target.value) })} required />
          </div>
          <div className="inline-fields">
            <input aria-label="Документ зарплаты" placeholder="Документ" value={salaryForm.documentNumber} onChange={(event) => setSalaryForm({ ...salaryForm, documentNumber: event.target.value })} />
            <input aria-label="Комментарий зарплаты" placeholder="Комментарий" value={salaryForm.comment} onChange={(event) => setSalaryForm({ ...salaryForm, comment: event.target.value })} />
          </div>
          <FormValidationSummary title="Проверьте начисление зарплаты" items={salaryValidationErrors} />
          {salaryStatus ? <p className="form-hint">{salaryStatus}</p> : null}
        </>
      )
    }

    return (
      <>
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
        <select aria-label="Тип счетчика" value={meterForm.meterKind} onChange={(event) => setMeterForm({ ...meterForm, meterKind: event.target.value as 'water' | 'electricity' })} required>
          <option value="water">Вода</option>
          <option value="electricity">Электричество</option>
        </select>
        <div className="inline-fields">
          <input aria-label="Месяц показания" type="month" value={meterForm.accountingMonth.slice(0, 7)} onChange={(event) => setMeterForm({ ...meterForm, accountingMonth: `${event.target.value}-01` })} required />
          <input aria-label="Дата показания" type="date" value={meterForm.readingDate} onChange={(event) => setMeterForm({ ...meterForm, readingDate: event.target.value })} required />
        </div>
        <div className="inline-fields">
          <input aria-label="Текущее показание" type="number" min="0" step="0.001" value={meterForm.currentValue} onChange={(event) => setMeterForm({ ...meterForm, currentValue: Number(event.target.value) })} required />
          <input aria-label="Комментарий к показанию" placeholder="Комментарий" value={meterForm.comment} onChange={(event) => setMeterForm({ ...meterForm, comment: event.target.value })} />
        </div>
        <FormValidationSummary title="Проверьте показание" items={meterValidationErrors} />
      </>
    )
  }

  return (
    <section className="finance-panel" aria-label="Платежи">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Платежи</p>
          <h2>Поступления владельцев и выплаты поставщикам</h2>
        </div>
        <span>{loading ? 'Загрузка...' : `${summary.operationCount} операций`}</span>
      </div>

      {error ? <FormError>{error}</FormError> : null}
      {!canWritePayments ? <p className="form-hint">Режим просмотра: для записи платежей, начислений и показаний нужно право payments.write.</p> : null}

      <div className="summary-strip" aria-label="Итоги платежей">
        <div>
          <span>Поступления</span>
          <strong>{formatMoney(summary.incomeTotal)}</strong>
        </div>
        <div>
          <span>Начислено</span>
          <strong>{formatMoney(summary.accrualTotal)}</strong>
        </div>
        <div>
          <span>{formatDebtLabel(summary.debt)}</span>
          <strong className={getDebtClassName(summary.debt)}>{formatDebtAmount(summary.debt)}</strong>
        </div>
        <div>
          <span>Выплаты</span>
          <strong>{formatMoney(summary.expenseTotal)}</strong>
        </div>
        <div>
          <span>Баланс</span>
          <strong>{formatMoney(summary.balance)}</strong>
        </div>
        <div>
          <span>Счетчики</span>
          <strong>{summary.meterReadingCount}</strong>
        </div>
      </div>

      <div className="finance-workbench">
        <div className="finance-tabs" role="tablist" aria-label="Разделы платежей">
          {financeSectionOptions.map((section) => (
            <button
              type="button"
              role="tab"
              aria-selected={activeFinanceSection === section.key}
              className={activeFinanceSection === section.key ? 'is-active' : undefined}
              key={section.key}
              onClick={() => setActiveFinanceSection(section.key)}
            >
              <span>{section.label}</span>
              <small>{section.description} · {getFinanceSectionCount(section.key)}</small>
            </button>
          ))}
        </div>

        <div className="dictionary-toolbar finance-table-toolbar">
          <div className="finance-period-filter" aria-label="Фильтр периода">
            <input aria-label="Период с" type="month" value={financeFilter.monthFrom} onChange={(event) => setFinanceFilter((value) => ({ ...value, monthFrom: event.target.value }))} />
            <input aria-label="Период по" type="month" value={financeFilter.monthTo} onChange={(event) => setFinanceFilter((value) => ({ ...value, monthTo: event.target.value }))} />
          </div>
          <label className="dictionary-search">
            <Search size={16} aria-hidden="true" />
            <input aria-label="Поиск по платежам" placeholder="Гараж, владелец, поставщик или документ" value={financeSearchInput} onChange={(event) => setFinanceSearchInput(event.target.value)} />
          </label>
          <div className="finance-toolbar-actions">
            {activeFinanceSection === 'accruals' ? (
              <button className="ghost-button" type="button" disabled={!canWritePayments} onClick={() => openFinanceEditor('regularAccruals')}>
                <Plus size={16} aria-hidden="true" />
                <span>Регулярные</span>
              </button>
            ) : null}
            {activeFinanceSection === 'supplierAccruals' ? (
              <button className="ghost-button" type="button" disabled={!canWritePayments} onClick={() => openFinanceEditor('supplierGroupSalaryAccruals')}>
                <Plus size={16} aria-hidden="true" />
                <span>Зарплата группы</span>
              </button>
            ) : null}
            <button className="secondary-button" type="button" disabled={!canWritePayments} onClick={() => openFinanceEditor(activeFinanceSection)}>
              <Plus size={16} aria-hidden="true" />
              <span>{getFinanceAddLabel()}</span>
            </button>
          </div>
        </div>

        <div className="dictionary-table-shell">
          <div className="dictionary-table-scroll" onContextMenu={(event) => openFinanceContextMenu(event, activeFinanceSection)}>
            {renderFinanceTable()}
            {getActiveFinanceRowsCount() === 0 ? <p className="empty-state" role="status" aria-live="polite">По выбранным условиям записей нет</p> : null}
          </div>
          <div className="dictionary-pagination" role="navigation" aria-label="Пагинация платежей">
            <span role="status" aria-live="polite">Показано {financePage.totalCount === 0 ? 0 : financePage.offset + 1}-{Math.min(financePage.offset + financePage.items.length, financePage.totalCount)} из {financePage.totalCount}</span>
            <label>
              Строк
              <select aria-label="Количество строк платежей" value={financePage.limit} onChange={(event) => void loadFinanceWorkbench(activeFinanceSection, 0, Number(event.target.value))}>
                {dictionaryPageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
              </select>
            </label>
            <button className="ghost-button" type="button" disabled={loading || financePage.offset === 0} onClick={() => void loadFinanceWorkbench(activeFinanceSection, Math.max(0, financePage.offset - financePage.limit), financePage.limit)}>Назад</button>
            <button className="ghost-button" type="button" disabled={loading || financePage.offset + financePage.limit >= financePage.totalCount} onClick={() => void loadFinanceWorkbench(activeFinanceSection, financePage.offset + financePage.limit, financePage.limit)}>Вперед</button>
          </div>
        </div>
      </div>

      <div className="finance-grid">
        <form className="dictionary-form" onSubmit={saveIncome}>
          <h3>Новое поступление</h3>
          <div className="inline-fields">
            <label className="dictionary-search">
              <Search size={16} aria-hidden="true" />
              <input aria-label="Поиск гаража для поступления" placeholder="Гараж или владелец" value={incomeGarageSearch} onChange={(event) => setIncomeGarageSearch(event.target.value)} />
            </label>
            <button className="icon-button" type="button" aria-label="Найти гараж для поступления" disabled={saving === 'income-garage-search'} onClick={() => void searchIncomeGarages()}>
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
          <FormValidationSummary title="Проверьте поступление" items={incomeValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'income' || !incomeForm.garageId || !incomeForm.incomeTypeId}>
            <Plus size={16} />
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
          <FormValidationSummary title="Проверьте выплату" items={expenseValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'expense' || !expenseForm.supplierId || !expenseForm.expenseTypeId}>
            <Plus size={16} />
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
          <FormValidationSummary title="Проверьте начисление" items={accrualValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'accrual' || !accrualForm.garageId || !accrualForm.incomeTypeId}>
            <Plus size={16} />
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
          <FormValidationSummary title="Проверьте начисление поставщику" items={supplierAccrualValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'supplier-accrual' || !supplierAccrualForm.supplierId || !supplierAccrualForm.expenseTypeId}>
            <Plus size={16} />
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
          <FormValidationSummary title="Проверьте регулярные начисления" items={regularValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'regular-accruals' || !regularForm.incomeTypeId || !regularForm.tariffId}>
            <Plus size={16} />
            <span>Создать месяц</span>
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
          <FormValidationSummary title="Проверьте показание счетчика" items={meterValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === 'meter-reading' || !meterForm.garageId}>
            <Plus size={16} />
            <span>Внести</span>
          </button>
        </form>

        <div className="operation-list" role="table" aria-label="Последние платежи">
          <div className="operation-row header" role="row">
            <span role="columnheader">Дата</span>
            <span role="columnheader">Операция</span>
            <span role="columnheader">Сумма</span>
          </div>
          {operations.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Операций пока нет</p> : null}
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
                {canWritePayments ? (
                  <button
                    className="icon-button"
                    type="button"
                    aria-label={`Отменить операцию ${operation.documentNumber ?? operation.id}`}
                    title="Отменить операцию"
                    disabled={saving === `cancel-${operation.id}`}
                    onClick={() => void cancelOperation(operation)}
                  >
                    <Trash2 size={16} aria-hidden="true" />
                  </button>
                ) : null}
              </span>
            </div>
          ))}
          {operations.length > visibleOperations.length ? <p className="empty-state" role="status" aria-live="polite">Показано {visibleOperations.length} из {operations.length} операций</p> : null}
        </div>

        <div className="operation-list" role="table" aria-label="Последние начисления">
          <div className="operation-row header" role="row">
            <span role="columnheader">Месяц</span>
            <span role="columnheader">Начисление</span>
            <span role="columnheader">Сумма</span>
          </div>
          {accruals.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Начислений пока нет</p> : null}
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
                {canWritePayments ? (
                  <button
                    className="icon-button"
                    type="button"
                    aria-label={`Отменить начисление ${accrual.incomeTypeName} гараж ${accrual.garageNumber}`}
                    title="Отменить начисление"
                    disabled={saving === `cancel-accrual-${accrual.id}`}
                    onClick={(event) => {
                      event.stopPropagation()
                      void cancelAccrual(accrual)
                    }}
                  >
                    <Trash2 size={16} aria-hidden="true" />
                  </button>
                ) : null}
              </span>
            </div>
          ))}
          {accruals.length > visibleAccruals.length ? <p className="empty-state" role="status" aria-live="polite">Показано {visibleAccruals.length} из {accruals.length} начислений</p> : null}
        </div>

        <div className="operation-list" role="table" aria-label="Последние начисления поставщикам">
          <div className="operation-row header" role="row">
            <span role="columnheader">Месяц</span>
            <span role="columnheader">Поставщик</span>
            <span role="columnheader">Сумма</span>
          </div>
          {supplierAccruals.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Начислений поставщикам пока нет</p> : null}
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
                {canWritePayments ? (
                  <button
                    className="icon-button"
                    type="button"
                    aria-label={`Отменить начисление поставщику ${accrual.supplierName}`}
                    title="Отменить начисление поставщику"
                    disabled={saving === `cancel-supplier-accrual-${accrual.id}`}
                    onClick={(event) => {
                      event.stopPropagation()
                      void cancelSupplierAccrual(accrual)
                    }}
                  >
                    <Trash2 size={16} aria-hidden="true" />
                  </button>
                ) : null}
              </span>
            </div>
          ))}
          {supplierAccruals.length > visibleSupplierAccruals.length ? <p className="empty-state" role="status" aria-live="polite">Показано {visibleSupplierAccruals.length} из {supplierAccruals.length} начислений поставщикам</p> : null}
        </div>

        <div className="operation-list" role="table" aria-label="Последние показания">
          <div className="operation-row header" role="row">
            <span role="columnheader">Месяц</span>
            <span role="columnheader">Счетчик</span>
            <span role="columnheader">Расход</span>
          </div>
          {meterReadings.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Показаний пока нет</p> : null}
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
                {canWritePayments ? (
                  <button
                    className="icon-button"
                    type="button"
                    aria-label={`Отменить показание ${reading.meterKind === 'water' ? 'Вода' : 'Электричество'} гараж ${reading.garageNumber}`}
                    title="Отменить показание"
                    disabled={saving === `cancel-meter-reading-${reading.id}`}
                    onClick={() => void cancelMeterReading(reading)}
                  >
                    <Trash2 size={16} aria-hidden="true" />
                  </button>
                ) : null}
              </span>
            </div>
          ))}
          {meterReadings.length > visibleMeterReadings.length ? <p className="empty-state" role="status" aria-live="polite">Показано {visibleMeterReadings.length} из {meterReadings.length} показаний</p> : null}
        </div>
      </div>
      {financeContextMenu ? (
        <div className="context-menu" style={{ left: financeContextMenu.x, top: financeContextMenu.y }} role="menu" aria-label="Операции с платежами" onClick={(event) => event.stopPropagation()}>
          <button type="button" role="menuitem" disabled={!canWritePayments} onClick={() => addFinanceRecord(financeContextMenu.section)}>
            <Plus size={15} aria-hidden="true" />
            <span>Добавить</span>
          </button>
          <button type="button" role="menuitem" disabled={!canWritePayments || !financeContextMenu.record} onClick={() => financeContextMenu.record ? editFinanceRecord(financeContextMenu.section, financeContextMenu.record) : undefined}>
            <Save size={15} aria-hidden="true" />
            <span>Изменить</span>
          </button>
          <button type="button" role="menuitem" disabled={!canWritePayments || !financeContextMenu.record} onClick={() => financeContextMenu.record ? deleteFinanceRecord(financeContextMenu.section, financeContextMenu.record) : undefined}>
            <Trash2 size={15} aria-hidden="true" />
            <span>Удалить</span>
          </button>
        </div>
      ) : null}
      {financeEditor ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setFinanceEditor(null)}>
          <section ref={financeEditorDialogRef} className="detail-dialog finance-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="finance-editor-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">{financeEditor.mode === 'edit' ? 'Изменение' : 'Платежи'}</p>
                <h3 id="finance-editor-title">{getFinanceEditorTitle(financeEditor.section)}</h3>
              </div>
              <button ref={financeEditorCloseButtonRef} className="icon-button" type="button" aria-label="Закрыть форму платежа" onClick={() => setFinanceEditor(null)}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <form className="dictionary-form finance-editor-form" onSubmit={handleFinanceEditorSubmit}>
              {renderFinanceEditorFields(financeEditor.section)}
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={() => setFinanceEditor(null)}>
                  Отмена
                </button>
                <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === getFinanceEditorSavingScope(financeEditor.section)}>
                  <Plus size={16} aria-hidden="true" />
                  <span>{financeEditor.mode === 'edit' ? 'Сохранить' : getFinanceEditorSubmitLabel(financeEditor.section)}</span>
                </button>
              </div>
            </form>
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
    </section>
  )
}

function ImportPanel({ auth, importClient }: { auth: AuthResponse; importClient: ImportClient }) {
  const [runs, setRuns] = useState<AccessImportRunDto[]>([])
  const [quarantineItems, setQuarantineItems] = useState<AccessImportQuarantineItemDto[]>([])
  const [runLogEntries, setRunLogEntries] = useState<AccessImportRunLogEntryDto[]>([])
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [currentRun, setCurrentRun] = useState<AccessImportRunDto | null>(null)
  const [activeImportTab, setActiveImportTab] = useState<ImportTab>('checks')
  const [loading, setLoading] = useState(true)
  const [loadingLog, setLoadingLog] = useState(false)
  const [saving, setSaving] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [resolvingQuarantineId, setResolvingQuarantineId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [exportMessage, setExportMessage] = useState<string | null>(null)
  const visibleRunLogEntries = runLogEntries.slice(0, 10)
  const visibleRuns = runs.slice(0, 8)
  const visibleQuarantineItems = quarantineItems.slice(0, 8)
  const importTabs: Array<{ key: ImportTab; label: string; meta: string }> = [
    { key: 'checks', label: 'Проверки', meta: currentRun ? formatImportRunCheckSummary(currentRun) : 'ожидают запуска' },
    { key: 'log', label: 'Лог', meta: loadingLog ? 'загрузка' : `${runLogEntries.length} строк` },
    { key: 'history', label: 'История', meta: `${runs.length} запусков` },
    { key: 'quarantine', label: 'Карантин', meta: `${quarantineItems.length} открыто` },
  ]

  useEffect(() => {
    let ignore = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const [loadedRuns, loadedQuarantineItems] = await Promise.all([
          importClient.getAccessRuns(auth.accessToken),
          importClient.getOpenQuarantineItems(auth.accessToken, undefined, importQuarantineScreenRequestLimit),
        ])
        if (!ignore) {
          setRuns(loadedRuns)
          setQuarantineItems(loadedQuarantineItems)
          setCurrentRun(loadedRuns[0] ?? null)
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить историю импорта.')
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
  }, [auth.accessToken, importClient])

  useEffect(() => {
    let ignore = false

    async function loadRunLog() {
      if (!currentRun) {
        setRunLogEntries([])
        return
      }

      setLoadingLog(true)
      try {
        const entries = await importClient.getAccessRunLog(auth.accessToken, currentRun.id)
        if (!ignore) {
          setRunLogEntries(entries)
        }
      } catch (caught) {
        if (!ignore) {
          setRunLogEntries([])
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить лог импорта.')
        }
      } finally {
        if (!ignore) {
          setLoadingLog(false)
        }
      }
    }

    void loadRunLog()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, currentRun, importClient])

  async function runDryRun(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    if (!selectedFile) {
      setError('Выберите файл Access для проверки.')
      return
    }

    setSaving(true)
    setError(null)
    try {
      const run = await importClient.dryRunAccess(auth.accessToken, selectedFile)
      setCurrentRun(run)
      setRuns((items) => [run, ...items.filter((item) => item.id !== run.id)])
      setQuarantineItems(await importClient.getOpenQuarantineItems(auth.accessToken, undefined, importQuarantineScreenRequestLimit))
      setSelectedFile(null)
      setExportMessage(null)
      form.reset()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось выполнить dry-run импорта.')
    } finally {
      setSaving(false)
    }
  }

  async function downloadCurrentReport() {
    if (!currentRun) {
      return
    }

    setExporting(true)
    setError(null)
    setExportMessage(null)
    try {
      const blob = await importClient.downloadAccessRunReport(auth.accessToken, currentRun.id)
      downloadBlob(blob, buildImportReportFileName(currentRun))
      setExportMessage('Отчет dry-run импорта готов.')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось скачать отчет dry-run импорта.')
    } finally {
      setExporting(false)
    }
  }

  async function resolveQuarantineItem(item: AccessImportQuarantineItemDto) {
    setResolvingQuarantineId(item.id)
    setError(null)
    setExportMessage(null)
    try {
      await importClient.resolveQuarantineItem(auth.accessToken, item.id, 'Разобрано из панели импорта.')
      setQuarantineItems((items) => items.filter((candidate) => candidate.id !== item.id))
      setExportMessage('Строка карантина закрыта.')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось закрыть строку карантина импорта.')
    } finally {
      setResolvingQuarantineId(null)
    }
  }

  return (
    <section className="dictionary-panel" aria-label="Импорт Access">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Импорт</p>
          <h2>Проверка старой базы Access перед переносом</h2>
        </div>
        <span>{loading ? 'Загрузка...' : `${runs.length} запусков`}</span>
      </div>

      {error ? <FormError>{error}</FormError> : null}
      {exportMessage ? <div className="form-note" role="status" aria-live="polite">{exportMessage}</div> : null}

      <div className="import-workbench">
        <form className="dictionary-form" onSubmit={runDryRun}>
          <h3>Dry-run Access</h3>
          <input aria-label="Файл Access" type="file" accept=".accdb,.mdb" onChange={(event) => setSelectedFile(event.target.files?.[0] ?? null)} />
          <button className="secondary-button" type="submit" disabled={saving || !selectedFile}>
            <DatabaseZap size={16} />
            <span>Проверить файл</span>
          </button>
          {selectedFile ? <p className="empty-state" role="status" aria-live="polite">{selectedFile.name}</p> : null}
        </form>

        <div className="dictionary-form">
          <h3>Отчет проверки</h3>
          <button className="secondary-button" type="button" disabled={!currentRun || exporting} onClick={downloadCurrentReport}>
            <FileText size={16} />
            <span>Скачать отчет JSON</span>
          </button>
          {currentRun ? (
            <>
              <p className="empty-state" role="status" aria-live="polite">{currentRun.originalFileName} · {formatImportRunCheckSummary(currentRun)}</p>
              <p className="empty-state" role="status" aria-live="polite">{currentRun.summary}</p>
              <div className="summary-strip" aria-label="Итоги dry-run импорта">
                <div>
                  <span>Статус</span>
                  <strong>{formatImportRunStatus(currentRun.status)}</strong>
                </div>
                <div>
                  <span>Успешно</span>
                  <strong className="status-active">{currentRun.passedChecks}</strong>
                </div>
                <div>
                  <span>Предупреждения</span>
                  <strong className="warning-text">{currentRun.warningCount}</strong>
                </div>
                <div>
                  <span>Ошибки</span>
                  <strong className={currentRun.errorCount > 0 ? 'status-disabled' : 'status-active'}>{currentRun.errorCount}</strong>
                </div>
              </div>
            </>
          ) : <p className="empty-state" role="status" aria-live="polite">Выберите запуск dry-run</p>}
        </div>
      </div>

      <div className="import-tabs" role="tablist" aria-label="Разделы импорта Access">
        {importTabs.map((tab) => (
          <button
            key={tab.key}
            type="button"
            role="tab"
            aria-selected={activeImportTab === tab.key}
            className={activeImportTab === tab.key ? 'is-active' : undefined}
            onClick={() => setActiveImportTab(tab.key)}
          >
            <span>{tab.label}</span>
            <small>{tab.meta}</small>
          </button>
        ))}
      </div>

      <div className="import-tab-panel" role="tabpanel" aria-label={importTabs.find((tab) => tab.key === activeImportTab)?.label}>
        {activeImportTab === 'checks' ? (
        <div className="operation-list import-table import-table--checks" role="table" aria-label="Проверки импорта">
          <div className="operation-row header" role="row">
            <span role="columnheader">Проверка</span>
            <span role="columnheader">Статус</span>
            <span role="columnheader">Итог</span>
          </div>
          {!currentRun ? <p className="empty-state" role="status" aria-live="polite">Проверок пока нет</p> : null}
          {currentRun?.checks.map((check) => (
            <div className="operation-row" role="row" key={check.code}>
              <span role="cell">
                <strong>{check.title}</strong>
                <small>{check.message}</small>
              </span>
              <span role="cell" className={check.status === 'passed' ? 'status-active' : check.status === 'warning' ? 'warning-text' : 'status-disabled'}>
                {formatImportCheckStatus(check.status)}
              </span>
              <span role="cell">{currentRun.originalFileName}</span>
            </div>
          ))}
        </div>
        ) : null}

        {activeImportTab === 'log' ? (
        <div className="operation-list import-table import-table--log" role="table" aria-label="Лог запуска Access">
          <div className="operation-row header" role="row">
            <span role="columnheader">Шаг</span>
            <span role="columnheader">Уровень</span>
            <span role="columnheader">Сообщение</span>
          </div>
          {loadingLog ? <p className="empty-state" role="status" aria-live="polite">Загрузка лога...</p> : null}
          {!loadingLog && runLogEntries.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Лог выбранного запуска пока пуст</p> : null}
          {visibleRunLogEntries.map((entry) => (
            <div className="operation-row" role="row" key={entry.id}>
              <span role="cell">
                <strong>{entry.stepCode}</strong>
                <small>{formatDateTime(entry.createdAtUtc)}</small>
              </span>
              <span role="cell" className={entry.level === 'info' ? 'status-active' : entry.level === 'warning' ? 'warning-text' : 'status-disabled'}>
                {formatImportLogLevel(entry.level)}
              </span>
              <span role="cell">{entry.message}</span>
            </div>
          ))}
          {runLogEntries.length > visibleRunLogEntries.length ? <p className="empty-state" role="status" aria-live="polite">Показано {visibleRunLogEntries.length} из {runLogEntries.length} строк лога</p> : null}
        </div>
        ) : null}

        {activeImportTab === 'history' ? (
        <div className="operation-list import-table import-table--history" role="table" aria-label="История импорта Access">
          <div className="operation-row header" role="row">
            <span role="columnheader">Файл</span>
            <span role="columnheader">Статус</span>
            <span role="columnheader">Проверки</span>
          </div>
          {runs.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Истории импорта пока нет</p> : null}
          {visibleRuns.map((run) => (
            <button className="operation-row" role="row" type="button" key={run.id} onClick={() => setCurrentRun(run)}>
              <span role="cell">
                <strong>{run.originalFileName}</strong>
                <small>{run.summary}</small>
              </span>
              <span role="cell" className={run.status === 'completed' ? 'status-active' : 'status-disabled'}>
                {formatImportRunStatus(run.status)}
              </span>
              <span role="cell">
                {formatImportRunCheckSummary(run)}
              </span>
            </button>
          ))}
          {runs.length > visibleRuns.length ? <p className="empty-state" role="status" aria-live="polite">Показано {visibleRuns.length} из {runs.length} запусков</p> : null}
        </div>
        ) : null}

        {activeImportTab === 'quarantine' ? (
        <div className="operation-list import-table import-table--quarantine" role="table" aria-label="Карантин импорта Access">
          <div className="operation-row header" role="row">
            <span role="columnheader">Строка</span>
            <span role="columnheader">Причина</span>
            <span role="columnheader">Действие</span>
          </div>
          {quarantineItems.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Открытых строк карантина нет</p> : null}
          {visibleQuarantineItems.map((item) => (
            <div className="operation-row" role="row" key={item.id}>
              <span role="cell">
                <strong>{item.entityType}{item.externalId ? ` #${item.externalId}` : ''}</strong>
                <small>{item.sourceSystem} · {item.rowHash.slice(0, 12)}</small>
              </span>
              <span role="cell" className={item.severity === 'warning' ? 'warning-text' : 'status-disabled'}>
                <strong>{item.reasonCode}</strong>
                <small>{item.reasonMessage}</small>
              </span>
              <span role="cell">
                <button className="secondary-button" type="button" disabled={resolvingQuarantineId === item.id} onClick={() => void resolveQuarantineItem(item)}>
                  <Save size={16} />
                  <span>Закрыть</span>
                </button>
              </span>
            </div>
          ))}
          {quarantineItems.length > visibleQuarantineItems.length ? <p className="empty-state" role="status" aria-live="polite">Показано {visibleQuarantineItems.length} из {quarantineItems.length} строк карантина</p> : null}
        </div>
        ) : null}
      </div>
    </section>
  )
}

function AuditPanel({ auth, auditClient }: { auth: AuthResponse; auditClient: AuditClient }) {
  const [events, setEvents] = useState<AuditEventDto[]>([])
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const [exporting, setExporting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [exportMessage, setExportMessage] = useState<string | null>(null)

  useEffect(() => {
    let ignore = false

    async function load() {
      setLoading(true)
      setError(null)
      try {
        const loadedEvents = await auditClient.getEvents(auth.accessToken, { search, limit: auditScreenRequestLimit })
        if (!ignore) {
          setEvents(loadedEvents)
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить audit-журнал.')
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
  }, [auth.accessToken, auditClient, search])

  async function exportCurrentEvents() {
    setExporting(true)
    setError(null)
    setExportMessage(null)
    try {
      const blob = await auditClient.exportEvents(auth.accessToken, { search })
      downloadBlob(blob, buildAuditExportFileName())
      setExportMessage('Audit-журнал CSV готов.')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось скачать audit-журнал.')
    } finally {
      setExporting(false)
    }
  }

  const visibleEvents = events.slice(0, 12)

  return (
    <section className="dictionary-panel" aria-label="Audit-журнал">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Audit</p>
          <h2>Журнал действий пользователей и системы</h2>
        </div>
        <div className="section-actions">
          <span>{loading ? 'Загрузка...' : `${events.length} событий`}</span>
          <button className="secondary-button" type="button" disabled={exporting} onClick={exportCurrentEvents}>
            <FileSpreadsheet size={16} />
            Скачать CSV
          </button>
        </div>
      </div>

      {error ? <FormError>{error}</FormError> : null}
      {exportMessage ? <div className="form-note" role="status" aria-live="polite">{exportMessage}</div> : null}

      <form className="compact-form" onSubmit={(event) => event.preventDefault()}>
        <input aria-label="Поиск в audit-журнале" placeholder="Действие, сущность или описание" value={search} onChange={(event) => setSearch(event.target.value)} />
      </form>

      <div className="operation-list" role="table" aria-label="События audit-журнала">
        <div className="operation-row header" role="row">
          <span role="columnheader">Дата</span>
          <span role="columnheader">Событие</span>
          <span role="columnheader">Сущность</span>
        </div>
        {!loading && events.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Событий пока нет</p> : null}
        {visibleEvents.map((auditEvent) => (
          <div className="operation-row" role="row" key={auditEvent.id}>
            <span role="cell">{formatDateTime(auditEvent.createdAtUtc)}</span>
            <span role="cell">
              <strong>{auditEvent.action}</strong>
              <small>{auditEvent.summary}</small>
            </span>
            <span role="cell">
              <strong>{auditEvent.entityType}</strong>
              <small>{auditEvent.entityId ?? 'без идентификатора'}</small>
            </span>
          </div>
        ))}
        {events.length > visibleEvents.length ? <p className="empty-state" role="status" aria-live="polite">Показано {visibleEvents.length} из {events.length} событий</p> : null}
      </div>
    </section>
  )
}

function ReportPanel({ auth, dictionaryClient, reportClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient; reportClient: ReportClient }) {
  const today = getLocalDateInputValue()
  const month = `${today.slice(0, 7)}-01`
  const [filters, setFilters] = useState<ConsolidatedReportFilters>(() => loadConsolidatedReportFilters(month))
  const [incomeFilters, setIncomeFilters] = useState<IncomeReportFilters>(() => loadIncomeReportFilters(month, today))
  const [expenseFilters, setExpenseFilters] = useState<ExpenseReportFilters>(() => loadExpenseReportFilters(month, today))
  const [report, setReport] = useState<ConsolidatedReportDto | null>(null)
  const [incomeReport, setIncomeReport] = useState<IncomeReportDto | null>(null)
  const [expenseReport, setExpenseReport] = useState<ExpenseReportDto | null>(null)
  const [incomeGarages, setIncomeGarages] = useState<GarageDto[]>([])
  const [incomeOwners, setIncomeOwners] = useState<OwnerDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [loading, setLoading] = useState(true)
  const [exporting, setExporting] = useState(false)
  const [incomeLoading, setIncomeLoading] = useState(true)
  const [expenseLoading, setExpenseLoading] = useState(true)
  const [incomeExporting, setIncomeExporting] = useState(false)
  const [expenseExporting, setExpenseExporting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [incomeError, setIncomeError] = useState<string | null>(null)
  const [expenseError, setExpenseError] = useState<string | null>(null)
  const [exportMessage, setExportMessage] = useState<string | null>(null)
  const [exportError, setExportError] = useState<string | null>(null)
  const [reportValidationErrors, setReportValidationErrors] = useState<string[]>([])
  const [incomeReportValidationErrors, setIncomeReportValidationErrors] = useState<string[]>([])
  const [expenseReportValidationErrors, setExpenseReportValidationErrors] = useState<string[]>([])
  const [activeReportTab, setActiveReportTab] = useState<ReportTab>('consolidated')

  useEffect(() => {
    let ignore = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const loadedReport = await reportClient.getConsolidatedReport(auth.accessToken, {
          ...filters,
          limit: garageReportScreenRowLimit,
        })
        if (!ignore) {
          setReport(loadedReport)
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось сформировать отчет.')
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
  }, [auth.accessToken, filters, reportClient])

  useEffect(() => {
    let ignore = false
    async function loadIncomeReport() {
      setIncomeLoading(true)
      setIncomeError(null)
      try {
        const [loadedGarages, loadedOwners, loadedIncomeTypes, loadedIncomeReport] = await Promise.all([
          dictionaryClient.getGarages(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getOwners(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getIncomeTypes(auth.accessToken, dictionaryScreenRequestLimit),
          reportClient.getIncomeReport(auth.accessToken, {
            dateFrom: incomeFilters.dateFrom,
            dateTo: incomeFilters.dateTo,
            search: incomeFilters.search,
            garageIds: incomeFilters.garageIds,
            ownerIds: incomeFilters.ownerIds,
            incomeTypeIds: incomeFilters.incomeTypeIds,
            rowMode: incomeFilters.rowMode,
            limit: reportScreenRowLimit,
          }),
        ])
        if (!ignore) {
          setIncomeGarages(loadedGarages)
          setIncomeOwners(loadedOwners)
          setIncomeTypes(loadedIncomeTypes)
          setIncomeReport(loadedIncomeReport)
        }
      } catch (caught) {
        if (!ignore) {
          setIncomeError(caught instanceof Error ? caught.message : 'Не удалось сформировать отчет по поступлениям.')
        }
      } finally {
        if (!ignore) {
          setIncomeLoading(false)
        }
      }
    }

    void loadIncomeReport()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, dictionaryClient, incomeFilters, reportClient])

  useEffect(() => {
    let ignore = false
    async function loadExpenseReport() {
      setExpenseLoading(true)
      setExpenseError(null)
      try {
        const [loadedSuppliers, loadedExpenseTypes, loadedExpenseReport] = await Promise.all([
          dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getExpenseTypes(auth.accessToken, dictionaryScreenRequestLimit),
          reportClient.getExpenseReport(auth.accessToken, {
            dateFrom: expenseFilters.dateFrom,
            dateTo: expenseFilters.dateTo,
            search: expenseFilters.search,
            supplierIds: expenseFilters.supplierIds,
            expenseTypeIds: expenseFilters.expenseTypeIds,
            rowMode: expenseFilters.rowMode,
            limit: reportScreenRowLimit,
          }),
        ])
        if (!ignore) {
          setSuppliers(loadedSuppliers)
          setExpenseTypes(loadedExpenseTypes)
          setExpenseReport(loadedExpenseReport)
        }
      } catch (caught) {
        if (!ignore) {
          setExpenseError(caught instanceof Error ? caught.message : 'Не удалось сформировать отчет по выплатам.')
        }
      } finally {
        if (!ignore) {
          setExpenseLoading(false)
        }
      }
    }

    void loadExpenseReport()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, dictionaryClient, expenseFilters, reportClient])

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const nextFilters = {
      monthFrom: `${form.get('monthFrom')}-01`,
      monthTo: `${form.get('monthTo')}-01`,
      search: String(form.get('search') ?? ''),
    }
    const errors = getReportMonthRangeValidationErrors(nextFilters)
    if (errors.length > 0) {
      setError(null)
      setReportValidationErrors(errors)
      return
    }

    setReportValidationErrors([])
    setFilters(nextFilters)
    saveSessionJson(reportFilterStorageKeys.consolidated, nextFilters)
  }

  function applyIncomeFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const nextFilters = {
      dateFrom: String(form.get('dateFrom') ?? today),
      dateTo: String(form.get('dateTo') ?? today),
      search: String(form.get('search') ?? ''),
      garageIds: getFormValues(form, 'garageIds'),
      ownerIds: getFormValues(form, 'ownerIds'),
      incomeTypeIds: getFormValues(form, 'incomeTypeIds'),
      rowMode: String(form.get('rowMode') ?? 'all'),
    }
    const errors = getIncomeReportValidationErrors(nextFilters)
    if (errors.length > 0) {
      setIncomeError(null)
      setIncomeReportValidationErrors(errors)
      return
    }

    setIncomeReportValidationErrors([])
    setIncomeFilters(nextFilters)
    saveSessionJson(reportFilterStorageKeys.income, nextFilters)
  }

  function applyExpenseFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const nextFilters = {
      dateFrom: String(form.get('dateFrom') ?? today),
      dateTo: String(form.get('dateTo') ?? today),
      search: String(form.get('search') ?? ''),
      supplierIds: getFormValues(form, 'supplierIds'),
      expenseTypeIds: getFormValues(form, 'expenseTypeIds'),
      rowMode: String(form.get('rowMode') ?? 'all'),
    }
    const errors = getExpenseReportValidationErrors(nextFilters)
    if (errors.length > 0) {
      setExpenseError(null)
      setExpenseReportValidationErrors(errors)
      return
    }

    setExpenseReportValidationErrors([])
    setExpenseFilters(nextFilters)
    saveSessionJson(reportFilterStorageKeys.expense, nextFilters)
  }

  async function exportConsolidatedXlsx() {
    const errors = getReportMonthRangeValidationErrors(filters)
    if (errors.length > 0) {
      setExportError(null)
      setReportValidationErrors(errors)
      return
    }

    setReportValidationErrors([])
    setExporting(true)
    setExportError(null)
    setExportMessage(null)
    try {
      const blob = await reportClient.exportConsolidatedReportXlsx(auth.accessToken, filters)
      downloadBlob(blob, buildReportFileName('consolidated', filters.monthFrom, filters.monthTo, 'xlsx'))
      setExportMessage('XLSX по сводному отчету готов.')
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить XLSX по сводному отчету.')
    } finally {
      setExporting(false)
    }
  }

  async function exportConsolidatedPdf() {
    const errors = getReportMonthRangeValidationErrors(filters)
    if (errors.length > 0) {
      setExportError(null)
      setReportValidationErrors(errors)
      return
    }

    setReportValidationErrors([])
    setExporting(true)
    setExportError(null)
    setExportMessage(null)
    try {
      const blob = await reportClient.exportConsolidatedReportPdf(auth.accessToken, filters)
      downloadBlob(blob, buildReportFileName('consolidated', filters.monthFrom, filters.monthTo, 'pdf'))
      setExportMessage('PDF по сводному отчету готов.')
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить PDF по сводному отчету.')
    } finally {
      setExporting(false)
    }
  }

  async function exportIncomeXlsx() {
    const errors = getIncomeReportValidationErrors(incomeFilters)
    if (errors.length > 0) {
      setExportError(null)
      setIncomeReportValidationErrors(errors)
      return
    }

    setIncomeReportValidationErrors([])
    setIncomeExporting(true)
    setExportError(null)
    setExportMessage(null)
    try {
      const blob = await reportClient.exportIncomeReportXlsx(auth.accessToken, {
        dateFrom: incomeFilters.dateFrom,
        dateTo: incomeFilters.dateTo,
        search: incomeFilters.search,
        garageIds: incomeFilters.garageIds,
        ownerIds: incomeFilters.ownerIds,
        incomeTypeIds: incomeFilters.incomeTypeIds,
        rowMode: incomeFilters.rowMode,
      })
      downloadBlob(blob, buildReportFileName('income', incomeFilters.dateFrom, incomeFilters.dateTo, 'xlsx'))
      setExportMessage('XLSX по поступлениям готов.')
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить XLSX по поступлениям.')
    } finally {
      setIncomeExporting(false)
    }
  }

  async function exportIncomePdf() {
    const errors = getIncomeReportValidationErrors(incomeFilters)
    if (errors.length > 0) {
      setExportError(null)
      setIncomeReportValidationErrors(errors)
      return
    }

    setIncomeReportValidationErrors([])
    setIncomeExporting(true)
    setExportError(null)
    setExportMessage(null)
    try {
      const blob = await reportClient.exportIncomeReportPdf(auth.accessToken, {
        dateFrom: incomeFilters.dateFrom,
        dateTo: incomeFilters.dateTo,
        search: incomeFilters.search,
        garageIds: incomeFilters.garageIds,
        ownerIds: incomeFilters.ownerIds,
        incomeTypeIds: incomeFilters.incomeTypeIds,
        rowMode: incomeFilters.rowMode,
      })
      downloadBlob(blob, buildReportFileName('income', incomeFilters.dateFrom, incomeFilters.dateTo, 'pdf'))
      setExportMessage('PDF по поступлениям готов.')
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить PDF по поступлениям.')
    } finally {
      setIncomeExporting(false)
    }
  }

  async function exportExpenseXlsx() {
    const errors = getExpenseReportValidationErrors(expenseFilters)
    if (errors.length > 0) {
      setExportError(null)
      setExpenseReportValidationErrors(errors)
      return
    }

    setExpenseReportValidationErrors([])
    setExpenseExporting(true)
    setExportError(null)
    setExportMessage(null)
    try {
      const blob = await reportClient.exportExpenseReportXlsx(auth.accessToken, {
        dateFrom: expenseFilters.dateFrom,
        dateTo: expenseFilters.dateTo,
        search: expenseFilters.search,
        supplierIds: expenseFilters.supplierIds,
        expenseTypeIds: expenseFilters.expenseTypeIds,
        rowMode: expenseFilters.rowMode,
      })
      downloadBlob(blob, buildReportFileName('expense', expenseFilters.dateFrom, expenseFilters.dateTo, 'xlsx'))
      setExportMessage('XLSX по выплатам готов.')
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить XLSX по выплатам.')
    } finally {
      setExpenseExporting(false)
    }
  }

  async function exportExpensePdf() {
    const errors = getExpenseReportValidationErrors(expenseFilters)
    if (errors.length > 0) {
      setExportError(null)
      setExpenseReportValidationErrors(errors)
      return
    }

    setExpenseReportValidationErrors([])
    setExpenseExporting(true)
    setExportError(null)
    setExportMessage(null)
    try {
      const blob = await reportClient.exportExpenseReportPdf(auth.accessToken, {
        dateFrom: expenseFilters.dateFrom,
        dateTo: expenseFilters.dateTo,
        search: expenseFilters.search,
        supplierIds: expenseFilters.supplierIds,
        expenseTypeIds: expenseFilters.expenseTypeIds,
        rowMode: expenseFilters.rowMode,
      })
      downloadBlob(blob, buildReportFileName('expense', expenseFilters.dateFrom, expenseFilters.dateTo, 'pdf'))
      setExportMessage('PDF по выплатам готов.')
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить PDF по выплатам.')
    } finally {
      setExpenseExporting(false)
    }
  }

  const reportTabs: Array<{ key: ReportTab; label: string; meta: string }> = [
    { key: 'consolidated', label: 'Сводный', meta: loading ? 'Формируем...' : `${report?.monthlyRows.length ?? 0} месяцев` },
    { key: 'income', label: 'Поступления', meta: incomeLoading ? 'Формируем...' : `${incomeReport?.rowCount ?? 0} строк` },
    { key: 'expense', label: 'Выплаты', meta: expenseLoading ? 'Формируем...' : `${expenseReport?.rowCount ?? 0} строк` },
  ]
  const activeReportMeta = reportTabs.find((tab) => tab.key === activeReportTab)?.meta ?? ''

  return (
    <section className="dictionary-panel reports-panel" aria-label="Отчеты">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Отчеты</p>
          <h2>Отчетность ГСК</h2>
        </div>
        <span>{activeReportMeta}</span>
      </div>

      {exportError ? <FormError>{exportError}</FormError> : null}
      {exportMessage ? <div className="form-note" role="status" aria-live="polite">{exportMessage}</div> : null}

      <div className="report-tabs" role="tablist" aria-label="Разделы отчетов">
        {reportTabs.map((tab) => (
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

      {activeReportTab === 'consolidated' ? (
        <div className="report-tab-panel" role="tabpanel" id="report-panel-consolidated" aria-labelledby="report-tab-consolidated">
          <div className="report-card">
            <div className="report-card-heading">
              <div>
                <h3>Консолидированный отчет за период</h3>
                <p>Начисления попадают в сводный отчет по учетному месяцу, поступления и выплаты - по фактической дате операции.</p>
              </div>
            </div>

            {error ? <FormError>{error}</FormError> : null}

            <form className="compact-form report-filter report-filter--consolidated" onSubmit={applyFilters}>
              <input aria-label="Начало периода отчета" aria-describedby="consolidated-report-date-format" name="monthFrom" type="month" defaultValue={filters.monthFrom.slice(0, 7)} required />
              <input aria-label="Конец периода отчета" aria-describedby="consolidated-report-date-format" name="monthTo" type="month" defaultValue={filters.monthTo.slice(0, 7)} required />
              <input aria-label="Поиск в отчете" name="search" placeholder="Гараж или владелец" defaultValue={filters.search} />
              <FormValidationSummary title="Проверьте период отчета" items={reportValidationErrors} />
              <div className="report-actions">
                <button className="secondary-button" type="submit">
                  <Search size={16} />
                  <span>Сформировать</span>
                </button>
                <button className="secondary-button" type="button" onClick={exportConsolidatedXlsx} disabled={loading || exporting}>
                  <FileSpreadsheet size={16} />
                  <span>{exporting ? 'Готовим XLSX' : 'Скачать сводный XLSX'}</span>
                </button>
                <button className="secondary-button" type="button" onClick={exportConsolidatedPdf} disabled={loading || exporting}>
                  <FileText size={16} />
                  <span>{exporting ? 'Готовим PDF' : 'Скачать сводный PDF'}</span>
                </button>
              </div>
              <p className="form-hint report-date-format" id="consolidated-report-date-format">Формат периода сводного отчета: ММ.ГГГГ.</p>
            </form>

            <div className="summary-strip report-summary-strip" aria-label="Итоги отчета">
              <div>
                <span>Начислено</span>
                <strong>{formatMoney(report?.accrualTotal ?? 0)}</strong>
              </div>
              <div>
                <span>Поступило</span>
                <strong>{formatMoney(report?.incomeTotal ?? 0)}</strong>
              </div>
              <div>
                <span>{formatDebtLabel(report?.debt ?? 0)}</span>
                <strong className={getDebtClassName(report?.debt ?? 0)}>{formatDebtAmount(report?.debt ?? 0)}</strong>
              </div>
              <div>
                <span>Выплаты</span>
                <strong>{formatMoney(report?.expenseTotal ?? 0)}</strong>
              </div>
              <div>
                <span>Баланс</span>
                <strong>{formatMoney(report?.balance ?? 0)}</strong>
              </div>
            </div>
          </div>

          <div className="report-table-grid">
            <div className="operation-list report-table report-table--monthly" role="table" aria-label="Помесячный отчет">
              <div className="operation-row header" role="row">
                <span role="columnheader">Месяц</span>
                <span role="columnheader">Итоги</span>
                <span role="columnheader">Долг</span>
              </div>
              {report?.monthlyRows.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Помесячных строк отчета пока нет</p> : null}
              {report?.monthlyRows.map((row) => (
                <div className="operation-row" role="row" key={row.accountingMonth}>
                  <span role="cell">{formatMonth(row.accountingMonth)}</span>
                  <span role="cell">
                    <strong>{formatMoney(row.accrualTotal)} начислено</strong>
                    <small>
                      {formatMoney(row.incomeTotal)} поступило, {formatMoney(row.expenseTotal)} выплат
                    </small>
                  </span>
                  <span role="cell" className={getDebtClassName(row.debt)}>
                    {formatDebtAmount(row.debt)}
                  </span>
                </div>
              ))}
            </div>

            <div className="operation-list report-table report-table--garages" role="table" aria-label="Отчет по гаражам">
              <div className="operation-row header" role="row">
                <span role="columnheader">Гараж</span>
                <span role="columnheader">Начисления</span>
                <span role="columnheader">Долг</span>
              </div>
              {report?.garageRowCount === 0 ? <p className="empty-state" role="status" aria-live="polite">По выбранному фильтру гаражей нет</p> : null}
              {report?.garageRows.slice(0, garageReportScreenRowLimit).map((row) => (
                <div className="operation-row" role="row" key={row.garageId}>
                  <span role="cell">
                    <strong>Гараж {row.garageNumber}</strong>
                    <small>{row.ownerName ?? 'владелец не указан'}</small>
                  </span>
                  <span role="cell">
                    <strong>{formatMoney(row.accrualTotal)}</strong>
                    <small>{formatMoney(row.incomeTotal)} оплачено</small>
                  </span>
                  <span role="cell" className={getDebtClassName(row.debt)}>
                    {formatDebtAmount(row.debt)}
                  </span>
                </div>
              ))}
              {report && report.garageRowCount > report.garageRows.length ? <p className="empty-state" role="status" aria-live="polite">Показано {report.garageRows.length} из {report.garageRowCount} строк</p> : null}
            </div>
          </div>
        </div>
      ) : null}

      {activeReportTab === 'income' ? (
        <div className="report-tab-panel" role="tabpanel" id="report-panel-income" aria-labelledby="report-tab-income">
          <div className="report-card">
            <div className="report-card-heading">
              <div>
                <h3>Отчет по поступлениям</h3>
                <p>В поступлениях начисления считаются по учетному месяцу, оплаты - по фактической дате поступления.</p>
              </div>
            </div>

            {incomeError ? <FormError>{incomeError}</FormError> : null}

            <form className="compact-form report-filter report-filter--detailed" onSubmit={applyIncomeFilters}>
              <input aria-label="Начало отчета по поступлениям" aria-describedby="income-report-date-format" name="dateFrom" type="date" defaultValue={incomeFilters.dateFrom} required />
              <input aria-label="Конец отчета по поступлениям" aria-describedby="income-report-date-format" name="dateTo" type="date" defaultValue={incomeFilters.dateTo} required />
              <input aria-label="Поиск в поступлениях" name="search" placeholder="Гараж, владелец, документ" defaultValue={incomeFilters.search} />
              <select aria-label="Тип строк отчета по поступлениям" name="rowMode" defaultValue={incomeFilters.rowMode}>
                <option value="all">Начисления и оплаты</option>
                <option value="accruals">Только начисления</option>
                <option value="payments">Только оплаты</option>
              </select>
              <select aria-label="Гаражи в отчете по поступлениям" name="garageIds" multiple defaultValue={incomeFilters.garageIds} size={Math.min(4, Math.max(2, incomeGarages.length))}>
                {incomeGarages.map((garage) => (
                  <option value={garage.id} key={garage.id}>
                    Гараж {garage.number}
                  </option>
                ))}
              </select>
              <select aria-label="Владельцы в отчете по поступлениям" name="ownerIds" multiple defaultValue={incomeFilters.ownerIds} size={Math.min(4, Math.max(2, incomeOwners.length))}>
                {incomeOwners.map((owner) => (
                  <option value={owner.id} key={owner.id}>
                    {owner.fullName}
                  </option>
                ))}
              </select>
              <select aria-label="Виды поступлений в отчете" name="incomeTypeIds" multiple defaultValue={incomeFilters.incomeTypeIds} size={Math.min(4, Math.max(2, incomeTypes.length))}>
                {incomeTypes.map((incomeType) => (
                  <option value={incomeType.id} key={incomeType.id}>
                    {incomeType.name}
                  </option>
                ))}
              </select>
              <FormValidationSummary title="Проверьте отчет по поступлениям" items={incomeReportValidationErrors} />
              <div className="report-actions">
                <button className="secondary-button" type="submit">
                  <Search size={16} />
                  <span>Показать</span>
                </button>
                <button className="secondary-button" type="button" onClick={exportIncomeXlsx} disabled={incomeLoading || incomeExporting}>
                  <FileSpreadsheet size={16} />
                  <span>{incomeExporting ? 'Готовим XLSX' : 'Скачать поступления XLSX'}</span>
                </button>
                <button className="secondary-button" type="button" onClick={exportIncomePdf} disabled={incomeLoading || incomeExporting}>
                  <FileText size={16} />
                  <span>{incomeExporting ? 'Готовим PDF' : 'Скачать поступления PDF'}</span>
                </button>
              </div>
              <p className="form-hint report-date-format" id="income-report-date-format">Формат дат поступлений: ДД.ММ.ГГГГ.</p>
            </form>

            <div className="summary-strip report-summary-strip" aria-label="Итоги отчета по поступлениям">
              <div>
                <span>Начислено</span>
                <strong>{formatMoney(incomeReport?.accrualTotal ?? 0)}</strong>
              </div>
              <div>
                <span>Оплачено</span>
                <strong>{formatMoney(incomeReport?.incomeTotal ?? 0)}</strong>
              </div>
              <div>
                <span>{formatDebtLabel(incomeReport?.debt ?? 0)}</span>
                <strong className={getDebtClassName(incomeReport?.debt ?? 0)}>{formatDebtAmount(incomeReport?.debt ?? 0)}</strong>
              </div>
            </div>
          </div>

          <div className="operation-list report-table report-table--wide" role="table" aria-label="Отчет по поступлениям">
            <div className="operation-row header" role="row">
              <span role="columnheader">Дата</span>
              <span role="columnheader">Гараж и вид</span>
              <span role="columnheader">Сумма</span>
            </div>
            {incomeReport?.rows.length === 0 ? <p className="empty-state" role="status" aria-live="polite">По выбранному фильтру поступлений нет</p> : null}
            {incomeReport?.rows.map((row) => (
              <div className="operation-row" role="row" key={`${row.rowType}-${row.date}-${row.garageId}-${row.documentNumber ?? row.incomeTypeId}`}>
                <span role="cell">
                  <strong>{formatDateOnly(row.date)}</strong>
                  <small>{row.rowType === 'starting_balance' ? 'стартовый баланс' : row.rowType === 'accruals' ? 'начисление' : 'оплата'}</small>
                </span>
                <span role="cell">
                  <strong>Гараж {row.garageNumber} · {row.incomeTypeName}</strong>
                  <small>{row.ownerName ?? 'владелец не указан'}{row.documentNumber ? ` · ${row.documentNumber}` : ''}</small>
                </span>
                <span role="cell" className={row.rowType === 'payments' ? 'money-income' : 'money-accrual'}>
                  {row.rowType === 'payments' ? '+' : ''}{formatMoney(row.rowType === 'payments' ? row.incomeAmount : row.accrualAmount)}
                </span>
              </div>
            ))}
            {incomeReport && incomeReport.rowCount > incomeReport.rows.length ? <p className="empty-state" role="status" aria-live="polite">Показано {incomeReport.rows.length} из {incomeReport.rowCount} строк</p> : null}
          </div>
        </div>
      ) : null}

      {activeReportTab === 'expense' ? (
        <div className="report-tab-panel" role="tabpanel" id="report-panel-expense" aria-labelledby="report-tab-expense">
          <div className="report-card">
            <div className="report-card-heading">
              <div>
                <h3>Отчет по выплатам</h3>
                <p>В выплатах начисления поставщикам считаются по учетному месяцу, фактические выплаты - по дате оплаты.</p>
              </div>
            </div>

            {expenseError ? <FormError>{expenseError}</FormError> : null}

            <form className="compact-form report-filter report-filter--detailed report-filter--expense" onSubmit={applyExpenseFilters}>
              <input aria-label="Начало отчета по выплатам" aria-describedby="expense-report-date-format" name="dateFrom" type="date" defaultValue={expenseFilters.dateFrom} required />
              <input aria-label="Конец отчета по выплатам" aria-describedby="expense-report-date-format" name="dateTo" type="date" defaultValue={expenseFilters.dateTo} required />
              <input aria-label="Поиск в выплатах" name="search" placeholder="Поставщик, вид, документ" defaultValue={expenseFilters.search} />
              <select aria-label="Тип строк отчета по выплатам" name="rowMode" defaultValue={expenseFilters.rowMode}>
                <option value="all">Начисления и выплаты</option>
                <option value="accruals">Только начисления</option>
                <option value="payments">Только выплаты</option>
              </select>
              <select aria-label="Поставщики в отчете по выплатам" name="supplierIds" multiple defaultValue={expenseFilters.supplierIds} size={Math.min(4, Math.max(2, suppliers.length))}>
                {suppliers.map((supplier) => (
                  <option value={supplier.id} key={supplier.id}>
                    {supplier.name}
                  </option>
                ))}
              </select>
              <select aria-label="Виды выплат в отчете" name="expenseTypeIds" multiple defaultValue={expenseFilters.expenseTypeIds} size={Math.min(4, Math.max(2, expenseTypes.length))}>
                {expenseTypes.map((expenseType) => (
                  <option value={expenseType.id} key={expenseType.id}>
                    {expenseType.name}
                  </option>
                ))}
              </select>
              <FormValidationSummary title="Проверьте отчет по выплатам" items={expenseReportValidationErrors} />
              <div className="report-actions">
                <button className="secondary-button" type="submit">
                  <Search size={16} />
                  <span>Показать</span>
                </button>
                <button className="secondary-button" type="button" onClick={exportExpenseXlsx} disabled={expenseLoading || expenseExporting}>
                  <FileSpreadsheet size={16} />
                  <span>{expenseExporting ? 'Готовим XLSX' : 'Скачать выплаты XLSX'}</span>
                </button>
                <button className="secondary-button" type="button" onClick={exportExpensePdf} disabled={expenseLoading || expenseExporting}>
                  <FileText size={16} />
                  <span>{expenseExporting ? 'Готовим PDF' : 'Скачать выплаты PDF'}</span>
                </button>
              </div>
              <p className="form-hint report-date-format" id="expense-report-date-format">Формат дат выплат: ДД.ММ.ГГГГ.</p>
            </form>

            <div className="summary-strip report-summary-strip" aria-label="Итоги отчета по выплатам">
              <div>
                <span>Начислено</span>
                <strong>{formatMoney(expenseReport?.accrualTotal ?? 0)}</strong>
              </div>
              <div>
                <span>Выплачено</span>
                <strong>{formatMoney(expenseReport?.expenseTotal ?? 0)}</strong>
              </div>
              <div>
                <span>Разница</span>
                <strong>{formatMoney(expenseReport?.difference ?? 0)}</strong>
              </div>
            </div>
          </div>

          <div className="operation-list report-table report-table--wide" role="table" aria-label="Отчет по выплатам">
            <div className="operation-row header" role="row">
              <span role="columnheader">Дата</span>
              <span role="columnheader">Поставщик и вид</span>
              <span role="columnheader">Сумма</span>
            </div>
            {expenseReport?.rows.length === 0 ? <p className="empty-state" role="status" aria-live="polite">По выбранному фильтру выплат нет</p> : null}
            {expenseReport?.rows.map((row) => (
              <div className="operation-row" role="row" key={`${row.rowType}-${row.date}-${row.supplierId}-${row.documentNumber ?? row.expenseTypeId}`}>
                <span role="cell">
                  <strong>{formatDateOnly(row.date)}</strong>
                  <small>{row.rowType === 'starting_balance' ? 'стартовый баланс' : row.rowType === 'accruals' ? 'начисление' : 'выплата'}</small>
                </span>
                <span role="cell">
                  <strong>{row.supplierName} · {row.expenseTypeName}</strong>
                  <small>{row.documentNumber ?? 'документ не указан'}</small>
                </span>
                <span role="cell" className={row.rowType === 'payments' ? 'money-expense' : 'money-accrual'}>
                  {row.rowType === 'payments' ? '-' : ''}{formatMoney(row.rowType === 'payments' ? row.expenseAmount : row.accrualAmount)}
                </span>
              </div>
            ))}
            {expenseReport && expenseReport.rowCount > expenseReport.rows.length ? <p className="empty-state" role="status" aria-live="polite">Показано {expenseReport.rows.length} из {expenseReport.rowCount} строк</p> : null}
          </div>
        </div>
      ) : null}
    </section>
  )
}

type UserEditorState = { mode: 'create' | 'edit'; user?: ManagedUserDto }
type UserFormState = { email: string; displayName: string; password: string; roleCode: string; isActive: boolean }

const userPageSizeOptions = [10, 25, 50, 100]

function createEmptyUsersPage(limit = 25): PagedManagedUsersDto {
  return { items: [], totalCount: 0, offset: 0, limit }
}

function getPrimaryRoleCode(user: ManagedUserDto | undefined, roles: ManagedRoleDto[]) {
  return user?.roles[0] ?? roles[0]?.code ?? ''
}

function getRoleLabel(roleCode: string, roles: ManagedRoleDto[]) {
  return roles.find((role) => role.code === roleCode)?.name ?? roleCode
}

function getUserEditorValidationErrors(form: UserFormState, mode: 'create' | 'edit') {
  if (mode === 'create') {
    return getManagedUserValidationErrors(form.email, form.displayName, form.password, form.roleCode)
  }

  const errors: string[] = []
  if (!form.displayName.trim()) {
    errors.push('Укажите имя пользователя.')
  }

  if (!form.roleCode) {
    errors.push('Выберите роль пользователя.')
  }

  if (form.password.trim()) {
    errors.push(...getPasswordPolicyErrors(form.password, 'Укажите новый пароль или оставьте поле пустым.'))
  }

  return errors
}

function UserManagementPanel({ auth, userClient }: { auth: AuthResponse; userClient: UserManagementClient }) {
  const [roles, setRoles] = useState<ManagedRoleDto[]>([])
  const [page, setPage] = useState<PagedManagedUsersDto>(() => createEmptyUsersPage())
  const [searchDraft, setSearchDraft] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [offset, setOffset] = useState(0)
  const [pageSize, setPageSize] = useState(25)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  const [toast, setToast] = useState<{ id: number; text: string; kind: 'success' | 'error' } | null>(null)
  const [contextMenu, setContextMenu] = useState<{ user: ManagedUserDto; x: number; y: number } | null>(null)
  const [editor, setEditor] = useState<UserEditorState | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ManagedUserDto | null>(null)
  const [form, setForm] = useState<UserFormState>({ email: '', displayName: '', password: '', roleCode: 'operator', isActive: true })
  const editorCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(editor))
  const editorDialogRef = useFocusTrap<HTMLElement>(Boolean(editor))
  const deleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(deleteTarget))
  const deleteDialogRef = useFocusTrap<HTMLElement>(Boolean(deleteTarget))

  useEscapeKey(Boolean(contextMenu), () => setContextMenu(null))
  useEscapeKey(Boolean(editor), () => closeEditor())
  useEscapeKey(Boolean(deleteTarget), () => setDeleteTarget(null))

  function showToast(text: string, kind: 'success' | 'error' = 'success') {
    const id = Date.now()
    setToast({ id, text, kind })
    window.setTimeout(() => {
      setToast((current) => (current?.id === id ? null : current))
    }, 3200)
  }

  async function refreshUsers() {
    setLoading(true)
    setError(null)
    try {
      const [loadedRoles, loadedPage] = await Promise.all([
        userClient.getRoles(auth.accessToken),
        userClient.getUsersPage(auth.accessToken, appliedSearch, offset, pageSize),
      ])
      setRoles(loadedRoles)
      setPage(loadedPage)
      setForm((value) => ({ ...value, roleCode: loadedRoles.find((role) => role.code === value.roleCode)?.code ?? loadedRoles[0]?.code ?? '' }))
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось загрузить пользователей.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    let ignore = false

    async function loadUsers() {
      setLoading(true)
      setError(null)
      try {
        const [loadedRoles, loadedPage] = await Promise.all([
          userClient.getRoles(auth.accessToken),
          userClient.getUsersPage(auth.accessToken, appliedSearch, offset, pageSize),
        ])
        if (!ignore) {
          setRoles(loadedRoles)
          setPage(loadedPage)
          setForm((value) => ({ ...value, roleCode: loadedRoles.find((role) => role.code === value.roleCode)?.code ?? loadedRoles[0]?.code ?? '' }))
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить пользователей.')
        }
      } finally {
        if (!ignore) {
          setLoading(false)
        }
      }
    }

    void loadUsers()
    return () => {
      ignore = true
    }
  }, [appliedSearch, auth.accessToken, offset, pageSize, userClient])

  function openEditor(mode: 'create' | 'edit', user?: ManagedUserDto) {
    setContextMenu(null)
    setValidationErrors([])
    setError(null)
    setEditor({ mode, user })
    setForm({
      email: user?.email ?? '',
      displayName: user?.displayName ?? '',
      password: '',
      roleCode: getPrimaryRoleCode(user, roles),
      isActive: user?.isActive ?? true,
    })
  }

  function closeEditor() {
    setEditor(null)
    setValidationErrors([])
  }

  async function saveUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!editor) {
      return
    }

    const errors = getUserEditorValidationErrors(form, editor.mode)
    if (errors.length > 0) {
      setValidationErrors(errors)
      return
    }

    setValidationErrors([])
    setSaving(editor.mode)
    setError(null)
    try {
      if (editor.mode === 'create') {
        const request: CreateManagedUserRequest = {
          email: form.email,
          displayName: form.displayName,
          password: form.password,
          roleCodes: [form.roleCode],
          isActive: form.isActive,
        }
        await userClient.createUser(auth.accessToken, request)
        setOffset(0)
        showToast('Пользователь добавлен.')
      } else if (editor.user) {
        const request: UpdateManagedUserRequest = {
          displayName: form.displayName,
          roleCodes: [form.roleCode],
          isActive: form.isActive,
          newPassword: form.password.trim() ? form.password : null,
        }
        await userClient.updateUser(auth.accessToken, editor.user.id, request)
        showToast('Пользователь изменен.')
      }

      closeEditor()
      await refreshUsers()
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить пользователя.'
      setError(message)
      showToast(message, 'error')
    } finally {
      setSaving(null)
    }
  }

  async function deleteUser() {
    if (!deleteTarget) {
      return
    }

    setSaving('delete')
    setError(null)
    try {
      await userClient.updateUser(auth.accessToken, deleteTarget.id, {
        displayName: deleteTarget.displayName,
        roleCodes: [getPrimaryRoleCode(deleteTarget, roles)],
        isActive: false,
        newPassword: null,
      })
      setDeleteTarget(null)
      showToast('Пользователь отключен.')
      await refreshUsers()
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось отключить пользователя.'
      setError(message)
      showToast(message, 'error')
    } finally {
      setSaving(null)
    }
  }

  function submitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setAppliedSearch(searchDraft.trim())
    setOffset(0)
  }

  const pageStart = page.totalCount === 0 ? 0 : page.offset + 1
  const pageEnd = Math.min(page.offset + page.items.length, page.totalCount)
  const canGoPrev = page.offset > 0
  const canGoNext = page.offset + page.limit < page.totalCount

  return (
    <section className="dictionary-panel-v2 users-panel-v2" aria-label="Пользователи" onClick={() => setContextMenu(null)}>
      <div className="section-heading">
        <div>
          <p className="eyebrow">Пользователи</p>
          <h2>Доступ в систему и роли сотрудников</h2>
        </div>
        <span>{loading ? 'Загрузка...' : `${page.totalCount} пользователей`}</span>
      </div>

      {error ? <FormError>{error}</FormError> : null}

      <div className="users-workbench">
        <div className="dictionary-table-shell">
          <form className="dictionary-toolbar" onSubmit={submitSearch}>
            <input aria-label="Поиск пользователей" placeholder="Email, имя или роль" value={searchDraft} onChange={(event) => setSearchDraft(event.target.value)} />
            <button className="ghost-button" type="submit" disabled={loading}>
              <Search size={16} />
              <span>Найти</span>
            </button>
          </form>

          <div className="dictionary-toolbar users-toolbar-actions">
            <span className="form-hint">Действия по строкам доступны через ПКМ.</span>
            <button className="secondary-button" type="button" onClick={() => openEditor('create')} disabled={roles.length === 0}>
              <Plus size={16} />
              <span>Добавить</span>
            </button>
          </div>

          <div className="dictionary-table-scroll">
            <table className="dictionary-data-table users-data-table" aria-label="Список пользователей" onContextMenu={(event) => event.preventDefault()}>
              <thead>
                <tr>
                  <th>Сотрудник</th>
                  <th>Email</th>
                  <th>Роль</th>
                  <th>Статус</th>
                  <th>Последний вход</th>
                </tr>
              </thead>
              <tbody>
                {page.items.map((managedUser) => (
                  <tr
                    key={managedUser.id}
                    tabIndex={0}
                    onContextMenu={(event) => {
                      event.preventDefault()
                      event.stopPropagation()
                      setContextMenu({ user: managedUser, x: event.clientX, y: event.clientY })
                    }}
                  >
                    <td><strong>{managedUser.displayName}</strong></td>
                    <td>{managedUser.email}</td>
                    <td>{managedUser.roles.map((role) => getRoleLabel(role, roles)).join(', ')}</td>
                    <td><span className={managedUser.isActive ? 'status-active' : 'status-disabled'}>{managedUser.isActive ? 'Активен' : 'Отключен'}</span></td>
                    <td>{managedUser.lastLoginAtUtc ? formatDateTime(managedUser.lastLoginAtUtc) : 'Не входил'}</td>
                  </tr>
                ))}
                {!loading && page.items.length === 0 ? (
                  <tr>
                    <td colSpan={5}>
                      <p className="empty-state" role="status" aria-live="polite">Пользователей пока нет</p>
                    </td>
                  </tr>
                ) : null}
              </tbody>
            </table>
            {loading ? <p className="empty-state" role="status" aria-live="polite">Загружаем пользователей...</p> : null}
          </div>

          <div className="dictionary-pagination">
            <span role="status" aria-live="polite">Показано {pageStart}-{pageEnd} из {page.totalCount}</span>
            <label>
              Строк:
              <select aria-label="Количество строк пользователей" value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setOffset(0) }}>
                {userPageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
              </select>
            </label>
            <button className="ghost-button" type="button" onClick={() => setOffset(Math.max(0, offset - pageSize))} disabled={!canGoPrev || loading}>Назад</button>
            <button className="ghost-button" type="button" onClick={() => setOffset(offset + pageSize)} disabled={!canGoNext || loading}>Вперед</button>
          </div>
        </div>
      </div>

      <RolePermissionMatrix roles={roles} />

      {contextMenu ? (
        <div className="context-menu" role="menu" style={{ left: contextMenu.x, top: contextMenu.y }} onClick={(event) => event.stopPropagation()}>
          <button type="button" role="menuitem" onClick={() => openEditor('create')}>
            <Plus size={15} />
            <span>Добавить</span>
          </button>
          <button type="button" role="menuitem" onClick={() => openEditor('edit', contextMenu.user)}>
            <Save size={15} />
            <span>Изменить</span>
          </button>
          <button type="button" role="menuitem" onClick={() => { setDeleteTarget(contextMenu.user); setContextMenu(null) }} disabled={!contextMenu.user.isActive}>
            <Trash2 size={15} />
            <span>Удалить</span>
          </button>
        </div>
      ) : null}

      {editor ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeEditor}>
          <section ref={editorDialogRef} className="detail-dialog dictionary-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="user-editor-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <h3 id="user-editor-title">{editor.mode === 'create' ? 'Новый пользователь' : 'Изменить пользователя'}</h3>
                <p>{editor.mode === 'create' ? 'Создайте сотрудника и назначьте роль.' : 'Измените имя, роль, статус или задайте новый пароль.'}</p>
              </div>
              <button ref={editorCloseRef} className="icon-button" type="button" onClick={closeEditor} aria-label="Закрыть окно пользователя">
                <X size={18} />
              </button>
            </div>
            <form className="dictionary-modal-form" onSubmit={saveUser}>
              {editor.mode === 'create' ? (
                <input aria-label="Email пользователя" placeholder="email@example.com" value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} type="email" required />
              ) : (
                <input aria-label="Email пользователя" value={form.email} disabled />
              )}
              <input aria-label="Имя пользователя" placeholder="Имя" value={form.displayName} onChange={(event) => setForm({ ...form, displayName: event.target.value })} required />
              <select aria-label="Роль пользователя" value={form.roleCode} onChange={(event) => setForm({ ...form, roleCode: event.target.value })} required>
                {roles.map((role) => (
                  <option value={role.code} key={role.code}>{role.name}</option>
                ))}
              </select>
              <select aria-label="Статус пользователя" value={form.isActive ? 'active' : 'inactive'} onChange={(event) => setForm({ ...form, isActive: event.target.value === 'active' })}>
                <option value="active">Активен</option>
                <option value="inactive">Отключен</option>
              </select>
              <input
                aria-label="Пароль пользователя"
                aria-describedby="new-user-password-policy-hint"
                placeholder={editor.mode === 'create' ? 'Пароль' : 'Новый пароль, если нужно изменить'}
                value={form.password}
                onChange={(event) => setForm({ ...form, password: event.target.value })}
                type="password"
                minLength={editor.mode === 'create' ? 8 : undefined}
                required={editor.mode === 'create'}
              />
              <p className="form-hint" id="new-user-password-policy-hint">Минимум 8 символов: заглавная буква, строчная буква и цифра.</p>
              <FormValidationSummary title={editor.mode === 'create' ? 'Проверьте нового пользователя' : 'Проверьте пользователя'} items={validationErrors} />
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={closeEditor}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={saving !== null || roles.length === 0}>
                  <Save size={16} />
                  <span>Сохранить</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}

      {deleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setDeleteTarget(null)}>
          <section ref={deleteDialogRef} className="detail-dialog dictionary-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="user-delete-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <h3 id="user-delete-title">Удалить пользователя</h3>
                <p>{deleteTarget.displayName} будет отключен и не сможет входить в систему. Audit-история сохранится.</p>
              </div>
              <button ref={deleteCancelRef} className="icon-button" type="button" onClick={() => setDeleteTarget(null)} aria-label="Закрыть подтверждение удаления">
                <X size={18} />
              </button>
            </div>
            <div className="detail-dialog-actions">
              <button className="ghost-button" type="button" onClick={() => setDeleteTarget(null)}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={deleteUser} disabled={saving === 'delete'}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {toast ? <div className={`toast-message ${toast.kind === 'error' ? 'toast-message--error' : ''}`} role="status" aria-live="polite">{toast.text}</div> : null}
    </section>
  )
}

function RolePermissionMatrix({ roles }: { roles: ManagedRoleDto[] }) {
  return (
    <section className="role-matrix" aria-label="Матрица ролей">
      <div className="section-heading compact-heading">
        <div>
          <p className="eyebrow">Роли и права</p>
          <h3>Матрица доступов</h3>
        </div>
        <span>{roles.length} ролей</span>
      </div>

      <div className="role-matrix-table" role="table" aria-label="Матрица ролей и прав">
        <div className="role-matrix-row header" role="row">
          <span role="columnheader">Роль</span>
          {rolePermissionGroups.map((group) => (
            <span role="columnheader" key={group.permission}>{group.label}</span>
          ))}
        </div>
        {roles.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Роли пока не загружены</p> : null}
        {roles.map((role) => (
          <div className="role-matrix-row" role="row" key={role.code}>
            <span role="cell">
              <strong>{role.name}</strong>
              <small>{role.code}</small>
            </span>
            {rolePermissionGroups.map((group) => {
              const allowed = role.permissions.includes(group.permission)
              return (
                <span role="cell" aria-label={`${role.name}: ${group.label} - ${allowed ? 'разрешено' : 'нет доступа'}`} className={allowed ? 'status-active' : 'status-disabled'} key={group.permission}>
                  {allowed ? 'Да' : 'Нет'}
                </span>
              )
            })}
          </div>
        ))}
      </div>
    </section>
  )
}

type DictionarySectionKey = 'owners' | 'garages' | 'supplierGroups' | 'suppliers' | 'incomeTypes' | 'expenseTypes' | 'tariffs'

type DictionaryRecord = OwnerDto | GarageDto | SupplierGroupDto | SupplierDto | AccountingTypeDto | TariffDto

type DictionarySectionGroupKey = 'counterparties' | 'operations' | 'tariffs'

type DictionarySectionOption = {
  key: DictionarySectionKey
  label: string
  group: DictionarySectionGroupKey
  writePermission: 'dictionaries' | 'tariffs'
}

const dictionarySectionGroups: { key: DictionarySectionGroupKey; label: string }[] = [
  { key: 'counterparties', label: 'Контрагенты' },
  { key: 'operations', label: 'Операции' },
  { key: 'tariffs', label: 'Тарифы' },
]

const dictionarySectionOptions: DictionarySectionOption[] = [
  { key: 'owners', label: 'Владельцы', group: 'counterparties', writePermission: 'dictionaries' },
  { key: 'garages', label: 'Гаражи', group: 'counterparties', writePermission: 'dictionaries' },
  { key: 'supplierGroups', label: 'Группы поставщиков и персонала', group: 'counterparties', writePermission: 'dictionaries' },
  { key: 'suppliers', label: 'Поставщики и персонал', group: 'counterparties', writePermission: 'dictionaries' },
  { key: 'incomeTypes', label: 'Виды поступлений', group: 'operations', writePermission: 'dictionaries' },
  { key: 'expenseTypes', label: 'Виды выплат', group: 'operations', writePermission: 'dictionaries' },
  { key: 'tariffs', label: 'Тарифы', group: 'tariffs', writePermission: 'tariffs' },
]

const dictionaryPageSizeOptions = [10, 25, 50, 100]

type OwnerGarageLinkForm = {
  existingGarageId: string
  newGarageNumber: string
  peopleCount: number
  floorCount: number
  startingBalance: number
  initialWaterMeterValue: string
  initialElectricityMeterValue: string
  comment: string
}

function createEmptyOwnerGarageLinkForm(): OwnerGarageLinkForm {
  return {
    existingGarageId: '',
    newGarageNumber: '',
    peopleCount: 1,
    floorCount: 1,
    startingBalance: 0,
    initialWaterMeterValue: '',
    initialElectricityMeterValue: '',
    comment: '',
  }
}

function createEmptyDictionaryPage<TItem>(limit = 25): PagedResult<TItem> {
  return { items: [], totalCount: 0, offset: 0, limit }
}

function createFallbackPage<TItem>(items: TItem[], offset: number, limit: number): PagedResult<TItem> {
  return { items: items.slice(offset, offset + limit), totalCount: items.length, offset, limit }
}

function DictionaryPanelV2({ auth, dictionaryClient, financeClient, initialSection }: { auth: AuthResponse; dictionaryClient: DictionaryClient; financeClient: FinanceClient; initialSection: DictionarySectionKey }) {
  const [activeSection, setActiveSection] = useState<DictionarySectionKey>(initialSection)
  const [owners, setOwners] = useState<OwnerDto[]>([])
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [groups, setGroups] = useState<SupplierGroupDto[]>([])
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [tariffs, setTariffs] = useState<TariffDto[]>([])
  const [ownerOptions, setOwnerOptions] = useState<OwnerDto[]>([])
  const [garageOptions, setGarageOptions] = useState<GarageDto[]>([])
  const [groupOptions, setGroupOptions] = useState<SupplierGroupDto[]>([])
  const [pages, setPages] = useState<Record<DictionarySectionKey, PagedResult<DictionaryRecord>>>({
    owners: createEmptyDictionaryPage<DictionaryRecord>(),
    garages: createEmptyDictionaryPage<DictionaryRecord>(),
    supplierGroups: createEmptyDictionaryPage<DictionaryRecord>(),
    suppliers: createEmptyDictionaryPage<DictionaryRecord>(),
    incomeTypes: createEmptyDictionaryPage<DictionaryRecord>(),
    expenseTypes: createEmptyDictionaryPage<DictionaryRecord>(),
    tariffs: createEmptyDictionaryPage<DictionaryRecord>(),
  })
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<{ id: number; text: string; kind: 'success' | 'error' } | null>(null)
  const [contextMenu, setContextMenu] = useState<{ section: DictionarySectionKey; item: DictionaryRecord; x: number; y: number } | null>(null)
  const [editor, setEditor] = useState<{ section: DictionarySectionKey; mode: 'create' | 'edit'; item?: DictionaryRecord } | null>(null)
  const [archiveTarget, setArchiveTarget] = useState<{ section: DictionarySectionKey; item: DictionaryRecord } | null>(null)
  const [balanceHistoryGarage, setBalanceHistoryGarage] = useState<GarageDto | null>(null)
  const [balanceHistory, setBalanceHistory] = useState<GarageBalanceHistoryDto | null>(null)
  const [balanceHistoryFilters, setBalanceHistoryFilters] = useState(() => createDefaultGarageBalanceHistoryFilters())
  const [balanceHistoryLoading, setBalanceHistoryLoading] = useState(false)
  const [balanceHistoryError, setBalanceHistoryError] = useState<string | null>(null)
  const [ownerForm, setOwnerForm] = useState<UpsertOwnerRequest>({ lastName: '', firstName: '', middleName: '', phone: '', address: '', meterNotes: '' })
  const [ownerGarageLinkForm, setOwnerGarageLinkForm] = useState<OwnerGarageLinkForm>(createEmptyOwnerGarageLinkForm())
  const [garageForm, setGarageForm] = useState({ number: '', peopleCount: 1, floorCount: 1, ownerId: '', startingBalance: 0, initialWaterMeterValue: '', initialElectricityMeterValue: '', comment: '' })
  const [supplierGroupName, setSupplierGroupName] = useState('')
  const [supplierForm, setSupplierForm] = useState({ name: '', groupId: '', inn: '', legalAddress: '', contactPerson: '', phone: '', email: '', startingBalance: 0, comment: '' })
  const [accountingTypeForm, setAccountingTypeForm] = useState({ name: '', code: '' })
  const [tariffForm, setTariffForm] = useState<UpsertTariffRequest>({ name: '', calculationBase: 'fixed', rate: 1, effectiveFrom: '2026-07-01', comment: '' })
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  const editorCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(editor))
  const editorDialogRef = useFocusTrap<HTMLElement>(Boolean(editor))
  const archiveCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(archiveTarget))
  const archiveDialogRef = useFocusTrap<HTMLElement>(Boolean(archiveTarget))
  const balanceHistoryCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(balanceHistoryGarage))
  const balanceHistoryDialogRef = useFocusTrap<HTMLElement>(Boolean(balanceHistoryGarage))
  const canWriteDictionaries = hasPermission(auth, permissions.dictionariesWrite)
  const canManageTariffs = hasPermission(auth, permissions.tariffsManage)
  const activePage = pages[activeSection]
  const activeOption = dictionarySectionOptions.find((item) => item.key === activeSection) ?? dictionarySectionOptions[0]
  const canWriteActiveSection = activeOption.writePermission === 'tariffs' ? canManageTariffs : canWriteDictionaries
  const supportsSearch = supportsSearchFor(activeSection)
  const searchPlaceholder = activeSection === 'garages'
    ? 'Номер гаража или ФИО владельца'
    : activeSection === 'suppliers'
      ? 'Название, ИНН или контакт'
      : activeSection === 'owners'
        ? 'ФИО или телефон'
        : activeSection === 'tariffs'
          ? 'Название или база расчета'
          : 'Поиск для этого справочника пока не применяется'
  const ownerGarageOptions = garageOptions.filter((garage) => {
    if (!garage.ownerId) {
      return true
    }

    if (editor?.section === 'owners' && editor.item) {
      return garage.ownerId === (editor.item as OwnerDto).id
    }

    return false
  })

  useEscapeKey(Boolean(contextMenu), () => setContextMenu(null))
  useEscapeKey(Boolean(editor), () => closeEditor())
  useEscapeKey(Boolean(archiveTarget), () => setArchiveTarget(null))
  useEscapeKey(Boolean(balanceHistoryGarage), () => closeBalanceHistory())

  useEffect(() => {
    if (!toast) {
      return undefined
    }

    const timeoutId = window.setTimeout(() => setToast(null), 3200)
    return () => window.clearTimeout(timeoutId)
  }, [toast])

  useEffect(() => {
    function closeMenu() {
      setContextMenu(null)
    }

    window.addEventListener('click', closeMenu)
    return () => window.removeEventListener('click', closeMenu)
  }, [])

  useEffect(() => {
    let ignore = false
    async function loadReferences() {
      try {
        const [loadedOwners, loadedGarages, loadedGroups] = await Promise.all([
          dictionaryClient.getOwners(auth.accessToken, undefined, 500),
          dictionaryClient.getGarages(auth.accessToken, undefined, 500),
          dictionaryClient.getSupplierGroups(auth.accessToken, 500),
        ])
        if (!ignore) {
          setOwnerOptions(loadedOwners)
          setGarageOptions(loadedGarages)
          setGroupOptions(loadedGroups)
        }
      } catch {
        if (!ignore) {
          setError('Не удалось загрузить справочные значения для форм.')
        }
      }
    }

    void loadReferences()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => {
    let ignore = false
    const timeoutId = window.setTimeout(() => {
      const page = pages[activeSection]
      setLoading(true)
      setError(null)
      loadPage(activeSection, 0, page.limit)
        .catch((caught) => {
          if (!ignore) {
            const message = caught instanceof Error ? caught.message : 'Не удалось загрузить таблицу справочника.'
            setError(message)
            showToast(message, 'error')
          }
        })
        .finally(() => {
          if (!ignore) {
            setLoading(false)
          }
        })
    }, supportsSearch && search.trim() ? 250 : 0)

    return () => {
      ignore = true
      window.clearTimeout(timeoutId)
    }
    // The loader intentionally captures the current page settings for the active dictionary section.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeSection, auth.accessToken, dictionaryClient, search])

  async function loadPage(section: DictionarySectionKey, offset = pages[section].offset, limit = pages[section].limit) {
    const query = supportsSearchFor(section) ? search.trim() || undefined : undefined
    let page: PagedResult<DictionaryRecord>
    if (section === 'owners') {
      page = dictionaryClient.getOwnersPage
        ? await dictionaryClient.getOwnersPage(auth.accessToken, query, offset, limit)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getOwners(auth.accessToken, query, 500), offset, limit)
      setOwners(page.items as OwnerDto[])
    } else if (section === 'garages') {
      page = dictionaryClient.getGaragesPage
        ? await dictionaryClient.getGaragesPage(auth.accessToken, query, offset, limit)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getGarages(auth.accessToken, query, 500), offset, limit)
      setGarages(page.items as GarageDto[])
    } else if (section === 'supplierGroups') {
      page = dictionaryClient.getSupplierGroupsPage
        ? await dictionaryClient.getSupplierGroupsPage(auth.accessToken, offset, limit)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getSupplierGroups(auth.accessToken, 500), offset, limit)
      setGroups(page.items as SupplierGroupDto[])
    } else if (section === 'suppliers') {
      page = dictionaryClient.getSuppliersPage
        ? await dictionaryClient.getSuppliersPage(auth.accessToken, undefined, query, offset, limit)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getSuppliers(auth.accessToken, undefined, query, 500), offset, limit)
      setSuppliers(page.items as SupplierDto[])
    } else if (section === 'incomeTypes') {
      page = dictionaryClient.getIncomeTypesPage
        ? await dictionaryClient.getIncomeTypesPage(auth.accessToken, offset, limit)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getIncomeTypes(auth.accessToken, 500), offset, limit)
      setIncomeTypes(page.items as AccountingTypeDto[])
    } else if (section === 'expenseTypes') {
      page = dictionaryClient.getExpenseTypesPage
        ? await dictionaryClient.getExpenseTypesPage(auth.accessToken, offset, limit)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getExpenseTypes(auth.accessToken, 500), offset, limit)
      setExpenseTypes(page.items as AccountingTypeDto[])
    } else {
      page = dictionaryClient.getTariffsPage
        ? await dictionaryClient.getTariffsPage(auth.accessToken, query, offset, limit)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getTariffs(auth.accessToken, query, 500), offset, limit)
      setTariffs(page.items as TariffDto[])
    }

    setPages((current) => ({ ...current, [section]: page }))
  }

  function supportsSearchFor(section: DictionarySectionKey) {
    return section === 'owners' || section === 'garages' || section === 'suppliers' || section === 'tariffs'
  }

  function showToast(text: string, kind: 'success' | 'error' = 'success') {
    setToast({ id: Date.now(), text, kind })
  }

  function openContextMenu(event: MouseEvent, section: DictionarySectionKey, item: DictionaryRecord) {
    event.preventDefault()
    setContextMenu({ section, item, x: event.clientX, y: event.clientY })
  }

  async function openBalanceHistory(garage: GarageDto) {
    const filters = createDefaultGarageBalanceHistoryFilters()
    setContextMenu(null)
    setBalanceHistoryGarage(garage)
    setBalanceHistoryFilters(filters)
    await loadBalanceHistory(garage.id, filters)
  }

  function closeBalanceHistory() {
    setBalanceHistoryGarage(null)
    setBalanceHistory(null)
    setBalanceHistoryError(null)
  }

  async function loadBalanceHistory(garageId = balanceHistoryGarage?.id, filters = balanceHistoryFilters) {
    if (!garageId) {
      return
    }

    setBalanceHistoryLoading(true)
    setBalanceHistoryError(null)
    try {
      const history = await financeClient.getGarageBalanceHistory(auth.accessToken, garageId, filters)
      setBalanceHistory(history)
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось загрузить историю баланса гаража.'
      setBalanceHistory(null)
      setBalanceHistoryError(message)
      showToast(message, 'error')
    } finally {
      setBalanceHistoryLoading(false)
    }
  }

  function openEditor(section: DictionarySectionKey, mode: 'create' | 'edit', item?: DictionaryRecord) {
    setValidationErrors([])
    setError(null)
    setContextMenu(null)
    if (mode === 'edit' && item) {
      if (section === 'owners') {
        const owner = item as OwnerDto
        setOwnerForm({ lastName: owner.lastName, firstName: owner.firstName, middleName: owner.middleName ?? '', phone: owner.phone ?? '', address: owner.address ?? '', meterNotes: owner.meterNotes ?? '' })
        setOwnerGarageLinkForm({ ...createEmptyOwnerGarageLinkForm(), existingGarageId: garageOptions.find((garage) => garage.ownerId === owner.id)?.id ?? '' })
      } else if (section === 'garages') {
        const garage = item as GarageDto
        setGarageForm({ number: garage.number, peopleCount: garage.peopleCount, floorCount: garage.floorCount, ownerId: garage.ownerId ?? '', startingBalance: garage.startingBalance, initialWaterMeterValue: garage.initialWaterMeterValue?.toString() ?? '', initialElectricityMeterValue: garage.initialElectricityMeterValue?.toString() ?? '', comment: garage.comment ?? '' })
      } else if (section === 'supplierGroups') {
        setSupplierGroupName((item as SupplierGroupDto).name)
      } else if (section === 'suppliers') {
        const supplier = item as SupplierDto
        setSupplierForm({ name: supplier.name, groupId: supplier.groupId, inn: supplier.inn ?? '', legalAddress: supplier.legalAddress ?? '', contactPerson: supplier.contactPerson ?? '', phone: supplier.phone ?? '', email: supplier.email ?? '', startingBalance: supplier.startingBalance, comment: supplier.comment ?? '' })
      } else if (section === 'incomeTypes' || section === 'expenseTypes') {
        const type = item as AccountingTypeDto
        setAccountingTypeForm({ name: type.name, code: type.code ?? '' })
      } else {
        const tariff = item as TariffDto
        setTariffForm(createTariffFormFromDto(tariff))
      }
    } else {
      setOwnerForm({ lastName: '', firstName: '', middleName: '', phone: '', address: '', meterNotes: '' })
      setOwnerGarageLinkForm(createEmptyOwnerGarageLinkForm())
      setGarageForm({ number: '', peopleCount: 1, floorCount: 1, ownerId: '', startingBalance: 0, initialWaterMeterValue: '', initialElectricityMeterValue: '', comment: '' })
      setSupplierGroupName('')
      setSupplierForm({ name: '', groupId: groupOptions[0]?.id ?? '', inn: '', legalAddress: '', contactPerson: '', phone: '', email: '', startingBalance: 0, comment: '' })
      setAccountingTypeForm({ name: '', code: '' })
      setTariffForm({ name: '', calculationBase: 'fixed', rate: 1, effectiveFrom: '2026-07-01', comment: '' })
    }

    setEditor({ section, mode, item })
  }

  function closeEditor() {
    setEditor(null)
    setValidationErrors([])
  }

  async function saveEditor(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!editor) {
      return
    }

    if (editor.section === 'tariffs' && !canManageTariffs) {
      setError('Для изменения тарифов нужно право tariffs.manage.')
      return
    }

    if (editor.section !== 'tariffs' && !canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    setSaving('dictionary-editor')
    setError(null)
    try {
      const saved = await saveEditorRequest(editor)
      if (!saved) {
        return
      }

      closeEditor()
      await refreshAfterMutation(editor.section)
      showToast(editor.mode === 'create' ? 'Запись добавлена.' : 'Изменения сохранены.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить запись.'
      setError(message)
      showToast(message, 'error')
    } finally {
      setSaving(null)
    }
  }

  async function saveEditorRequest(currentEditor: { section: DictionarySectionKey; mode: 'create' | 'edit'; item?: DictionaryRecord }) {
    if (currentEditor.section === 'owners') {
      const errors = [...getOwnerValidationErrors(ownerForm), ...getOwnerGarageLinkValidationErrors(ownerGarageLinkForm)]
      if (errors.length > 0) {
        setValidationErrors(errors)
        return false
      }
      let savedOwner: OwnerDto
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        savedOwner = await dictionaryClient.updateOwner(auth.accessToken, (currentEditor.item as OwnerDto).id, ownerForm)
      } else {
        savedOwner = await dictionaryClient.createOwner(auth.accessToken, ownerForm)
      }
      await saveOwnerGarageLinks(savedOwner.id)
    } else if (currentEditor.section === 'garages') {
      const request: UpsertGarageRequest = {
        number: garageForm.number,
        peopleCount: garageForm.peopleCount,
        floorCount: garageForm.floorCount,
        ownerId: garageForm.ownerId || null,
        startingBalance: garageForm.startingBalance,
        initialWaterMeterValue: garageForm.initialWaterMeterValue === '' ? null : Number(garageForm.initialWaterMeterValue),
        initialElectricityMeterValue: garageForm.initialElectricityMeterValue === '' ? null : Number(garageForm.initialElectricityMeterValue),
        comment: garageForm.comment.trim() || undefined,
      }
      const errors = getGarageValidationErrors(request)
      if (errors.length > 0) {
        setValidationErrors(errors)
        return false
      }
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateGarage(auth.accessToken, (currentEditor.item as GarageDto).id, request)
      } else {
        await dictionaryClient.createGarage(auth.accessToken, request)
      }
    } else if (currentEditor.section === 'supplierGroups') {
      const request = { name: supplierGroupName }
      const errors = getSupplierGroupValidationErrors(request)
      if (errors.length > 0) {
        setValidationErrors(errors)
        return false
      }
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        if (!dictionaryClient.updateSupplierGroup) {
          throw new Error('Изменение групп поставщиков недоступно в текущем клиенте.')
        }
        await dictionaryClient.updateSupplierGroup(auth.accessToken, (currentEditor.item as SupplierGroupDto).id, request)
      } else {
        await dictionaryClient.createSupplierGroup(auth.accessToken, request)
      }
    } else if (currentEditor.section === 'suppliers') {
      const request: UpsertSupplierRequest = { ...supplierForm, groupId: supplierForm.groupId || groupOptions[0]?.id || '' }
      const errors = getSupplierValidationErrors(request)
      if (errors.length > 0) {
        setValidationErrors(errors)
        return false
      }
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateSupplier(auth.accessToken, (currentEditor.item as SupplierDto).id, request)
      } else {
        await dictionaryClient.createSupplier(auth.accessToken, request)
      }
    } else if (currentEditor.section === 'incomeTypes') {
      const errors = getAccountingTypeValidationErrors(accountingTypeForm, 'вида поступления')
      if (errors.length > 0) {
        setValidationErrors(errors)
        return false
      }
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        if (!dictionaryClient.updateIncomeType) {
          throw new Error('Изменение видов поступлений недоступно в текущем клиенте.')
        }
        await dictionaryClient.updateIncomeType(auth.accessToken, (currentEditor.item as AccountingTypeDto).id, accountingTypeForm)
      } else {
        await dictionaryClient.createIncomeType(auth.accessToken, accountingTypeForm)
      }
    } else if (currentEditor.section === 'expenseTypes') {
      const errors = getAccountingTypeValidationErrors(accountingTypeForm, 'вида выплаты')
      if (errors.length > 0) {
        setValidationErrors(errors)
        return false
      }
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        if (!dictionaryClient.updateExpenseType) {
          throw new Error('Изменение видов выплат недоступно в текущем клиенте.')
        }
        await dictionaryClient.updateExpenseType(auth.accessToken, (currentEditor.item as AccountingTypeDto).id, accountingTypeForm)
      } else {
        await dictionaryClient.createExpenseType(auth.accessToken, accountingTypeForm)
      }
    } else {
      const errors = getTariffValidationErrors(tariffForm)
      if (errors.length > 0) {
        setValidationErrors(errors)
        return false
      }
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateTariff(auth.accessToken, (currentEditor.item as TariffDto).id, tariffForm)
      } else {
        await dictionaryClient.createTariff(auth.accessToken, tariffForm)
      }
    }

    return true
  }

  async function saveOwnerGarageLinks(ownerId: string) {
    if (ownerGarageLinkForm.existingGarageId) {
      const existingGarage = garageOptions.find((garage) => garage.id === ownerGarageLinkForm.existingGarageId)
      if (!existingGarage) {
        throw new Error('Выбранный гараж не найден в справочнике.')
      }

      await dictionaryClient.updateGarage(auth.accessToken, existingGarage.id, {
        number: existingGarage.number,
        peopleCount: existingGarage.peopleCount,
        floorCount: existingGarage.floorCount,
        ownerId,
        startingBalance: existingGarage.startingBalance,
        initialWaterMeterValue: existingGarage.initialWaterMeterValue,
        initialElectricityMeterValue: existingGarage.initialElectricityMeterValue,
        comment: existingGarage.comment ?? undefined,
      })
    }

    if (ownerGarageLinkForm.newGarageNumber.trim()) {
      await dictionaryClient.createGarage(auth.accessToken, {
        number: ownerGarageLinkForm.newGarageNumber,
        peopleCount: ownerGarageLinkForm.peopleCount,
        floorCount: ownerGarageLinkForm.floorCount,
        ownerId,
        startingBalance: ownerGarageLinkForm.startingBalance,
        initialWaterMeterValue: ownerGarageLinkForm.initialWaterMeterValue === '' ? null : Number(ownerGarageLinkForm.initialWaterMeterValue),
        initialElectricityMeterValue: ownerGarageLinkForm.initialElectricityMeterValue === '' ? null : Number(ownerGarageLinkForm.initialElectricityMeterValue),
        comment: ownerGarageLinkForm.comment.trim() || undefined,
      })
    }
  }

  async function refreshAfterMutation(section: DictionarySectionKey) {
    const page = pages[section]
    await loadPage(section, Math.min(page.offset, Math.max(0, page.totalCount - 1)), page.limit)
    if (section === 'owners') {
      setOwnerOptions(await dictionaryClient.getOwners(auth.accessToken, undefined, 500))
      setGarageOptions(await dictionaryClient.getGarages(auth.accessToken, undefined, 500))
    }
    if (section === 'garages') {
      setOwnerOptions(await dictionaryClient.getOwners(auth.accessToken, undefined, 500))
      setGarageOptions(await dictionaryClient.getGarages(auth.accessToken, undefined, 500))
    }
    if (section === 'supplierGroups') {
      setGroupOptions(await dictionaryClient.getSupplierGroups(auth.accessToken, 500))
    }
  }

  async function confirmArchive() {
    if (!archiveTarget) {
      return
    }

    if (archiveTarget.section === 'tariffs' && !canManageTariffs) {
      setError('Для удаления тарифов нужно право tariffs.manage.')
      return
    }

    if (archiveTarget.section !== 'tariffs' && !canWriteDictionaries) {
      setError('Для удаления справочников нужно право dictionaries.write.')
      return
    }

    setSaving('dictionary-archive')
    setError(null)
    try {
      if (archiveTarget.section === 'owners') {
        await dictionaryClient.archiveOwner(auth.accessToken, (archiveTarget.item as OwnerDto).id)
      } else if (archiveTarget.section === 'garages') {
        await dictionaryClient.archiveGarage(auth.accessToken, (archiveTarget.item as GarageDto).id)
      } else if (archiveTarget.section === 'supplierGroups') {
        await dictionaryClient.archiveSupplierGroup(auth.accessToken, (archiveTarget.item as SupplierGroupDto).id)
      } else if (archiveTarget.section === 'suppliers') {
        await dictionaryClient.archiveSupplier(auth.accessToken, (archiveTarget.item as SupplierDto).id)
      } else if (archiveTarget.section === 'incomeTypes') {
        await dictionaryClient.archiveIncomeType(auth.accessToken, (archiveTarget.item as AccountingTypeDto).id)
      } else if (archiveTarget.section === 'expenseTypes') {
        await dictionaryClient.archiveExpenseType(auth.accessToken, (archiveTarget.item as AccountingTypeDto).id)
      } else {
        await dictionaryClient.archiveTariff(auth.accessToken, (archiveTarget.item as TariffDto).id)
      }

      const section = archiveTarget.section
      setArchiveTarget(null)
      await refreshAfterMutation(section)
      showToast('Запись удалена из рабочего списка.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось удалить запись.'
      setError(message)
      showToast(message, 'error')
    } finally {
      setSaving(null)
    }
  }

  function changePageSize(value: number) {
    setPages((current) => ({ ...current, [activeSection]: { ...current[activeSection], offset: 0, limit: value } }))
    setLoading(true)
    void loadPage(activeSection, 0, value).finally(() => setLoading(false))
  }

  function movePage(direction: -1 | 1) {
    const nextOffset = Math.max(0, Math.min(activePage.offset + direction * activePage.limit, Math.max(0, activePage.totalCount - activePage.limit)))
    setLoading(true)
    void loadPage(activeSection, nextOffset, activePage.limit).finally(() => setLoading(false))
  }

  function getRows(): DictionaryRecord[] {
    if (activeSection === 'owners') return owners
    if (activeSection === 'garages') return garages
    if (activeSection === 'supplierGroups') return groups
    if (activeSection === 'suppliers') return suppliers
    if (activeSection === 'incomeTypes') return incomeTypes
    if (activeSection === 'expenseTypes') return expenseTypes
    return tariffs
  }

  function renderHeaders() {
    const headers = activeSection === 'owners'
      ? ['ФИО', 'Гаражи', 'Телефон', 'Адрес']
      : activeSection === 'garages'
        ? ['Номер', 'Владелец', 'Людей', 'Этажей', 'Стартовый баланс']
        : activeSection === 'supplierGroups'
          ? ['Название', 'Тип']
          : activeSection === 'suppliers'
            ? ['Название', 'Группа', 'ИНН', 'Стартовый баланс']
            : activeSection === 'tariffs'
              ? ['Название', 'База', 'Ставка', 'Дата начала']
              : ['Название', 'Код', 'Тип']
    return headers.map((header) => <th key={header}>{header}</th>)
  }

  function renderCells(item: DictionaryRecord) {
    if (activeSection === 'owners') {
      const owner = item as OwnerDto
      return [owner.fullName, owner.garageNumbers?.length ? owner.garageNumbers.join(', ') : 'без гаража', owner.phone ?? 'не указан', owner.address ?? 'не указан'].map((value, index) => <td key={index}>{value}</td>)
    }
    if (activeSection === 'garages') {
      const garage = item as GarageDto
      return [garage.number, garage.ownerName ?? 'без владельца', garage.peopleCount, garage.floorCount, formatMoney(garage.startingBalance)].map((value, index) => <td key={index}>{value}</td>)
    }
    if (activeSection === 'supplierGroups') {
      const group = item as SupplierGroupDto
      return [group.name, group.isSystem ? 'Системная' : 'Пользовательская'].map((value, index) => <td key={index}>{value}</td>)
    }
    if (activeSection === 'suppliers') {
      const supplier = item as SupplierDto
      return [supplier.name, supplier.groupName, supplier.inn ?? 'не указан', formatMoney(supplier.startingBalance)].map((value, index) => <td key={index}>{value}</td>)
    }
    if (activeSection === 'tariffs') {
      const tariff = item as TariffDto
      return [tariff.name, tariff.calculationBase, formatTariffRateSummary(tariff), formatDateOnly(tariff.effectiveFrom)].map((value, index) => <td key={index}>{value}</td>)
    }
    const type = item as AccountingTypeDto
    return [type.name, type.code ?? 'не указан', type.isSystem ? 'Системный' : 'Пользовательский'].map((value, index) => <td key={index}>{value}</td>)
  }

  function getRecordTitle(section: DictionarySectionKey, item: DictionaryRecord) {
    if (section === 'owners') return (item as OwnerDto).fullName
    if (section === 'garages') return `Гараж ${(item as GarageDto).number}`
    if (section === 'supplierGroups') return (item as SupplierGroupDto).name
    if (section === 'suppliers') return (item as SupplierDto).name
    if (section === 'tariffs') return (item as TariffDto).name
    return (item as AccountingTypeDto).name
  }

  function renderEditorFields(section: DictionarySectionKey) {
    if (section === 'owners') {
      return (
        <>
          <input aria-label="Фамилия владельца" placeholder="Фамилия" value={ownerForm.lastName} onChange={(event) => setOwnerForm({ ...ownerForm, lastName: event.target.value })} required />
          <input aria-label="Имя владельца" placeholder="Имя" value={ownerForm.firstName} onChange={(event) => setOwnerForm({ ...ownerForm, firstName: event.target.value })} required />
          <input aria-label="Отчество владельца" placeholder="Отчество" value={ownerForm.middleName ?? ''} onChange={(event) => setOwnerForm({ ...ownerForm, middleName: event.target.value })} />
          <input aria-label="Телефон владельца" placeholder="Телефон" value={ownerForm.phone ?? ''} onChange={(event) => setOwnerForm({ ...ownerForm, phone: event.target.value })} />
          <input aria-label="Адрес владельца" placeholder="Адрес" value={ownerForm.address ?? ''} onChange={(event) => setOwnerForm({ ...ownerForm, address: event.target.value })} />
          <textarea aria-label="Комментарий владельца по счетчикам" placeholder="Комментарий по счетчикам" value={ownerForm.meterNotes ?? ''} onChange={(event) => setOwnerForm({ ...ownerForm, meterNotes: event.target.value })} />
          <div className="dictionary-form-section">
            <h4>Гараж владельца</h4>
            <select aria-label="Привязать существующий гараж" value={ownerGarageLinkForm.existingGarageId} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, existingGarageId: event.target.value })}>
              <option value="">Не привязывать существующий гараж</option>
              {ownerGarageOptions.map((garage) => (
                <option value={garage.id} key={garage.id}>
                  {garage.ownerName ? `Гараж ${garage.number} - ${garage.ownerName}` : `Гараж ${garage.number}`}
                </option>
              ))}
            </select>
            <div className="inline-fields">
              <input aria-label="Номер нового гаража владельца" placeholder="Новый гараж" value={ownerGarageLinkForm.newGarageNumber} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, newGarageNumber: event.target.value })} />
              <input aria-label="Количество людей в новом гараже" type="number" min="0" value={ownerGarageLinkForm.peopleCount} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, peopleCount: Number(event.target.value) })} />
              <input aria-label="Количество этажей в новом гараже" type="number" min="0" value={ownerGarageLinkForm.floorCount} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, floorCount: Number(event.target.value) })} />
            </div>
            <div className="inline-fields">
              <input aria-label="Стартовый баланс нового гаража" type="number" step="0.01" value={ownerGarageLinkForm.startingBalance} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, startingBalance: Number(event.target.value) })} />
              <input aria-label="Стартовый счетчик воды нового гаража" type="number" min="0" step="0.001" value={ownerGarageLinkForm.initialWaterMeterValue} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, initialWaterMeterValue: event.target.value })} />
              <input aria-label="Стартовый счетчик электричества нового гаража" type="number" min="0" step="0.001" value={ownerGarageLinkForm.initialElectricityMeterValue} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, initialElectricityMeterValue: event.target.value })} />
            </div>
            <textarea aria-label="Комментарий нового гаража" placeholder="Комментарий по гаражу" value={ownerGarageLinkForm.comment} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, comment: event.target.value })} />
          </div>
        </>
      )
    }
    if (section === 'garages') {
      return (
        <>
          <input aria-label="Номер гаража" placeholder="Номер" value={garageForm.number} onChange={(event) => setGarageForm({ ...garageForm, number: event.target.value })} required />
          <div className="inline-fields">
            <input aria-label="Количество людей" type="number" min="0" value={garageForm.peopleCount} onChange={(event) => setGarageForm({ ...garageForm, peopleCount: Number(event.target.value) })} />
            <input aria-label="Количество этажей" type="number" min="0" value={garageForm.floorCount} onChange={(event) => setGarageForm({ ...garageForm, floorCount: Number(event.target.value) })} />
          </div>
          <select aria-label="Владелец гаража" value={garageForm.ownerId} onChange={(event) => setGarageForm({ ...garageForm, ownerId: event.target.value })}>
            <option value="">Без владельца</option>
            {ownerOptions.map((owner) => <option value={owner.id} key={owner.id}>{owner.fullName}</option>)}
          </select>
          <input aria-label="Стартовый баланс гаража" type="number" step="0.01" value={garageForm.startingBalance} onChange={(event) => setGarageForm({ ...garageForm, startingBalance: Number(event.target.value) })} />
          <div className="inline-fields">
            <input aria-label="Стартовый счетчик воды" type="number" min="0" step="0.001" value={garageForm.initialWaterMeterValue} onChange={(event) => setGarageForm({ ...garageForm, initialWaterMeterValue: event.target.value })} />
            <input aria-label="Стартовый счетчик электричества" type="number" min="0" step="0.001" value={garageForm.initialElectricityMeterValue} onChange={(event) => setGarageForm({ ...garageForm, initialElectricityMeterValue: event.target.value })} />
          </div>
          <textarea aria-label="Комментарий по гаражу" placeholder="Комментарий" value={garageForm.comment} onChange={(event) => setGarageForm({ ...garageForm, comment: event.target.value })} />
        </>
      )
    }
    if (section === 'supplierGroups') {
      return <input aria-label="Группа поставщиков" placeholder="Группа" value={supplierGroupName} onChange={(event) => setSupplierGroupName(event.target.value)} required />
    }
    if (section === 'suppliers') {
      return (
        <>
          <input aria-label="Название поставщика" placeholder="Название" value={supplierForm.name} onChange={(event) => setSupplierForm({ ...supplierForm, name: event.target.value })} required />
          <select aria-label="Группа для поставщика" value={supplierForm.groupId} onChange={(event) => setSupplierForm({ ...supplierForm, groupId: event.target.value })} required>
            <option value="" disabled>Выберите группу</option>
            {groupOptions.map((group) => <option value={group.id} key={group.id}>{group.name}</option>)}
          </select>
          <input aria-label="ИНН поставщика" placeholder="ИНН" value={supplierForm.inn} onChange={(event) => setSupplierForm({ ...supplierForm, inn: event.target.value })} />
          <input aria-label="Юридический адрес поставщика" placeholder="Юридический адрес" value={supplierForm.legalAddress} onChange={(event) => setSupplierForm({ ...supplierForm, legalAddress: event.target.value })} />
          <input aria-label="Контактное лицо поставщика" placeholder="Контактное лицо" value={supplierForm.contactPerson} onChange={(event) => setSupplierForm({ ...supplierForm, contactPerson: event.target.value })} />
          <input aria-label="Телефон поставщика" placeholder="Телефон" value={supplierForm.phone} onChange={(event) => setSupplierForm({ ...supplierForm, phone: event.target.value })} />
          <input aria-label="Email поставщика" placeholder="Email" value={supplierForm.email} onChange={(event) => setSupplierForm({ ...supplierForm, email: event.target.value })} />
          <input aria-label="Стартовый баланс поставщика" type="number" step="0.01" value={supplierForm.startingBalance} onChange={(event) => setSupplierForm({ ...supplierForm, startingBalance: Number(event.target.value) })} />
          <textarea aria-label="Комментарий поставщика" placeholder="Комментарий" value={supplierForm.comment} onChange={(event) => setSupplierForm({ ...supplierForm, comment: event.target.value })} />
        </>
      )
    }
    if (section === 'incomeTypes' || section === 'expenseTypes') {
      return (
        <>
          <input aria-label="Название вида операции" placeholder="Название" value={accountingTypeForm.name} onChange={(event) => setAccountingTypeForm({ ...accountingTypeForm, name: event.target.value })} required />
          <input aria-label="Код вида операции" placeholder="Код" value={accountingTypeForm.code} onChange={(event) => setAccountingTypeForm({ ...accountingTypeForm, code: event.target.value })} />
        </>
      )
    }
    return (
      <>
        <input aria-label="Название тарифа" placeholder="Название" value={tariffForm.name} onChange={(event) => setTariffForm({ ...tariffForm, name: event.target.value })} required />
        <select aria-label="База расчета тарифа" value={tariffForm.calculationBase} onChange={(event) => setTariffForm(updateTariffCalculationBase(tariffForm, event.target.value))}>
          <option value="fixed">Фиксированно</option>
          <option value="people">По людям</option>
          <option value="meter_water">По счетчику воды</option>
          <option value="meter_electricity">По счетчику электричества</option>
        </select>
        <div className="inline-fields">
          <input aria-label="Ставка тарифа" type="number" min="0.0001" step="0.0001" value={tariffForm.rate} onChange={(event) => setTariffForm({ ...tariffForm, rate: Number(event.target.value) })} />
          <input aria-label="Дата начала тарифа" type="date" value={tariffForm.effectiveFrom} onChange={(event) => setTariffForm({ ...tariffForm, effectiveFrom: event.target.value })} />
        </div>
        {tariffForm.calculationBase === 'meter_electricity' ? (
          <div className="inline-fields tariff-tier-fields">
            <input aria-label="Первый порог электроэнергии" placeholder="Порог 1, кВт" type="number" min="0.0001" step="0.0001" value={tariffForm.electricityFirstThreshold ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityFirstThreshold: parseOptionalNumberInput(event.target.value) })} />
            <input aria-label="Второй порог электроэнергии" placeholder="Порог 2, кВт" type="number" min="0.0001" step="0.0001" value={tariffForm.electricitySecondThreshold ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricitySecondThreshold: parseOptionalNumberInput(event.target.value) })} />
            <input aria-label="Первая ставка электроэнергии" placeholder="Ставка 1" type="number" min="0.0001" step="0.0001" value={tariffForm.electricityFirstRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityFirstRate: parseOptionalNumberInput(event.target.value) })} />
            <input aria-label="Вторая ставка электроэнергии" placeholder="Ставка 2" type="number" min="0.0001" step="0.0001" value={tariffForm.electricitySecondRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricitySecondRate: parseOptionalNumberInput(event.target.value) })} />
            <input aria-label="Третья ставка электроэнергии" placeholder="Ставка 3" type="number" min="0.0001" step="0.0001" value={tariffForm.electricityThirdRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityThirdRate: parseOptionalNumberInput(event.target.value) })} />
          </div>
        ) : null}
        <textarea aria-label="Комментарий тарифа" placeholder="Комментарий" value={tariffForm.comment ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, comment: event.target.value })} />
      </>
    )
  }

  const rows = getRows()
  const visibleFrom = activePage.totalCount === 0 ? 0 : activePage.offset + 1
  const visibleTo = Math.min(activePage.offset + activePage.limit, activePage.totalCount)

  return (
    <section className="dictionary-panel dictionary-panel-v2" aria-label="Справочники">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Справочники</p>
          <h2>{activeOption.label}</h2>
        </div>
        <span>{loading ? 'Загрузка...' : `${activePage.totalCount} записей`}</span>
      </div>

      {error ? <FormError>{error}</FormError> : null}
      {!canWriteDictionaries ? <p className="form-hint">Режим просмотра: для добавления, изменения и удаления справочников нужно право dictionaries.write.</p> : null}
      {!canManageTariffs ? <p className="form-hint">Режим просмотра тарифов: для изменения тарифов нужно право tariffs.manage.</p> : null}

      <div className="dictionary-workbench">
        <nav className="dictionary-subnav" aria-label="Подгруппы справочников">
          {dictionarySectionGroups.map((group) => (
            <div className="dictionary-subnav-group" key={group.key}>
              <span>{group.label}</span>
              {dictionarySectionOptions.filter((section) => section.group === group.key).map((section) => (
                <button className={section.key === activeSection ? 'is-active' : undefined} type="button" aria-label={`Подгруппа: ${section.label}`} aria-current={section.key === activeSection ? 'page' : undefined} onClick={() => {
                  setSearch('')
                  setActiveSection(section.key)
                }} key={section.key}>
                  {section.label}
                </button>
              ))}
            </div>
          ))}
        </nav>

        <div className="dictionary-table-shell">
          <div className="dictionary-toolbar">
            <input aria-label={`Поиск: ${activeOption.label}`} placeholder={searchPlaceholder} value={search} onChange={(event) => setSearch(event.target.value)} disabled={!supportsSearch} />
            <button className="secondary-button" type="button" disabled={!canWriteActiveSection} onClick={() => openEditor(activeSection, 'create')}>
              <Plus size={16} />
              <span>Добавить</span>
            </button>
          </div>

          <div className="dictionary-table-scroll">
            <table className="dictionary-data-table" aria-label={`Таблица: ${activeOption.label}`}>
              <thead>
                <tr>{renderHeaders()}</tr>
              </thead>
              <tbody>
                {rows.map((item) => (
                  <tr tabIndex={0} onContextMenu={(event) => openContextMenu(event, activeSection, item)} onDoubleClick={() => openEditor(activeSection, 'edit', item)} key={`${activeSection}-${getRecordTitle(activeSection, item)}-${'id' in item ? item.id : ''}`}>
                    {renderCells(item)}
                  </tr>
                ))}
              </tbody>
            </table>
            {!loading && rows.length === 0 ? <p className="empty-state" role="status" aria-live="polite">В этом справочнике пока нет записей</p> : null}
          </div>

          <div className="dictionary-pagination" role="navigation" aria-label="Пагинация справочника">
            <span role="status" aria-live="polite">Показано {visibleFrom}-{visibleTo} из {activePage.totalCount}</span>
            <label>
              Строк
              <select aria-label="Количество строк справочника" value={activePage.limit} onChange={(event) => changePageSize(Number(event.target.value))}>
                {dictionaryPageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
              </select>
            </label>
            <button className="ghost-button" type="button" disabled={loading || activePage.offset === 0} onClick={() => movePage(-1)}>Назад</button>
            <button className="ghost-button" type="button" disabled={loading || activePage.offset + activePage.limit >= activePage.totalCount} onClick={() => movePage(1)}>Вперед</button>
          </div>
        </div>
      </div>

      {contextMenu ? (
        <div className="context-menu" style={{ left: contextMenu.x, top: contextMenu.y }} role="menu" aria-label="Операции со справочником" onClick={(event) => event.stopPropagation()}>
          <button type="button" role="menuitem" onClick={() => openEditor(contextMenu.section, 'create')}>
            <Plus size={15} />
            <span>Добавить</span>
          </button>
          {contextMenu.section === 'garages' ? (
            <button type="button" role="menuitem" onClick={() => void openBalanceHistory(contextMenu.item as GarageDto)}>
              <FileText size={15} />
              <span>История баланса</span>
            </button>
          ) : null}
          <button type="button" role="menuitem" disabled={!canWriteActiveSection} onClick={() => openEditor(contextMenu.section, 'edit', contextMenu.item)}>
            <Save size={15} />
            <span>Изменить</span>
          </button>
          <button type="button" role="menuitem" disabled={!canWriteActiveSection} onClick={() => {
            setArchiveTarget({ section: contextMenu.section, item: contextMenu.item })
            setContextMenu(null)
          }}>
            <Trash2 size={15} />
            <span>Удалить</span>
          </button>
        </div>
      ) : null}

      {balanceHistoryGarage ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeBalanceHistory}>
          <section ref={balanceHistoryDialogRef} className="detail-dialog garage-balance-dialog" role="dialog" aria-modal="true" aria-labelledby="garage-balance-title" aria-describedby="garage-balance-owner" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">История баланса</p>
                <h3 id="garage-balance-title">Гараж {balanceHistoryGarage.number}</h3>
                <p id="garage-balance-owner">{balanceHistoryGarage.ownerName ?? 'Владелец не указан'}</p>
              </div>
              <button ref={balanceHistoryCloseRef} className="icon-button" type="button" aria-label="Закрыть историю баланса" onClick={closeBalanceHistory}>
                <X size={18} />
              </button>
            </div>
            <form className="balance-history-filters" onSubmit={(event) => {
              event.preventDefault()
              void loadBalanceHistory()
            }}>
              <label>
                Период с
                <input aria-label="Начало периода истории баланса" type="month" value={balanceHistoryFilters.monthFrom} onChange={(event) => setBalanceHistoryFilters((value) => ({ ...value, monthFrom: event.target.value }))} required />
              </label>
              <label>
                Период по
                <input aria-label="Конец периода истории баланса" type="month" value={balanceHistoryFilters.monthTo} onChange={(event) => setBalanceHistoryFilters((value) => ({ ...value, monthTo: event.target.value }))} required />
              </label>
              <button className="secondary-button" type="submit" disabled={balanceHistoryLoading}>
                <Search size={16} />
                <span>{balanceHistoryLoading ? 'Загружаем...' : 'Показать'}</span>
              </button>
            </form>
            {balanceHistoryError ? <FormError>{balanceHistoryError}</FormError> : null}
            {balanceHistory ? (
              <>
                <div className="balance-history-summary" aria-label="Итоги истории баланса">
                  <div>
                    <span>Старт</span>
                    <strong>{formatMoney(balanceHistory.startingBalance)}</strong>
                  </div>
                  <div>
                    <span>Начислено</span>
                    <strong>{formatMoney(balanceHistory.accrualTotal)}</strong>
                  </div>
                  <div>
                    <span>Поступило</span>
                    <strong>{formatMoney(balanceHistory.incomeTotal)}</strong>
                  </div>
                  <div>
                    <span>{formatDebtLabel(balanceHistory.debt)}</span>
                    <strong className={getDebtClassName(balanceHistory.debt)}>{formatDebtAmount(balanceHistory.debt)}</strong>
                  </div>
                </div>
                <div className="dictionary-table-scroll garage-balance-table-scroll">
                  <table className="dictionary-data-table" aria-label="История баланса гаража">
                    <thead>
                      <tr>
                        <th>Месяц</th>
                        <th>Долг на начало</th>
                        <th>Начислено</th>
                        <th>Поступило</th>
                        <th>Долг на конец</th>
                      </tr>
                    </thead>
                    <tbody>
                      {balanceHistory.rows.map((row) => (
                        <tr key={row.accountingMonth}>
                          <td>{formatMonth(row.accountingMonth)}</td>
                          <td className={getDebtClassName(row.openingDebt)}>{formatDebtAmount(row.openingDebt)}</td>
                          <td className="money-accrual">{formatMoney(row.accrualAmount)}</td>
                          <td className="money-income">{formatMoney(row.incomeAmount)}</td>
                          <td className={getDebtClassName(row.closingDebt)}>{formatDebtAmount(row.closingDebt)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {balanceHistory.rows.length === 0 ? <p className="empty-state" role="status" aria-live="polite">По выбранному периоду строк нет</p> : null}
                </div>
              </>
            ) : null}
          </section>
        </div>
      ) : null}

      {editor ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeEditor}>
          <section ref={editorDialogRef} className="detail-dialog dictionary-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="dictionary-editor-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">{editor.mode === 'create' ? 'Добавление' : 'Изменение'}</p>
                <h3 id="dictionary-editor-title">{dictionarySectionOptions.find((item) => item.key === editor.section)?.label ?? activeOption.label}</h3>
              </div>
              <button ref={editorCloseRef} className="icon-button" type="button" aria-label="Закрыть окно справочника" onClick={closeEditor}>
                <X size={18} />
              </button>
            </div>
            <form className="dictionary-modal-form" onSubmit={saveEditor}>
              {renderEditorFields(editor.section)}
              <FormValidationSummary title="Проверьте запись" items={validationErrors} />
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={closeEditor}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={saving === 'dictionary-editor'}>
                  <Save size={16} />
                  <span>{saving === 'dictionary-editor' ? 'Сохраняем...' : 'Сохранить'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}

      {archiveTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setArchiveTarget(null)}>
          <section ref={archiveDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="dictionary-archive-title" aria-describedby="dictionary-archive-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="dictionary-archive-title">Подтвердите удаление</h3>
                <p>{getRecordTitle(archiveTarget.section, archiveTarget.item)}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить удаление" onClick={() => setArchiveTarget(null)} disabled={saving === 'dictionary-archive'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="dictionary-archive-description">Запись будет скрыта из рабочих таблиц, но останется в audit-журнале и связанной финансовой истории.</p>
            <div className="detail-dialog-actions">
              <button ref={archiveCancelRef} className="ghost-button" type="button" onClick={() => setArchiveTarget(null)} disabled={saving === 'dictionary-archive'}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={() => void confirmArchive()} disabled={saving === 'dictionary-archive'}>
                <Trash2 size={16} />
                <span>{saving === 'dictionary-archive' ? 'Удаляем...' : 'Удалить запись'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {toast ? <div className={`toast-message toast-message--${toast.kind}`} role="status" aria-live="polite">{toast.text}</div> : null}
    </section>
  )
}

export function DictionaryPanel({ auth, dictionaryClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient }) {
  const [owners, setOwners] = useState<OwnerDto[]>([])
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [groups, setGroups] = useState<SupplierGroupDto[]>([])
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [tariffs, setTariffs] = useState<TariffDto[]>([])
  const [ownerForm, setOwnerForm] = useState({ lastName: '', firstName: '', phone: '' })
  const [garageForm, setGarageForm] = useState({ number: '', peopleCount: 1, floorCount: 1, ownerId: '', startingBalance: 0, initialWaterMeterValue: '', initialElectricityMeterValue: '', comment: '' })
  const [garageSearch, setGarageSearch] = useState('')
  const [garageSearchStatus, setGarageSearchStatus] = useState<string | null>(null)
  const garageSearchInitialized = useRef(false)
  const [selectedGarage, setSelectedGarage] = useState<GarageDto | null>(null)
  const [supplierGroupName, setSupplierGroupName] = useState('')
  const [supplierForm, setSupplierForm] = useState({ name: '', groupId: '', inn: '', startingBalance: 0 })
  const [supplierSearch, setSupplierSearch] = useState('')
  const [supplierSearchStatus, setSupplierSearchStatus] = useState<string | null>(null)
  const supplierSearchInitialized = useRef(false)
  const [incomeTypeForm, setIncomeTypeForm] = useState({ name: '', code: '' })
  const [expenseTypeForm, setExpenseTypeForm] = useState({ name: '', code: '' })
  const [tariffForm, setTariffForm] = useState<UpsertTariffRequest>({ name: '', calculationBase: 'fixed', rate: 1, effectiveFrom: '2026-07-01', comment: '' })
  const [editingTariffId, setEditingTariffId] = useState<string | null>(null)
  const [editingTariffBaseline, setEditingTariffBaseline] = useState<typeof tariffForm | null>(null)
  const [ownerValidationErrors, setOwnerValidationErrors] = useState<string[]>([])
  const [garageValidationErrors, setGarageValidationErrors] = useState<string[]>([])
  const [supplierGroupValidationErrors, setSupplierGroupValidationErrors] = useState<string[]>([])
  const [supplierValidationErrors, setSupplierValidationErrors] = useState<string[]>([])
  const [incomeTypeValidationErrors, setIncomeTypeValidationErrors] = useState<string[]>([])
  const [expenseTypeValidationErrors, setExpenseTypeValidationErrors] = useState<string[]>([])
  const [tariffValidationErrors, setTariffValidationErrors] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  useRestoreFocusOnClose(Boolean(selectedGarage))
  const selectedGarageCloseButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(selectedGarage))
  const selectedGarageDialogRef = useFocusTrap<HTMLElement>(Boolean(selectedGarage))

  useEscapeKey(Boolean(selectedGarage), () => setSelectedGarage(null))
  const canWriteDictionaries = hasPermission(auth, permissions.dictionariesWrite)
  const canManageTariffs = hasPermission(auth, permissions.tariffsManage)

  const defaultGroupId = useMemo(() => supplierForm.groupId || groups[0]?.id || '', [groups, supplierForm.groupId])

  useEffect(() => {
    let ignore = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const [loadedOwners, loadedGarages, loadedGroups, loadedSuppliers, loadedIncomeTypes, loadedExpenseTypes, loadedTariffs] = await Promise.all([
          dictionaryClient.getOwners(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getGarages(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getSupplierGroups(auth.accessToken, dictionaryScreenRequestLimit),
          dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getIncomeTypes(auth.accessToken, dictionaryScreenRequestLimit),
          dictionaryClient.getExpenseTypes(auth.accessToken, dictionaryScreenRequestLimit),
          dictionaryClient.getTariffs(auth.accessToken, undefined, dictionaryScreenRequestLimit),
        ])
        if (!ignore) {
          setOwners(loadedOwners)
          setGarages(loadedGarages)
          setGroups(loadedGroups)
          setSuppliers(loadedSuppliers)
          setIncomeTypes(loadedIncomeTypes)
          setExpenseTypes(loadedExpenseTypes)
          setTariffs(loadedTariffs)
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить справочники.')
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
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => {
    const query = garageSearch.trim()
    if (!garageSearchInitialized.current) {
      garageSearchInitialized.current = true
      return
    }

    let ignore = false
    const timeoutId = window.setTimeout(() => {
      setError(null)
      dictionaryClient
        .getGarages(auth.accessToken, query || undefined, dictionaryScreenRequestLimit)
        .then((result) => {
          if (!ignore) {
            setGarages(result)
            setGarageSearchStatus(query ? `Найдено гаражей: ${result.length}` : 'Показаны все гаражи')
          }
        })
        .catch((caught) => {
          if (!ignore) {
            setError(caught instanceof Error ? caught.message : 'Не удалось выполнить поиск гаражей.')
          }
        })
    }, 350)

    return () => {
      ignore = true
      window.clearTimeout(timeoutId)
    }
  }, [auth.accessToken, dictionaryClient, garageSearch])

  useEffect(() => {
    const query = supplierSearch.trim()
    if (!supplierSearchInitialized.current) {
      supplierSearchInitialized.current = true
      return
    }

    let ignore = false
    const timeoutId = window.setTimeout(() => {
      setError(null)
      dictionaryClient
        .getSuppliers(auth.accessToken, undefined, query || undefined, dictionaryScreenRequestLimit)
        .then((result) => {
          if (!ignore) {
            setSuppliers(result)
            setSupplierSearchStatus(query ? `Найдено поставщиков: ${result.length}` : 'Показаны все поставщики')
          }
        })
        .catch((caught) => {
          if (!ignore) {
            setError(caught instanceof Error ? caught.message : 'Не удалось выполнить поиск поставщиков.')
          }
        })
    }, 350)

    return () => {
      ignore = true
      window.clearTimeout(timeoutId)
    }
  }, [auth.accessToken, dictionaryClient, supplierSearch])

  async function saveOwner(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    const errors = getOwnerValidationErrors(ownerForm)
    if (errors.length > 0) {
      setError(null)
      setOwnerValidationErrors(errors)
      return
    }

    setOwnerValidationErrors([])
    await runSaving('owner', async () => {
      const owner = await dictionaryClient.createOwner(auth.accessToken, ownerForm)
      setOwners((items) => [owner, ...items])
      setOwnerForm({ lastName: '', firstName: '', phone: '' })
    })
  }

  async function saveGarage(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    const request: UpsertGarageRequest = {
      number: garageForm.number,
      peopleCount: garageForm.peopleCount,
      floorCount: garageForm.floorCount,
      ownerId: garageForm.ownerId || null,
      startingBalance: garageForm.startingBalance,
      initialWaterMeterValue: garageForm.initialWaterMeterValue === '' ? null : Number(garageForm.initialWaterMeterValue),
      initialElectricityMeterValue: garageForm.initialElectricityMeterValue === '' ? null : Number(garageForm.initialElectricityMeterValue),
      comment: garageForm.comment.trim() || undefined,
    }
    const errors = getGarageValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setGarageValidationErrors(errors)
      return
    }

    setGarageValidationErrors([])
    await runSaving('garage', async () => {
      const garage = await dictionaryClient.createGarage(auth.accessToken, request)
      setGarages((items) => [garage, ...items])
      setGarageForm({ number: '', peopleCount: 1, floorCount: 1, ownerId: '', startingBalance: 0, initialWaterMeterValue: '', initialElectricityMeterValue: '', comment: '' })
    })
  }

  async function searchGarages() {
    setSaving('garage-search')
    setError(null)
    setGarageSearchStatus(null)
    try {
      const result = await dictionaryClient.getGarages(auth.accessToken, garageSearch, dictionaryScreenRequestLimit)
      setGarages(result)
      const query = garageSearch.trim()
      setGarageSearchStatus(query ? `Найдено гаражей: ${result.length}` : 'Показаны все гаражи')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось выполнить поиск гаражей.')
    } finally {
      setSaving(null)
    }
  }

  async function saveSupplierGroup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    const errors = getSupplierGroupValidationErrors({ name: supplierGroupName })
    if (errors.length > 0) {
      setError(null)
      setSupplierGroupValidationErrors(errors)
      return
    }

    setSupplierGroupValidationErrors([])
    await runSaving('group', async () => {
      const group = await dictionaryClient.createSupplierGroup(auth.accessToken, { name: supplierGroupName })
      setGroups((items) => [...items, group])
      setSupplierGroupName('')
      setSupplierForm((value) => ({ ...value, groupId: group.id }))
    })
  }

  async function saveSupplier(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    const request: UpsertSupplierRequest = {
      name: supplierForm.name,
      groupId: defaultGroupId,
      inn: supplierForm.inn,
      startingBalance: supplierForm.startingBalance,
    }
    const errors = getSupplierValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setSupplierValidationErrors(errors)
      return
    }

    setSupplierValidationErrors([])
    await runSaving('supplier', async () => {
      const supplier = await dictionaryClient.createSupplier(auth.accessToken, request)
      setSuppliers((items) => [supplier, ...items])
      setSupplierForm({ name: '', groupId: defaultGroupId, inn: '', startingBalance: 0 })
    })
  }

  async function searchSuppliers() {
    setSaving('supplier-search')
    setError(null)
    setSupplierSearchStatus(null)
    try {
      const result = await dictionaryClient.getSuppliers(auth.accessToken, undefined, supplierSearch, dictionaryScreenRequestLimit)
      setSuppliers(result)
      const query = supplierSearch.trim()
      setSupplierSearchStatus(query ? `Найдено поставщиков: ${result.length}` : 'Показаны все поставщики')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось выполнить поиск поставщиков.')
    } finally {
      setSaving(null)
    }
  }

  async function saveIncomeType(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    const errors = getAccountingTypeValidationErrors(incomeTypeForm, 'вида поступления')
    if (errors.length > 0) {
      setError(null)
      setIncomeTypeValidationErrors(errors)
      return
    }

    setIncomeTypeValidationErrors([])
    await runSaving('income-type', async () => {
      const incomeType = await dictionaryClient.createIncomeType(auth.accessToken, incomeTypeForm)
      setIncomeTypes((items) => [incomeType, ...items])
      setIncomeTypeForm({ name: '', code: '' })
    })
  }

  async function saveExpenseType(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canWriteDictionaries) {
      setError('Для изменения справочников нужно право dictionaries.write.')
      return
    }

    const errors = getAccountingTypeValidationErrors(expenseTypeForm, 'вида выплаты')
    if (errors.length > 0) {
      setError(null)
      setExpenseTypeValidationErrors(errors)
      return
    }

    setExpenseTypeValidationErrors([])
    await runSaving('expense-type', async () => {
      const expenseType = await dictionaryClient.createExpenseType(auth.accessToken, expenseTypeForm)
      setExpenseTypes((items) => [expenseType, ...items])
      setExpenseTypeForm({ name: '', code: '' })
    })
  }

  async function saveTariff(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canManageTariffs) {
      setError('Для изменения тарифов нужно право tariffs.manage.')
      return
    }

    const errors = getTariffValidationErrors(tariffForm)
    if (errors.length > 0) {
      setError(null)
      setTariffValidationErrors(errors)
      return
    }

    setTariffValidationErrors([])
    await runSaving('tariff', async () => {
      if (editingTariffId) {
        const tariff = await dictionaryClient.updateTariff(auth.accessToken, editingTariffId, tariffForm)
        setTariffs((items) => items.map((item) => (item.id === tariff.id ? tariff : item)))
        setEditingTariffId(null)
        setEditingTariffBaseline(null)
      } else {
        const tariff = await dictionaryClient.createTariff(auth.accessToken, tariffForm)
        setTariffs((items) => [tariff, ...items])
      }

      setTariffForm((value) => withoutElectricityTierFields({ ...value, name: '', rate: 1, comment: '' }))
    })
  }

  function editTariff(tariff: TariffDto) {
    if (editingTariffId === tariff.id) {
      return
    }

    if (editingTariffId && hasUnsavedTariffChanges() && !window.confirm('Перейти к другому тарифу без сохранения изменений?')) {
      return
    }

    const nextForm = createTariffFormFromDto(tariff)

    setEditingTariffId(tariff.id)
    setTariffValidationErrors([])
    setTariffForm(nextForm)
    setEditingTariffBaseline(nextForm)
  }

  function hasUnsavedTariffChanges() {
    return Boolean(
      editingTariffBaseline
      && (
        tariffForm.name !== editingTariffBaseline.name
        || tariffForm.calculationBase !== editingTariffBaseline.calculationBase
        || tariffForm.rate !== editingTariffBaseline.rate
        || tariffForm.effectiveFrom !== editingTariffBaseline.effectiveFrom
        || tariffForm.comment !== editingTariffBaseline.comment
        || tariffForm.electricityFirstThreshold !== editingTariffBaseline.electricityFirstThreshold
        || tariffForm.electricitySecondThreshold !== editingTariffBaseline.electricitySecondThreshold
        || tariffForm.electricityFirstRate !== editingTariffBaseline.electricityFirstRate
        || tariffForm.electricitySecondRate !== editingTariffBaseline.electricitySecondRate
        || tariffForm.electricityThirdRate !== editingTariffBaseline.electricityThirdRate
      ),
    )
  }

  function resetTariffForm(options?: { skipConfirmation?: boolean }) {
    if (editingTariffId && !options?.skipConfirmation && hasUnsavedTariffChanges() && !window.confirm('Отменить редактирование тарифа без сохранения изменений?')) {
      return
    }

    setEditingTariffId(null)
    setEditingTariffBaseline(null)
    setTariffValidationErrors([])
    setTariffForm((value) => withoutElectricityTierFields({ ...value, name: '', rate: 1, comment: '' }))
  }

  async function archiveDictionaryItem(scope: string, action: () => Promise<void>) {
    if (scope === 'tariff' && !canManageTariffs) {
      setError('Для архивирования тарифов нужно право tariffs.manage.')
      return
    }

    if (scope !== 'tariff' && !canWriteDictionaries) {
      setError('Для архивирования справочников нужно право dictionaries.write.')
      return
    }

    await runSaving(`archive-${scope}`, action)
  }

  async function runSaving(scope: string, action: () => Promise<void>) {
    setSaving(scope)
    setError(null)
    try {
      await action()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось сохранить запись.')
    } finally {
      setSaving(null)
    }
  }

  return (
    <section className="dictionary-panel" aria-label="Справочники">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Справочники</p>
          <h2>База для импорта, начислений и отчетов</h2>
        </div>
        <span>{loading ? 'Загрузка...' : `${owners.length + garages.length + suppliers.length} записей`}</span>
      </div>

      {error ? <FormError>{error}</FormError> : null}
      {!canWriteDictionaries ? <p className="form-hint">Режим просмотра: для добавления и архивирования справочников нужно право dictionaries.write.</p> : null}
      {!canManageTariffs ? <p className="form-hint">Режим просмотра тарифов: для добавления и архивирования тарифов нужно право tariffs.manage.</p> : null}

      <div className="dictionary-grid">
        <form className="dictionary-form" onSubmit={saveOwner}>
          <h3>Владельцы</h3>
          <input aria-label="Фамилия владельца" placeholder="Фамилия" value={ownerForm.lastName} onChange={(event) => setOwnerForm({ ...ownerForm, lastName: event.target.value })} required />
          <input aria-label="Имя владельца" placeholder="Имя" value={ownerForm.firstName} onChange={(event) => setOwnerForm({ ...ownerForm, firstName: event.target.value })} required />
          <input aria-label="Телефон владельца" placeholder="Телефон" value={ownerForm.phone} onChange={(event) => setOwnerForm({ ...ownerForm, phone: event.target.value })} />
          <FormValidationSummary title="Проверьте владельца" items={ownerValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWriteDictionaries || saving === 'owner'}>
            <Plus size={16} />
            <span>Добавить</span>
          </button>
          <DictionaryList
            items={owners.map((owner) => ({
              id: owner.id,
              title: owner.fullName,
              meta: owner.phone ?? 'телефон не указан',
              archiveLabel: canWriteDictionaries ? `Архивировать владельца ${owner.fullName}` : undefined,
              onArchive: canWriteDictionaries ? () => archiveDictionaryItem('owner', async () => {
                await dictionaryClient.archiveOwner(auth.accessToken, owner.id)
                setOwners((items) => items.filter((item) => item.id !== owner.id))
              }) : undefined,
            }))}
            emptyText="Владельцев пока нет"
          />
        </form>

        <form className="dictionary-form" onSubmit={saveGarage}>
          <h3>Гаражи</h3>
          <div className="compact-form">
            <input
              aria-label="Поиск гаража или владельца"
              placeholder="Номер или ФИО владельца"
              value={garageSearch}
              onChange={(event) => setGarageSearch(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault()
                  void searchGarages()
                }
              }}
            />
            <button className="icon-button" type="button" aria-label="Найти гараж" disabled={saving === 'garage-search'} onClick={() => void searchGarages()}>
              <Search size={17} />
            </button>
          </div>
          {garageSearchStatus ? <p className="form-hint" role="status" aria-live="polite">{garageSearchStatus}</p> : null}
          <input aria-label="Номер гаража" placeholder="Номер" value={garageForm.number} onChange={(event) => setGarageForm({ ...garageForm, number: event.target.value })} required />
          <div className="inline-fields">
            <input aria-label="Количество людей" type="number" min="0" value={garageForm.peopleCount} onChange={(event) => setGarageForm({ ...garageForm, peopleCount: Number(event.target.value) })} />
            <input aria-label="Количество этажей" type="number" min="0" value={garageForm.floorCount} onChange={(event) => setGarageForm({ ...garageForm, floorCount: Number(event.target.value) })} />
          </div>
          <input aria-label="Стартовый баланс гаража" type="number" step="0.01" value={garageForm.startingBalance} onChange={(event) => setGarageForm({ ...garageForm, startingBalance: Number(event.target.value) })} />
          <div className="inline-fields">
            <input aria-label="Стартовый счетчик воды" type="number" min="0" step="0.001" value={garageForm.initialWaterMeterValue} onChange={(event) => setGarageForm({ ...garageForm, initialWaterMeterValue: event.target.value })} />
            <input aria-label="Стартовый счетчик электричества" type="number" min="0" step="0.001" value={garageForm.initialElectricityMeterValue} onChange={(event) => setGarageForm({ ...garageForm, initialElectricityMeterValue: event.target.value })} />
          </div>
          <textarea aria-label="Комментарий по гаражу" placeholder="Комментарий по счетчикам, особенностям начислений или импорта" value={garageForm.comment} onChange={(event) => setGarageForm({ ...garageForm, comment: event.target.value })} />
          <select aria-label="Владелец гаража" value={garageForm.ownerId} onChange={(event) => setGarageForm({ ...garageForm, ownerId: event.target.value })}>
            <option value="">Без владельца</option>
            {owners.map((owner) => (
              <option value={owner.id} key={owner.id}>
                {owner.fullName}
              </option>
            ))}
          </select>
          <FormValidationSummary title="Проверьте гараж" items={garageValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWriteDictionaries || saving === 'garage'}>
            <Plus size={16} />
            <span>Добавить</span>
          </button>
          <DictionaryList
            items={garages.map((garage) => ({
              id: garage.id,
              title: `Гараж ${garage.number}`,
              meta: `${garage.ownerName ?? 'владелец не указан'} · старт ${formatMoney(garage.startingBalance)}`,
              openLabel: `Открыть карточку гаража ${garage.number}`,
              onOpen: () => setSelectedGarage(garage),
              archiveLabel: canWriteDictionaries ? `Архивировать гараж ${garage.number}` : undefined,
              onArchive: canWriteDictionaries ? () => archiveDictionaryItem('garage', async () => {
                await dictionaryClient.archiveGarage(auth.accessToken, garage.id)
                setGarages((items) => items.filter((item) => item.id !== garage.id))
              }) : undefined,
            }))}
            emptyText="Гаражей пока нет"
          />
        </form>

        <div className="dictionary-form">
          <h3>Поставщики</h3>
          <div className="compact-form">
            <input
              aria-label="Поиск поставщика"
              placeholder="Название, ИНН или контакт"
              value={supplierSearch}
              onChange={(event) => setSupplierSearch(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault()
                  void searchSuppliers()
                }
              }}
            />
            <button className="icon-button" type="button" aria-label="Найти поставщика" disabled={saving === 'supplier-search'} onClick={() => void searchSuppliers()}>
              <Search size={17} />
            </button>
          </div>
          {supplierSearchStatus ? <p className="form-hint" role="status" aria-live="polite">{supplierSearchStatus}</p> : null}
          <form className="compact-form" onSubmit={saveSupplierGroup}>
            <input aria-label="Группа поставщиков" placeholder="Группа" value={supplierGroupName} onChange={(event) => setSupplierGroupName(event.target.value)} required />
            <button className="icon-button" type="submit" aria-label="Добавить группу" disabled={!canWriteDictionaries || saving === 'group'}>
              <Plus size={17} />
            </button>
          </form>
          <FormValidationSummary title="Проверьте группу поставщиков" items={supplierGroupValidationErrors} />
          <form className="compact-stack" onSubmit={saveSupplier}>
            <input aria-label="Название поставщика" placeholder="Название" value={supplierForm.name} onChange={(event) => setSupplierForm({ ...supplierForm, name: event.target.value })} required />
            <select aria-label="Группа для поставщика" value={defaultGroupId} onChange={(event) => setSupplierForm({ ...supplierForm, groupId: event.target.value })} required>
              <option value="" disabled>
                Выберите группу
              </option>
              {groups.map((group) => (
                <option value={group.id} key={group.id}>
                  {group.name}
                </option>
              ))}
            </select>
            <input aria-label="ИНН поставщика" placeholder="ИНН" value={supplierForm.inn} onChange={(event) => setSupplierForm({ ...supplierForm, inn: event.target.value })} />
            <input aria-label="Стартовый баланс поставщика" type="number" step="0.01" value={supplierForm.startingBalance} onChange={(event) => setSupplierForm({ ...supplierForm, startingBalance: Number(event.target.value) })} />
            <FormValidationSummary title="Проверьте поставщика" items={supplierValidationErrors} />
            <button className="secondary-button" type="submit" disabled={!canWriteDictionaries || !defaultGroupId || saving === 'supplier'}>
              <Plus size={16} />
              <span>Добавить</span>
            </button>
          </form>
          <DictionaryList
            items={suppliers.map((supplier) => ({
              id: supplier.id,
              title: supplier.name,
              meta: `${supplier.groupName}${supplier.inn ? `, ИНН ${supplier.inn}` : ''} · старт ${formatMoney(supplier.startingBalance)}`,
              archiveLabel: canWriteDictionaries ? `Архивировать поставщика ${supplier.name}` : undefined,
              onArchive: canWriteDictionaries ? () => archiveDictionaryItem('supplier', async () => {
                await dictionaryClient.archiveSupplier(auth.accessToken, supplier.id)
                setSuppliers((items) => items.filter((item) => item.id !== supplier.id))
              }) : undefined,
            }))}
            emptyText="Поставщиков пока нет"
          />
        </div>
      </div>

      <div className="finance-settings-grid" aria-label="Финансовые настройки">
        <form className="dictionary-form" onSubmit={saveIncomeType}>
          <h3>Виды поступлений</h3>
          <input aria-label="Название вида поступления" placeholder="Членский взнос" value={incomeTypeForm.name} onChange={(event) => setIncomeTypeForm({ ...incomeTypeForm, name: event.target.value })} required />
          <input aria-label="Код вида поступления" placeholder="Код" value={incomeTypeForm.code} onChange={(event) => setIncomeTypeForm({ ...incomeTypeForm, code: event.target.value })} />
          <FormValidationSummary title="Проверьте вид поступления" items={incomeTypeValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWriteDictionaries || saving === 'income-type'}>
            <Plus size={16} />
            <span>Добавить</span>
          </button>
          <DictionaryList
            items={incomeTypes.map((item) => ({
              id: item.id,
              title: item.name,
              meta: item.code ?? 'код не указан',
              archiveLabel: canWriteDictionaries ? `Архивировать вид поступления ${item.name}` : undefined,
              onArchive: canWriteDictionaries ? () => archiveDictionaryItem('income-type', async () => {
                await dictionaryClient.archiveIncomeType(auth.accessToken, item.id)
                setIncomeTypes((items) => items.filter((incomeType) => incomeType.id !== item.id))
              }) : undefined,
            }))}
            emptyText="Видов поступлений пока нет"
          />
        </form>

        <form className="dictionary-form" onSubmit={saveExpenseType}>
          <h3>Виды выплат</h3>
          <input aria-label="Название вида выплаты" placeholder="Электроэнергия" value={expenseTypeForm.name} onChange={(event) => setExpenseTypeForm({ ...expenseTypeForm, name: event.target.value })} required />
          <input aria-label="Код вида выплаты" placeholder="Код" value={expenseTypeForm.code} onChange={(event) => setExpenseTypeForm({ ...expenseTypeForm, code: event.target.value })} />
          <FormValidationSummary title="Проверьте вид выплаты" items={expenseTypeValidationErrors} />
          <button className="secondary-button" type="submit" disabled={!canWriteDictionaries || saving === 'expense-type'}>
            <Plus size={16} />
            <span>Добавить</span>
          </button>
          <DictionaryList
            items={expenseTypes.map((item) => ({
              id: item.id,
              title: item.name,
              meta: item.code ?? 'код не указан',
              archiveLabel: canWriteDictionaries ? `Архивировать вид выплаты ${item.name}` : undefined,
              onArchive: canWriteDictionaries ? () => archiveDictionaryItem('expense-type', async () => {
                await dictionaryClient.archiveExpenseType(auth.accessToken, item.id)
                setExpenseTypes((items) => items.filter((expenseType) => expenseType.id !== item.id))
              }) : undefined,
            }))}
            emptyText="Видов выплат пока нет"
          />
        </form>

        <form className="dictionary-form" onSubmit={saveTariff}>
          <h3>{editingTariffId ? 'Изменение тарифа' : 'Тарифы'}</h3>
          <input aria-label="Название тарифа" placeholder="Вода" value={tariffForm.name} onChange={(event) => setTariffForm({ ...tariffForm, name: event.target.value })} required />
          <select aria-label="База расчета тарифа" value={tariffForm.calculationBase} onChange={(event) => setTariffForm(updateTariffCalculationBase(tariffForm, event.target.value))}>
            <option value="fixed">Фиксированно</option>
            <option value="people">По людям</option>
            <option value="meter_water">По счетчику воды</option>
            <option value="meter_electricity">По счетчику электричества</option>
          </select>
          <div className="inline-fields">
            <input aria-label="Ставка тарифа" type="number" min="0.0001" step="0.0001" value={tariffForm.rate} onChange={(event) => setTariffForm({ ...tariffForm, rate: Number(event.target.value) })} />
            <input aria-label="Дата начала тарифа" type="date" value={tariffForm.effectiveFrom} onChange={(event) => setTariffForm({ ...tariffForm, effectiveFrom: event.target.value })} />
          </div>
          {tariffForm.calculationBase === 'meter_electricity' ? (
            <div className="inline-fields tariff-tier-fields">
              <input aria-label="Первый порог электроэнергии" placeholder="Порог 1, кВт" type="number" min="0.0001" step="0.0001" value={tariffForm.electricityFirstThreshold ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityFirstThreshold: parseOptionalNumberInput(event.target.value) })} />
              <input aria-label="Второй порог электроэнергии" placeholder="Порог 2, кВт" type="number" min="0.0001" step="0.0001" value={tariffForm.electricitySecondThreshold ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricitySecondThreshold: parseOptionalNumberInput(event.target.value) })} />
              <input aria-label="Первая ставка электроэнергии" placeholder="Ставка 1" type="number" min="0.0001" step="0.0001" value={tariffForm.electricityFirstRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityFirstRate: parseOptionalNumberInput(event.target.value) })} />
              <input aria-label="Вторая ставка электроэнергии" placeholder="Ставка 2" type="number" min="0.0001" step="0.0001" value={tariffForm.electricitySecondRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricitySecondRate: parseOptionalNumberInput(event.target.value) })} />
              <input aria-label="Третья ставка электроэнергии" placeholder="Ставка 3" type="number" min="0.0001" step="0.0001" value={tariffForm.electricityThirdRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityThirdRate: parseOptionalNumberInput(event.target.value) })} />
            </div>
          ) : null}
          <textarea aria-label="Комментарий тарифа" placeholder="Комментарий" value={tariffForm.comment ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, comment: event.target.value })} />
          {editingTariffId && hasUnsavedTariffChanges() ? <p className="form-hint" role="status" aria-live="polite">Есть несохраненные изменения тарифа.</p> : null}
          <FormValidationSummary title="Проверьте тариф" items={tariffValidationErrors} />
          <div className="inline-actions">
            <button className="secondary-button" type="submit" disabled={!canManageTariffs || saving === 'tariff'}>
              {editingTariffId ? <Save size={16} /> : <Plus size={16} />}
              <span>{editingTariffId ? 'Сохранить' : 'Добавить'}</span>
            </button>
            {editingTariffId ? (
              <button className="ghost-button" type="button" onClick={() => resetTariffForm()}>
                Отменить
              </button>
            ) : null}
          </div>
          <DictionaryList
            items={tariffs.map((item) => ({
              id: item.id,
              title: item.name,
              meta: `${formatTariffRateSummary(item)} с ${formatDateOnly(item.effectiveFrom)}${item.comment ? ` · ${item.comment}` : ''}`,
              isActive: editingTariffId === item.id,
              activeLabel: 'Редактируется',
              openLabel: canManageTariffs ? `Изменить тариф ${item.name}` : undefined,
              onOpen: canManageTariffs ? () => editTariff(item) : undefined,
              archiveLabel: canManageTariffs ? `Архивировать тариф ${item.name}` : undefined,
              onArchive: canManageTariffs ? () => archiveDictionaryItem('tariff', async () => {
                await dictionaryClient.archiveTariff(auth.accessToken, item.id)
                setTariffs((items) => items.filter((tariff) => tariff.id !== item.id))
                if (editingTariffId === item.id) {
                  resetTariffForm({ skipConfirmation: true })
                }
              }) : undefined,
            }))}
            emptyText="Тарифов пока нет"
          />
        </form>
      </div>
      {selectedGarage ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setSelectedGarage(null)}>
          <section ref={selectedGarageDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="garage-card-title" aria-describedby="garage-card-owner" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Карточка гаража</p>
                <h3 id="garage-card-title">Гараж {selectedGarage.number}</h3>
                <p id="garage-card-owner">{selectedGarage.ownerName ?? 'Владелец не указан'}</p>
              </div>
              <button ref={selectedGarageCloseButtonRef} className="icon-button" type="button" aria-label="Закрыть карточку гаража" onClick={() => setSelectedGarage(null)}>
                <X size={18} />
              </button>
            </div>
            <dl className="detail-grid">
              <div>
                <dt>Владелец</dt>
                <dd>{selectedGarage.ownerName ?? 'Не указан'}</dd>
              </div>
              <div>
                <dt>Людей</dt>
                <dd>{selectedGarage.peopleCount}</dd>
              </div>
              <div>
                <dt>Этажей</dt>
                <dd>{selectedGarage.floorCount}</dd>
              </div>
              <div>
                <dt>Стартовый баланс</dt>
                <dd>{formatMoney(selectedGarage.startingBalance)}</dd>
              </div>
              <div>
                <dt>Старт воды</dt>
                <dd>{formatNullableNumber(selectedGarage.initialWaterMeterValue)}</dd>
              </div>
              <div>
                <dt>Старт электричества</dt>
                <dd>{formatNullableNumber(selectedGarage.initialElectricityMeterValue)}</dd>
              </div>
              <div>
                <dt>Комментарий</dt>
                <dd>{selectedGarage.comment || 'Нет комментария'}</dd>
              </div>
            </dl>
          </section>
        </div>
      ) : null}
    </section>
  )
}

type DictionaryListItem = {
  id: string
  title: string
  meta: string
  isActive?: boolean
  activeLabel?: string
  openLabel?: string
  onOpen?: () => void
  archiveLabel?: string
  onArchive?: () => Promise<void> | void
}

function DictionaryList({ items, emptyText }: { items: DictionaryListItem[]; emptyText: string }) {
  const [pendingArchive, setPendingArchive] = useState<DictionaryListItem | null>(null)
  const [confirmingArchive, setConfirmingArchive] = useState(false)
  const [showAllItems, setShowAllItems] = useState(false)
  const listId = useId()
  const compactLimit = 5
  const visibleItems = showAllItems ? items : items.slice(0, compactLimit)
  const hasHiddenItems = items.length > compactLimit
  useRestoreFocusOnClose(Boolean(pendingArchive))
  const archiveCancelButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingArchive) && !confirmingArchive)
  const archiveDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingArchive))

  useEscapeKey(Boolean(pendingArchive) && !confirmingArchive, () => setPendingArchive(null))

  async function confirmArchive() {
    if (!pendingArchive?.onArchive) {
      return
    }

    setConfirmingArchive(true)
    try {
      await pendingArchive.onArchive()
      setPendingArchive(null)
    } finally {
      setConfirmingArchive(false)
    }
  }

  if (items.length === 0) {
    return <p className="empty-state" role="status" aria-live="polite">{emptyText}</p>
  }

  return (
    <>
      <ul className="dictionary-list" id={listId}>
        {visibleItems.map((item) => (
          <li className={item.isActive ? 'is-active' : undefined} aria-current={item.isActive ? 'true' : undefined} key={item.id}>
            <span>
              <strong>
                {item.title}
                {item.isActive ? <span className="dictionary-state">{item.activeLabel ?? 'Открыто'}</span> : null}
              </strong>
              <span>{item.meta}</span>
            </span>
            <span className="dictionary-actions">
              {item.onOpen ? (
                <button className="icon-button" type="button" aria-label={item.openLabel ?? `Открыть ${item.title}`} onClick={item.onOpen} disabled={item.isActive} title={item.isActive ? 'Запись уже открыта' : undefined}>
                  <FileText size={16} />
                </button>
              ) : null}
              {item.onArchive ? (
                <button className="icon-button" type="button" aria-label={item.archiveLabel ?? `Архивировать ${item.title}`} onClick={() => setPendingArchive(item)}>
                  <Trash2 size={16} />
                </button>
              ) : null}
            </span>
          </li>
        ))}
      </ul>
      {hasHiddenItems ? (
        <div className="dictionary-list-footer">
          <p className="empty-state" role="status" aria-live="polite">Показано {visibleItems.length} из {items.length} записей</p>
          <button className="ghost-button" type="button" aria-controls={listId} aria-expanded={showAllItems} onClick={() => setShowAllItems((value) => !value)}>
            {showAllItems ? 'Свернуть список' : 'Показать все записи'}
          </button>
        </div>
      ) : null}
      {pendingArchive ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!confirmingArchive) {
            setPendingArchive(null)
          }
        }}>
          <section ref={archiveDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby={`archive-confirmation-${pendingArchive.id}`} aria-describedby={`archive-confirmation-description-${pendingArchive.id}`} onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Архивирование</p>
                <h3 id={`archive-confirmation-${pendingArchive.id}`}>Подтвердите архивирование</h3>
                <p>{pendingArchive.title}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить архивирование" onClick={() => setPendingArchive(null)} disabled={confirmingArchive}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id={`archive-confirmation-description-${pendingArchive.id}`}>Запись исчезнет из рабочих списков, но останется в истории и audit-журнале.</p>
            <div className="detail-dialog-actions">
              <button ref={archiveCancelButtonRef} className="ghost-button" type="button" onClick={() => setPendingArchive(null)} disabled={confirmingArchive}>
                Отменить
              </button>
              <button className="secondary-button" type="button" onClick={() => void confirmArchive()} disabled={confirmingArchive}>
                <Trash2 size={16} />
                <span>{confirmingArchive ? 'Архивируем...' : 'Архивировать запись'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </>
  )
}

function formatMoney(value: number): string {
  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2, minimumFractionDigits: 2 }).format(value)
}

function formatTariffRateSummary(tariff: TariffDto): string {
  const hasElectricityTiers = tariff.electricityFirstThreshold !== null
    && tariff.electricitySecondThreshold !== null
    && tariff.electricityFirstRate !== null
    && tariff.electricitySecondRate !== null
    && tariff.electricityThirdRate !== null

  if (!hasElectricityTiers) {
    return formatMoney(tariff.rate)
  }

  return `до ${formatMoney(tariff.electricityFirstThreshold!)} кВт: ${formatMoney(tariff.electricityFirstRate!)}, до ${formatMoney(tariff.electricitySecondThreshold!)} кВт: ${formatMoney(tariff.electricitySecondRate!)}, выше: ${formatMoney(tariff.electricityThirdRate!)}`
}

function formatDebtLabel(value: number): string {
  return value < 0 ? 'Переплата' : 'Задолженность'
}

function formatDebtAmount(value: number): string {
  return formatMoney(Math.abs(value))
}

function getDebtClassName(value: number): string {
  return value < 0 ? 'money-overpayment' : 'money-accrual'
}

function formatPaymentAllocations(allocations: PaymentAllocationDto[]): string {
  const visible = allocations.slice(0, 3).map((allocation) => {
    const label = allocation.accountingMonth ? formatMonth(allocation.accountingMonth) : allocation.label
    return `${label} ${formatMoney(allocation.paidAmount)}`
  })
  const hiddenCount = allocations.length - visible.length
  return hiddenCount > 0 ? `${visible.join(', ')} и еще ${hiddenCount}` : visible.join(', ')
}

function createDefaultGarageBalanceHistoryFilters(date = new Date()) {
  const to = new Date(date.getFullYear(), date.getMonth(), 1)
  const from = new Date(date.getFullYear(), date.getMonth() - 5, 1)
  return { monthFrom: formatMonthInputValue(from), monthTo: formatMonthInputValue(to) }
}

function formatMonthInputValue(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  return `${year}-${month}`
}

function createDefaultConsolidatedReportFilters(month: string): ConsolidatedReportFilters {
  return { monthFrom: month, monthTo: month, search: '' }
}

function createDefaultIncomeReportFilters(month: string, today: string): IncomeReportFilters {
  return { dateFrom: month, dateTo: today, search: '', garageIds: [], ownerIds: [], incomeTypeIds: [], rowMode: 'all' }
}

function createDefaultExpenseReportFilters(month: string, today: string): ExpenseReportFilters {
  return { dateFrom: month, dateTo: today, search: '', supplierIds: [], expenseTypeIds: [], rowMode: 'all' }
}

function loadConsolidatedReportFilters(month: string): ConsolidatedReportFilters {
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

function loadIncomeReportFilters(month: string, today: string): IncomeReportFilters {
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

function loadExpenseReportFilters(month: string, today: string): ExpenseReportFilters {
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

function readSessionJson(key: string): unknown {
  try {
    const value = window.sessionStorage.getItem(key)
    return value ? JSON.parse(value) : null
  } catch {
    return null
  }
}

function saveSessionJson(key: string, value: unknown) {
  try {
    window.sessionStorage.setItem(key, JSON.stringify(value))
  } catch {
    // Session storage is a convenience only; reports must still work without it.
  }
}

function removeSessionJson(key: string) {
  try {
    window.sessionStorage.removeItem(key)
  } catch {
    // Session storage is a convenience only; logout must still complete without it.
  }
}

function loadStoredAuth(): AuthResponse | null {
  const parsed = readSessionJson(authSessionStorageKey)
  if (!isStoredAuthResponse(parsed)) {
    clearStoredAuth()
    return null
  }

  if (Date.parse(parsed.expiresAtUtc) <= Date.now()) {
    clearStoredAuth()
    return null
  }

  return parsed
}

function saveStoredAuth(auth: AuthResponse) {
  saveSessionJson(authSessionStorageKey, auth)
}

function clearStoredAuth() {
  removeSessionJson(authSessionStorageKey)
}

function isStoredAuthResponse(value: unknown): value is AuthResponse {
  if (!isRecord(value) || typeof value.accessToken !== 'string' || typeof value.expiresAtUtc !== 'string' || !isRecord(value.user)) {
    return false
  }

  if (!Number.isFinite(Date.parse(value.expiresAtUtc))) {
    return false
  }

  return (
    typeof value.user.id === 'string' &&
    typeof value.user.email === 'string' &&
    typeof value.user.displayName === 'string' &&
    Array.isArray(value.user.roles) &&
    value.user.roles.every((role) => typeof role === 'string') &&
    Array.isArray(value.user.permissions) &&
    value.user.permissions.every((permission) => typeof permission === 'string')
  )
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function getStringOrDefault(value: unknown, fallback: string): string {
  return typeof value === 'string' ? value : fallback
}

function getDateOnlyOrDefault(value: unknown, fallback: string): string {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : fallback
}

function getStringArrayOrDefault(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string' && item.length > 0) : []
}

function getRowModeOrDefault(value: unknown): string {
  return value === 'accruals' || value === 'payments' ? value : 'all'
}

function getLocalDateInputValue(date = new Date()): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function formatDateOnly(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (!match) {
    return value
  }

  return `${match[3]}.${match[2]}.${match[1]}`
}

function formatMonth(value: string): string {
  const match = /^(\d{4})-(\d{2})(?:-\d{2})?$/.exec(value)
  if (!match) {
    return value
  }

  return `${match[2]}.${match[1]}`
}

function formatAccrualSource(source: string): string {
  if (source === 'manual') {
    return 'Ручное'
  }

  if (source === 'regular') {
    return 'Авто'
  }

  return source
}

function formatMissingMeterReadings(items: MissingMeterReadingDto[]): string {
  const visibleItems = items.slice(0, 6)
  const suffix = items.length > visibleItems.length ? ` и еще ${items.length - visibleItems.length}` : ''
  return `${visibleItems.map((item) => `Гараж ${item.garageNumber} - ${item.meterKind === 'water' ? 'Вода' : 'Электричество'}`).join(', ')}${suffix}`
}

function formatImportRunStatus(status: AccessImportRunDto['status']): string {
  return status === 'completed' ? 'Завершен' : 'Заблокирован'
}

function formatImportCheckStatus(status: AccessImportCheckDto['status']): string {
  if (status === 'passed') {
    return 'Пройдено'
  }

  if (status === 'warning') {
    return 'Предупреждение'
  }

  return 'Ошибка'
}

function formatImportLogLevel(level: AccessImportRunLogEntryDto['level']): string {
  if (level === 'warning') {
    return 'Предупреждение'
  }

  if (level === 'error') {
    return 'Ошибка'
  }

  return 'Инфо'
}

function formatImportRunCheckSummary(run: AccessImportRunDto): string {
  return `${run.passedChecks}/${run.totalChecks} · ${formatCount(run.warningCount, 'предупреждение', 'предупреждения', 'предупреждений')} · ${formatCount(run.errorCount, 'ошибка', 'ошибки', 'ошибок')}`
}

function formatCount(value: number, one: string, few: string, many: string): string {
  const absoluteValue = Math.abs(value)
  const lastTwoDigits = absoluteValue % 100
  const lastDigit = absoluteValue % 10
  const form = lastTwoDigits >= 11 && lastTwoDigits <= 14 ? many : lastDigit === 1 ? one : lastDigit >= 2 && lastDigit <= 4 ? few : many
  return `${value} ${form}`
}

function formatNullableNumber(value: number | null): string {
  if (value === null) {
    return 'Не указан'
  }

  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 3 }).format(value)
}

function buildReportFileName(type: 'consolidated' | 'income' | 'expense', dateFrom: string, dateTo: string, extension: 'xlsx' | 'pdf'): string {
  return `garagebalance-${type}-${dateFrom.replaceAll('-', '')}-${dateTo.replaceAll('-', '')}.${extension}`
}

function buildImportReportFileName(run: AccessImportRunDto): string {
  const startedAt = run.startedAtUtc.slice(0, 19).replaceAll('-', '').replaceAll(':', '').replace('T', '-')
  const sourceName = run.originalFileName.replace(/\.[^.]+$/, '').replaceAll(' ', '-').toLowerCase()
  return `garagebalance-access-dry-run-${sourceName}-${startedAt}.json`
}

function buildAuditExportFileName(): string {
  return `garagebalance-audit-${new Date().toISOString().slice(0, 10).replaceAll('-', '')}.csv`
}

function getFormValues(form: FormData, name: string): string[] {
  return form
    .getAll(name)
    .map((value) => String(value))
    .filter(Boolean)
}

function downloadBlob(blob: Blob, fileName: string) {
  if (typeof URL.createObjectURL !== 'function') {
    return
  }

  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.append(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat('ru-RU', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value))
}

function formatReleaseDate(value: string): string {
  return new Intl.DateTimeFormat('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(new Date(value))
}

export default App
