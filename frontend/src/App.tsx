import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent, MouseEvent, ReactNode } from 'react'
import {
  Bell,
  BookOpenCheck,
  DatabaseZap,
  FileText,
  FileSpreadsheet,
  Gauge,
  LockKeyhole,
  LogOut,
  PanelLeftClose,
  PanelLeftOpen,
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
import type { AccountingTypeDto, DictionaryClient, GarageDto, OwnerDto, PagedResult, SupplierDto, SupplierGroupDto, TariffDto, UpsertGarageRequest, UpsertOwnerRequest, UpsertSupplierRequest, UpsertTariffRequest } from './services/dictionariesApi'
import { financeApi } from './services/financeApi'
import type { AccrualDto, CreateAccrualRequest, CreateExpenseOperationRequest, CreateIncomeOperationRequest, CreateMeterReadingRequest, CreateSupplierAccrualRequest, FinanceClient, FinancePagedResult, FinanceSummaryDto, FinancialOperationDto, GarageBalanceHistoryDto, GenerateRegularAccrualsRequest, GenerateSupplierGroupSalaryAccrualsRequest, MeterReadingDto, MissingMeterReadingDto, SupplierAccrualDto } from './services/financeApi'
import { importApi } from './services/importApi'
import type { AccessImportQuarantineItemDto, AccessImportRunDto, AccessImportRunLogEntryDto, ImportClient } from './services/importApi'
import { reportsApi } from './services/reportsApi'
import type { ConsolidatedReportDto, ExpenseReportDto, IncomeReportDto, ReportClient } from './services/reportsApi'
import { releasesApi } from './services/releasesApi'
import type { AppReleaseDto, ReleaseClient } from './services/releasesApi'
import { usersApi } from './services/usersApi'
import type { CreateManagedUserRequest, ManagedRoleDto, ManagedUserDto, PagedManagedUsersDto, UpdateManagedUserRequest, UserManagementClient } from './services/usersApi'
import { hasAnyPermission, hasPermission, permissions, rolePermissionGroups } from './shared/accessControl'
import type { DictionaryEditorFieldKey, DictionaryRecord, DictionarySectionKey } from './shared/dictionaryWorkbench'
import { canWriteDictionarySection, createAccountingTypeFormFromDto, createEmptyAccountingTypeForm, createEmptyGarageForm, createEmptyOwnerForm, createEmptyOwnerGarageLinkForm, createEmptySupplierForm, createEmptyTariffForm, createGarageFormFromDto, createOwnerFormFromDto, createSupplierFormFromDto, dictionarySectionGroups, dictionarySectionOptions, getDictionaryEditorFieldMeta, getDictionaryRecordCells, getDictionaryRecordTitle, getDictionarySearchPlaceholder, getDictionarySectionOption, getDictionaryTableHeaders, getOwnerGarageOptions, getTariffCalculationBaseOptions, supportsDictionarySearch, usesElectricityTariffTiers } from './shared/dictionaryWorkbench'
import type { FinanceEditorKey, FinanceSectionKey } from './shared/financeWorkbench'
import { financeSectionOptions, formatFinanceGarageLabel, formatFinanceIncomeGarageSearchStatus, formatFinanceOperationCount, formatFinanceVisibleListStatus, formatFinanceVisibleRange, getFinanceContextMenuLabel, getFinanceEditorFieldLabel, getFinanceEditorSavingScope, getFinanceEditorSubmitLabel, getFinanceEditorTitle, getFinanceEditorUiLabel, getFinanceEditorValidationTitle, getFinanceFallbackLabel, getFinanceMeterKindLabel, getFinanceOptionalText, getFinancePanelLabel, getFinanceSectionDescription, getFinanceTableHeaders, getFinanceToolbarLabel, getFinanceVisibleListEmptyLabel, getFinanceVisibleListTableHeaders, getFinanceVisibleListTableLabel } from './shared/financeWorkbench'
import { buildAuditExportFileName, buildImportReportFileName, buildReportFileName, downloadBlob, getFormValues } from './shared/fileExports'
import { FormError, FormValidationSummary } from './shared/formFeedback'
import {
  formatAccrualSource,
  formatDateOnly,
  formatDateTime,
  formatDebtAmount,
  formatDebtLabel,
  formatImportCheckStatus,
  formatImportLogLevel,
  formatImportRunCheckSummary,
  formatImportRunStatus,
  formatMissingMeterReadings,
  formatMoney,
  formatMonth,
  formatNullableNumber,
  formatPaymentAllocations,
  formatReleaseDate,
  formatTariffRateSummary,
  getDebtClassName,
  getLocalDateInputValue,
} from './shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from './shared/focusHooks'
import { createEmptyPage, createFallbackPage, getPageNavigation, getPageVisibleRange, pageSizeOptions } from './shared/pagination'
import { createDefaultGarageBalanceHistoryFilters, loadConsolidatedReportFilters, loadExpenseReportFilters, loadIncomeReportFilters, saveConsolidatedReportFilters, saveExpenseReportFilters, saveIncomeReportFilters } from './shared/reportFilters'
import { clearStoredAuthSession, loadStoredAuthSession, saveStoredAuthSession } from './shared/sessionStorage'
import type { UserFormState } from './shared/userManagement'
import { getPrimaryRoleCode, getRoleLabel, getUserEditorValidationErrors } from './shared/userManagement'
import type { ConsolidatedReportFilters, ExpenseReportFilters, IncomeReportFilters, OwnerGarageLinkForm } from './shared/validation'
import { chooseRegularTariffId, createTariffFormFromDto, getAccountingTypeValidationErrors, getAccrualValidationErrors, getAuthValidationErrors, getCompatibleRegularTariffs, getExpenseReportValidationErrors, getExpenseValidationErrors, getGarageValidationErrors, getIncomeReportValidationErrors, getIncomeValidationErrors, getMeterReadingValidationErrors, getOwnerGarageLinkValidationErrors, getOwnerValidationErrors, getPasswordChangeValidationErrors, getRegularAccrualValidationErrorsForCatalog, getReportMonthRangeValidationErrors, getSupplierAccrualValidationErrors, getSupplierGroupSalaryValidationErrors, getSupplierGroupValidationErrors, getSupplierValidationErrors, getTariffValidationErrors, parseOptionalNumberInput, updateTariffCalculationBase, withoutElectricityTierFields } from './shared/validation'
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

type AccrualBreakdown =
  | { kind: 'garage'; accrual: AccrualDto }
  | { kind: 'supplier'; accrual: SupplierAccrualDto }

const authSessionStorageKey = 'garagebalance.auth.session'
const sidebarExpandedStorageKey = 'garagebalance.sidebar.expanded'
const garageReportScreenRowLimit = 12
const reportScreenRowLimit = 16
const auditScreenRequestLimit = 50
const financeScreenRequestLimit = 50
const dictionaryScreenRequestLimit = 100
const importQuarantineScreenRequestLimit = 50

function FormField({ label, hint, children, className }: { label: string; hint?: string; children: ReactNode; className?: string }) {
  return (
    <label className={`form-field${className ? ` ${className}` : ''}`}>
      <span className="form-field-label">{label}</span>
      {children}
      {hint ? <span className="form-field-hint">{hint}</span> : null}
    </label>
  )
}

type NavigationItem = {
  section: WorkspaceSection
  label: string
  icon: typeof Gauge
  requiredAny?: readonly string[]
}

type WorkspaceSection = 'dashboard' | 'users' | 'contractors' | 'dictionaries' | 'meterReadings' | 'payments' | 'reports' | 'import' | 'audit' | 'releases' | 'settings'
type ReportTab = 'consolidated' | 'income' | 'expense'
type ImportTab = 'checks' | 'log' | 'history' | 'quarantine'

const navigation: NavigationItem[] = [
  { section: 'dashboard', label: 'Панель', icon: Gauge },
  { section: 'users', label: 'Пользователи', icon: ShieldCheck, requiredAny: [permissions.usersManage] },
  { section: 'contractors', label: 'Контрагенты', icon: UsersRound, requiredAny: [permissions.dictionariesRead] },
  { section: 'dictionaries', label: 'Справочники', icon: UsersRound, requiredAny: [permissions.dictionariesRead] },
  { section: 'meterReadings', label: 'Показания', icon: FileSpreadsheet, requiredAny: [permissions.paymentsRead] },
  { section: 'payments', label: 'Платежи', icon: WalletCards, requiredAny: [permissions.paymentsRead] },
  { section: 'reports', label: 'Отчеты', icon: FileSpreadsheet, requiredAny: [permissions.reportsRead] },
  { section: 'import', label: 'Импорт', icon: DatabaseZap, requiredAny: [permissions.importRun] },
  { section: 'audit', label: 'Audit', icon: FileText, requiredAny: [permissions.auditRead] },
  { section: 'releases', label: 'Что нового', icon: BookOpenCheck },
  { section: 'settings', label: 'Настройки', icon: LockKeyhole },
]

const dashboardTiles: { title: string; section: WorkspaceSection; requiredAny?: readonly string[] }[] = [
  { title: 'Тарифы\nи сборы', section: 'dictionaries', requiredAny: [permissions.dictionariesRead] },
  { title: 'Контрагенты', section: 'contractors', requiredAny: [permissions.dictionariesRead] },
  { title: 'Счётчики', section: 'meterReadings', requiredAny: [permissions.paymentsRead] },
  { title: 'Платежи', section: 'payments', requiredAny: [permissions.paymentsRead] },
  { title: 'Отчёты', section: 'reports', requiredAny: [permissions.reportsRead] },
  { title: 'Настройки', section: 'settings' },
  { title: 'Управление\nфондами', section: 'reports', requiredAny: [permissions.reportsRead] },
]

function loadStoredSidebarExpanded(key: string): boolean {
  try {
    return window.localStorage.getItem(key) === 'true'
  } catch {
    return false
  }
}

function saveStoredSidebarExpanded(key: string, expanded: boolean) {
  try {
    window.localStorage.setItem(key, expanded ? 'true' : 'false')
  } catch {
    // Sidebar state is only a UI preference; the app must work if localStorage is unavailable.
  }
}

function App({ authClient = authApi, auditClient = auditApi, dictionaryClient = dictionariesApi, financeClient = financeApi, importClient = importApi, reportClient = reportsApi, releaseClient = releasesApi, userClient = usersApi }: AppProps) {
  const [auth, setAuth] = useState<AuthResponse | null>(() => loadStoredAuthSession(authSessionStorageKey))
  const [activeSection, setActiveSection] = useState<WorkspaceSection>('dashboard')
  const [isSidebarExpanded, setSidebarExpanded] = useState(() => loadStoredSidebarExpanded(sidebarExpandedStorageKey))

  function handleAuthenticated(nextAuth: AuthResponse) {
    saveStoredAuthSession(authSessionStorageKey, nextAuth)
    setAuth(nextAuth)
  }

  function handleUserChanged(user: CurrentUserDto) {
    setAuth((current) => {
      if (!current) {
        return current
      }

      const nextAuth = { ...current, user }
      saveStoredAuthSession(authSessionStorageKey, nextAuth)
      return nextAuth
    })
  }

  function handleLogout() {
    clearStoredAuthSession(authSessionStorageKey)
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
  const showSidebar = hasPermission(auth, permissions.usersManage)
  const sidebarModeClass = isSidebarExpanded ? 'app-shell--sidebar-expanded' : 'app-shell--sidebar-collapsed'
  const sidebarToggleLabel = isSidebarExpanded ? 'Свернуть панель' : 'Развернуть панель'

  function handleToggleSidebar() {
    setSidebarExpanded((current) => {
      const next = !current
      saveStoredSidebarExpanded(sidebarExpandedStorageKey, next)
      return next
    })
  }

  return (
    <main className={showSidebar ? `app-shell ${sidebarModeClass}` : 'app-shell app-shell--no-sidebar'}>
      {showSidebar ? (
        <aside className={isSidebarExpanded ? 'sidebar sidebar--expanded' : 'sidebar sidebar--collapsed'}>
          <div className="brand">
            <div className="brand-mark">G</div>
            <div className="brand-text">
              <strong>GarageBalance</strong>
              <span>учет гаражного кооператива</span>
            </div>
            <button className="icon-button sidebar-toggle" type="button" aria-label={sidebarToggleLabel} title={sidebarToggleLabel} onClick={handleToggleSidebar}>
              {isSidebarExpanded ? <PanelLeftClose size={19} /> : <PanelLeftOpen size={19} />}
            </button>
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
                  aria-label={item.label}
                  title={item.label}
                  aria-current={isActive ? 'page' : undefined}
                  onClick={() => setActiveSection(item.section)}
                >
                  <Icon size={18} />
                  <span>{item.label}</span>
                </button>
              )
            })}
          </nav>

          <div className="sidebar-footer" title="Безопасный старт">
            <LockKeyhole size={18} />
            <div>
              <strong>Безопасный старт</strong>
              <span>первый этап начинается с ролей и доступа</span>
            </div>
          </div>
        </aside>
      ) : null}

      <section className="workspace">
        <Workspace activeSection={effectiveActiveSection} auth={auth} authClient={authClient} auditClient={auditClient} dictionaryClient={dictionaryClient} financeClient={financeClient} importClient={importClient} reportClient={reportClient} releaseClient={releaseClient} userClient={userClient} showHomeButton={!showSidebar} onOpenSection={setActiveSection} onUserChanged={handleUserChanged} onLogout={handleLogout} />
      </section>
    </main>
  )
}

