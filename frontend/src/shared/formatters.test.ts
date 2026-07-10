import { describe, expect, it } from 'vitest'
import type { TariffDto } from '../services/dictionariesApi'
import type { AccessImportRunDto } from '../services/importApi'
import {
  formatAccrualSource,
  formatCount,
  formatDateOnly,
  formatDebtAmount,
  formatDebtLabel,
  formatImportCheckStatus,
  formatImportLogLevel,
  formatImportRunCheckSummary,
  formatImportRunStatus,
  formatMissingMeterReadings,
  formatMoney,
  formatMonth,
  formatMonthInputValue,
  formatNullableNumber,
  formatPaymentAllocations,
  formatReleaseDate,
  formatTariffRateSummary,
  getDebtClassName,
  getLocalDateInputValue,
} from './formatters'

describe('shared formatters', () => {
  it('formats money and debt labels for Russian UI', () => {
    expect(formatMoney(1234.5)).toMatch(/^1\s234,50$/)
    expect(formatDebtLabel(15)).toBe('Задолженность')
    expect(formatDebtLabel(-15)).toBe('Переплата')
    expect(formatDebtAmount(-15.5)).toBe('15,50')
    expect(getDebtClassName(-1)).toBe('money-overpayment')
    expect(getDebtClassName(1)).toBe('money-accrual')
  })

  it('formats date and month values without reparsing date-only strings through UTC', () => {
    const localDate = new Date(2026, 5, 25)

    expect(getLocalDateInputValue(localDate)).toBe('2026-06-25')
    expect(formatMonthInputValue(localDate)).toBe('2026-06')
    expect(formatDateOnly('2026-06-25')).toBe('25.06.2026')
    expect(formatMonth('2026-06-01')).toBe('06.2026')
    expect(formatMonth('2026-06')).toBe('06.2026')
    expect(formatDateOnly('not-a-date')).toBe('not-a-date')
  })

  it('formats tariff rates with and without electricity tiers', () => {
    const fixedTariff = createTariff({ rate: 250 })
    const electricityTariff = createTariff({
      calculationBase: 'meter_electricity',
      rate: 0,
      electricityFirstThreshold: 100,
      electricitySecondThreshold: 250,
      electricityFirstRate: 3.1,
      electricitySecondRate: 4.2,
      electricityThirdRate: 5.3,
    })

    expect(formatTariffRateSummary(fixedTariff)).toBe('250,00')
    expect(formatTariffRateSummary(electricityTariff)).toBe('до 100,00 кВт: 3,10, до 250,00 кВт: 4,20, выше: 5,30')
  })

  it('formats allocations and meter reading gaps compactly', () => {
    expect(formatPaymentAllocations([
      createAllocation('2026-01-01', 100),
      createAllocation('2026-02-01', 200),
      createAllocation('2026-03-01', 300),
      createAllocation('2026-04-01', 400),
    ])).toBe('01.2026 100,00, 02.2026 200,00, 03.2026 300,00 и еще 1')

    expect(formatMissingMeterReadings([
      { garageId: '1', garageNumber: '12', ownerName: 'Иванов', meterKind: 'water', accountingMonth: '2026-06-01' },
      { garageId: '2', garageNumber: '13', ownerName: 'Петров', meterKind: 'electricity', accountingMonth: '2026-06-01' },
    ])).toBe('Гараж 12 - Вода, Гараж 13 - Электричество')
  })

  it('formats import and accrual statuses', () => {
    const run: AccessImportRunDto = {
      id: 'run-1',
      mode: 'dry_run',
      originalFileName: 'old.accdb',
      fileExtension: '.accdb',
      fileSizeBytes: 1024,
      contentSha256: 'hash',
      status: 'completed',
      totalChecks: 4,
      passedChecks: 3,
      warningCount: 1,
      errorCount: 0,
      startedAtUtc: '2026-06-25T01:02:03Z',
      finishedAtUtc: '2026-06-25T01:02:04Z',
      summary: 'Готово',
      checks: [],
    }

    expect(formatAccrualSource('manual')).toBe('Ручное')
    expect(formatAccrualSource('regular')).toBe('Авто')
    expect(formatAccrualSource('import')).toBe('import')
    expect(formatImportRunStatus('completed')).toBe('Завершен')
    expect(formatImportRunStatus('blocked')).toBe('Заблокирован')
    expect(formatImportRunStatus('rollback_requested')).toBe('Rollback запрошен')
    expect(formatImportRunStatus('import_requested')).toBe('Импорт запрошен')
    expect(formatImportRunStatus('import_request_cancelled')).toBe('Заявка отменена')
    expect(formatImportCheckStatus('passed')).toBe('Пройдено')
    expect(formatImportCheckStatus('warning')).toBe('Предупреждение')
    expect(formatImportCheckStatus('error')).toBe('Ошибка')
    expect(formatImportLogLevel('info')).toBe('Инфо')
    expect(formatImportLogLevel('warning')).toBe('Предупреждение')
    expect(formatImportLogLevel('error')).toBe('Ошибка')
    expect(formatImportRunCheckSummary(run)).toBe('3/4 · 1 предупреждение · 0 ошибок')
  })

  it('formats counts and nullable numbers', () => {
    expect(formatCount(1, 'запись', 'записи', 'записей')).toBe('1 запись')
    expect(formatCount(2, 'запись', 'записи', 'записей')).toBe('2 записи')
    expect(formatCount(11, 'запись', 'записи', 'записей')).toBe('11 записей')
    expect(formatNullableNumber(null)).toBe('Не указан')
    expect(formatNullableNumber(12.3456)).toBe('12,346')
  })

  it('formats release dates with Russian locale', () => {
    expect(formatReleaseDate('2026-06-25T01:02:03Z')).toMatch(/25\.06\.2026|24\.06\.2026/)
  })
})

function createTariff(overrides: Partial<TariffDto> = {}): TariffDto {
  return {
    id: 'tariff-1',
    name: 'Тариф',
    calculationBase: 'fixed',
    rate: 100,
    effectiveFrom: '2026-06-01',
    comment: null,
    isArchived: false,
    electricityFirstThreshold: null,
    electricitySecondThreshold: null,
    electricityFirstRate: null,
    electricitySecondRate: null,
    electricityThirdRate: null,
    ...overrides,
  }
}

function createAllocation(accountingMonth: string, paidAmount: number) {
  return {
    allocationKind: 'month' as const,
    accountingMonth,
    label: 'Месяц',
    debtBefore: 1000,
    paidAmount,
    debtAfter: 1000 - paidAmount,
  }
}
