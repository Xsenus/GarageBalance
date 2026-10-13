import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { GarageMeterStartFields } from './GarageMeterStartFields'
import type { GarageMeterStartValueDto } from '../../services/dictionariesApi'
import { toGarageMeterStartRows } from './garageMeterStartValues'

const values: GarageMeterStartValueDto[] = [{ meterKind: 'water', serviceName: 'Вода', unitName: 'м³', value: 10, hasReadings: true }]
const makeProps = () => ({ loadKey: 'garage-1', load: vi.fn().mockResolvedValue(values), rows: toGarageMeterStartRows(values),
  errors: {}, disabled: false, onLoaded: vi.fn(), onChange: vi.fn(), onConnectionDateChange: vi.fn(), onReasonChange: vi.fn(), onFieldEdited: vi.fn() })

it('loads labelled fields, preserves validation and disables edits during saving', async () => {
  const props = makeProps()
  const { rerender } = render(<GarageMeterStartFields {...props} errors={{ 'meter:water': 'Ошибка' }} />)
  expect(screen.getByRole('status', { name: 'Загружаем стартовые значения счётчиков' })).toHaveAttribute('aria-live', 'polite')
  const input = await screen.findByLabelText('Стартовое значение счётчика: Вода')
  expect(input).toHaveAttribute('aria-invalid', 'true')
  fireEvent.change(input, { target: { value: '15' } })
  expect(props.onFieldEdited).toHaveBeenCalledWith('meter:water')
  expect(props.onChange).toHaveBeenCalledWith('water', '15')
  rerender(<GarageMeterStartFields {...props} disabled />)
  expect(input).toBeDisabled()
  expect(props.load).toHaveBeenCalledOnce()
})

it('retries a failed load and accepts an empty service list', async () => {
  const props = makeProps()
  props.load.mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce([])
  render(<GarageMeterStartFields {...props} rows={[]} />)
  expect(await screen.findByText('Не удалось загрузить стартовые значения счётчиков.')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /Повторить/ }))
  await waitFor(() => expect(props.onLoaded).toHaveBeenCalledWith([]))
  expect(screen.getByText('Нет применимых услуг со счётчиком.')).toBeInTheDocument()
})

it('edits an individual connection date, displays missing baseline, and disables both fields during save', async () => {
  const props = makeProps()
  const rows = toGarageMeterStartRows([{ ...values[0], value: null, connectedOn: '2026-09-10', requiresStartValue: true, connectionVersion: 'v1' }])
  const { rerender } = render(<GarageMeterStartFields {...props} rows={rows} errors={{ 'meter:water:connectedOn': 'Дата' }} />)
  const date = await screen.findByLabelText('Дата подключения услуги: Вода')
  expect(date).toHaveValue('10.09.2026')
  expect(date).toHaveAttribute('aria-invalid', 'true')
  expect(screen.getByText('Требуется стартовое показание')).toBeInTheDocument()
  fireEvent.change(date, { target: { value: '12.09.2026' } })
  fireEvent.blur(date)
  expect(props.onFieldEdited).toHaveBeenCalledWith('meter:water:connectedOn')
  expect(props.onConnectionDateChange).toHaveBeenCalledWith('water', '2026-09-12')
  rerender(<GarageMeterStartFields {...props} rows={[{ ...rows[0], value: '0', reason: 'Акт сверки' }]} disabled />)
  expect(screen.getByText('Первый расчётный месяц: 09.2026')).toBeInTheDocument()
  expect(date).toBeDisabled()
  expect(screen.getByLabelText('Причина изменения учёта: Вода')).toBeDisabled()
  rerender(<GarageMeterStartFields {...props} rows={[{ ...rows[0], value: '0', reason: '' }]} />)
  fireEvent.change(screen.getByLabelText('Причина изменения учёта: Вода'), { target: { value: 'Акт сверки' } })
  expect(props.onReasonChange).toHaveBeenCalledWith('water', 'Акт сверки')
})

it('shows unconnected services without requiring a correction reason', async () => {
  const props = makeProps()
  render(<GarageMeterStartFields {...props} rows={toGarageMeterStartRows([{ ...values[0], value: null, hasReadings: false, requiresConnectionDate: true }])} />)
  expect(await screen.findByText('Не подключена')).toBeInTheDocument()
  expect(screen.queryByLabelText('Причина изменения учёта: Вода')).not.toBeInTheDocument()
})

it('shows the saved tariff warning independently of the baseline and hides it for a changed connection date', async () => {
  const props = makeProps()
  const rows = toGarageMeterStartRows([{ ...values[0], value: null, connectedOn: '2026-09-10', requiresTariff: true }])
  const { rerender } = render(<GarageMeterStartFields {...props} rows={rows} />)
  expect(await screen.findByText('Нет тарифа на весь первый месяц подключения: начисление не рассчитано.')).toBeInTheDocument()
  expect(screen.getByText('Требуется стартовое показание')).toBeInTheDocument()
  rerender(<GarageMeterStartFields {...props} rows={[{ ...rows[0], connectedOn: '2026-10-10' }]} />)
  expect(screen.queryByText(/Нет тарифа на весь/)).not.toBeInTheDocument()
  expect(screen.getByText('Тариф будет проверен после сохранения.')).toBeInTheDocument()
  rerender(<GarageMeterStartFields {...props} rows={[{ ...rows[0], requiresTariff: false }]} />)
  expect(screen.queryByText(/Нет тарифа на весь/)).not.toBeInTheDocument()
  expect(screen.queryByText('Тариф будет проверен после сохранения.')).not.toBeInTheDocument()
  expect(toGarageMeterStartRows(values)[0].requiresTariff).toBe(false)
})

it('requires connection permissions for dates and explicit baselines while allowing legacy value edits', async () => {
  const p = makeProps()
  const rows = toGarageMeterStartRows([values[0], { ...values[0], meterKind: 'electricity', serviceName: 'Свет', connectedOn: '2026-09-10' }])
  render(<GarageMeterStartFields {...p} rows={rows} canManageConnections={false} />)
  expect(await screen.findByLabelText('Стартовое значение счётчика: Вода')).toBeEnabled()
  expect(screen.getByLabelText('Дата подключения услуги: Вода')).toBeDisabled()
  expect(screen.getByLabelText('Стартовое значение счётчика: Свет')).toBeDisabled()
  expect(screen.getByLabelText('Дата подключения услуги: Свет')).toBeDisabled()
})

it('aborts old requests and ignores stale success and failure after the garage changes or closes', async () => {
  const props = makeProps()
  let resolveOld!: (value: GarageMeterStartValueDto[]) => void
  let rejectCurrent!: (error: Error) => void
  props.load.mockImplementationOnce(() => new Promise((resolve) => { resolveOld = resolve }))
    .mockImplementationOnce(() => new Promise((_, reject) => { rejectCurrent = reject }))
  const { rerender, unmount } = render(<GarageMeterStartFields {...props} />)
  const oldSignal = props.load.mock.calls[0][0] as AbortSignal
  rerender(<GarageMeterStartFields {...props} loadKey="garage-2" />)
  expect(oldSignal.aborted).toBe(true)
  await act(async () => resolveOld(values))
  expect(props.onLoaded).not.toHaveBeenCalled()
  const currentSignal = props.load.mock.calls[1][0] as AbortSignal
  unmount()
  expect(currentSignal.aborted).toBe(true)
  await act(async () => rejectCurrent(new Error('late failure')))
  expect(props.onLoaded).not.toHaveBeenCalled()
})
