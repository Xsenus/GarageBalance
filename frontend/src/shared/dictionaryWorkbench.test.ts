import { describe, expect, it } from 'vitest'
import type { AccountingTypeDto, GarageDto, OwnerDto, SupplierDto, SupplierGroupDto, TariffDto } from '../services/dictionariesApi'
import { canWriteDictionarySection, createAccountingTypeFormFromDto, createEmptyAccountingTypeForm, createEmptyGarageForm, createEmptyOwnerForm, createEmptyOwnerGarageLinkForm, createEmptySupplierForm, createEmptyTariffForm, createGarageFormFromDto, createOwnerFormFromDto, createSupplierFormFromDto, dictionarySectionGroups, dictionarySectionOptions, getDictionaryEditorFieldMeta, getDictionaryRecordCells, getDictionaryRecordTitle, getDictionarySearchPlaceholder, getDictionarySectionOption, getDictionaryTableHeaders, getOwnerGarageOptions, getTariffCalculationBaseOptions, supportsDictionarySearch, usesElectricityTariffTiers } from './dictionaryWorkbench'

describe('dictionary workbench metadata', () => {
  it('keeps dictionary groups in the expected order', () => {
    expect(dictionarySectionGroups).toEqual([
      { key: 'counterparties', label: 'Контрагенты' },
      { key: 'operations', label: 'Операции' },
      { key: 'tariffs', label: 'Тарифы' },
    ])
  })

  it('keeps dictionary sections grouped with their write permission', () => {
    expect(dictionarySectionOptions).toEqual([
      { key: 'owners', label: 'Владельцы', group: 'counterparties', writePermission: 'dictionaries' },
      { key: 'garages', label: 'Гаражи', group: 'counterparties', writePermission: 'dictionaries' },
      { key: 'supplierGroups', label: 'Группы поставщиков и персонала', group: 'counterparties', writePermission: 'dictionaries' },
      { key: 'suppliers', label: 'Поставщики и персонал', group: 'counterparties', writePermission: 'dictionaries' },
      { key: 'incomeTypes', label: 'Виды поступлений', group: 'operations', writePermission: 'dictionaries' },
      { key: 'expenseTypes', label: 'Виды выплат', group: 'operations', writePermission: 'dictionaries' },
      { key: 'tariffs', label: 'Тарифы', group: 'tariffs', writePermission: 'tariffs' },
    ])
  })

  it('returns section options and write access based on section permission', () => {
    expect(getDictionarySectionOption('tariffs')).toEqual({ key: 'tariffs', label: 'Тарифы', group: 'tariffs', writePermission: 'tariffs' })
    expect(getDictionarySectionOption('owners')).toEqual({ key: 'owners', label: 'Владельцы', group: 'counterparties', writePermission: 'dictionaries' })

    expect(canWriteDictionarySection('owners', true, false)).toBe(true)
    expect(canWriteDictionarySection('owners', false, true)).toBe(false)
    expect(canWriteDictionarySection('tariffs', true, false)).toBe(false)
    expect(canWriteDictionarySection('tariffs', false, true)).toBe(true)
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

    expect(createEmptySupplierForm('group-1')).toEqual({
      name: '',
      groupId: 'group-1',
      inn: '',
      legalAddress: '',
      contactPerson: '',
      phone: '',
      email: '',
      startingBalance: 0,
      comment: '',
    })

    expect(createEmptyAccountingTypeForm()).toEqual({
      name: '',
      code: '',
    })

    expect(createEmptyTariffForm()).toEqual({
      name: '',
      calculationBase: 'fixed',
      rate: 1,
      effectiveFrom: '2026-07-01',
      comment: '',
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

    expect(createSupplierFormFromDto(createSupplier({
      inn: '5400000000',
      legalAddress: 'Новосибирск',
      contactPerson: 'Петр',
      phone: '+73830000000',
      email: 'bank@example.test',
      startingBalance: 250,
      comment: 'основной банк',
    }))).toEqual({
      name: 'БАНК 12',
      groupId: 'group-1',
      inn: '5400000000',
      legalAddress: 'Новосибирск',
      contactPerson: 'Петр',
      phone: '+73830000000',
      email: 'bank@example.test',
      startingBalance: 250,
      comment: 'основной банк',
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
      supplierGroups: false,
      suppliers: true,
      incomeTypes: false,
      expenseTypes: false,
      tariffs: true,
    })
  })

  it('returns search placeholders for every dictionary section', () => {
    expect(Object.fromEntries(dictionarySectionOptions.map((section) => [section.key, getDictionarySearchPlaceholder(section.key)]))).toEqual({
      owners: 'ФИО или телефон',
      garages: 'Номер гаража или ФИО владельца',
      supplierGroups: 'Поиск для этого справочника пока не применяется',
      suppliers: 'Название, ИНН или контакт',
      incomeTypes: 'Поиск для этого справочника пока не применяется',
      expenseTypes: 'Поиск для этого справочника пока не применяется',
      tariffs: 'Название или база расчета',
    })
  })

  it('returns table headers for every dictionary section', () => {
    expect(Object.fromEntries(dictionarySectionOptions.map((section) => [section.key, getDictionaryTableHeaders(section.key)]))).toEqual({
      owners: ['ФИО', 'Гаражи', 'Телефон', 'Адрес'],
      garages: ['Номер', 'Владелец', 'Людей', 'Этажей', 'Стартовый баланс'],
      supplierGroups: ['Название', 'Тип'],
      suppliers: ['Название', 'Группа', 'ИНН', 'Стартовый баланс'],
      incomeTypes: ['Название', 'Код', 'Тип'],
      expenseTypes: ['Название', 'Код', 'Тип'],
      tariffs: ['Название', 'База', 'Ставка', 'Дата начала'],
    })
  })

  it('returns table cell values for every dictionary section', () => {
    expect(getDictionaryRecordCells('owners', createOwner({ garageNumbers: ['1', '2'] }))).toEqual(['Иванов Иван', '1, 2', 'не указан', 'не указан'])
    expect(getDictionaryRecordCells('owners', createOwner({ phone: '+79990000000', address: 'ул. Ленина, 1' }))).toEqual(['Иванов Иван', 'без гаража', '+79990000000', 'ул. Ленина, 1'])
    expect(getDictionaryRecordCells('garages', createGarage({ ownerName: 'Иванов Иван', startingBalance: 350 }))).toEqual(['42', 'Иванов Иван', 1, 1, '350,00'])
    expect(getDictionaryRecordCells('garages', createGarage())).toEqual(['42', 'без владельца', 1, 1, '0,00'])
    expect(getDictionaryRecordCells('supplierGroups', createSupplierGroup({ isSystem: true }))).toEqual(['Банковские услуги', 'Системная'])
    expect(getDictionaryRecordCells('supplierGroups', createSupplierGroup())).toEqual(['Банковские услуги', 'Пользовательская'])
    expect(getDictionaryRecordCells('suppliers', createSupplier({ inn: '5400000000', startingBalance: 250 }))).toEqual(['БАНК 12', 'Банковские услуги', '5400000000', '250,00'])
    expect(getDictionaryRecordCells('suppliers', createSupplier())).toEqual(['БАНК 12', 'Банковские услуги', 'не указан', '0,00'])
    expect(getDictionaryRecordCells('incomeTypes', createAccountingType({ code: 'MEMBER_FEE', isSystem: true }))).toEqual(['Членский взнос', 'MEMBER_FEE', 'Системный'])
    expect(getDictionaryRecordCells('expenseTypes', createAccountingType({ name: 'Вывоз мусора' }))).toEqual(['Вывоз мусора', 'не указан', 'Пользовательский'])
    expect(getDictionaryRecordCells('tariffs', createTariff())).toEqual(['Тариф на воду', 'water', '1,00', '01.07.2026'])
  })

  it('returns editor field metadata used by dictionary CRUD modals', () => {
    expect(getDictionaryEditorFieldMeta('ownerLastName')).toMatchObject({ label: 'Фамилия', ariaLabel: 'Фамилия владельца', placeholder: 'Иванов' })
    expect(getDictionaryEditorFieldMeta('ownerExistingGarage')).toMatchObject({ label: 'Существующий гараж', ariaLabel: 'Привязать существующий гараж' })
    expect(getDictionaryEditorFieldMeta('ownerNewGarageComment')).toMatchObject({ label: 'Комментарий по гаражу', ariaLabel: 'Комментарий нового гаража' })
    expect(getDictionaryEditorFieldMeta('garageComment')).toMatchObject({ label: 'Комментарий', ariaLabel: 'Комментарий по гаражу' })
    expect(getDictionaryEditorFieldMeta('supplierGroupName')).toMatchObject({ label: 'Название группы', ariaLabel: 'Группа поставщиков' })
    expect(getDictionaryEditorFieldMeta('supplierLegalAddress')).toMatchObject({ label: 'Юридический адрес', ariaLabel: 'Юридический адрес поставщика' })
    expect(getDictionaryEditorFieldMeta('accountingTypeCode')).toMatchObject({ label: 'Код', ariaLabel: 'Код вида операции' })
    expect(getDictionaryEditorFieldMeta('tariffCalculationBase')).toMatchObject({ label: 'База расчета', ariaLabel: 'База расчета тарифа' })
    expect(getDictionaryEditorFieldMeta('tariffElectricitySecondThreshold')).toMatchObject({ label: 'Порог 2, кВт', ariaLabel: 'Второй порог электроэнергии' })
    expect(getDictionaryEditorFieldMeta('tariffComment')).toMatchObject({ label: 'Комментарий', ariaLabel: 'Комментарий тарифа' })
  })

  it('returns tariff calculation base options in the editor order', () => {
    expect(getTariffCalculationBaseOptions()).toEqual([
      { value: 'fixed', label: 'Фиксированно' },
      { value: 'people', label: 'По людям' },
      { value: 'meter_water', label: 'По счетчику воды' },
      { value: 'meter_electricity', label: 'По счетчику электричества' },
    ])
  })

  it('detects when tariff editor should show electricity tier fields', () => {
    expect(Object.fromEntries(getTariffCalculationBaseOptions().map((option) => [option.value, usesElectricityTariffTiers(option.value)]))).toEqual({
      fixed: false,
      people: false,
      meter_water: false,
      meter_electricity: true,
    })
    expect(usesElectricityTariffTiers('unknown')).toBe(false)
  })

  it('returns record titles for every dictionary section', () => {
    expect(getDictionaryRecordTitle('owners', createOwner())).toBe('Иванов Иван')
    expect(getDictionaryRecordTitle('garages', createGarage())).toBe('Гараж 42')
    expect(getDictionaryRecordTitle('supplierGroups', createSupplierGroup())).toBe('Банковские услуги')
    expect(getDictionaryRecordTitle('suppliers', createSupplier())).toBe('БАНК 12')
    expect(getDictionaryRecordTitle('incomeTypes', createAccountingType())).toBe('Членский взнос')
    expect(getDictionaryRecordTitle('expenseTypes', createAccountingType({ name: 'Вывоз мусора' }))).toBe('Вывоз мусора')
    expect(getDictionaryRecordTitle('tariffs', createTariff())).toBe('Тариф на воду')
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
  return {
    id: 'garage-1',
    number: '42',
    peopleCount: 1,
    floorCount: 1,
    ownerId: null,
    ownerName: null,
    startingBalance: 0,
    initialWaterMeterValue: null,
    initialElectricityMeterValue: null,
    comment: null,
    isArchived: false,
    ...overrides,
  }
}

function createSupplierGroup(overrides: Partial<SupplierGroupDto> = {}): SupplierGroupDto {
  return {
    id: 'group-1',
    name: 'Банковские услуги',
    isSystem: false,
    isArchived: false,
    ...overrides,
  }
}

function createSupplier(overrides: Partial<SupplierDto> = {}): SupplierDto {
  return {
    id: 'supplier-1',
    name: 'БАНК 12',
    groupId: 'group-1',
    groupName: 'Банковские услуги',
    inn: null,
    legalAddress: null,
    contactPerson: null,
    phone: null,
    email: null,
    startingBalance: 0,
    comment: null,
    isArchived: false,
    ...overrides,
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

function createTariff(): TariffDto {
  return {
    id: 'tariff-1',
    name: 'Тариф на воду',
    calculationBase: 'water',
    rate: 1,
    electricityFirstThreshold: null,
    electricitySecondThreshold: null,
    electricityFirstRate: null,
    electricitySecondRate: null,
    electricityThirdRate: null,
    effectiveFrom: '2026-07-01',
    comment: null,
    isArchived: false,
  }
}
