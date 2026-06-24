import { useEffect, useId, useMemo, useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent, ReactNode } from 'react'
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
  Settings,
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
import type { AccountingTypeDto, DictionaryClient, GarageDto, OwnerDto, SupplierDto, SupplierGroupDto, TariffDto, UpsertAccountingTypeRequest, UpsertGarageRequest, UpsertOwnerRequest, UpsertSupplierGroupRequest, UpsertSupplierRequest, UpsertTariffRequest } from './services/dictionariesApi'
import { financeApi } from './services/financeApi'
import type { AccrualDto, CreateAccrualRequest, CreateExpenseOperationRequest, CreateIncomeOperationRequest, CreateMeterReadingRequest, CreateSupplierAccrualRequest, FinanceClient, FinanceSummaryDto, FinancialOperationDto, GenerateRegularAccrualsRequest, MeterReadingDto, SupplierAccrualDto } from './services/financeApi'
import { importApi } from './services/importApi'
import type { AccessImportCheckDto, AccessImportQuarantineItemDto, AccessImportRunDto, AccessImportRunLogEntryDto, ImportClient } from './services/importApi'
import { reportsApi } from './services/reportsApi'
import type { ConsolidatedReportDto, ExpenseReportDto, IncomeReportDto, ReportClient } from './services/reportsApi'
import { releasesApi } from './services/releasesApi'
import type { AppReleaseDto, ReleaseClient } from './services/releasesApi'
import { usersApi } from './services/usersApi'
import type { ManagedRoleDto, ManagedUserDto, UserManagementClient } from './services/usersApi'
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

  if (!form.effectiveFrom || Number.isNaN(Date.parse(form.effectiveFrom))) {
    errors.push('Укажите дату начала тарифа.')
  }

  return errors
}