function AuthGate({ authClient, onAuthenticated }: { authClient: AuthClient; onAuthenticated: (auth: AuthResponse) => void }) {
  const [email, setEmail] = useState('admin@example.com')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  const [loading, setLoading] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    const errors = getAuthValidationErrors('login', email, '', password)
    if (errors.length > 0) {
      setValidationErrors(errors)
      return
    }

    setValidationErrors([])
    setLoading(true)

    try {
      const response = await authClient.login({ email, password })
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
      <form className="auth-card" onSubmit={handleSubmit}>
        <div className="auth-card-header">
          <h1>Авторизация</h1>
        </div>

        <label>
          Email
          <input aria-label="Email" value={email} onChange={(event) => setEmail(event.target.value)} type="email" autoComplete="username" placeholder="admin@example.com" required />
        </label>

        <label>
          Пароль
          <input aria-label="Пароль" value={password} onChange={(event) => setPassword(event.target.value)} type="password" autoComplete="current-password" minLength={8} required />
        </label>

        <FormValidationSummary title="Проверьте форму входа" items={validationErrors} />
        {error ? <FormError>{error}</FormError> : null}

        <button className="primary-button" type="submit" disabled={loading}>
          {loading ? 'Проверяем...' : 'Войти'}
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
  showHomeButton,
  onOpenSection,
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
  showHomeButton: boolean
  onOpenSection: (section: WorkspaceSection) => void
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
          <section className="dashboard-home" aria-label="Панель">
            <div className="dashboard-tile-grid" role="group" aria-label="Главные разделы">
              {dashboardTiles.map((tile) => {
                const canOpen = hasAnyPermission(auth, tile.requiredAny)
                return (
                  <button
                    className="dashboard-tile"
                    type="button"
                    key={tile.title}
                    aria-label={tile.title.replace('\n', ' ')}
                    disabled={!canOpen}
                    onClick={() => onOpenSection(tile.section)}
                  >
                    {tile.title.split('\n').map((line) => (
                      <span key={line}>{line}</span>
                    ))}
                  </button>
                )
              })}
            </div>
          </section>
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
      case 'contractors':
        return canReadDictionaries ? (
          <ContractorsPrototypePanel />
        ) : (
          <AccessNotice label="Контрагенты недоступны" title="Контрагенты" permission={permissions.dictionariesRead} description="Для просмотра настроек услуг и сборов нужно право на чтение справочников." />
        )
      case 'payments':
        return canReadPayments && canReadDictionaries ? (
          <FinancePanel auth={auth} dictionaryClient={dictionaryClient} financeClient={financeClient} />
        ) : (
          <AccessNotice label="Платежи недоступны" title="Платежи" permission={permissions.paymentsRead} description="Для платежей нужны права на просмотр финансовых операций и справочников." />
        )
      case 'meterReadings':
        return canReadPayments ? (
          <MeterReadingsPrototypePanel />
        ) : (
          <AccessNotice label="Показания недоступны" title="Показания" permission={permissions.paymentsRead} description="Для просмотра показаний счетчиков нужно право на чтение финансовых операций." />
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
      case 'settings':
        return <PasswordPanel auth={auth} authClient={authClient} onUserChanged={onUserChanged} />
      default:
        return null
    }
  }

  return (
    <>
      <header className={activeSection === 'dashboard' ? 'topbar topbar--dashboard' : 'topbar'}>
        {showHomeButton && activeSection !== 'dashboard' ? (
          <button className="secondary-button topbar-home-button" type="button" onClick={() => onOpenSection('dashboard')}>
            <Gauge size={17} />
            <span>Панель</span>
          </button>
        ) : null}
        {activeSection !== 'dashboard' ? (
          <div className="search">
            <Search size={18} />
            <span>Поиск по гаражу, владельцу или поставщику</span>
          </div>
        ) : null}
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

type FinanceRecord = FinancialOperationDto | AccrualDto | SupplierAccrualDto | MeterReadingDto
type PaymentsPrototypeDialogKey = 'bank' | 'expense' | 'accrual'

const paymentPrototypeRows = [
  { supplier: '', service: 'Электроэнергия', cost: 39000, paid: 39000, balance: '', collected: 43000, difference: 4000, action: true },
  { supplier: '', service: 'Н/о', cost: 4000, paid: 0, balance: '', collected: 15000, difference: 1000, action: true },
  { supplier: '', service: 'Водоснабжение', cost: 32000, paid: 0, balance: '', collected: 29000, difference: -3000, action: true },
  { supplier: '', service: 'Вывоз мусора', cost: 15000, paid: 0, balance: '', collected: 13300, difference: -1700, action: true },
  { supplier: '', service: 'Юридические услуги', cost: 8500, paid: 0, balance: '', collected: '', difference: '', action: true },
  { supplier: 'Иванов', service: 'Электрик', cost: 20000, paid: '', balance: '', collected: '', difference: '', action: true },
  { supplier: 'Петрова', service: 'Бухгалтерия', cost: 40000, paid: '', balance: '', collected: '', difference: '', action: true },
  { supplier: 'Сидоров', service: 'Председатель', cost: 50000, paid: '', balance: '', collected: '', difference: '', action: true },
  { supplier: '', service: 'Прочие выплаты', cost: 10000, paid: '', balance: '', collected: '', difference: '', action: true },
  { supplier: '', service: 'Авансовые выплаты', cost: '', paid: 16500, balance: '', collected: '', difference: '', action: true },
  { supplier: '', service: 'Выплата без чека', cost: 16500, paid: '', balance: '', collected: '', difference: '', action: true },
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
  const [financeEditorInitialSnapshot, setFinanceEditorInitialSnapshot] = useState('')
  const [financePage, setFinancePage] = useState<FinancePagedResult<FinanceRecord>>({ items: [], totalCount: 0, offset: 0, limit: 25 })
  const [financeSectionCounts, setFinanceSectionCounts] = useState<Record<FinanceSectionKey, number>>({ income: 0, expense: 0, accruals: 0, supplierAccruals: 0, meterReadings: 0 })
  const [financeContextMenu, setFinanceContextMenu] = useState<{ section: FinanceSectionKey; record?: FinanceRecord; x: number; y: number } | null>(null)
  const [paymentsPrototypeDialog, setPaymentsPrototypeDialog] = useState<PaymentsPrototypeDialogKey | null>(null)
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
  useRestoreFocusOnClose(Boolean(financeContextMenu))
  useRestoreFocusOnClose(Boolean(paymentsPrototypeDialog))
  const accrualBreakdownCloseButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(accrualBreakdown))
  const accrualBreakdownDialogRef = useFocusTrap<HTMLElement>(Boolean(accrualBreakdown))
  const financeEditorCloseButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(financeEditor))
  const financeEditorDialogRef = useFocusTrap<HTMLElement>(Boolean(financeEditor))
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

    if (!options?.skipConfirmation && hasUnsavedFinanceEditorChanges() && !window.confirm(getFinanceEditorUiLabel('unsavedConfirm'))) {
      return
    }

    setFinanceEditorInitialSnapshot('')
    setFinanceEditor(null)
  }

  useEscapeKey(Boolean(accrualBreakdown), () => setAccrualBreakdown(null))
  useEscapeKey(Boolean(financeEditor), () => closeFinanceEditor())
  useEscapeKey(Boolean(financeContextMenu), () => setFinanceContextMenu(null))
  useEscapeKey(Boolean(paymentsPrototypeDialog), () => setPaymentsPrototypeDialog(null))
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
      setIncomeGarageSearchStatus(formatFinanceIncomeGarageSearchStatus(foundGarages.length, Boolean(query)))
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
      const nextForm = {
        garageId: record.garageId,
        incomeTypeId: record.incomeTypeId,
        accountingMonth: record.accountingMonth,
        amount: record.amount,
        source: record.source,
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
    setFinanceContextMenu({ section, record, x: event.clientX, y: event.clientY })
  }

  function selectFinanceSection(section: FinanceSectionKey) {
    setFinanceContextMenu(null)
    setActiveFinanceSection(section)
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
    } else if (event.key === 'ContextMenu' || (event.shiftKey && event.key === 'F10')) {
      event.preventDefault()
      const rect = event.currentTarget.getBoundingClientRect()
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
              <tr className="finance-table-row--interactive" key={operation.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'income', operation)} onClick={() => editFinanceRecord('income', operation)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'income', operation)}>
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
              <tr className="finance-table-row--interactive" key={operation.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'expense', operation)} onClick={() => editFinanceRecord('expense', operation)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'expense', operation)}>
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
              <tr className="finance-table-row--interactive" key={accrual.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'accruals', accrual)} onClick={() => editFinanceRecord('accruals', accrual)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'accruals', accrual)}>
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
              <tr className="finance-table-row--interactive" key={accrual.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'supplierAccruals', accrual)} onClick={() => editFinanceRecord('supplierAccruals', accrual)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'supplierAccruals', accrual)}>
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
              <tr className="finance-table-row--interactive" key={reading.id} tabIndex={0} onContextMenu={(event) => openFinanceContextMenu(event, 'meterReadings', reading)} onClick={() => editFinanceRecord('meterReadings', reading)} onKeyDown={(event) => handleFinanceRowKeyDown(event, 'meterReadings', reading)}>
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
  const financeVisibleRange = getPageVisibleRange(financePage)
  const financeNavigation = getPageNavigation(financePage)

  return (
    <section className="finance-panel" aria-label={getFinancePanelLabel('section')}>
      <div className="section-heading">
        <div>
          <p className="eyebrow">{getFinancePanelLabel('section')}</p>
          <h2>{getFinancePanelLabel('title')}</h2>
        </div>
        <span>{loading ? getFinancePanelLabel('loading') : formatFinanceOperationCount(summary.operationCount)}</span>
      </div>

      {error ? <FormError>{error}</FormError> : null}
      {!canWritePayments ? <p className="form-hint">{getFinancePanelLabel('readOnlyHint')}</p> : null}

      <PaymentsPrototypePanel onOpenDialog={setPaymentsPrototypeDialog} />

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
          <div className="dictionary-pagination" role="navigation" aria-label={getFinanceToolbarLabel('pagination')}>
            <span role="status" aria-live="polite">{formatFinanceVisibleRange(financeVisibleRange, financePage.totalCount)}</span>
            <label>
              {getFinanceToolbarLabel('rows')}
              <select aria-label={getFinanceToolbarLabel('pageSize')} value={financePage.limit} onChange={(event) => void loadFinanceWorkbench(activeFinanceSection, 0, Number(event.target.value))}>
                {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
              </select>
            </label>
            <button className="ghost-button" type="button" disabled={loading || !financeNavigation.canGoPrevious} onClick={() => void loadFinanceWorkbench(activeFinanceSection, financeNavigation.previousOffset, financePage.limit)}>{getFinanceToolbarLabel('previousPage')}</button>
            <button className="ghost-button" type="button" disabled={loading || !financeNavigation.canGoNext} onClick={() => void loadFinanceWorkbench(activeFinanceSection, financeNavigation.nextOffset, financePage.limit)}>{getFinanceToolbarLabel('nextPage')}</button>
          </div>
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
          <button ref={financeContextMenuFirstItemRef} type="button" role="menuitem" disabled={!canWritePayments} onClick={() => addFinanceRecord(financeContextMenu.section)}>
            <span>{getFinanceContextMenuLabel('add')}</span>
          </button>
          <button type="button" role="menuitem" disabled={!canWritePayments || !financeContextMenu.record} onClick={() => financeContextMenu.record ? editFinanceRecord(financeContextMenu.section, financeContextMenu.record) : undefined}>
            <span>{getFinanceContextMenuLabel('edit')}</span>
          </button>
          <button type="button" role="menuitem" disabled={!canWritePayments || !financeContextMenu.record} onClick={() => financeContextMenu.record ? deleteFinanceRecord(financeContextMenu.section, financeContextMenu.record) : undefined}>
            <span>{getFinanceContextMenuLabel('delete')}</span>
          </button>
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
                <button className="secondary-button" type="submit" disabled={!canWritePayments || saving === getFinanceEditorSavingScope(financeEditor.section)}>
                  <span>{financeEditor.mode === 'edit' ? getFinanceEditorUiLabel('save') : getFinanceEditorSubmitLabel(financeEditor.section)}</span>
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
      {paymentsPrototypeDialog === 'bank' ? <BankDepositPrototypeDialog onClose={() => setPaymentsPrototypeDialog(null)} /> : null}
      {paymentsPrototypeDialog === 'expense' ? <NewExpensePrototypeDialog onClose={() => setPaymentsPrototypeDialog(null)} /> : null}
      {paymentsPrototypeDialog === 'accrual' ? <NewAccrualPrototypeDialog onClose={() => setPaymentsPrototypeDialog(null)} /> : null}
    </section>
  )
}

