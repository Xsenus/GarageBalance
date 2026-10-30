import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { PenaltyAccrualPrototypeDialog } from './FinancePanel'

it('submits a negative garage discount without a cash operation and explains future credit', async () => {
  const onSubmit = vi.fn().mockResolvedValue(null)
  const onClose = vi.fn()
  render(<PenaltyAccrualPrototypeDialog onSubmit={onSubmit} onClose={onClose} />)
  expect(screen.getByRole('dialog', { name: 'Начислить штраф/ Применить скидку' })).toBeInTheDocument()
  expect(screen.getByText(/Остаток скидки зачтётся в будущих начислениях/)).toBeInTheDocument()
  expect(screen.getByLabelText('Сумма штрафа или скидки')).not.toHaveAttribute('min')
  fireEvent.change(screen.getByLabelText('Сумма штрафа или скидки'), { target: { value: '-150,25' } })
  fireEvent.change(screen.getByLabelText('Причина начисления штрафа'), { target: { value: 'Согласованная скидка' } })
  fireEvent.click(screen.getByRole('button', { name: 'Начислить' }))
  await waitFor(() => expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ amount: -150.25, reason: 'Согласованная скидка' })))
  await waitFor(() => expect(onClose).toHaveBeenCalledOnce())
})

it('rejects zero and preserves the signed draft on an API error', async () => {
  const onSubmit = vi.fn().mockResolvedValue('Конфликт данных')
  const onClose = vi.fn()
  render(<PenaltyAccrualPrototypeDialog onSubmit={onSubmit} onClose={onClose} />)
  fireEvent.change(screen.getByLabelText('Сумма штрафа или скидки'), { target: { value: '0' } })
  fireEvent.click(screen.getByRole('button', { name: 'Начислить' }))
  expect(screen.getByRole('alert')).toHaveTextContent('ненулевую сумму')
  expect(onSubmit).not.toHaveBeenCalled()
  fireEvent.change(screen.getByLabelText('Сумма штрафа или скидки'), { target: { value: '-50' } })
  fireEvent.change(screen.getByLabelText('Причина начисления штрафа'), { target: { value: 'Скидка' } })
  fireEvent.click(screen.getByRole('button', { name: 'Начислить' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Конфликт данных')
  expect(onClose).not.toHaveBeenCalled()
  expect(screen.getByLabelText('Сумма штрафа или скидки')).toHaveValue('-50.00')
})
