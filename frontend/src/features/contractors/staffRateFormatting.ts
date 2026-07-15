import { formatMoneyTextInput, parseMoneyInput } from '../../shared/moneyInputFormatting'

export function parseStaffRate(value: string) {
  return parseMoneyInput(value)
}

export function formatStaffRate(value: string | number | null | undefined) {
  if (value === null || value === undefined || (typeof value === 'string' && value.trim() === '')) {
    return ''
  }

  return formatMoneyTextInput(value)
}