function formatPaymentPrototypeValue(value: number | string) {
  return typeof value === 'number' ? value.toLocaleString('ru-RU') : value
}

function PaymentsPrototypePanel({ onOpenDialog }: { onOpenDialog: (dialog: PaymentsPrototypeDialogKey) => void }) {
  const [activeTab, setActiveTab] = useState<'income' | 'expense'>('income')

  return (
    <section className="payments-prototype" aria-label="Форма платежей">
      <div className="payments-prototype-toolbar">
        <div className="payments-prototype-tabs" role="tablist" aria-label="Разделы формы платежей">
          <button type="button" role="tab" aria-selected={activeTab === 'income'} className={activeTab === 'income' ? 'is-active' : undefined} onClick={() => setActiveTab('income')}>
            Поступления
          </button>
          <button type="button" role="tab" aria-selected={activeTab === 'expense'} className={activeTab === 'expense' ? 'is-active' : undefined} onClick={() => setActiveTab('expense')}>
            Выплаты
          </button>
        </div>
        <div className="payments-prototype-actions">
          <button className="secondary-button" type="button" onClick={() => onOpenDialog('accrual')}>
            <Plus size={16} />
            <span>Добавить начисление</span>
          </button>
          <button className="secondary-button" type="button" onClick={() => onOpenDialog('expense')}>
            <Plus size={16} />
            <span>Добавить выплату</span>
          </button>
        </div>
      </div>

      <div className="payments-prototype-sheet">
        <div className="payments-prototype-table-scroll">
          <table className="payments-prototype-table" aria-label="Форма платежей за июнь 2026">
            <thead>
              <tr>
                <th scope="col">
                  <label>
                    <span>Месяц</span>
                    <select aria-label="Месяц формы платежей" defaultValue="2026-06">
                      <option value="2026-06">июнь 2026</option>
                      <option value="2026-05">май 2026</option>
                      <option value="2026-07">июль 2026</option>
                    </select>
                  </label>
                </th>
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
              {paymentPrototypeRows.map((row, index) => (
                <tr key={`${row.supplier}-${row.service}-${index}`}>
                  <td>{index === 0 ? activeTab === 'income' ? 'Поступления' : 'Выплаты' : ''}</td>
                  <td>{row.supplier}</td>
                  <td>{row.service}</td>
                  <td className={row.cost ? 'money-income' : undefined}>{formatPaymentPrototypeValue(row.cost)}</td>
                  <td>{formatPaymentPrototypeValue(row.paid)}</td>
                  <td>{formatPaymentPrototypeValue(row.balance)}</td>
                  {index === 5 ? (
                    <td className="payments-prototype-merged-total" rowSpan={6}>
                      156 800
                    </td>
                  ) : index < 5 ? (
                    <td>{formatPaymentPrototypeValue(row.collected)}</td>
                  ) : null}
                  <td className={typeof row.difference === 'number' ? row.difference >= 0 ? 'money-income' : 'money-expense' : undefined}>
                    {formatPaymentPrototypeValue(row.difference)}
                  </td>
                  <td>
                    {row.action ? (
                      <button className="link-button" type="button" onClick={() => onOpenDialog('expense')} aria-label={`Оплатить ${row.service}`}>
                        Оплатить
                      </button>
                    ) : null}
                  </td>
                </tr>
              ))}
              <tr className="payments-prototype-total-row">
                <td>ИТОГО</td>
                <td />
                <td />
                <td>235 000</td>
                <td>55 500</td>
                <td />
                <td>257 100</td>
                <td className="money-income">22 100</td>
                <td />
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div className="payments-prototype-footer" aria-label="Итоги кассы и банка">
        <div>
          <span>Сумма в банке</span>
          <strong>234 000</strong>
        </div>
        <div>
          <span>Касса</span>
          <strong>23 100</strong>
        </div>
        <div>
          <span>ИТОГО</span>
          <strong className="money-income">257 100</strong>
        </div>
        <button className="secondary-button" type="button" onClick={() => onOpenDialog('bank')}>
          Сдать кассу в банк
        </button>
      </div>
    </section>
  )
}

