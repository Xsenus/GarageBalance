// Mirrors the server: the removed meter is counted from its last reading (or its start value),
// the new meter from its own start value.
export function getMeterReplacementConsumption(baseline: number | null, finalValue: number | null, initialValue: number | null, currentValue: number | null) {
  if (baseline === null || finalValue === null || initialValue === null || currentValue === null) return null
  return { oldMeter: Math.round((finalValue - baseline) * 1000) / 1000, newMeter: Math.round((currentValue - initialValue) * 1000) / 1000 }
}
