import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ArrowLeft, FileSpreadsheet, FileText, RefreshCw, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { AuditClient, AuditEventDto } from '../../services/auditApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { TableLoadingState } from '../../shared/AsyncState'
import { buildAuditExportFileName, downloadBlob } from '../../shared/fileExports'
import { FormField } from '../../shared/FormField'
import { FormError, FormValidationSummary } from '../../shared/formFeedback'
import { formatAuditDateTime } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { createEmptyPage } from '../../shared/pagination'
import type { PagedItems } from '../../shared/pagination'
import { TablePagination } from '../../shared/TablePagination'
import { SelectControl } from '../../shared/SelectControl'
import type { AuditPanelPreset, ContractorOpenTarget, WorkspaceOpenContext, WorkspaceSection } from '../../shared/workspaceNavigation'
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

function formatAuditActor(auditEvent: AuditEventDto) {
  if (!auditEvent.actorUserId) {
    return 'Система'
  }

  return auditEvent.actorDisplayName || auditEvent.actorEmail || 'Пользователь'
}

function formatAuditActorId(actorUserId: string | null) {
  return actorUserId ? `ID ${actorUserId}` : 'Системное событие'
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

  const parsed = parseAuditBeforeAfter(auditEvent.summary)
  if (parsed.before !== 'не указано' || parsed.after !== 'не указано') {
    return parsed
  }

  return auditEvent.actionKind === 'update'
    ? parsed
    : { before: '—', after: '—' }
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

function getAuditContractorOpenTarget(auditEvent: AuditEventDto): ContractorOpenTarget | null {
  if (auditEvent.entityType === 'garage') {
    return {
      section: 'garages',
      entityId: auditEvent.entityId,
      displayName: auditEvent.entityDisplayName ?? auditEvent.relatedGarageNumber ?? null,
      garageNumber: auditEvent.relatedGarageNumber,
    }
  }

  if (auditEvent.entityType === 'owner') {
    return {
      section: 'garages',
      entityId: auditEvent.relatedGarageId,
      displayName: auditEvent.entityDisplayName ?? auditEvent.relatedCounterpartyName ?? null,
      garageNumber: auditEvent.relatedGarageNumber,
    }
  }

  if (auditEvent.entityType === 'supplier') {
    return {
      section: 'suppliers',
      entityId: auditEvent.entityId ?? auditEvent.relatedCounterpartyId,
      displayName: auditEvent.entityDisplayName ?? auditEvent.relatedCounterpartyName ?? null,
      garageNumber: null,
    }
  }

  if (auditEvent.entityType === 'staff_member') {
    return {
      section: 'staff',
      entityId: auditEvent.entityId ?? auditEvent.relatedCounterpartyId,
      displayName: auditEvent.entityDisplayName ?? auditEvent.relatedCounterpartyName ?? null,
      garageNumber: null,
    }
  }

  return null
}
function getAuditWorkspaceTarget(auth: AuthResponse, auditEvent: AuditEventDto): { section: WorkspaceSection; label: string } | null {
  if (auditEvent.entityType === 'owner' || auditEvent.entityType === 'garage' || auditEvent.entityType === 'supplier' || auditEvent.entityType === 'staff_member') {
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

export function AuditPanel({ auth, auditClient, preset, onOpenSection }: { auth: AuthResponse; auditClient: AuditClient; preset: AuditPanelPreset | null; onOpenSection: (section: WorkspaceSection, context?: WorkspaceOpenContext | null) => void }) {
  const [page, setPage] = useState<PagedItems<AuditEventDto>>(() => createEmptyPage<AuditEventDto>(25))
  const [search, setSearch] = useState('')
  const [section, setSection] = useState(preset?.section ?? '')
  const [actionKind, setActionKind] = useState('')
  const [entityType, setEntityType] = useState(preset?.entityType ?? '')
  const [actorUserId, setActorUserId] = useState('')
  const [quickFilter, setQuickFilter] = useState('')
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [relatedGarage, setRelatedGarage] = useState('')
  const [relatedAccountingMonth, setRelatedAccountingMonth] = useState('')
  const [relatedCounterparty, setRelatedCounterparty] = useState(preset?.relatedCounterparty ?? '')
  const [relatedDocument, setRelatedDocument] = useState('')
  const [appliedTextFilters, setAppliedTextFilters] = useState(() => ({
    search: '',
    actorUserId: '',
    relatedGarage: '',
    relatedCounterparty: preset?.relatedCounterparty ?? '',
    relatedDocument: '',
  }))
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
  useEffect(() => {
    const handle = window.setTimeout(() => {
      setAppliedTextFilters({ search, actorUserId, relatedGarage, relatedCounterparty, relatedDocument })
      resetAuditPageOffset()
    }, 350)
    return () => window.clearTimeout(handle)
  }, [actorUserId, relatedCounterparty, relatedDocument, relatedGarage, resetAuditPageOffset, search])
  const auditQuery = useMemo(() => ({
    search: appliedTextFilters.search.trim() || undefined,
    section: section || undefined,
    actionKind: actionKind || undefined,
    entityType: entityType || undefined,
    actorUserId: appliedTextFilters.actorUserId.trim() || undefined,
    quickFilter: quickFilter || undefined,
    dateFrom: dateFrom || undefined,
    dateTo: dateTo || undefined,
    relatedGarage: appliedTextFilters.relatedGarage.trim() || undefined,
    relatedAccountingMonth: relatedAccountingMonth || undefined,
    relatedCounterparty: appliedTextFilters.relatedCounterparty.trim() || undefined,
    relatedDocument: appliedTextFilters.relatedDocument.trim() || undefined,
    offset: page.offset,
    limit: page.limit,
  }), [actionKind, appliedTextFilters, dateFrom, dateTo, entityType, page.limit, page.offset, quickFilter, relatedAccountingMonth, section])
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
    const contractorTarget = section === 'contractors' && detailState ? getAuditContractorOpenTarget(detailState.event) : null
    closeAuditEventDetail()
    onOpenSection(section, contractorTarget ? { contractorTarget } : null)
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
  const auditHasValidationErrors = auditValidationErrors.length > 0

  return (
    <section className="dictionary-panel" aria-label="История изменений">
      <div className="section-heading">
        <div>
          <p className="eyebrow">История</p>
          <h2>История изменений объектов и действий системы</h2>
        </div>
        <div className="section-actions">
          {!loading ? <span>{page.totalCount} событий</span> : null}
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
          <input aria-label="Поиск в истории изменений" placeholder="Действие, объект или описание" value={search} onChange={(event) => setSearch(event.target.value)} />
        </FormField>
        <FormField label="Раздел">
          <SelectControl aria-label="Раздел истории изменений" value={section} options={auditSectionOptions} onChange={(value) => { setSection(value); resetAuditPageOffset() }} />
        </FormField>
        <FormField label="Действие">
          <SelectControl aria-label="Тип действия истории изменений" value={actionKind} options={auditActionKindOptions} onChange={(value) => { setActionKind(value); resetAuditPageOffset() }} />
        </FormField>
        <FormField label="Объект">
          <SelectControl aria-label="Тип объекта истории изменений" value={entityType} options={auditEntityTypeOptions} onChange={(value) => { setEntityType(value); resetAuditPageOffset() }} />
        </FormField>
        <FormField label="Пользователь">
          <input aria-label="ID пользователя истории изменений" placeholder="ID пользователя" value={actorUserId} onChange={(event) => setActorUserId(event.target.value)} />
        </FormField>
        <FormField label="Быстрый фильтр">
          <SelectControl aria-label="Быстрый фильтр истории изменений" value={quickFilter} options={auditQuickFilterOptions} onChange={(value) => { setQuickFilter(value); resetAuditPageOffset() }} />
        </FormField>
        <FormField label="Гараж">
          <input aria-label="Связанный гараж истории изменений" placeholder="Номер или ID гаража" value={relatedGarage} onChange={(event) => setRelatedGarage(event.target.value)} />
        </FormField>
        <FormField label="Месяц">
          <LocalizedDatePicker ariaLabel="Связанный месяц истории изменений" mode="month" value={relatedAccountingMonth} onChange={(value) => { setRelatedAccountingMonth(value); resetAuditPageOffset() }} />
        </FormField>
        <FormField label="Контрагент">
          <input aria-label="Связанный контрагент истории изменений" placeholder="Название или ID" value={relatedCounterparty} onChange={(event) => setRelatedCounterparty(event.target.value)} />
        </FormField>
        <FormField label="Документ">
          <input aria-label="Связанный документ истории изменений" placeholder="Номер или ID документа" value={relatedDocument} onChange={(event) => setRelatedDocument(event.target.value)} />
        </FormField>
        <FormField label="С даты">
          <LocalizedDatePicker ariaLabel="Начало периода истории изменений" mode="date" value={dateFrom} onChange={(value) => { setDateFrom(value); resetAuditPageOffset() }} />
        </FormField>
        <FormField label="По дату">
          <LocalizedDatePicker ariaLabel="Конец периода истории изменений" mode="date" value={dateTo} onChange={(value) => { setDateTo(value); resetAuditPageOffset() }} />
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
        {loading ? <TableLoadingState label="Загружаем историю изменений" /> : null}
        {!loading && page.items.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Событий пока нет</p> : null}
        {!loading ? page.items.map((auditEvent) => {
          const beforeAfter = getAuditBeforeAfter(auditEvent)
          return (
            <div className="audit-event-row" role="row" key={auditEvent.id}>
              <span role="cell">{formatAuditDateTime(auditEvent.createdAtUtc)}</span>
              <span role="cell" className="audit-actor-cell">
                <strong>{formatAuditActor(auditEvent)}</strong>
                {auditEvent.actorEmail ? <small>{auditEvent.actorEmail}</small> : null}
                <small>{formatAuditActorId(auditEvent.actorUserId)}</small>
              </span>
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
              <span role="cell">{auditEvent.fieldName ?? (auditEvent.actionKind === 'update' ? 'не указано' : '—')}</span>
              <span role="cell">{beforeAfter.before}</span>
              <span role="cell">{beforeAfter.after}</span>
              <span role="cell">{auditEvent.reason ?? 'не указано'}</span>
              <span role="cell">
                <button className="icon-button audit-detail-button" type="button" aria-label={`Открыть карточку события ${getAuditEventActionKindLabel(auditEvent)}`} title="Карточка события" onClick={() => void openAuditEventDetail(auditEvent)}>
                  <FileText size={15} aria-hidden="true" />
                </button>
              </span>
            </div>
          )
        }) : null}
      </div>
      <TablePagination
        ariaLabel="Пагинация истории изменений"
        totalCount={page.totalCount}
        offset={page.offset}
        limit={page.limit}
        visibleCount={page.items.length}
        disabled={loading}
        pageSizeLabel="Количество строк истории изменений"
        onPageChange={(pageNumber) => setPage((current) => ({ ...current, offset: (pageNumber - 1) * current.limit }))}
        onPageSizeChange={(limit) => setPage(createEmptyPage<AuditEventDto>(limit))}
      />
      {detailState ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeAuditEventDetail}>
          <section ref={detailDialogRef} className="detail-dialog audit-detail-dialog" role="dialog" aria-modal="true" aria-labelledby="audit-detail-title" aria-describedby="audit-detail-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Карточка события</p>
                <h3 id="audit-detail-title">{getAuditEventActionKindLabel(detailState.event)}</h3>
                <p id="audit-detail-description">{getAuditEventSectionLabel(detailState.event)} · {formatAuditDateTime(detailState.event.createdAtUtc)}</p>
              </div>
              <button ref={detailCloseButtonRef} className="icon-button" type="button" aria-label="Закрыть карточку события" onClick={closeAuditEventDetail}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            {detailState.loading ? <TableLoadingState className="table-loading-state--compact" label="Загружаем карточку события" rows={3} columns={2} /> : null}
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
                <dd>{formatAuditActor(detailState.event)}{detailState.event.actorEmail ? ` · ${detailState.event.actorEmail}` : ''}</dd>
              </div>
              <div>
                <dt>ID пользователя</dt>
                <dd>{formatAuditActorId(detailState.event.actorUserId)}</dd>
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
