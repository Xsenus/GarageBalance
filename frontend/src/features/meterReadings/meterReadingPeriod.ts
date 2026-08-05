export function isFutureMeterReadingMonth(year: string, monthKey: string, currentMonth: string) {
  return `${year}-${monthKey}` > currentMonth
}

export function isOutsideCurrentMeterReadingMonth(year: string, monthKey: string, currentMonth: string) {
  return `${year}-${monthKey}` !== currentMonth
}

export function getMeterReadingDateForMonth(year: string, monthKey: string, currentMonth: string, currentDate: string) {
  const month = `${year}-${monthKey}`
  return month === currentMonth ? currentDate : `${month}-01`
}
