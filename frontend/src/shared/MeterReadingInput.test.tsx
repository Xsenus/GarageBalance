import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { MeterReadingInput } from './MeterReadingInput'

describe('MeterReadingInput', () => {
  it('keeps the decimal keyboard hint and forwards editing events', () => {
    const onChange = vi.fn()
    render(<MeterReadingInput aria-label="Показание воды" value="12,5" onChange={onChange} />)

    const input = screen.getByRole('textbox', { name: 'Показание воды' })
    expect(input).toHaveAttribute('inputmode', 'decimal')
    expect(input).toHaveValue('12,5')

    fireEvent.change(input, { target: { value: '13,25' } })
    expect(onChange).toHaveBeenCalledTimes(1)
  })
})