function isDateInputValue(value: string) {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(Date.parse(value))
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
const garageReportScreenRowLimit = 12
const reportScreenRowLimit = 16
const auditScreenRequestLimit = 50
const financeScreenRequestLimit = 50
const dictionaryScreenRequestLimit = 100
const userScreenRequestLimit = 50
const importQuarantineScreenRequestLimit = 50

type NavigationItem = {
  label: string
  icon: typeof Gauge
  active?: boolean
  requiredAny?: readonly string[]
}

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
  { label: 'Панель', icon: Gauge, active: true },
  { label: 'Справочники', icon: UsersRound, requiredAny: [permissions.dictionariesRead] },
  { label: 'Тарифы', icon: Settings, requiredAny: [permissions.tariffsManage] },
  { label: 'Платежи', icon: WalletCards, requiredAny: [permissions.paymentsRead] },
  { label: 'Отчеты', icon: FileSpreadsheet, requiredAny: [permissions.reportsRead] },
  { label: 'Импорт', icon: DatabaseZap, requiredAny: [permissions.importRun] },
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
  const [auth, setAuth] = useState<AuthResponse | null>(null)

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

        <nav className="nav-list" aria-label="Основные разделы" aria-disabled={!auth}>
          {navigation.map((item) => {
            const Icon = item.icon
            const canOpen = Boolean(auth && hasAnyPermission(auth, item.requiredAny))
            return (
              <button className={item.active ? 'nav-item active' : 'nav-item'} type="button" key={item.label} disabled={!canOpen}>
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
        {auth ? (
          <Workspace auth={auth} authClient={authClient} auditClient={auditClient} dictionaryClient={dictionaryClient} financeClient={financeClient} importClient={importClient} reportClient={reportClient} releaseClient={releaseClient} userClient={userClient} onUserChanged={(user) => setAuth((current) => current ? { ...current, user } : current)} onLogout={() => setAuth(null)} />
        ) : (
          <AuthGate authClient={authClient} onAuthenticated={setAuth} />
        )}
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
          <input value={email} onChange={(event) => setEmail(event.target.value)} type="email" required />
        </label>

        {mode === 'bootstrap' ? (
          <label>
            Имя пользователя
            <input value={displayName} onChange={(event) => setDisplayName(event.target.value)} required />
          </label>
        ) : null}

        <label>
          Пароль
          <input value={password} onChange={(event) => setPassword(event.target.value)} type="password" minLength={8} required />
        </label>
        <p className="form-hint">Минимум 8 символов: заглавная буква, строчная буква и цифра.</p>

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

      <section className="hero-panel">
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

      {canManageUsers ? (
        <UserManagementPanel auth={auth} userClient={userClient} />
      ) : (
        <AccessNotice label="Пользователи недоступны" title="Пользователи" permission={permissions.usersManage} description="Управлять сотрудниками и ролями может только пользователь с правом администрирования." />
      )}

      {canReadDictionaries ? (
        <DictionaryPanel auth={auth} dictionaryClient={dictionaryClient} />
      ) : (
        <AccessNotice label="Справочники недоступны" title="Справочники" permission={permissions.dictionariesRead} description="Для просмотра гаражей, владельцев и поставщиков нужно право на чтение справочников." />
      )}

      {canReadPayments && canReadDictionaries ? (
        <FinancePanel auth={auth} dictionaryClient={dictionaryClient} financeClient={financeClient} />
      ) : (
        <AccessNotice label="Платежи недоступны" title="Платежи" permission={permissions.paymentsRead} description="Для платежей нужны права на просмотр финансовых операций и справочников." />
      )}

      {canRunImport ? (
        <ImportPanel auth={auth} importClient={importClient} />
      ) : (
        <AccessNotice label="Импорт недоступен" title="Импорт Access" permission={permissions.importRun} description="Запускать проверку и перенос старой базы может только пользователь с правом импорта." />
      )}

      {canReadReports && canReadDictionaries ? (
        <ReportPanel auth={auth} dictionaryClient={dictionaryClient} reportClient={reportClient} />
      ) : (
        <AccessNotice label="Отчеты недоступны" title="Отчеты" permission={permissions.reportsRead} description="Для отчетов нужно право просмотра отчетности; справочники используются только для фильтров." />
      )}

      {canReadAudit ? (
        <AuditPanel auth={auth} auditClient={auditClient} />
      ) : (
        <AccessNotice label="Аудит недоступен" title="Аудит" permission={permissions.auditRead} description="Журнал действий доступен только пользователям с правом просмотра audit-событий." />
      )}

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

      <ReleasePanel auth={auth} releaseClient={releaseClient} />
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
            <input aria-label="Новый пароль" type="password" value={form.newPassword} onChange={(event) => setForm({ ...form, newPassword: event.target.value })} minLength={8} required />
          </label>
          <label>
            Повтор нового пароля
            <input aria-label="Повтор нового пароля" type="password" value={form.repeatPassword} onChange={(event) => setForm({ ...form, repeatPassword: event.target.value })} minLength={8} required />
          </label>
        </div>
        <p className="form-hint">Минимум 8 символов: заглавная буква, строчная буква и цифра.</p>
        <FormValidationSummary title="Проверьте смену пароля" items={validationErrors} />
        {error ? <FormError>{error}</FormError> : null}
        {message ? <div className="form-success">{message}</div> : null}
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

      {loading ? <p className="muted">Загружаем историю обновлений...</p> : null}
      {error ? <FormError>{error}</FormError> : null}
      {!loading && !error && releases.length === 0 ? <p className="muted">Пока нет опубликованных изменений.</p> : null}

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
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [tariffs, setTariffs] = useState<TariffDto[]>([])
  const [operations, setOperations] = useState<FinancialOperationDto[]>([])
  const [accruals, setAccruals] = useState<AccrualDto[]>([])
  const [supplierAccruals, setSupplierAccruals] = useState<SupplierAccrualDto[]>([])
  const [meterReadings, setMeterReadings] = useState<MeterReadingDto[]>([])
  const [summary, setSummary] = useState<FinanceSummaryDto>({ incomeTotal: 0, expenseTotal: 0, accrualTotal: 0, balance: 0, debt: 0, operationCount: 0, accrualCount: 0, meterReadingCount: 0 })
  const [incomeForm, setIncomeForm] = useState({ garageId: '', incomeTypeId: '', operationDate: today, accountingMonth: month, amount: 0, documentNumber: '' })
  const [expenseForm, setExpenseForm] = useState({ supplierId: '', expenseTypeId: '', operationDate: today, accountingMonth: month, amount: 0, documentNumber: '' })
  const [accrualForm, setAccrualForm] = useState({ garageId: '', incomeTypeId: '', accountingMonth: month, amount: 0, comment: '' })
  const [supplierAccrualForm, setSupplierAccrualForm] = useState({ supplierId: '', expenseTypeId: '', accountingMonth: month, amount: 0, documentNumber: '', comment: '' })
  const [regularForm, setRegularForm] = useState({ incomeTypeId: '', tariffId: '', accountingMonth: month, comment: '' })
  const [regularStatus, setRegularStatus] = useState<string | null>(null)
  const [meterForm, setMeterForm] = useState({ garageId: '', meterKind: 'water' as 'water' | 'electricity', accountingMonth: month, readingDate: today, currentValue: 0, comment: '' })
  const [incomeValidationErrors, setIncomeValidationErrors] = useState<string[]>([])
  const [expenseValidationErrors, setExpenseValidationErrors] = useState<string[]>([])
  const [accrualValidationErrors, setAccrualValidationErrors] = useState<string[]>([])
  const [supplierAccrualValidationErrors, setSupplierAccrualValidationErrors] = useState<string[]>([])
  const [regularValidationErrors, setRegularValidationErrors] = useState<string[]>([])
  const [meterValidationErrors, setMeterValidationErrors] = useState<string[]>([])
  const [accrualBreakdown, setAccrualBreakdown] = useState<AccrualBreakdown | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  useRestoreFocusOnClose(Boolean(accrualBreakdown))
  const accrualBreakdownCloseButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(accrualBreakdown))
  const accrualBreakdownDialogRef = useFocusTrap<HTMLElement>(Boolean(accrualBreakdown))

  useEscapeKey(Boolean(accrualBreakdown), () => setAccrualBreakdown(null))
  const canWritePayments = hasPermission(auth, permissions.paymentsWrite)
  const visibleOperations = operations.slice(0, 8)
  const visibleAccruals = accruals.slice(0, 8)
  const visibleSupplierAccruals = supplierAccruals.slice(0, 8)
  const visibleMeterReadings = meterReadings.slice(0, 8)

  useEffect(() => {
    let ignore = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const [loadedGarages, loadedSuppliers, loadedIncomeTypes, loadedExpenseTypes, loadedTariffs, loadedOperations, loadedAccruals, loadedSupplierAccruals, loadedMeterReadings, loadedSummary] = await Promise.all([
          dictionaryClient.getGarages(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getIncomeTypes(auth.accessToken, dictionaryScreenRequestLimit),
          dictionaryClient.getExpenseTypes(auth.accessToken, dictionaryScreenRequestLimit),
          dictionaryClient.getTariffs(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          financeClient.getOperations(auth.accessToken, financeScreenRequestLimit),
          financeClient.getAccruals(auth.accessToken, financeScreenRequestLimit),
          financeClient.getSupplierAccruals(auth.accessToken, financeScreenRequestLimit),
          financeClient.getMeterReadings(auth.accessToken, financeScreenRequestLimit),
          financeClient.getSummary(auth.accessToken),
        ])
        if (!ignore) {
          setGarages(loadedGarages)
          setSuppliers(loadedSuppliers)
          setIncomeTypes(loadedIncomeTypes)
          setExpenseTypes(loadedExpenseTypes)
          setTariffs(loadedTariffs)
          setOperations(loadedOperations)
          setAccruals(loadedAccruals)
          setSupplierAccruals(loadedSupplierAccruals)
          setMeterReadings(loadedMeterReadings)
          setSummary(loadedSummary)
          setIncomeForm((value) => ({ ...value, garageId: value.garageId || loadedGarages[0]?.id || '', incomeTypeId: value.incomeTypeId || loadedIncomeTypes[0]?.id || '' }))
          setExpenseForm((value) => ({ ...value, supplierId: value.supplierId || loadedSuppliers[0]?.id || '', expenseTypeId: value.expenseTypeId || loadedExpenseTypes[0]?.id || '' }))
          setAccrualForm((value) => ({ ...value, garageId: value.garageId || loadedGarages[0]?.id || '', incomeTypeId: value.incomeTypeId || loadedIncomeTypes[0]?.id || '' }))
          setSupplierAccrualForm((value) => ({ ...value, supplierId: value.supplierId || loadedSuppliers[0]?.id || '', expenseTypeId: value.expenseTypeId || loadedExpenseTypes[0]?.id || '' }))
          setRegularForm((value) => ({ ...value, incomeTypeId: value.incomeTypeId || loadedIncomeTypes[0]?.id || '', tariffId: value.tariffId || loadedTariffs[0]?.id || '' }))
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
  }, [auth.accessToken, dictionaryClient, financeClient])

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
    }
    const errors = getIncomeValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setIncomeValidationErrors(errors)
      return
    }

    setIncomeValidationErrors([])
    await runSaving('income', async () => {
      const operation = await financeClient.createIncome(auth.accessToken, request)
      addOperation(operation)
      setIncomeForm((value) => ({ ...value, amount: 0, documentNumber: '' }))
    })
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
    }
    const errors = getExpenseValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setExpenseValidationErrors(errors)
      return
    }

    setExpenseValidationErrors([])
    await runSaving('expense', async () => {
      const operation = await financeClient.createExpense(auth.accessToken, request)
      addOperation(operation)
      setExpenseForm((value) => ({ ...value, amount: 0, documentNumber: '' }))
    })
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
      source: 'manual',
      comment: accrualForm.comment,
    }
    const errors = getAccrualValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setAccrualValidationErrors(errors)
      return
    }

    setAccrualValidationErrors([])
    await runSaving('accrual', async () => {
      const accrual = await financeClient.createAccrual(auth.accessToken, request)
      setAccruals((items) => [accrual, ...items])
      setSummary((value) => ({
        ...value,
        accrualTotal: value.accrualTotal + accrual.amount,
        debt: value.debt + accrual.amount,
        accrualCount: value.accrualCount + 1,
      }))
      setAccrualForm((value) => ({ ...value, amount: 0, comment: '' }))
    })
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
    const errors = getRegularAccrualValidationErrors(request)
    if (errors.length > 0) {
      setError(null)
      setRegularValidationErrors(errors)
      return
    }

    setRegularValidationErrors([])
    await runSaving('regular-accruals', async () => {
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
      source: 'manual',
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
    await runSaving('supplier-accrual', async () => {
      const accrual = await financeClient.createSupplierAccrual(auth.accessToken, request)
      setSupplierAccruals((items) => [accrual, ...items])
      setSupplierAccrualForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
    })
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
    await runSaving('meter-reading', async () => {
      const reading = await financeClient.createMeterReading(auth.accessToken, request)
      setMeterReadings((items) => [reading, ...items])
      setSummary((value) => ({ ...value, meterReadingCount: value.meterReadingCount + 1 }))
      setMeterForm((value) => ({ ...value, currentValue: 0, comment: '' }))
    })
  }

  function openAccrualBreakdown(value: AccrualBreakdown) {
    setAccrualBreakdown(value)
  }

  function handleAccrualBreakdownKeyDown(event: KeyboardEvent<HTMLDivElement>, value: AccrualBreakdown) {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      openAccrualBreakdown(value)
    }
  }

  function addOperation(operation: FinancialOperationDto) {
    setOperations((items) => [operation, ...items])
    setSummary((value) => {
      const incomeDelta = operation.operationKind === 'income' ? operation.amount : 0
      const expenseDelta = operation.operationKind === 'expense' ? operation.amount : 0
      return {
        incomeTotal: value.incomeTotal + incomeDelta,
        expenseTotal: value.expenseTotal + expenseDelta,
        balance: value.balance + incomeDelta - expenseDelta,
        debt: value.debt - incomeDelta,
        operationCount: value.operationCount + 1,
        accrualTotal: value.accrualTotal,
        accrualCount: value.accrualCount,
        meterReadingCount: value.meterReadingCount,
      }
    })
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
      setOperations((items) => items.filter((item) => item.id !== operation.id))
      setSummary((value) => {
        const incomeDelta = operation.operationKind === 'income' ? operation.amount : 0
        const expenseDelta = operation.operationKind === 'expense' ? operation.amount : 0
        return {
          ...value,
          incomeTotal: value.incomeTotal - incomeDelta,
          expenseTotal: value.expenseTotal - expenseDelta,
          balance: value.balance - incomeDelta + expenseDelta,
          debt: value.debt + incomeDelta,
          operationCount: Math.max(0, value.operationCount - 1),
        }
      })
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
      setAccruals((items) => items.filter((item) => item.id !== accrual.id))
      setSummary((value) => ({
        ...value,
        accrualTotal: value.accrualTotal - accrual.amount,
        debt: value.debt - accrual.amount,
        accrualCount: Math.max(0, value.accrualCount - 1),
      }))
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
      setSupplierAccruals((items) => items.filter((item) => item.id !== accrual.id))
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
      setMeterReadings((items) => items.filter((item) => item.id !== reading.id))
      setSummary((value) => ({ ...value, meterReadingCount: Math.max(0, value.meterReadingCount - 1) }))
    })
  }

  async function runSaving(scope: string, action: () => Promise<void>) {
    setSaving(scope)
    setError(null)
    try {
      await action()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось сохранить финансовую операцию.')
    } finally {
      setSaving(null)
    }
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

      <div className="finance-grid">
        <form className="dictionary-form" onSubmit={saveIncome}>
          <h3>Новое поступление</h3>
          <select aria-label="Гараж для поступления" value={incomeForm.garageId} onChange={(event) => setIncomeForm({ ...incomeForm, garageId: event.target.value })} required>
            <option value="" disabled>
              Выберите гараж
            </option>
            {garages.map((garage) => (
              <option value={garage.id} key={garage.id}>
                Гараж {garage.number}
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
          <select aria-label="Вид регулярного начисления" value={regularForm.incomeTypeId} onChange={(event) => setRegularForm({ ...regularForm, incomeTypeId: event.target.value })} required>
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
            {tariffs.map((tariff) => (
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
          {regularStatus ? <p className="empty-state">{regularStatus}</p> : null}
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
          {operations.length === 0 ? <p className="empty-state" aria-live="polite">Операций пока нет</p> : null}
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
          {operations.length > visibleOperations.length ? <p className="empty-state" aria-live="polite">Показано {visibleOperations.length} из {operations.length} операций</p> : null}
        </div>

        <div className="operation-list" role="table" aria-label="Последние начисления">
          <div className="operation-row header" role="row">
            <span role="columnheader">Месяц</span>
            <span role="columnheader">Начисление</span>
            <span role="columnheader">Сумма</span>
          </div>
          {accruals.length === 0 ? <p className="empty-state" aria-live="polite">Начислений пока нет</p> : null}
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
          {accruals.length > visibleAccruals.length ? <p className="empty-state" aria-live="polite">Показано {visibleAccruals.length} из {accruals.length} начислений</p> : null}
        </div>

        <div className="operation-list" role="table" aria-label="Последние начисления поставщикам">
          <div className="operation-row header" role="row">
            <span role="columnheader">Месяц</span>
            <span role="columnheader">Поставщик</span>
            <span role="columnheader">Сумма</span>
          </div>
          {supplierAccruals.length === 0 ? <p className="empty-state" aria-live="polite">Начислений поставщикам пока нет</p> : null}
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
          {supplierAccruals.length > visibleSupplierAccruals.length ? <p className="empty-state" aria-live="polite">Показано {visibleSupplierAccruals.length} из {supplierAccruals.length} начислений поставщикам</p> : null}
        </div>

        <div className="operation-list" role="table" aria-label="Последние показания">
          <div className="operation-row header" role="row">
            <span role="columnheader">Месяц</span>
            <span role="columnheader">Счетчик</span>
            <span role="columnheader">Расход</span>
          </div>
          {meterReadings.length === 0 ? <p className="empty-state" aria-live="polite">Показаний пока нет</p> : null}
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
          {meterReadings.length > visibleMeterReadings.length ? <p className="empty-state" aria-live="polite">Показано {visibleMeterReadings.length} из {meterReadings.length} показаний</p> : null}
        </div>
      </div>
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
      {exportMessage ? <div className="form-note">{exportMessage}</div> : null}

      <div className="finance-grid">
        <form className="dictionary-form" onSubmit={runDryRun}>
          <h3>Dry-run Access</h3>
          <input aria-label="Файл Access" type="file" accept=".accdb,.mdb" onChange={(event) => setSelectedFile(event.target.files?.[0] ?? null)} />
          <button className="secondary-button" type="submit" disabled={saving || !selectedFile}>
            <DatabaseZap size={16} />
            <span>Проверить файл</span>
          </button>
          {selectedFile ? <p className="empty-state">{selectedFile.name}</p> : null}
        </form>

        <div className="dictionary-form">
          <h3>Отчет проверки</h3>
          <button className="secondary-button" type="button" disabled={!currentRun || exporting} onClick={downloadCurrentReport}>
            <FileText size={16} />
            <span>Скачать отчет JSON</span>
          </button>
          {currentRun ? (
            <>
              <p className="empty-state">{currentRun.originalFileName} · {formatImportRunCheckSummary(currentRun)}</p>
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
          ) : <p className="empty-state" aria-live="polite">Выберите запуск dry-run</p>}
        </div>

        <div className="operation-list" role="table" aria-label="Проверки импорта">
          <div className="operation-row header" role="row">
            <span role="columnheader">Проверка</span>
            <span role="columnheader">Статус</span>
            <span role="columnheader">Итог</span>
          </div>
          {!currentRun ? <p className="empty-state" aria-live="polite">Проверок пока нет</p> : null}
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

        <div className="operation-list" role="table" aria-label="Лог запуска Access">
          <div className="operation-row header" role="row">
            <span role="columnheader">Шаг</span>
            <span role="columnheader">Уровень</span>
            <span role="columnheader">Сообщение</span>
          </div>
          {loadingLog ? <p className="empty-state" aria-live="polite">Загрузка лога...</p> : null}
          {!loadingLog && runLogEntries.length === 0 ? <p className="empty-state" aria-live="polite">Лог выбранного запуска пока пуст</p> : null}
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
          {runLogEntries.length > visibleRunLogEntries.length ? <p className="empty-state" aria-live="polite">Показано {visibleRunLogEntries.length} из {runLogEntries.length} строк лога</p> : null}
        </div>

        <div className="operation-list" role="table" aria-label="История импорта Access">
          <div className="operation-row header" role="row">
            <span role="columnheader">Файл</span>
            <span role="columnheader">Статус</span>
            <span role="columnheader">Проверки</span>
          </div>
          {runs.length === 0 ? <p className="empty-state" aria-live="polite">Истории импорта пока нет</p> : null}
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
          {runs.length > visibleRuns.length ? <p className="empty-state" aria-live="polite">Показано {visibleRuns.length} из {runs.length} запусков</p> : null}
        </div>

        <div className="operation-list" role="table" aria-label="Карантин импорта Access">
          <div className="operation-row header" role="row">
            <span role="columnheader">Строка</span>
            <span role="columnheader">Причина</span>
            <span role="columnheader">Действие</span>
          </div>
          {quarantineItems.length === 0 ? <p className="empty-state" aria-live="polite">Открытых строк карантина нет</p> : null}
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
          {quarantineItems.length > visibleQuarantineItems.length ? <p className="empty-state" aria-live="polite">Показано {visibleQuarantineItems.length} из {quarantineItems.length} строк карантина</p> : null}
        </div>
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
      {exportMessage ? <div className="form-note">{exportMessage}</div> : null}

      <form className="compact-form" onSubmit={(event) => event.preventDefault()}>
        <input aria-label="Поиск в audit-журнале" placeholder="Действие, сущность или описание" value={search} onChange={(event) => setSearch(event.target.value)} />
      </form>

      <div className="operation-list" role="table" aria-label="События audit-журнала">
        <div className="operation-row header" role="row">
          <span role="columnheader">Дата</span>
          <span role="columnheader">Событие</span>
          <span role="columnheader">Сущность</span>
        </div>
        {!loading && events.length === 0 ? <p className="empty-state" aria-live="polite">Событий пока нет</p> : null}
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
        {events.length > visibleEvents.length ? <p className="empty-state" aria-live="polite">Показано {visibleEvents.length} из {events.length} событий</p> : null}
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
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчет по поступлениям.')
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
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчет по выплатам.')
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

  return (
    <section className="dictionary-panel" aria-label="Отчеты">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Отчеты</p>
          <h2>Консолидированный отчет за период</h2>
        </div>
        <span>{loading ? 'Формируем...' : `${report?.monthlyRows.length ?? 0} месяцев`}</span>
      </div>

      {error ? <FormError>{error}</FormError> : null}
      {exportError ? <FormError>{exportError}</FormError> : null}
      {exportMessage ? <div className="form-note">{exportMessage}</div> : null}

      <form className="compact-form report-filter" onSubmit={applyFilters}>
        <input aria-label="Начало периода отчета" aria-describedby="consolidated-report-date-format" name="monthFrom" type="month" defaultValue={filters.monthFrom.slice(0, 7)} required />
        <input aria-label="Конец периода отчета" aria-describedby="consolidated-report-date-format" name="monthTo" type="month" defaultValue={filters.monthTo.slice(0, 7)} required />
        <p className="form-hint report-date-format" id="consolidated-report-date-format">Формат периода сводного отчета: ММ.ГГГГ.</p>
        <input aria-label="Поиск в отчете" name="search" placeholder="Гараж или владелец" defaultValue={filters.search} />
        <FormValidationSummary title="Проверьте период отчета" items={reportValidationErrors} />
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
      </form>

      <div className="summary-strip" aria-label="Итоги отчета">
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
      <p className="form-hint report-accounting-rule">
        Начисления попадают в сводный отчет по учетному месяцу, поступления и выплаты - по фактической дате операции.
      </p>

      <div className="finance-grid">
        <div className="operation-list" role="table" aria-label="Помесячный отчет">
          <div className="operation-row header" role="row">
            <span role="columnheader">Месяц</span>
            <span role="columnheader">Итоги</span>
            <span role="columnheader">Долг</span>
          </div>
          {report?.monthlyRows.length === 0 ? <p className="empty-state" aria-live="polite">Помесячных строк отчета пока нет</p> : null}
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

        <div className="operation-list" role="table" aria-label="Отчет по гаражам">
          <div className="operation-row header" role="row">
            <span role="columnheader">Гараж</span>
            <span role="columnheader">Начисления</span>
            <span role="columnheader">Долг</span>
          </div>
          {report?.garageRowCount === 0 ? <p className="empty-state" aria-live="polite">По выбранному фильтру гаражей нет</p> : null}
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
          {report && report.garageRowCount > report.garageRows.length ? <p className="empty-state" aria-live="polite">Показано {report.garageRows.length} из {report.garageRowCount} строк</p> : null}
        </div>
      </div>

      <div className="subsection-heading">
        <div>
          <h3>Отчет по поступлениям</h3>
          <p>Начисления и оплаты по гаражам, владельцам и видам поступлений.</p>
        </div>
        <span>{incomeLoading ? 'Формируем...' : `${incomeReport?.rowCount ?? 0} строк`}</span>
      </div>

      {incomeError ? <FormError>{incomeError}</FormError> : null}

      <form className="compact-form report-filter" onSubmit={applyIncomeFilters}>
        <input aria-label="Начало отчета по поступлениям" aria-describedby="income-report-date-format" name="dateFrom" type="date" defaultValue={incomeFilters.dateFrom} required />
        <input aria-label="Конец отчета по поступлениям" aria-describedby="income-report-date-format" name="dateTo" type="date" defaultValue={incomeFilters.dateTo} required />
        <p className="form-hint report-date-format" id="income-report-date-format">Формат дат поступлений: ДД.ММ.ГГГГ.</p>
        <input aria-label="Поиск в поступлениях" name="search" placeholder="Гараж, владелец, документ" defaultValue={incomeFilters.search} />
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
        <select aria-label="Тип строк отчета по поступлениям" name="rowMode" defaultValue={incomeFilters.rowMode}>
          <option value="all">Начисления и оплаты</option>
          <option value="accruals">Только начисления</option>
          <option value="payments">Только оплаты</option>
        </select>
        <FormValidationSummary title="Проверьте отчет по поступлениям" items={incomeReportValidationErrors} />
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
      </form>

      <div className="summary-strip" aria-label="Итоги отчета по поступлениям">
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
      <p className="form-hint report-accounting-rule">
        В поступлениях начисления считаются по учетному месяцу, оплаты - по фактической дате поступления.
      </p>

      <div className="operation-list" role="table" aria-label="Отчет по поступлениям">
        <div className="operation-row header" role="row">
          <span role="columnheader">Дата</span>
          <span role="columnheader">Гараж и вид</span>
          <span role="columnheader">Сумма</span>
        </div>
        {incomeReport?.rows.length === 0 ? <p className="empty-state" aria-live="polite">По выбранному фильтру поступлений нет</p> : null}
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
        {incomeReport && incomeReport.rowCount > incomeReport.rows.length ? <p className="empty-state" aria-live="polite">Показано {incomeReport.rows.length} из {incomeReport.rowCount} строк</p> : null}
      </div>

      <div className="subsection-heading">
        <div>
          <h3>Отчет по выплатам</h3>
          <p>Фактические выплаты поставщикам по датам, видам расходов и документам.</p>
        </div>
        <span>{expenseLoading ? 'Формируем...' : `${expenseReport?.rowCount ?? 0} строк`}</span>
      </div>

      {expenseError ? <FormError>{expenseError}</FormError> : null}

      <form className="compact-form report-filter" onSubmit={applyExpenseFilters}>
        <input aria-label="Начало отчета по выплатам" aria-describedby="expense-report-date-format" name="dateFrom" type="date" defaultValue={expenseFilters.dateFrom} required />
        <input aria-label="Конец отчета по выплатам" aria-describedby="expense-report-date-format" name="dateTo" type="date" defaultValue={expenseFilters.dateTo} required />
        <p className="form-hint report-date-format" id="expense-report-date-format">Формат дат выплат: ДД.ММ.ГГГГ.</p>
        <input aria-label="Поиск в выплатах" name="search" placeholder="Поставщик, вид, документ" defaultValue={expenseFilters.search} />
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
        <select aria-label="Тип строк отчета по выплатам" name="rowMode" defaultValue={expenseFilters.rowMode}>
          <option value="all">Начисления и выплаты</option>
          <option value="accruals">Только начисления</option>
          <option value="payments">Только выплаты</option>
        </select>
        <FormValidationSummary title="Проверьте отчет по выплатам" items={expenseReportValidationErrors} />
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
      </form>

      <div className="summary-strip" aria-label="Итоги отчета по выплатам">
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
      <p className="form-hint report-accounting-rule">
        В выплатах начисления поставщикам считаются по учетному месяцу, фактические выплаты - по дате оплаты.
      </p>

      <div className="operation-list" role="table" aria-label="Отчет по выплатам">
        <div className="operation-row header" role="row">
          <span role="columnheader">Дата</span>
          <span role="columnheader">Поставщик и вид</span>
          <span role="columnheader">Сумма</span>
        </div>
        {expenseReport?.rows.length === 0 ? <p className="empty-state" aria-live="polite">По выбранному фильтру выплат нет</p> : null}
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
        {expenseReport && expenseReport.rowCount > expenseReport.rows.length ? <p className="empty-state" aria-live="polite">Показано {expenseReport.rows.length} из {expenseReport.rowCount} строк</p> : null}
      </div>
    </section>
  )
}

function UserManagementPanel({ auth, userClient }: { auth: AuthResponse; userClient: UserManagementClient }) {
  const [roles, setRoles] = useState<ManagedRoleDto[]>([])
  const [users, setUsers] = useState<ManagedUserDto[]>([])
  const [form, setForm] = useState({ email: '', displayName: '', password: '', roleCode: 'operator' })
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [validationErrors, setValidationErrors] = useState<string[]>([])

  useEffect(() => {
    let ignore = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const [loadedRoles, loadedUsers] = await Promise.all([
          userClient.getRoles(auth.accessToken),
          userClient.getUsers(auth.accessToken, undefined, userScreenRequestLimit),
        ])
        if (!ignore) {
          setRoles(loadedRoles)
          setUsers(loadedUsers)
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

    void load()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, userClient])

  async function saveUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const errors = getManagedUserValidationErrors(form.email, form.displayName, form.password, form.roleCode)
    if (errors.length > 0) {
      setError(null)
      setValidationErrors(errors)
      return
    }

    setValidationErrors([])
    setSaving(true)
    setError(null)
    try {
      const user = await userClient.createUser(auth.accessToken, {
        email: form.email,
        displayName: form.displayName,
        password: form.password,
        roleCodes: [form.roleCode],
        isActive: true,
      })
      setUsers((items) => [user, ...items])
      setForm((value) => ({ email: '', displayName: '', password: '', roleCode: value.roleCode }))
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось создать пользователя.')
    } finally {
      setSaving(false)
    }
  }

  const visibleUsers = users.slice(0, 8)

  return (
    <section className="dictionary-panel" aria-label="Пользователи">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Пользователи</p>
          <h2>Доступ в систему и роли сотрудников</h2>
        </div>
        <span>{loading ? 'Загрузка...' : `${users.length} пользователей`}</span>
      </div>

      {error ? <FormError>{error}</FormError> : null}

      <div className="user-management-grid">
        <form className="dictionary-form" onSubmit={saveUser}>
          <h3>Новый сотрудник</h3>
          <input aria-label="Email пользователя" placeholder="email@example.com" value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} type="email" required />
          <input aria-label="Имя пользователя" placeholder="Имя" value={form.displayName} onChange={(event) => setForm({ ...form, displayName: event.target.value })} required />
          <input aria-label="Пароль пользователя" placeholder="Пароль" value={form.password} onChange={(event) => setForm({ ...form, password: event.target.value })} type="password" minLength={8} required />
          <p className="form-hint">Минимум 8 символов: заглавная буква, строчная буква и цифра.</p>
          <select aria-label="Роль пользователя" value={form.roleCode} onChange={(event) => setForm({ ...form, roleCode: event.target.value })} required>
            {roles.map((role) => (
              <option value={role.code} key={role.code}>
                {role.name}
              </option>
            ))}
          </select>
          <FormValidationSummary title="Проверьте нового пользователя" items={validationErrors} />
          <button className="secondary-button" type="submit" disabled={saving || roles.length === 0}>
            <Plus size={16} />
            <span>Добавить</span>
          </button>
        </form>

        <div className="user-table" role="table" aria-label="Список пользователей">
          <div className="user-table-row header" role="row">
            <span role="columnheader">Сотрудник</span>
            <span role="columnheader">Роли</span>
            <span role="columnheader">Статус</span>
          </div>
          {users.length === 0 ? <p className="empty-state" aria-live="polite">Пользователей пока нет</p> : null}
          {visibleUsers.map((user) => (
            <div className="user-table-row" role="row" key={user.id}>
              <span role="cell">
                <strong>{user.displayName}</strong>
                <small>{user.email}</small>
              </span>
              <span role="cell">{user.roles.join(', ')}</span>
              <span role="cell" className={user.isActive ? 'status-active' : 'status-disabled'}>
                {user.isActive ? 'Активен' : 'Отключен'}
              </span>
            </div>
          ))}
          {users.length > visibleUsers.length ? <p className="empty-state" aria-live="polite">Показано {visibleUsers.length} из {users.length} пользователей</p> : null}
        </div>
      </div>

      <RolePermissionMatrix roles={roles} />
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
        {roles.length === 0 ? <p className="empty-state" aria-live="polite">Роли пока не загружены</p> : null}
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

function DictionaryPanel({ auth, dictionaryClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient }) {
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
  const [tariffForm, setTariffForm] = useState({ name: '', calculationBase: 'fixed', rate: 1, effectiveFrom: '2026-07-01', comment: '' })
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

      setTariffForm((value) => ({ ...value, name: '', rate: 1, comment: '' }))
    })
  }

  function editTariff(tariff: TariffDto) {
    if (editingTariffId === tariff.id) {
      return
    }

    if (editingTariffId && hasUnsavedTariffChanges() && !window.confirm('Перейти к другому тарифу без сохранения изменений?')) {
      return
    }

    const nextForm = {
      name: tariff.name,
      calculationBase: tariff.calculationBase,
      rate: tariff.rate,
      effectiveFrom: tariff.effectiveFrom,
      comment: tariff.comment ?? '',
    }

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
    setTariffForm((value) => ({ ...value, name: '', rate: 1, comment: '' }))
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
          {garageSearchStatus ? <p className="form-hint">{garageSearchStatus}</p> : null}
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
          {supplierSearchStatus ? <p className="form-hint">{supplierSearchStatus}</p> : null}
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
          <select aria-label="База расчета тарифа" value={tariffForm.calculationBase} onChange={(event) => setTariffForm({ ...tariffForm, calculationBase: event.target.value })}>
            <option value="fixed">Фиксированно</option>
            <option value="people">По людям</option>
            <option value="meter_water">По счетчику воды</option>
            <option value="meter_electricity">По счетчику электричества</option>
          </select>
          <div className="inline-fields">
            <input aria-label="Ставка тарифа" type="number" min="0.0001" step="0.0001" value={tariffForm.rate} onChange={(event) => setTariffForm({ ...tariffForm, rate: Number(event.target.value) })} />
            <input aria-label="Дата начала тарифа" type="date" value={tariffForm.effectiveFrom} onChange={(event) => setTariffForm({ ...tariffForm, effectiveFrom: event.target.value })} />
          </div>
          <textarea aria-label="Комментарий тарифа" placeholder="Комментарий" value={tariffForm.comment} onChange={(event) => setTariffForm({ ...tariffForm, comment: event.target.value })} />
          {editingTariffId && hasUnsavedTariffChanges() ? <p className="form-hint">Есть несохраненные изменения тарифа.</p> : null}
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
              meta: `${formatMoney(item.rate)} с ${formatDateOnly(item.effectiveFrom)}${item.comment ? ` · ${item.comment}` : ''}`,
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
    return <p className="empty-state">{emptyText}</p>
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
          <p className="empty-state" aria-live="polite">Показано {visibleItems.length} из {items.length} записей</p>
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

function formatDebtLabel(value: number): string {
  return value < 0 ? 'Переплата' : 'Задолженность'
}

function formatDebtAmount(value: number): string {
  return formatMoney(Math.abs(value))
}

function getDebtClassName(value: number): string {
  return value < 0 ? 'money-overpayment' : 'money-accrual'
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
