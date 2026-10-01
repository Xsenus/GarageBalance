import { useEffect, useRef, useState } from 'react'
import { ArrowDown, ArrowUp, Columns3, Save, Trash2 } from 'lucide-react'
import { serviceReportsApi } from '../../services/serviceReportsApi'
import type { ServiceReportColumns, ServiceReportScope, ServiceReportsClient } from '../../services/serviceReportsApi'
import { AsyncErrorState, EmptyState, LoadingSkeleton } from '../../shared/AsyncState'
import { FormField } from '../../shared/FormField'
import '../reports/serviceReports.css'

export function ReportColumnsPanel({ token, canManage, client = serviceReportsApi }: { token: string; canManage: boolean; client?: ServiceReportsClient }) {
  const [draft, setDraft] = useState<ServiceReportColumns | null>(null)
  const [report, setReport] = useState<ServiceReportScope>('payments')
  const drafts = useRef<Partial<Record<ServiceReportScope, ServiceReportColumns>>>({})
  const [selectedId, setSelectedId] = useState('')
  const [search, setSearch] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [retry, setRetry] = useState(0)
  const saveController = useRef<AbortController | null>(null)
  useEffect(() => { drafts.current = {} }, [token, client])
  useEffect(() => () => saveController.current?.abort(), [])
  useEffect(() => {
    if (!canManage) return
    const controller = new AbortController()
    const cached = drafts.current[report]
    void (cached ? Promise.resolve(cached) : client.getColumns(token, controller.signal, report)).then((result) => { if (!controller.signal.aborted) { setDraft(result); setSelectedId(result.columns[0]?.id ?? ''); setError(null); setMessage(null) } })
      .catch((caught: unknown) => { if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : 'Не удалось загрузить колонки.') })
      .finally(() => { if (!controller.signal.aborted) { setLoading(false); setSaving(false) } })
    return () => { controller.abort(); saveController.current?.abort() }
  }, [token, canManage, client, retry, report])
  if (!canManage) return <EmptyState>Для настройки колонок необходимо право управления тарифами.</EmptyState>
  const selected = draft?.columns.find((column) => column.id === selectedId)
  function changeReport(next: ServiceReportScope) {
    if (next === report) return
    if (draft) drafts.current[report] = draft
    setReport(next); setDraft(null); setSelectedId(''); setSearch(''); setError(null); setMessage(null); setLoading(true)
  }
  function reload() {
    delete drafts.current[report]
    setDraft(null); setError(null); setLoading(true); setRetry((value) => value + 1)
  }
  function move(index: number, direction: number) {
    if (!draft) return
    const columns = [...draft.columns]; [columns[index], columns[index + direction]] = [columns[index + direction], columns[index]]
    setDraft({ ...draft, columns }); setMessage(null)
  }
  async function save() {
    if (!draft) return
    const controller = new AbortController(); saveController.current = controller
    setSaving(true); setError(null); setMessage(null)
    try { const result = await client.saveColumns(token, { version: draft.version, columns: draft.columns, report }, controller.signal); if (!controller.signal.aborted) { setDraft(result); setMessage('Колонки сохранены только для выбранного отчёта. Финансовые записи не меняются.') } }
    catch (caught) { if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : 'Не удалось сохранить колонки.') }
    finally { if (!controller.signal.aborted) setSaving(false) }
  }
  return <section className="settings-card" aria-label="Колонки отчётов"><h2>Колонки отчётов</h2>
    <FormField label="Отчёт" help="У каждого отчёта свой состав и порядок колонок. Правки другого отчёта сохраняются на экране при переключении; для записи каждого набора нажмите «Сохранить колонки»."><select aria-label="Отчёт" value={report} disabled={saving} onChange={(event) => changeReport(event.target.value as ServiceReportScope)}><option value="payments">Оплата по услугам</option><option value="accrued">Задолженность — начисленная</option><option value="overdue">Задолженность — просроченная</option></select></FormField>
    {loading ? <LoadingSkeleton label="Загрузка колонок отчётов" columns={2} /> : draft ? <>
      <div className="report-column-editor"><div className="report-column-list">
      {draft.columns.map((column, index) => <div key={column.id} className="report-column-item"><button className="secondary-button" type="button" aria-label={`${column.name || 'Без названия'}, услуг: ${column.serviceIds.length}`} aria-pressed={selectedId === column.id} disabled={saving} onClick={() => setSelectedId(column.id)}><span className="report-column-name">{column.name || 'Без названия'}</span><span className="report-column-count" aria-hidden="true">{column.serviceIds.length}</span></button>
        <button type="button" className="ghost-button" aria-label={`Поднять колонку ${column.name}`} disabled={saving || index === 0} onClick={() => move(index, -1)}><ArrowUp size={16} aria-hidden="true" /></button>
        <button type="button" className="ghost-button" aria-label={`Опустить колонку ${column.name}`} disabled={saving || index === draft.columns.length - 1} onClick={() => move(index, 1)}><ArrowDown size={16} aria-hidden="true" /></button>
        <button type="button" className="ghost-button" aria-label={`Удалить колонку ${column.name}`} disabled={saving || draft.columns.length === 1} onClick={() => { const columns = draft.columns.filter((item) => item.id !== column.id); setDraft({ ...draft, columns }); if (selectedId === column.id) setSelectedId(columns[0].id); setMessage(null) }}><Trash2 size={16} aria-hidden="true" /></button>
      </div>)}
      <button className="secondary-button create-action-button" type="button" disabled={saving || draft.columns.length >= 20} onClick={() => { const id = crypto.randomUUID(); setDraft({ ...draft, columns: [...draft.columns, { id, name: 'Новая колонка', serviceIds: [] }] }); setSelectedId(id); setMessage(null) }}><Columns3 size={17} aria-hidden="true" />Добавить колонку</button>
      <div className="report-column-item">Прочее · автоматически</div>
      </div><div className="dictionary-form settings-card-form report-column-properties">{selected ? <>
        <FormField label="Название колонки" help="Колонка относится только к выбранному отчёту. Название «Прочее» зарезервировано для неназначенных услуг, сборов и нерегулярных операций."><input aria-label="Название колонки" value={selected.name} disabled={saving} maxLength={80} onChange={(event) => { setDraft({ ...draft, columns: draft.columns.map((column) => column.id === selectedId ? { ...column, name: event.target.value } : column) }); setMessage(null) }} /></FormField>
        <FormField label="Поиск услуги"><input value={search} disabled={saving} onChange={(event) => setSearch(event.target.value)} /></FormField>
        <div className="report-column-services" role="group" aria-label="Услуги колонки">{draft.services.filter((service) => service.name.toLocaleLowerCase('ru').includes(search.toLocaleLowerCase('ru'))).map((service) => {
          const assigned = draft.columns.find((column) => column.id !== selectedId && column.serviceIds.includes(service.id))
          return <label key={service.id}><input type="checkbox" checked={selected.serviceIds.includes(service.id)} disabled={saving || Boolean(assigned) || (!service.incomeTypeId)} aria-label={`Услуга ${service.name}`} onChange={(event) => { setDraft({ ...draft, columns: draft.columns.map((column) => column.id === selectedId ? { ...column, serviceIds: event.target.checked ? [...column.serviceIds, service.id] : column.serviceIds.filter((id) => id !== service.id) } : column) }); setMessage(null) }} /><span className="report-column-service-name">{service.name}{service.isArchived ? ' (архив)' : ''}</span>{assigned ? <span className="report-column-assignment"><span className="report-column-assignment-label">Колонка</span><strong>{assigned.name || 'Без названия'}</strong></span> : null}</label>
        })}</div>
        {!draft.services.some((service) => service.name.toLocaleLowerCase('ru').includes(search.toLocaleLowerCase('ru'))) ? <EmptyState>Услуги не найдены</EmptyState> : null}
      </> : null}</div></div>
      <button className="primary-button" type="button" disabled={saving || draft.columns.some((column) => !column.name.trim())} aria-busy={saving} onClick={() => void save()}><Save size={17} aria-hidden="true" />{saving ? 'Сохраняем' : 'Сохранить колонки'}</button>
    </> : null}
    {error ? draft ? <><p role="alert">{error}</p><button className="ghost-button" type="button" disabled={saving} onClick={reload}>Отменить правки и перечитать настройки</button></> : <AsyncErrorState message={error} onRetry={reload} /> : null}
    {message ? <p role="status" aria-live="polite">{message}</p> : null}
  </section>
}
