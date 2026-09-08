import type { AccrualDto, GarageIncomeWorksheetDto } from '../../services/financeApi'
import type { AccrualCalculationDetailsDto } from '../../services/financeApi'
import type { AccrualReasonDisplayMode } from '../../services/settingsApi'
import { roundPaymentMoney } from './fullPaymentPlan'

const paymentPrototypeMonthLabels = ['янв', 'фев', 'мар', 'апр', 'май', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек']

export type GarageIncomePrototypeRow = {
  id: string
  incomeTypeId: string | null
  month: string
  monthLabel: string
  service: string
  annualAccrualId: string | null
  feeCampaignId?: string | null
  feeCampaignRemainingAmount?: number | null
  irregularPaymentId?: string | null
  irregularPaymentRemainingAmount?: number | null
  meterKind: string | null
  meterReadingId: string | null
  meterReadingVersion: string | null
  meterReadingDate: string | null
  meter: number | null
  meterDraft: string
  meterError: string | null
  difference: number | null
  accrued: number
  payable: number
  paymentDraft: string
  paid: number
  advance: number
  debt: number
  meterRequired?: boolean
  calculationDetails?: AccrualCalculationDetailsDto | null
  reason?: string | null
  incomeTypeCode: string | null
}

export function createGarageIncomeRowsFromWorksheet(worksheet: GarageIncomeWorksheetDto): GarageIncomePrototypeRow[] {
  return worksheet.rows.map((row) => {
    const month = row.accountingMonth.slice(0, 7)
    const rowKey = row.feeCampaignId ?? row.irregularPaymentId ?? row.incomeTypeId ?? row.incomeTypeName.toLocaleLowerCase('ru-RU').replace(/\s+/g, '-')
    return {
      id: `garage-${worksheet.garageId}-${month}-${rowKey}`,
      incomeTypeId: row.incomeTypeId,
      month,
      monthLabel: formatPaymentPrototypeMonthLabel(row.accountingMonth),
      service: row.incomeTypeName,
      annualAccrualId: row.annualAccrualId ?? null,
      feeCampaignId: row.feeCampaignId ?? null,
      feeCampaignRemainingAmount: row.feeCampaignRemainingAmount ?? null,
      irregularPaymentId: row.irregularPaymentId ?? null,
      irregularPaymentRemainingAmount: row.irregularPaymentRemainingAmount ?? null,
      meterKind: row.meterKind,
      meterReadingId: row.meterReadingId ?? null,
      meterReadingVersion: row.meterReadingVersion ?? null,
      meterReadingDate: row.meterReadingDate ?? null,
      meter: row.meterValue,
      meterDraft: row.meterValue === null ? '' : String(row.meterValue),
      meterError: null,
      difference: row.meterConsumption,
      accrued: row.accrualAmount,
      payable: row.payableAmount ?? row.accrualAmount,
      paymentDraft: '',
      paid: row.incomeAmount,
      advance: row.advanceAmount ?? 0,
      debt: row.debt,
      meterRequired: row.meterKind !== null && row.meterValue === null,
      calculationDetails: row.calculationDetails ?? null,
      reason: row.reason ?? null,
      incomeTypeCode: row.incomeTypeCode ?? null,
    }
  })
}

export function mergeSavedGarageAccrual(rows: GarageIncomePrototypeRow[], accrual: AccrualDto, incomeTypeCode: string | null) {
  const month = accrual.accountingMonth.slice(0, 7)
  const service = accrual.irregularPaymentId ? accrual.irregularPaymentName ?? accrual.basis ?? accrual.incomeTypeName : accrual.incomeTypeName
  const reason = accrual.basis ?? accrual.comment
  const existing = rows.find((row) => row.month === month && !row.feeCampaignId
    && row.incomeTypeId === accrual.incomeTypeId
    && (accrual.irregularPaymentId
      ? row.irregularPaymentId === accrual.irregularPaymentId
      : !row.irregularPaymentId && (incomeTypeCode === 'penalty' || incomeTypeCode === 'other_payments' || row.service === service)))
  if (existing) {
    return rows.map((row) => row === existing ? {
      ...row,
      reason: [...new Set([...(row.reason?.split('; ') ?? []), reason].filter(Boolean))].join('; '),
      accrued: roundPaymentMoney(row.accrued + accrual.amount),
      payable: roundPaymentMoney(row.payable + accrual.amount),
      debt: roundPaymentMoney(row.debt + accrual.amount),
      irregularPaymentRemainingAmount: accrual.irregularPaymentId ? roundPaymentMoney(row.debt + accrual.amount) : null,
      calculationDetails: null,
    } : row)
  }
  return [...rows, {
    id: `garage-${accrual.garageId}-${month}-${accrual.irregularPaymentId ?? accrual.incomeTypeId}`,
    month,
    monthLabel: formatPaymentPrototypeMonthLabel(accrual.accountingMonth),
    service,
    incomeTypeId: accrual.incomeTypeId,
    incomeTypeCode,
    annualAccrualId: incomeTypeCode !== 'penalty' && accrual.accountingYear ? accrual.id : null,
    irregularPaymentId: accrual.irregularPaymentId,
    irregularPaymentRemainingAmount: accrual.irregularPaymentId ? accrual.amount : null,
    reason,
    meterKind: null,
    meterReadingId: null,
    meterReadingVersion: null,
    meterReadingDate: null,
    meter: null,
    meterDraft: '',
    meterError: null,
    difference: null,
    accrued: accrual.amount,
    payable: accrual.amount,
    paymentDraft: '',
    paid: 0,
    advance: 0,
    debt: accrual.amount,
  }]
}

export function isFeePaymentClosed(row: Pick<GarageIncomePrototypeRow, 'feeCampaignId' | 'feeCampaignRemainingAmount' | 'debt'>) {
  return Boolean(row.feeCampaignId && (row.debt <= 0 || row.feeCampaignRemainingAmount === 0))
}

export function shouldShowAccrualReason(row: GarageIncomePrototypeRow, mode: AccrualReasonDisplayMode) {
  if (row.irregularPaymentId || row.incomeTypeCode === 'other_payments' || row.incomeTypeCode === 'penalty') {
    return false
  }

  if (!row.reason || mode === 'hidden') {
    return false
  }

  return mode === 'all' || row.incomeTypeCode === 'penalty'
}

export function getGarageIncomeRowTitle(row: GarageIncomePrototypeRow) {
  if (row.incomeTypeCode === 'penalty') {
    return row.reason ? `Штраф: ${row.reason}` : 'Штраф'
  }

  if (row.irregularPaymentId || (row.incomeTypeCode === 'other_payments' && row.reason)) {
    return `Основание: ${row.irregularPaymentId ? row.service : row.reason}`
  }

  return row.service
}

export function normalizeGarageDebtAfterForHistory(value: number | null | undefined) {
  return Math.max(Math.round((value ?? 0) * 100) / 100, 0)
}

export function formatPaymentPrototypeMonthLabel(value: string) {
  const match = /^(\d{4})-(\d{2})(?:-\d{2})?$/.exec(value)
  if (!match) {
    return value
  }

  const monthIndex = Number(match[2]) - 1
  const monthLabel = paymentPrototypeMonthLabels[monthIndex] ?? match[2]
  return `${monthLabel}.${match[1].slice(2)}`
}

export function getAccrualCalculationSummary(
  details: AccrualCalculationDetailsDto | null | undefined,
  fallback: string,
) {
  if (!details || details.lines.length === 0) {
    return fallback
  }

  return details.monthlyCalculationFormula
    ?? details.lines.find((line) => line.hasTariff && line.amount !== 0)?.formula
    ?? details.lines.find((line) => line.hasTariff)?.formula
    ?? details.lines[0]?.formula
    ?? fallback
}
