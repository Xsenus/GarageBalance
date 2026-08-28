import type { GarageIncomeWorksheetDto } from '../../services/financeApi'
import type { AccrualCalculationDetailsDto } from '../../services/financeApi'

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
  payable: number
  paymentDraft: string
  paid: number
  advance: number
  debt: number
  meterRequired?: boolean
  calculationDetails?: AccrualCalculationDetailsDto | null
  reason?: string | null
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
      payable: row.payableAmount ?? row.accrualAmount,
      paymentDraft: '',
      paid: row.incomeAmount,
      advance: row.advanceAmount ?? 0,
      debt: row.debt,
      meterRequired: row.meterKind !== null && row.meterValue === null,
      calculationDetails: row.calculationDetails ?? null,
      reason: row.reason ?? null,
    }
  })
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

  return details.lines.find((line) => line.hasTariff && line.amount !== 0)?.formula
    ?? details.lines.find((line) => line.hasTariff)?.formula
    ?? details.lines[0]?.formula
    ?? fallback
}
