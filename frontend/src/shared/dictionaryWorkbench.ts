import type { AccountingTypeDto, GarageDto, MeasurementUnitDto, OwnerDto } from '../services/dictionariesApi'
import { formatMoney } from './formatters'
import type { OwnerGarageLinkForm } from './validation'
import { garageBalanceSignHelp, toDisplayedGarageStartingBalance } from './garageOpeningBalance'

export type DictionarySectionKey = 'owners' | 'garages' | 'incomeTypes' | 'expenseTypes' | 'measurementUnits'
export type DictionarySectionGroupKey = 'counterparties' | 'operations' | 'tariffs'
export type DictionaryWritePermission = 'dictionaries'
export type DictionaryRecord = OwnerDto | GarageDto | AccountingTypeDto | MeasurementUnitDto

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
  startingOverdueDebt: number
  initialWaterMeterValue: string
  initialElectricityMeterValue: string
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
  | 'garageStartingOverdueDebt'
  | 'garageInitialWaterMeterValue'
  | 'garageInitialElectricityMeterValue'
  | 'garageComment'
  | 'accountingTypeName'
  | 'accountingTypeCode'
  | 'measurementUnitName'

export type TariffCalculationBaseOption = {
  value: string
  label: string
}

export const dictionarySectionGroups: Array<{ key: DictionarySectionGroupKey; label: string }> = [
  { key: 'counterparties', label: 'Контрагенты' },
  { key: 'operations', label: 'Финансовые статьи' },
  { key: 'tariffs', label: 'Тарифы' },
]

export const dictionarySectionOptions: DictionarySectionOption[] = [
  { key: 'owners', label: 'Владельцы', group: 'counterparties', writePermission: 'dictionaries' },
  { key: 'garages', label: 'Гаражи', group: 'counterparties', writePermission: 'dictionaries' },
  { key: 'incomeTypes', label: 'Виды поступлений', group: 'operations', writePermission: 'dictionaries' },
  { key: 'expenseTypes', label: 'Статьи расходов', group: 'operations', writePermission: 'dictionaries' },
  { key: 'measurementUnits', label: 'Единицы измерения', group: 'tariffs', writePermission: 'dictionaries' },
]

const dictionarySearchPlaceholders: Record<DictionarySectionKey, string> = {
  owners: 'ФИО или телефон',
  garages: 'Номер гаража или ФИО владельца',
  incomeTypes: 'Название или код поступления',
  expenseTypes: 'Название или код выплаты',
  measurementUnits: 'Обозначение единицы измерения',
}

const dictionaryTableHeaders: Record<DictionarySectionKey, string[]> = {
  owners: ['ФИО', 'Гаражи', 'Телефон', 'Адрес'],
  garages: ['Номер', 'Владелец', 'Людей', 'Этажей', 'Стартовый баланс'],
  incomeTypes: ['Название', 'Код', 'Тип'],
  expenseTypes: ['Название', 'Код', 'Тип'],
  measurementUnits: ['Обозначение'],
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
  garageStartingBalance: { label: 'Стартовый баланс', ariaLabel: 'Стартовый баланс гаража', hint: garageBalanceSignHelp },
  garageStartingOverdueDebt: { label: 'Начальная просрочка', ariaLabel: 'Начальная просрочка' },
  garageInitialWaterMeterValue: { label: 'Старт воды', ariaLabel: 'Стартовый счетчик воды' },
  garageInitialElectricityMeterValue: { label: 'Старт электричества', ariaLabel: 'Стартовый счетчик электричества' },
  garageComment: { label: 'Комментарий', ariaLabel: 'Комментарий по гаражу', placeholder: 'Особенности гаража, начислений или импорта' },
  accountingTypeName: { label: 'Название', ariaLabel: 'Название вида операции', placeholder: 'Например, Членский взнос' },
  accountingTypeCode: { label: 'Код', ariaLabel: 'Код вида операции', placeholder: 'Например, security_2026' },
  measurementUnitName: { label: 'Обозначение', ariaLabel: 'Обозначение единицы измерения', placeholder: 'Например, м³' },
}

const tariffCalculationBaseOptions: TariffCalculationBaseOption[] = [
  { value: 'fixed', label: 'Фиксированно' },
  { value: 'people', label: 'По людям' },
  { value: 'meter_water', label: 'По счетчику воды' },
  { value: 'meter_electricity', label: 'По счетчику электричества' },
]