function BankDepositPrototypeDialog({ onClose }: { onClose: () => void }) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useEscapeKey(true, onClose)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog payments-prototype-dialog--wide" role="dialog" aria-modal="true" aria-labelledby="bank-deposit-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="bank-deposit-title">Учет суммы на счете в банке</h3>
            <p>Макет формы фиксации суммы, которая переносится из кассы на банковский счет.</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть учет суммы в банке" onClick={onClose}>
            <X size={18} />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={(event) => {
          event.preventDefault()
          onClose()
        }}>
          <FormField label="Сумма">
            <input aria-label="Сумма в банке" inputMode="decimal" />
          </FormField>
          <FormField label="Дата">
            <input aria-label="Дата учета суммы в банке" type="date" />
          </FormField>
          <FormField label="Коммент">
            <textarea aria-label="Комментарий к сумме в банке" rows={5} />
          </FormField>
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit">Ок</button>
            <button className="secondary-button" type="button" onClick={onClose}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function NewExpensePrototypeDialog({ onClose }: { onClose: () => void }) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useEscapeKey(true, onClose)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog" role="dialog" aria-modal="true" aria-labelledby="new-expense-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="new-expense-title">Новая выплата</h3>
            <p>Макет формы выплаты поставщику или выдачи без чека.</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть новую выплату" onClick={onClose}>
            <X size={18} />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={(event) => {
          event.preventDefault()
          onClose()
        }}>
          <FormField label="Тип выплаты">
            <select aria-label="Тип выплаты" defaultValue="advance">
              <option value="advance">Авансовая выплата</option>
              <option value="no-receipt">Выплата без чека</option>
            </select>
          </FormField>
          <FormField label="Дата">
            <input aria-label="Дата выплаты" type="date" />
          </FormField>
          <FormField label="Сумма">
            <input aria-label="Сумма выплаты" inputMode="decimal" />
          </FormField>
          <FormField label="Назначение">
            <input aria-label="Назначение выплаты" />
          </FormField>
          <FormField label="Комментарий">
            <textarea aria-label="Комментарий к выплате" rows={4} />
          </FormField>
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit">Провести</button>
            <button className="secondary-button" type="button" onClick={onClose}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function NewAccrualPrototypeDialog({ onClose }: { onClose: () => void }) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useEscapeKey(true, onClose)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog payments-prototype-dialog payments-prototype-dialog--wide" role="dialog" aria-modal="true" aria-labelledby="new-accrual-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="new-accrual-title">Новое начисление</h3>
            <p>Макет ручного начисления по основанию, сумме и комментарию.</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть новое начисление" onClick={onClose}>
            <X size={18} />
          </button>
        </div>
        <form className="dictionary-modal-form payments-prototype-modal-form" onSubmit={(event) => {
          event.preventDefault()
          onClose()
        }}>
          <FormField label="Основание">
            <input aria-label="Основание начисления" />
          </FormField>
          <FormField label="Сумма">
            <input aria-label="Сумма начисления" inputMode="decimal" />
          </FormField>
          <FormField label="Комментарий">
            <textarea aria-label="Комментарий к начислению" rows={5} />
          </FormField>
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit">Ок</button>
            <button className="secondary-button" type="button" onClick={onClose}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function ImportPanel({ auth, importClient }: { auth: AuthResponse; importClient: ImportClient }) {
  const fileInputId = useId()
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
          <div className="file-picker">
            <span className="form-field-label">Файл Access</span>
            <input id={fileInputId} aria-label="Файл Access" type="file" accept=".accdb,.mdb" onChange={(event) => setSelectedFile(event.target.files?.[0] ?? null)} />
            <label className="file-picker-button" htmlFor={fileInputId}>
              <FileText size={16} aria-hidden="true" />
              <span>Выбрать .accdb или .mdb</span>
            </label>
            <span className="file-picker-name" role="status" aria-live="polite">{selectedFile ? selectedFile.name : 'Файл не выбран'}</span>
          </div>
          <button className="secondary-button" type="submit" disabled={saving || !selectedFile}>
            <DatabaseZap size={16} />
            <span>Проверить файл</span>
          </button>
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
    saveConsolidatedReportFilters(nextFilters)
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
    saveIncomeReportFilters(nextFilters)
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
    saveExpenseReportFilters(nextFilters)
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

function UserManagementPanel({ auth, userClient }: { auth: AuthResponse; userClient: UserManagementClient }) {
  const [roles, setRoles] = useState<ManagedRoleDto[]>([])
  const [page, setPage] = useState<PagedManagedUsersDto>(() => createEmptyPage<ManagedUserDto>())
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

  const pageVisibleRange = getPageVisibleRange(page)
  const pageNavigation = getPageNavigation(page)

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
            <span role="status" aria-live="polite">Показано {pageVisibleRange.from}-{pageVisibleRange.to} из {page.totalCount}</span>
            <label>
              Строк:
              <select aria-label="Количество строк пользователей" value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setOffset(0) }}>
                {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
              </select>
            </label>
            <button className="ghost-button" type="button" onClick={() => setOffset(pageNavigation.previousOffset)} disabled={!pageNavigation.canGoPrevious || loading}>Назад</button>
            <button className="ghost-button" type="button" onClick={() => setOffset(pageNavigation.nextOffset)} disabled={!pageNavigation.canGoNext || loading}>Вперед</button>
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
                <FormField label="Email">
                  <input aria-label="Email пользователя" placeholder="email@example.com" value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} type="email" required />
                </FormField>
              ) : (
                <FormField label="Email">
                  <input aria-label="Email пользователя" value={form.email} disabled />
                </FormField>
              )}
              <FormField label="Имя сотрудника">
                <input aria-label="Имя пользователя" placeholder="ФИО или рабочее имя" value={form.displayName} onChange={(event) => setForm({ ...form, displayName: event.target.value })} required />
              </FormField>
              <FormField label="Роль">
                <select aria-label="Роль пользователя" value={form.roleCode} onChange={(event) => setForm({ ...form, roleCode: event.target.value })} required>
                  {roles.map((role) => (
                    <option value={role.code} key={role.code}>{role.name}</option>
                  ))}
                </select>
              </FormField>
              <FormField label="Статус">
                <select aria-label="Статус пользователя" value={form.isActive ? 'active' : 'inactive'} onChange={(event) => setForm({ ...form, isActive: event.target.value === 'active' })}>
                  <option value="active">Активен</option>
                  <option value="inactive">Отключен</option>
                </select>
              </FormField>
              <FormField label={editor.mode === 'create' ? 'Пароль' : 'Новый пароль'}>
                <input
                  aria-label="Пароль пользователя"
                  aria-describedby="new-user-password-policy-hint"
                  placeholder={editor.mode === 'create' ? 'Пароль' : 'Оставьте пустым, если менять не нужно'}
                  value={form.password}
                  onChange={(event) => setForm({ ...form, password: event.target.value })}
                  type="password"
                  minLength={editor.mode === 'create' ? 8 : undefined}
                  required={editor.mode === 'create'}
                />
              </FormField>
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

