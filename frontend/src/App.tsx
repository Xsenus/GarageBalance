import { useState } from 'react'
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
  PanelLeftClose,
  PanelLeftOpen,
  ShieldCheck,
  UsersRound,
  WalletCards,
} from 'lucide-react'
import { authApi } from './services/authApi'
import type { AuthClient, AuthResponse, CurrentUserDto } from './services/authApi'
import { AuthGate } from './features/auth/AuthGate'
import { PasswordPanel } from './features/settings/PasswordPanel'
import { FundsPrototypePanel } from './features/funds/FundsPanel'
import { ImportPanel } from './features/import/ImportPanel'
import { MeterReadingsPrototypePanel } from './features/meterReadings/MeterReadingsPanel'
import { AuditPanel } from './features/audit/AuditPanel'
import { ReportPanel } from './features/reports/ReportPanel'
import { UserManagementPanel } from './features/users/UserManagementPanel'
import { DictionaryPanelV2 } from './features/dictionaries/DictionaryPanel'
import { TariffsAndFeesPrototypePanel } from './features/tariffs/TariffsAndFeesPanel'
import { ContractorsPrototypePanel } from './features/contractors/ContractorsPanel'
import { FinancePanel } from './features/finance/FinancePanel'
import { auditApi } from './services/auditApi'
import type { AuditClient } from './services/auditApi'
import { dictionariesApi } from './services/dictionariesApi'
import type { DictionaryClient } from './services/dictionariesApi'
import { financeApi } from './services/financeApi'
import type { FinanceClient } from './services/financeApi'
import { fundsApi } from './services/fundsApi'
import type { FundsClient } from './services/fundsApi'
import { formStatesApi } from './services/formStatesApi'
import type { FormStateClient } from './services/formStatesApi'
import { importApi } from './services/importApi'
import type { ImportClient } from './services/importApi'
import { integrationsApi } from './services/integrationsApi'
import type { IntegrationClient } from './services/integrationsApi'
import { reportsApi } from './services/reportsApi'
import type { ReportClient } from './services/reportsApi'
import { releasesApi } from './services/releasesApi'
import { ReleasePanel } from './features/releases/ReleasePanel'
import type { ReleaseClient } from './services/releasesApi'
import { usersApi } from './services/usersApi'
import type { UserManagementClient } from './services/usersApi'
import { hasAnyPermission, hasPermission, permissions } from './shared/accessControl'
import type { AuditPanelPreset, WorkspaceOpenContext, WorkspaceSection } from './shared/workspaceNavigation'
import { clearStoredAuthSession, loadStoredAuthSession, saveStoredAuthSession } from './shared/sessionStorage'
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

const authSessionStorageKey = 'garagebalance.auth.session'
const sidebarExpandedStorageKey = 'garagebalance.sidebar.expanded'

type NavigationItem = {
  section: WorkspaceSection
  label: string
  icon: typeof Gauge
  requiredAny?: readonly string[]
}

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
  const [auditPreset, setAuditPreset] = useState<AuditPanelPreset | null>(null)
  const [workspaceOpenContext, setWorkspaceOpenContext] = useState<WorkspaceOpenContext | null>(null)
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
    setAuditPreset(null)
    setWorkspaceOpenContext(null)
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

  function openWorkspaceSection(section: WorkspaceSection, context: WorkspaceOpenContext | null = null) {
    setAuditPreset(null)
    setWorkspaceOpenContext(context)
    setActiveSection(section)
  }

  function openAuditWithPreset(preset: AuditPanelPreset) {
    setAuditPreset(preset)
    setWorkspaceOpenContext(null)
    setActiveSection('audit')
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
                  onClick={() => openWorkspaceSection(item.section)}
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
        <Workspace activeSection={effectiveActiveSection} auth={auth} authClient={authClient} auditClient={auditClient} auditPreset={auditPreset} workspaceOpenContext={workspaceOpenContext} dictionaryClient={dictionaryClient} financeClient={financeClient} fundsClient={fundsClient} formStateClient={formStateClient} importClient={importClient} integrationClient={integrationClient} reportClient={reportClient} releaseClient={releaseClient} userClient={userClient} onOpenAudit={openAuditWithPreset} onOpenSection={openWorkspaceSection} onUserChanged={handleUserChanged} onLogout={handleLogout} />
      </section>
    </main>
  )
}

function Workspace({
  activeSection,
  auth,
  authClient,
  auditClient,
  auditPreset,
  workspaceOpenContext,
  dictionaryClient,
  financeClient,
  fundsClient,
  formStateClient,
  importClient,
  integrationClient,
  reportClient,
  releaseClient,
  userClient,
  onOpenAudit,
  onOpenSection,
  onUserChanged,
  onLogout,
}: {
  activeSection: WorkspaceSection
  auth: AuthResponse
  authClient: AuthClient
  auditClient: AuditClient
  auditPreset: AuditPanelPreset | null
  workspaceOpenContext: WorkspaceOpenContext | null
  dictionaryClient: DictionaryClient
  financeClient: FinanceClient
  fundsClient: FundsClient
  formStateClient: FormStateClient
  importClient: ImportClient
  integrationClient: IntegrationClient
  reportClient: ReportClient
  releaseClient: ReleaseClient
  userClient: UserManagementClient
  onOpenAudit: (preset: AuditPanelPreset) => void
  onOpenSection: (section: WorkspaceSection, context?: WorkspaceOpenContext | null) => void
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
          <ContractorsPrototypePanel auth={auth} dictionaryClient={dictionaryClient} financeClient={financeClient} formStateClient={formStateClient} initialTarget={workspaceOpenContext?.contractorTarget ?? null} onOpenAudit={onOpenAudit} />
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
          <AuditPanel
            key={auditPreset ? `${auditPreset.section ?? ''}:${auditPreset.entityType ?? ''}:${auditPreset.relatedCounterparty ?? ''}` : 'audit-default'}
            auth={auth}
            auditClient={auditClient}
            preset={auditPreset}
            onOpenSection={onOpenSection}
          />
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

export default App
