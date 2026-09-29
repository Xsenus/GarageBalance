import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import type { ChargeServiceSettingDto, DictionaryClient, GarageDto } from '../../services/dictionariesApi'
import { DictionaryApiError } from '../../services/dictionariesApi'
import type { GarageTariffAssignment, GarageTariffAssignmentsClient } from '../../services/garageTariffAssignmentsApi'
import { GarageTariffAssignmentsDialog } from './GarageTariffAssignmentsDialog'

const service = { id: 'service', name: 'Охрана', version: 'service-version' } as ChargeServiceSettingDto
const garage = (number: string) => ({ id: `garage-${number}`, number, isArchived: false } as GarageDto)
const row = (number = '85'): GarageTariffAssignment => ({ id: `assignment-${number}`, garageId: `garage-${number}`, garageNumber: number,
  serviceId: service.id, tariffId: 'individual-rate', calculationBase: 'fixed', rate: 200, tiers: [], effectiveFrom: '2026-09-01', effectiveTo: null,
  comment: null, isArchived: false, version: 'assignment-version' })
function fixture(initial: GarageTariffAssignment[] = []) {
  let rows = initial
  const client: GarageTariffAssignmentsClient = {
    getPage: vi.fn(async (_token, _serviceId, params) => {
      const visible = rows.filter((item) => params?.includeArchived || !item.isArchived)
      return { items: visible.slice(params?.offset ?? 0, (params?.offset ?? 0) + (params?.limit ?? 25)), totalCount: visible.length, offset: params?.offset ?? 0, limit: params?.limit ?? 25 }
    }),
    create: vi.fn(async (_token, _serviceId, request) => { const added = request.garageIds.map((id) => ({ ...row(id.replace('garage-', '')), rate: request.rate, effectiveFrom: request.effectiveFrom })); rows = [...rows, ...added]; return added }),
    update: vi.fn(async (_token, _serviceId, id, request) => { const saved = { ...rows.find((item) => item.id === id)!, rate: request.rate, version: 'new-version' }; rows = rows.map((item) => item.id === id ? saved : item); return saved }),
    archive: vi.fn(async (_token, _serviceId, id) => { const saved = { ...rows.find((item) => item.id === id)!, isArchived: true }; rows = rows.map((item) => item.id === id ? saved : item); return saved }),
  }
  const getGaragesPage = vi.fn(async () => ({ items: [garage('85'), garage('86')], totalCount: 2, offset: 0, limit: 25 }))
  const props = { accessToken: 'token', service, rate: 100, calculationBase: 'fixed', dictionaryClient: { getGaragesPage } as unknown as DictionaryClient,
    canWrite: true, onClose: vi.fn(), client }
  return { props, client, getGaragesPage }
}

