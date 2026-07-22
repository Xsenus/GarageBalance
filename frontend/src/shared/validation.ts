import type { TariffDto, UpsertAccountingTypeRequest, UpsertGarageRequest, UpsertOwnerRequest, UpsertSupplierGroupRequest, UpsertSupplierRequest, UpsertTariffRequest } from '../services/dictionariesApi'
import type { AccountingTypeDto } from '../services/dictionariesApi'
import type {
  CreateAccrualRequest,
  CreateExpenseOperationRequest,
  CreateIncomeOperationRequest,
  CreateMeterReadingRequest,
  CreateSupplierAccrualRequest,
  GenerateRegularAccrualsRequest,
  GenerateSupplierGroupSalaryAccrualsRequest,
} from '../services/financeApi'
import { isCompleteRussianPhone } from './phoneNumber'

export type OwnerGarageLinkForm = {
  existingGarageId: string
  newGarageNumber: string
  peopleCount: number
  floorCount: number
  startingBalance: number
  initialWaterMeterValue: string
  initialElectricityMeterValue: string
  comment: string
}

export type IncomeReportFilters = {
  dateFrom: string
  dateTo: string
  search: string
  garageIds: string[]
  ownerIds: string[]
  incomeTypeIds: string[]
  rowMode: string
}

export type ConsolidatedReportFilters = {
  monthFrom: string
  monthTo: string
  search: string
}

export type ExpenseReportFilters = {
  dateFrom: string
  dateTo: string
  search: string
  supplierIds: string[]
  expenseTypeIds: string[]
  rowMode: string
}

export function getPasswordPolicyErrors(password: string, emptyMessage = 'Укажите пароль.') {
  const errors: string[] = []
  if (!password.trim()) {
    errors.push(emptyMessage)
  } else {
    if (password.length < 8) {
      errors.push('Пароль должен быть не короче 8 символов.')
    }
  }

  return errors
}

export function getAuthValidationErrors(mode: 'bootstrap' | 'login', email: string, displayName: string, password: string) {
  const errors: string[] = []
  const trimmedEmail = email.trim()

  if (!trimmedEmail) {
    errors.push('Укажите email.')
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)) {
    errors.push('Проверьте формат email.')
  }

  if (mode === 'bootstrap' && !displayName.trim()) {
    errors.push('Укажите имя пользователя.')
  }

  errors.push(...getPasswordPolicyErrors(password))

  return errors
}

export function getPasswordChangeValidationErrors(currentPassword: string, newPassword: string, repeatPassword: string) {
  const errors: string[] = []

  if (!currentPassword) {
    errors.push('Укажите текущий пароль.')
  }

  errors.push(...getPasswordPolicyErrors(newPassword, 'Укажите новый пароль.'))

  if (!repeatPassword) {
    errors.push('Повторите новый пароль.')
  } else if (newPassword !== repeatPassword) {
    errors.push('Новый пароль и повтор пароля не совпадают.')
  }

  return errors
}

export function getManagedUserValidationErrors(email: string, displayName: string, password: string, roleCode: string) {
  const errors: string[] = []
  const trimmedEmail = email.trim()

  if (!trimmedEmail) {
    errors.push('Укажите email пользователя.')
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)) {
    errors.push('Проверьте формат email пользователя.')
  }

  if (!displayName.trim()) {
    errors.push('Укажите имя пользователя.')
  }

  errors.push(...getPasswordPolicyErrors(password, 'Укажите пароль пользователя.'))

  if (!roleCode) {
    errors.push('Выберите роль пользователя.')
  }

  return errors
}

export function getOwnerValidationErrors(form: UpsertOwnerRequest) {
  const errors: string[] = []

  if (!form.lastName.trim()) {
    errors.push('Укажите фамилию владельца.')
  }

  if (!form.firstName.trim()) {
    errors.push('Укажите имя владельца.')
  }

  if (!isCompleteRussianPhone(form.phone)) {
    errors.push('Телефон владельца должен быть указан в формате +7 (999) 123-45-67.')
  }

  return errors
}

export function getOwnerGarageLinkValidationErrors(form: OwnerGarageLinkForm) {
  const errors: string[] = []
  const hasNewGarageDetails =
    Boolean(form.newGarageNumber.trim()) ||
    form.peopleCount !== 1 ||
    form.floorCount !== 1 ||
    form.startingBalance !== 0 ||
    form.initialWaterMeterValue.trim() !== '' ||
    form.initialElectricityMeterValue.trim() !== '' ||
    form.comment.trim() !== ''

  if (hasNewGarageDetails && !form.newGarageNumber.trim()) {
    errors.push('Укажите номер нового гаража или очистите поля создания гаража.')
  }

  if (form.newGarageNumber.trim()) {
    errors.push(...getGarageValidationErrors({
      number: form.newGarageNumber,
      peopleCount: form.peopleCount,
      floorCount: form.floorCount,
      ownerId: null,
      startingBalance: form.startingBalance,
      initialWaterMeterValue: form.initialWaterMeterValue === '' ? null : Number(form.initialWaterMeterValue),
      initialElectricityMeterValue: form.initialElectricityMeterValue === '' ? null : Number(form.initialElectricityMeterValue),
      comment: form.comment.trim() || undefined,
    }))
  }

  return errors
}

