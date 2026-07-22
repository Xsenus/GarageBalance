import type { AccountingTypeDto, GarageDto, OwnerDto, SupplierDto, SupplierGroupDto, TariffDto, UpsertTariffRequest } from '../services/dictionariesApi'
import { formatDateOnly, formatMoney, formatTariffRateSummary } from './formatters'
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

export type DictionaryEditorFieldMeta = {
  label: string
  ariaLabel: string
  placeholder?: string
  hint?: string
}

export type DictionaryEditorFieldKey =
  | 'ownerLastName'
  | 'ownerFirstName'
  | 'ownerMiddleName'
  | 'ownerPhone'
  | 'ownerAddress'
  | 'ownerMeterNotes'
  | 'ownerExistingGarage'
  | 'ownerNewGarageNumber'
  | 'ownerNewGaragePeopleCount'
  | 'ownerNewGarageFloorCount'
  | 'ownerNewGarageStartingBalance'
  | 'ownerNewGarageInitialWaterMeterValue'
  | 'ownerNewGarageInitialElectricityMeterValue'
  | 'ownerNewGarageComment'
  | 'garageNumber'
  | 'garagePeopleCount'
  | 'garageFloorCount'
  | 'garageOwner'
  | 'garageStartingBalance'
  | 'garageInitialWaterMeterValue'
  | 'garageInitialElectricityMeterValue'
  | 'garageComment'
  | 'supplierGroupName'
  | 'supplierName'
  | 'supplierGroup'
  | 'supplierInn'
  | 'supplierLegalAddress'
  | 'supplierContactPerson'
  | 'supplierPhone'
  | 'supplierEmail'
  | 'supplierStartingBalance'
  | 'supplierComment'
  | 'accountingTypeName'
  | 'accountingTypeCode'
  | 'tariffName'
  | 'tariffCalculationBase'
  | 'tariffRate'
  | 'tariffEffectiveFrom'
  | 'tariffElectricityFirstThreshold'
  | 'tariffElectricitySecondThreshold'
  | 'tariffElectricityFirstRate'
  | 'tariffElectricitySecondRate'
  | 'tariffElectricityThirdRate'
  | 'tariffComment'

export type TariffCalculationBaseOption = {
  value: string
  label: string
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
  supplierGroups: 'Название группы',
  suppliers: 'Название, ИНН или контакт',
  incomeTypes: 'Название или код поступления',
  expenseTypes: 'Название или код выплаты',
  tariffs: 'Название или база расчета',
}

const dictionaryTableHeaders: Record<DictionarySectionKey, string[]> = {
  owners: ['ФИО', 'Гаражи', 'Телефон', 'Адрес'],
  garages: ['Номер', 'Владелец', 'Людей', 'Этажей', 'Стартовый баланс'],
  supplierGroups: ['Название', 'Тип'],
  suppliers: ['Название', 'Группа', 'ИНН', 'Стартовый баланс'],
  incomeTypes: ['Название', 'Код', 'Тип'],
  expenseTypes: ['Название', 'Код', 'Тип'],
  tariffs: ['Название', 'База', 'Ставка', 'Дата начала'],
}

