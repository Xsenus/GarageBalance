import { describe, expect, it } from 'vitest'
import type { AccrualCalculationDetailsDto } from '../../services/financeApi'
import { createGarageIncomeRowsFromWorksheet, formatPaymentPrototypeMonthLabel, getAccrualCalculationSummary, getGarageIncomeRowTitle, mergeSavedGarageAccrual, normalizeGarageDebtAfterForHistory, shouldShowAccrualReason } from './garageIncomeWorksheetRows'
import type { GarageIncomePrototypeRow } from './garageIncomeWorksheetRows'
import type { AccrualDto } from '../../services/financeApi'

describe('mergeSavedGarageAccrual', () => {
  const saved = (overrides: Partial<AccrualDto> = {}) => ({
    id: 'accrual-new', incomeTypeId: 'other', incomeTypeName: 'Прочие оплаты', accountingMonth: '2026-09-01',
    amount: 150, basis: 'Ремонт ворот', irregularPaymentId: 'repair', irregularPaymentName: 'Ремонт ворот',
    accountingYear: null, comment: null, ...overrides,
  }) as AccrualDto

  it('preserves the saved catalog identity and basis before the worksheet refresh', () => {
    const [row] = mergeSavedGarageAccrual([], saved(), 'other_payments')
    expect(row).toMatchObject({ irregularPaymentId: 'repair', incomeTypeId: 'other', reason: 'Ремонт ворот', accrued: 150, payable: 150, debt: 150 })
    expect(getGarageIncomeRowTitle(row)).toBe('Основание: Ремонт ворот')
  })

  it('keeps a same-named fee separate and sums repeated accruals with the same identity', () => {
    const [fee] = mergeSavedGarageAccrual([], saved(), 'other_payments')
    fee.irregularPaymentId = null
    fee.feeCampaignId = 'fee-repair'
    const rows = mergeSavedGarageAccrual([fee], saved(), 'other_payments')
    expect(rows).toHaveLength(2)
    expect(rows[0]).toBe(fee)
    const nextRows = mergeSavedGarageAccrual(rows, saved({ id: 'next', amount: 0.15 }), 'other_payments')
    expect(nextRows).toHaveLength(2)
    expect(nextRows[1]).toMatchObject({ accrued: 150.15, payable: 150.15, debt: 150.15 })
  })

  it('aggregates custom bases in one row matching the shared FIFO payment target', () => {
    const rows = mergeSavedGarageAccrual([], saved({ irregularPaymentId: null }), 'other_payments')
    const nextRows = mergeSavedGarageAccrual(rows, saved({ irregularPaymentId: null, basis: 'Другая работа' }), 'other_payments')
    expect(nextRows).toHaveLength(1)
    expect(getGarageIncomeRowTitle(rows[0])).toBe('Основание: Ремонт ворот')
    expect(getGarageIncomeRowTitle(nextRows[0])).toBe('Основание: Ремонт ворот; Другая работа')
    expect(nextRows[0]).toMatchObject({ service: 'Прочие оплаты', accrued: 300, debt: 300 })
  })

  it('combines distinct penalty reasons in the same period without losing the earlier reason', () => {
    const first = saved({ incomeTypeId: 'penalty', incomeTypeName: 'Штраф', irregularPaymentId: null, basis: null, comment: 'Просрочка' })
    const rows = mergeSavedGarageAccrual([], first, 'penalty')
    const nextRows = mergeSavedGarageAccrual(rows, { ...first, id: 'penalty-next', comment: 'Нарушение' }, 'penalty')
    expect(nextRows).toHaveLength(1)
    expect(getGarageIncomeRowTitle(nextRows[0])).toBe('Штраф: Просрочка; Нарушение')
    expect(nextRows[0].accrued).toBe(300)
    const repeatedReason = mergeSavedGarageAccrual(nextRows, { ...first, id: 'penalty-third' }, 'penalty')
    expect(getGarageIncomeRowTitle(repeatedReason[0])).toBe('Штраф: Просрочка; Нарушение')
  })
})

describe('normalizeGarageDebtAfterForHistory', () => {
  it('shows zero remaining debt after an overpayment instead of a negative debt', () => {
    expect(normalizeGarageDebtAfterForHistory(-125.456)).toBe(0)
    expect(normalizeGarageDebtAfterForHistory(125.456)).toBe(125.46)
    expect(normalizeGarageDebtAfterForHistory(null)).toBe(0)
  })
})

describe('createGarageIncomeRowsFromWorksheet', () => {
  it('includes a service overpayment in the visible paid amount', () => {
    const [row] = createGarageIncomeRowsFromWorksheet({
      garageId: 'garage-1', garageNumber: '1', ownerName: 'Тестовый владелец',
      monthFrom: '2026-09-01', monthTo: '2026-09-01', openingBalance: 0, openingDebt: 0,
      unrepresentedOpeningDebt: 0, accrualTotal: 125, incomeTotal: 125.05, advanceTotal: 0.05,
      debtTotal: 0, closingBalance: -0.05, closingDebt: 0,
      rows: [{
        accountingMonth: '2026-09-01', incomeTypeId: 'waste', incomeTypeName: 'Мусор',
        annualAccrualId: null, meterKind: null, meterReadingId: null, meterReadingVersion: null,
        meterReadingDate: null, meterValue: null, meterConsumption: null, accrualAmount: 125,
        payableAmount: 125, incomeAmount: 125, advanceAmount: 0.05, debt: 0,
      }],
    })

    expect(row.paid).toBe(125.05)
    expect(row.advance).toBe(0.05)
    expect(row.debt).toBe(0)
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
  it('uses the catalog name rather than a legacy migration comment for a catalog accrual', () => {
    expect(getGarageIncomeRowTitle({ incomeTypeCode: 'other_payments', irregularPaymentId: 'entry-fee', reason: 'migration-seed', service: 'Вступительный взнос' } as GarageIncomePrototypeRow))
      .toBe('Основание: Вступительный взнос')
  })

  it('shows the business meaning instead of the destination fund for penalties and irregular accruals', () => {
    expect(getGarageIncomeRowTitle({ incomeTypeCode: 'penalty', reason: 'Просрочка оплаты' } as GarageIncomePrototypeRow))
      .toBe('Штраф: Просрочка оплаты')
    expect(getGarageIncomeRowTitle({ incomeTypeCode: 'other_income', irregularPaymentId: 'one', reason: 'Заметка оператора', service: 'Ремонт ворот' } as GarageIncomePrototypeRow))
      .toBe('Основание: Ремонт ворот')
  })
})
