import { describe, expect, it } from 'vitest'
import type { TariffDto, UpsertTariffRequest } from '../services/dictionariesApi'
import {
  createTariffFormFromDto,
  getAccountingTypeValidationErrors,
  getAuthValidationErrors,
  getGarageValidationErrors,
  getManagedUserValidationErrors,
  getOwnerGarageLinkValidationErrors,
  getOwnerValidationErrors,
  getPasswordChangeValidationErrors,
  getPasswordPolicyErrors,
  getSupplierGroupValidationErrors,
  getSupplierValidationErrors,
  getTariffValidationErrors,
  isDateInputValue,
  parseOptionalNumberInput,
  updateTariffCalculationBase,
  withoutElectricityTierFields,
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

  it('validates owner and linked garage forms', () => {
    expect(getOwnerValidationErrors({ lastName: '', firstName: '', middleName: null, phone: '12', address: null, meterNotes: null })).toEqual([
      'Укажите фамилию владельца.',
      'Укажите имя владельца.',
      'Проверьте телефон владельца.',
    ])

    expect(getOwnerGarageLinkValidationErrors({
      existingGarageId: '',
      newGarageNumber: '',
      peopleCount: 2,
      floorCount: 1,
      startingBalance: 0,
      initialWaterMeterValue: '',
      initialElectricityMeterValue: '',
      comment: '',
    })).toEqual(['Укажите номер нового гаража или очистите поля создания гаража.'])

    expect(getOwnerGarageLinkValidationErrors({
      existingGarageId: '',
      newGarageNumber: 'A-1',
      peopleCount: 2,
      floorCount: 1,
      startingBalance: 0,
      initialWaterMeterValue: '5',
      initialElectricityMeterValue: '',
      comment: '',
    })).toEqual([])
  })

  it('validates garage, supplier and accounting type forms', () => {
    expect(getGarageValidationErrors({
      number: '',
      peopleCount: -1,
      floorCount: 1.5,
      ownerId: null,
      startingBalance: Number.NaN,
      initialWaterMeterValue: -1,
      initialElectricityMeterValue: -2,
      comment: null,
    })).toEqual([
      'Укажите номер гаража.',
      'Количество людей должно быть целым числом 0 или больше.',
      'Количество этажей должно быть целым числом 0 или больше.',
      'Укажите корректный стартовый баланс гаража.',
      'Стартовый счетчик воды должен быть 0 или больше.',
      'Стартовый счетчик электричества должен быть 0 или больше.',
    ])

    expect(getSupplierGroupValidationErrors({ name: '' })).toEqual(['Укажите группу поставщиков.'])
    expect(getSupplierValidationErrors({
      name: '',
      groupId: '',
      inn: '123',
      legalAddress: null,
      contactPerson: null,
      phone: null,
      email: null,
      startingBalance: Number.NaN,
      comment: null,
    })).toEqual([
      'Укажите название поставщика.',
      'Выберите группу поставщика.',
      'ИНН поставщика должен содержать 10 или 12 цифр.',
      'Укажите корректный стартовый баланс поставщика.',
    ])
    expect(getAccountingTypeValidationErrors({ name: '', code: 'код', isSystem: false }, 'вида поступления')).toEqual([
      'Укажите название вида поступления.',
      'Код вида поступления должен содержать только латиницу, цифры, дефис или подчеркивание.',
    ])
  })

  it('validates tariff tiers and transforms tariff forms', () => {
    expect(getTariffValidationErrors({ name: '', calculationBase: 'bad', rate: 0, effectiveFrom: '', comment: '' })).toEqual([
      'Укажите название тарифа.',
      'Выберите базу расчета тарифа.',
      'Ставка тарифа должна быть больше 0.',
      'Укажите дату начала тарифа.',
    ])

    const incompleteElectricityTariff: UpsertTariffRequest = {
      name: 'Электроэнергия',
      calculationBase: 'meter_electricity',
      rate: 1,
      effectiveFrom: '2026-06-01',
      comment: '',
      electricityFirstThreshold: 100,
    }
    expect(getTariffValidationErrors(incompleteElectricityTariff)).toEqual(['Для трехтарифной электроэнергии заполните два порога и три ставки.'])

    const invalidTierOrder: UpsertTariffRequest = {
      name: 'Электроэнергия',
      calculationBase: 'meter_electricity',
      rate: 1,
      effectiveFrom: '2026-06-01',
      comment: '',
      electricityFirstThreshold: 200,
      electricitySecondThreshold: 100,
      electricityFirstRate: 1,
      electricitySecondRate: 2,
      electricityThirdRate: 3,
    }
    expect(getTariffValidationErrors(invalidTierOrder)).toEqual(['Второй порог электроэнергии должен быть больше первого.'])

    expect(parseOptionalNumberInput('')).toBeUndefined()
    expect(parseOptionalNumberInput('12.5')).toBe(12.5)
    expect(isDateInputValue('2026-06-25')).toBe(true)
    expect(isDateInputValue('25.06.2026')).toBe(false)

    const tariffForm = createTariffFormFromDto(createTariffDto())
    expect(tariffForm.electricityThirdRate).toBe(5)
    expect(withoutElectricityTierFields(tariffForm)).not.toHaveProperty('electricityThirdRate')
    expect(updateTariffCalculationBase(tariffForm, 'fixed')).not.toHaveProperty('electricityThirdRate')
    expect(updateTariffCalculationBase(tariffForm, 'meter_electricity')).toHaveProperty('electricityThirdRate')
  })
})

function createTariffDto(): TariffDto {
  return {
    id: 'tariff-1',
    name: 'Электроэнергия',
    calculationBase: 'meter_electricity',
    rate: 1,
    electricityFirstThreshold: 100,
    electricitySecondThreshold: 200,
    electricityFirstRate: 3,
    electricitySecondRate: 4,
    electricityThirdRate: 5,
    effectiveFrom: '2026-06-01',
    comment: null,
    isArchived: false,
  }
}
