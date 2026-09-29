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

it('does not load unrestricted garages and preserves selection across pagination, search and toggling', async () => {
  const getPage = vi.fn<NonNullable<DictionaryClient['getGaragesPage']>>(async (_token, _search, offset) => ({ items: [garage(offset ? '20' : '2')], totalCount: 20, offset: offset ?? 0, limit: 10 }))
  render(<Scope getPage={getPage} />)
  expect(getPage).not.toHaveBeenCalled()
  expect(screen.getByText('Тариф действует для всех гаражей.')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  fireEvent.click(await screen.findByRole('checkbox', { name: 'Выбрать гараж 2' }))
  fireEvent.click(screen.getByRole('button', { name: 'Страница 2' }))
  fireEvent.click(await screen.findByRole('checkbox', { name: 'Выбрать гараж 20' }))
  expect(screen.getByText('Выбрано: 2 из 100')).toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Поиск гаража'), { target: { value: '2' } })
  await waitFor(() => expect(getPage).toHaveBeenLastCalledWith('token', '2', 0, 10, false, 'number', 'asc', false, {}, expect.any(AbortSignal)))
  expect(await screen.findByRole('checkbox', { name: 'Выбрать гараж 2' })).toBeChecked()
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  expect(screen.getByText('Выбрано: 2 из 100')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Снять выбор' }))
  expect(screen.getByText('Выбрано: 0 из 100')).toBeInTheDocument()
})

it('supports loading, failure, retry and empty state without dropping persisted selection', async () => {
  const getPage = vi.fn().mockRejectedValueOnce(new Error('network')).mockResolvedValue({ items: [], totalCount: 0, offset: 0, limit: 10 })
  render(<Scope getPage={getPage} initialIds={['garage-85']} />)
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  expect(screen.getByRole('status', { name: 'Загрузка гаражей тарифа' })).toHaveAttribute('aria-live', 'polite')
  expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить гаражи')
  fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
  expect(await screen.findByText('Гаражи не найдены')).toBeInTheDocument()
  expect(screen.getByText('Выбрано: 1 из 100')).toBeInTheDocument()
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

it('blocks mutation when disabled and limits selection to 100 garages', async () => {
  const getPage = vi.fn(async () => ({ items: [garage('2'), garage('20')], totalCount: 2, offset: 0, limit: 10 }))
  const ids = ['garage-2', ...Array.from({ length: 99 }, (_, index) => `selected-${index}`)]
  const view = render(<Scope getPage={getPage} initialIds={ids} />)
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  expect(await screen.findByRole('checkbox', { name: 'Выбрать гараж 20' })).toBeDisabled()
  fireEvent.click(screen.getByRole('checkbox', { name: 'Выбрать гараж 2' }))
  expect(screen.getByRole('checkbox', { name: 'Выбрать гараж 20' })).toBeEnabled()
  view.rerender(<Scope getPage={getPage} initialIds={ids} disabled />)
  expect(screen.getByRole('checkbox', { name: 'Выбрать гараж 20' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Снять выбор' })).toBeDisabled()
})
