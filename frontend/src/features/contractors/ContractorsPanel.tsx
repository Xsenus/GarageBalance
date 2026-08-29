import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { CSSProperties, FormEvent, MouseEvent, ReactNode, RefObject } from 'react'
import { FileText, Gauge, LoaderCircle, Pencil, RotateCcw, Save, Search, Trash2, UserPlus, UsersRound, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { AccountingTypeDto, ChargeServiceSettingDto, CreateChargeServiceWithTariffRequest, DictionaryClient, GarageColumnFilters, GarageDto, OwnerDto, StaffDepartmentDto, StaffMemberDto, SupplierContactDto, SupplierDto, SupplierGroupDto, TariffDto, UpsertGarageRequest, UpsertOwnerRequest, UpsertStaffMemberRequest, UpsertSupplierContactRequest, UpsertSupplierRequest } from '../../services/dictionariesApi'
import type { FinanceClient, GarageBalanceHistoryDto } from '../../services/financeApi'
import type { FundOptionDto, FundsClient } from '../../services/fundsApi'
import type { DadataAddressSuggestionDto, DadataPartySuggestionDto, IntegrationClient } from '../../services/integrationsApi'
import { hasPermission, isAdministrator, permissions } from '../../shared/accessControl'
import { AsyncErrorState, BackgroundRefreshStatus, LoadingSkeleton, StatusMessage, TableLoadingState } from '../../shared/AsyncState'
import { FormError } from '../../shared/formFeedback'
import { FormField } from '../../shared/FormField'
import { MoneyTextInput } from '../../shared/MoneyInput'
import { PhoneInput } from '../../shared/PhoneInput'
import { formatDateOnly, formatDebtAmount, formatDebtLabel, formatMoney, formatMonth, getDebtClassName } from '../../shared/formatters'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { createSupplierOpeningBalanceEntries } from './contractorFinancialReport'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { createClientPage, createFallbackPage } from '../../shared/pagination'
import { ReportPeriodQuickSelect } from '../../shared/ReportPeriodQuickSelect'
import { TablePagination } from '../../shared/TablePagination'
import { createDefaultGarageBalanceHistoryFilters, createFullFinancialReportFilters } from '../../shared/reportFilters'
import { SelectControl } from '../../shared/SelectControl'
import { formatPrototypeChangeValue } from '../../shared/prototypeEditing'
import type { AuditPanelPreset, ContractorOpenTarget } from '../../shared/workspaceNavigation'
import { createRetryableLazyLoader } from '../../shared/retryableLazyLoader'
import { useColumnResize } from '../../shared/useColumnResize'
import { formatStaffRate, parseStaffRate } from './staffRateFormatting'

const AddServicePrototypeDialog = lazy(createRetryableLazyLoader(() =>
  import('../tariffs/TariffsAndFeesPanel').then((module) => ({ default: module.AddServicePrototypeDialog }))))

function normalizeContractorTargetText(value?: string | null) {
  return (value ?? '').trim().toLocaleLowerCase('ru-RU')
}

function extractGarageNumberFromTarget(target: ContractorOpenTarget) {
  if (target.garageNumber?.trim()) {
    return target.garageNumber.trim()
  }

  return target.displayName?.match(/\d+/)?.[0] ?? null
}

function findGarageForOpenTarget(garages: ContractorGarageRow[], target: ContractorOpenTarget) {
  const garageNumber = extractGarageNumberFromTarget(target)
  const displayName = normalizeContractorTargetText(target.displayName)

  return garages.find((garage) => target.entityId && garage.id === target.entityId)
    ?? garages.find((garage) => garageNumber && garage.number === garageNumber)
    ?? garages.find((garage) => displayName.length > 0 && normalizeContractorTargetText(garage.owner).includes(displayName))
    ?? null
}

function findSupplierForOpenTarget(suppliers: ContractorSupplierRow[], target: ContractorOpenTarget) {
  const displayName = normalizeContractorTargetText(target.displayName)

  return suppliers.find((supplier) => target.entityId && supplier.id === target.entityId)
    ?? suppliers.find((supplier) => displayName.length > 0 && normalizeContractorTargetText(supplier.name) === displayName)
    ?? suppliers.find((supplier) => displayName.length > 0 && normalizeContractorTargetText(supplier.name).includes(displayName))
    ?? null
}

function findStaffForOpenTarget(staff: ContractorStaffRow[], target: ContractorOpenTarget) {
  const displayName = normalizeContractorTargetText(target.displayName)

  return staff.find((employee) => target.entityId && employee.id === target.entityId)
    ?? staff.find((employee) => displayName.length > 0 && normalizeContractorTargetText(employee.fullName) === displayName)
    ?? staff.find((employee) => displayName.length > 0 && normalizeContractorTargetText(employee.fullName).includes(displayName))
    ?? null
}

type ContractorSection = 'garages' | 'suppliers' | 'staff'
type ContractorSortDirection = 'asc' | 'desc'
type ContractorSortableSection = ContractorSection
type GarageColumnFilterForm = Record<keyof GarageColumnFilters, string>
type FinancialReportFilters = ReturnType<typeof createDefaultGarageBalanceHistoryFilters>
type FinancialReportRequest = { controller: AbortController; sequence: number }

const emptyGarageColumnFilterForm: GarageColumnFilterForm = {
  number: '',
  peopleCountMin: '',
  peopleCountMax: '',
  floorCountMin: '',
  floorCountMax: '',
}

function toGarageColumnFilters(form: GarageColumnFilterForm): GarageColumnFilters {
  const number = form.number.trim()
  const parseOptionalNumber = (value: string) => value === '' ? undefined : Number(value)
  return {
    number: number || undefined,
    peopleCountMin: parseOptionalNumber(form.peopleCountMin),
    peopleCountMax: parseOptionalNumber(form.peopleCountMax),
    floorCountMin: parseOptionalNumber(form.floorCountMin),
    floorCountMax: parseOptionalNumber(form.floorCountMax),
  }
}

function hasGarageColumnFilters(filters: GarageColumnFilters) {
  return Object.values(filters).some((value) => value !== undefined)
}

type ContractorGarageRow = {
  id: string
  version?: string
  ownerId?: string | null
  number: string
  peopleCount: string
  floorCount: string
  owner: string
  phone: string
  address: string
  startingBalance?: string
  startingOverdueDebt?: string
  balance: string
  overdueDebt: string
  initialWater: string
  initialElectricity: string
  meters: string
  comment: string
  isDeleted: boolean
}

type ContractorSupplierRow = {
  id: string
  version?: string
  name: string
  serviceId?: string | null
  service: string
  expenseTypeId?: string | null
  expenseFundId?: string | null
  inn: string
  legalAddress: string
  contactPerson: string
  phone: string
  email: string
  contacts: ContractorSupplierContact[]
  startingBalance: string
  debt: string
  comment: string
  isDeleted: boolean
}

type ContractorSupplierContact = {
  id: string
  fullName: string
  position: string
  phone: string
  email: string
  status: 'Работает' | 'Не работает'
  comment: string
  isDeleted: boolean
  deleteReason?: string
}

type ContractorStaffRow = {
  id: string
  fullName: string
  department: string
  rate: string
  isDeleted: boolean
}

type ContractorFinancialReportTarget =
  | { type: 'supplier'; row: ContractorSupplierRow }
  | { type: 'employee'; row: ContractorStaffRow }

type ContractorFinancialReportRow = {
  id: string
  accountingMonth: string
  date: string
  documentNumber: string
  description: string
  accrualAmount: number
  paymentAmount: number
  sortOrder?: number
  balanceAfter: number
}

type ContractorFinancialReport = {
  openingBalance: number
  accrualTotal: number
  paymentTotal: number
  balance: number
  rows: ContractorFinancialReportRow[]
}

type ContractorDepartmentRow = {
  id: string
  name: string
  isDeleted?: boolean
}

type ContractorModal =
  | { type: 'garage'; item?: ContractorGarageRow }
  | { type: 'supplier'; item?: ContractorSupplierRow }
  | { type: 'service' }
  | { type: 'employee'; item?: ContractorStaffRow }
  | { type: 'department'; item?: ContractorDepartmentRow }

type ContractorRestoreTarget =
  | { type: 'garage'; item: ContractorGarageRow }
  | { type: 'supplier'; item: ContractorSupplierRow }
  | { type: 'employee'; item: ContractorStaffRow }
  | { type: 'department'; item: ContractorDepartmentRow }

type OpeningBalanceAdjustmentTarget =
  | { type: 'garage'; id: string; name: string; currentAmount: number }
  | { type: 'supplier'; id: string; name: string; currentAmount: number }

const contractorSectionLabels: Record<ContractorSection, string> = {
  garages: 'Гаражи',
  suppliers: 'Поставщики',
  staff: 'Персонал',
}

type ContractorGarageColumnKey = 'number' | 'peopleCount' | 'floorCount' | 'owner' | 'phone' | 'overdueDebt' | 'actions'
type ContractorGarageServerSortKey = Exclude<ContractorGarageColumnKey, 'actions'>
type ContractorSupplierSortKey = 'name' | 'service' | 'contactPerson' | 'phone' | 'email' | 'debt'
type ContractorSupplierServerSortKey = ContractorSupplierSortKey
type ContractorStaffSortKey = 'fullName' | 'department' | 'rate'
type ContractorSupplierColumnKey = ContractorSupplierSortKey | 'actions'
type ContractorStaffColumnKey = ContractorStaffSortKey | 'actions'
type ContractorSortKey = Exclude<ContractorGarageColumnKey, 'actions'> | ContractorSupplierSortKey | ContractorStaffSortKey
type ContractorSortState = {
  section: ContractorSortableSection
  key: ContractorSortKey
  direction: ContractorSortDirection
}
type ContractorColumnDefinition<TKey extends string> = { key: TKey; label: string; defaultWidth: number; minWidth: number }

const contractorGarageColumnStorageKey = 'garagebalance.contractors.garageColumnWidths'
const contractorSupplierColumnStorageKey = 'garagebalance.contractors.supplierColumnWidths'
const contractorStaffColumnStorageKey = 'garagebalance.contractors.staffColumnWidths'
const contractorsDictionaryListLimit = 500
const contractorsDefaultPageSize = 25

type ContractorPageState = {
  totalCount: number
  offset: number
  limit: number
}

const createContractorPageState = (): ContractorPageState => ({ totalCount: 0, offset: 0, limit: contractorsDefaultPageSize })
// PostgreSQL imports and deterministic staging records can contain canonical UUIDs
// whose version/variant nibbles are not limited to RFC 4122 v1-v5. The API accepts
// every canonical Guid, so the UI must not reject an otherwise valid persisted id.
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

const contractorGarageColumnDefinitions: Array<ContractorColumnDefinition<ContractorGarageColumnKey>> = [
  { key: 'number', label: 'Номер', defaultWidth: 96, minWidth: 72 },
  { key: 'peopleCount', label: 'Количество человек', defaultWidth: 170, minWidth: 132 },
  { key: 'floorCount', label: 'Количество этажей', defaultWidth: 170, minWidth: 132 },
  { key: 'owner', label: 'Владелец', defaultWidth: 260, minWidth: 160 },
  { key: 'phone', label: 'Телефон', defaultWidth: 220, minWidth: 150 },
  { key: 'overdueDebt', label: 'Просроченная задолженность', defaultWidth: 220, minWidth: 170 },
  { key: 'actions', label: 'Действия', defaultWidth: 132, minWidth: 112 },
]

const contractorSupplierColumnDefinitions: Array<ContractorColumnDefinition<ContractorSupplierColumnKey>> = [
  { key: 'name', label: 'Поставщик', defaultWidth: 220, minWidth: 170 },
  { key: 'service', label: 'Услуга', defaultWidth: 180, minWidth: 150 },
  { key: 'contactPerson', label: 'Контактное лицо', defaultWidth: 210, minWidth: 170 },
  { key: 'phone', label: 'Телефон', defaultWidth: 180, minWidth: 168 },
  { key: 'email', label: 'Почта', defaultWidth: 210, minWidth: 160 },
  { key: 'debt', label: 'Задолженность', defaultWidth: 160, minWidth: 150 },
  { key: 'actions', label: 'Действия', defaultWidth: 132, minWidth: 112 },
]

const contractorStaffColumnDefinitions: Array<ContractorColumnDefinition<ContractorStaffColumnKey>> = [
  { key: 'fullName', label: 'ФИО', defaultWidth: 260, minWidth: 180 },
  { key: 'department', label: 'Отдел', defaultWidth: 220, minWidth: 160 },
  { key: 'rate', label: 'Ставка', defaultWidth: 150, minWidth: 120 },
  { key: 'actions', label: 'Действия', defaultWidth: 132, minWidth: 112 },
]

function getDefaultContractorColumnWidths<TKey extends string>(definitions: Array<ContractorColumnDefinition<TKey>>) {
  return definitions.reduce<Record<TKey, number>>((widths, column) => {
    widths[column.key] = column.defaultWidth
    return widths
  }, {} as Record<TKey, number>)
}

function getSupplierPrimaryContact(supplier: ContractorSupplierRow) {
  return supplier.contacts.find((contact) => !contact.isDeleted && contact.status === 'Работает') ?? supplier.contacts.find((contact) => !contact.isDeleted) ?? null
}

function normalizeSupplierPrototype(supplier: ContractorSupplierRow): ContractorSupplierRow {
  const primaryContact = getSupplierPrimaryContact(supplier)
  const hasManagedContacts = supplier.contacts.length > 0

  return {
    ...supplier,
    contactPerson: primaryContact?.fullName ?? (hasManagedContacts ? '' : supplier.contactPerson),
    phone: primaryContact?.phone ?? (hasManagedContacts ? '' : supplier.phone),
    email: primaryContact?.email ?? (hasManagedContacts ? '' : supplier.email),
  }
}

function updateSupplierPrimaryContact(
  supplier: ContractorSupplierRow,
  patch: Partial<Pick<ContractorSupplierContact, 'phone' | 'email'>>,
) {
  const primaryContact = getSupplierPrimaryContact(supplier)
  const contacts = primaryContact
    ? supplier.contacts.map((contact) => (contact.id === primaryContact.id ? { ...contact, ...patch } : contact))
    : [
        ...supplier.contacts,
        {
          ...createEmptySupplierContact(),
          fullName: supplier.contactPerson.trim() || 'Основной контакт',
          phone: supplier.phone,
          email: supplier.email,
          ...patch,
        },
      ]

  return normalizeSupplierPrototype({ ...supplier, contacts })
}

function isBackendDictionaryId(id: string) {
  return guidPattern.test(id)
}

function formatPrototypeMoney(value: number | null | undefined) {
  return formatStaffRate(value)
}

function parsePrototypeMoney(value: string) {
  const parsed = parseStaffRate(value.replace(/\s*руб\.?$/i, ''))
  return Number.isFinite(parsed) ? parsed : 0
}

function comparePrototypeText(left: string, right: string) {
  return left.localeCompare(right, 'ru', { numeric: true, sensitivity: 'base' })
}

function applyContractorSortDirection(value: number, direction: ContractorSortDirection) {
  return direction === 'asc' ? value : -value
}

function compareContractorGarages(left: ContractorGarageRow, right: ContractorGarageRow, key: Exclude<ContractorGarageColumnKey, 'actions'>) {
  if (key === 'peopleCount' || key === 'floorCount' || key === 'overdueDebt') {
    return parsePrototypeMoney(left[key]) - parsePrototypeMoney(right[key])
  }

  return comparePrototypeText(left[key], right[key])
}

function compareContractorSuppliers(left: ContractorSupplierRow, right: ContractorSupplierRow, key: ContractorSupplierSortKey) {
  if (key === 'debt') {
    return parsePrototypeMoney(left.debt) - parsePrototypeMoney(right.debt)
  }

  if (key === 'contactPerson' || key === 'phone' || key === 'email') {
    const leftContact = getSupplierPrimaryContact(left)
    const rightContact = getSupplierPrimaryContact(right)
    const leftValue = key === 'contactPerson' ? leftContact?.fullName ?? left.contactPerson : key === 'phone' ? leftContact?.phone ?? left.phone : leftContact?.email ?? left.email
    const rightValue = key === 'contactPerson' ? rightContact?.fullName ?? right.contactPerson : key === 'phone' ? rightContact?.phone ?? right.phone : rightContact?.email ?? right.email
    return comparePrototypeText(leftValue, rightValue)
  }

  return comparePrototypeText(left[key], right[key])
}

function isGarageServerSortKey(key: ContractorSortKey): key is ContractorGarageServerSortKey {
  return key === 'number' || key === 'peopleCount' || key === 'floorCount' || key === 'owner' || key === 'phone' || key === 'overdueDebt'
}

function isSupplierServerSortKey(key: ContractorSortKey): key is ContractorSupplierServerSortKey {
  return key === 'name' || key === 'service' || key === 'contactPerson' || key === 'phone' || key === 'email' || key === 'debt'
}

function compareContractorStaff(left: ContractorStaffRow, right: ContractorStaffRow, key: ContractorStaffSortKey) {
  if (key === 'rate') {
    return parsePrototypeMoney(left.rate) - parsePrototypeMoney(right.rate)
  }

  return comparePrototypeText(left[key], right[key])
}

function compareContractorReportEntries(
  left: Omit<ContractorFinancialReportRow, 'balanceAfter'>,
  right: Omit<ContractorFinancialReportRow, 'balanceAfter'>,
) {
  const monthComparison = left.accountingMonth.localeCompare(right.accountingMonth)
  if (monthComparison !== 0) {
    return monthComparison
  }

  const dateComparison = left.date.localeCompare(right.date)
  if (dateComparison !== 0) {
    return dateComparison
  }

  const orderComparison = (left.sortOrder ?? 0) - (right.sortOrder ?? 0)
  if (orderComparison !== 0) {
    return orderComparison
  }

  return left.description.localeCompare(right.description)
}

function buildContractorFinancialReport(entries: Array<Omit<ContractorFinancialReportRow, 'balanceAfter'>>, openingBalance = 0): ContractorFinancialReport {
  let balance = openingBalance
  let accrualTotal = 0
  let paymentTotal = 0
  const rows = [...entries].sort(compareContractorReportEntries).map((entry) => {
    accrualTotal += entry.accrualAmount
    paymentTotal += entry.paymentAmount
    balance += entry.accrualAmount - entry.paymentAmount

    return {
      ...entry,
      balanceAfter: balance,
    }
  })

  return {
    openingBalance,
    accrualTotal,
    paymentTotal,
    balance,
    rows,
  }
}

function getContractorReportMonthStarts(monthFrom: string, monthTo: string) {
  const [fromYear, fromMonth] = monthFrom.split('-').map(Number)
  const [toYear, toMonth] = monthTo.split('-').map(Number)
  if (!fromYear || !fromMonth || !toYear || !toMonth) {
    return []
  }

  const months: string[] = []
  const cursor = new Date(fromYear, fromMonth - 1, 1)
  const last = new Date(toYear, toMonth - 1, 1)
  while (cursor <= last) {
    months.push(`${cursor.getFullYear()}-${String(cursor.getMonth() + 1).padStart(2, '0')}-01`)
    cursor.setMonth(cursor.getMonth() + 1)
  }

  return months
}

function createStaffFinancialReportEntries(row: ContractorStaffRow, monthFrom: string, monthTo: string) {
  const rate = parsePrototypeMoney(row.rate)
  if (rate <= 0) {
    return []
  }

  return getContractorReportMonthStarts(monthFrom, monthTo).map((month) => ({
    id: `staff-accrual-${row.id}-${month}`,
    accountingMonth: month,
    date: month,
    documentNumber: '—',
    description: 'Начисление зарплаты',
    accrualAmount: rate,
    paymentAmount: 0,
  }))
}

function formatPrototypeNumber(value: number | null | undefined) {
  if (value === null || value === undefined) {
    return ''
  }

  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 4 }).format(value)
}

function parsePrototypeInteger(value: string, fallback = 0) {
  const parsed = Number.parseInt(value.trim(), 10)
  return Number.isFinite(parsed) ? parsed : fallback
}

function parsePrototypeNullableNumber(value: string) {
  const normalized = value.replace(/\s/g, '').replace(',', '.')
  if (!normalized) {
    return null
  }

  const parsed = Number(normalized)
  return Number.isFinite(parsed) ? parsed : null
}

function normalizeOwnerName(value: string) {
  return value.trim().replace(/\s+/g, ' ')
}

function splitOwnerName(value: string) {
  const [lastName = '', firstName = '', ...middleNameParts] = normalizeOwnerName(value).split(' ')
  return {
    lastName,
    firstName: firstName || 'Без имени',
    middleName: middleNameParts.join(' '),
  }
}

function createOwnerRequestFromGarage(row: ContractorGarageRow): UpsertOwnerRequest {
  const parsedName = splitOwnerName(row.owner)

  return {
    lastName: parsedName.lastName || 'Без фамилии',
    firstName: parsedName.firstName,
    middleName: parsedName.middleName,
    phone: row.phone.trim(),
    address: row.address.trim(),
    meterNotes: row.meters.trim(),
  }
}

function createGarageRowFromDto(garage: GarageDto, owners: OwnerDto[]): ContractorGarageRow {
  const owner = garage.ownerId ? owners.find((item) => item.id === garage.ownerId) : null
  const balance = garage.balance ?? garage.startingBalance ?? 0
  const overdueDebt = garage.overdueDebt ?? Math.max(balance, 0)

  return {
    id: garage.id,
    version: garage.version,
    ownerId: garage.ownerId,
    number: garage.number,
    peopleCount: String(garage.peopleCount),
    floorCount: String(garage.floorCount),
    owner: garage.ownerName ?? owner?.fullName ?? '',
    phone: owner?.phone ?? '',
    address: owner?.address ?? '',
    startingBalance: formatPrototypeMoney(garage.startingBalance),
    startingOverdueDebt: formatPrototypeMoney(garage.startingOverdueDebt),
    balance: formatPrototypeMoney(balance),
    overdueDebt: overdueDebt > 0 ? `${formatMoney(overdueDebt)} руб.` : '',
    initialWater: formatPrototypeNumber(garage.initialWaterMeterValue),
    initialElectricity: formatPrototypeNumber(garage.initialElectricityMeterValue),
    meters: owner?.meterNotes ?? '',
    comment: garage.comment ?? '',
    isDeleted: garage.isArchived,
  }
}

function createGarageRequestFromRow(row: ContractorGarageRow, ownerId: string | null): UpsertGarageRequest {
  return {
    number: row.number.trim(),
    peopleCount: parsePrototypeInteger(row.peopleCount, 0),
    floorCount: parsePrototypeInteger(row.floorCount, 0),
    ownerId,
    startingBalance: parsePrototypeMoney(row.startingBalance ?? row.balance),
    startingOverdueDebt: parsePrototypeMoney(row.startingOverdueDebt ?? ''),
    initialWaterMeterValue: parsePrototypeNullableNumber(row.initialWater),
    initialElectricityMeterValue: parsePrototypeNullableNumber(row.initialElectricity),
    comment: row.comment.trim(),
    version: row.version,
  }
}

async function resolveGarageOwner(
  dictionaryClient: DictionaryClient,
  accessToken: string,
  owners: OwnerDto[],
  row: ContractorGarageRow,
) {
  const ownerName = normalizeOwnerName(row.owner)
  if (!ownerName) {
    return null
  }

  const existing = owners.find((owner) => owner.id === row.ownerId)
    ?? owners.find((owner) => normalizeOwnerName(owner.fullName).localeCompare(ownerName, 'ru', { sensitivity: 'accent' }) === 0)
  const request = createOwnerRequestFromGarage(row)

  if (existing) {
    const shouldUpdate = existing.phone !== (request.phone || null)
      || existing.address !== (request.address || null)
      || existing.meterNotes !== (request.meterNotes || null)
      || normalizeOwnerName(existing.fullName) !== ownerName
    if (!shouldUpdate) {
      return existing
    }

    return dictionaryClient.updateOwner(accessToken, existing.id, request)
  }

  return dictionaryClient.createOwner(accessToken, request)
}

