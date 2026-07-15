import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { MoneyInput } from './MoneyInput'
import { formatMoneyInput, parseMoneyInput } from './moneyInputFormatting'

describe('MoneyInput', () => {
  it('shows grouped values with a decimal point and two digits', () => {
    expect(formatMoneyInput(1_000_000)).toBe('1 000 000.00')
    expect(formatMoneyInput(7.4)).toBe('7.40')
    expect(parseMoneyInput('1 000 000.50')).toBe(1_000_000.5)
    expect(parseMoneyInput('128,69')).toBe(128.69)
  })

  it('keeps typing comfortable and formats the value on blur', async () => {
    const user = userEvent.setup()

    function Example() {
      const [value, setValue] = useState(1)
      return <MoneyInput aria-label="Ставка" value={value} onValueChange={setValue} />
    }

    render(<Example />)
    const input = screen.getByLabelText('Ставка')
    expect(input).toHaveValue('1.00')

    await user.clear(input)
    await user.type(input, '1000000.5')
    expect(input).toHaveValue('1000000.5')

    fireEvent.blur(input)
    expect(input).toHaveValue('1 000 000.50')
  })

  it('keeps an empty or invalid value empty for validation', async () => {
    const user = userEvent.setup()

    function Example() {
      const [value, setValue] = useState(10)
      return <MoneyInput aria-label="Ставка" value={value} onValueChange={setValue} />
    }

    render(<Example />)
    const input = screen.getByLabelText('Ставка')
    await user.clear(input)
    fireEvent.blur(input)
    expect(input).toHaveValue('')
  })
})
