// @vitest-environment node
import { describe, expect, it } from 'vitest'
import type { TariffDto, UpsertTariffRequest } from '../services/dictionariesApi'
import type { AccountingTypeDto } from '../services/dictionariesApi'
import {
  chooseRegularTariffId,
  createTariffFormFromDto,
  getAccrualValidationErrors,
  getAccountingTypeValidationErrors,
  getAuthValidationErrors,
  getCompatibleRegularTariffs,
  getExpenseReportValidationErrors,
  getExpenseValidationErrors,
  getGarageValidationErrors,
  getIncomeReportValidationErrors,
  getIncomeValidationErrors,
  getManagedUserValidationErrors,
  getMeterReadingValidationErrors,
  getOwnerGarageLinkValidationErrors,
  getOwnerValidationErrors,
  getPasswordChangeValidationErrors,
  getPasswordPolicyErrors,
  getRegularAccrualValidationErrorsForCatalog,
  getReportMonthRangeValidationErrors,
  getSupplierAccrualValidationErrors,
  getSupplierGroupSalaryValidationErrors,
  getSupplierGroupValidationErrors,
  getSupplierValidationErrors,
  getTariffValidationErrors,
  isAccountingMonthValue,
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

  it('validates finance operation and accrual forms', () => {
    expect(getIncomeValidationErrors({ garageId: '', incomeTypeId: '', operationDate: 'bad', accountingMonth: '2026-06', amount: 0, documentNumber: '', comment: '' })).toEqual([
      'Выберите гараж для поступления.',
      'Выберите вид поступления.',
      'Укажите дату поступления.',
      'Укажите месяц поступления.',
      'Сумма поступления должна быть больше 0.',
    ])

    expect(getExpenseValidationErrors({ supplierId: '', expenseTypeId: '', operationDate: 'bad', accountingMonth: 'bad', amount: -1, documentNumber: '', comment: '' })).toEqual([
      'Выберите поставщика для выплаты.',
      'Выберите вид выплаты.',
      'Укажите дату выплаты.',
      'Укажите месяц выплаты.',
      'Сумма выплаты должна быть больше 0.',
    ])

    expect(getAccrualValidationErrors({ garageId: '', incomeTypeId: '', accountingMonth: 'bad', amount: 0, source: 'manual', comment: '' })).toEqual([
      'Выберите гараж для начисления.',
      'Выберите вид начисления.',
      'Укажите месяц начисления.',
      'Сумма начисления должна быть больше 0.',
      'Укажите комментарий начисления.',
    ])

    expect(getSupplierAccrualValidationErrors({ supplierId: '', expenseTypeId: '', accountingMonth: 'bad', amount: 0, source: 'manual', documentNumber: '', comment: '' })).toEqual([
      'Выберите поставщика для начисления.',
      'Выберите вид начисления поставщику.',
      'Укажите месяц начисления поставщику.',
      'Сумма начисления поставщику должна быть больше 0.',
      'Укажите комментарий начисления поставщику.',
    ])
  })

  it('validates regular accruals, salary accruals and meter readings', () => {
    const incomeTypes = [
      createIncomeType('water-type', 'water'),
      createIncomeType('membership-type', 'membership'),
    ]
    const tariffs = [
      createTariffDto({ id: 'water-tariff', calculationBase: 'meter_water' }),
      createTariffDto({ id: 'fixed-tariff', calculationBase: 'fixed' }),
    ]

    expect(getSupplierGroupSalaryValidationErrors({ supplierGroupId: '', accountingMonth: 'bad', amount: 0, documentNumber: '', comment: '' })).toEqual([
      'Выберите группу персонала.',
      'Укажите месяц зарплаты.',
      'Сумма зарплаты должна быть больше 0.',
    ])
    expect(getRegularAccrualValidationErrorsForCatalog({ incomeTypeId: '', tariffId: '', accountingMonth: 'bad', comment: '' }, incomeTypes, tariffs)).toEqual([
      'Выберите вид регулярного начисления.',
      'Выберите тариф регулярного начисления.',
      'Укажите месяц регулярных начислений.',
    ])
    expect(getRegularAccrualValidationErrorsForCatalog({ incomeTypeId: 'water-type', tariffId: 'fixed-tariff', accountingMonth: '2026-06-01', comment: '' }, incomeTypes, tariffs)).toEqual([
      'Выбранный тариф не подходит для этого вида регулярного начисления.',
    ])
    expect(getCompatibleRegularTariffs('water-type', incomeTypes, tariffs).map((tariff) => tariff.id)).toEqual(['water-tariff'])
    expect(chooseRegularTariffId('water-type', 'fixed-tariff', incomeTypes, tariffs)).toBe('water-tariff')
    expect(chooseRegularTariffId('membership-type', 'fixed-tariff', incomeTypes, tariffs)).toBe('fixed-tariff')
    expect(getMeterReadingValidationErrors({ garageId: '', meterKind: 'bad' as 'water', accountingMonth: 'bad', readingDate: 'bad', currentValue: -1, comment: '' })).toEqual([
      'Выберите гараж для счетчика.',
      'Выберите тип счетчика.',
      'Укажите месяц показания.',
      'Укажите дату показания.',
      'Новое показание должно быть 0 или больше.',
    ])
  })

  it('validates report periods', () => {
    expect(isAccountingMonthValue('2026-06-01')).toBe(true)
    expect(isAccountingMonthValue('2026-06-02')).toBe(false)
    expect(getReportMonthRangeValidationErrors({ monthFrom: '2026-07-01', monthTo: '2026-06-01', search: '' })).toEqual(['Начало периода отчета не может быть позже конца.'])
    expect(getReportMonthRangeValidationErrors({ monthFrom: 'bad', monthTo: 'bad', search: '' })).toEqual([
      'Укажите начало периода отчета.',
      'Укажите конец периода отчета.',
    ])
    expect(getIncomeReportValidationErrors({ dateFrom: '2026-07-01', dateTo: '2026-06-01', search: '', garageIds: [], ownerIds: [], incomeTypeIds: [], rowMode: 'all' })).toEqual([
      'Начало отчета по поступлениям не может быть позже конца.',
    ])
    expect(getExpenseReportValidationErrors({ dateFrom: 'bad', dateTo: 'bad', search: '', supplierIds: [], expenseTypeIds: [], rowMode: 'all' })).toEqual([
      'Укажите начало отчета по выплатам.',
      'Укажите конец отчета по выплатам.',
    ])
  })
})

function createTariffDto(overrides: Partial<TariffDto> = {}): TariffDto {
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
    ...overrides,
  }
}

function createIncomeType(id: string, code: string): AccountingTypeDto {
  return {
    id,
    name: id,
    code,
    isSystem: false,
    isArchived: false,
  }
}