type ContractorTariffRow = {
  title: string
  amount?: string
  unit?: string
  threshold?: string
  byMeter: boolean
  tiered: boolean
  group?: string
}

const contractorTariffRows: ContractorTariffRow[] = [
  { group: 'Вода', title: 'Тариф на воду', amount: '', unit: 'руб.', byMeter: true, tiered: false },
  { title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: true, tiered: false },
  { group: 'Мусор', title: 'Ставка за вывоз мусора', amount: '', unit: 'руб.', byMeter: false, tiered: false },
  { title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: false, tiered: false },
  { group: 'Электроэнергия', title: 'От 0 кВт', threshold: 'x', amount: '', unit: 'руб.', byMeter: true, tiered: true },
  { title: 'От 1 кВт', threshold: 'x', amount: '', unit: 'руб.', byMeter: true, tiered: true },
  { title: 'От 3 кВт', threshold: 'x', amount: '', unit: 'руб.', byMeter: true, tiered: true },
  { title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: true, tiered: true },
  { group: 'Членский взнос', title: 'Сумма членского взноса', amount: '', unit: 'руб.', byMeter: false, tiered: false },
  { title: 'Оплата до', amount: '30 июн', byMeter: false, tiered: false },
  { title: 'Учитывать платеж с', amount: '01 янв', byMeter: false, tiered: false },
  { title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: false, tiered: false },
  { group: 'Целевой взнос', title: 'Сумма целевого взноса', amount: '', unit: 'руб.', byMeter: false, tiered: false },
  { title: 'Оплата за год до', amount: '30 июн', byMeter: false, tiered: false },
  { title: 'Учитывать платеж с', amount: '01 янв', byMeter: false, tiered: false },
  { title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: false, tiered: false },
  { group: 'Наружное освещение', title: 'Оплата за год до', amount: '31 дек', byMeter: false, tiered: false },
  { title: 'Учитывать платеж с', amount: '01 янв', byMeter: false, tiered: false },
  { title: 'Перенос долга в просроченный', amount: '0', unit: 'дн.', byMeter: false, tiered: false },
  { group: 'Зарплатный фонд', title: 'Электрики', amount: '', unit: 'руб.', byMeter: false, tiered: false },
  { title: 'Бухгалтерия', amount: '', unit: 'руб.', byMeter: false, tiered: false },
  { title: 'Руководство', amount: '', unit: 'руб.', byMeter: false, tiered: false },
]

const contractorOneTimeRows = [
  { name: 'Вступительный взнос', amount: '' },
  { name: 'Подключение канализации', amount: '' },
  { name: 'Подключение к линии электросети', amount: '' },
  { name: 'Штраф за то', amount: '' },
  { name: 'Штраф за это', amount: '' },
]

function ContractorsPrototypePanel() {
  const [modal, setModal] = useState<'service' | 'fee' | null>(null)

  return (
    <section className="contractors-page" aria-label="Контрагенты">
      <div className="contractors-heading">
        <div>
          <h1>Контрагенты</h1>
          <p>Черновой вид страницы услуг, регулярных платежей и разовых сборов.</p>
        </div>
        <div className="contractors-actions">
          <button className="secondary-button" type="button" onClick={() => setModal('service')}>
            <Plus size={17} />
            <span>Добавить услугу</span>
          </button>
          <button className="primary-button contractors-primary-action" type="button" onClick={() => setModal('fee')}>
            <Plus size={17} />
            <span>Объявить сбор</span>
          </button>
        </div>
      </div>

      <div className="contractors-sheet" role="table" aria-label="Тарифы и сборы контрагентов">
        <div className="contractors-sheet-header" role="row">
          <span role="columnheader">Основание</span>
          <span role="columnheader">Значение</span>
          <span role="columnheader">Ед.</span>
          <span role="columnheader">Пороговая тарификация</span>
          <span role="columnheader">По счетчику</span>
        </div>
        {contractorTariffRows.map((row, index) => (
          <div className={row.group ? 'contractors-sheet-row contractors-sheet-row--group' : 'contractors-sheet-row'} role="row" key={`${row.group ?? row.title}-${index}`}>
            <span role="cell">
              {row.group ? <strong>{row.group}</strong> : null}
              <span>{row.title}</span>
            </span>
            <span role="cell" className="contractors-value-cell">
              {row.threshold ? <em>{row.threshold}</em> : null}
              {row.amount}
            </span>
            <span role="cell">{row.unit ?? ''}</span>
            <span role="cell">{row.tiered ? 'Да' : 'Нет'}</span>
            <span role="cell">{row.byMeter ? 'Да' : 'Нет'}</span>
          </div>
        ))}
      </div>

      <div className="contractors-bottom-grid">
        <section className="contractors-mini-table" aria-label="Нерегулярные платежи">
          <div className="contractors-mini-title">Нерегулярные платежи</div>
          <div className="contractors-mini-header">
            <span>Основание</span>
            <span>Сумма, руб.</span>
          </div>
          {contractorOneTimeRows.map((row) => (
            <div className="contractors-mini-row" key={row.name}>
              <span>{row.name}</span>
              <span>{row.amount}</span>
            </div>
          ))}
        </section>

        <div className="contractors-action-stack" aria-label="Действия по контрагентам">
          <button className="secondary-button" type="button" onClick={() => setModal('service')}>
            Добавить услугу
          </button>
          <button className="secondary-button" type="button" onClick={() => setModal('fee')}>
            Объявить сбор
          </button>
        </div>
      </div>

      {modal === 'service' ? <AddServicePrototypeDialog onClose={() => setModal(null)} /> : null}
      {modal === 'fee' ? <AddFeePrototypeDialog onClose={() => setModal(null)} /> : null}
    </section>
  )
}

function AddServicePrototypeDialog({ onClose }: { onClose: () => void }) {
  const [isRegular, setIsRegular] = useState(true)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useEscapeKey(true, onClose)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-service-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="contractor-service-title">Добавить услугу</h3>
            <p>Макет формы для будущего подключения услуги к расчетам.</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть форму услуги" onClick={onClose}>
            <X size={18} />
          </button>
        </div>

        <form className="dictionary-modal-form contractors-modal-form" onSubmit={(event) => {
          event.preventDefault()
          onClose()
        }}>
          <FormField label="Наименование услуги">
            <input aria-label="Наименование услуги" />
          </FormField>
          <label className="contractors-check-row">
            <input type="checkbox" aria-label="Регулярные платежи" checked={isRegular} onChange={(event) => setIsRegular(event.target.checked)} />
            <span>Регулярные платежи</span>
          </label>
          {isRegular ? (
            <>
              <FormField label="Периодичность">
                <input aria-label="Периодичность" defaultValue="12" />
              </FormField>
              <FormField label="Учитывать платеж с">
                <select aria-label="Учитывать платеж с" defaultValue="Январь">
                  <option>Январь</option>
                  <option>Июль</option>
                </select>
              </FormField>
              <FormField label="Оплатить до">
                <select aria-label="Оплатить до" defaultValue="Июль">
                  <option>Июнь</option>
                  <option>Июль</option>
                  <option>Декабрь</option>
                </select>
              </FormField>
              <FormField label="Перенос долга в просроченный">
                <div className="contractors-inline-field">
                  <input aria-label="Перенос долга в просроченный" defaultValue="30" />
                  <span>дн.</span>
                </div>
              </FormField>
              <label className="contractors-check-row">
                <input type="checkbox" aria-label="По счетчику" defaultChecked />
                <span>По счетчику</span>
              </label>
              <label className="contractors-check-row">
                <input type="checkbox" aria-label="Пороговая тарификация" defaultChecked />
                <span>Пороговая тарификация</span>
              </label>
              <FormField label="Единица измерения">
                <input aria-label="Единица измерения" />
              </FormField>
              <div className="contractors-threshold-grid" aria-label="Пороги тарификации">
                <span>Порог 1</span>
                <input aria-label="Порог 1" />
                <span>x</span>
                <span>Цена за ед.</span>
                <input aria-label="Цена за единицу 1" />
                <span>Порог 2</span>
                <input aria-label="Порог 2" />
                <span>x</span>
                <span>Цена за ед.</span>
                <input aria-label="Цена за единицу 2" />
              </div>
              <button className="link-button" type="button">Добавить порог</button>
            </>
          ) : (
            <FormField label="Стоимость">
              <div className="contractors-inline-field">
                <input aria-label="Стоимость услуги" />
                <span>руб.</span>
              </div>
            </FormField>
          )}

          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit">
              <Save size={17} />
              <span>Сохранить</span>
            </button>
            <button className="secondary-button" type="button" onClick={onClose}>
              Отмена
            </button>
          </div>
        </form>
      </section>
    </div>
  )
}

