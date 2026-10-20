import type { ChargeServiceSettingDto, ChargeServiceTariffPeriodDto, TariffDto } from '../../services/dictionariesApi'
import { getTariffCalculationUnitName } from '../../shared/dictionaryWorkbench'
import { getLocalDateInputValue } from '../../shared/formatters'

export function getServiceTariffDisplayName(tariffName: string | null | undefined, serviceName: string) {
  if (!tariffName) return serviceName
  const generatedModeName = /^.+\s+—\s+(?:обычный|по счетчику|по счетчику с порогами)(?:,\s+\d{2}\.\d{2}\.\d{4},\s+[0-9a-f]{8})?$/iu
  return generatedModeName.test(tariffName.trim()) ? serviceName : tariffName
}

export function getServiceMeasurementUnit(setting: Pick<ChargeServiceSettingDto, 'unitName'>, tariff?: Pick<TariffDto, 'calculationBase'> | null) {
  return setting.unitName?.trim()
    || (tariff ? getTariffCalculationUnitName(tariff.calculationBase) : 'руб.')
}

export function getInlineTariffChangeEffectiveFrom(displayedTariffStartsOn?: string | null) {
  return displayedTariffStartsOn ?? getLocalDateInputValue()
}

export function findTariffGapSourcePeriod(periods: ChargeServiceTariffPeriodDto[], effectiveFrom: string) {
  const ordered = [...periods].sort((left, right) => (left.effectiveFrom ?? '').localeCompare(right.effectiveFrom ?? ''))
  return ordered.filter((period) => (period.effectiveFrom ?? '') <= effectiveFrom).at(-1) ?? ordered[0]
}

// Monthly accruals prorate a period that starts mid-month, so a rate entered in a
// gap covers the whole working month unless the previous period ends inside it.
export function getTariffGapRateStart(workingDate: string, sourcePeriod: ChargeServiceTariffPeriodDto) {
  const monthStart = `${workingDate.slice(0, 7)}-01`
  if (!sourcePeriod.effectiveTo || sourcePeriod.effectiveTo < monthStart) {
    return monthStart
  }
  const dayAfter = new Date(`${sourcePeriod.effectiveTo}T00:00:00Z`)
  dayAfter.setUTCDate(dayAfter.getUTCDate() + 1)
  const nextDay = dayAfter.toISOString().slice(0, 10)
  return nextDay > workingDate ? workingDate : nextDay
}