export function getGarageValidationErrors(form: UpsertGarageRequest) {
  const errors: string[] = []

  if (!form.number.trim()) {
    errors.push('Укажите номер гаража.')
  }

  if (!Number.isInteger(form.peopleCount) || form.peopleCount < 0) {
    errors.push('Количество людей должно быть целым числом 0 или больше.')
  }

  if (!Number.isInteger(form.floorCount) || form.floorCount < 0) {
    errors.push('Количество этажей должно быть целым числом 0 или больше.')
  }

  if (!Number.isFinite(form.startingBalance)) {
    errors.push('Укажите корректный стартовый баланс гаража.')
  }

  if (form.initialWaterMeterValue != null && (!Number.isFinite(form.initialWaterMeterValue) || form.initialWaterMeterValue < 0)) {
    errors.push('Стартовый счетчик воды должен быть 0 или больше.')
  }

  if (form.initialElectricityMeterValue != null && (!Number.isFinite(form.initialElectricityMeterValue) || form.initialElectricityMeterValue < 0)) {
    errors.push('Стартовый счетчик электричества должен быть 0 или больше.')
  }

  return errors
}

export function getSupplierGroupValidationErrors(form: UpsertSupplierGroupRequest) {
  const errors: string[] = []

  if (!form.name.trim()) {
    errors.push('Укажите группу поставщиков.')
  }

  return errors
}

export function getSupplierValidationErrors(form: UpsertSupplierRequest) {
  const errors: string[] = []
  const trimmedInn = form.inn?.trim()

  if (!form.name.trim()) {
    errors.push('Укажите название поставщика.')
  }

  if (!form.groupId) {
    errors.push('Выберите группу поставщика.')
  }

  if (trimmedInn && !/^\d{10}(\d{2})?$/.test(trimmedInn)) {
    errors.push('ИНН поставщика должен содержать 10 или 12 цифр.')
  }

  if (!isCompleteRussianPhone(form.phone)) {
    errors.push('Телефон поставщика должен быть указан в формате +7 (999) 123-45-67.')
  }

  if (!Number.isFinite(form.startingBalance)) {
    errors.push('Укажите корректный стартовый баланс поставщика.')
  }

  return errors
}

export function getAccountingTypeValidationErrors(form: UpsertAccountingTypeRequest, title: string) {
  const errors: string[] = []
  const code = form.code?.trim()

  if (!form.name.trim()) {
    errors.push(`Укажите название ${title}.`)
  }

  if (code && !/^[a-z0-9_-]+$/i.test(code)) {
    errors.push(`Код ${title} должен содержать только латиницу, цифры, дефис или подчеркивание.`)
  }

  return errors
}

export function getTariffValidationErrors(form: UpsertTariffRequest) {
  const errors: string[] = []

  if (!form.name.trim()) {
    errors.push('Укажите название тарифа.')
  }

  if (!['fixed', 'people', 'meter_water', 'meter_electricity'].includes(form.calculationBase)) {
    errors.push('Выберите базу расчета тарифа.')
  }

  if (!Number.isFinite(form.rate) || form.rate <= 0) {
    errors.push('Ставка тарифа должна быть больше 0.')
  }

  if (form.calculationBase === 'meter_electricity') {
    const tierValues = [
      form.electricityFirstThreshold,
      form.electricitySecondThreshold,
      form.electricityFirstRate,
      form.electricitySecondRate,
      form.electricityThirdRate,
    ]
    const hasAnyTierValue = tierValues.some((value) => value !== undefined)

    if (hasAnyTierValue) {
      if (tierValues.some((value) => value === undefined)) {
        errors.push('Для трехтарифной электроэнергии заполните два порога и три ставки.')
      } else if (tierValues.some((value) => !Number.isFinite(Number(value)) || Number(value) <= 0)) {
        errors.push('Пороги и ставки электроэнергии должны быть больше 0.')
      } else if (Number(form.electricitySecondThreshold) <= Number(form.electricityFirstThreshold)) {
        errors.push('Второй порог электроэнергии должен быть больше первого.')
      }
    }
  }

  if (!form.effectiveFrom || Number.isNaN(Date.parse(form.effectiveFrom))) {
    errors.push('Укажите дату начала тарифа.')
  }

  return errors
}

