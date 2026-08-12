// @vitest-environment node
import { describe, expect, it } from 'vitest'
import type { AccountingTypeDto, GarageDto, MeasurementUnitDto, OwnerDto } from '../services/dictionariesApi'
import { canWriteDictionarySection, createAccountingTypeFormFromDto, createEmptyAccountingTypeForm, createEmptyGarageForm, createEmptyOwnerForm, createEmptyOwnerGarageLinkForm, createGarageFormFromDto, createOwnerFormFromDto, dictionarySectionGroups, dictionarySectionOptions, getDictionaryEditorFieldMeta, getDictionaryRecordCells, getDictionaryRecordTitle, getDictionarySearchPlaceholder, getDictionarySectionOption, getDictionaryTableHeaders, getOwnerGarageOptions, getTariffCalculationBaseLabel, getTariffCalculationBaseOptions, getTariffCalculationUnitName, normalizeTariffCalculationUnitName, supportsDictionarySearch, usesElectricityTariffTiers } from './dictionaryWorkbench'

describe('dictionary workbench metadata', () => {
  it('keeps dictionary groups in the expected order', () => {
    expect(dictionarySectionGroups).toEqual([
      { key: 'counterparties', label: 'Контрагенты' },
      { key: 'operations', label: 'Финансовые статьи' },
      { key: 'tariffs', label: 'Тарифы' },
    ])
  })

  it('keeps dictionary sections grouped with their write permission', () => {
    expect(dictionarySectionOptions).toEqual([
      { key: 'owners', label: 'Владельцы', group: 'counterparties', writePermission: 'dictionaries' },
      { key: 'garages', label: 'Гаражи', group: 'counterparties', writePermission: 'dictionaries' },
      { key: 'incomeTypes', label: 'Виды поступлений', group: 'operations', writePermission: 'dictionaries' },
      { key: 'expenseTypes', label: 'Статьи расходов', group: 'operations', writePermission: 'dictionaries' },
      { key: 'measurementUnits', label: 'Единицы измерения', group: 'tariffs', writePermission: 'dictionaries' },
    ])
  })

  it('derives the service unit from the tariff calculation base', () => {
    expect(getTariffCalculationUnitName('fixed')).toBe('руб.')
    expect(getTariffCalculationUnitName('people')).toBe('чел.')
    expect(getTariffCalculationUnitName('meter_water')).toBe('м³')
    expect(getTariffCalculationUnitName('meter_electricity')).toBe('кВт·ч')
    expect(getTariffCalculationUnitName('unknown')).toBe('')
    expect(normalizeTariffCalculationUnitName('fixed', ' РУБ./ГАРАЖ ')).toBe('руб./гараж')
    expect(normalizeTariffCalculationUnitName('meter_water', 'руб.')).toBe('м³')
    expect(normalizeTariffCalculationUnitName('meter_water', ' гал. ')).toBe('гал.')
    expect(normalizeTariffCalculationUnitName('unknown', 'руб.')).toBe('')
  })

  it('returns section options and write access based on section permission', () => {
    expect(getDictionarySectionOption('owners')).toEqual({ key: 'owners', label: 'Владельцы', group: 'counterparties', writePermission: 'dictionaries' })

    expect(canWriteDictionarySection('owners', true)).toBe(true)
    expect(canWriteDictionarySection('owners', false)).toBe(false)
  })

  it('creates an empty owner garage link form with numeric defaults', () => {
    expect(createEmptyOwnerGarageLinkForm()).toEqual({
      existingGarageId: '',
      newGarageNumber: '',
      peopleCount: 1,
      floorCount: 1,
      startingBalance: 0,
      initialWaterMeterValue: '',
      initialElectricityMeterValue: '',
      comment: '',
    })
  })

  it('creates empty dictionary editor forms with stable defaults', () => {
    expect(createEmptyOwnerForm()).toEqual({
      lastName: '',
      firstName: '',
      middleName: '',
      phone: '',
      address: '',
      meterNotes: '',
    })

    expect(createEmptyGarageForm()).toEqual({
      number: '',
      peopleCount: 1,
      floorCount: 1,
      ownerId: '',
      startingBalance: 0,
      initialWaterMeterValue: '',
      initialElectricityMeterValue: '',
      comment: '',
    })

    expect(createEmptyAccountingTypeForm()).toEqual({
      name: '',
      code: '',
    })

  })

  it('creates dictionary editor forms from dto records', () => {
    expect(createOwnerFormFromDto(createOwner({
      middleName: 'Петрович',
      phone: '+79990000000',
      address: 'ул. Ленина, 1',
      meterNotes: 'Счетчик в боксе',
    }))).toEqual({
      lastName: 'Иванов',
      firstName: 'Иван',
      middleName: 'Петрович',
      phone: '+79990000000',
      address: 'ул. Ленина, 1',
      meterNotes: 'Счетчик в боксе',
    })

    expect(createOwnerFormFromDto(createOwner())).toEqual({
      lastName: 'Иванов',
      firstName: 'Иван',
      middleName: '',
      phone: '',
      address: '',
      meterNotes: '',
    })

    expect(createGarageFormFromDto(createGarage({
      ownerId: 'owner-1',
      startingBalance: -150,
      initialWaterMeterValue: 12.5,
      initialElectricityMeterValue: 1024,
      comment: 'угловой',
    }))).toEqual({
      number: '42',
      peopleCount: 1,
      floorCount: 1,
      ownerId: 'owner-1',
      startingBalance: -150,
      initialWaterMeterValue: '12.5',
      initialElectricityMeterValue: '1024',
      comment: 'угловой',
    })

    expect(createAccountingTypeFormFromDto(createAccountingType({ code: 'MEMBER_FEE' }))).toEqual({
      name: 'Членский взнос',
      code: 'MEMBER_FEE',
    })
  })

  it('marks only server-searchable dictionary sections as searchable', () => {
    expect(Object.fromEntries(dictionarySectionOptions.map((section) => [section.key, supportsDictionarySearch(section.key)]))).toEqual({
      owners: true,
      garages: true,
      incomeTypes: true,
      expenseTypes: true,
      measurementUnits: true,
    })
  })

  it('returns search placeholders for every dictionary section', () => {
    expect(Object.fromEntries(dictionarySectionOptions.map((section) => [section.key, getDictionarySearchPlaceholder(section.key)]))).toEqual({
      owners: 'ФИО или телефон',
      garages: 'Номер гаража или ФИО владельца',
      incomeTypes: 'Название или код поступления',
      expenseTypes: 'Название или код выплаты',
      measurementUnits: 'Обозначение единицы измерения',
    })
  })

  it('returns table headers for every dictionary section', () => {
    expect(Object.fromEntries(dictionarySectionOptions.map((section) => [section.key, getDictionaryTableHeaders(section.key)]))).toEqual({
      owners: ['ФИО', 'Гаражи', 'Телефон', 'Адрес'],
      garages: ['Номер', 'Владелец', 'Людей', 'Этажей', 'Стартовый баланс'],
      incomeTypes: ['Название', 'Код', 'Тип'],
      expenseTypes: ['Название', 'Код', 'Тип'],
      measurementUnits: ['Обозначение'],
    })
  })

  it('returns table cell values for every dictionary section', () => {
    expect(getDictionaryRecordCells('owners', createOwner({ garageNumbers: ['1', '2'] }))).toEqual(['Иванов Иван', '1, 2', 'не указан', 'не указан'])
    expect(getDictionaryRecordCells('owners', createOwner({ phone: '+79990000000', address: 'ул. Ленина, 1' }))).toEqual(['Иванов Иван', 'без гаража', '+79990000000', 'ул. Ленина, 1'])
    expect(getDictionaryRecordCells('garages', createGarage({ ownerName: 'Иванов Иван', startingBalance: 350 }))).toEqual(['42', 'Иванов Иван', 1, 1, '350.00'])
    expect(getDictionaryRecordCells('garages', createGarage())).toEqual(['42', 'без владельца', 1, 1, '0.00'])
    expect(getDictionaryRecordCells('incomeTypes', createAccountingType({ code: 'MEMBER_FEE', isSystem: true }))).toEqual(['Членский взнос', 'MEMBER_FEE', 'Системный'])
    expect(getDictionaryRecordCells('expenseTypes', createAccountingType({ name: 'Вывоз мусора' }))).toEqual(['Вывоз мусора', 'не указан', 'Пользовательский'])
    expect(getDictionaryRecordCells('measurementUnits', createMeasurementUnit())).toEqual(['м³'])
  })

  it('returns editor field metadata used by dictionary CRUD modals', () => {
    expect(getDictionaryEditorFieldMeta('ownerLastName')).toMatchObject({ label: 'Фамилия', ariaLabel: 'Фамилия владельца', placeholder: 'Иванов' })
    expect(getDictionaryEditorFieldMeta('ownerExistingGarage')).toMatchObject({ label: 'Существующий гараж', ariaLabel: 'Привязать существующий гараж' })
    expect(getDictionaryEditorFieldMeta('ownerNewGarageComment')).toMatchObject({ label: 'Комментарий по гаражу', ariaLabel: 'Комментарий нового гаража' })
    expect(getDictionaryEditorFieldMeta('garageComment')).toMatchObject({ label: 'Комментарий', ariaLabel: 'Комментарий по гаражу' })
    expect(getDictionaryEditorFieldMeta('accountingTypeCode')).toMatchObject({ label: 'Код', ariaLabel: 'Код вида операции' })
    expect(getDictionaryEditorFieldMeta('measurementUnitName')).toMatchObject({ label: 'Обозначение', ariaLabel: 'Обозначение единицы измерения' })
  })

  it('returns tariff calculation base options in the editor order', () => {
    expect(getTariffCalculationBaseOptions()).toEqual([
      { value: 'fixed', label: 'Фиксированно' },
      { value: 'people', label: 'По людям' },
      { value: 'meter_water', label: 'По счетчику воды' },
      { value: 'meter_electricity', label: 'По счетчику электричества' },
    ])
    expect(getTariffCalculationBaseLabel('meter_electricity')).toBe('По счетчику электричества')
  })

  it('detects when any meter tariff editor should show tier fields', () => {
    expect(Object.fromEntries(getTariffCalculationBaseOptions().map((option) => [option.value, usesElectricityTariffTiers(option.value)]))).toEqual({
      fixed: false,
      people: false,
      meter_water: true,
      meter_electricity: true,
    })
    expect(usesElectricityTariffTiers('unknown')).toBe(false)
  })

  it('returns record titles for every dictionary section', () => {
    expect(getDictionaryRecordTitle('owners', createOwner())).toBe('Иванов Иван')
    expect(getDictionaryRecordTitle('garages', createGarage())).toBe('Гараж 42')
    expect(getDictionaryRecordTitle('incomeTypes', createAccountingType())).toBe('Членский взнос')
    expect(getDictionaryRecordTitle('expenseTypes', createAccountingType({ name: 'Вывоз мусора' }))).toBe('Вывоз мусора')
    expect(getDictionaryRecordTitle('measurementUnits', createMeasurementUnit())).toBe('м³')
  })

  it('returns empty garages and garages already linked to the edited owner', () => {
    const owner = createOwner({ id: 'owner-1' })
    const garages = [
      createGarage({ id: 'free', number: '1', ownerId: null }),
      createGarage({ id: 'same-owner', number: '2', ownerId: 'owner-1' }),
      createGarage({ id: 'other-owner', number: '3', ownerId: 'owner-2' }),
    ]

    expect(getOwnerGarageOptions(garages).map((garage) => garage.id)).toEqual(['free'])
    expect(getOwnerGarageOptions(garages, owner).map((garage) => garage.id)).toEqual(['free', 'same-owner'])
  })
})

