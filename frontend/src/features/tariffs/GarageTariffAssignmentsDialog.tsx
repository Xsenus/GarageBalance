import { useEffect, useRef, useState } from 'react'
import { ListPlus, LoaderCircle, Pencil, Save, Trash2, UsersRound, X } from 'lucide-react'
import type { ChargeServiceSettingDto, DictionaryClient, ElectricityTariffTierDto, GarageDto, PagedResult } from '../../services/dictionariesApi'
import { DictionaryApiError } from '../../services/dictionariesApi'
import { garageTariffAssignmentsApi } from '../../services/garageTariffAssignmentsApi'
import type { GarageTariffAssignment, GarageTariffAssignmentsClient, GarageTariffTerms } from '../../services/garageTariffAssignmentsApi'
import { AsyncErrorState, EmptyState, TableLoadingState } from '../../shared/AsyncState'
import { useActionCommentSettings } from '../../shared/ActionCommentSettings'
import { FormField } from '../../shared/FormField'
import { FormError } from '../../shared/formFeedback'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { formatDateOnly, getLocalDateInputValue } from '../../shared/formatters'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { DecimalTextInput } from '../../shared/DecimalTextInput'
import { TablePagination } from '../../shared/TablePagination'
import { appendGarageTariffTier, createGarageTariffDraft, removeGarageTariffTier, validateGarageTariffDraft, validateGarageTariffReason } from './garageTariffDraft'
import type { GarageTariffDraft } from './garageTariffDraft'
import './garageTariffAssignments.css'

type Mode = { kind: 'create' } | { kind: 'edit' | 'archive'; row: GarageTariffAssignment }
const message = (error: unknown) => error instanceof Error ? error.message : 'Не удалось выполнить действие.'
const rateLabel = (rate: number) => rate.toLocaleString('ru-RU', { maximumFractionDigits: 4 })

