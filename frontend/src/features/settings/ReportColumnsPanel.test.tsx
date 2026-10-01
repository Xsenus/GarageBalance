import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import type { ServiceReportColumns, ServiceReportsClient } from '../../services/serviceReportsApi'
import { ReportColumnsPanel } from './ReportColumnsPanel'
import { readFileSync } from 'node:fs'
function selectReport(label: string) {
  fireEvent.click(screen.getByRole('combobox', { name: 'Отчёт' }))
  fireEvent.click(screen.getByRole('option', { name: label }))
}
const data: ServiceReportColumns = { version: 'old', columns: [{ id: 'c1', name: 'Свет', serviceIds: ['s1'] }, { id: 'c2', name: 'Вода', serviceIds: [] }], services: [{ id: 's1', name: 'Электричество', incomeTypeId: 'i1', isArchived: false }, { id: 's2', name: 'Вода', incomeTypeId: 'i2', isArchived: false }] }
function client(): ServiceReportsClient { return { getColumns: vi.fn(async () => structuredClone(data)), saveColumns: vi.fn(async (_token, request) => ({ ...data, ...request, version: 'new' })), getReport: vi.fn(), exportReport: vi.fn() } }

it('edits column name and bindings, prevents duplicate assignment, persists version and reorders', async () => {
  const api = client(); render(<ReportColumnsPanel token="token" canManage client={api} />)
  await screen.findByDisplayValue('Свет')
  fireEvent.change(screen.getByLabelText('Название колонки'), { target: { value: 'Энергия' } })
  fireEvent.click(screen.getByLabelText('Услуга Вода'))
  fireEvent.click(screen.getByRole('button', { name: 'Вода, услуг: 0' }))
  expect(screen.getByLabelText('Услуга Электричество')).toBeDisabled()
  expect(screen.getByLabelText('Услуга Вода')).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Поднять колонку Вода' }))
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить колонки' }))
  await screen.findByText(/Колонки сохранены/)
  expect(api.saveColumns).toHaveBeenCalledWith('token', { version: 'old', report: 'payments', columns: [{ id: 'c2', name: 'Вода', serviceIds: [] }, { id: 'c1', name: 'Энергия', serviceIds: ['s1', 's2'] }] }, expect.any(AbortSignal))
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить колонки' }))
  await waitFor(() => expect(api.saveColumns).toHaveBeenLastCalledWith('token', expect.objectContaining({ version: 'new' }), expect.any(AbortSignal)))
})

it('shows service counts as separate badges and identifies assigned columns without duplicate text separators', async () => {
  render(<ReportColumnsPanel token="token" canManage client={client()} />)
  const light = await screen.findByRole('button', { name: 'Свет, услуг: 1' })
  expect(screen.getByRole('region', { name: 'Колонки отчётов' })).toHaveClass('report-columns-settings')
  expect(readFileSync('src/features/reports/serviceReports.css', 'utf8')).toContain('.report-columns-settings > .form-field { margin-top: 8px; margin-bottom: 16px; padding-bottom: 16px; border-bottom: 1px solid #dce3ee; }')
  expect(within(light).getByText('1')).toHaveClass('report-column-count')
  expect(light).not.toHaveTextContent('·')
  const water = screen.getByRole('button', { name: 'Вода, услуг: 0' })
  expect(within(water).getByText('0')).toHaveClass('report-column-count')
  expect(screen.getByLabelText('Услуга Электричество').closest('label')?.querySelector('.report-column-assignment')).toBeNull()
  fireEvent.click(water)
  const assigned = screen.getByLabelText('Услуга Электричество').closest('label')!
  expect(within(assigned).getByText('Колонка')).toHaveClass('report-column-assignment-label')
  expect(within(assigned).getByText('Свет').closest('.report-column-assignment')).not.toBeNull()
  expect(assigned).not.toHaveTextContent('·')
  expect(screen.getByLabelText('Услуга Электричество')).toBeDisabled()
})

it('adds and removes columns and filters available services', async () => {
  render(<ReportColumnsPanel token="token" canManage client={client()} />)
  await screen.findByDisplayValue('Свет')
  fireEvent.click(screen.getByRole('button', { name: 'Добавить колонку' }))
  expect(screen.getByDisplayValue('Новая колонка')).toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Поиск услуги'), { target: { value: 'нет услуги' } })
  expect(screen.getByText('Услуги не найдены')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Удалить колонку Новая колонка' }))
  expect(screen.queryByDisplayValue('Новая колонка')).not.toBeInTheDocument()
})

it('retries initial errors and keeps edits on a save conflict', async () => {
  const api = client(); vi.mocked(api.getColumns).mockRejectedValueOnce(new Error('Загрузка недоступна')); vi.mocked(api.saveColumns).mockRejectedValueOnce(new Error('Конфликт версии'))
  render(<ReportColumnsPanel token="token" canManage client={api} />)
  expect(await screen.findByRole('alert')).toHaveTextContent('Загрузка недоступна')
  fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
  await screen.findByDisplayValue('Свет')
  fireEvent.change(screen.getByLabelText('Название колонки'), { target: { value: 'Новая энергия' } })
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить колонки' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Конфликт версии')
  expect(screen.getByDisplayValue('Новая энергия')).toBeInTheDocument()
})

