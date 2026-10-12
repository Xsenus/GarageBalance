import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TablePagination } from './TablePagination'

describe('TablePagination', () => {
  it('shows the visible range and navigates by numbered pages', () => {
    const onPageChange = vi.fn()
    render(<TablePagination ariaLabel="Пагинация тестовой таблицы" totalCount={80} offset={25} limit={25} visibleCount={25} onPageChange={onPageChange} onPageSizeChange={vi.fn()} />)

    expect(screen.getByRole('status')).toHaveTextContent('Показано 26-50 из 80')
    expect(screen.getByText('Страница 2 из 4 · Найдено: 80')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Страница 2' })).toHaveAttribute('aria-current', 'page')
    fireEvent.click(screen.getByRole('button', { name: 'Страница 3' }))
    expect(onPageChange).toHaveBeenCalledWith(3)
  })

  it('changes page size and renders the empty range consistently', () => {
    const onPageSizeChange = vi.fn()
    render(<TablePagination ariaLabel="Пагинация пустой таблицы" totalCount={0} offset={0} limit={25} visibleCount={0} onPageChange={vi.fn()} onPageSizeChange={onPageSizeChange} />)

    expect(screen.getByRole('status')).toHaveTextContent('Показано 0-0 из 0')
    expect(screen.getByRole('group', { name: 'Количество строк' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '50' }))
    expect(onPageSizeChange).toHaveBeenCalledWith(50)
  })

  it('keeps page-size and numbered navigation together on the left', () => {
    render(<TablePagination ariaLabel="Пагинация таблицы" totalCount={40} offset={0} limit={10} visibleCount={10} onPageChange={vi.fn()} onPageSizeChange={vi.fn()} />)

    const navigation = screen.getByRole('navigation', { name: 'Пагинация таблицы' })
    expect(navigation.firstElementChild).toHaveClass('pagination-primary')
    expect(screen.getByRole('button', { name: '10' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
  })

  it('uses the same controls and full status for a small table', () => {
    const onPageSizeChange = vi.fn()
    render(<TablePagination ariaLabel="Пагинация небольшой таблицы" totalCount={40} offset={10} limit={10} visibleCount={10} statusText="Тестовый список" onPageChange={vi.fn()} onPageSizeChange={onPageSizeChange} />)

    const navigation = screen.getByRole('navigation', { name: 'Пагинация небольшой таблицы' })
    expect(navigation).toHaveClass('dictionary-pagination')
    expect(screen.getByRole('status')).toHaveTextContent('Показано 11-20 из 40')
    expect(screen.getByText('Страница 2 из 4 · Найдено: 40 · Тестовый список')).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Количество строк' })).toBeInTheDocument()
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
    for (const size of ['10', '25', '50', '100']) expect(screen.getByRole('button', { name: size, exact: true })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '25', exact: true }))
    expect(onPageSizeChange).toHaveBeenCalledWith(25)
  })

  it('disables size changes and navigation together while loading', () => {
    const onPageChange = vi.fn()
    const onPageSizeChange = vi.fn()
    render(<TablePagination ariaLabel="Пагинация загрузки" totalCount={40} offset={0} limit={10} visibleCount={10} disabled onPageChange={onPageChange} onPageSizeChange={onPageSizeChange} />)
    for (const button of screen.getAllByRole('button')) expect(button).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: '50', exact: true }))
    fireEvent.click(screen.getByRole('button', { name: 'Страница 2', exact: true }))
    expect(onPageChange).not.toHaveBeenCalled()
    expect(onPageSizeChange).not.toHaveBeenCalled()
  })
})
