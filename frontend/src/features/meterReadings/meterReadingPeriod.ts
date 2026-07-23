export function isFutureMeterReadingMonth(year: string, monthKey: string, currentMonth: string) {
  return `${year}-${monthKey}` > currentMonth
}
