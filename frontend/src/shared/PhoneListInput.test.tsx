import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { PhoneListInput } from './PhoneListInput'

function PhoneListHarness({ initial = [''], required = false }: { initial?: string[]; required?: boolean }) {
  const [phones, setPhones] = useState(initial)
  return <PhoneListInput label="Телефоны владельца" firstPhoneLabel="Телефон владельца" values={phones} onChange={setPhones} required={required} />
}

describe('PhoneListInput', () => {
  it('adds, formats and removes additional owner phones', async () => {
    const user = userEvent.setup()
    render(<PhoneListHarness required />)

    const firstPhone = screen.getByRole('textbox', { name: 'Телефон владельца' })
    await user.type(firstPhone, '9131234567')
    await user.click(screen.getByRole('button', { name: 'Добавить телефон' }))
    const secondPhone = screen.getByRole('textbox', { name: 'Телефон владельца: телефон 2' })
    await user.type(secondPhone, '9237654321')

    expect(firstPhone).toHaveValue('+7 (913) 123-45-67')
    expect(secondPhone).toHaveValue('+7 (923) 765-43-21')
    expect(firstPhone).toBeRequired()
    expect(secondPhone).not.toBeRequired()
    expect(screen.queryByRole('button', { name: 'Удалить телефон 1' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Удалить телефон 2' }))
    expect(screen.getByRole('textbox', { name: 'Телефон владельца' })).toHaveValue('+7 (913) 123-45-67')
    expect(screen.queryByRole('textbox', { name: 'Телефон владельца: телефон 2' })).not.toBeInTheDocument()
  })

  it('groups additional garage phones in a separate two-column row', async () => {
    const user = userEvent.setup()
    render(<PhoneListHarness />)

    const group = screen.getByRole('group', { name: 'Телефоны владельца' })
    expect(within(group).getByRole('button', { name: 'Добавить телефон' }).parentElement).toContainElement(
      within(group).getByRole('textbox', { name: 'Телефон владельца' }),
    )

    await user.click(within(group).getByRole('button', { name: 'Добавить телефон' }))
    await user.click(within(group).getByRole('button', { name: 'Добавить телефон' }))

    const additionalPhones = group.querySelector('.contractors-garage-form-notes')
    expect(additionalPhones).not.toBeNull()
    expect(additionalPhones?.children).toHaveLength(2)
    expect(additionalPhones).toContainElement(within(group).getByRole('textbox', { name: 'Телефон владельца: телефон 2' }))
    expect(additionalPhones).toContainElement(within(group).getByRole('textbox', { name: 'Телефон владельца: телефон 3' }))
  })

  it('limits the owner to ten phone numbers', () => {
    render(<PhoneListHarness initial={Array.from({ length: 10 }, () => '')} />)

    expect(screen.getAllByRole('textbox')).toHaveLength(10)
    expect(screen.getByRole('button', { name: 'Добавить телефон' })).toBeDisabled()
  })
})
