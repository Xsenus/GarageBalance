import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { AuthResponse } from '../../services/authApi'
import type { DictionaryClient } from '../../services/dictionariesApi'
import type { CreateMeterReadingRequest, FinanceClient, MeterReadingYearGarageDto, MeterReadingYearPageDto } from '../../services/financeApi'
import type { ApplicationSettingsClient } from '../../services/settingsApi'
import { permissions } from '../../shared/accessControl'
import { MeterReadingsPrototypePanel } from './MeterReadingsPanel'

const allPermissions = [permissions.paymentsWrite, permissions.historicalMeterReadingsCorrect, permissions.dictionariesWrite, permissions.openingDataAdjust]

function fixture({
  late = {},
  granted = allPermissions,
  historicalCorrectionEnabled = true,
  meterKind = 'electricity',
}: {
  late?: Partial<MeterReadingYearGarageDto>
  granted?: string[]
  historicalCorrectionEnabled?: boolean
  meterKind?: string
} = {}) {
  const page: MeterReadingYearPageDto = {
    garages: [
      { id: 'early', number: 'EARLY', accountingStartMonth: '2026-07-01', connectedOn: '2026-07-01', startValueSource: 'connection', startValueDate: '2026-07-01', startValue: 10 },
      { id: 'late', number: 'LATE', accountingStartMonth: '2020-01-01', connectedOn: null, initialReadingMonth: '2026-06-01', initialReadingValue: 100, startValueSource: 'device', startValueDate: '2026-10-15', startValueDeviceSerial: 'SYNTHETIC-DEVICE', startValue: 0, ...late },
    ],
    readings: [], totalCount: 2, offset: 0, limit: 25, currentAccountingMonth: '2026-11-01',
  }
  const getMeterReadingYearPage = vi.fn().mockImplementation(async () => page)
  const createMeterReading = vi.fn().mockImplementation(async (_token: string, request: CreateMeterReadingRequest) => {
    const reading = { id: 'created', version: 'created-v1', ...request }
    page.readings.push(reading)
    return reading
  })
  const updateMeterReading = vi.fn().mockImplementation(async (_token: string, id: string, request: CreateMeterReadingRequest) => {
    const reading = page.readings.find((item) => item.id === id)!
    Object.assign(reading, request, { version: 'updated-v2' })
    return reading
  })
  const correctHistoricalMeterReading = vi.fn().mockImplementation(async (_token: string, id: string, request: { currentValue: number }) => {
    const reading = page.readings.find((item) => item.id === id)!
    Object.assign(reading, request, { version: 'corrected-v2' })
    return reading
  })
  const setMeterStartValue = vi.fn().mockImplementation(async (_token: string, request: { value: number | null }) => {
    page.garages[1].startValue = request.value
    return { garageId: 'late', garageNumber: 'LATE', changes: [] }
  })
  const props = {
    auth: { accessToken: 'synthetic-token', expiresAtUtc: '2099-01-01T00:00:00Z', user: { id: 'operator', email: 'operator@example.test', displayName: 'Test operator', roles: [], permissions: granted } } satisfies AuthResponse,
    dictionaryClient: { getChargeServiceSettings: vi.fn().mockResolvedValue([{ id: 'service', name: 'Test service', meterKind, isRegular: true, isMetered: true, isArchived: false, unitName: 'кВт·ч' }]) } as unknown as DictionaryClient,
    financeClient: { getMeterReadingYearPage, createMeterReading, updateMeterReading, correctHistoricalMeterReading, setMeterStartValue } as unknown as FinanceClient,
    settingsClient: { getHistoricalMeterReadingCorrectionSettings: vi.fn().mockResolvedValue({ enabled: historicalCorrectionEnabled }) } as unknown as ApplicationSettingsClient,
  }
  return { props, page, getMeterReadingYearPage, createMeterReading, updateMeterReading, correctHistoricalMeterReading, setMeterStartValue }
}

