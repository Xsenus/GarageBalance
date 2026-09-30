import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import type { DictionaryClient } from '../../services/dictionariesApi'
import type { ServiceReport, ServiceReportsClient } from '../../services/serviceReportsApi'
import { ServiceReportPanel } from './ServiceReportPanel'
import { downloadBlob } from '../../shared/fileExports'

vi.mock('../../shared/fileExports', () => ({ downloadBlob: vi.fn() }))
beforeEach(() => vi.mocked(downloadBlob).mockClear())
const data = (offset = 0): ServiceReport => ({ dateFrom: null, dateTo: '2046-09-30', columns: [{ id: 'light', name: 'Свет', serviceIds: ['service'] }],
  rows: [{ date: '2046-09-30', garageId: offset ? 'g10' : 'g2', garageNumber: offset ? '10' : '2', amounts: [50], total: 50 }],
  days: [{ date: '2046-09-30', amounts: [100], total: 100 }], totals: [100], total: 100, rowCount: 2, offset, limit: 50 })
const dictionary = { getGarages: vi.fn(async () => [{ id: 'g2', number: '2' }]) } as unknown as DictionaryClient
function client(): ServiceReportsClient { return { getColumns: vi.fn(), saveColumns: vi.fn(), getReport: vi.fn(async (_token, _kind, query) => data(query.offset)), exportReport: vi.fn(async () => new Blob(['export'])) } }

it('defaults to all history through month end and appends without losing rows or totals', async () => {
  const api = client()
  render(<ServiceReportPanel accessToken="token" canRead dictionaryClient={dictionary} kind="payments" client={api} />)
  await screen.findByRole('rowheader', { name: '2' })
  const request = vi.mocked(api.getReport).mock.calls[0][2]
  expect(request.dateFrom).toBeUndefined()
  const end = new Date(`${request.dateTo}T12:00:00`)
  expect(end.getDate()).toBe(new Date(end.getFullYear(), end.getMonth() + 1, 0).getDate())
  expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Показать ещё строки' }))
  await screen.findByRole('rowheader', { name: '10' })
  expect(screen.getByRole('rowheader', { name: '2' })).toBeInTheDocument()
  expect(screen.getByText('Загружено: 2 из 2')).toBeInTheDocument()
  expect(screen.getByRole('rowheader', { name: 'ИТОГО по всему фильтру' }).parentElement).toHaveTextContent('100.00')
  expect(screen.getByRole('rowheader', { name: 'Итого за 30.09.2046' })).toBeInTheDocument()
})

it('shows debt modes and resets the page on mode and garage changes', async () => {
  const api = client()
  render(<ServiceReportPanel accessToken="token" canRead dictionaryClient={dictionary} kind="debt" client={api} />)
  await screen.findByRole('rowheader', { name: '2' })
  expect(screen.queryByLabelText('Начало периода оплаты')).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Просроченная' }))
  await waitFor(() => expect(api.getReport).toHaveBeenLastCalledWith('token', 'debt', expect.objectContaining({ overdueOnly: true, offset: 0 }), expect.any(AbortSignal)))
  await waitFor(() => expect(screen.getByLabelText('Гараж отчёта по услугам')).toBeEnabled())
  fireEvent.click(screen.getByRole('combobox', { name: 'Гараж отчёта по услугам' }))
  fireEvent.click(screen.getByRole('option', { name: 'Гараж 2' }))
  await waitFor(() => expect(api.getReport).toHaveBeenLastCalledWith('token', 'debt', expect.objectContaining({ garageId: 'g2', offset: 0 }), expect.any(AbortSignal)))
})