function AddFeePrototypeDialog({ onClose }: { onClose: () => void }) {
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useEscapeKey(true, onClose)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-fee-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <h3 id="contractor-fee-title">Добавить сбор</h3>
            <p>Макет объявления разового или целевого сбора.</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть форму сбора" onClick={onClose}>
            <X size={18} />
          </button>
        </div>

        <form className="dictionary-modal-form contractors-modal-form" onSubmit={(event) => {
          event.preventDefault()
          onClose()
        }}>
          <FormField label="Наименование сбора">
            <input aria-label="Наименование сбора" />
          </FormField>
          <FormField label="Цель">
            <input aria-label="Цель сбора" />
          </FormField>
          <FormField label="Сумма взноса">
            <div className="contractors-inline-field">
              <input aria-label="Сумма взноса" />
              <span>руб.</span>
            </div>
          </FormField>
          <label className="contractors-check-row">
            <input type="checkbox" aria-label="Все гаражи" defaultChecked />
            <span>Все гаражи</span>
          </label>
          <FormField label="Сумма сбора">
            <input aria-label="Сумма сбора" />
          </FormField>
          <FormField label="Дата начала">
            <div className="contractors-inline-field">
              <input aria-label="Дата начала" type="date" />
              <button className="link-button" type="button">Сегодня</button>
            </div>
          </FormField>
          <FormField label="Перенос долга по сбору в просроченный">
            <input aria-label="Перенос долга по сбору в просроченный" />
          </FormField>

          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit">
              Объявить сбор
            </button>
            <button className="secondary-button" type="button" onClick={onClose}>
              Отмена
            </button>
          </div>
        </form>
      </section>
    </div>
  )
}

const meterReadingMonths = [
  'янв',
  'фев',
  'мар',
  'апр',
  'май',
  'июн',
  'июл',
  'авг',
  'сен',
  'окт',
  'ноя',
  'дек',
]

const meterReadingGarageRows = Array.from({ length: 35 }, (_, index) => {
  const garageNumber = index + 1
  return {
    garageNumber,
    readings: meterReadingMonths.map((month, monthIndex) => ({
      month,
      value: garageNumber === 1 && monthIndex === 0 ? '4654' : garageNumber === 7 && monthIndex === 5 ? '1280' : '',
      consumption: garageNumber === 1 && monthIndex === 1 ? '36' : garageNumber === 12 && monthIndex === 8 ? '18' : '',
    })),
  }
})

