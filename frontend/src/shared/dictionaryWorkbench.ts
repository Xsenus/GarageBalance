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
  ariaLabel: string
  placeholder?: string
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
  supplierGroups: 'Поиск для этого справочника пока не применяется',
  suppliers: 'Название, ИНН или контакт',
  incomeTypes: 'Поиск для этого справочника пока не применяется',
  expenseTypes: 'Поиск для этого справочника пока не применяется',
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
  ownerLastName: { ariaLabel: 'Фамилия владельца', placeholder: 'Фамилия' },
  ownerFirstName: { ariaLabel: 'Имя владельца', placeholder: 'Имя' },
  ownerMiddleName: { ariaLabel: 'Отчество владельца', placeholder: 'Отчество' },
  ownerPhone: { ariaLabel: 'Телефон владельца', placeholder: 'Телефон' },
  ownerAddress: { ariaLabel: 'Адрес владельца', placeholder: 'Адрес' },
  ownerMeterNotes: { ariaLabel: 'Комментарий владельца по счетчикам', placeholder: 'Комментарий по счетчикам' },
  ownerExistingGarage: { ariaLabel: 'Привязать существующий гараж' },
  ownerNewGarageNumber: { ariaLabel: 'Номер нового гаража владельца', placeholder: 'Новый гараж' },
  ownerNewGaragePeopleCount: { ariaLabel: 'Количество людей в новом гараже' },
  ownerNewGarageFloorCount: { ariaLabel: 'Количество этажей в новом гараже' },
  ownerNewGarageStartingBalance: { ariaLabel: 'Стартовый баланс нового гаража' },
  ownerNewGarageInitialWaterMeterValue: { ariaLabel: 'Стартовый счетчик воды нового гаража' },
  ownerNewGarageInitialElectricityMeterValue: { ariaLabel: 'Стартовый счетчик электричества нового гаража' },
  ownerNewGarageComment: { ariaLabel: 'Комментарий нового гаража', placeholder: 'Комментарий по гаражу' },
  garageNumber: { ariaLabel: 'Номер гаража', placeholder: 'Номер' },
  garagePeopleCount: { ariaLabel: 'Количество людей' },
  garageFloorCount: { ariaLabel: 'Количество этажей' },
  garageOwner: { ariaLabel: 'Владелец гаража' },
  garageStartingBalance: { ariaLabel: 'Стартовый баланс гаража' },
  garageInitialWaterMeterValue: { ariaLabel: 'Стартовый счетчик воды' },
  garageInitialElectricityMeterValue: { ariaLabel: 'Стартовый счетчик электричества' },
  garageComment: { ariaLabel: 'Комментарий по гаражу', placeholder: 'Комментарий' },
  supplierGroupName: { ariaLabel: 'Группа поставщиков', placeholder: 'Группа' },
  supplierName: { ariaLabel: 'Название поставщика', placeholder: 'Название' },
  supplierGroup: { ariaLabel: 'Группа для поставщика' },
  supplierInn: { ariaLabel: 'ИНН поставщика', placeholder: 'ИНН' },
  supplierLegalAddress: { ariaLabel: 'Юридический адрес поставщика', placeholder: 'Юридический адрес' },
  supplierContactPerson: { ariaLabel: 'Контактное лицо поставщика', placeholder: 'Контактное лицо' },
  supplierPhone: { ariaLabel: 'Телефон поставщика', placeholder: 'Телефон' },
  supplierEmail: { ariaLabel: 'Email поставщика', placeholder: 'Email' },
  supplierStartingBalance: { ariaLabel: 'Стартовый баланс поставщика' },
  supplierComment: { ariaLabel: 'Комментарий поставщика', placeholder: 'Комментарий' },
  accountingTypeName: { ariaLabel: 'Название вида операции', placeholder: 'Название' },
  accountingTypeCode: { ariaLabel: 'Код вида операции', placeholder: 'Код' },
  tariffName: { ariaLabel: 'Название тарифа', placeholder: 'Название' },
  tariffCalculationBase: { ariaLabel: 'База расчета тарифа' },
  tariffRate: { ariaLabel: 'Ставка тарифа' },
  tariffEffectiveFrom: { ariaLabel: 'Дата начала тарифа' },
  tariffElectricityFirstThreshold: { ariaLabel: 'Первый порог электроэнергии', placeholder: 'Порог 1, кВт' },
  tariffElectricitySecondThreshold: { ariaLabel: 'Второй порог электроэнергии', placeholder: 'Порог 2, кВт' },
  tariffElectricityFirstRate: { ariaLabel: 'Первая ставка электроэнергии', placeholder: 'Ставка 1' },
  tariffElectricitySecondRate: { ariaLabel: 'Вторая ставка электроэнергии', placeholder: 'Ставка 2' },
  tariffElectricityThirdRate: { ariaLabel: 'Третья ставка электроэнергии', placeholder: 'Ставка 3' },
  tariffComment: { ariaLabel: 'Комментарий тарифа', placeholder: 'Комментарий' },
}

const tariffCalculationBaseOptions: TariffCalculationBaseOption[] = [
  { value: 'fixed', label: 'Фиксированно' },
  { value: 'people', label: 'По людям' },
  { value: 'meter_water', label: 'По счетчику воды' },
  { value: 'meter_electricity', label: 'По счетчику электричества' },
]

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
  return section === 'owners' || section === 'garages' || section === 'suppliers' || section === 'tariffs'
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
