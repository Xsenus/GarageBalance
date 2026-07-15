import { formatMoneyTextInput } from '../../shared/moneyInputFormatting'

export function formatTariffDecimal(value: number | string) {
  return formatMoneyTextInput(value)
}
