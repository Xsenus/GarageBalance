import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import type { AuthResponse } from '../../services/authApi'
import type { DictionaryClient } from '../../services/dictionariesApi'
import type { FinanceClient, MeterDeviceDto } from '../../services/financeApi'
import GarageMeterHistoryDialog from './GarageMeterHistoryDialog'
import { ActionCommentSettingsProvider } from '../../shared/ActionCommentSettings'
import type { ApplicationSettingsClient } from '../../services/settingsApi'

function fixture() {
  const device: MeterDeviceDto = { id: 'new', garageId: 'garage', meterKind: 'water', serialNumber: 'W-2', installedOn: '2026-06-10', removedOn: null,
    initialValue: 5.125, finalValue: null, version: 'dv1', replacedDeviceId: 'old', replacementAccountingMonth: '2026-06-01',
    replacementReadingId: 'reading', replacementReadingVersion: 'rv1', replacementReason: 'Неисправность' }
  const old: MeterDeviceDto = { ...device, id: 'old', serialNumber: 'W-1', installedOn: '2026-01-01', removedOn: '2026-06-09',
    initialValue: 100, finalValue: 120, replacedDeviceId: null }
  const page = { items: [device, old], totalCount: 2, offset: 0, limit: 25 }
  const load = vi.fn().mockResolvedValue(page)
  const cancel = vi.fn().mockResolvedValue({})
  const props = { auth: { accessToken: 'token', user: { permissions: ['payments.read', 'payments.write'], roles: [] } } as AuthResponse,
    garage: { id: 'garage', number: '5' }, dictionaryClient: { getChargeServiceSettings: vi.fn().mockResolvedValue([]) } as unknown as DictionaryClient,
    financeClient: { getMeterDeviceHistory: load, cancelMeterDeviceReplacement: cancel } as unknown as FinanceClient,
    onClose: vi.fn(), onChanged: vi.fn() }
  return { props, device, page, load, cancel }
}

it('allows cancellation without a reason when the shared comment setting is disabled', async () => {
  const { props, cancel } = fixture()
  const settings = { getActionCommentSettings: vi.fn().mockResolvedValue({ required: false, version: 'settings-version' }) } as unknown as ApplicationSettingsClient
  render(<ActionCommentSettingsProvider accessToken="token" client={settings}><GarageMeterHistoryDialog {...props} /></ActionCommentSettingsProvider>)
  fireEvent.click(await screen.findByRole('button', { name: /Отменить замену на/ }))
  await waitFor(() => expect(screen.getByLabelText('Причина отмены замены')).not.toBeRequired())
  fireEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }))
  await waitFor(() => expect(cancel).toHaveBeenCalledWith('token', 'new', expect.objectContaining({ reason: '' })))
})

it('shows actual device history with dates, values, reasons and the shared pagination', async () => {
  const { props, load } = fixture()
  render(<GarageMeterHistoryDialog {...props} />)
  expect(screen.getByRole('status', { name: 'Загружаем историю приборов' })).toHaveAttribute('aria-live', 'polite')
  const table = await screen.findByRole('table', { name: 'Приборы гаража' })
  expect(table).toHaveTextContent('10.06.2026')
  expect(table).toHaveTextContent('09.06.2026')
  expect(table).toHaveTextContent('5,125')
  expect(table).toHaveTextContent('Неисправность')
  expect(table).toHaveTextContent('Действующий')
  expect(table).toHaveTextContent('Снят')
  expect(screen.getByRole('group', { name: 'Количество приборов' })).toBeInTheDocument()
  expect(load).toHaveBeenCalledWith('token', 'garage', { offset: 0, limit: 25 }, expect.any(AbortSignal))
})

