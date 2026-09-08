// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { NewExpensePrototypeDialog } from './FinancePanel'
import { FinanceApiError } from '../../services/financeApi'
import type { SupplierDto } from '../../services/dictionariesApi'

describe('NewExpensePrototypeDialog', () => {
  it('shows a fund loading failure and retries while retaining the default unallocated selection', async () => {
    const user = userEvent.setup()
    const getFundOptions = vi.fn().mockRejectedValueOnce(new Error('Список фондов недоступен')).mockResolvedValue([{ id: 'fund', name: 'Доступный фонд', allowOperations: true }])
    render(<NewExpensePrototypeDialog availableAmounts={[1000, 1000]} expenseTypes={[]} fundsClient={{ getFundOptions }} accessToken="token" preset={{ expensePaymentSource: 'cash' }} suppliers={[]} onClose={vi.fn()} onSubmit={vi.fn()} />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Список фондов недоступен')
    expect(screen.getByRole('combobox', { name: 'Фонд расходования' })).toHaveTextContent('Общий нераспределённый пул')
    await user.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
    await waitFor(() => expect(getFundOptions).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    await user.click(screen.getByRole('combobox', { name: 'Фонд расходования' }))
    expect(screen.getByRole('option', { name: 'Доступный фонд' })).toBeInTheDocument()
  })

  it.each(['amount', 'fund', 'source'] as const)('resets the server fund warning and consent when changing %s', async (changedField) => {
    const user = userEvent.setup()
    const onSubmit = vi.fn().mockRejectedValueOnce(new FinanceApiError('fund_balance_insufficient', 'Недостаточно средств в фонде.', 400)).mockResolvedValue(null)
    const supplier = { id: 'supplier', name: 'Поставщик', expenseTypeId: 'expense', expenseFundId: 'bank-fund', expenseFundName: 'Банковский фонд', expenseFundBalance: 1000 } as SupplierDto
    render(<NewExpensePrototypeDialog availableAmounts={[1000, 1000]} expenseTypes={[{ id: 'expense', name: 'Ремонт', code: 'repair', isSystem: false, isArchived: false }]} fundsClient={{ getFundOptions: vi.fn().mockResolvedValue([{ id: 'fund', name: 'Фонд', allowOperations: true }]) }} accessToken="token" preset={{ expensePaymentSource: 'cash' }} suppliers={[supplier]} onClose={vi.fn()} onSubmit={onSubmit} />)
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Фонд расходования' })).toBeEnabled())
    await user.click(screen.getByRole('combobox', { name: 'Фонд расходования' }))
    await user.click(screen.getByRole('option', { name: 'Фонд', exact: true }))
    await user.type(screen.getByRole('textbox', { name: 'Сумма выплаты' }), '100')
    await user.click(screen.getByRole('button', { name: 'Провести' }))
    await user.click(await screen.findByRole('checkbox', { name: 'Подтвердить отрицательный остаток фонда' }))
    if (changedField === 'amount') {
      await user.clear(screen.getByRole('textbox', { name: 'Сумма выплаты' }))
      await user.type(screen.getByRole('textbox', { name: 'Сумма выплаты' }), '101')
    } else if (changedField === 'fund') {
      await user.click(screen.getByRole('combobox', { name: 'Фонд расходования' }))
      await user.click(screen.getByRole('option', { name: 'Общий нераспределённый пул' }))
    } else {
      await user.click(screen.getByRole('combobox', { name: 'Источник выплаты' }))
      await user.click(screen.getByRole('option', { name: 'Банк · поставщику' }))
    }
    expect(screen.queryByRole('checkbox', { name: 'Подтвердить отрицательный остаток фонда' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Провести' }))
    expect(onSubmit).toHaveBeenLastCalledWith(expect.objectContaining({ confirmNegativeFundBalance: false }))
  })

  it('does not offer fund consent for an insufficient cash balance', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn().mockRejectedValue(new FinanceApiError('cash_balance_insufficient', 'Недостаточно денег в кассе.', 400))
    render(<NewExpensePrototypeDialog availableAmounts={[1000, 0]} expenseTypes={[{ id: 'expense', name: 'Ремонт', code: 'repair', isSystem: false, isArchived: false }]} fundsClient={{ getFundOptions: vi.fn().mockResolvedValue([]) }} accessToken="token" preset={{ expensePaymentSource: 'cash' }} suppliers={[]} onClose={vi.fn()} onSubmit={onSubmit} />)
    await user.type(screen.getByRole('textbox', { name: 'Сумма выплаты' }), '100')
    await user.click(screen.getByRole('button', { name: 'Провести' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Недостаточно денег в кассе')
    expect(screen.queryByRole('checkbox', { name: 'Подтвердить отрицательный остаток фонда' })).not.toBeInTheDocument()
  })

  it.each(['with_receipt', 'without_receipt'] as const)('requires explicit server-confirmed consent for an unknown fund balance: %s', async (paymentType) => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
      .mockRejectedValueOnce(new FinanceApiError('fund_balance_insufficient', 'Недостаточно средств в выбранном фонде.', 400))
      .mockResolvedValue(null)
    const onClose = vi.fn()
    render(<NewExpensePrototypeDialog
      availableAmounts={[1000, 1000]}
      expenseTypes={[{ id: 'expense-1', name: 'Ремонт', code: 'repair', isSystem: false, isArchived: false }]}
      fundsClient={{ getFundOptions: vi.fn().mockResolvedValue([{ id: 'fund-1', name: 'Ремонтный фонд', allowOperations: true }]) }} accessToken="token"
      preset={{ expensePaymentSource: 'cash' }} suppliers={[]} onClose={onClose} onSubmit={onSubmit}
    />)
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Фонд расходования' })).toBeEnabled())
    await user.click(screen.getByRole('combobox', { name: 'Фонд расходования' }))
    await user.click(screen.getByRole('option', { name: /Ремонтный фонд/ }))
    expect(screen.getByRole('combobox', { name: 'Фонд расходования' })).toHaveTextContent(/^Ремонтный фонд$/)
    if (paymentType === 'without_receipt') {
      await user.click(screen.getByRole('combobox', { name: 'Тип выплаты' }))
      await user.click(screen.getByRole('option', { name: 'Без чека' }))
    }
    await user.type(screen.getByRole('textbox', { name: 'Сумма выплаты' }), '100')
    expect(screen.queryByRole('checkbox', { name: 'Подтвердить отрицательный остаток фонда' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Провести' }))
    expect(onSubmit).toHaveBeenLastCalledWith(expect.objectContaining({ expenseFundId: 'fund-1', expensePaymentType: paymentType, confirmNegativeFundBalance: false }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Недостаточно средств')
    expect(onClose).not.toHaveBeenCalled()
    const confirmation = screen.getByRole('checkbox', { name: 'Подтвердить отрицательный остаток фонда' })
    expect(confirmation).not.toBeChecked()
    await user.click(confirmation)
    await user.click(screen.getByRole('button', { name: 'Провести' }))
    expect(onSubmit).toHaveBeenLastCalledWith(expect.objectContaining({ confirmNegativeFundBalance: true }))
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('defaults a cash payout to the unallocated pool and submits a selected fund', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn().mockResolvedValue(null)
    render(<NewExpensePrototypeDialog
      availableAmounts={[1000, 1000]}
      expenseTypes={[{ id: 'expense-1', name: 'Ремонт', code: 'repair', isSystem: false, isArchived: false }]}
      fundsClient={{ getFundOptions: vi.fn().mockResolvedValue([{ id: 'fund-1', name: 'Ремонтный фонд', allowOperations: true }]) }} accessToken="token"
      preset={{ expensePaymentSource: 'cash' }}
      suppliers={[]}
      onClose={vi.fn()}
      onSubmit={onSubmit}
    />)

    expect(screen.getByRole('combobox', { name: 'Фонд расходования' })).toHaveTextContent('Общий нераспределённый пул')
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Фонд расходования' })).toBeEnabled())
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
