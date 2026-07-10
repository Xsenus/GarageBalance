import { Fragment, memo, useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import type { CSSProperties, FormEvent, KeyboardEvent, MouseEvent, ReactNode, RefObject } from 'react'
import {
  ArrowLeft,
  Bell,
  BookOpenCheck,
  DatabaseZap,
  FileText,
  FileSpreadsheet,
  Gauge,
  LockKeyhole,
  LogOut,
  Minus,
  PanelLeftClose,
  PanelLeftOpen,
  Pencil,
  Plus,
  RefreshCw,
  RotateCcw,
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
import { dictionariesApi, DictionaryApiError } from './services/dictionariesApi'
import type { AccountingTypeDto, ChargeServiceSettingDto, DictionaryClient, FeeCampaignDto, GarageDto, IrregularPaymentDto, OwnerDto, PagedResult, StaffDepartmentDto, StaffMemberDto, SupplierContactDto, SupplierDto, SupplierGroupDto, TariffDto, UpsertChargeServiceSettingRequest, UpsertFeeCampaignRequest, UpsertGarageRequest, UpsertIrregularPaymentRequest, UpsertOwnerRequest, UpsertStaffMemberRequest, UpsertSupplierContactRequest, UpsertSupplierRequest, UpsertTariffRequest } from './services/dictionariesApi'
import { financeApi } from './services/financeApi'
import type { AccrualDto, CreateAccrualRequest, CreateExpenseOperationRequest, CreateIncomeOperationRequest, CreateMeterReadingRequest, CreateSupplierAccrualRequest, ExpenseWorksheetDto, FinanceClient, FinancePagedResult, FinanceSummaryDto, FinancialOperationDto, GarageBalanceHistoryDto, GarageIncomeWorksheetDto, GenerateRegularAccrualsRequest, GenerateSupplierGroupSalaryAccrualsRequest, MeterReadingDto, MissingMeterReadingDto, SupplierAccrualDto } from './services/financeApi'
import { fundsApi } from './services/fundsApi'
import type { FundDto, FundOperationDto, FundsClient } from './services/fundsApi'
import { formStatesApi } from './services/formStatesApi'
import type { FormStateClient } from './services/formStatesApi'
import { importApi } from './services/importApi'
import type { AccessImportQuarantineItemDto, AccessImportRunDto, AccessImportRunLogEntryDto, ImportClient } from './services/importApi'
import { integrationsApi } from './services/integrationsApi'
import type { IntegrationClient, OneCFreshIntegrationStatusDto, ReceiptPrintingActionKind, ReceiptPrintingIntegrationStatusDto } from './services/integrationsApi'
import { reportsApi } from './services/reportsApi'
import type { BankDepositReportDto, CashPaymentReportDto, ConsolidatedReportDto, ExpenseReportDto, FeeReportDto, FundChangeReportDto, IncomeReportDto, ReportClient } from './services/reportsApi'
import { releasesApi } from './services/releasesApi'
import type { AppReleaseDto, AppReleaseItemDto, ReleaseClient, UpsertAppReleaseRequest } from './services/releasesApi'
import { usersApi } from './services/usersApi'
import type { CreateManagedUserRequest, ManagedRoleDto, ManagedUserDto, PagedManagedUsersDto, UpdateManagedUserRequest, UserManagementClient } from './services/usersApi'
import { hasAnyPermission, hasPermission, permissions, rolePermissionGroups } from './shared/accessControl'
import type { DictionaryEditorFieldKey, DictionaryRecord, DictionarySectionKey } from './shared/dictionaryWorkbench'
import { canWriteDictionarySection, createAccountingTypeFormFromDto, createEmptyAccountingTypeForm, createEmptyGarageForm, createEmptyOwnerForm, createEmptyOwnerGarageLinkForm, createEmptySupplierForm, createEmptyTariffForm, createGarageFormFromDto, createOwnerFormFromDto, createSupplierFormFromDto, dictionarySectionGroups, dictionarySectionOptions, getDictionaryEditorFieldMeta, getDictionaryRecordCells, getDictionaryRecordTitle, getDictionarySearchPlaceholder, getDictionarySectionOption, getDictionaryTableHeaders, getOwnerGarageOptions, getTariffCalculationBaseOptions, supportsDictionarySearch, usesElectricityTariffTiers } from './shared/dictionaryWorkbench'
import type { FinanceEditorKey, FinanceSectionKey } from './shared/financeWorkbench'
import { financeSectionOptions, formatFinanceGarageLabel, formatFinanceIncomeGarageSearchStatus, formatFinanceOperationCount, formatFinanceVisibleListStatus, formatFinanceVisibleRange, getFinanceContextMenuLabel, getFinanceEditorFieldLabel, getFinanceEditorSavingScope, getFinanceEditorSubmitLabel, getFinanceEditorTitle, getFinanceEditorUiLabel, getFinanceEditorValidationTitle, getFinanceFallbackLabel, getFinanceMeterKindLabel, getFinanceOptionalText, getFinancePanelLabel, getFinanceSectionDescription, getFinanceTableHeaders, getFinanceToolbarLabel, getFinanceVisibleListEmptyLabel, getFinanceVisibleListTableHeaders, getFinanceVisibleListTableLabel } from './shared/financeWorkbench'
import { buildAuditExportFileName, buildImportReportFileName, buildReportFileName, buildSnapshotReportFileName, downloadBlob } from './shared/fileExports'
import type { ChangePreview } from './shared/changePreview'
import { appendChangePreview, formatChangeDate, formatChangeMoney, formatChangeNumber, formatChangeText, formatSensitiveChange } from './shared/changePreview'
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
import type { PagedItems } from './shared/pagination'
import { createDefaultGarageBalanceHistoryFilters } from './shared/reportFilters'
import { clearStoredAuthSession, loadStoredAuthSession, saveStoredAuthSession } from './shared/sessionStorage'
import type { UserEditChange, UserFormState } from './shared/userManagement'
import { getPrimaryRoleCode, getRoleLabel, getUserEditorChanges, getUserEditorValidationErrors } from './shared/userManagement'
import type { OwnerGarageLinkForm } from './shared/validation'
import { chooseRegularTariffId, createTariffFormFromDto, getAccountingTypeValidationErrors, getAccrualValidationErrors, getAuthValidationErrors, getCompatibleRegularTariffs, getExpenseValidationErrors, getGarageValidationErrors, getIncomeValidationErrors, getMeterReadingValidationErrors, getOwnerGarageLinkValidationErrors, getOwnerValidationErrors, getPasswordChangeValidationErrors, getRegularAccrualValidationErrorsForCatalog, getSupplierAccrualValidationErrors, getSupplierGroupSalaryValidationErrors, getSupplierGroupValidationErrors, getSupplierValidationErrors, getTariffValidationErrors, parseOptionalNumberInput, updateTariffCalculationBase, withoutElectricityTierFields } from './shared/validation'
import './App.css'

type AppProps = {
  authClient?: AuthClient
  auditClient?: AuditClient
  dictionaryClient?: DictionaryClient
  financeClient?: FinanceClient
  fundsClient?: FundsClient
  formStateClient?: FormStateClient
  importClient?: ImportClient
  integrationClient?: IntegrationClient
  reportClient?: ReportClient
  releaseClient?: ReleaseClient
  userClient?: UserManagementClient
}

type AccrualBreakdown =
  | { kind: 'garage'; accrual: AccrualDto }
  | { kind: 'supplier'; accrual: SupplierAccrualDto }

const authSessionStorageKey = 'garagebalance.auth.session'
const sidebarExpandedStorageKey = 'garagebalance.sidebar.expanded'
const financeScreenRequestLimit = 50
const dictionaryScreenRequestLimit = 100
const importQuarantineScreenRequestLimit = 50
const contractorsFormStateScope = 'contractors-prototype'
const tariffsFormStateScope = 'tariffs-and-fees-prototype'
const paymentsFormStateScope = 'payments-prototype'

const auditSectionOptions = [
  { value: '', label: 'Все разделы' },
  { value: 'dictionary', label: 'Справочники' },
  { value: 'finance', label: 'Финансы' },
  { value: 'users', label: 'Пользователи' },
  { value: 'auth', label: 'Вход и безопасность' },
  { value: 'import', label: 'Импорт' },
  { value: 'reports', label: 'Отчеты' },
  { value: 'app_releases', label: 'Что нового' },
]

const auditActionKindOptions = [
  { value: '', label: 'Все действия' },
  { value: 'create', label: 'Создание' },
  { value: 'update', label: 'Изменение' },
  { value: 'archive', label: 'Архивирование' },
  { value: 'restore', label: 'Восстановление' },
  { value: 'cancel', label: 'Отмена' },
  { value: 'login', label: 'Вход' },
  { value: 'fail', label: 'Ошибки и отказы' },
  { value: 'generate', label: 'Формирование' },
  { value: 'export', label: 'Выгрузка' },
  { value: 'import', label: 'Импорт' },
]

const auditEntityTypeOptions = [
  { value: '', label: 'Все объекты' },
  { value: 'owner', label: 'Владелец' },
  { value: 'garage', label: 'Гараж' },
  { value: 'supplier', label: 'Поставщик' },
  { value: 'tariff', label: 'Тариф' },
  { value: 'financial_operation', label: 'Платеж или выплата' },
  { value: 'accrual', label: 'Начисление' },
  { value: 'supplier_accrual', label: 'Начисление поставщику' },
  { value: 'meter_reading', label: 'Показание счетчика' },
  { value: 'report', label: 'Отчет' },
  { value: 'app_user', label: 'Пользователь' },
  { value: 'access_import_run', label: 'Импорт Access' },
]

const auditQuickFilterOptions = [
  { value: '', label: 'Все события' },
  { value: 'deletions', label: 'Только удаления' },
  { value: 'restores', label: 'Только восстановления' },
  { value: 'financial', label: 'Только финансы' },
]

function FormField({ label, hint, children, className }: { label: string; hint?: string; children: ReactNode; className?: string }) {
  return (
    <label className={`form-field${className ? ` ${className}` : ''}`}>
      <span className="form-field-label">{label}</span>
      {children}
      {hint ? <span className="form-field-hint">{hint}</span> : null}
    </label>
  )
}

type DictionaryEditorState = { section: DictionarySectionKey; mode: 'create' | 'edit'; item?: DictionaryRecord }

type DictionaryChangePreview = ChangePreview

type NavigationItem = {
  section: WorkspaceSection
  label: string
  icon: typeof Gauge
  requiredAny?: readonly string[]
}

type WorkspaceSection = 'dashboard' | 'users' | 'contractors' | 'tariffsAndFees' | 'dictionaries' | 'meterReadings' | 'payments' | 'funds' | 'reports' | 'import' | 'audit' | 'releases' | 'settings'
type ImportTab = 'checks' | 'log' | 'history' | 'quarantine'

const navigation: NavigationItem[] = [
  { section: 'dashboard', label: 'Главное меню', icon: Gauge },
  { section: 'users', label: 'Пользователи', icon: ShieldCheck, requiredAny: [permissions.usersManage] },
  { section: 'tariffsAndFees', label: 'Тарифы и сборы', icon: FileSpreadsheet, requiredAny: [permissions.dictionariesRead] },
  { section: 'contractors', label: 'Контрагенты', icon: UsersRound, requiredAny: [permissions.dictionariesRead] },
  { section: 'dictionaries', label: 'Справочники', icon: UsersRound, requiredAny: [permissions.dictionariesRead] },
  { section: 'meterReadings', label: 'Показания', icon: FileSpreadsheet, requiredAny: [permissions.paymentsRead] },
  { section: 'payments', label: 'Платежи', icon: WalletCards, requiredAny: [permissions.paymentsRead] },
  { section: 'funds', label: 'Фонды', icon: WalletCards, requiredAny: [permissions.reportsRead] },
  { section: 'reports', label: 'Отчеты', icon: FileSpreadsheet, requiredAny: [permissions.reportsRead] },
  { section: 'import', label: 'Импорт', icon: DatabaseZap, requiredAny: [permissions.importRun] },
  { section: 'audit', label: 'История изменений', icon: FileText, requiredAny: [permissions.auditRead] },
  { section: 'releases', label: 'Что нового', icon: BookOpenCheck },
  { section: 'settings', label: 'Настройки', icon: LockKeyhole },
]

const dashboardTiles: { title: string; section: WorkspaceSection; requiredAny?: readonly string[] }[] = [
  { title: 'Тарифы\nи сборы', section: 'tariffsAndFees', requiredAny: [permissions.dictionariesRead] },
  { title: 'Контрагенты', section: 'contractors', requiredAny: [permissions.dictionariesRead] },
  { title: 'Счётчики', section: 'meterReadings', requiredAny: [permissions.paymentsRead] },
  { title: 'Платежи', section: 'payments', requiredAny: [permissions.paymentsRead] },
  { title: 'Отчёты', section: 'reports', requiredAny: [permissions.reportsRead] },
  { title: 'Настройки', section: 'settings' },
  { title: 'Управление\nфондами', section: 'funds', requiredAny: [permissions.reportsRead] },
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

function App({ authClient = authApi, auditClient = auditApi, dictionaryClient = dictionariesApi, financeClient = financeApi, fundsClient = fundsApi, formStateClient = formStatesApi, importClient = importApi, integrationClient = integrationsApi, reportClient = reportsApi, releaseClient = releasesApi, userClient = usersApi }: AppProps) {
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
        <Workspace activeSection={effectiveActiveSection} auth={auth} authClient={authClient} auditClient={auditClient} dictionaryClient={dictionaryClient} financeClient={financeClient} fundsClient={fundsClient} formStateClient={formStateClient} importClient={importClient} integrationClient={integrationClient} reportClient={reportClient} releaseClient={releaseClient} userClient={userClient} onOpenSection={setActiveSection} onUserChanged={handleUserChanged} onLogout={handleLogout} />
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
  fundsClient,
  formStateClient,
  importClient,
  integrationClient,
  reportClient,
  releaseClient,
  userClient,
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
  fundsClient: FundsClient
  formStateClient: FormStateClient
  importClient: ImportClient
  integrationClient: IntegrationClient
  reportClient: ReportClient
  releaseClient: ReleaseClient
  userClient: UserManagementClient
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
                    title={tile.title.replace('\n', ' ')}
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
          <ContractorsPrototypePanel auth={auth} auditClient={auditClient} dictionaryClient={dictionaryClient} financeClient={financeClient} formStateClient={formStateClient} />
        ) : (
          <AccessNotice label="Контрагенты недоступны" title="Контрагенты" permission={permissions.dictionariesRead} description="Для просмотра гаражей, поставщиков и карточек контрагентов нужно право на чтение справочников." />
        )
      case 'tariffsAndFees':
        return canReadDictionaries ? (
          <TariffsAndFeesPrototypePanel auth={auth} dictionaryClient={dictionaryClient} financeClient={financeClient} formStateClient={formStateClient} />
        ) : (
          <AccessNotice label="Тарифы и сборы недоступны" title="Тарифы и сборы" permission={permissions.dictionariesRead} description="Для просмотра настроек услуг, тарифов и сборов нужно право на чтение справочников." />
        )
      case 'payments':
        return canReadPayments && canReadDictionaries ? (
          <FinancePanel auth={auth} dictionaryClient={dictionaryClient} financeClient={financeClient} fundsClient={fundsClient} formStateClient={formStateClient} integrationClient={integrationClient} />
        ) : (
          <AccessNotice label="Платежи недоступны" title="Платежи" permission={permissions.paymentsRead} description="Для платежей нужны права на просмотр финансовых операций и справочников." />
        )
      case 'meterReadings':
        return canReadPayments ? (
        <MeterReadingsPrototypePanel auth={auth} dictionaryClient={dictionaryClient} financeClient={financeClient} />
        ) : (
          <AccessNotice label="Показания недоступны" title="Показания" permission={permissions.paymentsRead} description="Для просмотра показаний счетчиков нужно право на чтение финансовых операций." />
        )
      case 'funds':
        return canReadReports ? (
          <FundsPrototypePanel auth={auth} fundsClient={fundsClient} />
        ) : (
          <AccessNotice label="Фонды недоступны" title="Управление фондами" permission={permissions.reportsRead} description="Для просмотра фондов нужно право на отчетность." />
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
          <AuditPanel auth={auth} auditClient={auditClient} onOpenSection={onOpenSection} />
        ) : (
          <AccessNotice label="История изменений недоступна" title="История изменений" permission={permissions.auditRead} description="История изменений доступна только пользователям с правом просмотра audit-событий." />
        )
      case 'releases':
        return <ReleasePanel auth={auth} releaseClient={releaseClient} />
      case 'settings':
        return <PasswordPanel auth={auth} authClient={authClient} integrationClient={integrationClient} onUserChanged={onUserChanged} />
      default:
        return null
    }
  }

  return (
    <>
      <header className={activeSection === 'dashboard' ? 'topbar topbar--dashboard' : 'topbar'}>
        {activeSection !== 'dashboard' ? (
          <button className="icon-button topbar-back-button" type="button" aria-label="Назад к выбору раздела" title="Назад к выбору раздела" onClick={() => onOpenSection('dashboard')}>
            <ArrowLeft size={19} />
          </button>
        ) : null}
        <div className="user-panel">
          <div>
            <strong>{auth.user.displayName}</strong>
            <span>{auth.user.roles.join(', ')}</span>
          </div>
          <button className="icon-button" type="button" aria-label="Уведомления" title="Уведомления">
            <Bell size={19} />
          </button>
          <button className="icon-button" type="button" aria-label="Выйти" title="Выйти" onClick={onLogout}>
            <LogOut size={19} />
          </button>
        </div>
      </header>
      {renderActiveSection()}
    </>
  )
}

function PasswordPanel({ auth, authClient, integrationClient, onUserChanged }: { auth: AuthResponse; authClient: AuthClient; integrationClient: IntegrationClient; onUserChanged: (user: CurrentUserDto) => void }) {
  const [form, setForm] = useState({ currentPassword: '', newPassword: '', repeatPassword: '' })
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  const [pendingPasswordChange, setPendingPasswordChange] = useState<{ currentPassword: string; newPassword: string } | null>(null)
  const [saving, setSaving] = useState(false)
  const [oneCFreshStatus, setOneCFreshStatus] = useState<OneCFreshIntegrationStatusDto | null>(null)
  const [integrationLoading, setIntegrationLoading] = useState(false)
  const [integrationError, setIntegrationError] = useState<string | null>(null)
  const [oneCFreshSyncConfirmation, setOneCFreshSyncConfirmation] = useState<{ mode: 'start' | 'retry'; comment: string; error: string | null } | null>(null)
  const [oneCFreshSyncSaving, setOneCFreshSyncSaving] = useState(false)
  const [oneCFreshSyncMessage, setOneCFreshSyncMessage] = useState<string | null>(null)
  const oneCFreshSyncTriggerRef = useRef<HTMLButtonElement | null>(null)
  const [receiptPrintingStatus, setReceiptPrintingStatus] = useState<ReceiptPrintingIntegrationStatusDto | null>(null)
  const [receiptPrintingLoading, setReceiptPrintingLoading] = useState(false)
  const [receiptPrintingError, setReceiptPrintingError] = useState<string | null>(null)
  const canViewIntegrationStatus = hasPermission(auth, permissions.importRun)
  const canViewReceiptPrintingStatus = hasPermission(auth, permissions.paymentsWrite)
  useRestoreFocusOnClose(Boolean(pendingPasswordChange))
  const confirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingPasswordChange))
  const confirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingPasswordChange))
  const oneCFreshSyncCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(oneCFreshSyncConfirmation))
  const oneCFreshSyncDialogRef = useFocusTrap<HTMLElement>(Boolean(oneCFreshSyncConfirmation))
  useEscapeKey(Boolean(pendingPasswordChange) && !saving, () => setPendingPasswordChange(null))
  useEscapeKey(Boolean(oneCFreshSyncConfirmation) && !oneCFreshSyncSaving, () => closeOneCFreshSyncConfirmation())

  useEffect(() => {
    if (!canViewIntegrationStatus) {
      return
    }

    let ignore = false
    async function loadOneCFreshStatus() {
      await Promise.resolve()
      if (ignore) {
        return
      }

      setIntegrationLoading(true)
      setIntegrationError(null)
      try {
        const status = await integrationClient.getOneCFreshStatus(auth.accessToken)
        if (!ignore) {
          setOneCFreshStatus(status)
        }
      } catch (caught: unknown) {
        if (!ignore) {
          setOneCFreshStatus(null)
          setIntegrationError(caught instanceof Error ? caught.message : 'Не удалось загрузить статус 1C Fresh.')
        }
      } finally {
        if (!ignore) {
          setIntegrationLoading(false)
        }
      }
    }

    void loadOneCFreshStatus()

    return () => {
      ignore = true
    }
  }, [auth.accessToken, canViewIntegrationStatus, integrationClient])

  useEffect(() => {
    if (!canViewReceiptPrintingStatus) {
      return
    }

    let ignore = false
    async function loadReceiptPrintingStatus() {
      await Promise.resolve()
      if (ignore) {
        return
      }

      setReceiptPrintingLoading(true)
      setReceiptPrintingError(null)
      try {
        const status = await integrationClient.getReceiptPrintingStatus(auth.accessToken)
        if (!ignore) {
          setReceiptPrintingStatus(status)
        }
      } catch (caught: unknown) {
        if (!ignore) {
          setReceiptPrintingStatus(null)
          setReceiptPrintingError(caught instanceof Error ? caught.message : 'Не удалось загрузить статус печати чеков и квитанций.')
        }
      } finally {
        if (!ignore) {
          setReceiptPrintingLoading(false)
        }
      }
    }

    void loadReceiptPrintingStatus()

    return () => {
      ignore = true
    }
  }, [auth.accessToken, canViewReceiptPrintingStatus, integrationClient])

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
    setPendingPasswordChange({
      currentPassword: form.currentPassword,
      newPassword: form.newPassword,
    })
  }

  async function confirmPasswordChange() {
    if (!pendingPasswordChange) {
      return
    }

    setSaving(true)
    try {
      const user = await authClient.changeOwnPassword(auth.accessToken, {
        currentPassword: pendingPasswordChange.currentPassword,
        newPassword: pendingPasswordChange.newPassword,
      })
      onUserChanged(user)
      setPendingPasswordChange(null)
      setForm({ currentPassword: '', newPassword: '', repeatPassword: '' })
      setMessage('Пароль изменен. Используйте новый пароль при следующем входе.')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось изменить пароль.')
      setPendingPasswordChange(null)
    } finally {
      setSaving(false)
    }
  }

  function openOneCFreshSyncConfirmation(trigger: HTMLButtonElement, mode: 'start' | 'retry' = 'start') {
    oneCFreshSyncTriggerRef.current = trigger
    setIntegrationError(null)
    if (mode === 'start') {
      setOneCFreshSyncMessage(null)
    }
    setOneCFreshSyncConfirmation({ mode, comment: '', error: null })
  }

  function closeOneCFreshSyncConfirmation() {
    const trigger = oneCFreshSyncTriggerRef.current
    setOneCFreshSyncConfirmation(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      oneCFreshSyncTriggerRef.current = null
    }, 0)
  }

  async function confirmOneCFreshSync() {
    if (!oneCFreshSyncConfirmation) {
      return
    }

    setOneCFreshSyncSaving(true)
    setOneCFreshSyncConfirmation((state) => state ? { ...state, error: null } : state)
    try {
      const request = {
        comment: oneCFreshSyncConfirmation.comment.trim() || undefined,
      }
      const result = oneCFreshSyncConfirmation.mode === 'retry'
        ? await integrationClient.retryOneCFreshSync(auth.accessToken, request)
        : await integrationClient.startOneCFreshSync(auth.accessToken, request)
      closeOneCFreshSyncConfirmation()
      setOneCFreshSyncMessage(result.statusMessage)
    } catch (caught) {
      setOneCFreshSyncConfirmation((state) => state ? { ...state, error: caught instanceof Error ? caught.message : 'Не удалось отправить запрос синхронизации 1C Fresh.' } : state)
    } finally {
      setOneCFreshSyncSaving(false)
    }
  }

  return (
    <>
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
          <button className="secondary-button" type="submit" disabled={saving || Boolean(pendingPasswordChange)}>
            <ShieldCheck size={16} />
            <span>{saving ? 'Сохраняем...' : 'Изменить пароль'}</span>
          </button>
        </form>
      </section>
      {canViewIntegrationStatus ? (
        <section className="password-panel" aria-label="Интеграция 1C Fresh">
          <div>
            <p className="eyebrow">Интеграции</p>
            <h2>1C Fresh</h2>
            <p>Статус подготовки будущей синхронизации показывается без раскрытия токенов и других защищенных настроек.</p>
          </div>
          {integrationError ? <FormError>{integrationError}</FormError> : null}
          {integrationLoading ? <p className="empty-state" role="status" aria-live="polite">Загрузка статуса 1C Fresh...</p> : null}
          {oneCFreshStatus ? (
            <div className="summary-strip" aria-label="Статус интеграции 1C Fresh">
              <div>
                <span>Состояние</span>
                <strong className={oneCFreshStatus.isConfigured ? 'status-active' : 'status-disabled'}>{oneCFreshStatus.isConfigured ? 'Подготовлено' : 'Не настроено'}</strong>
              </div>
              <div>
                <span>Синхронизация</span>
                <strong className={oneCFreshStatus.canSynchronize ? 'status-active' : 'warning-text'}>{oneCFreshStatus.canSynchronize ? 'Доступна' : 'Ожидает адаптер'}</strong>
              </div>
              <div>
                <span>Защищенные настройки</span>
                <strong>{oneCFreshStatus.configuredSettings.length} / {oneCFreshStatus.requiredSettings.length}</strong>
              </div>
          <div>
            <span>Обновлено</span>
            <strong>{oneCFreshStatus.lastProtectedSettingUpdatedAtUtc ? formatDateTime(oneCFreshStatus.lastProtectedSettingUpdatedAtUtc) : 'нет данных'}</strong>
          </div>
        </div>
          ) : null}
          {oneCFreshStatus ? (
            <p className="empty-state" role="status" aria-live="polite">{oneCFreshStatus.statusMessage}</p>
          ) : null}
          {oneCFreshSyncMessage ? <div className="form-success" role="status" aria-live="polite">{oneCFreshSyncMessage}</div> : null}
          {oneCFreshStatus ? (
            <button className="secondary-button" type="button" onClick={(event) => openOneCFreshSyncConfirmation(event.currentTarget, 'start')} disabled={integrationLoading || oneCFreshSyncSaving || !oneCFreshStatus.isConfigured}>
              <RefreshCw size={16} aria-hidden="true" />
              <span>{oneCFreshSyncSaving ? 'Запускаем...' : 'Запустить синхронизацию'}</span>
            </button>
          ) : null}
          {oneCFreshStatus && oneCFreshSyncMessage ? (
            <button className="ghost-button" type="button" onClick={(event) => openOneCFreshSyncConfirmation(event.currentTarget, 'retry')} disabled={integrationLoading || oneCFreshSyncSaving || !oneCFreshStatus.isConfigured}>
              <RefreshCw size={16} aria-hidden="true" />
              <span>Повторить запрос</span>
            </button>
          ) : null}
        </section>
      ) : null}
      {canViewReceiptPrintingStatus ? (
        <section className="password-panel" aria-label="Печать чеков и квитанций">
          <div>
            <p className="eyebrow">Интеграции</p>
            <h2>Чеки и квитанции</h2>
            <p>Статус подготовки печати показывается без раскрытия параметров фискального оборудования и шаблонов.</p>
          </div>
          {receiptPrintingError ? <FormError>{receiptPrintingError}</FormError> : null}
          {receiptPrintingLoading ? <p className="empty-state" role="status" aria-live="polite">Загрузка статуса печати...</p> : null}
          {receiptPrintingStatus ? (
            <div className="summary-strip" aria-label="Статус печати чеков и квитанций">
              <div>
                <span>Состояние</span>
                <strong className={receiptPrintingStatus.isConfigured ? 'status-active' : 'status-disabled'}>{receiptPrintingStatus.isConfigured ? 'Подготовлено' : 'Не настроено'}</strong>
              </div>
              <div>
                <span>Печать</span>
                <strong className={receiptPrintingStatus.canPrint ? 'status-active' : 'warning-text'}>{receiptPrintingStatus.canPrint ? 'Доступна' : 'Ожидает адаптер'}</strong>
              </div>
              <div>
                <span>Защищенные настройки</span>
                <strong>{receiptPrintingStatus.configuredSettings.length} / {receiptPrintingStatus.requiredSettings.length}</strong>
              </div>
              <div>
                <span>Обновлено</span>
                <strong>{receiptPrintingStatus.lastProtectedSettingUpdatedAtUtc ? formatDateTime(receiptPrintingStatus.lastProtectedSettingUpdatedAtUtc) : 'нет данных'}</strong>
              </div>
            </div>
          ) : null}
          {receiptPrintingStatus ? (
            <>
              <p className="empty-state" role="status" aria-live="polite">{receiptPrintingStatus.statusMessage}</p>
              <p className="form-hint">Будущие действия: {receiptPrintingStatus.plannedActions.join(', ')}.</p>
            </>
          ) : null}
        </section>
      ) : null}
      {pendingPasswordChange ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => !saving && setPendingPasswordChange(null)}>
          <section ref={confirmationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="password-change-confirmation-title" aria-describedby="password-change-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="dialog-heading">
              <div>
                <p className="eyebrow">Настройки</p>
                <h3 id="password-change-confirmation-title">Подтвердить смену пароля?</h3>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить подтверждение смены пароля" onClick={() => setPendingPasswordChange(null)} disabled={saving}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="password-change-confirmation-description">После подтверждения пароль будет изменен, а действие появится в истории изменений как смена учетных данных без раскрытия самого пароля.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля настройки">
              <li>
                <span className="dictionary-change-field">Пароль</span>
                <span className="dictionary-change-values">
                  <span className="dictionary-change-value">Без изменения</span>
                  <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                  <span className="dictionary-change-value dictionary-change-value-after">{formatSensitiveChange(pendingPasswordChange.newPassword)}</span>
                </span>
              </li>
            </ul>
            <div className="dialog-actions">
              <button ref={confirmationCancelRef} className="ghost-button" type="button" onClick={() => setPendingPasswordChange(null)} disabled={saving}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmPasswordChange()} disabled={saving}>
                <ShieldCheck size={16} />
                <span>{saving ? 'Сохраняем...' : 'Подтвердить смену пароля'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
      {oneCFreshSyncConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!oneCFreshSyncSaving) {
            closeOneCFreshSyncConfirmation()
          }
        }}>
          <section ref={oneCFreshSyncDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="one-c-fresh-sync-confirmation-title" aria-describedby="one-c-fresh-sync-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="dialog-heading">
              <div>
                <p className="eyebrow">Интеграции</p>
                <h3 id="one-c-fresh-sync-confirmation-title">{oneCFreshSyncConfirmation.mode === 'retry' ? 'Повторить запрос синхронизации 1C Fresh?' : 'Запустить синхронизацию 1C Fresh?'}</h3>
              </div>
              <button className="icon-button" type="button" aria-label={oneCFreshSyncConfirmation.mode === 'retry' ? 'Отменить повтор синхронизации 1C Fresh' : 'Отменить запуск синхронизации 1C Fresh'} onClick={closeOneCFreshSyncConfirmation} disabled={oneCFreshSyncSaving}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="one-c-fresh-sync-confirmation-description">{oneCFreshSyncConfirmation.mode === 'retry' ? 'Повтор будет записан в историю изменений отдельным событием. До подключения адаптера система зарегистрирует запрос и не будет передавать данные во внешнюю 1C Fresh.' : 'Запуск будет записан в историю изменений. До подключения адаптера система зарегистрирует запрос и не будет передавать данные во внешнюю 1C Fresh.'}</p>
            <FormField label="Комментарий">
              <textarea aria-label={oneCFreshSyncConfirmation.mode === 'retry' ? 'Комментарий к повтору синхронизации 1C Fresh' : 'Комментарий к запуску синхронизации 1C Fresh'} rows={4} value={oneCFreshSyncConfirmation.comment} onChange={(event) => setOneCFreshSyncConfirmation((state) => state ? { ...state, comment: event.target.value, error: null } : state)} disabled={oneCFreshSyncSaving} />
            </FormField>
            {oneCFreshSyncConfirmation.error ? <FormError>{oneCFreshSyncConfirmation.error}</FormError> : null}
            <div className="dialog-actions">
              <button ref={oneCFreshSyncCancelRef} className="ghost-button" type="button" onClick={closeOneCFreshSyncConfirmation} disabled={oneCFreshSyncSaving}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmOneCFreshSync()} disabled={oneCFreshSyncSaving}>
                <RefreshCw size={16} />
                  <span>{oneCFreshSyncSaving ? 'Отправляем...' : oneCFreshSyncConfirmation.mode === 'retry' ? 'Повторить' : 'Запустить'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </>
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
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)
  const [editor, setEditor] = useState<ReleaseEditorState | null>(null)
  const canManageReleases = hasPermission(auth, permissions.appReleasesManage)

  useEffect(() => {
    let ignore = false

    async function loadReleases() {
      setLoading(true)
      setError(null)

      try {
        const nextReleases = canManageReleases
          ? await releaseClient.getManageableReleases(auth.accessToken, 50)
          : await releaseClient.getReleases(auth.accessToken, 10)
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
  }, [auth.accessToken, canManageReleases, releaseClient])

  async function refreshReleases() {
    const nextReleases = canManageReleases
      ? await releaseClient.getManageableReleases(auth.accessToken, 50)
      : await releaseClient.getReleases(auth.accessToken, 10)
    setReleases(nextReleases)
  }

  async function saveRelease(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!editor) {
      return
    }

    const items = editor.items
      .map((item) => ({ type: item.type, text: item.text.trim() }))
      .filter((item) => item.text.length > 0)
    if (!editor.version.trim() || !editor.title.trim() || !editor.summary.trim() || items.length === 0) {
      setError('Заполните версию, заголовок, описание и хотя бы один пункт.')
      return
    }

    setSaving(true)
    setError(null)
    setSuccessMessage(null)
    try {
      const request: UpsertAppReleaseRequest = {
        releaseId: editor.releaseId,
        version: editor.version.trim(),
        publishedAt: editor.publishedAt || null,
        title: editor.title.trim(),
        summary: editor.summary.trim(),
        items,
        isPublished: editor.isPublished,
      }
      if (editor.mode === 'create') {
        await releaseClient.createRelease(auth.accessToken, request)
        setSuccessMessage(editor.isPublished ? 'Запись добавлена и опубликована.' : 'Черновик добавлен.')
      } else {
        await releaseClient.updateRelease(auth.accessToken, editor.releaseId, request)
        setSuccessMessage(editor.isPublished ? 'Запись обновлена.' : 'Черновик обновлен.')
      }

      setEditor(null)
      await refreshReleases()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось сохранить запись.')
    } finally {
      setSaving(false)
    }
  }

  async function publishRelease(release: AppReleaseDto) {
    setSaving(true)
    setError(null)
    setSuccessMessage(null)
    try {
      await releaseClient.publishRelease(auth.accessToken, release.releaseId)
      setSuccessMessage(`Запись ${release.version} опубликована.`)
      await refreshReleases()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось опубликовать запись.')
    } finally {
      setSaving(false)
    }
  }

  function openCreateEditor() {
    setEditor(createReleaseEditorState())
    setError(null)
    setSuccessMessage(null)
  }

  function openEditEditor(release: AppReleaseDto) {
    setEditor(createReleaseEditorState(release))
    setError(null)
    setSuccessMessage(null)
  }

  function updateEditorItem(index: number, patch: Partial<AppReleaseItemDto>) {
    setEditor((current) => {
      if (!current) {
        return current
      }

      return {
        ...current,
        items: current.items.map((item, itemIndex) => itemIndex === index ? { ...item, ...patch } : item),
      }
    })
  }

  function addEditorItem() {
    setEditor((current) => current
      ? { ...current, items: [...current.items, { type: 'improved', text: '' }] }
      : current)
  }

  function removeEditorItem(index: number) {
    setEditor((current) => {
      if (!current || current.items.length === 1) {
        return current
      }

      return {
        ...current,
        items: current.items.filter((_, itemIndex) => itemIndex !== index),
      }
    })
  }

  return (
    <section className="release-panel" aria-label="Что нового">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">Что нового</p>
          <h2>История обновлений</h2>
        </div>
        <div className="release-heading-actions">
          <span>{releases.length} версий</span>
          {canManageReleases ? (
            <button className="secondary-button" type="button" onClick={openCreateEditor}>
              <Plus size={17} />
              <span>Добавить запись</span>
            </button>
          ) : null}
        </div>
      </div>

      {loading ? <p className="muted" role="status" aria-live="polite">Загружаем историю обновлений...</p> : null}
      {error ? <FormError>{error}</FormError> : null}
      {successMessage ? <p className="success-text" role="status" aria-live="polite">{successMessage}</p> : null}
      {editor ? (
        <form className="release-editor" aria-label={editor.mode === 'create' ? 'Новая запись Что нового' : `Редактирование ${editor.version}`} onSubmit={saveRelease}>
          <div className="form-grid two-columns">
            <label>
              Версия
              <input aria-label="Версия записи Что нового" value={editor.version} onChange={(event) => setEditor({ ...editor, version: event.target.value })} />
            </label>
            <label>
              Дата публикации
              <input aria-label="Дата публикации записи Что нового" type="datetime-local" value={editor.publishedAt} onChange={(event) => setEditor({ ...editor, publishedAt: event.target.value })} />
            </label>
          </div>
          <label>
            Заголовок
            <input aria-label="Заголовок записи Что нового" value={editor.title} onChange={(event) => setEditor({ ...editor, title: event.target.value })} />
          </label>
          <label>
            Краткое описание
            <textarea aria-label="Краткое описание записи Что нового" rows={3} value={editor.summary} onChange={(event) => setEditor({ ...editor, summary: event.target.value })} />
          </label>
          <div className="release-editor__items">
            <div className="release-editor__items-heading">
              <strong>Пункты обновления</strong>
              <button className="ghost-button" type="button" onClick={addEditorItem}>Добавить пункт</button>
            </div>
            {editor.items.map((item, index) => (
              <div className="release-editor__item" key={`${index}-${item.type}`}>
                <label>
                  Тип
                  <select aria-label={`Тип пункта обновления ${index + 1}`} value={item.type} onChange={(event) => updateEditorItem(index, { type: event.target.value })}>
                    {releaseItemTypeOptions.map((option) => (
                      <option value={option.value} key={option.value}>{option.label}</option>
                    ))}
                  </select>
                </label>
                <label>
                  Текст
                  <textarea aria-label={`Текст пункта обновления ${index + 1}`} rows={2} value={item.text} onChange={(event) => updateEditorItem(index, { text: event.target.value })} />
                </label>
                <button className="ghost-button" type="button" onClick={() => removeEditorItem(index)} disabled={editor.items.length === 1}>Убрать</button>
              </div>
            ))}
          </div>
          <label className="checkbox-row">
            <input aria-label="Опубликовать запись Что нового сразу" type="checkbox" checked={editor.isPublished} onChange={(event) => setEditor({ ...editor, isPublished: event.target.checked })} />
            <span>Опубликовать сразу</span>
          </label>
          <div className="detail-dialog-actions">
            <button className="ghost-button" type="button" onClick={() => setEditor(null)} disabled={saving}>Отмена</button>
            <button className="secondary-button" type="submit" disabled={saving}>
              <Save size={17} />
              <span>{saving ? 'Сохраняем...' : 'Сохранить'}</span>
            </button>
          </div>
        </form>
      ) : null}
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
                <div className="release-entry__meta">
                  <span>
                    v{release.version} · {formatReleaseDate(release.publishedAt)}
                  </span>
                  {canManageReleases && release.isPublished === false ? <strong>Черновик</strong> : null}
                </div>
              </div>
              <ul>
                {release.items.map((item) => (
                  <li className={`release-item release-item--${item.type}`} key={`${release.releaseId}-${item.type}-${item.text}`}>
                    {item.text}
                  </li>
                ))}
              </ul>
              {canManageReleases ? (
                <div className="inline-actions release-entry__actions">
                  <button className="ghost-button" type="button" onClick={() => openEditEditor(release)} disabled={saving}>
                    <Pencil size={16} />
                    <span>Изменить</span>
                  </button>
                  {release.isPublished === false ? (
                    <button className="secondary-button" type="button" onClick={() => void publishRelease(release)} disabled={saving}>
                      <BookOpenCheck size={16} />
                      <span>Опубликовать</span>
                    </button>
                  ) : null}
                </div>
              ) : null}
            </article>
          ))}
        </div>
      ) : null}
    </section>
  )
}

type ReleaseEditorState = {
  mode: 'create' | 'edit'
  releaseId: string
  version: string
  publishedAt: string
  title: string
  summary: string
  items: AppReleaseItemDto[]
  isPublished: boolean
}

const releaseItemTypeOptions = [
  { value: 'new', label: 'Новое' },
  { value: 'improved', label: 'Улучшение' },
  { value: 'fixed', label: 'Исправление' },
  { value: 'important', label: 'Важно' },
]

function createReleaseEditorState(release?: AppReleaseDto): ReleaseEditorState {
  return {
    mode: release ? 'edit' : 'create',
    releaseId: release?.releaseId ?? '',
    version: release?.version ?? '',
    publishedAt: release ? formatDateTimeInputValue(release.publishedAt) : formatDateTimeInputValue(new Date().toISOString()),
    title: release?.title ?? '',
    summary: release?.summary ?? '',
    items: release?.items.length ? release.items.map((item) => ({ ...item })) : [{ type: 'improved', text: '' }],
    isPublished: release ? release.isPublished !== false : false,
  }
}

function formatDateTimeInputValue(value: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return ''
  }

  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  const hours = String(date.getHours()).padStart(2, '0')
  const minutes = String(date.getMinutes()).padStart(2, '0')
  return `${year}-${month}-${day}T${hours}:${minutes}`
}

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
    title: 'Повторно напечатать квитанцию?',
    button: 'Повторная печать',
    saving: 'Регистрируем...',
    description: 'Повторная печать будет зафиксирована в истории изменений. Укажите причину, например потерю квитанции или исправление печати.',
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

function FinancePanel({
  auth,
  dictionaryClient,
  financeClient,
  fundsClient,
  formStateClient,
  integrationClient,
}: {
  auth: AuthResponse
  dictionaryClient: DictionaryClient
  financeClient: FinanceClient
  fundsClient: FundsClient
  formStateClient: FormStateClient
  integrationClient: IntegrationClient
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
        const [loadedGarages, loadedSupplierGroups, loadedSuppliers, loadedStaffMembers, loadedIncomeTypes, loadedExpenseTypes, loadedTariffs, loadedOperations, loadedAccruals, loadedSupplierAccruals, loadedMeterReadings, loadedMissingMeterReadings, loadedSummary] = await Promise.all([
          dictionaryClient.getGarages(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getSupplierGroups(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getStaffMembers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getIncomeTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getExpenseTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit),
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
          setStaffMembers(loadedStaffMembers)
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

      <PaymentsPrototypePanel
        auth={auth}
        canWritePayments={canWritePayments}
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

function formatPaymentPrototypeOperationTime(value: string | null | undefined) {
  if (!value) {
    return ''
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return ''
  }

  return date.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
}

function createGaragePaymentHistoryRowsFromOperations(operations: FinancialOperationDto[]): GaragePaymentHistoryPrototypeRow[] {
  return operations
    .filter((operation) => operation.operationKind === 'income' && operation.garageId)
    .map((operation) => ({
      id: operation.id,
      date: formatDateOnly(operation.operationDate),
      time: formatPaymentPrototypeOperationTime(operation.createdAtUtc),
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
  const [selectedGarageId, setSelectedGarageId] = useState<string | null>(null)
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
  const realGarageIds = useMemo(() => new Set(garages.filter((garage) => !garage.isArchived).map((garage) => garage.id)), [garages])
  const garageOptions = useMemo<PaymentsPrototypeGarage[]>(
    () => garages
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
    [garages],
  )
  const selectedGarage = garageOptions.find((garage) => garage.id === selectedGarageId) ?? null
  const normalizedSearch = garageSearch.trim().toLowerCase()
  const garageSearchResults = garageOptions
    .filter((garage) => !normalizedSearch || garage.number.toLowerCase().includes(normalizedSearch) || garage.ownerName.toLowerCase().includes(normalizedSearch))
    .slice(0, 6)
  const shouldShowGarageResults = garageSearch.length > 0 && (!selectedGarage || garageSearch !== `Гараж ${selectedGarage.number} - ${selectedGarage.ownerName}`)
  const garageSearchListId = useId()
  const incomeWorksheetMonthOptions = useMemo(
    () => createPaymentPrototypeMonthOptions(getCurrentMonthInputValue(), [incomeWorksheetMonthFrom, incomeWorksheetMonthTo]),
    [incomeWorksheetMonthFrom, incomeWorksheetMonthTo],
  )

  useEffect(() => {
    let cancelled = false
    formStateClient
      .getState<PaymentsPrototypeSavedState>(auth.accessToken, paymentsFormStateScope)
      .then((state) => {
        if (cancelled) {
          return
        }

        if (state?.payload) {
          const restoredGarageId = state.payload.selectedGarageId ?? null
          const restoredMonthFrom = state.payload.incomeWorksheetMonthFrom ?? getPreviousMonthInputValue(getCurrentMonthInputValue())
          const restoredMonthTo = state.payload.incomeWorksheetMonthTo ?? getCurrentMonthInputValue()
          const isRestoredRealGarage = restoredGarageId !== null && realGarageIds.has(restoredGarageId)
          const restoredRealGarage = isRestoredRealGarage ? garageOptions.find((garage) => garage.id === restoredGarageId) ?? null : null
          setSelectedGarageId(isRestoredRealGarage ? restoredGarageId : null)
          setGarageSearch(isRestoredRealGarage ? state.payload.garageSearch ?? '' : '')
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
                limit: 500,
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
  }, [auth.accessToken, financeClient, formStateClient, garageOptions, realGarageIds])

  useEffect(() => {
    if (!formStateLoaded) {
      return
    }

    const handle = window.setTimeout(() => {
      void formStateClient
        .saveState<PaymentsPrototypeSavedState>(auth.accessToken, paymentsFormStateScope, {
          payload: { selectedGarageId, garageSearch, incomeWorksheetMonthFrom, incomeWorksheetMonthTo, garageRows, historyRows },
          summary: 'Сохранено состояние формы платежей и последние введенные значения.'
        })
        .catch((error: unknown) => setFormStateError(error instanceof Error ? error.message : 'Не удалось сохранить состояние платежей.'))
    }, 400)

    return () => window.clearTimeout(handle)
  }, [auth.accessToken, formStateClient, formStateLoaded, garageRows, garageSearch, historyRows, incomeWorksheetMonthFrom, incomeWorksheetMonthTo, selectedGarageId])

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
        limit: 500,
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
      setReceiptActionStatus(result.statusMessage)
    } catch (error) {
      setReceiptAction((state) => state ? { ...state, error: error instanceof Error ? error.message : 'Не удалось зарегистрировать действие квитанции.' } : state)
    } finally {
      setReceiptActionSaving(false)
    }
  }

  function selectGarage(garage: PaymentsPrototypeGarage) {
    setSelectedGarageId(garage.id)
    setGarageSearch(`Гараж ${garage.number} - ${garage.ownerName}`)
    setGarageRows([])
    setGarageWorksheetSummary(null)
    setHistoryRows([])
    setPaymentError(null)
    setReceiptActionStatus(null)
    void loadGarageIncomeWorksheet(garage)
    void loadGaragePaymentHistory(garage)
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
      selectGarage(garageSearchResults[0])
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
        { id: operation.id, date: formatDateOnly(operation.operationDate), time: formatPaymentPrototypeOperationTime(operation.createdAtUtc) || paymentTime, amount: operation.amount, purpose: operation.incomeTypeName ?? row.service, debtAfter: historyDebtAfter },
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
          time: formatPaymentPrototypeOperationTime(operation.createdAtUtc) || paymentTime,
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
              onChange={(event) => {
                setGarageSearch(event.target.value)
                setSelectedGarageId(null)
                setGarageWorksheetSummary(null)
                setGarageRows([])
                setHistoryRows([])
              }}
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
              {garageSearchResults.length > 0 ? garageSearchResults.map((garage) => (
                <button className="payments-prototype-search-option" key={garage.id} type="button" role="option" aria-selected={garage.id === selectedGarageId} onClick={() => selectGarage(garage)}>
                  <strong>Гараж {garage.number}</strong>
                  <span>{garage.ownerName}</span>
                </button>
              )) : <span className="payments-prototype-search-empty">Ничего не найдено</span>}
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
                              <button className="icon-button" type="button" title="Повторная печать квитанции" aria-label={`Повторно напечатать квитанцию платежа ${row.purpose}`} onClick={(event) => openReceiptAction(row, 'reprint', event.currentTarget)}>
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

type FundPrototypeRow = {
  id: string
  name: string
  amount: number | null
  actions?: false
}

type FundOperationKind = 'withdraw' | 'deposit'

type FundOperationDraft = {
  kind: FundOperationKind
  fundId: string
  fundName: string
  amount: string
  reason: string
}

type FundOperationStatusDraft = {
  action: 'cancel' | 'restore'
  operation: FundOperationDto
  reason: string
}

type FundOperationEditDraft = {
  operation: FundOperationDto
  amount: string
  reason: string
}

type FundOperationEditConfirmation = {
  operation: FundOperationDto
  amount: number
  reason: string
  changes: ChangePreview[]
}

type FundOperationReverseDraft = {
  operation: FundOperationDto
  reason: string
}

function mapFundDtoToPrototypeRow(fund: FundDto): FundPrototypeRow {
  return {
    id: fund.id,
    name: fund.name,
    amount: fund.balance,
    actions: fund.allowOperations ? undefined : false,
  }
}

function FundsPrototypePanel({ auth, fundsClient }: { auth: AuthResponse; fundsClient: FundsClient }) {
  const [rows, setRows] = useState<FundPrototypeRow[]>([])
  const [operationRows, setOperationRows] = useState<FundOperationDto[]>([])
  const [availableToDistribute, setAvailableToDistribute] = useState<number | null>(null)
  const [operation, setOperation] = useState<FundOperationDraft | null>(null)
  const [operationEdit, setOperationEdit] = useState<FundOperationEditDraft | null>(null)
  const [operationEditConfirmation, setOperationEditConfirmation] = useState<FundOperationEditConfirmation | null>(null)
  const [operationReverse, setOperationReverse] = useState<FundOperationReverseDraft | null>(null)
  const [statusAction, setStatusAction] = useState<FundOperationStatusDraft | null>(null)
  const [operationError, setOperationError] = useState<string | null>(null)
  const [operationMessage, setOperationMessage] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [savingOperation, setSavingOperation] = useState(false)
  const [savingStatusAction, setSavingStatusAction] = useState(false)
  useRestoreFocusOnClose(Boolean(operation))
  useRestoreFocusOnClose(Boolean(operationEdit))
  useRestoreFocusOnClose(Boolean(operationEditConfirmation))
  useRestoreFocusOnClose(Boolean(operationReverse))
  useRestoreFocusOnClose(Boolean(statusAction))
  const operationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(operation))
  const operationDialogRef = useFocusTrap<HTMLElement>(Boolean(operation))
  const operationEditCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(operationEdit) && !operationEditConfirmation)
  const operationEditDialogRef = useFocusTrap<HTMLElement>(Boolean(operationEdit) && !operationEditConfirmation)
  const operationEditConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(operationEditConfirmation))
  const operationEditConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(operationEditConfirmation))
  const operationReverseReasonRef = useFocusOnOpen<HTMLTextAreaElement>(Boolean(operationReverse))
  const operationReverseDialogRef = useFocusTrap<HTMLElement>(Boolean(operationReverse))
  const statusReasonRef = useFocusOnOpen<HTMLTextAreaElement>(statusAction?.action === 'cancel')
  const statusCancelRef = useFocusOnOpen<HTMLButtonElement>(statusAction?.action === 'restore')
  const statusDialogRef = useFocusTrap<HTMLElement>(Boolean(statusAction))

  useEscapeKey(Boolean(operation), () => closeFundOperation())
  useEscapeKey(Boolean(operationEdit) && !operationEditConfirmation && !savingOperation, () => closeFundOperationEdit())
  useEscapeKey(Boolean(operationEditConfirmation) && !savingOperation, () => setOperationEditConfirmation(null))
  useEscapeKey(Boolean(operationReverse) && !savingOperation, () => closeFundOperationReverse())
  useEscapeKey(Boolean(statusAction) && !savingStatusAction, () => closeFundStatusAction())

  useEffect(() => {
    let cancelled = false

    async function loadFunds() {
      setIsLoading(true)
      setLoadError(null)
      try {
        const [funds, operations] = await Promise.all([
          fundsClient.getFunds(auth.accessToken),
          fundsClient.getOperations(auth.accessToken, { limit: 20, includeCanceled: true }),
        ])
        if (!cancelled) {
          setRows(funds.map(mapFundDtoToPrototypeRow))
          setOperationRows(operations)
          setAvailableToDistribute(funds.length > 0 ? funds[0].availableToDistribute : null)
        }
      } catch (error: unknown) {
        if (!cancelled) {
          setLoadError(error instanceof Error ? error.message : 'Не удалось загрузить фонды.')
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false)
        }
      }
    }

    void loadFunds()

    return () => {
      cancelled = true
    }
  }, [auth.accessToken, fundsClient])

  async function refreshFundsPanel() {
    const [funds, operations] = await Promise.all([
      fundsClient.getFunds(auth.accessToken),
      fundsClient.getOperations(auth.accessToken, { limit: 20, includeCanceled: true }),
    ])
    setRows(funds.map(mapFundDtoToPrototypeRow))
    setOperationRows(operations)
    setAvailableToDistribute(funds.length > 0 ? funds[0].availableToDistribute : null)
  }

  function openFundOperation(kind: FundOperationKind, fund: FundPrototypeRow) {
    setOperation({ kind, fundId: fund.id, fundName: fund.name, amount: '', reason: '' })
    setOperationError(null)
    setOperationMessage(null)
  }

  function closeFundOperation() {
    setOperation(null)
    setOperationError(null)
  }

  function openFundOperationEdit(fundOperation: FundOperationDto) {
    setOperationEdit({
      operation: fundOperation,
      amount: String(fundOperation.amount).replace('.', ','),
      reason: fundOperation.reason,
    })
    setOperationError(null)
    setOperationMessage(null)
  }

  function closeFundOperationEdit() {
    setOperationEdit(null)
    setOperationEditConfirmation(null)
    setOperationError(null)
  }

  function getFundOperationEditChanges(operation: FundOperationDto, amount: number, reason: string) {
    const changes: ChangePreview[] = []
    appendChangePreview(changes, 'Сумма', formatChangeMoney(operation.amount), formatChangeMoney(amount))
    appendChangePreview(changes, 'Основание', formatChangeText(operation.reason), formatChangeText(reason))
    return changes
  }

  function openFundOperationReverse(fundOperation: FundOperationDto) {
    setOperationReverse({ operation: fundOperation, reason: '' })
    setOperationError(null)
    setOperationMessage(null)
  }

  function closeFundOperationReverse() {
    setOperationReverse(null)
    setOperationError(null)
  }

  function closeFundStatusAction() {
    setStatusAction(null)
    setOperationError(null)
  }

  function openFundStatusAction(action: 'cancel' | 'restore', fundOperation: FundOperationDto) {
    setStatusAction({ action, operation: fundOperation, reason: '' })
    setOperationError(null)
    setOperationMessage(null)
  }

  function parseFundOperationAmount(value: string) {
    const normalized = value.trim().replace(',', '.')
    if (!normalized) {
      return null
    }

    const amount = Number(normalized)
    return Number.isFinite(amount) && amount > 0 ? amount : null
  }

  function getReverseFundOperationKind(kind: FundOperationKind): FundOperationKind {
    return kind === 'deposit' ? 'withdraw' : 'deposit'
  }

  async function submitFundOperation(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!operation) {
      return
    }

    const amount = parseFundOperationAmount(operation.amount)
    const reason = operation.reason.trim()
    if (amount === null) {
      setOperationError('Укажите сумму больше нуля.')
      return
    }

    if (!reason) {
      setOperationError('Укажите причину операции.')
      return
    }

    if (operation.kind === 'deposit' && availableToDistribute !== null && amount > availableToDistribute) {
      setOperationError(`Сумма пополнения не может превышать доступную к распределению сумму ${formatMoney(availableToDistribute)} руб.`)
      return
    }

    setSavingOperation(true)
    try {
      const savedOperation = await fundsClient.createOperation(auth.accessToken, operation.fundId, {
        operationKind: operation.kind,
        amount,
        reason,
      })
      await refreshFundsPanel()
      setOperationMessage(`${operation.kind === 'deposit' ? 'Пополнение' : 'Изъятие'} по фонду "${savedOperation.fundName}" сохранено и записано в историю изменений.`)
      closeFundOperation()
    } catch (error: unknown) {
      setOperationError(error instanceof Error ? error.message : 'Не удалось выполнить операцию фонда.')
    } finally {
      setSavingOperation(false)
    }
  }

  async function submitFundStatusAction(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!statusAction) {
      return
    }

    if (statusAction.action === 'cancel' && !statusAction.reason.trim()) {
      setOperationError('Укажите причину отмены операции фонда.')
      return
    }

    setSavingStatusAction(true)
    try {
      if (statusAction.action === 'cancel') {
        await fundsClient.cancelOperation(auth.accessToken, statusAction.operation.id, { reason: statusAction.reason.trim() })
      } else {
        await fundsClient.restoreOperation(auth.accessToken, statusAction.operation.id)
      }
      await refreshFundsPanel()
      setOperationMessage(`${statusAction.action === 'cancel' ? 'Операция отменена' : 'Операция восстановлена'} и записана в историю изменений.`)
      closeFundStatusAction()
    } catch (error: unknown) {
      setOperationError(error instanceof Error ? error.message : 'Не удалось изменить статус операции фонда.')
    } finally {
      setSavingStatusAction(false)
    }
  }

  async function submitFundOperationEdit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!operationEdit) {
      return
    }

    const amount = parseFundOperationAmount(operationEdit.amount)
    const reason = operationEdit.reason.trim()
    if (amount === null) {
      setOperationError('Укажите сумму больше нуля.')
      return
    }

    if (!reason) {
      setOperationError('Укажите основание изменения операции фонда.')
      return
    }

    const changes = getFundOperationEditChanges(operationEdit.operation, amount, reason)
    if (changes.length === 0) {
      closeFundOperationEdit()
      return
    }

    setOperationEditConfirmation({ operation: operationEdit.operation, amount, reason, changes })
  }

  async function confirmFundOperationEdit() {
    if (!operationEditConfirmation) {
      return
    }

    setSavingOperation(true)
    try {
      const savedOperation = await fundsClient.updateOperation(auth.accessToken, operationEditConfirmation.operation.id, {
        amount: operationEditConfirmation.amount,
        reason: operationEditConfirmation.reason,
      })
      await refreshFundsPanel()
      setOperationMessage(`Операция фонда "${savedOperation.fundName}" изменена и записана в историю изменений.`)
      setOperationEditConfirmation(null)
      closeFundOperationEdit()
    } catch (error: unknown) {
      setOperationError(error instanceof Error ? error.message : 'Не удалось изменить операцию фонда.')
    } finally {
      setSavingOperation(false)
    }
  }

  async function submitFundOperationReverse(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!operationReverse) {
      return
    }

    const reason = operationReverse.reason.trim()
    if (!reason) {
      setOperationError('Укажите причину обратной операции фонда.')
      return
    }

    const reverseKind = getReverseFundOperationKind(operationReverse.operation.operationKind)
    setSavingOperation(true)
    try {
      const savedOperation = await fundsClient.createOperation(auth.accessToken, operationReverse.operation.fundId, {
        operationKind: reverseKind,
        amount: operationReverse.operation.amount,
        reason,
      })
      await refreshFundsPanel()
      setOperationMessage(`Обратная операция фонда "${savedOperation.fundName}" создана и записана в историю изменений.`)
      closeFundOperationReverse()
    } catch (error: unknown) {
      setOperationError(error instanceof Error ? error.message : 'Не удалось создать обратную операцию фонда.')
    } finally {
      setSavingOperation(false)
    }
  }

  const operationAmount = operation ? parseFundOperationAmount(operation.amount) : null
  const operationEditAmount = operationEdit ? parseFundOperationAmount(operationEdit.amount) : null
  const operationActionLabel = operation?.kind === 'deposit' ? 'Пополнить фонд' : 'Изъять из фонда'
  const statusActionTitle = statusAction?.action === 'cancel' ? 'Отменить операцию фонда?' : 'Вернуть операцию фонда?'
  const statusActionLabel = statusAction?.operation.operationKind === 'deposit' ? 'Пополнение' : 'Изъятие'
  const operationReverseKind = operationReverse ? getReverseFundOperationKind(operationReverse.operation.operationKind) : null
  const operationReverseLabel = operationReverseKind === 'deposit' ? 'Пополнение' : 'Изъятие'

  return (
    <section className="funds-page" aria-label="Управление фондами">
      <div className="funds-heading">
        <h1>Управление фондами</h1>
      </div>

      <div className="funds-sheet">
        {isLoading ? <p className="prototype-status" role="status">Загружаем фонды...</p> : null}
        {loadError ? <p className="form-error" role="alert">{loadError}</p> : null}
        <table className="funds-table" aria-label="Фонды и собранные суммы">
          <thead>
            <tr>
              <th scope="col">Фонд</th>
              <th scope="col">Собранная сумма</th>
              <th scope="col">Изъятие</th>
              <th scope="col">Пополнение</th>
            </tr>
          </thead>
          <tbody>
            {rows.length > 0 ? rows.map((row) => (
              <tr key={row.id}>
                <td>{row.name}</td>
                <td>{row.amount === null ? '—' : `${formatMoney(row.amount)} руб.`}</td>
                <td>
                  {row.actions === false ? null : (
                    <button className="funds-action-button funds-action-button--withdraw" type="button" aria-label={`Изъять из фонда ${row.name}`} title={`Изъять из фонда ${row.name}`} data-tooltip="Изъять" onClick={() => openFundOperation('withdraw', row)}>
                      <Minus size={16} aria-hidden="true" />
                    </button>
                  )}
                </td>
                <td>
                  {row.actions === false ? null : (
                    <button className="funds-action-button funds-action-button--deposit" type="button" aria-label={`Пополнить фонд ${row.name}`} title={`Пополнить фонд ${row.name}`} data-tooltip="Пополнить" onClick={() => openFundOperation('deposit', row)}>
                      <Plus size={16} aria-hidden="true" />
                    </button>
                  )}
                </td>
              </tr>
            )) : !isLoading ? (
              <tr>
                <td colSpan={4}>Фонды пока не настроены.</td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>

      <div className="funds-distribution" aria-label="Сумма к распределению">
        <span>Сумма к распределению</span>
        <strong>{availableToDistribute === null || availableToDistribute === 0 ? '—' : `${formatMoney(availableToDistribute)} руб.`}</strong>
      </div>

      {operationMessage ? <p className="form-success" role="status">{operationMessage}</p> : null}

      <div className="funds-sheet funds-operations-sheet">
        <table className="funds-table funds-operations-table" aria-label="Операции фондов">
          <thead>
            <tr>
              <th scope="col">Дата</th>
              <th scope="col">Фонд</th>
              <th scope="col">Операция</th>
              <th scope="col">Сумма</th>
              <th scope="col">Остаток</th>
              <th scope="col">Статус</th>
              <th scope="col">Действие</th>
            </tr>
          </thead>
          <tbody>
            {operationRows.length > 0 ? operationRows.map((fundOperation) => (
              <tr key={fundOperation.id}>
                <td>{formatDateTime(fundOperation.createdAtUtc)}</td>
                <td>{fundOperation.fundName}</td>
                <td>{fundOperation.operationKind === 'deposit' ? 'Пополнение' : 'Изъятие'}</td>
                <td>{formatMoney(fundOperation.amount)} руб.</td>
                <td>{formatMoney(fundOperation.balanceAfter)} руб.</td>
                <td>
                  <span className={fundOperation.isCanceled ? 'dictionary-status-pill dictionary-status-pill-archived' : 'dictionary-status-pill dictionary-status-pill-active'}>
                    {fundOperation.isCanceled ? 'Отменена' : 'Активна'}
                  </span>
                </td>
                <td>
                  <div className="funds-operation-actions">
                    {fundOperation.isCanceled ? (
                      <button className="funds-action-button" type="button" aria-label={`Вернуть операцию фонда ${fundOperation.fundName}`} title={`Вернуть операцию фонда ${fundOperation.fundName}`} data-tooltip="Вернуть" onClick={() => openFundStatusAction('restore', fundOperation)}>
                        <RotateCcw size={16} aria-hidden="true" />
                      </button>
                    ) : (
                      <>
                        <button className="funds-action-button" type="button" aria-label={`Изменить операцию фонда ${fundOperation.fundName}`} title={`Изменить операцию фонда ${fundOperation.fundName}`} data-tooltip="Изменить" onClick={() => openFundOperationEdit(fundOperation)}>
                          <Pencil size={16} aria-hidden="true" />
                        </button>
                        <button className="funds-action-button" type="button" aria-label={`Создать обратную операцию фонда ${fundOperation.fundName}`} title={`Создать обратную операцию фонда ${fundOperation.fundName}`} data-tooltip="Обратная" onClick={() => openFundOperationReverse(fundOperation)}>
                          <RefreshCw size={16} aria-hidden="true" />
                        </button>
                        <button className="funds-action-button funds-action-button--withdraw" type="button" aria-label={`Отменить операцию фонда ${fundOperation.fundName}`} title={`Отменить операцию фонда ${fundOperation.fundName}`} data-tooltip="Отменить" onClick={() => openFundStatusAction('cancel', fundOperation)}>
                          <Trash2 size={16} aria-hidden="true" />
                        </button>
                      </>
                    )}
                  </div>
                </td>
              </tr>
            )) : !isLoading ? (
              <tr>
                <td colSpan={7}>Операций фондов пока нет.</td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>

      {operation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeFundOperation}>
          <section ref={operationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="fund-operation-title" aria-describedby="fund-operation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Операция фонда</p>
                <h3 id="fund-operation-title">{operationActionLabel}</h3>
                <p>{operation.fundName}</p>
              </div>
              <button className="icon-button" type="button" onClick={closeFundOperation} aria-label="Закрыть операцию фонда">
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fund-operation-description">Проверьте сумму и укажите причину. Операция будет сохранена и записана в историю изменений.</p>
            <form className="dictionary-modal-form" onSubmit={submitFundOperation}>
              <FormField label="Сумма">
                <input
                  aria-label="Сумма операции фонда"
                  inputMode="decimal"
                  value={operation.amount}
                  onChange={(event) => {
                    setOperation({ ...operation, amount: event.target.value })
                    if (operationError) {
                      setOperationError(null)
                    }
                  }}
                  placeholder="0,00"
                />
              </FormField>
              {operation.kind === 'deposit' && availableToDistribute !== null ? (
                <p className="form-hint">Доступно к пополнению: {formatMoney(availableToDistribute)} руб.</p>
              ) : null}
              <FormField label="Причина">
                <textarea
                  aria-label="Причина операции фонда"
                  rows={3}
                  maxLength={1000}
                  value={operation.reason}
                  onChange={(event) => {
                    setOperation({ ...operation, reason: event.target.value })
                    if (operationError) {
                      setOperationError(null)
                    }
                  }}
                  placeholder="Например: распределение средств по решению правления"
                />
              </FormField>
              <dl className="fund-operation-preview">
                <div>
                  <dt>Действие</dt>
                  <dd>{operation.kind === 'deposit' ? 'Пополнение' : 'Изъятие'}</dd>
                </div>
                <div>
                  <dt>Сумма</dt>
                  <dd>{operationAmount === null ? '—' : `${formatMoney(operationAmount)} руб.`}</dd>
                </div>
              </dl>
              {operationError ? <p className="form-error" role="alert">{operationError}</p> : null}
              <div className="detail-dialog-actions">
                <button ref={operationCancelRef} className="ghost-button" type="button" onClick={closeFundOperation} disabled={savingOperation}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={savingOperation}>
                  <Save size={16} />
                  <span>{savingOperation ? 'Сохраняем...' : 'Подтвердить операцию'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
      {operationEdit ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!savingOperation && !operationEditConfirmation) {
            closeFundOperationEdit()
          }
        }}>
          <section ref={operationEditDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="fund-operation-edit-title" aria-describedby="fund-operation-edit-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Изменение операции</p>
                <h3 id="fund-operation-edit-title">Изменить операцию фонда?</h3>
                <p>{operationEdit.operation.fundName} · {operationEdit.operation.operationKind === 'deposit' ? 'Пополнение' : 'Изъятие'}</p>
              </div>
              <button className="icon-button" type="button" onClick={closeFundOperationEdit} aria-label="Закрыть изменение операции фонда" disabled={savingOperation}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fund-operation-edit-description">Проверьте сумму и основание. Изменение пересчитает остаток фонда и попадет в историю изменений.</p>
            <form className="dictionary-modal-form" onSubmit={submitFundOperationEdit}>
              <FormField label="Сумма">
                <input
                  aria-label="Новая сумма операции фонда"
                  inputMode="decimal"
                  value={operationEdit.amount}
                  onChange={(event) => {
                    setOperationEdit({ ...operationEdit, amount: event.target.value })
                    if (operationError) {
                      setOperationError(null)
                    }
                  }}
                  disabled={savingOperation}
                />
              </FormField>
              <FormField label="Основание">
                <textarea
                  aria-label="Новое основание операции фонда"
                  rows={3}
                  maxLength={1000}
                  value={operationEdit.reason}
                  onChange={(event) => {
                    setOperationEdit({ ...operationEdit, reason: event.target.value })
                    if (operationError) {
                      setOperationError(null)
                    }
                  }}
                  disabled={savingOperation}
                />
              </FormField>
              <dl className="fund-operation-preview">
                <div>
                  <dt>Было</dt>
                  <dd>{formatMoney(operationEdit.operation.amount)} руб.</dd>
                </div>
                <div>
                  <dt>Стало</dt>
                  <dd>{operationEditAmount === null ? '—' : `${formatMoney(operationEditAmount)} руб.`}</dd>
                </div>
              </dl>
              {operationError ? <p className="form-error" role="alert">{operationError}</p> : null}
              <div className="detail-dialog-actions">
                <button ref={operationEditCancelRef} className="ghost-button" type="button" onClick={closeFundOperationEdit} disabled={savingOperation}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={savingOperation}>
                  <Save size={16} aria-hidden="true" />
                  <span>{savingOperation ? 'Сохраняем...' : 'Сохранить изменения'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
      {operationEditConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!savingOperation) {
            setOperationEditConfirmation(null)
          }
        }}>
          <section ref={operationEditConfirmationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="fund-operation-edit-confirmation-title" aria-describedby="fund-operation-edit-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Проверка изменения</p>
                <h3 id="fund-operation-edit-confirmation-title">Подтвердить изменение операции фонда?</h3>
                <p>{operationEditConfirmation.operation.fundName} · {operationEditConfirmation.operation.operationKind === 'deposit' ? 'Пополнение' : 'Изъятие'}</p>
              </div>
              <button className="icon-button" type="button" onClick={() => setOperationEditConfirmation(null)} aria-label="Закрыть подтверждение операции фонда" disabled={savingOperation}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fund-operation-edit-confirmation-description">Проверьте изменения перед сохранением. После подтверждения backend пересчитает остаток фонда и запишет корректировку в историю изменений.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля операции фонда">
              {operationEditConfirmation.changes.map((change) => (
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
            {operationError ? <p className="form-error" role="alert">{operationError}</p> : null}
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={operationEditConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setOperationEditConfirmation(null)} disabled={savingOperation}>Отмена</button>
              <button className="secondary-button" type="button" onClick={confirmFundOperationEdit} disabled={savingOperation}>
                <Save size={16} aria-hidden="true" />
                <span>{savingOperation ? 'Сохраняем...' : 'Сохранить'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
      {operationReverse ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!savingOperation) {
            closeFundOperationReverse()
          }
        }}>
          <section ref={operationReverseDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="fund-operation-reverse-title" aria-describedby="fund-operation-reverse-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Обратная операция</p>
                <h3 id="fund-operation-reverse-title">Создать обратную операцию фонда?</h3>
                <p>{operationReverse.operation.fundName} · {operationReverse.operation.operationKind === 'deposit' ? 'Пополнение' : 'Изъятие'} {'->'} {operationReverseLabel}</p>
              </div>
              <button className="icon-button" type="button" onClick={closeFundOperationReverse} aria-label="Закрыть обратную операцию фонда" disabled={savingOperation}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fund-operation-reverse-description">Исходная операция останется в истории, а система создаст новую противоположную операцию на ту же сумму и запишет причину в историю изменений.</p>
            <form className="dictionary-modal-form" onSubmit={submitFundOperationReverse}>
              <FormField label="Причина">
                <textarea
                  ref={operationReverseReasonRef}
                  aria-label="Причина обратной операции фонда"
                  rows={3}
                  maxLength={1000}
                  value={operationReverse.reason}
                  onChange={(event) => {
                    setOperationReverse({ ...operationReverse, reason: event.target.value })
                    if (operationError) {
                      setOperationError(null)
                    }
                  }}
                  placeholder="Например: сторнирование ошибочной операции"
                  disabled={savingOperation}
                />
              </FormField>
              <dl className="fund-operation-preview">
                <div>
                  <dt>Будет создано</dt>
                  <dd>{operationReverseLabel}</dd>
                </div>
                <div>
                  <dt>Сумма</dt>
                  <dd>{formatMoney(operationReverse.operation.amount)} руб.</dd>
                </div>
              </dl>
              {operationError ? <p className="form-error" role="alert">{operationError}</p> : null}
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={closeFundOperationReverse} disabled={savingOperation}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={savingOperation}>
                  <RefreshCw size={16} aria-hidden="true" />
                  <span>{savingOperation ? 'Сохраняем...' : 'Создать обратную операцию'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
      {statusAction ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!savingStatusAction) {
            closeFundStatusAction()
          }
        }}>
          <section ref={statusDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="fund-status-title" aria-describedby="fund-status-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">{statusAction.action === 'cancel' ? 'Отмена операции' : 'Восстановление операции'}</p>
                <h3 id="fund-status-title">{statusActionTitle}</h3>
                <p>{statusAction.operation.fundName} · {statusActionLabel} · {formatMoney(statusAction.operation.amount)} руб.</p>
              </div>
              <button className="icon-button" type="button" onClick={closeFundStatusAction} aria-label="Закрыть подтверждение операции фонда" disabled={savingStatusAction}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fund-status-description">
              {statusAction.action === 'cancel'
                ? 'Укажите причину отмены. Операция будет скрыта из активных расчетов, а действие попадет в историю изменений.'
                : 'Операция снова попадет в активные расчеты фонда, а действие будет записано в историю изменений.'}
            </p>
            <form className="dictionary-modal-form" onSubmit={submitFundStatusAction}>
              {statusAction.action === 'cancel' ? (
                <FormField label="Причина отмены">
                  <textarea
                    ref={statusReasonRef}
                    aria-label="Причина отмены операции фонда"
                    rows={3}
                    maxLength={1000}
                    value={statusAction.reason}
                    onChange={(event) => {
                      setStatusAction({ ...statusAction, reason: event.target.value })
                      if (operationError) {
                        setOperationError(null)
                      }
                    }}
                    placeholder="Например: ошибочное распределение средств"
                    disabled={savingStatusAction}
                  />
                </FormField>
              ) : null}
              {operationError ? <p className="form-error" role="alert">{operationError}</p> : null}
              <div className="detail-dialog-actions">
                <button ref={statusCancelRef} className="ghost-button" type="button" onClick={closeFundStatusAction} disabled={savingStatusAction}>Отмена</button>
                <button className={statusAction.action === 'cancel' ? 'secondary-button danger-button' : 'secondary-button'} type="submit" disabled={savingStatusAction}>
                  {statusAction.action === 'cancel' ? <Trash2 size={16} aria-hidden="true" /> : <RotateCcw size={16} aria-hidden="true" />}
                  <span>{savingStatusAction ? 'Сохраняем...' : statusAction.action === 'cancel' ? 'Отменить операцию' : 'Вернуть операцию'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
    </section>
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
  const [rollbackingRunId, setRollbackingRunId] = useState<string | null>(null)
  const [rollbackTarget, setRollbackTarget] = useState<AccessImportRunDto | null>(null)
  const [rollbackReason, setRollbackReason] = useState('')
  const [rollbackError, setRollbackError] = useState<string | null>(null)
  const [quarantineResolveTarget, setQuarantineResolveTarget] = useState<AccessImportQuarantineItemDto | null>(null)
  const [quarantineResolveComment, setQuarantineResolveComment] = useState('')
  const [quarantineResolveError, setQuarantineResolveError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [exportMessage, setExportMessage] = useState<string | null>(null)
  const filePickerActionLabel = 'Выбрать файл Access .accdb или .mdb'
  const dryRunActionLabel = selectedFile ? `Проверить файл Access ${selectedFile.name}` : 'Проверить файл Access'
  const reportDownloadActionLabel = currentRun ? `Скачать JSON-отчет dry-run ${currentRun.originalFileName}` : 'Скачать JSON-отчет dry-run'
  const rollbackActionLabel = currentRun ? `Запросить rollback импорта ${currentRun.originalFileName}` : 'Запросить rollback импорта'
  const rollbackDisabled = !currentRun || currentRun.status === 'rollback_requested' || rollbackingRunId !== null
  useRestoreFocusOnClose(Boolean(quarantineResolveTarget))
  useRestoreFocusOnClose(Boolean(rollbackTarget))
  const quarantineResolveCommentRef = useFocusOnOpen<HTMLTextAreaElement>(Boolean(quarantineResolveTarget))
  const quarantineResolveDialogRef = useFocusTrap<HTMLElement>(Boolean(quarantineResolveTarget))
  const rollbackReasonRef = useFocusOnOpen<HTMLTextAreaElement>(Boolean(rollbackTarget))
  const rollbackDialogRef = useFocusTrap<HTMLElement>(Boolean(rollbackTarget))
  const visibleRunLogEntries = runLogEntries.slice(0, 10)
  const visibleRuns = runs.slice(0, 8)
  const visibleQuarantineItems = quarantineItems.slice(0, 8)
  const importTabs: Array<{ key: ImportTab; label: string; meta: string }> = [
    { key: 'checks', label: 'Проверки', meta: currentRun ? formatImportRunCheckSummary(currentRun) : 'ожидают запуска' },
    { key: 'log', label: 'Лог', meta: loadingLog ? 'загрузка' : `${runLogEntries.length} строк` },
    { key: 'history', label: 'История', meta: `${runs.length} запусков` },
    { key: 'quarantine', label: 'Карантин', meta: `${quarantineItems.length} открыто` },
  ]

  useEscapeKey(Boolean(quarantineResolveTarget) && resolvingQuarantineId === null, () => closeQuarantineResolveDialog())
  useEscapeKey(Boolean(rollbackTarget) && rollbackingRunId === null, () => closeRollbackDialog())

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

  function openRollbackDialog(run: AccessImportRunDto) {
    setRollbackTarget(run)
    setRollbackReason('')
    setRollbackError(null)
    setError(null)
    setExportMessage(null)
  }

  function closeRollbackDialog() {
    setRollbackTarget(null)
    setRollbackReason('')
    setRollbackError(null)
  }

  async function submitRollbackRequest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!rollbackTarget) {
      return
    }

    const reason = rollbackReason.trim()
    if (!reason) {
      setRollbackError('Укажите причину rollback импорта.')
      return
    }

    setRollbackingRunId(rollbackTarget.id)
    setError(null)
    setExportMessage(null)
    try {
      const updatedRun = await importClient.requestAccessImportRollback(auth.accessToken, rollbackTarget.id, reason)
      setCurrentRun(updatedRun)
      setRuns((items) => items.map((item) => item.id === updatedRun.id ? updatedRun : item))
      setRunLogEntries(await importClient.getAccessRunLog(auth.accessToken, updatedRun.id))
      setExportMessage('Rollback импорта запрошен. Фактический откат данных не выполнялся для dry-run запуска.')
      closeRollbackDialog()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось запросить rollback импорта.')
    } finally {
      setRollbackingRunId(null)
    }
  }

  function openQuarantineResolveDialog(item: AccessImportQuarantineItemDto) {
    setQuarantineResolveTarget(item)
    setQuarantineResolveComment('')
    setQuarantineResolveError(null)
    setError(null)
    setExportMessage(null)
  }

  function closeQuarantineResolveDialog() {
    setQuarantineResolveTarget(null)
    setQuarantineResolveComment('')
    setQuarantineResolveError(null)
  }

  async function resolveQuarantineItem(item: AccessImportQuarantineItemDto, resolutionComment: string) {
    setResolvingQuarantineId(item.id)
    setError(null)
    setExportMessage(null)
    try {
      await importClient.resolveQuarantineItem(auth.accessToken, item.id, resolutionComment)
      setQuarantineItems((items) => items.filter((candidate) => candidate.id !== item.id))
      setExportMessage('Строка карантина закрыта.')
      closeQuarantineResolveDialog()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось закрыть строку карантина импорта.')
    } finally {
      setResolvingQuarantineId(null)
    }
  }

  async function submitQuarantineResolve(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!quarantineResolveTarget) {
      return
    }

    const comment = quarantineResolveComment.trim()
    if (!comment) {
      setQuarantineResolveError('Укажите комментарий к закрытию строки карантина.')
      return
    }

    await resolveQuarantineItem(quarantineResolveTarget, comment)
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
            <label className="file-picker-button" htmlFor={fileInputId} title={filePickerActionLabel} data-tooltip={filePickerActionLabel}>
              <FileText size={16} aria-hidden="true" />
              <span>Выбрать .accdb или .mdb</span>
            </label>
            <span className="file-picker-name" role="status" aria-live="polite">{selectedFile ? selectedFile.name : 'Файл не выбран'}</span>
          </div>
          <button className="secondary-button" type="submit" aria-label={dryRunActionLabel} title={dryRunActionLabel} data-tooltip={dryRunActionLabel} disabled={saving || !selectedFile}>
            <DatabaseZap size={16} aria-hidden="true" />
            <span>Проверить файл</span>
          </button>
        </form>

        <div className="dictionary-form">
          <h3>Отчет проверки</h3>
          <button className="secondary-button" type="button" aria-label={reportDownloadActionLabel} title={reportDownloadActionLabel} data-tooltip={reportDownloadActionLabel} disabled={!currentRun || exporting} onClick={downloadCurrentReport}>
            <FileText size={16} aria-hidden="true" />
            <span>Скачать отчет JSON</span>
          </button>
          <button className="secondary-button" type="button" aria-label={rollbackActionLabel} title={rollbackActionLabel} data-tooltip={rollbackActionLabel} disabled={rollbackDisabled} onClick={() => currentRun ? openRollbackDialog(currentRun) : undefined}>
            <RotateCcw size={16} aria-hidden="true" />
            <span>Запросить rollback</span>
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
              <span role="cell" className={run.status === 'completed' ? 'status-active' : run.status === 'rollback_requested' ? 'warning-text' : 'status-disabled'}>
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
                <button className="secondary-button" type="button" title={`Закрыть строку карантина ${item.entityType}${item.externalId ? ` #${item.externalId}` : ''}`} data-tooltip="Закрыть" disabled={resolvingQuarantineId === item.id} onClick={() => openQuarantineResolveDialog(item)}>
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
      {rollbackTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (rollbackingRunId === null) {
            closeRollbackDialog()
          }
        }}>
          <section ref={rollbackDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="import-rollback-title" aria-describedby="import-rollback-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Rollback импорта</p>
                <h3 id="import-rollback-title">Запросить rollback импорта?</h3>
                <p>{rollbackTarget.originalFileName} · {formatImportRunStatus(rollbackTarget.status)}</p>
              </div>
              <button className="icon-button" type="button" onClick={closeRollbackDialog} aria-label="Закрыть подтверждение rollback импорта" title="Закрыть подтверждение rollback импорта" data-tooltip="Закрыть" disabled={rollbackingRunId !== null}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p className="confirmation-text" id="import-rollback-description">Rollback будет записан в историю импорта с причиной. Для dry-run запуска фактический откат данных не выполняется, потому что данные еще не переносились в рабочую базу.</p>
            <form className="dictionary-modal-form" onSubmit={submitRollbackRequest}>
              <FormField label="Причина rollback">
                <textarea
                  ref={rollbackReasonRef}
                  aria-label="Причина rollback импорта"
                  aria-invalid={Boolean(rollbackError)}
                  aria-describedby={rollbackError ? 'import-rollback-error' : undefined}
                  rows={3}
                  maxLength={1000}
                  value={rollbackReason}
                  onChange={(event) => {
                    setRollbackReason(event.target.value)
                    if (rollbackError && event.target.value.trim()) {
                      setRollbackError(null)
                    }
                  }}
                  placeholder="Например: выбран неверный файл старой базы"
                  disabled={rollbackingRunId !== null}
                />
              </FormField>
              <dl className="fund-operation-preview">
                <div>
                  <dt>Запуск</dt>
                  <dd>{rollbackTarget.originalFileName}</dd>
                </div>
                <div>
                  <dt>Проверки</dt>
                  <dd>{formatImportRunCheckSummary(rollbackTarget)}</dd>
                </div>
              </dl>
              {rollbackError ? <p className="form-error" id="import-rollback-error" role="alert">{rollbackError}</p> : null}
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={closeRollbackDialog} disabled={rollbackingRunId !== null}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={rollbackingRunId !== null}>
                  <RotateCcw size={16} aria-hidden="true" />
                  <span>{rollbackingRunId === rollbackTarget.id ? 'Запрашиваем...' : 'Запросить rollback'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
      {quarantineResolveTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (resolvingQuarantineId === null) {
            closeQuarantineResolveDialog()
          }
        }}>
          <section ref={quarantineResolveDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="import-quarantine-resolve-title" aria-describedby="import-quarantine-resolve-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Карантин импорта</p>
                <h3 id="import-quarantine-resolve-title">Закрыть строку карантина?</h3>
                <p>{quarantineResolveTarget.entityType}{quarantineResolveTarget.externalId ? ` #${quarantineResolveTarget.externalId}` : ''} · {quarantineResolveTarget.reasonCode}</p>
              </div>
              <button className="icon-button" type="button" onClick={closeQuarantineResolveDialog} aria-label="Закрыть подтверждение карантина" title="Закрыть подтверждение карантина" data-tooltip="Закрыть" disabled={resolvingQuarantineId !== null}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p className="confirmation-text" id="import-quarantine-resolve-description">Строка исчезнет из списка открытого карантина, а комментарий будет записан в историю импорта. Убедитесь, что причина разобрана и перенос можно продолжать безопасно.</p>
            <form className="dictionary-modal-form" onSubmit={submitQuarantineResolve}>
              <FormField label="Комментарий">
                <textarea
                  ref={quarantineResolveCommentRef}
                  aria-label="Комментарий к закрытию строки карантина"
                  aria-invalid={Boolean(quarantineResolveError)}
                  aria-describedby={quarantineResolveError ? 'import-quarantine-resolve-error' : undefined}
                  rows={3}
                  maxLength={1000}
                  value={quarantineResolveComment}
                  onChange={(event) => {
                    setQuarantineResolveComment(event.target.value)
                    if (quarantineResolveError && event.target.value.trim()) {
                      setQuarantineResolveError(null)
                    }
                  }}
                  placeholder="Например: владелец найден и сопоставлен вручную"
                  disabled={resolvingQuarantineId !== null}
                />
              </FormField>
              <dl className="fund-operation-preview">
                <div>
                  <dt>Строка</dt>
                  <dd>{quarantineResolveTarget.sourceSystem} · {quarantineResolveTarget.rowHash.slice(0, 12)}</dd>
                </div>
                <div>
                  <dt>Причина</dt>
                  <dd>{quarantineResolveTarget.reasonMessage}</dd>
                </div>
              </dl>
              {quarantineResolveError ? <p className="form-error" id="import-quarantine-resolve-error" role="alert">{quarantineResolveError}</p> : null}
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={closeQuarantineResolveDialog} disabled={resolvingQuarantineId !== null}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={resolvingQuarantineId !== null}>
                  <Save size={16} aria-hidden="true" />
                  <span>{resolvingQuarantineId === quarantineResolveTarget.id ? 'Закрываем...' : 'Закрыть строку'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}
    </section>
  )
}

function getAuditEventSectionLabel(auditEvent: AuditEventDto) {
  const sectionCode = auditEvent.section || auditEvent.action.split('.')[0] || ''
  return auditSectionOptions.find((option) => option.value === sectionCode)?.label ?? (sectionCode || 'Система')
}

function getAuditEventActionKindLabel(auditEvent: AuditEventDto) {
  if (auditEvent.actionKind) {
    const option = auditActionKindOptions.find((item) => item.value === auditEvent.actionKind)
    if (option && option.value) {
      return option.label
    }
  }

  const action = auditEvent.action.toLowerCase()
  if (action.includes('_created')) return 'Создание'
  if (action.includes('_updated') || action.includes('password_changed')) return 'Изменение'
  if (action.includes('_archived')) return 'Архивирование'
  if (action.includes('_restored')) return 'Восстановление'
  if (action.includes('_canceled') || action.includes('_cancelled')) return 'Отмена'
  if (action.includes('_failed') || action.includes('_rate_limited') || action.includes('_inactive')) return 'Ошибка'
  if (action.includes('_generated')) return 'Формирование'
  if (action.includes('_exported') || action.includes('.export')) return 'Выгрузка'
  if (action.startsWith('auth.login')) return 'Вход'
  if (action.startsWith('import.')) return 'Импорт'
  return auditEvent.action
}

function getAuditEntityTypeLabel(entityType: string) {
  return auditEntityTypeOptions.find((option) => option.value === entityType)?.label ?? entityType
}

function formatAuditActor(actorUserId: string | null) {
  if (!actorUserId) {
    return 'Система'
  }

  return `ID ${actorUserId.slice(0, 8)}`
}

function parseAuditBeforeAfter(summary: string) {
  const match = summary.match(/было\s+(.+?);?\s+стало\s+(.+?)(?:\.|$)/i)
  if (!match) {
    return { before: 'не указано', after: 'не указано' }
  }

  return {
    before: match[1].trim(),
    after: match[2].trim(),
  }
}

function getAuditBeforeAfter(auditEvent: AuditEventDto) {
  if (auditEvent.oldValue || auditEvent.newValue) {
    return {
      before: auditEvent.oldValue ?? 'не указано',
      after: auditEvent.newValue ?? 'не указано',
    }
  }

  return parseAuditBeforeAfter(auditEvent.summary)
}

function getAuditRelatedContext(auditEvent: AuditEventDto) {
  return [
    auditEvent.relatedGarageNumber || auditEvent.relatedGarageId
      ? ['Гараж', auditEvent.relatedGarageNumber ? `№ ${auditEvent.relatedGarageNumber}` : auditEvent.relatedGarageId]
      : null,
    auditEvent.relatedAccountingMonth ? ['Месяц', auditEvent.relatedAccountingMonth] : null,
    auditEvent.relatedCounterpartyName || auditEvent.relatedCounterpartyId
      ? ['Контрагент', auditEvent.relatedCounterpartyName ?? auditEvent.relatedCounterpartyId]
      : null,
    auditEvent.relatedDocumentNumber || auditEvent.relatedDocumentId
      ? ['Документ', auditEvent.relatedDocumentNumber ?? auditEvent.relatedDocumentId]
      : null,
  ].filter((item): item is [string, string] => Boolean(item))
}

function getAuditWorkspaceTarget(auth: AuthResponse, auditEvent: AuditEventDto): { section: WorkspaceSection; label: string } | null {
  if (auditEvent.entityType === 'owner' || auditEvent.entityType === 'garage' || auditEvent.entityType === 'supplier') {
    return hasPermission(auth, permissions.dictionariesRead) ? { section: 'contractors', label: 'Контрагенты' } : null
  }

  if (auditEvent.entityType === 'tariff' || auditEvent.section === 'dictionary') {
    return hasPermission(auth, permissions.dictionariesRead) ? { section: 'tariffsAndFees', label: 'Тарифы и сборы' } : null
  }

  if (auditEvent.entityType === 'meter_reading') {
    return hasPermission(auth, permissions.paymentsRead) ? { section: 'meterReadings', label: 'Показания' } : null
  }

  if (auditEvent.section === 'finance' || auditEvent.entityType === 'financial_operation' || auditEvent.entityType === 'accrual' || auditEvent.entityType === 'supplier_accrual') {
    return hasPermission(auth, permissions.paymentsRead) ? { section: 'payments', label: 'Платежи' } : null
  }

  if (auditEvent.section === 'users' || auditEvent.entityType === 'app_user') {
    return hasPermission(auth, permissions.usersManage) ? { section: 'users', label: 'Пользователи' } : null
  }

  if (auditEvent.section === 'import' || auditEvent.entityType === 'access_import_run') {
    return hasPermission(auth, permissions.importRun) ? { section: 'import', label: 'Импорт' } : null
  }

  if (auditEvent.section === 'reports' || auditEvent.section === 'report' || auditEvent.entityType === 'report' || auditEvent.entityType === 'report_export') {
    return hasPermission(auth, permissions.reportsRead) && hasPermission(auth, permissions.dictionariesRead) ? { section: 'reports', label: 'Отчеты' } : null
  }

  if (auditEvent.section === 'app_releases') {
    return { section: 'releases', label: 'Что нового' }
  }

  return null
}

type AuditPanelError = {
  message: string
  recovery: 'load' | 'exportCsv' | 'exportXlsx'
}

function AuditPanel({ auth, auditClient, onOpenSection }: { auth: AuthResponse; auditClient: AuditClient; onOpenSection: (section: WorkspaceSection) => void }) {
  const [page, setPage] = useState<PagedItems<AuditEventDto>>(() => createEmptyPage<AuditEventDto>(25))
  const [search, setSearch] = useState('')
  const [section, setSection] = useState('')
  const [actionKind, setActionKind] = useState('')
  const [entityType, setEntityType] = useState('')
  const [actorUserId, setActorUserId] = useState('')
  const [quickFilter, setQuickFilter] = useState('')
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [relatedGarage, setRelatedGarage] = useState('')
  const [relatedAccountingMonth, setRelatedAccountingMonth] = useState('')
  const [relatedCounterparty, setRelatedCounterparty] = useState('')
  const [relatedDocument, setRelatedDocument] = useState('')
  const [loading, setLoading] = useState(true)
  const [exporting, setExporting] = useState(false)
  const [error, setError] = useState<AuditPanelError | null>(null)
  const [reloadToken, setReloadToken] = useState(0)
  const [exportMessage, setExportMessage] = useState<string | null>(null)
  const [detailState, setDetailState] = useState<{ event: AuditEventDto; loading: boolean; error: string | null } | null>(null)
  const detailRequestIdRef = useRef(0)
  useRestoreFocusOnClose(Boolean(detailState))
  const detailCloseButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(detailState))
  const detailDialogRef = useFocusTrap<HTMLElement>(Boolean(detailState))
  const detailRelatedContext = detailState ? getAuditRelatedContext(detailState.event) : []
  const detailWorkspaceTarget = detailState ? getAuditWorkspaceTarget(auth, detailState.event) : null
  const auditValidationErrors = useMemo(() => {
    if (dateFrom && dateTo && dateFrom > dateTo) {
      return ['Начало периода истории изменений не может быть позже конца.']
    }

    return []
  }, [dateFrom, dateTo])
  const resetAuditPageOffset = useCallback(() => {
    setPage((current) => current.offset === 0 ? current : { ...current, offset: 0 })
  }, [])
  const auditQuery = useMemo(() => ({
    search: search.trim() || undefined,
    section: section || undefined,
    actionKind: actionKind || undefined,
    entityType: entityType || undefined,
    actorUserId: actorUserId.trim() || undefined,
    quickFilter: quickFilter || undefined,
    dateFrom: dateFrom || undefined,
    dateTo: dateTo || undefined,
    relatedGarage: relatedGarage.trim() || undefined,
    relatedAccountingMonth: relatedAccountingMonth || undefined,
    relatedCounterparty: relatedCounterparty.trim() || undefined,
    relatedDocument: relatedDocument.trim() || undefined,
    offset: page.offset,
    limit: page.limit,
  }), [actionKind, actorUserId, dateFrom, dateTo, entityType, page.limit, page.offset, quickFilter, relatedAccountingMonth, relatedCounterparty, relatedDocument, relatedGarage, search, section])
  const auditExportQuery = useMemo(() => ({
    search: auditQuery.search,
    section: auditQuery.section,
    actionKind: auditQuery.actionKind,
    entityType: auditQuery.entityType,
    actorUserId: auditQuery.actorUserId,
    quickFilter: auditQuery.quickFilter,
    dateFrom: auditQuery.dateFrom,
    dateTo: auditQuery.dateTo,
    relatedGarage: auditQuery.relatedGarage,
    relatedAccountingMonth: auditQuery.relatedAccountingMonth,
    relatedCounterparty: auditQuery.relatedCounterparty,
    relatedDocument: auditQuery.relatedDocument,
  }), [auditQuery])

  function closeAuditEventDetail() {
    detailRequestIdRef.current += 1
    setDetailState(null)
  }

  function openAuditWorkspaceTarget(section: WorkspaceSection) {
    closeAuditEventDetail()
    onOpenSection(section)
  }

  async function openAuditEventDetail(auditEvent: AuditEventDto) {
    const requestId = detailRequestIdRef.current + 1
    detailRequestIdRef.current = requestId
    setDetailState({ event: auditEvent, loading: true, error: null })
    try {
      const loadedEvent = await auditClient.getEvent(auth.accessToken, auditEvent.id)
      if (detailRequestIdRef.current === requestId) {
        setDetailState({ event: loadedEvent, loading: false, error: null })
      }
    } catch (caught) {
      if (detailRequestIdRef.current === requestId) {
        setDetailState({
          event: auditEvent,
          loading: false,
          error: caught instanceof Error ? caught.message : 'Не удалось загрузить карточку события.',
        })
      }
    }
  }

  useEscapeKey(Boolean(detailState), closeAuditEventDetail)

  useEffect(() => {
    let ignore = false

    async function load() {
      if (auditValidationErrors.length > 0) {
        setLoading(false)
        setError(null)
        return
      }

      setLoading(true)
      setError(null)
      try {
        const loadedPage = await auditClient.getEventsPage(auth.accessToken, auditQuery)
        if (!ignore) {
          setPage(loadedPage)
        }
      } catch (caught) {
        if (!ignore) {
          setError({
            message: caught instanceof Error ? caught.message : 'Не удалось загрузить историю изменений.',
            recovery: 'load',
          })
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
  }, [auditValidationErrors, auth.accessToken, auditClient, auditQuery, reloadToken])

  async function exportCurrentEventsCsv() {
    if (auditValidationErrors.length > 0) {
      return
    }

    setExporting(true)
    setError(null)
    setExportMessage(null)
    try {
      const blob = await auditClient.exportEvents(auth.accessToken, auditExportQuery)
      downloadBlob(blob, buildAuditExportFileName(undefined, 'csv'))
      setExportMessage('История изменений CSV готова.')
    } catch (caught) {
      setError({
        message: caught instanceof Error ? caught.message : 'Не удалось скачать историю изменений.',
        recovery: 'exportCsv',
      })
    } finally {
      setExporting(false)
    }
  }

  async function exportCurrentEventsXlsx() {
    if (auditValidationErrors.length > 0) {
      return
    }

    setExporting(true)
    setError(null)
    setExportMessage(null)
    try {
      const blob = await auditClient.exportEventsXlsx(auth.accessToken, auditExportQuery)
      downloadBlob(blob, buildAuditExportFileName(undefined, 'xlsx'))
      setExportMessage('История изменений XLSX готова.')
    } catch (caught) {
      setError({
        message: caught instanceof Error ? caught.message : 'Не удалось скачать XLSX истории изменений.',
        recovery: 'exportXlsx',
      })
    } finally {
      setExporting(false)
    }
  }

  function retryAuditError() {
    if (!error) {
      return
    }

    if (error.recovery === 'exportCsv') {
      void exportCurrentEventsCsv()
      return
    }

    if (error.recovery === 'exportXlsx') {
      void exportCurrentEventsXlsx()
      return
    }

    setReloadToken((value) => value + 1)
  }

  const retryAuditErrorBusy = error?.recovery === 'exportCsv' || error?.recovery === 'exportXlsx' ? exporting : loading
  const retryAuditErrorLabel = error?.recovery === 'exportCsv'
    ? (exporting ? 'Выгружаем...' : 'Повторить выгрузку CSV')
    : error?.recovery === 'exportXlsx'
      ? (exporting ? 'Выгружаем...' : 'Повторить выгрузку XLSX')
      : (loading ? 'Загружаем...' : 'Повторить загрузку')
  const auditVisibleRange = getPageVisibleRange(page)
  const auditNavigation = getPageNavigation(page)
  const auditHasValidationErrors = auditValidationErrors.length > 0

  return (
    <section className="dictionary-panel" aria-label="История изменений">
      <div className="section-heading">
        <div>
          <p className="eyebrow">История</p>
          <h2>История изменений объектов и действий системы</h2>
        </div>
        <div className="section-actions">
          <span>{loading ? 'Загрузка...' : `${page.totalCount} событий`}</span>
          <button className="secondary-button" type="button" disabled={exporting || auditHasValidationErrors} onClick={exportCurrentEventsCsv}>
            <FileSpreadsheet size={16} />
            Скачать CSV
          </button>
          <button className="secondary-button" type="button" disabled={exporting || auditHasValidationErrors} onClick={exportCurrentEventsXlsx}>
            <FileSpreadsheet size={16} />
            Скачать XLSX
          </button>
        </div>
      </div>

      {error ? (
        <div className="audit-error-state">
          <FormError>{error.message}</FormError>
          <button className="ghost-button" type="button" onClick={retryAuditError} disabled={retryAuditErrorBusy}>
            <RefreshCw size={16} aria-hidden="true" />
            <span>{retryAuditErrorLabel}</span>
          </button>
        </div>
      ) : null}
      {exportMessage ? <div className="form-note" role="status" aria-live="polite">{exportMessage}</div> : null}
      <FormValidationSummary title="Проверьте период истории" items={auditValidationErrors} />

      <form className="audit-filter-grid" onSubmit={(event) => event.preventDefault()} aria-label="Фильтры истории изменений">
        <FormField label="Поиск">
          <input aria-label="Поиск в истории изменений" placeholder="Действие, объект или описание" value={search} onChange={(event) => { setSearch(event.target.value); resetAuditPageOffset() }} />
        </FormField>
        <FormField label="Раздел">
          <select aria-label="Раздел истории изменений" value={section} onChange={(event) => { setSection(event.target.value); resetAuditPageOffset() }}>
            {auditSectionOptions.map((option) => <option value={option.value} key={option.value}>{option.label}</option>)}
          </select>
        </FormField>
        <FormField label="Действие">
          <select aria-label="Тип действия истории изменений" value={actionKind} onChange={(event) => { setActionKind(event.target.value); resetAuditPageOffset() }}>
            {auditActionKindOptions.map((option) => <option value={option.value} key={option.value}>{option.label}</option>)}
          </select>
        </FormField>
        <FormField label="Объект">
          <select aria-label="Тип объекта истории изменений" value={entityType} onChange={(event) => { setEntityType(event.target.value); resetAuditPageOffset() }}>
            {auditEntityTypeOptions.map((option) => <option value={option.value} key={option.value}>{option.label}</option>)}
          </select>
        </FormField>
        <FormField label="Пользователь">
          <input aria-label="ID пользователя истории изменений" placeholder="ID пользователя" value={actorUserId} onChange={(event) => { setActorUserId(event.target.value); resetAuditPageOffset() }} />
        </FormField>
        <FormField label="Быстрый фильтр">
          <select aria-label="Быстрый фильтр истории изменений" value={quickFilter} onChange={(event) => { setQuickFilter(event.target.value); resetAuditPageOffset() }}>
            {auditQuickFilterOptions.map((option) => <option value={option.value} key={option.value}>{option.label}</option>)}
          </select>
        </FormField>
        <FormField label="Гараж">
          <input aria-label="Связанный гараж истории изменений" placeholder="Номер или ID гаража" value={relatedGarage} onChange={(event) => { setRelatedGarage(event.target.value); resetAuditPageOffset() }} />
        </FormField>
        <FormField label="Месяц">
          <input aria-label="Связанный месяц истории изменений" type="month" value={relatedAccountingMonth} onChange={(event) => { setRelatedAccountingMonth(event.target.value); resetAuditPageOffset() }} />
        </FormField>
        <FormField label="Контрагент">
          <input aria-label="Связанный контрагент истории изменений" placeholder="Название или ID" value={relatedCounterparty} onChange={(event) => { setRelatedCounterparty(event.target.value); resetAuditPageOffset() }} />
        </FormField>
        <FormField label="Документ">
          <input aria-label="Связанный документ истории изменений" placeholder="Номер или ID документа" value={relatedDocument} onChange={(event) => { setRelatedDocument(event.target.value); resetAuditPageOffset() }} />
        </FormField>
        <FormField label="С даты">
          <input aria-label="Начало периода истории изменений" type="date" value={dateFrom} onChange={(event) => { setDateFrom(event.target.value); resetAuditPageOffset() }} />
        </FormField>
        <FormField label="По дату">
          <input aria-label="Конец периода истории изменений" type="date" value={dateTo} onChange={(event) => { setDateTo(event.target.value); resetAuditPageOffset() }} />
        </FormField>
      </form>

      <div className="operation-list audit-event-table" role="table" aria-label="События истории изменений">
        <div className="audit-event-row header" role="row">
          <span role="columnheader">Время</span>
          <span role="columnheader">Кто</span>
          <span role="columnheader">Раздел</span>
          <span role="columnheader">Объект</span>
          <span role="columnheader">Действие</span>
          <span role="columnheader">Поле</span>
          <span role="columnheader">Было</span>
          <span role="columnheader">Стало</span>
          <span role="columnheader">Причина</span>
          <span role="columnheader">Карточка</span>
        </div>
        {!loading && page.items.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Событий пока нет</p> : null}
        {page.items.map((auditEvent) => {
          const beforeAfter = getAuditBeforeAfter(auditEvent)
          return (
            <div className="audit-event-row" role="row" key={auditEvent.id}>
              <span role="cell">{formatDateTime(auditEvent.createdAtUtc)}</span>
              <span role="cell">{formatAuditActor(auditEvent.actorUserId)}</span>
              <span role="cell">{getAuditEventSectionLabel(auditEvent)}</span>
              <span role="cell">
                <strong>{getAuditEntityTypeLabel(auditEvent.entityType)}</strong>
                {auditEvent.entityDisplayName ? <small>{auditEvent.entityDisplayName}</small> : null}
                <small>{auditEvent.entityId ?? 'без идентификатора'}</small>
              </span>
              <span role="cell">
                <strong>{getAuditEventActionKindLabel(auditEvent)}</strong>
                <small>{auditEvent.summary}</small>
              </span>
              <span role="cell">{auditEvent.fieldName ?? 'не указано'}</span>
              <span role="cell">{beforeAfter.before}</span>
              <span role="cell">{beforeAfter.after}</span>
              <span role="cell">{auditEvent.reason ?? 'не указано'}</span>
              <span role="cell">
                <button className="ghost-button audit-detail-button" type="button" onClick={() => void openAuditEventDetail(auditEvent)}>
                  <FileText size={15} aria-hidden="true" />
                  <span>Открыть</span>
                </button>
              </span>
            </div>
          )
        })}
      </div>
      <div className="dictionary-pagination audit-pagination" role="navigation" aria-label="Пагинация истории изменений">
        <span role="status" aria-live="polite">Показано {auditVisibleRange.from}-{auditVisibleRange.to} из {page.totalCount}</span>
        <label>
          Строк
          <select aria-label="Количество строк истории изменений" value={page.limit} onChange={(event) => setPage(createEmptyPage<AuditEventDto>(Number(event.target.value)))}>
            {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
          </select>
        </label>
        <button className="ghost-button" type="button" disabled={loading || !auditNavigation.canGoPrevious} onClick={() => setPage((current) => ({ ...current, offset: auditNavigation.previousOffset }))}>Назад</button>
        <button className="ghost-button" type="button" disabled={loading || !auditNavigation.canGoNext} onClick={() => setPage((current) => ({ ...current, offset: auditNavigation.nextOffset }))}>Вперед</button>
      </div>
      {detailState ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeAuditEventDetail}>
          <section ref={detailDialogRef} className="detail-dialog audit-detail-dialog" role="dialog" aria-modal="true" aria-labelledby="audit-detail-title" aria-describedby="audit-detail-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Карточка события</p>
                <h3 id="audit-detail-title">{getAuditEventActionKindLabel(detailState.event)}</h3>
                <p id="audit-detail-description">{getAuditEventSectionLabel(detailState.event)} · {formatDateTime(detailState.event.createdAtUtc)}</p>
              </div>
              <button ref={detailCloseButtonRef} className="icon-button" type="button" aria-label="Закрыть карточку события" onClick={closeAuditEventDetail}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            {detailState.loading ? <p className="form-note" role="status" aria-live="polite">Загружаем карточку события...</p> : null}
            {detailState.error ? (
              <div className="audit-error-state">
                <FormError>{detailState.error}</FormError>
                <button className="ghost-button" type="button" onClick={() => void openAuditEventDetail(detailState.event)} disabled={detailState.loading}>
                  <RefreshCw size={16} aria-hidden="true" />
                  <span>{detailState.loading ? 'Загружаем...' : 'Повторить загрузку карточки'}</span>
                </button>
              </div>
            ) : null}
            <dl className="detail-grid audit-detail-grid">
              <div>
                <dt>Пользователь</dt>
                <dd>{formatAuditActor(detailState.event.actorUserId)}</dd>
              </div>
              <div>
                <dt>ID пользователя</dt>
                <dd>{detailState.event.actorUserId ?? 'Система'}</dd>
              </div>
              <div>
                <dt>Раздел</dt>
                <dd>{getAuditEventSectionLabel(detailState.event)}</dd>
              </div>
              <div>
                <dt>Объект</dt>
                <dd>{getAuditEntityTypeLabel(detailState.event.entityType)}</dd>
              </div>
              <div>
                <dt>Название объекта</dt>
                <dd>{detailState.event.entityDisplayName ?? 'не указано'}</dd>
              </div>
              <div>
                <dt>ID объекта</dt>
                <dd>{detailState.event.entityId ?? 'без идентификатора'}</dd>
              </div>
              <div>
                <dt>Код действия</dt>
                <dd>{detailState.event.action}</dd>
              </div>
              <div>
                <dt>Поле</dt>
                <dd>{detailState.event.fieldName ?? 'не указано'}</dd>
              </div>
              <div>
                <dt>Было</dt>
                <dd>{getAuditBeforeAfter(detailState.event).before}</dd>
              </div>
              <div>
                <dt>Стало</dt>
                <dd>{getAuditBeforeAfter(detailState.event).after}</dd>
              </div>
              <div>
                <dt>Причина/комментарий</dt>
                <dd>{detailState.event.reason ?? 'не указано'}</dd>
              </div>
            </dl>
            <div className="audit-detail-summary">
              <span>Описание события</span>
              <p>{detailState.event.summary}</p>
            </div>
            {detailRelatedContext.length > 0 ? (
              <div className="audit-detail-summary audit-detail-metadata">
                <span>Связанные данные</span>
                <dl>
                  {detailRelatedContext.map(([label, value]) => (
                    <div key={label}>
                      <dt>{label}</dt>
                      <dd>{value}</dd>
                    </div>
                  ))}
                </dl>
              </div>
            ) : null}
            {detailState.event.metadata && Object.keys(detailState.event.metadata).length > 0 ? (
              <div className="audit-detail-summary audit-detail-metadata">
                <span>Служебные данные</span>
                <dl>
                  {Object.entries(detailState.event.metadata).map(([key, value]) => (
                    <div key={key}>
                      <dt>{key}</dt>
                      <dd>{value}</dd>
                    </div>
                  ))}
                </dl>
              </div>
            ) : null}
            <div className="detail-dialog-actions">
              {detailWorkspaceTarget ? (
                <button className="ghost-button" type="button" onClick={() => openAuditWorkspaceTarget(detailWorkspaceTarget.section)}>
                  <ArrowLeft size={16} aria-hidden="true" />
                  <span>Открыть раздел: {detailWorkspaceTarget.label}</span>
                </button>
              ) : null}
              <button className="secondary-button" type="button" onClick={closeAuditEventDetail}>Закрыть</button>
            </div>
          </section>
        </div>
      ) : null}
    </section>
  )
}

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

type ReportGarageServiceRow = {
  monthLabel: string
  garageNumber: string
  serviceName: string
  accrualAmount: number
  incomeAmount: number
  debt: number
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

function getCurrentMonthInputValue(dateValue = getLocalDateInputValue()) {
  return dateValue.slice(0, 7)
}

function getPreviousMonthInputValue(monthValue: string) {
  const [yearText, monthText] = monthValue.split('-')
  const date = new Date(Number(yearText), Number(monthText) - 2, 1)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`
}

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

function aggregateGarageIncomeReportRows(report: IncomeReportDto | null): ReportGarageServiceRow[] {
  const totals = new Map<string, ReportGarageServiceRow>()
  report?.rows.forEach((row) => {
    const key = `${row.accountingMonth}|${row.garageNumber}|${row.incomeTypeName}`
    const current = totals.get(key) ?? {
      monthLabel: formatMonth(row.accountingMonth),
      garageNumber: row.garageNumber,
      serviceName: row.incomeTypeName,
      accrualAmount: 0,
      incomeAmount: 0,
      debt: 0,
    }
    current.accrualAmount += row.accrualAmount
    current.incomeAmount += row.incomeAmount
    current.debt = current.accrualAmount - current.incomeAmount
    totals.set(key, current)
  })
  return Array.from(totals.values())
    .sort((left, right) => left.monthLabel.localeCompare(right.monthLabel, 'ru') || left.garageNumber.localeCompare(right.garageNumber, 'ru') || left.serviceName.localeCompare(right.serviceName, 'ru'))
}

function aggregateGarageIncomeRowsByGarage(rows: ReportGarageServiceRow[]): ReportGarageServiceRow[] {
  const totals = new Map<string, ReportGarageServiceRow>()
  rows.forEach((row) => {
    const key = `${row.monthLabel}|${row.garageNumber}`
    const current = totals.get(key) ?? {
      monthLabel: row.monthLabel,
      garageNumber: row.garageNumber,
      serviceName: 'ИТОГО',
      accrualAmount: 0,
      incomeAmount: 0,
      debt: 0,
    }
    current.accrualAmount += row.accrualAmount
    current.incomeAmount += row.incomeAmount
    current.debt = current.accrualAmount - current.incomeAmount
    totals.set(key, current)
  })

  return Array.from(totals.values())
    .sort((left, right) => left.monthLabel.localeCompare(right.monthLabel, 'ru') || left.garageNumber.localeCompare(right.garageNumber, 'ru'))
}

function ReportPanel({ auth, dictionaryClient, reportClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient; reportClient: ReportClient }) {
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
  const [garageReport, setGarageReport] = useState<ConsolidatedReportDto | null>(null)
  const [garageIncomeDetailReport, setGarageIncomeDetailReport] = useState<IncomeReportDto | null>(null)
  const [payoutReport, setPayoutReport] = useState<ExpenseReportDto | null>(null)
  const [incomeReport, setIncomeReport] = useState<IncomeReportDto | null>(null)
  const [cashPaymentReport, setCashPaymentReport] = useState<CashPaymentReportDto | null>(null)
  const [bankDepositReport, setBankDepositReport] = useState<BankDepositReportDto | null>(null)
  const [feeReport, setFeeReport] = useState<FeeReportDto | null>(null)
  const [feeDebtorsVisible, setFeeDebtorsVisible] = useState(false)
  const [feeDetailMode, setFeeDetailMode] = useState<'debtors' | 'all'>('debtors')
  const [garageAccrualsGrouped, setGarageAccrualsGrouped] = useState(false)
  const [reportDataError, setReportDataError] = useState<string | null>(null)
  const [reportExporting, setReportExporting] = useState<string | null>(null)
  const [reportExportMessage, setReportExportMessage] = useState<string | null>(null)
  const [fundChangeReport, setFundChangeReport] = useState<FundChangeReportDto | null>(null)
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
        const garageFilterRange = monthlyFilters.garages
        const payoutFilter = monthlyFilters.payouts
        const incomeFilter = dateFilters.income
        const cashPaymentFilter = dateFilters.cashPayments
        const bankDepositFilter = dateFilters.bankDeposits
        const [loadedConsolidated, loadedConsolidatedIncome, loadedConsolidatedExpenses, loadedGarages, loadedGarageIncomeDetails, loadedPayouts, loadedIncome, loadedCashPayments, loadedBankDeposits, loadedFees] = await Promise.all([
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
          reportClient.getConsolidatedReport(auth.accessToken, {
            monthFrom: getReportMonthStart(garageFilterRange.monthFrom),
            monthTo: getReportMonthStart(garageFilterRange.monthTo),
            search: garageFilter.trim() || undefined,
            limit: 100,
          }),
          reportClient.getIncomeReport(auth.accessToken, {
            dateFrom: getReportMonthStart(garageFilterRange.monthFrom),
            dateTo: getReportMonthEnd(garageFilterRange.monthTo),
            search: garageFilter.trim() || undefined,
            limit: 500,
          }),
          reportClient.getExpenseReport(auth.accessToken, {
            dateFrom: getReportMonthStart(payoutFilter.monthFrom),
            dateTo: getReportMonthEnd(payoutFilter.monthTo),
            search: counterpartyFilter.trim() || undefined,
            limit: 100,
          }),
          reportClient.getIncomeReport(auth.accessToken, {
            dateFrom: incomeFilter.dateFrom,
            dateTo: incomeFilter.dateTo,
            search: incomeGarageFilter.trim() || undefined,
            rowMode: 'payments',
            limit: 100,
          }),
          reportClient.getCashPaymentReport(auth.accessToken, {
            dateFrom: cashPaymentFilter.dateFrom,
            dateTo: cashPaymentFilter.dateTo,
            limit: 100,
          }),
          reportClient.getBankDepositReport(auth.accessToken, {
            dateFrom: bankDepositFilter.dateFrom,
            dateTo: bankDepositFilter.dateTo,
            limit: 100,
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
        setGarageReport(loadedGarages)
        setGarageIncomeDetailReport(loadedGarageIncomeDetails)
        setPayoutReport(loadedPayouts)
        setIncomeReport(loadedIncome)
        setCashPaymentReport(loadedCashPayments)
        setBankDepositReport(loadedBankDeposits)
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
  }, [auth.accessToken, counterpartyFilter, dateFilters.bankDeposits, dateFilters.cashPayments, dateFilters.income, feeVariationFilter, garageFilter, incomeGarageFilter, monthlyFilters.consolidated, monthlyFilters.garages, monthlyFilters.payouts, reportClient])

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
          limit: 100,
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
  }, [activeReportTab, auth.accessToken, dateFilters.funds, reportClient])

  const selectedTab = reportWorkbookTabs.find((tab) => tab.key === activeReportTab) ?? reportWorkbookTabs[0]
  const garageFilterLabel = garageFilter.trim() || 'Все гаражи'
  const incomeGarageFilterLabel = incomeGarageFilter.trim() || 'Все гаражи'
  const counterpartyFilterLabel = counterpartyFilter.trim() || 'Все поставщики и сотрудники'
  const feeVariationLabel = feeVariationFilter.trim() || 'Все сборы'
  const feeOptions = Array.from(new Set(['Сбор на ворота', 'Вступительный взнос', 'Целевой взнос', ...incomeTypes.map((item) => item.name)]))
  const counterpartyOptions = Array.from(new Set([...suppliers.map((supplier) => supplier.name), ...expenseTypes.map((item) => item.name), 'Электрик', 'Председатель', 'Бухгалтерия']))

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

  function applyPreviousMonth(key: ReportMonthlyFilterKey) {
    setMonthlyFilters((current) => ({
      ...current,
      [key]: { monthFrom: previousMonth, monthTo: previousMonth },
    }))
  }

  function applyToday(key: ReportDateFilterKey) {
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
      const garageRowsByService = aggregateGarageIncomeReportRows(garageIncomeDetailReport)
      const groupedGarageRows = aggregateGarageIncomeRowsByGarage(garageRowsByService)
      const visibleGarageRows = garageAccrualsGrouped ? groupedGarageRows : garageRowsByService
      const garageReportColumns = garageAccrualsGrouped
        ? ['Месяц', 'Гараж', 'Начисления', 'Поступления', 'Разница']
        : ['Месяц', 'Гараж', 'Начисления', 'Услуга', 'Поступления', 'Разница']
      const reportRows = visibleGarageRows.map((row) => garageAccrualsGrouped
        ? [
          row.monthLabel,
          row.garageNumber,
          formatMoney(row.accrualAmount),
          formatMoney(row.incomeAmount),
          formatMoney(row.debt),
        ]
        : [
          row.monthLabel,
          row.garageNumber,
          formatMoney(row.accrualAmount),
          row.serviceName,
          formatMoney(row.incomeAmount),
          formatMoney(row.debt),
        ])
      const emptyGarageRow = garageAccrualsGrouped
        ? ['', garageFilterLabel, '', 'Данных за период нет', '']
        : ['', garageFilterLabel, '', 'Данных за период нет', '', '']
      const garageReportFooter = garageReport
        ? garageAccrualsGrouped
          ? ['ИТОГО', '', formatMoney(garageReport.accrualTotal), formatMoney(garageReport.incomeTotal), formatMoney(garageReport.debt)]
          : ['ИТОГО', '', formatMoney(garageReport.accrualTotal), '', formatMoney(garageReport.incomeTotal), formatMoney(garageReport.debt)]
        : undefined
      return (
        <ReportWorkbookSheet title="Отчёт по гаражам">
          {renderMonthlyFilter('garages', {
            from: 'Месяц с',
            to: 'Месяц по',
            extra: (
              <label className="report-workbook-filter-wide">
                <span>Гаражи</span>
                <input aria-label="Гаражи" list={garageOptionsId} value={garageFilter} onChange={(event) => setGarageFilter(event.target.value)} placeholder="Гараж или номер" />
              </label>
            ),
          })}
          <div className="report-workbook-summary-row">
            <strong>ИТОГО начислений</strong>
            <strong>ИТОГО поступлений</strong>
            <strong>Разница</strong>
          </div>
          <div className="report-workbook-toolbar" role="group" aria-label="Группировка отчета по гаражам">
            <button className="secondary-button" type="button" aria-pressed={garageAccrualsGrouped} onClick={() => setGarageAccrualsGrouped((current) => !current)}>
              {garageAccrualsGrouped ? 'Разгруппировать начисления' : 'Сгруппировать начисления'}
            </button>
          </div>
          {renderReportTable(
            'Отчет по гаражам',
            garageReportColumns,
            reportRows.length > 0 ? reportRows : [emptyGarageRow],
            garageReportFooter,
          )}
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
      return (
        <ReportWorkbookSheet title="Отчёт по выплатам">
          {renderMonthlyFilter('payouts', {
            from: 'Месяц с',
            to: 'Месяц по',
            extra: (
              <label className="report-workbook-filter-wide">
                <span>Поставщики/сотрудники</span>
                <input aria-label="Поставщики или сотрудники" list={supplierOptionsId} value={counterpartyFilter} onChange={(event) => setCounterpartyFilter(event.target.value)} placeholder="Поставщик или сотрудник" />
              </label>
            ),
          })}
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
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'income') {
      const incomeRows = incomeReport?.rows.filter((row) => row.rowType === 'payments').map((row) => [
        row.garageNumber,
        row.date,
        formatPaymentPrototypeOperationTime(row.createdAtUtc),
        formatMoney(row.incomeAmount),
        row.incomeTypeName,
        row.debtAfterPayment === null || row.debtAfterPayment === undefined ? '' : formatMoney(row.debtAfterPayment),
      ]) ?? []
      return (
        <ReportWorkbookSheet title="Отчет по поступлениям">
          {renderDateFilter('income', {
            from: 'С',
            to: 'По',
            extra: (
              <label className="report-workbook-filter-wide">
                <span>Гаражи</span>
                <input aria-label="Гаражи по поступлениям" list={garageOptionsId} value={incomeGarageFilter} onChange={(event) => setIncomeGarageFilter(event.target.value)} placeholder="Гараж или номер" />
              </label>
            ),
          })}
          <div className="report-workbook-summary-row report-workbook-summary-row--single"><strong>ИТОГО</strong></div>
          {renderReportTable(
            'Отчет по поступлениям',
            ['Гараж', 'Дата', 'Время', 'Сумма платежа', 'Назначение платежа', 'Остаток долга после платежа'],
            incomeRows.length > 0 ? incomeRows : [[incomeGarageFilterLabel, '', '', 'Данных за период нет', '', '']],
            incomeReport ? ['ИТОГО', '', '', formatMoney(incomeReport.incomeTotal), '', ''] : undefined,
          )}
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
      return (
        <ReportWorkbookSheet title="Отчёт по оплатам из кассы">
          {renderDateFilter('cashPayments', { from: 'С', to: 'По' })}
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
        </ReportWorkbookSheet>
      )
    }

    if (activeReportTab === 'bankDeposits') {
      const bankRows = bankDepositReport?.rows.map((row) => [
        row.date,
        formatMoney(row.amount),
        row.comment || row.fundName || '',
      ]) ?? []
      return (
        <ReportWorkbookSheet title="Отчёт по сдаче кассы в банк">
          {renderDateFilter('bankDeposits', { from: 'С', to: 'По' })}
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

type UserEditorState = { mode: 'create' | 'edit'; user?: ManagedUserDto }
type UserSaveConfirmationState = { user: ManagedUserDto; changes: UserEditChange[]; request: UpdateManagedUserRequest }
type RolePermissionEditorState = { role: ManagedRoleDto; permissions: string[] }
type RolePermissionConfirmationState = { role: ManagedRoleDto; changes: ChangePreview[]; permissions: string[] }

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
  const [saveConfirmation, setSaveConfirmation] = useState<UserSaveConfirmationState | null>(null)
  const [roleEditor, setRoleEditor] = useState<RolePermissionEditorState | null>(null)
  const [roleConfirmation, setRoleConfirmation] = useState<RolePermissionConfirmationState | null>(null)
  const [rolePermissionError, setRolePermissionError] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ManagedUserDto | null>(null)
  const [restoreTarget, setRestoreTarget] = useState<ManagedUserDto | null>(null)
  const [deleteReason, setDeleteReason] = useState('')
  const [deleteReasonError, setDeleteReasonError] = useState<string | null>(null)
  const [form, setForm] = useState<UserFormState>({ email: '', displayName: '', password: '', roleCode: 'operator', isActive: true, deactivationReason: '' })
  useRestoreFocusOnClose(Boolean(editor))
  const editorCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(editor))
  const editorDialogRef = useFocusTrap<HTMLElement>(Boolean(editor) && !saveConfirmation)
  useRestoreFocusOnClose(Boolean(saveConfirmation))
  const saveConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(saveConfirmation))
  const saveConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(saveConfirmation))
  useRestoreFocusOnClose(Boolean(roleEditor))
  const roleEditorCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(roleEditor))
  const roleEditorDialogRef = useFocusTrap<HTMLElement>(Boolean(roleEditor) && !roleConfirmation)
  useRestoreFocusOnClose(Boolean(roleConfirmation))
  const roleConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(roleConfirmation))
  const roleConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(roleConfirmation))
  useRestoreFocusOnClose(Boolean(deleteTarget))
  const deleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(deleteTarget))
  const deleteDialogRef = useFocusTrap<HTMLElement>(Boolean(deleteTarget))
  useRestoreFocusOnClose(Boolean(restoreTarget))
  const restoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(restoreTarget))
  const restoreDialogRef = useFocusTrap<HTMLElement>(Boolean(restoreTarget))

  useEscapeKey(Boolean(contextMenu), () => setContextMenu(null))
  useEscapeKey(Boolean(editor) && !saveConfirmation, () => closeEditor())
  useEscapeKey(Boolean(saveConfirmation), () => setSaveConfirmation(null))
  useEscapeKey(Boolean(roleEditor) && !roleConfirmation, () => closeRoleEditor())
  useEscapeKey(Boolean(roleConfirmation), () => setRoleConfirmation(null))
  useEscapeKey(Boolean(deleteTarget), () => closeDeleteDialog())
  useEscapeKey(Boolean(restoreTarget), () => setRestoreTarget(null))

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
      deactivationReason: '',
    })
  }

  function closeEditor() {
    setEditor(null)
    setSaveConfirmation(null)
    setValidationErrors([])
  }

  async function saveUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!editor) {
      return
    }

    const errors = getUserEditorValidationErrors(form, editor.mode, editor.user)
    if (errors.length > 0) {
      setValidationErrors(errors)
      return
    }

    setValidationErrors([])

    if (editor.mode === 'edit' && editor.user) {
      const request: UpdateManagedUserRequest = {
        displayName: form.displayName.trim(),
        roleCodes: [form.roleCode],
        isActive: form.isActive,
        newPassword: form.password.trim() ? form.password : null,
        deactivationReason: editor.user.isActive && !form.isActive ? form.deactivationReason.trim() : null,
      }
      const changes = getUserEditorChanges(form, editor.user, roles)
      if (changes.length === 0) {
        closeEditor()
        return
      }

      setSaveConfirmation({ user: editor.user, changes, request })
      return
    }

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

  async function confirmSaveUser() {
    if (!saveConfirmation) {
      return
    }

    setSaving('edit')
    setError(null)
    try {
      await userClient.updateUser(auth.accessToken, saveConfirmation.user.id, saveConfirmation.request)
      setSaveConfirmation(null)
      closeEditor()
      showToast('Пользователь изменен.')
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

    const reason = deleteReason.trim()
    if (!reason) {
      setDeleteReasonError('Укажите причину отключения пользователя.')
      return
    }

    setSaving('delete')
    setError(null)
    setDeleteReasonError(null)
    try {
      await userClient.updateUser(auth.accessToken, deleteTarget.id, {
        displayName: deleteTarget.displayName,
        roleCodes: [getPrimaryRoleCode(deleteTarget, roles)],
        isActive: false,
        newPassword: null,
        deactivationReason: reason,
      })
      closeDeleteDialog()
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

  async function restoreUser() {
    if (!restoreTarget) {
      return
    }

    setSaving('restore')
    setError(null)
    try {
      await userClient.restoreUser(auth.accessToken, restoreTarget.id)
      setRestoreTarget(null)
      showToast('Пользователь восстановлен.')
      await refreshUsers()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось восстановить пользователя.')
    } finally {
      setSaving(null)
    }
  }

  function openRoleEditor(role: ManagedRoleDto) {
    setRolePermissionError(null)
    setRoleEditor({ role, permissions: [...role.permissions] })
  }

  function closeRoleEditor() {
    setRoleEditor(null)
    setRoleConfirmation(null)
    setRolePermissionError(null)
  }

  function toggleRolePermission(permission: string, checked: boolean) {
    setRolePermissionError(null)
    setRoleEditor((current) => {
      if (!current) {
        return current
      }

      const permissionsSet = new Set(current.permissions)
      if (checked) {
        permissionsSet.add(permission)
      } else {
        permissionsSet.delete(permission)
      }

      return { ...current, permissions: [...permissionsSet].sort() }
    })
  }

  function saveRolePermissions(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!roleEditor) {
      return
    }

    if (roleEditor.permissions.length === 0) {
      setRolePermissionError('Выберите хотя бы одно право для роли.')
      return
    }

    const before = formatRolePermissionLabels(roleEditor.role.permissions)
    const after = formatRolePermissionLabels(roleEditor.permissions)
    if (before === after) {
      closeRoleEditor()
      return
    }

    setRoleConfirmation({
      role: roleEditor.role,
      permissions: roleEditor.permissions,
      changes: [{ field: 'Права', before, after }],
    })
  }

  async function confirmRolePermissions() {
    if (!roleConfirmation) {
      return
    }

    setSaving('role')
    setError(null)
    try {
      const updatedRole = await userClient.updateRolePermissions(auth.accessToken, roleConfirmation.role.code, { permissions: roleConfirmation.permissions })
      setRoles((current) => current.map((role) => (role.code === updatedRole.code ? updatedRole : role)))
      setRoleConfirmation(null)
      setRoleEditor(null)
      showToast('Права роли изменены.')
      await refreshUsers()
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить права роли.'
      setError(message)
      showToast(message, 'error')
    } finally {
      setSaving(null)
    }
  }

  function openDeleteDialog(user: ManagedUserDto) {
    setDeleteReason('')
    setDeleteReasonError(null)
    setDeleteTarget(user)
  }

  function closeDeleteDialog() {
    setDeleteTarget(null)
    setDeleteReason('')
    setDeleteReasonError(null)
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

      <RolePermissionMatrix roles={roles} onEditRole={openRoleEditor} />

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
          <button className="context-menu-danger" type="button" role="menuitem" onClick={() => { openDeleteDialog(contextMenu.user); setContextMenu(null) }} disabled={!contextMenu.user.isActive}>
            <Trash2 size={15} />
            <span>Удалить</span>
          </button>
          <button type="button" role="menuitem" onClick={() => { setRestoreTarget(contextMenu.user); setContextMenu(null) }} disabled={contextMenu.user.isActive}>
            <RotateCcw size={15} />
            <span>Вернуть</span>
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
              {editor.user?.isActive && !form.isActive ? (
                <FormField label="Причина отключения">
                  <textarea
                    aria-label="Причина отключения пользователя"
                    placeholder="Например: сотрудник больше не работает или доступ выдан ошибочно"
                    maxLength={1000}
                    value={form.deactivationReason}
                    onChange={(event) => setForm({ ...form, deactivationReason: event.target.value })}
                    required
                  />
                </FormField>
              ) : null}
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

      {saveConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setSaveConfirmation(null)}>
          <section ref={saveConfirmationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="user-save-confirmation-title" aria-describedby="user-save-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Изменение</p>
                <h3 id="user-save-confirmation-title">Подтвердите изменения пользователя</h3>
                <p>{saveConfirmation.user.displayName}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить подтверждение изменений пользователя" onClick={() => setSaveConfirmation(null)} disabled={saving === 'edit'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="user-save-confirmation-description">Проверьте, что именно изменится. После подтверждения действие будет записано в историю изменений.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля пользователя">
              {saveConfirmation.changes.map((change) => (
                <li key={`${change.field}-${change.before}-${change.after}`}>
                  <span className="dictionary-change-field">{change.field}</span>
                  <span className="dictionary-change-values">
                    <span className="dictionary-change-value">{change.before}</span>
                    <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                    <span className="dictionary-change-value dictionary-change-value-after">{change.after}</span>
                  </span>
                </li>
              ))}
            </ul>
            <div className="detail-dialog-actions">
              <button ref={saveConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setSaveConfirmation(null)} disabled={saving === 'edit'}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmSaveUser()} disabled={saving === 'edit'}>
                <Save size={16} />
                <span>{saving === 'edit' ? 'Сохраняем...' : 'Сохранить изменения'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {roleEditor ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeRoleEditor}>
          <section ref={roleEditorDialogRef} className="detail-dialog dictionary-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="role-permissions-title" aria-describedby="role-permissions-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Роль</p>
                <h3 id="role-permissions-title">Изменить права роли</h3>
                <p id="role-permissions-description">{roleEditor.role.name}</p>
              </div>
              <button ref={roleEditorCloseRef} className="icon-button" type="button" onClick={closeRoleEditor} aria-label="Закрыть изменение прав роли" disabled={saving === 'role'}>
                <X size={18} />
              </button>
            </div>
            <form className="dictionary-modal-form" onSubmit={saveRolePermissions}>
              <div className="role-permission-editor" role="group" aria-label={`Права роли ${roleEditor.role.name}`}>
                {rolePermissionGroups.map((group) => {
                  const administratorUsersManage = roleEditor.role.code === 'administrator' && group.permission === permissions.usersManage
                  return (
                    <label className="contractors-check-row" key={group.permission}>
                      <input
                        type="checkbox"
                        aria-label={`${roleEditor.role.name}: ${group.label}`}
                        checked={roleEditor.permissions.includes(group.permission)}
                        disabled={saving === 'role' || administratorUsersManage}
                        onChange={(event) => toggleRolePermission(group.permission, event.target.checked)}
                      />
                      <span>{group.label}</span>
                    </label>
                  )
                })}
              </div>
              {rolePermissionError ? <p className="form-error" role="alert">{rolePermissionError}</p> : null}
              <p className="form-hint">Права применяются к пользователям с этой ролью после обновления их сессии. Изменение будет записано в историю.</p>
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={closeRoleEditor} disabled={saving === 'role'}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={saving === 'role'}>
                  <Save size={16} />
                  <span>Сохранить</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}

      {roleConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setRoleConfirmation(null)}>
          <section ref={roleConfirmationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="role-permissions-confirmation-title" aria-describedby="role-permissions-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Права</p>
                <h3 id="role-permissions-confirmation-title">Подтвердите изменение прав роли</h3>
                <p>{roleConfirmation.role.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить подтверждение изменения прав роли" onClick={() => setRoleConfirmation(null)} disabled={saving === 'role'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="role-permissions-confirmation-description">Проверьте набор доступов. После подтверждения изменение будет записано в историю изменений.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля роли">
              {roleConfirmation.changes.map((change) => (
                <li key={`${change.field}-${change.before}-${change.after}`}>
                  <span className="dictionary-change-field">{change.field}</span>
                  <span className="dictionary-change-values">
                    <span className="dictionary-change-value">{change.before}</span>
                    <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                    <span className="dictionary-change-value dictionary-change-value-after">{change.after}</span>
                  </span>
                </li>
              ))}
            </ul>
            <div className="detail-dialog-actions">
              <button ref={roleConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setRoleConfirmation(null)} disabled={saving === 'role'}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmRolePermissions()} disabled={saving === 'role'}>
                <ShieldCheck size={16} />
                <span>{saving === 'role' ? 'Сохраняем...' : 'Сохранить права'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {deleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => closeDeleteDialog()}>
          <section ref={deleteDialogRef} className="detail-dialog dictionary-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="user-delete-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <h3 id="user-delete-title">Удалить пользователя</h3>
                <p>{deleteTarget.displayName} будет отключен и не сможет входить в систему. История изменений сохранится.</p>
              </div>
              <button className="icon-button" type="button" onClick={() => closeDeleteDialog()} aria-label="Закрыть подтверждение удаления">
                <X size={18} />
              </button>
            </div>
            <label className="field-label" htmlFor="user-delete-reason">Причина отключения</label>
            <textarea
              id="user-delete-reason"
              aria-label="Причина отключения пользователя"
              aria-invalid={Boolean(deleteReasonError)}
              aria-describedby={deleteReasonError ? 'user-delete-reason-error' : undefined}
              maxLength={1000}
              value={deleteReason}
              onChange={(event) => {
                setDeleteReason(event.target.value)
                if (deleteReasonError && event.target.value.trim()) {
                  setDeleteReasonError(null)
                }
              }}
              placeholder="Например: сотрудник больше не работает или доступ выдан ошибочно"
              disabled={saving === 'delete'}
              required
            />
            {deleteReasonError ? <p className="form-error" id="user-delete-reason-error">{deleteReasonError}</p> : null}
            <div className="detail-dialog-actions">
              <button ref={deleteCancelRef} className="ghost-button" type="button" onClick={() => closeDeleteDialog()}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={deleteUser} disabled={saving === 'delete' || !deleteReason.trim()}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {restoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setRestoreTarget(null)}>
          <section ref={restoreDialogRef} className="detail-dialog dictionary-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="user-restore-title" aria-describedby="user-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <h3 id="user-restore-title">Вернуть пользователя?</h3>
                <p>{restoreTarget.displayName} снова сможет входить в систему с прежними ролями.</p>
              </div>
              <button className="icon-button" type="button" onClick={() => setRestoreTarget(null)} aria-label="Отменить восстановление пользователя" disabled={saving === 'restore'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="user-restore-description">Действие будет записано в историю изменений.</p>
            <div className="detail-dialog-actions">
              <button ref={restoreCancelRef} className="ghost-button" type="button" onClick={() => setRestoreTarget(null)} disabled={saving === 'restore'}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void restoreUser()} disabled={saving === 'restore'}>
                <RotateCcw size={16} />
                <span>{saving === 'restore' ? 'Возвращаем...' : 'Вернуть'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {toast ? <div className={`toast-message ${toast.kind === 'error' ? 'toast-message--error' : ''}`} role="status" aria-live="polite">{toast.text}</div> : null}
    </section>
  )
}

function getRolePermissionLabel(permission: string) {
  return rolePermissionGroups.find((group) => group.permission === permission)?.label ?? permission
}

function formatRolePermissionLabels(permissionsList: readonly string[]) {
  const knownLabels = rolePermissionGroups
    .filter((group) => permissionsList.includes(group.permission))
    .map((group) => group.label)
  const customLabels = permissionsList
    .filter((permission) => !rolePermissionGroups.some((group) => group.permission === permission))
    .sort()
    .map(getRolePermissionLabel)
  const labels = [...knownLabels, ...customLabels]
  return labels.length > 0 ? labels.join(', ') : 'Нет прав'
}

function RolePermissionMatrix({ roles, onEditRole }: { roles: ManagedRoleDto[]; onEditRole(role: ManagedRoleDto): void }) {
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
          <span role="columnheader">Действия</span>
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
            <span role="cell">
              <button className="icon-button" type="button" aria-label={`Изменить права роли ${role.name}`} title={`Изменить права роли ${role.name}`} onClick={() => onEditRole(role)}>
                <ShieldCheck size={16} />
              </button>
            </span>
          </div>
        ))}
      </div>
    </section>
  )
}

type ContractorTariffRow = {
  id: string
  backendTariffId?: string
  backendServiceSettingId?: string
  serviceSettingKind?: 'main' | 'periodicity' | 'start-date' | 'due-date' | 'overdue-days'
  title: string
  amount?: string
  dateDay?: string
  dateMonth?: string
  unit?: string
  threshold?: string
  byMeter: boolean
  tiered: boolean
  group?: string
  category: string
  calculationBase?: string
  effectiveFrom?: string
  electricityFirstThreshold?: number | null
  electricitySecondThreshold?: number | null
  isDeleted?: boolean
}

const contractorTariffRows: ContractorTariffRow[] = [
  { id: 'water-rate', group: 'Вода', category: 'Вода', title: 'Тариф на воду', amount: '', unit: 'руб.', byMeter: true, tiered: false, calculationBase: 'meter_water' },
  { id: 'water-overdue-days', category: 'Вода', title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: true, tiered: false },
  { id: 'waste-rate', group: 'Мусор', category: 'Мусор', title: 'Ставка за вывоз мусора', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'people' },
  { id: 'waste-overdue-days', category: 'Мусор', title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: false, tiered: false },
  { id: 'electricity-tier-0', group: 'Электроэнергия', category: 'Электроэнергия', title: 'От 0 кВт', threshold: 'x', amount: '', unit: 'руб.', byMeter: true, tiered: true, calculationBase: 'meter_electricity', electricityFirstThreshold: 1, electricitySecondThreshold: 3 },
  { id: 'electricity-tier-1', category: 'Электроэнергия', title: 'От 1 кВт', threshold: 'x', amount: '', unit: 'руб.', byMeter: true, tiered: true, calculationBase: 'meter_electricity', electricityFirstThreshold: 1, electricitySecondThreshold: 3 },
  { id: 'electricity-tier-3', category: 'Электроэнергия', title: 'От 3 кВт', threshold: 'x', amount: '', unit: 'руб.', byMeter: true, tiered: true, calculationBase: 'meter_electricity', electricityFirstThreshold: 1, electricitySecondThreshold: 3 },
  { id: 'electricity-overdue-days', category: 'Электроэнергия', title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: true, tiered: true },
  { id: 'membership-fee', group: 'Членский взнос', category: 'Членский взнос', title: 'Сумма членского взноса', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'fixed' },
  { id: 'membership-due-date', category: 'Членский взнос', title: 'Оплата до', dateDay: '30', dateMonth: 'июн', byMeter: false, tiered: false },
  { id: 'membership-start-date', category: 'Членский взнос', title: 'Учитывать платеж с', dateDay: '01', dateMonth: 'янв', byMeter: false, tiered: false },
  { id: 'membership-overdue-days', category: 'Членский взнос', title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: false, tiered: false },
  { id: 'target-fee', group: 'Целевой взнос', category: 'Целевой взнос', title: 'Сумма целевого взноса', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'fixed' },
  { id: 'target-due-date', category: 'Целевой взнос', title: 'Оплата за год до', dateDay: '30', dateMonth: 'июн', byMeter: false, tiered: false },
  { id: 'target-start-date', category: 'Целевой взнос', title: 'Учитывать платеж с', dateDay: '01', dateMonth: 'янв', byMeter: false, tiered: false },
  { id: 'target-overdue-days', category: 'Целевой взнос', title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: false, tiered: false },
  { id: 'lighting-due-date', group: 'Наружное освещение', category: 'Наружное освещение', title: 'Оплата за год до', dateDay: '31', dateMonth: 'дек', byMeter: false, tiered: false },
  { id: 'lighting-start-date', category: 'Наружное освещение', title: 'Учитывать платеж с', dateDay: '01', dateMonth: 'янв', byMeter: false, tiered: false },
  { id: 'lighting-overdue-days', category: 'Наружное освещение', title: 'Перенос долга в просроченный', amount: '0', unit: 'дн.', byMeter: false, tiered: false },
  { id: 'salary-electricians', group: 'Зарплатный фонд', category: 'Зарплатный фонд', title: 'Электрики', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'fixed' },
  { id: 'salary-accounting', category: 'Зарплатный фонд', title: 'Бухгалтерия', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'fixed' },
  { id: 'salary-management', category: 'Зарплатный фонд', title: 'Руководство', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'fixed' },
]

type ContractorOneTimeRow = {
  id: string
  backendPaymentId?: string
  name: string
  amount: string
  isActive: boolean
  isDeleted: boolean
  isUsed: boolean
}

type ContractorSection = 'garages' | 'suppliers' | 'staff'
type ContractorSortDirection = 'asc' | 'desc'
type ContractorSortableSection = ContractorSection
type ContractorDebtorFilterSection = 'garages' | 'suppliers'

type ContractorGarageRow = {
  id: string
  ownerId?: string | null
  number: string
  peopleCount: string
  floorCount: string
  owner: string
  phone: string
  address: string
  startingBalance?: string
  balance: string
  overdueDebt: string
  initialWater: string
  initialElectricity: string
  meters: string
  comment: string
  isDeleted: boolean
}

type ContractorSupplierRow = {
  id: string
  name: string
  service: string
  inn: string
  legalAddress: string
  contactPerson: string
  phone: string
  email: string
  contacts: ContractorSupplierContact[]
  debt: string
  comment: string
  isDeleted: boolean
}

type ContractorSupplierContact = {
  id: string
  fullName: string
  position: string
  phone: string
  email: string
  status: 'Работает' | 'Не работает'
  comment: string
  isDeleted: boolean
  deleteReason?: string
}

type ContractorStaffRow = {
  id: string
  fullName: string
  department: string
  rate: string
  isDeleted: boolean
}

type ContractorFinancialReportTarget =
  | { type: 'supplier'; row: ContractorSupplierRow }
  | { type: 'employee'; row: ContractorStaffRow }

type ContractorFinancialReportRow = {
  id: string
  accountingMonth: string
  date: string
  documentNumber: string
  description: string
  accrualAmount: number
  paymentAmount: number
  balanceAfter: number
}

type ContractorFinancialReport = {
  accrualTotal: number
  paymentTotal: number
  balance: number
  rows: ContractorFinancialReportRow[]
}

type ContractorDepartmentRow = {
  id: string
  name: string
  isDeleted?: boolean
}

type ContractorModal =
  | { type: 'garage'; item?: ContractorGarageRow }
  | { type: 'supplier'; item?: ContractorSupplierRow }
  | { type: 'service' }
  | { type: 'employee'; item?: ContractorStaffRow }
  | { type: 'department' }

type ContractorRestoreTarget =
  | { type: 'garage'; item: ContractorGarageRow }
  | { type: 'supplier'; item: ContractorSupplierRow }
  | { type: 'employee'; item: ContractorStaffRow }
  | { type: 'department'; item: ContractorDepartmentRow }

const contractorSectionLabels: Record<ContractorSection, string> = {
  garages: 'Гаражи',
  suppliers: 'Поставщики',
  staff: 'Персонал',
}

type ContractorGarageColumnKey = 'number' | 'peopleCount' | 'floorCount' | 'owner' | 'phone' | 'overdueDebt' | 'actions'
type ContractorSupplierSortKey = 'name' | 'service' | 'contactPerson' | 'phone' | 'email' | 'debt'
type ContractorStaffSortKey = 'fullName' | 'department' | 'rate'
type ContractorSupplierColumnKey = ContractorSupplierSortKey | 'actions'
type ContractorStaffColumnKey = ContractorStaffSortKey | 'actions'
type ContractorSortKey = Exclude<ContractorGarageColumnKey, 'actions'> | ContractorSupplierSortKey | ContractorStaffSortKey
type ContractorSortState = {
  section: ContractorSortableSection
  key: ContractorSortKey
  direction: ContractorSortDirection
}
type ContractorColumnDefinition<TKey extends string> = { key: TKey; label: string; defaultWidth: number; minWidth: number }

const contractorGarageColumnStorageKey = 'garagebalance.contractors.garageColumnWidths'
const contractorSupplierColumnStorageKey = 'garagebalance.contractors.supplierColumnWidths'
const contractorStaffColumnStorageKey = 'garagebalance.contractors.staffColumnWidths'
const contractorsDictionaryListLimit = 500
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const contractorGarageColumnDefinitions: Array<ContractorColumnDefinition<ContractorGarageColumnKey>> = [
  { key: 'number', label: 'Номер', defaultWidth: 96, minWidth: 72 },
  { key: 'peopleCount', label: 'Количество человек', defaultWidth: 170, minWidth: 132 },
  { key: 'floorCount', label: 'Количество этажей', defaultWidth: 170, minWidth: 132 },
  { key: 'owner', label: 'Владелец', defaultWidth: 260, minWidth: 160 },
  { key: 'phone', label: 'Телефон', defaultWidth: 220, minWidth: 150 },
  { key: 'overdueDebt', label: 'Просроченная задолженность', defaultWidth: 220, minWidth: 170 },
  { key: 'actions', label: 'Действия', defaultWidth: 132, minWidth: 112 },
]

const contractorSupplierColumnDefinitions: Array<ContractorColumnDefinition<ContractorSupplierColumnKey>> = [
  { key: 'name', label: 'Поставщик', defaultWidth: 180, minWidth: 140 },
  { key: 'service', label: 'Услуга', defaultWidth: 190, minWidth: 140 },
  { key: 'contactPerson', label: 'Контактное лицо', defaultWidth: 190, minWidth: 150 },
  { key: 'phone', label: 'Телефон', defaultWidth: 160, minWidth: 130 },
  { key: 'email', label: 'Почта', defaultWidth: 180, minWidth: 140 },
  { key: 'debt', label: 'Задолженность', defaultWidth: 150, minWidth: 130 },
  { key: 'actions', label: 'Действия', defaultWidth: 132, minWidth: 112 },
]

const contractorStaffColumnDefinitions: Array<ContractorColumnDefinition<ContractorStaffColumnKey>> = [
  { key: 'fullName', label: 'ФИО', defaultWidth: 260, minWidth: 180 },
  { key: 'department', label: 'Отдел', defaultWidth: 220, minWidth: 160 },
  { key: 'rate', label: 'Ставка', defaultWidth: 150, minWidth: 120 },
  { key: 'actions', label: 'Действия', defaultWidth: 132, minWidth: 112 },
]

function getDefaultContractorColumnWidths<TKey extends string>(definitions: Array<ContractorColumnDefinition<TKey>>) {
  return definitions.reduce<Record<TKey, number>>((widths, column) => {
    widths[column.key] = column.defaultWidth
    return widths
  }, {} as Record<TKey, number>)
}

function getSupplierServiceOptions(services: string[]) {
  return Array.from(new Set(services.map((service) => service.trim()).filter(Boolean))).sort((left, right) => left.localeCompare(right, 'ru'))
}

function getSupplierPrimaryContact(supplier: ContractorSupplierRow) {
  return supplier.contacts.find((contact) => !contact.isDeleted && contact.status === 'Работает') ?? supplier.contacts.find((contact) => !contact.isDeleted) ?? null
}

function normalizeSupplierPrototype(supplier: ContractorSupplierRow): ContractorSupplierRow {
  const primaryContact = getSupplierPrimaryContact(supplier)

  return {
    ...supplier,
    contactPerson: primaryContact?.fullName ?? '',
    phone: primaryContact?.phone ?? '',
    email: primaryContact?.email ?? '',
  }
}

function isBackendDictionaryId(id: string) {
  return guidPattern.test(id)
}

function formatPrototypeMoney(value: number | null | undefined) {
  if (!value) {
    return ''
  }

  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(value)
}

function parsePrototypeMoney(value: string) {
  const normalized = value.replace(/\s/g, '').replace(',', '.').replace(/[^\d.-]/g, '')
  const parsed = Number(normalized)
  return Number.isFinite(parsed) ? parsed : 0
}

function comparePrototypeText(left: string, right: string) {
  return left.localeCompare(right, 'ru', { numeric: true, sensitivity: 'base' })
}

function applyContractorSortDirection(value: number, direction: ContractorSortDirection) {
  return direction === 'asc' ? value : -value
}

function isContractorMoneyDebt(value: string) {
  return parsePrototypeMoney(value) > 0
}

function compareContractorGarages(left: ContractorGarageRow, right: ContractorGarageRow, key: Exclude<ContractorGarageColumnKey, 'actions'>) {
  if (key === 'peopleCount' || key === 'floorCount' || key === 'overdueDebt') {
    return parsePrototypeMoney(left[key]) - parsePrototypeMoney(right[key])
  }

  return comparePrototypeText(left[key], right[key])
}

function compareContractorSuppliers(left: ContractorSupplierRow, right: ContractorSupplierRow, key: ContractorSupplierSortKey) {
  if (key === 'debt') {
    return parsePrototypeMoney(left.debt) - parsePrototypeMoney(right.debt)
  }

  if (key === 'contactPerson' || key === 'phone' || key === 'email') {
    const leftContact = getSupplierPrimaryContact(left)
    const rightContact = getSupplierPrimaryContact(right)
    const leftValue = key === 'contactPerson' ? leftContact?.fullName ?? left.contactPerson : key === 'phone' ? leftContact?.phone ?? left.phone : leftContact?.email ?? left.email
    const rightValue = key === 'contactPerson' ? rightContact?.fullName ?? right.contactPerson : key === 'phone' ? rightContact?.phone ?? right.phone : rightContact?.email ?? right.email
    return comparePrototypeText(leftValue, rightValue)
  }

  return comparePrototypeText(left[key], right[key])
}

function compareContractorStaff(left: ContractorStaffRow, right: ContractorStaffRow, key: ContractorStaffSortKey) {
  if (key === 'rate') {
    return parsePrototypeMoney(left.rate) - parsePrototypeMoney(right.rate)
  }

  return comparePrototypeText(left[key], right[key])
}

function compareContractorReportEntries(
  left: Omit<ContractorFinancialReportRow, 'balanceAfter'>,
  right: Omit<ContractorFinancialReportRow, 'balanceAfter'>,
) {
  const monthComparison = left.accountingMonth.localeCompare(right.accountingMonth)
  if (monthComparison !== 0) {
    return monthComparison
  }

  const dateComparison = left.date.localeCompare(right.date)
  if (dateComparison !== 0) {
    return dateComparison
  }

  return left.description.localeCompare(right.description)
}

function buildContractorFinancialReport(entries: Array<Omit<ContractorFinancialReportRow, 'balanceAfter'>>): ContractorFinancialReport {
  let balance = 0
  let accrualTotal = 0
  let paymentTotal = 0
  const rows = [...entries].sort(compareContractorReportEntries).map((entry) => {
    accrualTotal += entry.accrualAmount
    paymentTotal += entry.paymentAmount
    balance += entry.accrualAmount - entry.paymentAmount

    return {
      ...entry,
      balanceAfter: balance,
    }
  })

  return {
    accrualTotal,
    paymentTotal,
    balance,
    rows,
  }
}

function getContractorReportMonthStarts(monthFrom: string, monthTo: string) {
  const [fromYear, fromMonth] = monthFrom.split('-').map(Number)
  const [toYear, toMonth] = monthTo.split('-').map(Number)
  if (!fromYear || !fromMonth || !toYear || !toMonth) {
    return []
  }

  const months: string[] = []
  const cursor = new Date(fromYear, fromMonth - 1, 1)
  const last = new Date(toYear, toMonth - 1, 1)
  while (cursor <= last) {
    months.push(`${cursor.getFullYear()}-${String(cursor.getMonth() + 1).padStart(2, '0')}-01`)
    cursor.setMonth(cursor.getMonth() + 1)
  }

  return months
}

function createStaffFinancialReportEntries(row: ContractorStaffRow, monthFrom: string, monthTo: string) {
  const rate = parsePrototypeMoney(row.rate)
  if (rate <= 0) {
    return []
  }

  return getContractorReportMonthStarts(monthFrom, monthTo).map((month) => ({
    id: `staff-accrual-${row.id}-${month}`,
    accountingMonth: month,
    date: month,
    documentNumber: '—',
    description: 'Начисление зарплаты',
    accrualAmount: rate,
    paymentAmount: 0,
  }))
}

function formatPrototypeNumber(value: number | null | undefined) {
  if (value === null || value === undefined) {
    return ''
  }

  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 4 }).format(value)
}

function parsePrototypeInteger(value: string, fallback = 0) {
  const parsed = Number.parseInt(value.trim(), 10)
  return Number.isFinite(parsed) ? parsed : fallback
}

function parsePrototypeNullableNumber(value: string) {
  const normalized = value.replace(/\s/g, '').replace(',', '.')
  if (!normalized) {
    return null
  }

  const parsed = Number(normalized)
  return Number.isFinite(parsed) ? parsed : null
}

function normalizeOwnerName(value: string) {
  return value.trim().replace(/\s+/g, ' ')
}

function splitOwnerName(value: string) {
  const [lastName = '', firstName = '', ...middleNameParts] = normalizeOwnerName(value).split(' ')
  return {
    lastName,
    firstName: firstName || 'Без имени',
    middleName: middleNameParts.join(' '),
  }
}

function createOwnerRequestFromGarage(row: ContractorGarageRow): UpsertOwnerRequest {
  const parsedName = splitOwnerName(row.owner)

  return {
    lastName: parsedName.lastName || 'Без фамилии',
    firstName: parsedName.firstName,
    middleName: parsedName.middleName,
    phone: row.phone.trim(),
    address: row.address.trim(),
  }
}

function createGarageRowFromDto(garage: GarageDto, owners: OwnerDto[]): ContractorGarageRow {
  const owner = garage.ownerId ? owners.find((item) => item.id === garage.ownerId) : null
  const balance = garage.balance ?? garage.startingBalance ?? 0
  const overdueDebt = garage.overdueDebt ?? Math.max(balance, 0)

  return {
    id: garage.id,
    ownerId: garage.ownerId,
    number: garage.number,
    peopleCount: String(garage.peopleCount),
    floorCount: String(garage.floorCount),
    owner: garage.ownerName ?? owner?.fullName ?? '',
    phone: owner?.phone ?? '',
    address: owner?.address ?? '',
    startingBalance: formatPrototypeMoney(garage.startingBalance),
    balance: formatPrototypeMoney(balance),
    overdueDebt: overdueDebt > 0 ? `${formatPrototypeMoney(overdueDebt)} руб.` : '',
    initialWater: formatPrototypeNumber(garage.initialWaterMeterValue),
    initialElectricity: formatPrototypeNumber(garage.initialElectricityMeterValue),
    meters: owner?.meterNotes ?? '',
    comment: garage.comment ?? '',
    isDeleted: garage.isArchived,
  }
}

function createGarageRequestFromRow(row: ContractorGarageRow, ownerId: string | null): UpsertGarageRequest {
  return {
    number: row.number.trim(),
    peopleCount: parsePrototypeInteger(row.peopleCount, 0),
    floorCount: parsePrototypeInteger(row.floorCount, 0),
    ownerId,
    startingBalance: parsePrototypeMoney(row.startingBalance ?? row.balance),
    initialWaterMeterValue: parsePrototypeNullableNumber(row.initialWater),
    initialElectricityMeterValue: parsePrototypeNullableNumber(row.initialElectricity),
    comment: row.comment.trim(),
  }
}

async function resolveGarageOwner(
  dictionaryClient: DictionaryClient,
  accessToken: string,
  owners: OwnerDto[],
  row: ContractorGarageRow,
) {
  const ownerName = normalizeOwnerName(row.owner)
  if (!ownerName) {
    return null
  }

  const existing = owners.find((owner) => owner.id === row.ownerId)
    ?? owners.find((owner) => normalizeOwnerName(owner.fullName).localeCompare(ownerName, 'ru', { sensitivity: 'accent' }) === 0)
  const request = createOwnerRequestFromGarage(row)

  if (existing) {
    const shouldUpdate = existing.phone !== (request.phone || null) || existing.address !== (request.address || null) || normalizeOwnerName(existing.fullName) !== ownerName
    if (!shouldUpdate) {
      return existing
    }

    return dictionaryClient.updateOwner(accessToken, existing.id, request)
  }

  return dictionaryClient.createOwner(accessToken, request)
}

function createSupplierContactFromDto(contact: SupplierContactDto): ContractorSupplierContact {
  return {
    id: contact.id,
    fullName: contact.fullName,
    position: contact.position ?? '',
    phone: contact.phone ?? '',
    email: contact.email ?? '',
    status: contact.status === 'Не работает' ? 'Не работает' : 'Работает',
    comment: contact.comment ?? '',
    isDeleted: contact.isArchived,
  }
}

function createSupplierRowFromDto(supplier: SupplierDto, contacts: SupplierContactDto[]): ContractorSupplierRow {
  const supplierContacts = contacts.filter((contact) => contact.supplierId === supplier.id).map(createSupplierContactFromDto)

  return normalizeSupplierPrototype({
    id: supplier.id,
    name: supplier.name,
    service: supplier.groupName,
    inn: supplier.inn ?? '',
    legalAddress: supplier.legalAddress ?? '',
    contactPerson: supplier.contactPerson ?? '',
    phone: supplier.phone ?? '',
    email: supplier.email ?? '',
    contacts: supplierContacts,
    debt: formatPrototypeMoney(supplier.startingBalance),
    comment: supplier.comment ?? '',
    isDeleted: supplier.isArchived,
  })
}

function createStaffDepartmentRowFromDto(department: StaffDepartmentDto): ContractorDepartmentRow {
  return {
    id: department.id,
    name: department.name,
    isDeleted: department.isArchived,
  }
}

function createStaffRowFromDto(member: StaffMemberDto): ContractorStaffRow {
  return {
    id: member.id,
    fullName: member.fullName,
    department: member.departmentName,
    rate: formatPrototypeMoney(member.rate),
    isDeleted: member.isArchived,
  }
}

async function resolveSupplierGroup(
  dictionaryClient: DictionaryClient,
  accessToken: string,
  groups: SupplierGroupDto[],
  serviceName: string,
) {
  const normalizedName = serviceName.trim() || 'Прочее'
  const existing = groups.find((group) => !group.isArchived && group.name.localeCompare(normalizedName, 'ru', { sensitivity: 'accent' }) === 0)
  if (existing) {
    return existing
  }

  const created = await dictionaryClient.createSupplierGroup(accessToken, { name: normalizedName })
  groups.push(created)
  return created
}

function createSupplierRequestFromRow(row: ContractorSupplierRow, groupId: string): UpsertSupplierRequest {
  const normalized = normalizeSupplierPrototype(row)
  return {
    name: normalized.name.trim(),
    groupId,
    inn: normalized.inn.trim(),
    legalAddress: normalized.legalAddress.trim(),
    contactPerson: normalized.contactPerson.trim(),
    phone: normalized.phone.trim(),
    email: normalized.email.trim(),
    startingBalance: parsePrototypeMoney(normalized.debt),
    comment: normalized.comment.trim(),
  }
}

function createSupplierContactRequestFromRow(supplierId: string, contact: ContractorSupplierContact): UpsertSupplierContactRequest {
  return {
    supplierId,
    fullName: contact.fullName.trim(),
    position: contact.position.trim(),
    phone: contact.phone.trim(),
    email: contact.email.trim(),
    status: contact.status,
    comment: contact.comment.trim(),
  }
}

function createStaffMemberRequestFromRow(row: ContractorStaffRow, departmentId: string): UpsertStaffMemberRequest {
  return {
    fullName: row.fullName.trim(),
    departmentId,
    rate: parsePrototypeMoney(row.rate),
  }
}

function createEmptySupplierContact(): ContractorSupplierContact {
  return {
    id: `supplier-contact-${Date.now()}`,
    fullName: '',
    position: '',
    phone: '',
    email: '',
    status: 'Работает',
    comment: '',
    isDeleted: false,
  }
}

function formatSupplierContactSummary(contacts: ContractorSupplierContact[]) {
  if (contacts.length === 0) {
    return ''
  }

  return contacts
    .map((contact, index) => {
      const state = contact.isDeleted ? 'удален' : contact.status
      return `${index + 1}. ${contact.fullName || 'Без ФИО'} / ${contact.position || 'Без должности'} / ${contact.phone || 'Без телефона'} / ${contact.email || 'Без почты'} / ${state} / ${contact.comment || 'Без комментария'}`
    })
    .join('; ')
}

function loadContractorColumnWidths<TKey extends string>(storageKey: string, definitions: Array<ContractorColumnDefinition<TKey>>) {
  const defaults = getDefaultContractorColumnWidths(definitions)

  try {
    const rawValue = window.localStorage.getItem(storageKey)
    if (!rawValue) {
      return defaults
    }

    const parsed = JSON.parse(rawValue) as Partial<Record<TKey, number>>
    return definitions.reduce<Record<TKey, number>>((widths, column) => {
      const value = parsed[column.key]
      widths[column.key] = typeof value === 'number' && Number.isFinite(value) ? Math.max(column.minWidth, value) : defaults[column.key]
      return widths
    }, {} as Record<TKey, number>)
  } catch {
    return defaults
  }
}

function saveContractorColumnWidths<TKey extends string>(storageKey: string, widths: Record<TKey, number>) {
  try {
    window.localStorage.setItem(storageKey, JSON.stringify(widths))
  } catch {
    // Column widths are a UI preference; the table must work if localStorage is unavailable.
  }
}

function loadGarageColumnWidths() {
  return loadContractorColumnWidths(contractorGarageColumnStorageKey, contractorGarageColumnDefinitions)
}

function startContractorColumnResize<TKey extends string>(
  definitions: Array<ContractorColumnDefinition<TKey>>,
  widths: Record<TKey, number>,
  setWidths: (updater: (currentWidths: Record<TKey, number>) => Record<TKey, number>) => void,
  columnKey: TKey,
  event: MouseEvent<HTMLButtonElement>,
) {
  event.preventDefault()
  event.stopPropagation()
  const column = definitions.find((item) => item.key === columnKey)
  if (!column) {
    return
  }

  const startX = event.clientX
  const startWidth = widths[columnKey]
  const handleMouseMove = (moveEvent: globalThis.MouseEvent) => {
    const nextWidth = Math.max(column.minWidth, startWidth + moveEvent.clientX - startX)
    setWidths((currentWidths) => ({ ...currentWidths, [columnKey]: nextWidth }))
  }
  const handleMouseUp = () => {
    document.removeEventListener('mousemove', handleMouseMove)
    document.removeEventListener('mouseup', handleMouseUp)
  }

  document.addEventListener('mousemove', handleMouseMove)
  document.addEventListener('mouseup', handleMouseUp)
}

function getContractorRestoreTitle(target: ContractorRestoreTarget) {
  if (target.type === 'garage') {
    return `Гараж ${target.item.number || 'без номера'}`
  }

  if (target.type === 'supplier') {
    return target.item.name || 'Поставщик без названия'
  }

  if (target.type === 'department') {
    return `Отдел ${target.item.name || 'без названия'}`
  }

  return target.item.fullName || 'Сотрудник без имени'
}

type ContractorsPrototypeSavedState = {
  garages: ContractorGarageRow[]
  suppliers: ContractorSupplierRow[]
  staff: ContractorStaffRow[]
  departments: ContractorDepartmentRow[]
  supplierServices: string[]
}

function ContractorsPrototypePanel({ auth, auditClient, dictionaryClient, financeClient, formStateClient }: { auth: AuthResponse; auditClient: AuditClient; dictionaryClient: DictionaryClient; financeClient: FinanceClient; formStateClient: FormStateClient }) {
  const [activeSection, setActiveSection] = useState<ContractorSection>('garages')
  const [debtorFilters, setDebtorFilters] = useState<Record<ContractorDebtorFilterSection, boolean>>({ garages: false, suppliers: false })
  const [contractorSort, setContractorSort] = useState<ContractorSortState>({ section: 'garages', key: 'number', direction: 'asc' })
  const [garages, setGarages] = useState<ContractorGarageRow[]>([])
  const [owners, setOwners] = useState<OwnerDto[]>([])
  const [suppliers, setSuppliers] = useState<ContractorSupplierRow[]>([])
  const [staff, setStaff] = useState<ContractorStaffRow[]>([])
  const [departments, setDepartments] = useState<ContractorDepartmentRow[]>([])
  const [supplierGroups, setSupplierGroups] = useState<SupplierGroupDto[]>([])
  const [supplierServices, setSupplierServices] = useState<string[]>([])
  const [formStateLoaded, setFormStateLoaded] = useState(false)
  const [formStateError, setFormStateError] = useState<string | null>(null)
  const [modal, setModal] = useState<ContractorModal | null>(null)
  const [restoreTarget, setRestoreTarget] = useState<ContractorRestoreTarget | null>(null)
  const [garageColumnWidths, setGarageColumnWidths] = useState(loadGarageColumnWidths)
  const [supplierColumnWidths, setSupplierColumnWidths] = useState(() => loadContractorColumnWidths(contractorSupplierColumnStorageKey, contractorSupplierColumnDefinitions))
  const [staffColumnWidths, setStaffColumnWidths] = useState(() => loadContractorColumnWidths(contractorStaffColumnStorageKey, contractorStaffColumnDefinitions))
  const [garageContextMenu, setGarageContextMenu] = useState<{ row: ContractorGarageRow; x: number; y: number } | null>(null)
  const [garageDeleteTarget, setGarageDeleteTarget] = useState<ContractorGarageRow | null>(null)
  const [garageDeleteReason, setGarageDeleteReason] = useState('')
  const [garageFinancialReportTarget, setGarageFinancialReportTarget] = useState<ContractorGarageRow | null>(null)
  const [garageFinancialReport, setGarageFinancialReport] = useState<GarageBalanceHistoryDto | null>(null)
  const [garageFinancialReportFilters, setGarageFinancialReportFilters] = useState(() => createDefaultGarageBalanceHistoryFilters())
  const [garageFinancialReportLoading, setGarageFinancialReportLoading] = useState(false)
  const [garageFinancialReportError, setGarageFinancialReportError] = useState<string | null>(null)
  const [contractorFinancialReportTarget, setContractorFinancialReportTarget] = useState<ContractorFinancialReportTarget | null>(null)
  const [contractorFinancialReport, setContractorFinancialReport] = useState<ContractorFinancialReport | null>(null)
  const [contractorFinancialReportFilters, setContractorFinancialReportFilters] = useState(() => createDefaultGarageBalanceHistoryFilters())
  const [contractorFinancialReportLoading, setContractorFinancialReportLoading] = useState(false)
  const [contractorFinancialReportError, setContractorFinancialReportError] = useState<string | null>(null)
  const [contractorHistoryEvents, setContractorHistoryEvents] = useState<AuditEventDto[]>([])
  const [contractorHistoryLoading, setContractorHistoryLoading] = useState(false)
  const [contractorHistoryError, setContractorHistoryError] = useState<string | null>(null)
  const [supplierContextMenu, setSupplierContextMenu] = useState<{ row: ContractorSupplierRow; x: number; y: number } | null>(null)
  const [supplierDeleteTarget, setSupplierDeleteTarget] = useState<ContractorSupplierRow | null>(null)
  const [supplierDeleteReason, setSupplierDeleteReason] = useState('')
  const [employeeContextMenu, setEmployeeContextMenu] = useState<{ row: ContractorStaffRow; x: number; y: number } | null>(null)
  const [employeeDeleteTarget, setEmployeeDeleteTarget] = useState<ContractorStaffRow | null>(null)
  const [employeeDeleteReason, setEmployeeDeleteReason] = useState('')
  const [departmentDeleteTarget, setDepartmentDeleteTarget] = useState<ContractorDepartmentRow | null>(null)
  const [departmentDeleteReason, setDepartmentDeleteReason] = useState('')
  useRestoreFocusOnClose(Boolean(restoreTarget))
  useRestoreFocusOnClose(Boolean(garageDeleteTarget))
  useRestoreFocusOnClose(Boolean(garageFinancialReportTarget))
  useRestoreFocusOnClose(Boolean(contractorFinancialReportTarget))
  useRestoreFocusOnClose(Boolean(supplierDeleteTarget))
  useRestoreFocusOnClose(Boolean(employeeDeleteTarget))
  useRestoreFocusOnClose(Boolean(departmentDeleteTarget))
  const restoreDialogRef = useFocusTrap<HTMLElement>(Boolean(restoreTarget))
  const restoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(restoreTarget))
  const garageDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(garageDeleteTarget))
  const garageDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(garageDeleteTarget))
  const garageFinancialReportDialogRef = useFocusTrap<HTMLElement>(Boolean(garageFinancialReportTarget))
  const garageFinancialReportCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(garageFinancialReportTarget))
  const contractorFinancialReportDialogRef = useFocusTrap<HTMLElement>(Boolean(contractorFinancialReportTarget))
  const contractorFinancialReportCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(contractorFinancialReportTarget))
  const supplierDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(supplierDeleteTarget))
  const supplierDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(supplierDeleteTarget))
  const employeeDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(employeeDeleteTarget))
  const employeeDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(employeeDeleteTarget))
  const departmentDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(departmentDeleteTarget))
  const departmentDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(departmentDeleteTarget))
  useEscapeKey(Boolean(restoreTarget), () => setRestoreTarget(null))
  useEscapeKey(Boolean(garageContextMenu), () => setGarageContextMenu(null))
  useEscapeKey(Boolean(garageDeleteTarget), () => closeGarageDeleteDialog())
  useEscapeKey(Boolean(garageFinancialReportTarget), () => closeGarageFinancialReport())
  useEscapeKey(Boolean(contractorFinancialReportTarget), () => closeContractorFinancialReport())
  useEscapeKey(Boolean(supplierContextMenu), () => setSupplierContextMenu(null))
  useEscapeKey(Boolean(supplierDeleteTarget), () => closeSupplierDeleteDialog())
  useEscapeKey(Boolean(employeeContextMenu), () => setEmployeeContextMenu(null))
  useEscapeKey(Boolean(employeeDeleteTarget), () => closeEmployeeDeleteDialog())
  useEscapeKey(Boolean(departmentDeleteTarget), () => closeDepartmentDeleteDialog())

  useEffect(() => {
    let cancelled = false
    formStateClient
      .getState<ContractorsPrototypeSavedState>(auth.accessToken, contractorsFormStateScope)
      .catch((error: unknown) => {
        if (!cancelled) {
          setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить сохраненное состояние контрагентов.')
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
  }, [auth.accessToken, formStateClient])

  useEffect(() => {
    let cancelled = false

    async function loadContractorsFromDictionaries() {
      try {
        const [ownerRows, garageRows, groups, supplierRows, supplierContactRows, departmentRows, staffRows] = await Promise.all([
          dictionaryClient.getOwners(auth.accessToken, undefined, contractorsDictionaryListLimit, true),
          dictionaryClient.getGarages(auth.accessToken, undefined, contractorsDictionaryListLimit, true),
          dictionaryClient.getSupplierGroups(auth.accessToken, undefined, contractorsDictionaryListLimit, true),
          dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, contractorsDictionaryListLimit, true),
          dictionaryClient.getSupplierContacts(auth.accessToken, undefined, undefined, contractorsDictionaryListLimit, true),
          dictionaryClient.getStaffDepartments(auth.accessToken, contractorsDictionaryListLimit, true),
          dictionaryClient.getStaffMembers(auth.accessToken, undefined, undefined, contractorsDictionaryListLimit, true),
        ])

        if (cancelled) {
          return
        }

        const nextSuppliers = supplierRows.map((supplier) => createSupplierRowFromDto(supplier, supplierContactRows))

        setOwners(ownerRows)
        setGarages(garageRows.map((garage) => createGarageRowFromDto(garage, ownerRows)))
        setSupplierGroups(groups)
        setSuppliers(nextSuppliers)
        setSupplierServices(getSupplierServiceOptions([...groups.map((group) => group.name), ...nextSuppliers.map((supplier) => supplier.service)]))
        setDepartments(departmentRows.map(createStaffDepartmentRowFromDto))
        setStaff(staffRows.map(createStaffRowFromDto))
      } catch (error) {
        if (!cancelled) {
          setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить контрагентов из справочников.')
        }
      }
    }

    void loadContractorsFromDictionaries()

    return () => {
      cancelled = true
    }
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => {
    if (!formStateLoaded) {
      return
    }

    const handle = window.setTimeout(() => {
      void formStateClient
        .saveState<ContractorsPrototypeSavedState>(auth.accessToken, contractorsFormStateScope, {
          payload: { garages, suppliers, staff, departments, supplierServices },
          summary: 'Сохранено состояние раздела контрагентов.'
        })
        .catch((error: unknown) => setFormStateError(error instanceof Error ? error.message : 'Не удалось сохранить состояние контрагентов.'))
    }, 400)

    return () => window.clearTimeout(handle)
  }, [auth.accessToken, departments, formStateClient, formStateLoaded, garages, staff, supplierServices, suppliers])

  useEffect(() => {
    saveContractorColumnWidths(contractorGarageColumnStorageKey, garageColumnWidths)
  }, [garageColumnWidths])

  useEffect(() => {
    saveContractorColumnWidths(contractorSupplierColumnStorageKey, supplierColumnWidths)
  }, [supplierColumnWidths])

  useEffect(() => {
    saveContractorColumnWidths(contractorStaffColumnStorageKey, staffColumnWidths)
  }, [staffColumnWidths])

  const garageTableStyle = useMemo(() => {
    return contractorGarageColumnDefinitions.reduce<CSSProperties>((style, column) => {
      return { ...style, [`--garage-col-${column.key}`]: `${garageColumnWidths[column.key]}px` }
    }, {})
  }, [garageColumnWidths])

  const supplierTableStyle = useMemo(() => {
    return contractorSupplierColumnDefinitions.reduce<CSSProperties>((style, column) => {
      return { ...style, [`--supplier-col-${column.key}`]: `${supplierColumnWidths[column.key]}px` }
    }, {})
  }, [supplierColumnWidths])

  const staffTableStyle = useMemo(() => {
    return contractorStaffColumnDefinitions.reduce<CSSProperties>((style, column) => {
      return { ...style, [`--staff-col-${column.key}`]: `${staffColumnWidths[column.key]}px` }
    }, {})
  }, [staffColumnWidths])
  const canReadContractorHistory = hasPermission(auth, permissions.auditRead)

  const resizeGarageColumn = (columnKey: ContractorGarageColumnKey, event: MouseEvent<HTMLButtonElement>) => {
    startContractorColumnResize(contractorGarageColumnDefinitions, garageColumnWidths, setGarageColumnWidths, columnKey, event)
  }

  const resizeSupplierColumn = (columnKey: ContractorSupplierColumnKey, event: MouseEvent<HTMLButtonElement>) => {
    startContractorColumnResize(contractorSupplierColumnDefinitions, supplierColumnWidths, setSupplierColumnWidths, columnKey, event)
  }

  const resizeStaffColumn = (columnKey: ContractorStaffColumnKey, event: MouseEvent<HTMLButtonElement>) => {
    startContractorColumnResize(contractorStaffColumnDefinitions, staffColumnWidths, setStaffColumnWidths, columnKey, event)
  }

  const saveGarage = async (garage: ContractorGarageRow) => {
    const currentGarage = garages.find((item) => item.id === garage.id)

    try {
      const savedOwner = await resolveGarageOwner(dictionaryClient, auth.accessToken, owners, garage)
      if (savedOwner) {
        setOwners((currentOwners) => {
          if (currentOwners.some((owner) => owner.id === savedOwner.id)) {
            return currentOwners.map((owner) => (owner.id === savedOwner.id ? savedOwner : owner))
          }

          return [...currentOwners, savedOwner]
        })
      }

      const request = createGarageRequestFromRow(garage, savedOwner?.id ?? null)
      const savedGarage = isBackendDictionaryId(garage.id)
        ? await dictionaryClient.updateGarage(auth.accessToken, garage.id, request)
        : await dictionaryClient.createGarage(auth.accessToken, request)
      const nextGarage = createGarageRowFromDto(savedGarage, savedOwner ? [...owners.filter((owner) => owner.id !== savedOwner.id), savedOwner] : owners)

      setGarages((currentGarages) => {
        if (currentGarage) {
          return currentGarages.map((item) => (item.id === garage.id ? nextGarage : item))
        }

        return [...currentGarages, nextGarage]
      })
      return
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось сохранить гараж.')
      return
    }
  }

  const deleteGarage = async (garage: ContractorGarageRow, reason = 'Гараж удален из таблицы контрагентов.') => {
    try {
      if (isBackendDictionaryId(garage.id)) {
        await dictionaryClient.archiveGarage(auth.accessToken, garage.id, reason)
      }
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось удалить гараж.')
      return
    }

    setGarages((currentGarages) => currentGarages.map((item) => (item.id === garage.id ? { ...item, isDeleted: true } : item)))
  }

  function openGarageContextMenu(event: MouseEvent<HTMLDivElement>, row: ContractorGarageRow) {
    event.preventDefault()
    setGarageContextMenu({ row, x: event.clientX, y: event.clientY })
  }

  function openGarageEditor(row: ContractorGarageRow) {
    setGarageContextMenu(null)
    setModal({ type: 'garage', item: row })
  }

  function openGarageDeleteDialog(row: ContractorGarageRow) {
    setGarageContextMenu(null)
    setGarageDeleteTarget(row)
    setGarageDeleteReason('')
  }

  function closeGarageDeleteDialog() {
    setGarageDeleteTarget(null)
    setGarageDeleteReason('')
  }

  function confirmGarageDeleteFromTable() {
    if (!garageDeleteTarget || !garageDeleteReason.trim()) {
      return
    }

    void deleteGarage(garageDeleteTarget, garageDeleteReason.trim())
    closeGarageDeleteDialog()
  }

  function restoreGarage(row: ContractorGarageRow) {
    setGarageContextMenu(null)
    setRestoreTarget({ type: 'garage', item: row })
  }

  async function loadGarageFinancialReport(row = garageFinancialReportTarget, filters = garageFinancialReportFilters) {
    if (!row) {
      return
    }

    if (!isBackendDictionaryId(row.id)) {
      setGarageFinancialReport(null)
      setGarageFinancialReportError('Финансовый отчет доступен для гаража, сохраненного в справочнике.')
      return
    }

    setGarageFinancialReportLoading(true)
    setGarageFinancialReportError(null)

    try {
      const report = await financeClient.getGarageBalanceHistory(auth.accessToken, row.id, filters)
      setGarageFinancialReport(report)
    } catch (error) {
      setGarageFinancialReportError(error instanceof Error ? error.message : 'Не удалось загрузить финансовый отчет гаража.')
      setGarageFinancialReport(null)
    } finally {
      setGarageFinancialReportLoading(false)
    }
  }

  function openGarageFinancialReport(row: ContractorGarageRow) {
    setGarageContextMenu(null)
    const filters = createDefaultGarageBalanceHistoryFilters()
    setGarageFinancialReportTarget(row)
    setGarageFinancialReportFilters(filters)
    setGarageFinancialReport(null)
    setGarageFinancialReportError(null)
    void loadGarageFinancialReport(row, filters)
  }

  function closeGarageFinancialReport() {
    setGarageFinancialReportTarget(null)
    setGarageFinancialReport(null)
    setGarageFinancialReportError(null)
    setGarageFinancialReportLoading(false)
  }

  async function loadContractorFinancialReport(target = contractorFinancialReportTarget, filters = contractorFinancialReportFilters) {
    if (!target) {
      return
    }

    if (!isBackendDictionaryId(target.row.id)) {
      setContractorFinancialReport(null)
      setContractorFinancialReportError('Финансовый отчет доступен для записи, сохраненной в справочнике.')
      return
    }

    setContractorFinancialReportLoading(true)
    setContractorFinancialReportError(null)

    try {
      const operationsPage = await financeClient.getOperationsPage(auth.accessToken, {
        monthFrom: filters.monthFrom,
        monthTo: filters.monthTo,
        operationKind: 'expense',
        supplierId: target.type === 'supplier' ? target.row.id : undefined,
        staffMemberId: target.type === 'employee' ? target.row.id : undefined,
        limit: 500,
      })
      const operationEntries = operationsPage.items
        .filter((operation) => !operation.isCanceled)
        .map((operation) => ({
          id: `operation-${operation.id}`,
          accountingMonth: operation.accountingMonth,
          date: operation.operationDate,
          documentNumber: operation.documentNumber ?? '—',
          description: target.type === 'supplier'
            ? operation.expenseTypeName ?? operation.comment ?? 'Выплата поставщику'
            : operation.expenseTypeName ?? operation.comment ?? 'Выплата сотруднику',
          accrualAmount: 0,
          paymentAmount: operation.amount,
        }))

      if (target.type === 'supplier') {
        const accrualsPage = await financeClient.getSupplierAccrualsPage(auth.accessToken, {
          monthFrom: filters.monthFrom,
          monthTo: filters.monthTo,
          supplierId: target.row.id,
          limit: 500,
        })
        const accrualEntries = accrualsPage.items
          .filter((accrual) => !accrual.isCanceled)
          .map((accrual) => ({
            id: `supplier-accrual-${accrual.id}`,
            accountingMonth: accrual.accountingMonth,
            date: accrual.accountingMonth,
            documentNumber: accrual.documentNumber ?? '—',
            description: accrual.expenseTypeName,
            accrualAmount: accrual.amount,
            paymentAmount: 0,
          }))
        setContractorFinancialReport(buildContractorFinancialReport([...accrualEntries, ...operationEntries]))
      } else {
        const staffAccrualEntries = createStaffFinancialReportEntries(target.row, filters.monthFrom, filters.monthTo)
        setContractorFinancialReport(buildContractorFinancialReport([...staffAccrualEntries, ...operationEntries]))
      }
    } catch (error) {
      setContractorFinancialReportError(error instanceof Error ? error.message : 'Не удалось загрузить финансовый отчет контрагента.')
      setContractorFinancialReport(null)
    } finally {
      setContractorFinancialReportLoading(false)
    }
  }

  async function loadContractorHistory(target = contractorFinancialReportTarget) {
    if (!target) {
      return
    }

    if (!isBackendDictionaryId(target.row.id)) {
      setContractorHistoryEvents([])
      setContractorHistoryError(null)
      return
    }

    if (!canReadContractorHistory) {
      setContractorHistoryEvents([])
      setContractorHistoryError(null)
      return
    }

    setContractorHistoryLoading(true)
    setContractorHistoryError(null)

    try {
      const events = await auditClient.getEvents(auth.accessToken, {
        relatedCounterparty: target.row.id,
        limit: 5,
      })
      setContractorHistoryEvents(events)
    } catch (error) {
      setContractorHistoryEvents([])
      setContractorHistoryError(error instanceof Error ? error.message : 'Не удалось загрузить историю изменений контрагента.')
    } finally {
      setContractorHistoryLoading(false)
    }
  }

  function openContractorFinancialReport(target: ContractorFinancialReportTarget) {
    setSupplierContextMenu(null)
    setEmployeeContextMenu(null)
    setModal(null)
    const filters = createDefaultGarageBalanceHistoryFilters()
    setContractorFinancialReportTarget(target)
    setContractorFinancialReportFilters(filters)
    setContractorFinancialReport(null)
    setContractorFinancialReportError(null)
    setContractorHistoryEvents([])
    setContractorHistoryError(null)
    void loadContractorFinancialReport(target, filters)
    void loadContractorHistory(target)
  }

  function closeContractorFinancialReport() {
    setContractorFinancialReportTarget(null)
    setContractorFinancialReport(null)
    setContractorFinancialReportError(null)
    setContractorFinancialReportLoading(false)
    setContractorHistoryEvents([])
    setContractorHistoryError(null)
    setContractorHistoryLoading(false)
  }

  const saveSupplier = async (supplier: ContractorSupplierRow) => {
    const normalizedSupplier = normalizeSupplierPrototype(supplier)
    const currentSupplier = suppliers.find((item) => item.id === normalizedSupplier.id)

    if (normalizedSupplier.service.trim()) {
      setSupplierServices((currentServices) => getSupplierServiceOptions([...currentServices, normalizedSupplier.service]))
    }

    try {
      const groups = [...supplierGroups]
      const group = await resolveSupplierGroup(dictionaryClient, auth.accessToken, groups, normalizedSupplier.service)
      const request = createSupplierRequestFromRow(normalizedSupplier, group.id)
      const savedSupplier = isBackendDictionaryId(normalizedSupplier.id)
        ? await dictionaryClient.updateSupplier(auth.accessToken, normalizedSupplier.id, request)
        : await dictionaryClient.createSupplier(auth.accessToken, request)
      const savedContacts: SupplierContactDto[] = []

      for (const contact of normalizedSupplier.contacts) {
        if (contact.isDeleted) {
          if (isBackendDictionaryId(contact.id)) {
            await dictionaryClient.archiveSupplierContact(auth.accessToken, contact.id, contact.deleteReason?.trim() || 'Контакт удален из карточки поставщика.')
          }

          savedContacts.push({
            id: contact.id,
            supplierId: savedSupplier.id,
            supplierName: savedSupplier.name,
            fullName: contact.fullName,
            position: contact.position || null,
            phone: contact.phone || null,
            email: contact.email || null,
            status: contact.status,
            comment: contact.comment || null,
            isArchived: true,
          })
          continue
        }

        if (!contact.fullName.trim()) {
          continue
        }

        const contactRequest = createSupplierContactRequestFromRow(savedSupplier.id, contact)
        const savedContact = isBackendDictionaryId(contact.id)
          ? await dictionaryClient.updateSupplierContact(auth.accessToken, contact.id, contactRequest)
          : await dictionaryClient.createSupplierContact(auth.accessToken, contactRequest)
        savedContacts.push(savedContact)
      }

      const nextSupplier = createSupplierRowFromDto(savedSupplier, savedContacts)
      setSupplierGroups(groups)
      setSuppliers((currentSuppliers) => {
        if (currentSupplier) {
          return currentSuppliers.map((item) => (item.id === normalizedSupplier.id ? nextSupplier : item))
        }

        return [...currentSuppliers, nextSupplier]
      })
      return
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось сохранить поставщика.')
    }

    if (currentSupplier) {
      setSuppliers((currentSuppliers) => currentSuppliers.map((item) => (item.id === normalizedSupplier.id ? normalizedSupplier : item)))
      return
    }

    setSuppliers((currentSuppliers) => [...currentSuppliers, normalizedSupplier])
  }

  const deleteSupplier = async (supplier: ContractorSupplierRow, reason = 'Поставщик удален из таблицы контрагентов.') => {
    try {
      if (isBackendDictionaryId(supplier.id)) {
        await dictionaryClient.archiveSupplier(auth.accessToken, supplier.id, reason)
      }
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось удалить поставщика.')
      return
    }

    setSuppliers((currentSuppliers) => currentSuppliers.map((item) => (item.id === supplier.id ? { ...item, isDeleted: true } : item)))
  }

  function openSupplierContextMenu(event: MouseEvent<HTMLDivElement>, row: ContractorSupplierRow) {
    event.preventDefault()
    setSupplierContextMenu({ row, x: event.clientX, y: event.clientY })
  }

  function openSupplierEditor(row: ContractorSupplierRow) {
    setSupplierContextMenu(null)
    setModal({ type: 'supplier', item: row })
  }

  function openSupplierDeleteDialog(row: ContractorSupplierRow) {
    setSupplierContextMenu(null)
    setSupplierDeleteTarget(row)
    setSupplierDeleteReason('')
  }

  function closeSupplierDeleteDialog() {
    setSupplierDeleteTarget(null)
    setSupplierDeleteReason('')
  }

  function confirmSupplierDeleteFromTable() {
    if (!supplierDeleteTarget || !supplierDeleteReason.trim()) {
      return
    }

    void deleteSupplier(supplierDeleteTarget, supplierDeleteReason.trim())
    closeSupplierDeleteDialog()
  }

  function restoreSupplier(row: ContractorSupplierRow) {
    setSupplierContextMenu(null)
    setRestoreTarget({ type: 'supplier', item: row })
  }

  function openSupplierFinancialReport(row: ContractorSupplierRow) {
    openContractorFinancialReport({ type: 'supplier', row })
  }

  const saveEmployee = async (employee: ContractorStaffRow) => {
    const currentEmployee = staff.find((item) => item.id === employee.id)

    try {
      const department = departments.find((item) => item.name === employee.department)
      let departmentId = department?.id
      if (!departmentId || !isBackendDictionaryId(departmentId)) {
        const savedDepartment = await dictionaryClient.createStaffDepartment(auth.accessToken, { name: employee.department.trim() || 'Без отдела' })
        departmentId = savedDepartment.id
        setDepartments((currentDepartments) => {
          const withoutLocal = department ? currentDepartments.filter((item) => item.id !== department.id) : currentDepartments
          return [...withoutLocal, createStaffDepartmentRowFromDto(savedDepartment)]
        })
      }

      const request = createStaffMemberRequestFromRow(employee, departmentId)
      const savedEmployee = isBackendDictionaryId(employee.id)
        ? await dictionaryClient.updateStaffMember(auth.accessToken, employee.id, request)
        : await dictionaryClient.createStaffMember(auth.accessToken, request)
      const nextEmployee = createStaffRowFromDto(savedEmployee)

      setStaff((currentStaff) => {
        if (currentEmployee) {
          return currentStaff.map((item) => (item.id === employee.id ? nextEmployee : item))
        }

        return [...currentStaff, nextEmployee]
      })
      return
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось сохранить сотрудника.')
    }

    if (currentEmployee) {
      setStaff((currentStaff) => currentStaff.map((item) => (item.id === employee.id ? employee : item)))
      return
    }

    setStaff((currentStaff) => [...currentStaff, employee])
  }

  const deleteEmployee = async (employee: ContractorStaffRow, reason = 'Сотрудник удален из таблицы контрагентов.') => {
    try {
      if (isBackendDictionaryId(employee.id)) {
        await dictionaryClient.archiveStaffMember(auth.accessToken, employee.id, reason)
      }
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось удалить сотрудника.')
      return
    }

    setStaff((currentStaff) => currentStaff.map((item) => (item.id === employee.id ? { ...item, isDeleted: true } : item)))
  }

  const deleteDepartment = async (department: ContractorDepartmentRow, reason = 'Отдел удален из таблицы контрагентов.') => {
    try {
      if (isBackendDictionaryId(department.id)) {
        await dictionaryClient.archiveStaffDepartment(auth.accessToken, department.id, reason)
      }
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось удалить отдел.')
      return
    }

    setDepartments((currentDepartments) => currentDepartments.map((item) => (item.id === department.id ? { ...item, isDeleted: true } : item)))
  }

  function openEmployeeContextMenu(event: MouseEvent<HTMLDivElement>, row: ContractorStaffRow) {
    event.preventDefault()
    setEmployeeContextMenu({ row, x: event.clientX, y: event.clientY })
  }

  function openEmployeeEditor(row: ContractorStaffRow) {
    setEmployeeContextMenu(null)
    setModal({ type: 'employee', item: row })
  }

  function openEmployeeDeleteDialog(row: ContractorStaffRow) {
    setEmployeeContextMenu(null)
    setEmployeeDeleteTarget(row)
    setEmployeeDeleteReason('')
  }

  function closeEmployeeDeleteDialog() {
    setEmployeeDeleteTarget(null)
    setEmployeeDeleteReason('')
  }

  function confirmEmployeeDeleteFromTable() {
    if (!employeeDeleteTarget || !employeeDeleteReason.trim()) {
      return
    }

    void deleteEmployee(employeeDeleteTarget, employeeDeleteReason.trim())
    closeEmployeeDeleteDialog()
  }

  function openDepartmentDeleteDialog(row: ContractorDepartmentRow) {
    setDepartmentDeleteTarget(row)
    setDepartmentDeleteReason('')
  }

  function closeDepartmentDeleteDialog() {
    setDepartmentDeleteTarget(null)
    setDepartmentDeleteReason('')
  }

  function confirmDepartmentDeleteFromTable() {
    if (!departmentDeleteTarget || !departmentDeleteReason.trim()) {
      return
    }

    void deleteDepartment(departmentDeleteTarget, departmentDeleteReason.trim())
    closeDepartmentDeleteDialog()
  }

  function restoreEmployee(row: ContractorStaffRow) {
    setEmployeeContextMenu(null)
    setRestoreTarget({ type: 'employee', item: row })
  }

  function restoreDepartment(row: ContractorDepartmentRow) {
    setRestoreTarget({ type: 'department', item: row })
  }

  function openEmployeeFinancialReport(row: ContractorStaffRow) {
    openContractorFinancialReport({ type: 'employee', row })
  }

  const confirmRestore = async () => {
    if (!restoreTarget) {
      return
    }

    try {
      if (restoreTarget.type === 'garage') {
        if (isBackendDictionaryId(restoreTarget.item.id)) {
          const restoredGarage = await dictionaryClient.restoreGarage(auth.accessToken, restoreTarget.item.id)
          const nextGarage = createGarageRowFromDto(restoredGarage, owners)
          setGarages((currentGarages) => currentGarages.map((item) => (item.id === restoreTarget.item.id ? nextGarage : item)))
        } else {
          setGarages((currentGarages) => currentGarages.map((item) => (item.id === restoreTarget.item.id ? { ...item, isDeleted: false } : item)))
        }
      } else if (restoreTarget.type === 'supplier') {
        if (isBackendDictionaryId(restoreTarget.item.id)) {
          const restoredSupplier = await dictionaryClient.restoreSupplier(auth.accessToken, restoreTarget.item.id)
          const restoredContacts = await dictionaryClient.getSupplierContacts(auth.accessToken, restoredSupplier.id, undefined, contractorsDictionaryListLimit, true)
          const nextSupplier = createSupplierRowFromDto(restoredSupplier, restoredContacts)
          setSuppliers((currentSuppliers) => currentSuppliers.map((item) => (item.id === restoreTarget.item.id ? nextSupplier : item)))
        } else {
          setSuppliers((currentSuppliers) => currentSuppliers.map((item) => (item.id === restoreTarget.item.id ? { ...item, isDeleted: false } : item)))
        }
      } else if (restoreTarget.type === 'department') {
        if (isBackendDictionaryId(restoreTarget.item.id)) {
          const restoredDepartment = await dictionaryClient.restoreStaffDepartment(auth.accessToken, restoreTarget.item.id)
          setDepartments((currentDepartments) => currentDepartments.map((item) => (item.id === restoreTarget.item.id ? createStaffDepartmentRowFromDto(restoredDepartment) : item)))
        } else {
          setDepartments((currentDepartments) => currentDepartments.map((item) => (item.id === restoreTarget.item.id ? { ...item, isDeleted: false } : item)))
        }
      } else if (isBackendDictionaryId(restoreTarget.item.id)) {
        const restoredEmployee = await dictionaryClient.restoreStaffMember(auth.accessToken, restoreTarget.item.id)
        setStaff((currentStaff) => currentStaff.map((item) => (item.id === restoreTarget.item.id ? createStaffRowFromDto(restoredEmployee) : item)))
      } else {
        setStaff((currentStaff) => currentStaff.map((item) => (item.id === restoreTarget.item.id ? { ...item, isDeleted: false } : item)))
      }
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось восстановить запись.')
      return
    }

    setRestoreTarget(null)
  }

  const saveDepartment = async (departmentName: string) => {
    try {
      const savedDepartment = await dictionaryClient.createStaffDepartment(auth.accessToken, { name: departmentName.trim() })
      setDepartments((currentDepartments) => [...currentDepartments, createStaffDepartmentRowFromDto(savedDepartment)])
      return
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось сохранить отдел.')
    }

    const nextDepartment = { id: `department-${Date.now()}`, name: departmentName }
    setDepartments((currentDepartments) => [...currentDepartments, nextDepartment])
  }

  const saveService = (serviceName: string) => {
    setSupplierServices((currentServices) => getSupplierServiceOptions([...currentServices, serviceName]))
  }

  const changeContractorSort = (section: ContractorSortableSection, key: ContractorSortKey) => {
    setContractorSort((currentSort) => {
      if (currentSort.section === section && currentSort.key === key) {
        return {
          ...currentSort,
          direction: currentSort.direction === 'asc' ? 'desc' : 'asc',
        }
      }

      return { section, key, direction: 'asc' }
    })
  }

  const renderContractorSortHeader = (section: ContractorSortableSection, key: ContractorSortKey, label: string) => {
    const isActiveSort = contractorSort.section === section && contractorSort.key === key
    const indicator = isActiveSort ? (contractorSort.direction === 'asc' ? '↑' : '↓') : ''

    return (
      <button
        className="ghost-button contractors-sort-button"
        type="button"
        title={`Сортировать: ${label}`}
        aria-pressed={isActiveSort}
        onClick={() => changeContractorSort(section, key)}
      >
        <span>{label}</span>
        <span className="contractors-sort-indicator" aria-hidden="true">{indicator}</span>
      </button>
    )
  }

  const showGarageDebtorsOnly = debtorFilters.garages
  const showSupplierDebtorsOnly = debtorFilters.suppliers
  const showDebtorsOnly = activeSection === 'suppliers' ? showSupplierDebtorsOnly : activeSection === 'garages' ? showGarageDebtorsOnly : false
  const toggleDebtorsFilter = (section: ContractorDebtorFilterSection) => {
    setDebtorFilters((currentFilters) => ({ ...currentFilters, [section]: !currentFilters[section] }))
  }

  const filteredGarages = showGarageDebtorsOnly
    ? garages.filter((garage) => !garage.isDeleted && isContractorMoneyDebt(garage.overdueDebt))
    : garages
  const filteredSuppliers = showSupplierDebtorsOnly
    ? suppliers.filter((supplier) => !supplier.isDeleted && isContractorMoneyDebt(supplier.debt))
    : suppliers

  const visibleGarages = useMemo(() => {
    const rows = [...filteredGarages]
    if (contractorSort.section !== 'garages') {
      return rows
    }

    const sortKey = contractorSort.key as Exclude<ContractorGarageColumnKey, 'actions'>
    return rows.sort((left, right) => applyContractorSortDirection(compareContractorGarages(left, right, sortKey), contractorSort.direction))
  }, [filteredGarages, contractorSort])

  const visibleSuppliers = useMemo(() => {
    const rows = [...filteredSuppliers]
    if (contractorSort.section !== 'suppliers') {
      return rows
    }

    const sortKey = contractorSort.key as ContractorSupplierSortKey
    return rows.sort((left, right) => applyContractorSortDirection(compareContractorSuppliers(left, right, sortKey), contractorSort.direction))
  }, [filteredSuppliers, contractorSort])

  const visibleStaff = useMemo(() => {
    const rows = [...staff]
    if (contractorSort.section !== 'staff') {
      return rows
    }

    const sortKey = contractorSort.key as ContractorStaffSortKey
    return rows.sort((left, right) => applyContractorSortDirection(compareContractorStaff(left, right, sortKey), contractorSort.direction))
  }, [staff, contractorSort])
  const debtorsButtonLabel = activeSection === 'suppliers'
    ? showDebtorsOnly ? 'Показать всех поставщиков' : 'Показать должников'
    : showDebtorsOnly ? 'Показать все гаражи' : 'Показать должников'
  const contractorFinancialReportTitle = contractorFinancialReportTarget?.type === 'supplier'
    ? contractorFinancialReportTarget.row.name || 'Поставщик без названия'
    : contractorFinancialReportTarget?.row.fullName || 'Сотрудник без ФИО'
  const contractorFinancialReportDescription = contractorFinancialReportTarget?.type === 'supplier'
    ? contractorFinancialReportTarget.row.service || contractorFinancialReportTarget.row.contactPerson || 'Услуга не указана'
    : contractorFinancialReportTarget?.row.department || 'Отдел не указан'
  const contractorFinancialReportDialogTitleId = 'contractor-financial-report-title'
  const contractorFinancialReportDialogDescriptionId = 'contractor-financial-report-description'

  return (
    <section className="contractors-page contractors-page--directory" aria-label="Контрагенты">
      <div className="contractors-heading">
        <div>
          <h1>Контрагенты</h1>
        </div>
        <div className="contractors-actions">
          {activeSection === 'garages' ? (
            <>
              <button className="secondary-button" type="button" onClick={() => toggleDebtorsFilter('garages')}>{debtorsButtonLabel}</button>
              <button className="secondary-button" type="button" onClick={() => setModal({ type: 'garage' })}>Добавить гараж</button>
            </>
          ) : null}
          {activeSection === 'suppliers' ? (
            <>
              <button className="secondary-button" type="button" onClick={() => toggleDebtorsFilter('suppliers')}>{debtorsButtonLabel}</button>
              <button className="secondary-button" type="button" onClick={() => setModal({ type: 'supplier' })}>Добавить поставщика</button>
              <button className="secondary-button" type="button" onClick={() => setModal({ type: 'service' })}>Добавить услугу</button>
            </>
          ) : null}
          {activeSection === 'staff' ? (
            <>
              <button className="secondary-button" type="button" onClick={() => setModal({ type: 'department' })}>Добавить отдел</button>
              <button className="secondary-button" type="button" onClick={() => setModal({ type: 'employee' })}>Добавить сотрудника</button>
            </>
          ) : null}
        </div>
      </div>
      {formStateError ? <FormError>{formStateError}</FormError> : null}

      <div className="contractors-prototype-tabs" role="tablist" aria-label="Разделы контрагентов">
        {Object.entries(contractorSectionLabels).map(([section, label]) => (
          <button type="button" role="tab" aria-selected={activeSection === section} className={activeSection === section ? 'is-active' : ''} onClick={() => setActiveSection(section as ContractorSection)} key={section}>
            {label}
          </button>
        ))}
      </div>

      {activeSection === 'garages' ? (
        <section className="contractors-directory-card" aria-label="Гаражи">
          <div className="contractors-directory-table contractors-directory-table--garages" role="table" aria-label="Гаражи" style={garageTableStyle}>
            <div className="contractors-directory-row contractors-directory-row--header" role="row">
              {contractorGarageColumnDefinitions.map((column) => (
                <span className="contractors-directory-header-cell" role="columnheader" key={column.key}>
                  {column.key === 'actions' ? <span>{column.label}</span> : renderContractorSortHeader('garages', column.key, column.label)}
                  {column.key !== 'actions' ? (
                    <button
                      className="icon-button contractors-column-resizer"
                      type="button"
                      aria-label={`Изменить ширину столбца ${column.label}`}
                      onMouseDown={(event) => resizeGarageColumn(column.key, event)}
                    />
                  ) : null}
                </span>
              ))}
            </div>
            {visibleGarages.map((row) => (
              <div className={row.isDeleted ? 'contractors-directory-row contractors-directory-row--deleted' : 'contractors-directory-row'} role="row" key={row.id} onContextMenu={(event) => openGarageContextMenu(event, row)}>
                <span role="cell" className="contractors-directory-cell--center">{row.number}</span>
                <span role="cell" className="contractors-directory-cell--center">{row.peopleCount}</span>
                <span role="cell" className="contractors-directory-cell--center">{row.floorCount}</span>
                <span role="cell">{row.owner}</span>
                <span role="cell">{row.phone}</span>
                <span role="cell" className={row.overdueDebt ? 'contractors-directory-cell--center money-expense' : 'contractors-directory-cell--center'}>
                  {row.isDeleted ? 'Удален' : row.overdueDebt || 'Нет'}
                </span>
                <span role="cell" className="contractors-row-actions">
                  {row.isDeleted ? (
                    <button className="icon-button" type="button" aria-label={`Восстановить гараж ${row.number}`} title="Восстановить" onClick={() => restoreGarage(row)}>
                      <RotateCcw size={16} />
                    </button>
                  ) : (
                    <>
                      <button className="icon-button" type="button" aria-label={`Изменить гараж ${row.number}`} title="Изменить" onClick={() => openGarageEditor(row)}>
                        <Pencil size={16} />
                      </button>
                      <button className="icon-button" type="button" aria-label={`Открыть финансовый отчет гаража ${row.number}`} title="Финансовый отчет" onClick={() => openGarageFinancialReport(row)}>
                        <FileText size={16} />
                      </button>
                      <button className="icon-button contractors-delete-button" type="button" aria-label={`Удалить гараж ${row.number}`} title="Удалить" onClick={() => openGarageDeleteDialog(row)}>
                        <Trash2 size={16} />
                      </button>
                    </>
                  )}
                </span>
              </div>
            ))}
            {visibleGarages.length === 0 ? (
              <div className="contractors-directory-row contractors-directory-row--empty" role="row">
                <span className="contractors-directory-empty-cell" role="cell">{showGarageDebtorsOnly ? 'Гаражей с задолженностью не найдено.' : 'Гаражи пока не настроены.'}</span>
              </div>
            ) : null}
          </div>
        </section>
      ) : null}

      {activeSection === 'suppliers' ? (
        <section className="contractors-directory-card" aria-label="Поставщики">
          <div className="contractors-directory-table contractors-directory-table--suppliers" role="table" aria-label="Поставщики" style={supplierTableStyle}>
            <div className="contractors-directory-row contractors-directory-row--header" role="row">
              {contractorSupplierColumnDefinitions.map((column) => (
                <span className="contractors-directory-header-cell" role="columnheader" key={column.key}>
                  {column.key === 'actions' ? <span>{column.label}</span> : renderContractorSortHeader('suppliers', column.key, column.label)}
                  {column.key !== 'actions' ? (
                    <button
                      className="icon-button contractors-column-resizer"
                      type="button"
                      aria-label={`Изменить ширину столбца ${column.label}`}
                      onMouseDown={(event) => resizeSupplierColumn(column.key, event)}
                    />
                  ) : null}
                </span>
              ))}
            </div>
            {visibleSuppliers.map((row) => {
              const primaryContact = getSupplierPrimaryContact(row)
              return (
                <div className={row.isDeleted ? 'contractors-directory-row contractors-directory-row--deleted' : 'contractors-directory-row'} role="row" key={row.id} onContextMenu={(event) => openSupplierContextMenu(event, row)}>
                  <span role="cell">{row.name}</span>
                  <span role="cell">{row.service}</span>
                  <span role="cell">{primaryContact?.fullName ?? row.contactPerson}</span>
                  <span role="cell">{primaryContact?.phone ?? row.phone}</span>
                  <span role="cell">{primaryContact?.email ?? row.email}</span>
                  <span role="cell" className={row.debt ? 'contractors-directory-cell--center money-expense' : 'contractors-directory-cell--center'}>
                    {row.isDeleted ? 'Удален' : row.debt || 'Нет'}
                  </span>
                  <span role="cell" className="contractors-row-actions">
                    {row.isDeleted ? (
                      <button className="icon-button" type="button" aria-label={`Восстановить поставщика ${row.name}`} title="Восстановить" onClick={() => restoreSupplier(row)}>
                        <RotateCcw size={16} />
                      </button>
                    ) : (
                      <>
                        <button className="icon-button" type="button" aria-label={`Изменить поставщика ${row.name}`} title="Изменить" onClick={() => openSupplierEditor(row)}>
                          <Pencil size={16} />
                        </button>
                        <button className="icon-button" type="button" aria-label={`Открыть финансовый отчет поставщика ${row.name}`} title="Финансовый отчет" onClick={() => openSupplierFinancialReport(row)}>
                          <FileText size={16} />
                        </button>
                        <button className="icon-button contractors-delete-button" type="button" aria-label={`Удалить поставщика ${row.name}`} title="Удалить" onClick={() => openSupplierDeleteDialog(row)}>
                          <Trash2 size={16} />
                        </button>
                      </>
                    )}
                  </span>
                </div>
              )
            })}
            {visibleSuppliers.length === 0 ? (
              <div className="contractors-directory-row contractors-directory-row--empty" role="row">
                <span className="contractors-directory-empty-cell" role="cell">{showSupplierDebtorsOnly ? 'Поставщиков с задолженностью не найдено.' : 'Поставщики пока не настроены.'}</span>
              </div>
            ) : null}
          </div>
        </section>
      ) : null}

      {activeSection === 'staff' ? (
        <>
          <section className="contractors-directory-card" aria-label="Отделы персонала">
            <div className="contractors-directory-card-header">
              <h2>Отделы</h2>
            </div>
            <div className="contractors-directory-table contractors-directory-table--departments" role="table" aria-label="Отделы персонала">
              <div className="contractors-directory-row contractors-directory-row--header" role="row">
                <span className="contractors-directory-header-cell" role="columnheader">Отдел</span>
                <span className="contractors-directory-header-cell" role="columnheader">Статус</span>
                <span className="contractors-directory-header-cell" role="columnheader">Действия</span>
              </div>
              {departments.map((department) => (
                <div className={department.isDeleted ? 'contractors-directory-row contractors-directory-row--deleted' : 'contractors-directory-row'} role="row" key={department.id}>
                  <span role="cell">{department.name}</span>
                  <span role="cell" className="contractors-directory-cell--center">{department.isDeleted ? 'Удален' : 'Активен'}</span>
                  <span role="cell" className="contractors-row-actions">
                    {department.isDeleted ? (
                      <button className="icon-button" type="button" aria-label={`Восстановить отдел ${department.name}`} title="Восстановить" onClick={() => restoreDepartment(department)}>
                        <RotateCcw size={16} />
                      </button>
                    ) : (
                      <button className="icon-button contractors-delete-button" type="button" aria-label={`Удалить отдел ${department.name}`} title="Удалить" onClick={() => openDepartmentDeleteDialog(department)}>
                        <Trash2 size={16} />
                      </button>
                    )}
                  </span>
                </div>
              ))}
              {departments.length === 0 ? (
                <div className="contractors-directory-row contractors-directory-row--empty" role="row">
                  <span className="contractors-directory-empty-cell" role="cell">Отделы пока не настроены.</span>
                </div>
              ) : null}
            </div>
          </section>

          <section className="contractors-directory-card" aria-label="Персонал">
            <div className="contractors-directory-table contractors-directory-table--staff" role="table" aria-label="Персонал" style={staffTableStyle}>
              <div className="contractors-directory-row contractors-directory-row--header" role="row">
                {contractorStaffColumnDefinitions.map((column) => (
                  <span className="contractors-directory-header-cell" role="columnheader" key={column.key}>
                    {column.key === 'actions' ? <span>{column.label}</span> : renderContractorSortHeader('staff', column.key, column.label)}
                    {column.key !== 'actions' ? (
                      <button
                        className="icon-button contractors-column-resizer"
                        type="button"
                        aria-label={`Изменить ширину столбца ${column.label}`}
                        onMouseDown={(event) => resizeStaffColumn(column.key, event)}
                      />
                    ) : null}
                  </span>
                ))}
              </div>
              {visibleStaff.map((row) => (
                <div className={row.isDeleted ? 'contractors-directory-row contractors-directory-row--deleted' : 'contractors-directory-row'} role="row" key={row.id} onContextMenu={(event) => openEmployeeContextMenu(event, row)}>
                  <span role="cell">{row.fullName}</span>
                  <span role="cell">{row.department}</span>
                  <span role="cell">{row.isDeleted ? 'Удален' : row.rate}</span>
                  <span role="cell" className="contractors-row-actions">
                    {row.isDeleted ? (
                      <button className="icon-button" type="button" aria-label={`Восстановить сотрудника ${row.fullName}`} title="Восстановить" onClick={() => restoreEmployee(row)}>
                        <RotateCcw size={16} />
                      </button>
                    ) : (
                      <>
                        <button className="icon-button" type="button" aria-label={`Изменить сотрудника ${row.fullName}`} title="Изменить" onClick={() => openEmployeeEditor(row)}>
                          <Pencil size={16} />
                        </button>
                        <button className="icon-button" type="button" aria-label={`Открыть финансовый отчет сотрудника ${row.fullName}`} title="Финансовый отчет" onClick={() => openEmployeeFinancialReport(row)}>
                          <FileText size={16} />
                        </button>
                        <button className="icon-button contractors-delete-button" type="button" aria-label={`Удалить сотрудника ${row.fullName}`} title="Удалить" onClick={() => openEmployeeDeleteDialog(row)}>
                          <Trash2 size={16} />
                        </button>
                      </>
                    )}
                  </span>
                </div>
              ))}
              {visibleStaff.length === 0 ? (
                <div className="contractors-directory-row contractors-directory-row--empty" role="row">
                  <span className="contractors-directory-empty-cell" role="cell">Сотрудники пока не настроены.</span>
                </div>
              ) : null}
            </div>
          </section>
        </>
      ) : null}

      {garageContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setGarageContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия гаража ${garageContextMenu.row.number}`}
            style={{ left: garageContextMenu.x, top: garageContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {garageContextMenu.row.isDeleted ? (
              <button type="button" role="menuitem" onClick={() => restoreGarage(garageContextMenu.row)}>
                <RotateCcw size={16} />
                <span>Восстановить</span>
              </button>
            ) : (
              <>
                <button type="button" role="menuitem" onClick={() => openGarageEditor(garageContextMenu.row)}>
                  <Pencil size={16} />
                  <span>Изменить</span>
                </button>
                <button type="button" role="menuitem" onClick={() => openGarageFinancialReport(garageContextMenu.row)}>
                  <FileText size={16} />
                  <span>Открыть финансовый отчет</span>
                </button>
                <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openGarageDeleteDialog(garageContextMenu.row)}>
                  <Trash2 size={16} />
                  <span>Удалить</span>
                </button>
              </>
            )}
          </div>
        </div>
      ) : null}

      {supplierContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setSupplierContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия поставщика ${supplierContextMenu.row.name}`}
            style={{ left: supplierContextMenu.x, top: supplierContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {supplierContextMenu.row.isDeleted ? (
              <button type="button" role="menuitem" onClick={() => restoreSupplier(supplierContextMenu.row)}>
                <RotateCcw size={16} />
                <span>Восстановить</span>
              </button>
            ) : (
              <>
                <button type="button" role="menuitem" onClick={() => openSupplierEditor(supplierContextMenu.row)}>
                  <Pencil size={16} />
                  <span>Изменить</span>
                </button>
                <button type="button" role="menuitem" onClick={() => openSupplierFinancialReport(supplierContextMenu.row)}>
                  <FileText size={16} />
                  <span>Открыть финансовый отчет</span>
                </button>
                <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openSupplierDeleteDialog(supplierContextMenu.row)}>
                  <Trash2 size={16} />
                  <span>Удалить</span>
                </button>
              </>
            )}
          </div>
        </div>
      ) : null}

      {employeeContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setEmployeeContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия сотрудника ${employeeContextMenu.row.fullName}`}
            style={{ left: employeeContextMenu.x, top: employeeContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {employeeContextMenu.row.isDeleted ? (
              <button type="button" role="menuitem" onClick={() => restoreEmployee(employeeContextMenu.row)}>
                <RotateCcw size={16} />
                <span>Восстановить</span>
              </button>
            ) : (
              <>
                <button type="button" role="menuitem" onClick={() => openEmployeeEditor(employeeContextMenu.row)}>
                  <Pencil size={16} />
                  <span>Изменить</span>
                </button>
                <button type="button" role="menuitem" onClick={() => openEmployeeFinancialReport(employeeContextMenu.row)}>
                  <FileText size={16} />
                  <span>Открыть финансовый отчет</span>
                </button>
                <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openEmployeeDeleteDialog(employeeContextMenu.row)}>
                  <Trash2 size={16} />
                  <span>Удалить</span>
                </button>
              </>
            )}
          </div>
        </div>
      ) : null}

      {modal?.type === 'garage' ? <GaragePrototypeDialog item={modal.item} onClose={() => setModal(null)} onDelete={deleteGarage} onSave={saveGarage} onOpenFinancialReport={openGarageFinancialReport} /> : null}
      {modal?.type === 'supplier' ? <SupplierPrototypeDialog item={modal.item} services={supplierServices} onClose={() => setModal(null)} onOpenFinancialReport={openSupplierFinancialReport} onSave={saveSupplier} /> : null}
      {modal?.type === 'service' ? <ContractorServicePrototypeDialog onClose={() => setModal(null)} onSave={saveService} /> : null}
      {modal?.type === 'employee' ? <EmployeePrototypeDialog departments={departments} item={modal.item} onClose={() => setModal(null)} onOpenFinancialReport={openEmployeeFinancialReport} onSave={saveEmployee} /> : null}
      {modal?.type === 'department' ? <DepartmentPrototypeDialog onClose={() => setModal(null)} onSave={saveDepartment} /> : null}

      {garageFinancialReportTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeGarageFinancialReport}>
          <section ref={garageFinancialReportDialogRef} className="detail-dialog garage-balance-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-garage-report-title" aria-describedby="contractor-garage-report-owner" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Финансовый отчет</p>
                <h3 id="contractor-garage-report-title">Гараж {garageFinancialReportTarget.number || 'без номера'}</h3>
                <p id="contractor-garage-report-owner">{garageFinancialReportTarget.owner || 'Владелец не указан'}</p>
              </div>
              <button ref={garageFinancialReportCloseRef} className="icon-button" type="button" aria-label="Закрыть финансовый отчет гаража" onClick={closeGarageFinancialReport}>
                <X size={18} />
              </button>
            </div>
            <form className="balance-history-filters" onSubmit={(event) => {
              event.preventDefault()
              void loadGarageFinancialReport()
            }}>
              <label>
                Период с
                <input aria-label="Начало периода финансового отчета гаража" type="month" value={garageFinancialReportFilters.monthFrom} onChange={(event) => setGarageFinancialReportFilters((value) => ({ ...value, monthFrom: event.target.value }))} required />
              </label>
              <label>
                Период по
                <input aria-label="Конец периода финансового отчета гаража" type="month" value={garageFinancialReportFilters.monthTo} onChange={(event) => setGarageFinancialReportFilters((value) => ({ ...value, monthTo: event.target.value }))} required />
              </label>
              <button className="secondary-button" type="submit" disabled={garageFinancialReportLoading}>
                <Search size={16} />
                <span>{garageFinancialReportLoading ? 'Загружаем...' : 'Показать'}</span>
              </button>
            </form>
            {garageFinancialReportError ? <FormError>{garageFinancialReportError}</FormError> : null}
            {garageFinancialReport ? (
              <>
                <div className="balance-history-summary" aria-label="Итоги финансового отчета гаража">
                  <div>
                    <span>Старт</span>
                    <strong>{formatMoney(garageFinancialReport.startingBalance)}</strong>
                  </div>
                  <div>
                    <span>Начислено</span>
                    <strong>{formatMoney(garageFinancialReport.accrualTotal)}</strong>
                  </div>
                  <div>
                    <span>Поступило</span>
                    <strong>{formatMoney(garageFinancialReport.incomeTotal)}</strong>
                  </div>
                  <div>
                    <span>{formatDebtLabel(garageFinancialReport.debt)}</span>
                    <strong className={getDebtClassName(garageFinancialReport.debt)}>{formatDebtAmount(garageFinancialReport.debt)}</strong>
                  </div>
                </div>
                <div className="dictionary-table-scroll garage-balance-table-scroll">
                  <table className="dictionary-data-table" aria-label="Финансовый отчет гаража">
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
                      {garageFinancialReport.rows.map((row) => (
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
                  {garageFinancialReport.rows.length === 0 ? <p className="empty-state" role="status" aria-live="polite">По выбранному периоду строк нет</p> : null}
                </div>
              </>
            ) : null}
          </section>
        </div>
      ) : null}

      {contractorFinancialReportTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeContractorFinancialReport}>
          <section ref={contractorFinancialReportDialogRef} className="detail-dialog garage-balance-dialog" role="dialog" aria-modal="true" aria-labelledby={contractorFinancialReportDialogTitleId} aria-describedby={contractorFinancialReportDialogDescriptionId} onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Финансовый отчет</p>
                <h3 id={contractorFinancialReportDialogTitleId}>{contractorFinancialReportTitle}</h3>
                <p id={contractorFinancialReportDialogDescriptionId}>{contractorFinancialReportDescription}</p>
              </div>
              <button ref={contractorFinancialReportCloseRef} className="icon-button" type="button" aria-label="Закрыть финансовый отчет контрагента" onClick={closeContractorFinancialReport}>
                <X size={18} />
              </button>
            </div>
            <form className="balance-history-filters" onSubmit={(event) => {
              event.preventDefault()
              void loadContractorFinancialReport()
            }}>
              <label>
                Период с
                <input aria-label="Начало периода финансового отчета контрагента" type="month" value={contractorFinancialReportFilters.monthFrom} onChange={(event) => setContractorFinancialReportFilters((value) => ({ ...value, monthFrom: event.target.value }))} required />
              </label>
              <label>
                Период по
                <input aria-label="Конец периода финансового отчета контрагента" type="month" value={contractorFinancialReportFilters.monthTo} onChange={(event) => setContractorFinancialReportFilters((value) => ({ ...value, monthTo: event.target.value }))} required />
              </label>
              <button className="secondary-button" type="submit" disabled={contractorFinancialReportLoading}>
                <Search size={16} />
                <span>{contractorFinancialReportLoading ? 'Загружаем...' : 'Показать'}</span>
              </button>
            </form>
            {contractorFinancialReportError ? <FormError>{contractorFinancialReportError}</FormError> : null}
            {contractorFinancialReport ? (
              <>
                <div className="balance-history-summary" aria-label="Итоги финансового отчета контрагента">
                  <div>
                    <span>Начислено</span>
                    <strong>{formatMoney(contractorFinancialReport.accrualTotal)}</strong>
                  </div>
                  <div>
                    <span>Оплачено</span>
                    <strong>{formatMoney(contractorFinancialReport.paymentTotal)}</strong>
                  </div>
                  <div>
                    <span>{formatDebtLabel(contractorFinancialReport.balance)}</span>
                    <strong className={getDebtClassName(contractorFinancialReport.balance)}>{formatDebtAmount(contractorFinancialReport.balance)}</strong>
                  </div>
                  <div>
                    <span>Строк</span>
                    <strong>{contractorFinancialReport.rows.length}</strong>
                  </div>
                </div>
                <div className="dictionary-table-scroll garage-balance-table-scroll">
                  <table className="dictionary-data-table" aria-label={contractorFinancialReportTarget.type === 'supplier' ? 'Финансовый отчет поставщика' : 'Финансовый отчет сотрудника'}>
                    <thead>
                      <tr>
                        <th>Месяц</th>
                        <th>Дата</th>
                        <th>Документ</th>
                        <th>Операция</th>
                        <th>Начислено</th>
                        <th>Оплачено</th>
                        <th>Остаток</th>
                      </tr>
                    </thead>
                    <tbody>
                      {contractorFinancialReport.rows.map((row) => (
                        <tr key={row.id}>
                          <td>{formatMonth(row.accountingMonth)}</td>
                          <td>{formatDateOnly(row.date)}</td>
                          <td>{row.documentNumber}</td>
                          <td>{row.description}</td>
                          <td className="money-accrual">{row.accrualAmount > 0 ? formatMoney(row.accrualAmount) : '—'}</td>
                          <td className="money-expense">{row.paymentAmount > 0 ? formatMoney(row.paymentAmount) : '—'}</td>
                          <td className={getDebtClassName(row.balanceAfter)}>{formatDebtAmount(row.balanceAfter)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {contractorFinancialReport.rows.length === 0 ? <p className="empty-state" role="status" aria-live="polite">По выбранному периоду строк нет</p> : null}
                </div>
                <section className="contractor-history-section" aria-label="История изменений контрагента">
                  <h4>История изменений</h4>
                  {!canReadContractorHistory ? <p className="empty-state" role="status" aria-live="polite">История изменений доступна пользователям с правом просмотра audit-событий.</p> : null}
                  {contractorHistoryLoading ? <p className="prototype-status" role="status">Загружаем историю изменений...</p> : null}
                  {contractorHistoryError ? <FormError>{contractorHistoryError}</FormError> : null}
                  {canReadContractorHistory && !contractorHistoryLoading && !contractorHistoryError ? (
                    <div className="dictionary-table-scroll">
                      <table className="dictionary-data-table" aria-label="История изменений контрагента">
                        <thead>
                          <tr>
                            <th>Дата</th>
                            <th>Действие</th>
                            <th>Причина</th>
                          </tr>
                        </thead>
                        <tbody>
                          {contractorHistoryEvents.map((event) => (
                            <tr key={event.id}>
                              <td>{formatDateTime(event.createdAtUtc)}</td>
                              <td>{event.summary}</td>
                              <td>{event.reason || '—'}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                      {contractorHistoryEvents.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Истории изменений по контрагенту пока нет</p> : null}
                    </div>
                  ) : null}
                </section>
              </>
            ) : null}
          </section>
        </div>
      ) : null}

      {restoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setRestoreTarget(null)}>
          <section ref={restoreDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-restore-title" aria-describedby="contractor-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Восстановление</p>
                <h3 id="contractor-restore-title">Вернуть запись?</h3>
                <p>{getContractorRestoreTitle(restoreTarget)}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение восстановления контрагента" onClick={() => setRestoreTarget(null)}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="contractor-restore-description">Запись снова появится как активная в рабочем списке. Действие записывается в историю изменений.</p>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={restoreCancelRef} className="ghost-button" type="button" onClick={() => setRestoreTarget(null)}>Отмена</button>
              <button className="secondary-button" type="button" onClick={confirmRestore}>
                <RotateCcw size={16} />
                <span>Вернуть запись</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {garageDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeGarageDeleteDialog}>
          <section ref={garageDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="garage-table-delete-title" aria-describedby="garage-table-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="garage-table-delete-title">Удалить гараж?</h3>
                <p>{`Гараж ${garageDeleteTarget.number || 'без номера'}`}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления гаража" onClick={closeGarageDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="garage-table-delete-description">Гараж будет скрыт из рабочего списка, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
            <label className="field-label" htmlFor="garage-table-delete-reason">Причина удаления</label>
            <textarea
              id="garage-table-delete-reason"
              aria-label="Причина удаления гаража"
              maxLength={1000}
              value={garageDeleteReason}
              onChange={(event) => setGarageDeleteReason(event.target.value)}
              placeholder="Например: дубликат карточки"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={garageDeleteCancelRef} className="ghost-button" type="button" onClick={closeGarageDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={confirmGarageDeleteFromTable} disabled={!garageDeleteReason.trim()}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {supplierDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeSupplierDeleteDialog}>
          <section ref={supplierDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="supplier-table-delete-title" aria-describedby="supplier-table-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="supplier-table-delete-title">Удалить поставщика?</h3>
                <p>{supplierDeleteTarget.name || 'Поставщик без названия'}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления поставщика" onClick={closeSupplierDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="supplier-table-delete-description">Поставщик будет скрыт из рабочего списка, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
            <label className="field-label" htmlFor="supplier-table-delete-reason">Причина удаления</label>
            <textarea
              id="supplier-table-delete-reason"
              aria-label="Причина удаления поставщика"
              maxLength={1000}
              value={supplierDeleteReason}
              onChange={(event) => setSupplierDeleteReason(event.target.value)}
              placeholder="Например: договор больше не действует"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={supplierDeleteCancelRef} className="ghost-button" type="button" onClick={closeSupplierDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={confirmSupplierDeleteFromTable} disabled={!supplierDeleteReason.trim()}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {employeeDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeEmployeeDeleteDialog}>
          <section ref={employeeDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="employee-table-delete-title" aria-describedby="employee-table-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="employee-table-delete-title">Удалить сотрудника?</h3>
                <p>{employeeDeleteTarget.fullName || 'Сотрудник без имени'}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления сотрудника" onClick={closeEmployeeDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="employee-table-delete-description">Сотрудник будет скрыт из рабочего списка персонала, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
            <label className="field-label" htmlFor="employee-table-delete-reason">Причина удаления</label>
            <textarea
              id="employee-table-delete-reason"
              aria-label="Причина удаления сотрудника"
              maxLength={1000}
              value={employeeDeleteReason}
              onChange={(event) => setEmployeeDeleteReason(event.target.value)}
              placeholder="Например: сотрудник больше не работает"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={employeeDeleteCancelRef} className="ghost-button" type="button" onClick={closeEmployeeDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={confirmEmployeeDeleteFromTable} disabled={!employeeDeleteReason.trim()}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {departmentDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeDepartmentDeleteDialog}>
          <section ref={departmentDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="department-table-delete-title" aria-describedby="department-table-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="department-table-delete-title">Удалить отдел?</h3>
                <p>{departmentDeleteTarget.name || 'Отдел без названия'}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления отдела" onClick={closeDepartmentDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="department-table-delete-description">Отдел будет скрыт из рабочего списка персонала, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
            <label className="field-label" htmlFor="department-table-delete-reason">Причина удаления</label>
            <textarea
              id="department-table-delete-reason"
              aria-label="Причина удаления отдела"
              maxLength={1000}
              value={departmentDeleteReason}
              onChange={(event) => setDepartmentDeleteReason(event.target.value)}
              placeholder="Например: отдел больше не используется"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={departmentDeleteCancelRef} className="ghost-button" type="button" onClick={closeDepartmentDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={confirmDepartmentDeleteFromTable} disabled={!departmentDeleteReason.trim()}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </section>
  )
}

function createEmptyGaragePrototype(): ContractorGarageRow {
  return {
    id: `garage-${Date.now()}`,
    ownerId: null,
    number: '',
    peopleCount: '',
    floorCount: '',
    owner: '',
    phone: '',
    address: '',
    startingBalance: '',
    balance: '',
    overdueDebt: '',
    initialWater: '',
    initialElectricity: '',
    meters: '',
    comment: '',
    isDeleted: false,
  }
}

function createEmptySupplierPrototype(): ContractorSupplierRow {
  return {
    id: `supplier-${Date.now()}`,
    name: '',
    service: '',
    inn: '',
    legalAddress: '',
    contactPerson: '',
    phone: '',
    email: '',
    contacts: [],
    debt: '',
    comment: '',
    isDeleted: false,
  }
}

function createEmptyEmployeePrototype(department: string): ContractorStaffRow {
  return {
    id: `employee-${Date.now()}`,
    fullName: '',
    department,
    rate: '',
    isDeleted: false,
  }
}

type PrototypeChangeEntry = {
  fieldLabel: string
  previousValue: string
  nextValue: string
}

function createPrototypeChangeEntry(fieldLabel: string, previousValue: string, nextValue: string): PrototypeChangeEntry | null {
  if (previousValue.trim() === nextValue.trim()) {
    return null
  }

  return { fieldLabel, previousValue, nextValue }
}

function compactPrototypeChanges(changes: Array<PrototypeChangeEntry | null>) {
  return changes.filter((change): change is PrototypeChangeEntry => Boolean(change))
}

function getGaragePrototypeChanges(previous: ContractorGarageRow, next: ContractorGarageRow) {
  return compactPrototypeChanges([
    createPrototypeChangeEntry('Номер', previous.number, next.number),
    createPrototypeChangeEntry('Количество человек', previous.peopleCount, next.peopleCount),
    createPrototypeChangeEntry('Этажи', previous.floorCount, next.floorCount),
    createPrototypeChangeEntry('Стартовое значение счетчика воды', previous.initialWater, next.initialWater),
    createPrototypeChangeEntry('Стартовое значение счетчика электричества', previous.initialElectricity, next.initialElectricity),
    createPrototypeChangeEntry('Владелец', previous.owner, next.owner),
    createPrototypeChangeEntry('Телефон', previous.phone, next.phone),
    createPrototypeChangeEntry('Адрес', previous.address, next.address),
    createPrototypeChangeEntry('Комментарий', previous.comment, next.comment),
  ])
}

function getSupplierPrototypeChanges(previous: ContractorSupplierRow, next: ContractorSupplierRow) {
  return compactPrototypeChanges([
    createPrototypeChangeEntry('Наименование', previous.name, next.name),
    createPrototypeChangeEntry('Услуга', previous.service, next.service),
    createPrototypeChangeEntry('ИНН', previous.inn, next.inn),
    createPrototypeChangeEntry('Задолженность', previous.debt, next.debt),
    createPrototypeChangeEntry('Юридический адрес', previous.legalAddress, next.legalAddress),
    createPrototypeChangeEntry('Контакты', formatSupplierContactSummary(previous.contacts), formatSupplierContactSummary(next.contacts)),
    createPrototypeChangeEntry('Комментарий', previous.comment, next.comment),
  ])
}

function getEmployeePrototypeChanges(previous: ContractorStaffRow, next: ContractorStaffRow) {
  return compactPrototypeChanges([
    createPrototypeChangeEntry('ФИО', previous.fullName, next.fullName),
    createPrototypeChangeEntry('Отдел', previous.department, next.department),
    createPrototypeChangeEntry('Ставка', previous.rate, next.rate),
  ])
}

function PrototypeChangeConfirmationDialog({
  changes,
  objectName,
  onCancel,
  onConfirm,
  title,
}: {
  changes: PrototypeChangeEntry[]
  objectName: string
  onCancel: () => void
  onConfirm: () => void
  title: string
}) {
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  useEscapeKey(true, onCancel)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onCancel}>
      <section ref={dialogRef} className="detail-dialog contractors-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="prototype-change-title" aria-describedby="prototype-change-description" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <p className="eyebrow">Изменение</p>
            <h3 id="prototype-change-title">{title}</h3>
            <p>{objectName}</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть подтверждение изменений" onClick={onCancel}>
            <X size={18} />
          </button>
        </div>
        <p className="confirmation-text" id="prototype-change-description">Проверьте, что именно изменится. Действие записывается в историю изменений.</p>
        <dl className="dictionary-change-list">
          {changes.map((change) => (
            <div key={change.fieldLabel}>
              <dt>{change.fieldLabel}</dt>
              <dd>{formatPrototypeChangeValue(change.previousValue)} {'->'} {formatPrototypeChangeValue(change.nextValue)}</dd>
            </div>
          ))}
        </dl>
        <div className="detail-dialog-actions contractors-dialog-actions">
          <button ref={cancelRef} className="ghost-button" type="button" onClick={onCancel}>Отмена</button>
          <button className="secondary-button" type="button" onClick={onConfirm}>
            <Save size={16} />
            <span>Сохранить</span>
          </button>
        </div>
      </section>
    </div>
  )
}

function SupplierContactDeleteConfirmationDialog({
  contact,
  reason,
  cancelRef,
  dialogRef,
  onReasonChange,
  onCancel,
  onConfirm,
}: {
  contact: ContractorSupplierContact
  reason: string
  cancelRef: RefObject<HTMLButtonElement | null>
  dialogRef: RefObject<HTMLElement | null>
  onReasonChange: (reason: string) => void
  onCancel: () => void
  onConfirm: () => void
}) {
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onCancel}>
      <section ref={dialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="supplier-contact-delete-title" aria-describedby="supplier-contact-delete-description" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <p className="eyebrow">Удаление</p>
            <h3 id="supplier-contact-delete-title">Удалить контакт?</h3>
            <p>{contact.fullName || 'Контакт без ФИО'}</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления контакта" onClick={onCancel}>
            <X size={18} />
          </button>
        </div>
        <p className="confirmation-text" id="supplier-contact-delete-description">Контакт будет скрыт в карточке поставщика, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
        <label className="field-label" htmlFor="supplier-contact-delete-reason">Причина удаления</label>
        <textarea
          id="supplier-contact-delete-reason"
          aria-label="Причина удаления контакта"
          maxLength={1000}
          value={reason}
          onChange={(event) => onReasonChange(event.target.value)}
          placeholder="Например: контакт больше не работает у поставщика"
          required
        />
        <div className="detail-dialog-actions contractors-dialog-actions">
          <button className="secondary-button danger-button" type="button" onClick={onConfirm} disabled={!reason.trim()}>
            <Trash2 size={16} />
            <span>Удалить</span>
          </button>
          <button ref={cancelRef} className="ghost-button" type="button" onClick={onCancel}>Отмена</button>
        </div>
      </section>
    </div>
  )
}

function SupplierContactRestoreConfirmationDialog({
  contact,
  cancelRef,
  dialogRef,
  onCancel,
  onConfirm,
}: {
  contact: ContractorSupplierContact
  cancelRef: RefObject<HTMLButtonElement | null>
  dialogRef: RefObject<HTMLElement | null>
  onCancel: () => void
  onConfirm: () => void
}) {
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onCancel}>
      <section ref={dialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="supplier-contact-restore-title" aria-describedby="supplier-contact-restore-description" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <div>
            <p className="eyebrow">Восстановление</p>
            <h3 id="supplier-contact-restore-title">Восстановить контакт?</h3>
            <p>{contact.fullName || 'Контакт без ФИО'}</p>
          </div>
          <button className="icon-button" type="button" aria-label="Закрыть подтверждение восстановления контакта" onClick={onCancel}>
            <X size={18} />
          </button>
        </div>
        <p className="confirmation-text" id="supplier-contact-restore-description">Контакт снова станет активным. Если поставщик был скрыт, он тоже будет восстановлен после сохранения карточки.</p>
        <div className="detail-dialog-actions contractors-dialog-actions">
          <button className="secondary-button" type="button" onClick={onConfirm}>
            <RotateCcw size={16} />
            <span>Восстановить</span>
          </button>
          <button ref={cancelRef} className="ghost-button" type="button" onClick={onCancel}>Отмена</button>
        </div>
      </section>
    </div>
  )
}

function GaragePrototypeDialog({ item, onClose, onDelete, onOpenFinancialReport, onSave }: { item?: ContractorGarageRow; onClose: () => void; onDelete: (item: ContractorGarageRow, reason?: string) => void; onOpenFinancialReport: (item: ContractorGarageRow) => void; onSave: (item: ContractorGarageRow) => void }) {
  const [form, setForm] = useState<ContractorGarageRow>(item ?? createEmptyGaragePrototype())
  const [saveChanges, setSaveChanges] = useState<PrototypeChangeEntry[]>([])
  const [deleteConfirmationOpen, setDeleteConfirmationOpen] = useState(false)
  const [deleteReason, setDeleteReason] = useState('')
  useRestoreFocusOnClose(true)
  useRestoreFocusOnClose(Boolean(deleteConfirmationOpen))
  const dialogRef = useFocusTrap<HTMLElement>(!deleteConfirmationOpen && saveChanges.length === 0)
  const deleteDialogRef = useFocusTrap<HTMLElement>(deleteConfirmationOpen)
  const deleteCancelRef = useFocusOnOpen<HTMLButtonElement>(deleteConfirmationOpen)
  useEscapeKey(!deleteConfirmationOpen && saveChanges.length === 0, onClose)
  useEscapeKey(deleteConfirmationOpen, () => closeDeleteConfirmation())

  function saveAndClose() {
    onSave(form)
    setSaveChanges([])
    onClose()
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!item) {
      saveAndClose()
      return
    }

    const changes = getGaragePrototypeChanges(item, form)
    if (changes.length === 0) {
      onClose()
      return
    }

    setSaveChanges(changes)
  }

  function closeDeleteConfirmation() {
    setDeleteConfirmationOpen(false)
    setDeleteReason('')
  }

  function confirmGarageDelete() {
    if (!item || !deleteReason.trim()) {
      return
    }

    onDelete(item, deleteReason.trim())
    closeDeleteConfirmation()
    onClose()
  }

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog contractors-dialog--wide" role="dialog" aria-modal="true" aria-labelledby="garage-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="garage-dialog-title">{item ? `Гараж ${item.number}` : 'Новый гараж'}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму гаража" onClick={onClose}><X size={18} /></button>
          </div>
          <form className="dictionary-modal-form contractors-modal-form" onSubmit={handleSubmit}>
            <div className="contractors-modal-grid">
              <FormField label="Номер"><input aria-label="Номер гаража" value={form.number} onChange={(event) => setForm({ ...form, number: event.target.value })} /></FormField>
              <FormField label="Баланс"><input aria-label="Баланс гаража" value={form.balance || '0'} readOnly /></FormField>
              <FormField label="Количество человек"><input aria-label="Количество человек" value={form.peopleCount} onChange={(event) => setForm({ ...form, peopleCount: event.target.value })} /></FormField>
              <FormField label="Просроченная задолженность"><input aria-label="Просроченная задолженность гаража" value={form.overdueDebt || 'Нет'} readOnly /></FormField>
              <FormField label="Этажи"><input aria-label="Этажи гаража" value={form.floorCount} onChange={(event) => setForm({ ...form, floorCount: event.target.value })} /></FormField>
              <FormField label="Старт. зн. сч. за воду"><input aria-label="Стартовое значение счетчика воды" value={form.initialWater} onChange={(event) => setForm({ ...form, initialWater: event.target.value })} /></FormField>
              <FormField label="Старт. зн. сч. за эл-во"><input aria-label="Стартовое значение счетчика электричества" value={form.initialElectricity} onChange={(event) => setForm({ ...form, initialElectricity: event.target.value })} /></FormField>
            </div>
            <FormField label="Владелец"><input aria-label="Владелец гаража" value={form.owner} onChange={(event) => setForm({ ...form, owner: event.target.value })} /></FormField>
            <FormField label="Телефон"><input aria-label="Телефон владельца гаража" value={form.phone} onChange={(event) => setForm({ ...form, phone: event.target.value })} /></FormField>
            <FormField label="Адрес"><input aria-label="Адрес гаража" value={form.address} onChange={(event) => setForm({ ...form, address: event.target.value })} /></FormField>
            <FormField label="Комментарий"><textarea aria-label="Комментарий гаража" value={form.comment} onChange={(event) => setForm({ ...form, comment: event.target.value })} /></FormField>
            <div className="detail-dialog-actions contractors-dialog-actions contractors-garage-actions">
              <button className="secondary-button contractors-report-button" type="button" onClick={() => onOpenFinancialReport(form)}>
                <FileText size={16} />
                <span>Открыть фин. отчет</span>
              </button>
              <button className="secondary-button" type="submit"><Save size={17} /><span>Сохранить</span></button>
              <button className="ghost-button" type="button" onClick={onClose}>Отмена</button>
              {item ? <button className="danger-button" type="button" onClick={() => setDeleteConfirmationOpen(true)}>Удалить гараж</button> : null}
            </div>
          </form>
        </section>
      </div>

      {item && saveChanges.length > 0 ? (
        <PrototypeChangeConfirmationDialog changes={saveChanges} objectName={`Гараж ${item.number || 'без номера'}`} onCancel={() => setSaveChanges([])} onConfirm={saveAndClose} title="Подтвердить изменения гаража" />
      ) : null}

      {item && deleteConfirmationOpen ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeDeleteConfirmation}>
          <section ref={deleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="garage-delete-title" aria-describedby="garage-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="garage-delete-title">Удалить гараж?</h3>
                <p>{`Гараж ${item.number || 'без номера'}`}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления гаража" onClick={closeDeleteConfirmation}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="garage-delete-description">Гараж будет скрыт из рабочего списка контрагентов. Укажите причину, чтобы действие можно было проверить позже.</p>
            <label className="field-label" htmlFor="garage-delete-reason">Причина удаления</label>
            <textarea
              id="garage-delete-reason"
              aria-label="Причина удаления гаража"
              maxLength={1000}
              value={deleteReason}
              onChange={(event) => setDeleteReason(event.target.value)}
              placeholder="Например: дубликат карточки"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={deleteCancelRef} className="ghost-button" type="button" onClick={closeDeleteConfirmation}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={confirmGarageDelete} disabled={!deleteReason.trim()}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </>
  )
}

function SupplierPrototypeDialog({ item, services, onClose, onOpenFinancialReport, onSave }: { item?: ContractorSupplierRow; services: string[]; onClose: () => void; onOpenFinancialReport: (item: ContractorSupplierRow) => void; onSave: (item: ContractorSupplierRow) => void }) {
  const [form, setForm] = useState<ContractorSupplierRow>(item ?? { ...createEmptySupplierPrototype(), service: services[0] ?? '' })
  const [saveChanges, setSaveChanges] = useState<PrototypeChangeEntry[]>([])
  const [contactContextMenu, setContactContextMenu] = useState<{ contact: ContractorSupplierContact; x: number; y: number } | null>(null)
  const [contactDeleteTarget, setContactDeleteTarget] = useState<ContractorSupplierContact | null>(null)
  const [contactDeleteReason, setContactDeleteReason] = useState('')
  const [contactRestoreTarget, setContactRestoreTarget] = useState<ContractorSupplierContact | null>(null)
  useRestoreFocusOnClose(true)
  useRestoreFocusOnClose(Boolean(contactDeleteTarget))
  useRestoreFocusOnClose(Boolean(contactRestoreTarget))
  const dialogRef = useFocusTrap<HTMLElement>(saveChanges.length === 0 && !contactDeleteTarget && !contactRestoreTarget)
  const contactDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(contactDeleteTarget))
  const contactDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(contactDeleteTarget))
  const contactRestoreDialogRef = useFocusTrap<HTMLElement>(Boolean(contactRestoreTarget))
  const contactRestoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(contactRestoreTarget))
  useEscapeKey(saveChanges.length === 0 && !contactDeleteTarget && !contactRestoreTarget, onClose)
  useEscapeKey(Boolean(contactContextMenu), () => setContactContextMenu(null))
  useEscapeKey(Boolean(contactDeleteTarget), () => closeContactDeleteDialog())
  useEscapeKey(Boolean(contactRestoreTarget), () => closeContactRestoreDialog())

  function saveAndClose() {
    onSave(normalizeSupplierPrototype(form))
    setSaveChanges([])
    onClose()
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!item) {
      saveAndClose()
      return
    }

    const changes = getSupplierPrototypeChanges(item, form)
    if (changes.length === 0) {
      onClose()
      return
    }

    setSaveChanges(changes)
  }

  function addContact() {
    setForm((currentForm) => ({ ...currentForm, contacts: [...currentForm.contacts, createEmptySupplierContact()] }))
  }

  function updateContact(contactId: string, patch: Partial<ContractorSupplierContact>) {
    setForm((currentForm) => ({
      ...currentForm,
      contacts: currentForm.contacts.map((contact) => (contact.id === contactId ? { ...contact, ...patch } : contact)),
    }))
  }

  function openContactContextMenu(event: MouseEvent<HTMLDivElement>, contact: ContractorSupplierContact) {
    event.preventDefault()
    setContactContextMenu({ contact, x: event.clientX, y: event.clientY })
  }

  function requestDeleteContact(contact: ContractorSupplierContact) {
    setContactContextMenu(null)
    setContactDeleteTarget(contact)
    setContactDeleteReason(contact.deleteReason ?? '')
  }

  function closeContactDeleteDialog() {
    setContactDeleteTarget(null)
    setContactDeleteReason('')
  }

  function requestRestoreContact(contact: ContractorSupplierContact) {
    setContactContextMenu(null)
    setContactRestoreTarget(contact)
  }

  function closeContactRestoreDialog() {
    setContactRestoreTarget(null)
  }

  function confirmContactDelete() {
    if (!contactDeleteTarget || !contactDeleteReason.trim()) {
      return
    }

    updateContact(contactDeleteTarget.id, { isDeleted: true, status: 'Не работает', deleteReason: contactDeleteReason.trim() })
    closeContactDeleteDialog()
  }

  function confirmContactRestore() {
    if (!contactRestoreTarget) {
      return
    }

    setForm((currentForm) => ({
      ...currentForm,
      isDeleted: false,
      contacts: currentForm.contacts.map((itemContact) => (itemContact.id === contactRestoreTarget.id ? { ...itemContact, isDeleted: false, status: 'Работает', deleteReason: undefined } : itemContact)),
    }))
    closeContactRestoreDialog()
  }

  const availableServices = getSupplierServiceOptions([...services, form.service])

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog contractors-dialog--wide contractors-dialog--supplier" role="dialog" aria-modal="true" aria-labelledby="supplier-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="supplier-dialog-title">{item ? form.name : 'Новый поставщик'}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму поставщика" onClick={onClose}><X size={18} /></button>
          </div>
          <form className="dictionary-modal-form contractors-modal-form" onSubmit={handleSubmit}>
            <FormField label="Наименование"><input aria-label="Наименование поставщика" value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} /></FormField>
            <FormField label="Услуга">
              <select aria-label="Услуга поставщика" value={form.service} onChange={(event) => setForm({ ...form, service: event.target.value })}>
                <option value="">Выберите услугу</option>
                {availableServices.map((service) => (
                  <option value={service} key={service}>{service}</option>
                ))}
              </select>
            </FormField>
            <div className="contractors-modal-grid">
              <FormField label="ИНН"><input aria-label="ИНН поставщика" value={form.inn} onChange={(event) => setForm({ ...form, inn: event.target.value })} /></FormField>
              <FormField label="Задолженность"><input aria-label="Задолженность поставщика" value={form.debt} onChange={(event) => setForm({ ...form, debt: event.target.value })} /></FormField>
            </div>
            <FormField label="Юр. адрес"><input aria-label="Юридический адрес поставщика" value={form.legalAddress} onChange={(event) => setForm({ ...form, legalAddress: event.target.value })} /></FormField>
            <div className="contractors-contacts-toolbar">
              <span>Контакты</span>
              <button className="secondary-button" type="button" onClick={addContact}>Добавить контакт</button>
            </div>
            <div className="contractors-contacts-preview contractors-contacts-preview--editable" role="table" aria-label="Контакты поставщика">
              <div className="contractors-contacts-row contractors-contacts-row--header contractors-contacts-row--editable" role="row">
                <span role="columnheader">№</span>
                <span role="columnheader">ФИО</span>
                <span role="columnheader">Должность</span>
                <span role="columnheader">Телефон</span>
                <span role="columnheader">Почта</span>
                <span role="columnheader">Статус</span>
                <span role="columnheader">Комментарий</span>
              </div>
              {form.contacts.length === 0 ? (
                <div className="contractors-contacts-row contractors-contacts-row--editable contractors-contacts-row--empty" role="row">
                  <span role="cell">Контакты пока не добавлены</span>
                </div>
              ) : form.contacts.map((contact, index) => (
                <div className={contact.isDeleted ? 'contractors-contacts-row contractors-contacts-row--editable contractors-contacts-row--deleted' : 'contractors-contacts-row contractors-contacts-row--editable'} role="row" key={contact.id} onContextMenu={(event) => openContactContextMenu(event, contact)}>
                  <span role="cell">{index + 1}</span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: ФИО`} value={contact.fullName} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { fullName: event.target.value })} /></span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: должность`} value={contact.position} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { position: event.target.value })} /></span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: телефон`} value={contact.phone} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { phone: event.target.value })} /></span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: почта`} value={contact.email} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { email: event.target.value })} /></span>
                  <span role="cell">
                    <select aria-label={`Контакт ${index + 1}: статус`} value={contact.status} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { status: event.target.value as ContractorSupplierContact['status'] })}>
                      <option>Работает</option>
                      <option>Не работает</option>
                    </select>
                  </span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: комментарий`} value={contact.comment} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { comment: event.target.value })} /></span>
                </div>
              ))}
            </div>
            <FormField label="Комментарий"><textarea aria-label="Комментарий поставщика" value={form.comment} onChange={(event) => setForm({ ...form, comment: event.target.value })} /></FormField>
            <div className="detail-dialog-actions contractors-dialog-actions contractors-garage-actions">
              <button className="secondary-button contractors-report-button" type="button" onClick={() => onOpenFinancialReport(form)}>
                <FileText size={16} />
                <span>Открыть фин. отчет</span>
              </button>
              <button className="secondary-button" type="submit"><Save size={17} /><span>Сохранить</span></button>
              <button className="ghost-button" type="button" onClick={onClose}>Отмена</button>
            </div>
          </form>
        </section>
      </div>

      {contactContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setContactContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия контакта ${contactContextMenu.contact.fullName || 'без ФИО'}`}
            style={{ left: contactContextMenu.x, top: contactContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {contactContextMenu.contact.isDeleted ? (
              <>
                <p className="context-menu-hint">При восстановлении контакта будет восстановлен и поставщик.</p>
                <button type="button" role="menuitem" onClick={() => requestRestoreContact(contactContextMenu.contact)}>
                  <RotateCcw size={16} />
                  <span>Восстановить контакт</span>
                </button>
              </>
            ) : (
              <button className="context-menu-danger" type="button" role="menuitem" onClick={() => requestDeleteContact(contactContextMenu.contact)}>
                <Trash2 size={16} />
                <span>Удалить контакт</span>
              </button>
            )}
          </div>
        </div>
      ) : null}

      {item && saveChanges.length > 0 ? (
        <PrototypeChangeConfirmationDialog changes={saveChanges} objectName={item.name || 'Поставщик'} onCancel={() => setSaveChanges([])} onConfirm={saveAndClose} title="Подтвердить изменения поставщика" />
      ) : null}
      {contactDeleteTarget ? (
        <SupplierContactDeleteConfirmationDialog
          contact={contactDeleteTarget}
          reason={contactDeleteReason}
          cancelRef={contactDeleteCancelRef}
          dialogRef={contactDeleteDialogRef}
          onReasonChange={setContactDeleteReason}
          onCancel={closeContactDeleteDialog}
          onConfirm={confirmContactDelete}
        />
      ) : null}
      {contactRestoreTarget ? (
        <SupplierContactRestoreConfirmationDialog
          contact={contactRestoreTarget}
          cancelRef={contactRestoreCancelRef}
          dialogRef={contactRestoreDialogRef}
          onCancel={closeContactRestoreDialog}
          onConfirm={confirmContactRestore}
        />
      ) : null}
    </>
  )
}

function ContractorServicePrototypeDialog({ onClose, onSave }: { onClose: () => void; onSave: (serviceName: string) => void }) {
  const [serviceName, setServiceName] = useState('')
  const [isRegular, setIsRegular] = useState(true)
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useEscapeKey(true, onClose)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-directory-service-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <h3 id="contractor-directory-service-title">Добавить услугу</h3>
          <button className="icon-button" type="button" aria-label="Закрыть форму услуги" onClick={onClose}><X size={18} /></button>
        </div>
        <form className="dictionary-modal-form contractors-modal-form" onSubmit={(event) => {
          event.preventDefault()
          onSave(serviceName || 'Новая услуга')
          onClose()
        }}>
          <FormField label="Наименование услуги"><input aria-label="Наименование услуги контрагента" value={serviceName} onChange={(event) => setServiceName(event.target.value)} /></FormField>
          <label className="contractors-check-row"><input type="checkbox" aria-label="Регулярные платежи услуги" checked={isRegular} onChange={(event) => setIsRegular(event.target.checked)} /><span>Регулярные платежи</span></label>
          <FormField label="Периодичность"><input aria-label="Периодичность услуги" defaultValue="12" /></FormField>
          <FormField label="Учитывать платеж с"><input aria-label="Учитывать платеж услуги с" defaultValue="Январь" /></FormField>
          <FormField label="Оплатить до"><input aria-label="Оплатить услугу до" defaultValue="Июль" /></FormField>
          <FormField label="Перенос долга в просроченный"><input aria-label="Перенос долга услуги в просроченный" defaultValue="30" /></FormField>
          <label className="contractors-check-row"><input type="checkbox" aria-label="По счетчику услуги" defaultChecked /><span>По счетчику</span></label>
          <label className="contractors-check-row"><input type="checkbox" aria-label="Пороговая тарификация услуги" defaultChecked /><span>Пороговая тарификация</span></label>
          <FormField label="Единица измерения"><input aria-label="Единица измерения услуги" /></FormField>
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit"><Save size={17} /><span>Сохранить</span></button>
            <button className="secondary-button" type="button" onClick={onClose}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function EmployeePrototypeDialog({ departments, item, onClose, onOpenFinancialReport, onSave }: { departments: ContractorDepartmentRow[]; item?: ContractorStaffRow; onClose: () => void; onOpenFinancialReport: (item: ContractorStaffRow) => void; onSave: (item: ContractorStaffRow) => void }) {
  const activeDepartments = departments.filter((department) => !department.isDeleted)
  const [form, setForm] = useState<ContractorStaffRow>(item ?? createEmptyEmployeePrototype(activeDepartments[0]?.name ?? departments[0]?.name ?? ''))
  const selectableDepartments = departments.filter((department) => !department.isDeleted || department.name === form.department)
  const [saveChanges, setSaveChanges] = useState<PrototypeChangeEntry[]>([])
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(saveChanges.length === 0)
  useEscapeKey(saveChanges.length === 0, onClose)

  function saveAndClose() {
    onSave(form)
    setSaveChanges([])
    onClose()
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!item) {
      saveAndClose()
      return
    }

    const changes = getEmployeePrototypeChanges(item, form)
    if (changes.length === 0) {
      onClose()
      return
    }

    setSaveChanges(changes)
  }

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="employee-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="employee-dialog-title">{item ? form.fullName : 'Новый сотрудник'}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму сотрудника" onClick={onClose}><X size={18} /></button>
          </div>
          <form className="dictionary-modal-form contractors-modal-form" onSubmit={handleSubmit}>
            <FormField label="ФИО"><input aria-label="ФИО сотрудника" value={form.fullName} onChange={(event) => setForm({ ...form, fullName: event.target.value })} /></FormField>
            <FormField label="Отдел"><select aria-label="Отдел сотрудника" value={form.department} onChange={(event) => setForm({ ...form, department: event.target.value })}>{selectableDepartments.map((department) => <option value={department.name} key={department.id}>{department.name}</option>)}</select></FormField>
            <FormField label="Ставка"><div className="contractors-inline-field"><input aria-label="Ставка сотрудника" value={form.rate} onChange={(event) => setForm({ ...form, rate: event.target.value })} /><span>руб.</span></div></FormField>
            <div className="detail-dialog-actions contractors-dialog-actions contractors-garage-actions">
              <button className="secondary-button contractors-report-button" type="button" onClick={() => onOpenFinancialReport(form)}>
                <FileText size={16} />
                <span>Открыть фин. отчет</span>
              </button>
              <button className="secondary-button" type="submit"><Save size={17} /><span>Сохранить</span></button>
              <button className="ghost-button" type="button" onClick={onClose}>Отмена</button>
            </div>
          </form>
        </section>
      </div>

      {item && saveChanges.length > 0 ? (
        <PrototypeChangeConfirmationDialog changes={saveChanges} objectName={item.fullName || 'Сотрудник'} onCancel={() => setSaveChanges([])} onConfirm={saveAndClose} title="Подтвердить изменения сотрудника" />
      ) : null}

    </>
  )
}

function DepartmentPrototypeDialog({ onClose, onSave }: { onClose: () => void; onSave: (name: string) => void }) {
  const [name, setName] = useState('')
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useEscapeKey(true, onClose)

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="department-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <h3 id="department-dialog-title">Новый отдел</h3>
          <button className="icon-button" type="button" aria-label="Закрыть форму отдела" onClick={onClose}><X size={18} /></button>
        </div>
        <form className="dictionary-modal-form contractors-modal-form" onSubmit={(event) => {
          event.preventDefault()
          onSave(name || 'Новый отдел')
          onClose()
        }}>
          <FormField label="Наименование"><input aria-label="Наименование отдела" value={name} onChange={(event) => setName(event.target.value)} /></FormField>
          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit">Ок</button>
            <button className="secondary-button" type="button" onClick={onClose}>Отмена</button>
          </div>
        </form>
      </section>
    </div>
  )
}

type ContractorTariffDraft = {
  title: string
  amount: string
  unit: string
  dateDay: string
  dateMonth: string
}

const contractorTariffMonthOptions = [
  { value: 'янв', label: 'Январь', maxDay: 31 },
  { value: 'фев', label: 'Февраль', maxDay: 28 },
  { value: 'мар', label: 'Март', maxDay: 31 },
  { value: 'апр', label: 'Апрель', maxDay: 30 },
  { value: 'май', label: 'Май', maxDay: 31 },
  { value: 'июн', label: 'Июнь', maxDay: 30 },
  { value: 'июл', label: 'Июль', maxDay: 31 },
  { value: 'авг', label: 'Август', maxDay: 31 },
  { value: 'сен', label: 'Сентябрь', maxDay: 30 },
  { value: 'окт', label: 'Октябрь', maxDay: 31 },
  { value: 'ноя', label: 'Ноябрь', maxDay: 30 },
  { value: 'дек', label: 'Декабрь', maxDay: 31 },
]

function createEditableDrafts(rows: Array<{ id: string; title?: string; amount?: string; unit?: string; dateDay?: string; dateMonth?: string }>) {
  return rows.reduce<Record<string, ContractorTariffDraft>>((drafts, row) => {
    drafts[row.id] = { title: row.title ?? '', amount: row.amount ?? '', unit: row.unit ?? '', dateDay: row.dateDay ?? '', dateMonth: row.dateMonth ?? '' }
    return drafts
  }, {})
}

function handleEditableInputKeyDown(event: KeyboardEvent<HTMLInputElement | HTMLSelectElement>, onCommit: () => void | Promise<void>) {
  if (event.key === 'Enter') {
    event.preventDefault()
    void onCommit()
  }
}

function formatContractorTariffDate(day: string, month: string) {
  return `${day.padStart(2, '0')} ${month}`.trim()
}

function getContractorTariffDateError(day: string, month: string) {
  const trimmedDay = day.trim()
  const monthOption = contractorTariffMonthOptions.find((option) => option.value === month)

  if (!/^\d{1,2}$/.test(trimmedDay)) {
    return 'Укажите день числом от 1 до 31.'
  }

  if (!monthOption) {
    return 'Выберите месяц.'
  }

  const numericDay = Number(trimmedDay)
  if (numericDay < 1 || numericDay > monthOption.maxDay) {
    return `В месяце "${monthOption.label}" можно указать день от 1 до ${monthOption.maxDay}.`
  }

  return null
}

function formatTariffNumber(value: number | null | undefined) {
  return value == null ? '' : String(value)
}

function formatPrototypeAmount(value: number | null | undefined) {
  if (value == null) {
    return ''
  }

  return Number.isInteger(value) ? String(value) : String(value)
}

function parsePrototypeAmount(value: string) {
  const normalized = value.replace(',', '.').trim()
  if (!normalized) {
    return null
  }

  const parsed = Number(normalized)
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null
}

function parseTariffAmount(value: string) {
  const normalized = value.replace(',', '.').trim()
  if (!normalized) {
    return null
  }

  const parsed = Number(normalized)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null
}

function findTariffForPrototypeRow(tariffs: TariffDto[], row: ContractorTariffRow) {
  const lowerTitle = row.title.toLocaleLowerCase('ru')
  if (row.calculationBase === 'meter_electricity') {
    return tariffs.find((tariff) => tariff.calculationBase === 'meter_electricity') ?? null
  }

  if (row.id === 'water-rate') {
    return tariffs.find((tariff) => tariff.calculationBase === 'meter_water') ?? null
  }

  if (row.id === 'waste-rate') {
    return tariffs.find((tariff) => tariff.calculationBase === 'people' || tariff.name.toLocaleLowerCase('ru').includes('мусор')) ?? null
  }

  return tariffs.find((tariff) => tariff.name.toLocaleLowerCase('ru') === lowerTitle || tariff.name.toLocaleLowerCase('ru').includes(row.category.toLocaleLowerCase('ru'))) ?? null
}

function mergeTariffsIntoPrototypeRows(rows: ContractorTariffRow[], tariffs: TariffDto[]) {
  const electricityTariff = tariffs.find((tariff) => tariff.calculationBase === 'meter_electricity')
  return rows.map((row) => {
    if (row.calculationBase === 'meter_electricity' && electricityTariff) {
      const base = {
        ...row,
        backendTariffId: electricityTariff.id,
        effectiveFrom: electricityTariff.effectiveFrom,
        electricityFirstThreshold: electricityTariff.electricityFirstThreshold,
        electricitySecondThreshold: electricityTariff.electricitySecondThreshold,
      }
      if (row.id === 'electricity-tier-0') {
        return { ...base, title: electricityTariff.electricityFirstTierName ?? row.title, amount: formatTariffNumber(electricityTariff.electricityFirstRate) }
      }
      if (row.id === 'electricity-tier-1') {
        return { ...base, title: electricityTariff.electricitySecondTierName ?? row.title, amount: formatTariffNumber(electricityTariff.electricitySecondRate) }
      }
      return { ...base, title: electricityTariff.electricityThirdTierName ?? row.title, amount: formatTariffNumber(electricityTariff.electricityThirdRate) }
    }

    const tariff = findTariffForPrototypeRow(tariffs, row)
    return tariff && row.calculationBase
      ? { ...row, backendTariffId: tariff.id, effectiveFrom: tariff.effectiveFrom, amount: formatTariffNumber(tariff.rate), title: tariff.name }
      : row
  })
}

function createTariffRowsFromBackend(tariffs: TariffDto[], settings: ChargeServiceSettingDto[]) {
  const rowsBackedByTariffs = contractorTariffRows.filter((row) => Boolean(row.calculationBase && findTariffForPrototypeRow(tariffs, row)))
  return mergeChargeServicesIntoPrototypeRows(mergeTariffsIntoPrototypeRows(rowsBackedByTariffs, tariffs), settings)
}

function getContractorTariffMonthNumber(monthValue?: string | null) {
  if (!monthValue) {
    return null
  }

  const normalizedValue = monthValue.trim().toLocaleLowerCase('ru')
  const monthIndex = contractorTariffMonthOptions.findIndex((month) => (
    month.value === normalizedValue || month.label.toLocaleLowerCase('ru') === normalizedValue
  ))

  return monthIndex >= 0 ? monthIndex + 1 : null
}

function getContractorTariffMonthValue(monthNumber?: number | null) {
  if (!monthNumber || monthNumber < 1 || monthNumber > contractorTariffMonthOptions.length) {
    return contractorTariffMonthOptions[0].value
  }

  return contractorTariffMonthOptions[monthNumber - 1].value
}

function createChargeServiceRows(setting: ChargeServiceSettingDto): ContractorTariffRow[] {
  const rows: ContractorTariffRow[] = [
    {
      id: `charge-service-${setting.id}-main`,
      backendServiceSettingId: setting.id,
      serviceSettingKind: 'main',
      group: setting.name,
      category: setting.name,
      title: setting.name,
      amount: '',
      unit: setting.unitName ?? 'руб.',
      byMeter: setting.isMetered,
      tiered: setting.hasTieredTariff,
      isDeleted: setting.isArchived,
    },
  ]

  if (setting.isRegular) {
    rows.push(
      {
        id: `charge-service-${setting.id}-periodicity`,
        backendServiceSettingId: setting.id,
        serviceSettingKind: 'periodicity',
        category: setting.name,
        title: 'Периодичность',
        amount: String(setting.periodicityMonths ?? 12),
        unit: 'мес.',
        byMeter: setting.isMetered,
        tiered: setting.hasTieredTariff,
        isDeleted: setting.isArchived,
      },
      {
        id: `charge-service-${setting.id}-due-date`,
        backendServiceSettingId: setting.id,
        serviceSettingKind: 'due-date',
        category: setting.name,
        title: 'Оплата до',
        dateDay: setting.paymentDueDay ? String(setting.paymentDueDay).padStart(2, '0') : '01',
        dateMonth: getContractorTariffMonthValue(setting.paymentDueMonth),
        byMeter: setting.isMetered,
        tiered: setting.hasTieredTariff,
        isDeleted: setting.isArchived,
      },
      {
        id: `charge-service-${setting.id}-start-date`,
        backendServiceSettingId: setting.id,
        serviceSettingKind: 'start-date',
        category: setting.name,
        title: 'Учитывать платеж с',
        dateDay: '01',
        dateMonth: getContractorTariffMonthValue(setting.accrualStartMonth),
        byMeter: setting.isMetered,
        tiered: setting.hasTieredTariff,
        isDeleted: setting.isArchived,
      },
      {
        id: `charge-service-${setting.id}-overdue-days`,
        backendServiceSettingId: setting.id,
        serviceSettingKind: 'overdue-days',
        category: setting.name,
        title: 'Перенос долга в просроченный',
        amount: String(setting.overdueGraceDays),
        unit: 'дн.',
        byMeter: setting.isMetered,
        tiered: setting.hasTieredTariff,
        isDeleted: setting.isArchived,
      },
    )
  }

  return rows
}

function mergeChargeServicesIntoPrototypeRows(rows: ContractorTariffRow[], settings: ChargeServiceSettingDto[]) {
  const rowsWithoutBackendServices = rows.filter((row) => !row.backendServiceSettingId)
  return [
    ...rowsWithoutBackendServices,
    ...settings.flatMap((setting) => createChargeServiceRows(setting)),
  ]
}

function mergeIrregularPaymentsIntoPrototypeRows(rows: ContractorOneTimeRow[], payments: IrregularPaymentDto[], preferBackend = false) {
  const sourceRows = preferBackend && payments.length > 0
    ? rows.filter((row) => row.backendPaymentId || payments.some((payment) => payment.name.toLocaleLowerCase('ru') === row.name.toLocaleLowerCase('ru')))
    : rows
  const mergedRows = sourceRows.map((row) => {
    const payment = payments.find((item) => item.name.toLocaleLowerCase('ru') === row.name.toLocaleLowerCase('ru'))
    if (!payment) {
      return row
    }

    return {
      ...row,
      backendPaymentId: payment.id,
      amount: formatPrototypeAmount(payment.amount),
      isActive: payment.isActive,
      isDeleted: payment.isArchived,
      isUsed: payment.isUsed,
    }
  })

  const extraRows = payments
    .filter((payment) => !rows.some((row) => row.name.toLocaleLowerCase('ru') === payment.name.toLocaleLowerCase('ru')))
    .map((payment) => ({
      id: `one-time-${payment.id}`,
      backendPaymentId: payment.id,
      name: payment.name,
      amount: formatPrototypeAmount(payment.amount),
      isActive: payment.isActive,
      isDeleted: payment.isArchived,
      isUsed: payment.isUsed,
    }))

  return [...mergedRows, ...extraRows]
}

type TariffPrototypePendingChange =
  | {
    kind: 'tariff-text'
    rowId: string
    field: 'title' | 'amount' | 'unit'
    objectName: string
    fieldLabel: string
    previousValue: string
    nextValue: string
  }
  | {
    kind: 'tariff-boolean'
    rowId: string
    field: 'tiered' | 'byMeter'
    objectName: string
    fieldLabel: string
    previousValue: string
    nextValue: string
  }
  | {
    kind: 'tariff-date'
    rowId: string
    objectName: string
    fieldLabel: string
    previousValue: string
    nextValue: string
    nextDay: string
    nextMonth: string
  }
  | {
    kind: 'one-time-amount'
    rowId: string
    objectName: string
    fieldLabel: string
    previousValue: string
    nextValue: string
  }
  | {
    kind: 'one-time-active'
    rowId: string
    objectName: string
    fieldLabel: string
    previousValue: string
    nextValue: string
  }

function formatPrototypeChangeValue(value: string) {
  return value.trim() || 'Пусто'
}

function getTariffTextFieldLabel(row: ContractorTariffRow, field: 'title' | 'amount' | 'unit') {
  if (field === 'title') {
    return 'Наименование порога'
  }

  if (field === 'unit') {
    return 'Единица'
  }

  if (row.serviceSettingKind === 'periodicity') {
    return 'Периодичность'
  }

  if (row.serviceSettingKind === 'overdue-days') {
    return 'Перенос долга в просроченный'
  }

  return 'Значение'
}

function formatFeeCampaignParticipantsChange(appliesToAllGarages: boolean, participantGarageIds: string[], garageOptions: GarageDto[]) {
  if (appliesToAllGarages) {
    return 'Все гаражи'
  }

  const garageNumbers = participantGarageIds
    .map((garageId) => garageOptions.find((garage) => garage.id === garageId)?.number)
    .filter((number): number is string => Boolean(number))
    .sort((left, right) => left.localeCompare(right, 'ru', { numeric: true }))

  return garageNumbers.length > 0 ? garageNumbers.join(', ') : 'пусто'
}

function getFeeCampaignChangePreview(
  campaign: FeeCampaignDto,
  request: UpsertFeeCampaignRequest,
  incomeTypes: AccountingTypeDto[],
  garageOptions: GarageDto[],
) {
  const changes: ChangePreview[] = []
  const formatIncomeType = (incomeTypeId: string) => incomeTypes.find((incomeType) => incomeType.id === incomeTypeId)?.name ?? incomeTypeId

  appendChangePreview(changes, 'Наименование', formatChangeText(campaign.name), formatChangeText(request.name))
  appendChangePreview(changes, 'Вид поступления', formatIncomeType(campaign.incomeTypeId), formatIncomeType(request.incomeTypeId))
  appendChangePreview(changes, 'Цель', formatChangeText(campaign.goal), formatChangeText(request.goal))
  appendChangePreview(changes, 'Сумма взноса', formatChangeMoney(campaign.contributionAmount), formatChangeMoney(request.contributionAmount))
  appendChangePreview(changes, 'Сумма сбора', formatChangeMoney(campaign.targetAmount), formatChangeMoney(request.targetAmount))
  appendChangePreview(changes, 'Дата начала', formatChangeDate(campaign.startsOn), formatChangeDate(request.startsOn))
  appendChangePreview(changes, 'Дата окончания', formatChangeDate(campaign.endsOn), formatChangeDate(request.endsOn))
  appendChangePreview(
    changes,
    'Участники',
    formatFeeCampaignParticipantsChange(campaign.appliesToAllGarages, campaign.participantGarageIds, garageOptions),
    formatFeeCampaignParticipantsChange(request.appliesToAllGarages, request.participantGarageIds ?? [], garageOptions),
  )
  appendChangePreview(changes, 'Перенос долга в просроченный', `${formatChangeNumber(campaign.overdueGraceDays)} дн.`, `${formatChangeNumber(request.overdueGraceDays)} дн.`)

  return changes
}

type TariffsPrototypeSavedState = {
  tariffRows: ContractorTariffRow[]
  oneTimeRows: ContractorOneTimeRow[]
}

function TariffsAndFeesPrototypePanel({ auth, dictionaryClient, financeClient, formStateClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient; financeClient: FinanceClient; formStateClient: FormStateClient }) {
  const [modal, setModal] = useState<'service' | 'fee' | null>(null)
  const [tariffRows, setTariffRows] = useState<ContractorTariffRow[]>([])
  const [backendTariffs, setBackendTariffs] = useState<TariffDto[]>([])
  const [backendIncomeTypes, setBackendIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [backendChargeServices, setBackendChargeServices] = useState<ChargeServiceSettingDto[]>([])
  const [feeCampaignGarageOptions, setFeeCampaignGarageOptions] = useState<GarageDto[]>([])
  const [feeCampaigns, setFeeCampaigns] = useState<FeeCampaignDto[]>([])
  const [feeCampaignSavingId, setFeeCampaignSavingId] = useState<string | null>(null)
  const [feeCampaignEditTarget, setFeeCampaignEditTarget] = useState<FeeCampaignDto | null>(null)
  const [feeCampaignArchiveTarget, setFeeCampaignArchiveTarget] = useState<FeeCampaignDto | null>(null)
  const [feeCampaignArchiveReason, setFeeCampaignArchiveReason] = useState('')
  const [feeCampaignRestoreTarget, setFeeCampaignRestoreTarget] = useState<FeeCampaignDto | null>(null)
  const [feeCampaignGenerateTarget, setFeeCampaignGenerateTarget] = useState<FeeCampaignDto | null>(null)
  const [feeCampaignGenerateMonth, setFeeCampaignGenerateMonth] = useState(getCurrentMonthInputValue())
  const [feeCampaignGenerateComment, setFeeCampaignGenerateComment] = useState('')
  const [feeCampaignActionMessage, setFeeCampaignActionMessage] = useState<string | null>(null)
  const [chargeServiceArchiveTarget, setChargeServiceArchiveTarget] = useState<ChargeServiceSettingDto | null>(null)
  const [chargeServiceArchiveReason, setChargeServiceArchiveReason] = useState('')
  const [chargeServiceRestoreTarget, setChargeServiceRestoreTarget] = useState<ChargeServiceSettingDto | null>(null)
  const [thresholdDeleteTarget, setThresholdDeleteTarget] = useState<ContractorTariffRow | null>(null)
  const [thresholdDeleteReason, setThresholdDeleteReason] = useState('')
  const [oneTimeRows, setOneTimeRows] = useState<ContractorOneTimeRow[]>([])
  const [tariffDrafts, setTariffDrafts] = useState<Record<string, Partial<ContractorTariffRow>>>({})
  const [oneTimeDrafts, setOneTimeDrafts] = useState<Record<string, Partial<ContractorOneTimeRow>>>({})
  const [formStateLoaded, setFormStateLoaded] = useState(false)
  const [pendingChange, setPendingChange] = useState<TariffPrototypePendingChange | null>(null)
  const [tariffDateErrors, setTariffDateErrors] = useState<Record<string, string>>({})
  const [tariffPersistenceError, setTariffPersistenceError] = useState<string | null>(null)
  const [tariffsLoading, setTariffsLoading] = useState(false)
  const [tariffSavingRowId, setTariffSavingRowId] = useState<string | null>(null)
  const [oneTimeSavingRowId, setOneTimeSavingRowId] = useState<string | null>(null)
  const [oneTimeDeleteTarget, setOneTimeDeleteTarget] = useState<ContractorOneTimeRow | null>(null)
  const [oneTimeDeleteReason, setOneTimeDeleteReason] = useState('')
  const [oneTimeRestoreTarget, setOneTimeRestoreTarget] = useState<ContractorOneTimeRow | null>(null)
  const [oneTimeContextMenu, setOneTimeContextMenu] = useState<{ row: ContractorOneTimeRow; x: number; y: number } | null>(null)
  const [oneTimeActionMessage, setOneTimeActionMessage] = useState<string | null>(null)
  const canManageTariffs = hasPermission(auth, permissions.tariffsManage)

  useEffect(() => {
    let ignore = false

    async function loadTariffsAndFees() {
      setTariffsLoading(true)
      setTariffPersistenceError(null)
      try {
        const [loadedTariffs, loadedIncomeTypes, loadedIrregularPayments, loadedChargeServices, loadedFeeCampaigns, loadedGarages] = await Promise.all([
          dictionaryClient.getTariffs(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getIncomeTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getIrregularPayments(auth.accessToken, undefined, dictionaryScreenRequestLimit, true),
          dictionaryClient.getChargeServiceSettings(auth.accessToken, undefined, dictionaryScreenRequestLimit, true),
          dictionaryClient.getFeeCampaigns(auth.accessToken, undefined, dictionaryScreenRequestLimit, true),
          dictionaryClient.getGarages(auth.accessToken, undefined, dictionaryScreenRequestLimit),
        ])
        if (!ignore) {
          const mergedRows = createTariffRowsFromBackend(loadedTariffs, loadedChargeServices)
          const mergedOneTimeRows = mergeIrregularPaymentsIntoPrototypeRows([], loadedIrregularPayments, true)
          setBackendTariffs(loadedTariffs)
          setBackendIncomeTypes(loadedIncomeTypes)
          setBackendChargeServices(loadedChargeServices)
          setFeeCampaigns(loadedFeeCampaigns)
          setFeeCampaignGarageOptions(loadedGarages)
          setTariffRows(mergedRows)
          setOneTimeRows(mergedOneTimeRows)
          setTariffDrafts(createEditableDrafts(mergedRows))
          setOneTimeDrafts(createEditableDrafts(mergedOneTimeRows))
        }
      } catch (caught) {
        if (!ignore) {
          setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось загрузить тарифы и сборы.')
        }
      } finally {
        if (!ignore) {
          setTariffsLoading(false)
        }
      }
    }

    void loadTariffsAndFees()

    return () => {
      ignore = true
    }
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => {
    let ignore = false
    formStateClient
      .getState<TariffsPrototypeSavedState>(auth.accessToken, tariffsFormStateScope)
      .catch((error: unknown) => {
        if (!ignore) {
          setTariffPersistenceError(error instanceof Error ? error.message : 'Не удалось загрузить сохраненное состояние тарифов.')
        }
      })
      .finally(() => {
        if (!ignore) {
          setFormStateLoaded(true)
        }
      })

    return () => {
      ignore = true
    }
  }, [auth.accessToken, formStateClient])

  useEffect(() => {
    if (!formStateLoaded || tariffsLoading) {
      return
    }

    const handle = window.setTimeout(() => {
      void formStateClient
        .saveState<TariffsPrototypeSavedState>(auth.accessToken, tariffsFormStateScope, {
          payload: { tariffRows, oneTimeRows },
          summary: 'Сохранено состояние формы тарифов и сборов.'
        })
        .catch((error: unknown) => setTariffPersistenceError(error instanceof Error ? error.message : 'Не удалось сохранить состояние тарифов.'))
    }, 400)

    return () => window.clearTimeout(handle)
  }, [auth.accessToken, formStateClient, formStateLoaded, oneTimeRows, tariffRows, tariffsLoading])

  function closeOneTimeDeleteDialog() {
    setOneTimeDeleteTarget(null)
    setOneTimeDeleteReason('')
  }

  function closeOneTimeRestoreDialog() {
    setOneTimeRestoreTarget(null)
  }

  function closeFeeCampaignArchiveDialog() {
    setFeeCampaignArchiveTarget(null)
    setFeeCampaignArchiveReason('')
  }

  function closeFeeCampaignEditDialog() {
    setFeeCampaignEditTarget(null)
  }

  function closeFeeCampaignRestoreDialog() {
    setFeeCampaignRestoreTarget(null)
  }

  function closeChargeServiceArchiveDialog() {
    setChargeServiceArchiveTarget(null)
    setChargeServiceArchiveReason('')
  }

  function closeChargeServiceRestoreDialog() {
    setChargeServiceRestoreTarget(null)
  }

  function closeThresholdDeleteDialog() {
    setThresholdDeleteTarget(null)
    setThresholdDeleteReason('')
  }

  function closeFeeCampaignGenerateDialog() {
    setFeeCampaignGenerateTarget(null)
    setFeeCampaignGenerateMonth(getCurrentMonthInputValue())
    setFeeCampaignGenerateComment('')
  }

  function cancelPendingChange() {
    if (pendingChange?.kind === 'tariff-text') {
      setTariffDrafts((drafts) => ({
        ...drafts,
        [pendingChange.rowId]: {
          ...drafts[pendingChange.rowId],
          [pendingChange.field]: pendingChange.previousValue,
        },
      }))
    } else if (pendingChange?.kind === 'tariff-date') {
      const [previousDay = '', previousMonth = ''] = pendingChange.previousValue.split(' ')
      setTariffDrafts((drafts) => ({
        ...drafts,
        [pendingChange.rowId]: {
          ...drafts[pendingChange.rowId],
          dateDay: previousDay,
          dateMonth: previousMonth,
        },
      }))
    } else if (pendingChange?.kind === 'one-time-amount') {
      setOneTimeDrafts((drafts) => ({
        ...drafts,
        [pendingChange.rowId]: {
          ...drafts[pendingChange.rowId],
          amount: pendingChange.previousValue,
        },
      }))
    }

    setPendingChange(null)
  }

  async function confirmPendingChange() {
    if (!pendingChange) {
      return
    }

    if (pendingChange.kind === 'tariff-text') {
      const sourceRow = tariffRows.find((currentRow) => currentRow.id === pendingChange.rowId)
      const nextRows = tariffRows.map((currentRow) => (
        currentRow.id === pendingChange.rowId ? { ...currentRow, [pendingChange.field]: pendingChange.nextValue } : currentRow
      ))
      setTariffRows(nextRows)
      if (sourceRow && (sourceRow.backendServiceSettingId || pendingChange.field !== 'unit')) {
        await persistTariffRow(sourceRow, nextRows)
      }
    } else if (pendingChange.kind === 'tariff-boolean') {
      const sourceRow = tariffRows.find((currentRow) => currentRow.id === pendingChange.rowId)
      const nextRows = tariffRows.map((currentRow) => (
        currentRow.id === pendingChange.rowId ? { ...currentRow, [pendingChange.field]: pendingChange.nextValue === 'Да' } : currentRow
      ))
      setTariffRows(nextRows)
      if (sourceRow) {
        await persistTariffRow(sourceRow, nextRows)
      }
    } else if (pendingChange.kind === 'tariff-date') {
      const sourceRow = tariffRows.find((currentRow) => currentRow.id === pendingChange.rowId)
      const nextRows = tariffRows.map((currentRow) => (
        currentRow.id === pendingChange.rowId ? { ...currentRow, dateDay: pendingChange.nextDay, dateMonth: pendingChange.nextMonth } : currentRow
      ))
      setTariffRows(nextRows)
      setTariffDrafts((drafts) => ({
        ...drafts,
        [pendingChange.rowId]: {
          ...drafts[pendingChange.rowId],
          dateDay: pendingChange.nextDay,
          dateMonth: pendingChange.nextMonth,
        },
      }))
      if (sourceRow?.backendServiceSettingId) {
        await persistTariffRow(sourceRow, nextRows)
      }
    } else if (pendingChange.kind === 'one-time-amount') {
      const sourceRow = oneTimeRows.find((currentRow) => currentRow.id === pendingChange.rowId)
      if (sourceRow) {
        await persistOneTimeRow(sourceRow, { amount: pendingChange.nextValue })
      }
    } else {
      const sourceRow = oneTimeRows.find((currentRow) => currentRow.id === pendingChange.rowId)
      if (sourceRow) {
        await persistOneTimeStatus(sourceRow, pendingChange.nextValue === 'Активен')
      }
    }

    setPendingChange(null)
  }

  useRestoreFocusOnClose(Boolean(pendingChange))
  const changeDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingChange))
  const changeCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingChange))
  useRestoreFocusOnClose(Boolean(oneTimeDeleteTarget))
  const oneTimeDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(oneTimeDeleteTarget))
  const oneTimeDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(oneTimeDeleteTarget))
  useRestoreFocusOnClose(Boolean(oneTimeRestoreTarget))
  const oneTimeRestoreDialogRef = useFocusTrap<HTMLElement>(Boolean(oneTimeRestoreTarget))
  const oneTimeRestoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(oneTimeRestoreTarget))
  useRestoreFocusOnClose(Boolean(feeCampaignArchiveTarget))
  const feeCampaignArchiveDialogRef = useFocusTrap<HTMLElement>(Boolean(feeCampaignArchiveTarget))
  const feeCampaignArchiveCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(feeCampaignArchiveTarget))
  useRestoreFocusOnClose(Boolean(feeCampaignRestoreTarget))
  const feeCampaignRestoreDialogRef = useFocusTrap<HTMLElement>(Boolean(feeCampaignRestoreTarget))
  const feeCampaignRestoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(feeCampaignRestoreTarget))
  useRestoreFocusOnClose(Boolean(chargeServiceArchiveTarget))
  const chargeServiceArchiveDialogRef = useFocusTrap<HTMLElement>(Boolean(chargeServiceArchiveTarget))
  const chargeServiceArchiveCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(chargeServiceArchiveTarget))
  useRestoreFocusOnClose(Boolean(chargeServiceRestoreTarget))
  const chargeServiceRestoreDialogRef = useFocusTrap<HTMLElement>(Boolean(chargeServiceRestoreTarget))
  const chargeServiceRestoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(chargeServiceRestoreTarget))
  useRestoreFocusOnClose(Boolean(thresholdDeleteTarget))
  const thresholdDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(thresholdDeleteTarget))
  const thresholdDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(thresholdDeleteTarget))
  useRestoreFocusOnClose(Boolean(feeCampaignGenerateTarget))
  const feeCampaignGenerateDialogRef = useFocusTrap<HTMLElement>(Boolean(feeCampaignGenerateTarget))
  const feeCampaignGenerateCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(feeCampaignGenerateTarget))
  useEscapeKey(Boolean(pendingChange), () => cancelPendingChange())
  useEscapeKey(Boolean(oneTimeDeleteTarget), () => closeOneTimeDeleteDialog())
  useEscapeKey(Boolean(oneTimeRestoreTarget), () => closeOneTimeRestoreDialog())
  useEscapeKey(Boolean(feeCampaignArchiveTarget), () => closeFeeCampaignArchiveDialog())
  useEscapeKey(Boolean(feeCampaignRestoreTarget), () => closeFeeCampaignRestoreDialog())
  useEscapeKey(Boolean(chargeServiceArchiveTarget), () => closeChargeServiceArchiveDialog())
  useEscapeKey(Boolean(chargeServiceRestoreTarget), () => closeChargeServiceRestoreDialog())
  useEscapeKey(Boolean(thresholdDeleteTarget), () => closeThresholdDeleteDialog())
  useEscapeKey(Boolean(feeCampaignGenerateTarget), () => closeFeeCampaignGenerateDialog())
  useEscapeKey(Boolean(oneTimeContextMenu), () => setOneTimeContextMenu(null))

  function buildChargeServiceRequest(setting: ChargeServiceSettingDto, nextRows: ContractorTariffRow[]): UpsertChargeServiceSettingRequest {
    const relatedRows = nextRows.filter((item) => item.backendServiceSettingId === setting.id)
    const mainRow = relatedRows.find((item) => item.serviceSettingKind === 'main') ?? relatedRows[0]
    const periodicityRow = relatedRows.find((item) => item.serviceSettingKind === 'periodicity')
    const startRow = relatedRows.find((item) => item.serviceSettingKind === 'start-date')
    const dueRow = relatedRows.find((item) => item.serviceSettingKind === 'due-date')
    const overdueRow = relatedRows.find((item) => item.serviceSettingKind === 'overdue-days')
    const isRegular = setting.isRegular || Boolean(startRow || dueRow || overdueRow)
    const dueDay = dueRow?.dateDay ? Number(dueRow.dateDay) : setting.paymentDueDay
    const dueMonth = dueRow?.dateMonth ? getContractorTariffMonthNumber(dueRow.dateMonth) : setting.paymentDueMonth
    const startMonth = startRow?.dateMonth ? getContractorTariffMonthNumber(startRow.dateMonth) : setting.accrualStartMonth
    const periodicityMonths = parsePrototypeAmount(periodicityRow?.amount ?? '') ?? setting.periodicityMonths ?? 12
    const overdueGraceDays = parsePrototypeAmount(overdueRow?.amount ?? '') ?? setting.overdueGraceDays
    const isMetered = mainRow?.byMeter ?? setting.isMetered
    const hasTieredTariff = isMetered ? (mainRow?.tiered ?? setting.hasTieredTariff) : false

    return {
      name: (mainRow?.title ?? setting.name).trim() || setting.name,
      isRegular,
      periodicityMonths: isRegular ? Math.trunc(periodicityMonths) : null,
      accrualStartMonth: isRegular ? startMonth ?? 1 : null,
      paymentDueDay: isRegular ? dueDay ?? 1 : null,
      paymentDueMonth: isRegular ? dueMonth ?? 1 : null,
      overdueGraceDays: Math.trunc(overdueGraceDays),
      isMetered,
      hasTieredTariff,
      unitName: (mainRow?.unit ?? setting.unitName ?? '').trim() || null,
      incomeTypeId: isRegular ? setting.incomeTypeId ?? null : null,
      tariffId: isRegular ? setting.tariffId ?? null : null,
    }
  }

  async function persistServiceSettingRow(row: ContractorTariffRow, nextRows: ContractorTariffRow[]) {
    if (!canManageTariffs || row.isDeleted || !row.backendServiceSettingId) {
      return
    }

    const serviceSetting = backendChargeServices.find((setting) => setting.id === row.backendServiceSettingId)
    if (!serviceSetting) {
      return
    }

    setTariffSavingRowId(row.id)
    setTariffPersistenceError(null)
    try {
      const request = buildChargeServiceRequest(serviceSetting, nextRows)
      const savedSetting = await dictionaryClient.updateChargeServiceSetting(auth.accessToken, serviceSetting.id, request)
      const nextSettings = backendChargeServices.map((setting) => (setting.id === savedSetting.id ? savedSetting : setting))
      const mergedRows = mergeChargeServicesIntoPrototypeRows(nextRows, nextSettings)
      setBackendChargeServices(nextSettings)
      setTariffRows(mergedRows)
      setTariffDrafts(createEditableDrafts(mergedRows))
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось сохранить настройку услуги.')
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function persistTariffRow(row: ContractorTariffRow, nextRows: ContractorTariffRow[]) {
    if (row.backendServiceSettingId) {
      await persistServiceSettingRow(row, nextRows)
      return
    }

    if (!canManageTariffs || !row.calculationBase) {
      return
    }

    const targetRow = nextRows.find((item) => item.id === row.id) ?? row
    const backendTariff = targetRow.backendTariffId
      ? backendTariffs.find((tariff) => tariff.id === targetRow.backendTariffId)
      : findTariffForPrototypeRow(backendTariffs, targetRow)
    const effectiveFrom = targetRow.effectiveFrom ?? backendTariff?.effectiveFrom ?? getLocalDateInputValue()
    const amount = parseTariffAmount(targetRow.amount ?? '')
    if (amount == null) {
      return
    }

    let request: UpsertTariffRequest
    if (targetRow.calculationBase === 'meter_electricity') {
      const electricityRows = nextRows.filter((item) => item.calculationBase === 'meter_electricity' && item.threshold)
      const firstRow = electricityRows[0] ?? targetRow
      const secondRow = electricityRows[1] ?? targetRow
      const thirdRow = electricityRows[2] ?? targetRow
      const firstRate = parseTariffAmount(firstRow.amount ?? '')
      const secondRate = parseTariffAmount(secondRow.amount ?? '')
      const thirdRate = parseTariffAmount(thirdRow.amount ?? '')
      const firstThreshold = targetRow.electricityFirstThreshold ?? backendTariff?.electricityFirstThreshold ?? 1
      const secondThreshold = targetRow.electricitySecondThreshold ?? backendTariff?.electricitySecondThreshold ?? 3
      request = {
        name: backendTariff?.name ?? 'Электроэнергия',
        calculationBase: 'meter_electricity',
        rate: firstRate ?? amount,
        effectiveFrom,
        comment: backendTariff?.comment ?? '',
      }
      if (firstRate != null && secondRate != null && thirdRate != null) {
        request = {
          ...request,
          electricityFirstThreshold: firstThreshold,
          electricitySecondThreshold: secondThreshold,
          electricityFirstTierName: firstRow.title,
          electricitySecondTierName: secondRow.title,
          electricityThirdTierName: thirdRow.title,
          electricityFirstRate: firstRate,
          electricitySecondRate: secondRate,
          electricityThirdRate: thirdRate,
        }
      }
    } else {
      request = {
        name: targetRow.title,
        calculationBase: targetRow.calculationBase ?? row.calculationBase,
        rate: amount,
        effectiveFrom,
        comment: backendTariff?.comment ?? '',
      }
    }

    setTariffSavingRowId(targetRow.id)
    setTariffPersistenceError(null)
    try {
      const savedTariff = backendTariff
        ? await dictionaryClient.updateTariff(auth.accessToken, backendTariff.id, request)
        : await dictionaryClient.createTariff(auth.accessToken, request)
      const nextTariffs = backendTariffs.some((tariff) => tariff.id === savedTariff.id)
        ? backendTariffs.map((tariff) => (tariff.id === savedTariff.id ? savedTariff : tariff))
        : [...backendTariffs, savedTariff]
      const mergedRows = mergeTariffsIntoPrototypeRows(nextRows, nextTariffs)
      setBackendTariffs(nextTariffs)
      setTariffRows(mergedRows)
      setTariffDrafts(createEditableDrafts(mergedRows))
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось сохранить тариф.')
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function persistOneTimeRow(row: ContractorOneTimeRow, overrides: Partial<Pick<ContractorOneTimeRow, 'amount' | 'isActive'>> = {}) {
    if (!canManageTariffs) {
      return null
    }

    const amountText = overrides.amount ?? row.amount
    const amount = parsePrototypeAmount(amountText)
    if (amount == null) {
      setOneTimeActionMessage(`Укажите корректную сумму для нерегулярного платежа "${row.name}".`)
      return null
    }

    const request: UpsertIrregularPaymentRequest = {
      name: row.name,
      amount,
      isActive: overrides.isActive ?? row.isActive,
    }

    setOneTimeSavingRowId(row.id)
    setTariffPersistenceError(null)
    setOneTimeActionMessage(null)
    try {
      const savedPayment = row.backendPaymentId
        ? await dictionaryClient.updateIrregularPayment(auth.accessToken, row.backendPaymentId, request)
        : await dictionaryClient.createIrregularPayment(auth.accessToken, request)
      const nextRows = oneTimeRows.map((currentRow) => (
        currentRow.id === row.id
          ? {
            ...currentRow,
            backendPaymentId: savedPayment.id,
            amount: formatPrototypeAmount(savedPayment.amount),
            isActive: savedPayment.isActive,
            isDeleted: savedPayment.isArchived,
            isUsed: savedPayment.isUsed,
          }
          : currentRow
      ))
      setOneTimeRows(nextRows)
      setOneTimeDrafts(createEditableDrafts(nextRows))
      return savedPayment
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить нерегулярный платеж.'
      setOneTimeActionMessage(message)
      return null
    } finally {
      setOneTimeSavingRowId(null)
    }
  }

  async function persistOneTimeStatus(row: ContractorOneTimeRow, isActive: boolean) {
    if (!canManageTariffs) {
      return null
    }

    if (!row.backendPaymentId) {
      return persistOneTimeRow(row, { amount: row.amount || '0', isActive })
    }

    setOneTimeSavingRowId(row.id)
    setTariffPersistenceError(null)
    setOneTimeActionMessage(null)
    try {
      const savedPayment = await dictionaryClient.setIrregularPaymentStatus(auth.accessToken, row.backendPaymentId, {
        isActive,
        reason: isActive ? 'Активация через меню нерегулярных платежей' : 'Деактивация через меню нерегулярных платежей',
      })
      const nextRows = oneTimeRows.map((currentRow) => (
        currentRow.id === row.id
          ? {
            ...currentRow,
            amount: formatPrototypeAmount(savedPayment.amount),
            isActive: savedPayment.isActive,
            isDeleted: savedPayment.isArchived,
            isUsed: savedPayment.isUsed,
          }
          : currentRow
      ))
      setOneTimeRows(nextRows)
      setOneTimeDrafts(createEditableDrafts(nextRows))
      return savedPayment
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось изменить статус нерегулярного платежа.'
      setOneTimeActionMessage(message)
      return null
    } finally {
      setOneTimeSavingRowId(null)
    }
  }

  const commitTariffTextChange = async (row: ContractorTariffRow, field: 'title' | 'amount' | 'unit') => {
    const nextValue = (tariffDrafts[row.id]?.[field] ?? '').trim()
    const previousValue = row[field] ?? ''

    if (nextValue.trim() === previousValue.trim()) {
      return
    }

    if (!previousValue.trim()) {
      const nextRows = tariffRows.map((currentRow) => (
        currentRow.id === row.id ? { ...currentRow, [field]: nextValue } : currentRow
      ))
      setTariffRows(nextRows)
      setTariffDrafts((drafts) => ({
        ...drafts,
        [row.id]: {
          ...drafts[row.id],
          [field]: nextValue,
        },
      }))
      if (row.backendServiceSettingId || field !== 'unit') {
        await persistTariffRow(row, nextRows)
      }
      return
    }

    setPendingChange({
      kind: 'tariff-text',
      rowId: row.id,
      field,
      objectName: `${row.category}: ${row.title}`,
      fieldLabel: getTariffTextFieldLabel(row, field),
      previousValue,
      nextValue,
    })
  }

  const commitTariffDateChange = async (row: ContractorTariffRow) => {
    const draft = tariffDrafts[row.id] ?? { title: row.title, amount: '', unit: '', dateDay: '', dateMonth: row.dateMonth ?? '' }
    const nextDay = (draft.dateDay ?? '').trim().padStart(2, '0')
    const nextMonth = draft.dateMonth || row.dateMonth || contractorTariffMonthOptions[0].value
    const dateError = getContractorTariffDateError(nextDay, nextMonth)

    if (dateError) {
      setTariffDateErrors((errors) => ({ ...errors, [row.id]: dateError }))
      return
    }

    setTariffDateErrors((errors) => {
      const nextErrors = { ...errors }
      delete nextErrors[row.id]
      return nextErrors
    })

    const previousValue = formatContractorTariffDate(row.dateDay ?? '', row.dateMonth ?? '')
    const nextValue = formatContractorTariffDate(nextDay, nextMonth)

    if (nextValue === previousValue) {
      setTariffDrafts((drafts) => ({
        ...drafts,
        [row.id]: {
          ...drafts[row.id],
          dateDay: nextDay,
          dateMonth: nextMonth,
        },
      }))
      return
    }

    if (!previousValue.trim()) {
      const nextRows = tariffRows.map((currentRow) => (
        currentRow.id === row.id ? { ...currentRow, dateDay: nextDay, dateMonth: nextMonth } : currentRow
      ))
      setTariffRows(nextRows)
      setTariffDrafts((drafts) => ({
        ...drafts,
        [row.id]: {
          ...drafts[row.id],
          dateDay: nextDay,
          dateMonth: nextMonth,
        },
      }))
      if (row.backendServiceSettingId) {
        await persistTariffRow(row, nextRows)
      }
      return
    }

    setPendingChange({
      kind: 'tariff-date',
      rowId: row.id,
      objectName: `${row.category}: ${row.title}`,
      fieldLabel: 'Значение',
      previousValue,
      nextValue,
      nextDay,
      nextMonth,
    })
  }

  const commitTariffBooleanChange = (row: ContractorTariffRow, field: 'tiered' | 'byMeter', nextValue: boolean) => {
    const previousValue = row[field]

    if (previousValue === nextValue) {
      return
    }

    setPendingChange({
      kind: 'tariff-boolean',
      rowId: row.id,
      field,
      objectName: `${row.category}: ${row.title}`,
      fieldLabel: field === 'tiered' ? 'Пороговая тарификация' : 'По счетчику',
      previousValue: previousValue ? 'Да' : 'Нет',
      nextValue: nextValue ? 'Да' : 'Нет',
    })
  }

  const commitOneTimeAmountChange = async (row: ContractorOneTimeRow) => {
    const nextValue = (oneTimeDrafts[row.id]?.amount ?? '').trim()

    if (nextValue.trim() === row.amount.trim()) {
      return
    }

    if (!row.amount.trim()) {
      const nextRows = oneTimeRows.map((currentRow) => (
        currentRow.id === row.id ? { ...currentRow, amount: nextValue } : currentRow
      ))
      setOneTimeRows(nextRows)
      setOneTimeDrafts((drafts) => ({
        ...drafts,
        [row.id]: {
          ...drafts[row.id],
          amount: nextValue,
        },
      }))
      await persistOneTimeRow(row, { amount: nextValue })
      return
    }

    setPendingChange({
      kind: 'one-time-amount',
      rowId: row.id,
      objectName: row.name,
      fieldLabel: 'Сумма, руб.',
      previousValue: row.amount,
      nextValue,
    })
  }

  const openOneTimeContextMenu = (event: MouseEvent<HTMLDivElement>, row: ContractorOneTimeRow) => {
    event.preventDefault()
    if (row.isDeleted) {
      return
    }

    setOneTimeActionMessage(null)
    setOneTimeContextMenu({ row, x: event.clientX, y: event.clientY })
  }

  const toggleOneTimeActive = (row: ContractorOneTimeRow) => {
    setOneTimeContextMenu(null)
    setPendingChange({
      kind: 'one-time-active',
      rowId: row.id,
      objectName: row.name,
      fieldLabel: 'Статус',
      previousValue: row.isActive ? 'Активен' : 'Деактивирован',
      nextValue: row.isActive ? 'Деактивирован' : 'Активен',
    })
  }

  const openOneTimeDeleteDialog = (row: ContractorOneTimeRow) => {
    if (row.isDeleted) {
      return
    }

    setOneTimeContextMenu(null)
    if (row.isUsed) {
      setOneTimeActionMessage(`Удаление недоступно: нерегулярный платеж "${row.name}" уже используется в платежах или начислениях.`)
      return
    }

    setOneTimeActionMessage(null)
    setOneTimeDeleteTarget(row)
    setOneTimeDeleteReason('')
  }

  const confirmOneTimeDelete = async () => {
    if (!oneTimeDeleteTarget || !oneTimeDeleteReason.trim()) {
      return
    }

    if (!oneTimeDeleteTarget.backendPaymentId) {
      setOneTimeRows((currentRows) => currentRows.filter((currentRow) => currentRow.id !== oneTimeDeleteTarget.id))
      closeOneTimeDeleteDialog()
      return
    }

    setOneTimeSavingRowId(oneTimeDeleteTarget.id)
    setOneTimeActionMessage(null)
    try {
      await dictionaryClient.archiveIrregularPayment(auth.accessToken, oneTimeDeleteTarget.backendPaymentId, oneTimeDeleteReason.trim())
      setOneTimeRows((currentRows) => currentRows.map((currentRow) => (
        currentRow.id === oneTimeDeleteTarget.id ? { ...currentRow, isDeleted: true } : currentRow
      )))
      closeOneTimeDeleteDialog()
    } catch (caught) {
      const message = caught instanceof DictionaryApiError && caught.code === 'irregular_payment_used'
        ? 'Удаление недоступно: нерегулярный платеж уже используется в платежах или начислениях.'
        : caught instanceof Error ? caught.message : 'Не удалось удалить нерегулярный платеж.'
      setOneTimeActionMessage(message)
    } finally {
      setOneTimeSavingRowId(null)
    }
  }

  const confirmOneTimeRestore = async () => {
    if (!oneTimeRestoreTarget?.backendPaymentId) {
      closeOneTimeRestoreDialog()
      return
    }

    setOneTimeSavingRowId(oneTimeRestoreTarget.id)
    setOneTimeActionMessage(null)
    try {
      const restoredPayment = await dictionaryClient.restoreIrregularPayment(auth.accessToken, oneTimeRestoreTarget.backendPaymentId)
      const nextRows = oneTimeRows.map((currentRow) => (
        currentRow.id === oneTimeRestoreTarget.id
          ? {
            ...currentRow,
            backendPaymentId: restoredPayment.id,
            amount: formatPrototypeAmount(restoredPayment.amount),
            isActive: restoredPayment.isActive,
            isDeleted: restoredPayment.isArchived,
            isUsed: restoredPayment.isUsed,
          }
          : currentRow
      ))
      setOneTimeRows(nextRows)
      setOneTimeDrafts(createEditableDrafts(nextRows))
      setOneTimeActionMessage(`Нерегулярный платеж "${restoredPayment.name}" возвращен.`)
      closeOneTimeRestoreDialog()
    } catch (caught) {
      const message = caught instanceof DictionaryApiError && caught.code === 'irregular_payment_duplicate'
        ? 'Восстановление недоступно: активный нерегулярный платеж с таким наименованием уже существует.'
        : caught instanceof Error ? caught.message : 'Не удалось восстановить нерегулярный платеж.'
      setOneTimeActionMessage(message)
    } finally {
      setOneTimeSavingRowId(null)
    }
  }

  async function createServiceSetting(request: UpsertChargeServiceSettingRequest) {
    if (!canManageTariffs) {
      return
    }

    setTariffSavingRowId('new-service')
    setTariffPersistenceError(null)
    try {
      const savedSetting = await dictionaryClient.createChargeServiceSetting(auth.accessToken, request)
      const nextSettings = [...backendChargeServices.filter((setting) => setting.id !== savedSetting.id), savedSetting]
      const nextRows = mergeChargeServicesIntoPrototypeRows(tariffRows, nextSettings)
      setBackendChargeServices(nextSettings)
      setTariffRows(nextRows)
      setTariffDrafts(createEditableDrafts(nextRows))
      setModal(null)
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось добавить услугу.')
      throw caught
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function createFeeCampaign(request: UpsertFeeCampaignRequest) {
    if (!canManageTariffs) {
      return
    }

    setFeeCampaignSavingId('new-fee-campaign')
    setTariffPersistenceError(null)
    setFeeCampaignActionMessage(null)
    try {
      const savedCampaign = await dictionaryClient.createFeeCampaign(auth.accessToken, request)
      setFeeCampaigns((currentCampaigns) => [savedCampaign, ...currentCampaigns.filter((campaign) => campaign.id !== savedCampaign.id)])
      setModal(null)
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось объявить сбор.')
      throw caught
    } finally {
      setFeeCampaignSavingId(null)
    }
  }

  async function archiveChargeServiceSetting() {
    if (!chargeServiceArchiveTarget || !chargeServiceArchiveReason.trim()) {
      return
    }

    setTariffSavingRowId(`charge-service-${chargeServiceArchiveTarget.id}`)
    setTariffPersistenceError(null)
    try {
      await dictionaryClient.archiveChargeServiceSetting(auth.accessToken, chargeServiceArchiveTarget.id, chargeServiceArchiveReason.trim())
      const nextSettings = backendChargeServices.map((setting) => (
        setting.id === chargeServiceArchiveTarget.id ? { ...setting, isArchived: true } : setting
      ))
      const nextRows = mergeChargeServicesIntoPrototypeRows(tariffRows, nextSettings)
      setBackendChargeServices(nextSettings)
      setTariffRows(nextRows)
      setTariffDrafts(createEditableDrafts(nextRows))
      closeChargeServiceArchiveDialog()
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось архивировать услугу.')
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function restoreChargeServiceSetting() {
    if (!chargeServiceRestoreTarget) {
      return
    }

    setTariffSavingRowId(`charge-service-${chargeServiceRestoreTarget.id}`)
    setTariffPersistenceError(null)
    try {
      const restoredSetting = await dictionaryClient.restoreChargeServiceSetting(auth.accessToken, chargeServiceRestoreTarget.id)
      const nextSettings = backendChargeServices.map((setting) => (
        setting.id === restoredSetting.id ? restoredSetting : setting
      ))
      const nextRows = mergeChargeServicesIntoPrototypeRows(tariffRows, nextSettings)
      setBackendChargeServices(nextSettings)
      setTariffRows(nextRows)
      setTariffDrafts(createEditableDrafts(nextRows))
      closeChargeServiceRestoreDialog()
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось восстановить услугу.')
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function updateFeeCampaign(request: UpsertFeeCampaignRequest) {
    if (!canManageTariffs || !feeCampaignEditTarget) {
      return
    }

    setFeeCampaignSavingId(feeCampaignEditTarget.id)
    setTariffPersistenceError(null)
    setFeeCampaignActionMessage(null)
    try {
      const savedCampaign = await dictionaryClient.updateFeeCampaign(auth.accessToken, feeCampaignEditTarget.id, request)
      setFeeCampaigns((currentCampaigns) => currentCampaigns.map((campaign) => (
        campaign.id === savedCampaign.id ? savedCampaign : campaign
      )))
      closeFeeCampaignEditDialog()
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось изменить сбор.')
      throw caught
    } finally {
      setFeeCampaignSavingId(null)
    }
  }

  async function archiveFeeCampaign() {
    if (!feeCampaignArchiveTarget || !feeCampaignArchiveReason.trim()) {
      return
    }

    setFeeCampaignSavingId(feeCampaignArchiveTarget.id)
    setFeeCampaignActionMessage(null)
    try {
      await dictionaryClient.archiveFeeCampaign(auth.accessToken, feeCampaignArchiveTarget.id, feeCampaignArchiveReason.trim())
      setFeeCampaigns((currentCampaigns) => currentCampaigns.map((campaign) => (
        campaign.id === feeCampaignArchiveTarget.id ? { ...campaign, isArchived: true } : campaign
      )))
      closeFeeCampaignArchiveDialog()
    } catch (caught) {
      setFeeCampaignActionMessage(caught instanceof Error ? caught.message : 'Не удалось архивировать сбор.')
    } finally {
      setFeeCampaignSavingId(null)
    }
  }

  async function restoreFeeCampaign() {
    if (!feeCampaignRestoreTarget) {
      return
    }

    setFeeCampaignSavingId(feeCampaignRestoreTarget.id)
    setFeeCampaignActionMessage(null)
    try {
      const restoredCampaign = await dictionaryClient.restoreFeeCampaign(auth.accessToken, feeCampaignRestoreTarget.id)
      setFeeCampaigns((currentCampaigns) => currentCampaigns.map((currentCampaign) => (
        currentCampaign.id === restoredCampaign.id ? restoredCampaign : currentCampaign
      )))
      closeFeeCampaignRestoreDialog()
    } catch (caught) {
      setFeeCampaignActionMessage(caught instanceof Error ? caught.message : 'Не удалось восстановить сбор.')
    } finally {
      setFeeCampaignSavingId(null)
    }
  }

  function confirmThresholdDelete() {
    if (!thresholdDeleteTarget || !thresholdDeleteReason.trim()) {
      return
    }

    const nextRows = tariffRows.filter((row) => row.id !== thresholdDeleteTarget.id)
    setTariffRows(nextRows)
    setTariffDrafts((drafts) => {
      const nextDrafts = { ...drafts }
      delete nextDrafts[thresholdDeleteTarget.id]
      return nextDrafts
    })
    closeThresholdDeleteDialog()
  }

  async function generateFeeCampaignAccruals() {
    if (!feeCampaignGenerateTarget) {
      return
    }

    setFeeCampaignSavingId(feeCampaignGenerateTarget.id)
    setFeeCampaignActionMessage(null)
    try {
      const result = await financeClient.generateFeeCampaignAccruals(auth.accessToken, {
        feeCampaignId: feeCampaignGenerateTarget.id,
        accountingMonth: `${feeCampaignGenerateMonth}-01`,
        comment: feeCampaignGenerateComment.trim() || undefined,
      })
      setFeeCampaignActionMessage(`Создано начислений: ${result.createdCount}; сумма: ${formatMoney(result.totalAmount)} руб.; пропущено: ${result.skippedCount}.`)
      closeFeeCampaignGenerateDialog()
    } catch (caught) {
      setFeeCampaignActionMessage(caught instanceof Error ? caught.message : 'Не удалось начислить сбор.')
    } finally {
      setFeeCampaignSavingId(null)
    }
  }

  const addElectricityThreshold = () => {
    const electricityThresholdRows = tariffRows.filter((row) => row.category === 'Электроэнергия' && row.threshold)
    const nextIndex = electricityThresholdRows.length + 1
    const nextRow: ContractorTariffRow = {
      id: `electricity-tier-custom-${Date.now()}`,
      category: 'Электроэнергия',
      title: `Порог ${nextIndex}`,
      threshold: 'x',
      amount: '',
      unit: 'руб.',
      byMeter: true,
      tiered: true,
    }

    setTariffRows((currentRows) => {
      const overdueIndex = currentRows.findIndex((row) => row.id === 'electricity-overdue-days')
      if (overdueIndex < 0) {
        return [...currentRows, nextRow]
      }

      return [
        ...currentRows.slice(0, overdueIndex),
        nextRow,
        ...currentRows.slice(overdueIndex),
      ]
    })
    setTariffDrafts((drafts) => ({ ...drafts, [nextRow.id]: { title: nextRow.title, amount: '', unit: nextRow.unit ?? '', dateDay: '', dateMonth: '' } }))
  }

  const lastElectricityThresholdRowId = [...tariffRows]
    .reverse()
    .find((row) => row.category === 'Электроэнергия' && row.threshold)?.id

  function formatFeeCampaignParticipantSummary(campaign: FeeCampaignDto) {
    if (campaign.appliesToAllGarages) {
      return 'Все гаражи'
    }

    const selectedNumbers = campaign.participantGarageIds
      .map((garageId) => feeCampaignGarageOptions.find((garage) => garage.id === garageId)?.number)
      .filter((number): number is string => Boolean(number))
      .sort((left, right) => left.localeCompare(right, 'ru', { numeric: true }))

    if (selectedNumbers.length === 0) {
      return `${campaign.participantGarageIds.length} выбрано`
    }

    const visibleNumbers = selectedNumbers.slice(0, 4).join(', ')
    return selectedNumbers.length > 4 ? `${visibleNumbers} и еще ${selectedNumbers.length - 4}` : visibleNumbers
  }

  return (
    <section className="contractors-page" aria-label="Тарифы и сборы">
      <div className="contractors-heading">
        <div>
          <h1>Тарифы и сборы</h1>
          {tariffsLoading ? <p className="form-hint" role="status">Загружаем тарифы...</p> : null}
          {!canManageTariffs ? <p className="form-hint">Режим просмотра: для изменения тарифов нужно право tariffs.manage.</p> : null}
          {tariffPersistenceError ? <FormError>{tariffPersistenceError}</FormError> : null}
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

      <>
        <div className="contractors-sheet" role="table" aria-label="Тарифы и сборы">
            <div className="contractors-sheet-header" role="row">
              <span role="columnheader">Основание</span>
              <span role="columnheader">Значение</span>
              <span role="columnheader">Ед.</span>
              <span role="columnheader">Пороговая тарификация</span>
              <span role="columnheader">По счетчику</span>
            </div>
            {tariffRows.map((row) => {
              const serviceSetting = row.backendServiceSettingId
                ? backendChargeServices.find((setting) => setting.id === row.backendServiceSettingId) ?? null
                : null
              const isServiceSaving = Boolean(serviceSetting && tariffSavingRowId === `charge-service-${serviceSetting.id}`)
              const isRowDisabled = row.isDeleted || tariffSavingRowId === row.id || isServiceSaving
              const isCustomThreshold = Boolean(row.threshold && row.id.startsWith('electricity-tier-custom-'))

              return (
                <Fragment key={row.id}>
                <div
                  className={[
                    row.group ? 'contractors-sheet-row contractors-sheet-row--group' : 'contractors-sheet-row',
                    row.isDeleted ? 'contractors-sheet-row--deleted' : '',
                  ].filter(Boolean).join(' ')}
                  role="row"
                >
                  <span role="cell">
                    {row.group ? <strong>{row.group}</strong> : null}
                    {row.threshold ? (
                      <input
                        aria-label={`${row.category}: ${row.title}: наименование`}
                        className="contractors-editable-input contractors-editable-input--title"
                        disabled={!canManageTariffs || isRowDisabled}
                        value={tariffDrafts[row.id]?.title ?? row.title}
                        onChange={(event) => setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], title: event.target.value } }))}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffTextChange(row, 'title'))}
                      />
                    ) : (
                      <span>{row.title}</span>
                    )}
                    {row.serviceSettingKind === 'main' && serviceSetting ? (
                      <span className="contractors-sheet-row-actions">
                        {row.isDeleted ? (
                          <button
                            className="ghost-button"
                            type="button"
                            aria-label={`Вернуть услугу ${serviceSetting.name}`}
                            disabled={!canManageTariffs || isServiceSaving}
                            onClick={() => setChargeServiceRestoreTarget(serviceSetting)}
                          >
                            <RotateCcw size={15} />
                            <span>Вернуть</span>
                          </button>
                        ) : (
                          <button
                            className="icon-button danger-button"
                            type="button"
                            aria-label={`Архивировать услугу ${serviceSetting.name}`}
                            disabled={!canManageTariffs || isServiceSaving}
                            onClick={() => {
                              setChargeServiceArchiveTarget(serviceSetting)
                              setChargeServiceArchiveReason('')
                            }}
                          >
                            <Trash2 size={16} />
                          </button>
                        )}
                      </span>
                    ) : null}
                    {isCustomThreshold ? (
                      <span className="contractors-sheet-row-actions">
                        <button
                          className="icon-button danger-button"
                          type="button"
                          aria-label={`Удалить порог ${row.title}`}
                          disabled={!canManageTariffs || isRowDisabled}
                          onClick={() => {
                            setThresholdDeleteTarget(row)
                            setThresholdDeleteReason('')
                          }}
                        >
                          <Trash2 size={16} />
                        </button>
                      </span>
                    ) : null}
                  </span>
                  <span role="cell" className="contractors-value-cell">
                    {row.threshold ? <em>{row.threshold}</em> : null}
                    {row.dateDay !== undefined ? (
                      <div className="contractors-date-field">
                        <input
                          aria-label={`${row.category}: ${row.title}: день`}
                          aria-invalid={Boolean(tariffDateErrors[row.id])}
                          aria-describedby={tariffDateErrors[row.id] ? `${row.id}-date-error` : undefined}
                          className="contractors-editable-input contractors-editable-input--day"
                          disabled={!canManageTariffs || isRowDisabled}
                          inputMode="numeric"
                          maxLength={2}
                          value={tariffDrafts[row.id]?.dateDay ?? ''}
                          onChange={(event) => {
                            setTariffDateErrors((errors) => {
                              const nextErrors = { ...errors }
                              delete nextErrors[row.id]
                              return nextErrors
                            })
                            setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], dateDay: event.target.value } }))
                          }}
                          onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffDateChange(row))}
                        />
                        {tariffDateErrors[row.id] ? <span id={`${row.id}-date-error`} className="contractors-field-error" role="alert">{tariffDateErrors[row.id]}</span> : null}
                      </div>
                    ) : (
                      <input
                        aria-label={`${row.category}: ${row.title}: значение`}
                        className="contractors-editable-input"
                        disabled={!canManageTariffs || isRowDisabled}
                        value={tariffDrafts[row.id]?.amount ?? ''}
                        onChange={(event) => setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], amount: event.target.value } }))}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffTextChange(row, 'amount'))}
                      />
                    )}
                  </span>
                  <span role="cell">
                    {row.dateDay !== undefined ? (
                      <select
                        aria-label={`${row.category}: ${row.title}: месяц`}
                        className="contractors-editable-select contractors-editable-select--month"
                        disabled={!canManageTariffs || isRowDisabled}
                        value={tariffDrafts[row.id]?.dateMonth ?? row.dateMonth ?? contractorTariffMonthOptions[0].value}
                        onChange={(event) => {
                          setTariffDateErrors((errors) => {
                            const nextErrors = { ...errors }
                            delete nextErrors[row.id]
                            return nextErrors
                          })
                          setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], dateMonth: event.target.value } }))
                        }}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffDateChange(row))}
                      >
                        {contractorTariffMonthOptions.map((month) => (
                          <option key={month.value} value={month.value}>{month.label}</option>
                        ))}
                      </select>
                    ) : (
                      <input
                        aria-label={`${row.category}: ${row.title}: единица`}
                        className="contractors-editable-input contractors-editable-input--unit"
                        disabled={!canManageTariffs || isRowDisabled}
                        value={tariffDrafts[row.id]?.unit ?? ''}
                        onChange={(event) => setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], unit: event.target.value } }))}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffTextChange(row, 'unit'))}
                      />
                    )}
                  </span>
                  <span role="cell">
                    <select
                      aria-label={`${row.category}: ${row.title}: пороговая тарификация`}
                      className="contractors-editable-select"
                      disabled={!canManageTariffs || isRowDisabled}
                      value={row.tiered ? 'Да' : 'Нет'}
                    onChange={(event) => commitTariffBooleanChange(row, 'tiered', event.target.value === 'Да')}
                    >
                      <option>Да</option>
                      <option>Нет</option>
                    </select>
                  </span>
                  <span role="cell">
                    <select
                      aria-label={`${row.category}: ${row.title}: по счетчику`}
                      className="contractors-editable-select"
                      disabled={!canManageTariffs || isRowDisabled}
                      value={row.byMeter ? 'Да' : 'Нет'}
                    onChange={(event) => commitTariffBooleanChange(row, 'byMeter', event.target.value === 'Да')}
                    >
                      <option>Да</option>
                      <option>Нет</option>
                    </select>
                  </span>
                </div>
                {row.id === lastElectricityThresholdRowId ? (
                  <div className="contractors-sheet-row contractors-sheet-action-row" role="row">
                    <span role="cell">
                      <button className="link-button" type="button" onClick={addElectricityThreshold} disabled={!canManageTariffs}>
                        Добавить порог
                      </button>
                    </span>
                    <span role="cell" />
                    <span role="cell" />
                    <span role="cell" />
                    <span role="cell" />
                  </div>
                ) : null}
              </Fragment>
              )
            })}
            {tariffRows.length === 0 && !tariffsLoading ? (
              <div className="contractors-sheet-row contractors-sheet-action-row" role="row">
                <span role="cell">Тарифы и услуги пока не настроены.</span>
                <span role="cell" />
                <span role="cell" />
                <span role="cell" />
                <span role="cell" />
              </div>
            ) : null}
          </div>

          <div className="contractors-bottom-grid">
            <section className="contractors-mini-table" aria-label="Нерегулярные платежи">
              <div className="contractors-mini-title">Нерегулярные платежи</div>
              {oneTimeActionMessage ? <p className="contractors-action-message" role="alert">{oneTimeActionMessage}</p> : null}
              <div className="contractors-mini-header contractors-mini-header--editable">
                <span>Основание</span>
                <span>Сумма, руб.</span>
              </div>
              {oneTimeRows.map((row) => (
                <div
                  aria-label={`Нерегулярный платеж ${row.name}`}
                  className={[
                    'contractors-mini-row contractors-mini-row--editable',
                    row.isDeleted ? 'contractors-mini-row--deleted' : '',
                    !row.isActive ? 'contractors-mini-row--inactive' : '',
                  ].filter(Boolean).join(' ')}
                  key={row.id}
                  onContextMenu={(event) => openOneTimeContextMenu(event, row)}
                >
                  <span>{row.name}</span>
                  <span>
                    {row.isDeleted ? (
                      <span className="contractors-mini-actions">
                        <span>{row.amount}</span>
                        <button className="ghost-button" type="button" disabled={!canManageTariffs || oneTimeSavingRowId === row.id} onClick={() => setOneTimeRestoreTarget(row)}>
                          <RotateCcw size={16} />
                          <span>Вернуть</span>
                        </button>
                      </span>
                    ) : (
                      <input
                        aria-label={`Сумма: ${row.name}`}
                        className="contractors-editable-input"
                        disabled={!canManageTariffs || !row.isActive || oneTimeSavingRowId === row.id}
                        value={oneTimeDrafts[row.id]?.amount ?? ''}
                        onChange={(event) => setOneTimeDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], amount: event.target.value } }))}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitOneTimeAmountChange(row))}
                      />
                    )}
                  </span>
                </div>
              ))}
              {oneTimeRows.length === 0 && !tariffsLoading ? <p className="form-hint">Нерегулярные платежи пока не настроены.</p> : null}
            </section>

            <section className="contractors-mini-table" aria-label="Объявленные сборы">
              <div className="contractors-mini-title">Объявленные сборы</div>
              {feeCampaignActionMessage ? <p className="contractors-action-message" role="alert">{feeCampaignActionMessage}</p> : null}
              <div className="contractors-mini-header contractors-mini-header--fees">
                <span>Наименование</span>
                <span>Взнос</span>
                <span>План</span>
                <span>Участники</span>
                <span>Период</span>
                <span>Действия</span>
              </div>
              {feeCampaigns.map((campaign) => (
                <div
                  aria-label={`Объявленный сбор ${campaign.name}`}
                  className={[
                    'contractors-mini-row contractors-mini-row--fees',
                    campaign.isArchived ? 'contractors-mini-row--deleted' : '',
                  ].filter(Boolean).join(' ')}
                  key={campaign.id}
                >
                  <span>
                    <strong>{campaign.name}</strong>
                    <small>{campaign.incomeTypeName}{campaign.goal ? ` · ${campaign.goal}` : ''}</small>
                  </span>
                  <span>{formatMoney(campaign.contributionAmount)}</span>
                  <span>{formatMoney(campaign.targetAmount)}</span>
                  <span>{formatFeeCampaignParticipantSummary(campaign)}</span>
                  <span>{formatDateOnly(campaign.startsOn)}{campaign.endsOn ? ` - ${formatDateOnly(campaign.endsOn)}` : ''}</span>
                  <span className="contractors-mini-actions">
                    {campaign.isArchived ? (
                      <button className="ghost-button" type="button" disabled={!canManageTariffs || feeCampaignSavingId === campaign.id} onClick={() => setFeeCampaignRestoreTarget(campaign)}>
                        <RotateCcw size={16} />
                        <span>Вернуть</span>
                      </button>
                    ) : (
                      <>
                        <button className="ghost-button" type="button" disabled={!canManageTariffs || feeCampaignSavingId === campaign.id} onClick={() => {
                          setFeeCampaignGenerateTarget(campaign)
                          setFeeCampaignGenerateMonth(getCurrentMonthInputValue())
                          setFeeCampaignGenerateComment('')
                        }}>
                          <Plus size={16} />
                          <span>Начислить</span>
                        </button>
                        <button className="icon-button" type="button" aria-label={`Изменить сбор ${campaign.name}`} disabled={!canManageTariffs || feeCampaignSavingId === campaign.id} onClick={() => setFeeCampaignEditTarget(campaign)}>
                          <Pencil size={16} />
                        </button>
                        <button className="icon-button" type="button" aria-label={`Архивировать сбор ${campaign.name}`} disabled={!canManageTariffs || feeCampaignSavingId === campaign.id} onClick={() => {
                          setFeeCampaignArchiveTarget(campaign)
                          setFeeCampaignArchiveReason('')
                        }}>
                          <Trash2 size={16} />
                        </button>
                      </>
                    )}
                  </span>
                </div>
              ))}
              {feeCampaigns.length === 0 && !tariffsLoading ? <p className="form-hint">Объявленные сборы пока не настроены.</p> : null}
            </section>
          </div>
      </>

      {oneTimeContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setOneTimeContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия нерегулярного платежа ${oneTimeContextMenu.row.name}`}
            style={{ left: oneTimeContextMenu.x, top: oneTimeContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            <button type="button" role="menuitem" onClick={() => toggleOneTimeActive(oneTimeContextMenu.row)}>
              {oneTimeContextMenu.row.isActive ? 'Деактивировать' : 'Активировать'}
            </button>
            <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openOneTimeDeleteDialog(oneTimeContextMenu.row)}>
              <span>Удалить</span>
            </button>
          </div>
        </div>
      ) : null}

      {pendingChange ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={cancelPendingChange}>
          <section ref={changeDialogRef} className="detail-dialog contractors-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="tariff-prototype-change-title" aria-describedby="tariff-prototype-change-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Изменение</p>
                <h3 id="tariff-prototype-change-title">Подтвердить изменение?</h3>
                <p>{pendingChange.objectName}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение изменения тарифа" onClick={cancelPendingChange}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="tariff-prototype-change-description">Проверьте, что именно изменится. Действие записывается в историю изменений.</p>
            <div className="tariff-change-summary" aria-label="Изменяемое поле тарифа">
              <div className="tariff-change-field-row">
                <span>Поле</span>
                <strong>{pendingChange.fieldLabel}</strong>
              </div>
              <div className="tariff-change-values-row">
                <div>
                  <span>Было</span>
                  <strong>{formatPrototypeChangeValue(pendingChange.previousValue)}</strong>
                </div>
                <div>
                  <span>Стало</span>
                  <strong>{formatPrototypeChangeValue(pendingChange.nextValue)}</strong>
                </div>
              </div>
            </div>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={changeCancelRef} className="ghost-button" type="button" onClick={cancelPendingChange}>Отмена</button>
              <button className="secondary-button" type="button" onClick={confirmPendingChange} disabled={oneTimeSavingRowId === pendingChange.rowId || tariffSavingRowId === pendingChange.rowId}>
                <Save size={16} />
                <span>Сохранить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {thresholdDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeThresholdDeleteDialog}>
          <section ref={thresholdDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="threshold-delete-title" aria-describedby="threshold-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="threshold-delete-title">Удалить порог тарификации?</h3>
                <p>{thresholdDeleteTarget.title}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления порога" onClick={closeThresholdDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="threshold-delete-description">Порог будет удален из текущей настройки тарифов. Укажите причину, чтобы действие было понятным при проверке изменений.</p>
            <label className="field-label" htmlFor="threshold-delete-reason">Причина удаления</label>
            <textarea
              id="threshold-delete-reason"
              aria-label="Причина удаления порога"
              maxLength={1000}
              value={thresholdDeleteReason}
              onChange={(event) => setThresholdDeleteReason(event.target.value)}
              placeholder="Например: лишний порог добавлен ошибочно"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={thresholdDeleteCancelRef} className="ghost-button" type="button" onClick={closeThresholdDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={confirmThresholdDelete} disabled={!thresholdDeleteReason.trim()}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {oneTimeDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeOneTimeDeleteDialog}>
          <section ref={oneTimeDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="one-time-delete-title" aria-describedby="one-time-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="one-time-delete-title">Удалить нерегулярный платеж?</h3>
                <p>{oneTimeDeleteTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления нерегулярного платежа" onClick={closeOneTimeDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="one-time-delete-description">Платеж будет удален из списка нерегулярных платежей. Укажите причину, чтобы действие можно было проверить позже.</p>
            <label className="field-label" htmlFor="one-time-delete-reason">Причина удаления</label>
            <textarea
              id="one-time-delete-reason"
              aria-label="Причина удаления нерегулярного платежа"
              maxLength={1000}
              value={oneTimeDeleteReason}
              onChange={(event) => setOneTimeDeleteReason(event.target.value)}
              placeholder="Например: платеж больше не используется"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={oneTimeDeleteCancelRef} className="ghost-button" type="button" onClick={closeOneTimeDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={confirmOneTimeDelete} disabled={!oneTimeDeleteReason.trim() || oneTimeSavingRowId === oneTimeDeleteTarget.id}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {oneTimeRestoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeOneTimeRestoreDialog}>
          <section ref={oneTimeRestoreDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="one-time-restore-title" aria-describedby="one-time-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Восстановление</p>
                <h3 id="one-time-restore-title">Вернуть нерегулярный платеж?</h3>
                <p>{oneTimeRestoreTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение восстановления нерегулярного платежа" onClick={closeOneTimeRestoreDialog} disabled={oneTimeSavingRowId === oneTimeRestoreTarget.id}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="one-time-restore-description">Платеж снова появится в рабочих списках. Действие будет записано в историю изменений.</p>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={oneTimeRestoreCancelRef} className="ghost-button" type="button" onClick={closeOneTimeRestoreDialog} disabled={oneTimeSavingRowId === oneTimeRestoreTarget.id}>Отмена</button>
              <button className="secondary-button" type="button" onClick={confirmOneTimeRestore} disabled={oneTimeSavingRowId === oneTimeRestoreTarget.id}>
                <RotateCcw size={16} />
                <span>Вернуть</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {chargeServiceArchiveTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeChargeServiceArchiveDialog}>
          <section ref={chargeServiceArchiveDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="charge-service-archive-title" aria-describedby="charge-service-archive-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Архив</p>
                <h3 id="charge-service-archive-title">Архивировать услугу?</h3>
                <p>{chargeServiceArchiveTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение архивации услуги" onClick={closeChargeServiceArchiveDialog} disabled={tariffSavingRowId === `charge-service-${chargeServiceArchiveTarget.id}`}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="charge-service-archive-description">Услуга останется в справочнике как архивная и будет заблокирована для редактирования. Укажите причину для истории изменений.</p>
            <label className="field-label" htmlFor="charge-service-archive-reason">Причина архивации</label>
            <textarea
              id="charge-service-archive-reason"
              aria-label="Причина архивации услуги"
              maxLength={1000}
              value={chargeServiceArchiveReason}
              onChange={(event) => setChargeServiceArchiveReason(event.target.value)}
              placeholder="Например: услуга больше не используется"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={chargeServiceArchiveCancelRef} className="ghost-button" type="button" onClick={closeChargeServiceArchiveDialog} disabled={tariffSavingRowId === `charge-service-${chargeServiceArchiveTarget.id}`}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={archiveChargeServiceSetting} disabled={!chargeServiceArchiveReason.trim() || tariffSavingRowId === `charge-service-${chargeServiceArchiveTarget.id}`}>
                <Trash2 size={16} />
                <span>Архивировать</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {chargeServiceRestoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeChargeServiceRestoreDialog}>
          <section ref={chargeServiceRestoreDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="charge-service-restore-title" aria-describedby="charge-service-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Восстановление</p>
                <h3 id="charge-service-restore-title">Вернуть услугу?</h3>
                <p>{chargeServiceRestoreTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение восстановления услуги" onClick={closeChargeServiceRestoreDialog} disabled={tariffSavingRowId === `charge-service-${chargeServiceRestoreTarget.id}`}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="charge-service-restore-description">Услуга снова станет активной и доступной для редактирования в тарифах. Действие будет записано в историю изменений.</p>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={chargeServiceRestoreCancelRef} className="ghost-button" type="button" onClick={closeChargeServiceRestoreDialog} disabled={tariffSavingRowId === `charge-service-${chargeServiceRestoreTarget.id}`}>Отмена</button>
              <button className="secondary-button" type="button" onClick={restoreChargeServiceSetting} disabled={tariffSavingRowId === `charge-service-${chargeServiceRestoreTarget.id}`}>
                <RotateCcw size={16} />
                <span>Вернуть</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {feeCampaignArchiveTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeFeeCampaignArchiveDialog}>
          <section ref={feeCampaignArchiveDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="fee-campaign-archive-title" aria-describedby="fee-campaign-archive-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Архив</p>
                <h3 id="fee-campaign-archive-title">Архивировать сбор?</h3>
                <p>{feeCampaignArchiveTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение архивации сбора" onClick={closeFeeCampaignArchiveDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fee-campaign-archive-description">Сбор будет скрыт из активного списка, но его можно будет вернуть. Укажите причину для истории изменений.</p>
            <label className="field-label" htmlFor="fee-campaign-archive-reason">Причина архивации</label>
            <textarea
              id="fee-campaign-archive-reason"
              aria-label="Причина архивации сбора"
              maxLength={1000}
              value={feeCampaignArchiveReason}
              onChange={(event) => setFeeCampaignArchiveReason(event.target.value)}
              placeholder="Например: сбор больше не используется"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={feeCampaignArchiveCancelRef} className="ghost-button" type="button" onClick={closeFeeCampaignArchiveDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={archiveFeeCampaign} disabled={!feeCampaignArchiveReason.trim() || feeCampaignSavingId === feeCampaignArchiveTarget.id}>
                <Trash2 size={16} />
                <span>Архивировать</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {feeCampaignRestoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeFeeCampaignRestoreDialog}>
          <section ref={feeCampaignRestoreDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="fee-campaign-restore-title" aria-describedby="fee-campaign-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Восстановление</p>
                <h3 id="fee-campaign-restore-title">Вернуть сбор?</h3>
                <p>{feeCampaignRestoreTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение восстановления сбора" onClick={closeFeeCampaignRestoreDialog} disabled={feeCampaignSavingId === feeCampaignRestoreTarget.id}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fee-campaign-restore-description">Сбор снова появится как активный и будет доступен для начислений. Действие будет записано в историю изменений.</p>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={feeCampaignRestoreCancelRef} className="ghost-button" type="button" onClick={closeFeeCampaignRestoreDialog} disabled={feeCampaignSavingId === feeCampaignRestoreTarget.id}>Отмена</button>
              <button className="secondary-button" type="button" onClick={restoreFeeCampaign} disabled={feeCampaignSavingId === feeCampaignRestoreTarget.id}>
                <RotateCcw size={16} />
                <span>Вернуть</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {feeCampaignGenerateTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeFeeCampaignGenerateDialog}>
          <section ref={feeCampaignGenerateDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="fee-campaign-generate-title" aria-describedby="fee-campaign-generate-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Начисление</p>
                <h3 id="fee-campaign-generate-title">Начислить сбор?</h3>
                <p>{feeCampaignGenerateTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть форму начисления сбора" onClick={closeFeeCampaignGenerateDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fee-campaign-generate-description">Backend создаст начисления по активным гаражам и запишет действие в историю изменений.</p>
            <FormField label="Месяц начисления">
              <input aria-label="Месяц начисления сбора" type="month" value={feeCampaignGenerateMonth} onChange={(event) => setFeeCampaignGenerateMonth(event.target.value)} />
            </FormField>
            <FormField label="Комментарий">
              <textarea aria-label="Комментарий к начислению сбора" value={feeCampaignGenerateComment} onChange={(event) => setFeeCampaignGenerateComment(event.target.value)} placeholder="Например: начисление по решению правления" />
            </FormField>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={feeCampaignGenerateCancelRef} className="ghost-button" type="button" onClick={closeFeeCampaignGenerateDialog}>Отмена</button>
              <button className="secondary-button" type="button" onClick={generateFeeCampaignAccruals} disabled={!feeCampaignGenerateMonth || feeCampaignSavingId === feeCampaignGenerateTarget.id}>
                <Save size={16} />
                <span>Начислить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {modal === 'service' ? (
        <AddServicePrototypeDialog
          isSaving={tariffSavingRowId === 'new-service'}
          incomeTypes={backendIncomeTypes.filter((incomeType) => !incomeType.isArchived)}
          onClose={() => setModal(null)}
          onSave={createServiceSetting}
          tariffs={backendTariffs.filter((tariff) => !tariff.isArchived)}
          unitOptions={Array.from(new Set(tariffRows.map((row) => row.unit).filter((unit): unit is string => Boolean(unit))))}
        />
      ) : null}
      {modal === 'fee' ? (
        <AddFeePrototypeDialog
          garageOptions={feeCampaignGarageOptions}
          incomeTypes={backendIncomeTypes.filter((incomeType) => !incomeType.isArchived)}
          isSaving={feeCampaignSavingId === 'new-fee-campaign'}
          onClose={() => setModal(null)}
          onSave={createFeeCampaign}
        />
      ) : null}
      {feeCampaignEditTarget ? (
        <AddFeePrototypeDialog
          garageOptions={feeCampaignGarageOptions}
          incomeTypes={backendIncomeTypes.filter((incomeType) => !incomeType.isArchived || incomeType.id === feeCampaignEditTarget.incomeTypeId)}
          initialCampaign={feeCampaignEditTarget}
          isSaving={feeCampaignSavingId === feeCampaignEditTarget.id}
          onClose={closeFeeCampaignEditDialog}
          onSave={updateFeeCampaign}
          submitLabel="Сохранить"
          title="Изменить сбор"
        />
      ) : null}
    </section>
  )
}

function AddServicePrototypeDialog({
  isSaving,
  incomeTypes,
  onClose,
  onSave,
  tariffs,
  unitOptions,
}: {
  isSaving: boolean
  incomeTypes: AccountingTypeDto[]
  onClose: () => void
  onSave: (request: UpsertChargeServiceSettingRequest) => Promise<void>
  tariffs: TariffDto[]
  unitOptions: string[]
}) {
  const [name, setName] = useState('')
  const [isRegular, setIsRegular] = useState(false)
  const [incomeTypeId, setIncomeTypeId] = useState(incomeTypes[0]?.id ?? '')
  const [tariffId, setTariffId] = useState(() => chooseRegularTariffId(incomeTypes[0]?.id ?? '', '', incomeTypes, tariffs))
  const [isByMeter, setIsByMeter] = useState(true)
  const [isTiered, setIsTiered] = useState(true)
  const [periodicityMonths, setPeriodicityMonths] = useState('12')
  const [accrualStartMonth, setAccrualStartMonth] = useState(contractorTariffMonthOptions[0].value)
  const [paymentDueDay, setPaymentDueDay] = useState('30')
  const [paymentDueMonth, setPaymentDueMonth] = useState(contractorTariffMonthOptions[6].value)
  const [overdueGraceDays, setOverdueGraceDays] = useState('30')
  const [unitName, setUnitName] = useState(unitOptions[0] ?? 'руб.')
  const [error, setError] = useState<string | null>(null)
  const compatibleTariffs = getCompatibleRegularTariffs(incomeTypeId, incomeTypes, tariffs)
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useEscapeKey(true, onClose)

  async function submitService(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const trimmedName = name.trim()
    const parsedPeriodicity = Number(periodicityMonths)
    const parsedDueDay = Number(paymentDueDay)
    const parsedOverdueDays = Number(overdueGraceDays)
    const dueMonthOption = contractorTariffMonthOptions.find((month) => month.value === paymentDueMonth)

    if (!trimmedName) {
      setError('Укажите наименование услуги.')
      return
    }

    if (isRegular) {
      if (!incomeTypeId) {
        setError('Выберите вид начисления для регулярной услуги.')
        return
      }

      if (!tariffId) {
        setError('Выберите тариф для регулярной услуги.')
        return
      }

      if (!Number.isInteger(parsedPeriodicity) || parsedPeriodicity < 1 || parsedPeriodicity > 120) {
        setError('Периодичность должна быть числом от 1 до 120 месяцев.')
        return
      }

      if (!Number.isInteger(parsedDueDay) || !dueMonthOption || parsedDueDay < 1 || parsedDueDay > dueMonthOption.maxDay) {
        setError(`Для месяца "${dueMonthOption?.label ?? 'не выбран'}" укажите день от 1 до ${dueMonthOption?.maxDay ?? 31}.`)
        return
      }

      if (!Number.isInteger(parsedOverdueDays) || parsedOverdueDays < 0 || parsedOverdueDays > 366) {
        setError('Перенос долга должен быть числом от 0 до 366 дней.')
        return
      }
    }

    setError(null)
    await onSave({
      name: trimmedName,
      isRegular,
      periodicityMonths: isRegular ? parsedPeriodicity : null,
      accrualStartMonth: isRegular ? getContractorTariffMonthNumber(accrualStartMonth) ?? 1 : null,
      paymentDueDay: isRegular ? parsedDueDay : null,
      paymentDueMonth: isRegular ? getContractorTariffMonthNumber(paymentDueMonth) ?? 1 : null,
      overdueGraceDays: isRegular ? parsedOverdueDays : 0,
      isMetered: isByMeter,
      hasTieredTariff: isByMeter && isTiered,
      unitName: unitName.trim() || null,
      incomeTypeId: isRegular ? incomeTypeId : null,
      tariffId: isRegular ? tariffId : null,
    })
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog contractors-dialog contractors-tariff-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-service-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <h3 id="contractor-service-title">Добавить услугу</h3>
          <button className="icon-button" type="button" aria-label="Закрыть форму услуги" onClick={onClose}>
            <X size={18} />
          </button>
        </div>

        <form className="dictionary-modal-form contractors-modal-form" onSubmit={submitService}>
          {error ? <FormError>{error}</FormError> : null}
          <FormField label="Наименование услуги">
            <input aria-label="Наименование услуги" value={name} onChange={(event) => setName(event.target.value)} />
          </FormField>
          <label className="contractors-switch-row">
            <span>Регулярные платежи</span>
            <span className="contractors-switch-control">
              <input type="checkbox" aria-label="Регулярные платежи" checked={isRegular} onChange={(event) => setIsRegular(event.target.checked)} />
            </span>
          </label>
          {isRegular ? (
            <>
              <div className="contractors-service-period-grid">
                <FormField label="Вид начисления">
                  <select
                    aria-label="Вид начисления регулярной услуги"
                    value={incomeTypeId}
                    onChange={(event) => {
                      const nextIncomeTypeId = event.target.value
                      setIncomeTypeId(nextIncomeTypeId)
                      setTariffId(chooseRegularTariffId(nextIncomeTypeId, tariffId, incomeTypes, tariffs))
                      setError(null)
                    }}
                  >
                    {incomeTypes.length > 0 ? incomeTypes.map((incomeType) => (
                      <option key={incomeType.id} value={incomeType.id}>{incomeType.name}</option>
                    )) : <option value="">Нет видов поступлений</option>}
                  </select>
                </FormField>
                <FormField label="Тариф">
                  <select
                    aria-label="Тариф регулярной услуги"
                    value={tariffId}
                    onChange={(event) => {
                      setTariffId(event.target.value)
                      setError(null)
                    }}
                  >
                    {compatibleTariffs.length > 0 ? compatibleTariffs.map((tariff) => (
                      <option key={tariff.id} value={tariff.id}>{tariff.name}</option>
                    )) : <option value="">Нет совместимых тарифов</option>}
                  </select>
                </FormField>
              </div>
              <div className="contractors-service-period-grid">
                <FormField label="Периодичность">
                  <input aria-label="Периодичность" inputMode="numeric" value={periodicityMonths} onChange={(event) => setPeriodicityMonths(event.target.value)} />
                </FormField>
                <FormField label="Учитывать платеж с">
                  <select aria-label="Учитывать платеж с" value={accrualStartMonth} onChange={(event) => setAccrualStartMonth(event.target.value)}>
                    {contractorTariffMonthOptions.map((month) => (
                      <option key={month.value} value={month.value}>{month.label}</option>
                    ))}
                  </select>
                </FormField>
                <FormField label="Оплатить до">
                  <div className="contractors-inline-field contractors-inline-field--date">
                    <input aria-label="День оплаты" inputMode="numeric" maxLength={2} value={paymentDueDay} onChange={(event) => setPaymentDueDay(event.target.value)} />
                    <select aria-label="Месяц оплаты" value={paymentDueMonth} onChange={(event) => setPaymentDueMonth(event.target.value)}>
                      {contractorTariffMonthOptions.map((month) => (
                        <option key={month.value} value={month.value}>{month.label}</option>
                      ))}
                    </select>
                  </div>
                </FormField>
              </div>
              <FormField label="Перенос долга в просроченный">
                <div className="contractors-inline-field">
                  <input aria-label="Перенос долга в просроченный" inputMode="numeric" value={overdueGraceDays} onChange={(event) => setOverdueGraceDays(event.target.value)} />
                  <span>дн.</span>
                </div>
              </FormField>
              <div className="contractors-service-flags">
                <label className="contractors-check-row">
                  <input
                    type="checkbox"
                    aria-label="По счетчику"
                    checked={isByMeter}
                    onChange={(event) => {
                      setIsByMeter(event.target.checked)
                      if (!event.target.checked) {
                        setIsTiered(false)
                      }
                    }}
                  />
                  <span>По счетчику</span>
                </label>
                <label className="contractors-check-row">
                  <input type="checkbox" aria-label="Пороговая тарификация" checked={isTiered} disabled={!isByMeter} onChange={(event) => setIsTiered(event.target.checked)} />
                  <span>Пороговая тарификация</span>
                </label>
              </div>
              <FormField label="Единица измерения">
                <input aria-label="Единица измерения" list="contractor-service-unit-options" value={unitName} onChange={(event) => setUnitName(event.target.value)} />
              </FormField>
              <datalist id="contractor-service-unit-options">
                {unitOptions.map((unit) => (
                  <option key={unit} value={unit} />
                ))}
              </datalist>
              {isTiered ? (
                <>
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
              ) : null}
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
            <button className="secondary-button" type="submit" disabled={isSaving}>
              <Save size={17} />
              <span>Сохранить</span>
            </button>
            <button className="ghost-button" type="button" onClick={onClose}>
              Отмена
            </button>
          </div>
        </form>
      </section>
    </div>
  )
}

function AddFeePrototypeDialog({
  garageOptions,
  incomeTypes,
  initialCampaign,
  isSaving,
  onClose,
  onSave,
  submitLabel = 'Объявить сбор',
  title = 'Добавить сбор',
}: {
  garageOptions: GarageDto[]
  initialCampaign?: FeeCampaignDto | null
  incomeTypes: AccountingTypeDto[]
  isSaving: boolean
  onClose: () => void
  onSave: (request: UpsertFeeCampaignRequest) => Promise<void>
  submitLabel?: string
  title?: string
}) {
  const [name, setName] = useState(initialCampaign?.name ?? '')
  const [incomeTypeId, setIncomeTypeId] = useState(initialCampaign?.incomeTypeId ?? incomeTypes[0]?.id ?? '')
  const [goal, setGoal] = useState(initialCampaign?.goal ?? '')
  const [contributionAmount, setContributionAmount] = useState(initialCampaign ? String(initialCampaign.contributionAmount) : '')
  const [targetAmount, setTargetAmount] = useState(initialCampaign ? String(initialCampaign.targetAmount) : '')
  const [startsOn, setStartsOn] = useState(initialCampaign?.startsOn ?? getLocalDateInputValue())
  const [endsOn, setEndsOn] = useState(initialCampaign?.endsOn ?? '')
  const [appliesToAllGarages, setAppliesToAllGarages] = useState(initialCampaign?.appliesToAllGarages ?? true)
  const [participantGarageIds, setParticipantGarageIds] = useState<string[]>(initialCampaign?.participantGarageIds ?? [])
  const [overdueGraceDays, setOverdueGraceDays] = useState(String(initialCampaign?.overdueGraceDays ?? 30))
  const [error, setError] = useState<string | null>(null)
  const [pendingConfirmation, setPendingConfirmation] = useState<{ request: UpsertFeeCampaignRequest; changes: ChangePreview[] } | null>(null)
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useRestoreFocusOnClose(Boolean(pendingConfirmation))
  const confirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingConfirmation))
  const confirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingConfirmation))
  useEscapeKey(!pendingConfirmation, onClose)
  useEscapeKey(Boolean(pendingConfirmation), () => setPendingConfirmation(null))

  function toggleParticipantGarage(garageId: string, checked: boolean) {
    setParticipantGarageIds((currentIds) => {
      if (checked) {
        return currentIds.includes(garageId) ? currentIds : [...currentIds, garageId]
      }

      return currentIds.filter((currentId) => currentId !== garageId)
    })
  }

  async function submitFee(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmedName = name.trim()
    const parsedContributionAmount = parsePrototypeAmount(contributionAmount)
    const parsedTargetAmount = parsePrototypeAmount(targetAmount)
    const parsedOverdueGraceDays = Number(overdueGraceDays)

    if (!trimmedName) {
      setError('Укажите наименование сбора.')
      return
    }

    if (!incomeTypeId) {
      setError('Выберите вид поступления для сбора.')
      return
    }

    if (parsedContributionAmount === null || parsedContributionAmount <= 0) {
      setError('Сумма взноса должна быть больше нуля.')
      return
    }

    if (parsedTargetAmount === null || parsedTargetAmount <= 0) {
      setError('Сумма сбора должна быть больше нуля.')
      return
    }

    if (!startsOn) {
      setError('Укажите дату начала сбора.')
      return
    }

    if (endsOn && endsOn < startsOn) {
      setError('Дата окончания не может быть раньше даты начала.')
      return
    }

    if (!Number.isInteger(parsedOverdueGraceDays) || parsedOverdueGraceDays < 0 || parsedOverdueGraceDays > 366) {
      setError('Перенос долга должен быть числом от 0 до 366 дней.')
      return
    }

    if (!appliesToAllGarages && participantGarageIds.length === 0) {
      setError('Выберите хотя бы один гараж для сбора.')
      return
    }

    const request: UpsertFeeCampaignRequest = {
      name: trimmedName,
      incomeTypeId,
      goal: goal.trim() || null,
      contributionAmount: parsedContributionAmount,
      targetAmount: parsedTargetAmount,
      startsOn,
      endsOn: endsOn || null,
      appliesToAllGarages,
      participantGarageIds: appliesToAllGarages ? [] : participantGarageIds,
      overdueGraceDays: parsedOverdueGraceDays,
    }

    setError(null)

    if (initialCampaign) {
      const changes = getFeeCampaignChangePreview(initialCampaign, request, incomeTypes, garageOptions)
      if (changes.length === 0) {
        onClose()
        return
      }

      setPendingConfirmation({ request, changes })
      return
    }

    await onSave(request)
  }

  async function confirmFeeChanges() {
    if (!pendingConfirmation) {
      return
    }

    await onSave(pendingConfirmation.request)
    setPendingConfirmation(null)
  }

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={pendingConfirmation ? undefined : onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog contractors-tariff-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-fee-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="contractor-fee-title">{title}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму сбора" onClick={onClose} disabled={Boolean(pendingConfirmation)}>
              <X size={18} />
            </button>
          </div>

          <form className="dictionary-modal-form contractors-modal-form" onSubmit={submitFee}>
            {error ? <FormError>{error}</FormError> : null}
            <FormField label="Наименование сбора">
              <input aria-label="Наименование сбора" value={name} onChange={(event) => setName(event.target.value)} />
            </FormField>
          <FormField label="Вид поступления">
            <select aria-label="Вид поступления для сбора" value={incomeTypeId} onChange={(event) => setIncomeTypeId(event.target.value)}>
              {incomeTypes.length > 0 ? incomeTypes.map((incomeType) => (
                <option key={incomeType.id} value={incomeType.id}>{incomeType.name}</option>
              )) : <option value="">Нет видов поступлений</option>}
            </select>
          </FormField>
          <FormField label="Цель">
            <input aria-label="Цель сбора" value={goal} onChange={(event) => setGoal(event.target.value)} />
          </FormField>
          <div className="contractors-service-period-grid">
            <FormField label="Сумма взноса">
              <div className="contractors-inline-field">
                <input aria-label="Сумма взноса" inputMode="decimal" value={contributionAmount} onChange={(event) => setContributionAmount(event.target.value)} />
                <span>руб.</span>
              </div>
            </FormField>
            <FormField label="Сумма сбора">
              <div className="contractors-inline-field">
                <input aria-label="Сумма сбора" inputMode="decimal" value={targetAmount} onChange={(event) => setTargetAmount(event.target.value)} />
                <span>руб.</span>
              </div>
            </FormField>
          </div>
          <label className="contractors-switch-row">
            <span>Участники</span>
            <span className="contractors-switch-control">
              <input type="checkbox" aria-label="Все гаражи" checked={appliesToAllGarages} onChange={(event) => setAppliesToAllGarages(event.target.checked)} />
            </span>
            <span>все гаражи</span>
          </label>
          {!appliesToAllGarages ? (
            <fieldset className="contractors-participant-list">
              <legend>Выбранные гаражи</legend>
              {garageOptions.length > 0 ? garageOptions.map((garage) => (
                <label key={garage.id} className="contractors-participant-option">
                  <input
                    type="checkbox"
                    aria-label={`Гараж ${garage.number}`}
                    checked={participantGarageIds.includes(garage.id)}
                    onChange={(event) => toggleParticipantGarage(garage.id, event.target.checked)}
                  />
                  <span>
                    <strong>Гараж {garage.number}</strong>
                    {garage.ownerName ? <small>{garage.ownerName}</small> : null}
                  </span>
                </label>
              )) : <p className="form-hint">Активные гаражи не найдены.</p>}
            </fieldset>
          ) : null}
          <div className="contractors-service-period-grid">
            <FormField label="Дата начала">
              <div className="contractors-inline-field">
                <input aria-label="Дата начала" type="date" value={startsOn} onChange={(event) => setStartsOn(event.target.value)} />
                <button className="link-button" type="button" onClick={() => setStartsOn(getLocalDateInputValue())}>Сегодня</button>
              </div>
            </FormField>
            <FormField label="Дата окончания сбора">
              <input aria-label="Дата окончания сбора" type="date" value={endsOn} onChange={(event) => setEndsOn(event.target.value)} />
            </FormField>
          </div>
          <FormField label="Перенос долга по сбору в просроченный">
            <div className="contractors-inline-field">
              <input aria-label="Перенос долга по сбору в просроченный" inputMode="numeric" value={overdueGraceDays} onChange={(event) => setOverdueGraceDays(event.target.value)} />
              <span>дн.</span>
            </div>
          </FormField>

            <div className="detail-dialog-actions">
              <button className="secondary-button" type="submit" disabled={isSaving || Boolean(pendingConfirmation)}>
                {submitLabel}
              </button>
              <button className="ghost-button" type="button" onClick={onClose} disabled={Boolean(pendingConfirmation)}>
                Отмена
              </button>
            </div>
          </form>
        </section>
      </div>

      {pendingConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setPendingConfirmation(null)}>
          <section ref={confirmationDialogRef} className="detail-dialog contractors-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="fee-campaign-edit-confirmation-title" aria-describedby="fee-campaign-edit-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Изменение</p>
                <h3 id="fee-campaign-edit-confirmation-title">Подтвердите изменения сбора</h3>
                <p>{initialCampaign?.name ?? pendingConfirmation.request.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить подтверждение изменений сбора" onClick={() => setPendingConfirmation(null)} disabled={isSaving}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fee-campaign-edit-confirmation-description">Проверьте, что именно изменится. После подтверждения действие будет записано в историю изменений.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля сбора">
              {pendingConfirmation.changes.map((change) => (
                <li key={`${change.field}-${change.before}-${change.after}`}>
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
              <button ref={confirmationCancelRef} className="ghost-button" type="button" onClick={() => setPendingConfirmation(null)} disabled={isSaving}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmFeeChanges()} disabled={isSaving}>
                <Save size={16} />
                <span>{isSaving ? 'Сохраняем...' : 'Сохранить изменения'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </>
  )
}

const meterReadingMonths = [
  { key: '01', label: 'Январь' },
  { key: '02', label: 'Февраль' },
  { key: '03', label: 'Март' },
  { key: '04', label: 'Апрель' },
  { key: '05', label: 'Май' },
  { key: '06', label: 'Июнь' },
  { key: '07', label: 'Июль' },
  { key: '08', label: 'Август' },
  { key: '09', label: 'Сентябрь' },
  { key: '10', label: 'Октябрь' },
  { key: '11', label: 'Ноябрь' },
  { key: '12', label: 'Декабрь' },
]

const meterReadingTypes = [
  { id: 'electricity', label: 'Электроэнергия', unit: 'кВт' },
  { id: 'water', label: 'Вода', unit: 'м3' },
] as const

type MeterReadingTypeId = typeof meterReadingTypes[number]['id']

function createMeterReadingCellKey(year: string, meterType: MeterReadingTypeId, garageId: string, monthKey: string) {
  return `${year}:${meterType}:${garageId}:${monthKey}`
}

function isValidMeterReadingYear(value: string) {
  if (!/^\d{4}$/.test(value)) {
    return false
  }

  const year = Number(value)
  return year >= 1900 && year <= 9999
}

function formatMeterReadingInputValue(value: number) {
  return Number.isInteger(value) ? String(value) : String(value).replace('.', ',')
}

function parseMeterReadingInputValue(value: string) {
  const normalizedValue = value.trim().replace(/\s/g, '').replace(',', '.')
  if (!/^\d+(\.\d+)?$/.test(normalizedValue)) {
    return null
  }

  const parsedValue = Number(normalizedValue)
  return Number.isFinite(parsedValue) && parsedValue >= 0 ? parsedValue : null
}

type MeterReadingPrototypePendingChange = {
  cellKey: string
  readingId?: string
  garageNumber: string
  monthLabel: string
  meterTypeLabel: string
  unit: string
  previousValue: string
  nextValue: string
}

type MeterReadingMonth = typeof meterReadingMonths[number]

type MeterReadingsTableProps = {
  appliedYear: string
  draftReadings: Record<string, string>
  garages: GarageDto[]
  loading: boolean
  meterType: MeterReadingTypeId
  onCommitReading: (garage: GarageDto, month: MeterReadingMonth) => void
  onDraftReadingChange: (cellKey: string, value: string) => void
  savedReadings: Record<string, string>
  savingReadingKey: string | null
  selectedMeterType: typeof meterReadingTypes[number]
  yearIsValid: boolean
}

const MeterReadingsTable = memo(function MeterReadingsTable({
  appliedYear,
  draftReadings,
  garages,
  loading,
  meterType,
  onCommitReading,
  onDraftReadingChange,
  savedReadings,
  savingReadingKey,
  selectedMeterType,
  yearIsValid,
}: MeterReadingsTableProps) {
  return (
    <div className="meter-readings-table-shell">
      <div className="meter-readings-table" role="table" aria-label={`Показания счетчиков за ${appliedYear} год`}>
        <div className="meter-readings-title-row" role="row">
          <span role="columnheader">Гараж</span>
          <span role="columnheader">Показания</span>
        </div>
        <div className="meter-readings-month-row" role="row">
          <span role="columnheader">Гараж</span>
          {meterReadingMonths.map((month) => (
            <span role="columnheader" key={month.key}>
              <strong>{month.label}</strong>
              <small>{selectedMeterType.unit}</small>
            </span>
          ))}
        </div>
        {loading ? (
          <div className="meter-readings-empty-row" role="row">
            <span role="cell">Загрузка гаражей и показаний...</span>
          </div>
        ) : garages.length > 0 ? garages.map((garage) => (
          <div className="meter-readings-data-row" role="row" key={garage.id}>
            <span role="rowheader">Гараж {garage.number}</span>
            {meterReadingMonths.map((month) => {
              const cellKey = createMeterReadingCellKey(appliedYear, meterType, garage.id, month.key)
              return (
                <span role="cell" key={cellKey}>
                  <input
                    aria-label={`Гараж ${garage.number}, ${month.label}, показание`}
                    disabled={!yearIsValid || savingReadingKey === cellKey}
                    inputMode="decimal"
                    value={draftReadings[cellKey] ?? savedReadings[cellKey] ?? ''}
                    onBlur={() => onCommitReading(garage, month)}
                    onChange={(event) => onDraftReadingChange(cellKey, event.target.value)}
                    onKeyDown={(event) => handleEditableInputKeyDown(event, () => onCommitReading(garage, month))}
                  />
                </span>
              )
            })}
          </div>
        )) : (
          <div className="meter-readings-empty-row" role="row">
            <span role="cell">В справочнике пока нет гаражей</span>
          </div>
        )}
      </div>
    </div>
  )
})

function MeterReadingsPrototypePanel({ auth, dictionaryClient, financeClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient; financeClient: FinanceClient }) {
  const [yearDraft, setYearDraft] = useState('2026')
  const [appliedYear, setAppliedYear] = useState('2026')
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [savedReadings, setSavedReadings] = useState<Record<string, string>>({})
  const [draftReadings, setDraftReadings] = useState<Record<string, string>>({})
  const [savedReadingIds, setSavedReadingIds] = useState<Record<string, string>>({})
  const [savingReadingKey, setSavingReadingKey] = useState<string | null>(null)
  const [pendingReadingChange, setPendingReadingChange] = useState<MeterReadingPrototypePendingChange | null>(null)

  const meterType: MeterReadingTypeId = 'electricity'
  const selectedMeterType = meterReadingTypes[0]
  const yearIsValid = isValidMeterReadingYear(yearDraft)

  function cancelPendingReadingChange() {
    if (pendingReadingChange) {
      setDraftReadings((currentDrafts) => ({
        ...currentDrafts,
        [pendingReadingChange.cellKey]: pendingReadingChange.previousValue,
      }))
    }

    setPendingReadingChange(null)
  }

  function confirmPendingReadingChange() {
    if (!pendingReadingChange) {
      return
    }

    void saveReadingValue(pendingReadingChange.cellKey, pendingReadingChange.readingId, pendingReadingChange.nextValue)
  }

  function updateYearDraft(value: string) {
    const nextYear = value.replace(/\D/g, '').slice(0, 4)
    setYearDraft(nextYear)

    if (isValidMeterReadingYear(nextYear)) {
      setAppliedYear(nextYear)
    }
  }

  function applyYearDraft() {
    if (isValidMeterReadingYear(yearDraft)) {
      setAppliedYear(yearDraft)
    }
  }

  useRestoreFocusOnClose(Boolean(pendingReadingChange))
  const readingChangeDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingReadingChange))
  const readingChangeCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingReadingChange))
  useEscapeKey(Boolean(pendingReadingChange), () => cancelPendingReadingChange())

  useEffect(() => {
    let isMounted = true

    async function loadMeterReadings() {
      setLoading(true)
      setError(null)
      try {
        const [items, readingsPage] = await Promise.all([
          dictionaryClient.getGarages(auth.accessToken, undefined, 500),
          financeClient.getMeterReadingsPage(auth.accessToken, { monthFrom: `${appliedYear}-01`, monthTo: `${appliedYear}-12`, meterKind: meterType, limit: 6000, offset: 0 }),
        ])
        if (!isMounted) {
          return
        }

        const nextSavedReadings: Record<string, string> = {}
        const nextSavedReadingIds: Record<string, string> = {}
        readingsPage.items.filter((reading) => !reading.isCanceled).forEach((reading) => {
          const monthKey = reading.accountingMonth.slice(5, 7)
          const cellKey = createMeterReadingCellKey(appliedYear, meterType, reading.garageId, monthKey)
          nextSavedReadings[cellKey] = formatMeterReadingInputValue(reading.currentValue)
          nextSavedReadingIds[cellKey] = reading.id
        })

        setGarages([...items.filter((item) => !item.isArchived)].sort((left, right) => left.number.localeCompare(right.number, 'ru-RU', { numeric: true })))
        setSavedReadings(nextSavedReadings)
        setDraftReadings(nextSavedReadings)
        setSavedReadingIds(nextSavedReadingIds)
        setLoading(false)
      } catch (loadError) {
        if (!isMounted) {
          return
        }

        setError(loadError instanceof Error ? loadError.message : 'Не удалось загрузить гаражи.')
        setGarages([])
        setLoading(false)
      }
    }

    void loadMeterReadings()

    return () => {
      isMounted = false
    }
  }, [appliedYear, auth.accessToken, dictionaryClient, financeClient, meterType])

  const saveReadingValue = useCallback(async (cellKey: string, readingId: string | undefined, nextValue: string) => {
    const [, , garageId, monthKey] = cellKey.split(':')
    const parsedValue = parseMeterReadingInputValue(nextValue)
    if (parsedValue === null) {
      setError('Введите показание неотрицательным числом.')
      return
    }

    const request: CreateMeterReadingRequest = {
      garageId,
      meterKind: meterType,
      accountingMonth: `${appliedYear}-${monthKey}-01`,
      readingDate: getLocalDateInputValue(),
      currentValue: parsedValue,
      comment: 'Ввод из годовой таблицы показаний',
    }

    setSavingReadingKey(cellKey)
    setError(null)
    try {
      const savedReading = readingId
        ? await financeClient.updateMeterReading(auth.accessToken, readingId, request)
        : await financeClient.createMeterReading(auth.accessToken, request)
      const savedValue = formatMeterReadingInputValue(savedReading.currentValue)
      setSavedReadings((currentReadings) => ({ ...currentReadings, [cellKey]: savedValue }))
      setDraftReadings((currentDrafts) => ({ ...currentDrafts, [cellKey]: savedValue }))
      setSavedReadingIds((currentIds) => ({ ...currentIds, [cellKey]: savedReading.id }))
      setPendingReadingChange(null)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось сохранить показание.')
    } finally {
      setSavingReadingKey(null)
    }
  }, [appliedYear, auth.accessToken, financeClient, meterType])

  const changeDraftReading = useCallback((cellKey: string, value: string) => {
    setDraftReadings((currentDrafts) => ({ ...currentDrafts, [cellKey]: value }))
  }, [])

  const commitReading = useCallback((garage: GarageDto, month: MeterReadingMonth) => {
    if (!yearIsValid) {
      return
    }

    const cellKey = createMeterReadingCellKey(appliedYear, meterType, garage.id, month.key)
    if (savingReadingKey === cellKey) {
      return
    }

    const nextValue = draftReadings[cellKey] ?? ''
    const previousValue = savedReadings[cellKey] ?? ''

    if (nextValue.trim() === previousValue.trim()) {
      return
    }

    if (previousValue.trim() === '') {
      void saveReadingValue(cellKey, undefined, nextValue)
      return
    }

    setPendingReadingChange({
      cellKey,
      readingId: savedReadingIds[cellKey],
      garageNumber: garage.number,
      monthLabel: month.label,
      meterTypeLabel: selectedMeterType.label,
      unit: selectedMeterType.unit,
      previousValue,
      nextValue,
    })
  }, [appliedYear, draftReadings, meterType, savedReadingIds, savedReadings, saveReadingValue, savingReadingKey, selectedMeterType.label, selectedMeterType.unit, yearIsValid])

  return (
    <section className="meter-readings-page" aria-label="Показания">
      <div className="meter-readings-heading">
        <div>
          <h1>Показания</h1>
        </div>
        <div className="meter-readings-controls">
          <FormField label="Год">
            <input
              aria-label="Год показаний"
              aria-invalid={!yearIsValid}
              className="meter-readings-control"
              inputMode="numeric"
              maxLength={4}
              value={yearDraft}
              onChange={(event) => updateYearDraft(event.target.value)}
              onBlur={applyYearDraft}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  applyYearDraft()
                }
              }}
            />
          </FormField>
          <FormField label="Тип">
            <div className="meter-readings-control meter-readings-fixed-type" aria-label="Тип показаний">{selectedMeterType.label}, {selectedMeterType.unit}</div>
          </FormField>
        </div>
      </div>

      {!yearIsValid ? <div className="form-error" role="alert">Введите год четырьмя цифрами от 1900 до 9999.</div> : null}
      {error ? <div className="form-error" role="alert">{error}</div> : null}

      <MeterReadingsTable
        appliedYear={appliedYear}
        draftReadings={draftReadings}
        garages={garages}
        loading={loading}
        meterType={meterType}
        onCommitReading={commitReading}
        onDraftReadingChange={changeDraftReading}
        savedReadings={savedReadings}
        savingReadingKey={savingReadingKey}
        selectedMeterType={selectedMeterType}
        yearIsValid={yearIsValid}
      />

      {pendingReadingChange ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={cancelPendingReadingChange}>
          <section ref={readingChangeDialogRef} className="detail-dialog contractors-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="meter-reading-change-title" aria-describedby="meter-reading-change-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Изменение</p>
                <h3 id="meter-reading-change-title">Подтвердить показание?</h3>
                <p>{`Гараж ${pendingReadingChange.garageNumber}, ${pendingReadingChange.monthLabel}`}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение показания" onClick={cancelPendingReadingChange}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="meter-reading-change-description">Проверьте новое показание счетчика. После сохранения backend запишет изменение в историю по гаражу, месяцу и типу счетчика.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля показания">
              <li>
                <span className="dictionary-change-field">Показание</span>
                <span className="dictionary-change-values">
                  <span className="dictionary-change-value">{formatPrototypeChangeValue(pendingReadingChange.previousValue)}</span>
                  <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                  <span className="dictionary-change-value dictionary-change-value-after">{formatPrototypeChangeValue(pendingReadingChange.nextValue)}</span>
                </span>
              </li>
            </ul>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={readingChangeCancelRef} className="ghost-button" type="button" onClick={cancelPendingReadingChange}>Отмена</button>
              <button className="secondary-button" type="button" onClick={confirmPendingReadingChange}>
                <Save size={16} />
                <span>Сохранить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </section>
  )
}

function getDictionaryRestoreErrorMessage(section: DictionarySectionKey, caught: unknown) {
  if (caught instanceof DictionaryApiError) {
    if (caught.code === 'garage_number_duplicate') {
      return 'Гараж нельзя восстановить: активный гараж с таким номером уже есть. Проверьте рабочий список и архив.'
    }

    if (caught.code === 'supplier_group_duplicate') {
      return 'Группу поставщиков нельзя восстановить: активная группа с таким названием уже есть.'
    }

    if (caught.code === 'supplier_group_not_found' && section === 'suppliers') {
      return 'Поставщика нельзя восстановить: сначала верните его группу поставщиков.'
    }

    if (caught.code === 'income_type_duplicate') {
      return 'Вид поступления нельзя восстановить: активный вид с таким названием уже есть.'
    }

    if (caught.code === 'expense_type_duplicate') {
      return 'Вид выплаты нельзя восстановить: активный вид с таким названием уже есть.'
    }

    if (caught.code === 'tariff_duplicate') {
      return 'Тариф нельзя восстановить: активный тариф с таким названием и датой начала уже есть.'
    }
  }

  return caught instanceof Error ? caught.message : 'Не удалось восстановить запись.'
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
  const [showArchived, setShowArchived] = useState(false)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<{ id: number; text: string; kind: 'success' | 'error' } | null>(null)
  const [contextMenu, setContextMenu] = useState<{ section: DictionarySectionKey; item: DictionaryRecord; x: number; y: number } | null>(null)
  const [editor, setEditor] = useState<DictionaryEditorState | null>(null)
  const [pendingEditorConfirmation, setPendingEditorConfirmation] = useState<{ editor: DictionaryEditorState; changes: DictionaryChangePreview[] } | null>(null)
  const [archiveTarget, setArchiveTarget] = useState<{ section: DictionarySectionKey; item: DictionaryRecord } | null>(null)
  const [archiveReason, setArchiveReason] = useState('')
  const [archiveReasonError, setArchiveReasonError] = useState<string | null>(null)
  const [restoreTarget, setRestoreTarget] = useState<{ section: DictionarySectionKey; item: DictionaryRecord } | null>(null)
  const [balanceHistoryGarage, setBalanceHistoryGarage] = useState<GarageDto | null>(null)
  const [balanceHistory, setBalanceHistory] = useState<GarageBalanceHistoryDto | null>(null)
  const [balanceHistoryFilters, setBalanceHistoryFilters] = useState(() => createDefaultGarageBalanceHistoryFilters())
  const [balanceHistoryLoading, setBalanceHistoryLoading] = useState(false)
  const [balanceHistoryError, setBalanceHistoryError] = useState<string | null>(null)
  const balanceHistoryTriggerRef = useRef<HTMLElement | null>(null)
  const [ownerForm, setOwnerForm] = useState<UpsertOwnerRequest>(createEmptyOwnerForm())
  const [ownerGarageLinkForm, setOwnerGarageLinkForm] = useState<OwnerGarageLinkForm>(createEmptyOwnerGarageLinkForm())
  const [garageForm, setGarageForm] = useState(createEmptyGarageForm())
  const [supplierGroupName, setSupplierGroupName] = useState('')
  const [supplierForm, setSupplierForm] = useState(createEmptySupplierForm())
  const [accountingTypeForm, setAccountingTypeForm] = useState(createEmptyAccountingTypeForm())
  const [tariffForm, setTariffForm] = useState<UpsertTariffRequest>(createEmptyTariffForm())
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  useRestoreFocusOnClose(Boolean(editor))
  const editorCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(editor))
  const editorDialogRef = useFocusTrap<HTMLElement>(Boolean(editor))
  useRestoreFocusOnClose(Boolean(pendingEditorConfirmation))
  const editorConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingEditorConfirmation))
  const editorConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingEditorConfirmation))
  useRestoreFocusOnClose(Boolean(archiveTarget))
  const archiveCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(archiveTarget))
  const archiveDialogRef = useFocusTrap<HTMLElement>(Boolean(archiveTarget))
  useRestoreFocusOnClose(Boolean(restoreTarget))
  const restoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(restoreTarget))
  const restoreDialogRef = useFocusTrap<HTMLElement>(Boolean(restoreTarget))
  useRestoreFocusOnClose(Boolean(balanceHistoryGarage))
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
  useEscapeKey(Boolean(editor) && !pendingEditorConfirmation, () => closeEditor())
  useEscapeKey(Boolean(pendingEditorConfirmation), () => setPendingEditorConfirmation(null))
  useEscapeKey(Boolean(archiveTarget), () => closeArchiveTarget())
  useEscapeKey(Boolean(restoreTarget), () => setRestoreTarget(null))
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
          dictionaryClient.getSupplierGroups(auth.accessToken, undefined, 500),
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
  }, [activeSection, auth.accessToken, dictionaryClient, search, showArchived])

  async function loadPage(section: DictionarySectionKey, offset = pages[section].offset, limit = pages[section].limit) {
    const query = supportsDictionarySearch(section) ? search.trim() || undefined : undefined
    let page: PagedResult<DictionaryRecord>
    if (section === 'owners') {
      page = dictionaryClient.getOwnersPage
        ? await dictionaryClient.getOwnersPage(auth.accessToken, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getOwners(auth.accessToken, query, 500, showArchived), offset, limit)
      setOwners(page.items as OwnerDto[])
    } else if (section === 'garages') {
      page = dictionaryClient.getGaragesPage
        ? await dictionaryClient.getGaragesPage(auth.accessToken, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getGarages(auth.accessToken, query, 500, showArchived), offset, limit)
      setGarages(page.items as GarageDto[])
    } else if (section === 'supplierGroups') {
      page = dictionaryClient.getSupplierGroupsPage
        ? await dictionaryClient.getSupplierGroupsPage(auth.accessToken, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getSupplierGroups(auth.accessToken, query, 500, showArchived), offset, limit)
      setGroups(page.items as SupplierGroupDto[])
    } else if (section === 'suppliers') {
      page = dictionaryClient.getSuppliersPage
        ? await dictionaryClient.getSuppliersPage(auth.accessToken, undefined, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getSuppliers(auth.accessToken, undefined, query, 500, showArchived), offset, limit)
      setSuppliers(page.items as SupplierDto[])
    } else if (section === 'incomeTypes') {
      page = dictionaryClient.getIncomeTypesPage
        ? await dictionaryClient.getIncomeTypesPage(auth.accessToken, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getIncomeTypes(auth.accessToken, query, 500, showArchived), offset, limit)
      setIncomeTypes(page.items as AccountingTypeDto[])
    } else if (section === 'expenseTypes') {
      page = dictionaryClient.getExpenseTypesPage
        ? await dictionaryClient.getExpenseTypesPage(auth.accessToken, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getExpenseTypes(auth.accessToken, query, 500, showArchived), offset, limit)
      setExpenseTypes(page.items as AccountingTypeDto[])
    } else {
      page = dictionaryClient.getTariffsPage
        ? await dictionaryClient.getTariffsPage(auth.accessToken, query, offset, limit, showArchived)
        : createFallbackPage<DictionaryRecord>(await dictionaryClient.getTariffs(auth.accessToken, query, 500, showArchived), offset, limit)
      setTariffs(page.items as TariffDto[])
    }

    setPages((current) => ({ ...current, [section]: page }))
  }

  function showToast(text: string, kind: 'success' | 'error' = 'success') {
    setToast({ id: Date.now(), text, kind })
  }

  function openContextMenu(event: MouseEvent, section: DictionarySectionKey, item: DictionaryRecord) {
    event.preventDefault()
    if (section === 'garages') {
      balanceHistoryTriggerRef.current = event.currentTarget as HTMLElement
    } else {
      balanceHistoryTriggerRef.current = null
    }
    setContextMenu({ section, item, x: event.clientX, y: event.clientY })
  }

  function openArchiveTarget(section: DictionarySectionKey, item: DictionaryRecord) {
    setArchiveReason('')
    setArchiveReasonError(null)
    setArchiveTarget({ section, item })
  }

  function closeArchiveTarget() {
    setArchiveTarget(null)
    setArchiveReason('')
    setArchiveReasonError(null)
  }

  async function openBalanceHistory(garage: GarageDto) {
    const filters = createDefaultGarageBalanceHistoryFilters()
    setContextMenu(null)
    setBalanceHistoryGarage(garage)
    setBalanceHistoryFilters(filters)
    await loadBalanceHistory(garage.id, filters)
  }

  function closeBalanceHistory() {
    const trigger = balanceHistoryTriggerRef.current
    setBalanceHistoryGarage(null)
    setBalanceHistory(null)
    setBalanceHistoryError(null)
    window.setTimeout(() => {
      if (trigger?.isConnected) {
        trigger.focus()
      }
      balanceHistoryTriggerRef.current = null
    }, 0)
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
    setPendingEditorConfirmation(null)
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

    const validation = getEditorValidationErrors(editor)
    if (validation.length > 0) {
      setValidationErrors(validation)
      return
    }

    if (editor.mode === 'edit' && editor.item) {
      const changes = getDictionaryEditorChanges(editor.section, editor.item)
      if (changes.length === 0) {
        closeEditor()
        showToast('Изменений нет.')
        return
      }

      setPendingEditorConfirmation({ editor, changes })
      return
    }

    await saveConfirmedEditor(editor)
  }

  async function saveConfirmedEditor(currentEditor: DictionaryEditorState) {
    setSaving('dictionary-editor')
    setError(null)
    try {
      const saved = await saveEditorRequest(currentEditor)
      if (!saved) {
        return
      }

      closeEditor()
      await refreshAfterMutation(currentEditor.section)
      showToast(currentEditor.mode === 'create' ? 'Запись добавлена.' : 'Изменения сохранены.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить запись.'
      setError(message)
      showToast(message, 'error')
    } finally {
      setSaving(null)
    }
  }

  async function confirmEditorChanges() {
    if (!pendingEditorConfirmation) {
      return
    }

    const currentEditor = pendingEditorConfirmation.editor
    setPendingEditorConfirmation(null)
    await saveConfirmedEditor(currentEditor)
  }

  function getEditorValidationErrors(currentEditor: DictionaryEditorState) {
    if (currentEditor.section === 'owners') {
      return [...getOwnerValidationErrors(ownerForm), ...getOwnerGarageLinkValidationErrors(ownerGarageLinkForm)]
    }

    if (currentEditor.section === 'garages') {
      return getGarageValidationErrors(createGarageRequestFromForm())
    }

    if (currentEditor.section === 'supplierGroups') {
      return getSupplierGroupValidationErrors({ name: supplierGroupName })
    }

    if (currentEditor.section === 'suppliers') {
      return getSupplierValidationErrors(createSupplierRequestFromForm())
    }

    if (currentEditor.section === 'incomeTypes') {
      return getAccountingTypeValidationErrors(accountingTypeForm, 'вида поступления')
    }

    if (currentEditor.section === 'expenseTypes') {
      return getAccountingTypeValidationErrors(accountingTypeForm, 'вида выплаты')
    }

    return getTariffValidationErrors(tariffForm)
  }

  function createGarageRequestFromForm(): UpsertGarageRequest {
    return {
      number: garageForm.number,
      peopleCount: garageForm.peopleCount,
      floorCount: garageForm.floorCount,
      ownerId: garageForm.ownerId || null,
      startingBalance: garageForm.startingBalance,
      initialWaterMeterValue: garageForm.initialWaterMeterValue === '' ? null : Number(garageForm.initialWaterMeterValue),
      initialElectricityMeterValue: garageForm.initialElectricityMeterValue === '' ? null : Number(garageForm.initialElectricityMeterValue),
      comment: garageForm.comment.trim() || undefined,
    }
  }

  function createSupplierRequestFromForm(): UpsertSupplierRequest {
    return { ...supplierForm, groupId: supplierForm.groupId || groupOptions[0]?.id || '' }
  }

  async function saveEditorRequest(currentEditor: DictionaryEditorState) {
    const errors = getEditorValidationErrors(currentEditor)
    if (errors.length > 0) {
      setValidationErrors(errors)
      return false
    }

    if (currentEditor.section === 'owners') {
      let savedOwner: OwnerDto
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        savedOwner = await dictionaryClient.updateOwner(auth.accessToken, (currentEditor.item as OwnerDto).id, ownerForm)
      } else {
        savedOwner = await dictionaryClient.createOwner(auth.accessToken, ownerForm)
      }
      await saveOwnerGarageLinks(savedOwner.id)
    } else if (currentEditor.section === 'garages') {
      const request = createGarageRequestFromForm()
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateGarage(auth.accessToken, (currentEditor.item as GarageDto).id, request)
      } else {
        await dictionaryClient.createGarage(auth.accessToken, request)
      }
    } else if (currentEditor.section === 'supplierGroups') {
      const request = { name: supplierGroupName }
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateSupplierGroup(auth.accessToken, (currentEditor.item as SupplierGroupDto).id, request)
      } else {
        await dictionaryClient.createSupplierGroup(auth.accessToken, request)
      }
    } else if (currentEditor.section === 'suppliers') {
      const request = createSupplierRequestFromForm()
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateSupplier(auth.accessToken, (currentEditor.item as SupplierDto).id, request)
      } else {
        await dictionaryClient.createSupplier(auth.accessToken, request)
      }
    } else if (currentEditor.section === 'incomeTypes') {
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateIncomeType(auth.accessToken, (currentEditor.item as AccountingTypeDto).id, accountingTypeForm)
      } else {
        await dictionaryClient.createIncomeType(auth.accessToken, accountingTypeForm)
      }
    } else if (currentEditor.section === 'expenseTypes') {
      if (currentEditor.mode === 'edit' && currentEditor.item) {
        await dictionaryClient.updateExpenseType(auth.accessToken, (currentEditor.item as AccountingTypeDto).id, accountingTypeForm)
      } else {
        await dictionaryClient.createExpenseType(auth.accessToken, accountingTypeForm)
      }
    } else {
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
      setGroupOptions(await dictionaryClient.getSupplierGroups(auth.accessToken, undefined, 500))
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

    const reason = archiveReason.trim()
    if (!reason) {
      setArchiveReasonError('Укажите причину удаления записи.')
      return
    }

    setSaving('dictionary-archive')
    setError(null)
    setArchiveReasonError(null)
    try {
      if (archiveTarget.section === 'owners') {
        await dictionaryClient.archiveOwner(auth.accessToken, (archiveTarget.item as OwnerDto).id, reason)
      } else if (archiveTarget.section === 'garages') {
        await dictionaryClient.archiveGarage(auth.accessToken, (archiveTarget.item as GarageDto).id, reason)
      } else if (archiveTarget.section === 'supplierGroups') {
        await dictionaryClient.archiveSupplierGroup(auth.accessToken, (archiveTarget.item as SupplierGroupDto).id, reason)
      } else if (archiveTarget.section === 'suppliers') {
        await dictionaryClient.archiveSupplier(auth.accessToken, (archiveTarget.item as SupplierDto).id, reason)
      } else if (archiveTarget.section === 'incomeTypes') {
        await dictionaryClient.archiveIncomeType(auth.accessToken, (archiveTarget.item as AccountingTypeDto).id, reason)
      } else if (archiveTarget.section === 'expenseTypes') {
        await dictionaryClient.archiveExpenseType(auth.accessToken, (archiveTarget.item as AccountingTypeDto).id, reason)
      } else {
        await dictionaryClient.archiveTariff(auth.accessToken, (archiveTarget.item as TariffDto).id, reason)
      }

      const section = archiveTarget.section
      closeArchiveTarget()
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

  async function confirmRestore() {
    if (!restoreTarget) {
      return
    }

    if (restoreTarget.section === 'tariffs' && !canManageTariffs) {
      setError('Для восстановления тарифов нужно право tariffs.manage.')
      return
    }

    if (restoreTarget.section !== 'tariffs' && !canWriteDictionaries) {
      setError('Для восстановления справочников нужно право dictionaries.write.')
      return
    }

    setSaving('dictionary-restore')
    setError(null)
    try {
      if (restoreTarget.section === 'owners') {
        await dictionaryClient.restoreOwner(auth.accessToken, (restoreTarget.item as OwnerDto).id)
      } else if (restoreTarget.section === 'garages') {
        await dictionaryClient.restoreGarage(auth.accessToken, (restoreTarget.item as GarageDto).id)
      } else if (restoreTarget.section === 'supplierGroups') {
        await dictionaryClient.restoreSupplierGroup(auth.accessToken, (restoreTarget.item as SupplierGroupDto).id)
      } else if (restoreTarget.section === 'suppliers') {
        await dictionaryClient.restoreSupplier(auth.accessToken, (restoreTarget.item as SupplierDto).id)
      } else if (restoreTarget.section === 'incomeTypes') {
        await dictionaryClient.restoreIncomeType(auth.accessToken, (restoreTarget.item as AccountingTypeDto).id)
      } else if (restoreTarget.section === 'expenseTypes') {
        await dictionaryClient.restoreExpenseType(auth.accessToken, (restoreTarget.item as AccountingTypeDto).id)
      } else {
        await dictionaryClient.restoreTariff(auth.accessToken, (restoreTarget.item as TariffDto).id)
      }

      const section = restoreTarget.section
      setRestoreTarget(null)
      await refreshAfterMutation(section)
      showToast('Запись восстановлена и снова доступна в рабочих списках.')
    } catch (caught) {
      const message = getDictionaryRestoreErrorMessage(restoreTarget.section, caught)
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
    return [...getDictionaryTableHeaders(activeSection), 'Статус', 'Действие'].map((header) => <th key={header}>{header}</th>)
  }

  function renderCells(item: DictionaryRecord) {
    return getDictionaryRecordCells(activeSection, item).map((value, index) => <td key={index}>{value}</td>)
  }

  function isArchivedRecord(item: DictionaryRecord) {
    return item.isArchived
  }

  function renderRowAction(item: DictionaryRecord) {
    if (isArchivedRecord(item)) {
      return (
        <button className="ghost-button dictionary-row-action" type="button" disabled={!canWriteActiveSection} onClick={() => setRestoreTarget({ section: activeSection, item })}>
          <RotateCcw size={15} />
          <span>Вернуть</span>
        </button>
      )
    }

    return (
      <button className="ghost-button dictionary-row-action dictionary-row-action-danger" type="button" disabled={!canWriteActiveSection} onClick={() => openArchiveTarget(activeSection, item)}>
        <Trash2 size={15} />
        <span>Удалить</span>
      </button>
    )
  }

  function addDictionaryChange(changes: DictionaryChangePreview[], field: string, before: string, after: string) {
    appendChangePreview(changes, field, before, after)
  }

  function formatOwnerLabel(ownerId: string | null | undefined) {
    if (!ownerId) {
      return 'Без владельца'
    }

    return ownerOptions.find((owner) => owner.id === ownerId)?.fullName ?? `ID ${ownerId}`
  }

  function formatGarageLabel(garageId: string | null | undefined) {
    if (!garageId) {
      return 'Не выбран'
    }

    const garage = garageOptions.find((item) => item.id === garageId)
    return garage ? `Гараж ${garage.number}` : `ID ${garageId}`
  }

  function formatSupplierGroupLabel(groupId: string | null | undefined) {
    if (!groupId) {
      return 'Без группы'
    }

    return groupOptions.find((group) => group.id === groupId)?.name ?? `ID ${groupId}`
  }

  function formatCalculationBaseLabel(value: string) {
    return getTariffCalculationBaseOptions().find((option) => option.value === value)?.label ?? value
  }

  function getDictionaryEditorChanges(section: DictionarySectionKey, item: DictionaryRecord): DictionaryChangePreview[] {
    const changes: DictionaryChangePreview[] = []

    if (section === 'owners') {
      const owner = item as OwnerDto
      const currentGarageId = garageOptions.find((garage) => garage.ownerId === owner.id)?.id ?? ''

      addDictionaryChange(changes, 'Фамилия', formatChangeText(owner.lastName), formatChangeText(ownerForm.lastName))
      addDictionaryChange(changes, 'Имя', formatChangeText(owner.firstName), formatChangeText(ownerForm.firstName))
      addDictionaryChange(changes, 'Отчество', formatChangeText(owner.middleName), formatChangeText(ownerForm.middleName))
      addDictionaryChange(changes, 'Телефон', formatChangeText(owner.phone), formatChangeText(ownerForm.phone))
      addDictionaryChange(changes, 'Адрес', formatChangeText(owner.address), formatChangeText(ownerForm.address))
      addDictionaryChange(changes, 'Заметки по счетчикам', formatChangeText(owner.meterNotes), formatChangeText(ownerForm.meterNotes))
      addDictionaryChange(changes, 'Привязанный гараж', formatGarageLabel(currentGarageId), formatGarageLabel(ownerGarageLinkForm.existingGarageId))

      if (ownerGarageLinkForm.newGarageNumber.trim()) {
        addDictionaryChange(changes, 'Новый гараж', 'пусто', formatChangeText(ownerGarageLinkForm.newGarageNumber))
      }

      return changes
    }

    if (section === 'garages') {
      const garage = item as GarageDto
      const request = createGarageRequestFromForm()

      addDictionaryChange(changes, 'Номер', formatChangeText(garage.number), formatChangeText(request.number))
      addDictionaryChange(changes, 'Количество людей', formatChangeNumber(garage.peopleCount), formatChangeNumber(request.peopleCount))
      addDictionaryChange(changes, 'Количество этажей', formatChangeNumber(garage.floorCount), formatChangeNumber(request.floorCount))
      addDictionaryChange(changes, 'Владелец', formatOwnerLabel(garage.ownerId), formatOwnerLabel(request.ownerId))
      addDictionaryChange(changes, 'Стартовый баланс', formatChangeMoney(garage.startingBalance), formatChangeMoney(request.startingBalance))
      addDictionaryChange(changes, 'Стартовый счетчик воды', formatChangeNumber(garage.initialWaterMeterValue), formatChangeNumber(request.initialWaterMeterValue))
      addDictionaryChange(changes, 'Стартовый счетчик электроэнергии', formatChangeNumber(garage.initialElectricityMeterValue), formatChangeNumber(request.initialElectricityMeterValue))
      addDictionaryChange(changes, 'Комментарий', formatChangeText(garage.comment), formatChangeText(request.comment))
      return changes
    }

    if (section === 'supplierGroups') {
      addDictionaryChange(changes, 'Название', formatChangeText((item as SupplierGroupDto).name), formatChangeText(supplierGroupName))
      return changes
    }

    if (section === 'suppliers') {
      const supplier = item as SupplierDto
      const request = createSupplierRequestFromForm()

      addDictionaryChange(changes, 'Наименование', formatChangeText(supplier.name), formatChangeText(request.name))
      addDictionaryChange(changes, 'Группа', formatSupplierGroupLabel(supplier.groupId), formatSupplierGroupLabel(request.groupId))
      addDictionaryChange(changes, 'ИНН', formatChangeText(supplier.inn), formatChangeText(request.inn))
      addDictionaryChange(changes, 'Юридический адрес', formatChangeText(supplier.legalAddress), formatChangeText(request.legalAddress))
      addDictionaryChange(changes, 'Контактное лицо', formatChangeText(supplier.contactPerson), formatChangeText(request.contactPerson))
      addDictionaryChange(changes, 'Телефон', formatChangeText(supplier.phone), formatChangeText(request.phone))
      addDictionaryChange(changes, 'Почта', formatChangeText(supplier.email), formatChangeText(request.email))
      addDictionaryChange(changes, 'Стартовый баланс', formatChangeMoney(supplier.startingBalance), formatChangeMoney(request.startingBalance))
      addDictionaryChange(changes, 'Комментарий', formatChangeText(supplier.comment), formatChangeText(request.comment))
      return changes
    }

    if (section === 'incomeTypes' || section === 'expenseTypes') {
      const type = item as AccountingTypeDto
      addDictionaryChange(changes, 'Название', formatChangeText(type.name), formatChangeText(accountingTypeForm.name))
      addDictionaryChange(changes, 'Код', formatChangeText(type.code), formatChangeText(accountingTypeForm.code))
      return changes
    }

    const tariff = item as TariffDto
    addDictionaryChange(changes, 'Название', formatChangeText(tariff.name), formatChangeText(tariffForm.name))
    addDictionaryChange(changes, 'База расчета', formatCalculationBaseLabel(tariff.calculationBase), formatCalculationBaseLabel(tariffForm.calculationBase))
    addDictionaryChange(changes, 'Ставка', formatChangeNumber(tariff.rate), formatChangeNumber(tariffForm.rate))
    addDictionaryChange(changes, 'Дата начала', formatChangeDate(tariff.effectiveFrom), formatChangeDate(tariffForm.effectiveFrom))
    addDictionaryChange(changes, 'Первый порог электроэнергии', formatChangeNumber(tariff.electricityFirstThreshold), formatChangeNumber(tariffForm.electricityFirstThreshold))
    addDictionaryChange(changes, 'Второй порог электроэнергии', formatChangeNumber(tariff.electricitySecondThreshold), formatChangeNumber(tariffForm.electricitySecondThreshold))
    addDictionaryChange(changes, 'Первая ставка электроэнергии', formatChangeNumber(tariff.electricityFirstRate), formatChangeNumber(tariffForm.electricityFirstRate))
    addDictionaryChange(changes, 'Вторая ставка электроэнергии', formatChangeNumber(tariff.electricitySecondRate), formatChangeNumber(tariffForm.electricitySecondRate))
    addDictionaryChange(changes, 'Третья ставка электроэнергии', formatChangeNumber(tariff.electricityThirdRate), formatChangeNumber(tariffForm.electricityThirdRate))
    addDictionaryChange(changes, 'Комментарий', formatChangeText(tariff.comment), formatChangeText(tariffForm.comment))
    return changes
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
            <label className="dictionary-archive-toggle">
              <input aria-label="Показывать архивные" type="checkbox" checked={showArchived} onChange={(event) => setShowArchived(event.target.checked)} />
              <span>Показывать архивные</span>
            </label>
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
                  <tr className={isArchivedRecord(item) ? 'dictionary-data-row-archived' : undefined} tabIndex={0} onContextMenu={(event) => openContextMenu(event, activeSection, item)} onDoubleClick={() => {
                    if (!isArchivedRecord(item)) {
                      openEditor(activeSection, 'edit', item)
                    }
                  }} key={`${activeSection}-${getDictionaryRecordTitle(activeSection, item)}-${'id' in item ? item.id : ''}`}>
                    {renderCells(item)}
                    <td>
                      <span className={isArchivedRecord(item) ? 'dictionary-status-pill dictionary-status-pill-archived' : 'dictionary-status-pill'}>
                        {isArchivedRecord(item) ? 'Архив' : 'Активна'}
                      </span>
                    </td>
                    <td>{renderRowAction(item)}</td>
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
          {isArchivedRecord(contextMenu.item) ? (
            <button type="button" role="menuitem" disabled={!canWriteActiveSection} onClick={() => {
              setRestoreTarget({ section: contextMenu.section, item: contextMenu.item })
              setContextMenu(null)
            }}>
              <RotateCcw size={15} />
              <span>Вернуть</span>
            </button>
          ) : (
            <>
              <button type="button" role="menuitem" disabled={!canWriteActiveSection} onClick={() => openEditor(contextMenu.section, 'edit', contextMenu.item)}>
                <Save size={15} />
                <span>Изменить</span>
              </button>
              <button className="context-menu-danger" type="button" role="menuitem" disabled={!canWriteActiveSection} onClick={() => {
                openArchiveTarget(contextMenu.section, contextMenu.item)
                setContextMenu(null)
              }}>
                <Trash2 size={15} />
                <span>Удалить</span>
              </button>
            </>
          )}
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

      {pendingEditorConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setPendingEditorConfirmation(null)}>
          <section ref={editorConfirmationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="dictionary-edit-confirmation-title" aria-describedby="dictionary-edit-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Изменение</p>
                <h3 id="dictionary-edit-confirmation-title">Подтвердите изменения</h3>
                <p>{getDictionaryRecordTitle(pendingEditorConfirmation.editor.section, pendingEditorConfirmation.editor.item as DictionaryRecord)}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить подтверждение изменений" onClick={() => setPendingEditorConfirmation(null)} disabled={saving === 'dictionary-editor'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="dictionary-edit-confirmation-description">Проверьте, что именно изменится. После подтверждения действие будет записано в историю изменений.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля">
              {pendingEditorConfirmation.changes.map((change) => (
                <li key={`${change.field}-${change.before}-${change.after}`}>
                  <span className="dictionary-change-field">{change.field}</span>
                  <span className="dictionary-change-values">
                    <span className="dictionary-change-value">{change.before}</span>
                    <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                    <span className="dictionary-change-value dictionary-change-value-after">{change.after}</span>
                  </span>
                </li>
              ))}
            </ul>
            <div className="detail-dialog-actions">
              <button ref={editorConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setPendingEditorConfirmation(null)} disabled={saving === 'dictionary-editor'}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmEditorChanges()} disabled={saving === 'dictionary-editor'}>
                <Save size={16} />
                <span>{saving === 'dictionary-editor' ? 'Сохраняем...' : 'Сохранить изменения'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {archiveTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => closeArchiveTarget()}>
          <section ref={archiveDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="dictionary-archive-title" aria-describedby="dictionary-archive-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="dictionary-archive-title">Подтвердите удаление</h3>
                <p>{getDictionaryRecordTitle(archiveTarget.section, archiveTarget.item)}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить удаление" onClick={() => closeArchiveTarget()} disabled={saving === 'dictionary-archive'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="dictionary-archive-description">Запись будет скрыта из рабочих таблиц, но останется в истории изменений и связанной финансовой истории.</p>
            <label className="field-label" htmlFor="dictionary-archive-reason">Причина удаления</label>
            <textarea
              id="dictionary-archive-reason"
              aria-label="Причина удаления"
              aria-invalid={Boolean(archiveReasonError)}
              aria-describedby={archiveReasonError ? 'dictionary-archive-reason-error' : undefined}
              maxLength={1000}
              value={archiveReason}
              onChange={(event) => {
                setArchiveReason(event.target.value)
                if (archiveReasonError && event.target.value.trim()) {
                  setArchiveReasonError(null)
                }
              }}
              placeholder="Например: дубль, ошибочная карточка, услуга больше не используется"
              disabled={saving === 'dictionary-archive'}
              required
            />
            {archiveReasonError ? <p className="form-error" id="dictionary-archive-reason-error">{archiveReasonError}</p> : null}
            <div className="detail-dialog-actions">
              <button ref={archiveCancelRef} className="ghost-button" type="button" onClick={() => closeArchiveTarget()} disabled={saving === 'dictionary-archive'}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={() => void confirmArchive()} disabled={saving === 'dictionary-archive' || !archiveReason.trim()}>
                <Trash2 size={16} />
                <span>{saving === 'dictionary-archive' ? 'Удаляем...' : 'Удалить запись'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {restoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setRestoreTarget(null)}>
          <section ref={restoreDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="dictionary-restore-title" aria-describedby="dictionary-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Восстановление</p>
                <h3 id="dictionary-restore-title">Вернуть запись из архива?</h3>
                <p>{getDictionaryRecordTitle(restoreTarget.section, restoreTarget.item)}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить восстановление" onClick={() => setRestoreTarget(null)} disabled={saving === 'dictionary-restore'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="dictionary-restore-description">Запись снова появится в рабочих списках. Действие будет записано в историю изменений.</p>
            <div className="detail-dialog-actions">
              <button ref={restoreCancelRef} className="ghost-button" type="button" onClick={() => setRestoreTarget(null)} disabled={saving === 'dictionary-restore'}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmRestore()} disabled={saving === 'dictionary-restore'}>
                <RotateCcw size={16} />
                <span>{saving === 'dictionary-restore' ? 'Возвращаем...' : 'Вернуть запись'}</span>
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
  const [tariffDraftConfirmation, setTariffDraftConfirmation] = useState<{
    title: string
    description: string
    confirmLabel: string
    action: () => void
  } | null>(null)
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
  useRestoreFocusOnClose(Boolean(tariffDraftConfirmation))
  const selectedGarageCloseButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(selectedGarage))
  const selectedGarageDialogRef = useFocusTrap<HTMLElement>(Boolean(selectedGarage))
  const tariffDraftConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(tariffDraftConfirmation))
  const tariffDraftConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(tariffDraftConfirmation))

  useEscapeKey(Boolean(selectedGarage), () => setSelectedGarage(null))
  useEscapeKey(Boolean(tariffDraftConfirmation), () => setTariffDraftConfirmation(null))
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
          dictionaryClient.getSupplierGroups(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getIncomeTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getExpenseTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit),
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

  function applyEditTariff(tariff: TariffDto) {
    const nextForm = createTariffFormFromDto(tariff)

    setEditingTariffId(tariff.id)
    setTariffValidationErrors([])
    setTariffForm(nextForm)
    setEditingTariffBaseline(nextForm)
  }

  function editTariff(tariff: TariffDto) {
    if (editingTariffId === tariff.id) {
      return
    }

    if (editingTariffId && hasUnsavedTariffChanges()) {
      setTariffDraftConfirmation({
        title: 'Перейти к другому тарифу?',
        description: 'Несохраненные изменения текущего тарифа будут потеряны.',
        confirmLabel: 'Перейти без сохранения',
        action: () => applyEditTariff(tariff),
      })
      return
    }

    applyEditTariff(tariff)
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
    if (editingTariffId && !options?.skipConfirmation && hasUnsavedTariffChanges()) {
      setTariffDraftConfirmation({
        title: 'Отменить редактирование тарифа?',
        description: 'Несохраненные изменения текущего тарифа будут потеряны.',
        confirmLabel: 'Отменить без сохранения',
        action: () => resetTariffForm({ skipConfirmation: true }),
      })
      return
    }

    setTariffDraftConfirmation(null)
    setEditingTariffId(null)
    setEditingTariffBaseline(null)
    setTariffValidationErrors([])
    setTariffForm((value) => withoutElectricityTierFields({ ...value, name: '', rate: 1, comment: '' }))
  }

  function confirmTariffDraftAction() {
    if (!tariffDraftConfirmation) {
      return
    }

    const action = tariffDraftConfirmation.action
    setTariffDraftConfirmation(null)
    action()
  }

  async function archiveDictionaryItem(scope: string, reason: string, action: (reason: string) => Promise<void>) {
    if (scope === 'tariff' && !canManageTariffs) {
      setError('Для архивирования тарифов нужно право tariffs.manage.')
      return
    }

    if (scope !== 'tariff' && !canWriteDictionaries) {
      setError('Для архивирования справочников нужно право dictionaries.write.')
      return
    }

    await runSaving(`archive-${scope}`, () => action(reason))
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
              onArchive: canWriteDictionaries ? (reason) => archiveDictionaryItem('owner', reason, async (archiveReason) => {
                await dictionaryClient.archiveOwner(auth.accessToken, owner.id, archiveReason)
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
              onArchive: canWriteDictionaries ? (reason) => archiveDictionaryItem('garage', reason, async (archiveReason) => {
                await dictionaryClient.archiveGarage(auth.accessToken, garage.id, archiveReason)
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
              onArchive: canWriteDictionaries ? (reason) => archiveDictionaryItem('supplier', reason, async (archiveReason) => {
                await dictionaryClient.archiveSupplier(auth.accessToken, supplier.id, archiveReason)
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
              onArchive: canWriteDictionaries ? (reason) => archiveDictionaryItem('income-type', reason, async (archiveReason) => {
                await dictionaryClient.archiveIncomeType(auth.accessToken, item.id, archiveReason)
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
              onArchive: canWriteDictionaries ? (reason) => archiveDictionaryItem('expense-type', reason, async (archiveReason) => {
                await dictionaryClient.archiveExpenseType(auth.accessToken, item.id, archiveReason)
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
              onArchive: canManageTariffs ? (reason) => archiveDictionaryItem('tariff', reason, async (archiveReason) => {
                await dictionaryClient.archiveTariff(auth.accessToken, item.id, archiveReason)
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
      {tariffDraftConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setTariffDraftConfirmation(null)}>
          <section ref={tariffDraftConfirmationDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="tariff-draft-confirmation-title" aria-describedby="tariff-draft-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Черновик тарифа</p>
                <h3 id="tariff-draft-confirmation-title">{tariffDraftConfirmation.title}</h3>
                <p>{editingTariffBaseline?.name || 'Тариф'}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Остаться в редактировании тарифа" onClick={() => setTariffDraftConfirmation(null)}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="tariff-draft-confirmation-description">{tariffDraftConfirmation.description}</p>
            <div className="detail-dialog-actions">
              <button ref={tariffDraftConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setTariffDraftConfirmation(null)}>
                Остаться
              </button>
              <button className="secondary-button danger-button" type="button" onClick={confirmTariffDraftAction}>
                <X size={16} />
                <span>{tariffDraftConfirmation.confirmLabel}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
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
  onArchive?: (reason: string) => Promise<void> | void
}

function DictionaryList({ items, emptyText }: { items: DictionaryListItem[]; emptyText: string }) {
  const [pendingArchive, setPendingArchive] = useState<DictionaryListItem | null>(null)
  const [confirmingArchive, setConfirmingArchive] = useState(false)
  const [archiveReason, setArchiveReason] = useState('')
  const [archiveReasonError, setArchiveReasonError] = useState<string | null>(null)
  const [showAllItems, setShowAllItems] = useState(false)
  const listId = useId()
  const compactLimit = 5
  const visibleItems = showAllItems ? items : items.slice(0, compactLimit)
  const hasHiddenItems = items.length > compactLimit
  useRestoreFocusOnClose(Boolean(pendingArchive))
  const archiveCancelButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingArchive) && !confirmingArchive)
  const archiveDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingArchive))

  useEscapeKey(Boolean(pendingArchive) && !confirmingArchive, () => closeArchiveDialog())

  function openArchiveDialog(item: DictionaryListItem) {
    setArchiveReason('')
    setArchiveReasonError(null)
    setPendingArchive(item)
  }

  function closeArchiveDialog() {
    setPendingArchive(null)
    setArchiveReason('')
    setArchiveReasonError(null)
  }

  async function confirmArchive() {
    if (!pendingArchive?.onArchive) {
      return
    }

    const reason = archiveReason.trim()
    if (!reason) {
      setArchiveReasonError('Укажите причину архивирования записи.')
      return
    }

    setConfirmingArchive(true)
    try {
      await pendingArchive.onArchive(reason)
      closeArchiveDialog()
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
                <button className="icon-button" type="button" aria-label={item.archiveLabel ?? `Архивировать ${item.title}`} onClick={() => openArchiveDialog(item)}>
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
            closeArchiveDialog()
          }
        }}>
          <section ref={archiveDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby={`archive-confirmation-${pendingArchive.id}`} aria-describedby={`archive-confirmation-description-${pendingArchive.id}`} onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Архивирование</p>
                <h3 id={`archive-confirmation-${pendingArchive.id}`}>Подтвердите архивирование</h3>
                <p>{pendingArchive.title}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить архивирование" onClick={() => closeArchiveDialog()} disabled={confirmingArchive}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id={`archive-confirmation-description-${pendingArchive.id}`}>Запись исчезнет из рабочих списков, но останется в истории изменений.</p>
            <label className="field-label" htmlFor={`archive-reason-${pendingArchive.id}`}>Причина архивирования</label>
            <textarea
              id={`archive-reason-${pendingArchive.id}`}
              aria-label="Причина архивирования"
              aria-invalid={Boolean(archiveReasonError)}
              aria-describedby={archiveReasonError ? `archive-reason-error-${pendingArchive.id}` : undefined}
              maxLength={1000}
              value={archiveReason}
              onChange={(event) => {
                setArchiveReason(event.target.value)
                if (archiveReasonError && event.target.value.trim()) {
                  setArchiveReasonError(null)
                }
              }}
              placeholder="Например: дубль, ошибочная запись, больше не используется"
              disabled={confirmingArchive}
              required
            />
            {archiveReasonError ? <p className="form-error" id={`archive-reason-error-${pendingArchive.id}`}>{archiveReasonError}</p> : null}
            <div className="detail-dialog-actions">
              <button ref={archiveCancelButtonRef} className="ghost-button" type="button" onClick={() => closeArchiveDialog()} disabled={confirmingArchive}>
                Отменить
              </button>
              <button className="secondary-button danger-button" type="button" onClick={() => void confirmArchive()} disabled={confirmingArchive || !archiveReason.trim()}>
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