function createSupplierContactFromDto(contact: SupplierContactDto): ContractorSupplierContact {
  return {
    id: contact.id,
    fullName: contact.fullName,
    position: contact.position ?? '',
    phone: contact.phone ?? '',
    email: contact.email ?? '',
    status: contact.status === 'Не работает' ? 'Не работает' : 'Работает',
    comment: contact.comment ?? '',
    isDeleted: contact.isArchived,
  }
}

function createSupplierRowFromDto(supplier: SupplierDto, contacts: SupplierContactDto[]): ContractorSupplierRow {
  const supplierContacts = contacts.filter((contact) => contact.supplierId === supplier.id).map(createSupplierContactFromDto)

  return normalizeSupplierPrototype({
    id: supplier.id,
    version: supplier.version,
    name: supplier.name,
    serviceId: supplier.chargeServiceSettingId ?? null,
    service: supplier.chargeServiceSettingName ?? supplier.groupName,
    expenseTypeId: supplier.expenseTypeId ?? null,
    expenseFundId: supplier.expenseFundId ?? null,
    inn: supplier.inn ?? '',
    legalAddress: supplier.legalAddress ?? '',
    contactPerson: supplier.contactPerson ?? '',
    phone: supplier.phone ?? '',
    email: supplier.email ?? '',
    contacts: supplierContacts,
    startingBalance: formatPrototypeMoney(supplier.startingBalance),
    debt: formatPrototypeMoney(supplier.debt),
    comment: supplier.comment ?? '',
    isDeleted: supplier.isArchived,
  })
}

function createStaffDepartmentRowFromDto(department: StaffDepartmentDto): ContractorDepartmentRow {
  return {
    id: department.id,
    name: department.name,
    isDeleted: department.isArchived,
  }
}

function createStaffRowFromDto(member: StaffMemberDto): ContractorStaffRow {
  return {
    id: member.id,
    fullName: member.fullName,
    department: member.departmentName,
    rate: formatStaffRate(member.rate),
    isDeleted: member.isArchived,
  }
}

async function resolveSupplierGroup(
  dictionaryClient: DictionaryClient,
  accessToken: string,
  groups: SupplierGroupDto[],
  serviceName: string,
) {
  const normalizedName = serviceName.trim() || 'Прочее'
  const existing = groups.find((group) => !group.isArchived && group.name.localeCompare(normalizedName, 'ru', { sensitivity: 'accent' }) === 0)
  if (existing) {
    return existing
  }

  const created = await dictionaryClient.createSupplierGroup(accessToken, { name: normalizedName })
  groups.push(created)
  return created
}

function createSupplierRequestFromRow(row: ContractorSupplierRow, groupId: string): UpsertSupplierRequest {
  const normalized = normalizeSupplierPrototype(row)
  return {
    name: normalized.name.trim(),
    groupId,
    inn: normalized.inn.trim(),
    legalAddress: normalized.legalAddress.trim(),
    contactPerson: normalized.contactPerson.trim(),
    phone: normalized.phone.trim(),
    email: normalized.email.trim() || null,
    startingBalance: parsePrototypeMoney(normalized.startingBalance),
    comment: normalized.comment.trim(),
    chargeServiceSettingId: normalized.serviceId,
    expenseTypeId: normalized.expenseTypeId,
    expenseFundId: normalized.expenseFundId,
    version: normalized.version,
  }
}

function createSupplierContactRequestFromRow(supplierId: string, contact: ContractorSupplierContact): UpsertSupplierContactRequest {
  return {
    supplierId,
    fullName: contact.fullName.trim(),
    position: contact.position.trim(),
    phone: contact.phone.trim(),
    email: contact.email.trim() || null,
    status: contact.status,
    comment: contact.comment.trim(),
  }
}

function createStaffMemberRequestFromRow(row: ContractorStaffRow, departmentId: string): UpsertStaffMemberRequest {
  return {
    fullName: row.fullName.trim(),
    departmentId,
    rate: parsePrototypeMoney(row.rate),
  }
}

function createEmptySupplierContact(): ContractorSupplierContact {
  return {
    id: `supplier-contact-${Date.now()}`,
    fullName: '',
    position: '',
    phone: '',
    email: '',
    status: 'Работает',
    comment: '',
    isDeleted: false,
  }
}

function formatSupplierContactSummary(contacts: ContractorSupplierContact[]) {
  if (contacts.length === 0) {
    return ''
  }

  return contacts
    .map((contact, index) => {
      const state = contact.isDeleted ? 'удален' : contact.status
      return `${index + 1}. ${contact.fullName || 'Без ФИО'} / ${contact.position || 'Без должности'} / ${contact.phone || 'Без телефона'} / ${contact.email || 'Без почты'} / ${state} / ${contact.comment || 'Без комментария'}`
    })
    .join('; ')
}

function loadContractorColumnWidths<TKey extends string>(storageKey: string, definitions: Array<ContractorColumnDefinition<TKey>>) {
  const defaults = getDefaultContractorColumnWidths(definitions)

  try {
    const rawValue = window.localStorage.getItem(storageKey)
    if (!rawValue) {
      return defaults
    }

    const parsed = JSON.parse(rawValue) as Partial<Record<TKey, number>>
    return definitions.reduce<Record<TKey, number>>((widths, column) => {
      const value = parsed[column.key]
      widths[column.key] = typeof value === 'number' && Number.isFinite(value) ? Math.max(column.minWidth, value) : defaults[column.key]
      return widths
    }, {} as Record<TKey, number>)
  } catch {
    return defaults
  }
}

function saveContractorColumnWidths<TKey extends string>(storageKey: string, widths: Record<TKey, number>) {
  try {
    window.localStorage.setItem(storageKey, JSON.stringify(widths))
  } catch {
    // Column widths are a UI preference; the table must work if localStorage is unavailable.
  }
}

function loadGarageColumnWidths() {
  return loadContractorColumnWidths(contractorGarageColumnStorageKey, contractorGarageColumnDefinitions)
}

function getContractorRestoreTitle(target: ContractorRestoreTarget) {
  if (target.type === 'garage') {
    return `Гараж ${target.item.number || 'без номера'}`
  }

  if (target.type === 'supplier') {
    return target.item.name || 'Поставщик без названия'
  }

  if (target.type === 'department') {
    return `Отдел ${target.item.name || 'без названия'}`
  }

  return target.item.fullName || 'Сотрудник без имени'
}

function applyGarageOwner(row: ContractorGarageRow, owner?: OwnerDto | null): ContractorGarageRow {
  return owner
    ? { ...row, owner: row.owner || owner.fullName, phone: owner.phone ?? '', address: owner.address ?? '', meters: owner.meterNotes ?? '' }
    : row
}

function FinancialReportPeriodFilters({ filters, targetLabel, onChange }: { filters: FinancialReportFilters; targetLabel: string; onChange: (filters: FinancialReportFilters) => void }) {
  return (
    <div className="balance-history-filters">
      <label>
        Период с
        <LocalizedDatePicker ariaLabel={`Начало периода финансового отчета ${targetLabel}`} mode="month" value={filters.monthFrom} onChange={(monthFrom) => onChange({ ...filters, monthFrom })} required />
      </label>
      <label>
        Период по
        <LocalizedDatePicker ariaLabel={`Конец периода финансового отчета ${targetLabel}`} mode="month" value={filters.monthTo} onChange={(monthTo) => onChange({ ...filters, monthTo })} required />
      </label>
      <ReportPeriodQuickSelect
        mode="month"
        valueFrom={filters.monthFrom}
        valueTo={filters.monthTo}
        className="balance-history-filters__quick-periods"
        onSelect={({ monthFrom, monthTo }) => onChange({ monthFrom, monthTo })}
      />
    </div>
  )
}

