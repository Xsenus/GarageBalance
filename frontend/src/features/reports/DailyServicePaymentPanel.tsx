import { Fragment, useEffect, useRef, useState } from 'react'
import type { CSSProperties } from 'react'
import { FileSpreadsheet, FileText, LoaderCircle } from 'lucide-react'
import type { DictionaryClient, GarageDto } from '../../services/dictionariesApi'
import type { DailyServicePaymentAmounts, DailyServicePaymentQuery, DailyServicePaymentReportDto, ReportClient } from '../../services/reportsApi'
import { AsyncErrorState, EmptyState, LoadingSkeleton } from '../../shared/AsyncState'
import { scheduleDebouncedRequest } from '../../shared/debouncedRequest'
import { downloadBlob } from '../../shared/fileExports'
import { FormField } from '../../shared/FormField'
import { formatDateOnly, formatMoney, getLocalDateInputValue } from '../../shared/formatters'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { SelectControl } from '../../shared/SelectControl'
import { TablePagination } from '../../shared/TablePagination'

const serviceColumns: Array<[keyof DailyServicePaymentAmounts, string]> = [
  ['electricity', 'Электроэнергия'], ['water', 'Вода'], ['trash', 'Мусор'], ['outdoorLighting', 'Наружное освещение'],
  ['membership', 'Членский взнос'], ['target', 'Целевой взнос'], ['other', 'Прочее'], ['total', 'ИТОГО'],
]