it('ignores stale load responses and aborts saves on unmount', async () => {
  const api = client(); let resolve!: (value: ServiceReportColumns) => void
  vi.mocked(api.getColumns).mockImplementationOnce(() => new Promise((finish) => { resolve = finish }))
  const view = render(<ReportColumnsPanel token="old" canManage client={api} />)
  view.rerender(<ReportColumnsPanel token="new" canManage client={api} />)
  await screen.findByDisplayValue('Свет')
  await act(async () => resolve({ ...data, columns: [{ id: 'stale', name: 'Устаревшая', serviceIds: [] }] }))
  expect(screen.queryByDisplayValue('Устаревшая')).not.toBeInTheDocument()
  vi.mocked(api.saveColumns).mockImplementation(() => new Promise(() => {}))
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить колонки' }))
  view.unmount(); expect(vi.mocked(api.saveColumns).mock.calls[0][2]?.aborted).toBe(true)
})

it('does not call the API without permission', () => {
  const api = client(); render(<ReportColumnsPanel token="token" canManage={false} client={api} />)
  expect(api.getColumns).not.toHaveBeenCalled()
  expect(screen.getByText(/необходимо право управления тарифами/)).toBeInTheDocument()
})

it('keeps separate drafts and versions for each report and saves only the selected report', async () => {
  const api = client()
  vi.mocked(api.getColumns).mockImplementation(async (_token, _signal, report = 'payments') => ({ ...structuredClone(data), version: report, report, columns: [{ id: 'c1', name: report, serviceIds: [] }] }))
  render(<ReportColumnsPanel token="token" canManage client={api} />)
  await screen.findByDisplayValue('payments')
  fireEvent.change(screen.getByLabelText('Название колонки'), { target: { value: 'Мои оплаты' } })
  selectReport('Задолженность — начисленная')
  await screen.findByDisplayValue('accrued')
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить колонки' }))
  await screen.findByText(/Колонки сохранены только/)
  expect(api.saveColumns).toHaveBeenLastCalledWith('token', expect.objectContaining({ report: 'accrued', version: 'accrued' }), expect.any(AbortSignal))
  selectReport('Задолженность — просроченная')
  await screen.findByDisplayValue('overdue')
  selectReport('Оплата по услугам')
  await screen.findByDisplayValue('Мои оплаты')
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить колонки' }))
  await screen.findByText(/Колонки сохранены только/)
  expect(api.saveColumns).toHaveBeenLastCalledWith('token', expect.objectContaining({ report: 'payments', version: 'payments', columns: [{ id: 'c1', name: 'Мои оплаты', serviceIds: [] }] }), expect.any(AbortSignal))
})

it('cancels stale report loads, disables switching while saving and reloads the selected scope after a conflict', async () => {
  const api = client(); let resolve!: (value: ServiceReportColumns) => void
  vi.mocked(api.getColumns).mockImplementationOnce(() => new Promise((finish) => { resolve = finish }))
  vi.mocked(api.saveColumns).mockRejectedValueOnce(new Error('Конфликт версии'))
  render(<ReportColumnsPanel token="token" canManage client={api} />)
  selectReport('Задолженность — просроченная')
  await screen.findByDisplayValue('Свет')
  await act(async () => resolve({ ...data, columns: [{ id: 'stale', name: 'Чужой отчёт', serviceIds: [] }] }))
  expect(screen.queryByDisplayValue('Чужой отчёт')).not.toBeInTheDocument()
  expect(vi.mocked(api.getColumns).mock.calls[0][1]?.aborted).toBe(true)
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить колонки' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Конфликт версии')
  fireEvent.click(screen.getByRole('button', { name: 'Отменить правки и перечитать настройки' }))
  await screen.findByDisplayValue('Свет')
  expect(api.getColumns).toHaveBeenLastCalledWith('token', expect.any(AbortSignal), 'overdue')
  vi.mocked(api.saveColumns).mockImplementation(() => new Promise(() => {}))
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить колонки' }))
  expect(screen.getByRole('combobox', { name: 'Отчёт' })).toBeDisabled()
})

it('aborts a save of a cached draft when the token changes and ignores its late response', async () => {
  const api = client(); let finish!: (value: ServiceReportColumns) => void
  const view = render(<ReportColumnsPanel token="token" canManage client={api} />)
  await screen.findByDisplayValue('Свет')
  selectReport('Задолженность — начисленная')
  await screen.findByDisplayValue('Свет')
  selectReport('Оплата по услугам')
  await screen.findByDisplayValue('Свет')
  vi.mocked(api.saveColumns).mockImplementationOnce(() => new Promise((resolve) => { finish = resolve }))
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить колонки' }))
  view.rerender(<ReportColumnsPanel token="new-token" canManage client={api} />)
  expect(vi.mocked(api.saveColumns).mock.calls[0][2]?.aborted).toBe(true)
  await act(async () => finish({ ...data, columns: [{ id: 'late', name: 'Позднее сохранение', serviceIds: [] }] }))
  expect(screen.queryByDisplayValue('Позднее сохранение')).not.toBeInTheDocument()
  expect(api.getColumns).toHaveBeenLastCalledWith('new-token', expect.any(AbortSignal), 'payments')
  await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить колонки' })).toBeEnabled())
})
