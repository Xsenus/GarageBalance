import { describe, expect, it } from 'vitest'
import type { AccrualCalculationDetailsDto } from '../../services/financeApi'
import { formatPaymentPrototypeMonthLabel, getAccrualCalculationSummary } from './garageIncomeWorksheetRows'

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