export function GarageTariffAssignmentsDialog({ accessToken, service, rate, calculationBase, initialTiers = [],
  dictionaryClient, canWrite, onClose, client = garageTariffAssignmentsApi }: {
  accessToken: string; service: ChargeServiceSettingDto; rate: number; calculationBase: string; initialTiers?: ElectricityTariffTierDto[]
  dictionaryClient: DictionaryClient; canWrite: boolean; onClose: () => void; client?: GarageTariffAssignmentsClient
}) {
  const [page, setPage] = useState<PagedResult<GarageTariffAssignment> | null>(null)
  const [offset, setOffset] = useState(0)
  const [limit, setLimit] = useState(25)
  const [archived, setArchived] = useState(false)
  const [revision, setRevision] = useState(0)
  const [pageSource, setPageSource] = useState<{ key: string; client: GarageTariffAssignmentsClient } | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [mode, setMode] = useState<Mode | null>(null)
  const [draft, setDraft] = useState<GarageTariffDraft>(() => createGarageTariffDraft(getLocalDateInputValue(), rate, initialTiers))
  const [selected, setSelected] = useState<Array<{ id: string; number: string }>>([])
  const [search, setSearch] = useState('')
  const [garageOffset, setGarageOffset] = useState(0)
  const [garageLimit, setGarageLimit] = useState(25)
  const [garagePage, setGaragePage] = useState<PagedResult<GarageDto> | null>(null)
  const [garageSource, setGarageSource] = useState<{ key: string; client: DictionaryClient } | null>(null)
  const [garageError, setGarageError] = useState<string | null>(null)
  const [garageRevision, setGarageRevision] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [review, setReview] = useState<GarageTariffTerms | { reason: string | null } | null>(null)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const saving = useRef(false)
  const mutation = useRef<AbortController | null>(null)
  const [reasonRequired, reasonLoading, reasonError] = useActionCommentSettings()
  const dialogRef = useFocusTrap<HTMLDivElement>(true)
  useRestoreFocusOnClose(true)
  const closeRef = useFocusOnOpen<HTMLButtonElement>(!busy && !review)
  const confirmRef = useFocusOnOpen<HTMLButtonElement>(Boolean(review))
  useEscapeKey(!busy, () => { if (review) setReview(null); else if (mode) back(); else onClose() })
  const pageKey = JSON.stringify([accessToken, service.id, offset, limit, archived, revision])
  const loading = pageSource?.key !== pageKey || pageSource?.client !== client
  const garageKey = JSON.stringify([accessToken, search.trim(), garageOffset, garageLimit, garageRevision])
  const garageLoading = garageSource?.key !== garageKey || garageSource?.client !== dictionaryClient

  useEffect(() => {
    const controller = new AbortController()
    client.getPage(accessToken, service.id, { offset, limit, includeArchived: archived }, controller.signal)
      .then((result) => { if (!controller.signal.aborted) {
        if (offset > 0 && offset >= result.totalCount) { setOffset(Math.max(0, Math.floor((result.totalCount - 1) / limit) * limit)); return }
        setPage(result); setLoadError(null); setPageSource({ key: pageKey, client })
      } })
      .catch((caught: unknown) => { if (!controller.signal.aborted) { setLoadError(message(caught)); setPageSource({ key: pageKey, client }) } })
    return () => controller.abort()
  }, [client, accessToken, service.id, offset, limit, archived, revision, pageKey])

  useEffect(() => {
    if (mode?.kind !== 'create' || review) return
    const controller = new AbortController()
    const timer = setTimeout(() => {
      const load = dictionaryClient.getGaragesPage?.(accessToken, search.trim() || undefined, garageOffset, garageLimit, false, 'number', 'asc', false, {}, controller.signal)
      if (!load) { setGarageError('Выбор гаражей недоступен. Обновите приложение.'); setGarageSource({ key: garageKey, client: dictionaryClient }); return }
      load.then((result) => { if (!controller.signal.aborted) { setGaragePage(result); setGarageError(null); setGarageSource({ key: garageKey, client: dictionaryClient }) } })
        .catch((caught: unknown) => { if (!controller.signal.aborted) { setGarageError(message(caught)); setGarageSource({ key: garageKey, client: dictionaryClient }) } })
    }, 250)
    return () => { clearTimeout(timer); controller.abort() }
  }, [dictionaryClient, accessToken, mode?.kind, review, search, garageOffset, garageLimit, garageRevision, garageKey])
  useEffect(() => () => { mutation.current?.abort() }, [accessToken, service.id])

  function back() { setMode(null); setReview(null); setError(null); setRevision((value) => value + 1) }
  function open(next: Mode) {
    if (!canWrite) return
    setMode(next); setReview(null); setError(null); setNotice(null)
    setSelected([]); setSearch(''); setGarageOffset(0)
    setGarageSource(null)
    setDraft(createGarageTariffDraft(getLocalDateInputValue(), rate, initialTiers, next.kind === 'create' ? undefined : next.row))
  }
  function preview() {
    if (!mode || !canWrite || saving.current || reasonLoading || reasonError) return
    setError(null)
    if (mode.kind === 'archive') {
      const issue = validateGarageTariffReason(draft.reason, reasonRequired)
      if (issue) setError(issue); else setReview({ reason: draft.reason.trim() || null })
      return
    }
    if (mode.kind === 'create' && (selected.length === 0 || selected.length > 100)) { setError('Выберите от 1 до 100 гаражей.'); return }
    const result = validateGarageTariffDraft(draft, calculationBase.startsWith('meter_'), reasonRequired)
    if (result.terms) setReview(result.terms); else setError(result.error)
  }
  async function save() {
    if (!mode || !review || !canWrite || saving.current) return
    const controller = new AbortController()
    mutation.current = controller; saving.current = true; setBusy(true); setError(null)
    try {
      if (mode.kind === 'archive') await client.archive(accessToken, service.id, mode.row.id, { version: mode.row.version, reason: review.reason }, controller.signal)
      else if ('rate' in review) {
        if (mode.kind === 'create') await client.create(accessToken, service.id, { ...review, garageIds: selected.map((garage) => garage.id), serviceVersion: service.version }, controller.signal)
        else await client.update(accessToken, service.id, mode.row.id, { ...review, version: mode.row.version, serviceVersion: service.version }, controller.signal)
      }
      if (!controller.signal.aborted) { back(); setNotice('Назначения сохранены. Неоплаченные начисления действующих гаражей пересчитаны; оплаченные суммы и история удалённых гаражей сохранены.') }
    } catch (caught) {
      if (!controller.signal.aborted) setError(caught instanceof DictionaryApiError && caught.status === 409
        ? `${caught.message} Вернитесь к списку и откройте запись заново; повторное сохранение автоматически не выполняется.` : message(caught))
    } finally {
      saving.current = false
      if (!controller.signal.aborted) setBusy(false)
    }
  }
  function change(field: 'from' | 'to' | 'rate' | 'comment' | 'reason', value: string) { setDraft((current) => ({ ...current, [field]: value })); setError(null) }
  function tier(index: number, field: 'name' | 'upper' | 'rate', value: string) { setDraft((current) => ({ ...current, tiers: current.tiers.map((item, itemIndex) => itemIndex === index ? { ...item, [field]: value } : item) })); setError(null) }
  const title = `Индивидуальные тарифы — ${service.name}`
  return <div className="modal-backdrop"><div className="detail-dialog garage-tariff-dialog" ref={dialogRef} role="dialog" aria-modal="true" aria-label={title}>
    <div className="detail-dialog-header"><h3>{title}</h3><button className="icon-button" type="button" ref={closeRef} disabled={busy} aria-label="Закрыть индивидуальные тарифы" onClick={onClose}><X size={18} aria-hidden="true" /></button></div>
    <div className="garage-tariff-body">
      {notice ? <p role="status">{notice}</p> : null}
      {error ? <FormError>{error}</FormError> : null}
      {!mode ? <>
        <div className="garage-tariff-toolbar"><label><input type="checkbox" checked={archived} onChange={(event) => { setArchived(event.target.checked); setOffset(0) }} /> Показывать отменённые</label>
          {canWrite ? <button className="primary-button create-action-button" type="button" disabled={loading || Boolean(loadError)} onClick={() => open({ kind: 'create' })}><UsersRound size={16} aria-hidden="true" />Назначить тариф</button> : <span>Только просмотр</span>}</div>
        {loading ? <TableLoadingState label="Получаем индивидуальные тарифы" columns={5} /> : loadError ? <AsyncErrorState message={loadError} onRetry={() => setRevision((value) => value + 1)} /> : page ? <>
          <div className="garage-tariff-table-scroll"><table className="garage-tariff-table" aria-label="Назначения индивидуальных тарифов"><thead><tr><th>Гараж</th><th>Период</th><th>Ставка</th><th>Комментарий</th><th>Действия</th></tr></thead><tbody>
            {page.items.map((row) => <tr key={row.id}><th scope="row">{row.garageNumber}</th><td>{formatDateOnly(row.effectiveFrom)} — {row.effectiveTo ? formatDateOnly(row.effectiveTo) : 'без окончания'}{row.isArchived ? <span className="form-hint"> · Отменён</span> : null}</td><td>{row.tiers.length ? row.tiers.map((item) => `${item.name}: ${rateLabel(item.rate)}`).join('; ') : rateLabel(row.rate)}</td><td>{row.comment ?? '—'}</td><td>{canWrite && !row.isArchived ? <div className="garage-tariff-row-actions"><button className="icon-button" type="button" aria-label={`Изменить тариф гаража ${row.garageNumber}`} onClick={() => open({ kind: 'edit', row })}><Pencil size={16} aria-hidden="true" /></button><button className="icon-button danger-icon-button" type="button" aria-label={`Отменить тариф гаража ${row.garageNumber}`} onClick={() => open({ kind: 'archive', row })}><Trash2 size={16} aria-hidden="true" /></button></div> : '—'}</td></tr>)}
            {page.items.length === 0 ? <tr><td colSpan={5}><EmptyState>Индивидуальные тарифы пока не назначены</EmptyState></td></tr> : null}
          </tbody></table></div>
          <TablePagination ariaLabel="Страницы индивидуальных тарифов" totalCount={page.totalCount} offset={offset} limit={limit} visibleCount={page.items.length} onPageChange={(number) => setOffset((number - 1) * limit)} onPageSizeChange={(size) => { setLimit(size); setOffset(0) }} />
        </> : null}
      </> : <>
        <h4>{mode.kind === 'create' ? 'Новое назначение' : `${mode.kind === 'edit' ? 'Изменить' : 'Отменить'} тариф гаража ${mode.row.garageNumber}`}</h4>
        {!canWrite ? <p role="alert">Право изменения тарифов недоступно. Сохранение запрещено; можно вернуться к списку.</p> : null}
        {review ? <section aria-label="Подтверждение индивидуального тарифа"><p>{mode.kind === 'create' ? `Гаражи: ${selected.map((garage) => garage.number).join(', ')}` : `Гараж: ${mode.row.garageNumber}`}</p>
          {'rate' in review ? <><p>Период: {formatDateOnly(review.effectiveFrom)} — {review.effectiveTo ? formatDateOnly(review.effectiveTo) : 'без окончания'}</p><p>Ставка: {rateLabel(review.rate)}</p>{review.tiers?.map((item, index) => <p key={index}>{item.name}: {rateLabel(item.rate)}{item.upperBound !== undefined ? `, до ${item.upperBound}` : ', без границы'}</p>)}</> : <p>Индивидуальное назначение будет отменено.</p>}
          <p>Причина: {review.reason ?? 'Не указана'}</p><p>Неоплаченные начисления будут пересчитаны. Оплаченные суммы и история сохранятся.</p>
          <div className="detail-dialog-actions"><button className="ghost-button" type="button" disabled={busy} onClick={() => setReview(null)}>Назад</button><button className={mode.kind === 'archive' ? 'danger-button' : 'primary-button'} ref={confirmRef} type="button" disabled={busy || !canWrite} onClick={() => void save()}>{busy ? <LoaderCircle size={16} className="button-spinner" aria-hidden="true" /> : <Save size={16} aria-hidden="true" />}{busy ? 'Сохраняем…' : 'Подтвердить'}</button></div>
        </section> : <form noValidate onSubmit={(event) => { event.preventDefault(); preview() }}>
          <fieldset className="garage-tariff-edit-fields" disabled={!canWrite}>
          {mode.kind === 'create' ? <section aria-label="Выбор гаражей для тарифа"><FormField label="Поиск гаража"><input aria-label="Поиск гаража для тарифа" value={search} onChange={(event) => { setSearch(event.target.value); setGarageOffset(0) }} /></FormField><p role="status">Выбрано: {selected.length} из 100{selected.length ? ` · ${selected.map((garage) => garage.number).join(', ')}` : ''}</p>
            {selected.length ? <div className="garage-tariff-selection">{selected.map((garage) => <button className="secondary-button" type="button" key={garage.id} aria-label={`Убрать гараж ${garage.number} из назначения`} onClick={() => setSelected((current) => current.filter((item) => item.id !== garage.id))}>Гараж {garage.number}<X size={14} aria-hidden="true" /></button>)}</div> : null}
            {garageLoading ? <TableLoadingState label="Получаем гаражи для тарифа" columns={2} rows={2} /> : garageError ? <AsyncErrorState message={garageError} onRetry={() => setGarageRevision((value) => value + 1)} /> : garagePage ? <><div className="garage-tariff-garages">{garagePage.items.map((garage) => <label key={garage.id}><input type="checkbox" aria-label={`Выбрать гараж ${garage.number}`} checked={selected.some((item) => item.id === garage.id)} disabled={selected.length >= 100 && !selected.some((item) => item.id === garage.id)} onChange={(event) => setSelected((current) => event.target.checked ? [...current, { id: garage.id, number: garage.number }] : current.filter((item) => item.id !== garage.id))} />Гараж {garage.number}</label>)}{garagePage.items.length === 0 ? <EmptyState>Гаражи не найдены</EmptyState> : null}</div><TablePagination ariaLabel="Страницы выбора гаражей" totalCount={garagePage.totalCount} offset={garageOffset} limit={garageLimit} visibleCount={garagePage.items.length} onPageChange={(number) => setGarageOffset((number - 1) * garageLimit)} onPageSizeChange={(size) => { setGarageLimit(size); setGarageOffset(0) }} /></> : null}
          </section> : null}
          {mode.kind !== 'archive' ? <>
            <div className="garage-tariff-fields"><FormField label="Действует с"><LocalizedDatePicker ariaLabel="Индивидуальный тариф с" mode="date" required value={draft.from} onChange={(value) => change('from', value)} /></FormField><FormField label="Действует по" help="Пустое окончание означает бессрочный тариф."><LocalizedDatePicker ariaLabel="Индивидуальный тариф по" mode="date" value={draft.to} onChange={(value) => change('to', value)} /></FormField><FormField label="Ставка" help="До четырёх знаков после запятой; база расчёта совпадает с общей услугой."><DecimalTextInput aria-label="Ставка индивидуального тарифа" value={draft.rate} onChange={(event) => change('rate', event.target.value)} /></FormField></div>
            {calculationBase.startsWith('meter_') ? <label><input type="checkbox" checked={draft.tiered} onChange={(event) => setDraft((current) => ({ ...current, tiered: event.target.checked }))} />Тарифные ступени</label> : null}
            {draft.tiered ? <section aria-label="Ступени индивидуального тарифа">{draft.tiers.map((item, index) => <div className="garage-tariff-tier" key={item.id ?? index}><FormField label={`Ступень ${index + 1}`}><input aria-label={`Название ступени ${index + 1}`} value={item.name} maxLength={120} onChange={(event) => tier(index, 'name', event.target.value)} /></FormField><FormField label="Верхняя граница" help={index === draft.tiers.length - 1 ? 'Последняя ступень не имеет верхней границы.' : 'Границы должны строго возрастать; до трёх знаков после запятой.'}><DecimalTextInput aria-label={`Граница ступени ${index + 1}`} disabled={index === draft.tiers.length - 1} placeholder={index === draft.tiers.length - 1 ? 'Без границы' : ''} value={item.upper} onChange={(event) => tier(index, 'upper', event.target.value)} /></FormField><FormField label="Ставка"><DecimalTextInput aria-label={`Ставка ступени ${index + 1}`} value={item.rate} onChange={(event) => tier(index, 'rate', event.target.value)} /></FormField><button className="icon-button danger-icon-button" type="button" disabled={draft.tiers.length <= 2} aria-label={`Удалить ступень ${index + 1}`} onClick={() => setDraft(removeGarageTariffTier(draft, index))}><Trash2 size={16} aria-hidden="true" /></button></div>)}<button className="secondary-button create-action-button" type="button" disabled={draft.tiers.length >= 20} onClick={() => setDraft(appendGarageTariffTier(draft))}><ListPlus size={16} aria-hidden="true" />Добавить ступень</button></section> : null}
            <FormField label="Комментарий"><textarea aria-label="Комментарий индивидуального тарифа" maxLength={2000} value={draft.comment} onChange={(event) => change('comment', event.target.value)} /></FormField>
          </> : null}
          <FormField label="Причина изменения" help="Основание сохраняется в истории изменений."><textarea aria-label="Причина изменения индивидуального тарифа" maxLength={1000} required={reasonRequired} value={draft.reason} onChange={(event) => change('reason', event.target.value)} /></FormField>
          {reasonError ? <FormError>{reasonError}</FormError> : null}
          </fieldset>
          <div className="detail-dialog-actions"><button className="ghost-button" type="button" onClick={back}>К списку</button><button className="primary-button" type="submit" disabled={!canWrite || reasonLoading || Boolean(reasonError)}><Save size={16} aria-hidden="true" />{mode.kind === 'archive' ? 'Отменить назначение' : 'Сохранить назначение'}</button></div>
        </form>}
      </>}
    </div>
  </div></div>
}
