export function isFutureMeterReadingMonth(year: string, monthKey: string, currentMonth: string) {
  return `${year}-${monthKey}` > currentMonth
}

export function isOutsideCurrentMeterReadingMonth(year: string, monthKey: string, currentMonth: string) {
  return `${year}-${monthKey}` !== currentMonth
}

export function getMeterReadingDateForMonth(year: string, monthKey: string, currentMonth: string, currentDate: string, connectedOn?: string | null, closedOn?: string | null) {
  const month = `${year}-${monthKey}`
  const usualDate = month === currentMonth ? currentDate : `${month}-01`
  const afterConnection = connectedOn?.slice(0, 7) === month && connectedOn > usualDate ? connectedOn : usualDate
  return closedOn?.slice(0, 7) === month && closedOn < afterConnection ? closedOn : afterConnection
}

export function getMeterReadingConnectionStatus(garage: { connectedOn?: string | null; closedOn?: string | null; requiresStartValue?: boolean; requiresConnectionDate?: boolean }, month: string) {
  if (garage.requiresConnectionDate || garage.connectedOn && month < garage.connectedOn.slice(0, 7)) return 'Не подключена'
  if (garage.closedOn && month > garage.closedOn.slice(0, 7)) return 'Услуга отключена'
  if (garage.requiresStartValue) return 'Требуется стартовое показание'
  return null
}