function MeterReadingsPrototypePanel() {
  const [year, setYear] = useState('2026')

  return (
    <section className="meter-readings-page" aria-label="Показания">
      <div className="meter-readings-heading">
        <div>
          <h1>Показания</h1>
          <p>Черновой вид годовой таблицы показаний счетчиков по гаражам.</p>
        </div>
        <div className="meter-readings-controls">
          <FormField label="Год">
            <select aria-label="Год показаний" value={year} onChange={(event) => setYear(event.target.value)}>
              <option>2026</option>
              <option>2025</option>
              <option>2024</option>
            </select>
          </FormField>
          <FormField label="Тип">
            <select aria-label="Тип показаний" defaultValue="electricity">
              <option value="electricity">Электроэнергия, кВт</option>
              <option value="water">Вода, м3</option>
            </select>
          </FormField>
        </div>
      </div>

      <div className="meter-readings-table-shell">
        <div className="meter-readings-table" role="table" aria-label={`Показания счетчиков за ${year} год`}>
          <div className="meter-readings-title-row" role="row">
            <span role="columnheader">Гараж</span>
            <span role="columnheader">Показания</span>
          </div>
          <div className="meter-readings-month-row" role="row">
            <span role="columnheader">Гараж</span>
            {meterReadingMonths.map((month) => (
              <span role="columnheader" key={month}>
                <strong>{month}</strong>
                <small>кВт</small>
              </span>
            ))}
          </div>
          {meterReadingGarageRows.map((row) => (
            <div className="meter-readings-data-row" role="row" key={row.garageNumber}>
              <span role="rowheader">{row.garageNumber}</span>
              {row.readings.map((reading) => (
                <span role="cell" key={`${row.garageNumber}-${reading.month}`}>
                  <input aria-label={`Гараж ${row.garageNumber}, ${reading.month}, показание`} defaultValue={reading.value} />
                  <input aria-label={`Гараж ${row.garageNumber}, ${reading.month}, кВт`} defaultValue={reading.consumption} />
                </span>
              ))}
            </div>
          ))}
        </div>
      </div>
    </section>
  )
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
    owners: createEmptyPage<DictionaryRecord>(),
    garages: createEmptyPage<DictionaryRecord>(),
    supplierGroups: createEmptyPage<DictionaryRecord>(),
    suppliers: createEmptyPage<DictionaryRecord>(),
    incomeTypes: createEmptyPage<DictionaryRecord>(),
    expenseTypes: createEmptyPage<DictionaryRecord>(),
    tariffs: createEmptyPage<DictionaryRecord>(),
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
  const [ownerForm, setOwnerForm] = useState<UpsertOwnerRequest>(createEmptyOwnerForm())
  const [ownerGarageLinkForm, setOwnerGarageLinkForm] = useState<OwnerGarageLinkForm>(createEmptyOwnerGarageLinkForm())
  const [garageForm, setGarageForm] = useState(createEmptyGarageForm())
  const [supplierGroupName, setSupplierGroupName] = useState('')
  const [supplierForm, setSupplierForm] = useState(createEmptySupplierForm())
  const [accountingTypeForm, setAccountingTypeForm] = useState(createEmptyAccountingTypeForm())
  const [tariffForm, setTariffForm] = useState<UpsertTariffRequest>(createEmptyTariffForm())
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
  const activeOption = getDictionarySectionOption(activeSection)
  const canWriteActiveSection = canWriteDictionarySection(activeSection, canWriteDictionaries, canManageTariffs)
  const supportsSearch = supportsDictionarySearch(activeSection)
  const searchPlaceholder = getDictionarySearchPlaceholder(activeSection)
  const ownerGarageOptions = getOwnerGarageOptions(garageOptions, editor?.section === 'owners' && editor.item ? editor.item as OwnerDto : undefined)

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
    const query = supportsDictionarySearch(section) ? search.trim() || undefined : undefined
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
        setOwnerForm(createOwnerFormFromDto(owner))
        setOwnerGarageLinkForm({ ...createEmptyOwnerGarageLinkForm(), existingGarageId: garageOptions.find((garage) => garage.ownerId === owner.id)?.id ?? '' })
      } else if (section === 'garages') {
        const garage = item as GarageDto
        setGarageForm(createGarageFormFromDto(garage))
      } else if (section === 'supplierGroups') {
        setSupplierGroupName((item as SupplierGroupDto).name)
      } else if (section === 'suppliers') {
        const supplier = item as SupplierDto
        setSupplierForm(createSupplierFormFromDto(supplier))
      } else if (section === 'incomeTypes' || section === 'expenseTypes') {
        const type = item as AccountingTypeDto
        setAccountingTypeForm(createAccountingTypeFormFromDto(type))
      } else {
        const tariff = item as TariffDto
        setTariffForm(createTariffFormFromDto(tariff))
      }
    } else {
      setOwnerForm(createEmptyOwnerForm())
      setOwnerGarageLinkForm(createEmptyOwnerGarageLinkForm())
      setGarageForm(createEmptyGarageForm())
      setSupplierGroupName('')
      setSupplierForm(createEmptySupplierForm(groupOptions[0]?.id ?? ''))
      setAccountingTypeForm(createEmptyAccountingTypeForm())
      setTariffForm(createEmptyTariffForm())
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
    const navigation = getPageNavigation(activePage)
    const nextOffset = direction === -1 ? navigation.previousOffset : navigation.nextOffset
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
    return getDictionaryTableHeaders(activeSection).map((header) => <th key={header}>{header}</th>)
  }

  function renderCells(item: DictionaryRecord) {
    return getDictionaryRecordCells(activeSection, item).map((value, index) => <td key={index}>{value}</td>)
  }

  function renderEditorFields(section: DictionarySectionKey) {
    const fieldMeta = getDictionaryEditorFieldMeta
    const dictionaryField = (key: DictionaryEditorFieldKey, children: ReactNode) => {
      const meta = fieldMeta(key)
      return <FormField label={meta.label} hint={meta.hint}>{children}</FormField>
    }

    if (section === 'owners') {
      return (
        <>
          {dictionaryField('ownerLastName', <input aria-label={fieldMeta('ownerLastName').ariaLabel} placeholder={fieldMeta('ownerLastName').placeholder} value={ownerForm.lastName} onChange={(event) => setOwnerForm({ ...ownerForm, lastName: event.target.value })} required />)}
          {dictionaryField('ownerFirstName', <input aria-label={fieldMeta('ownerFirstName').ariaLabel} placeholder={fieldMeta('ownerFirstName').placeholder} value={ownerForm.firstName} onChange={(event) => setOwnerForm({ ...ownerForm, firstName: event.target.value })} required />)}
          {dictionaryField('ownerMiddleName', <input aria-label={fieldMeta('ownerMiddleName').ariaLabel} placeholder={fieldMeta('ownerMiddleName').placeholder} value={ownerForm.middleName ?? ''} onChange={(event) => setOwnerForm({ ...ownerForm, middleName: event.target.value })} />)}
          {dictionaryField('ownerPhone', <input aria-label={fieldMeta('ownerPhone').ariaLabel} placeholder={fieldMeta('ownerPhone').placeholder} value={ownerForm.phone ?? ''} onChange={(event) => setOwnerForm({ ...ownerForm, phone: event.target.value })} />)}
          {dictionaryField('ownerAddress', <input aria-label={fieldMeta('ownerAddress').ariaLabel} placeholder={fieldMeta('ownerAddress').placeholder} value={ownerForm.address ?? ''} onChange={(event) => setOwnerForm({ ...ownerForm, address: event.target.value })} />)}
          {dictionaryField('ownerMeterNotes', <textarea aria-label={fieldMeta('ownerMeterNotes').ariaLabel} placeholder={fieldMeta('ownerMeterNotes').placeholder} value={ownerForm.meterNotes ?? ''} onChange={(event) => setOwnerForm({ ...ownerForm, meterNotes: event.target.value })} />)}
          <div className="dictionary-form-section">
            <h4>Гараж владельца</h4>
            {dictionaryField('ownerExistingGarage', (
              <select aria-label={fieldMeta('ownerExistingGarage').ariaLabel} value={ownerGarageLinkForm.existingGarageId} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, existingGarageId: event.target.value })}>
                <option value="">Не привязывать существующий гараж</option>
                {ownerGarageOptions.map((garage) => (
                  <option value={garage.id} key={garage.id}>
                    {garage.ownerName ? `Гараж ${garage.number} - ${garage.ownerName}` : `Гараж ${garage.number}`}
                  </option>
                ))}
              </select>
            ))}
            <div className="inline-fields">
              {dictionaryField('ownerNewGarageNumber', <input aria-label={fieldMeta('ownerNewGarageNumber').ariaLabel} placeholder={fieldMeta('ownerNewGarageNumber').placeholder} value={ownerGarageLinkForm.newGarageNumber} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, newGarageNumber: event.target.value })} />)}
              {dictionaryField('ownerNewGaragePeopleCount', <input aria-label={fieldMeta('ownerNewGaragePeopleCount').ariaLabel} type="number" min="0" value={ownerGarageLinkForm.peopleCount} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, peopleCount: Number(event.target.value) })} />)}
              {dictionaryField('ownerNewGarageFloorCount', <input aria-label={fieldMeta('ownerNewGarageFloorCount').ariaLabel} type="number" min="0" value={ownerGarageLinkForm.floorCount} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, floorCount: Number(event.target.value) })} />)}
            </div>
            <div className="inline-fields">
              {dictionaryField('ownerNewGarageStartingBalance', <input aria-label={fieldMeta('ownerNewGarageStartingBalance').ariaLabel} type="number" step="0.01" value={ownerGarageLinkForm.startingBalance} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, startingBalance: Number(event.target.value) })} />)}
              {dictionaryField('ownerNewGarageInitialWaterMeterValue', <input aria-label={fieldMeta('ownerNewGarageInitialWaterMeterValue').ariaLabel} type="number" min="0" step="0.001" value={ownerGarageLinkForm.initialWaterMeterValue} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, initialWaterMeterValue: event.target.value })} />)}
              {dictionaryField('ownerNewGarageInitialElectricityMeterValue', <input aria-label={fieldMeta('ownerNewGarageInitialElectricityMeterValue').ariaLabel} type="number" min="0" step="0.001" value={ownerGarageLinkForm.initialElectricityMeterValue} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, initialElectricityMeterValue: event.target.value })} />)}
            </div>
            {dictionaryField('ownerNewGarageComment', <textarea aria-label={fieldMeta('ownerNewGarageComment').ariaLabel} placeholder={fieldMeta('ownerNewGarageComment').placeholder} value={ownerGarageLinkForm.comment} onChange={(event) => setOwnerGarageLinkForm({ ...ownerGarageLinkForm, comment: event.target.value })} />)}
          </div>
        </>
      )
    }
    if (section === 'garages') {
      return (
        <>
          {dictionaryField('garageNumber', <input aria-label={fieldMeta('garageNumber').ariaLabel} placeholder={fieldMeta('garageNumber').placeholder} value={garageForm.number} onChange={(event) => setGarageForm({ ...garageForm, number: event.target.value })} required />)}
          <div className="inline-fields">
            {dictionaryField('garagePeopleCount', <input aria-label={fieldMeta('garagePeopleCount').ariaLabel} type="number" min="0" value={garageForm.peopleCount} onChange={(event) => setGarageForm({ ...garageForm, peopleCount: Number(event.target.value) })} />)}
            {dictionaryField('garageFloorCount', <input aria-label={fieldMeta('garageFloorCount').ariaLabel} type="number" min="0" value={garageForm.floorCount} onChange={(event) => setGarageForm({ ...garageForm, floorCount: Number(event.target.value) })} />)}
          </div>
          {dictionaryField('garageOwner', (
            <select aria-label={fieldMeta('garageOwner').ariaLabel} value={garageForm.ownerId} onChange={(event) => setGarageForm({ ...garageForm, ownerId: event.target.value })}>
              <option value="">Без владельца</option>
              {ownerOptions.map((owner) => <option value={owner.id} key={owner.id}>{owner.fullName}</option>)}
            </select>
          ))}
          {dictionaryField('garageStartingBalance', <input aria-label={fieldMeta('garageStartingBalance').ariaLabel} type="number" step="0.01" value={garageForm.startingBalance} onChange={(event) => setGarageForm({ ...garageForm, startingBalance: Number(event.target.value) })} />)}
          <div className="inline-fields">
            {dictionaryField('garageInitialWaterMeterValue', <input aria-label={fieldMeta('garageInitialWaterMeterValue').ariaLabel} type="number" min="0" step="0.001" value={garageForm.initialWaterMeterValue} onChange={(event) => setGarageForm({ ...garageForm, initialWaterMeterValue: event.target.value })} />)}
            {dictionaryField('garageInitialElectricityMeterValue', <input aria-label={fieldMeta('garageInitialElectricityMeterValue').ariaLabel} type="number" min="0" step="0.001" value={garageForm.initialElectricityMeterValue} onChange={(event) => setGarageForm({ ...garageForm, initialElectricityMeterValue: event.target.value })} />)}
          </div>
          {dictionaryField('garageComment', <textarea aria-label={fieldMeta('garageComment').ariaLabel} placeholder={fieldMeta('garageComment').placeholder} value={garageForm.comment} onChange={(event) => setGarageForm({ ...garageForm, comment: event.target.value })} />)}
        </>
      )
    }
    if (section === 'supplierGroups') {
      return dictionaryField('supplierGroupName', <input aria-label={fieldMeta('supplierGroupName').ariaLabel} placeholder={fieldMeta('supplierGroupName').placeholder} value={supplierGroupName} onChange={(event) => setSupplierGroupName(event.target.value)} required />)
    }
    if (section === 'suppliers') {
      return (
        <>
          {dictionaryField('supplierName', <input aria-label={fieldMeta('supplierName').ariaLabel} placeholder={fieldMeta('supplierName').placeholder} value={supplierForm.name} onChange={(event) => setSupplierForm({ ...supplierForm, name: event.target.value })} required />)}
          {dictionaryField('supplierGroup', (
            <select aria-label={fieldMeta('supplierGroup').ariaLabel} value={supplierForm.groupId} onChange={(event) => setSupplierForm({ ...supplierForm, groupId: event.target.value })} required>
              <option value="" disabled>Выберите группу</option>
              {groupOptions.map((group) => <option value={group.id} key={group.id}>{group.name}</option>)}
            </select>
          ))}
          {dictionaryField('supplierInn', <input aria-label={fieldMeta('supplierInn').ariaLabel} placeholder={fieldMeta('supplierInn').placeholder} value={supplierForm.inn} onChange={(event) => setSupplierForm({ ...supplierForm, inn: event.target.value })} />)}
          {dictionaryField('supplierLegalAddress', <input aria-label={fieldMeta('supplierLegalAddress').ariaLabel} placeholder={fieldMeta('supplierLegalAddress').placeholder} value={supplierForm.legalAddress} onChange={(event) => setSupplierForm({ ...supplierForm, legalAddress: event.target.value })} />)}
          {dictionaryField('supplierContactPerson', <input aria-label={fieldMeta('supplierContactPerson').ariaLabel} placeholder={fieldMeta('supplierContactPerson').placeholder} value={supplierForm.contactPerson} onChange={(event) => setSupplierForm({ ...supplierForm, contactPerson: event.target.value })} />)}
          {dictionaryField('supplierPhone', <input aria-label={fieldMeta('supplierPhone').ariaLabel} placeholder={fieldMeta('supplierPhone').placeholder} value={supplierForm.phone} onChange={(event) => setSupplierForm({ ...supplierForm, phone: event.target.value })} />)}
          {dictionaryField('supplierEmail', <input aria-label={fieldMeta('supplierEmail').ariaLabel} placeholder={fieldMeta('supplierEmail').placeholder} value={supplierForm.email} onChange={(event) => setSupplierForm({ ...supplierForm, email: event.target.value })} />)}
          {dictionaryField('supplierStartingBalance', <input aria-label={fieldMeta('supplierStartingBalance').ariaLabel} type="number" step="0.01" value={supplierForm.startingBalance} onChange={(event) => setSupplierForm({ ...supplierForm, startingBalance: Number(event.target.value) })} />)}
          {dictionaryField('supplierComment', <textarea aria-label={fieldMeta('supplierComment').ariaLabel} placeholder={fieldMeta('supplierComment').placeholder} value={supplierForm.comment} onChange={(event) => setSupplierForm({ ...supplierForm, comment: event.target.value })} />)}
        </>
      )
    }
    if (section === 'incomeTypes' || section === 'expenseTypes') {
      return (
        <>
          {dictionaryField('accountingTypeName', <input aria-label={fieldMeta('accountingTypeName').ariaLabel} placeholder={fieldMeta('accountingTypeName').placeholder} value={accountingTypeForm.name} onChange={(event) => setAccountingTypeForm({ ...accountingTypeForm, name: event.target.value })} required />)}
          {dictionaryField('accountingTypeCode', <input aria-label={fieldMeta('accountingTypeCode').ariaLabel} placeholder={fieldMeta('accountingTypeCode').placeholder} value={accountingTypeForm.code} onChange={(event) => setAccountingTypeForm({ ...accountingTypeForm, code: event.target.value })} />)}
        </>
      )
    }
    return (
      <>
        {dictionaryField('tariffName', <input aria-label={fieldMeta('tariffName').ariaLabel} placeholder={fieldMeta('tariffName').placeholder} value={tariffForm.name} onChange={(event) => setTariffForm({ ...tariffForm, name: event.target.value })} required />)}
        {dictionaryField('tariffCalculationBase', (
          <select aria-label={fieldMeta('tariffCalculationBase').ariaLabel} value={tariffForm.calculationBase} onChange={(event) => setTariffForm(updateTariffCalculationBase(tariffForm, event.target.value))}>
            {getTariffCalculationBaseOptions().map((option) => <option value={option.value} key={option.value}>{option.label}</option>)}
          </select>
        ))}
        <div className="inline-fields">
          {dictionaryField('tariffRate', <input aria-label={fieldMeta('tariffRate').ariaLabel} type="number" min="0.0001" step="0.0001" value={tariffForm.rate} onChange={(event) => setTariffForm({ ...tariffForm, rate: Number(event.target.value) })} />)}
          {dictionaryField('tariffEffectiveFrom', <input aria-label={fieldMeta('tariffEffectiveFrom').ariaLabel} type="date" value={tariffForm.effectiveFrom} onChange={(event) => setTariffForm({ ...tariffForm, effectiveFrom: event.target.value })} />)}
        </div>
        {usesElectricityTariffTiers(tariffForm.calculationBase) ? (
          <div className="inline-fields tariff-tier-fields">
            {dictionaryField('tariffElectricityFirstThreshold', <input aria-label={fieldMeta('tariffElectricityFirstThreshold').ariaLabel} placeholder={fieldMeta('tariffElectricityFirstThreshold').placeholder} type="number" min="0.0001" step="0.0001" value={tariffForm.electricityFirstThreshold ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityFirstThreshold: parseOptionalNumberInput(event.target.value) })} />)}
            {dictionaryField('tariffElectricitySecondThreshold', <input aria-label={fieldMeta('tariffElectricitySecondThreshold').ariaLabel} placeholder={fieldMeta('tariffElectricitySecondThreshold').placeholder} type="number" min="0.0001" step="0.0001" value={tariffForm.electricitySecondThreshold ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricitySecondThreshold: parseOptionalNumberInput(event.target.value) })} />)}
            {dictionaryField('tariffElectricityFirstRate', <input aria-label={fieldMeta('tariffElectricityFirstRate').ariaLabel} placeholder={fieldMeta('tariffElectricityFirstRate').placeholder} type="number" min="0.0001" step="0.0001" value={tariffForm.electricityFirstRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityFirstRate: parseOptionalNumberInput(event.target.value) })} />)}
            {dictionaryField('tariffElectricitySecondRate', <input aria-label={fieldMeta('tariffElectricitySecondRate').ariaLabel} placeholder={fieldMeta('tariffElectricitySecondRate').placeholder} type="number" min="0.0001" step="0.0001" value={tariffForm.electricitySecondRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricitySecondRate: parseOptionalNumberInput(event.target.value) })} />)}
            {dictionaryField('tariffElectricityThirdRate', <input aria-label={fieldMeta('tariffElectricityThirdRate').ariaLabel} placeholder={fieldMeta('tariffElectricityThirdRate').placeholder} type="number" min="0.0001" step="0.0001" value={tariffForm.electricityThirdRate ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, electricityThirdRate: parseOptionalNumberInput(event.target.value) })} />)}
          </div>
        ) : null}
        {dictionaryField('tariffComment', <textarea aria-label={fieldMeta('tariffComment').ariaLabel} placeholder={fieldMeta('tariffComment').placeholder} value={tariffForm.comment ?? ''} onChange={(event) => setTariffForm({ ...tariffForm, comment: event.target.value })} />)}
      </>
    )
  }

  const rows = getRows()
  const visibleRange = getPageVisibleRange(activePage)
  const pageNavigation = getPageNavigation(activePage)

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
                  <tr tabIndex={0} onContextMenu={(event) => openContextMenu(event, activeSection, item)} onDoubleClick={() => openEditor(activeSection, 'edit', item)} key={`${activeSection}-${getDictionaryRecordTitle(activeSection, item)}-${'id' in item ? item.id : ''}`}>
                    {renderCells(item)}
                  </tr>
                ))}
              </tbody>
            </table>
            {!loading && rows.length === 0 ? <p className="empty-state" role="status" aria-live="polite">В этом справочнике пока нет записей</p> : null}
          </div>

          <div className="dictionary-pagination" role="navigation" aria-label="Пагинация справочника">
            <span role="status" aria-live="polite">Показано {visibleRange.from}-{visibleRange.to} из {activePage.totalCount}</span>
            <label>
              Строк
              <select aria-label="Количество строк справочника" value={activePage.limit} onChange={(event) => changePageSize(Number(event.target.value))}>
                {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
              </select>
            </label>
            <button className="ghost-button" type="button" disabled={loading || !pageNavigation.canGoPrevious} onClick={() => movePage(-1)}>Назад</button>
            <button className="ghost-button" type="button" disabled={loading || !pageNavigation.canGoNext} onClick={() => movePage(1)}>Вперед</button>
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
                <p>{getDictionaryRecordTitle(archiveTarget.section, archiveTarget.item)}</p>
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
            {getTariffCalculationBaseOptions().map((option) => <option value={option.value} key={option.value}>{option.label}</option>)}
          </select>
          <div className="inline-fields">
            <input aria-label="Ставка тарифа" type="number" min="0.0001" step="0.0001" value={tariffForm.rate} onChange={(event) => setTariffForm({ ...tariffForm, rate: Number(event.target.value) })} />
            <input aria-label="Дата начала тарифа" type="date" value={tariffForm.effectiveFrom} onChange={(event) => setTariffForm({ ...tariffForm, effectiveFrom: event.target.value })} />
          </div>
          {usesElectricityTariffTiers(tariffForm.calculationBase) ? (
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

export default App