export function isDateInputValue(value: string) {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(Date.parse(value))
}

export function parseOptionalNumberInput(value: string): number | undefined {
  return value === '' ? undefined : Number(value)
}

export function createTariffFormFromDto(tariff: TariffDto): UpsertTariffRequest {
  const form: UpsertTariffRequest = {
    name: tariff.name,
    calculationBase: tariff.calculationBase,
    rate: tariff.rate,
    effectiveFrom: tariff.effectiveFrom,
    comment: tariff.comment ?? '',
  }

  const hasElectricityTiers = tariff.electricityFirstThreshold !== null
    && tariff.electricitySecondThreshold !== null
    && tariff.electricityFirstRate !== null
    && tariff.electricitySecondRate !== null
    && tariff.electricityThirdRate !== null

  return hasElectricityTiers
    ? {
        ...form,
        electricityFirstThreshold: tariff.electricityFirstThreshold!,
        electricitySecondThreshold: tariff.electricitySecondThreshold!,
        electricityFirstRate: tariff.electricityFirstRate!,
        electricitySecondRate: tariff.electricitySecondRate!,
        electricityThirdRate: tariff.electricityThirdRate!,
      }
    : form
}

export function withoutElectricityTierFields(form: UpsertTariffRequest): UpsertTariffRequest {
  return {
    name: form.name,
    calculationBase: form.calculationBase,
    rate: form.rate,
    effectiveFrom: form.effectiveFrom,
    comment: form.comment,
  }
}

export function updateTariffCalculationBase(form: UpsertTariffRequest, calculationBase: string): UpsertTariffRequest {
  const nextForm = { ...form, calculationBase }
  return calculationBase === 'meter_electricity' ? nextForm : withoutElectricityTierFields(nextForm)
}

export function isAccountingMonthValue(value: string) {
  return /^\d{4}-\d{2}-01$/.test(value) && !Number.isNaN(Date.parse(value))
}

function addPositiveAmountValidation(errors: string[], amount: number, label: string) {
  if (!Number.isFinite(amount) || amount <= 0) {
    errors.push(`${label} должна быть больше 0.`)
  }
}

export function getIncomeValidationErrors(form: CreateIncomeOperationRequest) {
  const errors: string[] = []

  if (!form.garageId) {
    errors.push('Выберите гараж для поступления.')
  }

  if (!form.incomeTypeId) {
    errors.push('Выберите вид поступления.')
  }

  if (!isDateInputValue(form.operationDate)) {
    errors.push('Укажите дату поступления.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц поступления.')
  }

  addPositiveAmountValidation(errors, form.amount, 'Сумма поступления')

  return errors
}

export function getExpenseValidationErrors(form: CreateExpenseOperationRequest) {
  const errors: string[] = []

  if (!form.supplierId) {
    errors.push('Выберите поставщика для выплаты.')
  }

  if (!form.expenseTypeId) {
    errors.push('Выберите вид выплаты.')
  }

  if (!isDateInputValue(form.operationDate)) {
    errors.push('Укажите дату выплаты.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц выплаты.')
  }

  addPositiveAmountValidation(errors, form.amount, 'Сумма выплаты')

  return errors
}

export function getAccrualValidationErrors(form: CreateAccrualRequest) {
  const errors: string[] = []

  if (!form.garageId) {
    errors.push('Выберите гараж для начисления.')
  }

  if (!form.incomeTypeId) {
    errors.push('Выберите вид начисления.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц начисления.')
  }

  addPositiveAmountValidation(errors, form.amount, 'Сумма начисления')

  if (!form.comment?.trim()) {
    errors.push('Укажите комментарий начисления.')
  }

  return errors
}

export function getSupplierAccrualValidationErrors(form: CreateSupplierAccrualRequest) {
  const errors: string[] = []

  if (!form.supplierId) {
    errors.push('Выберите поставщика для начисления.')
  }

  if (!form.expenseTypeId) {
    errors.push('Выберите вид начисления поставщику.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц начисления поставщику.')
  }

  addPositiveAmountValidation(errors, form.amount, 'Сумма начисления поставщику')

  if (!form.comment?.trim()) {
    errors.push('Укажите комментарий начисления поставщику.')
  }

  return errors
}

export function getSupplierGroupSalaryValidationErrors(form: GenerateSupplierGroupSalaryAccrualsRequest) {
  const errors: string[] = []

  if (!form.supplierGroupId) {
    errors.push('Выберите группу персонала.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц зарплаты.')
  }

  addPositiveAmountValidation(errors, form.amount, 'Сумма зарплаты')

  return errors
}

