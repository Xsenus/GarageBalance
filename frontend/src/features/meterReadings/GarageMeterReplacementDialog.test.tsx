import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import type { AuthResponse } from '../../services/authApi'
import type { DictionaryClient } from '../../services/dictionariesApi'
import type { FinanceClient, MeterReadingDto } from '../../services/financeApi'
import { GarageMeterReplacementDialog } from './GarageMeterReplacementDialog'

function fixture() {
  const reading = { id: 'reading', garageId: 'garage', version: 'reading-v1', isCanceled: false } as MeterReadingDto
  const getPage = vi.fn().mockResolvedValue({ items: [reading], totalCount: 1 })
  const replace = vi.fn().mockResolvedValue({})
  const settings = vi.fn().mockResolvedValue([])
  const props = { auth: { accessToken: 'token', user: { permissions: ['payments.write'], roles: [] } } as AuthResponse,
    garage: { id: 'garage', number: '5' }, dictionaryClient: { getChargeServiceSettings: settings } as unknown as DictionaryClient,
    financeClient: { getMeterReadingsPage: getPage, replaceMeterDevice: replace } as unknown as FinanceClient,
    onClose: vi.fn(), onSaved: vi.fn() }
  return { props, replace, getPage, settings, reading }
}
function fill() {
  fireEvent.change(screen.getByLabelText('Номер нового счетчика'), { target: { value: ' NEW-1 ' } })
  fireEvent.change(screen.getByLabelText('Конечное показание старого счетчика'), { target: { value: '47,125' } })
  fireEvent.change(screen.getByLabelText('Причина замены счетчика'), { target: { value: ' Замена неисправного прибора ' } })
}

it('includes earlier devices in a repeated replacement and transfers the final value only on an explicit action', async () => {
  const { props, reading, getPage, replace } = fixture()
  getPage.mockResolvedValue({ items: [{ ...reading, isMeterReplacement: true, currentValue: 23, previousValue: 5, previousDeviceConsumption: 10 }], totalCount: 1 })
  render(<GarageMeterReplacementDialog {...props} />)
  await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить замену' })).toBeEnabled())
  fireEvent.change(screen.getByLabelText('Конечное показание старого счетчика'), { target: { value: '9' } })
  fireEvent.change(screen.getByLabelText('Начальное показание нового счетчика'), { target: { value: '20' } })
  // The month-end value comes from «Платежи»; the dialog does not ask for it.
  expect(screen.queryByLabelText('Текущее показание нового счетчика')).not.toBeInTheDocument()
  expect(screen.getByText(/Расход за месяц: 17/)).toHaveTextContent('ранее снятые приборы 10')
  expect(replace).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Перенести конечное в начальное' }))
  expect(screen.getByLabelText('Начальное показание нового счетчика')).toHaveValue('9')
  expect(screen.getByText(/Расход за месяц: 28/)).toBeInTheDocument()
})

it('checks the current reading, validates required fields and saves exact garage and reading versions', async () => {
  const { props, replace } = fixture()
  render(<GarageMeterReplacementDialog {...props} />)
  expect(screen.getByRole('status', { name: 'Проверяем показания счётчика' })).toHaveAttribute('aria-live', 'polite')
  await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить замену' })).toBeEnabled())
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить замену' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('корректные показания')
  expect(replace).not.toHaveBeenCalled()
  fill()
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить замену' }))
  await waitFor(() => expect(props.onSaved).toHaveBeenCalledOnce())
  expect(replace).toHaveBeenCalledWith('token', expect.objectContaining({ garageId: 'garage', meterKind: 'electricity',
    newSerialNumber: 'NEW-1', newInitialValue: 0, removedDeviceFinalValue: 47.125,
    meterReadingId: 'reading', expectedReadingVersion: 'reading-v1', reason: 'Замена неисправного прибора' }))
  expect(replace).toHaveBeenCalledWith('token', expect.not.objectContaining({ currentValue: expect.anything() }))
})