const tariffCalculationUnitNames: Record<string, string[]> = {
  fixed: ['руб.', 'руб./гараж'],
  people: ['чел.', 'человек'],
  meter_water: ['м³', 'куб. м'],
  meter_electricity: ['кВт·ч'],
}

const tieredMeterCalculationBases = new Set(['meter_water', 'meter_electricity'])

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
    startingOverdueDebt: 0,
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
    startingBalance: toDisplayedGarageStartingBalance(garage.startingBalance),
    startingOverdueDebt: garage.startingOverdueDebt,
    initialWaterMeterValue: garage.initialWaterMeterValue?.toString() ?? '',
    initialElectricityMeterValue: garage.initialElectricityMeterValue?.toString() ?? '',
    comment: garage.comment ?? '',
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

export function getTariffCalculationBaseLabel(calculationBase: string) {
  if (calculationBase === 'water') {
    return 'По счетчику воды'
  }

  return tariffCalculationBaseOptions.find((option) => option.value === calculationBase)?.label ?? calculationBase
}

export function getTariffCalculationUnitName(calculationBase: string) {
  return tariffCalculationUnitNames[calculationBase]?.[0] ?? ''
}

export function normalizeTariffCalculationUnitName(calculationBase: string, unitName?: string | null) {
  const compatibleUnitNames = tariffCalculationUnitNames[calculationBase] ?? []
  const trimmedUnitName = unitName?.trim() ?? ''
  const normalizedUnitName = trimmedUnitName.toLocaleLowerCase('ru')
  const matchingUnitName = compatibleUnitNames.find((candidate) => candidate.toLocaleLowerCase('ru') === normalizedUnitName)
  if (matchingUnitName) return matchingUnitName

  const knownForAnotherBase = Object.values(tariffCalculationUnitNames)
    .flat()
    .some((candidate) => candidate.toLocaleLowerCase('ru') === normalizedUnitName)
  if (trimmedUnitName && !knownForAnotherBase) return trimmedUnitName

  return compatibleUnitNames[0] ?? ''
}

export function usesElectricityTariffTiers(calculationBase: string) {
  return tieredMeterCalculationBases.has(calculationBase)
}

export function getDictionaryRecordCells(section: DictionarySectionKey, item: DictionaryRecord): Array<string | number> {
  if (section === 'owners') {
    const owner = item as OwnerDto
    return [owner.fullName, owner.garageNumbers?.length ? owner.garageNumbers.join(', ') : 'без гаража', owner.phone ?? 'не указан', owner.address ?? 'не указан']
  }

  if (section === 'garages') {
    const garage = item as GarageDto
    return [garage.number, garage.ownerName ?? 'без владельца', garage.peopleCount, garage.floorCount, formatMoney(toDisplayedGarageStartingBalance(garage.startingBalance))]
  }

  if (section === 'measurementUnits') {
    return [(item as MeasurementUnitDto).name]
  }

  const type = item as AccountingTypeDto
  return [type.name, type.code ?? 'не указан', type.isSystem ? 'Системный' : 'Пользовательский']
}

export function getDictionarySectionOption(section: DictionarySectionKey) {
  return dictionarySectionOptions.find((item) => item.key === section) ?? dictionarySectionOptions[0]
}

export function canWriteDictionarySection(section: DictionarySectionKey, canWriteDictionaries: boolean) {
  const option = dictionarySectionOptions.find((item) => item.key === section)
  if (!option) {
    return false
  }

  return canWriteDictionaries
}

export function getDictionaryRecordTitle(section: DictionarySectionKey, item: DictionaryRecord) {
  if (section === 'owners') {
    return (item as OwnerDto).fullName
  }

  if (section === 'garages') {
    return `Гараж ${(item as GarageDto).number}`
  }

  if (section === 'measurementUnits') {
    return (item as MeasurementUnitDto).name
  }

  return (item as AccountingTypeDto).name
}

export function getOwnerGarageOptions(garages: GarageDto[], owner?: OwnerDto) {
  return garages.filter((garage) => !garage.ownerId || (owner ? garage.ownerId === owner.id : false))
}
