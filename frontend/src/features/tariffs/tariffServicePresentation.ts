import type { ChargeServiceSettingDto, TariffDto } from '../../services/dictionariesApi'
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
  const today = getLocalDateInputValue()
  return displayedTariffStartsOn && displayedTariffStartsOn > today ? displayedTariffStartsOn : today
}
