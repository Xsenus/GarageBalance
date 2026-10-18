import type { MeterReadingYearGarageDto } from '../../services/financeApi'

function getPreviousMonth(month: string) {
  const [year, monthNumber] = month.split('-').map(Number)
  const previous = new Date(Date.UTC(year, monthNumber - 2, 1))
  return `${previous.getUTCFullYear()}-${String(previous.getUTCMonth() + 1).padStart(2, '0')}`
}

// The start value is the reading at the end of the month before the garage's "Учитывать с"
// month (or before a later service connection). That month is shown as an editable column.
export function getMeterBaselineMonth(garage: MeterReadingYearGarageDto) {
  if (garage.connectedOn) {
    const starts = [garage.accountingStartMonth?.slice(0, 7), garage.connectedOn.slice(0, 7)]
      .filter((value): value is string => Boolean(value))
      .sort()
    return getPreviousMonth(starts.at(-1)!)
  }
  if (garage.startValueSource === 'device') return null
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
        if (garage.initialReadingMonth && garage.initialReadingValue != null && month <= garage.initialReadingMonth.slice(0, 7)) return false
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
      ? `Прибор${garage.startValueDeviceSerial ? ` ${garage.startValueDeviceSerial}` : ''}${formattedDate ? ` · ${formattedDate}` : ''}`
      : source === 'legacy'
        ? formattedDate ? `База за ${formattedDate}` : 'Месяц базы неизвестен'
        : garage.connectedOn ? `Подключено ${garage.connectedOn.slice(0, 10).split('-').reverse().join('.')}` : 'База не заполнена'
  return { value, description }
}
