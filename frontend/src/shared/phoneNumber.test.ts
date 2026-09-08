import { describe, expect, it } from 'vitest'
import { formatRussianPhoneInput, isCompleteRussianPhone } from './phoneNumber'

describe('Russian phone formatting', () => {
  it.each([
    ['9131234567', '+7 (913) 123-45-67'],
    ['8123456789', '+7 (812) 345-67-89'],
    ['812 345-67-89', '+7 (812) 345-67-89'],
    ['8 812 345 67 89', '+7 (812) 345-67-89'],
    ['+7 (812) 345-67-8', '+7 (812) 345-67-8'],
    ['8 913 123 45 67', '+7 (913) 123-45-67'],
    ['+7 (913) 123-45-67', '+7 (913) 123-45-67'],
    ['91312', '+7 (913) 12'],
    ['', ''],
  ])('formats %s progressively', (source, expected) => {
    expect(formatRussianPhoneInput(source)).toBe(expected)
  })

  it('limits input to one complete Russian number', () => {
    expect(formatRussianPhoneInput('8 913 123 45 67 999')).toBe('+7 (913) 123-45-67')
  })

  it.each([
    ['', true],
    ['+7 (913) 123-45-67', true],
    ['8 913 123 45 67', true],
    ['8123456789', true],
    ['+7 (913) 123', false],
    ['+1 (202) 555-01-23', false],
    ['+7 (120) 255-50-12', false],
  ])('validates completeness for %s', (source, expected) => {
    expect(isCompleteRussianPhone(source)).toBe(expected)
  })
})
