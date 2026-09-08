export function removeTariffSchedulePeriod<T extends { key: string; effectiveFrom: string | null; effectiveTo: string | null }>(
  periods: T[],
  removedKey: string,
) {
  const ordered = [...periods].sort((left, right) => (left.effectiveFrom ?? '').localeCompare(right.effectiveFrom ?? ''))
  const removedIndex = ordered.findIndex((period) => period.key === removedKey)
  if (removedIndex < 0 || ordered.length <= 1) {
    return periods
  }

  const removed = ordered[removedIndex]
  const remaining = ordered.filter((period) => period.key !== removedKey)
  if (removedIndex === 0) {
    return remaining.map((period, index) => index === 0 ? { ...period, effectiveFrom: removed.effectiveFrom } : period)
  }

  return remaining.map((period, index) => index === removedIndex - 1 ? { ...period, effectiveTo: removed.effectiveTo } : period)
}
