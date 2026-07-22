export const russianPhonePlaceholder = '+7 (___) ___-__-__'

export function formatRussianPhoneInput(value: string | null | undefined) {
  if (!value) {
    return ''
  }

  const digits = value.replace(/\D/g, '')
  const nationalDigits = (digits.startsWith('7') || digits.startsWith('8') ? digits.slice(1) : digits).slice(0, 10)
  if (nationalDigits.length === 0) {
    return digits.length > 0 ? '+7 (' : ''
  }

  let formatted = `+7 (${nationalDigits.slice(0, 3)}`
  if (nationalDigits.length >= 3) {
    formatted += ')'
  }
  if (nationalDigits.length > 3) {
    formatted += ` ${nationalDigits.slice(3, 6)}`
  }
  if (nationalDigits.length > 6) {
    formatted += `-${nationalDigits.slice(6, 8)}`
  }
  if (nationalDigits.length > 8) {
    formatted += `-${nationalDigits.slice(8, 10)}`
  }

  return formatted
}

export function isCompleteRussianPhone(value: string | null | undefined) {
  if (!value?.trim()) {
    return true
  }

  const digits = value.replace(/\D/g, '')
  const nationalDigits = digits.length === 10
    ? digits
    : digits.length === 11 && (digits.startsWith('7') || digits.startsWith('8'))
      ? digits.slice(1)
      : ''
  return nationalDigits.length === 10 && /^[3489]/.test(nationalDigits)
}
