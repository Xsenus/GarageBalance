import { render, screen } from '@testing-library/react'
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

    await user.click(screen.getByRole('button', { name: 'Удалить телефон 1' }))
    expect(screen.getByRole('textbox', { name: 'Телефон владельца' })).toHaveValue('+7 (923) 765-43-21')
  })

  it('limits the owner to ten phone numbers', () => {
    render(<PhoneListHarness initial={Array.from({ length: 10 }, () => '')} />)

    expect(screen.getAllByRole('textbox')).toHaveLength(10)
    expect(screen.getByRole('button', { name: 'Добавить телефон' })).toBeDisabled()
  })
})