export function getRegularAccrualValidationErrors(form: GenerateRegularAccrualsRequest) {
  const errors: string[] = []

  if (!form.incomeTypeId) {
    errors.push('Выберите вид регулярного начисления.')
  }

  if (!form.tariffId) {
    errors.push('Выберите тариф регулярного начисления.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц регулярных начислений.')
  }

  return errors
}

export function getRegularAccrualValidationErrorsForCatalog(
  form: GenerateRegularAccrualsRequest,
  incomeTypes: AccountingTypeDto[],
  tariffs: TariffDto[],
) {
  const errors = getRegularAccrualValidationErrors(form)
  const incomeType = incomeTypes.find((item) => item.id === form.incomeTypeId)
  const tariff = tariffs.find((item) => item.id === form.tariffId)

  if (incomeType && tariff && !isTariffCompatibleWithRegularIncomeType(incomeType, tariff)) {
    errors.push('Выбранный тариф не подходит для этого вида регулярного начисления.')
  }

  return errors
}

export function getRegularIncomeTypeCalculationBase(incomeType?: AccountingTypeDto | null) {
  switch (incomeType?.code?.trim().toLowerCase()) {
    case 'water':
      return 'meter_water'
    case 'trash':
      return 'people'
    case 'electricity':
      return 'meter_electricity'
    case 'membership':
    case 'target':
    case 'entry':
    case 'connection':
      return 'fixed'
    default:
      return null
  }
}

export function isTariffCompatibleWithRegularIncomeType(incomeType: AccountingTypeDto, tariff: TariffDto) {
  const calculationBase = getRegularIncomeTypeCalculationBase(incomeType)
  return calculationBase === null || tariff.calculationBase === calculationBase
}

export function getCompatibleRegularTariffs(incomeTypeId: string, incomeTypes: AccountingTypeDto[], tariffs: TariffDto[]) {
  const incomeType = incomeTypes.find((item) => item.id === incomeTypeId)
  return incomeType ? tariffs.filter((tariff) => isTariffCompatibleWithRegularIncomeType(incomeType, tariff)) : tariffs
}

export function chooseRegularTariffId(incomeTypeId: string, currentTariffId: string, incomeTypes: AccountingTypeDto[], tariffs: TariffDto[]) {
  const compatibleTariffs = getCompatibleRegularTariffs(incomeTypeId, incomeTypes, tariffs)
  return compatibleTariffs.some((tariff) => tariff.id === currentTariffId) ? currentTariffId : compatibleTariffs[0]?.id ?? ''
}

export function getMeterReadingValidationErrors(form: CreateMeterReadingRequest) {
  const errors: string[] = []

  if (!form.garageId) {
    errors.push('Выберите гараж для счетчика.')
  }

  if (!['water', 'electricity'].includes(form.meterKind)) {
    errors.push('Выберите тип счетчика.')
  }

  if (!isAccountingMonthValue(form.accountingMonth)) {
    errors.push('Укажите месяц показания.')
  }

  if (!isDateInputValue(form.readingDate)) {
    errors.push('Укажите дату показания.')
  }

  if (!Number.isFinite(form.currentValue) || form.currentValue < 0) {
    errors.push('Новое показание должно быть 0 или больше.')
  }

  return errors
}

export function getReportMonthRangeValidationErrors(filters: ConsolidatedReportFilters) {
  const errors: string[] = []

  if (!isAccountingMonthValue(filters.monthFrom)) {
    errors.push('Укажите начало периода отчета.')
  }

  if (!isAccountingMonthValue(filters.monthTo)) {
    errors.push('Укажите конец периода отчета.')
  }

  if (isAccountingMonthValue(filters.monthFrom) && isAccountingMonthValue(filters.monthTo) && filters.monthFrom > filters.monthTo) {
    errors.push('Начало периода отчета не может быть позже конца.')
  }

  return errors
}

export function getReportDateRangeValidationErrors(dateFrom: string, dateTo: string, label: string) {
  const errors: string[] = []

  if (!isDateInputValue(dateFrom)) {
    errors.push(`Укажите начало ${label}.`)
  }

  if (!isDateInputValue(dateTo)) {
    errors.push(`Укажите конец ${label}.`)
  }

  if (isDateInputValue(dateFrom) && isDateInputValue(dateTo) && dateFrom > dateTo) {
    errors.push(`Начало ${label} не может быть позже конца.`)
  }

  return errors
}

export function getIncomeReportValidationErrors(filters: IncomeReportFilters) {
  return getReportDateRangeValidationErrors(filters.dateFrom, filters.dateTo, 'отчета по поступлениям')
}

export function getExpenseReportValidationErrors(filters: ExpenseReportFilters) {
  return getReportDateRangeValidationErrors(filters.dateFrom, filters.dateTo, 'отчета по выплатам')
}
