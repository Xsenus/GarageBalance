import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ReportPeriodQuickSelect } from './ReportPeriodQuickSelect'

describe('ReportPeriodQuickSelect', () => {
  it.each(['date', 'month'] as const)('selects the previous month by keyboard in %s mode and follows reference date changes', async (mode) => {
    const user = userEvent.setup()
    const onSelect = vi.fn()
    const props = { mode, valueFrom: '', valueTo: '', referenceDate: '2026-01-05', onSelect }
    const { rerender } = render(<ReportPeriodQuickSelect {...props} />)
    const previousMonth = screen.getByRole('button', { name: 'Предыдущий месяц' })
    expect(previousMonth).toHaveAttribute('aria-pressed', 'false')
    previousMonth.focus()
    await user.keyboard('{Enter}')
    expect(onSelect).toHaveBeenLastCalledWith({ monthFrom: '2025-12', monthTo: '2025-12', dateFrom: '2025-12-01', dateTo: '2025-12-31' })

    rerender(<ReportPeriodQuickSelect {...props} valueFrom={mode === 'date' ? '2025-12-01' : '2025-12'} valueTo={mode === 'date' ? '2025-12-31' : '2025-12'} />)
    expect(previousMonth).toHaveAttribute('aria-pressed', 'true')
    rerender(<ReportPeriodQuickSelect {...props} referenceDate="2024-03-15" />)
    await user.click(previousMonth)
    expect(onSelect).toHaveBeenLastCalledWith({ monthFrom: '2024-02', monthTo: '2024-02', dateFrom: '2024-02-01', dateTo: '2024-02-29' })
  })

  it('announces and applies each quick period for date filters', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()

    render(
      <ReportPeriodQuickSelect
        mode="date"
        valueFrom="2026-01-01"
        valueTo="2026-12-31"
        referenceDate="2026-07-28"
        onSelect={onSelect}
      />,
    )

    expect(screen.getByRole('group', { name: 'Быстрый выбор периода' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Текущий год' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Текущий месяц' })).toHaveAttribute('aria-pressed', 'false')

    await user.click(screen.getByRole('button', { name: 'Текущий месяц' }))
    await user.click(screen.getByRole('button', { name: 'Предыдущий год' }))

    expect(onSelect).toHaveBeenNthCalledWith(1, {
      monthFrom: '2026-07',
      monthTo: '2026-07',
      dateFrom: '2026-07-01',
      dateTo: '2026-07-31',
    })
    expect(onSelect).toHaveBeenNthCalledWith(2, {
      monthFrom: '2025-01',
      monthTo: '2025-12',
      dateFrom: '2025-01-01',
      dateTo: '2025-12-31',
    })
  })

  it('marks a selected month range and keeps an optional layout class', () => {
    render(
      <ReportPeriodQuickSelect
        mode="month"
        valueFrom="2025-01"
        valueTo="2025-12"
        referenceDate="2026-07-28"
        className="financial-report-periods"
        onSelect={vi.fn()}
      />,
    )

    expect(screen.getByRole('group', { name: 'Быстрый выбор периода' })).toHaveClass('financial-report-periods')
    expect(screen.getByRole('button', { name: 'Предыдущий год' })).toHaveAttribute('aria-pressed', 'true')
  })
})