it('creates selected assignments only after confirmation, then edits and archives with exact versions', async () => {
  const user = userEvent.setup()
  const { props, client } = fixture()
  render(<GarageTariffAssignmentsDialog {...props} />)
  await screen.findByText('Индивидуальные тарифы пока не назначены')
  await user.click(screen.getByRole('button', { name: 'Назначить тариф' }))
  await user.click(await screen.findByRole('checkbox', { name: 'Выбрать гараж 85' }))
  await user.click(screen.getByRole('checkbox', { name: 'Выбрать гараж 86' }))
  await user.clear(screen.getByLabelText('Ставка индивидуального тарифа'))
  await user.type(screen.getByLabelText('Ставка индивидуального тарифа'), '250,1234')
  await user.click(screen.getByRole('button', { name: 'Сохранить назначение' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Укажите причину')
  await user.type(screen.getByLabelText('Причина изменения индивидуального тарифа'), 'Разные условия оплаты')
  await user.click(screen.getByRole('button', { name: 'Сохранить назначение' }))
  expect(client.create).not.toHaveBeenCalled()
  expect(screen.getByRole('region', { name: 'Подтверждение индивидуального тарифа' })).toHaveTextContent('85, 86')
  await user.click(screen.getByRole('button', { name: 'Подтвердить' }))
  await screen.findByRole('button', { name: 'Изменить тариф гаража 85' })
  expect(screen.getByRole('button', { name: 'Закрыть индивидуальные тарифы' })).toHaveFocus()
  expect(client.create).toHaveBeenCalledWith('token', 'service', expect.objectContaining({ garageIds: ['garage-85', 'garage-86'], rate: 250.1234, serviceVersion: 'service-version' }), expect.any(AbortSignal))
  await user.click(screen.getByRole('button', { name: 'Изменить тариф гаража 85' }))
  await user.clear(screen.getByLabelText('Ставка индивидуального тарифа'))
  await user.type(screen.getByLabelText('Ставка индивидуального тарифа'), '300')
  await user.type(screen.getByLabelText('Причина изменения индивидуального тарифа'), 'Исправление')
  await user.click(screen.getByRole('button', { name: 'Сохранить назначение' }))
  await user.click(screen.getByRole('button', { name: 'Подтвердить' }))
  await screen.findByRole('button', { name: 'Отменить тариф гаража 86' })
  expect(screen.getByRole('button', { name: 'Закрыть индивидуальные тарифы' })).toHaveFocus()
  expect(client.update).toHaveBeenCalledWith('token', 'service', 'assignment-85', expect.objectContaining({ rate: 300, version: 'assignment-version', serviceVersion: 'service-version' }), expect.any(AbortSignal))
  await user.click(screen.getByRole('button', { name: 'Отменить тариф гаража 86' }))
  await user.type(screen.getByLabelText('Причина изменения индивидуального тарифа'), 'Возврат к общему тарифу')
  await user.click(screen.getByRole('button', { name: 'Отменить назначение' }))
  expect(client.archive).not.toHaveBeenCalled()
  await user.click(screen.getByRole('button', { name: 'Подтвердить' }))
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Отменить тариф гаража 86' })).not.toBeInTheDocument())
  expect(screen.getByRole('button', { name: 'Закрыть индивидуальные тарифы' })).toHaveFocus()
  expect(client.archive).toHaveBeenCalledWith('token', 'service', 'assignment-86', { version: 'assignment-version', reason: 'Возврат к общему тарифу' }, expect.any(AbortSignal))
  expect(screen.getByText(/история удалённых гаражей сохранены/)).toHaveAttribute('role', 'status')
  await user.click(screen.getByRole('checkbox', { name: 'Показывать отменённые' }))
  expect(await screen.findByText(/Отменён/)).toBeInTheDocument()
})

it('retries loading and gives a read-only user no mutation or garage-selection path', async () => {
  const user = userEvent.setup()
  const { props, client, getGaragesPage } = fixture([row()])
  vi.mocked(client.getPage).mockRejectedValueOnce(new Error('Сервер недоступен'))
  render(<GarageTariffAssignmentsDialog {...props} canWrite={false} />)
  expect(screen.getByRole('status', { name: 'Получаем индивидуальные тарифы' })).toBeInTheDocument()
  await user.click(await screen.findByRole('button', { name: 'Повторить загрузку' }))
  await screen.findByRole('rowheader', { name: '85' })
  expect(screen.queryByRole('button', { name: 'Назначить тариф' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Изменить тариф/ })).not.toBeInTheDocument()
  expect(getGaragesPage).not.toHaveBeenCalled()
  expect(client.create).not.toHaveBeenCalled()
})

it('aborts and ignores a stale list response when the access token changes', async () => {
  const { props, client } = fixture([row('86')])
  let complete!: (value: Awaited<ReturnType<typeof client.getPage>>) => void
  vi.mocked(client.getPage).mockImplementationOnce(() => new Promise((resolve) => { complete = resolve }))
  const view = render(<GarageTariffAssignmentsDialog {...props} />)
  const signal = vi.mocked(client.getPage).mock.calls[0][3]!
  expect(screen.queryByText('Индивидуальные тарифы пока не назначены')).not.toBeInTheDocument()
  view.rerender(<GarageTariffAssignmentsDialog {...props} accessToken="new-token" />)
  await screen.findByRole('rowheader', { name: '86' })
  expect(signal.aborted).toBe(true)
  await act(async () => complete({ items: [row('85')], totalCount: 1, offset: 0, limit: 25 }))
  expect(screen.queryByRole('rowheader', { name: '85' })).not.toBeInTheDocument()
})

it('shows a conflict without resubmitting and disables closing while a save is pending', async () => {
  const user = userEvent.setup()
  const { props, client } = fixture([row()])
  let reject!: (error: unknown) => void
  vi.mocked(client.archive).mockImplementationOnce(() => new Promise((_resolve, failure) => { reject = failure }))
  render(<GarageTariffAssignmentsDialog {...props} />)
  await user.click(await screen.findByRole('button', { name: 'Отменить тариф гаража 85' }))
  await user.type(screen.getByLabelText('Причина изменения индивидуального тарифа'), 'Отмена')
  await user.click(screen.getByRole('button', { name: 'Отменить назначение' }))
  await user.click(screen.getByRole('button', { name: 'Подтвердить' }))
  expect(screen.getByRole('button', { name: 'Закрыть индивидуальные тарифы' })).toBeDisabled()
  await user.keyboard('{Escape}')
  expect(props.onClose).not.toHaveBeenCalled()
  await act(async () => reject(new DictionaryApiError('concurrent_write_conflict', 'Обновите запись', 409)))
  expect(await screen.findByRole('alert')).toHaveTextContent('Вернитесь к списку')
  expect(client.archive).toHaveBeenCalledTimes(1)
  expect(screen.getByRole('button', { name: 'Закрыть индивидуальные тарифы' })).toBeEnabled()
})

it('retains garage selection across pages and searches, then sends both garages', async () => {
  const user = userEvent.setup()
  const { props, getGaragesPage, client } = fixture()
  getGaragesPage.mockImplementation(async (...args: unknown[]) => {
    const offset = args[2] as number
    return { items: [garage(offset ? '86' : '85')], totalCount: 26, offset, limit: 25 }
  })
  render(<GarageTariffAssignmentsDialog {...props} />)
  await user.click(await screen.findByRole('button', { name: 'Назначить тариф' }))
  await user.click(await screen.findByRole('checkbox', { name: 'Выбрать гараж 85' }))
  await user.click(within(screen.getByRole('navigation', { name: 'Страницы выбора гаражей' })).getByRole('button', { name: 'Страница 2' }))
  await user.click(await screen.findByRole('checkbox', { name: 'Выбрать гараж 86' }))
  expect(screen.getByText(/Выбрано: 2 из 100/)).toHaveTextContent('85, 86')
  fireEvent.change(screen.getByLabelText('Поиск гаража для тарифа'), { target: { value: '85' } })
  await waitFor(() => expect(getGaragesPage).toHaveBeenLastCalledWith('token', '85', 0, 25, false, 'number', 'asc', false, {}, expect.any(AbortSignal)))
  await user.type(screen.getByLabelText('Причина изменения индивидуального тарифа'), 'Выбранные гаражи')
  await user.click(screen.getByRole('button', { name: 'Сохранить назначение' }))
  await user.click(screen.getByRole('button', { name: 'Подтвердить' }))
  await waitFor(() => expect(client.create).toHaveBeenCalledWith('token', 'service', expect.objectContaining({ garageIds: ['garage-85', 'garage-86'] }), expect.any(AbortSignal)))
})

it('removes an off-page selection and refuses to save without a selected garage', async () => {
  const user = userEvent.setup()
  const { props, client, getGaragesPage } = fixture()
  getGaragesPage.mockImplementation(async (...args: unknown[]) => ({ items: [garage(args[1] ? '86' : '85')], totalCount: 1, offset: 0, limit: 25 }))
  render(<GarageTariffAssignmentsDialog {...props} />)
  await user.click(await screen.findByRole('button', { name: 'Назначить тариф' }))
  await user.click(await screen.findByRole('checkbox', { name: 'Выбрать гараж 85' }))
  await user.type(screen.getByLabelText('Поиск гаража для тарифа'), '86')
  await screen.findByRole('checkbox', { name: 'Выбрать гараж 86' })
  expect(screen.queryByRole('checkbox', { name: 'Выбрать гараж 85' })).not.toBeInTheDocument()
  await user.click(screen.getByRole('button', { name: 'Убрать гараж 85 из назначения' }))
  await user.click(screen.getByRole('button', { name: 'Сохранить назначение' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Выберите от 1 до 100 гаражей')
  expect(client.create).not.toHaveBeenCalled()
})

it('disables a draft and its confirmation if write permission is revoked', async () => {
  const user = userEvent.setup()
  const { props, client } = fixture([row()])
  const view = render(<GarageTariffAssignmentsDialog {...props} />)
  await user.click(await screen.findByRole('button', { name: 'Изменить тариф гаража 85' }))
  view.rerender(<GarageTariffAssignmentsDialog {...props} canWrite={false} />)
  expect(screen.getByLabelText('Ставка индивидуального тарифа')).toBeDisabled()
  expect(screen.getByLabelText('Причина изменения индивидуального тарифа')).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Сохранить назначение' })).toBeDisabled()
  view.rerender(<GarageTariffAssignmentsDialog {...props} />)
  await user.type(screen.getByLabelText('Причина изменения индивидуального тарифа'), 'Уточнение ставки')
  await user.click(screen.getByRole('button', { name: 'Сохранить назначение' }))
  view.rerender(<GarageTariffAssignmentsDialog {...props} canWrite={false} />)
  expect(screen.getByRole('button', { name: 'Подтвердить' })).toBeDisabled()
  expect(screen.getByRole('alert')).toHaveTextContent('Сохранение запрещено')
  expect(client.update).not.toHaveBeenCalled()
  await user.click(screen.getByRole('button', { name: 'Назад' }))
  await user.click(screen.getByRole('button', { name: 'К списку' }))
  expect(await screen.findByRole('rowheader', { name: '85' })).toBeInTheDocument()
})

it('validates meter tiers and submits precise bounds without general tariff ids', async () => {
  const user = userEvent.setup()
  const { props, client } = fixture()
  render(<GarageTariffAssignmentsDialog {...props} calculationBase="meter_electricity" initialTiers={[
    { id: 'general-first', name: 'Первая', upperBound: 100, rate: 1.2345, isCustom: false },
    { id: 'general-last', name: 'Последняя', upperBound: null, rate: 3.5678, isCustom: false },
  ]} />)
  await user.click(await screen.findByRole('button', { name: 'Назначить тариф' }))
  await user.click(await screen.findByRole('checkbox', { name: 'Выбрать гараж 85' }))
  expect(screen.getByLabelText('Граница ступени 2')).toBeDisabled()
  await user.clear(screen.getByLabelText('Граница ступени 1'))
  await user.type(screen.getByLabelText('Граница ступени 1'), '0')
  await user.type(screen.getByLabelText('Причина изменения индивидуального тарифа'), 'Тариф счётчика')
  await user.click(screen.getByRole('button', { name: 'Сохранить назначение' }))
  expect(await screen.findByRole('alert')).toBeInTheDocument()
  expect(client.create).not.toHaveBeenCalled()
  await user.clear(screen.getByLabelText('Граница ступени 1'))
  await user.type(screen.getByLabelText('Граница ступени 1'), '100,125')
  await user.click(screen.getByRole('button', { name: 'Добавить ступень' }))
  await user.click(screen.getByRole('button', { name: 'Удалить ступень 2' }))
  await user.click(screen.getByRole('button', { name: 'Сохранить назначение' }))
  await user.click(screen.getByRole('button', { name: 'Подтвердить' }))
  await waitFor(() => expect(client.create).toHaveBeenCalledWith('token', 'service', expect.objectContaining({ tiers: [
    { id: undefined, name: 'Первая', upperBound: 100.125, rate: 1.2345 },
    { id: undefined, name: 'Последняя', upperBound: undefined, rate: 3.5678 },
  ] }), expect.any(AbortSignal)))
})

it('retries garage loading and ignores an aborted search response', async () => {
  const user = userEvent.setup()
  const { props, getGaragesPage } = fixture()
  getGaragesPage.mockRejectedValueOnce(new Error('Не удалось получить гаражи'))
  render(<GarageTariffAssignmentsDialog {...props} />)
  await user.click(await screen.findByRole('button', { name: 'Назначить тариф' }))
  expect(screen.queryByText('Гаражи не найдены')).not.toBeInTheDocument()
  await user.click(await screen.findByRole('button', { name: 'Повторить загрузку' }))
  await screen.findByRole('checkbox', { name: 'Выбрать гараж 85' })
  let finish!: (value: Awaited<ReturnType<typeof getGaragesPage>>) => void
  getGaragesPage.mockImplementationOnce(() => new Promise((resolve) => { finish = resolve }))
  await user.type(screen.getByLabelText('Поиск гаража для тарифа'), '85')
  await waitFor(() => expect(getGaragesPage).toHaveBeenCalledTimes(3))
  const signal = (getGaragesPage.mock.calls as unknown as unknown[][])[2][9] as AbortSignal
  getGaragesPage.mockResolvedValueOnce({ items: [garage('86')], totalCount: 1, offset: 0, limit: 25 })
  fireEvent.change(screen.getByLabelText('Поиск гаража для тарифа'), { target: { value: '86' } })
  await screen.findByRole('checkbox', { name: 'Выбрать гараж 86' })
  expect(signal.aborted).toBe(true)
  await act(async () => finish({ items: [garage('85')], totalCount: 1, offset: 0, limit: 25 }))
  expect(screen.queryByRole('checkbox', { name: 'Выбрать гараж 85' })).not.toBeInTheDocument()
})

it('returns to the last valid assignment page after its final row is archived', async () => {
  const user = userEvent.setup()
  const { props, client } = fixture(Array.from({ length: 26 }, (_, index) => row(String(index + 1))))
  render(<GarageTariffAssignmentsDialog {...props} />)
  await user.click(await screen.findByRole('button', { name: 'Страница 2' }))
  await user.click(await screen.findByRole('button', { name: 'Отменить тариф гаража 26' }))
  await user.type(screen.getByLabelText('Причина изменения индивидуального тарифа'), 'Вернуть общий тариф')
  await user.click(screen.getByRole('button', { name: 'Отменить назначение' }))
  await user.click(screen.getByRole('button', { name: 'Подтвердить' }))
  await screen.findByRole('rowheader', { name: '1' })
  expect(screen.queryByText('Индивидуальные тарифы пока не назначены')).not.toBeInTheDocument()
  expect(client.getPage).toHaveBeenLastCalledWith('token', 'service', { offset: 0, limit: 25, includeArchived: false }, expect.any(AbortSignal))
})

it('keeps the confirmed draft when the server rejects an incompatible calculation period', async () => {
  const user = userEvent.setup()
  const { props, client } = fixture()
  vi.mocked(client.create).mockRejectedValueOnce(new DictionaryApiError('garage_tariff_calculation_base_conflict', 'Ограничьте индивидуальный тариф периодом с одной базой расчёта.', 400))
  render(<GarageTariffAssignmentsDialog {...props} />)
  await user.click(await screen.findByRole('button', { name: 'Назначить тариф' }))
  await user.click(await screen.findByRole('checkbox', { name: 'Выбрать гараж 85' }))
  await user.type(screen.getByLabelText('Причина изменения индивидуального тарифа'), 'Индивидуальные условия')
  await user.click(screen.getByRole('button', { name: 'Сохранить назначение' }))
  await user.click(screen.getByRole('button', { name: 'Подтвердить' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('периодом с одной базой расчёта')
  expect(client.create).toHaveBeenCalledTimes(1)
  await user.click(screen.getByRole('button', { name: 'Назад' }))
  expect(screen.getByLabelText('Причина изменения индивидуального тарифа')).toHaveValue('Индивидуальные условия')
  expect(await screen.findByRole('checkbox', { name: 'Выбрать гараж 85' })).toBeChecked()
})
