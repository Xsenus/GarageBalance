import { useState } from 'react'
import {
  BookOpenCheck,
  DatabaseZap,
  FileSpreadsheet,
  FileText,
  Gauge,
  LockKeyhole,
  PanelLeftClose,
  PanelLeftOpen,
  ShieldCheck,
  UsersRound,
  WalletCards,
} from 'lucide-react'
import type { AuthClient, AuthResponse, CurrentUserDto } from '../../services/authApi'
import type { AuditClient } from '../../services/auditApi'
import type { DictionaryClient } from '../../services/dictionariesApi'
import type { FinanceClient } from '../../services/financeApi'
import type { FormStateClient } from '../../services/formStatesApi'
import type { FundsClient } from '../../services/fundsApi'
import type { ImportClient } from '../../services/importApi'
import type { IntegrationClient } from '../../services/integrationsApi'
import type { ReleaseClient } from '../../services/releasesApi'
import type { ReportClient } from '../../services/reportsApi'
import type { UserManagementClient } from '../../services/usersApi'
import type { ApplicationSettingsClient } from '../../services/settingsApi'
import { hasAnyPermission, isAdministrator, permissions } from '../../shared/accessControl'
import type { AuditPanelPreset, WorkspaceOpenContext, WorkspaceSection } from '../../shared/workspaceNavigation'
import { Workspace } from './Workspace'

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

function loadStoredSidebarExpanded(): boolean {
  try {
    return window.localStorage.getItem(sidebarExpandedStorageKey) === 'true'
  } catch {
    return false
  }
}

function saveStoredSidebarExpanded(expanded: boolean) {
  try {
    window.localStorage.setItem(sidebarExpandedStorageKey, expanded ? 'true' : 'false')
  } catch {
    // Sidebar state is only a UI preference; the app must work if localStorage is unavailable.
  }
}

type AppShellProps = {
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
  settingsClient: ApplicationSettingsClient
  onUserChanged: (user: CurrentUserDto) => void
  onLogout: () => void
}

export function AuthenticatedAppShell({ auth, authClient, auditClient, dictionaryClient, financeClient, fundsClient, formStateClient, importClient, integrationClient, reportClient, releaseClient, settingsClient, userClient, onUserChanged, onLogout }: AppShellProps) {
  const [activeSection, setActiveSection] = useState<WorkspaceSection>('dashboard')
  const [auditPreset, setAuditPreset] = useState<AuditPanelPreset | null>(null)
  const [workspaceOpenContext, setWorkspaceOpenContext] = useState<WorkspaceOpenContext | null>(null)
  const [isSidebarExpanded, setSidebarExpanded] = useState(loadStoredSidebarExpanded)

  const activeNavigationItem = navigation.find((entry) => entry.section === activeSection)
  const effectiveActiveSection = activeNavigationItem && hasAnyPermission(auth, activeNavigationItem.requiredAny) ? activeSection : 'dashboard'
  const showSidebar = isAdministrator(auth)
  const sidebarModeClass = isSidebarExpanded ? 'app-shell--sidebar-expanded' : 'app-shell--sidebar-collapsed'
  const sidebarToggleLabel = isSidebarExpanded ? 'Свернуть панель' : 'Развернуть панель'
  const workspaceClassName = [
    'workspace',
    effectiveActiveSection === 'meterReadings' ? 'workspace--meter-readings' : '',
    effectiveActiveSection === 'contractors' ? 'workspace--contractors' : '',
  ].filter(Boolean).join(' ')

  function handleToggleSidebar() {
    setSidebarExpanded((current) => {
      const next = !current
      saveStoredSidebarExpanded(next)
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
                <button className={isActive ? 'nav-item active' : 'nav-item'} type="button" key={item.section} disabled={!canOpen} aria-label={item.label} title={item.label} aria-current={isActive ? 'page' : undefined} onClick={() => openWorkspaceSection(item.section)}>
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

      <section className={workspaceClassName}>
        <Workspace activeSection={effectiveActiveSection} auth={auth} authClient={authClient} auditClient={auditClient} auditPreset={auditPreset} workspaceOpenContext={workspaceOpenContext} dictionaryClient={dictionaryClient} financeClient={financeClient} fundsClient={fundsClient} formStateClient={formStateClient} importClient={importClient} integrationClient={integrationClient} reportClient={reportClient} releaseClient={releaseClient} settingsClient={settingsClient} userClient={userClient} onOpenAudit={openAuditWithPreset} onOpenSection={openWorkspaceSection} onUserChanged={onUserChanged} onLogout={onLogout} />
      </section>
    </main>
  )
}
