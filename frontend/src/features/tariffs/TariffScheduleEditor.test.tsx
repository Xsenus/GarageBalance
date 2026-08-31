// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { AddServicePrototypeDialog } from './TariffsAndFeesPanel'

describe('редактор тарифной сетки услуги', () => {
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

    const form = screen.getByLabelText('Наименование услуги').closest('form')
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
    expect(screen.getByLabelText('Наименование услуги').closest('form')).toHaveClass('contractors-modal-form--service-edit')
    expect(screen.getByRole('heading', { name: 'Настройки услуги' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Параметры начисления' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить тарифную сетку' }))

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

    const saveButton = screen.getByRole('button', { name: 'Сохранить тарифную сетку' })
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

    const saveButton = screen.getByRole('button', { name: 'Сохранить тарифную сетку' })
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
    await user.click(screen.getByRole('button', { name: 'Сохранить тарифную сетку' }))

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

    const form = screen.getByLabelText('Наименование услуги').closest('form')
    expect(screen.getByRole('heading', { name: 'Изменение тарифов по периодам' })).toBeInTheDocument()
    expect(screen.queryByRole('checkbox', { name: 'Разрешить периоды без тарифа' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('checkbox', { name: 'Пороговая тарификация' }))

    expect(screen.queryByRole('heading', { name: 'Изменение тарифов по периодам' })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Пороги и тарифы' })).toBeInTheDocument()
    expect(form).toHaveClass('contractors-modal-form--service-edit-tiered')
  })
})
