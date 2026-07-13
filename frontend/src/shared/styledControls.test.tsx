import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { LocalizedDatePicker } from './LocalizedDatePicker'
import { SelectControl } from './SelectControl'

describe('styled form controls', () => {
  it('selects an option with the keyboard and closes the list', async () => {
    const user = userEvent.setup()
    function Example() {
      const [value, setValue] = useState('')
      return <SelectControl aria-label="Раздел" value={value} options={[{ value: '', label: 'Все разделы' }, { value: 'finance', label: 'Финансы' }]} onChange={setValue} />
    }

    render(<Example />)
    const control = screen.getByRole('combobox', { name: 'Раздел' })
    control.focus()
    await user.keyboard('{ArrowDown}{Enter}')

    expect(control).toHaveTextContent('Финансы')
    expect(control).toHaveAttribute('aria-expanded', 'false')
  })

  it('accepts localized dates and returns an ISO filter value', async () => {
    const user = userEvent.setup()
    function Example() {
      const [value, setValue] = useState('')
      return (
        <>
          <LocalizedDatePicker ariaLabel="Дата события" mode="date" value={value} onChange={setValue} />
          <output>{value}</output>
        </>
      )
    }

    render(<Example />)
    await user.type(screen.getByLabelText('Дата события'), '10.07.1992')

    expect(screen.getByText('1992-07-10')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Открыть календарь: Дата события' }))
    expect(screen.getByRole('dialog', { name: 'Дата события: календарь' })).toBeInTheDocument()
  })
})
