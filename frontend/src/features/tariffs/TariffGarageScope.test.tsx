import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { useMemo, useState } from 'react'
import { expect, it, vi } from 'vitest'
import type { DictionaryClient, GarageDto } from '../../services/dictionariesApi'
import { TariffGarageScope } from './TariffGarageScope'

const garage = (number: string) => ({ id: `garage-${number}`, number } as GarageDto)
function Scope({ getPage, initialIds = [], disabled = false }: { getPage: NonNullable<DictionaryClient['getGaragesPage']>; initialIds?: string[]; disabled?: boolean }) {
  const [restricted, setRestricted] = useState(false)
  const [ids, setIds] = useState(initialIds)
  const client = useMemo(() => ({ getGaragesPage: getPage }) as DictionaryClient, [getPage])
  return <TariffGarageScope accessToken="token" dictionaryClient={client} restricted={restricted} garageIds={ids} disabled={disabled}
    onRestrictedChange={setRestricted} onSelectionChange={setIds} />
}

it('does not load unrestricted garages and preserves selection across appended pages, search and toggling', async () => {
  const getPage = vi.fn<NonNullable<DictionaryClient['getGaragesPage']>>(async (_token, _search, offset) => ({ items: [garage(offset ? '20' : '2')], totalCount: 20, offset: offset ?? 0, limit: 10 }))
  render(<Scope getPage={getPage} />)
  expect(getPage).not.toHaveBeenCalled()
  expect(screen.getByText('Тариф действует для всех гаражей.')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  fireEvent.click(await screen.findByRole('checkbox', { name: 'Выбрать гараж 2' }))
  expect(screen.queryByRole('navigation', { name: 'Гаражи тарифа' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Показать ещё гаражи' }))
  fireEvent.click(await screen.findByRole('checkbox', { name: 'Выбрать гараж 20' }))
  expect(screen.getByText('Выбрано: 2')).toBeInTheDocument()
  expect(screen.getByRole('checkbox', { name: 'Выбрать гараж 2' })).toBeChecked()
  fireEvent.change(screen.getByLabelText('Поиск гаража'), { target: { value: '2' } })
  await waitFor(() => expect(getPage).toHaveBeenLastCalledWith('token', '2', 0, 48, false, 'number', 'asc', false, {}, expect.any(AbortSignal)))
  expect(await screen.findByRole('checkbox', { name: 'Выбрать гараж 2' })).toBeChecked()
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  expect(screen.getByText('Выбрано: 2')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Снять всё' }))
  expect(screen.getByText('Выбрано: 0')).toBeInTheDocument()
})

it('supports loading, failure, retry and empty state without dropping persisted selection', async () => {
  const getPage = vi.fn().mockRejectedValueOnce(new Error('network')).mockResolvedValue({ items: [], totalCount: 0, offset: 0, limit: 10 })
  render(<Scope getPage={getPage} initialIds={['garage-85']} />)
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  expect(screen.getByRole('status', { name: 'Загрузка гаражей тарифа' })).toHaveAttribute('aria-live', 'polite')
  expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить гаражи')
  fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
  expect(await screen.findByText('Гаражи не найдены')).toBeInTheDocument()
  expect(screen.getByText('Выбрано: 1')).toBeInTheDocument()
})

it('rejects late responses after changing the search or closing the selector', async () => {
  let resolveOld!: (page: { items: GarageDto[]; totalCount: number; offset: number; limit: number }) => void
  const getPage = vi.fn().mockImplementationOnce(() => new Promise((resolve) => { resolveOld = resolve })).mockResolvedValue({ items: [garage('20')], totalCount: 1, offset: 0, limit: 10 })
  const view = render(<Scope getPage={getPage} />)
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  await waitFor(() => expect(getPage).toHaveBeenCalledOnce())
  fireEvent.change(screen.getByLabelText('Поиск гаража'), { target: { value: '20' } })
  await screen.findByRole('checkbox', { name: 'Выбрать гараж 20' })
  await act(async () => resolveOld({ items: [garage('2')], totalCount: 1, offset: 0, limit: 10 }))
  expect(screen.queryByRole('checkbox', { name: 'Выбрать гараж 2' })).not.toBeInTheDocument()
  expect(getPage.mock.calls[0][9].aborted).toBe(true)
  view.unmount()
  expect(getPage.mock.calls[1][9].aborted).toBe(true)
})

it('allows selection above 100 garages and blocks mutation when disabled', async () => {
  const getPage = vi.fn(async () => ({ items: [garage('2'), garage('20')], totalCount: 2, offset: 0, limit: 10 }))
  const ids = ['garage-2', ...Array.from({ length: 99 }, (_, index) => `selected-${index}`)]
  const view = render(<Scope getPage={getPage} initialIds={ids} />)
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  expect(await screen.findByRole('checkbox', { name: 'Выбрать гараж 20' })).toBeEnabled()
  fireEvent.click(screen.getByRole('checkbox', { name: 'Выбрать гараж 20' }))
  expect(screen.getByText('Выбрано: 101')).toBeInTheDocument()
  expect(screen.getByRole('checkbox', { name: 'Выбрать гараж 20' })).toBeEnabled()
  view.rerender(<Scope getPage={getPage} initialIds={ids} disabled />)
  expect(screen.getByRole('checkbox', { name: 'Выбрать гараж 20' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Снять всё' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Выделить всё' })).toBeDisabled()
})

it('selects every active garage across pages independently of search and sorts selected garages first', async () => {
  const all = Array.from({ length: 125 }, (_, index) => garage(String(index + 1)))
  const getPage = vi.fn(async (_token, search, offset = 0, limit = 48) => ({
    items: search ? [garage('20')] : all.slice(offset, offset + limit), totalCount: search ? 1 : all.length, offset, limit,
  }))
  render(<Scope getPage={getPage} initialIds={['garage-20', 'garage-2']} />)
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  await screen.findByRole('checkbox', { name: 'Выбрать гараж 1' })
  expect(screen.getAllByRole('checkbox').slice(1, 4).map((item) => item.getAttribute('aria-label')))
    .toEqual(['Выбрать гараж 2', 'Выбрать гараж 20', 'Выбрать гараж 1'])
  fireEvent.change(screen.getByLabelText('Поиск гаража'), { target: { value: '20' } })
  await waitFor(() => expect(getPage).toHaveBeenLastCalledWith('token', '20', 0, 48, false, 'number', 'asc', false, {}, expect.any(AbortSignal)))
  fireEvent.click(screen.getByRole('button', { name: 'Выделить всё' }))
  await screen.findByText('Выбрано: 125')
  expect(getPage).toHaveBeenCalledWith('token', undefined, 100, 100, false, 'number', 'asc', false, {}, expect.any(AbortSignal))
  fireEvent.click(screen.getByRole('button', { name: 'Снять всё' }))
  expect(screen.getByText('Выбрано: 0')).toBeInTheDocument()
})

it('shows persisted selections from later pages first without expanding the visible page', async () => {
  const all = Array.from({ length: 165 }, (_, index) => garage(String(index + 1)))
  const getPage = vi.fn(async (_token, _search, offset = 0, limit = 48) => ({ items: all.slice(offset, offset + limit), totalCount: all.length, offset, limit }))
  render(<Scope getPage={getPage} initialIds={['garage-165', 'garage-160', 'garage-2']} />)
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  expect(await screen.findByRole('checkbox', { name: 'Выбрать гараж 165' })).toBeChecked()
  expect(screen.getAllByRole('checkbox').slice(1, 5).map((item) => item.getAttribute('aria-label')))
    .toEqual(['Выбрать гараж 2', 'Выбрать гараж 160', 'Выбрать гараж 165', 'Выбрать гараж 1'])
  expect(screen.getAllByRole('checkbox')).toHaveLength(49)
  expect(getPage).toHaveBeenCalledWith('token', undefined, 148, 100, false, 'number', 'asc', false, {}, expect.any(AbortSignal))
})

it('retries failed selected-reference loading and aborts a stale selection lookup on search', async () => {
  let resolve!: (value: unknown) => void
  const getPage = vi.fn().mockResolvedValueOnce({ items: [garage('2')], totalCount: 2 })
    .mockRejectedValueOnce(new Error('offline'))
    .mockResolvedValueOnce({ items: [garage('2')], totalCount: 2 })
    .mockImplementationOnce(() => new Promise((done) => { resolve = done }))
    .mockResolvedValue({ items: [garage('2')], totalCount: 1 })
  render(<Scope getPage={getPage} initialIds={['garage-165']} />)
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('выбранные гаражи')
  fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
  await waitFor(() => expect(getPage).toHaveBeenCalledTimes(4))
  fireEvent.change(screen.getByLabelText('Поиск гаража'), { target: { value: '2' } })
  await act(async () => resolve({ items: [garage('165')], totalCount: 2 }))
  expect(getPage.mock.calls[3][9].aborted).toBe(true)
  await screen.findByRole('checkbox', { name: 'Выбрать гараж 2' })
  expect(screen.queryByRole('checkbox', { name: 'Выбрать гараж 165' })).not.toBeInTheDocument()
  expect(screen.getByText('Выбрано: 1')).toBeInTheDocument()
})

it('preserves selection on partial failure and supports retry without duplicate requests', async () => {
  const getPage = vi.fn().mockResolvedValueOnce({ items: [garage('2')], totalCount: 2 })
    .mockResolvedValueOnce({ items: [garage('2')], totalCount: 2 }).mockRejectedValueOnce(new Error('offline'))
    .mockResolvedValue({ items: [garage('2'), garage('20')], totalCount: 2 })
  render(<Scope getPage={getPage} initialIds={['garage-2']} />)
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  await screen.findByRole('checkbox', { name: 'Выбрать гараж 2' })
  fireEvent.click(screen.getByRole('button', { name: 'Выделить всё' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('прежний выбор сохранён')
  expect(screen.getByText('Выбрано: 1')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Выделить всё' }))
  await screen.findByText('Выбрано: 2')
})

it('clearing selection cancels select-all and rejects a late response', async () => {
  let resolve!: (value: unknown) => void
  const getPage = vi.fn().mockResolvedValueOnce({ items: [garage('2')], totalCount: 1 })
    .mockImplementationOnce(() => new Promise((done) => { resolve = done }))
  render(<Scope getPage={getPage} initialIds={['garage-2']} />)
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  await screen.findByRole('checkbox', { name: 'Выбрать гараж 2' })
  fireEvent.click(screen.getByRole('button', { name: 'Выделить всё' }))
  expect(screen.getByRole('button', { name: 'Выделить всё' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Снять всё' }))
  await act(async () => resolve({ items: [garage('2')], totalCount: 1 }))
  expect(getPage.mock.calls[1][9].aborted).toBe(true)
  expect(screen.getByText('Выбрано: 0')).toBeInTheDocument()
})

it('rejects an incomplete select-all page and retains the previous selection', async () => {
  const getPage = vi.fn().mockResolvedValueOnce({ items: [garage('2')], totalCount: 1 })
    .mockResolvedValueOnce({ items: [], totalCount: 2 })
  render(<Scope getPage={getPage} initialIds={['garage-2']} />)
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  await screen.findByRole('checkbox', { name: 'Выбрать гараж 2' })
  fireEvent.click(screen.getByRole('button', { name: 'Выделить всё' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('прежний выбор сохранён')
  expect(screen.getByText('Выбрано: 1')).toBeInTheDocument()
})

it('aborts select-all when authentication changes and ignores a late response', async () => {
  let resolve!: (value: unknown) => void
  const getPage = vi.fn().mockResolvedValueOnce({ items: [garage('2')], totalCount: 1 })
    .mockImplementationOnce(() => new Promise((done) => { resolve = done }))
    .mockResolvedValue({ items: [garage('2')], totalCount: 1 })
  const client = { getGaragesPage: getPage } as unknown as DictionaryClient
  const selection = vi.fn()
  const props = { dictionaryClient: client, restricted: true, garageIds: ['garage-2'], disabled: false,
    onRestrictedChange: vi.fn(), onSelectionChange: selection }
  const view = render(<TariffGarageScope {...props} accessToken="old-token" />)
  await screen.findByRole('checkbox', { name: 'Выбрать гараж 2' })
  fireEvent.click(screen.getByRole('button', { name: 'Выделить всё' }))
  view.rerender(<TariffGarageScope {...props} accessToken="new-token" />)
  await act(async () => resolve({ items: [garage('20')], totalCount: 1 }))
  expect(getPage.mock.calls[1][9].aborted).toBe(true)
  expect(selection).not.toHaveBeenCalled()
})

it('appends on scrolling without duplicate garages and retains loaded items on retry', async () => {
  const getPage = vi.fn().mockResolvedValueOnce({ items: [garage('2')], totalCount: 3, offset: 0, limit: 48 }).mockRejectedValueOnce(new Error('network'))
    .mockResolvedValue({ items: [garage('2'), garage('20')], totalCount: 3, offset: 48, limit: 48 })
  render(<Scope getPage={getPage} />)
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  await screen.findByRole('checkbox', { name: 'Выбрать гараж 2' })
  const region = screen.getByRole('region', { name: 'Список гаражей тарифа' })
  Object.defineProperties(region, { scrollHeight: { value: 500 }, clientHeight: { value: 300 }, scrollTop: { value: 180 } })
  fireEvent.scroll(region)
  expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить гаражи')
  expect(screen.getByRole('checkbox', { name: 'Выбрать гараж 2' })).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
  await screen.findByRole('checkbox', { name: 'Выбрать гараж 20' })
  expect(screen.getAllByRole('checkbox', { name: 'Выбрать гараж 2', exact: true })).toHaveLength(1)
  expect(getPage.mock.calls.slice(1).map((call) => call[2])).toEqual([48, 48])
})
