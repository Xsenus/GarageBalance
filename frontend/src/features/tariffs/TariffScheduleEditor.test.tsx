// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { AddServicePrototypeDialog } from './TariffsAndFeesPanel'
import { removeTariffSchedulePeriod } from './tariffSchedulePeriods'
import type { DictionaryClient, GarageDto, TariffDto } from '../../services/dictionariesApi'

it('saves garage scope and tariff periods together and keeps keyboard-accessible tabs in one card', async () => {
  const savedPeriods = [{ tariffId: 'tariff-1', effectiveFrom: '2026-01-01', effectiveTo: null, rate: 350, tariffVersion: 'tariff-version' }]
  const save = vi.fn().mockResolvedValue(savedPeriods)
  const updateWithTariff = vi.fn()
  const client = { getGaragesPage: vi.fn(async () => ({ items: [{ id: 'garage-85', number: '85' } as GarageDto], totalCount: 1, offset: 0, limit: 10 })) } as unknown as DictionaryClient
  render(<AddServicePrototypeDialog accessToken="token" dictionaryClient={client}
    initialSetting={{ id: 'service-1', name: 'Охрана', isRegular: true, periodicityMonths: 1, accrualStartMonth: 1, paymentDueDay: 20,
      paymentDueMonth: null, overdueGraceDays: 0, incomeTypeId: 'income-1', tariffId: 'tariff-1', isMetered: false, hasTieredTariff: false,
      unitName: 'руб.', isArchived: false, version: 'service-version' }}
    funds={[{ id: 'fund-1', name: 'Охрана', allowOperations: true }]}
    incomeTypes={[{ id: 'income-1', name: 'Охрана', code: 'security', isArchived: false, destinationFundId: 'fund-1' }]}
    tariffs={[{ id: 'tariff-1', calculationBase: 'fixed', rate: 350, effectiveFrom: '2026-01-01', version: 'tariff-version' } as TariffDto]}
    tariffSchedule={savedPeriods} isSaving={false} onClose={vi.fn()} onUpdateTariffSchedule={save} onUpdateWithTariff={updateWithTariff} />)
  const tariffTab = screen.getByRole('tab', { name: 'Тариф и периоды' })
  tariffTab.focus()
  fireEvent.keyDown(tariffTab, { key: 'ArrowRight' })
  expect(screen.getByRole('tab', { name: 'Гаражи' })).toHaveFocus()
  fireEvent.click(screen.getByRole('checkbox', { name: 'Только для выбранных гаражей' }))
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить', exact: true }))
  expect(screen.getByRole('alert')).toHaveTextContent('Выберите хотя бы один гараж')
  expect(save).not.toHaveBeenCalled()
  fireEvent.click(await screen.findByRole('checkbox', { name: 'Выбрать гараж 85' }))
  fireEvent.keyDown(screen.getByRole('tab', { name: 'Гаражи (1)' }), { key: 'Home' })
  expect(tariffTab).toHaveFocus()
  fireEvent.change(screen.getByLabelText('День оплаты'), { target: { value: '27' } })
  fireEvent.click(screen.getByRole('button', { name: 'Сохранить', exact: true }))
  await waitFor(() => expect(save).toHaveBeenCalledOnce())
  expect(save).toHaveBeenCalledWith(expect.objectContaining({ serviceVersion: 'service-version', incomeFundId: 'fund-1',
    service: expect.objectContaining({ appliesToSelectedGarages: true, garageIds: ['garage-85'], paymentDueDay: 27 }), periods: savedPeriods }))
  expect(updateWithTariff).not.toHaveBeenCalled()
  expect(screen.getAllByRole('dialog')).toHaveLength(1)
})

describe('removeTariffSchedulePeriod', () => {
  const periods = [
    { key: 'one', effectiveFrom: '2026-01-01', effectiveTo: '2026-04-30' },
    { key: 'two', effectiveFrom: '2026-05-01', effectiveTo: '2026-08-31' },
    { key: 'three', effectiveFrom: '2026-09-01', effectiveTo: null },
  ]

  it('extends the previous interval when a middle or final period is removed', () => {
    expect(removeTariffSchedulePeriod(periods, 'two')).toEqual([
      { key: 'one', effectiveFrom: '2026-01-01', effectiveTo: '2026-08-31' },
      periods[2],
    ])
    expect(removeTariffSchedulePeriod(periods, 'three')[1].effectiveTo).toBeNull()
  })

  it('extends the next interval backwards when the first period is removed', () => {
    expect(removeTariffSchedulePeriod(periods, 'one')[0].effectiveFrom).toBe('2026-01-01')
  })
})

