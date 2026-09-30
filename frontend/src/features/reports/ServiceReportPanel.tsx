import { useEffect, useMemo, useRef, useState } from 'react'
import { FileSpreadsheet, FileText, LoaderCircle } from 'lucide-react'
import type { DictionaryClient, GarageDto } from '../../services/dictionariesApi'
import { serviceReportsApi } from '../../services/serviceReportsApi'
import type { ServiceReport, ServiceReportKind, ServiceReportQuery, ServiceReportsClient } from '../../services/serviceReportsApi'
import { AsyncErrorState, EmptyState, LoadingSkeleton } from '../../shared/AsyncState'
import { scheduleDebouncedRequest } from '../../shared/debouncedRequest'
import { downloadBlob } from '../../shared/fileExports'
import { FormField } from '../../shared/FormField'
import { formatDateOnly, formatMoney, getLocalDateInputValue } from '../../shared/formatters'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { SelectControl } from '../../shared/SelectControl'
import './serviceReports.css'
import { serviceReportDisplayRows, serviceReportWindow } from './serviceReportRows'

export function ServiceReportPanel({ accessToken, canRead, dictionaryClient, kind, client = serviceReportsApi, initialQuery, onQueryChange, initialSelectedGarage, onSelectedGarageChange }: {
  accessToken: string; canRead: boolean; dictionaryClient: Pick<DictionaryClient, 'getGarages'>; kind: ServiceReportKind; client?: ServiceReportsClient
  initialQuery?: ServiceReportQuery; onQueryChange?: (query: ServiceReportQuery) => void; initialSelectedGarage?: GarageDto | null; onSelectedGarageChange?: (garage: GarageDto | null) => void
}) {
  const [query, setQuery] = useState<ServiceReportQuery>(() => {
    const today = new Date()
    return initialQuery ?? { dateTo: getLocalDateInputValue(kind === 'debt' ? today : new Date(today.getFullYear(), today.getMonth() + 1, 0)), offset: 0, limit: 50 }
  })
  const [report, setReport] = useState<ServiceReport | null>(null)
  const currentColumns = useRef<string | null>(null)
  const [scrollTop, setScrollTop] = useState(0)
  const scrollRef = useRef<HTMLDivElement | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [retry, setRetry] = useState(0)
  const [search, setSearch] = useState('')
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [selectedGarage, setSelectedGarage] = useState<GarageDto | null>(initialSelectedGarage ?? null)
  const [searchLoading, setSearchLoading] = useState(true)
  const [searchError, setSearchError] = useState<string | null>(null)
  const [searchRetry, setSearchRetry] = useState(0)
  const [exporting, setExporting] = useState(false)
  const [exportMessage, setExportMessage] = useState<string | null>(null)
  const [exportError, setExportError] = useState<string | null>(null)
  const exportController = useRef<AbortController | null>(null)
  useEffect(() => () => exportController.current?.abort(), [])
  const valid = Boolean(query.dateTo) && (!query.dateFrom || query.dateFrom <= query.dateTo!)
  useEffect(() => {
    if (!canRead || !valid) return
    const controller = new AbortController()
    void client.getReport(accessToken, kind, query, controller.signal).then((result) => {
      if (controller.signal.aborted) return
      const signature = JSON.stringify(result.columns)
      if (query.offset && currentColumns.current !== signature) { setError('Колонки отчёта изменились. Переформируйте отчёт, чтобы суммы не попали в другие колонки.'); return }
      currentColumns.current = signature
      setReport((previous) => !query.offset || !previous ? result : { ...result,
        rows: [...previous.rows, ...result.rows.filter((row) => !previous.rows.some((old) => old.date === row.date && old.garageId === row.garageId))],
        days: [...previous.days.filter((day) => !result.days.some((next) => next.date === day.date)), ...result.days],
      })
    }).catch((caught: unknown) => { if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : 'Не удалось загрузить отчёт.') })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [accessToken, canRead, client, kind, query, retry, valid])
  useEffect(() => {
    if (!canRead) return
    return scheduleDebouncedRequest({ request: (signal) => dictionaryClient.getGarages(accessToken, search, 20, true, signal), onStart: () => {},
      onSuccess: (items) => { setGarages(items); setSearchLoading(false) }, onError: () => { setSearchError('Не удалось найти гаражи.'); setSearchLoading(false) } })
  }, [accessToken, canRead, dictionaryClient, search, searchRetry])
  function change(change: ServiceReportQuery) {
    exportController.current?.abort(); setExporting(false)
    const next = { ...query, ...change, offset: 0 }
    setQuery(next); onQueryChange?.(next); setReport(null); currentColumns.current = null; setScrollTop(0); setLoading(true); setError(null); setExportMessage(null); setExportError(null)
    if (scrollRef.current) scrollRef.current.scrollTop = 0
  }
  function append() { if (loading || error || exporting || !report || report.rows.length >= report.rowCount) return; setLoading(true); setQuery((previous) => ({ ...previous, offset: (previous.offset ?? 0) + (previous.limit ?? 50) })) }
  async function exportReport(format: 'xlsx' | 'pdf') {
    const controller = new AbortController(); exportController.current = controller; setExporting(true); setExportMessage(null); setExportError(null)
    try { const blob = await client.exportReport(accessToken, kind, query, format, controller.signal); if (!controller.signal.aborted) { downloadBlob(blob, `garagebalance-${kind}-${query.dateTo}.${format}`); setExportMessage('Отчёт выгружен полностью по выбранным фильтрам.') } }
    catch (caught) { if (!controller.signal.aborted) setExportError(caught instanceof Error ? caught.message : 'Выгрузка не удалась.') }
    finally { if (!controller.signal.aborted) setExporting(false) }
  }
  const displayRows = useMemo(() => report ? serviceReportDisplayRows(report, kind) : [], [report, kind])
  const window = serviceReportWindow(displayRows, scrollTop)
  if (!canRead) return <EmptyState>Необходимо право чтения отчётов.</EmptyState>
  const options = selectedGarage && !garages.some((garage) => garage.id === selectedGarage.id) ? [selectedGarage, ...garages] : garages
  const title = kind === 'debt' ? 'Задолженность по услугам' : 'Оплата по услугам'
  const cells = (amounts: number[]) => amounts.map((amount, index) => <td key={index}>{formatMoney(amount)}</td>)
  return <section aria-label={title} className="service-report-panel">
    <div className="report-workbook-filter"><div className="report-workbook-filter__fields">
      {kind === 'payments' ? <FormField label="Дата с" help="Оставьте пустой, чтобы включить всю историю оплат."><LocalizedDatePicker mode="date" ariaLabel="Начало периода оплаты" value={query.dateFrom ?? ''} disabled={exporting} onChange={(dateFrom) => change({ dateFrom: dateFrom || undefined })} /></FormField> : null}
      <FormField label={kind === 'debt' ? 'На дату' : 'Дата по'} help="Выбранная дата включается в отчёт. Итоги и выгрузки учитывают весь фильтр."><LocalizedDatePicker required mode="date" ariaLabel="Конец периода отчёта" value={query.dateTo ?? ''} disabled={exporting} onChange={(dateTo) => change({ dateTo })} /></FormField>
      <FormField label="Поиск гаража"><input aria-label="Поиск гаража отчёта по услугам" value={search} disabled={exporting} onChange={(event) => { setSearch(event.target.value); setSearchLoading(true); setSearchError(null) }} /></FormField>
      <FormField label="Гараж"><SelectControl aria-label="Гараж отчёта по услугам" value={query.garageId ?? ''} disabled={searchLoading || exporting} options={[{ value: '', label: 'Все гаражи' }, ...options.map((garage) => ({ value: garage.id, label: `Гараж ${garage.number}` }))]} onChange={(id) => { const garage = options.find((garage) => garage.id === id) ?? null; setSelectedGarage(garage); onSelectedGarageChange?.(garage); change({ garageId: id || undefined }) }} /></FormField>
    </div><div className="report-workbook-filter__actions">{(['xlsx', 'pdf'] as const).map((format) => <button key={format} type="button" className={`secondary-button report-export-button report-export-button--${format}`} aria-label={`Скачать отчёт ${format.toUpperCase()}`} disabled={loading || exporting || !report || !valid} onClick={() => void exportReport(format)}>{exporting ? <LoaderCircle size={19} aria-hidden="true" /> : format === 'xlsx' ? <FileSpreadsheet size={19} aria-hidden="true" /> : <FileText size={19} aria-hidden="true" />}</button>)}</div></div>
    {kind === 'debt' ? <div className="service-report-modes" role="group" aria-label="Вид задолженности">{[false, true].map((overdueOnly) => <button key={String(overdueOnly)} type="button" className="secondary-button" aria-pressed={Boolean(query.overdueOnly) === overdueOnly} disabled={exporting} onClick={() => change({ overdueOnly })}>{overdueOnly ? 'Просроченная' : 'Начисленная'}</button>)}</div> : null}
    {searchError ? <AsyncErrorState message={searchError} onRetry={() => { setSearchError(null); setSearchLoading(true); setSearchRetry((value) => value + 1) }} /> : null}
    {exportMessage ? <p role="status" aria-live="polite">{exportMessage}</p> : null}
    {exportError ? <p role="alert">{exportError}</p> : null}
    {!valid ? <p role="alert">{!query.dateTo ? 'Укажите дату окончания.' : 'Дата начала не может быть позже даты окончания.'}</p> : <>
    {loading && !report ? <LoadingSkeleton label="Получаем отчёт по услугам" columns={8} /> : null}
    {report ? <><p className="service-report-summary">{kind === 'debt' ? `Задолженность на ${formatDateOnly(report.dateTo)}` : `${report.dateFrom ? formatDateOnly(report.dateFrom) : 'С начала учёта'} — ${formatDateOnly(report.dateTo)}`} · ИТОГО: <strong>{formatMoney(report.total)}</strong></p>
      <div ref={scrollRef} className="service-report-scroll" tabIndex={0} role="region" aria-label="Строки отчёта по услугам" onScroll={(event) => { const target = event.currentTarget; setScrollTop(target.scrollTop); if (target.scrollHeight - target.scrollTop - target.clientHeight < 100) append() }}>
      <table className="service-report-table" aria-label={kind === 'debt' ? 'Задолженность гаражей по услугам' : 'Оплаты гаражей по услугам'}><thead><tr>{kind === 'payments' ? <th scope="col">Дата</th> : null}<th scope="col">Гараж</th>{report.columns.map((column) => <th scope="col" key={column.id}>{column.name}</th>)}<th scope="col">ИТОГО</th></tr></thead><tbody>
        {window.before ? <tr aria-hidden="true"><td colSpan={report.columns.length + (kind === 'payments' ? 3 : 2)} style={{ height: window.before, padding: 0, border: 0 }} /></tr> : null}
        {window.rows.map((row) => <tr key={row.key} className={row.dayTotal ? 'service-report-total' : undefined}>{row.dayTotal ? <th colSpan={2} scope="row">Итого за {formatDateOnly(row.date!)}</th> : <>{kind === 'payments' ? <td>{formatDateOnly(row.date!)}</td> : null}<th scope="row">{row.garageNumber}</th></>}{cells(row.amounts)}<td>{formatMoney(row.total)}</td></tr>)}
        {window.after ? <tr aria-hidden="true"><td colSpan={report.columns.length + (kind === 'payments' ? 3 : 2)} style={{ height: window.after, padding: 0, border: 0 }} /></tr> : null}
        {!report.rows.length ? <tr><td colSpan={report.columns.length + (kind === 'payments' ? 3 : 2)}><EmptyState>{kind === 'debt' ? 'Задолженности нет' : 'Оплат за выбранный период нет'}</EmptyState></td></tr> : null}
      </tbody><tfoot><tr><th colSpan={kind === 'payments' ? 2 : 1} scope="row">ИТОГО по всему фильтру</th>{cells(report.totals)}<td>{formatMoney(report.total)}</td></tr></tfoot></table>
      {loading ? <LoadingSkeleton label="Подгружаем строки отчёта" rows={1} /> : null}
      </div><div className="service-report-progress"><span role="status">Загружено: {report.rows.length} из {report.rowCount}</span>{report.rows.length < report.rowCount ? <button type="button" className="ghost-button" disabled={loading || exporting || Boolean(error)} onClick={append}>Показать ещё строки</button> : null}</div>
    </> : null}
    {error ? <><AsyncErrorState message={error} onRetry={() => { setError(null); setLoading(true); setRetry((value) => value + 1) }} />{report ? <button type="button" className="ghost-button" onClick={() => change({})}>Переформировать отчёт</button> : null}</> : null}
    </>}
  </section>
}
