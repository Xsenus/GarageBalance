import { describe, expect, it } from 'vitest'
import {
  getAuthValidationErrors,
  getManagedUserValidationErrors,
  getPasswordChangeValidationErrors,
  getPasswordPolicyErrors,
} from './validation'

describe('shared validation helpers', () => {
  it('accepts strong passwords and reports every missing password policy part', () => {
    expect(getPasswordPolicyErrors('StrongPass123')).toEqual([])
    expect(getPasswordPolicyErrors('')).toEqual(['Укажите пароль.'])
    expect(getPasswordPolicyErrors('weak')).toEqual([
      'Пароль должен быть не короче 8 символов.',
      'Добавьте заглавную букву в пароль.',
      'Добавьте хотя бы одну цифру в пароль.',
    ])
    expect(getPasswordPolicyErrors('Сильный123')).toEqual([])
  })

  it('validates login and bootstrap auth forms', () => {
    expect(getAuthValidationErrors('login', 'admin@example.com', '', 'StrongPass123')).toEqual([])
    expect(getAuthValidationErrors('bootstrap', 'admin@example.com', '', 'StrongPass123')).toEqual(['Укажите имя пользователя.'])
    expect(getAuthValidationErrors('login', 'bad-email', '', '')).toEqual([
      'Проверьте формат email.',
      'Укажите пароль.',
    ])
    expect(getAuthValidationErrors('login', '  ', '', 'StrongPass123')).toEqual(['Укажите email.'])
  })

  it('validates password change form', () => {
    expect(getPasswordChangeValidationErrors('OldPass123', 'NewPass123', 'NewPass123')).toEqual([])
    expect(getPasswordChangeValidationErrors('', 'short', '')).toEqual([
      'Укажите текущий пароль.',
      'Пароль должен быть не короче 8 символов.',
      'Добавьте заглавную букву в пароль.',
      'Добавьте хотя бы одну цифру в пароль.',
      'Повторите новый пароль.',
    ])
    expect(getPasswordChangeValidationErrors('OldPass123', 'NewPass123', 'OtherPass123')).toContain('Новый пароль и повтор пароля не совпадают.')
  })

  it('validates managed user creation form', () => {
    expect(getManagedUserValidationErrors('operator@example.com', 'Оператор', 'StrongPass123', 'operator')).toEqual([])
    expect(getManagedUserValidationErrors('bad-email', '', '', '')).toEqual([
      'Проверьте формат email пользователя.',
      'Укажите имя пользователя.',
      'Укажите пароль пользователя.',
      'Выберите роль пользователя.',
    ])
    expect(getManagedUserValidationErrors(' ', 'Оператор', 'StrongPass123', 'operator')).toEqual(['Укажите email пользователя.'])
  })
})