export function ContractorsPrototypePanel({ auth, dictionaryClient, financeClient, fundsClient, integrationClient, initialTarget = null, onOpenAudit }: { auth: AuthResponse; dictionaryClient: DictionaryClient; financeClient: FinanceClient; fundsClient: FundsClient; integrationClient: IntegrationClient; initialTarget?: ContractorOpenTarget | null; onOpenAudit: (preset: AuditPanelPreset) => void }) {
  const [activeSection, setActiveSection] = useState<ContractorSection>(initialTarget?.section ?? 'garages')
  const [showGarageDebtorsOnly, setShowGarageDebtorsOnly] = useState(false)
  const [garageColumnFilterForm, setGarageColumnFilterForm] = useState<GarageColumnFilterForm>(emptyGarageColumnFilterForm)
  const [garageColumnFilters, setGarageColumnFilters] = useState<GarageColumnFilters>({})
  const [contractorSort, setContractorSort] = useState<ContractorSortState>({ section: 'garages', key: 'number', direction: 'asc' })
  const [garages, setGarages] = useState<ContractorGarageRow[]>([])
  const [garagePage, setGaragePage] = useState<ContractorPageState>(createContractorPageState)
  const [owners, setOwners] = useState<OwnerDto[]>([])
  const [suppliers, setSuppliers] = useState<ContractorSupplierRow[]>([])
  const [supplierPage, setSupplierPage] = useState<ContractorPageState>(createContractorPageState)
  const [supplierContacts, setSupplierContacts] = useState<SupplierContactDto[]>([])
  const [contractorPageLoading, setContractorPageLoading] = useState<Record<ContractorSection, boolean>>({ garages: true, suppliers: true, staff: true })
  const activeContractorPageLoading = contractorPageLoading[activeSection]
  const [staff, setStaff] = useState<ContractorStaffRow[]>([])
  const [staffPage, setStaffPage] = useState<ContractorPageState>(createContractorPageState)
  const [departments, setDepartments] = useState<ContractorDepartmentRow[]>([])
  const [departmentPageNumber, setDepartmentPageNumber] = useState(1)
  const [departmentPageSize, setDepartmentPageSize] = useState(10)
  const [supplierGroups, setSupplierGroups] = useState<SupplierGroupDto[]>([])
  const [chargeServices, setChargeServices] = useState<ChargeServiceSettingDto[]>([])
  const [serviceIncomeTypes, setServiceIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [serviceFunds, setServiceFunds] = useState<FundOptionDto[]>([])
  const [serviceTariffs, setServiceTariffs] = useState<TariffDto[]>([])
  const [serviceSaving, setServiceSaving] = useState(false)
  const [formStateError, setFormStateError] = useState<string | null>(null)
  const [sectionReloadRevision, setSectionReloadRevision] = useState(0)
  const [modal, setModal] = useState<ContractorModal | null>(null)
  const [restoreTarget, setRestoreTarget] = useState<ContractorRestoreTarget | null>(null)
  const [confirmationSaving, setConfirmationSaving] = useState(false)
  const [confirmationError, setConfirmationError] = useState<string | null>(null)
  const [openingBalanceAdjustmentTarget, setOpeningBalanceAdjustmentTarget] = useState<OpeningBalanceAdjustmentTarget | null>(null)
  const [garageColumnWidths, setGarageColumnWidths] = useState(loadGarageColumnWidths)
  const [supplierColumnWidths, setSupplierColumnWidths] = useState(() => loadContractorColumnWidths(contractorSupplierColumnStorageKey, contractorSupplierColumnDefinitions))
  const [staffColumnWidths, setStaffColumnWidths] = useState(() => loadContractorColumnWidths(contractorStaffColumnStorageKey, contractorStaffColumnDefinitions))
  const [garageContextMenu, setGarageContextMenu] = useState<{ row: ContractorGarageRow; x: number; y: number } | null>(null)
  const [garageDeleteTarget, setGarageDeleteTarget] = useState<ContractorGarageRow | null>(null)
  const [garageDeleteReason, setGarageDeleteReason] = useState('')
  const [garageFinancialReportTarget, setGarageFinancialReportTarget] = useState<ContractorGarageRow | null>(null)
  const [garageFinancialReport, setGarageFinancialReport] = useState<GarageBalanceHistoryDto | null>(null)
  const [garageFinancialReportFilters, setGarageFinancialReportFilters] = useState(() => createDefaultGarageBalanceHistoryFilters())
  const [garageFinancialReportLoading, setGarageFinancialReportLoading] = useState(false)
  const [garageFinancialReportError, setGarageFinancialReportError] = useState<string | null>(null)
  const financialReportRequestSequenceRef = useRef(0)
  const financialReportRequestControllerRef = useRef<AbortController | null>(null)
  const [contractorFinancialReportTarget, setContractorFinancialReportTarget] = useState<ContractorFinancialReportTarget | null>(null)
  const [contractorFinancialReport, setContractorFinancialReport] = useState<ContractorFinancialReport | null>(null)
  const [contractorFinancialReportFilters, setContractorFinancialReportFilters] = useState(() => createDefaultGarageBalanceHistoryFilters())
  const [contractorFinancialReportLoading, setContractorFinancialReportLoading] = useState(false)
  const [contractorFinancialReportError, setContractorFinancialReportError] = useState<string | null>(null)
  const [supplierContextMenu, setSupplierContextMenu] = useState<{ row: ContractorSupplierRow; x: number; y: number } | null>(null)
  const [supplierDeleteTarget, setSupplierDeleteTarget] = useState<ContractorSupplierRow | null>(null)
  const [supplierDeleteReason, setSupplierDeleteReason] = useState('')
  const [employeeContextMenu, setEmployeeContextMenu] = useState<{ row: ContractorStaffRow; x: number; y: number } | null>(null)
  const [employeeDeleteTarget, setEmployeeDeleteTarget] = useState<ContractorStaffRow | null>(null)
  const [employeeDeleteReason, setEmployeeDeleteReason] = useState('')
  const [departmentContextMenu, setDepartmentContextMenu] = useState<{ row: ContractorDepartmentRow; x: number; y: number } | null>(null)
  const [departmentDeleteTarget, setDepartmentDeleteTarget] = useState<ContractorDepartmentRow | null>(null)
  const [departmentDeleteReason, setDepartmentDeleteReason] = useState('')
  const openedInitialTargetRef = useRef<string | null>(null)
  const loadedContractorSectionsRef = useRef<Record<ContractorSection, boolean>>({ garages: false, suppliers: false, staff: false })
  const loadedContractorReferencesRef = useRef<Record<'garages' | 'suppliers', boolean>>({ garages: false, suppliers: false })
  const contractorReferenceRequestsRef = useRef<Partial<Record<'garages' | 'suppliers', Promise<boolean>>>>({})
  const contractorReferenceControllersRef = useRef<Partial<Record<'garages' | 'suppliers', AbortController>>>({})
  const [contractorReferenceLoading, setContractorReferenceLoading] = useState<'garages' | 'suppliers' | null>(null)
  const ownersRef = useRef<OwnerDto[]>([])
  const supplierContactsRef = useRef<SupplierContactDto[]>([])
  const loadedSupplierContactsRef = useRef(new Set<string>())
  const supplierEditorRequestSequenceRef = useRef(0)
  const supplierEditorRequestControllerRef = useRef<AbortController | null>(null)
  const [supplierEditorLoadingId, setSupplierEditorLoadingId] = useState<string | null>(null)
  const garagePageRequestSequenceRef = useRef(0)
  const garagePageRequestControllerRef = useRef<AbortController | null>(null)
  const supplierPageRequestControllerRef = useRef<AbortController | null>(null)
  const staffPageRequestControllerRef = useRef<AbortController | null>(null)
  useRestoreFocusOnClose(Boolean(restoreTarget))
  useRestoreFocusOnClose(Boolean(garageDeleteTarget))
  useRestoreFocusOnClose(Boolean(garageFinancialReportTarget))
  useRestoreFocusOnClose(Boolean(contractorFinancialReportTarget))
  useRestoreFocusOnClose(Boolean(supplierDeleteTarget))
  useRestoreFocusOnClose(Boolean(employeeDeleteTarget))
  useRestoreFocusOnClose(Boolean(departmentDeleteTarget))
  useEffect(() => () => {
    garagePageRequestControllerRef.current?.abort()
    supplierPageRequestControllerRef.current?.abort()
    staffPageRequestControllerRef.current?.abort()
    contractorReferenceControllersRef.current.garages?.abort()
    contractorReferenceControllersRef.current.suppliers?.abort()
    supplierEditorRequestControllerRef.current?.abort()
    financialReportRequestControllerRef.current?.abort()
  }, [])
  useEffect(() => () => {
    if (activeSection === 'garages') {
      garagePageRequestControllerRef.current?.abort()
    } else if (activeSection === 'suppliers') {
      supplierPageRequestControllerRef.current?.abort()
    } else {
      staffPageRequestControllerRef.current?.abort()
    }
    if (activeSection !== 'staff') {
      contractorReferenceControllersRef.current[activeSection]?.abort()
    }
    if (activeSection === 'suppliers') {
      supplierEditorRequestControllerRef.current?.abort()
      supplierEditorRequestSequenceRef.current += 1
    }
  }, [activeSection])
  const restoreDialogRef = useFocusTrap<HTMLElement>(Boolean(restoreTarget))
  const restoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(restoreTarget))
  const garageDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(garageDeleteTarget))
  const garageDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(garageDeleteTarget))
  const garageFinancialReportDialogRef = useFocusTrap<HTMLElement>(Boolean(garageFinancialReportTarget))
  const garageFinancialReportCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(garageFinancialReportTarget))
  const contractorFinancialReportDialogRef = useFocusTrap<HTMLElement>(Boolean(contractorFinancialReportTarget))
  const contractorFinancialReportCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(contractorFinancialReportTarget))
  const supplierDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(supplierDeleteTarget))
  const supplierDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(supplierDeleteTarget))
  const employeeDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(employeeDeleteTarget))
  const employeeDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(employeeDeleteTarget))
  const departmentDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(departmentDeleteTarget))
  const departmentDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(departmentDeleteTarget))
  useEscapeKey(Boolean(restoreTarget) && !confirmationSaving, () => closeRestoreDialog())
  useEscapeKey(Boolean(garageContextMenu), () => setGarageContextMenu(null))
  useEscapeKey(Boolean(garageDeleteTarget), () => closeGarageDeleteDialog())
  useEscapeKey(Boolean(garageFinancialReportTarget), () => closeGarageFinancialReport())
  useEscapeKey(Boolean(contractorFinancialReportTarget), () => closeContractorFinancialReport())
  useEscapeKey(Boolean(supplierContextMenu), () => setSupplierContextMenu(null))
  useEscapeKey(Boolean(supplierDeleteTarget), () => closeSupplierDeleteDialog())
  useEscapeKey(Boolean(employeeContextMenu), () => setEmployeeContextMenu(null))
  useEscapeKey(Boolean(employeeDeleteTarget), () => closeEmployeeDeleteDialog())
  useEscapeKey(Boolean(departmentContextMenu), () => setDepartmentContextMenu(null))
  useEscapeKey(Boolean(departmentDeleteTarget), () => closeDepartmentDeleteDialog())

  const ensureContractorReferences = useCallback((referenceSection: 'garages' | 'suppliers') => {
    if (loadedContractorReferencesRef.current[referenceSection]) {
      return Promise.resolve(true)
    }

    const pendingRequest = contractorReferenceRequestsRef.current[referenceSection]
    if (pendingRequest) {
      return pendingRequest
    }

    const controller = new AbortController()
    contractorReferenceControllersRef.current[referenceSection] = controller
    setContractorReferenceLoading(referenceSection)
    const request = (async () => {
      try {
        if (referenceSection === 'garages') {
          const ownerRows = await dictionaryClient.getOwners(auth.accessToken, undefined, contractorsDictionaryListLimit, true, controller.signal)
          if (controller.signal.aborted) return false
          const ownersById = new Map(ownerRows.map((owner) => [owner.id, owner]))
          ownersRef.current = ownerRows
          setOwners(ownerRows)
          setGarages((current) => current.map((garage) => applyGarageOwner(garage, garage.ownerId ? ownersById.get(garage.ownerId) : null)))
        } else {
          const [groups, loadedChargeServices, loadedIncomeTypes, loadedTariffs, loadedFunds] = await Promise.all([
            dictionaryClient.getSupplierGroups(auth.accessToken, undefined, contractorsDictionaryListLimit, true, controller.signal),
            dictionaryClient.getChargeServiceSettings(auth.accessToken, undefined, contractorsDictionaryListLimit, true, undefined, undefined, controller.signal),
            dictionaryClient.getIncomeTypes(auth.accessToken, undefined, contractorsDictionaryListLimit, true, controller.signal),
            dictionaryClient.getTariffs(auth.accessToken, undefined, contractorsDictionaryListLimit, true, controller.signal),
            fundsClient.getFundOptions(auth.accessToken, controller.signal),
          ])
          if (controller.signal.aborted) return false
          setSupplierGroups(groups)
          setChargeServices(loadedChargeServices)
          setServiceIncomeTypes(loadedIncomeTypes)
          setServiceTariffs(loadedTariffs)
          setServiceFunds(loadedFunds)
        }

        loadedContractorReferencesRef.current[referenceSection] = true
        return true
      } catch (error) {
        if (!controller.signal.aborted) {
          setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить данные для формы контрагента.')
        }
        return false
      } finally {
        if (contractorReferenceControllersRef.current[referenceSection] === controller) {
          delete contractorReferenceControllersRef.current[referenceSection]
          delete contractorReferenceRequestsRef.current[referenceSection]
          setContractorReferenceLoading((current) => current === referenceSection ? null : current)
        }
      }
    })()
    contractorReferenceRequestsRef.current[referenceSection] = request
    return request
  }, [auth.accessToken, dictionaryClient, fundsClient])

  const openSupplierEditor = useCallback(async (row: ContractorSupplierRow) => {
    setSupplierContextMenu(null)
    setFormStateError(null)
    supplierEditorRequestControllerRef.current?.abort()
    const controller = new AbortController()
    supplierEditorRequestControllerRef.current = controller
    const requestSequence = ++supplierEditorRequestSequenceRef.current
    setSupplierEditorLoadingId(row.id)
    try {
      const shouldLoadContacts = isBackendDictionaryId(row.id) && !loadedSupplierContactsRef.current.has(row.id)
      const contactsRequest = shouldLoadContacts
        ? dictionaryClient.getSupplierContacts(auth.accessToken, row.id, undefined, contractorsDictionaryListLimit, true, controller.signal)
        : Promise.resolve<SupplierContactDto[] | null>(null)
      const [referencesReady, loadedContacts] = await Promise.all([
        ensureContractorReferences('suppliers'),
        contactsRequest,
      ])
      if (controller.signal.aborted || !referencesReady || requestSequence !== supplierEditorRequestSequenceRef.current) return

      if (!isBackendDictionaryId(row.id)) {
        setModal({ type: 'supplier', item: row })
        return
      }

      if (loadedSupplierContactsRef.current.has(row.id)) {
        const cachedContacts = supplierContactsRef.current
          .filter((contact) => contact.supplierId === row.id)
          .map(createSupplierContactFromDto)
        setModal({ type: 'supplier', item: normalizeSupplierPrototype({ ...row, contacts: cachedContacts }) })
        return
      }

      const contacts = loadedContacts ?? []

      const contactRows = contacts.map(createSupplierContactFromDto)
      const nextContacts = [
        ...supplierContactsRef.current.filter((contact) => contact.supplierId !== row.id),
        ...contacts,
      ]
      supplierContactsRef.current = nextContacts
      loadedSupplierContactsRef.current.add(row.id)
      setSupplierContacts(nextContacts)
      const nextRow = normalizeSupplierPrototype({ ...row, contacts: contactRows })
      setModal({ type: 'supplier', item: nextRow })
    } catch (error) {
      if (!controller.signal.aborted && requestSequence === supplierEditorRequestSequenceRef.current) {
        setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить контакты поставщика.')
      }
    } finally {
      if (supplierEditorRequestControllerRef.current === controller) {
        supplierEditorRequestControllerRef.current = null
        setSupplierEditorLoadingId(null)
      }
    }
  }, [auth.accessToken, dictionaryClient, ensureContractorReferences])

  useEffect(() => {
    if (loadedContractorSectionsRef.current[activeSection]) {
      setContractorPageLoading((current) => ({ ...current, [activeSection]: false }))
      return
    }

    let cancelled = false
    const controller = new AbortController()
    async function loadActiveContractorSection() {
      let garageRequestSequence: number | null = null
      const isCurrentRequest = () => !cancelled && (garageRequestSequence === null || garageRequestSequence === garagePageRequestSequenceRef.current)
      setContractorPageLoading((current) => ({ ...current, [activeSection]: true }))
      try {
        if (activeSection === 'garages') {
          garageRequestSequence = ++garagePageRequestSequenceRef.current
          const garageRows = await (dictionaryClient.getGaragesPage
            ? dictionaryClient.getGaragesPage(auth.accessToken, undefined, 0, contractorsDefaultPageSize, true, undefined, undefined, false, undefined, controller.signal)
            : dictionaryClient.getGarages(auth.accessToken, undefined, contractorsDictionaryListLimit, true, controller.signal).then((items) => createFallbackPage(items, 0, contractorsDefaultPageSize)))
          if (isCurrentRequest()) {
            setGarages(garageRows.items.map((garage) => createGarageRowFromDto(garage, ownersRef.current)))
            setGaragePage({ totalCount: garageRows.totalCount, offset: garageRows.offset, limit: garageRows.limit })
          }
        } else if (activeSection === 'suppliers') {
          const supplierRows = await (dictionaryClient.getSuppliersPage
            ? dictionaryClient.getSuppliersPage(auth.accessToken, undefined, undefined, 0, contractorsDefaultPageSize, true, undefined, undefined, controller.signal)
            : dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, contractorsDictionaryListLimit, true, controller.signal).then((items) => createFallbackPage(items, 0, contractorsDefaultPageSize)))
          if (!cancelled) {
            setSuppliers(supplierRows.items.map((supplier) => createSupplierRowFromDto(supplier, supplierContactsRef.current)))
            setSupplierPage({ totalCount: supplierRows.totalCount, offset: supplierRows.offset, limit: supplierRows.limit })
          }
        } else {
          const [departmentRows, staffRows] = await Promise.all([
            dictionaryClient.getStaffDepartments(auth.accessToken, contractorsDictionaryListLimit, true, controller.signal),
            dictionaryClient.getStaffMembersPage
              ? dictionaryClient.getStaffMembersPage(auth.accessToken, undefined, undefined, 0, contractorsDefaultPageSize, true, undefined, undefined, controller.signal)
              : dictionaryClient.getStaffMembers(auth.accessToken, undefined, undefined, contractorsDictionaryListLimit, true, controller.signal).then((items) => createFallbackPage(items, 0, contractorsDefaultPageSize)),
          ])
          if (!cancelled) {
            setDepartments(departmentRows.map(createStaffDepartmentRowFromDto))
            setStaff(staffRows.items.map(createStaffRowFromDto))
            setStaffPage({ totalCount: staffRows.totalCount, offset: staffRows.offset, limit: staffRows.limit })
          }
        }

        if (isCurrentRequest()) {
          loadedContractorSectionsRef.current[activeSection] = true
        }
      } catch (error) {
        if (isCurrentRequest()) {
          setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить контрагентов из справочников.')
        }
      } finally {
        if (isCurrentRequest()) {
          setContractorPageLoading((current) => ({ ...current, [activeSection]: false }))
        }
      }
    }

    void loadActiveContractorSection()
    return () => {
      cancelled = true
      controller.abort()
    }
  }, [activeSection, auth.accessToken, dictionaryClient, sectionReloadRevision])

  useEffect(() => {
    if (!initialTarget) {
      openedInitialTargetRef.current = null
      return
    }

    const targetKey = `${initialTarget.section}:${initialTarget.entityId ?? ''}:${initialTarget.garageNumber ?? ''}:${initialTarget.displayName ?? ''}`
    if (openedInitialTargetRef.current === targetKey) {
      return
    }

    let nextSection: ContractorSection
    let nextModal: ContractorModal
    let closeContextMenu: () => void

    if (initialTarget.section === 'garages') {
      const targetGarage = findGarageForOpenTarget(garages, initialTarget)
      if (!targetGarage) {
        return
      }

      nextSection = 'garages'
      nextModal = { type: 'garage', item: targetGarage }
      closeContextMenu = () => setGarageContextMenu(null)
    } else if (initialTarget.section === 'suppliers') {
      const targetSupplier = findSupplierForOpenTarget(suppliers, initialTarget)
      if (!targetSupplier) {
        return
      }

      nextSection = 'suppliers'
      nextModal = { type: 'supplier', item: targetSupplier }
      closeContextMenu = () => setSupplierContextMenu(null)
    } else {
      const targetEmployee = findStaffForOpenTarget(staff, initialTarget)
      if (!targetEmployee) {
        return
      }

      nextSection = 'staff'
      nextModal = { type: 'employee', item: targetEmployee }
      closeContextMenu = () => setEmployeeContextMenu(null)
    }

    openedInitialTargetRef.current = targetKey
    const handle = window.setTimeout(() => {
      setActiveSection(nextSection)
      closeContextMenu()
      if (nextModal.type === 'supplier' && nextModal.item) {
        void openSupplierEditor(nextModal.item)
      } else if (nextModal.type === 'garage') {
        void ensureContractorReferences('garages').then((referencesReady) => {
          if (referencesReady) {
            const row = nextModal.item
            setModal(row
              ? { type: 'garage', item: applyGarageOwner(row, row.ownerId ? ownersRef.current.find((owner) => owner.id === row.ownerId) : null) }
              : nextModal)
          }
        })
      } else {
        setModal(nextModal)
      }
    }, 0)

    return () => window.clearTimeout(handle)
  }, [ensureContractorReferences, garages, initialTarget, openSupplierEditor, staff, suppliers])

  useEffect(() => {
    saveContractorColumnWidths(contractorGarageColumnStorageKey, garageColumnWidths)
  }, [garageColumnWidths])

  useEffect(() => {
    saveContractorColumnWidths(contractorSupplierColumnStorageKey, supplierColumnWidths)
  }, [supplierColumnWidths])

  useEffect(() => {
    saveContractorColumnWidths(contractorStaffColumnStorageKey, staffColumnWidths)
  }, [staffColumnWidths])

  const garageTableStyle = useMemo(() => {
    return contractorGarageColumnDefinitions.reduce<CSSProperties>((style, column) => {
      return { ...style, [`--garage-col-${column.key}`]: `${garageColumnWidths[column.key]}px` }
    }, {})
  }, [garageColumnWidths])

  const supplierTableStyle = useMemo(() => {
    return contractorSupplierColumnDefinitions.reduce<CSSProperties>((style, column) => {
      return { ...style, [`--supplier-col-${column.key}`]: `${supplierColumnWidths[column.key]}px` }
    }, {})
  }, [supplierColumnWidths])

  const staffTableStyle = useMemo(() => {
    return contractorStaffColumnDefinitions.reduce<CSSProperties>((style, column) => {
      return { ...style, [`--staff-col-${column.key}`]: `${staffColumnWidths[column.key]}px` }
    }, {})
  }, [staffColumnWidths])
  const canReadContractorHistory = hasPermission(auth, permissions.auditRead)
  const canManageTariffs = hasPermission(auth, permissions.tariffsManage)
  const canAdjustOpeningData = hasPermission(auth, permissions.openingDataAdjust)
  const canUseGarageColumnFilters = isAdministrator(auth)

  function retryActiveContractorSection() {
    setFormStateError(null)
    if (activeSection !== 'staff' && loadedContractorSectionsRef.current[activeSection] && !loadedContractorReferencesRef.current[activeSection]) {
      void ensureContractorReferences(activeSection)
      return
    }
    loadedContractorSectionsRef.current[activeSection] = false
    if (activeSection !== 'staff') {
      loadedContractorReferencesRef.current[activeSection] = false
    }
    setSectionReloadRevision((value) => value + 1)
  }

  async function loadGaragePage(
    offset = garagePage.offset,
    limit = garagePage.limit,
    sort: ContractorSortState = contractorSort.section === 'garages' && isGarageServerSortKey(contractorSort.key)
      ? contractorSort
      : { section: 'garages', key: 'number', direction: 'asc' },
    debtorsOnly = showGarageDebtorsOnly,
    filters = garageColumnFilters,
  ) {
    garagePageRequestControllerRef.current?.abort()
    const controller = new AbortController()
    garagePageRequestControllerRef.current = controller
    const effectiveFilters = canUseGarageColumnFilters ? filters : {}
    const requestSequence = ++garagePageRequestSequenceRef.current
    setContractorPageLoading((current) => ({ ...current, garages: true }))
    setGarageContextMenu(null)
    try {
      const page = dictionaryClient.getGaragesPage
        ? await (hasGarageColumnFilters(effectiveFilters)
            ? dictionaryClient.getGaragesPage(auth.accessToken, undefined, offset, limit, true, sort.key, sort.direction, debtorsOnly, effectiveFilters, controller.signal)
            : dictionaryClient.getGaragesPage(auth.accessToken, undefined, offset, limit, true, sort.key, sort.direction, debtorsOnly, undefined, controller.signal))
        : createFallbackPage(
            (await dictionaryClient.getGarages(auth.accessToken, undefined, contractorsDictionaryListLimit, true, controller.signal))
              .filter((garage) => !debtorsOnly || (!garage.isArchived && garage.overdueDebt > 0))
              .filter((garage) => !effectiveFilters.number || garage.number.toLocaleLowerCase('ru-RU').includes(effectiveFilters.number.toLocaleLowerCase('ru-RU')))
              .filter((garage) => effectiveFilters.peopleCountMin === undefined || garage.peopleCount >= effectiveFilters.peopleCountMin)
              .filter((garage) => effectiveFilters.peopleCountMax === undefined || garage.peopleCount <= effectiveFilters.peopleCountMax)
              .filter((garage) => effectiveFilters.floorCountMin === undefined || garage.floorCount >= effectiveFilters.floorCountMin)
              .filter((garage) => effectiveFilters.floorCountMax === undefined || garage.floorCount <= effectiveFilters.floorCountMax),
            offset,
            limit,
          )
      if (controller.signal.aborted || requestSequence !== garagePageRequestSequenceRef.current) {
        return null
      }
      setGarages(page.items.map((garage) => createGarageRowFromDto(garage, owners)))
      setGaragePage({ totalCount: page.totalCount, offset: page.offset, limit: page.limit })
      loadedContractorSectionsRef.current.garages = true
      setFormStateError(null)
      return true
    } catch (error) {
      if (controller.signal.aborted || requestSequence !== garagePageRequestSequenceRef.current) {
        return null
      }
      setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить страницу гаражей.')
      return false
    } finally {
      if (requestSequence === garagePageRequestSequenceRef.current) {
        setContractorPageLoading((current) => ({ ...current, garages: false }))
      }
    }
  }

  async function loadSupplierPage(
    offset = supplierPage.offset,
    limit = supplierPage.limit,
    sort: ContractorSortState = contractorSort.section === 'suppliers' && isSupplierServerSortKey(contractorSort.key)
      ? contractorSort
      : { section: 'suppliers', key: 'service', direction: 'asc' },
  ) {
    supplierPageRequestControllerRef.current?.abort()
    const controller = new AbortController()
    supplierPageRequestControllerRef.current = controller
    setContractorPageLoading((current) => ({ ...current, suppliers: true }))
    setSupplierContextMenu(null)
    try {
      const page = dictionaryClient.getSuppliersPage
        ? await dictionaryClient.getSuppliersPage(auth.accessToken, undefined, undefined, offset, limit, true, sort.key, sort.direction, controller.signal)
        : createFallbackPage(await dictionaryClient.getSuppliers(auth.accessToken, undefined, undefined, contractorsDictionaryListLimit, true, controller.signal), offset, limit)
      if (controller.signal.aborted) {
        return
      }
      const nextSuppliers = page.items.map((supplier) => createSupplierRowFromDto(supplier, supplierContacts))
      setSuppliers(nextSuppliers)
      setSupplierPage({ totalCount: page.totalCount, offset: page.offset, limit: page.limit })
    } catch (error) {
      if (controller.signal.aborted) {
        return
      }
      setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить страницу поставщиков.')
    } finally {
      if (supplierPageRequestControllerRef.current === controller) {
        setContractorPageLoading((current) => ({ ...current, suppliers: false }))
      }
    }
  }

  async function loadStaffPage(
    offset = staffPage.offset,
    limit = staffPage.limit,
    sort: ContractorSortState = contractorSort.section === 'staff'
      ? contractorSort
      : { section: 'staff', key: 'fullName', direction: 'asc' },
  ) {
    staffPageRequestControllerRef.current?.abort()
    const controller = new AbortController()
    staffPageRequestControllerRef.current = controller
    setContractorPageLoading((current) => ({ ...current, staff: true }))
    setEmployeeContextMenu(null)
    try {
      const page = dictionaryClient.getStaffMembersPage
        ? await dictionaryClient.getStaffMembersPage(auth.accessToken, undefined, undefined, offset, limit, true, sort.key, sort.direction, controller.signal)
        : createFallbackPage(await dictionaryClient.getStaffMembers(auth.accessToken, undefined, undefined, contractorsDictionaryListLimit, true, controller.signal), offset, limit)
      if (controller.signal.aborted) {
        return
      }
      setStaff(page.items.map(createStaffRowFromDto))
      setStaffPage({ totalCount: page.totalCount, offset: page.offset, limit: page.limit })
    } catch (error) {
      if (controller.signal.aborted) {
        return
      }
      setFormStateError(error instanceof Error ? error.message : 'Не удалось загрузить страницу персонала.')
    } finally {
      if (staffPageRequestControllerRef.current === controller) {
        setContractorPageLoading((current) => ({ ...current, staff: false }))
      }
    }
  }

  const garageColumnResize = useColumnResize(contractorGarageColumnDefinitions, garageColumnWidths, setGarageColumnWidths)
  const supplierColumnResize = useColumnResize(contractorSupplierColumnDefinitions, supplierColumnWidths, setSupplierColumnWidths)
  const staffColumnResize = useColumnResize(contractorStaffColumnDefinitions, staffColumnWidths, setStaffColumnWidths)

  const saveGarage = async (garage: ContractorGarageRow) => {
    const currentGarage = garages.find((item) => item.id === garage.id)

    try {
      const savedOwner = await resolveGarageOwner(dictionaryClient, auth.accessToken, owners, garage)
      if (savedOwner) {
        setOwners((currentOwners) => {
          if (currentOwners.some((owner) => owner.id === savedOwner.id)) {
            return currentOwners.map((owner) => (owner.id === savedOwner.id ? savedOwner : owner))
          }

          return [...currentOwners, savedOwner]
        })
      }

      const request = createGarageRequestFromRow(garage, savedOwner?.id ?? null)
      const savedGarage = isBackendDictionaryId(garage.id)
        ? await dictionaryClient.updateGarage(auth.accessToken, garage.id, request)
        : await dictionaryClient.createGarage(auth.accessToken, request)
      const nextGarage = createGarageRowFromDto(savedGarage, savedOwner ? [...owners.filter((owner) => owner.id !== savedOwner.id), savedOwner] : owners)

      setGarages((currentGarages) => {
        if (currentGarage) {
          return currentGarages.map((item) => (item.id === garage.id ? nextGarage : item))
        }

        return [...currentGarages.slice(0, Math.max(0, garagePage.limit - 1)), nextGarage]
      })
      if (!currentGarage) {
        setGaragePage((currentPage) => ({ ...currentPage, totalCount: currentPage.totalCount + 1 }))
      }
      return
    } catch (error) {
      const saveError = error instanceof Error ? error : new Error('Не удалось сохранить гараж.')
      throw saveError
    }
  }

  const deleteGarage = async (garage: ContractorGarageRow, reason: string) => {
    if (isBackendDictionaryId(garage.id)) {
      await dictionaryClient.archiveGarage(auth.accessToken, garage.id, reason)
    }

    setGarages((currentGarages) => currentGarages.map((item) => (item.id === garage.id ? { ...item, isDeleted: true } : item)))
  }

  function openGarageContextMenu(event: MouseEvent<HTMLDivElement>, row: ContractorGarageRow) {
    event.preventDefault()
    setGarageContextMenu({ row, x: event.clientX, y: event.clientY })
  }

  async function openGarageEditor(row: ContractorGarageRow) {
    setGarageContextMenu(null)
    setFormStateError(null)
    if (await ensureContractorReferences('garages')) {
      setModal({ type: 'garage', item: applyGarageOwner(row, row.ownerId ? ownersRef.current.find((owner) => owner.id === row.ownerId) : null) })
    }
  }

  async function openGarageCreator() {
    setFormStateError(null)
    if (await ensureContractorReferences('garages')) {
      setModal({ type: 'garage' })
    }
  }

  async function openSupplierCreator() {
    setFormStateError(null)
    if (await ensureContractorReferences('suppliers')) {
      setModal({ type: 'supplier' })
    }
  }

  async function openServiceCreator() {
    setFormStateError(null)
    if (await ensureContractorReferences('suppliers')) {
      setModal({ type: 'service' })
    }
  }

  function openGarageDeleteDialog(row: ContractorGarageRow) {
    setGarageContextMenu(null)
    setGarageDeleteTarget(row)
    setGarageDeleteReason('')
    setConfirmationError(null)
  }

  function closeGarageDeleteDialog() {
    if (confirmationSaving) {
      return
    }
    setGarageDeleteTarget(null)
    setGarageDeleteReason('')
    setConfirmationError(null)
  }

  async function runConfirmation(action: () => Promise<void>, onSuccess: () => void, fallbackError: string) {
    setConfirmationSaving(true)
    setConfirmationError(null)
    try {
      await action()
      onSuccess()
    } catch (error) {
      setConfirmationError(error instanceof Error ? error.message : fallbackError)
    } finally {
      setConfirmationSaving(false)
    }
  }

  async function confirmGarageDeleteFromTable() {
    if (!garageDeleteTarget || !garageDeleteReason.trim()) {
      return
    }

    await runConfirmation(
      () => deleteGarage(garageDeleteTarget, garageDeleteReason.trim()),
      () => {
      setGarageDeleteTarget(null)
      setGarageDeleteReason('')
      },
      'Не удалось удалить гараж.',
    )
  }

  function restoreGarage(row: ContractorGarageRow) {
    setGarageContextMenu(null)
    setConfirmationError(null)
    setRestoreTarget({ type: 'garage', item: row })
  }

  function beginFinancialReportRequest(): FinancialReportRequest {
    financialReportRequestControllerRef.current?.abort()
    const controller = new AbortController()
    const sequence = ++financialReportRequestSequenceRef.current
    financialReportRequestControllerRef.current = controller
    return { controller, sequence }
  }

  function isCurrentFinancialReportRequest(request: FinancialReportRequest) {
    return !request.controller.signal.aborted
      && request.sequence === financialReportRequestSequenceRef.current
      && financialReportRequestControllerRef.current === request.controller
  }

  function finishFinancialReportRequest(request: FinancialReportRequest) {
    if (financialReportRequestControllerRef.current === request.controller) {
      financialReportRequestControllerRef.current = null
    }
  }

  function cancelFinancialReportRequest() {
    financialReportRequestSequenceRef.current += 1
    financialReportRequestControllerRef.current?.abort()
    financialReportRequestControllerRef.current = null
  }

  async function loadGarageFinancialReport(
    row = garageFinancialReportTarget,
    filters = garageFinancialReportFilters,
    existingRequest?: FinancialReportRequest,
  ) {
    if (!row) {
      return
    }

    if (!isBackendDictionaryId(row.id)) {
      setGarageFinancialReport(null)
      setGarageFinancialReportError('Финансовый отчет доступен для гаража, сохраненного в справочнике.')
      setGarageFinancialReportLoading(false)
      return
    }

    const request = existingRequest ?? beginFinancialReportRequest()
    setGarageFinancialReportLoading(true)
    setGarageFinancialReportError(null)

    try {
      const report = await financeClient.getGarageBalanceHistory(auth.accessToken, row.id, filters, request.controller.signal)
      if (!isCurrentFinancialReportRequest(request)) return
      setGarageFinancialReport(report)
    } catch (error) {
      if (!isCurrentFinancialReportRequest(request)) return
      setGarageFinancialReportError(error instanceof Error ? error.message : 'Не удалось загрузить финансовый отчет гаража.')
      setGarageFinancialReport(null)
    } finally {
      if (isCurrentFinancialReportRequest(request)) {
        setGarageFinancialReportLoading(false)
        finishFinancialReportRequest(request)
      }
    }
  }

  function applyGarageFinancialReportFilters(filters: FinancialReportFilters) {
    setGarageFinancialReportFilters(filters)
    void loadGarageFinancialReport(garageFinancialReportTarget, filters)
  }

  async function openGarageFinancialReport(row: ContractorGarageRow) {
    setGarageContextMenu(null)
    const fallbackFilters = createDefaultGarageBalanceHistoryFilters()
    const request = beginFinancialReportRequest()
    setGarageFinancialReportTarget(row)
    setGarageFinancialReportFilters(fallbackFilters)
    setGarageFinancialReport(null)
    setGarageFinancialReportError(null)
    setGarageFinancialReportLoading(true)

    try {
      const period = await financeClient.getFinancialReportPeriod(auth.accessToken, { garageId: row.id }, request.controller.signal)
      if (!isCurrentFinancialReportRequest(request)) return
      const filters = createFullFinancialReportFilters(period)
      setGarageFinancialReportFilters(filters)
      await loadGarageFinancialReport(row, filters, request)
    } catch {
      if (!isCurrentFinancialReportRequest(request)) return
      // The period endpoint is an optimization. A temporary failure must not block
      // opening the report itself, so the standard period remains a safe fallback.
      await loadGarageFinancialReport(row, fallbackFilters, request)
    }
  }

  function closeGarageFinancialReport() {
    cancelFinancialReportRequest()
    setGarageFinancialReportTarget(null)
    setGarageFinancialReport(null)
    setGarageFinancialReportError(null)
    setGarageFinancialReportLoading(false)
  }

  async function loadContractorFinancialReport(
    target = contractorFinancialReportTarget,
    filters = contractorFinancialReportFilters,
    existingRequest?: FinancialReportRequest,
  ) {
    if (!target) {
      return
    }

    if (!isBackendDictionaryId(target.row.id)) {
      setContractorFinancialReport(null)
      setContractorFinancialReportError('Финансовый отчет доступен для записи, сохраненной в справочнике.')
      return
    }

    const request = existingRequest ?? beginFinancialReportRequest()
    setContractorFinancialReportLoading(true)
    setContractorFinancialReportError(null)

    try {
      const operationsRequest = financeClient.getOperationsPage(auth.accessToken, {
        monthFrom: filters.monthFrom,
        monthTo: filters.monthTo,
        operationKind: 'expense',
        supplierId: target.type === 'supplier' ? target.row.id : undefined,
        staffMemberId: target.type === 'employee' ? target.row.id : undefined,
        limit: 500,
      }, request.controller.signal)
      const createOperationEntries = (operationsPage: Awaited<ReturnType<FinanceClient['getOperationsPage']>>) => operationsPage.items
        .filter((operation) => !operation.isCanceled)
        .map((operation) => ({
          id: `operation-${operation.id}`,
          accountingMonth: operation.accountingMonth,
          date: operation.operationDate,
          documentNumber: operation.documentNumber ?? '—',
          description: target.type === 'supplier'
            ? operation.expenseTypeName ?? operation.comment ?? 'Выплата поставщику'
            : operation.expenseTypeName ?? operation.comment ?? 'Выплата сотруднику',
          accrualAmount: 0,
          paymentAmount: operation.amount,
        }))

      if (target.type === 'supplier') {
        const [operationsPage, accrualsPage, openingBalance] = await Promise.all([
          operationsRequest,
          financeClient.getSupplierAccrualsPage(auth.accessToken, {
            monthFrom: filters.monthFrom,
            monthTo: filters.monthTo,
            supplierId: target.row.id,
            limit: 500,
          }, request.controller.signal),
          financeClient.getSupplierOpeningBalance(auth.accessToken, target.row.id, filters.monthFrom, request.controller.signal),
        ])
        const operationEntries = createOperationEntries(operationsPage)
        const accrualEntries = accrualsPage.items
          .filter((accrual) => !accrual.isCanceled)
          .map((accrual) => ({
            id: `supplier-accrual-${accrual.id}`,
            accountingMonth: accrual.accountingMonth,
            date: accrual.accountingMonth,
            documentNumber: accrual.documentNumber ?? '—',
            description: accrual.expenseTypeName,
            accrualAmount: accrual.amount,
            paymentAmount: 0,
          }))
        const openingBalanceEntries = createSupplierOpeningBalanceEntries(
          target.row.id,
          openingBalance.openingBalance,
          filters.monthFrom,
          openingBalance.priorAccrualTotal !== 0 || openingBalance.priorPaymentTotal !== 0,
        )
        if (!isCurrentFinancialReportRequest(request)) return
        setContractorFinancialReport(buildContractorFinancialReport(
          [...openingBalanceEntries, ...accrualEntries, ...operationEntries],
          openingBalance.openingBalance,
        ))
      } else {
        const operationsPage = await operationsRequest
        const operationEntries = createOperationEntries(operationsPage)
        const staffAccrualEntries = createStaffFinancialReportEntries(target.row, filters.monthFrom, filters.monthTo)
        if (!isCurrentFinancialReportRequest(request)) return
        setContractorFinancialReport(buildContractorFinancialReport([...staffAccrualEntries, ...operationEntries]))
      }
    } catch (error) {
      if (!isCurrentFinancialReportRequest(request)) return
      setContractorFinancialReportError(error instanceof Error ? error.message : 'Не удалось загрузить финансовый отчет контрагента.')
      setContractorFinancialReport(null)
      setContractorFinancialReportLoading(false)
      request.controller.abort()
      finishFinancialReportRequest(request)
    } finally {
      if (isCurrentFinancialReportRequest(request)) {
        setContractorFinancialReportLoading(false)
        finishFinancialReportRequest(request)
      }
    }
  }

  function applyContractorFinancialReportFilters(filters: FinancialReportFilters) {
    setContractorFinancialReportFilters(filters)
    void loadContractorFinancialReport(contractorFinancialReportTarget, filters)
  }

  async function openContractorFinancialReport(target: ContractorFinancialReportTarget) {
    setSupplierContextMenu(null)
    setEmployeeContextMenu(null)
    setModal(null)
    const fallbackFilters = createDefaultGarageBalanceHistoryFilters()
    const request = beginFinancialReportRequest()
    setContractorFinancialReportTarget(target)
    setContractorFinancialReportFilters(fallbackFilters)
    setContractorFinancialReport(null)
    setContractorFinancialReportError(null)
    setContractorFinancialReportLoading(true)

    try {
      const period = await financeClient.getFinancialReportPeriod(auth.accessToken, target.type === 'supplier'
        ? { supplierId: target.row.id }
        : { staffMemberId: target.row.id }, request.controller.signal)
      if (!isCurrentFinancialReportRequest(request)) return
      const filters = createFullFinancialReportFilters(period)
      setContractorFinancialReportFilters(filters)
      await loadContractorFinancialReport(target, filters, request)
    } catch (error) {
      if (!isCurrentFinancialReportRequest(request)) return
      setContractorFinancialReportError(error instanceof Error ? error.message : 'Не удалось определить полный период финансового отчета контрагента.')
      setContractorFinancialReportLoading(false)
      finishFinancialReportRequest(request)
    }
  }

  function closeContractorFinancialReport() {
    cancelFinancialReportRequest()
    setContractorFinancialReportTarget(null)
    setContractorFinancialReport(null)
    setContractorFinancialReportError(null)
    setContractorFinancialReportLoading(false)
  }

  function openContractorHistoryInAudit(target = contractorFinancialReportTarget) {
    if (!target || !isBackendDictionaryId(target.row.id)) {
      return
    }

    closeContractorFinancialReport()
    onOpenAudit({
      section: 'dictionary',
      entityType: target.type === 'supplier' ? 'supplier' : 'staff_member',
      relatedCounterparty: target.row.id,
    })
  }

  const saveSupplier = async (supplier: ContractorSupplierRow) => {
    const normalizedSupplier = normalizeSupplierPrototype(supplier)
    const currentSupplier = suppliers.find((item) => item.id === normalizedSupplier.id)

    try {
      const groups = [...supplierGroups]
      const group = await resolveSupplierGroup(dictionaryClient, auth.accessToken, groups, normalizedSupplier.service)
      const request = createSupplierRequestFromRow(normalizedSupplier, group.id)
      const savedSupplier = isBackendDictionaryId(normalizedSupplier.id)
        ? await dictionaryClient.updateSupplier(auth.accessToken, normalizedSupplier.id, request)
        : await dictionaryClient.createSupplier(auth.accessToken, request)
      const savedContacts: SupplierContactDto[] = []

      for (const contact of normalizedSupplier.contacts) {
        if (contact.isDeleted) {
          if (isBackendDictionaryId(contact.id)) {
            await dictionaryClient.archiveSupplierContact(auth.accessToken, contact.id, contact.deleteReason?.trim() || 'Контакт удален из карточки поставщика.')
          }

          savedContacts.push({
            id: contact.id,
            supplierId: savedSupplier.id,
            supplierName: savedSupplier.name,
            fullName: contact.fullName,
            position: contact.position || null,
            phone: contact.phone || null,
            email: contact.email || null,
            status: contact.status,
            comment: contact.comment || null,
            isArchived: true,
          })
          continue
        }

        if (!contact.fullName.trim()) {
          continue
        }

        const contactRequest = createSupplierContactRequestFromRow(savedSupplier.id, contact)
        const savedContact = isBackendDictionaryId(contact.id)
          ? await dictionaryClient.updateSupplierContact(auth.accessToken, contact.id, contactRequest)
          : await dictionaryClient.createSupplierContact(auth.accessToken, contactRequest)
        savedContacts.push(savedContact)
      }

      const nextSupplier = createSupplierRowFromDto(savedSupplier, savedContacts)
      const nextSupplierContacts = [
        ...supplierContactsRef.current.filter((contact) => contact.supplierId !== savedSupplier.id),
        ...savedContacts,
      ]
      supplierContactsRef.current = nextSupplierContacts
      loadedSupplierContactsRef.current.add(savedSupplier.id)
      setSupplierContacts(nextSupplierContacts)
      setSupplierGroups(groups)
      setSuppliers((currentSuppliers) => {
        if (currentSupplier) {
          return currentSuppliers.map((item) => (item.id === normalizedSupplier.id ? nextSupplier : item))
        }

        return [...currentSuppliers.slice(0, Math.max(0, supplierPage.limit - 1)), nextSupplier]
      })
      if (!currentSupplier) {
        setSupplierPage((currentPage) => ({ ...currentPage, totalCount: currentPage.totalCount + 1 }))
      }
      return
    } catch (error) {
      const saveError = error instanceof Error ? error : new Error('Не удалось сохранить поставщика.')
      throw saveError
    }
  }

  const deleteSupplier = async (supplier: ContractorSupplierRow, reason: string) => {
    if (isBackendDictionaryId(supplier.id)) {
      await dictionaryClient.archiveSupplier(auth.accessToken, supplier.id, reason)
    }

    setSuppliers((currentSuppliers) => currentSuppliers.map((item) => (item.id === supplier.id ? { ...item, isDeleted: true } : item)))
  }

  function openSupplierContextMenu(event: MouseEvent<HTMLDivElement>, row: ContractorSupplierRow) {
    event.preventDefault()
    setSupplierContextMenu({ row, x: event.clientX, y: event.clientY })
  }

  function openSupplierDeleteDialog(row: ContractorSupplierRow) {
    setSupplierContextMenu(null)
    setSupplierDeleteTarget(row)
    setSupplierDeleteReason('')
    setConfirmationError(null)
  }

  function closeSupplierDeleteDialog() {
    if (confirmationSaving) {
      return
    }
    setSupplierDeleteTarget(null)
    setSupplierDeleteReason('')
    setConfirmationError(null)
  }

  async function confirmSupplierDeleteFromTable() {
    if (!supplierDeleteTarget || !supplierDeleteReason.trim()) {
      return
    }

    await runConfirmation(
      () => deleteSupplier(supplierDeleteTarget, supplierDeleteReason.trim()),
      () => { setSupplierDeleteTarget(null); setSupplierDeleteReason('') },
      'Не удалось удалить поставщика.',
    )
  }

  function restoreSupplier(row: ContractorSupplierRow) {
    setSupplierContextMenu(null)
    setConfirmationError(null)
    setRestoreTarget({ type: 'supplier', item: row })
  }

  function openSupplierFinancialReport(row: ContractorSupplierRow) {
    openContractorFinancialReport({ type: 'supplier', row })
  }

  const saveEmployee = async (employee: ContractorStaffRow) => {
    const currentEmployee = staff.find((item) => item.id === employee.id)

    try {
      const department = departments.find((item) => item.name === employee.department)
      let departmentId = department?.id
      if (!departmentId || !isBackendDictionaryId(departmentId)) {
        const savedDepartment = await dictionaryClient.createStaffDepartment(auth.accessToken, { name: employee.department.trim() || 'Без отдела' })
        departmentId = savedDepartment.id
        setDepartments((currentDepartments) => {
          const withoutLocal = department ? currentDepartments.filter((item) => item.id !== department.id) : currentDepartments
          return [...withoutLocal, createStaffDepartmentRowFromDto(savedDepartment)]
        })
      }

      const request = createStaffMemberRequestFromRow(employee, departmentId)
      const savedEmployee = isBackendDictionaryId(employee.id)
        ? await dictionaryClient.updateStaffMember(auth.accessToken, employee.id, request)
        : await dictionaryClient.createStaffMember(auth.accessToken, request)
      const nextEmployee = createStaffRowFromDto(savedEmployee)

      setStaff((currentStaff) => {
        if (currentEmployee) {
          return currentStaff.map((item) => (item.id === employee.id ? nextEmployee : item))
        }

        return [...currentStaff.slice(0, Math.max(0, staffPage.limit - 1)), nextEmployee]
      })
      if (!currentEmployee) {
        setStaffPage((currentPage) => ({ ...currentPage, totalCount: currentPage.totalCount + 1 }))
      }
      return
    } catch (error) {
      const saveError = error instanceof Error ? error : new Error('Не удалось сохранить сотрудника.')
      throw saveError
    }
  }

  const deleteEmployee = async (employee: ContractorStaffRow, reason: string) => {
    if (isBackendDictionaryId(employee.id)) {
      await dictionaryClient.archiveStaffMember(auth.accessToken, employee.id, reason)
    }

    setStaff((currentStaff) => currentStaff.map((item) => (item.id === employee.id ? { ...item, isDeleted: true } : item)))
  }

  const deleteDepartment = async (department: ContractorDepartmentRow, reason: string) => {
    if (isBackendDictionaryId(department.id)) {
      await dictionaryClient.archiveStaffDepartment(auth.accessToken, department.id, reason)
    }

    setDepartments((currentDepartments) => currentDepartments.map((item) => (item.id === department.id ? { ...item, isDeleted: true } : item)))
  }

  function openEmployeeContextMenu(event: MouseEvent<HTMLDivElement>, row: ContractorStaffRow) {
    event.preventDefault()
    setEmployeeContextMenu({ row, x: event.clientX, y: event.clientY })
  }

  function openEmployeeEditor(row: ContractorStaffRow) {
    setEmployeeContextMenu(null)
    setModal({ type: 'employee', item: row })
  }

  function openEmployeeDeleteDialog(row: ContractorStaffRow) {
    setEmployeeContextMenu(null)
    setEmployeeDeleteTarget(row)
    setEmployeeDeleteReason('')
    setConfirmationError(null)
  }

  function closeEmployeeDeleteDialog() {
    if (confirmationSaving) {
      return
    }
    setEmployeeDeleteTarget(null)
    setEmployeeDeleteReason('')
    setConfirmationError(null)
  }

  async function confirmEmployeeDeleteFromTable() {
    if (!employeeDeleteTarget || !employeeDeleteReason.trim()) {
      return
    }

    await runConfirmation(
      () => deleteEmployee(employeeDeleteTarget, employeeDeleteReason.trim()),
      () => { setEmployeeDeleteTarget(null); setEmployeeDeleteReason('') },
      'Не удалось удалить сотрудника.',
    )
  }

  function openDepartmentDeleteDialog(row: ContractorDepartmentRow) {
    setDepartmentContextMenu(null)
    setDepartmentDeleteTarget(row)
    setDepartmentDeleteReason('')
    setConfirmationError(null)
  }

  function closeDepartmentDeleteDialog() {
    if (confirmationSaving) {
      return
    }
    setDepartmentDeleteTarget(null)
    setDepartmentDeleteReason('')
    setConfirmationError(null)
  }

  async function confirmDepartmentDeleteFromTable() {
    if (!departmentDeleteTarget || !departmentDeleteReason.trim()) {
      return
    }

    await runConfirmation(
      () => deleteDepartment(departmentDeleteTarget, departmentDeleteReason.trim()),
      () => { setDepartmentDeleteTarget(null); setDepartmentDeleteReason('') },
      'Не удалось удалить отдел.',
    )
  }

  function restoreEmployee(row: ContractorStaffRow) {
    setEmployeeContextMenu(null)
    setConfirmationError(null)
    setRestoreTarget({ type: 'employee', item: row })
  }

  function restoreDepartment(row: ContractorDepartmentRow) {
    setDepartmentContextMenu(null)
    setConfirmationError(null)
    setRestoreTarget({ type: 'department', item: row })
  }

  function openDepartmentContextMenu(event: MouseEvent<HTMLDivElement>, row: ContractorDepartmentRow) {
    event.preventDefault()
    setDepartmentContextMenu({ row, x: event.clientX, y: event.clientY })
  }

  function openDepartmentEditor(row: ContractorDepartmentRow) {
    setDepartmentContextMenu(null)
    setModal({ type: 'department', item: row })
  }

  function openEmployeeFinancialReport(row: ContractorStaffRow) {
    openContractorFinancialReport({ type: 'employee', row })
  }

  const confirmRestore = async () => {
    if (!restoreTarget) {
      return
    }

    setConfirmationSaving(true)
    setConfirmationError(null)
    try {
      if (restoreTarget.type === 'garage') {
        if (isBackendDictionaryId(restoreTarget.item.id)) {
          const restoredGarage = await dictionaryClient.restoreGarage(auth.accessToken, restoreTarget.item.id)
          const nextGarage = createGarageRowFromDto(restoredGarage, owners)
          setGarages((currentGarages) => currentGarages.map((item) => (item.id === restoreTarget.item.id ? nextGarage : item)))
        } else {
          setGarages((currentGarages) => currentGarages.map((item) => (item.id === restoreTarget.item.id ? { ...item, isDeleted: false } : item)))
        }
      } else if (restoreTarget.type === 'supplier') {
        if (isBackendDictionaryId(restoreTarget.item.id)) {
          const restoredSupplier = await dictionaryClient.restoreSupplier(auth.accessToken, restoreTarget.item.id)
          const restoredContacts = supplierContactsRef.current.filter((contact) => contact.supplierId === restoredSupplier.id)
          const nextSupplier = createSupplierRowFromDto(restoredSupplier, restoredContacts)
          setSuppliers((currentSuppliers) => currentSuppliers.map((item) => (item.id === restoreTarget.item.id ? nextSupplier : item)))
        } else {
          setSuppliers((currentSuppliers) => currentSuppliers.map((item) => (item.id === restoreTarget.item.id ? { ...item, isDeleted: false } : item)))
        }
      } else if (restoreTarget.type === 'department') {
        if (isBackendDictionaryId(restoreTarget.item.id)) {
          const restoredDepartment = await dictionaryClient.restoreStaffDepartment(auth.accessToken, restoreTarget.item.id)
          setDepartments((currentDepartments) => currentDepartments.map((item) => (item.id === restoreTarget.item.id ? createStaffDepartmentRowFromDto(restoredDepartment) : item)))
        } else {
          setDepartments((currentDepartments) => currentDepartments.map((item) => (item.id === restoreTarget.item.id ? { ...item, isDeleted: false } : item)))
        }
      } else if (isBackendDictionaryId(restoreTarget.item.id)) {
        const restoredEmployee = await dictionaryClient.restoreStaffMember(auth.accessToken, restoreTarget.item.id)
        setStaff((currentStaff) => currentStaff.map((item) => (item.id === restoreTarget.item.id ? createStaffRowFromDto(restoredEmployee) : item)))
      } else {
        setStaff((currentStaff) => currentStaff.map((item) => (item.id === restoreTarget.item.id ? { ...item, isDeleted: false } : item)))
      }
    } catch (error) {
      setConfirmationError(error instanceof Error ? error.message : 'Не удалось восстановить запись.')
      setConfirmationSaving(false)
      return
    }

    setRestoreTarget(null)
    setConfirmationSaving(false)
  }

  function closeRestoreDialog() {
    if (confirmationSaving) {
      return
    }
    setRestoreTarget(null)
    setConfirmationError(null)
  }

  const saveDepartment = async (department: ContractorDepartmentRow) => {
    const currentDepartment = departments.find((item) => item.id === department.id)
    const normalizedName = department.name.trim() || 'Новый отдел'

    try {
      const savedDepartment = currentDepartment && isBackendDictionaryId(department.id)
        ? await dictionaryClient.updateStaffDepartment(auth.accessToken, department.id, { name: normalizedName })
        : await dictionaryClient.createStaffDepartment(auth.accessToken, { name: normalizedName })
      const nextDepartment = createStaffDepartmentRowFromDto(savedDepartment)
      setDepartments((currentDepartments) => currentDepartment
        ? currentDepartments.map((item) => (item.id === department.id ? nextDepartment : item))
        : [...currentDepartments, nextDepartment])
      if (currentDepartment && currentDepartment.name !== nextDepartment.name) {
        setStaff((currentStaff) => currentStaff.map((employee) => employee.department === currentDepartment.name
          ? { ...employee, department: nextDepartment.name }
          : employee))
      }
      return
    } catch (error) {
      const saveError = error instanceof Error ? error : new Error('Не удалось сохранить отдел.')
      throw saveError
    }
  }

  const saveServiceWithTariff = async (request: CreateChargeServiceWithTariffRequest) => {
    setServiceSaving(true)
    setFormStateError(null)
    try {
      const created = await dictionaryClient.createChargeServiceWithTariff(auth.accessToken, request)
      setChargeServices((currentServices) => [...currentServices.filter((service) => service.id !== created.service.id), created.service])
      setServiceTariffs((currentTariffs) => [...currentTariffs.filter((tariff) => tariff.id !== created.tariff.id), created.tariff])
      setModal(null)
    } catch (error) {
      setFormStateError(error instanceof Error ? error.message : 'Не удалось добавить услугу в единый каталог тарифов.')
      throw error
    } finally {
      setServiceSaving(false)
    }
  }

  const changeContractorSort = (section: ContractorSortableSection, key: ContractorSortKey) => {
    const nextSort: ContractorSortState = contractorSort.section === section && contractorSort.key === key
      ? { ...contractorSort, direction: contractorSort.direction === 'asc' ? 'desc' : 'asc' }
      : { section, key, direction: 'asc' }
    setContractorSort(nextSort)
    if (section === 'garages' && isGarageServerSortKey(key)) {
      void loadGaragePage(0, garagePage.limit, nextSort)
    } else if (section === 'staff') {
      void loadStaffPage(0, staffPage.limit, nextSort)
    } else if (section === 'suppliers' && isSupplierServerSortKey(key)) {
      void loadSupplierPage(0, supplierPage.limit, nextSort)
    }
  }

  const renderContractorSortHeader = (section: ContractorSortableSection, key: ContractorSortKey, label: string) => {
    const isActiveSort = contractorSort.section === section && contractorSort.key === key
    const indicator = isActiveSort ? (contractorSort.direction === 'asc' ? '↑' : '↓') : ''

    return (
      <button
        className="ghost-button contractors-sort-button"
        type="button"
        title={`Сортировать: ${label}`}
        aria-pressed={isActiveSort}
        onClick={() => changeContractorSort(section, key)}
      >
        <span>{label}</span>
        <span className="contractors-sort-indicator" aria-hidden="true">{indicator}</span>
      </button>
    )
  }

  const hasActiveGarageFilters = canUseGarageColumnFilters && hasGarageColumnFilters(garageColumnFilters)
  const toggleGarageDebtorsFilter = () => {
    const nextValue = !showGarageDebtorsOnly
    setShowGarageDebtorsOnly(nextValue)
    void loadGaragePage(0, garagePage.limit, undefined, nextValue).then((loaded) => {
      if (loaded === false) {
        setShowGarageDebtorsOnly(!nextValue)
      }
    })
  }

  function openGarageOpeningBalanceAdjustment(row: ContractorGarageRow) {
    setModal(null)
    setOpeningBalanceAdjustmentTarget({ type: 'garage', id: row.id, name: `Гараж ${row.number}`, currentAmount: Number(row.startingBalance ?? 0) })
  }

  function openSupplierOpeningBalanceAdjustment(row: ContractorSupplierRow) {
    setModal(null)
    setOpeningBalanceAdjustmentTarget({ type: 'supplier', id: row.id, name: row.name, currentAmount: Number(row.startingBalance || 0) })
  }

  function handleOpeningBalanceAdjusted() {
    const target = openingBalanceAdjustmentTarget
    setOpeningBalanceAdjustmentTarget(null)
    if (target?.type === 'garage') {
      void loadGaragePage()
    } else if (target?.type === 'supplier') {
      void loadSupplierPage()
    }
  }

  const filteredGarages = garages

  const visibleGarages = useMemo(() => {
    const rows = [...filteredGarages]
    if (contractorSort.section !== 'garages') {
      return rows
    }

    const sortKey = contractorSort.key as Exclude<ContractorGarageColumnKey, 'actions'>
    return rows.sort((left, right) => applyContractorSortDirection(compareContractorGarages(left, right, sortKey), contractorSort.direction))
  }, [filteredGarages, contractorSort])

  const visibleSuppliers = useMemo(() => {
    const rows = [...suppliers]
    if (contractorSort.section !== 'suppliers') {
      return rows
    }

    const sortKey = contractorSort.key as ContractorSupplierSortKey
    return rows.sort((left, right) => applyContractorSortDirection(compareContractorSuppliers(left, right, sortKey), contractorSort.direction))
  }, [suppliers, contractorSort])

  const visibleStaff = useMemo(() => {
    const rows = [...staff]
    if (contractorSort.section !== 'staff') {
      return rows
    }

    const sortKey = contractorSort.key as ContractorStaffSortKey
    return rows.sort((left, right) => applyContractorSortDirection(compareContractorStaff(left, right, sortKey), contractorSort.direction))
  }, [staff, contractorSort])
  const departmentPage = createClientPage(departments, departmentPageNumber, departmentPageSize)
  const debtorsButtonLabel = showGarageDebtorsOnly ? 'Показать все гаражи' : 'Показать должников'
  const contractorFinancialReportTitle = contractorFinancialReportTarget?.type === 'supplier'
    ? contractorFinancialReportTarget.row.name || 'Поставщик без названия'
    : contractorFinancialReportTarget?.row.fullName || 'Сотрудник без ФИО'
  const contractorFinancialReportDescription = contractorFinancialReportTarget?.type === 'supplier'
    ? contractorFinancialReportTarget.row.service || contractorFinancialReportTarget.row.contactPerson || 'Услуга не указана'
    : contractorFinancialReportTarget?.row.department || 'Отдел не указан'
  const contractorFinancialReportDialogTitleId = 'contractor-financial-report-title'
  const contractorFinancialReportDialogDescriptionId = 'contractor-financial-report-description'

  return (
    <section className="contractors-page contractors-page--directory" aria-label="Контрагенты">
      <div className="contractors-heading">
        <div>
          <h1>Контрагенты</h1>
        </div>
        <div className="contractors-actions">
          {activeSection === 'garages' ? (
            <>
              <button className="secondary-button" type="button" aria-busy={contractorPageLoading.garages} onClick={toggleGarageDebtorsFilter}>{debtorsButtonLabel}</button>
              <button className="secondary-button create-action-button" type="button" aria-busy={contractorReferenceLoading === 'garages'} disabled={contractorReferenceLoading === 'garages'} onClick={() => void openGarageCreator()}>
                <Gauge size={17} aria-hidden="true" />
                <span>Добавить гараж</span>
              </button>
            </>
          ) : null}
          {activeSection === 'suppliers' ? (
            <>
              <button className="secondary-button create-action-button" type="button" aria-busy={contractorReferenceLoading === 'suppliers'} disabled={contractorReferenceLoading === 'suppliers'} onClick={() => void openSupplierCreator()}>
                <UsersRound size={17} aria-hidden="true" />
                <span>Добавить поставщика</span>
              </button>
              <button className="secondary-button create-action-button" type="button" aria-busy={contractorReferenceLoading === 'suppliers'} disabled={!canManageTariffs || contractorReferenceLoading === 'suppliers'} title={!canManageTariffs ? 'Нужно право управления тарифами' : undefined} onClick={() => void openServiceCreator()}>
                <FileText size={17} aria-hidden="true" />
                <span>Добавить услугу</span>
              </button>
            </>
          ) : null}
          {activeSection === 'staff' ? (
            <>
              <button className="secondary-button create-action-button" type="button" onClick={() => setModal({ type: 'department' })}>
                <UsersRound size={17} aria-hidden="true" />
                <span>Добавить отдел</span>
              </button>
              <button className="secondary-button create-action-button" type="button" onClick={() => setModal({ type: 'employee' })}>
                <UserPlus size={17} aria-hidden="true" />
                <span>Добавить сотрудника</span>
              </button>
            </>
          ) : null}
        </div>
      </div>
      {formStateError && !modal ? (
        <AsyncErrorState message={formStateError} onRetry={retryActiveContractorSection} retrying={activeContractorPageLoading || contractorReferenceLoading !== null || supplierEditorLoadingId !== null} />
      ) : null}

      <div className="contractors-prototype-tabs" role="tablist" aria-label="Разделы контрагентов">
        {Object.entries(contractorSectionLabels).map(([section, label]) => (
          <button type="button" role="tab" aria-selected={activeSection === section} className={activeSection === section ? 'is-active' : ''} onClick={() => setActiveSection(section as ContractorSection)} key={section}>
            {label}
          </button>
        ))}
      </div>

      {activeSection === 'garages' ? (
        <section className="contractors-directory-card" aria-label="Гаражи">
          {canUseGarageColumnFilters ? <form className="contractors-column-filters" aria-label="Фильтры гаражей" onSubmit={(event) => {
            event.preventDefault()
            const filters = toGarageColumnFilters(garageColumnFilterForm)
            setGarageColumnFilters(filters)
            void loadGaragePage(0, garagePage.limit, undefined, undefined, filters)
          }}>
            <label className="contractors-column-filters__field contractors-column-filters__field--number">
              <span>Номер гаража</span>
              <span className="contractors-column-filters__input-shell">
                <Search size={16} aria-hidden="true" />
                <input aria-label="Фильтр по номеру гаража" placeholder="Например, А-20" value={garageColumnFilterForm.number} onChange={(event) => setGarageColumnFilterForm((current) => ({ ...current, number: event.target.value }))} />
              </span>
            </label>
            <fieldset className="contractors-column-filters__range">
              <legend>Количество людей</legend>
              <div>
                <label>
                  <span>От</span>
                  <input aria-label="Минимальное количество человек" type="number" inputMode="numeric" min="0" step="1" placeholder="0" value={garageColumnFilterForm.peopleCountMin} onChange={(event) => setGarageColumnFilterForm((current) => ({ ...current, peopleCountMin: event.target.value }))} />
                </label>
                <label>
                  <span>До</span>
                  <input aria-label="Максимальное количество человек" type="number" inputMode="numeric" min="0" step="1" placeholder="Любое" value={garageColumnFilterForm.peopleCountMax} onChange={(event) => setGarageColumnFilterForm((current) => ({ ...current, peopleCountMax: event.target.value }))} />
                </label>
              </div>
            </fieldset>
            <fieldset className="contractors-column-filters__range">
              <legend>Количество этажей</legend>
              <div>
                <label>
                  <span>От</span>
                  <input aria-label="Минимальное количество этажей" type="number" inputMode="numeric" min="0" step="1" placeholder="0" value={garageColumnFilterForm.floorCountMin} onChange={(event) => setGarageColumnFilterForm((current) => ({ ...current, floorCountMin: event.target.value }))} />
                </label>
                <label>
                  <span>До</span>
                  <input aria-label="Максимальное количество этажей" type="number" inputMode="numeric" min="0" step="1" placeholder="Любое" value={garageColumnFilterForm.floorCountMax} onChange={(event) => setGarageColumnFilterForm((current) => ({ ...current, floorCountMax: event.target.value }))} />
                </label>
              </div>
            </fieldset>
            <div className="contractors-column-filters__actions">
              <button className="secondary-button" type="submit" aria-label="Применить фильтры" disabled={contractorPageLoading.garages}>
                <Search size={16} aria-hidden="true" />
                <span>Применить</span>
              </button>
              <button className="ghost-button" type="button" aria-label="Сбросить фильтры" disabled={Object.values(garageColumnFilterForm).every((value) => value === '')} onClick={() => {
                setGarageColumnFilterForm(emptyGarageColumnFilterForm)
                setGarageColumnFilters({})
                void loadGaragePage(0, garagePage.limit, undefined, undefined, {})
              }}>
                <RotateCcw size={16} aria-hidden="true" />
                <span>Сбросить</span>
              </button>
            </div>
          </form> : null}
          <div className="contractors-directory-table contractors-directory-table--garages" role="table" aria-label="Гаражи" style={garageTableStyle}>
            <div className="contractors-directory-row contractors-directory-row--header" role="row">
              {contractorGarageColumnDefinitions.map((column) => (
                <span className={`contractors-directory-header-cell${column.key === 'actions' ? ' table-actions-column' : ''}`} role="columnheader" key={column.key}>
                  {column.key === 'actions' ? <span>{column.label}</span> : renderContractorSortHeader('garages', column.key, column.label)}
                  {column.key !== 'actions' ? (
                    <button
                      className="icon-button contractors-column-resizer"
                      type="button"
                      aria-label={`Изменить ширину столбца ${column.label}`}
                      onPointerDown={(event) => garageColumnResize.startResize(column.key, event)}
                      onPointerMove={garageColumnResize.continueResize}
                      onPointerUp={garageColumnResize.finishResize}
                      onPointerCancel={garageColumnResize.cancelResize}
                      onKeyDown={(event) => garageColumnResize.resizeWithKeyboard(column.key, event)}
                    />
                  ) : null}
                </span>
              ))}
            </div>
            {visibleGarages.map((row) => (
              <div className={row.isDeleted ? 'contractors-directory-row contractors-directory-row--deleted' : 'contractors-directory-row'} role="row" key={row.id} onContextMenu={(event) => openGarageContextMenu(event, row)}>
                <span role="cell" className="contractors-directory-cell--center">{row.number}</span>
                <span role="cell" className="contractors-directory-cell--center">{row.peopleCount}</span>
                <span role="cell" className="contractors-directory-cell--center">{row.floorCount}</span>
                <span role="cell">{row.owner}</span>
                <span role="cell">{row.phone}</span>
                <span role="cell" className={row.overdueDebt ? 'contractors-directory-cell--right money-expense' : 'contractors-directory-cell--right'}>
                  {row.isDeleted ? 'Удален' : row.overdueDebt || 'Нет'}
                </span>
                <span role="cell" className="contractors-row-actions table-actions-column">
                  {row.isDeleted ? (
                    <button className="icon-button" type="button" aria-label={`Восстановить гараж ${row.number}`} title="Восстановить" onClick={() => restoreGarage(row)}>
                      <RotateCcw size={16} />
                    </button>
                  ) : (
                    <>
                      <button className="icon-button" type="button" aria-label={`Изменить гараж ${row.number}`} title="Изменить" aria-busy={contractorReferenceLoading === 'garages'} disabled={contractorReferenceLoading === 'garages'} onClick={() => void openGarageEditor(row)}>
                        <Pencil size={16} />
                      </button>
                      <button className="icon-button" type="button" aria-label={`Открыть финансовый отчет гаража ${row.number}`} title="Финансовый отчет" onClick={() => openGarageFinancialReport(row)}>
                        <FileText size={16} />
                      </button>
                      <button className="icon-button danger-icon-button" type="button" aria-label={`Удалить гараж ${row.number}`} title="Удалить" onClick={() => openGarageDeleteDialog(row)}>
                        <Trash2 size={16} />
                      </button>
                    </>
                  )}
                </span>
              </div>
            ))}
            {contractorPageLoading.garages && visibleGarages.length === 0 ? <TableLoadingState className="table-loading-state--compact" label="Загружаем гаражи" /> : null}
            {contractorPageLoading.garages && visibleGarages.length > 0 ? <BackgroundRefreshStatus className="contractors-table-refresh" label="Обновляем список гаражей" /> : null}
            {!contractorPageLoading.garages && visibleGarages.length === 0 ? (
              <div className="contractors-directory-row contractors-directory-row--empty" role="row">
                <span className="contractors-directory-empty-cell" role="cell">{hasActiveGarageFilters ? 'По заданным фильтрам гаражи не найдены.' : showGarageDebtorsOnly ? 'Гаражей с задолженностью не найдено.' : 'Гаражи пока не настроены.'}</span>
              </div>
            ) : null}
          </div>
          <TablePagination
            ariaLabel="Пагинация гаражей"
            totalCount={garagePage.totalCount}
            offset={garagePage.offset}
            limit={garagePage.limit}
            visibleCount={visibleGarages.length}
            disabled={contractorPageLoading.garages}
            pageSizeLabel="Количество строк гаражей"
            statusText={showGarageDebtorsOnly ? `Всего должников: ${garagePage.totalCount}` : undefined}
            onPageChange={(page) => void loadGaragePage((page - 1) * garagePage.limit)}
            onPageSizeChange={(limit) => void loadGaragePage(0, limit)}
          />
        </section>
      ) : null}

      {activeSection === 'suppliers' ? (
        <section className="contractors-directory-card" aria-label="Поставщики">
          {supplierEditorLoadingId ? <span className="contractors-directory-loading-note" role="status" aria-live="polite">Загружаем контакты поставщика…</span> : null}
          <div className="contractors-directory-table contractors-directory-table--suppliers" role="table" aria-label="Поставщики" style={supplierTableStyle}>
            <div className="contractors-directory-row contractors-directory-row--header" role="row">
              {contractorSupplierColumnDefinitions.map((column) => (
                <span className={`contractors-directory-header-cell contractors-directory-header-cell--${column.key}${column.key === 'actions' ? ' table-actions-column' : ''}`} role="columnheader" key={column.key}>
                  {column.key === 'actions' ? <span>{column.label}</span> : renderContractorSortHeader('suppliers', column.key, column.label)}
                  {column.key !== 'actions' ? (
                    <button
                      className="icon-button contractors-column-resizer"
                      type="button"
                      aria-label={`Изменить ширину столбца ${column.label}`}
                      onPointerDown={(event) => supplierColumnResize.startResize(column.key, event)}
                      onPointerMove={supplierColumnResize.continueResize}
                      onPointerUp={supplierColumnResize.finishResize}
                      onPointerCancel={supplierColumnResize.cancelResize}
                      onKeyDown={(event) => supplierColumnResize.resizeWithKeyboard(column.key, event)}
                    />
                  ) : null}
                </span>
              ))}
            </div>
            {visibleSuppliers.map((row) => {
              const primaryContact = getSupplierPrimaryContact(row)
              return (
                <div className={row.isDeleted ? 'contractors-directory-row contractors-directory-row--deleted' : 'contractors-directory-row'} role="row" key={row.id} onContextMenu={(event) => openSupplierContextMenu(event, row)}>
                  <span role="cell" className="contractors-supplier-cell contractors-supplier-cell--name">{row.name}</span>
                  <span role="cell" className="contractors-supplier-cell contractors-supplier-cell--service">{row.service}</span>
                  <span role="cell" className="contractors-supplier-cell contractors-supplier-cell--contact">{primaryContact?.fullName ?? row.contactPerson}</span>
                  <span role="cell" className="contractors-supplier-cell contractors-supplier-cell--phone">{primaryContact?.phone ?? row.phone}</span>
                  <span role="cell" className="contractors-supplier-cell contractors-supplier-cell--email">{primaryContact?.email ?? row.email}</span>
                  <span role="cell" className={row.debt ? 'contractors-supplier-cell contractors-supplier-cell--debt contractors-directory-cell--center money-expense' : 'contractors-supplier-cell contractors-supplier-cell--debt contractors-directory-cell--center'}>
                    {row.isDeleted ? 'Удален' : row.debt || 'Нет'}
                  </span>
                  <span role="cell" className="contractors-row-actions table-actions-column">
                    {row.isDeleted ? (
                      <button className="icon-button" type="button" aria-label={`Восстановить поставщика ${row.name}`} title="Восстановить" onClick={() => restoreSupplier(row)}>
                        <RotateCcw size={16} />
                      </button>
                    ) : (
                      <>
                        <button className="icon-button" type="button" aria-label={`Изменить поставщика ${row.name}`} title="Изменить" aria-busy={supplierEditorLoadingId === row.id} disabled={supplierEditorLoadingId === row.id} onClick={() => void openSupplierEditor(row)}>
                          {supplierEditorLoadingId === row.id ? <LoaderCircle className="financial-report-button__spinner" size={16} aria-hidden="true" /> : <Pencil size={16} />}
                        </button>
                        <button className="icon-button" type="button" aria-label={`Открыть финансовый отчет поставщика ${row.name}`} title="Финансовый отчет" onClick={() => openSupplierFinancialReport(row)}>
                          <FileText size={16} />
                        </button>
                        <button className="icon-button danger-icon-button" type="button" aria-label={`Удалить поставщика ${row.name}`} title="Удалить" onClick={() => openSupplierDeleteDialog(row)}>
                          <Trash2 size={16} />
                        </button>
                      </>
                    )}
                  </span>
                </div>
              )
            })}
            {contractorPageLoading.suppliers && visibleSuppliers.length === 0 ? <TableLoadingState className="table-loading-state--compact" label="Загружаем поставщиков" /> : null}
            {contractorPageLoading.suppliers && visibleSuppliers.length > 0 ? <BackgroundRefreshStatus className="contractors-table-refresh" label="Обновляем список поставщиков" /> : null}
            {!contractorPageLoading.suppliers && visibleSuppliers.length === 0 ? (
              <div className="contractors-directory-row contractors-directory-row--empty" role="row">
                <span className="contractors-directory-empty-cell" role="cell">Поставщики пока не настроены.</span>
              </div>
            ) : null}
          </div>
          <TablePagination
            ariaLabel="Пагинация поставщиков"
            totalCount={supplierPage.totalCount}
            offset={supplierPage.offset}
            limit={supplierPage.limit}
            visibleCount={visibleSuppliers.length}
            disabled={contractorPageLoading.suppliers}
            pageSizeLabel="Количество строк поставщиков"
            onPageChange={(page) => void loadSupplierPage((page - 1) * supplierPage.limit)}
            onPageSizeChange={(limit) => void loadSupplierPage(0, limit)}
          />
        </section>
      ) : null}

      {activeSection === 'staff' ? (
        <div className="contractors-staff-directory-grid">
          <section className="contractors-directory-card contractors-staff-directory-card" aria-label="Персонал">
            <div className="contractors-directory-card-header">
              <h2>Сотрудники</h2>
            </div>
            <div className="contractors-directory-table contractors-directory-table--staff" role="table" aria-label="Персонал" style={staffTableStyle}>
              <div className="contractors-directory-row contractors-directory-row--header" role="row">
                {contractorStaffColumnDefinitions.map((column) => (
                  <span className={`contractors-directory-header-cell${column.key === 'actions' ? ' table-actions-column' : ''}`} role="columnheader" key={column.key}>
                    {column.key === 'actions' ? <span>{column.label}</span> : renderContractorSortHeader('staff', column.key, column.label)}
                    {column.key !== 'actions' ? (
                      <button
                        className="icon-button contractors-column-resizer"
                        type="button"
                        aria-label={`Изменить ширину столбца ${column.label}`}
                        onPointerDown={(event) => staffColumnResize.startResize(column.key, event)}
                        onPointerMove={staffColumnResize.continueResize}
                        onPointerUp={staffColumnResize.finishResize}
                        onPointerCancel={staffColumnResize.cancelResize}
                        onKeyDown={(event) => staffColumnResize.resizeWithKeyboard(column.key, event)}
                      />
                    ) : null}
                  </span>
                ))}
              </div>
              {visibleStaff.map((row) => (
                <div className={row.isDeleted ? 'contractors-directory-row contractors-directory-row--deleted' : 'contractors-directory-row'} role="row" key={row.id} onContextMenu={(event) => openEmployeeContextMenu(event, row)}>
                  <span role="cell">{row.fullName}</span>
                  <span role="cell">{row.department}</span>
                  <span role="cell" className="contractors-directory-cell--right contractors-staff-rate-cell">{row.isDeleted ? 'Удален' : formatStaffRate(row.rate)}</span>
                  <span role="cell" className="contractors-row-actions table-actions-column">
                    {row.isDeleted ? (
                      <button className="icon-button" type="button" aria-label={`Восстановить сотрудника ${row.fullName}`} title="Восстановить" onClick={() => restoreEmployee(row)}>
                        <RotateCcw size={16} />
                      </button>
                    ) : (
                      <>
                        <button className="icon-button" type="button" aria-label={`Изменить сотрудника ${row.fullName}`} title="Изменить" onClick={() => openEmployeeEditor(row)}>
                          <Pencil size={16} />
                        </button>
                        <button className="icon-button" type="button" aria-label={`Открыть финансовый отчет сотрудника ${row.fullName}`} title="Финансовый отчет" onClick={() => openEmployeeFinancialReport(row)}>
                          <FileText size={16} />
                        </button>
                        <button className="icon-button danger-icon-button" type="button" aria-label={`Удалить сотрудника ${row.fullName}`} title="Удалить" onClick={() => openEmployeeDeleteDialog(row)}>
                          <Trash2 size={16} />
                        </button>
                      </>
                    )}
                  </span>
                </div>
              ))}
              {contractorPageLoading.staff && visibleStaff.length === 0 ? <TableLoadingState className="table-loading-state--compact" label="Загружаем персонал" /> : null}
              {contractorPageLoading.staff && visibleStaff.length > 0 ? <BackgroundRefreshStatus className="contractors-table-refresh" label="Обновляем список персонала" /> : null}
              {!contractorPageLoading.staff && visibleStaff.length === 0 ? (
                <div className="contractors-directory-row contractors-directory-row--empty" role="row">
                  <span className="contractors-directory-empty-cell" role="cell">Сотрудники пока не настроены.</span>
                </div>
              ) : null}
            </div>
            <TablePagination
              ariaLabel="Пагинация персонала"
              totalCount={staffPage.totalCount}
              offset={staffPage.offset}
              limit={staffPage.limit}
              visibleCount={visibleStaff.length}
              disabled={contractorPageLoading.staff}
              pageSizeLabel="Количество строк персонала"
              onPageChange={(page) => void loadStaffPage((page - 1) * staffPage.limit)}
              onPageSizeChange={(limit) => void loadStaffPage(0, limit)}
            />
          </section>

          <section className="contractors-directory-card contractors-staff-directory-card" aria-label="Отделы персонала">
            <div className="contractors-directory-card-header">
              <h2>Отделы</h2>
            </div>
            <div className="contractors-directory-table contractors-directory-table--departments" role="table" aria-label="Отделы персонала">
              <div className="contractors-directory-row contractors-directory-row--header" role="row">
                <span className="contractors-directory-header-cell" role="columnheader">Отдел</span>
                <span className="contractors-directory-header-cell table-actions-column" role="columnheader">Действия</span>
              </div>
              {departmentPage.items.map((department) => (
                <div className={department.isDeleted ? 'contractors-directory-row contractors-directory-row--deleted' : 'contractors-directory-row'} role="row" key={department.id} onContextMenu={(event) => openDepartmentContextMenu(event, department)}>
                  <span role="cell">{department.name}</span>
                  <span role="cell" className="contractors-row-actions table-actions-column">
                    {department.isDeleted ? (
                      <button className="icon-button" type="button" aria-label={`Восстановить отдел ${department.name}`} title="Восстановить" onClick={() => restoreDepartment(department)}>
                        <RotateCcw size={16} />
                      </button>
                    ) : (
                      <>
                        <button className="icon-button" type="button" aria-label={`Изменить отдел ${department.name}`} title="Изменить" onClick={() => openDepartmentEditor(department)}>
                          <Pencil size={16} />
                        </button>
                        <button className="icon-button danger-icon-button" type="button" aria-label={`Удалить отдел ${department.name}`} title="Удалить" onClick={() => openDepartmentDeleteDialog(department)}>
                          <Trash2 size={16} />
                        </button>
                      </>
                    )}
                  </span>
                </div>
              ))}
              {departments.length === 0 ? (
                <div className="contractors-directory-row contractors-directory-row--empty" role="row">
                  <span className="contractors-directory-empty-cell" role="cell">Отделы пока не настроены.</span>
                </div>
              ) : null}
            </div>
            <TablePagination
              ariaLabel="Пагинация отделов"
              totalCount={departmentPage.totalCount}
              offset={departmentPage.offset}
              limit={departmentPage.limit}
              visibleCount={departmentPage.items.length}
              pageSizeLabel="Количество строк отделов"
              onPageChange={setDepartmentPageNumber}
              onPageSizeChange={(limit) => {
                setDepartmentPageNumber(1)
                setDepartmentPageSize(limit)
              }}
            />
          </section>

        </div>
      ) : null}

      {garageContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setGarageContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия гаража ${garageContextMenu.row.number}`}
            style={{ left: garageContextMenu.x, top: garageContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {garageContextMenu.row.isDeleted ? (
              <div className="context-menu-group" role="group">
                <button type="button" role="menuitem" onClick={() => restoreGarage(garageContextMenu.row)}>
                  <RotateCcw size={16} />
                  <span>Восстановить</span>
                </button>
              </div>
            ) : (
              <>
                <div className="context-menu-group" role="group">
                  <button type="button" role="menuitem" aria-busy={contractorReferenceLoading === 'garages'} disabled={contractorReferenceLoading === 'garages'} onClick={() => void openGarageEditor(garageContextMenu.row)}>
                    <Pencil size={16} />
                    <span>Изменить</span>
                  </button>
                  <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openGarageDeleteDialog(garageContextMenu.row)}>
                    <Trash2 size={16} />
                    <span>Удалить</span>
                  </button>
                </div>
                <div className="context-menu-separator" role="separator" />
                <div className="context-menu-group" role="group">
                  <button type="button" role="menuitem" onClick={() => openGarageFinancialReport(garageContextMenu.row)}>
                    <FileText size={16} />
                    <span>Финансовый отчет</span>
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      ) : null}

      {supplierContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setSupplierContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия поставщика ${supplierContextMenu.row.name}`}
            style={{ left: supplierContextMenu.x, top: supplierContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {supplierContextMenu.row.isDeleted ? (
              <div className="context-menu-group" role="group">
                <button type="button" role="menuitem" onClick={() => restoreSupplier(supplierContextMenu.row)}>
                  <RotateCcw size={16} />
                  <span>Восстановить</span>
                </button>
              </div>
            ) : (
              <>
                <div className="context-menu-group" role="group">
                  <button type="button" role="menuitem" aria-busy={supplierEditorLoadingId === supplierContextMenu.row.id} disabled={supplierEditorLoadingId === supplierContextMenu.row.id} onClick={() => void openSupplierEditor(supplierContextMenu.row)}>
                    {supplierEditorLoadingId === supplierContextMenu.row.id ? <LoaderCircle className="financial-report-button__spinner" size={16} aria-hidden="true" /> : <Pencil size={16} />}
                    <span>Изменить</span>
                  </button>
                  <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openSupplierDeleteDialog(supplierContextMenu.row)}>
                    <Trash2 size={16} />
                    <span>Удалить</span>
                  </button>
                </div>
                <div className="context-menu-separator" role="separator" />
                <div className="context-menu-group" role="group">
                  <button type="button" role="menuitem" onClick={() => openSupplierFinancialReport(supplierContextMenu.row)}>
                    <FileText size={16} />
                    <span>Финансовый отчет</span>
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      ) : null}

      {employeeContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setEmployeeContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия сотрудника ${employeeContextMenu.row.fullName}`}
            style={{ left: employeeContextMenu.x, top: employeeContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {employeeContextMenu.row.isDeleted ? (
              <div className="context-menu-group" role="group">
                <button type="button" role="menuitem" onClick={() => restoreEmployee(employeeContextMenu.row)}>
                  <RotateCcw size={16} />
                  <span>Восстановить</span>
                </button>
              </div>
            ) : (
              <>
                <div className="context-menu-group" role="group">
                  <button type="button" role="menuitem" onClick={() => openEmployeeEditor(employeeContextMenu.row)}>
                    <Pencil size={16} />
                    <span>Изменить</span>
                  </button>
                  <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openEmployeeDeleteDialog(employeeContextMenu.row)}>
                    <Trash2 size={16} />
                    <span>Удалить</span>
                  </button>
                </div>
                <div className="context-menu-separator" role="separator" />
                <div className="context-menu-group" role="group">
                  <button type="button" role="menuitem" onClick={() => openEmployeeFinancialReport(employeeContextMenu.row)}>
                    <FileText size={16} />
                    <span>Финансовый отчет</span>
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      ) : null}

      {departmentContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setDepartmentContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия отдела ${departmentContextMenu.row.name}`}
            style={{ left: departmentContextMenu.x, top: departmentContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {departmentContextMenu.row.isDeleted ? (
              <div className="context-menu-group" role="group">
                <button type="button" role="menuitem" onClick={() => restoreDepartment(departmentContextMenu.row)}>
                  <RotateCcw size={16} />
                  <span>Восстановить</span>
                </button>
              </div>
            ) : (
              <div className="context-menu-group" role="group">
                <button type="button" role="menuitem" onClick={() => openDepartmentEditor(departmentContextMenu.row)}>
                  <Pencil size={16} />
                  <span>Изменить</span>
                </button>
                <button className="context-menu-danger" type="button" role="menuitem" onClick={() => openDepartmentDeleteDialog(departmentContextMenu.row)}>
                  <Trash2 size={16} />
                  <span>Удалить</span>
                </button>
              </div>
            )}
          </div>
        </div>
      ) : null}

      {modal?.type === 'garage' ? <GaragePrototypeDialog accessToken={auth.accessToken} canAdjustOpeningData={canAdjustOpeningData} integrationClient={integrationClient} item={modal.item} onAdjustOpeningBalance={openGarageOpeningBalanceAdjustment} onClose={() => setModal(null)} onSave={saveGarage} onOpenFinancialReport={openGarageFinancialReport} /> : null}
      {modal?.type === 'supplier' ? <SupplierPrototypeDialog accessToken={auth.accessToken} canAdjustOpeningData={canAdjustOpeningData} funds={serviceFunds} integrationClient={integrationClient} item={modal.item} services={chargeServices} onAdjustOpeningBalance={openSupplierOpeningBalanceAdjustment} onClose={() => setModal(null)} onOpenFinancialReport={openSupplierFinancialReport} onSave={saveSupplier} /> : null}
      {modal?.type === 'service' ? (
        <Suspense fallback={(
          <div className="modal-backdrop" role="presentation">
            <section className="detail-dialog contractors-dialog contractors-tariff-dialog contractors-service-dialog" role="dialog" aria-modal="true" aria-label="Загрузка формы услуги">
              <LoadingSkeleton label="Загружаем форму услуги" rows={5} columns={2} />
            </section>
          </div>
        )}>
          <AddServicePrototypeDialog funds={serviceFunds.filter((fund) => fund.allowOperations)} isSaving={serviceSaving} incomeTypes={serviceIncomeTypes.filter((item) => !item.isArchived)} onClose={() => setModal(null)} onCreateWithTariff={saveServiceWithTariff} regularOnly tariffs={serviceTariffs.filter((item) => !item.isArchived)} />
        </Suspense>
      ) : null}
      {modal?.type === 'employee' ? <EmployeePrototypeDialog departments={departments} item={modal.item} onClose={() => setModal(null)} onOpenFinancialReport={openEmployeeFinancialReport} onSave={saveEmployee} /> : null}
      {modal?.type === 'department' ? <DepartmentPrototypeDialog item={modal.item} onClose={() => setModal(null)} onSave={saveDepartment} /> : null}

      {openingBalanceAdjustmentTarget ? (
        <OpeningBalanceAdjustmentDialog
          accessToken={auth.accessToken}
          dictionaryClient={dictionaryClient}
          target={openingBalanceAdjustmentTarget}
          onClose={() => setOpeningBalanceAdjustmentTarget(null)}
          onSaved={handleOpeningBalanceAdjusted}
        />
      ) : null}

      {garageFinancialReportTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeGarageFinancialReport}>
          <section ref={garageFinancialReportDialogRef} className="detail-dialog garage-balance-dialog financial-report-dialog" role="dialog" aria-modal="true" aria-busy={garageFinancialReportLoading} aria-labelledby="contractor-garage-report-title" aria-describedby="contractor-garage-report-owner" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Финансовый отчет</p>
                <h3 id="contractor-garage-report-title">Гараж {garageFinancialReportTarget.number || 'без номера'}</h3>
                <p id="contractor-garage-report-owner">{garageFinancialReportTarget.owner || 'Владелец не указан'}</p>
              </div>
              <button ref={garageFinancialReportCloseRef} className="icon-button" type="button" aria-label="Закрыть финансовый отчет гаража" onClick={closeGarageFinancialReport}>
                <X size={18} />
              </button>
            </div>
            <FinancialReportPeriodFilters filters={garageFinancialReportFilters} targetLabel="гаража" onChange={applyGarageFinancialReportFilters} />
            {garageFinancialReportError ? <FormError>{garageFinancialReportError}</FormError> : null}
            {garageFinancialReportLoading && !garageFinancialReport ? (
              <LoadingSkeleton className="financial-report-loading-skeleton" label="Загружаем финансовый отчет гаража" rows={6} columns={5} />
            ) : garageFinancialReport ? (
              <>
                <div className="balance-history-summary" aria-label="Итоги финансового отчета гаража">
                  <div>
                    <span>Старт</span>
                    <strong>{formatMoney(garageFinancialReport.startingBalance)}</strong>
                  </div>
                  <div>
                    <span>Начислено</span>
                    <strong>{formatMoney(garageFinancialReport.accrualTotal)}</strong>
                  </div>
                  <div>
                    <span>Поступило</span>
                    <strong>{formatMoney(garageFinancialReport.incomeTotal)}</strong>
                  </div>
                  <div>
                    <span>{formatDebtLabel(garageFinancialReport.debt)}</span>
                    <strong className={getDebtClassName(garageFinancialReport.debt)}>{formatDebtAmount(garageFinancialReport.debt)}</strong>
                  </div>
                </div>
                <div className="dictionary-table-scroll garage-balance-table-scroll">
                  <table className="dictionary-data-table" aria-label="Финансовый отчет гаража">
                    <thead>
                      <tr>
                        <th>Месяц</th>
                        <th>Долг на начало</th>
                        <th>Начислено</th>
                        <th>Поступило</th>
                        <th>Долг на конец</th>
                      </tr>
                    </thead>
                    <tbody>
                      {garageFinancialReport.rows.map((row) => (
                        <tr key={row.accountingMonth}>
                          <td>{formatMonth(row.accountingMonth)}</td>
                          <td className={getDebtClassName(row.openingDebt)}>{formatDebtAmount(row.openingDebt)}</td>
                          <td className="money-accrual">{formatMoney(row.accrualAmount)}</td>
                          <td className="money-income">{formatMoney(row.incomeAmount)}</td>
                          <td className={getDebtClassName(row.closingDebt)}>{formatDebtAmount(row.closingDebt)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {garageFinancialReport.rows.length === 0 ? <StatusMessage>По выбранному периоду строк нет</StatusMessage> : null}
                </div>
              </>
            ) : null}
          </section>
        </div>
      ) : null}

      {contractorFinancialReportTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeContractorFinancialReport}>
          <section ref={contractorFinancialReportDialogRef} className="detail-dialog garage-balance-dialog financial-report-dialog" role="dialog" aria-modal="true" aria-busy={contractorFinancialReportLoading} aria-labelledby={contractorFinancialReportDialogTitleId} aria-describedby={contractorFinancialReportDialogDescriptionId} onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Финансовый отчет</p>
                <h3 id={contractorFinancialReportDialogTitleId}>{contractorFinancialReportTitle}</h3>
                <p id={contractorFinancialReportDialogDescriptionId}>{contractorFinancialReportDescription}</p>
              </div>
              <button ref={contractorFinancialReportCloseRef} className="icon-button" type="button" aria-label="Закрыть финансовый отчет контрагента" onClick={closeContractorFinancialReport}>
                <X size={18} />
              </button>
            </div>
            <FinancialReportPeriodFilters filters={contractorFinancialReportFilters} targetLabel="контрагента" onChange={applyContractorFinancialReportFilters} />
            {contractorFinancialReportError ? <FormError>{contractorFinancialReportError}</FormError> : null}
            {contractorFinancialReportLoading && !contractorFinancialReport ? (
              <LoadingSkeleton className="financial-report-loading-skeleton" label="Загружаем финансовый отчет контрагента" rows={6} columns={7} />
            ) : contractorFinancialReport ? (
              <>
                <div className="balance-history-summary contractor-financial-report__summary" aria-label="Итоги финансового отчета контрагента">
                  {contractorFinancialReportTarget.type === 'supplier' ? (
                    <div>
                      <span>Входящий остаток</span>
                      <strong className={getDebtClassName(contractorFinancialReport.openingBalance)}>{formatDebtAmount(contractorFinancialReport.openingBalance)}</strong>
                    </div>
                  ) : null}
                  <div>
                    <span>Начислено</span>
                    <strong>{formatMoney(contractorFinancialReport.accrualTotal)}</strong>
                  </div>
                  <div>
                    <span>Оплачено</span>
                    <strong>{formatMoney(contractorFinancialReport.paymentTotal)}</strong>
                  </div>
                  <div>
                    <span>{formatDebtLabel(contractorFinancialReport.balance)}</span>
                    <strong className={getDebtClassName(contractorFinancialReport.balance)}>{formatDebtAmount(contractorFinancialReport.balance)}</strong>
                  </div>
                  <div>
                    <span>Строк</span>
                    <strong>{contractorFinancialReport.rows.length}</strong>
                  </div>
                </div>
                <div className="dictionary-table-scroll garage-balance-table-scroll">
                  <table className="dictionary-data-table" aria-label={contractorFinancialReportTarget.type === 'supplier' ? 'Финансовый отчет поставщика' : 'Финансовый отчет сотрудника'}>
                    <thead>
                      <tr>
                        <th>Месяц</th>
                        <th>Дата</th>
                        <th>Документ</th>
                        <th>Операция</th>
                        <th>Начислено</th>
                        <th>Оплачено</th>
                        <th>Остаток</th>
                      </tr>
                    </thead>
                    <tbody>
                      {contractorFinancialReport.rows.map((row) => (
                        <tr key={row.id}>
                          <td>{formatMonth(row.accountingMonth)}</td>
                          <td>{formatDateOnly(row.date)}</td>
                          <td>{row.documentNumber}</td>
                          <td>{row.description}</td>
                          <td className="money-accrual contractor-financial-report__amount">{row.accrualAmount !== 0 ? formatMoney(row.accrualAmount) : '—'}</td>
                          <td className="money-expense contractor-financial-report__amount">{row.paymentAmount > 0 ? formatMoney(row.paymentAmount) : '—'}</td>
                          <td className={`${getDebtClassName(row.balanceAfter)} contractor-financial-report__amount`}>{formatDebtAmount(row.balanceAfter)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {contractorFinancialReport.rows.length === 0 ? <StatusMessage>По выбранному периоду строк нет</StatusMessage> : null}
                </div>
                {canReadContractorHistory ? (
                  <section className="contractor-history-section" aria-label="Переход к истории изменений контрагента">
                    <h4>История изменений</h4>
                    <div className="inline-action-row">
                      <p>Откройте общий журнал с фильтром по этому контрагенту.</p>
                      <button className="secondary-button" type="button" onClick={() => openContractorHistoryInAudit()}>
                        <FileText size={16} />
                        <span>Открыть в истории изменений</span>
                      </button>
                    </div>
                  </section>
                ) : null}
              </>
            ) : null}
          </section>
        </div>
      ) : null}

      {restoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeRestoreDialog}>
          <section ref={restoreDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-restore-title" aria-describedby="contractor-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Восстановление</p>
                <h3 id="contractor-restore-title">Вернуть запись?</h3>
                <p>{getContractorRestoreTitle(restoreTarget)}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение восстановления контрагента" disabled={confirmationSaving} onClick={closeRestoreDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="contractor-restore-description">Запись снова появится как активная в рабочем списке. Действие записывается в историю изменений.</p>
            {confirmationError ? <FormError>{confirmationError}</FormError> : null}
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={restoreCancelRef} className="ghost-button" type="button" disabled={confirmationSaving} onClick={closeRestoreDialog}>Отмена</button>
              <button className="secondary-button" type="button" aria-busy={confirmationSaving} disabled={confirmationSaving} onClick={() => void confirmRestore()}>
                {confirmationSaving ? <LoaderCircle className="financial-report-button__spinner" size={16} aria-hidden="true" /> : <RotateCcw size={16} />}
                <span>Вернуть запись</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {garageDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeGarageDeleteDialog}>
          <section ref={garageDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="garage-table-delete-title" aria-describedby="garage-table-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="garage-table-delete-title">Удалить гараж?</h3>
                <p>{`Гараж ${garageDeleteTarget.number || 'без номера'}`}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления гаража" disabled={confirmationSaving} onClick={closeGarageDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="garage-table-delete-description">Гараж будет скрыт из рабочего списка, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
            <label className="field-label" htmlFor="garage-table-delete-reason">Причина удаления</label>
            <textarea
              id="garage-table-delete-reason"
              aria-label="Причина удаления гаража"
              maxLength={1000}
              value={garageDeleteReason}
              disabled={confirmationSaving}
              onChange={(event) => setGarageDeleteReason(event.target.value)}
              placeholder="Например: дубликат карточки"
              required
            />
            {confirmationError ? <FormError>{confirmationError}</FormError> : null}
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={garageDeleteCancelRef} className="ghost-button" type="button" disabled={confirmationSaving} onClick={closeGarageDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" aria-busy={confirmationSaving} onClick={() => void confirmGarageDeleteFromTable()} disabled={confirmationSaving || !garageDeleteReason.trim()}>
                {confirmationSaving ? <LoaderCircle className="financial-report-button__spinner" size={16} aria-hidden="true" /> : <Trash2 size={16} />}
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {supplierDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeSupplierDeleteDialog}>
          <section ref={supplierDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="supplier-table-delete-title" aria-describedby="supplier-table-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="supplier-table-delete-title">Удалить поставщика?</h3>
                <p>{supplierDeleteTarget.name || 'Поставщик без названия'}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления поставщика" disabled={confirmationSaving} onClick={closeSupplierDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="supplier-table-delete-description">Поставщик будет скрыт из рабочего списка, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
            <label className="field-label" htmlFor="supplier-table-delete-reason">Причина удаления</label>
            <textarea
              id="supplier-table-delete-reason"
              aria-label="Причина удаления поставщика"
              maxLength={1000}
              value={supplierDeleteReason}
              disabled={confirmationSaving}
              onChange={(event) => setSupplierDeleteReason(event.target.value)}
              placeholder="Например: договор больше не действует"
              required
            />
            {confirmationError ? <FormError>{confirmationError}</FormError> : null}
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={supplierDeleteCancelRef} className="ghost-button" type="button" disabled={confirmationSaving} onClick={closeSupplierDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" aria-busy={confirmationSaving} onClick={() => void confirmSupplierDeleteFromTable()} disabled={confirmationSaving || !supplierDeleteReason.trim()}>
                {confirmationSaving ? <LoaderCircle className="financial-report-button__spinner" size={16} aria-hidden="true" /> : <Trash2 size={16} />}
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {employeeDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeEmployeeDeleteDialog}>
          <section ref={employeeDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="employee-table-delete-title" aria-describedby="employee-table-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="employee-table-delete-title">Удалить сотрудника?</h3>
                <p>{employeeDeleteTarget.fullName || 'Сотрудник без имени'}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления сотрудника" disabled={confirmationSaving} onClick={closeEmployeeDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="employee-table-delete-description">Сотрудник будет скрыт из рабочего списка персонала, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
            <label className="field-label" htmlFor="employee-table-delete-reason">Причина удаления</label>
            <textarea
              id="employee-table-delete-reason"
              aria-label="Причина удаления сотрудника"
              maxLength={1000}
              value={employeeDeleteReason}
              disabled={confirmationSaving}
              onChange={(event) => setEmployeeDeleteReason(event.target.value)}
              placeholder="Например: сотрудник больше не работает"
              required
            />
            {confirmationError ? <FormError>{confirmationError}</FormError> : null}
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={employeeDeleteCancelRef} className="ghost-button" type="button" disabled={confirmationSaving} onClick={closeEmployeeDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" aria-busy={confirmationSaving} onClick={() => void confirmEmployeeDeleteFromTable()} disabled={confirmationSaving || !employeeDeleteReason.trim()}>
                {confirmationSaving ? <LoaderCircle className="financial-report-button__spinner" size={16} aria-hidden="true" /> : <Trash2 size={16} />}
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {departmentDeleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeDepartmentDeleteDialog}>
          <section ref={departmentDeleteDialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="department-table-delete-title" aria-describedby="department-table-delete-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Удаление</p>
                <h3 id="department-table-delete-title">Удалить отдел?</h3>
                <p>{departmentDeleteTarget.name || 'Отдел без названия'}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение удаления отдела" disabled={confirmationSaving} onClick={closeDepartmentDeleteDialog}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="department-table-delete-description">Отдел будет скрыт из рабочего списка персонала, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
            <label className="field-label" htmlFor="department-table-delete-reason">Причина удаления</label>
            <textarea
              id="department-table-delete-reason"
              aria-label="Причина удаления отдела"
              maxLength={1000}
              value={departmentDeleteReason}
              disabled={confirmationSaving}
              onChange={(event) => setDepartmentDeleteReason(event.target.value)}
              placeholder="Например: отдел больше не используется"
              required
            />
            {confirmationError ? <FormError>{confirmationError}</FormError> : null}
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={departmentDeleteCancelRef} className="ghost-button" type="button" disabled={confirmationSaving} onClick={closeDepartmentDeleteDialog}>Отмена</button>
              <button className="secondary-button danger-button" type="button" aria-busy={confirmationSaving} onClick={() => void confirmDepartmentDeleteFromTable()} disabled={confirmationSaving || !departmentDeleteReason.trim()}>
                {confirmationSaving ? <LoaderCircle className="financial-report-button__spinner" size={16} aria-hidden="true" /> : <Trash2 size={16} />}
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </section>
  )
}

function createEmptyGaragePrototype(): ContractorGarageRow {
  return {
    id: `garage-${Date.now()}`,
    ownerId: null,
    number: '',
    peopleCount: '',
    floorCount: '',
    owner: '',
    phone: '',
    address: '',
    startingBalance: '',
    startingOverdueDebt: '',
    balance: '',
    overdueDebt: '',
    initialWater: '',
    initialElectricity: '',
    meters: '',
    comment: '',
    isDeleted: false,
  }
}

function createEmptySupplierPrototype(): ContractorSupplierRow {
  return {
    id: `supplier-${Date.now()}`,
    name: '',
    serviceId: null,
    service: '',
    expenseTypeId: null,
    expenseFundId: null,
    inn: '',
    legalAddress: '',
    contactPerson: '',
    phone: '',
    email: '',
    contacts: [],
    startingBalance: '',
    debt: '',
    comment: '',
    isDeleted: false,
  }
}

function createEmptyEmployeePrototype(department: string): ContractorStaffRow {
  return {
    id: `employee-${Date.now()}`,
    fullName: '',
    department,
    rate: '',
    isDeleted: false,
  }
}

type PrototypeChangeEntry = {
  fieldLabel: string
  previousValue: string
  nextValue: string
}

function createPrototypeChangeEntry(fieldLabel: string, previousValue: string, nextValue: string): PrototypeChangeEntry | null {
  if (previousValue.trim() === nextValue.trim()) {
    return null
  }

  return { fieldLabel, previousValue, nextValue }
}

function compactPrototypeChanges(changes: Array<PrototypeChangeEntry | null>) {
  return changes.filter((change): change is PrototypeChangeEntry => Boolean(change))
}

function getGaragePrototypeChanges(previous: ContractorGarageRow, next: ContractorGarageRow) {
  return compactPrototypeChanges([
    createPrototypeChangeEntry('Номер', previous.number, next.number),
    createPrototypeChangeEntry('Количество человек', previous.peopleCount, next.peopleCount),
    createPrototypeChangeEntry('Этажи', previous.floorCount, next.floorCount),
    createPrototypeChangeEntry('Стартовое значение счетчика воды', previous.initialWater, next.initialWater),
    createPrototypeChangeEntry('Стартовое значение счетчика электричества', previous.initialElectricity, next.initialElectricity),
    createPrototypeChangeEntry('Владелец', previous.owner, next.owner),
    createPrototypeChangeEntry('Телефон', previous.phone, next.phone),
    createPrototypeChangeEntry('Адрес', previous.address, next.address),
    createPrototypeChangeEntry('Счётчики', previous.meters, next.meters),
    createPrototypeChangeEntry('Комментарий', previous.comment, next.comment),
  ])
}

function getSupplierPrototypeChanges(previous: ContractorSupplierRow, next: ContractorSupplierRow) {
  return compactPrototypeChanges([
    createPrototypeChangeEntry('Наименование', previous.name, next.name),
    createPrototypeChangeEntry('Услуга', previous.service, next.service),
    createPrototypeChangeEntry('Фонд расходования', previous.expenseFundId ?? '', next.expenseFundId ?? ''),
    createPrototypeChangeEntry('ИНН', previous.inn, next.inn),
    createPrototypeChangeEntry('Стартовый баланс', previous.startingBalance, next.startingBalance),
    createPrototypeChangeEntry('Задолженность', previous.debt, next.debt),
    createPrototypeChangeEntry('Юридический адрес', previous.legalAddress, next.legalAddress),
    createPrototypeChangeEntry('Контакты', formatSupplierContactSummary(previous.contacts), formatSupplierContactSummary(next.contacts)),
    createPrototypeChangeEntry('Комментарий', previous.comment, next.comment),
  ])
}

function getEmployeePrototypeChanges(previous: ContractorStaffRow, next: ContractorStaffRow) {
  return compactPrototypeChanges([
    createPrototypeChangeEntry('ФИО', previous.fullName, next.fullName),
    createPrototypeChangeEntry('Отдел', previous.department, next.department),
    createPrototypeChangeEntry('Ставка', previous.rate, next.rate),
  ])
}

function PrototypeChangeConfirmationDialog({
  changes,
  objectName,
  onCancel,
  onConfirm,
  saving = false,
  title,
}: {
  changes: PrototypeChangeEntry[]
  objectName: string
  onCancel: () => void
  onConfirm: () => void
  saving?: boolean
  title: string
}) {
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  const cancelRef = useFocusOnOpen<HTMLButtonElement>(true)
  useEscapeKey(!saving, onCancel)

  return (
    <ContractorDialogShell className="dictionary-confirmation-dialog" closeDisabled={saving} closeLabel="Закрыть подтверждение изменений" descriptionId="prototype-change-description" dialogRef={dialogRef} eyebrow="Изменение" onClose={onCancel} subtitle={objectName} title={title} titleId="prototype-change-title">
        <p className="confirmation-text" id="prototype-change-description">Проверьте, что именно изменится. Действие записывается в историю изменений.</p>
        <dl className="dictionary-change-list">
          {changes.map((change) => (
            <div key={change.fieldLabel}>
              <dt>{change.fieldLabel}</dt>
              <dd>{formatPrototypeChangeValue(change.previousValue)} {'->'} {formatPrototypeChangeValue(change.nextValue)}</dd>
            </div>
          ))}
        </dl>
        <div className="detail-dialog-actions contractors-dialog-actions">
          <button ref={cancelRef} className="ghost-button" type="button" disabled={saving} onClick={onCancel}>Отмена</button>
          <button className="secondary-button" type="button" aria-busy={saving} disabled={saving} onClick={onConfirm}>
            {saving ? <LoaderCircle className="financial-report-button__spinner" size={16} aria-hidden="true" /> : <Save size={16} />}
            <span>{saving ? 'Сохраняем…' : 'Сохранить'}</span>
          </button>
        </div>
    </ContractorDialogShell>
  )
}

function SupplierContactDeleteConfirmationDialog({
  contact,
  reason,
  cancelRef,
  dialogRef,
  onReasonChange,
  onCancel,
  onConfirm,
}: {
  contact: ContractorSupplierContact
  reason: string
  cancelRef: RefObject<HTMLButtonElement | null>
  dialogRef: RefObject<HTMLElement | null>
  onReasonChange: (reason: string) => void
  onCancel: () => void
  onConfirm: () => void
}) {
  return (
    <ContractorDialogShell
      closeLabel="Закрыть подтверждение удаления контакта"
      descriptionId="supplier-contact-delete-description"
      dialogRef={dialogRef}
      eyebrow="Удаление"
      onClose={onCancel}
      subtitle={contact.fullName || 'Контакт без ФИО'}
      title="Удалить контакт?"
      titleId="supplier-contact-delete-title"
    >
        <p className="confirmation-text" id="supplier-contact-delete-description">Контакт будет скрыт в карточке поставщика, но его можно будет восстановить. Укажите причину, чтобы действие было видно в истории изменений.</p>
        <label className="field-label" htmlFor="supplier-contact-delete-reason">Причина удаления</label>
        <textarea
          id="supplier-contact-delete-reason"
          aria-label="Причина удаления контакта"
          maxLength={1000}
          value={reason}
          onChange={(event) => onReasonChange(event.target.value)}
          placeholder="Например: контакт больше не работает у поставщика"
          required
        />
        <div className="detail-dialog-actions contractors-dialog-actions">
          <button className="secondary-button danger-button" type="button" onClick={onConfirm} disabled={!reason.trim()}>
            <Trash2 size={16} />
            <span>Удалить</span>
          </button>
          <button ref={cancelRef} className="ghost-button" type="button" onClick={onCancel}>Отмена</button>
        </div>
    </ContractorDialogShell>
  )
}

function SupplierContactRestoreConfirmationDialog({
  contact,
  cancelRef,
  dialogRef,
  onCancel,
  onConfirm,
}: {
  contact: ContractorSupplierContact
  cancelRef: RefObject<HTMLButtonElement | null>
  dialogRef: RefObject<HTMLElement | null>
  onCancel: () => void
  onConfirm: () => void
}) {
  return (
    <ContractorDialogShell
      closeLabel="Закрыть подтверждение восстановления контакта"
      descriptionId="supplier-contact-restore-description"
      dialogRef={dialogRef}
      eyebrow="Восстановление"
      onClose={onCancel}
      subtitle={contact.fullName || 'Контакт без ФИО'}
      title="Восстановить контакт?"
      titleId="supplier-contact-restore-title"
    >
        <p className="confirmation-text" id="supplier-contact-restore-description">Контакт снова станет активным. Если поставщик был скрыт, он тоже будет восстановлен после сохранения карточки.</p>
        <div className="detail-dialog-actions contractors-dialog-actions">
          <button className="secondary-button" type="button" onClick={onConfirm}>
            <RotateCcw size={16} />
            <span>Восстановить</span>
          </button>
          <button ref={cancelRef} className="ghost-button" type="button" onClick={onCancel}>Отмена</button>
        </div>
    </ContractorDialogShell>
  )
}

function SuggestionStatus({ id, message }: { id: string; message: string }) {
  const isAddressSelectionConfirmation = message === 'Адрес выбран из DaData.'
  return (
    <small
      className={`suggestion-status${isAddressSelectionConfirmation ? ' suggestion-status--visually-hidden' : ''}`}
      id={id}
      role={message ? 'status' : undefined}
      aria-live={message ? 'polite' : undefined}
      aria-hidden={message ? undefined : true}
      title={message || undefined}
    >
      {message || '\u00a0'}
    </small>
  )
}

function DadataAddressField({ accessToken, inputLabel, integrationClient, label, listboxLabel, suggestionsId, value, onChange }: { accessToken: string; inputLabel: string; integrationClient: IntegrationClient; label: string; listboxLabel: string; suggestionsId: string; value: string; onChange: (value: string) => void }) {
  const [suggestions, setSuggestions] = useState<DadataAddressSuggestionDto[]>([])
  const [suggestionsOpen, setSuggestionsOpen] = useState(false)
  const [status, setStatus] = useState('')
  const requestSequence = useRef(0)
  const inputTouched = useRef(false)
  const statusId = `${suggestionsId}-status`

  useEffect(() => {
    const query = value.trim()
    const sequence = ++requestSequence.current
    const controller = new AbortController()
    if (!inputTouched.current || query.length < 2) {
      return
    }

    const timer = window.setTimeout(() => {
      setStatus('Ищем адрес...')
      void integrationClient.suggestAddresses(accessToken, query, undefined, controller.signal).then((items) => {
        if (sequence !== requestSequence.current) return
        setSuggestions(items)
        setSuggestionsOpen(items.length > 0)
        setStatus(items.length > 0 ? `Найдено вариантов: ${items.length}` : 'Подходящих адресов не найдено. Можно продолжить ввод вручную.')
      }).catch(() => {
        if (controller.signal.aborted || sequence !== requestSequence.current) return
        setSuggestions([])
        setSuggestionsOpen(false)
        setStatus('Подсказки DaData недоступны. Можно продолжить ввод вручную.')
      })
    }, 350)

    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [accessToken, integrationClient, value])

  function selectSuggestion(suggestion: DadataAddressSuggestionDto) {
    inputTouched.current = false
    onChange(suggestion.unrestrictedValue || suggestion.value)
    setSuggestionsOpen(false)
    setStatus('Адрес выбран из DaData.')
  }

  return (
    <FormField label={label}>
      <div className="suggestion-combobox">
        <input
          aria-label={inputLabel}
          role="combobox"
          aria-autocomplete="list"
          aria-expanded={suggestionsOpen}
          aria-controls={suggestionsId}
          aria-describedby={status ? statusId : undefined}
          autoComplete="off"
          value={value}
          onBlur={() => setSuggestionsOpen(false)}
          onChange={(event) => {
            const nextValue = event.target.value
            inputTouched.current = true
            if (nextValue.trim().length < 2) {
              setSuggestions([])
              setSuggestionsOpen(false)
              setStatus('')
            }
            onChange(nextValue)
          }}
        />
        {suggestionsOpen ? (
          <div className="suggestion-options suggestion-options--above" id={suggestionsId} role="listbox" aria-label={listboxLabel}>
            {suggestions.map((suggestion) => (
              <button className="ghost-button suggestion-option" type="button" role="option" aria-selected="false" title={suggestion.unrestrictedValue || suggestion.value} key={`${suggestion.fiasId ?? ''}-${suggestion.value}`} onMouseDown={(event) => event.preventDefault()} onClick={() => selectSuggestion(suggestion)}>
                <strong>{suggestion.value}</strong>
                {suggestion.postalCode ? <span>Индекс {suggestion.postalCode}</span> : null}
              </button>
            ))}
          </div>
        ) : null}
      </div>
      <SuggestionStatus id={statusId} message={status} />
    </FormField>
  )
}

function OpeningBalanceAdjustmentDialog({ accessToken, dictionaryClient, target, onClose, onSaved }: { accessToken: string; dictionaryClient: DictionaryClient; target: OpeningBalanceAdjustmentTarget; onClose: () => void; onSaved: () => void }) {
  const [effectiveDate, setEffectiveDate] = useState(() => new Date().toLocaleDateString('sv-SE'))
  const [newAmount, setNewAmount] = useState(String(target.currentAmount))
  const [reason, setReason] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useRestoreFocusOnClose(true)
  useEscapeKey(!saving, onClose)

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedAmount = parseStaffRate(newAmount)
    if (!effectiveDate || parsedAmount === null || !reason.trim()) {
      setError('Укажите дату, новое значение и причину корректировки.')
      return
    }

    const save = target.type === 'garage'
      ? dictionaryClient.adjustGarageOpeningBalance
      : dictionaryClient.adjustSupplierOpeningBalance
    if (!save) {
      setError('Корректировка начальных данных недоступна в текущей сборке.')
      return
    }

    setSaving(true)
    setError(null)
    try {
      await save(accessToken, target.id, { effectiveDate, newAmount: parsedAmount, reason: reason.trim() })
      onSaved()
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : 'Не удалось сохранить корректировку.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <ContractorDialogShell className="opening-balance-adjustment-dialog" closeDisabled={saving} closeLabel="Закрыть корректировку начального баланса" dialogRef={dialogRef} eyebrow="Начальные данные" onClose={onClose} title={`Корректировка: ${target.name}`} titleId="opening-balance-adjustment-title">
        <form className="dictionary-modal-form contractors-modal-form" onSubmit={(event) => void submit(event)}>
          {error ? <FormError>{error}</FormError> : null}
          <div className="contractors-modal-grid">
            <FormField label="Действующее значение"><input aria-label="Действующий начальный баланс" value={formatMoney(target.currentAmount)} readOnly /></FormField>
            <FormField label="Новое значение"><MoneyTextInput aria-label="Новое значение начального баланса" value={newAmount} onValueChange={setNewAmount} /></FormField>
            <FormField label="Дата корректировки"><LocalizedDatePicker ariaLabel="Дата корректировки начального баланса" mode="date" value={effectiveDate} required onChange={setEffectiveDate} /></FormField>
          </div>
          <FormField label="Причина"><textarea aria-label="Причина корректировки начального баланса" maxLength={1000} required value={reason} onChange={(event) => setReason(event.target.value)} /></FormField>
          <p className="form-field-hint">Документ и его автор сохраняются в разделе «История изменений».</p>
          <div className="detail-dialog-actions contractors-dialog-actions">
            <button className="secondary-button" type="submit" disabled={saving}>{saving ? <LoaderCircle className="financial-report-button__spinner" size={16} aria-hidden="true" /> : <Save size={17} />}<span>{saving ? 'Сохраняем…' : 'Сохранить корректировку'}</span></button>
            <button className="ghost-button" type="button" disabled={saving} onClick={onClose}>Отмена</button>
          </div>
        </form>
    </ContractorDialogShell>
  )
}

function ContractorDialogShell({ children, className = '', closeDisabled = false, closeLabel, descriptionId, dialogRef, eyebrow, onClose, subtitle, title, titleId }: { children: ReactNode; className?: string; closeDisabled?: boolean; closeLabel: string; descriptionId?: string; dialogRef: RefObject<HTMLElement | null>; eyebrow: string; onClose: () => void; subtitle?: string; title: string; titleId: string }) {
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={closeDisabled ? undefined : onClose}>
      <section ref={dialogRef} className={`detail-dialog contractors-dialog ${className}`.trim()} role="dialog" aria-modal="true" aria-labelledby={titleId} aria-describedby={descriptionId} onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header"><div><p className="eyebrow">{eyebrow}</p><h3 id={titleId}>{title}</h3>{subtitle ? <p>{subtitle}</p> : null}</div><button className="icon-button" type="button" aria-label={closeLabel} disabled={closeDisabled} onClick={onClose}><X size={18} /></button></div>
        {children}
      </section>
    </div>
  )
}

function GaragePrototypeDialog({ accessToken, canAdjustOpeningData, integrationClient, item, onAdjustOpeningBalance, onClose, onOpenFinancialReport, onSave }: { accessToken: string; canAdjustOpeningData: boolean; integrationClient: IntegrationClient; item?: ContractorGarageRow; onAdjustOpeningBalance: (item: ContractorGarageRow) => void; onClose: () => void; onOpenFinancialReport: (item: ContractorGarageRow) => void; onSave: (item: ContractorGarageRow) => Promise<void> }) {
  const [form, setForm] = useState<ContractorGarageRow>(item ?? createEmptyGaragePrototype())
  const [saveChanges, setSaveChanges] = useState<PrototypeChangeEntry[]>([])
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(saveChanges.length === 0)
  useEscapeKey(saveChanges.length === 0 && !saving, onClose)
  const totalDebt = Math.max(parsePrototypeMoney(form.balance), 0)
  const overdueDebt = Math.min(parsePrototypeMoney(form.overdueDebt), totalDebt)
  const notYetOverdueDebt = Math.max(totalDebt - overdueDebt, 0)

  async function saveAndClose() {
    setSaving(true)
    setSaveError(null)
    try {
      await onSave(form)
      setSaveChanges([])
      onClose()
    } catch (error) {
      setSaveChanges([])
      setSaveError(error instanceof Error ? error.message : 'Не удалось сохранить гараж.')
    } finally {
      setSaving(false)
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!item) {
      void saveAndClose()
      return
    }

    const changes = getGaragePrototypeChanges(item, form)
    if (changes.length === 0) {
      onClose()
      return
    }

    setSaveChanges(changes)
  }

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={saving ? undefined : onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog contractors-dialog--wide contractors-dialog--garage" role="dialog" aria-modal="true" aria-labelledby="garage-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="garage-dialog-title">{item ? `Гараж ${item.number}` : 'Новый гараж'}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму гаража" disabled={saving} onClick={onClose}><X size={18} /></button>
          </div>
          <form className="dictionary-modal-form contractors-modal-form" onSubmit={handleSubmit}>
            {saveError ? <FormError>{saveError}</FormError> : null}
            <div className="contractors-garage-form-columns">
              <div className="contractors-garage-form-column" role="group" aria-label="Основные сведения о гараже">
                <label className="form-field">
                  <span className="form-field-label">Номер *</span>
                  <input aria-label="Номер гаража" required value={form.number} onChange={(event) => setForm({ ...form, number: event.target.value })} />
                </label>
                <FormField label="Количество человек"><input aria-label="Количество человек" value={form.peopleCount} onChange={(event) => setForm({ ...form, peopleCount: event.target.value })} /></FormField>
                <FormField label="Этажи"><input aria-label="Этажи гаража" value={form.floorCount} onChange={(event) => setForm({ ...form, floorCount: event.target.value })} /></FormField>
              </div>
              <div className="contractors-garage-form-column contractors-garage-form-column--financial" role="group" aria-label="Финансовые показатели гаража">
                {!item ? (
                  <>
                    <FormField label="Начальный баланс">
                      <MoneyTextInput aria-label="Начальный баланс гаража" value={form.startingBalance ?? ''} onValueChange={(startingBalance) => setForm({ ...form, startingBalance })} />
                    </FormField>
                    <FormField label="Начальная просрочка">
                      <MoneyTextInput aria-label="Начальная просрочка" value={form.startingOverdueDebt ?? ''} onValueChange={(startingOverdueDebt) => setForm({ ...form, startingOverdueDebt })} />
                    </FormField>
                  </>
                ) : (
                  <>
                    <FormField label="Общая задолженность"><input aria-label="Общая задолженность гаража" value={`${formatMoney(totalDebt)} руб.`} readOnly /></FormField>
                    <FormField label="Из неё просрочено"><input aria-label="Просроченная часть задолженности гаража" value={`${formatMoney(overdueDebt)} руб.`} readOnly /></FormField>
                    <FormField label="Срок оплаты не наступил"><input aria-label="Непросроченная часть задолженности гаража" value={`${formatMoney(notYetOverdueDebt)} руб.`} readOnly /></FormField>
                  </>
                )}
                <FormField label="Старт. зн. сч. за воду"><input aria-label="Стартовое значение счетчика воды" value={form.initialWater} onChange={(event) => setForm({ ...form, initialWater: event.target.value })} /></FormField>
                <FormField label="Старт. зн. сч. за эл-во"><input aria-label="Стартовое значение счетчика электричества" value={form.initialElectricity} onChange={(event) => setForm({ ...form, initialElectricity: event.target.value })} /></FormField>
              </div>
            </div>
            <div className="contractors-garage-form-details">
              <FormField label="Владелец"><input aria-label="Владелец гаража" value={form.owner} onChange={(event) => setForm({ ...form, owner: event.target.value })} /></FormField>
              <FormField label="Телефон"><PhoneInput aria-label="Телефон владельца гаража" value={form.phone} onValueChange={(phone) => setForm({ ...form, phone })} /></FormField>
              <DadataAddressField accessToken={accessToken} inputLabel="Адрес гаража" integrationClient={integrationClient} label="Адрес" listboxLabel="Адреса гаражей DaData" suggestionsId="garage-address-suggestions" value={form.address} onChange={(address) => setForm((currentForm) => ({ ...currentForm, address }))} />
            </div>
            <div className="contractors-garage-form-notes">
              <FormField label="Счётчики"><textarea aria-label="Счетчики гаража" maxLength={1000} value={form.meters} onChange={(event) => setForm({ ...form, meters: event.target.value })} /></FormField>
              <FormField label="Комментарий"><textarea aria-label="Комментарий гаража" value={form.comment} onChange={(event) => setForm({ ...form, comment: event.target.value })} /></FormField>
            </div>
            <div className="detail-dialog-actions contractors-dialog-actions contractors-garage-actions">
              {item ? (
                <button className="secondary-button contractors-report-button" type="button" disabled={saving} onClick={() => onOpenFinancialReport(form)}>
                  <FileText size={16} />
                  <span>Открыть фин. отчет</span>
                </button>
              ) : null}
              {item && canAdjustOpeningData ? (
                <button className="secondary-button" type="button" disabled={saving} onClick={() => onAdjustOpeningBalance(form)}>
                  <Pencil size={16} />
                  <span>Корректировать начальный баланс</span>
                </button>
              ) : null}
              <button className="secondary-button" type="submit" aria-busy={saving} disabled={saving}>{saving ? <LoaderCircle className="financial-report-button__spinner" size={17} aria-hidden="true" /> : <Save size={17} />}<span>{saving ? 'Сохраняем…' : 'Сохранить'}</span></button>
              <button className="ghost-button" type="button" disabled={saving} onClick={onClose}>Отмена</button>
            </div>
          </form>
        </section>
      </div>

      {item && saveChanges.length > 0 ? (
        <PrototypeChangeConfirmationDialog changes={saveChanges} objectName={`Гараж ${item.number || 'без номера'}`} saving={saving} onCancel={() => setSaveChanges([])} onConfirm={() => void saveAndClose()} title="Подтвердить изменения гаража" />
      ) : null}
    </>
  )
}

function getDepartmentPrototypeChanges(previous: ContractorDepartmentRow, next: ContractorDepartmentRow) {
  return compactPrototypeChanges([
    createPrototypeChangeEntry('Наименование', previous.name, next.name),
  ])
}

function SupplierPrototypeDialog({ accessToken, canAdjustOpeningData, funds, integrationClient, item, services, onAdjustOpeningBalance, onClose, onOpenFinancialReport, onSave }: { accessToken: string; canAdjustOpeningData: boolean; funds: FundOptionDto[]; integrationClient: IntegrationClient; item?: ContractorSupplierRow; services: ChargeServiceSettingDto[]; onAdjustOpeningBalance: (item: ContractorSupplierRow) => void; onClose: () => void; onOpenFinancialReport: (item: ContractorSupplierRow) => void; onSave: (item: ContractorSupplierRow) => Promise<void> }) {
  const activeServices = services.filter((service) =>
    service.id === item?.serviceId || (!service.isArchived && service.isRegular))
  const initialService = activeServices.find((service) => service.id === item?.serviceId) ?? activeServices.find((service) => service.name === item?.service) ?? activeServices[0] ?? null
  const [form, setForm] = useState<ContractorSupplierRow>(item
    ? { ...item, serviceId: initialService?.id ?? item.serviceId, service: initialService?.name ?? item.service }
    : { ...createEmptySupplierPrototype(), serviceId: initialService?.id ?? null, service: initialService?.name ?? '' })
  const [partySuggestions, setPartySuggestions] = useState<DadataPartySuggestionDto[]>([])
  const [partySuggestionsOpen, setPartySuggestionsOpen] = useState(false)
  const [partySuggestionStatus, setPartySuggestionStatus] = useState('')
  const partyRequestSequence = useRef(0)
  const partyInputTouched = useRef(false)
  const [contactContextMenu, setContactContextMenu] = useState<{ contact: ContractorSupplierContact; x: number; y: number } | null>(null)
  const [contactDeleteTarget, setContactDeleteTarget] = useState<ContractorSupplierContact | null>(null)
  const [contactDeleteReason, setContactDeleteReason] = useState('')
  const [contactRestoreTarget, setContactRestoreTarget] = useState<ContractorSupplierContact | null>(null)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  useRestoreFocusOnClose(true)
  useRestoreFocusOnClose(Boolean(contactDeleteTarget))
  useRestoreFocusOnClose(Boolean(contactRestoreTarget))
  const dialogRef = useFocusTrap<HTMLElement>(!contactDeleteTarget && !contactRestoreTarget)
  const contactDeleteDialogRef = useFocusTrap<HTMLElement>(Boolean(contactDeleteTarget))
  const contactDeleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(contactDeleteTarget))
  const contactRestoreDialogRef = useFocusTrap<HTMLElement>(Boolean(contactRestoreTarget))
  const contactRestoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(contactRestoreTarget))
  useEscapeKey(!contactDeleteTarget && !contactRestoreTarget && !saving, onClose)
  useEscapeKey(Boolean(contactContextMenu), () => setContactContextMenu(null))
  useEscapeKey(Boolean(contactDeleteTarget), () => closeContactDeleteDialog())
  useEscapeKey(Boolean(contactRestoreTarget), () => closeContactRestoreDialog())

  useEffect(() => {
    const query = form.inn.trim()
    const sequence = ++partyRequestSequence.current
    const controller = new AbortController()
    if (!partyInputTouched.current || query.length < 2) {
      return
    }

    const timer = window.setTimeout(() => {
      setPartySuggestionStatus('Ищем организацию...')
      void integrationClient.suggestParties(accessToken, query, undefined, controller.signal).then((suggestions) => {
        if (sequence !== partyRequestSequence.current) return
        setPartySuggestions(suggestions)
        setPartySuggestionsOpen(suggestions.length > 0)
        setPartySuggestionStatus(suggestions.length > 0 ? `Найдено вариантов: ${suggestions.length}` : 'Подходящих организаций не найдено. Можно продолжить ввод вручную.')
      }).catch(() => {
        if (controller.signal.aborted || sequence !== partyRequestSequence.current) return
        setPartySuggestions([])
        setPartySuggestionsOpen(false)
        setPartySuggestionStatus('Подсказки DaData недоступны. Можно продолжить ввод вручную.')
      })
    }, 350)

    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [accessToken, form.inn, integrationClient])

  async function saveAndClose() {
    setSaving(true)
    setSaveError(null)
    try {
      await onSave(normalizeSupplierPrototype(form))
      onClose()
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : 'Не удалось сохранить поставщика.')
    } finally {
      setSaving(false)
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (form.serviceId && !form.expenseFundId) {
      setSaveError('Для поставщика с услугой выберите фонд расходования.')
      return
    }

    if (item && getSupplierPrototypeChanges(item, form).length === 0) {
      onClose()
      return
    }

    void saveAndClose()
  }

  function addContact() {
    setForm((currentForm) => ({ ...currentForm, contacts: [...currentForm.contacts, createEmptySupplierContact()] }))
  }

  function updateContact(contactId: string, patch: Partial<ContractorSupplierContact>) {
    setForm((currentForm) => normalizeSupplierPrototype({
      ...currentForm,
      contacts: currentForm.contacts.map((contact) => (contact.id === contactId ? { ...contact, ...patch } : contact)),
    }))
  }

  function openContactContextMenu(event: MouseEvent<HTMLDivElement>, contact: ContractorSupplierContact) {
    event.preventDefault()
    setContactContextMenu({ contact, x: event.clientX, y: event.clientY })
  }

  function requestDeleteContact(contact: ContractorSupplierContact) {
    setContactContextMenu(null)
    setContactDeleteTarget(contact)
    setContactDeleteReason(contact.deleteReason ?? '')
  }

  function closeContactDeleteDialog() {
    setContactDeleteTarget(null)
    setContactDeleteReason('')
  }

  function requestRestoreContact(contact: ContractorSupplierContact) {
    setContactContextMenu(null)
    setContactRestoreTarget(contact)
  }

  function closeContactRestoreDialog() {
    setContactRestoreTarget(null)
  }

  function confirmContactDelete() {
    if (!contactDeleteTarget || !contactDeleteReason.trim()) {
      return
    }

    updateContact(contactDeleteTarget.id, { isDeleted: true, status: 'Не работает', deleteReason: contactDeleteReason.trim() })
    closeContactDeleteDialog()
  }

  function confirmContactRestore() {
    if (!contactRestoreTarget) {
      return
    }

    setForm((currentForm) => normalizeSupplierPrototype({
      ...currentForm,
      isDeleted: false,
      contacts: currentForm.contacts.map((itemContact) => (itemContact.id === contactRestoreTarget.id ? { ...itemContact, isDeleted: false, status: 'Работает', deleteReason: undefined } : itemContact)),
    }))
    closeContactRestoreDialog()
  }

  const availableServices = [...activeServices].sort((left, right) => left.name.localeCompare(right.name, 'ru'))
  const selectableExpenseFunds = funds.filter((fund) => fund.allowOperations || fund.id === form.expenseFundId)

  function selectPartySuggestion(suggestion: DadataPartySuggestionDto) {
    partyInputTouched.current = false
    setForm((currentForm) => ({
      ...currentForm,
      name: suggestion.value || currentForm.name,
      inn: suggestion.inn || currentForm.inn,
      legalAddress: suggestion.legalAddress || currentForm.legalAddress,
    }))
    setPartySuggestionsOpen(false)
    setPartySuggestionStatus('Организация выбрана из DaData.')
  }

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={saving ? undefined : onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog contractors-dialog--wide contractors-dialog--supplier" role="dialog" aria-modal="true" aria-labelledby="supplier-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="supplier-dialog-title">{item ? form.name : 'Новый поставщик'}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму поставщика" disabled={saving} onClick={onClose}><X size={18} /></button>
          </div>
          <form className="dictionary-modal-form contractors-modal-form" onSubmit={handleSubmit}>
            {saveError ? <FormError>{saveError}</FormError> : null}
            <div className="contractors-supplier-primary-grid">
              <FormField label="Наименование"><input aria-label="Наименование поставщика" value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} /></FormField>
              <FormField label="Услуга">
                <SelectControl
                  aria-label="Услуга поставщика"
                  value={form.serviceId ?? ''}
                  options={[{ value: '', label: 'Выберите услугу' }, ...availableServices.map((service) => ({ value: service.id, label: service.name }))]}
                  onChange={(serviceId) => {
                    const service = availableServices.find((itemService) => itemService.id === serviceId)
                    setForm({ ...form, serviceId: service?.id ?? null, service: service?.name ?? '', expenseTypeId: null })
                  }}
                />
              </FormField>
              <FormField
                label="Фонд расходования"
                help="Фонд применяется при начислениях и банковских выплатах этому поставщику."
              >
                <SelectControl
                  aria-label="Фонд расходования поставщика"
                  value={form.expenseFundId ?? ''}
                  options={[
                    {
                      value: '',
                      label: 'Выберите фонд расходования',
                    },
                    ...selectableExpenseFunds.map((fund) => ({
                      value: fund.id,
                      label: fund.allowOperations ? fund.name : `${fund.name} — операции запрещены`,
                    })),
                  ]}
                  onChange={(expenseFundId) => setForm({ ...form, expenseFundId: expenseFundId || null })}
                />
              </FormField>
            </div>
            <div className="contractors-modal-grid contractors-supplier-lookup-grid">
              <FormField label="ИНН">
                <div className="suggestion-combobox">
                  <input
                    aria-label="ИНН поставщика"
                    role="combobox"
                    aria-autocomplete="list"
                    aria-expanded={partySuggestionsOpen}
                    aria-controls="supplier-party-suggestions"
                    aria-describedby={partySuggestionStatus ? 'supplier-party-suggestions-status' : undefined}
                    autoComplete="off"
                    value={form.inn}
                    onBlur={() => setPartySuggestionsOpen(false)}
                    onChange={(event) => {
                      const value = event.target.value
                      partyInputTouched.current = true
                      setForm({ ...form, inn: value })
                      if (value.trim().length < 2) {
                        setPartySuggestions([])
                        setPartySuggestionsOpen(false)
                        setPartySuggestionStatus('')
                      }
                    }}
                  />
                  {partySuggestionsOpen ? (
                    <div className="suggestion-options" id="supplier-party-suggestions" role="listbox" aria-label="Организации DaData">
                      {partySuggestions.map((suggestion) => (
                        <button className="ghost-button suggestion-option" type="button" role="option" aria-selected="false" title={[suggestion.value, suggestion.inn ? `ИНН ${suggestion.inn}` : null, suggestion.legalAddress].filter(Boolean).join(' · ')} key={`${suggestion.inn ?? ''}-${suggestion.kpp ?? ''}-${suggestion.value}`} onMouseDown={(event) => event.preventDefault()} onClick={() => selectPartySuggestion(suggestion)}>
                          <strong>{suggestion.value}</strong>
                          <span>{[suggestion.inn ? `ИНН ${suggestion.inn}` : null, suggestion.legalAddress].filter(Boolean).join(' · ')}</span>
                        </button>
                      ))}
                    </div>
                  ) : null}
                </div>
                <SuggestionStatus id="supplier-party-suggestions-status" message={partySuggestionStatus} />
              </FormField>
              <FormField label="Начальная задолженность" help={item ? 'Исходная сумма на момент начала учёта. Текущая задолженность с учётом начислений и выплат показана в основной таблице поставщиков.' : undefined}>
                <MoneyTextInput
                  aria-label="Начальная задолженность"
                  value={form.startingBalance}
                  onValueChange={(startingBalance) => setForm({ ...form, startingBalance })}
                />
              </FormField>
              <DadataAddressField accessToken={accessToken} inputLabel="Юридический адрес поставщика" integrationClient={integrationClient} label="Юр. адрес" listboxLabel="Адреса DaData" suggestionsId="supplier-address-suggestions" value={form.legalAddress} onChange={(legalAddress) => setForm((currentForm) => ({ ...currentForm, legalAddress }))} />
            </div>
            <div className="contractors-supplier-contact-summary-grid" aria-describedby="supplier-primary-contact-hint">
              <FormField label="Телефон">
                <PhoneInput
                  aria-label="Телефон поставщика"
                  value={form.phone}
                  onValueChange={(phone) => setForm((currentForm) => updateSupplierPrimaryContact(currentForm, { phone }))}
                />
              </FormField>
              <FormField label="Почта">
                <input
                  aria-label="Почта поставщика"
                  type="email"
                  value={form.email}
                  onChange={(event) => setForm((currentForm) => updateSupplierPrimaryContact(currentForm, { email: event.target.value }))}
                />
              </FormField>
            </div>
            <p className="contractors-supplier-contact-hint" id="supplier-primary-contact-hint">
              Телефон и почта берутся из первого действующего контакта. Изменение здесь сразу обновляет ту же строку в таблице контактов.
            </p>
            <div className="contractors-contacts-toolbar">
              <span>Контакты</span>
              <button className="secondary-button create-action-button" type="button" onClick={addContact}>
                <UsersRound size={17} aria-hidden="true" />
                <span>Добавить контакт</span>
              </button>
            </div>
            <div className="contractors-contacts-preview contractors-contacts-preview--editable" role="table" aria-label="Контакты поставщика">
              <div className="contractors-contacts-row contractors-contacts-row--header contractors-contacts-row--editable" role="row">
                <span role="columnheader">№</span>
                <span role="columnheader">ФИО</span>
                <span role="columnheader">Должность</span>
                <span role="columnheader">Телефон</span>
                <span role="columnheader">Почта</span>
                <span role="columnheader">Статус</span>
                <span role="columnheader">Комментарий</span>
              </div>
              {form.contacts.length === 0 ? (
                <div className="contractors-contacts-row contractors-contacts-row--editable contractors-contacts-row--empty" role="row">
                  <span role="cell">Контакты пока не добавлены</span>
                </div>
              ) : form.contacts.map((contact, index) => (
                <div className={contact.isDeleted ? 'contractors-contacts-row contractors-contacts-row--editable contractors-contacts-row--deleted' : 'contractors-contacts-row contractors-contacts-row--editable'} role="row" key={contact.id} onContextMenu={(event) => openContactContextMenu(event, contact)}>
                  <span role="cell">{index + 1}</span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: ФИО`} value={contact.fullName} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { fullName: event.target.value })} /></span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: должность`} value={contact.position} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { position: event.target.value })} /></span>
                  <span role="cell"><PhoneInput aria-label={`Контакт ${index + 1}: телефон`} value={contact.phone} disabled={contact.isDeleted} onValueChange={(phone) => updateContact(contact.id, { phone })} /></span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: почта`} value={contact.email} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { email: event.target.value })} /></span>
                  <span role="cell">
                    <SelectControl
                      aria-label={`Контакт ${index + 1}: статус`}
                      value={contact.status}
                      options={[{ value: 'Работает', label: 'Работает' }, { value: 'Не работает', label: 'Не работает' }]}
                      disabled={contact.isDeleted}
                      onChange={(status) => updateContact(contact.id, { status: status as ContractorSupplierContact['status'] })}
                    />
                  </span>
                  <span role="cell"><input aria-label={`Контакт ${index + 1}: комментарий`} value={contact.comment} disabled={contact.isDeleted} onChange={(event) => updateContact(contact.id, { comment: event.target.value })} /></span>
                </div>
              ))}
            </div>
            <div className="contractors-supplier-footer-grid">
              <FormField label="Комментарий"><textarea aria-label="Комментарий поставщика" value={form.comment} onChange={(event) => setForm({ ...form, comment: event.target.value })} /></FormField>
            </div>
            <div className="detail-dialog-actions contractors-dialog-actions contractors-garage-actions">
              {item ? (
                <button className="secondary-button contractors-report-button" type="button" disabled={saving} onClick={() => onOpenFinancialReport(form)}>
                  <FileText size={16} />
                  <span>Открыть фин. отчет</span>
                </button>
              ) : null}
              {item && canAdjustOpeningData ? (
                <button className="secondary-button" type="button" disabled={saving} onClick={() => onAdjustOpeningBalance(form)}>
                  <Pencil size={16} />
                  <span>Корректировать начальный баланс</span>
                </button>
              ) : null}
              <button className="secondary-button" type="submit" aria-busy={saving} disabled={saving}>{saving ? <LoaderCircle className="financial-report-button__spinner" size={17} aria-hidden="true" /> : <Save size={17} />}<span>{saving ? 'Сохраняем…' : 'Сохранить'}</span></button>
              <button className="ghost-button" type="button" disabled={saving} onClick={onClose}>Отмена</button>
            </div>
          </form>
        </section>
      </div>

      {contactContextMenu ? (
        <div className="context-menu-backdrop" role="presentation" onMouseDown={() => setContactContextMenu(null)}>
          <div
            className="context-menu contractors-context-menu"
            role="menu"
            aria-label={`Действия контакта ${contactContextMenu.contact.fullName || 'без ФИО'}`}
            style={{ left: contactContextMenu.x, top: contactContextMenu.y }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {contactContextMenu.contact.isDeleted ? (
              <>
                <p className="context-menu-hint">При восстановлении контакта будет восстановлен и поставщик.</p>
                <div className="context-menu-group" role="group">
                  <button type="button" role="menuitem" onClick={() => requestRestoreContact(contactContextMenu.contact)}>
                    <RotateCcw size={16} />
                    <span>Восстановить контакт</span>
                  </button>
                </div>
              </>
            ) : (
              <div className="context-menu-group" role="group">
                <button className="context-menu-danger" type="button" role="menuitem" onClick={() => requestDeleteContact(contactContextMenu.contact)}>
                  <Trash2 size={16} />
                  <span>Удалить контакт</span>
                </button>
              </div>
            )}
          </div>
        </div>
      ) : null}

      {contactDeleteTarget ? (
        <SupplierContactDeleteConfirmationDialog
          contact={contactDeleteTarget}
          reason={contactDeleteReason}
          cancelRef={contactDeleteCancelRef}
          dialogRef={contactDeleteDialogRef}
          onReasonChange={setContactDeleteReason}
          onCancel={closeContactDeleteDialog}
          onConfirm={confirmContactDelete}
        />
      ) : null}
      {contactRestoreTarget ? (
        <SupplierContactRestoreConfirmationDialog
          contact={contactRestoreTarget}
          cancelRef={contactRestoreCancelRef}
          dialogRef={contactRestoreDialogRef}
          onCancel={closeContactRestoreDialog}
          onConfirm={confirmContactRestore}
        />
      ) : null}
    </>
  )
}

function EmployeePrototypeDialog({ departments, item, onClose, onOpenFinancialReport, onSave }: { departments: ContractorDepartmentRow[]; item?: ContractorStaffRow; onClose: () => void; onOpenFinancialReport: (item: ContractorStaffRow) => void; onSave: (item: ContractorStaffRow) => Promise<void> }) {
  const activeDepartments = departments.filter((department) => !department.isDeleted)
  const [form, setForm] = useState<ContractorStaffRow>(item ?? createEmptyEmployeePrototype(activeDepartments[0]?.name ?? departments[0]?.name ?? ''))
  const selectableDepartments = departments.filter((department) => !department.isDeleted || department.name === form.department)
  const [saveChanges, setSaveChanges] = useState<PrototypeChangeEntry[]>([])
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(saveChanges.length === 0)
  useEscapeKey(saveChanges.length === 0 && !saving, onClose)

  async function saveAndClose() {
    setSaving(true)
    setSaveError(null)
    try {
      await onSave({ ...form, rate: formatStaffRate(form.rate) })
      setSaveChanges([])
      onClose()
    } catch (error) {
      setSaveChanges([])
      setSaveError(error instanceof Error ? error.message : 'Не удалось сохранить сотрудника.')
    } finally {
      setSaving(false)
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!item) {
      void saveAndClose()
      return
    }

    const normalizedForm = { ...form, rate: formatStaffRate(form.rate) }
    const changes = getEmployeePrototypeChanges(item, normalizedForm)
    if (changes.length === 0) {
      onClose()
      return
    }

    setForm(normalizedForm)
    setSaveChanges(changes)
  }

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={saving ? undefined : onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog contractors-dialog--staff" role="dialog" aria-modal="true" aria-labelledby="employee-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="employee-dialog-title">{item ? form.fullName : 'Новый сотрудник'}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму сотрудника" disabled={saving} onClick={onClose}><X size={18} /></button>
          </div>
          <form className="dictionary-modal-form contractors-modal-form" onSubmit={handleSubmit}>
            {saveError ? <FormError>{saveError}</FormError> : null}
            <FormField label="ФИО"><input aria-label="ФИО сотрудника" value={form.fullName} onChange={(event) => setForm({ ...form, fullName: event.target.value })} /></FormField>
            <div className="contractors-staff-fields">
              <FormField label="Отдел">
                <SelectControl
                  aria-label="Отдел сотрудника"
                  value={form.department}
                  placement="above"
                  maxVisibleOptions={3}
                  options={selectableDepartments.length > 0
                    ? selectableDepartments.map((department) => ({ value: department.name, label: department.name }))
                    : [{ value: '', label: 'Отделы не настроены' }]}
                  onChange={(department) => setForm({ ...form, department })}
                />
              </FormField>
              <FormField label="Ставка">
                <div className="contractors-inline-field contractors-staff-rate-field">
                  <MoneyTextInput
                    aria-label="Ставка сотрудника"
                    value={form.rate}
                    onValueChange={(rate) => setForm({ ...form, rate })}
                  />
                  <span>руб.</span>
                </div>
              </FormField>
            </div>
            <div className="detail-dialog-actions contractors-dialog-actions contractors-staff-actions">
              {item ? (
                <button className="secondary-button contractors-report-button" type="button" disabled={saving} onClick={() => onOpenFinancialReport(form)}>
                  <FileText size={16} />
                  <span>Открыть фин. отчет</span>
                </button>
              ) : null}
              <div className="contractors-dialog-submit-actions">
                <button className="secondary-button" type="submit" aria-busy={saving} disabled={saving}>{saving ? <LoaderCircle className="financial-report-button__spinner" size={17} aria-hidden="true" /> : <Save size={17} />}<span>{saving ? 'Сохраняем…' : 'Сохранить'}</span></button>
                <button className="ghost-button" type="button" disabled={saving} onClick={onClose}>Отмена</button>
              </div>
            </div>
          </form>
        </section>
      </div>

      {item && saveChanges.length > 0 ? (
        <PrototypeChangeConfirmationDialog changes={saveChanges} objectName={item.fullName || 'Сотрудник'} saving={saving} onCancel={() => setSaveChanges([])} onConfirm={() => void saveAndClose()} title="Подтвердить изменения сотрудника" />
      ) : null}

    </>
  )
}

function DepartmentPrototypeDialog({ item, onClose, onSave }: { item?: ContractorDepartmentRow; onClose: () => void; onSave: (department: ContractorDepartmentRow) => Promise<void> }) {
  const [form, setForm] = useState<ContractorDepartmentRow>(() => item ?? { id: `department-${Date.now()}`, name: '', isDeleted: false })
  const [saveChanges, setSaveChanges] = useState<PrototypeChangeEntry[]>([])
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  useRestoreFocusOnClose(true)
  const dialogRef = useFocusTrap<HTMLElement>(saveChanges.length === 0)
  useEscapeKey(saveChanges.length === 0 && !saving, onClose)

  async function saveAndClose() {
    setSaving(true)
    setSaveError(null)
    try {
      await onSave(form)
      setSaveChanges([])
      onClose()
    } catch (error) {
      setSaveChanges([])
      setSaveError(error instanceof Error ? error.message : 'Не удалось сохранить отдел.')
    } finally {
      setSaving(false)
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!item) {
      void saveAndClose()
      return
    }

    const changes = getDepartmentPrototypeChanges(item, form)
    if (changes.length === 0) {
      onClose()
      return
    }

    setSaveChanges(changes)
  }

  return (
    <>
      <div className="modal-backdrop" role="presentation" onMouseDown={saving ? undefined : onClose}>
        <section ref={dialogRef} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="department-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
          <div className="detail-dialog-header">
            <h3 id="department-dialog-title">{item ? form.name : 'Новый отдел'}</h3>
            <button className="icon-button" type="button" aria-label="Закрыть форму отдела" disabled={saving} onClick={onClose}><X size={18} /></button>
          </div>
          <form className="dictionary-modal-form contractors-modal-form" onSubmit={handleSubmit}>
            {saveError ? <FormError>{saveError}</FormError> : null}
            <FormField label="Наименование"><input aria-label="Наименование отдела" maxLength={200} required value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} /></FormField>
            <div className="detail-dialog-actions">
              <button className="secondary-button" type="submit" aria-busy={saving} disabled={saving}>{saving ? <LoaderCircle className="financial-report-button__spinner" size={17} aria-hidden="true" /> : <Save size={17} />}<span>{saving ? 'Сохраняем…' : item ? 'Сохранить' : 'Ок'}</span></button>
              <button className="ghost-button" type="button" disabled={saving} onClick={onClose}>Отмена</button>
            </div>
          </form>
        </section>
      </div>

      {item && saveChanges.length > 0 ? (
        <PrototypeChangeConfirmationDialog changes={saveChanges} objectName={item.name || 'Отдел'} saving={saving} onCancel={() => setSaveChanges([])} onConfirm={() => void saveAndClose()} title="Подтвердить изменения отдела" />
      ) : null}
    </>
  )
}
