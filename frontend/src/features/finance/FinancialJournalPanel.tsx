import { useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent, MouseEvent } from 'react'
import { History, Pencil, RotateCcw, Search, Trash2 } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { FinanceClient, FinancePagedResult, FinancialJournalEntryDto, FinancialJournalPageParams } from '../../services/financeApi'
import type { FundsClient } from '../../services/fundsApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { BackgroundRefreshStatus, EmptyState, TableLoadingState } from '../../shared/AsyncState'
import { FormError } from '../../shared/formFeedback'
import { formatDateOnly, formatMoney, formatMonth } from '../../shared/formatters'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { SelectControl } from '../../shared/SelectControl'
import { TablePagination } from '../../shared/TablePagination'
import type { AuditPanelPreset } from '../../shared/workspaceNavigation'
import { focusAfterDomUpdate, useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'

const journalEntityOptions = [
  { value: '', label: 'Все виды операций' },
  { value: 'financial_operation', label: 'Поступления и выплаты' },
  { value: 'accrual', label: 'Начисления гаражам' },
  { value: 'supplier_accrual', label: 'Начисления поставщикам' },
  { value: 'staff_salary_adjustment', label: 'Премии и штрафы' },
  { value: 'fund_operation', label: 'Движения фондов' },
  { value: 'cash_bank_transfer', label: 'Переводы касса → банк' },
  { value: 'cash_bank_balance_operation', label: 'Стартовые корректировки' },
]

const journalStatusOptions = [
  { value: '', label: 'Все статусы' },
  { value: 'active', label: 'Активные' },
  { value: 'canceled', label: 'Отменённые' },
]

const entityLabels: Record<FinancialJournalEntryDto['entityType'], string> = {
  financial_operation: 'Платёж',
  accrual: 'Начисление гаражу',
  supplier_accrual: 'Начисление поставщику',
  staff_salary_adjustment: 'Корректировка зарплаты',
  fund_operation: 'Движение фонда',
  cash_bank_transfer: 'Касса → банк',
  cash_bank_balance_operation: 'Корректировка остатка',
}

const sourceLabels: Record<string, string> = {
  manual: 'Ручная запись',
  regular: 'Регулярное начисление',
  receipt_batch: 'Пакет оплаты',
  derived: 'Рассчитано автоматически',
  protected_adjustment: 'Защищённая корректировка',
}

type JournalFilter = {
  dateFrom: string
  dateTo: string
  entityType: string
  counterparty: string
  status: '' | 'active' | 'canceled'
  document: string
}

const emptyFilter: JournalFilter = { dateFrom: '', dateTo: '', entityType: '', counterparty: '', status: '', document: '' }

export function FinancialJournalPanel({
  auth,
  financeClient,
  fundsClient,
  onEdit,
  onOpenAudit,
}: {
  auth: AuthResponse
  financeClient: FinanceClient
  fundsClient: FundsClient
  onEdit: (entry: FinancialJournalEntryDto) => void
  onOpenAudit?: (preset: AuditPanelPreset) => void
}) {
  const canWrite = hasPermission(auth, permissions.paymentsWrite)
  const canReadAudit = hasPermission(auth, permissions.auditRead)
  const [draft, setDraft] = useState<JournalFilter>(emptyFilter)
  const [filter, setFilter] = useState<JournalFilter>(emptyFilter)
  const [page, setPage] = useState<FinancePagedResult<FinancialJournalEntryDto>>({ items: [], totalCount: 0, offset: 0, limit: 25 })
  const [loading, setLoading] = useState(true)
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [menu, setMenu] = useState<{ entry: FinancialJournalEntryDto; x: number; y: number } | null>(null)
  const [cancelTarget, setCancelTarget] = useState<FinancialJournalEntryDto | null>(null)
  const [cancelReason, setCancelReason] = useState('')
  const [actionError, setActionError] = useState<string | null>(null)
  const [actionPending, setActionPending] = useState(false)
  const requestSequence = useRef(0)
  const filterRef = useRef(filter)
  const menuTriggerRef = useRef<HTMLElement | null>(null)
  const menuRef = useRef<HTMLDivElement | null>(null)
  const firstMenuItemRef = useFocusOnOpen<HTMLButtonElement>(Boolean(menu))
  useRestoreFocusOnClose(Boolean(cancelTarget))
  const cancelDialogRef = useFocusTrap<HTMLElement>(Boolean(cancelTarget))
  const cancelReasonRef = useFocusOnOpen<HTMLTextAreaElement>(Boolean(cancelTarget))

  const dismissMenu = useCallback(() => {
    setMenu(null)
    focusAfterDomUpdate(menuTriggerRef.current)
  }, [])

  useEscapeKey(Boolean(menu), dismissMenu)
  useEscapeKey(Boolean(cancelTarget) && !actionPending, () => {
    setCancelTarget(null)
    setActionError(null)
  })

  useEffect(() => {
    if (!menu) return undefined
    const handlePointerDown = (event: PointerEvent) => {
      if (!menuRef.current?.contains(event.target as Node)) dismissMenu()
    }
    window.addEventListener('pointerdown', handlePointerDown, true)
    return () => window.removeEventListener('pointerdown', handlePointerDown, true)
  }, [dismissMenu, menu])

  useEffect(() => { filterRef.current = filter }, [filter])

  const loadPage = useCallback(async (nextOffset: number, nextLimit: number, nextFilter?: JournalFilter) => {
    const getPage = financeClient.getFinancialJournalPage
    if (!getPage) {
      setError('Единый журнал недоступен в этой версии клиента.')
      setLoading(false)
      return
    }

    requestSequence.current += 1
    const requestId = requestSequence.current
    setLoading(true)
    setError(null)
    const params: FinancialJournalPageParams = { ...(nextFilter ?? filterRef.current), offset: nextOffset, limit: nextLimit }
    try {
      const result = await getPage(auth.accessToken, params)
      if (requestId !== requestSequence.current) return
      setPage(result)
      setLoaded(true)
    } catch (caught) {
      if (requestId !== requestSequence.current) return
      setError(caught instanceof Error ? caught.message : 'Не удалось загрузить единый журнал.')
    } finally {
      if (requestId === requestSequence.current) setLoading(false)
    }
  }, [auth.accessToken, financeClient])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadPage(0, 25, emptyFilter)
    return () => { requestSequence.current += 1 }
  }, [loadPage])

  function applyFilter(event: FormEvent) {
    event.preventDefault()
    setFilter(draft)
    void loadPage(0, page.limit, draft)
  }

  function openMenu(event: MouseEvent<HTMLElement>, entry: FinancialJournalEntryDto) {
    event.preventDefault()
    menuTriggerRef.current = event.currentTarget
    setMenu({ entry, x: event.clientX, y: event.clientY })
  }

  function openMenuFromKeyboard(event: KeyboardEvent<HTMLElement>, entry: FinancialJournalEntryDto) {
    if (event.key !== 'ContextMenu' && !(event.shiftKey && event.key === 'F10')) return
    event.preventDefault()
    const bounds = event.currentTarget.getBoundingClientRect()
    menuTriggerRef.current = event.currentTarget
    setMenu({ entry, x: bounds.left + 16, y: bounds.top + 16 })
  }

  function closeMenu() {
    setMenu(null)
  }

  function editEntry(entry: FinancialJournalEntryDto) {
    closeMenu()
    onEdit(entry)
  }

  async function restoreEntry(entry: FinancialJournalEntryDto) {
    setActionPending(true)
    setActionError(null)
    closeMenu()
    try {
      if (entry.entityType === 'financial_operation') await financeClient.restoreOperation(auth.accessToken, entry.id)
      else if (entry.entityType === 'accrual') await financeClient.restoreAccrual(auth.accessToken, entry.id)
      else if (entry.entityType === 'supplier_accrual') await financeClient.restoreSupplierAccrual(auth.accessToken, entry.id)
      else if (entry.entityType === 'staff_salary_adjustment' && entry.version) await financeClient.restoreStaffSalaryAdjustment(auth.accessToken, entry.id, entry.version)
      else if (entry.entityType === 'fund_operation') await fundsClient.restoreOperation(auth.accessToken, entry.id)
      else throw new Error(entry.correctionHint ?? 'Для этой строки восстановление недоступно.')
      await loadPage(page.offset, page.limit)
    } catch (caught) {
      setActionError(caught instanceof Error ? caught.message : 'Не удалось восстановить запись.')
    } finally {
      setActionPending(false)
      menuTriggerRef.current?.focus()
    }
  }

  async function confirmCancel() {
    if (!cancelTarget) return
    if (!cancelReason.trim()) {
      setActionError('Укажите причину отмены.')
      return
    }

    setActionPending(true)
    setActionError(null)
    try {
      const request = { reason: cancelReason.trim() }
      if (cancelTarget.entityType === 'financial_operation') await financeClient.cancelOperation(auth.accessToken, cancelTarget.id, request)
      else if (cancelTarget.entityType === 'accrual') await financeClient.cancelAccrual(auth.accessToken, cancelTarget.id, request)
      else if (cancelTarget.entityType === 'supplier_accrual') await financeClient.cancelSupplierAccrual(auth.accessToken, cancelTarget.id, request)
      else if (cancelTarget.entityType === 'staff_salary_adjustment' && cancelTarget.version) await financeClient.cancelStaffSalaryAdjustment(auth.accessToken, cancelTarget.id, { ...request, expectedVersion: cancelTarget.version })
      else if (cancelTarget.entityType === 'fund_operation') await fundsClient.cancelOperation(auth.accessToken, cancelTarget.id, request)
      else throw new Error(cancelTarget.correctionHint ?? 'Для этой строки отмена недоступна.')
      setCancelTarget(null)
      setCancelReason('')
      await loadPage(page.offset, page.limit)
      menuTriggerRef.current?.focus()
    } catch (caught) {
      setActionError(caught instanceof Error ? caught.message : 'Не удалось отменить запись.')
    } finally {
      setActionPending(false)
    }
  }

  function openHistory(entry: FinancialJournalEntryDto) {
    closeMenu()
    onOpenAudit?.({ section: 'finance', entityType: entry.entityType, relatedCounterparty: entry.counterparty })
  }

  return (
    <section className="finance-workbench" aria-label="Единый журнал финансовых операций">
      <form className="dictionary-toolbar finance-table-toolbar" onSubmit={applyFilter} aria-label="Фильтры единого журнала">
        <div className="finance-period-filter" aria-label="Период журнала">
          <LocalizedDatePicker ariaLabel="Дата журнала с" mode="date" value={draft.dateFrom} onChange={(dateFrom) => setDraft((current) => ({ ...current, dateFrom }))} />
          <LocalizedDatePicker ariaLabel="Дата журнала по" mode="date" value={draft.dateTo} onChange={(dateTo) => setDraft((current) => ({ ...current, dateTo }))} />
        </div>
        <SelectControl aria-label="Вид операции журнала" value={draft.entityType} options={journalEntityOptions} onChange={(entityType) => setDraft((current) => ({ ...current, entityType }))} />
        <SelectControl aria-label="Статус операции журнала" value={draft.status} options={journalStatusOptions} onChange={(status) => setDraft((current) => ({ ...current, status: status as JournalFilter['status'] }))} />
        <label className="dictionary-search">
          <Search size={16} aria-hidden="true" />
          <input aria-label="Контрагент журнала" placeholder="Гараж, владелец, поставщик, сотрудник" value={draft.counterparty} onChange={(event) => setDraft((current) => ({ ...current, counterparty: event.target.value }))} />
        </label>
        <label className="dictionary-search">
          <Search size={16} aria-hidden="true" />
          <input aria-label="Документ журнала" placeholder="Номер документа" value={draft.document} onChange={(event) => setDraft((current) => ({ ...current, document: event.target.value }))} />
        </label>
        <button className="secondary-button" type="submit" disabled={loading}>Применить</button>
      </form>

      {error ? <FormError>{error} <button className="inline-button" type="button" onClick={() => void loadPage(page.offset, page.limit)}>Повторить</button></FormError> : null}
      {actionError && !cancelTarget ? <FormError>{actionError}</FormError> : null}
      <div className="dictionary-table-shell">
        <div className="dictionary-table-scroll" aria-busy={loading}>
          <table className="dictionary-table" aria-label="Все финансовые операции">
            <thead><tr><th>Дата</th><th>Вид</th><th>Контрагент</th><th>Статья</th><th>Документ</th><th>Сумма</th><th>Статус</th></tr></thead>
            <tbody>
              {page.items.map((entry) => (
                <tr key={`${entry.entityType}-${entry.id}`} tabIndex={0} onContextMenu={(event) => openMenu(event, entry)} onKeyDown={(event) => openMenuFromKeyboard(event, entry)} onDoubleClick={() => entry.canEdit && canWrite ? editEntry(entry) : undefined}>
                  <td>{formatDateOnly(entry.operationDate)}{entry.accountingMonth ? <small>{formatMonth(entry.accountingMonth)}</small> : null}</td>
                  <td><strong>{entityLabels[entry.entityType]}</strong><small>{sourceLabels[entry.source] ?? entry.source}</small></td>
                  <td>{entry.counterparty}</td>
                  <td>{entry.category}{entry.protectionReason ? <small title={entry.correctionHint ?? undefined}>Защищено: {entry.protectionReason}</small> : null}</td>
                  <td>{entry.documentNumber ?? '—'}</td>
                  <td className="operation-amount">{formatMoney(entry.amount)}</td>
                  <td>{entry.isCanceled ? 'Отменена' : 'Активна'}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {loading && !loaded ? <TableLoadingState label="Загружаем единый журнал" /> : null}
          {loading && loaded ? <BackgroundRefreshStatus label="Обновляем единый журнал" /> : null}
          {!loading && loaded && page.items.length === 0 ? <EmptyState>По выбранным условиям операций нет.</EmptyState> : null}
        </div>
        <TablePagination
          ariaLabel="Пагинация единого журнала"
          totalCount={page.totalCount}
          offset={page.offset}
          limit={page.limit}
          visibleCount={page.items.length}
          disabled={loading || actionPending}
          pageSizeLabel="Количество строк единого журнала"
          onPageChange={(nextPage) => void loadPage((nextPage - 1) * page.limit, page.limit)}
          onPageSizeChange={(limit) => void loadPage(0, limit)}
        />
      </div>

      {menu ? (
        <div ref={menuRef} className="context-menu" style={{ left: menu.x, top: menu.y }} role="menu" aria-label={`Действия записи журнала ${menu.entry.counterparty}`}>
          <div className="context-menu-group" role="group">
            {menu.entry.canEdit && canWrite ? <button ref={firstMenuItemRef} type="button" role="menuitem" onClick={() => editEntry(menu.entry)}><Pencil size={15} aria-hidden="true" /><span>Редактировать</span></button> : null}
            {menu.entry.canCancel && canWrite && !menu.entry.isCanceled ? <button ref={!menu.entry.canEdit || !canWrite ? firstMenuItemRef : undefined} className="context-menu-danger" type="button" role="menuitem" onClick={() => { setCancelTarget(menu.entry); setCancelReason(''); setActionError(null); closeMenu() }}><Trash2 size={15} aria-hidden="true" /><span>Отменить</span></button> : null}
            {menu.entry.canRestore && canWrite && menu.entry.isCanceled ? <button ref={!menu.entry.canEdit || !canWrite ? firstMenuItemRef : undefined} type="button" role="menuitem" onClick={() => void restoreEntry(menu.entry)}><RotateCcw size={15} aria-hidden="true" /><span>Восстановить</span></button> : null}
          </div>
          {canReadAudit && onOpenAudit ? <><div className="context-menu-separator" role="separator" /><div className="context-menu-group" role="group"><button ref={!canWrite || (!menu.entry.canEdit && !menu.entry.canCancel && !menu.entry.canRestore) ? firstMenuItemRef : undefined} type="button" role="menuitem" onClick={() => openHistory(menu.entry)}><History size={15} aria-hidden="true" /><span>История</span></button></div></> : null}
        </div>
      ) : null}

      {cancelTarget ? (
        <div className="modal-backdrop" role="presentation">
          <section ref={cancelDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="journal-cancel-title">
            <div className="detail-dialog-header"><div><p className="eyebrow">Единый журнал</p><h3 id="journal-cancel-title">Отменить запись?</h3><p>{cancelTarget.counterparty} · {formatMoney(cancelTarget.amount)}</p></div></div>
            <label>Причина отмены<textarea ref={cancelReasonRef} aria-label="Причина отмены записи журнала" value={cancelReason} onChange={(event) => setCancelReason(event.target.value)} /></label>
            {actionError ? <FormError>{actionError}</FormError> : null}
            <div className="detail-dialog-actions">
              <button className="ghost-button" type="button" disabled={actionPending} onClick={() => { setCancelTarget(null); setActionError(null); menuTriggerRef.current?.focus() }}>Оставить запись</button>
              <button className="secondary-button danger-button" type="button" disabled={actionPending} onClick={() => void confirmCancel()}>{actionPending ? 'Отменяем…' : 'Отменить запись'}</button>
            </div>
          </section>
        </div>
      ) : null}
    </section>
  )
}