export function DailyServicePaymentPanel({ accessToken, canRead, dictionaryClient, reportClient, initialFilters, onFiltersChange, initialSelectedGarage, onSelectedGarageChange }: {
  accessToken: string; canRead: boolean; dictionaryClient: Pick<DictionaryClient, 'getGarages'>
  reportClient: Pick<ReportClient, 'getDailyServicePayments' | 'exportDailyServicePayments'>
  initialFilters?: DailyServicePaymentQuery; onFiltersChange?: (filters: DailyServicePaymentQuery) => void
  initialSelectedGarage?: GarageDto | null; onSelectedGarageChange?: (garage: GarageDto | null) => void
}) {
  const [filters, setFilters] = useState<DailyServicePaymentQuery>(() => initialFilters ?? { throughDate: getLocalDateInputValue(), offset: 0, limit: 25 })
  const [report, setReport] = useState<DailyServicePaymentReportDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [retry, setRetry] = useState(0)
  const [search, setSearch] = useState('')
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [selectedGarage, setSelectedGarage] = useState<GarageDto | null>(() => initialSelectedGarage && initialSelectedGarage.id === initialFilters?.garageId ? initialSelectedGarage : null)
  const [searchLoading, setSearchLoading] = useState(true)
  const [searchError, setSearchError] = useState<string | null>(null)
  const [searchRetry, setSearchRetry] = useState(0)
  const [exporting, setExporting] = useState<'xlsx' | 'pdf' | null>(null)
  const [exportMessage, setExportMessage] = useState<string | null>(null)
  const [exportError, setExportError] = useState<string | null>(null)
  const exportController = useRef<AbortController | null>(null)
  useEffect(() => () => exportController.current?.abort(), [])

  useEffect(() => {
    if (!canRead || !filters.throughDate) return
    const controller = new AbortController()
    void reportClient.getDailyServicePayments(accessToken, filters, controller.signal).then((result) => {
      if (!controller.signal.aborted) setReport(result)
    }).catch((caught: unknown) => {
      if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : 'Не удалось получить ежедневный отчёт.')
    }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [accessToken, canRead, filters, reportClient, retry])

  useEffect(() => {
    if (!canRead) return
    return scheduleDebouncedRequest({
      request: (signal) => dictionaryClient.getGarages(accessToken, search, 20, true, signal),
      onStart: () => {},
      onSuccess: (results) => { setGarages(results); setSearchLoading(false) },
      onError: (caught) => { setSearchError(caught instanceof Error ? caught.message : 'Поиск гаражей недоступен.'); setSearchLoading(false) },
    })
  }, [accessToken, canRead, dictionaryClient, search, searchRetry])

  function changeFilters(change: DailyServicePaymentQuery) {
    const next = { ...filters, ...change }
    setFilters(next)
    onFiltersChange?.(next)
    setLoading(Boolean(next.throughDate))
    setError(next.throughDate ? null : 'Укажите дату отчёта.')
    setReport(null)
    setExportMessage(null)
    setExportError(null)
  }

  async function exportReport(format: 'xlsx' | 'pdf') {
    const controller = new AbortController()
    exportController.current = controller
    setExporting(format)
    setExportError(null)
    setExportMessage(null)
    try {
      const blob = await reportClient.exportDailyServicePayments(accessToken, { throughDate: filters.throughDate, garageId: filters.garageId }, format, controller.signal)
      if (controller.signal.aborted) return
      downloadBlob(blob, `garagebalance-daily-services-${filters.throughDate!.replaceAll('-', '')}.${format}`)
      setExportMessage(`Отчёт ${format.toUpperCase()} готов.`)
    } catch (caught: unknown) {
      if (!controller.signal.aborted) setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчёт.')
    } finally {
      if (!controller.signal.aborted) setExporting(null)
    }
  }

  if (!canRead) return <EmptyState>Для ежедневного отчёта необходимо право чтения отчётов.</EmptyState>
  const data = report?.data
  const columns = serviceColumns.filter(([key]) => key !== 'other' || data?.hasOther)
  const options = selectedGarage && !garages.some((garage) => garage.id === selectedGarage.id) ? [selectedGarage, ...garages] : garages
  const dates = [...new Set(data?.rows.map((row) => row.date))]
  const rowStyle = {
    '--report-columns': columns.length + 2,
    gridTemplateColumns: `90px minmax(130px, 1.2fr) minmax(130px, 1.2fr) repeat(${columns.length - 1}, minmax(100px, 1fr))`,
    minWidth: `${250 + columns.length * 100}px`,
  } as CSSProperties
  const amountCells = (amounts: DailyServicePaymentAmounts) => columns.map(([key]) => <span role="cell" key={key}>{formatMoney(amounts[key])}</span>)
  return <section aria-label="Ежедневная оплата услуг">
    <div className="report-workbook-filter">
      <div className="report-workbook-filter__fields">
      <FormField className="report-period-field report-period-field--date" label="По дату включительно" help="Оплаты за фактические даты с первого числа выбранного месяца. Услуги суммируются по гаражу, затем по дню и месяцу. Итоги учитывают все страницы отчёта.">
        <LocalizedDatePicker ariaLabel="Дата ежедневного отчёта" mode="date" required disabled={Boolean(exporting)} value={filters.throughDate ?? ''} onChange={(throughDate) => changeFilters({ throughDate, offset: 0 })} />
      </FormField>
      <FormField label="Поиск гаража"><input aria-label="Поиск гаража ежедневного отчёта" value={search} disabled={Boolean(exporting)} onChange={(event) => { setSearch(event.target.value); setSearchLoading(true); setSearchError(null) }} /></FormField>
      <FormField label="Гараж"><SelectControl aria-label="Гараж ежедневного отчёта" value={filters.garageId ?? ''} disabled={searchLoading || Boolean(exporting)} options={[{ value: '', label: 'Все гаражи' }, ...options.map((garage) => ({ value: garage.id, label: `Гараж ${garage.number}${garage.ownerName ? ` · ${garage.ownerName}` : ''}` }))]} onChange={(garageId) => { const garage = options.find((item) => item.id === garageId) ?? null; setSelectedGarage(garage); onSelectedGarageChange?.(garage); changeFilters({ garageId: garageId || undefined, offset: 0 }) }} /></FormField>
      </div>
      <div className="report-workbook-filter__actions" role="group" aria-label="Выгрузка ежедневного отчёта">{(['xlsx', 'pdf'] as const).map((format) => <button type="button" className={`secondary-button report-export-button report-export-button--${format}`} key={format} disabled={loading || !report || Boolean(exporting)} aria-busy={exporting === format} aria-label={`Скачать ежедневный отчёт ${format.toUpperCase()}`} title={`Скачать ежедневный отчёт ${format.toUpperCase()}`} onClick={() => { void exportReport(format) }}>{exporting === format ? <LoaderCircle className="report-export-button__spinner" size={19} aria-hidden="true" /> : format === 'xlsx' ? <FileSpreadsheet size={19} aria-hidden="true" /> : <FileText size={19} aria-hidden="true" />}</button>)}</div>
    </div>
    {searchLoading ? <LoadingSkeleton label="Поиск гаражей ежедневного отчёта" rows={1} /> : searchError ? <AsyncErrorState message={searchError} onRetry={() => { setSearchLoading(true); setSearchError(null); setSearchRetry((value) => value + 1) }} /> : null}
    {exportMessage ? <p role="status" aria-live="polite">{exportMessage}</p> : null}
    {exportError ? <p role="alert">{exportError}</p> : null}
    {loading ? <LoadingSkeleton label="Получаем ежедневный отчёт" columns={columns.length} /> : error ? filters.throughDate ? <AsyncErrorState message={error} onRetry={() => { setLoading(true); setError(null); setRetry((value) => value + 1) }} /> : <p role="alert">{error}</p> : data ? <>
      <p>Итого с начала месяца по {formatDateOnly(report.throughDate)}: <strong>{formatMoney(data.monthTotal.total)}</strong></p>
      <div className="report-workbook-table" role="table" aria-label="Оплаты гаражей по услугам">
        <div className="report-workbook-row report-workbook-row--header" role="row" style={rowStyle}><span role="columnheader">Дата</span><span role="columnheader">Гараж</span>{columns.map(([key, label]) => <span role="columnheader" key={key}>{label}</span>)}</div>
        {dates.map((date) => <Fragment key={date}>{data.rows.filter((row) => row.date === date).map((row) => <div className="report-workbook-row" role="row" style={rowStyle} key={row.garageId}><span role="cell">{formatDateOnly(date)}</span><span role="rowheader">{row.garageNumber}</span>{amountCells(row.amounts)}</div>)}{data.days.filter((day) => day.date === date).map((day) => <div className="report-workbook-row report-workbook-row--footer" role="row" style={rowStyle} key={`total-${date}`}><span role="rowheader">{formatDateOnly(date)}</span><span role="cell">Итого за день по всем строкам</span>{amountCells(day.amounts)}</div>)}</Fragment>)}
        {data.rows.length === 0 ? <div className="report-workbook-row" role="row" style={rowStyle}><div className="report-workbook-empty-cell" role="cell"><EmptyState className="report-workbook-empty-state">Оплат за выбранный период нет</EmptyState></div></div> : null}
        <div className="report-workbook-row report-workbook-row--footer" role="row" style={rowStyle}><span role="rowheader">Итого с начала месяца</span><span role="cell" />{amountCells(data.monthTotal)}</div>
      </div>
      <TablePagination ariaLabel="Страницы ежедневного отчёта" totalCount={data.rowCount} offset={data.offset} limit={data.limit} visibleCount={data.rows.length} disabled={Boolean(exporting)} onPageChange={(page) => changeFilters({ offset: (page - 1) * data.limit })} onPageSizeChange={(limit) => changeFilters({ offset: 0, limit })} />
    </> : null}
  </section>
}