it('treats a lower month value as the removed meter value, blocks invalid precision, then preserves the draft and reloads on failure', async () => {
  const { props, replace, getPage, reading } = fixture()
  getPage.mockResolvedValue({ items: [{ ...reading, currentValue: 0, previousValue: 0 }], totalCount: 1 })
  replace.mockRejectedValueOnce(new Error('Показание уже изменено')).mockResolvedValue({})
  render(<GarageMeterReplacementDialog {...props} />)
  await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить замену' })).toBeEnabled())
  fill()
  fireEvent.change(screen.getByLabelText('Начальное показание нового счетчика'), { target: { value: '1' } })
  // A month value below the new meter's start was taken from the removed meter, so it does not block the replacement.
  expect(screen.getByRole('status')).toHaveTextContent('Показание за месяц 0 меньше начального показания нового счётчика и будет считаться показанием старого счётчика')
  expect(screen.getByRole('status')).toHaveTextContent('Показание нового счётчика на конец месяца введите в «Платежах»')
  fireEvent.change(screen.getByLabelText('Конечное показание старого счетчика'), { target: { value: '1.1234' } })
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить замену' }))
  expect(replace).not.toHaveBeenCalled()
  fill()
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить замену' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Показание уже изменено')
  await waitFor(() => expect(getPage).toHaveBeenCalledTimes(2))
  expect(screen.getByLabelText('Номер нового счетчика')).toHaveValue(' NEW-1 ')
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить замену' }))
  await waitFor(() => expect(props.onSaved).toHaveBeenCalledOnce())
  expect(replace).toHaveBeenLastCalledWith('token', expect.objectContaining({ newInitialValue: 1, removedDeviceFinalValue: 47.125 }))
  expect(replace).toHaveBeenLastCalledWith('token', expect.not.objectContaining({ currentValue: expect.anything() }))
})

it('shows a retryable loading error and rejects stale responses after a meter change or unmount', async () => {
  const { props, getPage } = fixture()
  let resolve!: (value: unknown) => void
  getPage.mockRejectedValueOnce(new Error('network')).mockImplementationOnce(() => new Promise((done) => { resolve = done }))
    .mockResolvedValue({ items: [], totalCount: 0 })
  const view = render(<GarageMeterReplacementDialog {...props} />)
  expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось проверить')
  fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
  await waitFor(() => expect(getPage).toHaveBeenCalledTimes(2))
  fireEvent.click(screen.getByRole('combobox', { name: 'Счётчик для замены' }))
  fireEvent.click(screen.getByRole('option', { name: 'Вода' }))
  await waitFor(() => expect(getPage).toHaveBeenCalledTimes(3))
  await act(async () => resolve({ items: [{ id: 'old', garageId: 'garage', version: 'old-v1' }], totalCount: 1 }))
  expect(getPage.mock.calls[1][2].aborted).toBe(true)
  fill()
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить замену' }))
  await waitFor(() => expect(props.onSaved).toHaveBeenCalledOnce())
  expect(props.financeClient.replaceMeterDevice).toHaveBeenCalledWith('token', expect.objectContaining({ meterKind: 'water', meterReadingId: undefined }))
  view.unmount()
  expect(getPage.mock.calls[2][2].aborted).toBe(true)
})

it('locates the exact garage across bounded pages and prevents duplicate submits', async () => {
  const { props, getPage, replace, reading } = fixture()
  getPage.mockResolvedValueOnce({ items: [{ ...reading, garageId: 'other' }], totalCount: 2 })
    .mockResolvedValueOnce({ items: [reading], totalCount: 2 })
  let done!: (value: unknown) => void
  replace.mockImplementationOnce(() => new Promise((resolve) => { done = resolve }))
  render(<GarageMeterReplacementDialog {...props} />)
  await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить замену' })).toBeEnabled())
  expect(getPage).toHaveBeenLastCalledWith('token', expect.objectContaining({ offset: 1, limit: 100, search: '5' }), expect.any(AbortSignal))
  fill()
  const form = screen.getByRole('button', { name: 'Сохранить замену' }).closest('form')!
  fireEvent.submit(form)
  fireEvent.submit(form)
  expect(replace).toHaveBeenCalledOnce()
  expect(screen.getByRole('button', { name: 'Отмена' })).toBeDisabled()
  await act(async () => done({}))
  expect(props.onSaved).toHaveBeenCalledOnce()
})

it('blocks writes without permission and allows cancellation by Escape', async () => {
  const { props, replace } = fixture()
  props.auth.user.permissions = []
  render(<GarageMeterReplacementDialog {...props} />)
  expect(screen.getByRole('alert')).toHaveTextContent('Нет права')
  expect(screen.getByRole('button', { name: 'Сохранить замену' })).toBeDisabled()
  fireEvent.submit(screen.getByRole('button', { name: 'Сохранить замену' }).closest('form')!)
  expect(replace).not.toHaveBeenCalled()
  fireEvent.keyDown(document, { key: 'Escape' })
  expect(props.onClose).toHaveBeenCalledOnce()
})