it('keeps existing rows after append failure and retries the same page', async () => {
  const api = client(); vi.mocked(api.getReport).mockResolvedValueOnce(data()).mockRejectedValueOnce(new Error('Ошибка страницы')).mockResolvedValue(data(50))
  render(<ServiceReportPanel accessToken="token" canRead dictionaryClient={dictionary} kind="payments" client={api} />)
  await screen.findByRole('rowheader', { name: '2' })
  fireEvent.click(screen.getByRole('button', { name: 'Показать ещё строки' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Ошибка страницы')
  expect(screen.getByRole('rowheader', { name: '2' })).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
  await screen.findByRole('rowheader', { name: '10' })
  expect(vi.mocked(api.getReport).mock.calls.slice(1).map((call) => call[2].offset)).toEqual([50, 50])
})

it('ignores late responses and aborts requests on unmount', async () => {
  let finish!: (value: ServiceReport) => void
  const api = client(); vi.mocked(api.getReport).mockImplementationOnce(() => new Promise((resolve) => { finish = resolve })).mockResolvedValue({ ...data(), rows: [], rowCount: 0, total: 0, totals: [0] })
  const view = render(<ServiceReportPanel accessToken="token" canRead dictionaryClient={dictionary} kind="debt" client={api} />)
  fireEvent.click(screen.getByRole('button', { name: 'Просроченная' }))
  await screen.findByText('Задолженности нет')
  await act(async () => finish(data()))
  expect(screen.queryByRole('rowheader', { name: '2' })).not.toBeInTheDocument()
  expect(vi.mocked(api.getReport).mock.calls[0][3]?.aborted).toBe(true)
  view.unmount(); expect(vi.mocked(api.getReport).mock.calls[1][3]?.aborted).toBe(true)
})

it.each(['xlsx', 'pdf'] as const)('exports the full filter as %s and exposes export errors', async (format) => {
  const api = client(); vi.mocked(api.exportReport).mockRejectedValueOnce(new Error('Экспорт недоступен')).mockResolvedValue(new Blob(['report']))
  render(<ServiceReportPanel accessToken="token" canRead dictionaryClient={dictionary} kind="payments" client={api} />)
  await screen.findByRole('rowheader', { name: '2' })
  fireEvent.click(screen.getByRole('button', { name: `Скачать отчёт ${format.toUpperCase()}` }))
  expect(await screen.findByText('Экспорт недоступен')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: `Скачать отчёт ${format.toUpperCase()}` }))
  await screen.findByText('Отчёт выгружен полностью по выбранным фильтрам.')
  expect(api.exportReport).toHaveBeenCalledWith('token', 'payments', expect.objectContaining({ offset: 0 }), format, expect.any(AbortSignal))
})

it('denies access without requests', () => {
  const api = client()
  render(<ServiceReportPanel accessToken="token" canRead={false} dictionaryClient={dictionary} kind="payments" client={api} />)
  expect(screen.getByText('Необходимо право чтения отчётов.')).toBeInTheDocument()
  expect(api.getReport).not.toHaveBeenCalled()
})

it('validates inclusive date ranges and clearing the required ending date without sending invalid requests', async () => {
  const api = client()
  render(<ServiceReportPanel accessToken="token" canRead dictionaryClient={dictionary} kind="payments" client={api} initialQuery={{ dateTo: '2046-09-30', offset: 0, limit: 50 }} />)
  await screen.findByRole('rowheader', { name: '2' })
  fireEvent.change(screen.getByLabelText('Начало периода оплаты'), { target: { value: '01.10.2046' } })
  expect(screen.getByRole('alert')).toHaveTextContent('Дата начала не может быть позже даты окончания.')
  expect(api.getReport).toHaveBeenCalledOnce()
  fireEvent.change(screen.getByLabelText('Начало периода оплаты'), { target: { value: '30.09.2046' } })
  await waitFor(() => expect(api.getReport).toHaveBeenLastCalledWith('token', 'payments', expect.objectContaining({ dateFrom: '2046-09-30', dateTo: '2046-09-30', offset: 0 }), expect.any(AbortSignal)))
  fireEvent.change(screen.getByLabelText('Конец периода отчёта'), { target: { value: '' } })
  expect(screen.getByRole('alert')).toHaveTextContent('Укажите дату окончания.')
  expect(screen.getByRole('button', { name: 'Скачать отчёт XLSX' })).toBeDisabled()
})

it('refuses to merge pages with changed column bindings and allows reformatting from the beginning', async () => {
  const api = client(); vi.mocked(api.getReport).mockResolvedValueOnce(data()).mockResolvedValue({ ...data(50), columns: [{ id: 'water', name: 'Вода', serviceIds: ['water-service'] }] })
  render(<ServiceReportPanel accessToken="token" canRead dictionaryClient={dictionary} kind="payments" client={api} />)
  await screen.findByRole('rowheader', { name: '2' })
  fireEvent.click(screen.getByRole('button', { name: 'Показать ещё строки' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Колонки отчёта изменились')
  expect(screen.queryByRole('columnheader', { name: 'Вода' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Переформировать отчёт' }))
  await screen.findByRole('columnheader', { name: 'Вода' })
  expect(api.getReport).toHaveBeenLastCalledWith('token', 'payments', expect.objectContaining({ offset: 0 }), expect.any(AbortSignal))
})

it('retries garage search and forwards the search filter', async () => {
  const api = client(); const search = { getGarages: vi.fn().mockRejectedValueOnce(new Error('search')).mockResolvedValue([]) } as unknown as DictionaryClient
  render(<ServiceReportPanel accessToken="token" canRead dictionaryClient={search} kind="payments" client={api} />)
  expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось найти гаражи.')
  fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
  await waitFor(() => expect(screen.getByRole('combobox', { name: 'Гараж отчёта по услугам' })).toBeEnabled())
  fireEvent.change(screen.getByLabelText('Поиск гаража отчёта по услугам'), { target: { value: '85' } })
  await waitFor(() => expect(search.getGarages).toHaveBeenLastCalledWith('token', '85', 20, true, expect.any(AbortSignal)))
})

it('aborts an in-progress export without downloading after unmount', async () => {
  const api = client(); let finish!: (blob: Blob) => void
  vi.mocked(api.exportReport).mockImplementation(() => new Promise((resolve) => { finish = resolve }))
  const view = render(<ServiceReportPanel accessToken="token" canRead dictionaryClient={dictionary} kind="debt" client={api} />)
  await screen.findByRole('rowheader', { name: '2' })
  fireEvent.click(screen.getByRole('button', { name: 'Скачать отчёт PDF' }))
  view.unmount(); expect(vi.mocked(api.exportReport).mock.calls[0][4]?.aborted).toBe(true)
  await act(async () => finish(new Blob(['late'])))
  expect(downloadBlob).not.toHaveBeenCalled()
})