const dictionaryEditorFieldMeta: Record<DictionaryEditorFieldKey, DictionaryEditorFieldMeta> = {
  ownerLastName: { label: 'Фамилия', ariaLabel: 'Фамилия владельца', placeholder: 'Иванов' },
  ownerFirstName: { label: 'Имя', ariaLabel: 'Имя владельца', placeholder: 'Иван' },
  ownerMiddleName: { label: 'Отчество', ariaLabel: 'Отчество владельца', placeholder: 'Иванович' },
  ownerPhone: { label: 'Телефон', ariaLabel: 'Телефон владельца', placeholder: '+7...' },
  ownerAddress: { label: 'Адрес', ariaLabel: 'Адрес владельца', placeholder: 'Адрес для связи' },
  ownerMeterNotes: { label: 'Заметки по счетчикам', ariaLabel: 'Комментарий владельца по счетчикам', placeholder: 'Особенности учета воды или электричества' },
  ownerExistingGarage: { label: 'Существующий гараж', ariaLabel: 'Привязать существующий гараж', hint: 'Выберите уже созданный гараж или оставьте без привязки.' },
  ownerNewGarageNumber: { label: 'Новый гараж', ariaLabel: 'Номер нового гаража владельца', placeholder: 'Номер' },
  ownerNewGaragePeopleCount: { label: 'Людей', ariaLabel: 'Количество людей в новом гараже' },
  ownerNewGarageFloorCount: { label: 'Этажей', ariaLabel: 'Количество этажей в новом гараже' },
  ownerNewGarageStartingBalance: { label: 'Стартовый баланс', ariaLabel: 'Стартовый баланс нового гаража' },
  ownerNewGarageInitialWaterMeterValue: { label: 'Старт воды', ariaLabel: 'Стартовый счетчик воды нового гаража' },
  ownerNewGarageInitialElectricityMeterValue: { label: 'Старт электричества', ariaLabel: 'Стартовый счетчик электричества нового гаража' },
  ownerNewGarageComment: { label: 'Комментарий по гаражу', ariaLabel: 'Комментарий нового гаража', placeholder: 'Особенности гаража, начислений или импорта' },
  garageNumber: { label: 'Номер гаража', ariaLabel: 'Номер гаража', placeholder: 'Например, 42' },
  garagePeopleCount: { label: 'Людей', ariaLabel: 'Количество людей' },
  garageFloorCount: { label: 'Этажей', ariaLabel: 'Количество этажей' },
  garageOwner: { label: 'Владелец', ariaLabel: 'Владелец гаража' },
  garageStartingBalance: { label: 'Стартовый баланс', ariaLabel: 'Стартовый баланс гаража', hint: 'Долг положительным числом, переплата отрицательным.' },
  garageInitialWaterMeterValue: { label: 'Старт воды', ariaLabel: 'Стартовый счетчик воды' },
  garageInitialElectricityMeterValue: { label: 'Старт электричества', ariaLabel: 'Стартовый счетчик электричества' },
  garageComment: { label: 'Комментарий', ariaLabel: 'Комментарий по гаражу', placeholder: 'Особенности гаража, начислений или импорта' },
  supplierGroupName: { label: 'Название группы', ariaLabel: 'Группа поставщиков', placeholder: 'Например, Коммунальные услуги' },
  supplierName: { label: 'Название', ariaLabel: 'Название поставщика', placeholder: 'Название организации или сотрудника' },
  supplierGroup: { label: 'Группа', ariaLabel: 'Группа для поставщика' },
  supplierInn: { label: 'ИНН', ariaLabel: 'ИНН поставщика', placeholder: 'ИНН' },
  supplierLegalAddress: { label: 'Юридический адрес', ariaLabel: 'Юридический адрес поставщика', placeholder: 'Адрес из документов' },
  supplierContactPerson: { label: 'Контактное лицо', ariaLabel: 'Контактное лицо поставщика', placeholder: 'ФИО или должность' },
  supplierPhone: { label: 'Телефон', ariaLabel: 'Телефон поставщика', placeholder: '+7...' },
  supplierEmail: { label: 'Email', ariaLabel: 'Email поставщика', placeholder: 'mail@example.ru' },
  supplierStartingBalance: { label: 'Стартовый баланс', ariaLabel: 'Стартовый баланс поставщика', hint: 'Наша задолженность поставщику на начало учета.' },
  supplierComment: { label: 'Комментарий', ariaLabel: 'Комментарий поставщика', placeholder: 'Договор, условия оплаты или заметки' },
  accountingTypeName: { label: 'Название', ariaLabel: 'Название вида операции', placeholder: 'Например, Членский взнос' },
  accountingTypeCode: { label: 'Код', ariaLabel: 'Код вида операции', placeholder: 'Код из старой базы или учета' },
  tariffName: { label: 'Название тарифа', ariaLabel: 'Название тарифа', placeholder: 'Например, Электроэнергия' },
  tariffCalculationBase: { label: 'База расчета', ariaLabel: 'База расчета тарифа' },
  tariffRate: { label: 'Ставка', ariaLabel: 'Ставка тарифа' },
  tariffEffectiveFrom: { label: 'Дата начала', ariaLabel: 'Дата начала тарифа' },
  tariffElectricityFirstThreshold: { label: 'Порог 1, кВт', ariaLabel: 'Первый порог электроэнергии', placeholder: 'Порог 1, кВт' },
  tariffElectricitySecondThreshold: { label: 'Порог 2, кВт', ariaLabel: 'Второй порог электроэнергии', placeholder: 'Порог 2, кВт' },
  tariffElectricityFirstRate: { label: 'Ставка 1', ariaLabel: 'Первая ставка электроэнергии', placeholder: 'Ставка 1' },
  tariffElectricitySecondRate: { label: 'Ставка 2', ariaLabel: 'Вторая ставка электроэнергии', placeholder: 'Ставка 2' },
  tariffElectricityThirdRate: { label: 'Ставка 3', ariaLabel: 'Третья ставка электроэнергии', placeholder: 'Ставка 3' },
  tariffComment: { label: 'Комментарий', ariaLabel: 'Комментарий тарифа', placeholder: 'Когда и почему действует тариф' },
}

const tariffCalculationBaseOptions: TariffCalculationBaseOption[] = [
  { value: 'fixed', label: 'Фиксированно' },
  { value: 'people', label: 'По людям' },
  { value: 'meter_water', label: 'По счетчику воды' },
  { value: 'meter_electricity', label: 'По счетчику электричества' },
]

