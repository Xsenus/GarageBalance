import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TablePagination } from './TablePagination'

describe('TablePagination', () => {
  it('shows the visible range and navigates by numbered pages', () => {
    const onPageChange = vi.fn()
    render(<TablePagination ariaLabel="Пагинация тестовой таблицы" totalCount={80} offset={25} limit={25} visibleCount={25} onPageChange={onPageChange} onPageSizeChange={vi.fn()} />)

    expect(screen.getByRole('status')).toHaveTextContent('Показано 26-50 из 80')
    expect(screen.getByRole('button', { name: 'Страница 2' })).toHaveAttribute('aria-current', 'page')
    fireEvent.click(screen.getByRole('button', { name: 'Страница 3' }))
    expect(onPageChange).toHaveBeenCalledWith(3)
  })

  it('changes page size and renders the empty range consistently', () => {
    const onPageSizeChange = vi.fn()
    render(<TablePagination ariaLabel="Пагинация пустой таблицы" totalCount={0} offset={0} limit={25} visibleCount={0} onPageChange={vi.fn()} onPageSizeChange={onPageSizeChange} />)

    expect(screen.getByRole('status')).toHaveTextContent('Показано 0-0 из 0')
    fireEvent.change(screen.getByRole('combobox', { name: 'Количество строк' }), { target: { value: '50' } })
    expect(onPageSizeChange).toHaveBeenCalledWith(50)
  })
})
