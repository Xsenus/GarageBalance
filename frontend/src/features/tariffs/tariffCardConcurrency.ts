import type { ChargeServiceSettingDto, ChargeServiceTariffPeriodDto } from '../../services/dictionariesApi'

export function sameTariffServiceSettings(left: ChargeServiceSettingDto, right: ChargeServiceSettingDto) {
  const fields = ['name', 'isRegular', 'periodicityMonths', 'accrualStartMonth', 'paymentDueDay', 'paymentDueMonth', 'overdueGraceDays',
    'incomeTypeId', 'tariffId', 'isMetered', 'hasTieredTariff', 'unitName', 'isArchived'] as const
  return fields.every((field) => left[field] === right[field])
    && Boolean(left.appliesToSelectedGarages) === Boolean(right.appliesToSelectedGarages)
    && JSON.stringify([...(left.garageIds ?? [])].sort()) === JSON.stringify([...(right.garageIds ?? [])].sort())
}

export function sameTariffScheduleTerms(left: ChargeServiceTariffPeriodDto[], right: ChargeServiceTariffPeriodDto[]) {
  const key = (period: ChargeServiceTariffPeriodDto) => JSON.stringify([period.tariffId, period.effectiveFrom, period.effectiveTo, period.rate])
  return JSON.stringify(left.map(key).sort()) === JSON.stringify(right.map(key).sort())
}
