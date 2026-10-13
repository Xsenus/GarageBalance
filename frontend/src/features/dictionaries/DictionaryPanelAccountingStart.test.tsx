import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { DictionaryPanelV2 } from './DictionaryPanel'
import type { AuthResponse } from '../../services/authApi'
import type { DictionaryClient, OwnerDto, GarageDto } from '../../services/dictionariesApi'
import type { FinanceClient } from '../../services/financeApi'
import type { IntegrationClient } from '../../services/integrationsApi'

const owner: OwnerDto = { id: '11111111-1111-4111-8111-111111111111', lastName: 'Тестовый', firstName: 'Владелец', middleName: null, fullName: 'Тестовый Владелец', phone: null, address: null, meterNotes: null, isArchived: false }

function setup(permissionList = ['dictionaries.read', 'dictionaries.write', 'opening_data.adjust', 'payments.write']) {
  const createOwner = vi.fn(async () => owner)
  const createGarage = vi.fn(async () => ({ id: 'garage-created' } as GarageDto))
  const client = { getOwners: async () => [], getGarages: async () => [], createOwner, createGarage } as unknown as DictionaryClient
  const auth: AuthResponse = { accessToken: 'token', expiresAtUtc: '2099-01-01', user: { id: 'user', email: 'test@example.invalid', displayName: 'Тест', roles: [], permissions: permissionList } }
  render(<DictionaryPanelV2 auth={auth} dictionaryClient={client} financeClient={{} as FinanceClient} integrationClient={{} as IntegrationClient} initialSection="owners" />)
  return { createOwner, createGarage }
}

async function openOwner(user: ReturnType<typeof userEvent.setup>) {
  const add = await screen.findByRole('button', { name: 'Добавить', exact: true })
  await waitFor(() => expect(add).toBeEnabled())
  await user.click(add)
  const dialog = await screen.findByRole('dialog')
  await user.type(within(dialog).getByLabelText('Фамилия владельца'), 'Тестовый')
  await user.type(within(dialog).getByLabelText('Имя владельца'), 'Владелец')
  return dialog
}

describe('owner garage accounting month', () => {
  it('creates the linked garage with the selected month and rejects an empty month before saving', async () => {
    const user = userEvent.setup()
    const { createOwner, createGarage } = setup()
    const dialog = await openOwner(user)
    const month = within(dialog).getByLabelText('Учитывать новый гараж с', { exact: true })
    expect(month).toBeDisabled()
    await user.type(within(dialog).getByLabelText('Номер нового гаража владельца'), '77')
    expect(month).toBeEnabled()
    expect(month).toHaveValue('')
    fireEvent.change(month, { target: { value: '09.2026' } })
    fireEvent.blur(month)
    fireEvent.change(month, { target: { value: '' } })
    fireEvent.blur(month)
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить', exact: true }))
    expect(await within(dialog).findByText('Выберите месяц начала учёта не раньше января 1900 года.')).toBeInTheDocument()
    expect(createOwner).not.toHaveBeenCalled()
    fireEvent.change(month, { target: { value: '09.2026' } })
    fireEvent.blur(month)
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить', exact: true }))
    await waitFor(() => expect(createGarage).toHaveBeenCalledWith('token', expect.objectContaining({ number: '77', ownerId: owner.id, accountingStartMonth: '2026-09-01' })))
    expect(createOwner).toHaveBeenCalledTimes(1)
  })

  it('allows creating only an owner without a garage or accounting changes', async () => {
    const user = userEvent.setup()
    const { createOwner, createGarage } = setup()
    const dialog = await openOwner(user)
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить', exact: true }))
    await waitFor(() => expect(createOwner).toHaveBeenCalledTimes(1))
    expect(createGarage).not.toHaveBeenCalled()
  })

  it.each(['opening_data.adjust', 'payments.write'])('does not offer month correction without %s', async (missing) => {
    const user = userEvent.setup()
    setup(['dictionaries.read', 'dictionaries.write', 'opening_data.adjust', 'payments.write'].filter(permission => permission !== missing))
    const dialog = await openOwner(user)
    await user.type(within(dialog).getByLabelText('Номер нового гаража владельца'), '77')
    expect(within(dialog).getByLabelText('Учитывать новый гараж с', { exact: true })).toBeDisabled()
  })
})
