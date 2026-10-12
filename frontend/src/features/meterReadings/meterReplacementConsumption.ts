// Mirrors the server: the removed meter is counted from its last reading (or its start value),
// the new meter from its own start value.
export function parseReplacementValue(value: string) {
  const normalized = value.trim().replace(',', '.')
  return /^\d+(?:\.\d{1,3})?$/.test(normalized) && Number(normalized) <= 999999999 ? Number(normalized) : null
}

export function getMeterReplacementConsumption(baseline: number | null, finalValue: number | null, initialValue: number | null, currentValue: number | null, previousDeviceConsumption = 0) {
  if (baseline === null || finalValue === null || initialValue === null || currentValue === null) return null
  const oldMeter = Math.round((finalValue - baseline) * 1000) / 1000
  const newMeter = Math.round((currentValue - initialValue) * 1000) / 1000
  return { oldMeter, newMeter, total: Math.round((previousDeviceConsumption + oldMeter + newMeter) * 1000) / 1000 }
}
