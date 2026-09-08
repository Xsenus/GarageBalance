// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { NewExpensePrototypeDialog } from './FinancePanel'

describe('NewExpensePrototypeDialog', () => {
  it('defaults a cash payout to the unallocated pool and submits a selected fund', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn().mockResolvedValue(null)
    render(<NewExpensePrototypeDialog
      availableAmounts={[1000, 1000]}
      expenseTypes={[{ id: 'expense-1', name: 'Ремонт', code: 'repair', isSystem: false, isArchived: false }]}
      fundOptions={[{ id: 'fund-1', name: 'Ремонтный фонд', balance: 500 }]}
      preset={{ expensePaymentSource: 'cash' }}
      suppliers={[]}
      onClose={vi.fn()}
      onSubmit={onSubmit}
    />)

    expect(screen.getByRole('combobox', { name: 'Фонд расходования' })).toHaveTextContent('Общий нераспределённый пул')
    await user.click(screen.getByRole('combobox', { name: 'Фонд расходования' }))
    await user.click(screen.getByRole('option', { name: /Ремонтный фонд/ }))
    await user.type(screen.getByRole('textbox', { name: 'Сумма выплаты' }), '100')
    await user.click(screen.getByRole('button', { name: 'Провести' }))

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
      expensePaymentSource: 'cash',
      expenseFundId: 'fund-1',
    }))
  })
})
