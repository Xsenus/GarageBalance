import { Fragment, useEffect, useRef, useState } from 'react'
import type { CSSProperties, FormEvent, KeyboardEvent, MouseEvent, PointerEvent } from 'react'
import { CalendarPlus, CircleCheck, FileSpreadsheet, FileText, Pencil, PowerOff, RotateCcw, Save, Trash2, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import { DictionaryApiError } from '../../services/dictionariesApi'
import type { AccountingTypeDto, ChargeServiceSettingDto, ChargeServiceTariffPeriodDto, CreateChargeServiceWithTariffRequest, DictionaryClient, FeeCampaignDto, GarageDto, IrregularPaymentDto, MeasurementUnitDto, StaffDepartmentSalaryFundDto, TariffDto, UpdateChargeServiceWithTariffRequest, UpsertChargeServiceSettingRequest, UpsertChargeServiceTariffScheduleRequest, UpsertFeeCampaignRequest, UpsertIrregularPaymentRequest, UpsertTariffRequest } from '../../services/dictionariesApi'
import { areFeeCampaignAmountsEqual, calculateFeeCampaignContributionAmount, calculateFeeCampaignLastContribution, calculateFeeCampaignTargetAmount } from './feeCampaignAmounts'
import type { FundOptionDto, FundsClient } from '../../services/fundsApi'
import type { ApplicationSettingsClient } from '../../services/settingsApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { AsyncErrorState, EmptyState, TableLoadingState } from '../../shared/AsyncState'
import type { ChangePreview } from '../../shared/changePreview'
import { appendChangePreview, formatChangeDate, formatChangeNumber, formatChangeText } from '../../shared/changePreview'
import { FormError } from '../../shared/formFeedback'
import { FormField } from '../../shared/FormField'
import { EditableCombobox } from '../../shared/EditableCombobox'
import { getTariffCalculationUnitName } from '../../shared/dictionaryWorkbench'
import { formatDateOnly, getLocalDateInputValue } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { MeterReadingInput } from '../../shared/MeterReadingInput'
import { MoneyInput, MoneyTextInput } from '../../shared/MoneyInput'
import { formatPrototypeChangeValue, handleEditableInputKeyDown, shouldCommitEditableInputOnBlur } from '../../shared/prototypeEditing'
import { createClientPage } from '../../shared/pagination'
import { SelectControl } from '../../shared/SelectControl'
import { TablePagination } from '../../shared/TablePagination'
import { isMeterTariff } from '../../shared/validation'
import { formatTariffDecimal } from './tariffFormatting'
import { getInlineTariffChangeEffectiveFrom, getServiceMeasurementUnit, getServiceTariffDisplayName } from './tariffServicePresentation'

const dictionaryScreenRequestLimit = 100
const persistedGuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const defaultTariffPanelsSplitPercent = 40
const minimumTariffPanelsSplitPercent = 25
const maximumTariffPanelsSplitPercent = 60

type ContractorTariffRow = {
  id: string
  backendTariffId?: string
  backendServiceSettingId?: string
  serviceSettingKind?: 'main' | 'periodicity' | 'start-date' | 'due-date' | 'overdue-days'
  title: string
  amount?: string
  dateDay?: string
  dateMonth?: string
  monthlyDue?: boolean
  unit?: string
  threshold?: string
  byMeter?: boolean
  tiered?: boolean
  group?: string
  category: string
  calculationBase?: string
  effectiveFrom?: string
  electricityFirstThreshold?: number | null
  electricitySecondThreshold?: number | null
  electricityTierId?: string
  electricityUpperBound?: number | null
  isCustomThreshold?: boolean
  isDeleted?: boolean
}

const contractorTariffRows: ContractorTariffRow[] = [
  { id: 'water-rate', group: 'Вода', category: 'Вода', title: 'Тариф на воду', amount: '', unit: 'руб.', byMeter: true, tiered: false, calculationBase: 'meter_water' },
  { id: 'water-overdue-days', category: 'Вода', title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: true, tiered: false },
  { id: 'waste-rate', group: 'Мусор', category: 'Мусор', title: 'Ставка за вывоз мусора', amount: '', unit: 'руб.', byMeter: false, tiered: false, calculationBase: 'people' },
  { id: 'waste-overdue-days', category: 'Мусор', title: 'Перенос долга в просроченный', amount: '30', unit: 'дн.', byMeter: false, tiered: false },
  { id: 'electricity-tier-0', group: 'Электроэнергия', category: 'Электроэнергия', title: 'До 1 100 кВт·ч', threshold: 'x', amount: '', unit: 'руб.', byMeter: true, tiered: true, calculationBase: 'meter_electricity' },
  { id: 'electricity-tier-1', category: 'Электроэнергия', title: 'От 1 100 до 1 700 кВт·ч', threshold: 'x', amount: '', unit: 'руб.', byMeter: true, tiered: true, calculationBase: 'meter_electricity' },
  { id: 'electricity-tier-3', category: 'Электроэнергия', title: 'Свыше 1 700 кВт·ч', threshold: 'x', amount: '', unit: 'руб.', byMeter: true, tiered: true, calculationBase: 'meter_electricity' },
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
]

const salaryFundCategory = 'Зарплатный фонд'

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
  dateDay: string
  dateMonth: string
  electricityUpperBoundText: string
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
const yesNoOptions = [
  { value: 'Да', label: 'Да' },
  { value: 'Нет', label: 'Нет' },
]

const regularServicePeriodicityOptions = [
  { value: '1', label: 'Ежемесячно' },
  { value: '12', label: 'Ежегодно' },
]

function normalizeRegularServicePeriodicity(periodicityMonths?: number | string | null) {
  return Number(periodicityMonths) >= 12 ? '12' : '1'
}

function createEditableDrafts(rows: Array<{ id: string; title?: string; amount?: string; unit?: string; dateDay?: string; dateMonth?: string; electricityUpperBound?: number | null }>) {
  return rows.reduce<Record<string, ContractorTariffDraft>>((drafts, row) => {
    drafts[row.id] = {
      title: row.title ?? '',
      amount: row.amount ?? '',
      dateDay: row.dateDay ?? '',
      dateMonth: row.dateMonth ?? '',
      electricityUpperBoundText: row.electricityUpperBound == null ? '' : formatTariffNumber(row.electricityUpperBound),
    }
    return drafts
  }, {})
}

function createOneTimeEditableDrafts(rows: ContractorOneTimeRow[]) {
  return rows.reduce<Record<string, Partial<ContractorOneTimeRow>>>((drafts, row) => {
    drafts[row.id] = { amount: row.amount }
    return drafts
  }, {})
}

function formatContractorTariffDate(day: string, month: string) {
  return `${day.padStart(2, '0')} ${month}`.trim()
}

function getContractorTariffDateError(day: string, month: string, dayOnly = false) {
  const trimmedDay = day.trim()
  const monthOption = contractorTariffMonthOptions.find((option) => option.value === month)

  if (!/^\d{1,2}$/.test(trimmedDay)) {
    return 'Укажите день числом от 1 до 31.'
  }

  if (!dayOnly && !monthOption) {
    return 'Выберите месяц.'
  }

  const numericDay = Number(trimmedDay)
  const maxDay = dayOnly ? 31 : monthOption!.maxDay
  if (numericDay < 1 || numericDay > maxDay) {
    if (dayOnly) {
      return 'Укажите день числом от 1 до 31.'
    }
    return `В месяце "${monthOption!.label}" можно указать день от 1 до ${monthOption!.maxDay}.`
  }

  return null
}

function formatTariffNumber(value: number | null | undefined) {
  return value == null ? '' : formatTariffDecimal(value)
}

function getElectricityThresholdRows(rows: ContractorTariffRow[], sourceRow?: ContractorTariffRow) {
  return rows.filter((row) => Boolean(row.threshold)
    && (!sourceRow?.backendTariffId || row.backendTariffId === sourceRow.backendTariffId))
}

function getElectricityTierLowerBound(rows: ContractorTariffRow[], rowId: string) {
  const sourceRow = rows.find((row) => row.id === rowId)
  const thresholdRows = getElectricityThresholdRows(rows, sourceRow)
  const rowIndex = thresholdRows.findIndex((row) => row.id === rowId)
  return rowIndex <= 0 ? 0 : (thresholdRows[rowIndex - 1].electricityUpperBound ?? -1) + 1
}

function formatElectricityTierName(lowerBound: number, upperBound: number | null | undefined) {
  const formattedLowerBound = formatTariffDecimal(lowerBound)
  return upperBound == null
    ? `${formattedLowerBound} и выше`
    : `${formattedLowerBound}–${formatTariffDecimal(upperBound)}`
}

function normalizeElectricityTierNames(rows: ContractorTariffRow[]) {
  const lowerBounds = new Map<string, number>()
  const groups = new Map<string, ContractorTariffRow[]>()
  rows.filter((row) => row.threshold).forEach((row) => {
    const key = row.backendTariffId ?? row.category
    groups.set(key, [...(groups.get(key) ?? []), row])
  })
  groups.forEach((thresholdRows) => thresholdRows.forEach((row, index) => {
    lowerBounds.set(row.id, index === 0 ? 0 : (thresholdRows[index - 1].electricityUpperBound ?? -1) + 1)
  }))
  return rows.map((row) => row.threshold
    ? { ...row, title: formatElectricityTierName(lowerBounds.get(row.id) ?? 0, row.electricityUpperBound) }
    : row)
}

function formatPrototypeAmount(value: number | null | undefined) {
  return value == null ? '' : formatTariffDecimal(value)
}

function parsePrototypeAmount(value: string) {
  const normalized = value.replace(/[\s\u00a0]+/g, '').replace(',', '.').trim()
  if (!normalized) {
    return null
  }

  const parsed = Number(normalized)
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null
}

function formatFeeCampaignParticipantCount(participantCount: number) {
  const lastTwoDigits = participantCount % 100
  if (lastTwoDigits >= 11 && lastTwoDigits <= 14) {
    return `${participantCount} участников`
  }

  return `${participantCount} ${participantCount % 10 === 1 ? 'участник' : participantCount % 10 >= 2 && participantCount % 10 <= 4 ? 'участника' : 'участников'}`
}

function parseTariffAmount(value: string, allowZero = false) {
  const normalized = value.replace(/[\s\u00a0]+/g, '').replace(',', '.').trim()
  if (!normalized) {
    return null
  }

  const parsed = Number(normalized)
  return Number.isFinite(parsed) && (allowZero ? parsed >= 0 : parsed > 0) ? parsed : null
}

function getElectricityTariffTiers(tariff: TariffDto | null) {
  if (!tariff || !tariff.calculationBase.startsWith('meter_')) {
    return []
  }

  if ((tariff.electricityTiers?.length ?? 0) > 0) {
    return tariff.electricityTiers!
  }

  if (
    tariff.electricityFirstThreshold == null
    || tariff.electricitySecondThreshold == null
    || tariff.electricityFirstRate == null
    || tariff.electricitySecondRate == null
    || tariff.electricityThirdRate == null
  ) {
    return []
  }

  return [
    { id: `${tariff.id}-legacy-1`, name: tariff.electricityFirstTierName ?? 'Порог 1', upperBound: tariff.electricityFirstThreshold, rate: tariff.electricityFirstRate ?? tariff.rate, isCustom: false },
    { id: `${tariff.id}-legacy-2`, name: tariff.electricitySecondTierName ?? 'Порог 2', upperBound: tariff.electricitySecondThreshold, rate: tariff.electricitySecondRate ?? tariff.rate, isCustom: false },
    { id: `${tariff.id}-legacy-3`, name: tariff.electricityThirdTierName ?? 'Порог 3', upperBound: null, rate: tariff.electricityThirdRate ?? tariff.rate, isCustom: false },
  ]
}

function isTariffMoneyAmount(row: ContractorTariffRow) {
  if (row.serviceSettingKind === 'periodicity' || row.serviceSettingKind === 'overdue-days') {
    return false
  }

  const unit = (row.unit ?? '').trim().toLocaleLowerCase('ru')
  return Boolean(row.calculationBase) || unit.startsWith('руб')
}

function normalizeTariffDraftValue(row: ContractorTariffRow, field: 'title' | 'amount', value: string) {
  return field === 'amount' && isTariffMoneyAmount(row) ? formatTariffDecimal(value) : value
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

function isPrototypeServiceMatch(row: ContractorTariffRow, setting: ChargeServiceSettingDto) {
  const normalizedCategory = row.category.toLocaleLowerCase('ru')
  const normalizedName = setting.name.toLocaleLowerCase('ru')
  if (normalizedCategory === normalizedName) return true
  if (row.category === 'Вода') return setting.meterKind === 'water'
  if (row.category === 'Электроэнергия') return setting.meterKind === 'electricity'
  return false
}

function findPrototypeServiceSetting(row: ContractorTariffRow, settings: ChargeServiceSettingDto[]) {
  return settings.find((setting) => isPrototypeServiceMatch(row, setting)) ?? null
}

function mergeTariffsIntoPrototypeRows(rows: ContractorTariffRow[], tariffs: TariffDto[]) {
  return rows
    .filter((row) => row.calculationBase !== 'meter_electricity' || Boolean(row.group))
    .map((row) => {
      const tariff = row.backendTariffId
        ? tariffs.find((item) => item.id === row.backendTariffId) ?? null
        : findTariffForPrototypeRow(tariffs, row)
      return tariff && row.calculationBase
        ? {
            ...row,
            backendTariffId: tariff.id,
            effectiveFrom: tariff.effectiveFrom,
            amount: formatTariffNumber(tariff.rate),
            title: row.serviceSettingKind === 'main' ? row.title : tariff.name,
            unit: getTariffCalculationUnitName(tariff.calculationBase),
          }
        : row
    })
}

function expandTieredServiceRows(rows: ContractorTariffRow[], tariffs: TariffDto[]) {
  return normalizeElectricityTierNames(rows.flatMap((row) => {
    if ((row.serviceSettingKind !== 'main' && !row.group) || !row.tiered || !row.backendTariffId) return [row]
    const tariff = tariffs.find((candidate) => candidate.id === row.backendTariffId) ?? null
    const tiers = getElectricityTariffTiers(tariff)
    if (!tariff || tiers.length < 2) return [row]

    return tiers.map((tier, index): ContractorTariffRow => ({
      ...row,
      id: `tariff-tier-${tariff.id}-${tier.id}`,
      serviceSettingKind: index === 0 ? row.serviceSettingKind : undefined,
      group: index === 0 ? row.group : undefined,
      title: formatElectricityTierName(index === 0 ? 0 : tiers[index - 1].upperBound ?? 0, tier.upperBound),
      threshold: 'x',
      amount: formatTariffNumber(tier.rate),
      // The service card owns the visible unit. The calculation base is only a
      // fallback for legacy tariffs that do not have a configured unit yet.
      unit: row.unit?.trim() || getTariffCalculationUnitName(tariff.calculationBase),
      byMeter: true,
      tiered: true,
      calculationBase: tariff.calculationBase,
      electricityTierId: tier.id,
      electricityUpperBound: tier.upperBound,
      isCustomThreshold: tier.isCustom,
    }))
  }))
}

function createSalaryFundRows(items: StaffDepartmentSalaryFundDto[]): ContractorTariffRow[] {
  return items.map((item, index) => ({
    id: item.departmentId,
    group: index === 0 ? salaryFundCategory : undefined,
    category: salaryFundCategory,
    title: item.departmentName,
    amount: formatPrototypeAmount(item.totalRate),
    unit: 'руб.',
  }))
}

function createTariffRowsFromBackend(tariffs: TariffDto[], settings: ChargeServiceSettingDto[], salaryFund: StaffDepartmentSalaryFundDto[] = []) {
  const customSettings = settings
    .filter((setting) => !contractorTariffRows.some((row) => isPrototypeServiceMatch(row, setting)))
  const customServiceTariffIds = new Set(customSettings
    .map((setting) => setting.tariffId)
    .filter((tariffId): tariffId is string => Boolean(tariffId)))
  const linkedTariffIds = new Set(settings
    .map((setting) => setting.tariffId)
    .filter((tariffId): tariffId is string => Boolean(tariffId)))
  const linkedTariffNames = settings
    .map((setting) => tariffs.find((tariff) => tariff.id === setting.tariffId)?.name.trim().toLocaleLowerCase('ru'))
    .filter((name): name is string => Boolean(name))
  const displacedPrototypeCategories = new Set(customSettings.flatMap((setting) => {
    const linkedTariff = tariffs.find((tariff) => tariff.id === setting.tariffId)
    if (!linkedTariff) {
      return []
    }

    const normalizedTariffName = linkedTariff.name.toLocaleLowerCase('ru')
    const prototypeRow = contractorTariffRows.find((row) => (
      Boolean(row.calculationBase)
      && (
        row.title.toLocaleLowerCase('ru') === normalizedTariffName
        || normalizedTariffName.includes(row.category.toLocaleLowerCase('ru'))
      )
    ))
    return prototypeRow ? [prototypeRow.category] : []
  }))
  const prototypeTariffs = tariffs.filter((tariff) => {
    if (customServiceTariffIds.has(tariff.id)) {
      return false
    }
    if (linkedTariffIds.has(tariff.id)) {
      return true
    }

    const normalizedName = tariff.name.trim().toLocaleLowerCase('ru')
    return !linkedTariffNames.some((linkedName) => (
      linkedName === normalizedName
      || linkedName.startsWith(`${normalizedName} —`)
      || normalizedName.startsWith(`${linkedName} —`)
    ))
  })
  const backedCategories = new Set(contractorTariffRows
    .filter((row) => (
      !displacedPrototypeCategories.has(row.category)
      && Boolean(row.calculationBase && findTariffForPrototypeRow(prototypeTariffs, row))
    ))
    .map((row) => row.category))
  const rowsBackedByTariffs = contractorTariffRows.filter((row) => backedCategories.has(row.category))
  return [
    ...expandTieredServiceRows(mergeChargeServicesIntoPrototypeRows(
    mergeTariffsIntoPrototypeRows(rowsBackedByTariffs, prototypeTariffs),
    settings.filter((setting) => setting.isRegular),
    tariffs,
    ), tariffs),
    ...createSalaryFundRows(salaryFund),
  ]
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

function createChargeServiceRows(setting: ChargeServiceSettingDto, tariffs: TariffDto[]): ContractorTariffRow[] {
  const periodicityMonths = normalizeRegularServicePeriodicity(setting.periodicityMonths)
  const isMonthly = periodicityMonths === '1'
  const linkedTariff = tariffs.find((tariff) => tariff.id === setting.tariffId)
  const unitName = getServiceMeasurementUnit(setting, linkedTariff)
  const rows: ContractorTariffRow[] = [
    {
      id: `charge-service-${setting.id}-main`,
      backendServiceSettingId: setting.id,
      serviceSettingKind: 'main',
      group: setting.name,
      category: setting.name,
      title: getServiceTariffDisplayName(linkedTariff?.name, setting.name),
      amount: linkedTariff ? formatPrototypeAmount(linkedTariff.rate) : '',
      unit: unitName,
      byMeter: setting.isMetered,
      tiered: setting.hasTieredTariff,
      calculationBase: linkedTariff?.calculationBase,
      backendTariffId: linkedTariff?.id,
      effectiveFrom: linkedTariff?.effectiveFrom,
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
        amount: periodicityMonths,
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
        dateMonth: isMonthly ? undefined : getContractorTariffMonthValue(setting.paymentDueMonth),
        monthlyDue: isMonthly,
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
    if (!isMonthly) {
      rows.splice(3, 0, {
        id: `charge-service-${setting.id}-start-date`,
        backendServiceSettingId: setting.id,
        serviceSettingKind: 'start-date',
        category: setting.name,
        title: 'Месяц начисления',
        dateDay: '01',
        dateMonth: getContractorTariffMonthValue(setting.accrualStartMonth),
        byMeter: setting.isMetered,
        tiered: setting.hasTieredTariff,
        isDeleted: setting.isArchived,
      })
    }
  }

  return rows
}

function mergeChargeServicesIntoPrototypeRows(rows: ContractorTariffRow[], settings: ChargeServiceSettingDto[], tariffs: TariffDto[]) {
  const rowsWithoutBackendServices = rows.filter((row) => !row.backendServiceSettingId)
  const matchedSettingIds = new Set(rowsWithoutBackendServices
    .map((row) => findPrototypeServiceSetting(row, settings)?.id)
    .filter((id): id is string => Boolean(id)))
  const mergedRows = rowsWithoutBackendServices.flatMap((row) => {
    const setting = findPrototypeServiceSetting(row, settings)
    if (!setting) {
      return row
    }

    const linkedTariff = tariffs.find((tariff) => tariff.id === setting.tariffId)
    const common = {
      ...row,
      category: setting.name,
      unit: (row.serviceSettingKind === 'main' || row.calculationBase)
        ? setting.unitName?.trim() || row.unit
        : row.unit,
      byMeter: setting.isMetered,
      tiered: setting.hasTieredTariff,
      isDeleted: setting.isArchived,
    }
    const periodicityMonths = normalizeRegularServicePeriodicity(setting.periodicityMonths)
    const isMonthly = periodicityMonths === '1'
    if (row.title === 'Периодичность') {
      return [{ ...common, backendServiceSettingId: setting.id, serviceSettingKind: 'periodicity' as const, amount: periodicityMonths, unit: undefined }]
    }
    if (row.title === 'Учитывать платеж с') {
      return isMonthly
        ? []
        : [{ ...common, backendServiceSettingId: setting.id, serviceSettingKind: 'start-date' as const, title: 'Месяц начисления', dateDay: '01', dateMonth: getContractorTariffMonthValue(setting.accrualStartMonth) }]
    }
    if (row.title === 'Оплата до' || row.title === 'Оплата за год до') {
      return [{
        ...common,
        backendServiceSettingId: setting.id,
        serviceSettingKind: 'due-date' as const,
        dateDay: setting.paymentDueDay ? String(setting.paymentDueDay).padStart(2, '0') : row.dateDay,
        dateMonth: isMonthly ? undefined : (setting.paymentDueMonth ? getContractorTariffMonthValue(setting.paymentDueMonth) : row.dateMonth),
        monthlyDue: isMonthly,
      }]
    }
    if (row.title === 'Перенос долга в просроченный') {
      return [{ ...common, backendServiceSettingId: setting.id, serviceSettingKind: 'overdue-days' as const, amount: String(setting.overdueGraceDays) }]
    }
    if (row.group && row.calculationBase) {
      return [{
        ...common,
        backendServiceSettingId: setting.id,
        serviceSettingKind: 'main' as const,
        group: setting.name,
        category: setting.name,
        title: getServiceTariffDisplayName(linkedTariff?.name, setting.name),
        amount: linkedTariff ? formatPrototypeAmount(linkedTariff.rate) : row.amount,
        unit: setting.unitName?.trim() || row.unit || (linkedTariff ? getTariffCalculationUnitName(linkedTariff.calculationBase) : undefined),
        calculationBase: linkedTariff?.calculationBase ?? row.calculationBase,
        backendTariffId: linkedTariff?.id,
        effectiveFrom: linkedTariff?.effectiveFrom,
      }]
    }
    return [common]
  })
  const unmatchedSettings = settings.filter((setting) => !matchedSettingIds.has(setting.id))
  return [
    ...mergedRows,
    ...unmatchedSettings.flatMap((setting) => createChargeServiceRows(setting, tariffs)),
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

type TariffPrototypePendingChange = (
  | {
    kind: 'tariff-text'
    rowId: string
    field: 'title' | 'amount'
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
)

function getTariffTextFieldLabel(row: ContractorTariffRow, field: 'title' | 'amount') {
  return field === 'title'
    ? 'Наименование порога'
    : row.serviceSettingKind === 'main'
      ? 'Стоимость, руб.'
      : row.serviceSettingKind === 'overdue-days' ? 'Перенос долга в просроченный' : 'Значение'
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
  appendChangePreview(changes, 'Назначение поступления', formatIncomeType(campaign.incomeTypeId), formatIncomeType(request.incomeTypeId))
  appendChangePreview(changes, 'Цель', formatChangeText(campaign.goal), formatChangeText(request.goal))
  appendChangePreview(changes, 'Сумма взноса', formatTariffDecimal(campaign.contributionAmount), formatTariffDecimal(request.contributionAmount))
  appendChangePreview(changes, 'Сумма сбора', formatTariffDecimal(campaign.targetAmount), formatTariffDecimal(request.targetAmount))
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

function mergeFeeCampaignSnapshots(currentCampaigns: FeeCampaignDto[], loadedCampaigns: FeeCampaignDto[]) {
  const currentIds = new Set(currentCampaigns.map((campaign) => campaign.id))
  return [
    ...currentCampaigns,
    ...loadedCampaigns.filter((campaign) => !currentIds.has(campaign.id)),
  ]
}

function getFeeCampaignDisplayRank(campaign: FeeCampaignDto, today: string) {
  return Number(Boolean(campaign.isArchived || campaign.closedAtUtc || (campaign.endsOn && campaign.endsOn < today)))
}

export function TariffsAndFeesPrototypePanel({ auth, dictionaryClient, fundsClient, settingsClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient; fundsClient: FundsClient; settingsClient: ApplicationSettingsClient }) {
  const [modal, setModal] = useState<'service' | 'fee' | null>(null)
  const [tariffRows, setTariffRows] = useState<ContractorTariffRow[]>([])
  const [tariffPageNumber, setTariffPageNumber] = useState(1)
  const [tariffPageSize, setTariffPageSize] = useState(25)
  const [chargeServiceView, setChargeServiceView] = useState<'active' | 'deleted'>('active')
  const [backendTariffs, setBackendTariffs] = useState<TariffDto[]>([])
  const [backendIncomeTypes, setBackendIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [backendMeasurementUnits, setBackendMeasurementUnits] = useState<MeasurementUnitDto[]>([])
  const [backendFunds, setBackendFunds] = useState<FundOptionDto[]>([])
  const [backendChargeServices, setBackendChargeServices] = useState<ChargeServiceSettingDto[]>([])
  const [feeCampaignGarageOptions, setFeeCampaignGarageOptions] = useState<GarageDto[]>([])
  const [feeCampaignActiveGarageCount, setFeeCampaignActiveGarageCount] = useState(0)
  const feeCampaignGarageOptionsLoadedRef = useRef(false)
  const feeCampaignGarageOptionsRequestRef = useRef<Promise<boolean> | null>(null)
  const tariffReferencesLoadedRef = useRef(false)
  const tariffReferencesFailedRef = useRef(false)
  const tariffReferencesRequestRef = useRef<Promise<boolean> | null>(null)
  const tariffReferencesControllerRef = useRef<AbortController | null>(null)
  const feeCampaignMutationVersionRef = useRef(0)
  const [feeCampaigns, setFeeCampaigns] = useState<FeeCampaignDto[]>([])
  const [feeCampaignPageNumber, setFeeCampaignPageNumber] = useState(1)
  const [feeCampaignPageSize, setFeeCampaignPageSize] = useState(10)
  const [feeCampaignSavingId, setFeeCampaignSavingId] = useState<string | null>(null)
  const [feeCampaignEditTarget, setFeeCampaignEditTarget] = useState<FeeCampaignDto | null>(null)
  const [feeCampaignArchiveTarget, setFeeCampaignArchiveTarget] = useState<FeeCampaignDto | null>(null)
  const [feeCampaignArchiveReason, setFeeCampaignArchiveReason] = useState('')
  const [feeCampaignCloseTarget, setFeeCampaignCloseTarget] = useState<FeeCampaignDto | null>(null)
  const [feeCampaignClosureComment, setFeeCampaignClosureComment] = useState('')
  const [feeCampaignRestoreTarget, setFeeCampaignRestoreTarget] = useState<FeeCampaignDto | null>(null)
  const [feeCampaignActionMessage, setFeeCampaignActionMessage] = useState<string | null>(null)
  const [chargeServiceEditTarget, setChargeServiceEditTarget] = useState<ChargeServiceSettingDto | null>(null)
  const [chargeServiceTariffSchedule, setChargeServiceTariffSchedule] = useState<ChargeServiceTariffPeriodDto[] | null>(null)
  const [chargeServiceTariffScheduleLoading, setChargeServiceTariffScheduleLoading] = useState(false)
  const [chargeServiceArchiveTarget, setChargeServiceArchiveTarget] = useState<ChargeServiceSettingDto | null>(null)
  const [chargeServiceArchiveReason, setChargeServiceArchiveReason] = useState('')
  const [chargeServiceRestoreTarget, setChargeServiceRestoreTarget] = useState<ChargeServiceSettingDto | null>(null)
  const [thresholdDeleteTarget, setThresholdDeleteTarget] = useState<ContractorTariffRow | null>(null)
  const [thresholdDeleteReason, setThresholdDeleteReason] = useState('')
  const [thresholdCreateOpen, setThresholdCreateOpen] = useState(false)
  const [thresholdCreateTarget, setThresholdCreateTarget] = useState<ContractorTariffRow | null>(null)
  const [thresholdCreateUpperBound, setThresholdCreateUpperBound] = useState('')
  const [thresholdCreateRate, setThresholdCreateRate] = useState('')
  const [thresholdCreateError, setThresholdCreateError] = useState<string | null>(null)
  const [thresholdRangeErrors, setThresholdRangeErrors] = useState<Record<string, string>>({})
  const [oneTimeRows, setOneTimeRows] = useState<ContractorOneTimeRow[]>([])
  const [oneTimePageNumber, setOneTimePageNumber] = useState(1)
  const [oneTimePageSize, setOneTimePageSize] = useState(10)
  const [tariffDrafts, setTariffDrafts] = useState<Record<string, ContractorTariffDraft>>({})
  const [oneTimeDrafts, setOneTimeDrafts] = useState<Record<string, Partial<ContractorOneTimeRow>>>({})
  const [pendingChange, setPendingChange] = useState<TariffPrototypePendingChange | null>(null)
  const [tariffDateErrors, setTariffDateErrors] = useState<Record<string, string>>({})
  const [tariffPersistenceError, setTariffPersistenceError] = useState<string | null>(null)
  const [tariffReloadRevision, setTariffReloadRevision] = useState(0)
  const [tariffsLoading, setTariffsLoading] = useState(true)
  const [oneTimeLoading, setOneTimeLoading] = useState(true)
  const [feeCampaignsLoading, setFeeCampaignsLoading] = useState(true)
  const [tariffReferencesLoading, setTariffReferencesLoading] = useState(false)
  const [feeCampaignGarageOptionsLoading, setFeeCampaignGarageOptionsLoading] = useState(false)
  const [tariffSavingRowId, setTariffSavingRowId] = useState<string | null>(null)
  const [tableColumns, setTableColumns] = useState([false, false])
  const [tariffPanelsWidth, setTariffPanelsWidthState] = useState(defaultTariffPanelsSplitPercent)
  const [tariffPanelsLayoutError, setTariffPanelsLayoutError] = useState<string | null>(null)
  const tariffPanelsGridRef = useRef<HTMLDivElement>(null)
  const tariffPanelsWidthRef = useRef(defaultTariffPanelsSplitPercent)
  const [oneTimeSavingRowId, setOneTimeSavingRowId] = useState<string | null>(null)
  const [oneTimeDeleteTarget, setOneTimeDeleteTarget] = useState<ContractorOneTimeRow | null>(null)
  const [oneTimeDeleteReason, setOneTimeDeleteReason] = useState('')
  const [oneTimeRestoreTarget, setOneTimeRestoreTarget] = useState<ContractorOneTimeRow | null>(null)
  const [oneTimeContextMenu, setOneTimeContextMenu] = useState<{ row: ContractorOneTimeRow; x: number; y: number } | null>(null)
  const [oneTimeActionMessage, setOneTimeActionMessage] = useState<string | null>(null)
  const canManageTariffs = hasPermission(auth, permissions.tariffsManage)

  useEffect(() => {
    let ignore = false
    const controller = new AbortController()

    async function loadTariffsAndServices() {
      setTariffPersistenceError(null)
      setTariffsLoading(true)
      try {
        const [loadedTariffs, loadedChargeServices, loadedSalaryFund] = await Promise.all([
          dictionaryClient.getTariffs(auth.accessToken, undefined, dictionaryScreenRequestLimit, false, controller.signal),
          dictionaryClient.getChargeServiceSettings(auth.accessToken, undefined, dictionaryScreenRequestLimit, true, undefined, undefined, controller.signal),
          dictionaryClient.getSalaryFund(auth.accessToken, controller.signal),
        ])
        if (!ignore) {
          const mergedRows = createTariffRowsFromBackend(loadedTariffs, loadedChargeServices, loadedSalaryFund)
          setBackendTariffs(loadedTariffs)
          setBackendChargeServices(loadedChargeServices)
          setTariffRows(mergedRows)
          setTariffDrafts(createEditableDrafts(mergedRows))
        }
      } catch (caught) {
        if (!ignore) {
          setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось загрузить тарифы и услуги.')
        }
      } finally {
        if (!ignore) {
          setTariffsLoading(false)
        }
      }
    }

    async function loadIrregularPayments() {
      setOneTimeLoading(true)
      try {
        const loadedIrregularPayments = await dictionaryClient.getIrregularPayments(auth.accessToken, undefined, dictionaryScreenRequestLimit, true, controller.signal)
        if (!ignore) {
          const mergedOneTimeRows = mergeIrregularPaymentsIntoPrototypeRows([], loadedIrregularPayments, true)
          setOneTimeRows(mergedOneTimeRows)
          setOneTimeDrafts(createOneTimeEditableDrafts(mergedOneTimeRows))
        }
      } catch (caught) {
        if (!ignore) {
          setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось загрузить нерегулярные платежи.')
        }
      } finally {
        if (!ignore) {
          setOneTimeLoading(false)
        }
      }
    }

    async function loadFeeCampaigns() {
      const mutationVersionAtStart = feeCampaignMutationVersionRef.current
      setFeeCampaignsLoading(true)
      try {
        const loadedFeeCampaigns = await dictionaryClient.getFeeCampaigns(auth.accessToken, undefined, dictionaryScreenRequestLimit, true, controller.signal)
        if (!ignore) {
          if (feeCampaignMutationVersionRef.current === mutationVersionAtStart) {
            setFeeCampaigns(loadedFeeCampaigns)
          } else {
            setFeeCampaigns((currentCampaigns) => mergeFeeCampaignSnapshots(currentCampaigns, loadedFeeCampaigns))
          }
        }
      } catch (caught) {
        if (!ignore && feeCampaignMutationVersionRef.current === mutationVersionAtStart) {
          setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось загрузить объявленные сборы.')
        }
      } finally {
        if (!ignore) {
          setFeeCampaignsLoading(false)
        }
      }
    }

    void loadTariffsAndServices()
    void loadIrregularPayments()
    void loadFeeCampaigns()

    return () => {
      ignore = true
      controller.abort()
      const tariffReferencesController = tariffReferencesControllerRef.current
      tariffReferencesControllerRef.current = null
      tariffReferencesRequestRef.current = null
      tariffReferencesController?.abort()
    }
  }, [auth.accessToken, dictionaryClient, fundsClient, tariffReloadRevision])

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

  function ensureTariffReferences() {
    if (tariffReferencesLoadedRef.current) return Promise.resolve(true)
    if (tariffReferencesRequestRef.current) return tariffReferencesRequestRef.current

    const controller = new AbortController()
    tariffReferencesControllerRef.current = controller
    tariffReferencesFailedRef.current = false
    setTariffReferencesLoading(true)
    const request = Promise.all([
      dictionaryClient.getIncomeTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit, false, controller.signal),
      dictionaryClient.getMeasurementUnitsPage(auth.accessToken, undefined, 0, dictionaryScreenRequestLimit, false, controller.signal),
      fundsClient.getFundOptions(auth.accessToken, controller.signal),
    ]).then(([loadedIncomeTypes, loadedMeasurementUnits, loadedFunds]) => {
      if (controller.signal.aborted) return false
      setBackendIncomeTypes(loadedIncomeTypes)
      setBackendMeasurementUnits(loadedMeasurementUnits.items)
      setBackendFunds(loadedFunds)
      tariffReferencesLoadedRef.current = true
      return true
    }).catch((caught: unknown) => {
      if (!controller.signal.aborted) {
        tariffReferencesFailedRef.current = true
        setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось загрузить данные для формы.')
      }
      return false
    }).finally(() => {
      if (tariffReferencesControllerRef.current === controller) {
        tariffReferencesControllerRef.current = null
        tariffReferencesRequestRef.current = null
        setTariffReferencesLoading(false)
      }
    })
    tariffReferencesRequestRef.current = request
    return request
  }

  function closeFeeCampaignCloseDialog() {
    setFeeCampaignCloseTarget(null)
    setFeeCampaignClosureComment('')
  }

  function ensureFeeCampaignGarageOptions() {
    if (feeCampaignGarageOptionsLoadedRef.current) {
      return Promise.resolve(true)
    }

    if (feeCampaignGarageOptionsRequestRef.current) {
      return feeCampaignGarageOptionsRequestRef.current
    }

    setFeeCampaignGarageOptionsLoading(true)
    const garageCountRequest = dictionaryClient.getGaragesPage
      ? dictionaryClient.getGaragesPage(auth.accessToken, undefined, 0, 1)
      : Promise.resolve(null)
    const request = Promise
      .all([
        dictionaryClient.getGarages(auth.accessToken, undefined, dictionaryScreenRequestLimit),
        garageCountRequest,
      ])
      .then(([loadedGarages, garagePage]) => {
        setFeeCampaignGarageOptions(loadedGarages)
        setFeeCampaignActiveGarageCount(garagePage?.totalCount ?? loadedGarages.length)
        feeCampaignGarageOptionsLoadedRef.current = true
        return true
      })
      .catch((caught: unknown) => {
        setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось загрузить гаражи для формы сбора.')
        return false
      })
      .finally(() => {
        feeCampaignGarageOptionsRequestRef.current = null
        setFeeCampaignGarageOptionsLoading(false)
      })

    feeCampaignGarageOptionsRequestRef.current = request
    return request
  }

  async function openFeeCampaignCreateDialog() {
    setTariffPersistenceError(null)
    const [referencesReady, garagesReady] = await Promise.all([ensureTariffReferences(), ensureFeeCampaignGarageOptions()])
    if (referencesReady && garagesReady) {
      setModal('fee')
    }
  }

  async function openFeeCampaignEditDialog(campaign: FeeCampaignDto) {
    setTariffPersistenceError(null)
    const [referencesReady, garagesReady] = await Promise.all([ensureTariffReferences(), ensureFeeCampaignGarageOptions()])
    if (referencesReady && garagesReady) {
      setFeeCampaignEditTarget(campaign)
    }
  }

  async function openServiceCreateDialog() {
    setTariffPersistenceError(null)
    if (await ensureTariffReferences()) setModal('service')
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

  function closeThresholdCreateDialog() {
    setThresholdCreateOpen(false)
    setThresholdCreateTarget(null)
    setThresholdCreateUpperBound('')
    setThresholdCreateRate('')
    setThresholdCreateError(null)
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
      const sourceRow = tariffRows.find((row) => row.id === pendingChange.rowId)
      setTariffDrafts((drafts) => ({
        ...drafts,
        [pendingChange.rowId]: {
          ...drafts[pendingChange.rowId],
          dateDay: sourceRow?.dateDay ?? '',
          dateMonth: sourceRow?.dateMonth ?? '',
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
  useRestoreFocusOnClose(Boolean(feeCampaignCloseTarget))
  const feeCampaignCloseDialogRef = useFocusTrap<HTMLElement>(Boolean(feeCampaignCloseTarget))
  const feeCampaignCloseCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(feeCampaignCloseTarget))
  useRestoreFocusOnClose(Boolean(feeCampaignRestoreTarget))
  const feeCampaignRestoreDialogRef = useFocusTrap<HTMLElement>(Boolean(feeCampaignRestoreTarget))
  const feeCampaignRestoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(feeCampaignRestoreTarget))
  const tariffArchiveDialogOpen = Boolean(chargeServiceArchiveTarget)
  useRestoreFocusOnClose(tariffArchiveDialogOpen)
  const chargeServiceArchiveDialogRef = useFocusTrap<HTMLElement>(tariffArchiveDialogOpen)
  const chargeServiceArchiveCancelRef = useFocusOnOpen<HTMLButtonElement>(tariffArchiveDialogOpen)
  useRestoreFocusOnClose(Boolean(chargeServiceRestoreTarget))
  const chargeServiceRestoreDialogRef = useFocusTrap<HTMLElement>(Boolean(chargeServiceRestoreTarget))
  const chargeServiceRestoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(chargeServiceRestoreTarget))
  useRestoreFocusOnClose(Boolean(thresholdDeleteTarget))
  const thresholdDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(thresholdDeleteTarget))
  const thresholdDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(thresholdDeleteTarget))
  useRestoreFocusOnClose(thresholdCreateOpen)
  const thresholdCreateDialogRef = useFocusTrap<HTMLElement>(thresholdCreateOpen)
  const thresholdCreateCancelRef = useFocusOnOpen<HTMLButtonElement>(thresholdCreateOpen)
  useEscapeKey(Boolean(pendingChange), () => cancelPendingChange())
  useEscapeKey(Boolean(oneTimeDeleteTarget), () => closeOneTimeDeleteDialog())
  useEscapeKey(Boolean(oneTimeRestoreTarget), () => closeOneTimeRestoreDialog())
  useEscapeKey(Boolean(feeCampaignArchiveTarget), () => closeFeeCampaignArchiveDialog())
  useEscapeKey(Boolean(feeCampaignCloseTarget), () => closeFeeCampaignCloseDialog())
  useEscapeKey(Boolean(feeCampaignRestoreTarget), () => closeFeeCampaignRestoreDialog())
  useEscapeKey(tariffArchiveDialogOpen, () => closeChargeServiceArchiveDialog())
  useEscapeKey(Boolean(chargeServiceRestoreTarget), () => closeChargeServiceRestoreDialog())
  useEscapeKey(Boolean(thresholdDeleteTarget), () => closeThresholdDeleteDialog())
  useEscapeKey(thresholdCreateOpen, () => closeThresholdCreateDialog())
  useEscapeKey(Boolean(oneTimeContextMenu), () => setOneTimeContextMenu(null))

  useEffect(() => {
    let ignore = false
    const controller = new AbortController()
    tariffReferencesLoadedRef.current = false
    tariffReferencesFailedRef.current = false
    tariffReferencesRequestRef.current = null
    settingsClient.getPaymentDisplaySettings(auth.accessToken, controller.signal)
      .then((settings) => {
        if (ignore) return
        setTableColumns([settings.showPeriodicityColumn, settings.showAccrualMonthColumn])
      })
      .catch(() => undefined)
    return () => {
      ignore = true
      controller.abort()
    }
  }, [auth.accessToken, settingsClient])

  useEffect(() => {
    let ignore = false
    const controller = new AbortController()
    settingsClient.getTariffPanelsLayout(auth.accessToken, controller.signal)
      .then((layout) => {
        if (ignore) return
        const width = layout.irregularPaymentsWidthPercent
        tariffPanelsWidthRef.current = width
        setTariffPanelsWidthState(width)
      })
      .catch(() => undefined)
    return () => {
      ignore = true
      controller.abort()
    }
  }, [auth.accessToken, settingsClient])

  function setTariffPanelsWidth(width: number) {
    const normalizedWidth = Math.min(maximumTariffPanelsSplitPercent, Math.max(minimumTariffPanelsSplitPercent, Math.round(width)))
    tariffPanelsWidthRef.current = normalizedWidth
    setTariffPanelsWidthState(normalizedWidth)
  }

  function setTariffPanelsWidthFromPointer(clientX: number) {
    const bounds = tariffPanelsGridRef.current?.getBoundingClientRect()
    if (!bounds?.width) return
    setTariffPanelsWidth(((clientX - bounds.left) / bounds.width) * 100)
  }

  async function persistTariffPanelsWidth() {
    setTariffPanelsLayoutError(null)
    try {
      await settingsClient.updateTariffPanelsLayout(auth.accessToken, {
        irregularPaymentsWidthPercent: tariffPanelsWidthRef.current,
      })
    } catch {
      setTariffPanelsLayoutError('Не удалось сохранить ширину таблиц.')
    }
  }

  function startTariffPanelsResize(event: PointerEvent<HTMLDivElement>) {
    event.currentTarget.setPointerCapture(event.pointerId)
    setTariffPanelsWidthFromPointer(event.clientX)
  }

  function moveTariffPanelsResize(event: PointerEvent<HTMLDivElement>) {
    if (event.buttons !== 1) return
    setTariffPanelsWidthFromPointer(event.clientX)
  }

  function finishTariffPanelsResize(event: PointerEvent<HTMLDivElement>) {
    event.currentTarget.releasePointerCapture(event.pointerId)
    void persistTariffPanelsWidth()
  }

  function resizeTariffPanelsWithKeyboard(event: KeyboardEvent<HTMLDivElement>) {
    const delta = event.key === 'ArrowLeft' ? -1 : event.key === 'ArrowRight' ? 1 : 0
    if (!delta) return
    event.preventDefault()
    setTariffPanelsWidth(tariffPanelsWidthRef.current + delta)
    void persistTariffPanelsWidth()
  }

  function buildChargeServiceRequest(setting: ChargeServiceSettingDto, nextRows: ContractorTariffRow[]): UpsertChargeServiceSettingRequest {
    const relatedRows = nextRows.filter((item) => item.backendServiceSettingId === setting.id)
    const mainRow = relatedRows.find((item) => item.serviceSettingKind === 'main')
    const periodicityRow = relatedRows.find((item) => item.serviceSettingKind === 'periodicity')
    const startRow = relatedRows.find((item) => item.serviceSettingKind === 'start-date')
    const dueRow = relatedRows.find((item) => item.serviceSettingKind === 'due-date')
    const overdueRow = relatedRows.find((item) => item.serviceSettingKind === 'overdue-days')
    const isRegular = setting.isRegular || Boolean(startRow || dueRow || overdueRow)
    const dueDay = dueRow?.dateDay ? Number(dueRow.dateDay) : setting.paymentDueDay
    const periodicityMonths = Number(normalizeRegularServicePeriodicity(periodicityRow?.amount ?? setting.periodicityMonths))
    const dueMonth = periodicityMonths === 1
      ? null
      : (dueRow?.dateMonth ? getContractorTariffMonthNumber(dueRow.dateMonth) : setting.paymentDueMonth)
    const startMonth = periodicityMonths === 1
      ? setting.accrualStartMonth ?? 1
      : (startRow?.dateMonth ? getContractorTariffMonthNumber(startRow.dateMonth) : setting.accrualStartMonth)
    const overdueGraceDays = parsePrototypeAmount(overdueRow?.amount ?? '') ?? setting.overdueGraceDays
    const isMetered = mainRow?.byMeter ?? setting.isMetered
    const hasTieredTariff = isMetered ? (mainRow?.tiered ?? setting.hasTieredTariff) : false
    const linkedTariffId = mainRow?.backendTariffId ?? setting.tariffId
    const linkedTariff = linkedTariffId ? backendTariffs.find((tariff) => tariff.id === linkedTariffId) : null
    const unitName = mainRow?.unit
      ?? setting.unitName
      ?? (linkedTariff ? getTariffCalculationUnitName(linkedTariff.calculationBase) : null)

    return {
      name: (mainRow?.category ?? setting.name).trim() || setting.name,
      isRegular,
      periodicityMonths: isRegular ? periodicityMonths : null,
      accrualStartMonth: isRegular ? startMonth ?? 1 : null,
      paymentDueDay: isRegular ? dueDay ?? 1 : null,
      paymentDueMonth: isRegular ? dueMonth : null,
      overdueGraceDays: Math.trunc(overdueGraceDays),
      isMetered,
      hasTieredTariff,
      unitName: unitName?.trim() || null,
      incomeTypeId: isRegular ? setting.incomeTypeId ?? null : null,
      tariffId: isRegular ? linkedTariffId ?? null : null,
      version: setting.version,
    }
  }

  function applyTariffRows(nextTariffs: TariffDto[], nextSettings = backendChargeServices) {
    const nextRows = [
      ...createTariffRowsFromBackend(nextTariffs, nextSettings),
      ...tariffRows.filter((row) => row.category === salaryFundCategory),
    ]
    setBackendTariffs(nextTariffs)
    setBackendChargeServices(nextSettings)
    setTariffRows(nextRows)
    setTariffDrafts(createEditableDrafts(nextRows))
  }

  function applySavedServiceTariff(saved: { service: ChargeServiceSettingDto, tariff: TariffDto }) {
    applyTariffRows(
      [...backendTariffs.filter((tariff) => tariff.id !== saved.tariff.id), saved.tariff],
      [...backendChargeServices.filter((setting) => setting.id !== saved.service.id), saved.service],
    )
  }

  async function persistServiceSettingRow(row: ContractorTariffRow, nextRows: ContractorTariffRow[]) {
    if (!canManageTariffs || row.isDeleted || !row.backendServiceSettingId) {
      return false
    }

    const serviceSetting = backendChargeServices.find((setting) => setting.id === row.backendServiceSettingId)
    if (!serviceSetting) {
      return false
    }

    setTariffSavingRowId(row.id)
    setTariffPersistenceError(null)
    try {
      const request = buildChargeServiceRequest(serviceSetting, nextRows)
      const savedSetting = await dictionaryClient.updateChargeServiceSetting(auth.accessToken, serviceSetting.id, request)
      const nextSettings = backendChargeServices.map((setting) => (setting.id === savedSetting.id ? savedSetting : setting))
      applyTariffRows(backendTariffs, nextSettings)
      return true
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось сохранить настройку услуги.')
      return false
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function persistServiceTariffMode(
    row: ContractorTariffRow,
    nextMetered: boolean,
    nextTiered: boolean,
  ) {
    if (!canManageTariffs || row.isDeleted || !row.backendServiceSettingId) {
      return false
    }

    const serviceSetting = backendChargeServices.find((setting) => setting.id === row.backendServiceSettingId)
    const sourceTariffId = row.backendTariffId ?? serviceSetting?.tariffId
    const sourceTariff = sourceTariffId ? backendTariffs.find((tariff) => tariff.id === sourceTariffId) : null
    if (!serviceSetting || !sourceTariff) {
      setTariffPersistenceError('Не удалось определить действующий тариф услуги.')
      return false
    }

    const targetCalculationBase = nextMetered
      ? sourceTariff.calculationBase === 'meter_water' || sourceTariff.calculationBase === 'meter_electricity'
        ? sourceTariff.calculationBase
        : 'meter_electricity'
      : sourceTariff.calculationBase === 'people'
        ? 'people'
        : 'fixed'
    if (!targetCalculationBase) {
      setTariffPersistenceError('Для расчета по счетчику выберите вид поступления «Вода» или «Электроэнергия».')
      return false
    }

    const tariffMode = nextTiered ? 'metered_tiered' : nextMetered ? 'metered' : 'regular'
    const targetUnit = row.unit?.trim()
      || serviceSetting.unitName?.trim()
      || getTariffCalculationUnitName(targetCalculationBase)
    const nextRows = tariffRows.map((currentRow) => currentRow.id === row.id
      ? {
        ...currentRow,
        byMeter: nextMetered,
        tiered: nextTiered,
        calculationBase: targetCalculationBase,
        unit: targetUnit,
      }
      : currentRow)
    const rate = parseTariffAmount(row.amount ?? '') ?? sourceTariff.rate
    const electricityTiers = nextTiered
      ? getElectricityTariffTiers(sourceTariff).map((tier) => ({
        id: tier.id,
        name: tier.name,
        upperBound: tier.upperBound ?? undefined,
        rate: tier.rate,
      }))
      : null

    setTariffSavingRowId(row.id)
    setTariffPersistenceError(null)
    try {
      const serviceRequest = buildChargeServiceRequest(serviceSetting, nextRows)
      const saved = await dictionaryClient.updateChargeServiceWithTariff(auth.accessToken, serviceSetting.id, {
        service: {
          ...serviceRequest,
          tariffId: sourceTariff.id,
          isMetered: nextMetered,
          hasTieredTariff: nextTiered,
          unitName: targetUnit,
        },
        rate,
        tariffMode,
        // A mode switch is a new tariff version. Reusing the source version date
        // would rewrite the calculation rules for already configured periods.
        effectiveFrom: getLocalDateInputValue(),
        electricityTiers: electricityTiers && electricityTiers.length >= 2 ? electricityTiers : null,
        changeReason: 'Смена режима тарифа в таблице услуг.',
        calculationBase: targetCalculationBase,
        tariffVersion: sourceTariff.version,
      })
      applySavedServiceTariff(saved)
      return true
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось сменить режим тарифа.')
      return false
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function persistTariffRow(row: ContractorTariffRow, nextRows: ContractorTariffRow[], electricityTierChangeReason?: string) {
    if (row.backendServiceSettingId && !row.threshold && (row.serviceSettingKind !== 'main' || !row.backendTariffId)) {
      await persistServiceSettingRow(row, nextRows)
      return true
    }

    if (!canManageTariffs || !row.calculationBase) {
      return false
    }

    const targetRow = nextRows.find((item) => item.id === row.id) ?? row
    const backendTariff = targetRow.backendTariffId
      ? backendTariffs.find((tariff) => tariff.id === targetRow.backendTariffId)
      : findTariffForPrototypeRow(backendTariffs, targetRow)
    // An inline edit is a correction from today, not a rewrite of the tariff
    // version whose start date happens to be displayed in the loaded DTO.
    // The backend inserts/replaces the version at this date and keeps the
    // preceding and already scheduled future periods intact.
    const effectiveFrom = getInlineTariffChangeEffectiveFrom(backendTariff?.effectiveFrom)
    const amount = parseTariffAmount(targetRow.amount ?? '')
    if (amount == null) {
      return false
    }

    let request: UpsertTariffRequest
    if (targetRow.threshold && targetRow.calculationBase?.startsWith('meter_')) {
      const normalizedRows = normalizeElectricityTierNames(nextRows)
      const electricityRows = getElectricityThresholdRows(normalizedRows, targetRow)
      const firstRow = electricityRows[0] ?? targetRow
      const firstRate = parseTariffAmount(firstRow.amount ?? '')
      const electricityTiers = electricityRows.map((tierRow) => ({
        id: tierRow.electricityTierId,
        name: tierRow.title,
        upperBound: tierRow.electricityUpperBound ?? undefined,
        rate: parseTariffAmount(tierRow.amount ?? '') ?? 0,
      }))
      request = {
        name: backendTariff?.name ?? targetRow.category,
        calculationBase: targetRow.calculationBase,
        rate: firstRate ?? amount,
        effectiveFrom,
        comment: backendTariff?.comment ?? '',
        version: backendTariff?.version,
        electricityTiers,
        electricityTierChangeReason,
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

    if (backendTariff) {
      request = { ...request, version: backendTariff.version }
    }

    setTariffSavingRowId(targetRow.id)
    setTariffPersistenceError(null)
    try {
      const linkedServiceSettingId = targetRow.backendServiceSettingId
        ?? nextRows.find((candidate) => (
          candidate.backendTariffId === backendTariff?.id
          && Boolean(candidate.backendServiceSettingId)
        ))?.backendServiceSettingId
      const linkedSetting = linkedServiceSettingId
        ? backendChargeServices.find((setting) => setting.id === linkedServiceSettingId)
        : null
      if (linkedSetting && backendTariff) {
        const tariffMode = linkedSetting.hasTieredTariff ? 'metered_tiered' : linkedSetting.isMetered ? 'metered' : 'regular'
        const saved = await dictionaryClient.updateChargeServiceWithTariff(auth.accessToken, linkedSetting.id, {
          service: {
            ...buildChargeServiceRequest(linkedSetting, nextRows),
            tariffId: backendTariff.id,
          },
          rate: request.rate,
          tariffMode,
          effectiveFrom: request.effectiveFrom,
          electricityTiers: request.electricityTiers ?? null,
          changeReason: electricityTierChangeReason,
          calculationBase: request.calculationBase,
          tariffVersion: backendTariff.version,
        })
        applySavedServiceTariff(saved)
        return true
      }

      const savedTariff = backendTariff
        ? await dictionaryClient.updateTariff(auth.accessToken, backendTariff.id, request)
        : await dictionaryClient.createTariff(auth.accessToken, request)
      const nextTariffs = backendTariffs.some((tariff) => tariff.id === savedTariff.id)
        ? backendTariffs.map((tariff) => (tariff.id === savedTariff.id ? savedTariff : tariff))
        : [...backendTariffs, savedTariff]
      applyTariffRows(nextTariffs)
      return true
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось сохранить тариф.')
      return false
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
      setOneTimeDrafts(createOneTimeEditableDrafts(nextRows))
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
      setOneTimeDrafts(createOneTimeEditableDrafts(nextRows))
      return savedPayment
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось изменить статус нерегулярного платежа.'
      setOneTimeActionMessage(message)
      return null
    } finally {
      setOneTimeSavingRowId(null)
    }
  }

  const commitTariffTextChange = async (row: ContractorTariffRow, field: 'title' | 'amount', selectedValue?: string) => {
    const draftValue = (selectedValue ?? tariffDrafts[row.id]?.[field] ?? '').trim()
    const nextValue = normalizeTariffDraftValue(row, field, draftValue)
    const previousValue = row[field] ?? ''

    if (nextValue.trim() === previousValue.trim()) {
      if (draftValue !== nextValue) {
        setTariffDrafts((drafts) => ({
          ...drafts,
          [row.id]: {
            ...drafts[row.id],
            [field]: nextValue,
          },
        }))
      }
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
      await persistTariffRow(row, nextRows)
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

  const commitTariffDateChange = async (row: ContractorTariffRow, selectedMonth?: string) => {
    const draft = tariffDrafts[row.id] ?? { title: row.title, amount: '', dateDay: '', dateMonth: row.dateMonth ?? '' }
    const nextDay = (draft.dateDay ?? '').trim().padStart(2, '0')
    const nextMonth = row.monthlyDue ? '' : (selectedMonth || draft.dateMonth || row.dateMonth || contractorTariffMonthOptions[0].value)
    const dateError = getContractorTariffDateError(nextDay, nextMonth, row.monthlyDue)

    if (dateError) {
      setTariffDateErrors((errors) => ({ ...errors, [row.id]: dateError }))
      return
    }

    setTariffDateErrors((errors) => {
      const nextErrors = { ...errors }
      delete nextErrors[row.id]
      return nextErrors
    })

    const previousValue = row.monthlyDue
      ? `${row.dateDay ?? ''} числа следующего месяца`.trim()
      : formatContractorTariffDate(row.dateDay ?? '', row.dateMonth ?? '')
    const nextValue = row.monthlyDue
      ? `${nextDay} числа следующего месяца`
      : formatContractorTariffDate(nextDay, nextMonth)

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

  const commitTariffBooleanChange = async (row: ContractorTariffRow, field: 'tiered' | 'byMeter', nextValue: boolean) => {
    const previousValue = row[field]

    if (previousValue === nextValue) {
      return
    }

    const previousRows = tariffRows
    const nextMetered = field === 'byMeter' ? nextValue : (nextValue ? true : row.byMeter ?? false)
    const nextTiered = field === 'tiered' ? nextValue : (nextMetered ? row.tiered ?? false : false)
    if (row.backendServiceSettingId) {
      await persistServiceTariffMode(row, nextMetered, nextTiered)
      return
    }

    const nextRows = tariffRows.map((currentRow) => currentRow.id === row.id
      ? { ...currentRow, byMeter: nextMetered, tiered: nextTiered }
      : currentRow)

    setTariffRows(nextRows)
    setTariffDrafts(createEditableDrafts(nextRows))
    const saved = row.backendServiceSettingId
      ? await persistServiceSettingRow(row, nextRows)
      : await persistTariffRow(row, nextRows)
    if (!saved) {
      setTariffRows(previousRows)
      setTariffDrafts(createEditableDrafts(previousRows))
    }
  }

  const commitOneTimeAmountChange = async (row: ContractorOneTimeRow) => {
    const draftValue = (oneTimeDrafts[row.id]?.amount ?? '').trim()
    const nextValue = formatTariffDecimal(draftValue)

    if (nextValue.trim() === row.amount.trim()) {
      if (draftValue !== nextValue) {
        setOneTimeDrafts((drafts) => ({
          ...drafts,
          [row.id]: {
            ...drafts[row.id],
            amount: nextValue,
          },
        }))
      }
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
      setOneTimeDrafts(createOneTimeEditableDrafts(nextRows))
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

  async function createServiceWithTariff(request: CreateChargeServiceWithTariffRequest) {
    if (!canManageTariffs) {
      return
    }

    setTariffSavingRowId('new-service')
    setTariffPersistenceError(null)
    try {
      const created = await dictionaryClient.createChargeServiceWithTariff(auth.accessToken, request)
      applySavedServiceTariff(created)
      // The backend creates an internal income type together with a new service.
      // Refresh references so the edit dialog resolves the selected fund at once.
      try {
        setBackendIncomeTypes(await dictionaryClient.getIncomeTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit))
      } catch {
        // The service is already saved; a later panel reload will retry references.
      }
      setModal(null)
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось добавить услугу.')
      throw caught
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function updateServiceSettingWithTariff(request: UpdateChargeServiceWithTariffRequest) {
    if (!canManageTariffs || !chargeServiceEditTarget) {
      return
    }

    setTariffSavingRowId(`charge-service-${chargeServiceEditTarget.id}`)
    setTariffPersistenceError(null)
    try {
      const currentTariff = backendTariffs.find((tariff) => tariff.id === request.service.tariffId)
      const saved = await dictionaryClient.updateChargeServiceWithTariff(auth.accessToken, chargeServiceEditTarget.id, {
        ...request,
        service: { ...request.service, version: chargeServiceEditTarget.version },
        tariffVersion: currentTariff?.version,
      })
      applySavedServiceTariff(saved)
      setChargeServiceEditTarget(null)
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось изменить услугу.')
      throw caught
    } finally {
      setTariffSavingRowId(null)
    }
  }

  async function openChargeServiceEditor(setting: ChargeServiceSettingDto) {
    setTariffPersistenceError(null)
    if (!await ensureTariffReferences()) return
    if (setting.incomeTypeId && !backendIncomeTypes.some((incomeType) => incomeType.id === setting.incomeTypeId)) {
      try {
        setBackendIncomeTypes(await dictionaryClient.getIncomeTypes(auth.accessToken, undefined, dictionaryScreenRequestLimit))
      } catch {
        // The editor can still open; saving will show the backend validation if the link is no longer available.
      }
    }
    setChargeServiceEditTarget(setting)
    const currentTariff = backendTariffs.find((tariff) => tariff.id === setting.tariffId)
    if (!dictionaryClient.getChargeServiceTariffSchedule) {
      setChargeServiceTariffSchedule(currentTariff ? [{
        tariffId: currentTariff.id,
        effectiveFrom: currentTariff.effectiveFrom,
        effectiveTo: null,
        rate: currentTariff.rate,
        tariffVersion: currentTariff.version,
      }] : [])
      return
    }

    setChargeServiceTariffScheduleLoading(true)
    setChargeServiceTariffSchedule(null)
    try {
      setChargeServiceTariffSchedule(await dictionaryClient.getChargeServiceTariffSchedule(auth.accessToken, setting.id))
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось загрузить тарифную сетку.')
      setChargeServiceTariffSchedule([])
    } finally {
      setChargeServiceTariffScheduleLoading(false)
    }
  }

  async function updateChargeServiceTariffSchedule(request: UpsertChargeServiceTariffScheduleRequest) {
    if (!chargeServiceEditTarget || !dictionaryClient.updateChargeServiceTariffSchedule) {
      throw new Error('Сохранение тарифной сетки недоступно.')
    }

    const saved = await dictionaryClient.updateChargeServiceTariffSchedule(auth.accessToken, chargeServiceEditTarget.id, {
      ...request,
      serviceVersion: chargeServiceEditTarget.version,
    })
    applySavedServiceTariff(saved)
    setChargeServiceEditTarget(saved.service)
    setChargeServiceTariffSchedule(saved.periods)
    return saved.periods
  }

  async function createIrregularService(request: UpsertIrregularPaymentRequest) {
    if (!canManageTariffs) {
      return
    }

    setTariffSavingRowId('new-service')
    setTariffPersistenceError(null)
    setOneTimeActionMessage(null)
    try {
      const savedPayment = await dictionaryClient.createIrregularPayment(auth.accessToken, request)
      const nextRows = mergeIrregularPaymentsIntoPrototypeRows(oneTimeRows, [savedPayment])
      setOneTimeRows(nextRows)
      setOneTimeDrafts(createOneTimeEditableDrafts(nextRows))
      setModal(null)
    } catch (caught) {
      setTariffPersistenceError(caught instanceof Error ? caught.message : 'Не удалось добавить нерегулярную услугу.')
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
      feeCampaignMutationVersionRef.current += 1
      setFeeCampaigns((currentCampaigns) => [savedCampaign, ...currentCampaigns.filter((campaign) => campaign.id !== savedCampaign.id)])
      setFeeCampaignsLoading(false)
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
      const nextRows = createTariffRowsFromBackend(backendTariffs, nextSettings)
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
      const nextRows = createTariffRowsFromBackend(backendTariffs, nextSettings)
      setBackendChargeServices(nextSettings)
      setTariffRows(nextRows)
      setTariffDrafts(createEditableDrafts(nextRows))
      setChargeServiceView('active')
      setTariffPageNumber(1)
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
      feeCampaignMutationVersionRef.current += 1
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
      feeCampaignMutationVersionRef.current += 1
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
      feeCampaignMutationVersionRef.current += 1
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

  async function confirmThresholdDelete() {
    if (!thresholdDeleteTarget || !thresholdDeleteReason.trim()) {
      return
    }

    const currentThresholdRows = getElectricityThresholdRows(tariffRows, thresholdDeleteTarget)
    if (currentThresholdRows.length <= 2) {
      setThresholdRangeErrors((errors) => ({
        ...errors,
        [thresholdDeleteTarget.id]: 'Должен остаться минимум один порог и две тарифные ступени.',
      }))
      closeThresholdDeleteDialog()
      return
    }
    const deletingLastTier = currentThresholdRows.at(-1)?.id === thresholdDeleteTarget.id
    const remainingThresholdRows = currentThresholdRows.filter((row) => row.id !== thresholdDeleteTarget.id)
    const nextLastTierId = remainingThresholdRows.at(-1)?.id
    const nextRows = normalizeElectricityTierNames(tariffRows
      .filter((row) => row.id !== thresholdDeleteTarget.id)
      .map((row) => deletingLastTier && row.id === nextLastTierId
        ? { ...row, electricityUpperBound: null }
        : row))
    const saved = await persistTariffRow(thresholdDeleteTarget, nextRows, thresholdDeleteReason.trim())
    if (saved) {
      closeThresholdDeleteDialog()
    }
  }

  async function commitElectricityThresholdBound(row: ContractorTariffRow) {
    if (row.electricityUpperBound == null) return

    const thresholdRows = getElectricityThresholdRows(tariffRows, row)
    const rowIndex = thresholdRows.findIndex((candidate) => candidate.id === row.id)
    const lowerBound = getElectricityTierLowerBound(tariffRows, row.id)
    const nextUpperBound = parseTariffAmount(tariffDrafts[row.id]?.electricityUpperBoundText ?? '', true)
    const followingUpperBound = thresholdRows[rowIndex + 1]?.electricityUpperBound
    const error = nextUpperBound == null || nextUpperBound < lowerBound
      ? `Значение «До» должно быть не меньше ${formatTariffDecimal(lowerBound)} ${row.unit}.`
      : followingUpperBound != null && nextUpperBound >= followingUpperBound
        ? `Значение «До» должно быть меньше ${formatTariffDecimal(followingUpperBound)} ${row.unit}.`
        : null
    if (error) {
      setThresholdRangeErrors((errors) => ({ ...errors, [row.id]: error }))
      return
    }

    setThresholdRangeErrors((errors) => {
      const nextErrors = { ...errors }
      delete nextErrors[row.id]
      return nextErrors
    })
    if (nextUpperBound === row.electricityUpperBound) return

    const nextRows = normalizeElectricityTierNames(tariffRows.map((currentRow) => currentRow.id === row.id
      ? { ...currentRow, electricityUpperBound: nextUpperBound }
      : currentRow))
    const saved = await persistTariffRow(row, nextRows, 'Изменены числовые границы пороговой тарификации.')
    if (!saved) {
      setTariffDrafts((drafts) => ({
        ...drafts,
        [row.id]: { ...drafts[row.id], electricityUpperBoundText: formatTariffNumber(row.electricityUpperBound) },
      }))
    }
  }

  const addElectricityThreshold = (targetRow: ContractorTariffRow) => {
    const electricityThresholdRows = getElectricityThresholdRows(tariffRows, targetRow)
    if (electricityThresholdRows.length >= 20) {
      setTariffPersistenceError('Можно настроить не более 20 тарифных ступеней.')
      return
    }

    setThresholdCreateTarget(targetRow)
    setThresholdCreateUpperBound('')
    setThresholdCreateRate(electricityThresholdRows.at(-1)?.amount ?? '')
    setThresholdCreateError(null)
    setThresholdCreateOpen(true)
  }

  async function closeFeeCampaign() {
    if (!feeCampaignCloseTarget) {
      return
    }

    setFeeCampaignSavingId(feeCampaignCloseTarget.id)
    setFeeCampaignActionMessage(null)
    try {
      const closedCampaign = await dictionaryClient.closeFeeCampaign(auth.accessToken, feeCampaignCloseTarget.id, {
        comment: feeCampaignClosureComment.trim() || null,
      })
      feeCampaignMutationVersionRef.current += 1
      setFeeCampaigns((currentCampaigns) => currentCampaigns.map((campaign) => (
        campaign.id === closedCampaign.id ? closedCampaign : campaign
      )))
      setFeeCampaignActionMessage(closedCampaign.isClosedEarly
        ? `Сбор «${closedCampaign.name}» закрыт досрочно.`
        : `Сбор «${closedCampaign.name}» закрыт после выполнения плана.`)
      closeFeeCampaignCloseDialog()
    } catch (caught) {
      setFeeCampaignActionMessage(caught instanceof Error ? caught.message : 'Не удалось закрыть сбор.')
    } finally {
      setFeeCampaignSavingId(null)
    }
  }

  async function confirmThresholdCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!thresholdCreateTarget) return
    const electricityThresholdRows = getElectricityThresholdRows(tariffRows, thresholdCreateTarget)
    const lastRow = electricityThresholdRows.at(-1)
    const upperBound = parseTariffAmount(thresholdCreateUpperBound, true)
    const rate = parseTariffAmount(thresholdCreateRate)
    const previousUpperBound = electricityThresholdRows
      .map((row) => row.electricityUpperBound)
      .filter((value): value is number => value != null)
      .at(-1) ?? 0
    const nextLowerBound = electricityThresholdRows.some((row) => row.electricityUpperBound != null)
      ? previousUpperBound + 1
      : 0
    if (upperBound == null || upperBound < nextLowerBound) {
      setThresholdCreateError(`Верхняя граница должна быть не меньше ${formatTariffDecimal(nextLowerBound)} ${thresholdCreateTarget.unit ?? ''}.`)
      return
    }
    if (rate == null) {
      setThresholdCreateError('Укажите ставку больше нуля.')
      return
    }
    if (!lastRow?.backendTariffId) {
      setThresholdCreateError('Тариф не найден. Обновите страницу и повторите действие.')
      return
    }

    const nextRow: ContractorTariffRow = {
      id: `tariff-tier-custom-${lastRow.backendTariffId}-${electricityThresholdRows.length}-${upperBound}`,
      category: thresholdCreateTarget.category,
      title: formatElectricityTierName(nextLowerBound, upperBound),
      threshold: 'x',
      amount: formatTariffDecimal(rate),
      unit: lastRow.unit,
      byMeter: true,
      tiered: true,
      calculationBase: thresholdCreateTarget.calculationBase,
      backendTariffId: lastRow.backendTariffId,
      effectiveFrom: lastRow.effectiveFrom,
      electricityUpperBound: upperBound,
      isCustomThreshold: true,
    }
    const lastRowIndex = tariffRows.findIndex((row) => row.id === lastRow.id)
    const nextRows = normalizeElectricityTierNames([
      ...tariffRows.slice(0, lastRowIndex),
      nextRow,
      ...tariffRows.slice(lastRowIndex),
    ])
    const saved = await persistTariffRow(nextRow, nextRows, 'Добавлен числовой диапазон пороговой тарификации.')
    if (saved) {
      closeThresholdCreateDialog()
    }
  }

  const tieredTariffIds = new Set(tariffRows
    .filter((row) => row.calculationBase?.startsWith('meter_') && Boolean(row.group) && row.tiered && row.backendTariffId)
    .map((row) => row.backendTariffId!))
  const archivedServiceCount = backendChargeServices.filter((setting) => setting.isArchived).length
  const visibleTariffRows = tariffRows.filter((row) => (
    row.serviceSettingKind !== 'overdue-days'
    && row.serviceSettingKind !== 'due-date'
    && row.serviceSettingKind !== 'start-date'
    && row.serviceSettingKind !== 'periodicity'
    && row.title !== 'Перенос долга в просроченный'
    && (!row.threshold || Boolean(row.group) || Boolean(row.backendTariffId && tieredTariffIds.has(row.backendTariffId)))
    && (chargeServiceView === 'deleted'
      ? Boolean(row.backendServiceSettingId && row.isDeleted)
      : !row.isDeleted)
  ))
  const tariffTableLabel = chargeServiceView === 'deleted' ? 'Удалённые услуги' : 'Тарифы и сборы'
  const tariffPaginationLabel = chargeServiceView === 'deleted' ? 'Пагинация удалённых услуг' : 'Пагинация тарифов и услуг'
  const tariffPageSizeLabel = chargeServiceView === 'deleted' ? 'Количество строк удалённых услуг' : 'Количество строк тарифов и услуг'
  const tariffPage = createClientPage(visibleTariffRows, tariffPageNumber, tariffPageSize)
  const oneTimePage = createClientPage(oneTimeRows, oneTimePageNumber, oneTimePageSize)
  const currentBusinessDate = getLocalDateInputValue()
  const feeCampaignPage = createClientPage(
    [...feeCampaigns].sort((left, right) => getFeeCampaignDisplayRank(left, currentBusinessDate) - getFeeCampaignDisplayRank(right, currentBusinessDate)),
    feeCampaignPageNumber,
    feeCampaignPageSize,
  )

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

  function retryTariffLoading() {
    setTariffPersistenceError(null)
    if (tariffReferencesFailedRef.current) {
      void ensureTariffReferences()
      return
    }
    setTariffReloadRevision((value) => value + 1)
  }

  return (
    <section className="contractors-page tariffs-page" aria-label="Тарифы и сборы">
      <div className="contractors-heading">
        <div>
          <h1>Тарифы и сборы</h1>
          {!canManageTariffs ? <p className="form-hint">Режим просмотра: для изменения тарифов нужно право tariffs.manage.</p> : null}
          {tariffPersistenceError && !modal ? (
            <AsyncErrorState
              message={tariffPersistenceError}
              onRetry={retryTariffLoading}
              retrying={tariffsLoading || oneTimeLoading || feeCampaignsLoading || tariffReferencesLoading}
            />
          ) : tariffPersistenceError ? <FormError>{tariffPersistenceError}</FormError> : null}
        </div>
        <div className="contractors-actions">
          <button
            className="secondary-button create-action-button tariffs-action-button"
            type="button"
            aria-busy={tariffReferencesLoading}
            disabled={!canManageTariffs || tariffReferencesLoading}
            title={!canManageTariffs ? 'Нужно право управления тарифами' : undefined}
            onClick={() => {
              if (canManageTariffs) void openServiceCreateDialog()
            }}
          >
            <FileSpreadsheet size={17} aria-hidden="true" />
            <span>Добавить услугу</span>
          </button>
          <button
            className="primary-button contractors-primary-action create-action-button tariffs-action-button"
            type="button"
            aria-busy={tariffReferencesLoading || feeCampaignGarageOptionsLoading}
            disabled={!canManageTariffs || tariffReferencesLoading || feeCampaignGarageOptionsLoading}
            title={!canManageTariffs ? 'Нужно право управления тарифами' : undefined}
            onClick={() => {
              if (canManageTariffs) {
                void openFeeCampaignCreateDialog()
              }
            }}
          >
            <FileText size={17} aria-hidden="true" />
            <span>Объявить сбор</span>
          </button>
        </div>
      </div>

      <>
        <div className="contractors-prototype-tabs" role="tablist" aria-label="Режимы списка услуг">
          <button
            className={chargeServiceView === 'active' ? 'is-active' : ''}
            type="button"
            role="tab"
            aria-selected={chargeServiceView === 'active'}
            onClick={() => {
              setChargeServiceView('active')
              setTariffPageNumber(1)
            }}
          >
            Действующие услуги
          </button>
          <button
            className={chargeServiceView === 'deleted' ? 'is-active' : ''}
            type="button"
            role="tab"
            aria-selected={chargeServiceView === 'deleted'}
            onClick={() => {
              setChargeServiceView('deleted')
              setTariffPageNumber(1)
            }}
          >
            Удалённые услуги ({archivedServiceCount})
          </button>
        </div>
        <div
          className={`contractors-sheet${tableColumns[0] ? ' tariffs-show-periodicity' : ''}${tableColumns[1] ? ' tariffs-show-month' : ''}`}
          role="table"
          aria-label={tariffTableLabel}
        >
            <div className="contractors-sheet-header" role="row">
              <span role="columnheader">Основание</span>
              <span role="columnheader" aria-label="Единица измерения">Ед.</span>
              <span role="columnheader">Значение / ставка</span>
              {tableColumns[0] ? <span role="columnheader">Периодичность</span> : null}
              {tableColumns[1] ? <span role="columnheader">Месяц начисления</span> : null}
              <span role="columnheader">Оплата до</span>
              <span role="columnheader" aria-label="Перенос долга в просроченный, дней">Просрочка, дн.</span>
              <span role="columnheader">Пороговая тарификация</span>
              <span role="columnheader">По счетчику</span>
              <span className="table-actions-column" role="columnheader">Действия</span>
            </div>
            {tariffsLoading ? <TableLoadingState label="Загружаем тарифы и услуги" /> : null}
            {!tariffsLoading ? tariffPage.items.map((row, pageIndex) => {
              const serviceSetting = row.backendServiceSettingId
                ? backendChargeServices.find((setting) => setting.id === row.backendServiceSettingId) ?? null
                : null
              const isServiceSaving = Boolean(serviceSetting && tariffSavingRowId === `charge-service-${serviceSetting.id}`)
              const isSalaryFundSummary = row.category === salaryFundCategory
              const isRowDisabled = isSalaryFundSummary || row.isDeleted || tariffSavingRowId === row.id || isServiceSaving
              const thresholdRowsForTariff = getElectricityThresholdRows(tariffRows, row)
              const canDeleteThreshold = Boolean(row.threshold && thresholdRowsForTariff.length > 2)
              const isLastThresholdRow = thresholdRowsForTariff.at(-1)?.id === row.id
              const showsElectricityRange = Boolean(row.threshold && row.backendTariffId && tieredTariffIds.has(row.backendTariffId))
              const electricityLowerBound = showsElectricityRange ? getElectricityTierLowerBound(tariffRows, row.id) : 0
              const showsServiceCalculationFlags = !isSalaryFundSummary && (row.serviceSettingKind === 'main' || Boolean(row.group))
              const showsOverdueGracePeriod = !isSalaryFundSummary && (row.serviceSettingKind === 'main' || Boolean(row.group && row.calculationBase))
              const overdueRow = showsOverdueGracePeriod
                ? tariffRows.find((candidate) => (
                    candidate.serviceSettingKind === 'overdue-days'
                    && candidate.backendServiceSettingId === row.backendServiceSettingId
                  ) || (
                    candidate.title === 'Перенос долга в просроченный'
                    && candidate.category === row.category
                  ))
                : null
              const dueDateRow = row.serviceSettingKind === 'main'
                ? tariffRows.find((candidate) => candidate.backendServiceSettingId === row.backendServiceSettingId && candidate.serviceSettingKind === 'due-date')
                : null
              const startDateRow = row.serviceSettingKind === 'main'
                ? tariffRows.find((candidate) => candidate.backendServiceSettingId === row.backendServiceSettingId && candidate.serviceSettingKind === 'start-date')
                : null
              const periodicityRow = row.serviceSettingKind === 'main'
                ? tariffRows.find((candidate) => candidate.backendServiceSettingId === row.backendServiceSettingId && candidate.serviceSettingKind === 'periodicity')
                : null
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
                    {row.group || pageIndex === 0 ? <strong>{row.group ?? row.category}</strong> : null}
                    {showsElectricityRange ? (
                      <div className="tariffs-threshold-range" role="group" aria-label={`${row.category}: диапазон ${row.title}`}>
                        <span>От</span>
                        <input
                          aria-label={`${row.category}: ${row.title}: от`}
                          className="contractors-editable-input tariffs-threshold-range__input"
                          disabled
                          value={formatTariffNumber(electricityLowerBound)}
                        />
                        <span>До</span>
                        {row.electricityUpperBound == null ? (
                          <span className="tariffs-threshold-range__unbounded">без границы</span>
                        ) : (
                          <MeterReadingInput
                            aria-label={`${row.category}: ${row.title}: до`}
                            aria-invalid={Boolean(thresholdRangeErrors[row.id])}
                            className="contractors-editable-input tariffs-threshold-range__input"
                            disabled={!canManageTariffs || isRowDisabled}
                            value={tariffDrafts[row.id]?.electricityUpperBoundText ?? ''}
                            onChange={(event) => {
                              setThresholdRangeErrors((errors) => {
                                const nextErrors = { ...errors }
                                delete nextErrors[row.id]
                                return nextErrors
                              })
                              setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], electricityUpperBoundText: event.target.value } }))
                            }}
                            onBlur={(event) => {
                              if (shouldCommitEditableInputOnBlur(event.currentTarget)) void commitElectricityThresholdBound(row)
                            }}
                            onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitElectricityThresholdBound(row))}
                          />
                        )}
                        <span className="tariffs-threshold-range__unit">{row.unit}</span>
                        {thresholdRangeErrors[row.id] ? <small className="contractors-field-error" role="alert">{thresholdRangeErrors[row.id]}</small> : null}
                      </div>
                    ) : row.serviceSettingKind !== 'main' && row.title !== (row.group ?? row.category) && (
                      <span>{row.title}</span>
                    )}
                  </span>
                  <span role="cell" aria-label={`${row.category}: ${row.title}: единица`}>
                    {row.dateDay === undefined && row.serviceSettingKind !== 'periodicity' ? row.unit : null}
                  </span>
                  <span role="cell" className="contractors-value-cell">
                    {row.dateDay !== undefined ? (
                      <div className="contractors-date-value">
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
                        {!row.monthlyDue ? (
                          <SelectControl
                            aria-label={`${row.category}: ${row.title}: месяц`}
                            className="contractors-editable-select-control--month"
                            disabled={!canManageTariffs || isRowDisabled}
                            value={tariffDrafts[row.id]?.dateMonth ?? row.dateMonth ?? contractorTariffMonthOptions[0].value}
                            options={contractorTariffMonthOptions}
                            onChange={(nextMonth) => {
                              setTariffDateErrors((errors) => {
                                const nextErrors = { ...errors }
                                delete nextErrors[row.id]
                                return nextErrors
                              })
                              setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], dateMonth: nextMonth } }))
                              void commitTariffDateChange(row, nextMonth)
                            }}
                          />
                        ) : <span className="contractors-date-suffix">числа следующего месяца</span>}
                        {tariffDateErrors[row.id] ? <span id={`${row.id}-date-error`} className="contractors-field-error" role="alert">{tariffDateErrors[row.id]}</span> : null}
                      </div>
                    ) : isTariffMoneyAmount(row) ? (
                      <MoneyTextInput
                        id={`tariff-value-${row.id}`}
                        aria-label={`${row.category}: ${row.title}: значение`}
                        className="contractors-editable-input"
                        disabled={!canManageTariffs || isRowDisabled}
                        value={tariffDrafts[row.id]?.amount ?? ''}
                        onValueChange={(amount) => setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], amount } }))}
                        onBlur={(event) => {
                          if (shouldCommitEditableInputOnBlur(event.currentTarget)) void commitTariffTextChange(row, 'amount')
                        }}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffTextChange(row, 'amount'))}
                      />
                    ) : (
                      <input
                        aria-label={`${row.category}: ${row.title}: значение`}
                        className="contractors-editable-input"
                        disabled={!canManageTariffs || isRowDisabled}
                        inputMode="numeric"
                        value={tariffDrafts[row.id]?.amount ?? ''}
                        onChange={(event) => setTariffDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], amount: event.target.value } }))}
                        onBlur={(event) => {
                          if (shouldCommitEditableInputOnBlur(event.currentTarget)) void commitTariffTextChange(row, 'amount')
                        }}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffTextChange(row, 'amount'))}
                      />
                    )}
                  </span>
                  {tableColumns[0] ? (
                    <span role="cell" className="tariffs-schedule-cell">
                      {periodicityRow ? (Number(periodicityRow.amount) >= 12 ? 'Ежегодно' : 'Ежемесячно') : null}
                    </span>
                  ) : null}
                  {tableColumns[1] ? (
                    <span role="cell" className="tariffs-schedule-cell">
                      {startDateRow
                        ? contractorTariffMonthOptions.find((option) => option.value === startDateRow.dateMonth)?.label ?? startDateRow.dateMonth
                        : null}
                    </span>
                  ) : null}
                  <span role="cell" className="tariffs-due-date-cell">
                    {dueDateRow ? (
                      <div className="tariffs-cell-stack">
                        <span className="contractors-date-value">
                          <input
                            aria-label={`${row.category}: оплата до: день`}
                            aria-invalid={Boolean(tariffDateErrors[dueDateRow.id])}
                            aria-describedby={tariffDateErrors[dueDateRow.id] ? `${dueDateRow.id}-date-error` : undefined}
                            className="contractors-editable-input contractors-editable-input--day"
                            disabled={!canManageTariffs || isRowDisabled}
                            inputMode="numeric"
                            maxLength={2}
                            value={tariffDrafts[dueDateRow.id]?.dateDay ?? dueDateRow.dateDay ?? ''}
                            onChange={(event) => {
                              setTariffDateErrors((errors) => {
                                const nextErrors = { ...errors }
                                delete nextErrors[dueDateRow.id]
                                return nextErrors
                              })
                              setTariffDrafts((drafts) => ({ ...drafts, [dueDateRow.id]: { ...drafts[dueDateRow.id], dateDay: event.target.value } }))
                            }}
                            onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffDateChange(dueDateRow))}
                          />
                          {!dueDateRow.monthlyDue ? (
                            <SelectControl
                              aria-label={`${row.category}: оплата до: месяц`}
                              className="contractors-editable-select-control--month"
                              disabled={!canManageTariffs || isRowDisabled}
                              value={tariffDrafts[dueDateRow.id]?.dateMonth ?? dueDateRow.dateMonth ?? contractorTariffMonthOptions[0].value}
                              options={contractorTariffMonthOptions}
                              onChange={(nextMonth) => {
                                setTariffDateErrors((errors) => {
                                  const nextErrors = { ...errors }
                                  delete nextErrors[dueDateRow.id]
                                  return nextErrors
                                })
                                setTariffDrafts((drafts) => ({ ...drafts, [dueDateRow.id]: { ...drafts[dueDateRow.id], dateMonth: nextMonth } }))
                                void commitTariffDateChange(dueDateRow, nextMonth)
                              }}
                            />
                          ) : <span className="contractors-date-suffix">следующего месяца</span>}
                        </span>
                        {tariffDateErrors[dueDateRow.id] ? <small id={`${dueDateRow.id}-date-error`} className="contractors-field-error" role="alert">{tariffDateErrors[dueDateRow.id]}</small> : null}
                      </div>
                    ) : null}
                  </span>
                  <span role="cell" className="tariffs-overdue-cell">
                    {overdueRow ? (
                      <input
                        aria-label={`${overdueRow.category}: ${overdueRow.title}: значение`}
                        className="contractors-editable-input contractors-editable-input--overdue-days"
                        disabled={!canManageTariffs || isRowDisabled || tariffSavingRowId === overdueRow.id}
                        inputMode="numeric"
                        value={tariffDrafts[overdueRow.id]?.amount ?? ''}
                        onChange={(event) => setTariffDrafts((drafts) => ({ ...drafts, [overdueRow.id]: { ...drafts[overdueRow.id], amount: event.target.value } }))}
                        onBlur={(event) => {
                          if (shouldCommitEditableInputOnBlur(event.currentTarget)) void commitTariffTextChange(overdueRow, 'amount')
                        }}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitTariffTextChange(overdueRow, 'amount'))}
                      />
                    ) : null}
                  </span>
                  <span role="cell">
                    {showsServiceCalculationFlags ? (
                      <SelectControl
                        aria-label={`${row.category}: пороговая тарификация`}
                        disabled={!canManageTariffs || isRowDisabled}
                        value={row.tiered ? 'Да' : 'Нет'}
                        options={yesNoOptions}
                        onChange={(value) => void commitTariffBooleanChange(row, 'tiered', value === 'Да')}
                      />
                    ) : null}
                  </span>
                  <span role="cell">
                    {showsServiceCalculationFlags ? (
                      <SelectControl
                        aria-label={`${row.category}: по счетчику`}
                        disabled={!canManageTariffs || isRowDisabled}
                        value={row.byMeter ? 'Да' : 'Нет'}
                        options={yesNoOptions}
                        onChange={(value) => void commitTariffBooleanChange(row, 'byMeter', value === 'Да')}
                      />
                    ) : null}
                  </span>
                  <span role="cell" className="tariffs-row-actions-cell table-actions-column">
                    <span className="tariffs-row-actions">
                      {row.serviceSettingKind === 'main' && serviceSetting && !row.isDeleted ? (
                        <>
                          <button
                            className="icon-button tariffs-row-action-button"
                            type="button"
                            aria-label={`Изменить услугу ${serviceSetting.name}`}
                            title="Изменить"
                            aria-busy={tariffReferencesLoading}
                            disabled={!canManageTariffs || isRowDisabled || tariffReferencesLoading}
                            onClick={() => {
                              void openChargeServiceEditor(serviceSetting)
                            }}
                          >
                            <Pencil size={16} aria-hidden="true" />
                          </button>
                          <button
                            className="icon-button tariffs-row-action-button danger-icon-button"
                            type="button"
                            aria-label={`Деактивировать услугу ${serviceSetting.name}`}
                            title="Деактивировать"
                            disabled={!canManageTariffs || isRowDisabled}
                            onClick={() => {
                              setTariffPersistenceError(null)
                              setChargeServiceArchiveReason('')
                              setChargeServiceArchiveTarget(serviceSetting)
                            }}
                          >
                            <PowerOff size={16} aria-hidden="true" />
                          </button>
                        </>
                      ) : null}
                      {row.serviceSettingKind === 'main' && serviceSetting && row.isDeleted ? (
                        <button
                          className="icon-button tariffs-row-action-button"
                          type="button"
                          aria-label={`Вернуть услугу ${serviceSetting.name}`}
                          title="Вернуть"
                          disabled={!canManageTariffs || isServiceSaving}
                          onClick={() => {
                            setTariffPersistenceError(null)
                            setChargeServiceRestoreTarget(serviceSetting)
                          }}
                        >
                          <RotateCcw size={16} aria-hidden="true" />
                        </button>
                      ) : null}
                      {canDeleteThreshold ? (
                        <button
                          className="icon-button tariffs-row-action-button danger-icon-button"
                          type="button"
                          aria-label={`Удалить порог ${row.title}`}
                          title="Удалить порог"
                          disabled={!canManageTariffs || isRowDisabled}
                          onClick={() => {
                            setThresholdDeleteTarget(row)
                            setThresholdDeleteReason('')
                          }}
                        >
                          <Trash2 size={16} aria-hidden="true" />
                        </button>
                      ) : null}
                    </span>
                  </span>
                </div>
                {isLastThresholdRow && Boolean(row.backendTariffId && tieredTariffIds.has(row.backendTariffId)) ? (
                  <div className="contractors-sheet-row contractors-sheet-action-row" role="row">
                    <span role="cell">
                      <button className="link-button create-action-button create-action-button--subtle tariffs-add-threshold-button" type="button" onClick={() => addElectricityThreshold(row)} disabled={!canManageTariffs}>
                        <FileSpreadsheet size={15} aria-hidden="true" />
                        <span>Добавить порог</span>
                      </button>
                    </span>
                    <span role="cell" />
                    <span role="cell" />
                    <span role="cell" />
                    <span role="cell" />
                    <span role="cell" />
                    <span role="cell" />
                    <span role="cell" />
                    <span role="cell" />
                  </div>
                ) : null}
              </Fragment>
              )
            }) : null}
            {visibleTariffRows.length === 0 && !tariffsLoading ? (
              <div className="contractors-sheet-row contractors-sheet-action-row" role="row">
                <span className="contractors-table-empty" role="cell">
                  {chargeServiceView === 'deleted' ? 'Удалённых услуг нет.' : 'Тарифы и услуги пока не настроены.'}
                </span>
              </div>
            ) : null}
          </div>
          <TablePagination
            ariaLabel={tariffPaginationLabel}
            totalCount={tariffPage.totalCount}
            offset={tariffPage.offset}
            limit={tariffPage.limit}
            visibleCount={tariffPage.items.length}
            disabled={tariffsLoading}
            pageSizeLabel={tariffPageSizeLabel}
            onPageChange={setTariffPageNumber}
            onPageSizeChange={(limit) => {
              setTariffPageNumber(1)
              setTariffPageSize(limit)
            }}
          />

          {tariffPanelsLayoutError ? <p className="form-error" role="alert">{tariffPanelsLayoutError}</p> : null}
          <div
            className="contractors-bottom-grid"
            ref={tariffPanelsGridRef}
            style={{ '--tariffs-irregular-width': `${tariffPanelsWidth}%` } as CSSProperties}
          >
            <section className="contractors-mini-table tariffs-summary-card" aria-label="Нерегулярные платежи">
              <div className="contractors-mini-title">Нерегулярные платежи</div>
              {oneTimeActionMessage ? <p className="contractors-action-message" role="alert">{oneTimeActionMessage}</p> : null}
              <div className="contractors-mini-header contractors-mini-header--editable">
                <span>Основание</span>
                <span>Сумма, руб.</span>
              </div>
              {oneTimeLoading ? <TableLoadingState className="table-loading-state--compact" label="Загружаем нерегулярные платежи" /> : null}
              {!oneTimeLoading ? oneTimePage.items.map((row) => (
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
                  <span className="contractors-irregular-name-cell">
                    <span>{row.name}</span>
                    {!row.isActive && !row.isDeleted ? <small className="dictionary-status-pill dictionary-status-pill-archived">Отключён</small> : null}
                  </span>
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
                      <MoneyTextInput
                        aria-label={`Сумма: ${row.name}`}
                        className="contractors-editable-input"
                        disabled={!canManageTariffs || !row.isActive || oneTimeSavingRowId === row.id}
                        value={oneTimeDrafts[row.id]?.amount ?? ''}
                        onValueChange={(amount) => setOneTimeDrafts((drafts) => ({ ...drafts, [row.id]: { ...drafts[row.id], amount } }))}
                        onBlur={(event) => {
                          if (shouldCommitEditableInputOnBlur(event.currentTarget)) void commitOneTimeAmountChange(row)
                        }}
                        onKeyDown={(event) => handleEditableInputKeyDown(event, () => commitOneTimeAmountChange(row))}
                      />
                    )}
                  </span>
                </div>
              )) : null}
              {oneTimeRows.length === 0 && !oneTimeLoading ? <EmptyState>Нерегулярные платежи пока не настроены.</EmptyState> : null}
              <TablePagination
                ariaLabel="Пагинация нерегулярных платежей"
                totalCount={oneTimePage.totalCount}
                offset={oneTimePage.offset}
                limit={oneTimePage.limit}
                visibleCount={oneTimePage.items.length}
                disabled={oneTimeLoading}
                pageSizeLabel="Количество строк нерегулярных платежей"
                onPageChange={setOneTimePageNumber}
                onPageSizeChange={(limit) => {
                  setOneTimePageNumber(1)
                  setOneTimePageSize(limit)
                }}
              />
            </section>

            <div
              className="tariffs-panels-splitter"
              role="separator"
              aria-label="Изменить ширину таблиц"
              aria-orientation="vertical"
              aria-valuemin={minimumTariffPanelsSplitPercent}
              aria-valuemax={maximumTariffPanelsSplitPercent}
              aria-valuenow={tariffPanelsWidth}
              tabIndex={0}
              onPointerDown={startTariffPanelsResize}
              onPointerMove={moveTariffPanelsResize}
              onPointerUp={finishTariffPanelsResize}
              onKeyDown={resizeTariffPanelsWithKeyboard}
            />

            <section className="contractors-mini-table tariffs-summary-card" aria-label="Объявленные сборы">
              <div className="contractors-mini-title">Объявленные сборы</div>
              {feeCampaignActionMessage ? <p className="contractors-action-message" role="alert">{feeCampaignActionMessage}</p> : null}
              <div className="fee-campaign-table-scroll">
                <div className="contractors-mini-header contractors-mini-header--fees">
                  <span>Наименование</span>
                  <span>Фонд</span>
                  <span>Взнос</span>
                  <span>План</span>
                  <span>Собрано</span>
                  <span>Участники</span>
                  <span className="fee-period">Период</span>
                  <span>Действия</span>
                </div>
                {feeCampaignsLoading ? <TableLoadingState className="table-loading-state--compact" label="Загружаем объявленные сборы" /> : null}
                {!feeCampaignsLoading ? feeCampaignPage.items.map((campaign) => {
                  const isPeriodMuted = getFeeCampaignDisplayRank(campaign, currentBusinessDate) > 0
                  return (
                  <div
                    aria-label={`Объявленный сбор ${campaign.name}`}
                    className={[
                      'contractors-mini-row contractors-mini-row--fees',
                      isPeriodMuted ? 'contractors-mini-row--deleted' : '',
                    ].filter(Boolean).join(' ')}
                    key={campaign.id}
                  >
                    <span className="contractors-fee-name-cell">
                      <strong>{campaign.name}</strong>
                      <small>{campaign.incomeTypeName}{campaign.goal ? ` · ${campaign.goal}` : ''}</small>
                      {campaign.closedAtUtc ? (
                        <small>
                          {campaign.isClosedEarly ? 'Закрыт досрочно' : 'Закрыт после выполнения плана'}
                          {campaign.closureComment ? ` · ${campaign.closureComment}` : ''}
                        </small>
                      ) : null}
                    </span>
                    <span className="contractors-fee-fund-cell">{campaign.destinationFundName ?? 'Не назначен'}</span>
                    <span className="contractors-fee-money-cell">{formatTariffDecimal(campaign.contributionAmount)}</span>
                    <span className="contractors-fee-money-cell">{formatTariffDecimal(campaign.targetAmount)}</span>
                    <span className="contractors-fee-money-cell money-income">{formatTariffDecimal(campaign.collectedAmount)}</span>
                    <span className="contractors-fee-participants-cell">{formatFeeCampaignParticipantSummary(campaign)}</span>
                    <span className="fee-period">
                      <time className={isPeriodMuted ? undefined : 'money-income'}>{formatDateOnly(campaign.startsOn)}</time>
                      {campaign.endsOn ? <time className={isPeriodMuted ? undefined : 'money-expense'}>{formatDateOnly(campaign.endsOn)}</time> : null}
                    </span>
                    <span className="contractors-mini-actions">
                    {campaign.isArchived ? (
                      <button className="ghost-button" type="button" disabled={!canManageTariffs || feeCampaignSavingId === campaign.id} onClick={() => setFeeCampaignRestoreTarget(campaign)}>
                        <RotateCcw size={16} />
                        <span>Вернуть</span>
                      </button>
                    ) : campaign.closedAtUtc ? (
                      <>
                        <button className="icon-button" type="button" aria-label={`Изменить закрытый сбор ${campaign.name}`} aria-busy={tariffReferencesLoading || feeCampaignGarageOptionsLoading} disabled={!canManageTariffs || feeCampaignSavingId === campaign.id || tariffReferencesLoading || feeCampaignGarageOptionsLoading} onClick={() => void openFeeCampaignEditDialog(campaign)}>
                          <Pencil size={16} />
                        </button>
                        <button className="icon-button danger-icon-button" type="button" aria-label={`Архивировать закрытый сбор ${campaign.name}`} disabled={!canManageTariffs || feeCampaignSavingId === campaign.id} onClick={() => {
                          setFeeCampaignArchiveTarget(campaign)
                          setFeeCampaignArchiveReason('')
                        }}>
                          <Trash2 size={16} />
                        </button>
                      </>
                    ) : (
                      <>
                        <button className="icon-button" type="button" aria-label={`Изменить сбор ${campaign.name}`} aria-busy={tariffReferencesLoading || feeCampaignGarageOptionsLoading} disabled={!canManageTariffs || feeCampaignSavingId === campaign.id || tariffReferencesLoading || feeCampaignGarageOptionsLoading} onClick={() => void openFeeCampaignEditDialog(campaign)}>
                          <Pencil size={16} />
                        </button>
                        {!campaign.endsOn || campaign.endsOn > currentBusinessDate ? (
                          <button className="icon-button" type="button" aria-label={`Закрыть сбор ${campaign.name}`} disabled={!canManageTariffs || feeCampaignSavingId === campaign.id} onClick={() => {
                            setFeeCampaignCloseTarget(campaign)
                            setFeeCampaignClosureComment('')
                          }}>
                            <CircleCheck size={16} />
                          </button>
                        ) : null}
                        <button className="icon-button danger-icon-button" type="button" aria-label={`Архивировать сбор ${campaign.name}`} disabled={!canManageTariffs || feeCampaignSavingId === campaign.id} onClick={() => {
                          setFeeCampaignArchiveTarget(campaign)
                          setFeeCampaignArchiveReason('')
                        }}>
                          <Trash2 size={16} />
                        </button>
                      </>
                    )}
                    </span>
                  </div>
                  )
                }) : null}
                {feeCampaigns.length === 0 && !feeCampaignsLoading ? <EmptyState>Объявленные сборы пока не настроены.</EmptyState> : null}
              </div>
              <TablePagination
                ariaLabel="Пагинация объявленных сборов"
                totalCount={feeCampaignPage.totalCount}
                offset={feeCampaignPage.offset}
                limit={feeCampaignPage.limit}
                visibleCount={feeCampaignPage.items.length}
                disabled={feeCampaignsLoading}
                pageSizeLabel="Количество строк объявленных сборов"
                onPageChange={setFeeCampaignPageNumber}
                onPageSizeChange={(limit) => {
                  setFeeCampaignPageNumber(1)
                  setFeeCampaignPageSize(limit)
                }}
              />
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
            <div className="context-menu-group" role="group">
              <button type="button" role="menuitem" onClick={() => toggleOneTimeActive(oneTimeContextMenu.row)}>
                {oneTimeContextMenu.row.isActive ? 'Деактивировать' : 'Активировать'}
              </button>
              <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openOneTimeDeleteDialog(oneTimeContextMenu.row)}>
                <span>Удалить</span>
              </button>
            </div>
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
              <button className="secondary-button" type="button" onClick={confirmPendingChange} disabled={oneTimeSavingRowId === pendingChange.rowId || tariffSavingRowId === pendingChange.rowId}>
                <Save size={16} />
                <span>Сохранить</span>
              </button>
              <button ref={changeCancelRef} className="ghost-button" type="button" onClick={cancelPendingChange}>Отмена</button>
            </div>
          </section>
        </div>
      ) : null}

      {thresholdCreateOpen ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeThresholdCreateDialog}>
          <section ref={thresholdCreateDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="threshold-create-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Новый порог</p>
                <h3 id="threshold-create-title">Добавить тарифный порог</h3>
                <p>Новая ступень будет сохранена перед тарифом без верхней границы.</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть добавление порога" onClick={closeThresholdCreateDialog}>
                <X size={18} />
              </button>
            </div>
            <form onSubmit={confirmThresholdCreate}>
              <div className="contractors-service-secondary-grid">
                <FormField label={`От, ${thresholdCreateTarget?.unit ?? 'ед.'}`}>
                  <MeterReadingInput
                    aria-label="Нижняя граница нового порога"
                    disabled
                    value={formatTariffNumber((getElectricityThresholdRows(tariffRows, thresholdCreateTarget ?? undefined).map((row) => row.electricityUpperBound).filter((value): value is number => value != null).at(-1) ?? -1) + 1)}
                  />
                </FormField>
                <FormField label={`До, ${thresholdCreateTarget?.unit ?? 'ед.'}`}>
                  <MeterReadingInput aria-label="Верхняя граница нового порога" value={thresholdCreateUpperBound} onChange={(event) => setThresholdCreateUpperBound(event.target.value)} />
                  <small className="form-field-hint">Единица: {thresholdCreateTarget?.unit ?? 'по выбранному счетчику'}</small>
                </FormField>
                <FormField label="Ставка, руб.">
                  <MoneyTextInput aria-label="Ставка нового порога" value={thresholdCreateRate} onValueChange={setThresholdCreateRate} />
                </FormField>
              </div>
              {thresholdCreateError ? <FormError>{thresholdCreateError}</FormError> : null}
              <div className="detail-dialog-actions contractors-dialog-actions">
                <button ref={thresholdCreateCancelRef} className="ghost-button" type="button" onClick={closeThresholdCreateDialog} disabled={Boolean(tariffSavingRowId)}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={Boolean(tariffSavingRowId)}>
                  <Save size={16} />
                  <span>{tariffSavingRowId ? 'Сохраняем…' : 'Добавить'}</span>
                </button>
              </div>
            </form>
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
              <button className="secondary-button danger-button" type="button" onClick={confirmThresholdDelete} disabled={!thresholdDeleteReason.trim() || Boolean(tariffSavingRowId)}>
                <Trash2 size={16} />
                <span>{tariffSavingRowId ? 'Удаляем…' : 'Удалить'}</span>
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

      {tariffArchiveDialogOpen ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeChargeServiceArchiveDialog}>
          <section ref={chargeServiceArchiveDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="charge-service-archive-title" aria-describedby="charge-service-archive-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Деактивация</p>
                <h3 id="charge-service-archive-title">Деактивировать услугу?</h3>
                <p>{chargeServiceArchiveTarget?.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение деактивации услуги" onClick={closeChargeServiceArchiveDialog} disabled={Boolean(tariffSavingRowId)}>
                <X size={18} aria-hidden="true" />
              </button>
            </div>
            <div className="confirmation-text" id="charge-service-archive-description">
              <p>Новые регулярные начисления по услуге больше не будут создаваться.</p>
              <p>Уже созданные начисления и связанные платежи не удалятся: задолженность можно будет погашать, а операции останутся в отчётах и истории.</p>
              <p>Причина и само действие попадут в историю изменений. После восстановления услуга снова будет участвовать только в будущих начислениях — прошлые периоды автоматически не пересчитаются.</p>
            </div>
            <label className="field-label" htmlFor="charge-service-archive-reason">Причина деактивации</label>
            <textarea
              id="charge-service-archive-reason"
              aria-label="Причина деактивации услуги"
              maxLength={1000}
              value={chargeServiceArchiveReason}
              onChange={(event) => setChargeServiceArchiveReason(event.target.value)}
              placeholder="Например: услуга больше не используется"
              disabled={Boolean(tariffSavingRowId)}
              required
            />
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={chargeServiceArchiveCancelRef} className="ghost-button" type="button" onClick={closeChargeServiceArchiveDialog} disabled={Boolean(tariffSavingRowId)}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={archiveChargeServiceSetting} disabled={!chargeServiceArchiveReason.trim() || Boolean(tariffSavingRowId)}>
                <PowerOff size={16} aria-hidden="true" />
                <span>Деактивировать</span>
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
            <p className="confirmation-text" id="charge-service-restore-description">Услуга снова станет доступной для редактирования и будущих начислений. Уже проведённые начисления и платежи не изменятся, прошлые периоды автоматически не пересчитаются. Действие будет записано в историю изменений.</p>
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

      {feeCampaignCloseTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeFeeCampaignCloseDialog}>
          <section ref={feeCampaignCloseDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="fee-campaign-close-title" aria-describedby="fee-campaign-close-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Завершение сбора</p>
                <h3 id="fee-campaign-close-title">Закрыть сбор?</h3>
                <p>{feeCampaignCloseTarget.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение завершения сбора" onClick={closeFeeCampaignCloseDialog} disabled={feeCampaignSavingId === feeCampaignCloseTarget.id}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="fee-campaign-close-description">После закрытия новые начисления по сбору не создаются. Уже созданные начисления, оплаты и отчёты сохраняются. Если план ещё не оплачен полностью, комментарий обязателен и закрытие будет отмечено как досрочное.</p>
            <FormField label="Комментарий к закрытию" help="Необязательно при полном выполнении плана; обязательно для досрочного закрытия.">
              <textarea
                aria-label="Комментарий к закрытию сбора"
                maxLength={1000}
                value={feeCampaignClosureComment}
                onChange={(event) => setFeeCampaignClosureComment(event.target.value)}
                placeholder="Например: сбор прекращён по решению правления"
                disabled={feeCampaignSavingId === feeCampaignCloseTarget.id}
              />
            </FormField>
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={feeCampaignCloseCancelRef} className="ghost-button" type="button" onClick={closeFeeCampaignCloseDialog} disabled={feeCampaignSavingId === feeCampaignCloseTarget.id}>Отмена</button>
              <button className="secondary-button" type="button" onClick={closeFeeCampaign} disabled={feeCampaignSavingId === feeCampaignCloseTarget.id}>
                <CircleCheck size={16} />
                <span>Закрыть сбор</span>
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
            <p className="confirmation-text" id="fee-campaign-restore-description">{feeCampaignRestoreTarget.closedAtUtc
              ? 'Закрытый сбор снова появится в рабочем списке, но новые начисления по нему останутся запрещены. Действие будет записано в историю изменений.'
              : 'Сбор снова появится как активный и будет доступен для начислений. Действие будет записано в историю изменений.'}</p>
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

      {modal === 'service' ? (
        <AddServicePrototypeDialog
          isSaving={tariffSavingRowId === 'new-service'}
          funds={backendFunds.filter((fund) => fund.allowOperations)}
          incomeTypes={backendIncomeTypes.filter((incomeType) => !incomeType.isArchived)}
          measurementUnits={backendMeasurementUnits.filter((unit) => !unit.isArchived)}
          onClose={() => setModal(null)}
          onCreateWithTariff={createServiceWithTariff}
          onSaveIrregular={createIrregularService}
          tariffs={backendTariffs.filter((tariff) => !tariff.isArchived)}
        />
      ) : null}
      {chargeServiceEditTarget ? (
        <AddServicePrototypeDialog
          key={`${chargeServiceEditTarget.id}-${chargeServiceTariffScheduleLoading ? 'loading' : 'ready'}`}
          initialSetting={chargeServiceEditTarget}
          tariffSchedule={chargeServiceTariffSchedule}
          tariffScheduleLoading={chargeServiceTariffScheduleLoading}
          isSaving={tariffSavingRowId === `charge-service-${chargeServiceEditTarget.id}`}
          funds={backendFunds.filter((fund) => fund.allowOperations)}
          incomeTypes={backendIncomeTypes.filter((incomeType) => !incomeType.isArchived)}
          measurementUnits={backendMeasurementUnits.filter((unit) => !unit.isArchived)}
          onClose={() => setChargeServiceEditTarget(null)}
          onUpdateWithTariff={updateServiceSettingWithTariff}
          onUpdateTariffSchedule={updateChargeServiceTariffSchedule}
          submitLabel="Сохранить изменения"
          tariffs={backendTariffs.filter((tariff) => !tariff.isArchived)}
          title="Изменить услугу"
        />
      ) : null}
      {modal === 'fee' ? (
        <AddFeePrototypeDialog
          activeGarageCount={feeCampaignActiveGarageCount}
          garageOptions={feeCampaignGarageOptions}
          incomeTypes={backendIncomeTypes.filter((incomeType) => !incomeType.isArchived && Boolean(incomeType.destinationFundId))}
          isSaving={feeCampaignSavingId === 'new-fee-campaign'}
          onClose={() => setModal(null)}
          onSave={createFeeCampaign}
        />
      ) : null}
      {feeCampaignEditTarget ? (
        <AddFeePrototypeDialog
          activeGarageCount={feeCampaignActiveGarageCount}
          garageOptions={feeCampaignGarageOptions}
          incomeTypes={backendIncomeTypes.filter((incomeType) => !incomeType.isArchived && Boolean(incomeType.destinationFundId))}
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

export function AddServicePrototypeDialog({
  funds,
  initialSetting,
  isSaving,
  incomeTypes,
  measurementUnits = [],
  onClose,
  onCreateWithTariff,
  onSaveIrregular,
  onSave,
  onUpdateWithTariff,
  onUpdateTariffSchedule,
  regularOnly = false,
  submitLabel = 'Сохранить',
  tariffs,
  tariffSchedule = null,
  tariffScheduleLoading = false,
  title = 'Добавить услугу',
}: {
  initialSetting?: ChargeServiceSettingDto
  isSaving: boolean
  funds: FundOptionDto[]
  incomeTypes: AccountingTypeDto[]
  measurementUnits?: MeasurementUnitDto[]
  onClose: () => void
  onCreateWithTariff?: (request: CreateChargeServiceWithTariffRequest) => Promise<void>
  onSaveIrregular?: (request: UpsertIrregularPaymentRequest) => Promise<void>
  onSave?: (request: UpsertChargeServiceSettingRequest) => Promise<void>
  onUpdateWithTariff?: (request: UpdateChargeServiceWithTariffRequest) => Promise<void>
  onUpdateTariffSchedule?: (request: UpsertChargeServiceTariffScheduleRequest) => Promise<ChargeServiceTariffPeriodDto[]>
  regularOnly?: boolean
  submitLabel?: string
  tariffs: TariffDto[]
  tariffSchedule?: ChargeServiceTariffPeriodDto[] | null
  tariffScheduleLoading?: boolean
  title?: string
}) {
  const initialIncomeTypeId = initialSetting?.incomeTypeId ?? ''
  const initialTariffId = initialSetting?.tariffId ?? ''
  const initialTariff = tariffs.find((tariff) => tariff.id === initialTariffId) ?? null
  const [name, setName] = useState(initialSetting?.name ?? '')
  const [isRegular, setIsRegular] = useState(initialSetting?.isRegular ?? regularOnly)
  const [incomeFundId, setIncomeFundId] = useState(() => (
    incomeTypes.find((incomeType) => incomeType.id === initialIncomeTypeId)?.destinationFundId
      ?? (initialSetting ? '' : funds[0]?.id ?? '')
  ))
  const [calculationBase, setCalculationBase] = useState(initialTariff?.calculationBase ?? 'fixed')
  const [unitName, setUnitName] = useState(
    initialSetting?.unitName?.trim()
      || getTariffCalculationUnitName(initialTariff?.calculationBase ?? 'fixed'),
  )
  const [isByMeter, setIsByMeter] = useState(initialSetting?.isMetered ?? isMeterTariff(initialTariff ?? undefined))
  const [isTiered, setIsTiered] = useState(initialSetting?.hasTieredTariff ?? false)
  const [periodicityMonths, setPeriodicityMonths] = useState(() => normalizeRegularServicePeriodicity(initialSetting?.periodicityMonths ?? 1))
  const [accrualStartMonth, setAccrualStartMonth] = useState(() => getContractorTariffMonthValue(initialSetting?.accrualStartMonth ?? 1))
  const [paymentDueDay, setPaymentDueDay] = useState(String(initialSetting?.paymentDueDay ?? 30))
  const [paymentDueMonth, setPaymentDueMonth] = useState(() => getContractorTariffMonthValue(initialSetting?.paymentDueMonth ?? 7))
  const [overdueGraceDays, setOverdueGraceDays] = useState(String(initialSetting?.overdueGraceDays ?? 30))
  const [cost, setCost] = useState('')
  const [regularRate, setRegularRate] = useState(initialTariff ? formatTariffDecimal(initialTariff.rate) : '')
  const [tariffTiers, setTariffTiers] = useState(() => getElectricityTariffTiers(initialTariff))
  const [tariffEffectiveFrom, setTariffEffectiveFrom] = useState(initialTariff?.effectiveFrom ?? getLocalDateInputValue())
  const [error, setError] = useState<string | null>(null)
  const [scheduleDraft, setScheduleDraft] = useState<Array<ChargeServiceTariffPeriodDto & { key: string; rateText: string }>>(() =>
    (tariffSchedule ?? []).map((period) => ({ ...period, rateText: formatTariffDecimal(period.rate), key: `${period.tariffId}-${period.effectiveFrom ?? 'all'}-${period.effectiveTo ?? 'all'}` })))
  const [scheduleMessage, setScheduleMessage] = useState<string | null>(null)
  const [scheduleSaving, setScheduleSaving] = useState(false)
  const selectedTariff = initialTariff
  const supportedMeterCalculationBase = isMeterTariff(initialTariff ?? undefined)
    ? initialTariff!.calculationBase
    : 'meter_electricity'
  const effectiveCalculationBase = calculationBase
  const canUseTieredTariff = isByMeter
    && (effectiveCalculationBase === 'meter_water' || effectiveCalculationBase === 'meter_electricity')
  const isMonthly = periodicityMonths === '1'
  const canChooseRegularity = !regularOnly && !initialSetting
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useEscapeKey(true, onClose)

  async function saveTariffSchedule() {
    if (!onUpdateTariffSchedule || !initialSetting) {
      return
    }

    if (scheduleDraft.length === 0) {
      setScheduleMessage('Добавьте хотя бы один период тарифа.')
      return
    }

    const ordered = [...scheduleDraft].sort((left, right) => (left.effectiveFrom ?? '').localeCompare(right.effectiveFrom ?? ''))
    for (let index = 0; index < ordered.length; index += 1) {
      const period = ordered[index]
      const parsedRate = parsePrototypeAmount(period.rateText)
      if (parsedRate == null || parsedRate <= 0 || parsedRate > 999999999) {
        setScheduleMessage('Для каждого периода укажите тариф больше нуля.')
        return
      }
      if (period.effectiveFrom && period.effectiveTo && period.effectiveFrom > period.effectiveTo) {
        setScheduleMessage('Конечная дата тарифа не может быть раньше начальной.')
        return
      }
      if (index > 0) {
        const previous = ordered[index - 1]
        if (!previous.effectiveTo || !period.effectiveFrom || period.effectiveFrom <= previous.effectiveTo) {
          setScheduleMessage('Периоды тарифов пересекаются. Исправьте даты.')
          return
        }
      }
    }

    setScheduleSaving(true)
    setScheduleMessage(null)
    try {
      const saved = await onUpdateTariffSchedule({
        periods: ordered.map(({ tariffId, tariffVersion, effectiveFrom, effectiveTo, rateText }) => ({
          tariffId: tariffId || null,
          tariffVersion: tariffVersion || null,
          effectiveFrom,
          effectiveTo,
          rate: parsePrototypeAmount(rateText)!,
        })),
        allowGaps: true,
        changeReason: 'Изменение тарифной сетки в карточке услуги.',
        serviceVersion: initialSetting.version,
      })
      setScheduleDraft(saved.map((period) => ({ ...period, rateText: formatTariffDecimal(period.rate), key: `${period.tariffId}-${period.effectiveFrom ?? 'all'}-${period.effectiveTo ?? 'all'}` })))
      setScheduleMessage('Тарифная сетка сохранена.')
    } catch (caught) {
      setScheduleMessage(caught instanceof Error ? caught.message : 'Не удалось сохранить тарифную сетку.')
    } finally {
      setScheduleSaving(false)
    }
  }

  function changeMeterMode(nextIsMetered: boolean) {
    const nextCalculationBase = nextIsMetered
      ? supportedMeterCalculationBase
      : selectedTariff?.calculationBase === 'people' ? 'people' : 'fixed'
    setIsByMeter(nextIsMetered)
    setIsTiered((currentValue) => nextIsMetered ? currentValue : false)
    setCalculationBase(nextCalculationBase)
    setError(null)
  }

  function changeRegularity(nextIsRegular: boolean) {
    setIsRegular(nextIsRegular)
    if (nextIsRegular && !regularRate && selectedTariff) {
      setRegularRate(formatTariffDecimal(selectedTariff.rate))
    }
    setError(null)
  }

  async function submitService(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const trimmedName = name.trim()
    const parsedPeriodicity = Number(periodicityMonths)
    const parsedDueDay = Number(paymentDueDay)
    const parsedOverdueDays = Number(overdueGraceDays)
    const parsedRegularRate = parsePrototypeAmount(regularRate)
    const effectiveRate = isTiered ? tariffTiers[0]?.rate ?? null : parsedRegularRate
    const dueMonthOption = isMonthly
      ? null
      : contractorTariffMonthOptions.find((month) => month.value === paymentDueMonth)

    if (!trimmedName) {
      setError('Укажите наименование услуги.')
      return
    }

    if (!unitName.trim()) {
      setError('Укажите единицу измерения услуги.')
      return
    }

    if (isRegular) {
      if (!incomeFundId) {
        setError('Выберите фонд поступления услуги.')
        return
      }

      if (effectiveRate == null || effectiveRate <= 0 || effectiveRate > 999999999) {
        setError('Укажите корректный тариф услуги.')
        return
      }

      if (isTiered) {
        if (tariffTiers.length < 2) {
          setError('Для пороговой тарификации укажите минимум один порог и последнюю ступень без верхней границы.')
          return
        }
        for (let index = 0; index < tariffTiers.length - 1; index += 1) {
          const upperBound = tariffTiers[index].upperBound
          const lowerBound = index === 0 ? 0 : (tariffTiers[index - 1].upperBound ?? -1) + 1
          if (upperBound == null || !Number.isFinite(upperBound) || upperBound < lowerBound) {
            setError(`В ступени ${index + 1} укажите верхнюю границу не меньше ${lowerBound}.`)
            return
          }
        }
      }

      if (parsedPeriodicity !== 1 && parsedPeriodicity !== 12) {
        setError('Выберите ежемесячную или ежегодную периодичность.')
        return
      }

      const maxDueDay = dueMonthOption?.maxDay ?? 31
      if (!Number.isInteger(parsedDueDay) || parsedDueDay < 1 || parsedDueDay > maxDueDay) {
        setError(isMonthly
          ? 'Для ежемесячной услуги укажите день оплаты от 1 до 31.'
          : `Для месяца "${dueMonthOption?.label ?? 'не выбран'}" укажите день от 1 до ${maxDueDay}.`)
        return
      }

      if (!Number.isInteger(parsedOverdueDays) || parsedOverdueDays < 0 || parsedOverdueDays > 366) {
        setError('Перенос долга должен быть числом от 0 до 366 дней.')
        return
      }
    } else if (onSaveIrregular) {
      const parsedCost = parsePrototypeAmount(cost)
      if (parsedCost == null) {
        setError('Укажите корректную стоимость нерегулярной услуги.')
        return
      }

      setError(null)
      try {
        await onSaveIrregular({
          name: trimmedName,
          amount: parsedCost,
          isActive: true,
        })
      } catch (caught) {
        setError(caught instanceof Error ? caught.message : 'Не удалось сохранить услугу.')
      }
      return
    }

    setError(null)
    try {
      const serviceRequest: UpsertChargeServiceSettingRequest = {
        name: trimmedName,
        isRegular,
        periodicityMonths: isRegular ? parsedPeriodicity : null,
        accrualStartMonth: isRegular ? (isMonthly ? 1 : getContractorTariffMonthNumber(accrualStartMonth) ?? 1) : null,
        paymentDueDay: isRegular ? parsedDueDay : null,
        paymentDueMonth: isRegular && !isMonthly ? getContractorTariffMonthNumber(paymentDueMonth) ?? 1 : null,
        overdueGraceDays: isRegular ? parsedOverdueDays : 0,
        isMetered: isRegular && isByMeter,
        hasTieredTariff: isRegular && isByMeter && isTiered,
        unitName: unitName.trim() || null,
        incomeTypeId: isRegular ? initialSetting?.incomeTypeId ?? null : null,
        tariffId: isRegular ? initialSetting?.tariffId ?? null : null,
      }
      if (isRegular && !initialSetting && onCreateWithTariff) {
        await onCreateWithTariff({
          service: serviceRequest,
          rate: effectiveRate!,
          effectiveFrom: tariffEffectiveFrom,
          incomeFundId,
          tariffMode: isTiered ? 'metered_tiered' : isByMeter ? 'metered' : 'regular',
          calculationBase: effectiveCalculationBase,
          electricityTiers: isTiered && tariffTiers.length >= 2
            ? tariffTiers.map(({ id, name, upperBound, rate }) => ({
              id: persistedGuidPattern.test(id) ? id : undefined,
              name,
              upperBound: upperBound ?? undefined,
              rate,
            }))
            : null,
        })
      } else if (initialSetting && onUpdateWithTariff) {
        const tariffMode = isTiered ? 'metered_tiered' : isByMeter ? 'metered' : 'regular'
        const modeChanged = initialSetting.isMetered !== isByMeter || initialSetting.hasTieredTariff !== isTiered
        const calculationChanged = selectedTariff?.calculationBase !== effectiveCalculationBase
        const tiersChanged = JSON.stringify(getElectricityTariffTiers(selectedTariff)) !== JSON.stringify(tariffTiers)
        const tariffStructureChanged = modeChanged || calculationChanged || (isTiered && tiersChanged)
        await onUpdateWithTariff({
          service: serviceRequest,
          rate: effectiveRate!,
          effectiveFrom: tariffEffectiveFrom,
          incomeFundId,
          tariffVersion: selectedTariff?.version,
          ...(tariffStructureChanged ? {
            tariffMode,
            electricityTiers: isTiered && tariffTiers.length >= 2
              ? tariffTiers.map(({ id, name, upperBound, rate }) => ({
                id: persistedGuidPattern.test(id) ? id : undefined,
                name,
                upperBound: upperBound ?? undefined,
                rate,
              }))
              : null,
            changeReason: 'Изменение параметров тарифа в карточке услуги.',
            calculationBase: effectiveCalculationBase,
          } : {}),
        })
      } else if (onSave) {
        await onSave(serviceRequest)
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось сохранить услугу.')
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section
        ref={dialogRef}
        className={`detail-dialog contractors-dialog contractors-tariff-dialog contractors-service-dialog ${isRegular ? 'contractors-service-dialog--regular' : 'contractors-service-dialog--compact'}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby="contractor-service-title"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <div className="detail-dialog-header">
          <h3 id="contractor-service-title">{title}</h3>
          <div className="contractors-service-header-actions">
            <button className="icon-button" type="button" aria-label="Закрыть форму услуги" onClick={onClose}>
              <X size={18} />
            </button>
          </div>
        </div>

        <form className={`dictionary-modal-form contractors-modal-form${isRegular ? ` contractors-modal-form--service-edit${isTiered ? ' contractors-modal-form--service-edit-tiered' : ''}` : ''}`} onSubmit={submitService}>
          {error ? <FormError>{error}</FormError> : null}
          {isRegular ? <h4 className="contractors-service-section-title contractors-service-section-title--settings">Настройки услуги</h4> : null}
          <div className="contractors-service-heading-grid contractors-service-heading-grid--name-only">
            <FormField label="Наименование услуги">
              <input aria-label="Наименование услуги" value={name} onChange={(event) => setName(event.target.value)} />
            </FormField>
          </div>
          {isRegular ? (
            <>
              <div className="contractors-service-period-grid contractors-service-period-grid--catalogs">
                <FormField label="Фонд поступления" help="Фонд, куда будут поступать оплаты по услуге.">
                  <SelectControl
                    aria-label="Фонд поступления регулярной услуги"
                    value={incomeFundId}
                    options={[
                      { value: '', label: 'Выберите фонд поступления' },
                      ...funds.map((fund) => ({ value: fund.id, label: fund.name })),
                    ]}
                    onChange={(nextIncomeFundId) => {
                      setIncomeFundId(nextIncomeFundId)
                      setError(null)
                    }}
                  />
                </FormField>
              </div>
              {initialSetting && !isTiered ? (
                <section className="tariff-schedule-editor" aria-labelledby="tariff-schedule-title">
                  <div className="tariff-schedule-heading">
                    <div>
                      <h4 id="tariff-schedule-title">Изменение тарифов по периодам</h4>
                      <p>Пустая дата означает, что тариф действует без ограничения с этой стороны.</p>
                    </div>
                    <button
                      className="secondary-button create-action-button"
                      type="button"
                      aria-label="Добавить период тарифа"
                      disabled={scheduleSaving || scheduleDraft.length === 120}
                      onClick={() => setScheduleDraft((current) => [...current, {
                        key: `new-${Date.now()}`,
                        tariffId: '',
                        tariffVersion: '',
                        effectiveFrom: null,
                        effectiveTo: null,
                        rate: parsePrototypeAmount(regularRate) ?? 1,
                        rateText: regularRate || '1',
                      }])}
                    >
                      <CalendarPlus size={16} aria-hidden="true" />
                      <span>Добавить период</span>
                    </button>
                  </div>
                  {tariffScheduleLoading ? (
                    <div className="tariff-schedule-loading" role="status" aria-live="polite">Загрузка тарифной сетки…</div>
                  ) : (
                    <div className="tariff-schedule-table" role="table" aria-label="Тарифная сетка услуги">
                      <div className="tariff-schedule-row tariff-schedule-row--header" role="row">
                        <span role="columnheader">Начальная дата</span>
                        <span role="columnheader">Конечная дата</span>
                        <span role="columnheader">Тариф</span>
                        <span role="columnheader">Действия</span>
                      </div>
                      {scheduleDraft.map((period, periodIndex) => (
                        <div className="tariff-schedule-row" role="row" key={period.key}>
                          <span role="cell">
                            <LocalizedDatePicker
                              ariaLabel={periodIndex === 0 ? 'Ставка с' : 'Начальная дата тарифа'}
                              mode="date"
                              value={period.effectiveFrom ?? ''}
                              onChange={(value) => {
                                const normalizedValue = value || null
                                if (periodIndex === 0 && normalizedValue) {
                                  setTariffEffectiveFrom(normalizedValue)
                                }
                                setScheduleDraft((current) => current.map((item) => item.key === period.key
                                  ? { ...item, effectiveFrom: normalizedValue }
                                  : item))
                              }}
                            />
                          </span>
                          <span role="cell">
                            <LocalizedDatePicker
                              ariaLabel="Конечная дата тарифа"
                              mode="date"
                              value={period.effectiveTo ?? ''}
                              onChange={(value) => setScheduleDraft((current) => current.map((item) => item.key === period.key
                                ? { ...item, effectiveTo: value || null }
                                : item))}
                            />
                          </span>
                          <span role="cell">
                            {isTiered ? <span className="tariff-schedule-tiered-value">По пороговой сетке</span> : (
                              <MoneyTextInput
                                aria-label="Тариф регулярной услуги"
                                value={period.rateText}
                                onValueChange={(value) => {
                                  setRegularRate(value)
                                  setScheduleDraft((current) => current.map((item) => item.key === period.key
                                    ? { ...item, rateText: value }
                                    : item))
                                }}
                              />
                            )}
                          </span>
                          <span role="cell" className="tariff-schedule-actions">
                            <button
                              className="icon-button danger-icon-button"
                              type="button"
                              aria-label="Удалить период тарифа"
                              disabled={scheduleDraft.length <= 1 || scheduleSaving}
                              onClick={() => setScheduleDraft((current) => current.filter((item) => item.key !== period.key))}
                            ><Trash2 size={16} aria-hidden="true" /></button>
                          </span>
                        </div>
                      ))}
                    </div>
                  )}
                  {scheduleMessage ? <p className="tariff-schedule-message" role="status">{scheduleMessage}</p> : null}
                  <div className="tariff-schedule-footer tariff-schedule-footer--actions-only">
                    <button className="secondary-button" type="button" disabled={scheduleSaving || tariffScheduleLoading} onClick={() => void saveTariffSchedule()}>
                      <Save size={16} aria-hidden="true" />
                      <span>{scheduleSaving ? 'Сохраняем…' : 'Сохранить тарифную сетку'}</span>
                    </button>
                  </div>
                </section>
              ) : null}
              {!initialSetting && !isTiered ? (
                <section className="tariff-schedule-editor tariff-schedule-editor--initial" aria-labelledby="initial-tariff-title">
                  <div className="tariff-schedule-heading">
                    <div>
                      <h4 id="initial-tariff-title">Начальный тариф</h4>
                      <p>После создания следующие значения можно добавлять отдельными периодами.</p>
                    </div>
                  </div>
                  <div className="tariff-schedule-row tariff-schedule-row--initial">
                    <FormField label="Начальная дата">
                      <LocalizedDatePicker ariaLabel="Ставка с" mode="date" value={tariffEffectiveFrom} onChange={setTariffEffectiveFrom} />
                    </FormField>
                    <FormField label="Тариф" help="Ставка начисления.">
                      <MoneyTextInput
                        aria-label="Тариф регулярной услуги"
                        value={regularRate}
                        onValueChange={(nextRate) => {
                          setRegularRate(nextRate)
                          setError(null)
                        }}
                      />
                    </FormField>
                  </div>
                </section>
              ) : null}
              {isRegular ? <h4 className="contractors-service-section-title contractors-service-section-title--parameters">Параметры начисления</h4> : null}
              <div className="contractors-service-period-grid contractors-service-period-grid--single-row">
                <FormField label="Периодичность" help={isMonthly ? 'Начисление создаётся каждый месяц.' : 'Начисление создаётся один раз в год.'}>
                  <SelectControl aria-label="Периодичность регулярной услуги" value={periodicityMonths} options={regularServicePeriodicityOptions} onChange={setPeriodicityMonths} />
                </FormField>
                {!isMonthly ? <FormField label="Месяц начисления" help="В этом месяце ежегодная услуга попадёт в начисления.">
                  <SelectControl aria-label="Месяц начисления ежегодной услуги" value={accrualStartMonth} options={contractorTariffMonthOptions} onChange={setAccrualStartMonth} />
                </FormField> : null}
                <FormField label="Оплатить до" help={isMonthly ? 'Выбранного числа месяца, следующего за месяцем начисления.' : 'Выбранной календарной даты после ежегодного начисления.'}>
                  <div className="contractors-inline-field contractors-inline-field--date">
                    <input aria-label="День оплаты" inputMode="numeric" maxLength={2} value={paymentDueDay} onChange={(event) => setPaymentDueDay(event.target.value)} />
                    {!isMonthly
                      ? <SelectControl aria-label="Месяц оплаты" value={paymentDueMonth} options={contractorTariffMonthOptions} onChange={setPaymentDueMonth} />
                      : <span className="contractors-date-suffix">числа следующего месяца</span>}
                  </div>
                </FormField>
                <FormField label="Перенос долга в просроченный" help="Количество дней после срока оплаты до переноса задолженности в просроченную.">
                  <div className="contractors-inline-field">
                    <input aria-label="Перенос долга в просроченный" inputMode="numeric" value={overdueGraceDays} onChange={(event) => setOverdueGraceDays(event.target.value)} />
                    <span>дн.</span>
                  </div>
                </FormField>
                <FormField label="Единица измерения" help="Это обозначение показывается в тарифах, начислениях и показаниях.">
                  <EditableCombobox
                    aria-label="Единица измерения"
                    maxLength={40}
                    placement="above"
                    value={unitName}
                    options={measurementUnits.map((unit) => ({ value: unit.name, label: unit.name }))}
                    onChange={(nextUnitName) => {
                      setUnitName(nextUnitName)
                      setError(null)
                    }}
                  />
                </FormField>
              </div>
              <div className="contractors-service-flags">
                <label className="contractors-check-row">
                  <input
                    type="checkbox"
                    aria-label="По счетчику"
                    checked={isByMeter}
                    onChange={(event) => changeMeterMode(event.target.checked)}
                  />
                  <span>По счетчику</span>
                </label>
                <label className="contractors-check-row">
                  <input
                    type="checkbox"
                    aria-label="Пороговая тарификация"
                    checked={isTiered}
                    disabled={!canUseTieredTariff}
                    onChange={(event) => {
                      const nextTiered = event.target.checked
                      setIsTiered(nextTiered)
                      if (nextTiered && tariffTiers.length < 2) {
                        const rate = parsePrototypeAmount(regularRate) ?? 1
                        setTariffTiers([
                          { id: 'draft-tier-1', name: 'Ступень 1', upperBound: 1100, rate, isCustom: true },
                          { id: 'draft-tier-2', name: 'Ступень 2', upperBound: null, rate, isCustom: true },
                        ])
                      }
                    }}
                  />
                  <span>Пороговая тарификация</span>
                </label>
              </div>
              {isTiered ? (
                <section className="contractors-tier-editor" aria-labelledby="contractors-tier-editor-title">
                  <div className="contractors-tier-editor-heading">
                    <h4 id="contractors-tier-editor-title">Пороги и тарифы</h4>
                    <span>{tariffTiers.length} {tariffTiers.length === 1 ? 'порог' : tariffTiers.length < 5 ? 'порога' : 'порогов'}</span>
                  </div>
                  {tariffTiers.length > 0 ? (
                    <div className="contractors-threshold-grid" role="group" aria-label="Пороги тарификации выбранного тарифа">
                      {tariffTiers.map((tier, index) => {
                        const lowerBound = index === 0 ? 0 : (tariffTiers[index - 1]?.upperBound ?? 0) + 1
                        return (
                        <div className="contractors-threshold-row" key={tier.id}>
                          <label>
                            <span>От</span>
                            <MeterReadingInput
                              aria-label={`${tier.name}: нижняя граница`}
                              value={lowerBound}
                              disabled
                            />
                          </label>
                          <label>
                            <span>До</span>
                            <div className="contractors-threshold-with-unit">
                              <MeterReadingInput
                                aria-label={`${tier.name}: верхняя граница`}
                                value={tier.upperBound ?? ''}
                                placeholder="Без верхней границы"
                                disabled={index === tariffTiers.length - 1}
                                onChange={(event) => {
                                  const nextValue = event.target.value === '' ? null : Number(event.target.value)
                                  setTariffTiers((current) => current.map((item) => item.id === tier.id
                                    ? { ...item, upperBound: Number.isFinite(nextValue) ? nextValue : null }
                                    : item))
                                }}
                              />
                              <span>{unitName}</span>
                            </div>
                          </label>
                          <label>
                            <span>Тариф</span>
                            <div className="contractors-threshold-with-unit">
                              <MoneyInput
                                aria-label={`${tier.name}: цена за единицу`}
                                value={tier.rate}
                                onValueChange={(parsedRate) => {
                                  setTariffTiers((current) => current.map((item) => item.id === tier.id
                                    ? { ...item, rate: parsedRate }
                                    : item))
                                }}
                              />
                              <span>руб.</span>
                            </div>
                          </label>
                          <button
                            className="icon-button danger-icon-button contractors-threshold-delete"
                            type="button"
                            aria-label={`Удалить порог ${index + 1}`}
                            disabled={tariffTiers.length <= 2}
                            onClick={() => setTariffTiers((current) => {
                              const remaining = current.filter((item) => item.id !== tier.id)
                              return remaining.map((item, remainingIndex) => remainingIndex === remaining.length - 1
                                ? { ...item, upperBound: null }
                                : item)
                            })}
                          >
                            <Trash2 size={16} />
                          </button>
                        </div>
                        )
                      })}
                      <button
                        className="ghost-button contractors-threshold-add"
                        type="button"
                        disabled={tariffTiers.length >= 20}
                        onClick={() => {
                          const baseRate = parsePrototypeAmount(regularRate) ?? 1
                          setTariffTiers((current) => {
                            const last = current.at(-1)
                            const previous = current.at(-2)
                            const nextUpperBound = (previous?.upperBound ?? 0) + 100
                            const nextTier = {
                              id: `draft-tier-${globalThis.crypto.randomUUID()}`,
                              name: `Ступень ${current.length}`,
                              upperBound: nextUpperBound,
                              rate: last?.rate ?? baseRate,
                              isCustom: true,
                            }
                            return last ? [...current.slice(0, -1), nextTier, last] : [...current, nextTier]
                          })
                        }}
                      >Добавить порог</button>
                    </div>
                  ) : (
                    <p className="form-hint">Добавьте минимум один порог и последнюю ступень без верхней границы.</p>
                  )}
                </section>
              ) : null}
            </>
          ) : (
            <div className="contractors-service-cost-grid">
              <FormField label="Стоимость" className="contractors-service-cost-field">
                <div className="contractors-inline-field">
                  <MoneyTextInput
                    aria-label="Стоимость услуги"
                    value={cost}
                    onValueChange={setCost}
                  />
                  <span>руб.</span>
                </div>
              </FormField>
            </div>
          )}

          <div className="detail-dialog-actions contractors-service-dialog-actions">
            {canChooseRegularity ? (
              <label className="contractors-service-regular-toggle contractors-service-regular-toggle--in-actions">
                <strong>Регулярные платежи</strong>
                <span className="contractors-switch-control">
                  <input
                    type="checkbox"
                    aria-label="Регулярные платежи"
                    checked={isRegular}
                    onChange={(event) => changeRegularity(event.target.checked)}
                  />
                </span>
              </label>
            ) : <span aria-hidden="true" />}
            <button className="secondary-button" type="submit" disabled={isSaving}>
              <Save size={17} />
              <span>{submitLabel}</span>
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
  activeGarageCount,
  garageOptions,
  incomeTypes,
  initialCampaign,
  isSaving,
  onClose,
  onSave,
  submitLabel = 'Объявить сбор',
  title = 'Добавить сбор',
}: {
  activeGarageCount: number
  garageOptions: GarageDto[]
  initialCampaign?: FeeCampaignDto | null
  incomeTypes: AccountingTypeDto[]
  isSaving: boolean
  onClose: () => void
  onSave: (request: UpsertFeeCampaignRequest) => Promise<void>
  submitLabel?: string
  title?: string
}) {
  const initialParticipantCount = initialCampaign?.appliesToAllGarages
    ? activeGarageCount
    : initialCampaign?.participantGarageIds.length ?? 0
  const initialAmountCalculationMode = initialCampaign
    && calculateFeeCampaignTargetAmount(initialCampaign.contributionAmount, initialParticipantCount) !== initialCampaign.targetAmount
    ? 'target'
    : 'contribution'
  const [name, setName] = useState(initialCampaign?.name ?? '')
  const defaultIncomeTypeId = initialCampaign?.incomeTypeId
    ?? incomeTypes.find((incomeType) => incomeType.code === 'other_income')?.id
    ?? incomeTypes[0]?.id
    ?? ''
  const [incomeTypeId, setIncomeTypeId] = useState(defaultIncomeTypeId)
  const [goal, setGoal] = useState(initialCampaign?.goal ?? '')
  const [contributionAmount, setContributionAmount] = useState(initialCampaign ? formatTariffDecimal(initialCampaign.contributionAmount) : '')
  const [targetAmountInput, setTargetAmountInput] = useState(initialCampaign ? formatTariffDecimal(initialCampaign.targetAmount) : '')
  const [amountCalculationMode, setAmountCalculationMode] = useState<'contribution' | 'target'>(initialAmountCalculationMode)
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
  const participantCount = appliesToAllGarages ? activeGarageCount : participantGarageIds.length
  const parsedContributionInput = parsePrototypeAmount(contributionAmount)
  const parsedTargetAmountInput = parsePrototypeAmount(targetAmountInput)
  const parsedContributionAmount = amountCalculationMode === 'target'
    ? calculateFeeCampaignContributionAmount(parsedTargetAmountInput, participantCount)
    : parsedContributionInput
  const targetAmount = amountCalculationMode === 'target'
    ? parsedTargetAmountInput ?? 0
    : calculateFeeCampaignTargetAmount(parsedContributionInput, participantCount)
  const lastContributionAmount = calculateFeeCampaignLastContribution(targetAmount, parsedContributionAmount ?? 0, participantCount)

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
    const parsedOverdueGraceDays = Number(overdueGraceDays)

    if (!trimmedName) {
      setError('Укажите наименование сбора.')
      return
    }

    if (!incomeTypeId) {
      setError('Выберите назначение поступления.')
      return
    }

    if (parsedContributionAmount === null || parsedContributionAmount <= 0) {
      setError('Сумма взноса должна быть больше нуля.')
      return
    }

    if (targetAmount <= 0) {
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
      targetAmount,
      amountCalculationMode,
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

    try {
      await onSave(request)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось объявить сбор.')
    }
  }

  async function confirmFeeChanges() {
    if (!pendingConfirmation) {
      return
    }

    try {
      await onSave(pendingConfirmation.request)
      setPendingConfirmation(null)
    } catch (caught) {
      setPendingConfirmation(null)
      setError(caught instanceof Error ? caught.message : 'Не удалось сохранить изменения сбора.')
    }
  }

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={pendingConfirmation ? undefined : onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog contractors-tariff-dialog contractors-fee-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-fee-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="contractor-fee-title">{title}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму сбора" onClick={onClose} disabled={Boolean(pendingConfirmation)}>
              <X size={18} />
            </button>
          </div>

          <form className="dictionary-modal-form contractors-modal-form contractors-fee-form" onSubmit={submitFee}>
            {error ? <FormError>{error}</FormError> : null}
            <div className="contractors-fee-layout">
              <section className="contractors-fee-card" aria-labelledby="fee-settings-title">
                <h4 id="fee-settings-title">Настройки сбора</h4>
                <FormField label="Наименование сбора">
                  <input aria-label="Наименование сбора" value={name} onChange={(event) => setName(event.target.value)} />
                </FormField>
                <FormField label="Назначение поступления">
                  <SelectControl
                    aria-label="Назначение поступления для сбора"
                    value={incomeTypeId}
                    options={incomeTypes.map((incomeType) => ({
                      value: incomeType.id,
                      label: incomeType.destinationFundName
                        ? `${incomeType.name} → фонд «${incomeType.destinationFundName}»`
                        : `${incomeType.name} → фонд не назначен`,
                    }))}
                    maxVisibleOptions={6}
                    onChange={setIncomeTypeId}
                  />
                </FormField>
                <p className="form-hint" role="status">
                  Фонд назначения: {incomeTypes.find((incomeType) => incomeType.id === incomeTypeId)?.destinationFundName ?? 'не назначен'}
                </p>
                <FormField label="Цель">
                  <input aria-label="Цель сбора" value={goal} onChange={(event) => setGoal(event.target.value)} />
                </FormField>
                <label className="contractors-switch-row contractors-fee-participant-switch">
                  <span className="contractors-fee-participant-label">
                    <strong>Участники</strong>
                    <small>все гаражи</small>
                  </span>
                  <span className="contractors-switch-control">
                    <input type="checkbox" aria-label="Все гаражи" checked={appliesToAllGarages} onChange={(event) => setAppliesToAllGarages(event.target.checked)} />
                  </span>
                </label>
              </section>
              <section className="contractors-fee-card" aria-labelledby="fee-parameters-title">
                <h4 id="fee-parameters-title">Параметры сбора</h4>
                <div className="contractors-fee-two-column-grid">
                  <FormField label="Сумма взноса">
                    <div className="contractors-inline-field contractors-fee-money-field">
                      <MoneyTextInput
                        aria-label="Сумма взноса"
                        value={amountCalculationMode === 'target' ? formatTariffDecimal(parsedContributionAmount ?? 0) : contributionAmount}
                        onValueChange={(value) => {
                          const nextAmount = parsePrototypeAmount(value)
                          if (amountCalculationMode !== 'target' || !areFeeCampaignAmountsEqual(nextAmount, parsedContributionAmount)) {
                            setAmountCalculationMode('contribution')
                          }
                          setContributionAmount(value)
                        }}
                      />
                      <span>руб.</span>
                    </div>
                  </FormField>
                  <FormField label="Сумма сбора">
                    <div className="contractors-inline-field contractors-fee-money-field">
                      <MoneyTextInput
                        aria-label="Сумма сбора"
                        value={amountCalculationMode === 'contribution' ? formatTariffDecimal(targetAmount) : targetAmountInput}
                        onValueChange={(value) => {
                          const nextAmount = parsePrototypeAmount(value)
                          if (amountCalculationMode !== 'contribution' || !areFeeCampaignAmountsEqual(nextAmount, targetAmount)) {
                            setAmountCalculationMode('target')
                          }
                          setTargetAmountInput(value)
                        }}
                      />
                      <span>руб.</span>
                    </div>
                  </FormField>
                </div>
                <small className="contractors-fee-calculation-status" role="status" aria-live="polite">
                  {amountCalculationMode === 'target'
                    ? <>Рассчитано автоматически: {formatTariffDecimal(targetAmount)} руб. ÷ {formatFeeCampaignParticipantCount(participantCount)} = до {formatTariffDecimal(parsedContributionAmount ?? 0)} руб.{lastContributionAmount > 0 && lastContributionAmount !== parsedContributionAmount ? ` Последний — ${formatTariffDecimal(lastContributionAmount)} руб.` : ''}</>
                    : <>Рассчитано автоматически: {formatFeeCampaignParticipantCount(participantCount)} × {formatTariffDecimal(parsedContributionAmount ?? 0)} руб.</>}
                </small>
                <div className="contractors-fee-date-grid">
                  <FormField label="Дата начала">
                    <LocalizedDatePicker ariaLabel="Дата начала" mode="date" value={startsOn} onChange={setStartsOn} />
                  </FormField>
                  <FormField label="Дата окончания сбора">
                    <LocalizedDatePicker ariaLabel="Дата окончания сбора" mode="date" value={endsOn} onChange={setEndsOn} />
                  </FormField>
                </div>
                <FormField label="Перенос долга по сбору в просроченный">
                  <div className="contractors-inline-field">
                    <input aria-label="Перенос долга по сбору в просроченный" inputMode="numeric" value={overdueGraceDays} onChange={(event) => setOverdueGraceDays(event.target.value)} />
                    <span>дн.</span>
                  </div>
                </FormField>
              </section>

              {!appliesToAllGarages ? (
                <fieldset className="contractors-participant-list contractors-fee-participant-list">
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
            </div>

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
