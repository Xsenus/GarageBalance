import { formatMoneyTextInput, parseMoneyInput } from '../../shared/moneyInputFormatting'

export function parsePaymentMoney(value: string) {
  return parseMoneyInput(value)
}

export function formatPaymentMoney(value: number | string) {
  return formatMoneyTextInput(value)
}