it('requires an explicit removed meter value, shows the consumption and refuses a value below the last reading', async () => {
  const { props, getPage, reading } = fixture()
  getPage.mockResolvedValue({ items: [{ ...reading, currentValue: 1200, previousValue: 1000 }], totalCount: 1 })
  const getMeterDevices = vi.fn().mockResolvedValue([
    { id: 'old', serialNumber: 'Начало учёта', installedOn: '2026-10-01', removedOn: null, initialValue: 1000, finalValue: null, lastReadingValue: 1200 },
  ])
  props.financeClient = { ...props.financeClient, getMeterDevices } as unknown as FinanceClient
  render(<GarageMeterReplacementDialog {...props} />)

  // The month value 1200 belongs to the new meter; the removed meter starts from the month's opening value.
  await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить замену' })).toBeEnabled())
  expect(screen.getByLabelText('Конечное показание старого счетчика')).toHaveValue('')
  expect(getMeterDevices).toHaveBeenCalledWith('token', 'garage', 'electricity', expect.any(AbortSignal))
  fireEvent.change(screen.getByLabelText('Конечное показание старого счетчика'), { target: { value: '1200' } })
  fireEvent.change(screen.getByLabelText('Начальное показание нового счетчика'), { target: { value: '1100' } })
  expect(screen.getByText(/Расход за месяц: 300 \(старый счётчик 200, новый 100; показание за месяц .+ из «Платежей»\)\./)).toBeInTheDocument()

  fireEvent.change(screen.getByLabelText('Конечное показание старого счетчика'), { target: { value: '900' } })
  expect(screen.getByText(/Конечное показание старого счётчика меньше его последнего показания/)).toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Конечное показание старого счетчика'), { target: { value: '' } })
  expect(screen.getByLabelText('Конечное показание старого счетчика')).toHaveValue('')
})

it('uses the active device history when the month has no reading yet', async () => {
  const { props, getPage } = fixture()
  getPage.mockResolvedValue({ items: [], totalCount: 0 })
  props.financeClient = { ...props.financeClient, getMeterDevices: vi.fn().mockResolvedValue([
    { id: 'old', serialNumber: '1', installedOn: '2026-10-01', removedOn: null, initialValue: 100, finalValue: null, lastReadingValue: null },
    { id: 'older', serialNumber: '0', installedOn: '2026-01-01', removedOn: '2026-09-30', initialValue: 0, finalValue: 50 },
  ]) } as unknown as FinanceClient
  render(<GarageMeterReplacementDialog {...props} />)
  await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить замену' })).toBeEnabled())

  expect(screen.getByLabelText('Конечное показание старого счетчика')).toHaveValue('')
  fireEvent.change(screen.getByLabelText('Конечное показание старого счетчика'), { target: { value: '130' } })

  expect(screen.getByText(/Расход старого счётчика: 30\. Показание нового счётчика на конец месяца введите в «Платежах»/)).toBeInTheDocument()
})

it('requires the operator to enter the final value even when the device has a saved reading', async () => {
  const { props, getPage, replace } = fixture()
  getPage.mockResolvedValue({ items: [], totalCount: 0 })
  props.financeClient = { ...props.financeClient, getMeterDevices: vi.fn().mockResolvedValue([
    { id: 'old', removedOn: null, initialValue: 100, lastReadingValue: 130 },
  ]) } as unknown as FinanceClient
  render(<GarageMeterReplacementDialog {...props} />)
  await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить замену' })).toBeEnabled())
  expect(screen.getByLabelText('Конечное показание старого счетчика')).toHaveValue('')
  fireEvent.change(screen.getByLabelText('Номер нового счетчика'), { target: { value: 'NEW' } })
  fireEvent.change(screen.getByLabelText('Причина замены счетчика'), { target: { value: 'Замена' } })
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить замену' }))
  expect(replace).not.toHaveBeenCalled()
  fireEvent.change(screen.getByLabelText('Конечное показание старого счетчика'), { target: { value: '129' } })
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить замену' }))
  expect(screen.getByRole('alert')).toHaveTextContent('не может быть меньше')
  expect(replace).not.toHaveBeenCalled()
  fireEvent.change(screen.getByLabelText('Конечное показание старого счетчика'), { target: { value: '130' } })
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить замену' }))
  await waitFor(() => expect(replace).toHaveBeenCalledWith('token', expect.objectContaining({ removedDeviceFinalValue: 130 })))
})
