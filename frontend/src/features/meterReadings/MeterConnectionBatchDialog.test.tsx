import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import type { FinanceClient, MeterReadingYearPageDto } from '../../services/financeApi'
import { MeterConnectionBatchDialog } from './MeterConnectionBatchDialog'

const page: MeterReadingYearPageDto = { garages: [{ id: 'g1', number: '7', requiresConnectionDate: true }, { id: 'g2', number: '8', requiresConnectionDate: true }, { id: 'g3', number: '9', connectedOn: '2026-01-01' }], readings: [], totalCount: 3, offset: 0, limit: 25 }
const props = () => ({ accessToken: 'token', financeClient: { getMeterReadingYearPage: vi.fn().mockResolvedValue(page), connectMeterServicesBatch: vi.fn().mockResolvedValue({ items: [] }) } as unknown as FinanceClient, meterKind: 'water', serviceName: 'Вода', year: '2026', onClose: vi.fn(), onSaved: vi.fn() })
function enterDate() { const input = screen.getByLabelText('Общая дата подключения'); fireEvent.change(input, { target: { value: '10.09.2026' } }); fireEvent.blur(input) }

it('previews selected individual zero and pending bases and submits all garages atomically', async () => {
  const p = props()
  render(<MeterConnectionBatchDialog {...p} />)
  expect(screen.getByRole('status', { name: 'Загружаем гаражи для подключения' })).toBeInTheDocument()
  fireEvent.click(await screen.findByLabelText('Подключить гараж 7'))
  fireEvent.click(screen.getByLabelText('Подключить гараж 8'))
  expect(screen.getByLabelText('Стартовое показание гаража 7')).toHaveAttribute('inputmode', 'decimal')
  expect(screen.getByLabelText('Подключить гараж 9')).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Проверить' })).toHaveClass('create-action-button')
  enterDate()
  fireEvent.change(screen.getByLabelText('Стартовое показание гаража 7'), { target: { value: '0' } })
  fireEvent.click(screen.getByRole('button', { name: 'Проверить' }))
  expect(screen.getByRole('table', { name: 'Проверка подключения гаражей' })).toHaveTextContent('Требуется стартовое показание')
  expect(screen.getByText(/Дата подключения: 10.09.2026/)).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Подключить', exact: true }))
  await waitFor(() => expect(p.onSaved).toHaveBeenCalledOnce())
  expect(p.financeClient.connectMeterServicesBatch).toHaveBeenCalledWith('token', { meterKind: 'water', connectedOn: '2026-09-10', items: [{ garageId: 'g1', value: 0 }, { garageId: 'g2', value: null }] })
})

it('validates selection, date and individual baselines, preserving edits when returning from preview', async () => {
  const p = props()
  render(<MeterConnectionBatchDialog {...p} />)
  await screen.findByLabelText('Подключить гараж 7')
  fireEvent.click(screen.getByRole('button', { name: 'Проверить' }))
  expect(screen.getByRole('alert')).toHaveTextContent('Выберите хотя бы один гараж.')
  fireEvent.click(screen.getByLabelText('Подключить гараж 7'))
  fireEvent.click(screen.getByRole('button', { name: 'Проверить' }))
  expect(screen.getByRole('alert')).toHaveTextContent('Укажите дату подключения.')
  enterDate()
  fireEvent.change(screen.getByLabelText('Стартовое показание гаража 7'), { target: { value: '-5' } })
  fireEvent.click(screen.getByRole('button', { name: 'Проверить' }))
  expect(screen.getByRole('alert')).toHaveTextContent('должно быть числом')
  fireEvent.change(screen.getByLabelText('Стартовое показание гаража 7'), { target: { value: '250' } })
  fireEvent.click(screen.getByRole('button', { name: 'Проверить' }))
  fireEvent.click(screen.getByRole('button', { name: 'Назад' }))
  expect(screen.getByLabelText('Стартовое показание гаража 7')).toHaveValue('250')
  fireEvent.click(screen.getByLabelText('Подключить гараж 7'))
  expect(screen.getByText('Выбрано гаражей: 0 из 100')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Отмена' }))
  expect(p.onClose).toHaveBeenCalledOnce()
})

it('retries loading and save failures without losing the selected bases', async () => {
  const p = props()
  vi.mocked(p.financeClient.getMeterReadingYearPage).mockRejectedValueOnce(new Error('Сеть недоступна'))
  vi.mocked(p.financeClient.connectMeterServicesBatch!).mockRejectedValueOnce(new Error('Услуга уже подключена другим сотрудником'))
  render(<MeterConnectionBatchDialog {...p} />)
  expect(await screen.findByRole('alert')).toHaveTextContent('Сеть недоступна')
  fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
  fireEvent.click(await screen.findByLabelText('Подключить гараж 7'))
  enterDate()
  fireEvent.click(screen.getByRole('button', { name: 'Проверить' }))
  fireEvent.click(screen.getByRole('button', { name: 'Подключить', exact: true }))
  expect(await screen.findByRole('alert')).toHaveTextContent('другим сотрудником')
  expect(p.onSaved).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Подключить', exact: true }))
  await waitFor(() => expect(p.onSaved).toHaveBeenCalledOnce())
})

it('keeps selection across paginated loads and limits the batch to 100 garages', async () => {
  const p = props()
  vi.mocked(p.financeClient.getMeterReadingYearPage).mockImplementation(async (_token, params) => ({ ...page, totalCount: 101, offset: params.offset ?? 0, garages: Array.from({ length: params.offset ? 1 : 100 }, (_, index) => ({ id: `g${index + (params.offset ?? 0)}`, number: String(index + (params.offset ?? 0)), requiresConnectionDate: true })) }))
  render(<MeterConnectionBatchDialog {...p} />)
  await screen.findByLabelText('Подключить гараж 0')
  fireEvent.click(screen.getByRole('button', { name: '100', exact: true }))
  await waitFor(() => expect(p.financeClient.getMeterReadingYearPage).toHaveBeenLastCalledWith('token', expect.objectContaining({ limit: 100 }), expect.any(AbortSignal)))
  for (const checkbox of await screen.findAllByRole('checkbox')) fireEvent.click(checkbox)
  expect(screen.getByText('Выбрано гаражей: 100 из 100')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Страница 2', exact: true }))
  expect(await screen.findByLabelText('Подключить гараж 100')).toBeDisabled()
  expect(screen.getByText('Выбрано гаражей: 100 из 100')).toBeInTheDocument()
})

it('aborts stale loads and supports an empty page and closing by Escape', async () => {
  const p = props()
  let resolve!: (value: MeterReadingYearPageDto) => void
  vi.mocked(p.financeClient.getMeterReadingYearPage).mockImplementationOnce(() => new Promise((done) => { resolve = done })).mockResolvedValue({ ...page, garages: [], totalCount: 0 })
  const { rerender, unmount } = render(<MeterConnectionBatchDialog {...p} />)
  const signal = vi.mocked(p.financeClient.getMeterReadingYearPage).mock.calls[0][2]!
  rerender(<MeterConnectionBatchDialog {...p} meterKind="electricity" />)
  expect(signal.aborted).toBe(true)
  await act(async () => resolve(page))
  expect(await screen.findByText('Нет гаражей для этой услуги.')).toBeInTheDocument()
  fireEvent.keyDown(document, { key: 'Escape' })
  expect(p.onClose).toHaveBeenCalledOnce()
  const lastSignal = vi.mocked(p.financeClient.getMeterReadingYearPage).mock.calls[1][2]!
  unmount()
  expect(lastSignal.aborted).toBe(true)
})
