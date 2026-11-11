import type { GarageIncomeWorksheetDto, GarageIncomeWorksheetRowDto } from '../../services/financeApi'

function diagnosticKey(row: GarageIncomeWorksheetRowDto): string | null {
  if (!row.incomeTypeId || row.feeCampaignId || row.irregularPaymentId) return null
  return row.annualAccrualId
    ? JSON.stringify(['annual', row.incomeTypeId, row.annualAccrualId])
    : JSON.stringify(['regular', row.accountingMonth.slice(0, 7), row.incomeTypeId])
}

function sameCalculationState(previous: GarageIncomeWorksheetRowDto, current: GarageIncomeWorksheetRowDto) {
  return previous.accrualAmount === current.accrualAmount
    && previous.meterKind === current.meterKind
    && (previous.meterReadingId ?? null) === (current.meterReadingId ?? null)
    && (previous.meterReadingVersion ?? null) === (current.meterReadingVersion ?? null)
    && (previous.meterReadingDate ?? null) === (current.meterReadingDate ?? null)
    && previous.meterValue === current.meterValue
    && previous.meterConsumption === current.meterConsumption
    && (previous.incomeTypeCode ?? null) === (current.incomeTypeCode ?? null)
    && (previous.reason ?? null) === (current.reason ?? null)
    && JSON.stringify(previous.calculationDetails ?? null) === JSON.stringify(current.calculationDetails ?? null)
}

function isVirtualCalculationRow(row: GarageIncomeWorksheetRowDto) {
  return !row.annualAccrualId && !row.meterReadingId && row.meterValue === null
    && row.accrualAmount === 0 && row.incomeAmount === 0 && row.debt === 0
    && (row.payableAmount ?? 0) === 0 && (row.advanceAmount ?? 0) === 0 && (row.discountAmount ?? 0) === 0
    && Boolean(row.calculationIssue || row.meterKind)
}

/**
 * The refresh is an authoritative financial snapshot, not another calculation.
 * Retain only diagnostics from this same completed calculation. Never restore a
 * disappeared monetary row or overwrite newer payment/reading values.
 */
export function preserveWorksheetCalculationDiagnostics(
  calculated: GarageIncomeWorksheetDto,
  refreshed: GarageIncomeWorksheetDto,
): GarageIncomeWorksheetDto {
  if (calculated.garageId !== refreshed.garageId) return refreshed
  const withinPeriod = (row: GarageIncomeWorksheetRowDto) => {
    const month = row.accountingMonth.slice(0, 7)
    return month >= refreshed.monthFrom.slice(0, 7) && month <= refreshed.monthTo.slice(0, 7)
  }
  const calculatedRows = new Map<string, GarageIncomeWorksheetRowDto>()
  for (const row of calculated.rows) {
    const key = diagnosticKey(row)
    if (key && withinPeriod(row)) calculatedRows.set(key, row)
  }
  const representedKeys = new Set<string>()
  // The table uses month + income type for regular/annual display identities.
  // A newly classified annual row must also suppress an older regular placeholder.
  const representedCells = new Set<string>()
  const displayKey = (row: GarageIncomeWorksheetRowDto) => JSON.stringify([row.accountingMonth.slice(0, 7), row.incomeTypeId])
  const rows = refreshed.rows.map((row) => {
    const key = diagnosticKey(row)
    if (!key) return row
    representedKeys.add(key)
    representedCells.add(displayKey(row))
    const previous = calculatedRows.get(key)
    if (!previous?.calculationIssue || row.calculationIssue || !sameCalculationState(previous, row)) return row
    return { ...row, calculationIssue: previous.calculationIssue }
  })
  for (const [key, row] of calculatedRows) {
    if (!representedKeys.has(key) && !representedCells.has(displayKey(row)) && isVirtualCalculationRow(row)) {
      rows.push({ ...row })
      representedKeys.add(key)
      representedCells.add(displayKey(row))
    }
  }
  return { ...refreshed, rows }
}
