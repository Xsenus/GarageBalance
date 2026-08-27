import { useCallback, useMemo, useState } from 'react'
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
import type { AuthClient, AuthResponse } from '../../services/authApi'
import { auditApi } from '../../services/auditApi'
import type { AuditClient } from '../../services/auditApi'
import { dictionariesApi } from '../../services/dictionariesApi'
import type { DictionaryClient } from '../../services/dictionariesApi'
import { financeApi } from '../../services/financeApi'
import type { FinanceClient } from '../../services/financeApi'
import { fundsApi } from '../../services/fundsApi'
import type { FundsClient } from '../../services/fundsApi'
import { importApi } from '../../services/importApi'
import type { ImportClient } from '../../services/importApi'
import { integrationsApi } from '../../services/integrationsApi'
import type { IntegrationClient } from '../../services/integrationsApi'
import { releasesApi } from '../../services/releasesApi'
import type { ReleaseClient } from '../../services/releasesApi'
import { reportsApi } from '../../services/reportsApi'
import type { ReportClient } from '../../services/reportsApi'
import { usersApi } from '../../services/usersApi'
import type { UserManagementClient } from '../../services/usersApi'
import { settingsApi } from '../../services/settingsApi'
import type { ApplicationSettingsClient } from '../../services/settingsApi'
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
  auditClient?: AuditClient
  dictionaryClient?: DictionaryClient
  financeClient?: FinanceClient
  fundsClient?: FundsClient
  importClient?: ImportClient
  integrationClient?: IntegrationClient
  reportClient?: ReportClient
  releaseClient?: ReleaseClient
  userClient?: UserManagementClient
  settingsClient?: ApplicationSettingsClient
  onLogout: () => void
}

export function AuthenticatedAppShell({ auth, authClient, auditClient = auditApi, dictionaryClient = dictionariesApi, financeClient = financeApi, fundsClient = fundsApi, importClient = importApi, integrationClient = integrationsApi, reportClient = reportsApi, releaseClient = releasesApi, settingsClient = settingsApi, userClient = usersApi, onLogout }: AppShellProps) {
  const [activeSection, setActiveSection] = useState<WorkspaceSection>('dashboard')
  const [auditPreset, setAuditPreset] = useState<AuditPanelPreset | null>(null)
  const [workspaceOpenContext, setWorkspaceOpenContext] = useState<WorkspaceOpenContext | null>(null)
  const [isSidebarExpanded, setSidebarExpanded] = useState(loadStoredSidebarExpanded)

  const effectiveActiveSection = canAccessWorkspaceSection(auth, activeSection) ? activeSection : 'dashboard'
  const visibleNavigation = useMemo(
    () => navigation.filter((item) => canAccessWorkspaceSection(auth, item.section)),
    [auth],
  )
  const sidebarModeClass = isSidebarExpanded ? 'app-shell--sidebar-expanded' : 'app-shell--sidebar-collapsed'
  const sidebarToggleLabel = isSidebarExpanded ? 'Свернуть панель' : 'Развернуть панель'
  const workspaceClassName = [
    'workspace',
    effectiveActiveSection === 'meterReadings' ? 'workspace--meter-readings' : '',
    effectiveActiveSection === 'contractors' ? 'workspace--contractors' : '',
    effectiveActiveSection === 'reports' ? 'workspace--reports' : '',
    effectiveActiveSection === 'funds' ? 'workspace--funds' : '',
  ].filter(Boolean).join(' ')

  const handleToggleSidebar = useCallback(() => {
    setSidebarExpanded((current) => {
      const next = !current
      saveStoredSidebarExpanded(next)
      return next
    })
  }, [])

  const openWorkspaceSection = useCallback((section: WorkspaceSection, context: WorkspaceOpenContext | null = null) => {
    const canOpen = canAccessWorkspaceSection(auth, section)
    setAuditPreset(null)
    setWorkspaceOpenContext(canOpen ? context : null)
    setActiveSection(canOpen ? section : 'dashboard')
  }, [auth])

  const openAuditWithPreset = useCallback((preset: AuditPanelPreset) => {
    if (!canAccessWorkspaceSection(auth, 'audit')) {
      openWorkspaceSection('dashboard')
      return
    }

    setAuditPreset(preset)
    setWorkspaceOpenContext(null)
    setActiveSection('audit')
  }, [auth, openWorkspaceSection])

  return (
    <main className={`app-shell ${sidebarModeClass}`}>
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

      <section className={workspaceClassName}>
        <Workspace activeSection={effectiveActiveSection} auth={auth} authClient={authClient} auditClient={auditClient} auditPreset={auditPreset} workspaceOpenContext={workspaceOpenContext} dictionaryClient={dictionaryClient} financeClient={financeClient} fundsClient={fundsClient} importClient={importClient} integrationClient={integrationClient} reportClient={reportClient} releaseClient={releaseClient} settingsClient={settingsClient} userClient={userClient} onOpenAudit={openAuditWithPreset} onOpenSection={openWorkspaceSection} onLogout={onLogout} />
      </section>
    </main>
  )
}
