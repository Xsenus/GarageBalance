import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { EditableCombobox } from './EditableCombobox'
import { editableComboboxValuesMatch, findEditableComboboxMatch } from './editableComboboxMatching'

const options = [
  { value: 'кВт', label: 'кВт' },
  { value: 'кВт·ч', label: 'кВт·ч' },
  { value: 'куб. м', label: 'куб. м' },
  { value: 'м3', label: 'м3' },
  { value: 'м³', label: 'м³' },
  { value: 'руб.', label: 'руб.' },
  { value: 'руб./гараж', label: 'руб./гараж' },
  { value: 'чел.', label: 'чел.' },
]

function TestCombobox({ initialValue = 'м³', disabled = false }: { initialValue?: string, disabled?: boolean }) {
  const [value, setValue] = useState(initialValue)
  return <EditableCombobox aria-label="Единица измерения" value={value} options={options} disabled={disabled} maxLength={40} onChange={setValue} />
}

afterEach(() => {
  vi.restoreAllMocks()
  delete (HTMLElement.prototype as { scrollIntoView?: HTMLElement['scrollIntoView'] }).scrollIntoView
})

function recordScrolls(labels: string[]) {
  Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
    configurable: true,
    value(this: HTMLElement) {
      labels.push(this.textContent ?? '')
    },
  })
}

describe('EditableCombobox', () => {
  it('finds an exact, prefix or contained match without filtering options', () => {
    expect(findEditableComboboxMatch(options, ' М³ ')).toBe(4)
    expect(findEditableComboboxMatch(options, 'руб/')).toBe(6)
    expect(findEditableComboboxMatch(options, 'гараж')).toBe(6)
    expect(findEditableComboboxMatch(options, 'упаковка')).toBe(-1)
    expect(findEditableComboboxMatch(options, '')).toBe(0)
    expect(editableComboboxValuesMatch(' М³ ', 'м³')).toBe(true)
    expect(editableComboboxValuesMatch('м3', 'м³')).toBe(false)
  })

  it('shows the entire dictionary when opened and scrolls to the current value', async () => {
    const user = userEvent.setup()
    const scrolledLabels: string[] = []
    recordScrolls(scrolledLabels)
    render(<TestCombobox />)

    await user.click(screen.getByRole('combobox', { name: 'Единица измерения' }))

    const listbox = screen.getByRole('listbox', { name: 'Единица измерения: варианты' })
    expect(within(listbox).getAllByRole('option')).toHaveLength(options.length)
    expect(within(listbox).getByRole('option', { name: 'м³' })).toHaveClass('is-active')
    expect(within(listbox).getByRole('option', { name: 'м³' })).toHaveAttribute('aria-selected', 'true')
    expect(scrolledLabels).toContain('м³')
  })

  it('keeps all options visible while typing and scrolls to the found unit', async () => {
    const user = userEvent.setup()
    const scrolledLabels: string[] = []
    recordScrolls(scrolledLabels)
    render(<TestCombobox />)
    const input = screen.getByRole('combobox', { name: 'Единица измерения' })

    await user.clear(input)
    await user.type(input, 'гараж')

    const listbox = screen.getByRole('listbox', { name: 'Единица измерения: варианты' })
    expect(within(listbox).getAllByRole('option')).toHaveLength(options.length)
    expect(within(listbox).getByRole('option', { name: 'руб./гараж' })).toHaveClass('is-active')
    expect(scrolledLabels).toContain('руб./гараж')
  })

  it('allows a custom value and selects a dictionary option with mouse or keyboard', async () => {
    const user = userEvent.setup()
    render(<TestCombobox initialValue="" />)
    const input = screen.getByRole('combobox', { name: 'Единица измерения' })

    await user.type(input, 'упаковка')
    expect(input).toHaveValue('упаковка')
    expect(screen.getAllByRole('option')).toHaveLength(options.length)
    expect(document.querySelector('.select-control__option.is-active')).toBeNull()

    await user.clear(input)
    await user.type(input, 'руб/')
    await user.keyboard('{Enter}')
    expect(input).toHaveValue('руб./гараж')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Единица измерения: открыть список' }))
    await user.click(screen.getByRole('option', { name: 'кВт·ч' }))
    expect(input).toHaveValue('кВт·ч')
  })

  it('supports navigation, closing and disabled states', async () => {
    const user = userEvent.setup()
    const { rerender } = render(<TestCombobox initialValue="куб. м" />)
    const input = screen.getByRole('combobox', { name: 'Единица измерения' })

    await user.click(input)
    await user.keyboard('{End}{ArrowUp}{Enter}')
    expect(input).toHaveValue('руб./гараж')

    await user.click(input)
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()

    await user.click(input)
    await user.click(document.body)
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()

    rerender(<TestCombobox initialValue="куб. м" disabled />)
    expect(screen.getByRole('combobox', { name: 'Единица измерения' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Единица измерения: открыть список' })).toBeDisabled()
  })
})
