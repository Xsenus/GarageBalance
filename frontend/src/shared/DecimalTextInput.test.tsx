import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { DecimalTextInput } from './DecimalTextInput'

describe('DecimalTextInput', () => {
  it.each(['1.2345', '100,125', '', 'некорректная ставка'])('preserves the caller-owned draft %j without rounding', (value) => {
    render(<DecimalTextInput aria-label="Ставка" value={value} onChange={vi.fn()} />)
    const input = screen.getByRole('textbox', { name: 'Ставка' })
    expect(input).toHaveAttribute('type', 'text')
    expect(input).toHaveAttribute('inputmode', 'decimal')
    expect(input).toHaveValue(value)
    fireEvent.focus(input)
    fireEvent.blur(input)
    expect(input).toHaveValue(value)
  })

  it('forwards edit, focus, blur and keyboard events without deciding numeric validity', () => {
    const onChange = vi.fn()
    const onFocus = vi.fn()
    const onBlur = vi.fn()
    const onKeyDown = vi.fn()
    render(<DecimalTextInput aria-label="Граница" defaultValue="100" aria-invalid="true"
      onChange={onChange} onFocus={onFocus} onBlur={onBlur} onKeyDown={onKeyDown} />)
    const input = screen.getByRole('textbox', { name: 'Граница' })
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '100,125' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    fireEvent.blur(input)
    expect(input).toHaveValue('100,125')
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onFocus).toHaveBeenCalledTimes(1)
    expect(onBlur).toHaveBeenCalledTimes(1)
    expect(onKeyDown).toHaveBeenCalledTimes(1)
  })

  it.each(['disabled', 'readOnly'] as const)('keeps the %s state supplied by the caller', async (state) => {
    const onChange = vi.fn()
    render(<DecimalTextInput aria-label="Ставка" value="1.2345" onChange={onChange} {...{ [state]: true }} />)
    const input = screen.getByRole('textbox', { name: 'Ставка' })
    await userEvent.setup().type(input, '9')
    expect(input).toHaveAttribute(state.toLowerCase())
    expect(input).toHaveValue('1.2345')
    expect(onChange).not.toHaveBeenCalled()
  })
})