it('shows canceled custom-service devices without cancellation actions and keeps keyboard focus in the dialog', async () => {
  const { props, load, page, device } = fixture()
  load.mockResolvedValue({ ...page, items: [{ ...device, serviceName: 'Коммерческая энергия', isCanceled: true, cancellationReason: 'Ошибка установки' }] })
  render(<GarageMeterHistoryDialog {...props} />)
  const table = await screen.findByRole('table', { name: 'Приборы гаража' })
  expect(table).toHaveTextContent('Коммерческая энергия')
  expect(table).toHaveTextContent('Замена отменена')
  expect(table).toHaveTextContent('Ошибка установки')
  expect(screen.queryByRole('button', { name: /Отменить замену на/ })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Закрыть историю приборов' })).toHaveFocus()
})

it('focuses the cancellation reason and restores the selected row after Escape', async () => {
  const { props } = fixture()
  render(<GarageMeterHistoryDialog {...props} />)
  const action = await screen.findByRole('button', { name: /Отменить замену на/ })
  action.focus()
  fireEvent.click(action)
  expect(screen.getByLabelText('Причина отмены замены')).toHaveFocus()
  fireEvent.keyDown(window, { key: 'Escape' })
  expect(screen.queryByLabelText('Причина отмены замены')).not.toBeInTheDocument()
  expect(action).toHaveFocus()
  expect(props.onClose).not.toHaveBeenCalled()
})

it('waits for a finished load before showing the empty state and retries failures', async () => {
  const { props, load } = fixture()
  load.mockRejectedValueOnce(new Error('Сеть недоступна')).mockResolvedValue({ items: [], totalCount: 0, offset: 0, limit: 25 })
  render(<GarageMeterHistoryDialog {...props} />)
  expect(screen.queryByText('Приборов пока нет.')).not.toBeInTheDocument()
  expect(await screen.findByRole('alert')).toHaveTextContent('Сеть недоступна')
  fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
  expect(await screen.findByText('Приборов пока нет.')).toBeInTheDocument()
  expect(load).toHaveBeenCalledTimes(2)
})

it('requests page sizes and numbered pages on the server', async () => {
  const { props, load, page } = fixture()
  load.mockImplementation((_token, _garage, query) => Promise.resolve({ ...page, totalCount: 80, ...query }))
  render(<GarageMeterHistoryDialog {...props} />)
  const sizes = await screen.findByRole('group', { name: 'Количество приборов' })
  fireEvent.click(within(sizes).getByRole('button', { name: '10', exact: true }))
  await waitFor(() => expect(load).toHaveBeenLastCalledWith('token', 'garage', { offset: 0, limit: 10 }, expect.any(AbortSignal)))
  const nav = screen.getByRole('navigation', { name: 'Пагинация истории приборов' })
  fireEvent.click(within(nav).getByRole('button', { name: 'Страница 2', exact: true }))
  await waitFor(() => expect(load).toHaveBeenLastCalledWith('token', 'garage', { offset: 10, limit: 10 }, expect.any(AbortSignal)))
})

it('does not request protected history without permission and hides cancellation for read-only users', async () => {
  const { props, load } = fixture()
  const denied = { ...props, auth: { ...props.auth, user: { ...props.auth.user, permissions: [] } } }
  const view = render(<GarageMeterHistoryDialog {...denied} />)
  expect(screen.getByRole('alert')).toHaveTextContent('Нет права')
  expect(load).not.toHaveBeenCalled()
  view.rerender(<GarageMeterHistoryDialog {...props} auth={{ ...props.auth, user: { ...props.auth.user, permissions: ['payments.read'] } }} />)
  await screen.findByRole('table')
  expect(screen.queryByRole('button', { name: /Отменить замену на/ })).not.toBeInTheDocument()
})

it('ignores a stale response after changing the garage and aborts on unmount', async () => {
  const { props, load, page } = fixture()
  let finish!: (value: unknown) => void
  load.mockImplementationOnce(() => new Promise((resolve) => { finish = resolve })).mockResolvedValue({ ...page, items: [] })
  const view = render(<GarageMeterHistoryDialog {...props} />)
  const firstSignal = load.mock.calls[0][3] as AbortSignal
  view.rerender(<GarageMeterHistoryDialog {...props} garage={{ id: 'another', number: '6' }} />)
  await screen.findByText('Приборов пока нет.')
  await act(async () => finish(page))
  expect(firstSignal.aborted).toBe(true)
  expect(screen.queryByText('№ W-2')).not.toBeInTheDocument()
  const lastSignal = load.mock.calls.at(-1)![3] as AbortSignal
  view.unmount()
  expect(lastSignal.aborted).toBe(true)
})

it('validates the reason and cancels only after explicit confirmation with both versions', async () => {
  const { props, cancel, load, page } = fixture()
  load.mockResolvedValueOnce(page).mockResolvedValue({ ...page, items: [] })
  render(<GarageMeterHistoryDialog {...props} />)
  fireEvent.click(await screen.findByRole('button', { name: 'Отменить замену на прибор W-2' }))
  fireEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }))
  expect(screen.getByRole('alert')).toHaveTextContent('Проверьте причину и показание')
  expect(cancel).not.toHaveBeenCalled()
  fireEvent.change(screen.getByLabelText('Причина отмены замены'), { target: { value: ' Ошибочная запись ' } })
  fireEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }))
  await waitFor(() => expect(props.onChanged).toHaveBeenCalledOnce())
  expect(cancel).toHaveBeenCalledWith('token', 'new', { expectedDeviceVersion: 'dv1', expectedReadingVersion: 'rv1', accountingMonth: '2026-06-01', reason: 'Ошибочная запись' })
  expect(screen.queryByRole('form', { name: 'Подтверждение отмены замены' })).not.toBeInTheDocument()
})

