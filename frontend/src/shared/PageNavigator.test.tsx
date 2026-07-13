import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Pagination } from './PageNavigator'
import { clampPage, getVisiblePaginationItems } from './pageNavigatorModel'

describe('Pagination', () => {
  it('clamps page numbers and keeps a compact visible range', () => {
    expect(clampPage(0, 10)).toBe(1)
    expect(clampPage(12, 10)).toBe(10)
    expect(getVisiblePaginationItems(1, 10)).toEqual([1, 2, 3, 4, 5, 6, 'dots', 10])
    expect(getVisiblePaginationItems(5, 10)).toEqual([1, 'dots', 3, 4, 5, 6, 7, 'dots', 10])
    expect(getVisiblePaginationItems(10, 10)).toEqual([1, 'dots', 5, 6, 7, 8, 9, 10])
  })

  it('changes pages through arrows and numbered buttons', async () => {
    const user = userEvent.setup()
    const onPageChange = vi.fn()
    render(<Pagination currentPage={2} totalPages={5} onPageChange={onPageChange} />)

    expect(screen.getByRole('button', { name: 'Страница 2' })).toHaveAttribute('aria-current', 'page')
    await user.click(screen.getByRole('button', { name: 'Предыдущая страница' }))
    await user.click(screen.getByRole('button', { name: 'Страница 4' }))
    await user.click(screen.getByRole('button', { name: 'Следующая страница' }))

    expect(onPageChange.mock.calls).toEqual([[1], [4], [3]])
  })

  it('supports a clamped quick jump for large result sets', async () => {
    const user = userEvent.setup()
    const onPageChange = vi.fn()
    render(<Pagination currentPage={5} totalPages={140} showQuickJump quickJumpThreshold={100} onPageChange={onPageChange} />)

    const input = screen.getByRole('textbox', { name: 'Перейти к странице' })
    await user.clear(input)
    await user.type(input, '999')
    fireEvent.submit(input.closest('form')!)

    expect(onPageChange).toHaveBeenCalledWith(140)
    expect(input).toHaveValue('140')
  })

  it('disables every navigation action while data is loading', () => {
    render(<Pagination currentPage={2} totalPages={4} disabled onPageChange={vi.fn()} />)

    for (const button of screen.getAllByRole('button')) {
      expect(button).toBeDisabled()
    }
  })
})
