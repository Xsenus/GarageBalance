import { lazy, Suspense, useState } from 'react'
import { ArrowLeft, Bell, LockKeyhole, LogOut, X } from 'lucide-react'
import type { AuthClient, AuthResponse, CurrentUserDto } from '../../services/authApi'
import type { AuditClient } from '../../services/auditApi'
import type { DictionaryClient } from '../../services/dictionariesApi'
import type { FinanceClient } from '../../services/financeApi'
import type { FundsClient } from '../../services/fundsApi'
import type { FormStateClient } from '../../services/formStatesApi'
import type { ImportClient } from '../../services/importApi'
import type { IntegrationClient } from '../../services/integrationsApi'
import type { ReportClient } from '../../services/reportsApi'
import type { ReleaseClient } from '../../services/releasesApi'
import type { UserManagementClient } from '../../services/usersApi'
import type { ApplicationSettingsClient } from '../../services/settingsApi'
import { hasAnyPermission, hasPermission, permissions } from '../../shared/accessControl'
import { LoadingSkeleton } from '../../shared/AsyncState'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import type { AuditPanelPreset, WorkspaceOpenContext, WorkspaceSection } from '../../shared/workspaceNavigation'

const PasswordPanel = lazy(() => import('../settings/PasswordPanel').then((module) => ({ default: module.PasswordPanel })))
const FundsPrototypePanel = lazy(() => import('../funds/FundsPanel').then((module) => ({ default: module.FundsPrototypePanel })))
const ImportPanel = lazy(() => import('../import/ImportPanel').then((module) => ({ default: module.ImportPanel })))
const MeterReadingsPrototypePanel = lazy(() => import('../meterReadings/MeterReadingsPanel').then((module) => ({ default: module.MeterReadingsPrototypePanel })))
const AuditPanel = lazy(() => import('../audit/AuditPanel').then((module) => ({ default: module.AuditPanel })))
const ReportPanel = lazy(() => import('../reports/ReportPanel').then((module) => ({ default: module.ReportPanel })))
const UserManagementPanel = lazy(() => import('../users/UserManagementPanel').then((module) => ({ default: module.UserManagementPanel })))
const DictionaryPanelV2 = lazy(() => import('../dictionaries/DictionaryPanel').then((module) => ({ default: module.DictionaryPanelV2 })))
const TariffsAndFeesPrototypePanel = lazy(() => import('../tariffs/TariffsAndFeesPanel').then((module) => ({ default: module.TariffsAndFeesPrototypePanel })))
const ContractorsPrototypePanel = lazy(() => import('../contractors/ContractorsPanel').then((module) => ({ default: module.ContractorsPrototypePanel })))
const FinancePanel = lazy(() => import('../finance/FinancePanel').then((module) => ({ default: module.FinancePanel })))
const ReleasePanel = lazy(() => import('../releases/ReleasePanel').then((module) => ({ default: module.ReleasePanel })))

const dashboardTiles: { title: string; section: WorkspaceSection; requiredAny?: readonly string[] }[] = [
  { title: 'Тарифы\nи сборы', section: 'tariffsAndFees', requiredAny: [permissions.dictionariesRead] },
  { title: 'Контрагенты', section: 'contractors', requiredAny: [permissions.dictionariesRead] },
  { title: 'Счётчики', section: 'meterReadings', requiredAny: [permissions.paymentsRead] },
  { title: 'Платежи', section: 'payments', requiredAny: [permissions.paymentsRead] },
  { title: 'Отчёты', section: 'reports', requiredAny: [permissions.reportsRead] },
  { title: 'Настройки', section: 'settings' },
  { title: 'Управление\nфондами', section: 'funds', requiredAny: [permissions.reportsRead] },
]

export function Workspace({
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
  settingsClient,
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
  settingsClient: ApplicationSettingsClient
  onOpenAudit: (preset: AuditPanelPreset) => void
  onOpenSection: (section: WorkspaceSection, context?: WorkspaceOpenContext | null) => void
  onUserChanged: (user: CurrentUserDto) => void
  onLogout: () => void
}) {
  const [logoutConfirmationOpen, setLogoutConfirmationOpen] = useState(false)
  useRestoreFocusOnClose(logoutConfirmationOpen)
  const logoutConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(logoutConfirmationOpen)
  const logoutConfirmationDialogRef = useFocusTrap<HTMLElement>(logoutConfirmationOpen)
  useEscapeKey(logoutConfirmationOpen, () => setLogoutConfirmationOpen(false))

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
          <ContractorsPrototypePanel auth={auth} dictionaryClient={dictionaryClient} financeClient={financeClient} formStateClient={formStateClient} integrationClient={integrationClient} initialTarget={workspaceOpenContext?.contractorTarget ?? null} onOpenAudit={onOpenAudit} />
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
          <FinancePanel auth={auth} dictionaryClient={dictionaryClient} financeClient={financeClient} fundsClient={fundsClient} formStateClient={formStateClient} integrationClient={integrationClient} settingsClient={settingsClient} />
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
        return <PasswordPanel auth={auth} authClient={authClient} integrationClient={integrationClient} settingsClient={settingsClient} onUserChanged={onUserChanged} />
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
          <button className="icon-button" type="button" aria-label="Выйти" title="Выйти" onClick={() => setLogoutConfirmationOpen(true)}>
            <LogOut size={19} />
          </button>
        </div>
      </header>
      <Suspense fallback={<LoadingSkeleton label="Загружаем раздел" rows={5} columns={4} />}>
        {renderActiveSection()}
      </Suspense>
      {logoutConfirmationOpen ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setLogoutConfirmationOpen(false)}>
          <section
            ref={logoutConfirmationDialogRef}
            className="detail-dialog logout-confirmation-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="logout-confirmation-title"
            aria-describedby="logout-confirmation-description"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Завершение работы</p>
                <h3 id="logout-confirmation-title">Выйти из системы?</h3>
              </div>
              <button className="icon-button" type="button" aria-label="Остаться в системе" onClick={() => setLogoutConfirmationOpen(false)}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <p className="confirmation-text" id="logout-confirmation-description">
              Текущий сеанс будет завершён. Чтобы продолжить работу, потребуется снова войти в систему.
            </p>
            <div className="detail-dialog-actions">
              <button ref={logoutConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setLogoutConfirmationOpen(false)}>
                Отмена
              </button>
              <button
                className="secondary-button"
                type="button"
                onClick={() => {
                  setLogoutConfirmationOpen(false)
                  onLogout()
                }}
              >
                <LogOut size={16} aria-hidden="true" />
                <span>Выйти</span>
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
