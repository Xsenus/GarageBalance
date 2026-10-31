import type { MeterReplacementMarkerDto } from '../services/financeApi'

export function describeMeterReplacement(marker: MeterReplacementMarkerDto) {
  const number = (value: number | null) => value === null ? 'не указано' : new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 3 }).format(value)
  const date = marker.date.slice(0, 10).split('-').reverse().join('.')
  const newConsumption = marker.currentValue - marker.newInitialValue
  if (marker.isAccountingRestart) {
    return `Начало учёта нового прибора ${date}: старый № ${marker.oldSerial} — конечное ${number(marker.oldFinalValue)}; новый № ${marker.newSerial} — начальное ${number(marker.newInitialValue)}, текущее ${number(marker.currentValue)}. Расход ${number(marker.totalConsumption)} = ${number(marker.currentValue)} − ${number(marker.newInitialValue)}. Конечное старого прибора сохранено в истории и в этот расход не включено.`
  }
  return `Замена ${date}: старый № ${marker.oldSerial} — конечное ${number(marker.oldFinalValue)}; новый № ${marker.newSerial} — начальное ${number(marker.newInitialValue)}, текущее ${number(marker.currentValue)}. Расход ${number(marker.totalConsumption)} = снятые приборы ${number(marker.removedDevicesConsumption)} + новый ${number(newConsumption)}.`
}
