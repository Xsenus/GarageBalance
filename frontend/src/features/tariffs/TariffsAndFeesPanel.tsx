import { Fragment, useEffect, useState } from 'react'
import type { FormEvent, MouseEvent } from 'react'
import { Pencil, Plus, RotateCcw, Save, Trash2, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import { DictionaryApiError } from '../../services/dictionariesApi'
import type { AccountingTypeDto, ChargeServiceSettingDto, DictionaryClient, FeeCampaignDto, GarageDto, IrregularPaymentDto, TariffDto, UpsertChargeServiceSettingRequest, UpsertFeeCampaignRequest, UpsertIrregularPaymentRequest, UpsertTariffRequest } from '../../services/dictionariesApi'
import type { FinanceClient } from '../../services/financeApi'
import type { FormStateClient } from '../../services/formStatesApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import type { ChangePreview } from '../../shared/changePreview'
import { appendChangePreview, formatChangeDate, formatChangeMoney, formatChangeNumber, formatChangeText } from '../../shared/changePreview'
import { FormError } from '../../shared/formFeedback'
import { FormField } from '../../shared/FormField'
import { formatDateOnly, formatMoney, getCurrentMonthInputValue, getLocalDateInputValue } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { formatPrototypeChangeValue, handleEditableInputKeyDown } from '../../shared/prototypeEditing'
import { chooseRegularTariffId, getCompatibleRegularTariffs } from '../../shared/validation'

const tariffsFormStateScope = 'tariffs-and-fees-prototype'
const dictionaryScreenRequestLimit = 100

type ContractorTariffRow = {
  id: string
  backendTariffId?: string
  backendServiceSettingId?: string
  serviceSettingKind?: 'main' | 'periodicity' | 'start-date' | 'due-date' | 'overdue-days'
  title: string
  amount?: string
  dateDay?: string
  dateMonth?: string
  unit?: string
  threshold?: string
  byMeter: boolean
  tiered: boolean
  group?: string
  category: string
  calculationBase?: string
  effectiveFrom?: string
  electricityFirstThreshold?: number | null
  electricitySecondThreshold?: number | null
  isDeleted?: boolean
}

const contractorTariffRows: ContractorTariffRow[] = [
  { id: 'water-rate', group: 'Вода', category: 'Вода', title: 'Тариф на воду', amount: '', unit: 'руб.', byMeter: true, tiered: false, calculationBase: 'meter_water' },
  { id: 'water-overdue-days', category: 'Вода', title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: true, tiered: false },
  { id: 'waste-rate', group: 'Мусор', category: 'Мусор', title: 'Ставка за вывоз мусора', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'people' },
  { id: 'waste-overdue-days', category: 'Мусор', title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: false, tiered: false },
  { id: 'electricity-tier-0', group: 'Электроэнергия', category: 'Электроэнергия', title: 'От 0 кВт', threshold: 'x', amount: '', unit: 'руб.', byMeter: true, tiered: true, calculationBase: 'meter_electricity', electricityFirstThreshold: 1, electricitySecondThreshold: 3 },
  { id: 'electricity-tier-1', category: 'Электроэнергия', title: 'От 1 кВт', threshold: 'x', amount: '', unit: 'руб.', byMeter: true, tiered: true, calculationBase: 'meter_electricity', electricityFirstThreshold: 1, electricitySecondThreshold: 3 },
  { id: 'electricity-tier-3', category: 'Электроэнергия', title: 'От 3 кВт', threshold: 'x', amount: '', unit: 'руб.', byMeter: true, tiered: true, calculationBase: 'meter_electricity', electricityFirstThreshold: 1, electricitySecondThreshold: 3 },
  { id: 'electricity-overdue-days', category: 'Электроэнергия', title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: true, tiered: true },
  { id: 'membership-fee', group: 'Членский взнос', category: 'Членский взнос', title: 'Сумма членского взноса', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'fixed' },
  { id: 'membership-due-date', category: 'Членский взнос', title: 'Оплата до', dateDay: '30', dateMonth: 'июн', byMeter: false, tiered: false },
  { id: 'membership-start-date', category: 'Членский взнос', title: 'Учитывать платеж с', dateDay: '01', dateMonth: 'янв', byMeter: false, tiered: false },
  { id: 'membership-overdue-days', category: 'Членский взнос', title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: false, tiered: false },
  { id: 'target-fee', group: 'Целевой взнос', category: 'Целевой взнос', title: 'Сумма целевого взноса', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'fixed' },
  { id: 'target-due-date', category: 'Целевой взнос', title: 'Оплата за год до', dateDay: '30', dateMonth: 'июн', byMeter: false, tiered: false },
  { id: 'target-start-date', category: 'Целевой взнос', title: 'Учитывать платеж с', dateDay: '01', dateMonth: 'янв', byMeter: false, tiered: false },
  { id: 'target-overdue-days', category: 'Целевой взнос', title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: false, tiered: false },
  { id: 'lighting-rate', group: 'Наружное освещение', category: 'Наружное освещение', title: 'Наружное освещение', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'fixed' },
  { id: 'lighting-due-date', group: 'Наружное освещение', category: 'Наружное освещение', title: 'Оплата за год до', dateDay: '31', dateMonth: 'дек', byMeter: false, tiered: false },
  { id: 'lighting-start-date', category: 'Наружное освещение', title: 'Учитывать платеж с', dateDay: '01', dateMonth: 'янв', byMeter: false, tiered: false },
  { id: 'lighting-overdue-days', category: 'Наружное освещение', title: 'Перенос долга в просроченный', amount: '0', unit: 'дн.', byMeter: false, tiered: false },
  { id: 'salary-electricians', group: 'Зарплатный фонд', category: 'Зарплатный фонд', title: 'Электрики', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'fixed' },
  { id: 'salary-accounting', category: 'Зарплатный фонд', title: 'Бухгалтерия', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'fixed' },
  { id: 'salary-management', category: 'Зарплатный фонд', title: 'Руководство', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'fixed' },
]

type ContractorOneTimeRow = {
  id: string
  backendPaymentId?: string
  name: string
  amount: string
  isActive: boolean
  isDeleted: boolean
  isUsed: boolean
}

type ContractorTariffDraft = {
  title: string
  amount: string
  unit: string
  dateDay: string
  dateMonth: string
}

const contractorTariffMonthOptions = [
  { value: 'янв', label: 'Январь', maxDay: 31 },
  { value: 'фев', label: 'Февраль', maxDay: 28 },
  { value: 'мар', label: 'Март', maxDay: 31 },
  { value: 'апр', label: 'Апрель', maxDay: 30 },
  { value: 'май', label: 'Май', maxDay: 31 },
  { value: 'июн', label: 'Июнь', maxDay: 30 },
  { value: 'июл', label: 'Июль', maxDay: 31 },
  { value: 'авг', label: 'Август', maxDay: 31 },
  { value: 'сен', label: 'Сентябрь', maxDay: 30 },
  { value: 'окт', label: 'Октябрь', maxDay: 31 },
  { value: 'ноя', label: 'Ноябрь', maxDay: 30 },
  { value: 'дек', label: 'Декабрь', maxDay: 31 },
]

function createEditableDrafts(rows: Array<{ id: string; title?: string; amount?: string; unit?: string; dateDay?: string; dateMonth?: string }>) {
  return rows.reduce<Record<string, ContractorTariffDraft>>((drafts, row) => {
    drafts[row.id] = { title: row.title ?? '', amount: row.amount ?? '', unit: row.unit ?? '', dateDay: row.dateDay ?? '', dateMonth: row.dateMonth ?? '' }
    return drafts
  }, {})
}

function formatContractorTariffDate(day: string, month: string) {
  return `${day.padStart(2, '0')} ${month}`.trim()
}

function getContractorTariffDateError(day: string, month: string) {
  const trimmedDay = day.trim()
  const monthOption = contractorTariffMonthOptions.find((option) => option.value === month)

  if (!/^\d{1,2}$/.test(trimmedDay)) {
    return 'Укажите день числом от 1 до 31.'
  }

  if (!monthOption) {
    return 'Выберите месяц.'
  }

  const numericDay = Number(trimmedDay)
  if (numericDay < 1 || numericDay > monthOption.maxDay) {
    return `В месяце "${monthOption.label}" можно указать день от 1 до ${monthOption.maxDay}.`
  }

  return null
}

function formatTariffNumber(value: number | null | undefined) {
  return value == null ? '' : String(value)
}

function formatPrototypeAmount(value: number | null | undefined) {
  if (value == null) {
    return ''
  }

  return Number.isInteger(value) ? String(value) : String(value)
}

function parsePrototypeAmount(value: string) {
  const normalized = value.replace(',', '.').trim()
  if (!normalized) {
    return null
  }

  const parsed = Number(normalized)
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null
}

function parseTariffAmount(value: string) {
  const normalized = value.replace(',', '.').trim()
  if (!normalized) {
    return null
  }

  const parsed = Number(normalized)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null
}

function findTariffForPrototypeRow(tariffs: TariffDto[], row: ContractorTariffRow) {
  const lowerTitle = row.title.toLocaleLowerCase('ru')
  if (row.calculationBase === 'meter_electricity') {
    return tariffs.find((tariff) => tariff.calculationBase === 'meter_electricity') ?? null
  }

  if (row.id === 'water-rate') {
    return tariffs.find((tariff) => tariff.calculationBase === 'meter_water') ?? null
  }

  if (row.id === 'waste-rate') {
    return tariffs.find((tariff) => tariff.calculationBase === 'people' || tariff.name.toLocaleLowerCase('ru').includes('мусор')) ?? null
  }

  return tariffs.find((tariff) => tariff.name.toLocaleLowerCase('ru') === lowerTitle || tariff.name.toLocaleLowerCase('ru').includes(row.category.toLocaleLowerCase('ru'))) ?? null
}

function mergeTariffsIntoPrototypeRows(rows: ContractorTariffRow[], tariffs: TariffDto[]) {
  const electricityTariff = tariffs.find((tariff) => tariff.calculationBase === 'meter_electricity')
  return rows.map((row) => {
    if (row.calculationBase === 'meter_electricity' && electricityTariff) {
      const base = {
        ...row,
        backendTariffId: electricityTariff.id,
        effectiveFrom: electricityTariff.effectiveFrom,
        electricityFirstThreshold: electricityTariff.electricityFirstThreshold,
        electricitySecondThreshold: electricityTariff.electricitySecondThreshold,
      }
      if (row.id === 'electricity-tier-0') {
        return { ...base, title: electricityTariff.electricityFirstTierName ?? row.title, amount: formatTariffNumber(electricityTariff.electricityFirstRate) }
      }
      if (row.id === 'electricity-tier-1') {
        return { ...base, title: electricityTariff.electricitySecondTierName ?? row.title, amount: formatTariffNumber(electricityTariff.electricitySecondRate) }
      }
      return { ...base, title: electricityTariff.electricityThirdTierName ?? row.title, amount: formatTariffNumber(electricityTariff.electricityThirdRate) }
    }

    const tariff = findTariffForPrototypeRow(tariffs, row)
    return tariff && row.calculationBase
      ? { ...row, backendTariffId: tariff.id, effectiveFrom: tariff.effectiveFrom, amount: formatTariffNumber(tariff.rate), title: tariff.name }
      : row
  })
}

function createTariffRowsFromBackend(tariffs: TariffDto[], settings: ChargeServiceSettingDto[]) {
  const rowsBackedByTariffs = contractorTariffRows.filter((row) => Boolean(row.calculationBase && findTariffForPrototypeRow(tariffs, row)))
  return mergeChargeServicesIntoPrototypeRows(mergeTariffsIntoPrototypeRows(rowsBackedByTariffs, tariffs), settings)
}

function getContractorTariffMonthNumber(monthValue?: string | null) {
  if (!monthValue) {
    return null
  }

  const normalizedValue = monthValue.trim().toLocaleLowerCase('ru')
  const monthIndex = contractorTariffMonthOptions.findIndex((month) => (
    month.value === normalizedValue || month.label.toLocaleLowerCase('ru') === normalizedValue
  ))

  return monthIndex >= 0 ? monthIndex + 1 : null
}

function getContractorTariffMonthValue(monthNumber?: number | null) {
  if (!monthNumber || monthNumber < 1 || monthNumber > contractorTariffMonthOptions.length) {
    return contractorTariffMonthOptions[0].value
  }

  return contractorTariffMonthOptions[monthNumber - 1].value
}

function createChargeServiceRows(setting: ChargeServiceSettingDto): ContractorTariffRow[] {
  const rows: ContractorTariffRow[] = [
    {
      id: `charge-service-${setting.id}-main`,
      backendServiceSettingId: setting.id,
      serviceSettingKind: 'main',
      group: setting.name,
      category: setting.name,
      title: setting.name,
      amount: '',
      unit: setting.unitName ?? 'руб.',
      byMeter: setting.isMetered,
      tiered: setting.hasTieredTariff,
      isDeleted: setting.isArchived,
    },
  ]

  if (setting.isRegular) {
    rows.push(
      {
        id: `charge-service-${setting.id}-periodicity`,
        backendServiceSettingId: setting.id,
        serviceSettingKind: 'periodicity',
        category: setting.name,
        title: 'Периодичность',
        amount: String(setting.periodicityMonths ?? 12),
        unit: 'мес.',
        byMeter: setting.isMetered,
        tiered: setting.hasTieredTariff,
        isDeleted: setting.isArchived,
      },
      {
        id: `charge-service-${setting.id}-due-date`,
        backendServiceSettingId: setting.id,
        serviceSettingKind: 'due-date',
        category: setting.name,
        title: 'Оплата до',
        dateDay: setting.paymentDueDay ? String(setting.paymentDueDay).padStart(2, '0') : '01',
        dateMonth: getContractorTariffMonthValue(setting.paymentDueMonth),
        byMeter: setting.isMetered,
        tiered: setting.hasTieredTariff,
        isDeleted: setting.isArchived,
      },
      {
        id: `charge-service-${setting.id}-start-date`,
        backendServiceSettingId: setting.id,
        serviceSettingKind: 'start-date',
        category: setting.name,
        title: 'Учитывать платеж с',
        dateDay: '01',
        dateMonth: getContractorTariffMonthValue(setting.accrualStartMonth),
        byMeter: setting.isMetered,
        tiered: setting.hasTieredTariff,
        isDeleted: setting.isArchived,
      },
      {
        id: `charge-service-${setting.id}-overdue-days`,
        backendServiceSettingId: setting.id,
        serviceSettingKind: 'overdue-days',
        category: setting.name,
        title: 'Перенос долга в просроченный',
        amount: String(setting.overdueGraceDays),
        unit: 'дн.',
        byMeter: setting.isMetered,
        tiered: setting.hasTieredTariff,
        isDeleted: setting.isArchived,
      },
    )
  }

  return rows
}

function mergeChargeServicesIntoPrototypeRows(rows: ContractorTariffRow[], settings: ChargeServiceSettingDto[]) {
  const rowsWithoutBackendServices = rows.filter((row) => !row.backendServiceSettingId)
  return [
    ...rowsWithoutBackendServices,
    ...settings.flatMap((setting) => createChargeServiceRows(setting)),
  ]
}

function mergeIrregularPaymentsIntoPrototypeRows(rows: ContractorOneTimeRow[], payments: IrregularPaymentDto[], preferBackend = false) {
  const sourceRows = preferBackend && payments.length > 0
    ? rows.filter((row) => row.backendPaymentId || payments.some((payment) => payment.name.toLocaleLowerCase('ru') === row.name.toLocaleLowerCase('ru')))
    : rows
  const mergedRows = sourceRows.map((row) => {
    const payment = payments.find((item) => item.name.toLocaleLowerCase('ru') === row.name.toLocaleLowerCase('ru'))
    if (!payment) {
      return row
    }

    return {
      ...row,
      backendPaymentId: payment.id,
      amount: formatPrototypeAmount(payment.amount),
      isActive: payment.isActive,
      isDeleted: payment.isArchived,
      isUsed: payment.isUsed,
    }
  })

  const extraRows = payments
    .filter((payment) => !rows.some((row) => row.name.toLocaleLowerCase('ru') === payment.name.toLocaleLowerCase('ru')))
    .map((payment) => ({
      id: `one-time-${payment.id}`,
      backendPaymentId: payment.id,
      name: payment.name,
      amount: formatPrototypeAmount(payment.amount),
      isActive: payment.isActive,
      isDeleted: payment.isArchived,
      isUsed: payment.isUsed,
    }))

  return [...mergedRows, ...extraRows]
}

type TariffPrototypePendingChange =
  | {
    kind: 'tariff-text'
    rowId: string
    field: 'title' | 'amount' | 'unit'
    objectName: string
    fieldLabel: string
    previousValue: string
    nextValue: string
  }
  | {
    kind: 'tariff-boolean'
    rowId: string
    field: 'tiered' | 'byMeter'
    objectName: string
    fieldLabel: string
    previousValue: string
    nextValue: string
  }
  | {
    kind: 'tariff-date'
    rowId: string
    objectName: string
    fieldLabel: string
    previousValue: string
    nextValue: string
    nextDay: string
    nextMonth: string
  }
  | {
    kind: 'one-time-amount'
    rowId: string
    objectName: string
    fieldLabel: string
    previousValue: string
    nextValue: string
  }
  | {
    kind: 'one-time-active'
    rowId: string
    objectName: string
    fieldLabel: string
    previousValue: string
    nextValue: string
  }

function getTariffTextFieldLabel(row: ContractorTariffRow, field: 'title' | 'amount' | 'unit') {
  if (field === 'title') {
    return 'Наименование порога'
  }

  if (field === 'unit') {
    return 'Единица'
  }

  if (row.serviceSettingKind === 'periodicity') {
    return 'Периодичность'
  }

  if (row.serviceSettingKind === 'overdue-days') {
    return 'Перенос долга в просроченный'
  }

  return 'Значение'
}

function formatFeeCampaignParticipantsChange(appliesToAllGarages: boolean, participantGarageIds: string[], garageOptions: GarageDto[]) {
  if (appliesToAllGarages) {
    return 'Все гаражи'
  }

  const garageNumbers = participantGarageIds
    .map((garageId) => garageOptions.find((garage) => garage.id === garageId)?.number)
    .filter((number): number is string => Boolean(number))
    .sort((left, right) => left.localeCompare(right, 'ru', { numeric: true }))

  return garageNumbers.length > 0 ? garageNumbers.join(', ') : 'пусто'
}

function getFeeCampaignChangePreview(
  campaign: FeeCampaignDto,
  request: UpsertFeeCampaignRequest,
  incomeTypes: AccountingTypeDto[],
  garageOptions: GarageDto[],
) {
  const changes: ChangePreview[] = []
  const formatIncomeType = (incomeTypeId: string) => incomeTypes.find((incomeType) => incomeType.id === incomeTypeId)?.name ?? incomeTypeId

  appendChangePreview(changes, 'Наименование', formatChangeText(campaign.name), formatChangeText(request.name))
  appendChangePreview(changes, 'Вид поступления', formatIncomeType(campaign.incomeTypeId), formatIncomeType(request.incomeTypeId))
  appendChangePreview(changes, 'Цель', formatChangeText(campaign.goal), formatChangeText(request.goal))
  appendChangePreview(changes, 'Сумма взноса', formatChangeMoney(campaign.contributionAmount), formatChangeMoney(request.contributionAmount))
  appendChangePreview(changes, 'Сумма сбора', formatChangeMoney(campaign.targetAmount), formatChangeMoney(request.targetAmount))
  appendChangePreview(changes, 'Дата начала', formatChangeDate(campaign.startsOn), formatChangeDate(request.startsOn))
  appendChangePreview(changes, 'Дата окончания', formatChangeDate(campaign.endsOn), formatChangeDate(request.endsOn))
  appendChangePreview(
    changes,
    'Участники',
    formatFeeCampaignParticipantsChange(campaign.appliesToAllGarages, campaign.participantGarageIds, garageOptions),
    formatFeeCampaignParticipantsChange(request.appliesToAllGarages, request.participantGarageIds ?? [], garageOptions),
  )
  appendChangePreview(changes, 'Перенос долга в просроченный', `${formatChangeNumber(campaign.overdueGraceDays)} дн.`, `${formatChangeNumber(request.overdueGraceDays)} дн.`)

  return changes
}

type TariffsPrototypeSavedState = {
  tariffRows: ContractorTariffRow[]
  oneTimeRows: ContractorOneTimeRow[]
}

export function TariffsAndFeesPrototypePanel({ auth, dictionaryClient, financeClient, formStateClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient; financeClient: FinanceClient; formStateClient: FormStateClient }) {
  const [modal, setModal] = useState<'service' | 'fee' | null>(null)
  const [tariffRows, setTariffRows] = useState<ContractorTariffRow[]>([])
  const [backendTariffs, setBackendTariffs] = useState<TariffDto[]>([])
  const [backendIncomeTypes, setBackendIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [backendChargeServices, setBackendChargeServices] = useState<ChargeServiceSettingDto[]>([])
  const [feeCampaignGarageOptions, setFeeCampaignGarageOptions] = useState<GarageDto[]>([])
  const [feeCampaigns, setFeeCampaigns] = useState<FeeCampaignDto[]>([])
  const [feeCampaignSavingId, setFeeCampaignSavingId] = useState<string | null>(null)
  const [feeCampaignEditTarget, setFeeCampaignEditTarget] = useState<FeeCampaignDto | null>(null)
  const [feeCampaignArchiveTarget, setFeeCampaignArchiveTarget] = useState<FeeCampaignDto | null>(null)
  const [feeCampaignArchiveReason, setFeeCampaignArchiveReason] = useState('')
  const [feeCampaignRestoreTarget, setFeeCampaignRestoreTarget] = useState<FeeCampaignDto | null>(null)
  const [feeCampaignGenerateTarget, setFeeCampaignGenerateTarget] = useState<FeeCampaignDto | null>(null)
  const [feeCampaignGenerateMonth, setFeeCampaignGenerateMonth] = useState(getCurrentMonthInputValue())
  const [feeCampaignGenerateComment, setFeeCampaignGenerateComment] = useState('')
  const [feeCampaignActionMessage, setFeeCampaignActionMessage] = useState<string | null>(null)
  const [chargeServiceArchiveTarget, setChargeServiceArchiveTarget] = useState<ChargeServiceSettingDto | null>(null)
  const [chargeServiceArchiveReason, setChargeServiceArchiveReason] = useState('')
  const [chargeServiceRestoreTarget, setChargeServiceRestoreTarget] = useState<ChargeServiceSettingDto | null>(null)
  const [thresholdDeleteTarget, setThresholdDeleteTarget] = useState<ContractorTariffRow | null>(null)
  const [thresholdDeleteReason, setThresholdDeleteReason] = useState('')
  const [oneTimeRows, setOneTimeRows] = useState<ContractorOneTimeRow[]>([])
  const [tariffDrafts, setTariffDrafts] = useState<Record<string, Partial<ContractorTariffRow>>>({})
  const [oneTimeDrafts, setOneTimeDrafts] = useState<Record<string, Partial<ContractorOneTimeRow>>>({})
  const [formStateLoaded, setFormStateLoaded] = useState(false)
  const [pendingChange, setPendingChange] = useState<TariffPrototypePendingChange | null>(null)
  const [tariffDateErrors, setTariffDateErrors] = useState<Record<string, string>>({})
  const [tariffPersistenceError, setTariffPersistenceError] = useState<string | null>(null)
  const [tariffsLoading, setTariffsLoading] = useState(false)
  const [tariffSavingRowId, setTariffSavingRowId] = useState<string | null>(null)
  const [oneTimeSavingRowId, setOneTimeSavingRowId] = useState<string | null>(null)
  const [oneTimeDeleteTarget, setOneTimeDeleteTarget] = useState<ContractorOneTimeRow | null>(null)
  const [oneTimeDeleteReason, setOneTimeDeleteReason] = useState('')
  const [oneTimeRestoreTarget, setOneTimeRestoreTarget] = useState<ContractorOneTimeRow | null>(null)
  const [oneTimeContextMenu, setOneTimeContextMenu] = useState<{ row: ContractorOneTimeRow; x: number; y: number } | null>(null)
  const [oneTimeActionMessage, setOneTimeActionMessage] = useState<string | null>(null)
  const canManageTariffs = hasPermission(auth, permissions.tariffsManage)

  useEffect(() => {
    let ignore = false

    async function loadTariffsAndFees() {
      setTariffsLoading(true)
      setTariffPersistenceError(null)
      try {
        const [loadedTariffs, loadedIncomeTypes, loadedIrregularPayments, loadedChargeServices, loadedFeeCampaigns, loadedGarages] = await Promise.all([
          dictionaryClient.getTariffs(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getIncomeTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit),
          dictionaryClient.getIrregularPayments(auth.accessToken, undefined, dictionaryScreenRequestLimit, true),
          dictionaryClient.getChargeServiceSettings(auth.accessToken, undefined, dictionaryScreenRequestLimit, true),
          dictionaryClient.getFeeCampaigns(auth.accessToken, undefined, dictionaryScreenRequestLimit, true),
          dictionaryClient.getGarages(auth.accessToken, undefined, dictionaryScreenRequestLimit),
        ])
        if (!ignore) {
          const mergedRows = createTariffRowsFromBackend(loadedTariffs, loadedChargeServices)
          const mergedOneTimeRows = mergeIrregularPaymentsIntoPrototypeRows([], loadedIrregularPayments, true)
          setBackendTariffs(loadedTariffs)
          setBackendIncomeTypes(loadedIncomeTypes)
          setBackendChargeServices(loadedChargeServices)
          setFeeCampaigns(loadedFeeCampaigns)
          setFeeCampaignGarageOptions(loadedGarages)
          setTariffRows(mergedRows)
          setOneTimeRows(mergedOneTimeRows)
          setTariffDrafts(createEditableDrafts(mergedRows))
          setOneTimeDrafts(createEditableDrafts(mergedOneTimeRows))
        }
      } catch (caught) {
        if (!ignore) {
          setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось загрузить тарифы и сборы.')
        }
      } finally {
        if (!ignore) {
          setTariffsLoading(false)
        }
      }
    }

    void loadTariffsAndFees()

    return () => {
      ignore = true
    }
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => {
    let ignore = false
    formStateClient
      .getState<TariffsPrototypeSavedState>(auth.accessToken, tariffsFormStateScope)
      .catch((error: unknown) => {
        if (!ignore) {
          setTariffPersistenceError(error instanceof Error ? error.message : 'Не удалось загрузить сохраненное состояние тарифов.')
        }
      })
      .finally(() => {
        if (!ignore) {
          setFormStateLoaded(true)
        }
      })

    return () => {
      ignore = true
    }
  }, [auth.accessToken, formStateClient])

  useEffect(() => {
    if (!formStateLoaded || tariffsLoading) {
      return
    }

    const handle = window.setTimeout(() => {
      void formStateClient
        .saveState<TariffsPrototypeSavedState>(auth.accessToken, tariffsFormStateScope, {
          payload: { tariffRows, oneTimeRows },
          summary: 'Сохранено состояние формы тарифов и сборов.'
        })
        .catch((error: unknown) => setTariffPersistenceError(error instanceof Error ? error.message : 'Не удалось сохранить состояние тарифов.'))
    }, 400)

    return () => window.clearTimeout(handle)
  }, [auth.accessToken, formStateClient, formStateLoaded, oneTimeRows, tariffRows, tariffsLoading])

  function closeOneTimeDeleteDialog() {
    setOneTimeDeleteTarget(null)
    setOneTimeDeleteReason('')
  }

  function closeOneTimeRestoreDialog() {
    setOneTimeRestoreTarget(null)
  }

  function closeFeeCampaignArchiveDialog() {
    setFeeCampaignArchiveTarget(null)
    setFeeCampaignArchiveReason('')
  }

  function closeFeeCampaignEditDialog() {
    setFeeCampaignEditTarget(null)
  }

  function closeFeeCampaignRestoreDialog() {
    setFeeCampaignRestoreTarget(null)
  }

  function closeChargeServiceArchiveDialog() {
    setChargeServiceArchiveTarget(null)
    setChargeServiceArchiveReason('')
  }

  function closeChargeServiceRestoreDialog() {
    setChargeServiceRestoreTarget(null)
  }

  function closeThresholdDeleteDialog() {
    setThresholdDeleteTarget(null)
    setThresholdDeleteReason('')
  }

  function closeFeeCampaignGenerateDialog() {
    setFeeCampaignGenerateTarget(null)
    setFeeCampaignGenerateMonth(getCurrentMonthInputValue())
    setFeeCampaignGenerateComment('')
  }

  function cancelPendingChange() {
    if (pendingChange?.kind === 'tariff-text') {
      setTariffDrafts((drafts) => ({
        ...drafts,
        [pendingChange.rowId]: {
          ...drafts[pendingChange.rowId],
          [pendingChange.field]: pendingChange.previousValue,
        },
      }))
    } else if (pendingChange?.kind === 'tariff-date') {
      const [previousDay = '', previousMonth = ''] = pendingChange.previousValue.split(' ')
      setTariffDrafts((drafts) => ({
        ...drafts,
        [pendingChange.rowId]: {
          ...drafts[pendingChange.rowId],
          dateDay: previousDay,
          dateMonth: previousMonth,
        },
      }))
    } else if (pendingChange?.kind === 'one-time-amount') {
      setOneTimeDrafts((drafts) => ({
        ...drafts,
        [pendingChange.rowId]: {
          ...drafts[pendingChange.rowId],
          amount: pendingChange.previousValue,
        },
      }))
    }

    setPendingChange(null)
  }

  async function confirmPendingChange() {
    if (!pendingChange) {
      return
    }

    if (pendingChange.kind === 'tariff-text') {
      const sourceRow = tariffRows.find((currentRow) => currentRow.id === pendingChange.rowId)
      const nextRows = tariffRows.map((currentRow) => (
        currentRow.id === pendingChange.rowId ? { ...currentRow, [pendingChange.field]: pendingChange.nextValue } : currentRow
      ))
      setTariffRows(nextRows)
      if (sourceRow && (sourceRow.backendServiceSettingId || pendingChange.field !== 'unit')) {
        await persistTariffRow(sourceRow, nextRows)
      }
    } else if (pendingChange.kind === 'tariff-boolean') {
      const sourceRow = tariffRows.find((currentRow) => currentRow.id === pendingChange.rowId)
      const nextRows = tariffRows.map((currentRow) => (
        currentRow.id === pendingChange.rowId ? { ...currentRow, [pendingChange.field]: pendingChange.nextValue === 'Да' } : currentRow
      ))
      setTariffRows(nextRows)
      if (sourceRow) {
        await persistTariffRow(sourceRow, nextRows)
      }
    } else if (pendingChange.kind === 'tariff-date') {
      const sourceRow = tariffRows.find((currentRow) => currentRow.id === pendingChange.rowId)
      const nextRows = tariffRows.map((currentRow) => (
        currentRow.id === pendingChange.rowId ? { ...currentRow, dateDay: pendingChange.nextDay, dateMonth: pendingChange.nextMonth } : currentRow
      ))
      setTariffRows(nextRows)
      setTariffDrafts((drafts) => ({
        ...drafts,
        [pendingChange.rowId]: {
          ...drafts[pendingChange.rowId],
          dateDay: pendingChange.nextDay,
          dateMonth: pendingChange.nextMonth,
        },
      }))
      if (sourceRow?.backendServiceSettingId) {
        await persistTariffRow(sourceRow, nextRows)
      }
    } else if (pendingChange.kind === 'one-time-amount') {
      const sourceRow = oneTimeRows.find((currentRow) => currentRow.id === pendingChange.rowId)
      if (sourceRow) {
        await persistOneTimeRow(sourceRow, { amount: pendingChange.nextValue })
      }
    } else {
      const sourceRow = oneTimeRows.find((currentRow) => currentRow.id === pendingChange.rowId)
      if (sourceRow) {
        await persistOneTimeStatus(sourceRow, pendingChange.nextValue === 'Активен')
      }
    }

    setPendingChange(null)
  }

  useRestoreFocusOnClose(Boolean(pendingChange))
  const changeDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingChange))
  const changeCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingChange))
  useRestoreFocusOnClose(Boolean(oneTimeDeleteTarget))
  const oneTimeDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(oneTimeDeleteTarget))
  const oneTimeDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(oneTimeDeleteTarget))
  useRestoreFocusOnClose(Boolean(oneTimeRestoreTarget))
  const oneTimeRestoreDialogRef = useFocusTrap<HTMLElement>(Boolean(oneTimeRestoreTarget))
  const oneTimeRestoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(oneTimeRestoreTarget))
  useRestoreFocusOnClose(Boolean(feeCampaignArchiveTarget))
  const feeCampaignArchiveDialogRef = useFocusTrap<HTMLElement>(Boolean(feeCampaignArchiveTarget))
  const feeCampaignArchiveCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(feeCampaignArchiveTarget))
  useRestoreFocusOnClose(Boolean(feeCampaignRestoreTarget))
  const feeCampaignRestoreDialogRef = useFocusTrap<HTMLElement>(Boolean(feeCampaignRestoreTarget))
  const feeCampaignRestoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(feeCampaignRestoreTarget))
  useRestoreFocusOnClose(Boolean(chargeServiceArchiveTarget))
  const chargeServiceArchiveDialogRef = useFocusTrap<HTMLElement>(Boolean(chargeServiceArchiveTarget))
  const chargeServiceArchiveCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(chargeServiceArchiveTarget))
  useRestoreFocusOnClose(Boolean(chargeServiceRestoreTarget))
  const chargeServiceRestoreDialogRef = useFocusTrap<HTMLElement>(Boolean(chargeServiceRestoreTarget))
  const chargeServiceRestoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(chargeServiceRestoreTarget))
  useRestoreFocusOnClose(Boolean(thresholdDeleteTarget))
  const thresholdDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(thresholdDeleteTarget))
  const thresholdDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(thresholdDeleteTarget))
  useRestoreFocusOnClose(Boolean(feeCampaignGenerateTarget))
  const feeCampaignGenerateDialogRef = useFocusTrap<HTMLElement>(Boolean(feeCampaignGenerateTarget))
  const feeCampaignGenerateCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(feeCampaignGenerateTarget))
  useEscapeKey(Boolean(pendingChange), () => cancelPendingChange())
  useEscapeKey(Boolean(oneTimeDeleteTarget), () => closeOneTimeDeleteDialog())
  useEscapeKey(Boolean(oneTimeRestoreTarget), () => closeOneTimeRestoreDialog())
  useEscapeKey(Boolean(feeCampaignArchiveTarget), () => closeFeeCampaignArchiveDialog())
  useEscapeKey(Boolean(feeCampaignRestoreTarget), () => closeFeeCampaignRestoreDialog())
  useEscapeKey(Boolean(chargeServiceArchiveTarget), () => closeChargeServiceArchiveDialog())
  useEscapeKey(Boolean(chargeServiceRestoreTarget), () => closeChargeServiceRestoreDialog())
  useEscapeKey(Boolean(thresholdDeleteTarget), () => closeThresholdDeleteDialog())
  useEscapeKey(Boolean(feeCampaignGenerateTarget), () => closeFeeCampaignGenerateDialog())
  useEscapeKey(Boolean(oneTimeContextMenu), () => setOneTimeContextMenu(null))

  function buildChargeServiceRequest(setting: ChargeServiceSettingDto, nextRows: ContractorTariffRow[]): UpsertChargeServiceSettingRequest {
    const relatedRows = nextRows.filter((item) => item.backendServiceSettingId === setting.id)
    const mainRow = relatedRows.find((item) => item.serviceSettingKind === 'main') ?? relatedRows[0]
    const periodicityRow = relatedRows.find((item) => item.serviceSettingKind === 'periodicity')
    const startRow = relatedRows.find((item) => item.serviceSettingKind === 'start-date')
    const dueRow = relatedRows.find((item) => item.serviceSettingKind === 'due-date')
    const overdueRow = relatedRows.find((item) => item.serviceSettingKind === 'overdue-days')
    const isRegular = setting.isRegular || Boolean(startRow || dueRow || overdueRow)
    const dueDay = dueRow?.dateDay ? Number(dueRow.dateDay) : setting.paymentDueDay
    const dueMonth = dueRow?.dateMonth ? getContractorTariffMonthNumber(dueRow.dateMonth) : setting.paymentDueMonth
    const startMonth = startRow?.dateMonth ? getContractorTariffMonthNumber(startRow.dateMonth) : setting.accrualStartMonth
    const periodicityMonths = parsePrototypeAmount(periodicityRow?.amount ?? '') ?? setting.periodicityMonths ?? 12
    const overdueGraceDays = parsePrototypeAmount(overdueRow?.amount ?? '') ?? setting.overdueGraceDays
    const isMetered = mainRow?.byMeter ?? setting.isMetered
    const hasTieredTariff = isMetered ? (mainRow?.tiered ?? setting.hasTieredTariff) : false

    return {
      name: (mainRow?.title ?? setting.name).trim() || setting.name,
      isRegular,
      periodicityMonths: isRegular ? Math.trunc(periodicityMonths) : null,
      accrualStartMonth: isRegular ? startMonth ?? 1 : null,
      paymentDueDay: isRegular ? dueDay ?? 1 : null,
      paymentDueMonth: isRegular ? dueMonth ?? 1 : null,
      overdueGraceDays: Math.trunc(overdueGraceDays),
      isMetered,
      hasTieredTariff,
      unitName: (mainRow?.unit ?? setting.unitName ?? '').trim() || null,
      incomeTypeId: isRegular ? setting.incomeTypeId ?? null : null,
      tariffId: isRegular ? setting.tariffId ?? null : null,
    }
  }

  async function persistServiceSettingRow(row: ContractorTariffRow, nextRows: ContractorTariffRow[]) {
    if (!canManageTariffs || row.isDeleted || !row.backendServiceSettingId) {
      return
    }

    const serviceSetting = backendChargeServices.find((setting) => setting.id === row.backendServiceSettingId)
    if (!serviceSetting) {
      return
    }

    setTariffSavingRowId(row.id)
    setTariffPersistenceError(null)
    try {
      const request = buildChargeServiceRequest(serviceSetting, nextRows)
      const savedSetting = await dictionaryClient.updateChargeServiceSetting(auth.accessToken, serviceSetting.id, request)
      const nextSettings = backendChargeServices.map((setting) => (setting.id === savedSetting.id ? savedSetting : setting))
      const mergedRows = mergeChargeServicesIntoPrototypeRows(nextRows, nextSettings)
      setBackendChargeServices(nextSettings)
      setTariffRows(mergedRows)
      setTariffDrafts(createEditableDrafts(mergedRows))
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось сохранить настройку услуги.')
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function persistTariffRow(row: ContractorTariffRow, nextRows: ContractorTariffRow[]) {
    if (row.backendServiceSettingId) {
      await persistServiceSettingRow(row, nextRows)
      return
    }

    if (!canManageTariffs || !row.calculationBase) {
      return
    }

    const targetRow = nextRows.find((item) => item.id === row.id) ?? row
    const backendTariff = targetRow.backendTariffId
      ? backendTariffs.find((tariff) => tariff.id === targetRow.backendTariffId)
      : findTariffForPrototypeRow(backendTariffs, targetRow)
    const effectiveFrom = targetRow.effectiveFrom ?? backendTariff?.effectiveFrom ?? getLocalDateInputValue()
    const amount = parseTariffAmount(targetRow.amount ?? '')
    if (amount == null) {
      return
    }

    let request: UpsertTariffRequest
    if (targetRow.calculationBase === 'meter_electricity') {
      const electricityRows = nextRows.filter((item) => item.calculationBase === 'meter_electricity' && item.threshold)
      const firstRow = electricityRows[0] ?? targetRow
      const secondRow = electricityRows[1] ?? targetRow
      const thirdRow = electricityRows[2] ?? targetRow
      const firstRate = parseTariffAmount(firstRow.amount ?? '')
      const secondRate = parseTariffAmount(secondRow.amount ?? '')
      const thirdRate = parseTariffAmount(thirdRow.amount ?? '')
      const firstThreshold = targetRow.electricityFirstThreshold ?? backendTariff?.electricityFirstThreshold ?? 1
      const secondThreshold = targetRow.electricitySecondThreshold ?? backendTariff?.electricitySecondThreshold ?? 3
      request = {
        name: backendTariff?.name ?? 'Электроэнергия',
        calculationBase: 'meter_electricity',
        rate: firstRate ?? amount,
        effectiveFrom,
        comment: backendTariff?.comment ?? '',
      }
      if (firstRate != null && secondRate != null && thirdRate != null) {
        request = {
          ...request,
          electricityFirstThreshold: firstThreshold,
          electricitySecondThreshold: secondThreshold,
          electricityFirstTierName: firstRow.title,
          electricitySecondTierName: secondRow.title,
          electricityThirdTierName: thirdRow.title,
          electricityFirstRate: firstRate,
          electricitySecondRate: secondRate,
          electricityThirdRate: thirdRate,
        }
      }
    } else {
      request = {
        name: targetRow.title,
        calculationBase: targetRow.calculationBase ?? row.calculationBase,
        rate: amount,
        effectiveFrom,
        comment: backendTariff?.comment ?? '',
      }
    }

    setTariffSavingRowId(targetRow.id)
    setTariffPersistenceError(null)
    try {
      const savedTariff = backendTariff
        ? await dictionaryClient.updateTariff(auth.accessToken, backendTariff.id, request)
        : await dictionaryClient.createTariff(auth.accessToken, request)
      const nextTariffs = backendTariffs.some((tariff) => tariff.id === savedTariff.id)
        ? backendTariffs.map((tariff) => (tariff.id === savedTariff.id ? savedTariff : tariff))
        : [...backendTariffs, savedTariff]
      const mergedRows = mergeTariffsIntoPrototypeRows(nextRows, nextTariffs)
      setBackendTariffs(nextTariffs)
      setTariffRows(mergedRows)
      setTariffDrafts(createEditableDrafts(mergedRows))
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось сохранить тариф.')
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function persistOneTimeRow(row: ContractorOneTimeRow, overrides: Partial<Pick<ContractorOneTimeRow, 'amount' | 'isActive'>> = {}) {
    if (!canManageTariffs) {
      return null
    }

    const amountText = overrides.amount ?? row.amount
    const amount = parsePrototypeAmount(amountText)
    if (amount == null) {
      setOneTimeActionMessage(`Укажите корректную сумму для нерегулярного платежа "${row.name}".`)
      return null
    }

    const request: UpsertIrregularPaymentRequest = {
      name: row.name,
      amount,
      isActive: overrides.isActive ?? row.isActive,
    }

    setOneTimeSavingRowId(row.id)
    setTariffPersistenceError(null)
    setOneTimeActionMessage(null)
    try {
      const savedPayment = row.backendPaymentId
        ? await dictionaryClient.updateIrregularPayment(auth.accessToken, row.backendPaymentId, request)
        : await dictionaryClient.createIrregularPayment(auth.accessToken, request)
      const nextRows = oneTimeRows.map((currentRow) => (
        currentRow.id === row.id
          ? {
            ...currentRow,
            backendPaymentId: savedPayment.id,
            amount: formatPrototypeAmount(savedPayment.amount),
            isActive: savedPayment.isActive,
            isDeleted: savedPayment.isArchived,
            isUsed: savedPayment.isUsed,
          }
          : currentRow
      ))
      setOneTimeRows(nextRows)
      setOneTimeDrafts(createEditableDrafts(nextRows))
      return savedPayment
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить нерегулярный платеж.'
      setOneTimeActionMessage(message)
      return null
    } finally {
      setOneTimeSavingRowId(null)
    }
  }

  async function persistOneTimeStatus(row: ContractorOneTimeRow, isActive: boolean) {
    if (!canManageTariffs) {
      return null
    }

    if (!row.backendPaymentId) {
      return persistOneTimeRow(row, { amount: row.amount || '0', isActive })
    }

    setOneTimeSavingRowId(row.id)
    setTariffPersistenceError(null)
    setOneTimeActionMessage(null)
    try {
      const savedPayment = await dictionaryClient.setIrregularPaymentStatus(auth.accessToken, row.backendPaymentId, {
        isActive,
        reason: isActive ? 'Активация через меню нерегулярных платежей' : 'Деактивация через меню нерегулярных платежей',
      })
      const nextRows = oneTimeRows.map((currentRow) => (
        currentRow.id === row.id
          ? {
            ...currentRow,
            amount: formatPrototypeAmount(savedPayment.amount),
            isActive: savedPayment.isActive,
            isDeleted: savedPayment.isArchived,
            isUsed: savedPayment.isUsed,
          }
          : currentRow
      ))
      setOneTimeRows(nextRows)
      setOneTimeDrafts(createEditableDrafts(nextRows))
      return savedPayment
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось изменить статус нерегулярного платежа.'
      setOneTimeActionMessage(message)
      return null
    } finally {
      setOneTimeSavingRowId(null)
    }
  }

  const commitTariffTextChange = async (row: ContractorTariffRow, field: 'title' | 'amount' | 'unit') => {
    const nextValue = (tariffDrafts[row.id]?.[field] ?? '').trim()
    const previousValue = row[field] ?? ''

    if (nextValue.trim() === previousValue.trim()) {
      return
    }

    if (!previousValue.trim()) {
      const nextRows = tariffRows.map((currentRow) => (
        currentRow.id === row.id ? { ...currentRow, [field]: nextValue } : currentRow
      ))
      setTariffRows(nextRows)
      setTariffDrafts((drafts) => ({
        ...drafts,
        [row.id]: {
          ...drafts[row.id],
          [field]: nextValue,
        },
      }))
      if (row.backendServiceSettingId || field !== 'unit') {
        await persistTariffRow(row, nextRows)
      }
      return
    }

    setPendingChange({
      kind: 'tariff-text',
      rowId: row.id,
      field,
      objectName: `${row.category}: ${row.title}`,
      fieldLabel: getTariffTextFieldLabel(row, field),
      previousValue,
      nextValue,
    })
  }

  const commitTariffDateChange = async (row: ContractorTariffRow) => {
    const draft = tariffDrafts[row.id] ?? { title: row.title, amount: '', unit: '', dateDay: '', dateMonth: row.dateMonth ?? '' }
    const nextDay = (draft.dateDay ?? '').trim().padStart(2, '0')
    const nextMonth = draft.dateMonth || row.dateMonth || contractorTariffMonthOptions[0].value
    const dateError = getContractorTariffDateError(nextDay, nextMonth)

    if (dateError) {
      setTariffDateErrors((errors) => ({ ...errors, [row.id]: dateError }))
      return
    }

    setTariffDateErrors((errors) => {
      const nextErrors = { ...errors }
      delete nextErrors[row.id]
      return nextErrors
    })

    const previousValue = formatContractorTariffDate(row.dateDay ?? '', row.dateMonth ?? '')
    const nextValue = formatContractorTariffDate(nextDay, nextMonth)

    if (nextValue === previousValue) {
      setTariffDrafts((drafts) => ({
        ...drafts,
        [row.id]: {
          ...drafts[row.id],
          dateDay: nextDay,
          dateMonth: nextMonth,
        },
      }))
      return
    }

    if (!previousValue.trim()) {
      const nextRows = tariffRows.map((currentRow) => (
        currentRow.id === row.id ? { ...currentRow, dateDay: nextDay, dateMonth: nextMonth } : currentRow
      ))
      setTariffRows(nextRows)
      setTariffDrafts((drafts) => ({
        ...drafts,
        [row.id]: {
          ...drafts[row.id],
          dateDay: nextDay,
          dateMonth: nextMonth,
        },
      }))
      if (row.backendServiceSettingId) {
        await persistTariffRow(row, nextRows)
      }
      return
    }

    setPendingChange({
      kind: 'tariff-date',
      rowId: row.id,
      objectName: `${row.category}: ${row.title}`,
      fieldLabel: 'Значение',
      previousValue,
      nextValue,
      nextDay,
      nextMonth,
    })
  }

  const commitTariffBooleanChange = (row: ContractorTariffRow, field: 'tiered' | 'byMeter', nextValue: boolean) => {
    const previousValue = row[field]

    if (previousValue === nextValue) {
      return
    }

    setPendingChange({
      kind: 'tariff-boolean',
      rowId: row.id,
      field,
      objectName: `${row.category}: ${row.title}`,
      fieldLabel: field === 'tiered' ? 'Пороговая тарификация' : 'По счетчику',
      previousValue: previousValue ? 'Да' : 'Нет',
      nextValue: nextValue ? 'Да' : 'Нет',
    })
  }

  const commitOneTimeAmountChange = async (row: ContractorOneTimeRow) => {
    const nextValue = (oneTimeDrafts[row.id]?.amount ?? '').trim()

    if (nextValue.trim() === row.amount.trim()) {
      return
    }

    if (!row.amount.trim()) {
      const nextRows = oneTimeRows.map((currentRow) => (
        currentRow.id === row.id ? { ...currentRow, amount: nextValue } : currentRow
      ))
      setOneTimeRows(nextRows)
      setOneTimeDrafts((drafts) => ({
        ...drafts,
        [row.id]: {
          ...drafts[row.id],
          amount: nextValue,
        },
      }))
      await persistOneTimeRow(row, { amount: nextValue })
      return
    }

    setPendingChange({
      kind: 'one-time-amount',
      rowId: row.id,
      objectName: row.name,
      fieldLabel: 'Сумма, руб.',
      previousValue: row.amount,
      nextValue,
    })
  }

  const openOneTimeContextMenu = (event: MouseEvent<HTMLDivElement>, row: ContractorOneTimeRow) => {
    event.preventDefault()
    if (row.isDeleted) {
      return
    }

    setOneTimeActionMessage(null)
    setOneTimeContextMenu({ row, x: event.clientX, y: event.clientY })
  }

  const toggleOneTimeActive = (row: ContractorOneTimeRow) => {
    setOneTimeContextMenu(null)
    setPendingChange({
      kind: 'one-time-active',
      rowId: row.id,
      objectName: row.name,
      fieldLabel: 'Статус',
      previousValue: row.isActive ? 'Активен' : 'Деактивирован',
      nextValue: row.isActive ? 'Деактивирован' : 'Активен',
    })
  }

  const openOneTimeDeleteDialog = (row: ContractorOneTimeRow) => {
    if (row.isDeleted) {
      return
    }

    setOneTimeContextMenu(null)
    if (row.isUsed) {
      setOneTimeActionMessage(`Удаление недоступно: нерегулярный платеж "${row.name}" уже используется в платежах или начислениях.`)
      return
    }

    setOneTimeActionMessage(null)
    setOneTimeDeleteTarget(row)
    setOneTimeDeleteReason('')
  }

  const confirmOneTimeDelete = async () => {
    if (!oneTimeDeleteTarget || !oneTimeDeleteReason.trim()) {
      return
    }

    if (!oneTimeDeleteTarget.backendPaymentId) {
      setOneTimeRows((currentRows) => currentRows.filter((currentRow) => currentRow.id !== oneTimeDeleteTarget.id))
      closeOneTimeDeleteDialog()
      return
    }

    setOneTimeSavingRowId(oneTimeDeleteTarget.id)
    setOneTimeActionMessage(null)
    try {
      await dictionaryClient.archiveIrregularPayment(auth.accessToken, oneTimeDeleteTarget.backendPaymentId, oneTimeDeleteReason.trim())
      setOneTimeRows((currentRows) => currentRows.map((currentRow) => (
        currentRow.id === oneTimeDeleteTarget.id ? { ...currentRow, isDeleted: true } : currentRow
      )))
      closeOneTimeDeleteDialog()
    } catch (caught) {
      const message = caught instanceof DictionaryApiError && caught.code === 'irregular_payment_used'
        ? 'Удаление недоступно: нерегулярный платеж уже используется в платежах или начислениях.'
        : caught instanceof Error ? caught.message : 'Не удалось удалить нерегулярный платеж.'
      setOneTimeActionMessage(message)
    } finally {
      setOneTimeSavingRowId(null)
    }
  }

  const confirmOneTimeRestore = async () => {
    if (!oneTimeRestoreTarget?.backendPaymentId) {
      closeOneTimeRestoreDialog()
      return
    }

    setOneTimeSavingRowId(oneTimeRestoreTarget.id)
    setOneTimeActionMessage(null)
    try {
      const restoredPayment = await dictionaryClient.restoreIrregularPayment(auth.accessToken, oneTimeRestoreTarget.backendPaymentId)
      const nextRows = oneTimeRows.map((currentRow) => (
        currentRow.id === oneTimeRestoreTarget.id
          ? {
            ...currentRow,
            backendPaymentId: restoredPayment.id,
            amount: formatPrototypeAmount(restoredPayment.amount),
            isActive: restoredPayment.isActive,
            isDeleted: restoredPayment.isArchived,
            isUsed: restoredPayment.isUsed,
          }
          : currentRow
      ))
      setOneTimeRows(nextRows)
      setOneTimeDrafts(createEditableDrafts(nextRows))
      setOneTimeActionMessage(`Нерегулярный платеж "${restoredPayment.name}" возвращен.`)
      closeOneTimeRestoreDialog()
    } catch (caught) {
      const message = caught instanceof DictionaryApiError && caught.code === 'irregular_payment_duplicate'
        ? 'Восстановление недоступно: активный нерегулярный платеж с таким наименованием уже существует.'
        : caught instanceof Error ? caught.message : 'Не удалось восстановить нерегулярный платеж.'
      setOneTimeActionMessage(message)
    } finally {
      setOneTimeSavingRowId(null)
    }
  }

  async function createServiceSetting(request: UpsertChargeServiceSettingRequest) {
    if (!canManageTariffs) {
      return
    }

    setTariffSavingRowId('new-service')
    setTariffPersistenceError(null)
    try {
      const savedSetting = await dictionaryClient.createChargeServiceSetting(auth.accessToken, request)
      const nextSettings = [...backendChargeServices.filter((setting) => setting.id !== savedSetting.id), savedSetting]
      const nextRows = mergeChargeServicesIntoPrototypeRows(tariffRows, nextSettings)
      setBackendChargeServices(nextSettings)
      setTariffRows(nextRows)
      setTariffDrafts(createEditableDrafts(nextRows))
      setModal(null)
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось добавить услугу.')
      throw caught
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function createFeeCampaign(request: UpsertFeeCampaignRequest) {
    if (!canManageTariffs) {
      return
    }

    setFeeCampaignSavingId('new-fee-campaign')
    setTariffPersistenceError(null)
    setFeeCampaignActionMessage(null)
    try {
      const savedCampaign = await dictionaryClient.createFeeCampaign(auth.accessToken, request)
      setFeeCampaigns((currentCampaigns) => [savedCampaign, ...currentCampaigns.filter((campaign) => campaign.id !== savedCampaign.id)])
      setModal(null)
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось объявить сбор.')
      throw caught
    } finally {
      setFeeCampaignSavingId(null)
    }
  }

  async function archiveChargeServiceSetting() {
    if (!chargeServiceArchiveTarget || !chargeServiceArchiveReason.trim()) {
      return
    }

    setTariffSavingRowId(`charge-service-${chargeServiceArchiveTarget.id}`)
    setTariffPersistenceError(null)
    try {
      await dictionaryClient.archiveChargeServiceSetting(auth.accessToken, chargeServiceArchiveTarget.id, chargeServiceArchiveReason.trim())
      const nextSettings = backendChargeServices.map((setting) => (
        setting.id === chargeServiceArchiveTarget.id ? { ...setting, isArchived: true } : setting
      ))
      const nextRows = mergeChargeServicesIntoPrototypeRows(tariffRows, nextSettings)
      setBackendChargeServices(nextSettings)
      setTariffRows(nextRows)
      setTariffDrafts(createEditableDrafts(nextRows))
      closeChargeServiceArchiveDialog()
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось архивировать услугу.')
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function restoreChargeServiceSetting() {
    if (!chargeServiceRestoreTarget) {
      return
    }

    setTariffSavingRowId(`charge-service-${chargeServiceRestoreTarget.id}`)
    setTariffPersistenceError(null)
    try {
      const restoredSetting = await dictionaryClient.restoreChargeServiceSetting(auth.accessToken, chargeServiceRestoreTarget.id)
      const nextSettings = backendChargeServices.map((setting) => (
        setting.id === restoredSetting.id ? restoredSetting : setting
      ))
      const nextRows = mergeChargeServicesIntoPrototypeRows(tariffRows, nextSettings)
      setBackendChargeServices(nextSettings)
      setTariffRows(nextRows)
      setTariffDrafts(createEditableDrafts(nextRows))
      closeChargeServiceRestoreDialog()
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось восстановить услугу.')
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function updateFeeCampaign(request: UpsertFeeCampaignRequest) {
    if (!canManageTariffs || !feeCampaignEditTarget) {
      return
    }

    setFeeCampaignSavingId(feeCampaignEditTarget.id)
    setTariffPersistenceError(null)
    setFeeCampaignActionMessage(null)
    try {
      const savedCampaign = await dictionaryClient.updateFeeCampaign(auth.accessToken, feeCampaignEditTarget.id, request)
      setFeeCampaigns((currentCampaigns) => currentCampaigns.map((campaign) => (
        campaign.id === savedCampaign.id ? savedCampaign : campaign
      )))
      closeFeeCampaignEditDialog()
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось изменить сбор.')
      throw caught
    } finally {
      setFeeCampaignSavingId(null)
    }
  }

  async function archiveFeeCampaign() {
    if (!feeCampaignArchiveTarget || !feeCampaignArchiveReason.trim()) {
      return
    }

    setFeeCampaignSavingId(feeCampaignArchiveTarget.id)
    setFeeCampaignActionMessage(null)
    try {
      await dictionaryClient.archiveFeeCampaign(auth.accessToken, feeCampaignArchiveTarget.id, feeCampaignArchiveReason.trim())
      setFeeCampaigns((currentCampaigns) => currentCampaigns.map((campaign) => (
        campaign.id === feeCampaignArchiveTarget.id ? { ...campaign, isArchived: true } : campaign
      )))
      closeFeeCampaignArchiveDialog()
    } catch (caught) {
      setFeeCampaignActionMessage(caught instanceof Error ? caught.message : 'Не удалось архивировать сбор.')
    } finally {
      setFeeCampaignSavingId(null)
    }
  }

  async function restoreFeeCampaign() {
    if (!feeCampaignRestoreTarget) {
      return
    }

    setFeeCampaignSavingId(feeCampaignRestoreTarget.id)
    setFeeCampaignActionMessage(null)
    try {
      const restoredCampaign = await dictionaryClient.restoreFeeCampaign(auth.accessToken, feeCampaignRestoreTarget.id)
      setFeeCampaigns((currentCampaigns) => currentCampaigns.map((currentCampaign) => (
        currentCampaign.id === restoredCampaign.id ? restoredCampaign : currentCampaign
      )))
      closeFeeCampaignRestoreDialog()
    } catch (caught) {
      setFeeCampaignActionMessage(caught instanceof Error ? caught.message : 'Не удалось восстановить сбор.')
    } finally {
      setFeeCampaignSavingId(null)
    }
  }

  function confirmThresholdDelete() {
    if (!thresholdDeleteTarget || !thresholdDeleteReason.trim()) {
      return
    }

    const nextRows = tariffRows.filter((row) => row.id !== thresholdDeleteTarget.id)
    setTariffRows(nextRows)
    setTariffDrafts((drafts) => {
      const nextDrafts = { ...drafts }
      delete nextDrafts[thresholdDeleteTarget.id]
      return nextDrafts
    })
    closeThresholdDeleteDialog()
  }

  async function generateFeeCampaignAccruals() {
    if (!feeCampaignGenerateTarget) {
      return
    }

    setFeeCampaignSavingId(feeCampaignGenerateTarget.id)
    setFeeCampaignActionMessage(null)
    try {
      const result = await financeClient.generateFeeCampaignAccruals(auth.accessToken, {
        feeCampaignId: feeCampaignGenerateTarget.id,
        accountingMonth: `${feeCampaignGenerateMonth}-01`,
        comment: feeCampaignGenerateComment.trim() || undefined,
      })
      setFeeCampaignActionMessage(`Создано начислений: ${result.createdCount}; сумма: ${formatMoney(result.totalAmount)} руб.; пропущено: ${result.skippedCount}.`)
      closeFeeCampaignGenerateDialog()
    } catch (caught) {
      setFeeCampaignActionMessage(caught instanceof Error ? caught.message : 'Не удалось начислить сбор.')
    } finally {
      setFeeCampaignSavingId(null)
    }
  }

  const addElectricityThreshold = () => {
    const electricityThresholdRows = tariffRows.filter((row) => row.category === 'Электроэнергия' && row.threshold)
    const nextIndex = electricityThresholdRows.length + 1
    const nextRow: ContractorTariffRow = {
      id: `electricity-tier-custom-${Date.now()}`,
      category: 'Электроэнергия',
      title: `Порог ${nextIndex}`,
      threshold: 'x',
      amount: '',
      unit: 'руб.',
      byMeter: true,
      tiered: true,
    }

    setTariffRows((currentRows) => {
      const overdueIndex = currentRows.findIndex((row) => row.id === 'electricity-overdue-days')
      if (overdueIndex < 0) {
        return [...currentRows, nextRow]
      }

      return [
        ...currentRows.slice(0, overdueIndex),
        nextRow,
        ...currentRows.slice(overdueIndex),
      ]
    })
    setTariffDrafts((drafts) => ({ ...drafts, [nextRow.id]: { title: nextRow.title, amount: '', unit: nextRow.unit ?? '', dateDay: '', dateMonth: '' } }))
  }

  const lastElectricityThresholdRowId = [...tariffRows]
    .reverse()
    .find((row) => row.category === 'Электроэнергия' && row.threshold)?.id

  function formatFeeCampaignParticipantSummary(campaign: FeeCampaignDto) {
    if (campaign.appliesToAllGarages) {
      return 'Все гаражи'
    }

    const selectedNumbers = campaign.participantGarageIds
      .map((garageId) => feeCampaignGarageOptions.find((garage) => garage.id === garageId)?.number)
      .filter((number): number is string => Boolean(number))
      .sort((left, right) => left.localeCompare(right, 'ru', { numeric: true }))

    if (selectedNumbers.length === 0) {
      return `${campaign.participantGarageIds.length} выбрано`
    }

    const visibleNumbers = selectedNumbers.slice(0, 4).join(', ')
    return selectedNumbers.length > 4 ? `${visibleNumbers} и еще ${selectedNumbers.length - 4}` : visibleNumbers
  }

  return (
    <section className="contractors-page" aria-label="Тарифы и сборы">
      <div className="contractors-heading">
        <div>
          <h1>Тарифы и сборы</h1>
          {tariffsLoading ? <p className="form-hint" role="status">Загружаем тарифы...</p> : null}
          {!canManageTariffs ? <p className="form-hint">Режим просмотра: для изменения тарифов нужно право tariffs.manage.</p> : null}
          {tariffPersistenceError ? <FormError>{tariffPersistenceError}</FormError> : null}
        </div>
        <div className="contractors-actions">
          <button className="secondary-button" type="button" onClick={() => setModal('service')}>
            <Plus size={17} />
            <span>Добавить услугу</span>
          </button>
          <button className="primary-button contractors-primary-action" type="button" onClick={() => setModal('fee')}>
            <Plus size={17} />
            <span>Объявить сбор</span>
          </button>
        </div>
      </div>

      <>
        <div className="contractors-sheet" role="table" aria-label="Тарифы и сборы">
            <div className="contractors-sheet-header" role="row">
              <span role="columnheader">Основание</span>
              <span role="columnheader">Значение</span>
              <span role="columnheader">Ед.</span>
              <span role="columnheader">Пороговая тарификация</span>
              <span role="columnheader">По счетчику</span>
            </div>
            {tariffRows.map((row) => {
              const serviceSetting = row.backendServiceSettingId
                ? backendChargeServices.find((setting) => setting.id === row.backendServiceSettingId) ?? null
                : null
              const isServiceSaving = Boolean(serviceSetting && tariffSavingRowId === `charge-service-${serviceSetting.id}`)
              const isRowDisabled = row.isDeleted || tariffSavingRowId === row.id || isServiceSaving
              const isCustomThreshold = Boolean(row.threshold && row.id.startsWith('electricity-tier-custom-'))

              return (
                <Fragment key={row.id}>
                <div
                  className={[
                    row.group ? 'contractors-sheet-row contractors-sheet-row--group' : 'contractors-sheet-row',
                    row.isDeleted ? 'contractors-sheet-row--deleted' : '',
                  ].filter(Boolean).join(' ')}
                  role="row"
                >
                  <span role="cell">
                    {row.group ? <strong>{row.group}</strong> : null}
                    {row.threshold ? (
                      <input
                        aria-label={`${row.category}: ${row.title}: наименование`}
                        className="contractors-editable-input contractors-editable-input--title"
                        disabled={!canManageTariffs || isRowDisabled}
                        value={tariffDrafts[row.id]?.title ?? row.title}
                        onChange={(event) => setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], title: event.target.value } }))}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffTextChange(row, 'title'))}
                      />
                    ) : (
                      <span>{row.title}</span>
                    )}
                    {row.serviceSettingKind === 'main' && serviceSetting ? (
                      <span className="contractors-sheet-row-actions">
                        {row.isDeleted ? (
                          <button
                            className="ghost-button"
                            type="button"
                            aria-label={`Вернуть услугу ${serviceSetting.name}`}
                            disabled={!canManageTariffs || isServiceSaving}
                            onClick={() => setChargeServiceRestoreTarget(serviceSetting)}
                          >
                            <RotateCcw size={15} />
                            <span>Вернуть</span>
                          </button>
                        ) : (
                          <button
                            className="icon-button danger-button"
                            type="button"
                            aria-label={`Архивировать услугу ${serviceSetting.name}`}
                            disabled={!canManageTariffs || isServiceSaving}
                            onClick={() => {
                              setChargeServiceArchiveTarget(serviceSetting)
                              setChargeServiceArchiveReason('')
                            }}
                          >
                            <Trash2 size={16} />
                          </button>
                        )}
                      </span>
                    ) : null}
                    {isCustomThreshold ? (
                      <span className="contractors-sheet-row-actions">
                        <button
                          className="icon-button danger-button"
                          type="button"
                          aria-label={`Удалить порог ${row.title}`}
                          disabled={!canManageTariffs || isRowDisabled}
                          onClick={() => {
                            setThresholdDeleteTarget(row)
                            setThresholdDeleteReason('')
                          }}
                        >
                          <Trash2 size={16} />
                        </button>
                      </span>
                    ) : null}
                  </span>
                  <span role="cell" className="contractors-value-cell">
                    {row.threshold ? <em>{row.threshold}</em> : null}
                    {row.dateDay !== undefined ? (
                      <div className="contractors-date-field">
                        <input
                          aria-label={`${row.category}: ${row.title}: день`}
                          aria-invalid={Boolean(tariffDateErrors[row.id])}
                          aria-describedby={tariffDateErrors[row.id] ? `${row.id}-date-error` : undefined}
                          className="contractors-editable-input contractors-editable-input--day"
                          disabled={!canManageTariffs || isRowDisabled}
                          inputMode="numeric"
                          maxLength={2}
                          value={tariffDrafts[row.id]?.dateDay ?? ''}
                          onChange={(event) => {
                            setTariffDateErrors((errors) => {
                              const nextErrors = { ...errors }
                              delete nextErrors[row.id]
                              return nextErrors
                            })
                            setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], dateDay: event.target.value } }))
                          }}
                          onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffDateChange(row))}
                        />
                        {tariffDateErrors[row.id] ? <span id={`${row.id}-date-error`} className="contractors-field-error" role="alert">{tariffDateErrors[row.id]}</span> : null}
                      </div>
                    ) : (
                      <input
                        aria-label={`${row.category}: ${row.title}: значение`}
                        className="contractors-editable-input"
                        disabled={!canManageTariffs || isRowDisabled}
                        value={tariffDrafts[row.id]?.amount ?? ''}
                        onChange={(event) => setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], amount: event.target.value } }))}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffTextChange(row, 'amount'))}
                      />
                    )}
                  </span>
                  <span role="cell">
                    {row.dateDay !== undefined ? (
                      <select
                        aria-label={`${row.category}: ${row.title}: месяц`}
                        className="contractors-editable-select contractors-editable-select--month"
                        disabled={!canManageTariffs || isRowDisabled}
                        value={tariffDrafts[row.id]?.dateMonth ?? row.dateMonth ?? contractorTariffMonthOptions[0].value}
                        onChange={(event) => {
                          setTariffDateErrors((errors) => {
                            const nextErrors = { ...errors }
                            delete nextErrors[row.id]
                            return nextErrors
                          })
                          setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], dateMonth: event.target.value } }))
                        }}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffDateChange(row))}
                      >
                        {contractorTariffMonthOptions.map((month) => (
                          <option key={month.value} value={month.value}>{month.label}</option>
                        ))}
                      </select>
                    ) : (
                      <input
                        aria-label={`${row.category}: ${row.title}: единица`}
                        className="contractors-editable-input contractors-editable-input--unit"
                        disabled={!canManageTariffs || isRowDisabled}
                        value={tariffDrafts[row.id]?.unit ?? ''}
                        onChange={(event) => setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], unit: event.target.value } }))}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffTextChange(row, 'unit'))}
                      />
                    )}
                  </span>
                  <span role="cell">
                    <select
                      aria-label={`${row.category}: ${row.title}: пороговая тарификация`}
                      className="contractors-editable-select"
                      disabled={!canManageTariffs || isRowDisabled}
                      value={row.tiered ? 'Да' : 'Нет'}
                    onChange={(event) => commitTariffBooleanChange(row, 'tiered', event.target.value === 'Да')}
                    >
                      <option>Да</option>
                      <option>Нет</option>
                    </select>
                  </span>
                  <span role="cell">
                    <select
                      aria-label={`${row.category}: ${row.title}: по счетчику`}
                      className="contractors-editable-select"
                      disabled={!canManageTariffs || isRowDisabled}
                      value={row.byMeter ? 'Да' : 'Нет'}
                    onChange={(event) => commitTariffBooleanChange(row, 'byMeter', event.target.value === 'Да')}
                    >
                      <option>Да</option>
                      <option>Нет</option>
                    </select>
                  </span>
                </div>
                {row.id === lastElectricityThresholdRowId ? (
                  <div className="contractors-sheet-row contractors-sheet-action-row" role="row">
                    <span role="cell">
                      <button className="link-button" type="button" onClick={addElectricityThreshold} disabled={!canManageTariffs}>
                        Добавить порог
                      </button>
                    </span>
                    <span role="cell" />
                    <span role="cell" />
                    <span role="cell" />
                    <span role="cell" />
                  </div>
                ) : null}
              </Fragment>
              )
            })}
            {tariffRows.length === 0 && !tariffsLoading ? (
              <div className="contractors-sheet-row contractors-sheet-action-row" role="row">
                <span role="cell">Тарифы и услуги пока не настроены.</span>
                <span role="cell" />
                <span role="cell" />
                <span role="cell" />
                <span role="cell" />
              </div>
            ) : null}
          </div>

          <div className="contractors-bottom-grid">
            <section className="contractors-mini-table" aria-label="Нерегулярные платежи">
              <div className="contractors-mini-title">Нерегулярные платежи</div>
              {oneTimeActionMessage ? <p className="contractors-action-message" role="alert">{oneTimeActionMessage}</p> : null}
              <div className="contractors-mini-header contractors-mini-header--editable">
                <span>Основание</span>
                <span>Сумма, руб.</span>
              </div>
              {oneTimeRows.map((row) => (
                <div
                  aria-label={`Нерегулярный платеж ${row.name}`}
                  className={[
                    'contractors-mini-row contractors-mini-row--editable',
                    row.isDeleted ? 'contractors-mini-row--deleted' : '',
                    !row.isActive ? 'contractors-mini-row--inactive' : '',
                  ].filter(Boolean).join(' ')}
                  key={row.id}
                  onContextMenu={(event) => openOneTimeContextMenu(event, row)}
                >
                  <span>{row.name}</span>
                  <span>
                    {row.isDeleted ? (
                      <span className="contractors-mini-actions">
                        <span>{row.amount}</span>
                        <button className="ghost-button" type="button" disabled={!canManageTariffs || oneTimeSavingRowId === row.id} onClick={() => setOneTimeRestoreTarget(row)}>
                          <RotateCcw size={16} />
                          <span>Вернуть</span>
                        </button>
                      </span>
                    ) : (
                      <input
                        aria-label={`Сумма: ${row.name}`}
                        className="contractors-editable-input"
                        disabled={!canManageTariffs || !row.isActive || oneTimeSavingRowId === row.id}
                        value={oneTimeDrafts[row.id]?.amount ?? ''}
                        onChange={(event) => setOneTimeDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], amount: event.target.value } }))}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitOneTimeAmountChange(row))}
                      />
                    )}
                  </span>
                </div>
              ))}
              {oneTimeRows.length === 0 && !tariffsLoading ? <p className="form-hint">Нерегулярные платежи пока не настроены.</p> : null}
            </section>

            <section className="contractors-mini-table" aria-label="Объявленные сборы">
              <div className="contractors-mini-title">Объявленные сборы</div>
              {feeCampaignActionMessage ? <p className="contractors-action-message" role="alert">{feeCampaignActionMessage}</p> : null}
              <div className="contractors-mini-header contractors-mini-header--fees">
                <span>Наименование</span>
                <span>Взнос</span>
                <span>План</span>
                <span>Участники</span>
                <span>Период</span>
                <span>Действия</span>
              </div>
              {feeCampaigns.map((campaign) => (
                <div
                  aria-label={`Объявленный сбор ${campaign.name}`}
                  className={[
                    'contractors-mini-row contractors-mini-row--fees',
                    campaign.isArchived ? 'contractors-mini-row--deleted' : '',
                  ].filter(Boolean).join(' ')}
                  key={campaign.id}
                >
                  <span>
                    <strong>{campaign.name}</strong>
                    <small>{campaign.incomeTypeName}{campaign.goal ? ` · ${campaign.goal}` : ''}</small>
                  </span>
                  <span>{formatMoney(campaign.contributionAmount)}</span>
                  <span>{formatMoney(campaign.targetAmount)}</span>
                  <span>{formatFeeCampaignParticipantSummary(campaign)}</span>
                  <span>{formatDateOnly(campaign.startsOn)}{campaign.endsOn ? ` - ${formatDateOnly(campaign.endsOn)}` : ''}</span>
                  <span className="contractors-mini-actions">
                    {campaign.isArchived ? (
                      <button className="ghost-button" type="button" disabled={!canManageTariffs || feeCampaignSavingId === campaign.id} onClick={() => setFeeCampaignRestoreTarget(campaign)}>
                        <RotateCcw size={16} />
                        <span>Вернуть</span>
                      </button>
                    ) : (
                      <>
                        <button className="ghost-button" type="button" disabled={!canManageTariffs || feeCampaignSavingId === campaign.id} onClick={() => {
                          setFeeCampaignGenerateTarget(campaign)
                          setFeeCampaignGenerateMonth(getCurrentMonthInputValue())
                          setFeeCampaignGenerateComment('')
                        }}>
                          <Plus size={16} />
                          <span>Начислить</span>
                        </button>
                        <button className="icon-button" type="button" aria-label={`Изменить сбор ${campaign.name}`} disabled={!canManageTariffs || feeCampaignSavingId === campaign.id} onClick={() => setFeeCampaignEditTarget(campaign)}>
                          <Pencil size={16} />
                        </button>
                        <button className="icon-button" type="button" aria-label={`Архивировать сбор ${campaign.name}`} disabled={!canManageTariffs || feeCampaignSavingId === campaign.id} onClick={() => {
                          setFeeCampaignArchiveTarget(campaign)
                          setFeeCampaignArchiveReason('')
                        }}>
                          <Trash2 size={16} />
                        </button>
                      </>
                    )}
                  </span>
                </div>
              ))}
              {feeCampaigns.length === 0 && !tariffsLoading ? <p className="form-hint">Объявленные сборы пока не настроены.</p> : null}
            </section>
          </div>
      </>

      {oneTimeContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setOneTimeContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия нерегулярного платежа ${oneTimeContextMenu.row.name}`}
            style={{ left: oneTimeContextMenu.x, top: oneTimeContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            <button type="button" role="menuitem" onClick={() => toggleOneTimeActive(oneTimeContextMenu.row)}>
              {oneTimeContextMenu.row.isActive ? 'Деактивировать' : 'Активировать'}
            </button>
            <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openOneTimeDeleteDialog(oneTimeContextMenu.row)}>
              <span>Удалить</span>
            </button>
          </div>
        </div>
      ) : null}

      {pendingChange ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={cancelPendingChange}>
          <section ref={changeDialogRef} className="detail-dialog contractors-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="tariff-prototype-change-title" aria-describedby="tariff-prototype-change-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Изменение</p>
                <h3 id="tariff-prototype-change-title">Подтвердить изменение?</h3>
                <p>{pendingChange.objectName}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение изменения тарифа" onClick={cancelPendingChange}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="tariff-prototype-change-description">Проверьте, что именно изменится. Действие записывается в историю изменений.</p>
            <div className="tariff-change-summary" aria-label="Изменяемое поле тарифа">
              <div className="tariff-change-field-row">
                <span>Поле</span>
                <strong>{pendingChange.fieldLabel}</strong>
              </div>
              <div className="tariff-change-values-row">
                <div>
                  <span>Было</span>
                  <strong>{formatPrototypeChangeValue(pendingChange.previousValue)}</strong>
                </div>
                <div>
                  <span>Стало</span>
                  <strong>{formatPrototypeChangeValue(pendingChange.nextValue)}</strong>
                </div>
              </div>
            </div>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={changeCancelRef} className="ghost-button" type="button" onClick={cancelPendingChange}>Отмена</button>
              <button className="secondary-button" type="button" onClick={confirmPendingChange} disabled={oneTimeSavingRowId === pendingChange.rowId || tariffSavingRowId === pendingChange.rowId}>
                <Save size={16} />
                <span>Сохранить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {thresholdDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeThresholdDeleteDialog}>
          <section ref={thresholdDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="threshold-delete-title" aria-describedby="threshold-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="threshold-delete-title">Удалить порог тарификации?</h3>
                <p>{thresholdDeleteTarget.title}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления порога" onClick={closeThresholdDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="threshold-delete-description">Порог будет удален из текущей настройки тарифов. Укажите причину, чтобы действие было понятным при проверке изменений.</p>
            <label className="field-label" htmlFor="threshold-delete-reason">Причина удаления</label>
            <textarea
              id="threshold-delete-reason"
              aria-label="Причина удаления порога"
              maxLength={1000}
              value={thresholdDeleteReason}
              onChange={(event) => setThresholdDeleteReason(event.target.value)}
              placeholder="Например: лишний порог добавлен ошибочно"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={thresholdDeleteCancelRef} className="ghost-button" type="button" onClick={closeThresholdDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={confirmThresholdDelete} disabled={!thresholdDeleteReason.trim()}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {oneTimeDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeOneTimeDeleteDialog}>
          <section ref={oneTimeDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="one-time-delete-title" aria-describedby="one-time-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="one-time-delete-title">Удалить нерегулярный платеж?</h3>
                <p>{oneTimeDeleteTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления нерегулярного платежа" onClick={closeOneTimeDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="one-time-delete-description">Платеж будет удален из списка нерегулярных платежей. Укажите причину, чтобы действие можно было проверить позже.</p>
            <label className="field-label" htmlFor="one-time-delete-reason">Причина удаления</label>
            <textarea
              id="one-time-delete-reason"
              aria-label="Причина удаления нерегулярного платежа"
              maxLength={1000}
              value={oneTimeDeleteReason}
              onChange={(event) => setOneTimeDeleteReason(event.target.value)}
              placeholder="Например: платеж больше не используется"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={oneTimeDeleteCancelRef} className="ghost-button" type="button" onClick={closeOneTimeDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={confirmOneTimeDelete} disabled={!oneTimeDeleteReason.trim() || oneTimeSavingRowId === oneTimeDeleteTarget.id}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {oneTimeRestoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeOneTimeRestoreDialog}>
          <section ref={oneTimeRestoreDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="one-time-restore-title" aria-describedby="one-time-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Восстановление</p>
                <h3 id="one-time-restore-title">Вернуть нерегулярный платеж?</h3>
                <p>{oneTimeRestoreTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение восстановления нерегулярного платежа" onClick={closeOneTimeRestoreDialog} disabled={oneTimeSavingRowId === oneTimeRestoreTarget.id}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="one-time-restore-description">Платеж снова появится в рабочих списках. Действие будет записано в историю изменений.</p>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={oneTimeRestoreCancelRef} className="ghost-button" type="button" onClick={closeOneTimeRestoreDialog} disabled={oneTimeSavingRowId === oneTimeRestoreTarget.id}>Отмена</button>
              <button className="secondary-button" type="button" onClick={confirmOneTimeRestore} disabled={oneTimeSavingRowId === oneTimeRestoreTarget.id}>
                <RotateCcw size={16} />
                <span>Вернуть</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {chargeServiceArchiveTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeChargeServiceArchiveDialog}>
          <section ref={chargeServiceArchiveDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="charge-service-archive-title" aria-describedby="charge-service-archive-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Архив</p>
                <h3 id="charge-service-archive-title">Архивировать услугу?</h3>
                <p>{chargeServiceArchiveTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение архивации услуги" onClick={closeChargeServiceArchiveDialog} disabled={tariffSavingRowId === `charge-service-${chargeServiceArchiveTarget.id}`}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="charge-service-archive-description">Услуга останется в справочнике как архивная и будет заблокирована для редактирования. Укажите причину для истории изменений.</p>
            <label className="field-label" htmlFor="charge-service-archive-reason">Причина архивации</label>
            <textarea
              id="charge-service-archive-reason"
              aria-label="Причина архивации услуги"
              maxLength={1000}
              value={chargeServiceArchiveReason}
              onChange={(event) => setChargeServiceArchiveReason(event.target.value)}
              placeholder="Например: услуга больше не используется"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={chargeServiceArchiveCancelRef} className="ghost-button" type="button" onClick={closeChargeServiceArchiveDialog} disabled={tariffSavingRowId === `charge-service-${chargeServiceArchiveTarget.id}`}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={archiveChargeServiceSetting} disabled={!chargeServiceArchiveReason.trim() || tariffSavingRowId === `charge-service-${chargeServiceArchiveTarget.id}`}>
                <Trash2 size={16} />
                <span>Архивировать</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {chargeServiceRestoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeChargeServiceRestoreDialog}>
          <section ref={chargeServiceRestoreDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="charge-service-restore-title" aria-describedby="charge-service-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Восстановление</p>
                <h3 id="charge-service-restore-title">Вернуть услугу?</h3>
                <p>{chargeServiceRestoreTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение восстановления услуги" onClick={closeChargeServiceRestoreDialog} disabled={tariffSavingRowId === `charge-service-${chargeServiceRestoreTarget.id}`}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="charge-service-restore-description">Услуга снова станет активной и доступной для редактирования в тарифах. Действие будет записано в историю изменений.</p>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={chargeServiceRestoreCancelRef} className="ghost-button" type="button" onClick={closeChargeServiceRestoreDialog} disabled={tariffSavingRowId === `charge-service-${chargeServiceRestoreTarget.id}`}>Отмена</button>
              <button className="secondary-button" type="button" onClick={restoreChargeServiceSetting} disabled={tariffSavingRowId === `charge-service-${chargeServiceRestoreTarget.id}`}>
                <RotateCcw size={16} />
                <span>Вернуть</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {feeCampaignArchiveTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeFeeCampaignArchiveDialog}>
          <section ref={feeCampaignArchiveDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="fee-campaign-archive-title" aria-describedby="fee-campaign-archive-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Архив</p>
                <h3 id="fee-campaign-archive-title">Архивировать сбор?</h3>
                <p>{feeCampaignArchiveTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение архивации сбора" onClick={closeFeeCampaignArchiveDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fee-campaign-archive-description">Сбор будет скрыт из активного списка, но его можно будет вернуть. Укажите причину для истории изменений.</p>
            <label className="field-label" htmlFor="fee-campaign-archive-reason">Причина архивации</label>
            <textarea
              id="fee-campaign-archive-reason"
              aria-label="Причина архивации сбора"
              maxLength={1000}
              value={feeCampaignArchiveReason}
              onChange={(event) => setFeeCampaignArchiveReason(event.target.value)}
              placeholder="Например: сбор больше не используется"
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={feeCampaignArchiveCancelRef} className="ghost-button" type="button" onClick={closeFeeCampaignArchiveDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={archiveFeeCampaign} disabled={!feeCampaignArchiveReason.trim() || feeCampaignSavingId === feeCampaignArchiveTarget.id}>
                <Trash2 size={16} />
                <span>Архивировать</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {feeCampaignRestoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeFeeCampaignRestoreDialog}>
          <section ref={feeCampaignRestoreDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="fee-campaign-restore-title" aria-describedby="fee-campaign-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Восстановление</p>
                <h3 id="fee-campaign-restore-title">Вернуть сбор?</h3>
                <p>{feeCampaignRestoreTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение восстановления сбора" onClick={closeFeeCampaignRestoreDialog} disabled={feeCampaignSavingId === feeCampaignRestoreTarget.id}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fee-campaign-restore-description">Сбор снова появится как активный и будет доступен для начислений. Действие будет записано в историю изменений.</p>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={feeCampaignRestoreCancelRef} className="ghost-button" type="button" onClick={closeFeeCampaignRestoreDialog} disabled={feeCampaignSavingId === feeCampaignRestoreTarget.id}>Отмена</button>
              <button className="secondary-button" type="button" onClick={restoreFeeCampaign} disabled={feeCampaignSavingId === feeCampaignRestoreTarget.id}>
                <RotateCcw size={16} />
                <span>Вернуть</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {feeCampaignGenerateTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeFeeCampaignGenerateDialog}>
          <section ref={feeCampaignGenerateDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="fee-campaign-generate-title" aria-describedby="fee-campaign-generate-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Начисление</p>
                <h3 id="fee-campaign-generate-title">Начислить сбор?</h3>
                <p>{feeCampaignGenerateTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть форму начисления сбора" onClick={closeFeeCampaignGenerateDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fee-campaign-generate-description">Backend создаст начисления по активным гаражам и запишет действие в историю изменений.</p>
            <FormField label="Месяц начисления">
              <input aria-label="Месяц начисления сбора" type="month" value={feeCampaignGenerateMonth} onChange={(event) => setFeeCampaignGenerateMonth(event.target.value)} />
            </FormField>
            <FormField label="Комментарий">
              <textarea aria-label="Комментарий к начислению сбора" value={feeCampaignGenerateComment} onChange={(event) => setFeeCampaignGenerateComment(event.target.value)} placeholder="Например: начисление по решению правления" />
            </FormField>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={feeCampaignGenerateCancelRef} className="ghost-button" type="button" onClick={closeFeeCampaignGenerateDialog}>Отмена</button>
              <button className="secondary-button" type="button" onClick={generateFeeCampaignAccruals} disabled={!feeCampaignGenerateMonth || feeCampaignSavingId === feeCampaignGenerateTarget.id}>
                <Save size={16} />
                <span>Начислить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {modal === 'service' ? (
        <AddServicePrototypeDialog
          isSaving={tariffSavingRowId === 'new-service'}
          incomeTypes={backendIncomeTypes.filter((incomeType) => !incomeType.isArchived)}
          onClose={() => setModal(null)}
          onSave={createServiceSetting}
          tariffs={backendTariffs.filter((tariff) => !tariff.isArchived)}
          unitOptions={Array.from(new Set(tariffRows.map((row) => row.unit).filter((unit): unit is string => Boolean(unit))))}
        />
      ) : null}
      {modal === 'fee' ? (
        <AddFeePrototypeDialog
          garageOptions={feeCampaignGarageOptions}
          incomeTypes={backendIncomeTypes.filter((incomeType) => !incomeType.isArchived)}
          isSaving={feeCampaignSavingId === 'new-fee-campaign'}
          onClose={() => setModal(null)}
          onSave={createFeeCampaign}
        />
      ) : null}
      {feeCampaignEditTarget ? (
        <AddFeePrototypeDialog
          garageOptions={feeCampaignGarageOptions}
          incomeTypes={backendIncomeTypes.filter((incomeType) => !incomeType.isArchived || incomeType.id === feeCampaignEditTarget.incomeTypeId)}
          initialCampaign={feeCampaignEditTarget}
          isSaving={feeCampaignSavingId === feeCampaignEditTarget.id}
          onClose={closeFeeCampaignEditDialog}
          onSave={updateFeeCampaign}
          submitLabel="Сохранить"
          title="Изменить сбор"
        />
      ) : null}
    </section>
  )
}

function AddServicePrototypeDialog({
  isSaving,
  incomeTypes,
  onClose,
  onSave,
  tariffs,
  unitOptions,
}: {
  isSaving: boolean
  incomeTypes: AccountingTypeDto[]
  onClose: () => void
  onSave: (request: UpsertChargeServiceSettingRequest) => Promise<void>
  tariffs: TariffDto[]
  unitOptions: string[]
}) {
  const [name, setName] = useState('')
  const [isRegular, setIsRegular] = useState(false)
  const [incomeTypeId, setIncomeTypeId] = useState(incomeTypes[0]?.id ?? '')
  const [tariffId, setTariffId] = useState(() => chooseRegularTariffId(incomeTypes[0]?.id ?? '', '', incomeTypes, tariffs))
  const [isByMeter, setIsByMeter] = useState(true)
  const [isTiered, setIsTiered] = useState(true)
  const [periodicityMonths, setPeriodicityMonths] = useState('12')
  const [accrualStartMonth, setAccrualStartMonth] = useState(contractorTariffMonthOptions[0].value)
  const [paymentDueDay, setPaymentDueDay] = useState('30')
  const [paymentDueMonth, setPaymentDueMonth] = useState(contractorTariffMonthOptions[6].value)
  const [overdueGraceDays, setOverdueGraceDays] = useState('30')
  const [unitName, setUnitName] = useState(unitOptions[0] ?? 'руб.')
  const [error, setError] = useState<string | null>(null)
  const compatibleTariffs = getCompatibleRegularTariffs(incomeTypeId, incomeTypes, tariffs)
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useEscapeKey(true, onClose)

  async function submitService(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const trimmedName = name.trim()
    const parsedPeriodicity = Number(periodicityMonths)
    const parsedDueDay = Number(paymentDueDay)
    const parsedOverdueDays = Number(overdueGraceDays)
    const dueMonthOption = contractorTariffMonthOptions.find((month) => month.value === paymentDueMonth)

    if (!trimmedName) {
      setError('Укажите наименование услуги.')
      return
    }

    if (isRegular) {
      if (!incomeTypeId) {
        setError('Выберите вид начисления для регулярной услуги.')
        return
      }

      if (!tariffId) {
        setError('Выберите тариф для регулярной услуги.')
        return
      }

      if (!Number.isInteger(parsedPeriodicity) || parsedPeriodicity < 1 || parsedPeriodicity > 120) {
        setError('Периодичность должна быть числом от 1 до 120 месяцев.')
        return
      }

      if (!Number.isInteger(parsedDueDay) || !dueMonthOption || parsedDueDay < 1 || parsedDueDay > dueMonthOption.maxDay) {
        setError(`Для месяца "${dueMonthOption?.label ?? 'не выбран'}" укажите день от 1 до ${dueMonthOption?.maxDay ?? 31}.`)
        return
      }

      if (!Number.isInteger(parsedOverdueDays) || parsedOverdueDays < 0 || parsedOverdueDays > 366) {
        setError('Перенос долга должен быть числом от 0 до 366 дней.')
        return
      }
    }

    setError(null)
    await onSave({
      name: trimmedName,
      isRegular,
      periodicityMonths: isRegular ? parsedPeriodicity : null,
      accrualStartMonth: isRegular ? getContractorTariffMonthNumber(accrualStartMonth) ?? 1 : null,
      paymentDueDay: isRegular ? parsedDueDay : null,
      paymentDueMonth: isRegular ? getContractorTariffMonthNumber(paymentDueMonth) ?? 1 : null,
      overdueGraceDays: isRegular ? parsedOverdueDays : 0,
      isMetered: isByMeter,
      hasTieredTariff: isByMeter && isTiered,
      unitName: unitName.trim() || null,
      incomeTypeId: isRegular ? incomeTypeId : null,
      tariffId: isRegular ? tariffId : null,
    })
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="detail-dialog contractors-dialog contractors-tariff-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-service-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <h3 id="contractor-service-title">Добавить услугу</h3>
          <button className="icon-button" type="button" aria-label="Закрыть форму услуги" onClick={onClose}>
            <X size={18} />
          </button>
        </div>

        <form className="dictionary-modal-form contractors-modal-form" onSubmit={submitService}>
          {error ? <FormError>{error}</FormError> : null}
          <FormField label="Наименование услуги">
            <input aria-label="Наименование услуги" value={name} onChange={(event) => setName(event.target.value)} />
          </FormField>
          <label className="contractors-switch-row">
            <span>Регулярные платежи</span>
            <span className="contractors-switch-control">
              <input type="checkbox" aria-label="Регулярные платежи" checked={isRegular} onChange={(event) => setIsRegular(event.target.checked)} />
            </span>
          </label>
          {isRegular ? (
            <>
              <div className="contractors-service-period-grid">
                <FormField label="Вид начисления">
                  <select
                    aria-label="Вид начисления регулярной услуги"
                    value={incomeTypeId}
                    onChange={(event) => {
                      const nextIncomeTypeId = event.target.value
                      setIncomeTypeId(nextIncomeTypeId)
                      setTariffId(chooseRegularTariffId(nextIncomeTypeId, tariffId, incomeTypes, tariffs))
                      setError(null)
                    }}
                  >
                    {incomeTypes.length > 0 ? incomeTypes.map((incomeType) => (
                      <option key={incomeType.id} value={incomeType.id}>{incomeType.name}</option>
                    )) : <option value="">Нет видов поступлений</option>}
                  </select>
                </FormField>
                <FormField label="Тариф">
                  <select
                    aria-label="Тариф регулярной услуги"
                    value={tariffId}
                    onChange={(event) => {
                      setTariffId(event.target.value)
                      setError(null)
                    }}
                  >
                    {compatibleTariffs.length > 0 ? compatibleTariffs.map((tariff) => (
                      <option key={tariff.id} value={tariff.id}>{tariff.name}</option>
                    )) : <option value="">Нет совместимых тарифов</option>}
                  </select>
                </FormField>
              </div>
              <div className="contractors-service-period-grid">
                <FormField label="Периодичность">
                  <input aria-label="Периодичность" inputMode="numeric" value={periodicityMonths} onChange={(event) => setPeriodicityMonths(event.target.value)} />
                </FormField>
                <FormField label="Учитывать платеж с">
                  <select aria-label="Учитывать платеж с" value={accrualStartMonth} onChange={(event) => setAccrualStartMonth(event.target.value)}>
                    {contractorTariffMonthOptions.map((month) => (
                      <option key={month.value} value={month.value}>{month.label}</option>
                    ))}
                  </select>
                </FormField>
                <FormField label="Оплатить до">
                  <div className="contractors-inline-field contractors-inline-field--date">
                    <input aria-label="День оплаты" inputMode="numeric" maxLength={2} value={paymentDueDay} onChange={(event) => setPaymentDueDay(event.target.value)} />
                    <select aria-label="Месяц оплаты" value={paymentDueMonth} onChange={(event) => setPaymentDueMonth(event.target.value)}>
                      {contractorTariffMonthOptions.map((month) => (
                        <option key={month.value} value={month.value}>{month.label}</option>
                      ))}
                    </select>
                  </div>
                </FormField>
              </div>
              <FormField label="Перенос долга в просроченный">
                <div className="contractors-inline-field">
                  <input aria-label="Перенос долга в просроченный" inputMode="numeric" value={overdueGraceDays} onChange={(event) => setOverdueGraceDays(event.target.value)} />
                  <span>дн.</span>
                </div>
              </FormField>
              <div className="contractors-service-flags">
                <label className="contractors-check-row">
                  <input
                    type="checkbox"
                    aria-label="По счетчику"
                    checked={isByMeter}
                    onChange={(event) => {
                      setIsByMeter(event.target.checked)
                      if (!event.target.checked) {
                        setIsTiered(false)
                      }
                    }}
                  />
                  <span>По счетчику</span>
                </label>
                <label className="contractors-check-row">
                  <input type="checkbox" aria-label="Пороговая тарификация" checked={isTiered} disabled={!isByMeter} onChange={(event) => setIsTiered(event.target.checked)} />
                  <span>Пороговая тарификация</span>
                </label>
              </div>
              <FormField label="Единица измерения">
                <input aria-label="Единица измерения" list="contractor-service-unit-options" value={unitName} onChange={(event) => setUnitName(event.target.value)} />
              </FormField>
              <datalist id="contractor-service-unit-options">
                {unitOptions.map((unit) => (
                  <option key={unit} value={unit} />
                ))}
              </datalist>
              {isTiered ? (
                <>
                  <div className="contractors-threshold-grid" aria-label="Пороги тарификации">
                    <span>Порог 1</span>
                    <input aria-label="Порог 1" />
                    <span>x</span>
                    <span>Цена за ед.</span>
                    <input aria-label="Цена за единицу 1" />
                    <span>Порог 2</span>
                    <input aria-label="Порог 2" />
                    <span>x</span>
                    <span>Цена за ед.</span>
                    <input aria-label="Цена за единицу 2" />
                  </div>
                  <button className="link-button" type="button">Добавить порог</button>
                </>
              ) : null}
            </>
          ) : (
            <FormField label="Стоимость">
              <div className="contractors-inline-field">
                <input aria-label="Стоимость услуги" />
                <span>руб.</span>
              </div>
            </FormField>
          )}

          <div className="detail-dialog-actions">
            <button className="secondary-button" type="submit" disabled={isSaving}>
              <Save size={17} />
              <span>Сохранить</span>
            </button>
            <button className="ghost-button" type="button" onClick={onClose}>
              Отмена
            </button>
          </div>
        </form>
      </section>
    </div>
  )
}

function AddFeePrototypeDialog({
  garageOptions,
  incomeTypes,
  initialCampaign,
  isSaving,
  onClose,
  onSave,
  submitLabel = 'Объявить сбор',
  title = 'Добавить сбор',
}: {
  garageOptions: GarageDto[]
  initialCampaign?: FeeCampaignDto | null
  incomeTypes: AccountingTypeDto[]
  isSaving: boolean
  onClose: () => void
  onSave: (request: UpsertFeeCampaignRequest) => Promise<void>
  submitLabel?: string
  title?: string
}) {
  const [name, setName] = useState(initialCampaign?.name ?? '')
  const [incomeTypeId, setIncomeTypeId] = useState(initialCampaign?.incomeTypeId ?? incomeTypes[0]?.id ?? '')
  const [goal, setGoal] = useState(initialCampaign?.goal ?? '')
  const [contributionAmount, setContributionAmount] = useState(initialCampaign ? String(initialCampaign.contributionAmount) : '')
  const [targetAmount, setTargetAmount] = useState(initialCampaign ? String(initialCampaign.targetAmount) : '')
  const [startsOn, setStartsOn] = useState(initialCampaign?.startsOn ?? getLocalDateInputValue())
  const [endsOn, setEndsOn] = useState(initialCampaign?.endsOn ?? '')
  const [appliesToAllGarages, setAppliesToAllGarages] = useState(initialCampaign?.appliesToAllGarages ?? true)
  const [participantGarageIds, setParticipantGarageIds] = useState<string[]>(initialCampaign?.participantGarageIds ?? [])
  const [overdueGraceDays, setOverdueGraceDays] = useState(String(initialCampaign?.overdueGraceDays ?? 30))
  const [error, setError] = useState<string | null>(null)
  const [pendingConfirmation, setPendingConfirmation] = useState<{ request: UpsertFeeCampaignRequest; changes: ChangePreview[] } | null>(null)
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useRestoreFocusOnClose(Boolean(pendingConfirmation))
  const confirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingConfirmation))
  const confirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingConfirmation))
  useEscapeKey(!pendingConfirmation, onClose)
  useEscapeKey(Boolean(pendingConfirmation), () => setPendingConfirmation(null))

  function toggleParticipantGarage(garageId: string, checked: boolean) {
    setParticipantGarageIds((currentIds) => {
      if (checked) {
        return currentIds.includes(garageId) ? currentIds : [...currentIds, garageId]
      }

      return currentIds.filter((currentId) => currentId !== garageId)
    })
  }

  async function submitFee(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmedName = name.trim()
    const parsedContributionAmount = parsePrototypeAmount(contributionAmount)
    const parsedTargetAmount = parsePrototypeAmount(targetAmount)
    const parsedOverdueGraceDays = Number(overdueGraceDays)

    if (!trimmedName) {
      setError('Укажите наименование сбора.')
      return
    }

    if (!incomeTypeId) {
      setError('Выберите вид поступления для сбора.')
      return
    }

    if (parsedContributionAmount === null || parsedContributionAmount <= 0) {
      setError('Сумма взноса должна быть больше нуля.')
      return
    }

    if (parsedTargetAmount === null || parsedTargetAmount <= 0) {
      setError('Сумма сбора должна быть больше нуля.')
      return
    }

    if (!startsOn) {
      setError('Укажите дату начала сбора.')
      return
    }

    if (endsOn && endsOn < startsOn) {
      setError('Дата окончания не может быть раньше даты начала.')
      return
    }

    if (!Number.isInteger(parsedOverdueGraceDays) || parsedOverdueGraceDays < 0 || parsedOverdueGraceDays > 366) {
      setError('Перенос долга должен быть числом от 0 до 366 дней.')
      return
    }

    if (!appliesToAllGarages && participantGarageIds.length === 0) {
      setError('Выберите хотя бы один гараж для сбора.')
      return
    }

    const request: UpsertFeeCampaignRequest = {
      name: trimmedName,
      incomeTypeId,
      goal: goal.trim() || null,
      contributionAmount: parsedContributionAmount,
      targetAmount: parsedTargetAmount,
      startsOn,
      endsOn: endsOn || null,
      appliesToAllGarages,
      participantGarageIds: appliesToAllGarages ? [] : participantGarageIds,
      overdueGraceDays: parsedOverdueGraceDays,
    }

    setError(null)

    if (initialCampaign) {
      const changes = getFeeCampaignChangePreview(initialCampaign, request, incomeTypes, garageOptions)
      if (changes.length === 0) {
        onClose()
        return
      }

      setPendingConfirmation({ request, changes })
      return
    }

    await onSave(request)
  }

  async function confirmFeeChanges() {
    if (!pendingConfirmation) {
      return
    }

    await onSave(pendingConfirmation.request)
    setPendingConfirmation(null)
  }

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={pendingConfirmation ? undefined : onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog contractors-tariff-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-fee-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="contractor-fee-title">{title}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму сбора" onClick={onClose} disabled={Boolean(pendingConfirmation)}>
              <X size={18} />
            </button>
          </div>

          <form className="dictionary-modal-form contractors-modal-form" onSubmit={submitFee}>
            {error ? <FormError>{error}</FormError> : null}
            <FormField label="Наименование сбора">
              <input aria-label="Наименование сбора" value={name} onChange={(event) => setName(event.target.value)} />
            </FormField>
          <FormField label="Вид поступления">
            <select aria-label="Вид поступления для сбора" value={incomeTypeId} onChange={(event) => setIncomeTypeId(event.target.value)}>
              {incomeTypes.length > 0 ? incomeTypes.map((incomeType) => (
                <option key={incomeType.id} value={incomeType.id}>{incomeType.name}</option>
              )) : <option value="">Нет видов поступлений</option>}
            </select>
          </FormField>
          <FormField label="Цель">
            <input aria-label="Цель сбора" value={goal} onChange={(event) => setGoal(event.target.value)} />
          </FormField>
          <div className="contractors-service-period-grid">
            <FormField label="Сумма взноса">
              <div className="contractors-inline-field">
                <input aria-label="Сумма взноса" inputMode="decimal" value={contributionAmount} onChange={(event) => setContributionAmount(event.target.value)} />
                <span>руб.</span>
              </div>
            </FormField>
            <FormField label="Сумма сбора">
              <div className="contractors-inline-field">
                <input aria-label="Сумма сбора" inputMode="decimal" value={targetAmount} onChange={(event) => setTargetAmount(event.target.value)} />
                <span>руб.</span>
              </div>
            </FormField>
          </div>
          <label className="contractors-switch-row">
            <span>Участники</span>
            <span className="contractors-switch-control">
              <input type="checkbox" aria-label="Все гаражи" checked={appliesToAllGarages} onChange={(event) => setAppliesToAllGarages(event.target.checked)} />
            </span>
            <span>все гаражи</span>
          </label>
          {!appliesToAllGarages ? (
            <fieldset className="contractors-participant-list">
              <legend>Выбранные гаражи</legend>
              {garageOptions.length > 0 ? garageOptions.map((garage) => (
                <label key={garage.id} className="contractors-participant-option">
                  <input
                    type="checkbox"
                    aria-label={`Гараж ${garage.number}`}
                    checked={participantGarageIds.includes(garage.id)}
                    onChange={(event) => toggleParticipantGarage(garage.id, event.target.checked)}
                  />
                  <span>
                    <strong>Гараж {garage.number}</strong>
                    {garage.ownerName ? <small>{garage.ownerName}</small> : null}
                  </span>
                </label>
              )) : <p className="form-hint">Активные гаражи не найдены.</p>}
            </fieldset>
          ) : null}
          <div className="contractors-service-period-grid">
            <FormField label="Дата начала">
              <div className="contractors-inline-field">
                <input aria-label="Дата начала" type="date" value={startsOn} onChange={(event) => setStartsOn(event.target.value)} />
                <button className="link-button" type="button" onClick={() => setStartsOn(getLocalDateInputValue())}>Сегодня</button>
              </div>
            </FormField>
            <FormField label="Дата окончания сбора">
              <input aria-label="Дата окончания сбора" type="date" value={endsOn} onChange={(event) => setEndsOn(event.target.value)} />
            </FormField>
          </div>
          <FormField label="Перенос долга по сбору в просроченный">
            <div className="contractors-inline-field">
              <input aria-label="Перенос долга по сбору в просроченный" inputMode="numeric" value={overdueGraceDays} onChange={(event) => setOverdueGraceDays(event.target.value)} />
              <span>дн.</span>
            </div>
          </FormField>

            <div className="detail-dialog-actions">
              <button className="secondary-button" type="submit" disabled={isSaving || Boolean(pendingConfirmation)}>
                {submitLabel}
              </button>
              <button className="ghost-button" type="button" onClick={onClose} disabled={Boolean(pendingConfirmation)}>
                Отмена
              </button>
            </div>
          </form>
        </section>
      </div>

      {pendingConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setPendingConfirmation(null)}>
          <section ref={confirmationDialogRef} className="detail-dialog contractors-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="fee-campaign-edit-confirmation-title" aria-describedby="fee-campaign-edit-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Изменение</p>
                <h3 id="fee-campaign-edit-confirmation-title">Подтвердите изменения сбора</h3>
                <p>{initialCampaign?.name ?? pendingConfirmation.request.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить подтверждение изменений сбора" onClick={() => setPendingConfirmation(null)} disabled={isSaving}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fee-campaign-edit-confirmation-description">Проверьте, что именно изменится. После подтверждения действие будет записано в историю изменений.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля сбора">
              {pendingConfirmation.changes.map((change) => (
                <li key={`${change.field}-${change.before}-${change.after}`}>
                  <span className="dictionary-change-field">{change.field}</span>
                  <span className="dictionary-change-values">
                    <span className="dictionary-change-value">{change.before}</span>
                    <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                    <span className="dictionary-change-value dictionary-change-value-after">{change.after}</span>
                  </span>
                </li>
              ))}
            </ul>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={confirmationCancelRef} className="ghost-button" type="button" onClick={() => setPendingConfirmation(null)} disabled={isSaving}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmFeeChanges()} disabled={isSaving}>
                <Save size={16} />
                <span>{isSaving ? 'Сохраняем...' : 'Сохранить изменения'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </>
  )
}
