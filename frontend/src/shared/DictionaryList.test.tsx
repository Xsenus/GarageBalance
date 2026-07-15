import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { DictionaryList, type DictionaryListItem } from './DictionaryList'

function createItems(count: number, overrides: Partial<DictionaryListItem> = {}): DictionaryListItem[] {
  return Array.from({ length: count }, (_, index) => ({
    id: `item-${index + 1}`,
    title: `Запись ${index + 1}`,
    meta: `Описание ${index + 1}`,
    ...overrides,
  }))
}

describe('DictionaryList', () => {
  it('announces an empty list without rendering actions', () => {
    render(<DictionaryList items={[]} emptyText="Записей пока нет" />)

    expect(screen.getByRole('status')).toHaveTextContent('Записей пока нет')
    expect(screen.queryByRole('list')).not.toBeInTheDocument()
  })

  it('keeps a long list compact and expands and collapses it accessibly', async () => {
    const user = userEvent.setup()
    render(<DictionaryList items={createItems(7)} emptyText="Пусто" />)

    expect(screen.getAllByRole('listitem')).toHaveLength(5)
    const toggle = screen.getByRole('button', { name: 'Показать все записи' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByRole('status')).toHaveTextContent('Показано 5 из 7 записей')

    await user.click(toggle)
    expect(screen.getAllByRole('listitem')).toHaveLength(7)
    expect(screen.getByRole('button', { name: 'Свернуть список' })).toHaveAttribute('aria-expanded', 'true')

    await user.click(screen.getByRole('button', { name: 'Свернуть список' }))
    expect(screen.getAllByRole('listitem')).toHaveLength(5)
  })

  it('opens an item once and disables the action for the active item', async () => {
    const user = userEvent.setup()
    const onOpen = vi.fn()
    render(<DictionaryList items={[
      ...createItems(1, { onOpen, openLabel: 'Открыть первую запись' }),
      { ...createItems(1, { onOpen })[0], id: 'active', title: 'Активная запись', isActive: true },
    ]} emptyText="Пусто" />)

    await user.click(screen.getByRole('button', { name: 'Открыть первую запись' }))
    expect(onOpen).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Открыть Активная запись' })).toBeDisabled()
    expect(screen.getByText('Открыто')).toBeInTheDocument()
  })

  it('requires an archive reason, preserves cancel semantics and passes the trimmed reason', async () => {
    const user = userEvent.setup()
    const onArchive = vi.fn().mockResolvedValue(undefined)
    render(<DictionaryList items={createItems(1, { onArchive, archiveLabel: 'Архивировать первую запись' })} emptyText="Пусто" />)

    const archiveButton = screen.getByRole('button', { name: 'Архивировать первую запись' })
    await user.click(archiveButton)
    let dialog = screen.getByRole('dialog', { name: 'Подтвердите архивирование' })
    const reason = within(dialog).getByLabelText('Причина архивирования')
    expect(within(dialog).getByRole('button', { name: 'Архивировать запись' })).toBeDisabled()

    await user.type(reason, '   ')
    expect(within(dialog).getByRole('button', { name: 'Архивировать запись' })).toBeDisabled()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(onArchive).not.toHaveBeenCalled()
    expect(archiveButton).toHaveFocus()

    await user.click(archiveButton)
    dialog = screen.getByRole('dialog', { name: 'Подтвердите архивирование' })
    await user.type(within(dialog).getByLabelText('Причина архивирования'), '  Дублирующая запись  ')
    await user.click(within(dialog).getByRole('button', { name: 'Архивировать запись' }))

    await waitFor(() => expect(onArchive).toHaveBeenCalledWith('Дублирующая запись'))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
})