describe('редактор тарифной сетки услуги', () => {
  it.each(['fixed', 'people'])('saves a valid %s schedule when the linked historical tariff is absent from the bounded dictionary', async (calculationBase) => {
    const periods = [{ tariffId: 'historical', tariffVersion: 'v1', effectiveFrom: '2026-09-01', effectiveTo: null, rate: 128 }]
    const save = vi.fn().mockResolvedValue(periods)
    const update = vi.fn()
    render(<AddServicePrototypeDialog isSaving={false} onClose={vi.fn()} funds={[{ id: 'fund', name: 'Мусор', allowOperations: true }]}
      incomeTypes={[{ id: 'income', name: 'Мусор', code: 'waste', isArchived: false, destinationFundId: 'fund' }]}
      initialSetting={{ id: 'service', name: 'Мусор', isRegular: true, periodicityMonths: 1, accrualStartMonth: 1,
        paymentDueDay: 20, paymentDueMonth: null, overdueGraceDays: 30, incomeTypeId: 'income', tariffId: 'historical',
        isMetered: false, hasTieredTariff: false, unitName: 'чел.', isArchived: false, version: 'service-v1', tariffCalculationBase: calculationBase }}
      tariffs={[]} tariffSchedule={periods} onUpdateTariffSchedule={save} onUpdateWithTariff={update} />)
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить', exact: true }))
    await waitFor(() => expect(save).toHaveBeenCalledOnce())
    expect(save).toHaveBeenCalledWith(expect.objectContaining({ periods }))
    expect(update).not.toHaveBeenCalled()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('validates each visible period instead of an absent linked rate', async () => {
    const save = vi.fn()
    render(<AddServicePrototypeDialog isSaving={false} onClose={vi.fn()} funds={[{ id: 'fund', name: 'Мусор', allowOperations: true }]}
      incomeTypes={[{ id: 'income', name: 'Мусор', code: 'waste', isArchived: false, destinationFundId: 'fund' }]}
      initialSetting={{ id: 'service', name: 'Мусор', isRegular: true, periodicityMonths: 1, accrualStartMonth: 1,
        paymentDueDay: 20, paymentDueMonth: null, overdueGraceDays: 30, incomeTypeId: 'income', tariffId: 'historical',
        isMetered: false, hasTieredTariff: false, unitName: 'чел.', isArchived: false, version: 'v1' }}
      tariffs={[]} tariffSchedule={[{ tariffId: 'historical', effectiveFrom: '2026-09-01', effectiveTo: null, rate: 0 }]}
      onUpdateTariffSchedule={save} onUpdateWithTariff={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить', exact: true }))
    expect(await screen.findByText('Для каждого периода укажите тариф больше нуля.')).toBeInTheDocument()
    expect(save).not.toHaveBeenCalled()
  })
  it('does not confuse a failed schedule load with an empty schedule and supports retry', () => {
    const onClose = vi.fn()
    const onRetryTariffSchedule = vi.fn()
    const { rerender } = render(<AddServicePrototypeDialog isSaving={false} funds={[]} incomeTypes={[]} tariffs={[]}
      onClose={onClose} onRetryTariffSchedule={onRetryTariffSchedule} tariffScheduleError="Сетка временно недоступна" />)
    expect(screen.getByRole('alert')).toHaveTextContent('Сетка временно недоступна')
    expect(screen.queryByLabelText('Наименование услуги')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Сохранить' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
    expect(onRetryTariffSchedule).toHaveBeenCalledOnce()
    expect(onClose).not.toHaveBeenCalled()
    rerender(<AddServicePrototypeDialog isSaving={false} funds={[]} incomeTypes={[]} tariffs={[]}
      onClose={onClose} onRetryTariffSchedule={onRetryTariffSchedule} tariffScheduleError={null} tariffScheduleLoading />)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('status', { name: 'Загрузка тарифной сетки' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Отмена' }))
    expect(onClose).toHaveBeenCalledOnce()
    rerender(<AddServicePrototypeDialog isSaving={false} funds={[]} incomeTypes={[]} tariffs={[]}
      onClose={onClose} tariffScheduleError="Не удалось загрузить периоды" />)
    fireEvent.click(screen.getByRole('button', { name: 'Закрыть' }))
    expect(onClose).toHaveBeenCalledTimes(2)
  })

  it('waits for the tariff schedule before allowing edits and still allows canceling the load', () => {
    const onClose = vi.fn()
    const props = {
      initialSetting: {
        id: 'service-loading', name: 'Охрана', isRegular: true, periodicityMonths: 1,
        accrualStartMonth: 1, paymentDueDay: 20, paymentDueMonth: null, overdueGraceDays: 0,
        incomeTypeId: 'income-loading', tariffId: null, isMetered: false,
        hasTieredTariff: false, unitName: null, isArchived: false, version: 'service-version',
      },
      isSaving: false, funds: [], incomeTypes: [], measurementUnits: [], tariffs: [], onClose,
    }
    const { rerender } = render(<AddServicePrototypeDialog {...props} tariffScheduleLoading />)
    const status = screen.getByRole('status', { name: 'Загрузка тарифной сетки' })
    expect(status).toHaveClass('loading-skeleton')
    expect(status).toHaveAttribute('aria-live', 'polite')
    expect(screen.queryByLabelText('Наименование услуги')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Сохранить' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Закрыть форму услуги' })).toBeEnabled()
    fireEvent.click(screen.getByRole('button', { name: 'Отмена' }))
    expect(onClose).toHaveBeenCalledOnce()
    rerender(<AddServicePrototypeDialog {...props} tariffScheduleLoading={false} tariffSchedule={[]} />)
    expect(screen.queryByRole('status', { name: 'Загрузка тарифной сетки' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Наименование услуги')).toHaveValue('Охрана')
    expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled()
  })

  it('keeps a new irregular service compact and places the regularity switch on the left side of the action row', () => {
    render(<AddServicePrototypeDialog
      isSaving={false}
      funds={[{ id: 'fund-1', name: 'Водоснабжение', allowOperations: true }]}
      incomeTypes={[]}
      measurementUnits={[]}
      tariffs={[]}
      onClose={vi.fn()}
      onSaveIrregular={vi.fn()}
    />)

    const dialog = screen.getByRole('dialog', { name: 'Добавить услугу' })
    const regularitySwitch = screen.getByRole('checkbox', { name: 'Регулярные платежи' })
    const closeButton = screen.getByRole('button', { name: 'Закрыть форму услуги' })

    expect(dialog).toHaveClass('contractors-service-dialog--compact')
    const actionRow = regularitySwitch.closest('.detail-dialog-actions')
    expect(regularitySwitch.closest('.detail-dialog-header')).toBeNull()
    expect(actionRow).not.toBeNull()
    expect(actionRow?.firstElementChild).toContainElement(regularitySwitch)
    expect(actionRow?.children[1]).toBe(screen.getByRole('button', { name: 'Сохранить' }))
    expect(actionRow?.children[2]).toBe(screen.getByRole('button', { name: 'Отмена' }))
    expect(closeButton.closest('.detail-dialog-header')).not.toBeNull()

    fireEvent.click(regularitySwitch)

    expect(dialog).toHaveClass('contractors-service-dialog--regular')
    expect(dialog).not.toHaveClass('contractors-service-dialog--compact')
    expect(screen.getByRole('heading', { name: 'Начальный тариф' })).toBeInTheDocument()

    fireEvent.click(regularitySwitch)

    expect(dialog).toHaveClass('contractors-service-dialog--compact')
    expect(screen.queryByRole('heading', { name: 'Начальный тариф' })).not.toBeInTheDocument()
  })

  it('uses the two-column tariff layout when a regular service is created', () => {
    render(<AddServicePrototypeDialog
      isSaving={false}
      funds={[{ id: 'fund-1', name: 'Водоснабжение', allowOperations: true }]}
      incomeTypes={[]}
      measurementUnits={[{ id: 'unit-1', name: 'м³', isArchived: false, version: 'unit-version' }]}
      tariffs={[]}
      onClose={vi.fn()}
      onCreateWithTariff={vi.fn()}
    />)

    fireEvent.click(screen.getByRole('checkbox', { name: 'Регулярные платежи' }))

    const form = screen.getByRole('tabpanel', { name: 'Тариф и периоды' })
    expect(form).toHaveClass('contractors-modal-form--service-edit')
    expect(screen.getByRole('heading', { name: 'Настройки услуги' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Начальный тариф' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Параметры начисления' })).toBeInTheDocument()
    expect(screen.getByLabelText('Тариф регулярной услуги')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('combobox', { name: 'Единица измерения' }))
    expect(screen.getByRole('listbox', { name: 'Единица измерения: варианты' })).toHaveClass('select-control__list--above')
  })

  it('показывает интервалы и сохраняет сетку с разрешённым промежутком без отдельного подтверждения', async () => {
    const onUpdateTariffSchedule = vi.fn().mockResolvedValue([
      { tariffId: 'tariff-1', effectiveFrom: '2026-01-01', effectiveTo: '2026-06-30', rate: 101, tariffVersion: 'tariff-version-1' },
      { tariffId: 'tariff-2', effectiveFrom: '2026-08-01', effectiveTo: null, rate: 102, tariffVersion: 'tariff-version-2' },
    ])

    render(<AddServicePrototypeDialog
      initialSetting={{
        id: 'service-1', name: 'Вода', isRegular: true, periodicityMonths: 1, accrualStartMonth: 1,
        paymentDueDay: 30, paymentDueMonth: null, overdueGraceDays: 30, incomeTypeId: 'income-1',
        tariffId: 'tariff-1', isMetered: true, hasTieredTariff: false, unitName: 'м³', isArchived: false,
        version: 'service-version-1',
      }}
      isSaving={false}
      funds={[{ id: 'fund-1', name: 'Водоснабжение', allowOperations: true }]}
      incomeTypes={[{ id: 'income-1', name: 'Вода', code: 'water', isArchived: false, destinationFundId: 'fund-1', destinationFundName: 'Водоснабжение' }]}
      measurementUnits={[]}
      tariffs={[{
        id: 'tariff-1', name: 'Вода', calculationBase: 'meter_water', rate: 101,
        electricityFirstThreshold: null, electricitySecondThreshold: null, electricityFirstTierName: null,
        electricitySecondTierName: null, electricityThirdTierName: null, electricityFirstRate: null,
        electricitySecondRate: null, electricityThirdRate: null, effectiveFrom: '2026-01-01', comment: null,
        isArchived: false, version: 'tariff-version-1',
      }]}
      tariffSchedule={[
        { tariffId: 'tariff-1', effectiveFrom: '2026-01-01', effectiveTo: '2026-06-30', rate: 101, tariffVersion: 'tariff-version-1' },
        { tariffId: 'tariff-2', effectiveFrom: '2026-08-01', effectiveTo: null, rate: 102, tariffVersion: 'tariff-version-2' },
      ]}
      onClose={vi.fn()}
      onUpdateWithTariff={vi.fn()}
      onUpdateTariffSchedule={onUpdateTariffSchedule}
    />)

    expect(screen.getByRole('heading', { name: 'Изменение тарифов по периодам' })).toBeInTheDocument()
    expect(screen.getByRole('table', { name: 'Тарифная сетка услуги' })).toBeInTheDocument()
    expect(screen.queryByRole('checkbox', { name: 'Регулярные платежи' })).not.toBeInTheDocument()
    const form = screen.getByRole('tabpanel', { name: 'Тариф и периоды' })
    const settingsColumn = screen.getByRole('heading', { name: 'Настройки услуги' }).closest('.contractors-service-settings-column')
    const scheduleEditor = screen.getByRole('heading', { name: 'Изменение тарифов по периодам' }).closest('.tariff-schedule-editor')
    expect(form).toHaveClass('contractors-modal-form--service-edit')
    expect(settingsColumn).not.toBeNull()
    expect(settingsColumn).toContainElement(screen.getByLabelText('Наименование услуги'))
    expect(settingsColumn).toContainElement(screen.getByRole('heading', { name: 'Параметры начисления' }))
    expect(settingsColumn).toContainElement(screen.getByRole('checkbox', { name: 'По счетчику' }))
    expect(settingsColumn).not.toContainElement(scheduleEditor)
    expect(scheduleEditor?.parentElement).toBe(form)
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить', exact: true }))

    await waitFor(() => expect(onUpdateTariffSchedule).toHaveBeenCalledWith(expect.objectContaining({
      allowGaps: true,
      serviceVersion: 'service-version-1',
      periods: [
        expect.objectContaining({ effectiveFrom: '2026-01-01', effectiveTo: '2026-06-30', rate: 101 }),
        expect.objectContaining({ effectiveFrom: '2026-08-01', effectiveTo: null, rate: 102 }),
      ],
    })))
    expect(await screen.findByText('Тарифная сетка сохранена.')).toBeInTheDocument()
  })

  it('не переносит будущую ставку на действующий период при сохранении карточки услуги', async () => {
    const user = userEvent.setup()
    const onUpdateWithTariff = vi.fn().mockResolvedValue(undefined)
    const onUpdateTariffSchedule = vi.fn().mockImplementation(async (request) => request.periods.map((period: {
      tariffId: string | null
      effectiveFrom: string | null
      effectiveTo: string | null
      rate: number
      tariffVersion: string | null
    }, index: number) => ({
      ...period,
      tariffId: period.tariffId ?? `tariff-${index + 1}`,
      tariffVersion: period.tariffVersion ?? `tariff-version-${index + 1}`,
    })))

    render(<AddServicePrototypeDialog
      initialSetting={{
        id: 'service-1', name: 'Охрана', isRegular: true, periodicityMonths: 1, accrualStartMonth: 1,
        paymentDueDay: 30, paymentDueMonth: null, overdueGraceDays: 30, incomeTypeId: 'income-1',
        tariffId: 'tariff-1', isMetered: false, hasTieredTariff: false, unitName: 'руб.', isArchived: false,
        version: 'service-version-1',
      }}
      isSaving={false}
      funds={[{ id: 'fund-1', name: 'Прочее', allowOperations: true }]}
      incomeTypes={[{ id: 'income-1', name: 'Охрана', code: 'other_income', isArchived: false, destinationFundId: 'fund-1', destinationFundName: 'Прочее' }]}
      measurementUnits={[]}
      tariffs={[{
        id: 'tariff-1', name: 'Охрана', calculationBase: 'fixed', rate: 101,
        electricityFirstThreshold: null, electricitySecondThreshold: null, electricityFirstTierName: null,
        electricitySecondTierName: null, electricityThirdTierName: null, electricityFirstRate: null,
        electricitySecondRate: null, electricityThirdRate: null, effectiveFrom: '2026-09-01', comment: null,
        isArchived: false, version: 'tariff-version-1',
      }]}
      tariffSchedule={[
        { tariffId: 'tariff-1', effectiveFrom: '2026-09-01', effectiveTo: '2026-09-30', rate: 101, tariffVersion: 'tariff-version-1' },
        { tariffId: 'tariff-2', effectiveFrom: '2026-10-01', effectiveTo: null, rate: 102, tariffVersion: 'tariff-version-2' },
      ]}
      onClose={vi.fn()}
      onUpdateWithTariff={onUpdateWithTariff}
      onUpdateTariffSchedule={onUpdateTariffSchedule}
    />)

    const rateInputs = screen.getAllByLabelText('Тариф регулярной услуги')
    await user.clear(rateInputs[1])
    await user.type(rateInputs[1], '150')
    await user.click(screen.getByRole('button', { name: 'Сохранить', exact: true }))
    expect(await screen.findByText('Тарифная сетка сохранена.')).toBeInTheDocument()

    expect(onUpdateTariffSchedule).toHaveBeenCalledWith(expect.objectContaining({
      periods: [expect.objectContaining({ rate: 101 }), expect.objectContaining({ rate: 150 })],
    }))
    expect(onUpdateWithTariff).not.toHaveBeenCalled()
  })

  it('не отправляет повторный запрос при двух нажатиях до завершения сохранения', async () => {
    const savedPeriods = [
      { tariffId: 'tariff-1', effectiveFrom: '2026-01-01', effectiveTo: null, rate: 101, tariffVersion: 'tariff-version-2' },
    ]
    let resolveSave!: (periods: typeof savedPeriods) => void
    const onUpdateTariffSchedule = vi.fn().mockImplementation(() => new Promise<typeof savedPeriods>((resolve) => {
      resolveSave = resolve
    }))

    render(<AddServicePrototypeDialog
      initialSetting={{
        id: 'service-1', name: 'Вода', isRegular: true, periodicityMonths: 1, accrualStartMonth: 1,
        paymentDueDay: 30, paymentDueMonth: null, overdueGraceDays: 30, incomeTypeId: 'income-1',
        tariffId: 'tariff-1', isMetered: true, hasTieredTariff: false, unitName: 'м³', isArchived: false,
        version: 'service-version-1',
      }}
      isSaving={false}
      funds={[{ id: 'fund-1', name: 'Водоснабжение', allowOperations: true }]}
      incomeTypes={[{ id: 'income-1', name: 'Вода', code: 'water', isArchived: false, destinationFundId: 'fund-1', destinationFundName: 'Водоснабжение' }]}
      measurementUnits={[]}
      tariffs={[{
        id: 'tariff-1', name: 'Вода', calculationBase: 'meter_water', rate: 101,
        electricityFirstThreshold: null, electricitySecondThreshold: null, electricityFirstTierName: null,
        electricitySecondTierName: null, electricityThirdTierName: null, electricityFirstRate: null,
        electricitySecondRate: null, electricityThirdRate: null, effectiveFrom: '2026-01-01', comment: null,
        isArchived: false, version: 'tariff-version-1',
      }]}
      tariffSchedule={savedPeriods}
      onClose={vi.fn()}
      onUpdateWithTariff={vi.fn()}
      onUpdateTariffSchedule={onUpdateTariffSchedule}
    />)

    const saveButton = screen.getByRole('button', { name: 'Сохранить', exact: true })
    await act(async () => {
      saveButton.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      saveButton.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    })

    expect(onUpdateTariffSchedule).toHaveBeenCalledTimes(1)
    expect(saveButton).toBeDisabled()

    resolveSave(savedPeriods)
    expect(await screen.findByText('Тарифная сетка сохранена.')).toBeInTheDocument()
    expect(saveButton).toBeEnabled()
  })

  it('после ошибки снимает блокировку и позволяет повторить сохранение', async () => {
    const savedPeriods = [
      { tariffId: 'tariff-1', effectiveFrom: '2026-01-01', effectiveTo: null, rate: 101, tariffVersion: 'tariff-version-2' },
    ]
    const onUpdateTariffSchedule = vi.fn()
      .mockRejectedValueOnce(new Error('Не удалось сохранить тарифную сетку.'))
      .mockResolvedValueOnce(savedPeriods)

    render(<AddServicePrototypeDialog
      initialSetting={{
        id: 'service-1', name: 'Вода', isRegular: true, periodicityMonths: 1, accrualStartMonth: 1,
        paymentDueDay: 30, paymentDueMonth: null, overdueGraceDays: 30, incomeTypeId: 'income-1',
        tariffId: 'tariff-1', isMetered: true, hasTieredTariff: false, unitName: 'м³', isArchived: false,
        version: 'service-version-1',
      }}
      isSaving={false}
      funds={[{ id: 'fund-1', name: 'Водоснабжение', allowOperations: true }]}
      incomeTypes={[{ id: 'income-1', name: 'Вода', code: 'water', isArchived: false, destinationFundId: 'fund-1', destinationFundName: 'Водоснабжение' }]}
      measurementUnits={[]}
      tariffs={[{
        id: 'tariff-1', name: 'Вода', calculationBase: 'meter_water', rate: 101,
        electricityFirstThreshold: null, electricitySecondThreshold: null, electricityFirstTierName: null,
        electricitySecondTierName: null, electricityThirdTierName: null, electricityFirstRate: null,
        electricitySecondRate: null, electricityThirdRate: null, effectiveFrom: '2026-01-01', comment: null,
        isArchived: false, version: 'tariff-version-1',
      }]}
      tariffSchedule={savedPeriods}
      onClose={vi.fn()}
      onUpdateWithTariff={vi.fn()}
      onUpdateTariffSchedule={onUpdateTariffSchedule}
    />)

    const saveButton = screen.getByRole('button', { name: 'Сохранить', exact: true })
    fireEvent.click(saveButton)
    expect(await screen.findByText('Не удалось сохранить тарифную сетку.')).toBeInTheDocument()
    expect(saveButton).toBeEnabled()

    fireEvent.click(saveButton)
    expect(await screen.findByText('Тарифная сетка сохранена.')).toBeInTheDocument()
    expect(onUpdateTariffSchedule).toHaveBeenCalledTimes(2)
  })

  it('отправляет отсутствующие идентификаторы нового тарифного периода как null', async () => {
    const user = userEvent.setup()
    const onUpdateTariffSchedule = vi.fn().mockResolvedValue([])

    render(<AddServicePrototypeDialog
      initialSetting={{
        id: 'service-1', name: 'Вода', isRegular: true, periodicityMonths: 1, accrualStartMonth: 1,
        paymentDueDay: 30, paymentDueMonth: null, overdueGraceDays: 30, incomeTypeId: 'income-1',
        tariffId: 'tariff-1', isMetered: true, hasTieredTariff: false, unitName: 'м³', isArchived: false,
        version: 'service-version-1',
      }}
      isSaving={false}
      funds={[{ id: 'fund-1', name: 'Водоснабжение', allowOperations: true }]}
      incomeTypes={[{ id: 'income-1', name: 'Вода', code: 'water', isArchived: false, destinationFundId: 'fund-1', destinationFundName: 'Водоснабжение' }]}
      measurementUnits={[]}
      tariffs={[{
        id: 'tariff-1', name: 'Вода', calculationBase: 'meter_water', rate: 101,
        electricityFirstThreshold: null, electricitySecondThreshold: null, electricityFirstTierName: null,
        electricitySecondTierName: null, electricityThirdTierName: null, electricityFirstRate: null,
        electricitySecondRate: null, electricityThirdRate: null, effectiveFrom: '2026-01-01', comment: null,
        isArchived: false, version: 'tariff-version-1',
      }]}
      tariffSchedule={[{ tariffId: 'tariff-1', effectiveFrom: '2026-01-01', effectiveTo: '2026-06-30', rate: 101, tariffVersion: 'tariff-version-1' }]}
      onClose={vi.fn()}
      onUpdateWithTariff={vi.fn()}
      onUpdateTariffSchedule={onUpdateTariffSchedule}
    />)

    fireEvent.click(screen.getByRole('button', { name: 'Добавить период тарифа' }))
    await user.type(screen.getByLabelText('Начальная дата тарифа'), '01.07.2026')
    await user.click(screen.getByRole('button', { name: 'Сохранить', exact: true }))

    await waitFor(() => expect(onUpdateTariffSchedule).toHaveBeenCalledWith(expect.objectContaining({
      periods: expect.arrayContaining([
        expect.objectContaining({ tariffId: null, tariffVersion: null, effectiveFrom: '2026-07-01' }),
      ]),
    })))
  })

  it('заменяет тарифные периоды порогами без дополнительного разрыва в форме', () => {
    render(<AddServicePrototypeDialog
      initialSetting={{
        id: 'service-1', name: 'Вода', isRegular: true, periodicityMonths: 1, accrualStartMonth: 1,
        paymentDueDay: 30, paymentDueMonth: null, overdueGraceDays: 30, incomeTypeId: 'income-1',
        tariffId: 'tariff-1', isMetered: true, hasTieredTariff: false, unitName: 'м³', isArchived: false,
        version: 'service-version-1',
      }}
      isSaving={false}
      funds={[{ id: 'fund-1', name: 'Водоснабжение', allowOperations: true }]}
      incomeTypes={[{ id: 'income-1', name: 'Вода', code: 'water', isArchived: false, destinationFundId: 'fund-1', destinationFundName: 'Водоснабжение' }]}
      measurementUnits={[]}
      tariffs={[{
        id: 'tariff-1', name: 'Вода', calculationBase: 'meter_water', rate: 101,
        electricityFirstThreshold: null, electricitySecondThreshold: null, electricityFirstTierName: null,
        electricitySecondTierName: null, electricityThirdTierName: null, electricityFirstRate: null,
        electricitySecondRate: null, electricityThirdRate: null, effectiveFrom: '2026-01-01', comment: null,
        isArchived: false, version: 'tariff-version-1',
      }]}
      tariffSchedule={[{ tariffId: 'tariff-1', effectiveFrom: '2026-01-01', effectiveTo: '2026-12-31', rate: 101, tariffVersion: 'tariff-version-1' }]}
      onClose={vi.fn()}
      onUpdateWithTariff={vi.fn()}
      onUpdateTariffSchedule={vi.fn()}
    />)

    const form = screen.getByRole('tabpanel', { name: 'Тариф и периоды' })
    expect(screen.getByRole('heading', { name: 'Изменение тарифов по периодам' })).toBeInTheDocument()
    expect(screen.queryByRole('checkbox', { name: 'Разрешить периоды без тарифа' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('checkbox', { name: 'Пороговая тарификация' }))

    expect(screen.queryByRole('heading', { name: 'Изменение тарифов по периодам' })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Пороги и тарифы' })).toBeInTheDocument()
    expect(form).toHaveClass('contractors-modal-form--service-edit-tiered')
  })

  describe('пороговые услуги', () => {
    const tiersA = [
      { id: '11111111-1111-4111-8111-111111111111', name: 'Ступень 1', upperBound: 100, rate: 7.47, isCustom: false },
      { id: '22222222-2222-4222-8222-222222222222', name: 'Ступень 2', upperBound: null, rate: 10.17, isCustom: false },
    ]
    const tiersB = [
      { id: '33333333-3333-4333-8333-333333333333', name: 'Ступень 1', upperBound: 120, rate: 8.31, isCustom: false },
      { id: '44444444-4444-4444-8444-444444444444', name: 'Ступень 2', upperBound: null, rate: 11.7, isCustom: false },
    ]
    const periods = [
      { tariffId: 'tariff-a', effectiveFrom: '2026-01-01', effectiveTo: '2026-09-30', rate: 7.47, tariffVersion: 'va', electricityTiers: tiersA },
      { tariffId: 'tariff-b', effectiveFrom: '2026-10-01', effectiveTo: null, rate: 8.31, tariffVersion: 'vb', electricityTiers: tiersB },
    ]

    function renderTiered(overrides: { onUpdateTariffSchedule?: ReturnType<typeof vi.fn>; onUpdateWithTariff?: ReturnType<typeof vi.fn> } = {}) {
      const onUpdateTariffSchedule = overrides.onUpdateTariffSchedule ?? vi.fn().mockResolvedValue(periods)
      const onUpdateWithTariff = overrides.onUpdateWithTariff ?? vi.fn()
      render(<AddServicePrototypeDialog
        initialSetting={{
          id: 'service-tiered', name: 'Электроэнергия', isRegular: true, periodicityMonths: 1, accrualStartMonth: 1,
          paymentDueDay: 30, paymentDueMonth: null, overdueGraceDays: 30, incomeTypeId: 'income-1',
          tariffId: 'tariff-b', isMetered: true, hasTieredTariff: true, unitName: 'кВт·ч', isArchived: false, version: 'service-version',
        }}
        isSaving={false}
        funds={[{ id: 'fund-1', name: 'Электроэнергия', allowOperations: true }]}
        incomeTypes={[{ id: 'income-1', name: 'Электроэнергия', code: 'electricity', isArchived: false, destinationFundId: 'fund-1', destinationFundName: 'Электроэнергия' }]}
        measurementUnits={[]}
        tariffs={[]}
        tariffSchedule={periods}
        onClose={vi.fn()}
        onUpdateWithTariff={onUpdateWithTariff}
        onUpdateTariffSchedule={onUpdateTariffSchedule}
      />)
      return { onUpdateTariffSchedule, onUpdateWithTariff }
    }

    it('показывает даты и пороги каждой версии и сохраняет их через тарифную сетку', async () => {
      const { onUpdateTariffSchedule, onUpdateWithTariff } = renderTiered()

      expect(screen.getByRole('heading', { name: 'Изменение тарифов по периодам' })).toBeInTheDocument()
      expect(screen.getAllByLabelText('Конечная дата тарифа')).toHaveLength(2)
      expect(screen.getByRole('heading', { name: 'Пороги и тарифы с 01.10.2026' })).toBeInTheDocument()
      expect(screen.getByLabelText('Ступень 1: верхняя граница')).toHaveValue('120')

      fireEvent.click(screen.getByRole('button', { name: 'Пороги периода с 01.01.2026' }))
      expect(screen.getByRole('heading', { name: 'Пороги и тарифы с 01.01.2026' })).toBeInTheDocument()
      expect(screen.getByLabelText('Ступень 1: верхняя граница')).toHaveValue('100')
      fireEvent.change(screen.getByLabelText('Ступень 2: цена за единицу'), { target: { value: '10,50' } })

      fireEvent.click(screen.getByRole('button', { name: 'Сохранить', exact: true }))
      await waitFor(() => expect(onUpdateTariffSchedule).toHaveBeenCalledOnce())
      const request = onUpdateTariffSchedule.mock.calls[0][0]
      expect(request.periods).toHaveLength(2)
      expect(request.periods[0]).toMatchObject({ tariffId: 'tariff-a', effectiveFrom: '2026-01-01', effectiveTo: '2026-09-30', rate: 7.47 })
      expect(request.periods[0].electricityTiers).toEqual([
        { id: tiersA[0].id, name: 'Ступень 1', upperBound: 100, rate: 7.47 },
        { id: tiersA[1].id, name: 'Ступень 2', upperBound: undefined, rate: 10.5 },
      ])
      expect(request.periods[1]).toMatchObject({ tariffId: 'tariff-b', effectiveFrom: '2026-10-01', rate: 8.31 })
      expect(request.periods[1].electricityTiers[0]).toMatchObject({ upperBound: 120, rate: 8.31 })
      expect(onUpdateWithTariff).not.toHaveBeenCalled()
    })

    it('добавляет период с копией порогов и датой смены тарифа', async () => {
      const user = userEvent.setup()
      const { onUpdateTariffSchedule } = renderTiered()
      fireEvent.click(screen.getByRole('button', { name: 'Добавить период тарифа' }))

      expect(screen.getByRole('heading', { name: 'Пороги и тарифы' })).toBeInTheDocument()
      expect(screen.getByLabelText('Ступень 1: верхняя граница')).toHaveValue('120')
      const startDates = screen.getAllByLabelText('Начальная дата тарифа')
      await user.type(startDates[startDates.length - 1], '01.01.2027')
      await user.type(screen.getAllByLabelText('Конечная дата тарифа')[1], '31.12.2026')
      fireEvent.change(screen.getByLabelText('Ступень 1: верхняя граница'), { target: { value: '130' } })

      fireEvent.click(screen.getByRole('button', { name: 'Сохранить', exact: true }))
      await waitFor(() => expect(onUpdateTariffSchedule).toHaveBeenCalledOnce())
      const request = onUpdateTariffSchedule.mock.calls[0][0]
      expect(request.periods).toHaveLength(3)
      expect(request.periods[2]).toMatchObject({ tariffId: null, effectiveFrom: '2027-01-01', effectiveTo: null })
      expect(request.periods[2].electricityTiers[0]).toMatchObject({ id: undefined, upperBound: 130, rate: 8.31 })
      expect(request.periods[1].electricityTiers[0].upperBound).toBe(120)
    })

    it('не отправляет период с некорректными порогами и называет период в сообщении', async () => {
      const { onUpdateTariffSchedule } = renderTiered()
      fireEvent.click(screen.getByRole('button', { name: 'Пороги периода с 01.01.2026' }))
      fireEvent.change(screen.getByLabelText('Ступень 1: верхняя граница'), { target: { value: '' } })
      fireEvent.click(screen.getByRole('button', { name: 'Пороги периода с 01.10.2026' }))

      fireEvent.click(screen.getByRole('button', { name: 'Сохранить', exact: true }))

      expect(await screen.findByText('Период с 01.01.2026: В ступени 1 укажите верхнюю границу не меньше 0.')).toBeInTheDocument()
      expect(screen.getByRole('heading', { name: 'Пороги и тарифы с 01.01.2026' })).toBeInTheDocument()
      expect(onUpdateTariffSchedule).not.toHaveBeenCalled()
    })

    it('для нового порогового тарифа даёт выбрать дату начала действия', async () => {
      const onCreateWithTariff = vi.fn().mockResolvedValue(undefined)
      render(<AddServicePrototypeDialog
        isSaving={false}
        funds={[{ id: 'fund-1', name: 'Электроэнергия', allowOperations: true }]}
        incomeTypes={[]}
        measurementUnits={[]}
        tariffs={[]}
        onClose={vi.fn()}
        onCreateWithTariff={onCreateWithTariff}
      />)
      fireEvent.click(screen.getByRole('checkbox', { name: 'Регулярные платежи' }))
      fireEvent.change(screen.getByLabelText('Наименование услуги'), { target: { value: 'Снег' } })
      fireEvent.click(screen.getByRole('checkbox', { name: 'По счетчику' }))
      fireEvent.click(screen.getByRole('checkbox', { name: 'Пороговая тарификация' }))

      const startDate = screen.getByLabelText('Ставка с')
      const user = userEvent.setup()
      await user.clear(startDate)
      await user.type(startDate, '01.11.2026')

      expect(startDate).toHaveValue('01.11.2026')
    })
  })
})
