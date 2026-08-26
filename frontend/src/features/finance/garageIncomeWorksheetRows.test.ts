import { describe, expect, it } from 'vitest'
import type { AccrualCalculationDetailsDto } from '../../services/financeApi'
import { getAccrualCalculationSummary } from './garageIncomeWorksheetRows'

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
})