async function openPanel(context: ReturnType<typeof fixture>) {
  render(<MeterReadingsPrototypePanel {...context.props} />)
  await screen.findByLabelText('Гараж EARLY, Июнь, стартовое показание')
  await waitFor(() => expect(screen.getByRole('table', { name: 'Показания счетчиков за 2026 год' })).toHaveAttribute('aria-busy', 'false'))
}

function expectNoWrite(context: ReturnType<typeof fixture>) {
  expect(context.createMeterReading).not.toHaveBeenCalled()
  expect(context.updateMeterReading).not.toHaveBeenCalled()
  expect(context.correctHistoricalMeterReading).not.toHaveBeenCalled()
  expect(context.setMeterStartValue).not.toHaveBeenCalled()
}

describe('meter reading start boundary in the rendered yearly panel', () => {
  it.each(['electricity', 'custom-meter'])('disables empty months before the device baseline in a mixed %s page', async (meterKind) => {
    const context = fixture({ meterKind })
    await openPanel(context)
    expect(screen.getByLabelText('Гараж EARLY, Август, показание')).toBeEnabled()
    expect(screen.getByLabelText('Гараж LATE, Август, показание')).toBeDisabled()
    expect(screen.getByLabelText('Гараж LATE, Сентябрь, стартовое показание')).toHaveValue('0')
    expect(screen.getByLabelText('Гараж LATE, Октябрь, показание')).toBeEnabled()
    expectNoWrite(context)
  })

  it('does not submit an empty pre-device month through typing and blur', async () => {
    const user = userEvent.setup()
    const context = fixture()
    await openPanel(context)
    const beforeStart = screen.getByLabelText('Гараж LATE, Август, показание')
    await user.type(beforeStart, '5')
    await user.tab()
    expectNoWrite(context)
    expect(beforeStart).toHaveValue('')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('guards the commit handler against a pre-device month even when events bypass disabled UI', async () => {
    const context = fixture()
    await openPanel(context)
    const beforeStart = screen.getByLabelText('Гараж LATE, Август, показание')
    fireEvent.change(beforeStart, { target: { value: '5' } })
    await act(async () => { fireEvent.keyDown(beforeStart, { key: 'Enter' }) })
    expectNoWrite(context)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('uses the device boundary rather than a stale later legacy month', async () => {
    const context = fixture({ late: { initialReadingMonth: '2026-12-01' } })
    await openPanel(context)
    expect(screen.getByLabelText('Гараж LATE, Сентябрь, стартовое показание')).toHaveValue('0')
    expect(screen.getByLabelText('Гараж LATE, Октябрь, показание')).toBeEnabled()
    expectNoWrite(context)
  })

  it('preserves the first device date through create and subsequent historical correction', async () => {
    const user = userEvent.setup()
    const context = fixture()
    await openPanel(context)
    await user.type(screen.getByLabelText('Гараж LATE, Октябрь, показание'), '5')
    await user.tab()
    await waitFor(() => expect(context.createMeterReading).toHaveBeenCalledOnce())
    expect(context.createMeterReading).toHaveBeenCalledWith('synthetic-token', expect.objectContaining({ garageId: 'late', meterKind: 'electricity', accountingMonth: '2026-10-01', readingDate: '2026-10-15', currentValue: 5 }))
    expect(context.page.garages[1].startValueDate).toBe('2026-10-15')
    await waitFor(() => expect(context.getMeterReadingYearPage).toHaveBeenCalledTimes(2))
    const saved = screen.getByLabelText('Гараж LATE, Октябрь, показание')
    await user.clear(saved)
    await user.type(saved, '6')
    await user.tab()
    const dialog = await screen.findByRole('dialog', { name: 'Сохранить показание за другой месяц?' })
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(context.correctHistoricalMeterReading).toHaveBeenCalledWith('synthetic-token', 'created', expect.objectContaining({ currentValue: 6, readingDate: '2026-10-15', expectedVersion: 'created-v1' })))
    await waitFor(() => expect(context.getMeterReadingYearPage).toHaveBeenCalledTimes(3))
  })

  it('preserves an explicit zero baseline and sends its confirmed correction only to the start-value API', async () => {
    const user = userEvent.setup()
    const context = fixture()
    await openPanel(context)
    const baseline = screen.getByLabelText('Гараж LATE, Сентябрь, стартовое показание')
    expect(baseline).toHaveValue('0')
    await user.clear(baseline)
    await user.type(baseline, '2')
    await user.tab()
    const dialog = await screen.findByRole('dialog', { name: 'Изменить стартовое показание?' })
    expectNoWrite(context)
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(context.setMeterStartValue).toHaveBeenCalledWith('synthetic-token', { garageId: 'late', meterKind: 'electricity', value: 2 }))
    await waitFor(() => expect(screen.getByLabelText('Гараж LATE, Сентябрь, стартовое показание')).toHaveValue('2'))
    expect(context.createMeterReading).not.toHaveBeenCalled()
  })

  it('keeps saved zero history before a restart editable with historical-correction permission', async () => {
    const user = userEvent.setup()
    const context = fixture({ late: { initialReadingMonth: '2026-09-01' } })
    context.page.readings.push({ id: 'old', garageId: 'late', accountingMonth: '2026-08-01', readingDate: '2026-08-01', currentValue: 0, version: 'old-v1' })
    await openPanel(context)
    const history = screen.getByLabelText('Гараж LATE, Август, показание')
    expect(history).toHaveValue('0')
    expect(history).toBeEnabled()
    expect(screen.getByLabelText('Гараж LATE, Сентябрь, показание')).toBeDisabled()
    await user.clear(history)
    await user.type(history, '5')
    await user.tab()
    const dialog = await screen.findByRole('dialog', { name: 'Сохранить показание за другой месяц?' })
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(context.correctHistoricalMeterReading).toHaveBeenCalledWith('synthetic-token', 'old', expect.objectContaining({ currentValue: 5, readingDate: '2026-08-01', expectedVersion: 'old-v1' })))
    expect(context.createMeterReading).not.toHaveBeenCalled()
    await waitFor(() => expect(context.getMeterReadingYearPage).toHaveBeenCalledTimes(2))
  })

  it('keeps saved history locked when historical correction is disabled', async () => {
    const context = fixture({ historicalCorrectionEnabled: false })
    context.page.readings.push({ id: 'old', garageId: 'late', accountingMonth: '2026-08-01', currentValue: 0, version: 'old-v1' })
    await openPanel(context)
    expect(screen.getByLabelText('Гараж LATE, Август, показание')).toBeDisabled()
    expect(screen.getByLabelText('Гараж LATE, Октябрь, показание')).toBeEnabled()
    expectNoWrite(context)
  })

  it.each([true, false])('preserves the actual saved date rather than the month start or device date, current month %s', async (isCurrentMonth) => {
    const user = userEvent.setup()
    const context = fixture()
    if (isCurrentMonth) context.page.currentAccountingMonth = '2026-10-01'
    context.page.readings.push({ id: 'same-month', garageId: 'late', accountingMonth: '2026-10-01', readingDate: '2026-10-23', currentValue: 5, version: 'same-v1' })
    await openPanel(context)
    const history = screen.getByLabelText('Гараж LATE, Октябрь, показание')
    await user.clear(history)
    await user.type(history, '6')
    await user.tab()
    const dialog = await screen.findByRole('dialog', { name: isCurrentMonth ? 'Подтвердить показание?' : 'Сохранить показание за другой месяц?' })
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    const save = isCurrentMonth ? context.updateMeterReading : context.correctHistoricalMeterReading
    await waitFor(() => expect(save).toHaveBeenCalledWith('synthetic-token', 'same-month', expect.objectContaining({ currentValue: 6, readingDate: '2026-10-23', expectedVersion: 'same-v1' })))
    expect(context.page.garages[1].startValueDate).toBe('2026-10-15')
    expect(context.createMeterReading).not.toHaveBeenCalled()
    await waitFor(() => expect(context.getMeterReadingYearPage).toHaveBeenCalledTimes(2))
  })

  it('keeps a removed-device historical reading on its own date despite a later active connection', async () => {
    const user = userEvent.setup()
    const context = fixture({ late: { connectedOn: '2026-11-10', startValueDate: '2026-11-15' } })
    context.page.readings.push({ id: 'removed-device', garageId: 'late', accountingMonth: '2026-10-01', readingDate: '2026-10-21', currentValue: 5, version: 'removed-v1', meterDeviceId: 'removed', meterDeviceSerialNumber: 'OLD-SYNTHETIC', isMeterReplacement: true })
    await openPanel(context)
    const history = screen.getByLabelText('Гараж LATE, Октябрь, показание')
    expect(history).toBeEnabled()
    await user.clear(history)
    await user.type(history, '6')
    await user.tab()
    const dialog = await screen.findByRole('dialog', { name: 'Сохранить показание за другой месяц?' })
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(context.correctHistoricalMeterReading).toHaveBeenCalledWith('synthetic-token', 'removed-device', expect.objectContaining({ currentValue: 6, readingDate: '2026-10-21', expectedVersion: 'removed-v1' })))
    await waitFor(() => expect(context.getMeterReadingYearPage).toHaveBeenCalledTimes(2))
  })

  it('blocks old responses without a saved reading date instead of guessing and allows cancellation', async () => {
    const user = userEvent.setup()
    const context = fixture()
    context.page.readings.push({ id: 'old-response', garageId: 'late', accountingMonth: '2026-10-01', currentValue: 5, version: 'old-v1' })
    await openPanel(context)
    const history = screen.getByLabelText('Гараж LATE, Октябрь, показание')
    await user.clear(history)
    await user.type(history, '6')
    await user.tab()
    const dialog = await screen.findByRole('dialog', { name: 'Сохранить показание за другой месяц?' })
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Не удалось определить дату сохранённого показания. Обновите таблицу.')
    expectNoWrite(context)
    await user.click(within(dialog).getByRole('button', { name: 'Отмена' }))
    expect(history).toHaveValue('5')
  })

  it('keeps the date returned by create when the background table reload fails', async () => {
    const user = userEvent.setup()
    const context = fixture()
    await openPanel(context)
    context.createMeterReading.mockResolvedValueOnce({ id: 'created', garageId: 'late', accountingMonth: '2026-10-01', readingDate: '2026-10-18', currentValue: 5, version: 'created-v1' })
    context.getMeterReadingYearPage.mockRejectedValueOnce(new Error('Обновление таблицы не выполнено.'))
    await user.type(screen.getByLabelText('Гараж LATE, Октябрь, показание'), '5')
    await user.tab()
    expect(await screen.findByRole('alert')).toHaveTextContent('Обновление таблицы не выполнено.')
    const saved = screen.getByLabelText('Гараж LATE, Октябрь, показание')
    await user.clear(saved)
    await user.type(saved, '6')
    await user.tab()
    context.correctHistoricalMeterReading.mockResolvedValueOnce({ id: 'created', garageId: 'late', accountingMonth: '2026-10-01', readingDate: '2026-10-18', currentValue: 6, version: 'corrected-v2' })
    const dialog = await screen.findByRole('dialog', { name: 'Сохранить показание за другой месяц?' })
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(context.correctHistoricalMeterReading).toHaveBeenCalledWith('synthetic-token', 'created', expect.objectContaining({ readingDate: '2026-10-18', expectedVersion: 'created-v1' })))
    await waitFor(() => expect(context.getMeterReadingYearPage).toHaveBeenCalledTimes(3))
  })

  it('rejects a historical correction without its dedicated permission', async () => {
    const user = userEvent.setup()
    const context = fixture({ granted: [permissions.paymentsWrite] })
    context.page.readings.push({ id: 'old', garageId: 'late', accountingMonth: '2026-08-01', currentValue: 0, version: 'old-v1' })
    await openPanel(context)
    const history = screen.getByLabelText('Гараж LATE, Август, показание')
    await user.clear(history)
    await user.type(history, '5')
    await user.tab()
    expect(await screen.findByRole('alert')).toHaveTextContent('Нет права на показания вне текущего месяца.')
    expect(history).toHaveValue('0')
    expectNoWrite(context)
  })

  it('preserves legacy and unknown-date fallback without treating the baseline as consumption', async () => {
    const context = fixture({ late: { accountingStartMonth: null, startValueSource: null, startValueDate: null, initialReadingMonth: '2026-09-01', initialReadingValue: 0 } })
    context.page.garages.push({ id: 'unknown', number: 'UNKNOWN', initialReadingValue: 0 })
    context.page.totalCount = 3
    await openPanel(context)
    expect(screen.getByLabelText('Гараж LATE, Август, показание')).toBeDisabled()
    expect(screen.getByLabelText('Гараж LATE, Сентябрь, стартовое показание')).toHaveValue('0')
    expect(screen.getByLabelText('Гараж LATE, Октябрь, показание')).toBeEnabled()
    expect(screen.getByLabelText('Гараж UNKNOWN, Январь, показание')).toBeEnabled()
    expectNoWrite(context)
  })

  it('prefers the connection boundary to a later physical device date and honours service closure', async () => {
    const user = userEvent.setup()
    const context = fixture({ late: { accountingStartMonth: '2026-08-01', connectedOn: '2026-09-10', closedOn: '2026-11-05' } })
    await openPanel(context)
    expect(screen.getByLabelText('Гараж LATE, Июль, показание')).toBeDisabled()
    expect(screen.getByLabelText('Гараж LATE, Август, стартовое показание')).toHaveValue('0')
    expect(screen.getByLabelText('Гараж LATE, Сентябрь, показание')).toBeEnabled()
    expect(screen.getByLabelText('Гараж LATE, Декабрь, показание')).toBeDisabled()
    await user.type(screen.getByLabelText('Гараж LATE, Сентябрь, показание'), '5')
    await user.tab()
    await waitFor(() => expect(context.createMeterReading).toHaveBeenCalledWith('synthetic-token', expect.objectContaining({ accountingMonth: '2026-09-01', readingDate: '2026-09-10' })))
    expect(context.page.garages[1].startValueDate).toBe('2026-10-15')
    await waitFor(() => expect(context.getMeterReadingYearPage).toHaveBeenCalledTimes(2))
  })

  it.each([
    { requiresConnectionDate: true },
    { requiresStartValue: true },
    { accountingStartMonth: '2026-12-01' },
  ])('blocks ordinary readings for unavailable service state %j', async (late) => {
    const context = fixture({ late })
    await openPanel(context)
    const input = screen.getByLabelText('Гараж LATE, Октябрь, показание')
    expect(input).toBeDisabled()
    fireEvent.change(input, { target: { value: '5' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    expectNoWrite(context)
  })

  it('honours separate write, baseline and outside-month permissions', async () => {
    const user = userEvent.setup()
    const context = fixture({ granted: [permissions.paymentsWrite] })
    await openPanel(context)
    expect(screen.getByLabelText('Гараж LATE, Сентябрь, стартовое показание')).toBeDisabled()
    expect(screen.getByLabelText('Гараж LATE, Декабрь, показание')).toBeDisabled()
    expect(screen.getByLabelText('Гараж LATE, Ноябрь, показание')).toBeEnabled()
    await user.type(screen.getByLabelText('Гараж LATE, Октябрь, показание'), '5')
    await user.tab()
    expect(await screen.findByRole('alert')).toHaveTextContent('Нет права на показания вне текущего месяца.')
    expectNoWrite(context)
  })

  it('does not write without payments permission, even through direct events', async () => {
    const context = fixture({ granted: [] })
    await openPanel(context)
    const input = screen.getByLabelText('Гараж LATE, Октябрь, показание')
    expect(input).toBeDisabled()
    expect(screen.getByLabelText('Гараж LATE, Сентябрь, стартовое показание')).toBeDisabled()
    fireEvent.change(input, { target: { value: '5' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    expectNoWrite(context)
  })
})
