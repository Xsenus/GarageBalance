import { useEffect, useRef, useState } from 'react'
import { Pencil, Trash2, X } from 'lucide-react'
import type { AccrualDto, FinanceClient } from '../../services/financeApi'
import { AsyncErrorState, EmptyState, LoadingSkeleton } from '../../shared/AsyncState'
import { fitContextMenuToViewport, handleMenuArrowNavigation, useDismissOnWindowClick, useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { formatMoney } from '../../shared/formatters'
import { TablePagination } from '../../shared/TablePagination'
import type { GarageIncomePrototypeRow } from './garageIncomeWorksheetRows'

export type GarageAccrualActionTarget = { row: GarageIncomePrototypeRow; garageId: string; x: number; y: number }

export function GarageAccrualActions({ target, accessToken, financeClient, canWrite, onClose, onEdit, onCancel }: {
  target: GarageAccrualActionTarget
  accessToken: string
  financeClient: FinanceClient
  canWrite: boolean
  onClose: () => void
  onEdit: (record: AccrualDto) => void
  onCancel: (record: AccrualDto) => void
}) {
  const [action, setAction] = useState<'edit' | 'cancel' | null>(null)
  const [page, setPage] = useState({ offset: 0, limit: 10 })
  const [records, setRecords] = useState<AccrualDto[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [retry, setRetry] = useState(0)
  const firstItemRef = useFocusOnOpen<HTMLButtonElement>(true)
  const closeButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(action))
  const dialogRef = useFocusTrap<HTMLElement>(Boolean(action))
  const requestRef = useRef(0)
  useRestoreFocusOnClose(true)
  useEscapeKey(true, onClose)
  useDismissOnWindowClick(!action, onClose)

  useEffect(() => {
    if (!action || !canWrite) return
    const controller = new AbortController()
    const request = ++requestRef.current
    void financeClient.getAccrualsPage(accessToken, {
      garageId: target.garageId,
      incomeTypeId: target.row.incomeTypeId ?? undefined,
      irregularPaymentId: target.row.irregularPaymentId ?? undefined,
      feeCampaignId: target.row.feeCampaignId ?? undefined,
      monthFrom: `${target.row.month}-01`,
      monthTo: `${target.row.month}-01`,
      includeCanceled: false,
      ...page,
    }, controller.signal).then((result) => {
      if (controller.signal.aborted || request !== requestRef.current) return
      setRecords(result.items.filter((record) => record.garageId === target.garageId && record.incomeTypeId === target.row.incomeTypeId
        && (record.irregularPaymentId ?? null) === (target.row.irregularPaymentId ?? null)
        && (record.feeCampaignId ?? null) === (target.row.feeCampaignId ?? null)))
      setTotalCount(result.totalCount)
    }).catch((caught: unknown) => {
      if (!controller.signal.aborted && request === requestRef.current) {
        setError(caught instanceof Error ? caught.message : 'Не удалось получить начисления.')
      }
    }).finally(() => {
      if (!controller.signal.aborted && request === requestRef.current) setLoading(false)
    })
    return () => controller.abort()
  }, [accessToken, action, canWrite, financeClient, page, retry, target])

  if (!canWrite) return null
  if (!action) {
    const position = fitContextMenuToViewport(target.x, target.y)
    return <div className="context-menu" style={{ left: position.x, top: position.y }} role="menu" aria-label={`Действия начисления ${target.row.service}`} onClick={(event) => event.stopPropagation()} onKeyDown={handleMenuArrowNavigation}>
      <div className="context-menu-group" role="group">
        <button ref={firstItemRef} type="button" role="menuitem" onClick={() => { setLoading(true); setAction('edit') }}><Pencil size={15} aria-hidden="true" />Редактировать</button>
        <button className="context-menu-danger" type="button" role="menuitem" onClick={() => { setLoading(true); setAction('cancel') }}><Trash2 size={15} aria-hidden="true" />Удалить</button>
      </div>
    </div>
  }
  return <div className="modal-backdrop" role="presentation">
    <section ref={dialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-label="Выбор начисления гаража">
      <div className="detail-dialog-header"><h3>Выберите начисление: {target.row.service}</h3><button ref={closeButtonRef} className="icon-button" type="button" aria-label="Закрыть выбор начисления" onClick={onClose}><X size={18} aria-hidden="true" /></button></div>
      {loading ? <LoadingSkeleton label="Получаем начисления гаража" rows={3} columns={3} /> : error ? <AsyncErrorState message={error} onRetry={() => { setLoading(true); setError(null); setRetry((value) => value + 1) }} /> : records.length === 0 ? <EmptyState>На этой странице нет начислений выбранной услуги</EmptyState> : <div className="dictionary-table-scroll"><table className="dictionary-table" aria-label="Начисления выбранной услуги"><thead><tr><th>Основание</th><th>Сумма</th><th>Действие</th></tr></thead><tbody>{records.map((record) => <tr key={record.id}>
        <td>{record.basis ?? record.comment ?? record.incomeTypeName}</td><td>{formatMoney(record.amount)}</td><td>{record.feeCampaignId ? <span>Изменяется в карточке сбора</span> : <button className={action === 'cancel' ? 'ghost-button context-menu-danger' : 'secondary-button'} type="button" onClick={() => { onClose(); (action === 'edit' ? onEdit : onCancel)(record) }}>{action === 'edit' ? 'Редактировать' : 'Удалить'}</button>}</td>
      </tr>)}</tbody></table></div>}
      <TablePagination ariaLabel="Страницы начислений гаража" totalCount={totalCount} offset={page.offset} limit={page.limit} visibleCount={loading ? 0 : records.length} disabled={loading} onPageChange={(value) => { setLoading(true); setError(null); setPage((current) => ({ ...current, offset: (value - 1) * current.limit })) }} onPageSizeChange={(limit) => { setLoading(true); setError(null); setPage({ offset: 0, limit }) }} />
    </section>
  </div>
}
