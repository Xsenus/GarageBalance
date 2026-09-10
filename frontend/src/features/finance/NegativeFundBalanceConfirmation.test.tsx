import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { NegativeFundBalanceConfirmation } from './NegativeFundBalanceConfirmation'

describe('NegativeFundBalanceConfirmation', () => {
  it('supports keyboard consent and prevents changes while saving', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    const view = render(<NegativeFundBalanceConfirmation visible checked={false} disabled={false} onChange={onChange} />)
    const checkbox = screen.getByRole('checkbox', { name: 'Подтвердить отрицательный остаток фонда' })
    expect(checkbox).toBeRequired()
    expect(checkbox).toBeInvalid()
    await user.tab()
    expect(checkbox).toHaveFocus()
    await user.keyboard(' ')
    expect(onChange).toHaveBeenLastCalledWith(true)
    view.rerender(<NegativeFundBalanceConfirmation visible checked disabled onChange={onChange} />)
    expect(checkbox).toBeChecked()
    expect(checkbox).toBeDisabled()
    expect(checkbox).toBeValid()
    await user.click(checkbox)
    expect(onChange).toHaveBeenCalledTimes(1)
  })

  it('does not offer consent when the fund does not require it', () => {
    render(<NegativeFundBalanceConfirmation visible={false} checked={false} disabled={false} onChange={vi.fn()} />)
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  })
})