const tariffCalculationUnitNames: Record<string, string> = {
  fixed: 'руб.',
  people: 'чел.',
  meter_water: 'м³',
  meter_electricity: 'кВт·ч',
}

const electricityTierCalculationBase = 'meter_electricity'

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

export function createOwnerFormFromDto(owner: OwnerDto): DictionaryOwnerFormState {
  return {
    lastName: owner.lastName,
    firstName: owner.firstName,
    middleName: owner.middleName ?? '',
    phone: owner.phone ?? '',
    address: owner.address ?? '',
    meterNotes: owner.meterNotes ?? '',
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

export function createGarageFormFromDto(garage: GarageDto): DictionaryGarageFormState {
  return {
    number: garage.number,
    peopleCount: garage.peopleCount,
    floorCount: garage.floorCount,
    ownerId: garage.ownerId ?? '',
    startingBalance: garage.startingBalance,
    initialWaterMeterValue: garage.initialWaterMeterValue?.toString() ?? '',
    initialElectricityMeterValue: garage.initialElectricityMeterValue?.toString() ?? '',
    comment: garage.comment ?? '',
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

export function createSupplierFormFromDto(supplier: SupplierDto): DictionarySupplierFormState {
  return {
    name: supplier.name,
    groupId: supplier.groupId,
    inn: supplier.inn ?? '',
    legalAddress: supplier.legalAddress ?? '',
    contactPerson: supplier.contactPerson ?? '',
    phone: supplier.phone ?? '',
    email: supplier.email ?? '',
    startingBalance: supplier.startingBalance,
    comment: supplier.comment ?? '',
  }
}

export function createEmptyAccountingTypeForm(): DictionaryAccountingTypeFormState {
  return {
    name: '',
    code: '',
  }
}

export function createAccountingTypeFormFromDto(type: AccountingTypeDto): DictionaryAccountingTypeFormState {
  return {
    name: type.name,
    code: type.code ?? '',
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
  return dictionarySectionOptions.some((item) => item.key === section)
}

export function getDictionarySearchPlaceholder(section: DictionarySectionKey) {
  return dictionarySearchPlaceholders[section]
}

export function getDictionaryTableHeaders(section: DictionarySectionKey) {
  return dictionaryTableHeaders[section]
}

export function getDictionaryEditorFieldMeta(key: DictionaryEditorFieldKey) {
  return dictionaryEditorFieldMeta[key]
}

export function getTariffCalculationBaseOptions() {
  return tariffCalculationBaseOptions
}

export function getTariffCalculationUnitName(calculationBase: string) {
  return tariffCalculationUnitNames[calculationBase] ?? ''
}

export function usesElectricityTariffTiers(calculationBase: string) {
  return calculationBase === electricityTierCalculationBase
}

export function getDictionaryRecordCells(section: DictionarySectionKey, item: DictionaryRecord): Array<string | number> {
  if (section === 'owners') {
    const owner = item as OwnerDto
    return [owner.fullName, owner.garageNumbers?.length ? owner.garageNumbers.join(', ') : 'без гаража', owner.phone ?? 'не указан', owner.address ?? 'не указан']
  }

  if (section === 'garages') {
    const garage = item as GarageDto
    return [garage.number, garage.ownerName ?? 'без владельца', garage.peopleCount, garage.floorCount, formatMoney(garage.startingBalance)]
  }

  if (section === 'supplierGroups') {
    const group = item as SupplierGroupDto
    return [group.name, group.isSystem ? 'Системная' : 'Пользовательская']
  }

  if (section === 'suppliers') {
    const supplier = item as SupplierDto
    return [supplier.name, supplier.groupName, supplier.inn ?? 'не указан', formatMoney(supplier.startingBalance)]
  }

  if (section === 'tariffs') {
    const tariff = item as TariffDto
    return [tariff.name, tariff.calculationBase, formatTariffRateSummary(tariff), formatDateOnly(tariff.effectiveFrom)]
  }

  const type = item as AccountingTypeDto
  return [type.name, type.code ?? 'не указан', type.isSystem ? 'Системный' : 'Пользовательский']
}

export function getDictionarySectionOption(section: DictionarySectionKey) {
  return dictionarySectionOptions.find((item) => item.key === section) ?? dictionarySectionOptions[0]
}

export function canWriteDictionarySection(section: DictionarySectionKey, canWriteDictionaries: boolean, canManageTariffs: boolean) {
  return getDictionarySectionOption(section).writePermission === 'tariffs' ? canManageTariffs : canWriteDictionaries
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

export function getOwnerGarageOptions(garages: GarageDto[], owner?: OwnerDto) {
  return garages.filter((garage) => !garage.ownerId || (owner ? garage.ownerId === owner.id : false))
}
