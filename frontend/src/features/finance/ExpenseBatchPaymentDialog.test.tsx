import { act, fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { ExpenseBatchPreview } from '../../services/expenseBatchesApi'
import ExpenseBatchPaymentDialog from './ExpenseBatchPaymentDialog'

const preview: ExpenseBatchPreview = {
  accountingMonth: '2026-09-01', operationDate: '2026-09-06',
  items: Array.from({ length: 11 }, (_, index) => ({ recipientName: `Получатель ${index + 1}`, expenseTypeName: 'Уборка', payment: {
    recipientKind: 'supplier' as const, recipientId: `id-${index}`, expenseTypeId: 'clean', fundId: 'fund',
    accountingMonth: '2026-09-01', amount: 10, paymentSource: 'bank' as const,
  } })),
  bankAmount: 110, cashAmount: 0, availableBankAmount: 200, availableCashAmount: 0,
  funds: [{ fundId: 'fund', name: 'Хозяйственный фонд', amount: 110, availableAmount: 100 }],
  issues: [], requiresNegativeFundConfirmation: true, fingerprint: 'a'.repeat(64), canSubmit: true,
}
function setup(options: Partial<ExpenseBatchPreview> = {}) {
  const client = { preview: vi.fn().mockResolvedValue({ ...preview, ...options }), pay: vi.fn().mockResolvedValue({ requestId: 'paid', operationIds: ['one', 'two'] }) }
  const props = { accessToken: 'test', open: true, accountingMonth: '2026-09-01', canPay: true, onClose: vi.fn(), onPaid: vi.fn(), client }
  return { client, props }
}

describe('ExpenseBatchPaymentDialog', () => {
  it('shows shared loading, paginated recipients and full totals; validates and pays once with focus restoration', async () => {
    const user = userEvent.setup()
    const { client, props } = setup()
    let complete!: (value: ExpenseBatchPreview) => void
    client.preview.mockImplementationOnce(() => new Promise((resolve) => { complete = resolve }))
    render(<button>Открыть выплаты</button>)
    const trigger = screen.getByRole('button', { name: 'Открыть выплаты' })
    trigger.focus()
    const view = render(<ExpenseBatchPaymentDialog {...props} />)
    expect(screen.getByRole('status', { name: 'Рассчитываем все выплаты' })).toHaveClass('loading-skeleton')
    expect(screen.queryByText('Задолженности для оплаты нет.')).not.toBeInTheDocument()
    await act(async () => complete(preview))
    const table = screen.getByRole('table', { name: 'Предварительный список выплат' })
    expect(table).toHaveClass('dictionary-data-table')
    expect(screen.getByRole('dialog', { name: 'Оплатить все' })).toHaveClass('payments-prototype-calculation-dialog')
    expect(within(table).getAllByRole('row')).toHaveLength(11)
    expect(screen.queryByText('Получатель 11')).not.toBeInTheDocument()
    const navigation = screen.getByRole('navigation', { name: 'Страницы предварительных выплат' })
    await user.click(within(navigation).getByRole('button', { name: 'Страница 2', exact: true }))
    expect(screen.getByText('Получатель 11')).toBeVisible()
    await user.click(within(navigation).getByRole('button', { name: '25', exact: true }))
    expect(within(table).getAllByRole('row')).toHaveLength(12)
    expect(screen.getByLabelText('Итоги общей выплаты')).toHaveTextContent('Хозяйственный фонд')
    await user.click(screen.getByRole('button', { name: 'Подтвердить выплаты' }))
    expect(screen.getByRole('alert')).toHaveTextContent('комментарий')
    await user.type(screen.getByRole('textbox', { name: 'Комментарий к общей выплате' }), 'Погашение долга')
    await user.click(screen.getByRole('button', { name: 'Подтвердить выплаты' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Подтвердите')
    await user.click(screen.getByRole('checkbox', { name: 'Подтверждаю выплату сверх остатка фонда' }))
    await user.click(screen.getByRole('button', { name: 'Подтвердить выплаты' }))
    expect(await screen.findByText('Выплаты проведены: 2. Форма выплат обновляется.')).toBeVisible()
    expect(props.onPaid).toHaveBeenCalledOnce()
    expect(client.pay).toHaveBeenCalledOnce()
    view.rerender(<ExpenseBatchPaymentDialog {...props} open={false} />)
    expect(trigger).toHaveFocus()
  })

  it('blocks conflicting actions while saving and preserves the exact request after closing an uncertain result', async () => {
    const user = userEvent.setup()
    const { client, props } = setup({ requiresNegativeFundConfirmation: false })
    let fail!: (reason: Error) => void
    client.pay.mockImplementationOnce(() => new Promise((_, reject) => { fail = reject }))
    const view = render(<ExpenseBatchPaymentDialog {...props} />)
    await screen.findByRole('table')
    await user.type(screen.getByRole('textbox', { name: 'Комментарий к общей выплате' }), 'Общая выплата')
    await user.click(screen.getByRole('button', { name: 'Подтвердить выплаты' }))
    expect(screen.getByRole('button', { name: 'Отмена' })).toBeDisabled()
    await user.keyboard('{Escape}')
    expect(props.onClose).not.toHaveBeenCalled()
    fireEvent.submit(screen.getByRole('button', { name: 'Проводим выплаты…' }).closest('form')!)
    expect(client.pay).toHaveBeenCalledOnce()
    await act(async () => fail(new Error('Связь потеряна')))
    expect(screen.getByRole('textbox', { name: 'Комментарий к общей выплате' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Отмена' }))
    expect(props.onClose).toHaveBeenCalledOnce()
    view.rerender(<ExpenseBatchPaymentDialog {...props} open={false} />)
    view.rerender(<ExpenseBatchPaymentDialog {...props} />)
    await user.click(screen.getByRole('button', { name: 'Проверить результат' }))
    expect(client.pay.mock.calls[1][1]).toBe(client.pay.mock.calls[0][1])
    expect(props.onPaid).toHaveBeenCalledOnce()
    expect(client.preview).toHaveBeenCalledOnce()
  })

  it('shows empty, blocked, and permission-denied states without allowing payment', async () => {
    const { props, client } = setup({ items: [], canSubmit: false, issues: ['Недостаточно денег в банке.'], requiresNegativeFundConfirmation: false })
    const view = render(<ExpenseBatchPaymentDialog {...props} />)
    expect(await screen.findByText('Задолженности для оплаты нет.')).toHaveClass('empty-state--spacious')
    expect(screen.getByRole('alert')).toHaveTextContent('Недостаточно денег')
    expect(screen.getByRole('button', { name: 'Подтвердить выплаты' })).toBeDisabled()
    view.rerender(<ExpenseBatchPaymentDialog {...props} canPay={false} />)
    expect(screen.getByRole('alert')).toHaveTextContent('Нет разрешения')
    expect(screen.queryByRole('button', { name: 'Подтвердить выплаты' })).not.toBeInTheDocument()
    expect(client.pay).not.toHaveBeenCalled()
  })

  it('displays load errors without the manual calculation refresh control', async () => {
    const { props, client } = setup()
    client.preview.mockRejectedValueOnce(new Error('Сервис недоступен'))
    render(<ExpenseBatchPaymentDialog {...props} />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Сервис недоступен')
    expect(screen.queryByRole('button', { name: 'Обновить расчёт' })).not.toBeInTheDocument()
    expect(client.preview).toHaveBeenCalledOnce()
  })
})