it('keeps the draft after a rejected cancellation and uses refreshed versions on explicit retry', async () => {
  const { props, cancel, load, page, device } = fixture()
  cancel.mockRejectedValueOnce(new Error('Показание изменено')).mockResolvedValue({})
  load.mockResolvedValueOnce(page).mockResolvedValue({ ...page, items: [{ ...device, version: 'dv2', replacementReadingVersion: 'rv2' }] })
  render(<GarageMeterHistoryDialog {...props} />)
  fireEvent.click(await screen.findByRole('button', { name: /Отменить замену на/ }))
  fireEvent.change(screen.getByLabelText('Причина отмены замены'), { target: { value: 'Причина' } })
  fireEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Показание изменено')
  await waitFor(() => expect(load).toHaveBeenCalledTimes(2))
  await waitFor(() => expect(screen.getByRole('button', { name: 'Подтвердить отмену' })).toBeEnabled())
  expect(screen.getByLabelText('Причина отмены замены')).toHaveValue('Причина')
  fireEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }))
  await waitFor(() => expect(cancel).toHaveBeenCalledTimes(2))
  expect(cancel).toHaveBeenLastCalledWith('token', 'new', expect.objectContaining({ expectedDeviceVersion: 'dv2', expectedReadingVersion: 'rv2', reason: 'Причина' }))
})

it('blocks duplicate submits and closing during cancellation', async () => {
  const { props, cancel } = fixture()
  let finish!: () => void
  cancel.mockImplementation(() => new Promise<void>((resolve) => { finish = resolve }))
  render(<GarageMeterHistoryDialog {...props} />)
  fireEvent.click(await screen.findByRole('button', { name: /Отменить замену на/ }))
  fireEvent.change(screen.getByLabelText('Причина отмены замены'), { target: { value: 'Причина' } })
  const submit = screen.getByRole('button', { name: 'Подтвердить отмену' })
  fireEvent.click(submit)
  fireEvent.click(submit)
  expect(cancel).toHaveBeenCalledOnce()
  expect(screen.getByRole('button', { name: 'Закрыть историю приборов' })).toBeDisabled()
  fireEvent.keyDown(document, { key: 'Escape' })
  expect(props.onClose).not.toHaveBeenCalled()
  await act(async () => finish())
  expect(props.onChanged).toHaveBeenCalledOnce()
})

it('requires the actual old value for a legacy replacement and preserves thousandth precision', async () => {
  const { props, cancel, load, page, device } = fixture()
  load.mockResolvedValue({ ...page, items: [{ ...device, requiresRestoredValue: true }] })
  render(<GarageMeterHistoryDialog {...props} />)
  fireEvent.click(await screen.findByRole('button', { name: /Отменить замену на/ }))
  fireEvent.change(screen.getByLabelText('Причина отмены замены'), { target: { value: 'Старая ошибка' } })
  fireEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }))
  expect(cancel).not.toHaveBeenCalled()
  fireEvent.change(screen.getByLabelText('Показание восстанавливаемого прибора'), { target: { value: '123,4567' } })
  fireEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }))
  expect(cancel).not.toHaveBeenCalled()
  fireEvent.change(screen.getByLabelText('Показание восстанавливаемого прибора'), { target: { value: '123,456' } })
  fireEvent.click(screen.getByRole('button', { name: 'Подтвердить отмену' }))
  await waitFor(() => expect(cancel).toHaveBeenCalledWith('token', 'new', expect.objectContaining({ restoredCurrentValue: 123.456 })))
})

it('keeps the replacement on dismissal and prevents cancellation after a permission is revoked', async () => {
  const { props, cancel } = fixture()
  const view = render(<GarageMeterHistoryDialog {...props} />)
  fireEvent.click(await screen.findByRole('button', { name: /Отменить замену на/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Оставить замену' }))
  expect(screen.queryByLabelText('Причина отмены замены')).not.toBeInTheDocument()
  expect(cancel).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: /Отменить замену на/ }))
  view.rerender(<GarageMeterHistoryDialog {...props} auth={{ ...props.auth, user: { ...props.auth.user, permissions: [] } }} />)
  expect(screen.queryByLabelText('Причина отмены замены')).not.toBeInTheDocument()
  expect(screen.getByRole('alert')).toHaveTextContent('Нет права')
  expect(cancel).not.toHaveBeenCalled()
})
