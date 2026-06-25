import type { AccountingTypeDto, GarageDto, OwnerDto, SupplierDto, SupplierGroupDto, TariffDto, UpsertTariffRequest } from '../services/dictionariesApi'
import type { OwnerGarageLinkForm } from './validation'

export type DictionarySectionKey = 'owners' | 'garages' | 'supplierGroups' | 'suppliers' | 'incomeTypes' | 'expenseTypes' | 'tariffs'
export type DictionarySectionGroupKey = 'counterparties' | 'operations' | 'tariffs'
export type DictionaryWritePermission = 'dictionaries' | 'tariffs'
export type DictionaryRecord = OwnerDto | GarageDto | SupplierGroupDto | SupplierDto | AccountingTypeDto | TariffDto

export type DictionarySectionOption = {
  key: DictionarySectionKey
  label: string
  group: DictionarySectionGroupKey
  writePermission: DictionaryWritePermission
}

export type DictionaryOwnerFormState = {
  lastName: string
  firstName: string
  middleName: string
  phone: string
  address: string
  meterNotes: string
}

export type DictionaryGarageFormState = {
  number: string
  peopleCount: number
  floorCount: number
  ownerId: string
  startingBalance: number
  initialWaterMeterValue: string
  initialElectricityMeterValue: string
  comment: string
}

export type DictionarySupplierFormState = {
  name: string
  groupId: string
  inn: string
  legalAddress: string
  contactPerson: string
  phone: string
  email: string
  startingBalance: number
  comment: string
}

export type DictionaryAccountingTypeFormState = {
  name: string
  code: string
}

export const dictionarySectionGroups: Array<{ key: DictionarySectionGroupKey; label: string }> = [
  { key: 'counterparties', label: 'Контрагенты' },
  { key: 'operations', label: 'Операции' },
  { key: 'tariffs', label: 'Тарифы' },
]

export const dictionarySectionOptions: DictionarySectionOption[] = [
  { key: 'owners', label: 'Владельцы', group: 'counterparties', writePermission: 'dictionaries' },
  { key: 'garages', label: 'Гаражи', group: 'counterparties', writePermission: 'dictionaries' },
  { key: 'supplierGroups', label: 'Группы поставщиков и персонала', group: 'counterparties', writePermission: 'dictionaries' },
  { key: 'suppliers', label: 'Поставщики и персонал', group: 'counterparties', writePermission: 'dictionaries' },
  { key: 'incomeTypes', label: 'Виды поступлений', group: 'operations', writePermission: 'dictionaries' },
  { key: 'expenseTypes', label: 'Виды выплат', group: 'operations', writePermission: 'dictionaries' },
  { key: 'tariffs', label: 'Тарифы', group: 'tariffs', writePermission: 'tariffs' },
]

const dictionarySearchPlaceholders: Record<DictionarySectionKey, string> = {
  owners: 'ФИО или телефон',
  garages: 'Номер гаража или ФИО владельца',
  supplierGroups: 'Поиск для этого справочника пока не применяется',
  suppliers: 'Название, ИНН или контакт',
  incomeTypes: 'Поиск для этого справочника пока не применяется',
  expenseTypes: 'Поиск для этого справочника пока не применяется',
  tariffs: 'Название или база расчета',
}

export function createEmptyOwnerForm(): DictionaryOwnerFormState {
  return {
    lastName: '',
    firstName: '',
    middleName: '',
    phone: '',
    address: '',
    meterNotes: '',
  }
}

export function createEmptyGarageForm(): DictionaryGarageFormState {
  return {
    number: '',
    peopleCount: 1,
    floorCount: 1,
    ownerId: '',
    startingBalance: 0,
    initialWaterMeterValue: '',
    initialElectricityMeterValue: '',
    comment: '',
  }
}

export function createEmptySupplierForm(groupId = ''): DictionarySupplierFormState {
  return {
    name: '',
    groupId,
    inn: '',
    legalAddress: '',
    contactPerson: '',
    phone: '',
    email: '',
    startingBalance: 0,
    comment: '',
  }
}

export function createEmptyAccountingTypeForm(): DictionaryAccountingTypeFormState {
  return {
    name: '',
    code: '',
  }
}

export function createEmptyTariffForm(effectiveFrom = '2026-07-01'): UpsertTariffRequest {
  return {
    name: '',
    calculationBase: 'fixed',
    rate: 1,
    effectiveFrom,
    comment: '',
  }
}

export function createEmptyOwnerGarageLinkForm(): OwnerGarageLinkForm {
  return {
    existingGarageId: '',
    newGarageNumber: '',
    peopleCount: 1,
    floorCount: 1,
    startingBalance: 0,
    initialWaterMeterValue: '',
    initialElectricityMeterValue: '',
    comment: '',
  }
}

export function supportsDictionarySearch(section: DictionarySectionKey) {
  return section === 'owners' || section === 'garages' || section === 'suppliers' || section === 'tariffs'
}

export function getDictionarySearchPlaceholder(section: DictionarySectionKey) {
  return dictionarySearchPlaceholders[section]
}

export function getDictionaryRecordTitle(section: DictionarySectionKey, item: DictionaryRecord) {
  if (section === 'owners') {
    return (item as OwnerDto).fullName
  }

  if (section === 'garages') {
    return `Гараж ${(item as GarageDto).number}`
  }

  if (section === 'supplierGroups') {
    return (item as SupplierGroupDto).name
  }

  if (section === 'suppliers') {
    return (item as SupplierDto).name
  }

  if (section === 'tariffs') {
    return (item as TariffDto).name
  }

  return (item as AccountingTypeDto).name
}
