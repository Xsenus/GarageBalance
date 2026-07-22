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
import { isAdministrator } from '../../shared/accessControl'
import { canAccessWorkspaceSection } from '../../shared/workspaceNavigation'
import type { AuditPanelPreset, WorkspaceOpenContext, WorkspaceSection } from '../../shared/workspaceNavigation'
import { Workspace } from './Workspace'
import { preloadWorkspaceSection } from './workspaceSectionLoader'

const sidebarExpandedStorageKey = 'garagebalance.sidebar.expanded'

type NavigationItem = {
  section: WorkspaceSection
  label: string
  icon: typeof Gauge
}

const navigation: NavigationItem[] = [
  { section: 'dashboard', label: 'Главное меню', icon: Gauge },
  { section: 'users', label: 'Пользователи', icon: ShieldCheck },
  { section: 'tariffsAndFees', label: 'Тарифы и сборы', icon: FileSpreadsheet },
  { section: 'contractors', label: 'Контрагенты', icon: UsersRound },
  { section: 'dictionaries', label: 'Справочники', icon: UsersRound },
  { section: 'meterReadings', label: 'Показания', icon: FileSpreadsheet },
  { section: 'payments', label: 'Платежи', icon: WalletCards },
  { section: 'funds', label: 'Фонды', icon: WalletCards },
  { section: 'reports', label: 'Отчеты', icon: FileSpreadsheet },
  { section: 'import', label: 'Импорт', icon: DatabaseZap },
  { section: 'audit', label: 'История изменений', icon: FileText },
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

  const effectiveActiveSection = canAccessWorkspaceSection(auth, activeSection) ? activeSection : 'dashboard'
  const visibleNavigation = navigation.filter((item) => canAccessWorkspaceSection(auth, item.section))
  const showSidebar = isAdministrator(auth)
  const sidebarModeClass = isSidebarExpanded ? 'app-shell--sidebar-expanded' : 'app-shell--sidebar-collapsed'
  const sidebarToggleLabel = isSidebarExpanded ? 'Свернуть панель' : 'Развернуть панель'
  const workspaceClassName = [
    'workspace',
    effectiveActiveSection === 'meterReadings' ? 'workspace--meter-readings' : '',
    effectiveActiveSection === 'contractors' ? 'workspace--contractors' : '',
    effectiveActiveSection === 'reports' ? 'workspace--reports' : '',
    effectiveActiveSection === 'funds' ? 'workspace--funds' : '',
  ].filter(Boolean).join(' ')

  function handleToggleSidebar() {
    setSidebarExpanded((current) => {
      const next = !current
      saveStoredSidebarExpanded(next)
      return next
    })
  }

  function openWorkspaceSection(section: WorkspaceSection, context: WorkspaceOpenContext | null = null) {
    const canOpen = canAccessWorkspaceSection(auth, section)
    setAuditPreset(null)
    setWorkspaceOpenContext(canOpen ? context : null)
    setActiveSection(canOpen ? section : 'dashboard')
  }

  function openAuditWithPreset(preset: AuditPanelPreset) {
    if (!canAccessWorkspaceSection(auth, 'audit')) {
      openWorkspaceSection('dashboard')
      return
    }

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
            {visibleNavigation.map((item) => {
              const Icon = item.icon
              const isActive = effectiveActiveSection === item.section
              return (
                <button className={isActive ? 'nav-item active' : 'nav-item'} type="button" key={item.section} aria-label={item.label} title={item.label} aria-current={isActive ? 'page' : undefined} onPointerEnter={() => preloadWorkspaceSection(item.section)} onFocus={() => preloadWorkspaceSection(item.section)} onClick={() => openWorkspaceSection(item.section)}>
                  <Icon size={18} />
                  <span>{item.label}</span>
                </button>
              )
            })}
          </nav>
        </aside>
      ) : null}

      <section className={workspaceClassName}>
        <Workspace activeSection={effectiveActiveSection} auth={auth} authClient={authClient} auditClient={auditClient} auditPreset={auditPreset} workspaceOpenContext={workspaceOpenContext} dictionaryClient={dictionaryClient} financeClient={financeClient} fundsClient={fundsClient} formStateClient={formStateClient} importClient={importClient} integrationClient={integrationClient} reportClient={reportClient} releaseClient={releaseClient} settingsClient={settingsClient} userClient={userClient} onOpenAudit={openAuditWithPreset} onOpenSection={openWorkspaceSection} onUserChanged={onUserChanged} onLogout={onLogout} />
      </section>
    </main>
  )
}
