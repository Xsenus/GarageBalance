import type { MeterReadingYearGarageDto } from '../../services/financeApi'

function getPreviousMonth(month: string) {
  const [year, monthNumber] = month.split('-').map(Number)
  const previous = new Date(Date.UTC(year, monthNumber - 2, 1))
  return `${previous.getUTCFullYear()}-${String(previous.getUTCMonth() + 1).padStart(2, '0')}`
}

// Logical boundary written when meter accounting is restarted; it is not a physical device.
export const meterAccountingRestartSerialPrefix = 'Начало учёта'
const unnumberedMeterSerial = 'Без номера'

function getPreviousMonthOfLatest(...months: (string | null | undefined)[]) {
  const latest = months.map((value) => value?.slice(0, 7)).filter((value): value is string => Boolean(value)).sort().at(-1)
  return latest ? getPreviousMonth(latest) : null
}

// The start value is the reading at the end of the month before the garage's "Учитывать с"
// month, or before a later service connection or meter accounting restart. That month is
// shown as an editable column.
export function getMeterBaselineMonth(garage: MeterReadingYearGarageDto) {
  if (garage.connectedOn) return getPreviousMonthOfLatest(garage.accountingStartMonth, garage.connectedOn)
  if (garage.startValueSource === 'device') {
    return garage.startValueDate ? getPreviousMonthOfLatest(garage.accountingStartMonth, garage.startValueDate) : null
  }
  if (garage.accountingStartMonth) return getPreviousMonth(garage.accountingStartMonth.slice(0, 7))
  if (garage.initialReadingMonth) return garage.initialReadingMonth.slice(0, 7)
  return garage.accountingStartMonth ? getPreviousMonth(garage.accountingStartMonth.slice(0, 7)) : null
}

export function getVisibleMeterReadingMonthKeys(
  year: string,
  garages: readonly MeterReadingYearGarageDto[],
  savedMonths: ReadonlySet<string>,
) {
  if (!/^\d{4}$/.test(year) || Number(year) < 1900) return []
  return Array.from({ length: 12 }, (_, index) => String(index + 1).padStart(2, '0')).filter((key) => {
    const month = `${year}-${key}`
    if (savedMonths.has(month)) return true
    return garages.some((garage) => {
      if (getMeterBaselineMonth(garage) === month) return true
      if (garage.requiresConnectionDate) return false
      const starts = [garage.accountingStartMonth?.slice(0, 7), garage.connectedOn?.slice(0, 7)]
      if (!garage.connectedOn) {
        if (!garage.accountingStartMonth && garage.startValueSource !== 'device' && garage.initialReadingMonth && garage.initialReadingValue != null && month <= garage.initialReadingMonth.slice(0, 7)) return false
        if (garage.startValueSource === 'device' && garage.startValueDate) starts.push(garage.startValueDate.slice(0, 7))
      }
      const start = starts.filter((value): value is string => Boolean(value)).sort().at(-1)
      return (!start || month >= start) && (!garage.closedOn || month <= garage.closedOn.slice(0, 7))
    })
  })
}

export function getMeterReadingStartDisplay(garage: MeterReadingYearGarageDto) {
  const source = garage.startValueSource ?? (garage.initialReadingValue != null && !garage.connectedOn ? 'legacy' : 'unknown')
  const value = garage.startValueSource ? garage.startValue ?? null : !garage.connectedOn ? garage.initialReadingValue ?? null : null
  const date = garage.startValueSource ? garage.startValueDate : garage.initialReadingMonth
  const formattedDate = date ? (source === 'legacy' ? date.slice(0, 7) : date.slice(0, 10)).split('-').reverse().join('.') : null
  const baselineMonth = getMeterBaselineMonth(garage)
  const description = source === 'connection'
    ? baselineMonth ? `База за ${baselineMonth.split('-').reverse().join('.')}` : 'Месяц базы неизвестен'
    : source === 'device'
      ? getDeviceStartDescription(baselineMonth, garage.startValueDeviceSerial, formattedDate)
      : source === 'legacy'
        ? formattedDate ? `База за ${formattedDate}` : 'Месяц базы неизвестен'
        : garage.connectedOn ? `Подключено ${garage.connectedOn.slice(0, 10).split('-').reverse().join('.')}` : 'База не заполнена'
  return { value, description }
}

function getDeviceStartDescription(baselineMonth: string | null, serial: string | null | undefined, formattedDate: string | null) {
  const physicalSerial = serial?.trim() && !serial.startsWith(meterAccountingRestartSerialPrefix) && serial.trim() !== unnumberedMeterSerial
    ? serial.trim()
    : null
  if (!baselineMonth) return `Прибор${physicalSerial ? ` ${physicalSerial}` : ''}${formattedDate ? ` · ${formattedDate}` : ''}`
  return `База за ${baselineMonth.split('-').reverse().join('.')}${physicalSerial ? ` · прибор ${physicalSerial}` : ''}`
}
