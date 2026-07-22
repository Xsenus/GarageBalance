import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { PhoneInput } from './PhoneInput'

describe('PhoneInput', () => {
  it('shows the shared mask and returns the canonical value while typing', async () => {
    const user = userEvent.setup()
    function TestHarness() {
      const [value, setValue] = useState('')
      return <PhoneInput aria-label="Телефон" value={value} onValueChange={setValue} />
    }
    render(<TestHarness />)

    await user.type(screen.getByRole('textbox', { name: 'Телефон' }), '9131234567')

    expect(screen.getByRole('textbox', { name: 'Телефон' })).toHaveValue('+7 (913) 123-45-67')
    expect(screen.getByRole('textbox', { name: 'Телефон' })).toHaveAttribute('placeholder', '+7 (___) ___-__-__')
    expect(screen.getByRole('textbox', { name: 'Телефон' })).toHaveAttribute('inputmode', 'tel')
  })
})
