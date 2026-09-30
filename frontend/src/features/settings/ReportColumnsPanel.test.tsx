import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import type { ServiceReportColumns, ServiceReportsClient } from '../../services/serviceReportsApi'
import { ReportColumnsPanel } from './ReportColumnsPanel'
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
  expect(api.saveColumns).toHaveBeenCalledWith('token', { version: 'old', columns: [{ id: 'c2', name: 'Вода', serviceIds: [] }, { id: 'c1', name: 'Энергия', serviceIds: ['s1', 's2'] }] }, expect.any(AbortSignal))
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить колонки' }))
  await waitFor(() => expect(api.saveColumns).toHaveBeenLastCalledWith('token', expect.objectContaining({ version: 'new' }), expect.any(AbortSignal)))
})

it('shows service counts as separate badges and identifies assigned columns without duplicate text separators', async () => {
  render(<ReportColumnsPanel token="token" canManage client={client()} />)
  const light = await screen.findByRole('button', { name: 'Свет, услуг: 1' })
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
