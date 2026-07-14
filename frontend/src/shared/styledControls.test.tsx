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

  it('keeps a disabled select closed', async () => {
    const user = userEvent.setup()
    render(<SelectControl aria-label="Статус" value="active" options={[{ value: 'active', label: 'Работает' }]} disabled onChange={() => undefined} />)

    const control = screen.getByRole('combobox', { name: 'Статус' })
    expect(control).toBeDisabled()
    await user.click(control)
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('supports full keyboard navigation and keeps list options out of the tab order', async () => {
    const user = userEvent.setup()
    function Example() {
      const [value, setValue] = useState('draft')
      return <SelectControl aria-label="Статус документа" value={value} options={[
        { value: 'draft', label: 'Черновик' },
        { value: 'approved', label: 'Проведен' },
        { value: 'cancelled', label: 'Отменен' },
      ]} onChange={setValue} />
    }

    render(<Example />)
    const control = screen.getByRole('combobox', { name: 'Статус документа' })
    control.focus()
    await user.keyboard('{Enter}{End}')

    const options = screen.getAllByRole('option')
    expect(options.every((option) => option.getAttribute('tabindex') === '-1')).toBe(true)
    await user.keyboard('{Enter}')
    expect(control).toHaveTextContent('Отменен')

    await user.keyboard('{Enter}{Home}{Escape}')
    expect(control).toHaveAttribute('aria-expanded', 'false')
    expect(control).toHaveFocus()
  })

  it('disables an empty select instead of opening an invalid list', () => {
    render(<SelectControl aria-label="Пустой справочник" value="" options={[]} onChange={() => undefined} />)

    expect(screen.getByRole('combobox', { name: 'Пустой справочник' })).toBeDisabled()
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
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

  it('keeps a disabled localized date picker closed', async () => {
    const user = userEvent.setup()
    render(<LocalizedDatePicker ariaLabel="Дата операции" mode="date" value="2026-07-14" disabled onChange={() => undefined} />)

    expect(screen.getByLabelText('Дата операции')).toBeDisabled()
    const trigger = screen.getByRole('button', { name: 'Открыть календарь: Дата операции' })
    expect(trigger).toBeDisabled()
    await user.click(trigger)
    expect(screen.queryByRole('dialog', { name: 'Дата операции: календарь' })).not.toBeInTheDocument()
  })

  it('synchronizes an externally changed value and closes the calendar with Escape', async () => {
    const user = userEvent.setup()
    function Example() {
      const [value, setValue] = useState('2026-07-14')
      return (
        <>
          <LocalizedDatePicker ariaLabel="Дата сверки" mode="date" value={value} onChange={setValue} />
          <button type="button" onClick={() => setValue('2026-08-01')}>Следующая дата</button>
        </>
      )
    }

    render(<Example />)
    const input = screen.getByLabelText('Дата сверки')
    expect(input).toHaveValue('14.07.2026')
    await user.click(screen.getByRole('button', { name: 'Следующая дата' }))
    expect(input).toHaveValue('01.08.2026')

    const trigger = screen.getByRole('button', { name: 'Открыть календарь: Дата сверки' })
    await user.click(trigger)
    expect(screen.getByRole('dialog', { name: 'Дата сверки: календарь' })).toBeInTheDocument()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Дата сверки: календарь' })).not.toBeInTheDocument()
    expect(trigger).toHaveFocus()
  })

  it('does not display an impossible externally supplied date', () => {
    render(<LocalizedDatePicker ariaLabel="Некорректная дата" mode="date" value="2026-02-31" onChange={() => undefined} />)

    expect(screen.getByLabelText('Некорректная дата')).toHaveValue('')
  })

  it('selects and clears the current localized period through project actions', async () => {
    const user = userEvent.setup()
    function Example() {
      const [value, setValue] = useState('')
      return (
        <>
          <LocalizedDatePicker ariaLabel="Учетный месяц" mode="month" value={value} onChange={setValue} />
          <output>{value}</output>
        </>
      )
    }

    render(<Example />)
    const trigger = screen.getByRole('button', { name: 'Открыть календарь: Учетный месяц' })
    await user.click(trigger)
    await user.click(screen.getByRole('button', { name: 'Текущий месяц' }))
    const today = new Date()
    const currentMonth = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}`
    expect(screen.getByText(currentMonth)).toBeInTheDocument()

    await user.click(trigger)
    await user.click(screen.getByRole('button', { name: 'Очистить' }))
    expect(screen.getByLabelText('Учетный месяц')).toHaveValue('')
  })
})
