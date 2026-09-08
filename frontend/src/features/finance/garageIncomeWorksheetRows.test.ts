import { describe, expect, it } from 'vitest'
import type { AccrualCalculationDetailsDto } from '../../services/financeApi'
import { formatPaymentPrototypeMonthLabel, getAccrualCalculationSummary, getGarageIncomeRowTitle, normalizeGarageDebtAfterForHistory, shouldShowAccrualReason } from './garageIncomeWorksheetRows'
import type { GarageIncomePrototypeRow } from './garageIncomeWorksheetRows'

describe('normalizeGarageDebtAfterForHistory', () => {
  it('shows zero remaining debt after an overpayment instead of a negative debt', () => {
    expect(normalizeGarageDebtAfterForHistory(-125.456)).toBe(0)
    expect(normalizeGarageDebtAfterForHistory(125.456)).toBe(125.46)
    expect(normalizeGarageDebtAfterForHistory(null)).toBe(0)
  })
})

describe('formatPaymentPrototypeMonthLabel', () => {
  it('formats an accounting month and a date through the same compact label', () => {
    expect(formatPaymentPrototypeMonthLabel('2026-08')).toBe('авг.26')
    expect(formatPaymentPrototypeMonthLabel('2026-08-29')).toBe('авг.26')
  })

  it('keeps an invalid value unchanged', () => {
    expect(formatPaymentPrototypeMonthLabel('август 2026')).toBe('август 2026')
  })
})

describe('getAccrualCalculationSummary', () => {
  it('shows the tariff segment that produced the amount after an initial period without a tariff', () => {
    const details: AccrualCalculationDetailsDto = {
      version: 2,
      accountingMonth: '2026-08-01',
      previousMeterValue: null,
      currentMeterValue: null,
      meterConsumption: null,
      requiresMeter: false,
      volumeAllocationRule: null,
      totalAmount: 116.13,
      lines: [
        {
          effectiveFrom: '2026-08-01',
          effectiveTo: '2026-08-25',
          days: 25,
          monthDays: 31,
          calculationBase: null,
          calculationMode: 'no_tariff',
          unitName: 'руб.',
          rate: 0,
          quantity: 0,
          amount: 0,
          tiers: [],
          formula: 'Тариф на этот участок не задан: 0,00',
          hasTariff: false,
        },
        {
          effectiveFrom: '2026-08-26',
          effectiveTo: '2026-08-31',
          days: 6,
          monthDays: 31,
          calculationBase: 'fixed',
          calculationMode: 'fixed',
          unitName: 'руб.',
          rate: 600,
          quantity: 6 / 31,
          amount: 116.13,
          tiers: [],
          formula: '600 × 6/31 = 116,13',
          hasTariff: true,
        },
      ],
    }

    expect(getAccrualCalculationSummary(details, 'Сохранённое начисление: 116.13'))
      .toBe('600 × 6/31 = 116,13')
  })

  it('keeps a safe fallback when calculation details are unavailable', () => {
    expect(getAccrualCalculationSummary(null, 'Сохранённое начисление: 750.00'))
      .toBe('Сохранённое начисление: 750.00')
  })

  it('shows the complete monthly formula for the arithmetic mean calculation', () => {
    const details: AccrualCalculationDetailsDto = {
      version: 3,
      accountingMonth: '2026-08-01',
      previousMeterValue: 0,
      currentMeterValue: 8,
      meterConsumption: 8,
      requiresMeter: true,
      volumeAllocationRule: null,
      averageRate: 2.5,
      rateAveragingRule: 'Средняя ставка за месяц: (1 + 2 + 3 + 4) / 4 = 2,5. Количество дней действия ставок на среднее не влияет.',
      monthlyCalculationFormula: 'Расчёт за месяц: 8 м³ × 2,5 = 20,00.',
      totalAmount: 20,
      lines: [{
        effectiveFrom: '2026-08-01',
        effectiveTo: '2026-08-20',
        days: 20,
        monthDays: 31,
        calculationBase: 'meter_water',
        calculationMode: 'metered',
        unitName: 'м³',
        rate: 1,
        quantity: 2,
        amount: 2,
        tiers: [],
        formula: 'Равный вес 1/4: 8 × 1 / 4 = 2,00',
        hasTariff: true,
      }],
    }

    expect(getAccrualCalculationSummary(details, 'Сохранённое начисление: 20.00'))
      .toBe('Расчёт за месяц: 8 м³ × 2,5 = 20,00.')
  })
})

describe('shouldShowAccrualReason', () => {
  const row = (incomeTypeCode: string | null, reason: string | null) => ({ incomeTypeCode, reason }) as GarageIncomePrototypeRow

  it('does not repeat a penalty reason that is included in the row title', () => {
    expect(shouldShowAccrualReason(row('penalty', 'Пеня за просрочку'), 'penalties_only')).toBe(false)
    expect(shouldShowAccrualReason(row('water', 'Повторный расчёт'), 'penalties_only')).toBe(false)
  })

  it('shows every available reason in the all mode', () => {
    expect(shouldShowAccrualReason(row('water', 'Повторный расчёт'), 'all')).toBe(true)
    expect(shouldShowAccrualReason(row(null, null), 'all')).toBe(false)
  })

  it('hides every reason in the hidden mode', () => {
    expect(shouldShowAccrualReason(row('penalty', 'Пеня за просрочку'), 'hidden')).toBe(false)
  })
})

describe('getGarageIncomeRowTitle', () => {
  it('shows the business meaning instead of the destination fund for penalties and irregular accruals', () => {
    expect(getGarageIncomeRowTitle({ incomeTypeCode: 'penalty', reason: 'Просрочка оплаты' } as GarageIncomePrototypeRow))
      .toBe('Штраф: Просрочка оплаты')
    expect(getGarageIncomeRowTitle({ incomeTypeCode: 'other_income', irregularPaymentId: 'one', reason: 'Ремонт ворот', service: 'Прочее' } as GarageIncomePrototypeRow))
      .toBe('Основание: Ремонт ворот')
  })
})
