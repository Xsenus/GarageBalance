import { describe, expect, it } from 'vitest'
import { formatPaymentMoney, parsePaymentMoney } from './paymentMoneyFormatting'

describe('payment money formatting', () => {
  it('shows spaces between groups and two digits after a decimal point', () => {
    expect(formatPaymentMoney(1_000_000)).toBe('1 000 000.00')
    expect(formatPaymentMoney('4500')).toBe('4 500.00')
    expect(formatPaymentMoney('124,5')).toBe('124.50')
  })

  it('keeps an empty payment input empty', () => {
    expect(formatPaymentMoney('')).toBe('')
  })

  it('parses a formatted payment for the API request', () => {
    expect(parsePaymentMoney('1 000 000.00')).toBe(1_000_000)
  })
})
