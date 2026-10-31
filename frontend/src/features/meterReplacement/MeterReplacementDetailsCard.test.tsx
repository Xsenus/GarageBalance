import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import type { AuthResponse } from '../../services/authApi'
import type { FinanceClient, MeterDeviceReplacementDetailsDto } from '../../services/financeApi'
import { MeterReplacementDetailsCard } from './MeterReplacementDetailsCard'

function fixture(writable = true) {
  const data = { device: { id: 'new', garageId: 'garage', installedOn: '2026-06-10', serialNumber: 'NEW', initialValue: 0, version: 'new-v', replacementAccountingMonth: '2026-06-01', replacementReason: 'Замена' },
    removedDevice: { id: 'old', garageId: 'garage', serialNumber: 'OLD', finalValue: 15, version: 'old-v' },
    reading: { id: 'reading', garageId: 'garage', meterDeviceId: 'new', version: 'reading-v', accountingMonth: '2026-06-01', currentValue: 2, consumption: 7, previousDeviceConsumption: 5 } } as MeterDeviceReplacementDetailsDto
  const get = vi.fn().mockResolvedValue(data)
  const update = vi.fn().mockResolvedValue(data)
  const props = { auth: { accessToken: 'token', user: { roles: [], permissions: writable ? ['payments.read', 'payments.write', 'payments.meter_readings.historical_correct'] : ['payments.read'] } } as AuthResponse,
    financeClient: { getMeterDeviceReplacement: get, updateMeterDeviceReplacement: update } as unknown as FinanceClient,
    garageId: 'garage', deviceId: 'new', onClose: vi.fn(), onSaved: vi.fn(), onBusyChange: vi.fn() }
  return { props, get, update }
}

it('opens the saved card and submits an explicit edit with all three versions', async () => {
  const { props, update } = fixture()
  render(<MeterReplacementDetailsCard {...props} />)
  expect(await screen.findByText(/Старый № OLD: конечное 15/)).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Изменить замену' }))
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить исправление' }))
  expect(screen.getByRole('alert')).toHaveTextContent('причину исправления')
  expect(update).not.toHaveBeenCalled()
  fireEvent.change(screen.getByLabelText('Исправленное конечное показание'), { target: { value: '19' } })
  fireEvent.change(screen.getByLabelText('Причина исправления замены'), { target: { value: 'Уточнён акт' } })
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить исправление' }))
  await waitFor(() => expect(update).toHaveBeenCalledWith('token', 'new', expect.objectContaining({ expectedDeviceVersion: 'new-v', expectedRemovedDeviceVersion: 'old-v', expectedReadingVersion: 'reading-v', removedDeviceFinalValue: 19, newInitialValue: 0, reason: 'Уточнён акт' })))
  await waitFor(() => expect(props.onSaved).toHaveBeenCalledOnce())
})

it('keeps the draft after a conflict and prevents duplicate saves', async () => {
  const { props, update } = fixture()
  let reject!: (reason: Error) => void
  update.mockImplementation(() => new Promise((_, fail) => { reject = fail }))
  render(<MeterReplacementDetailsCard {...props} />)
  fireEvent.click(await screen.findByRole('button', { name: 'Изменить замену' }))
  fireEvent.change(screen.getByLabelText('Причина исправления замены'), { target: { value: 'Исправление' } })
  const form = screen.getByRole('button', { name: 'Сохранить исправление' }).closest('form')!
  fireEvent.submit(form); fireEvent.submit(form)
  expect(update).toHaveBeenCalledOnce()
  expect(screen.getByRole('button', { name: 'Закрыть карточку замены' })).toBeDisabled()
  reject(new Error('История уже изменена'))
  expect(await screen.findByRole('alert')).toHaveTextContent('История уже изменена')
  expect(screen.getByLabelText('Причина исправления замены')).toHaveValue('Исправление')
  expect(props.onSaved).not.toHaveBeenCalled()
  expect(props.onBusyChange).toHaveBeenLastCalledWith(false)
})

it('allows read-only access and a retry after a load failure', async () => {
  const { props, get } = fixture(false)
  get.mockRejectedValueOnce(new Error('Не удалось открыть карточку'))
  render(<MeterReplacementDetailsCard {...props} />)
  expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось открыть карточку')
  fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
  expect(await screen.findByText(/Старый № OLD/)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Изменить замену' })).not.toBeInTheDocument()
})
