import { describe, expect, it } from 'vitest'
import type { GarageIncomeWorksheetDto, GarageIncomeWorksheetRowDto } from '../../services/financeApi'
import { preserveWorksheetCalculationDiagnostics } from './worksheetRefreshDiagnostics'

const row = (changes: Partial<GarageIncomeWorksheetRowDto> = {}): GarageIncomeWorksheetRowDto => ({
  accountingMonth: '2026-11-01', incomeTypeId: 'water', incomeTypeName: 'Вода',
  meterKind: 'water', meterValue: null, meterConsumption: null,
  accrualAmount: 0, incomeAmount: 0, debt: 0, calculationIssue: 'Нет показания за месяц.',
  ...changes,
})
const worksheet = (rows: GarageIncomeWorksheetRowDto[], changes: Partial<GarageIncomeWorksheetDto> = {}): GarageIncomeWorksheetDto => ({
  garageId: 'garage-a', garageNumber: 'synthetic', ownerName: null,
  monthFrom: '2026-10-01', monthTo: '2026-11-01', openingBalance: 0, openingDebt: 0,
  accrualTotal: 0, incomeTotal: 0, debtTotal: 0, closingBalance: 0, closingDebt: 0,
  rows, ...changes,
})

describe('preserveWorksheetCalculationDiagnostics', () => {
  it('retains completed calculation warnings while the refreshed financial snapshot wins', () => {
    const previous = row({ accrualAmount: 900, payableAmount: 900, debt: 900 })
    const fresh = row({ calculationIssue: null, accrualAmount: 900, incomeAmount: 250, payableAmount: 650, debt: 650, advanceAmount: 25 })
    const calculated = worksheet([previous], { accrualTotal: 900, closingDebt: 900 })
    const refreshed = worksheet([fresh], { accrualTotal: 900, incomeTotal: 275, closingDebt: 650, heldAnnualYears: [2027] })
    const snapshot = JSON.stringify([calculated, refreshed])
    const result = preserveWorksheetCalculationDiagnostics(calculated, refreshed)
    expect(result).toEqual({ ...refreshed, rows: [{ ...fresh, calculationIssue: previous.calculationIssue }] })
    expect(JSON.stringify([calculated, refreshed])).toBe(snapshot)
  })

  it('retains an annual warning by obligation id after its display month changes', () => {
    const calculated = worksheet([row({ meterKind: null, annualAccrualId: 'annual-a', accrualAmount: 900 })])
    const fresh = row({ accountingMonth: '2026-10-01', meterKind: null, annualAccrualId: 'annual-a', accrualAmount: 900, calculationIssue: null })
    expect(preserveWorksheetCalculationDiagnostics(calculated, worksheet([fresh])).rows)
      .toEqual([{ ...fresh, calculationIssue: calculated.rows[0].calculationIssue }])
  })

  it('does not transfer warnings between annual obligations or a regular and annual row', () => {
    const calculated = worksheet([row({ annualAccrualId: 'old-annual' })])
    const fresh = [row({ annualAccrualId: 'new-annual', calculationIssue: null }), row({ calculationIssue: null })]
    expect(preserveWorksheetCalculationDiagnostics(calculated, worksheet(fresh)).rows).toEqual(fresh)
  })

  it('keeps warnings month-scoped for the same regular service', () => {
    const fresh = [row({ accountingMonth: '2026-10-01', calculationIssue: null }), row({ calculationIssue: null })]
    const result = preserveWorksheetCalculationDiagnostics(worksheet([row()]), worksheet(fresh))
    expect(result.rows[0].calculationIssue).toBeNull()
    expect(result.rows[1].calculationIssue).toBe('Нет показания за месяц.')
  })

  it('keeps a newer GET warning instead of replacing it', () => {
    const fresh = row({ calculationIssue: 'Более новое предупреждение.' })
    expect(preserveWorksheetCalculationDiagnostics(worksheet([row()]), worksheet([fresh])).rows[0]).toBe(fresh)
  })

  it.each([
    { accrualAmount: 100 },
    { meterKind: 'electricity' },
    { meterReadingId: 'new-reading' },
    { meterReadingVersion: 'new-version' },
    { meterReadingDate: '2026-11-15' },
    { meterValue: 10 },
    { meterConsumption: 5 },
    { incomeTypeCode: 'changed' },
    { reason: 'Новое основание расчёта' },
  ])('does not attach stale warnings after calculation state changed: %j', (change) => {
    const fresh = row({ calculationIssue: null, ...change })
    expect(preserveWorksheetCalculationDiagnostics(worksheet([row()]), worksheet([fresh])).rows[0]).toBe(fresh)
  })

  it('drops an old warning when calculation details are restored or changed without changing the amount', () => {
    const details = { version: 1, accountingMonth: '2026-11-01', previousMeterValue: null, currentMeterValue: null,
      meterConsumption: null, requiresMeter: false, volumeAllocationRule: null, lines: [], totalAmount: 900 }
    const old = row({ accrualAmount: 900, calculationDetails: null, calculationIssue: 'Историческое основание не определено.' })
    const fresh = row({ accrualAmount: 900, calculationDetails: details, calculationIssue: null })
    expect(preserveWorksheetCalculationDiagnostics(worksheet([old]), worksheet([fresh])).rows[0]).toBe(fresh)
    const changed = { ...fresh, calculationDetails: { ...details, version: 2 } }
    expect(preserveWorksheetCalculationDiagnostics(worksheet([{ ...fresh, calculationIssue: 'Старое пояснение' }]), worksheet([changed])).rows[0]).toBe(changed)
    const matching = { ...fresh, calculationIssue: 'Пояснение того же расчёта' }
    expect(preserveWorksheetCalculationDiagnostics(worksheet([matching]), worksheet([fresh])).rows[0].calculationIssue).toBe(matching.calculationIssue)
  })

  it('keeps zero-valued meter placeholders but never resurrects missing monetary or saved-reading rows', () => {
    const missing = row()
    const excluded = [
      row({ incomeTypeId: 'money', accrualAmount: 100 }),
      row({ incomeTypeId: 'income', incomeAmount: 100 }),
      row({ incomeTypeId: 'debt', debt: 100 }),
      row({ incomeTypeId: 'payable', payableAmount: 100 }),
      row({ incomeTypeId: 'advance', advanceAmount: 100 }),
      row({ incomeTypeId: 'discount', discountAmount: 100 }),
      row({ incomeTypeId: 'annual', annualAccrualId: 'annual-a' }),
      row({ incomeTypeId: 'reading', meterReadingId: 'reading-a' }),
      row({ incomeTypeId: 'value', meterValue: 0 }),
    ]
    const result = preserveWorksheetCalculationDiagnostics(worksheet([missing, ...excluded]), worksheet([]))
    expect(result.rows).toEqual([missing])
    expect(result.accrualTotal).toBe(0)
    expect(result.closingDebt).toBe(0)
  })

  it('does not restore a canceled monetary row when refresh returns no row or zero amount', () => {
    const old = row({ accrualAmount: 900, payableAmount: 900, debt: 900 })
    expect(preserveWorksheetCalculationDiagnostics(worksheet([old]), worksheet([])).rows).toEqual([])
    const zero = row({ calculationIssue: null })
    expect(preserveWorksheetCalculationDiagnostics(worksheet([old]), worksheet([zero])).rows).toEqual([zero])
  })

  it('does not duplicate a display cell when a regular placeholder became an annual obligation', () => {
    const fresh = row({ annualAccrualId: 'new-annual', accrualAmount: 1200, payableAmount: 1200, debt: 1200, calculationIssue: null })
    expect(preserveWorksheetCalculationDiagnostics(worksheet([row()]), worksheet([fresh])).rows).toEqual([fresh])
  })

  it('deduplicates virtual rows and preserves ordinary missing-input explanations', () => {
    const missing = row()
    const noTariff = row({ incomeTypeId: 'fixed', meterKind: null, calculationIssue: 'Не задан тариф.' })
    const silentMeter = row({ incomeTypeId: 'electricity', meterKind: 'electricity', calculationIssue: null })
    const empty = row({ incomeTypeId: 'empty', meterKind: null, calculationIssue: null })
    const result = preserveWorksheetCalculationDiagnostics(worksheet([missing, missing, noTariff, silentMeter, empty]), worksheet([]))
    expect(result.rows).toEqual([missing, noTariff, silentMeter])
    expect(preserveWorksheetCalculationDiagnostics(worksheet([missing]), worksheet([row({ calculationIssue: null })])).rows).toHaveLength(1)
  })

  it('never mixes fee, irregular or unidentified rows with ordinary diagnostics', () => {
    const fee = row({ feeCampaignId: 'campaign' })
    const irregular = row({ irregularPaymentId: 'irregular' })
    const unidentified = row({ incomeTypeId: null })
    expect(preserveWorksheetCalculationDiagnostics(worksheet([fee, irregular, unidentified]), worksheet([])).rows).toEqual([])
    const result = preserveWorksheetCalculationDiagnostics(worksheet([row()]), worksheet([{ ...fee, calculationIssue: null }, { ...irregular, calculationIssue: null }]))
    expect(result.rows.slice(0, 2)).toEqual([{ ...fee, calculationIssue: null }, { ...irregular, calculationIssue: null }])
    expect(result.rows).toHaveLength(3)
  })

  it('does not preserve any diagnostics from another garage or outside the new range', () => {
    const refreshed = worksheet([], { garageId: 'garage-b' })
    expect(preserveWorksheetCalculationDiagnostics(worksheet([row()]), refreshed)).toBe(refreshed)
    const outOfRange = [row({ accountingMonth: '2026-09-01' }), row({ accountingMonth: '2026-12-01' })]
    expect(preserveWorksheetCalculationDiagnostics(worksheet(outOfRange), worksheet([])).rows).toEqual([])
  })

  it('leaves already refreshed rows without a matching warning intact', () => {
    const fresh = row({ calculationIssue: null })
    expect(preserveWorksheetCalculationDiagnostics(worksheet([]), worksheet([fresh])).rows[0]).toBe(fresh)
    expect(preserveWorksheetCalculationDiagnostics(worksheet([row({ calculationIssue: null })]), worksheet([fresh])).rows[0]).toBe(fresh)
  })
})
