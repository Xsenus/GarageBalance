import type { MeterReadingYearGarageDto, MeterReadingYearValueDto } from '../../services/financeApi'
import { getMeterBaselineMonth, getMeterReadingStartDisplay } from './meterYearColumns'

// A lower reading suggests a replacement only within one device's chain. The
// server remains authoritative when a device's initial value is not in the page.
export function getMeterReadingComparisonValue(
  garage: MeterReadingYearGarageDto,
  month: string,
  readings: readonly MeterReadingYearValueDto[],
) {
  const history = readings.filter((reading) => reading.garageId === garage.id)
  const current = history.find((reading) => reading.accountingMonth.slice(0, 7) === month)
  const baselineMonth = getMeterBaselineMonth(garage)
  const inCurrentPeriod = baselineMonth !== null && month > baselineMonth
  const previous = history
    .filter((reading) => reading.accountingMonth.slice(0, 7) < month &&
      (!inCurrentPeriod || reading.accountingMonth.slice(0, 7) > baselineMonth))
    .sort((left, right) => right.accountingMonth.localeCompare(left.accountingMonth))[0]

  if (current) {
    const sameDevice = previous && (previous.meterDeviceId ?? null) === (current.meterDeviceId ?? null)
    // Older payloads may have a replacement marker without device IDs.
    if (sameDevice && !(current.isMeterReplacement && !current.meterDeviceId)) return previous.currentValue
    if (current.meterReplacement && (!current.meterDeviceId || current.meterReplacement.deviceId === current.meterDeviceId)) {
      return current.meterReplacement.newInitialValue
    }
    // The garage baseline carries no device ID. It cannot establish the baseline
    // of a saved physical device when that device has no preceding reading here.
    if (previous || current.isMeterReplacement || current.meterDeviceId) return null
  } else if (previous) {
    return previous.currentValue
  }

  // A baseline can share a display month with a removed device's real reading.
  // Use its value directly, without overwriting that saved history in the table.
  return inCurrentPeriod && baselineMonth.slice(0, 4) === month.slice(0, 4)
    ? getMeterReadingStartDisplay(garage).value
    : null
}