function createOwner(overrides: Partial<OwnerDto> = {}): OwnerDto {
  return {
    id: 'owner-1',
    lastName: 'Иванов',
    firstName: 'Иван',
    middleName: null,
    fullName: 'Иванов Иван',
    phone: null,
    address: null,
    meterNotes: null,
    isArchived: false,
    ...overrides,
  }
}

function createGarage(overrides: Partial<GarageDto> = {}): GarageDto {
  const garage = {
    id: 'garage-1',
    number: '42',
    peopleCount: 1,
    floorCount: 1,
    ownerId: null,
    ownerName: null,
    ownerPhone: null,
    startingBalance: 0,
    balance: 0,
    overdueDebt: 0,
    initialWaterMeterValue: null,
    initialElectricityMeterValue: null,
    comment: null,
    isArchived: false,
    ...overrides,
  }

  return {
    ...garage,
    balance: overrides.balance ?? garage.startingBalance,
    overdueDebt: overrides.overdueDebt ?? Math.max(garage.startingBalance, 0),
  }
}



function createAccountingType(overrides: Partial<AccountingTypeDto> = {}): AccountingTypeDto {
  return {
    id: 'type-1',
    name: 'Членский взнос',
    code: null,
    isSystem: false,
    isArchived: false,
    ...overrides,
  }
}


function createMeasurementUnit(overrides: Partial<MeasurementUnitDto> = {}): MeasurementUnitDto {
  return {
    id: 'unit-1',
    name: 'м³',
    isArchived: false,
    ...overrides,
  }
}
