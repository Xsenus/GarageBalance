import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'

vi.mock('./services/formStatesApi', () => ({
  FormStateApiError: class FormStateApiError extends Error {},
  formStatesApi: {
    getState: vi.fn(async () => null),
    saveState: vi.fn(async (_accessToken: string, scope: string, request: { payload: unknown }) => ({
      scope,
      payload: request.payload,
      updatedAtUtc: '2026-06-30T03:00:00Z',
      updatedByUserId: 'admin-user',
    })),
  },
}))

import App from './App'
import { formStatesApi } from './services/formStatesApi'
import type { AuditClient, AuditEventDto } from './services/auditApi'
import type { AuthClient, AuthResponse } from './services/authApi'
import { DictionaryApiError } from './services/dictionariesApi'
import type { AccountingTypeDto, ChargeServiceSettingDto, DictionaryClient, FeeCampaignDto, GarageDto, IrregularPaymentDto, OwnerDto, StaffDepartmentDto, StaffMemberDto, SupplierContactDto, SupplierDto, SupplierGroupDto, TariffDto, UpsertGarageRequest, UpsertStaffMemberRequest, UpsertSupplierRequest, UpsertTariffRequest } from './services/dictionariesApi'
import type { AccrualDto, CreateAccrualRequest, CreateDebtTransferRequest, CreateExpenseOperationRequest, CreateIncomeOperationRequest, CreateMeterReadingRequest, CreateStaffPaymentRequest, CreateSupplierAccrualRequest, ExpenseWorksheetDto, FeeCampaignAccrualGenerationResultDto, FinanceClient, FinanceSummaryDto, FinancialOperationDto, GarageBalanceHistoryDto, GarageIncomeWorksheetDto, GenerateFeeCampaignAccrualsRequest, GenerateRegularCatalogAccrualsRequest, GenerateSupplierGroupSalaryAccrualsRequest, MeterReadingDto, MissingMeterReadingDto, RegularAccrualGenerationResultDto, RegularCatalogAccrualGenerationResultDto, SupplierAccrualDto, SupplierGroupSalaryAccrualGenerationResultDto } from './services/financeApi'
import type { CreateFundOperationRequest, FundDto, FundOperationDto, FundsClient } from './services/fundsApi'
import type { AccessImportCreatedRecordDto, AccessImportQuarantineItemDto, AccessImportReaderStatusDto, AccessImportRunDto, AccessImportRunLogEntryDto, ImportClient } from './services/importApi'
import type { IntegrationClient, OneCFreshIntegrationStatusDto, OneCFreshSyncDto, OneCFreshSyncRequest, ReceiptPrintingActionDto, ReceiptPrintingActionRequest, ReceiptPrintingIntegrationStatusDto } from './services/integrationsApi'
import type { BankDepositReportDto, CashPaymentReportDto, ConsolidatedReportDto, ExpenseReportDto, FeeReportDto, FundChangeReportDto, IncomeReportDto, ReportClient } from './services/reportsApi'
import type { AppReleaseDto, ReleaseClient } from './services/releasesApi'
import type { ManagedRoleDto, ManagedUserDto, UserManagementClient } from './services/usersApi'

describe('App', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-06-30T10:00:00+07:00'))
    vi.mocked(formStatesApi.getState).mockImplementation(async () => null)
    vi.mocked(formStatesApi.saveState).mockImplementation(async (_accessToken: string, scope: string, request: { payload: unknown }) => ({
      scope,
      payload: request.payload,
      updatedAtUtc: '2026-06-30T03:00:00Z',
      updatedByUserId: 'admin-user',
    }))
    window.sessionStorage.clear()
    window.localStorage.clear()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  async function openSection(user: ReturnType<typeof userEvent.setup>, name: string) {
    const mainNavigation = screen.queryByRole('navigation', { name: 'Основные разделы' })
    if (mainNavigation) {
      const tab = within(mainNavigation).getByRole('button', { name })

      if (tab.getAttribute('aria-current') !== 'page') {
        await user.click(tab)
      }

      return
    }

    if (!screen.queryByRole('group', { name: 'Главные разделы' })) {
      await user.click(screen.getByRole('button', { name: 'Назад к выбору раздела' }))
    }

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    const tileName = name === 'Справочники' ? /Тарифы\s+и\s+сборы/i : name === 'Отчеты' ? 'Отчёты' : name
    await user.click(within(dashboardTiles).getByRole('button', { name: tileName }))
  }

  async function openFinanceContextMenuByCellText(panel: HTMLElement, text: string) {
    const row = (await within(panel).findAllByText(text))
      .map((node) => node.closest('tr'))
      .find((node): node is HTMLTableRowElement => node !== null)

    if (!row) {
      throw new Error(`Строка платежной таблицы с текстом "${text}" не найдена.`)
    }

    fireEvent.contextMenu(row)
    return screen.findByRole('menu', { name: 'Операции с платежами' })
  }

  async function openDictionarySubgroup(user: ReturnType<typeof userEvent.setup>, panel: HTMLElement, name: string) {
    const aliases: Record<string, string[]> = {
      'Группы поставщиков': ['Группы поставщиков и персонала'],
      'Поставщики': ['Поставщики и персонал'],
    }
    const names = [name, ...(aliases[name] ?? [])]
    const button = names
      .map((candidate) => within(panel).queryByRole('button', { name: `Подгруппа: ${candidate}` }))
      .find((candidate): candidate is HTMLElement => candidate !== null)

    if (!button) {
      throw new Error(`Подгруппа ${name} не найдена.`)
    }

    if (button.getAttribute('aria-current') !== 'page') {
      await user.click(button)
    }

    return within(panel).findByRole('table', { name: new RegExp(`Таблица: (${names.join('|')})`) })
  }

  async function openDictionaryCreateDialog(user: ReturnType<typeof userEvent.setup>, panel: HTMLElement) {
    await user.click(within(panel).getByRole('button', { name: 'Добавить' }))
    return screen.findByRole('dialog')
  }

  async function openReportTab(user: ReturnType<typeof userEvent.setup>, panel: HTMLElement, name: string) {
    const tab = within(panel).getByRole('tab', { name: new RegExp(name) })
    if (tab.getAttribute('aria-selected') !== 'true') {
      await user.click(tab)
    }

    return tab
  }

  it('shows auth gate before workspace is available', () => {
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    expect(screen.getByRole('region', { name: 'Вход в систему' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Авторизация' })).toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toBeInTheDocument()
    expect(screen.getByLabelText('Пароль')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Войти' })).toBeInTheDocument()
    expect(screen.queryByText('GarageBalance')).not.toBeInTheDocument()
    expect(screen.queryByText('Минимум 8 символов: заглавная буква, строчная буква и цифра.')).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /сначала вход и права/i })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Имя пользователя')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Создать администратора' })).not.toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Основные разделы' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /финансовый учет гск/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Главное меню' })).not.toBeInTheDocument()
  })

  it('restores authenticated workspace after page reload', async () => {
    const user = userEvent.setup()
    const auth = createAuthResponse({ accessToken: 'persisted-token' })
    const { unmount } = render(<App authClient={createAuthClient({ login: async () => auth })} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    expect((await screen.findAllByRole('button', { name: /Тарифы\s+и\s+сборы/i })).length).toBeGreaterThan(0)
    expect(JSON.parse(window.sessionStorage.getItem('garagebalance.auth.session') ?? '{}').accessToken).toBe('persisted-token')

    unmount()

    const authClient = createAuthClient({
      bootstrapAdmin: async () => {
        throw new Error('Bootstrap не должен вызываться при восстановлении сессии.')
      },
      login: async () => {
        throw new Error('Login не должен вызываться при восстановлении сессии.')
      },
    })

    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} integrationClient={createIntegrationClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    expect(screen.queryByRole('region', { name: 'Вход в систему' })).not.toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /Тарифы\s+и\s+сборы/i }).length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: 'Главное меню' })).toBeEnabled()
  })

  it('ignores expired stored auth session', () => {
    window.sessionStorage.setItem('garagebalance.auth.session', JSON.stringify(createAuthResponse({ expiresAtUtc: new Date(Date.now() - 60_000).toISOString() })))

    render(
      <App
        authClient={createAuthClient()}
        auditClient={createThrowingClient<AuditClient>()}
        dictionaryClient={createThrowingClient<DictionaryClient>()}
        financeClient={createThrowingClient<FinanceClient>()}
        importClient={createThrowingClient<ImportClient>()}
        reportClient={createThrowingClient<ReportClient>()}
        releaseClient={createThrowingClient<ReleaseClient>()}
        userClient={createThrowingClient<UserManagementClient>()}
      />,
    )

    expect(screen.getByRole('region', { name: 'Вход в систему' })).toBeInTheDocument()
    expect(window.sessionStorage.getItem('garagebalance.auth.session')).toBeNull()
  })

  it('does not call protected workspace clients before authentication', () => {
    render(
      <App
        authClient={createAuthClient()}
        auditClient={createThrowingClient<AuditClient>()}
        dictionaryClient={createThrowingClient<DictionaryClient>()}
        financeClient={createThrowingClient<FinanceClient>()}
        importClient={createThrowingClient<ImportClient>()}
        reportClient={createThrowingClient<ReportClient>()}
        releaseClient={createThrowingClient<ReleaseClient>()}
        userClient={createThrowingClient<UserManagementClient>()}
      />,
    )

    expect(screen.getByRole('region', { name: 'Вход в систему' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /финансовый учет гск/i })).not.toBeInTheDocument()
  })

  it('creates first administrator and opens the workspace with users and dictionaries', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.clear(screen.getByLabelText('Пароль'))
    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    expect(within(dashboardTiles).getByRole('button', { name: /Тарифы\s+и\s+сборы/i })).toBeInTheDocument()
    expect(within(dashboardTiles).getByRole('button', { name: 'Контрагенты' })).toBeInTheDocument()
    expect(within(dashboardTiles).getByRole('button', { name: 'Счётчики' })).toBeInTheDocument()
    expect(within(dashboardTiles).getByRole('button', { name: 'Платежи' })).toBeInTheDocument()
    expect(within(dashboardTiles).getByRole('button', { name: 'Отчёты' })).toBeInTheDocument()
    expect(within(dashboardTiles).getByRole('button', { name: 'Настройки' })).toBeInTheDocument()
    expect(within(dashboardTiles).getByRole('button', { name: /Управление\s+фондами/i })).toBeInTheDocument()
    expect(screen.queryByText('Поиск по гаражу, владельцу или поставщику')).not.toBeInTheDocument()
    expect(screen.getAllByText('Администратор').length).toBeGreaterThan(0)
    expect(screen.getAllByText('administrator').length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: 'Главное меню' })).toBeEnabled()

    await openSection(user, 'Пользователи')

    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })
    expect(within(usersPanel).getByText('Администратор ГСК')).toBeInTheDocument()
    expect(within(usersPanel).getByText('admin@example.com')).toBeInTheDocument()
    const roleMatrix = within(usersPanel).getByRole('region', { name: 'Матрица ролей' })
    expect(within(roleMatrix).getByRole('table', { name: 'Матрица ролей и прав' })).toBeInTheDocument()
    expect(within(roleMatrix).getByText('Администратор')).toBeInTheDocument()
    expect(within(roleMatrix).getByText('Бухгалтер')).toBeInTheDocument()
    expect(within(roleMatrix).getByText('История изменений')).toBeInTheDocument()
    expect(within(roleMatrix).getByRole('cell', { name: 'Бухгалтер: Тарифы - разрешено' })).toHaveTextContent('Да')
    expect(within(roleMatrix).getByRole('cell', { name: 'Оператор: Отчеты - нет доступа' })).toHaveTextContent('Нет')

    await openSection(user, 'Справочники')

    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    expect(within(dictionaryPanel).getAllByText('Иванов Иван').length).toBeGreaterThan(0)
    await openDictionarySubgroup(user, dictionaryPanel, 'Гаражи')
    expect(within(dictionaryPanel).getByText('12')).toBeInTheDocument()
    await openDictionarySubgroup(user, dictionaryPanel, 'Поставщики')
    expect(within(dictionaryPanel).getByText('Водоканал')).toBeInTheDocument()
    await openDictionarySubgroup(user, dictionaryPanel, 'Виды поступлений')
    expect(within(dictionaryPanel).getByText('Членский взнос')).toBeInTheDocument()
    await openDictionarySubgroup(user, dictionaryPanel, 'Тарифы')
    expect(within(dictionaryPanel).getByText('Тариф воды')).toBeInTheDocument()

    await openSection(user, 'Платежи')

    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    expect(within(financePanel).getAllByText('1 500,00').length).toBeGreaterThan(0)
    expect(within(financePanel).getAllByText('2 000,00').length).toBeGreaterThan(0)
    expect(within(financePanel).getByText('500,00')).toBeInTheDocument()
    expect(within(financePanel).getAllByText('1').length).toBeGreaterThan(0)
    expect(within(financePanel).getAllByText('19.06.2026').length).toBeGreaterThan(0)
    expect(within(financePanel).getAllByText('06.2026').length).toBeGreaterThan(0)
    expect(within(financePanel).queryByText('2026-06-19')).not.toBeInTheDocument()
    expect(within(financePanel).getAllByText('Гараж 12').length).toBeGreaterThan(0)
  })

  it('edits role permissions from matrix with no-op and confirmation', async () => {
    const user = userEvent.setup()
    let roles = createRoles()
    const updateRolePermissions = vi.fn(async (_token: string, roleCode: string, request: { permissions: string[] }) => {
      const role = roles.find((item) => item.code === roleCode)
      if (!role) {
        throw new Error('Роль не найдена.')
      }

      const updatedRole = { ...role, permissions: request.permissions }
      roles = roles.map((item) => (item.code === roleCode ? updatedRole : item))
      return updatedRole
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient({
      getRoles: async () => roles,
      updateRolePermissions,
    })} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Пользователи')

    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })
    const roleMatrix = within(usersPanel).getByRole('region', { name: 'Матрица ролей' })
    expect(within(roleMatrix).getByRole('cell', { name: 'Оператор: Отчеты - нет доступа' })).toHaveTextContent('Нет')

    await user.click(within(roleMatrix).getByRole('button', { name: 'Изменить права роли Оператор' }))
    let roleDialog = await screen.findByRole('dialog', { name: 'Изменить права роли' })
    await user.click(within(roleDialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердите изменение прав роли' })).not.toBeInTheDocument())
    expect(updateRolePermissions).not.toHaveBeenCalled()

    await user.click(within(roleMatrix).getByRole('button', { name: 'Изменить права роли Оператор' }))
    roleDialog = await screen.findByRole('dialog', { name: 'Изменить права роли' })
    await user.click(within(roleDialog).getByLabelText('Оператор: Отчеты'))
    await user.click(within(roleDialog).getByRole('button', { name: 'Сохранить' }))

    const confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменение прав роли' })
    const changeList = within(confirmationDialog).getByRole('list', { name: 'Изменяемые поля роли' })
    expect(changeList).toHaveTextContent('Права')
    expect(changeList).toHaveTextContent('Платежи')
    expect(changeList).toHaveTextContent('Отчеты')
    expect(updateRolePermissions).not.toHaveBeenCalled()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердите изменение прав роли' })).not.toBeInTheDocument())
    expect(updateRolePermissions).not.toHaveBeenCalled()

    await user.click(within(roleDialog).getByRole('button', { name: 'Сохранить' }))
    const reopenedConfirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменение прав роли' })
    await user.click(within(reopenedConfirmationDialog).getByRole('button', { name: 'Сохранить права' }))

    await waitFor(() => expect(updateRolePermissions).toHaveBeenCalledWith('token', 'operator', expect.objectContaining({
      permissions: expect.arrayContaining(['reports.read']),
    })))
    await waitFor(() => expect(within(roleMatrix).getByRole('cell', { name: 'Оператор: Отчеты - разрешено' })).toHaveTextContent('Да'))
  })

  it('shows icon back button in every dashboard section', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const tileNames: Array<string | RegExp> = [
      /Тарифы\s+и\s+сборы/i,
      'Контрагенты',
      'Счётчики',
      'Платежи',
      'Отчёты',
      'Настройки',
      /Управление\s+фондами/i,
    ]

    for (const tileName of tileNames) {
      const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
      await user.click(within(dashboardTiles).getByRole('button', { name: tileName }))

      const backButton = await screen.findByRole('button', { name: 'Назад к выбору раздела' })
      expect(backButton).toHaveClass('topbar-back-button')
      expect(backButton).toHaveAttribute('title', 'Назад к выбору раздела')
      expect(within(backButton).queryByText('Главное меню')).not.toBeInTheDocument()
      expect(screen.queryByRole('group', { name: 'Главные разделы' })).not.toBeInTheDocument()

      await user.click(backButton)
      expect(await screen.findByRole('group', { name: 'Главные разделы' })).toBeInTheDocument()
    }
  })

  it('shows tariffs and fees prototype page and opens service and fee modals', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: /Тарифы\s+и\s+сборы/i }))

    const tariffsPanel = await screen.findByRole('region', { name: 'Тарифы и сборы' })
    expect(screen.queryByText('Поиск по гаражу, владельцу или поставщику')).not.toBeInTheDocument()
    expect(within(tariffsPanel).getByRole('table', { name: 'Тарифы и сборы' })).toBeInTheDocument()
    expect(within(tariffsPanel).getByText('Тариф воды')).toBeInTheDocument()
    expect(within(tariffsPanel).getByText('Нерегулярные платежи')).toBeInTheDocument()

    const addTariffServiceButton = within(tariffsPanel).getAllByRole('button', { name: 'Добавить услугу' })[0]
    await user.click(addTariffServiceButton)
    const serviceDialog = await screen.findByRole('dialog', { name: 'Добавить услугу' })
    expect(within(serviceDialog).getByLabelText('Стоимость услуги')).toBeInTheDocument()
    expect(within(serviceDialog).queryByLabelText('Периодичность')).not.toBeInTheDocument()
    await user.click(within(serviceDialog).getByLabelText('Регулярные платежи'))
    expect(within(serviceDialog).getByLabelText('Периодичность')).toHaveValue('12')
    expect(within(serviceDialog).getByLabelText('Учитывать платеж с')).toHaveValue('янв')
    expect(within(serviceDialog).getByLabelText('День оплаты')).toHaveValue('30')
    expect(within(serviceDialog).getByLabelText('Месяц оплаты')).toHaveValue('июл')
    expect(within(serviceDialog).getByRole('combobox', { name: 'Учитывать платеж с' })).toHaveTextContent('Декабрь')
    expect(within(serviceDialog).getByRole('combobox', { name: 'Учитывать платеж с' }).querySelectorAll('option')).toHaveLength(12)
    expect(within(serviceDialog).getByLabelText('Перенос долга в просроченный')).toHaveValue('30')
    expect(within(serviceDialog).getByLabelText('По счетчику')).toBeChecked()
    expect(within(serviceDialog).getByLabelText('Пороговая тарификация')).toBeChecked()
    expect(within(serviceDialog).getByLabelText('Цена за единицу 1')).toBeInTheDocument()
    expect(within(serviceDialog).getByLabelText('Единица измерения')).toHaveAttribute('list', 'contractor-service-unit-options')
    expect(Array.from(serviceDialog.querySelectorAll<HTMLDataListElement>('#contractor-service-unit-options option')).map((option) => option.value)).toEqual(['руб.'])
    expect(within(serviceDialog).getByLabelText('По счетчику').closest('.contractors-service-flags')).toContainElement(within(serviceDialog).getByLabelText('Пороговая тарификация'))
    await user.click(within(serviceDialog).getByLabelText('Пороговая тарификация'))
    expect(within(serviceDialog).queryByLabelText('Цена за единицу 1')).not.toBeInTheDocument()
    expect(within(serviceDialog).queryByRole('button', { name: 'Добавить порог' })).not.toBeInTheDocument()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Добавить услугу' })).not.toBeInTheDocument())
    await waitFor(() => expect(addTariffServiceButton).toHaveFocus())

    const addTariffFeeButton = within(tariffsPanel).getAllByRole('button', { name: 'Объявить сбор' })[0]
    await user.click(addTariffFeeButton)
    const feeDialog = await screen.findByRole('dialog', { name: 'Добавить сбор' })
    expect(within(feeDialog).getByLabelText('Наименование сбора')).toBeInTheDocument()
    expect(within(feeDialog).getByLabelText('Вид поступления для сбора')).toHaveValue('income-type-1')
    expect(within(feeDialog).getByLabelText('Цель сбора')).toBeInTheDocument()
    expect(within(feeDialog).getByLabelText('Сумма взноса')).toBeInTheDocument()
    expect(within(feeDialog).getByLabelText('Все гаражи')).toBeChecked()
    expect(within(feeDialog).getByLabelText('Сумма сбора')).toBeInTheDocument()
    expect(within(feeDialog).getByRole('button', { name: 'Сегодня' })).toBeInTheDocument()
    expect(within(feeDialog).getByLabelText('Перенос долга по сбору в просроченный')).toBeInTheDocument()
    await user.type(within(feeDialog).getByLabelText('Наименование сбора'), 'Черновой сбор')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Добавить сбор' })).not.toBeInTheDocument())
    await waitFor(() => expect(addTariffFeeButton).toHaveFocus())
    expect(within(tariffsPanel).queryByText('Черновой сбор')).not.toBeInTheDocument()
  })

  it('creates, accrues, archives and restores announced fee campaigns from tariffs page', async () => {
    const user = userEvent.setup()
    const targetIncomeType = createAccountingType({ id: 'income-type-target', name: 'Целевой взнос', code: 'target' })
    const participantGarage = createGarage({ id: 'garage-target', number: '27', ownerName: 'Сидорова Анна' })
    const otherGarage = createGarage({ id: 'garage-other', number: '12', ownerName: 'Петров Петр' })
    let campaigns = [
      createFeeCampaign({ id: 'fee-campaign-active', name: 'Сбор на ворота', incomeTypeId: targetIncomeType.id, incomeTypeName: targetIncomeType.name }),
      createFeeCampaign({ id: 'fee-campaign-archived', name: 'Старый сбор', incomeTypeId: targetIncomeType.id, incomeTypeName: targetIncomeType.name, isArchived: true }),
    ]
    const createdRequests: string[] = []
    const updatedRequests: Array<{ id: string; request: unknown }> = []
    const archiveRequests: Array<{ id: string; reason: string }> = []
    const restoredRequests: string[] = []
    const generateRequests: GenerateFeeCampaignAccrualsRequest[] = []
    const dictionaryClient = createDictionaryClient({
      getGarages: async () => [participantGarage, otherGarage],
      getIncomeTypes: async () => [targetIncomeType],
      getFeeCampaigns: async () => campaigns,
      createFeeCampaign: async (_token, request) => {
        const campaign = createFeeCampaign({
          id: 'fee-campaign-new',
          name: request.name,
          incomeTypeId: targetIncomeType.id,
          incomeTypeName: targetIncomeType.name,
          goal: request.goal ?? null,
          contributionAmount: request.contributionAmount,
          targetAmount: request.targetAmount,
          startsOn: request.startsOn,
          endsOn: request.endsOn ?? null,
          appliesToAllGarages: request.appliesToAllGarages,
          participantGarageIds: request.participantGarageIds ?? [],
          overdueGraceDays: request.overdueGraceDays,
        })
        createdRequests.push(JSON.stringify(request))
        campaigns = [campaign, ...campaigns]
        return campaign
      },
      updateFeeCampaign: async (_token, id, request) => {
        const campaign = createFeeCampaign({
          id,
          name: request.name,
          incomeTypeId: targetIncomeType.id,
          incomeTypeName: targetIncomeType.name,
          goal: request.goal ?? null,
          contributionAmount: request.contributionAmount,
          targetAmount: request.targetAmount,
          startsOn: request.startsOn,
          endsOn: request.endsOn ?? null,
          appliesToAllGarages: request.appliesToAllGarages,
          participantGarageIds: request.participantGarageIds ?? [],
          overdueGraceDays: request.overdueGraceDays,
        })
        updatedRequests.push({ id, request })
        campaigns = campaigns.map((item) => (item.id === id ? campaign : item))
        return campaign
      },
      archiveFeeCampaign: async (_token, id, reason) => {
        archiveRequests.push({ id, reason })
        campaigns = campaigns.map((campaign) => (campaign.id === id ? { ...campaign, isArchived: true } : campaign))
      },
      restoreFeeCampaign: async (_token, id) => {
        restoredRequests.push(id)
        const restoredCampaign = campaigns.find((campaign) => campaign.id === id) ?? createFeeCampaign({ id, isArchived: false })
        campaigns = campaigns.map((campaign) => (campaign.id === id ? { ...restoredCampaign, isArchived: false } : campaign))
        return { ...restoredCampaign, isArchived: false }
      },
    })
    const financeClient = createFinanceClient({
      generateFeeCampaignAccruals: async (_token, request) => {
        generateRequests.push(request)
        return createFeeCampaignAccrualGenerationResult({
          accountingMonth: request.accountingMonth,
          feeCampaignId: request.feeCampaignId,
          feeCampaignName: 'Сбор на ворота',
          incomeTypeId: targetIncomeType.id,
          incomeTypeName: targetIncomeType.name,
          contributionAmount: 500,
          createdCount: 3,
          skippedCount: 1,
          totalAmount: 1500,
          createdAccruals: [
            createAccrual({ id: 'fee-accrual-1', amount: 500, source: 'fee_campaign' }),
            createAccrual({ id: 'fee-accrual-2', amount: 500, source: 'fee_campaign' }),
            createAccrual({ id: 'fee-accrual-3', amount: 500, source: 'fee_campaign' }),
          ],
          skippedGarages: ['12'],
        })
      },
    })

    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Тарифы и сборы')
    const tariffsPanel = await screen.findByRole('region', { name: 'Тарифы и сборы' })
    const feeCampaignsSection = within(tariffsPanel).getByLabelText('Объявленные сборы')
    expect(within(feeCampaignsSection).getByText('Сбор на ворота')).toBeInTheDocument()
    expect(within(feeCampaignsSection).getByText('Старый сбор')).toBeInTheDocument()

    await user.click(within(tariffsPanel).getAllByRole('button', { name: 'Объявить сбор' })[0])
    const createDialog = await screen.findByRole('dialog', { name: 'Добавить сбор' })
    await user.type(within(createDialog).getByLabelText('Наименование сбора'), 'Сбор на камеры')
    await user.selectOptions(within(createDialog).getByLabelText('Вид поступления для сбора'), targetIncomeType.id)
    await user.type(within(createDialog).getByLabelText('Цель сбора'), 'Видеонаблюдение')
    await user.type(within(createDialog).getByLabelText('Сумма взноса'), '700')
    await user.type(within(createDialog).getByLabelText('Сумма сбора'), '35000')
    await user.click(within(createDialog).getByLabelText('Все гаражи'))
    await user.click(await within(createDialog).findByLabelText('Гараж 27'))
    expect(within(createDialog).getByLabelText('Гараж 12')).not.toBeChecked()
    await user.clear(within(createDialog).getByLabelText('Перенос долга по сбору в просроченный'))
    await user.type(within(createDialog).getByLabelText('Перенос долга по сбору в просроченный'), '45')
    await user.click(within(createDialog).getByRole('button', { name: 'Объявить сбор' }))
    await waitFor(() => expect(createdRequests).toHaveLength(1))
    expect(JSON.parse(createdRequests[0])).toMatchObject({
      name: 'Сбор на камеры',
      incomeTypeId: targetIncomeType.id,
      goal: 'Видеонаблюдение',
      contributionAmount: 700,
      targetAmount: 35000,
      appliesToAllGarages: false,
      participantGarageIds: [participantGarage.id],
      overdueGraceDays: 45,
    })
    expect(within(feeCampaignsSection).getByText('Сбор на камеры')).toBeInTheDocument()
    expect(within(feeCampaignsSection).getByText('27')).toBeInTheDocument()

    const editFeeCampaignButton = within(feeCampaignsSection).getByRole('button', { name: 'Изменить сбор Сбор на камеры' })
    await user.click(editFeeCampaignButton)
    let editDialog = await screen.findByRole('dialog', { name: 'Изменить сбор' })
    expect(within(editDialog).getByLabelText('Наименование сбора')).toHaveValue('Сбор на камеры')
    await user.click(within(editDialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Изменить сбор' })).not.toBeInTheDocument())
    await waitFor(() => expect(editFeeCampaignButton).toHaveFocus())
    expect(updatedRequests).toHaveLength(0)

    await user.click(editFeeCampaignButton)
    editDialog = await screen.findByRole('dialog', { name: 'Изменить сбор' })
    expect(within(editDialog).getByLabelText('Наименование сбора')).toHaveValue('Сбор на камеры')
    expect(within(editDialog).getByLabelText('Гараж 27')).toBeChecked()
    await user.clear(within(editDialog).getByLabelText('Сумма сбора'))
    await user.type(within(editDialog).getByLabelText('Сумма сбора'), '36000')
    await user.click(within(editDialog).getByLabelText('Гараж 12'))
    await user.click(within(editDialog).getByRole('button', { name: 'Сохранить' }))
    const editConfirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения сбора' })
    expect(within(editConfirmationDialog).getByLabelText('Изменяемые поля сбора')).toHaveTextContent('Сумма сбора')
    expect(within(editConfirmationDialog).getByLabelText('Изменяемые поля сбора')).toHaveTextContent('Участники')
    expect(updatedRequests).toHaveLength(0)
    const editConfirmationCancelButton = within(editConfirmationDialog).getByRole('button', { name: 'Отмена' })
    await waitFor(() => expect(editConfirmationCancelButton).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения сбора' })).not.toBeInTheDocument())
    expect(screen.getByRole('dialog', { name: 'Изменить сбор' })).toBeInTheDocument()
    expect(updatedRequests).toHaveLength(0)
    await user.click(within(editDialog).getByRole('button', { name: 'Сохранить' }))
    const reopenedEditConfirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения сбора' })
    await user.click(within(reopenedEditConfirmationDialog).getByRole('button', { name: 'Сохранить изменения' }))
    await waitFor(() => expect(updatedRequests).toHaveLength(1))
    expect(updatedRequests[0]).toMatchObject({
      id: 'fee-campaign-new',
      request: {
        name: 'Сбор на камеры',
        targetAmount: 36000,
        appliesToAllGarages: false,
        participantGarageIds: [participantGarage.id, otherGarage.id],
      },
    })
    expect(within(feeCampaignsSection).getByText('12, 27')).toBeInTheDocument()

    await user.click(within(feeCampaignsSection).getAllByRole('button', { name: 'Начислить' })[0])
    const generateDialog = await screen.findByRole('dialog', { name: 'Начислить сбор?' })
    expect(within(generateDialog).getByLabelText('Месяц начисления сбора')).toHaveValue('2026-06')
    const generateCancelButton = within(generateDialog).getByRole('button', { name: 'Отмена' })
    const generateConfirmButton = within(generateDialog).getByRole('button', { name: 'Начислить' })
    expect(Boolean(generateCancelButton.compareDocumentPosition(generateConfirmButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(generateCancelButton).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Начислить сбор?' })).not.toBeInTheDocument()
    expect(generateRequests).toHaveLength(0)

    await user.click(within(feeCampaignsSection).getAllByRole('button', { name: 'Начислить' })[0])
    const reopenedGenerateDialog = await screen.findByRole('dialog', { name: 'Начислить сбор?' })
    expect(within(reopenedGenerateDialog).getByLabelText('Месяц начисления сбора')).toHaveValue('2026-06')
    await user.type(within(reopenedGenerateDialog).getByLabelText('Комментарий к начислению сбора'), 'Решение правления')
    await user.click(within(reopenedGenerateDialog).getByRole('button', { name: 'Начислить' }))
    await waitFor(() => expect(generateRequests).toHaveLength(1))
    expect(generateRequests[0]).toMatchObject({
      accountingMonth: '2026-06-01',
      comment: 'Решение правления',
    })
    expect(await within(feeCampaignsSection).findByText(/Создано начислений: 3/)).toBeInTheDocument()

    await user.click(within(feeCampaignsSection).getByRole('button', { name: 'Архивировать сбор Сбор на ворота' }))
    const archiveDialog = await screen.findByRole('dialog', { name: 'Архивировать сбор?' })
    const archiveCancelButton = within(archiveDialog).getByRole('button', { name: 'Отмена' })
    const archiveConfirmButton = within(archiveDialog).getByRole('button', { name: 'Архивировать' })
    expect(archiveConfirmButton).toBeDisabled()
    expect(Boolean(archiveCancelButton.compareDocumentPosition(archiveConfirmButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(archiveCancelButton).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Архивировать сбор?' })).not.toBeInTheDocument()
    expect(archiveRequests).toHaveLength(0)
    expect(within(feeCampaignsSection).getByRole('button', { name: 'Архивировать сбор Сбор на ворота' })).toBeInTheDocument()

    await user.click(within(feeCampaignsSection).getByRole('button', { name: 'Архивировать сбор Сбор на ворота' }))
    const reopenedArchiveDialog = await screen.findByRole('dialog', { name: 'Архивировать сбор?' })
    expect(within(reopenedArchiveDialog).getByRole('button', { name: 'Архивировать' })).toBeDisabled()
    await user.type(within(reopenedArchiveDialog).getByLabelText('Причина архивации сбора'), 'Сбор закрыт')
    await user.click(within(reopenedArchiveDialog).getByRole('button', { name: 'Архивировать' }))
    await waitFor(() => expect(archiveRequests).toEqual([{ id: 'fee-campaign-active', reason: 'Сбор закрыт' }]))

    const archivedCampaignRow = within(feeCampaignsSection).getByText('Старый сбор').closest('.contractors-mini-row')
    expect(archivedCampaignRow).not.toBeNull()
    const restoreCampaignButton = within(archivedCampaignRow as HTMLElement).getByRole('button', { name: 'Вернуть' })
    await user.click(restoreCampaignButton)
    const restoreDialog = await screen.findByRole('dialog', { name: 'Вернуть сбор?' })
    expect(within(restoreDialog).getByText('Старый сбор')).toBeInTheDocument()
    const restoreCancelButton = within(restoreDialog).getByRole('button', { name: 'Отмена' })
    const restoreConfirmButton = within(restoreDialog).getByRole('button', { name: 'Вернуть' })
    expect(Boolean(restoreCancelButton.compareDocumentPosition(restoreConfirmButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(restoreCancelButton).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Вернуть сбор?' })).not.toBeInTheDocument()
    expect(restoredRequests).toHaveLength(0)
    expect(restoreCampaignButton).toHaveFocus()

    await user.click(restoreCampaignButton)
    const reopenedRestoreDialog = await screen.findByRole('dialog', { name: 'Вернуть сбор?' })
    await user.click(within(reopenedRestoreDialog).getByRole('button', { name: 'Вернуть' }))
    await waitFor(() => expect(restoredRequests).toContain('fee-campaign-archived'))
  })

  it('edits tariffs and one-time payments without local history access', async () => {
    const user = userEvent.setup()
    const irregularPaymentListRequests: Array<{ includeArchived?: boolean }> = []
    const restoredIrregularPaymentIds: string[] = []
    const waterTariff = createTariff({ id: 'tariff-water', name: 'Тариф на воду', calculationBase: 'meter_water', rate: 1250 })
    const electricityTariff = createTariff({
      id: 'tariff-electricity',
      name: 'Электроэнергия',
      calculationBase: 'meter_electricity',
      rate: 4,
      electricityFirstThreshold: 1,
      electricitySecondThreshold: 3,
      electricityFirstTierName: 'От 0 кВт',
      electricitySecondTierName: 'От 1 кВт',
      electricityThirdTierName: 'От 3 кВт',
      electricityFirstRate: 2,
      electricitySecondRate: 3,
      electricityThirdRate: 5,
    })
    const membershipSetting = createChargeServiceSetting({
      id: 'membership',
      name: 'Членский взнос',
      isRegular: true,
      periodicityMonths: 12,
      accrualStartMonth: 1,
      paymentDueDay: 30,
      paymentDueMonth: 6,
      overdueGraceDays: 30,
      unitName: 'руб.',
    })
    const dictionaryClient = createDictionaryClient({
      getTariffs: async () => [waterTariff, electricityTariff],
      getChargeServiceSettings: async () => [membershipSetting],
      getIrregularPayments: async (_token, _search, _limit, includeArchived) => {
        irregularPaymentListRequests.push({ includeArchived })
        return [
        createIrregularPayment({ id: 'irregular-entry-fee', name: 'Вступительный взнос', isUsed: true }),
        createIrregularPayment({ id: 'irregular-fine-that', name: 'Штраф за то' }),
        createIrregularPayment({ id: 'irregular-fine-this', name: 'Штраф за это' }),
        createIrregularPayment({ id: 'irregular-archived', name: 'Архивный штраф', amount: 250, isArchived: true }),
        createIrregularPayment({ id: 'irregular-conflict', name: 'Архивный дубль', amount: 300, isArchived: true }),
      ]
      },
      createTariff: async (_token, request) => createTariff({
        id: 'tariff-water-created',
        name: request.name,
        calculationBase: request.calculationBase,
        rate: request.rate,
        effectiveFrom: request.effectiveFrom,
        comment: request.comment ?? null,
      }),
      updateTariff: async (_token, id, request) => createTariff({
        id,
        name: request.name,
        calculationBase: request.calculationBase,
        rate: request.rate,
        effectiveFrom: request.effectiveFrom,
        comment: request.comment ?? null,
      }),
      createIrregularPayment: async (_token, request) => createIrregularPayment({
        id: `irregular-${request.name}`,
        name: request.name,
        amount: request.amount,
        isActive: request.isActive ?? true,
        isUsed: request.name === 'Вступительный взнос',
      }),
      updateIrregularPayment: async (_token, id, request) => createIrregularPayment({
        id,
        name: request.name,
        amount: request.amount,
        isActive: request.isActive ?? true,
        isUsed: request.name === 'Вступительный взнос',
      }),
      restoreIrregularPayment: async (_token, id) => {
        restoredIrregularPaymentIds.push(id)
        if (id === 'irregular-conflict') {
          throw new DictionaryApiError('irregular_payment_duplicate', 'Активный нерегулярный платеж с таким наименованием уже существует.', 409)
        }

        return createIrregularPayment({ id, name: 'Архивный штраф', amount: 250, isArchived: false })
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: /Тарифы\s+и\s+сборы/i }))

    const tariffsPanel = await screen.findByRole('region', { name: 'Тарифы и сборы' })
    const waterRateInput = await within(tariffsPanel).findByLabelText('Вода: Тариф на воду: значение')
    expect(waterRateInput).toHaveValue('1250')

    await user.clear(waterRateInput)
    await user.type(waterRateInput, '1300{Enter}')
    const waterRateConfirmDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение?' })
    expect(within(waterRateConfirmDialog).getByText('Вода: Тариф на воду')).toBeInTheDocument()
    expect(within(waterRateConfirmDialog).getByText('Было')).toBeInTheDocument()
    expect(within(waterRateConfirmDialog).getByText('1250')).toBeInTheDocument()
    expect(within(waterRateConfirmDialog).getByText('Стало')).toBeInTheDocument()
    expect(within(waterRateConfirmDialog).getByText('1300')).toBeInTheDocument()
    const waterRateCancelButton = within(waterRateConfirmDialog).getByRole('button', { name: 'Отмена' })
    const waterRateConfirmButton = within(waterRateConfirmDialog).getByRole('button', { name: 'Сохранить' })
    const waterRateCloseButton = within(waterRateConfirmDialog).getByRole('button', { name: 'Закрыть подтверждение изменения тарифа' })
    expect(Boolean(waterRateCancelButton.compareDocumentPosition(waterRateConfirmButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(waterRateCancelButton).toHaveFocus())
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(waterRateCloseButton).toHaveFocus()
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(waterRateConfirmButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(waterRateCloseButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(waterRateCancelButton).toHaveFocus()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердить изменение?' })).not.toBeInTheDocument())
    await waitFor(() => expect(waterRateInput).toHaveFocus())
    expect(waterRateInput).toHaveValue('1250')

    await user.clear(waterRateInput)
    await user.type(waterRateInput, '1300{Enter}')
    const reopenedWaterRateConfirmDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение?' })
    await user.click(within(reopenedWaterRateConfirmDialog).getByRole('button', { name: 'Сохранить' }))
    expect(waterRateInput).toHaveValue('1300')

    const membershipDueDayInput = within(tariffsPanel).getByLabelText('Членский взнос: Оплата до: день')
    const membershipDueMonthSelect = within(tariffsPanel).getByLabelText('Членский взнос: Оплата до: месяц')
    expect(membershipDueDayInput).toHaveValue('30')
    expect(membershipDueMonthSelect).toHaveValue('июн')
    await user.selectOptions(membershipDueMonthSelect, 'фев')
    await user.keyboard('{Enter}')
    expect(await within(tariffsPanel).findByRole('alert')).toHaveTextContent('В месяце "Февраль" можно указать день от 1 до 28.')
    expect(screen.queryByRole('dialog', { name: 'Подтвердить изменение?' })).not.toBeInTheDocument()
    await user.clear(membershipDueDayInput)
    await user.type(membershipDueDayInput, '28{Enter}')
    const dateConfirmDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение?' })
    expect(within(dateConfirmDialog).getByText('30 июн')).toBeInTheDocument()
    expect(within(dateConfirmDialog).getByText('28 фев')).toBeInTheDocument()
    await user.click(within(dateConfirmDialog).getByRole('button', { name: 'Сохранить' }))
    expect(membershipDueDayInput).toHaveValue('28')
    expect(membershipDueMonthSelect).toHaveValue('фев')

    const electricityThresholdNameInput = within(tariffsPanel).getByLabelText('Электроэнергия: От 1 кВт: наименование')
    await user.clear(electricityThresholdNameInput)
    await user.type(electricityThresholdNameInput, 'От 2 кВт{Enter}')
    const thresholdNameConfirmDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение?' })
    expect(within(thresholdNameConfirmDialog).getByText('Наименование порога')).toBeInTheDocument()
    expect(within(thresholdNameConfirmDialog).getByText('От 1 кВт')).toBeInTheDocument()
    expect(within(thresholdNameConfirmDialog).getByText('От 2 кВт')).toBeInTheDocument()
    await user.click(within(thresholdNameConfirmDialog).getByRole('button', { name: 'Сохранить' }))
    expect(electricityThresholdNameInput).toHaveValue('От 2 кВт')

    await user.click(within(tariffsPanel).getByRole('button', { name: 'Добавить порог' }))
    const customThresholdNameInput = within(tariffsPanel).getByLabelText('Электроэнергия: Порог 4: наименование')
    expect(customThresholdNameInput).toHaveValue('Порог 4')
    const electricityThresholdInput = within(tariffsPanel).getByLabelText('Электроэнергия: Порог 4: значение')
    expect(electricityThresholdInput).toBeInTheDocument()
    await user.type(electricityThresholdInput, '7.5{Enter}')
    expect(screen.queryByRole('dialog', { name: 'Подтвердить изменение?' })).not.toBeInTheDocument()
    expect(electricityThresholdInput).toHaveValue('7.5')
    const deleteThresholdButton = within(tariffsPanel).getByRole('button', { name: 'Удалить порог Порог 4' })
    await user.click(deleteThresholdButton)
    const thresholdDeleteDialog = await screen.findByRole('dialog', { name: 'Удалить порог тарификации?' })
    const thresholdDeleteCancelButton = within(thresholdDeleteDialog).getByRole('button', { name: 'Отмена' })
    const thresholdDeleteConfirmButton = within(thresholdDeleteDialog).getByRole('button', { name: 'Удалить' })
    expect(thresholdDeleteConfirmButton).toBeDisabled()
    await waitFor(() => expect(thresholdDeleteCancelButton).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Удалить порог тарификации?' })).not.toBeInTheDocument())
    expect(deleteThresholdButton).toHaveFocus()
    expect(within(tariffsPanel).getByLabelText('Электроэнергия: Порог 4: значение')).toHaveValue('7.5')

    await user.click(deleteThresholdButton)
    const reopenedThresholdDeleteDialog = await screen.findByRole('dialog', { name: 'Удалить порог тарификации?' })
    await user.type(within(reopenedThresholdDeleteDialog).getByLabelText('Причина удаления порога'), 'Лишний порог добавлен ошибочно')
    await user.click(within(reopenedThresholdDeleteDialog).getByRole('button', { name: 'Удалить' }))
    await waitFor(() => expect(within(tariffsPanel).queryByLabelText('Электроэнергия: Порог 4: значение')).not.toBeInTheDocument())

    const entryFeeInput = within(tariffsPanel).getByLabelText('Сумма: Вступительный взнос')
    await user.clear(entryFeeInput)
    await user.type(entryFeeInput, '5500{Enter}')
    const entryFeeConfirmDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение?' })
    expect(within(entryFeeConfirmDialog).getByText('Вступительный взнос')).toBeInTheDocument()
    expect(within(entryFeeConfirmDialog).getByText('Сумма, руб.')).toBeInTheDocument()
    await user.click(within(entryFeeConfirmDialog).getByRole('button', { name: 'Сохранить' }))
    expect(entryFeeInput).toHaveValue('5500')

    const oneTimeTable = within(tariffsPanel).getByRole('region', { name: 'Нерегулярные платежи' })
    expect(irregularPaymentListRequests[0]?.includeArchived).toBe(true)
    expect(within(oneTimeTable).queryByText('Статус')).not.toBeInTheDocument()
    expect(within(oneTimeTable).queryByText('Действие')).not.toBeInTheDocument()
    expect(within(tariffsPanel).queryByRole('group', { name: 'Действия по тарифам и сборам' })).not.toBeInTheDocument()
    expect(within(tariffsPanel).getAllByRole('button', { name: 'Добавить услугу' })).toHaveLength(1)
    expect(within(tariffsPanel).getAllByRole('button', { name: 'Объявить сбор' })).toHaveLength(1)

    const archivedPaymentRow = within(oneTimeTable).getByLabelText('Нерегулярный платеж Архивный штраф')
    const restoreArchivedButton = within(archivedPaymentRow).getByRole('button', { name: 'Вернуть' })
    await user.click(restoreArchivedButton)
    const restoreArchivedDialog = await screen.findByRole('dialog', { name: 'Вернуть нерегулярный платеж?' })
    expect(within(restoreArchivedDialog).getByText('Архивный штраф')).toBeInTheDocument()
    const restoreArchivedCancelButton = within(restoreArchivedDialog).getByRole('button', { name: 'Отмена' })
    const restoreArchivedConfirmButton = within(restoreArchivedDialog).getByRole('button', { name: 'Вернуть' })
    expect(Boolean(restoreArchivedCancelButton.compareDocumentPosition(restoreArchivedConfirmButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(restoreArchivedCancelButton).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Вернуть нерегулярный платеж?' })).not.toBeInTheDocument())
    expect(restoredIrregularPaymentIds).toHaveLength(0)
    expect(restoreArchivedButton).toHaveFocus()
    await user.click(restoreArchivedButton)
    const reopenedRestoreArchivedDialog = await screen.findByRole('dialog', { name: 'Вернуть нерегулярный платеж?' })
    await user.click(within(reopenedRestoreArchivedDialog).getByRole('button', { name: 'Вернуть' }))
    await waitFor(() => expect(restoredIrregularPaymentIds).toEqual(['irregular-archived']))
    expect(within(archivedPaymentRow).getByLabelText('Сумма: Архивный штраф')).toBeEnabled()

    const conflictPaymentRow = within(oneTimeTable).getByLabelText('Нерегулярный платеж Архивный дубль')
    await user.click(within(conflictPaymentRow).getByRole('button', { name: 'Вернуть' }))
    const conflictRestoreDialog = await screen.findByRole('dialog', { name: 'Вернуть нерегулярный платеж?' })
    await user.click(within(conflictRestoreDialog).getByRole('button', { name: 'Вернуть' }))
    expect(await within(oneTimeTable).findByRole('alert')).toHaveTextContent('Восстановление недоступно')
    await user.click(within(conflictRestoreDialog).getByRole('button', { name: 'Отмена' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Вернуть нерегулярный платеж?' })).not.toBeInTheDocument())

    const usedPaymentRow = within(oneTimeTable).getByLabelText('Нерегулярный платеж Вступительный взнос')
    fireEvent.contextMenu(usedPaymentRow)
    const usedPaymentMenu = await screen.findByRole('menu', { name: 'Действия нерегулярного платежа Вступительный взнос' })
    await user.click(within(usedPaymentMenu).getByRole('menuitem', { name: 'Удалить' }))
    expect(await within(oneTimeTable).findByRole('alert')).toHaveTextContent('Удаление недоступно')
    expect(screen.queryByRole('dialog', { name: 'Удалить нерегулярный платеж?' })).not.toBeInTheDocument()

    const fineThatRow = within(oneTimeTable).getByLabelText('Нерегулярный платеж Штраф за то')
    const fineThatInput = within(fineThatRow).getByLabelText('Сумма: Штраф за то')
    expect(fineThatInput).toBeEnabled()
    fireEvent.contextMenu(fineThatRow)
    const deactivateMenu = await screen.findByRole('menu', { name: 'Действия нерегулярного платежа Штраф за то' })
    await user.click(within(deactivateMenu).getByRole('menuitem', { name: 'Деактивировать' }))
    const deactivateDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение?' })
    expect(within(deactivateDialog).getByText('Статус')).toBeInTheDocument()
    expect(within(deactivateDialog).getByText('Деактивирован')).toBeInTheDocument()
    await user.click(within(deactivateDialog).getByRole('button', { name: 'Сохранить' }))
    expect(fineThatInput).toBeDisabled()
    fireEvent.contextMenu(fineThatRow)
    const activateMenu = await screen.findByRole('menu', { name: 'Действия нерегулярного платежа Штраф за то' })
    await user.click(within(activateMenu).getByRole('menuitem', { name: 'Активировать' }))
    const activateDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение?' })
    expect(within(activateDialog).getByText('Активен')).toBeInTheDocument()
    await user.click(within(activateDialog).getByRole('button', { name: 'Сохранить' }))
    expect(fineThatInput).toBeEnabled()

    const fineThisRow = within(oneTimeTable).getByLabelText('Нерегулярный платеж Штраф за это')
    fireEvent.contextMenu(fineThisRow)
    const deleteFineMenu = await screen.findByRole('menu', { name: 'Действия нерегулярного платежа Штраф за это' })
    await user.click(within(deleteFineMenu).getByRole('menuitem', { name: 'Удалить' }))
    const deleteFineDialog = await screen.findByRole('dialog', { name: 'Удалить нерегулярный платеж?' })
    expect(within(deleteFineDialog).getByText('Штраф за это')).toBeInTheDocument()
    const deleteFineCancelButton = within(deleteFineDialog).getByRole('button', { name: 'Отмена' })
    const deleteFineConfirmButton = within(deleteFineDialog).getByRole('button', { name: 'Удалить' })
    const deleteFineCloseButton = within(deleteFineDialog).getByRole('button', { name: 'Закрыть подтверждение удаления нерегулярного платежа' })
    expect(deleteFineConfirmButton).toBeDisabled()
    expect(Boolean(deleteFineCancelButton.compareDocumentPosition(deleteFineConfirmButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(deleteFineCancelButton).toHaveFocus())
    await user.keyboard('{Tab}')
    expect(deleteFineCloseButton).toHaveFocus()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Удалить нерегулярный платеж?' })).not.toBeInTheDocument())

    fireEvent.contextMenu(fineThisRow)
    const reopenedDeleteFineMenu = await screen.findByRole('menu', { name: 'Действия нерегулярного платежа Штраф за это' })
    await user.click(within(reopenedDeleteFineMenu).getByRole('menuitem', { name: 'Удалить' }))
    const reopenedDeleteFineDialog = await screen.findByRole('dialog', { name: 'Удалить нерегулярный платеж?' })
    expect(within(reopenedDeleteFineDialog).getByRole('button', { name: 'Удалить' })).toBeDisabled()
    await user.type(within(reopenedDeleteFineDialog).getByLabelText('Причина удаления нерегулярного платежа'), 'Больше не используется')
    await user.click(within(reopenedDeleteFineDialog).getByRole('button', { name: 'Удалить' }))
    await waitFor(() => expect(within(fineThisRow).getByRole('button', { name: 'Вернуть' })).toBeEnabled())

    expect(within(tariffsPanel).queryByRole('tab', { name: 'История изменений' })).not.toBeInTheDocument()
    expect(within(tariffsPanel).queryByRole('table', { name: 'История изменений тарифов и сборов', hidden: true })).not.toBeInTheDocument()
  }, 180000)

  it('shows contractors tabs and section dialogs without local history access', async () => {
    const user = userEvent.setup()
    const contractorOwner = createOwner({
      id: 'owner-1',
      lastName: 'Иванов',
      firstName: 'Иван',
      phone: '+7 900 000-00-01',
      address: 'ГСК, ряд 1',
    })
    const contractorGarage = createGarage({
      id: '11111111-1111-4111-8111-111111111111',
      number: '1',
      ownerId: contractorOwner.id,
      ownerName: contractorOwner.fullName,
      peopleCount: 3,
      floorCount: 1,
      startingBalance: 100,
      balance: 5300,
      overdueDebt: 1300,
      initialWaterMeterValue: 59,
      initialElectricityMeterValue: 49,
    })
    let savedGarageStartingBalance: number | null = null
    let archivedGarageReason: string | null = null
    let archivedSupplierReason: string | null = null
    let deletedSupplierContactReason: string | null = null
    let archivedStaffMemberReason: string | null = null
    const archivedStaffDepartmentRequests: Array<{ id: string; reason: string }> = []
    const restoredSupplierIds: string[] = []
    const restoredStaffMemberIds: string[] = []
    const restoredStaffDepartmentIds: string[] = []
    let staffDepartments = [
      createStaffDepartment({ id: '55555555-5555-4555-8555-555555555555', name: 'Архивный отдел', isArchived: true }),
      createStaffDepartment({ id: '66666666-6666-4666-8666-666666666666', name: 'Бухгалтерия' }),
    ]
    const dictionaryClient = createDictionaryClient({
      getOwners: async () => [contractorOwner],
      getGarages: async () => [contractorGarage],
      updateGarage: async (_token, id, request) => {
        savedGarageStartingBalance = request.startingBalance
        return createGarage({
          ...contractorGarage,
          id,
          number: request.number,
          peopleCount: request.peopleCount,
          floorCount: request.floorCount,
          ownerId: request.ownerId,
          ownerName: 'Новый владелец',
          startingBalance: request.startingBalance,
          balance: 5300,
          overdueDebt: 1300,
          initialWaterMeterValue: request.initialWaterMeterValue ?? null,
          initialElectricityMeterValue: request.initialElectricityMeterValue ?? null,
          comment: request.comment ?? null,
        })
      },
      archiveGarage: async (_token, _id, reason) => {
        archivedGarageReason = reason
      },
      restoreGarage: async (_token, id) => createGarage({
        ...contractorGarage,
        id,
        ownerName: 'Новый владелец',
        isArchived: false,
      }),
      createSupplier: async (_token, request) => createSupplier({
        id: '22222222-2222-4222-8222-222222222222',
        name: request.name,
        groupId: request.groupId,
        groupName: 'Коммунальные услуги',
        inn: request.inn ?? null,
        legalAddress: request.legalAddress ?? null,
        contactPerson: request.contactPerson ?? null,
        phone: request.phone ?? null,
        email: request.email ?? null,
        startingBalance: request.startingBalance,
        comment: request.comment ?? null,
      }),
      createSupplierContact: async (_token, request) => createSupplierContact({
        id: '33333333-3333-4333-8333-333333333333',
        supplierId: request.supplierId,
        supplierName: 'Новый подрядчик',
        fullName: request.fullName,
        position: request.position ?? null,
        phone: request.phone ?? null,
        email: request.email ?? null,
        status: request.status,
        comment: request.comment ?? null,
      }),
      restoreSupplier: async (_token, id) => {
        restoredSupplierIds.push(id)
        return createSupplier({
          id,
          name: 'Новый подрядчик',
          groupId: 'group-1',
          groupName: 'Коммунальные услуги',
          isArchived: false,
        })
      },
      archiveSupplier: async (_token, _id, reason) => {
        archivedSupplierReason = reason
      },
      archiveSupplierContact: async (_token, _id, reason) => {
        deletedSupplierContactReason = reason
      },
      getStaffDepartments: async () => staffDepartments,
      archiveStaffDepartment: async (_token, id, reason) => {
        archivedStaffDepartmentRequests.push({ id, reason })
        staffDepartments = staffDepartments.map((item) => (item.id === id ? { ...item, isArchived: true } : item))
      },
      createStaffDepartment: async (_token, request) => {
        const department = createStaffDepartment({
          id: 'staff-department-2',
          name: request.name,
          isArchived: false,
        })
        staffDepartments = [department, ...staffDepartments]
        return department
      },
      restoreStaffDepartment: async (_token, id) => {
        restoredStaffDepartmentIds.push(id)
        const department = staffDepartments.find((item) => item.id === id) ?? createStaffDepartment({ id, name: 'Архивный отдел' })
        const restoredDepartment = { ...department, isArchived: false }
        staffDepartments = staffDepartments.map((item) => (item.id === id ? restoredDepartment : item))
        return restoredDepartment
      },
      createStaffMember: async (_token, request) => createStaffMember({
        id: '44444444-4444-4444-8444-444444444444',
        fullName: request.fullName,
        departmentId: request.departmentId,
        departmentName: 'Охрана',
        rate: request.rate,
      }),
      archiveStaffMember: async (_token, _id, reason) => {
        archivedStaffMemberReason = reason
      },
      restoreStaffMember: async (_token, id) => {
        restoredStaffMemberIds.push(id)
        return createStaffMember({
          id,
          fullName: 'Смирнов Алексей',
          departmentId: 'staff-department-2',
          departmentName: 'Охрана',
          rate: 30000,
          isArchived: false,
        })
      },
    })
    let requestedGarageFinancialReportId: string | null = null
    let requestedGarageFinancialReportPeriod: { monthFrom?: string; monthTo?: string } | null = null
    const garageFinancialReport = createGarageBalanceHistory({
      garageId: contractorGarage.id,
      garageNumber: contractorGarage.number,
      ownerName: contractorOwner.fullName,
      monthFrom: '2026-07-01',
      monthTo: '2026-07-01',
      startingBalance: 100,
      accrualTotal: 500,
      incomeTotal: 200,
      debt: 400,
      rows: [
        { accountingMonth: '2026-07-01', openingDebt: 100, accrualAmount: 500, incomeAmount: 200, closingDebt: 400 },
      ],
    })
    const financeClient = createFinanceClient({
      getGarageBalanceHistory: async (_token, garageId, params) => {
        requestedGarageFinancialReportId = garageId
        requestedGarageFinancialReportPeriod = params ?? null
        return garageFinancialReport
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: 'Контрагенты' }))

    const contractorsPanel = await screen.findByRole('region', { name: 'Контрагенты' })
    expect(within(contractorsPanel).getByRole('tab', { name: 'Гаражи' })).toHaveAttribute('aria-selected', 'true')
    expect(within(contractorsPanel).getByRole('tab', { name: 'Поставщики' })).toBeInTheDocument()
    expect(within(contractorsPanel).getByRole('tab', { name: 'Персонал' })).toBeInTheDocument()
    expect(screen.queryByText('Поиск по гаражу, владельцу или поставщику')).not.toBeInTheDocument()
    expect(within(contractorsPanel).getByRole('table', { name: 'Гаражи' })).toBeInTheDocument()
    expect(within(contractorsPanel).getByRole('columnheader', { name: 'Просроченная задолженность' })).toBeInTheDocument()
    expect(within(contractorsPanel).getByText('Иванов Иван')).toBeInTheDocument()
    expect(within(contractorsPanel).getByText('1 300 руб.')).toBeInTheDocument()
    expect(within(contractorsPanel).getByRole('button', { name: 'Показать должников' })).toBeInTheDocument()

    await user.click(within(contractorsPanel).getByRole('button', { name: 'Изменить гараж 1' }))
    const garageDialog = await screen.findByRole('dialog', { name: 'Гараж 1' })
    expect(within(garageDialog).getByLabelText('Баланс гаража')).toHaveAttribute('readonly')
    expect(within(garageDialog).getByLabelText('Баланс гаража')).toHaveValue('5\u00A0300')
    expect(within(garageDialog).getByLabelText('Просроченная задолженность гаража')).toHaveAttribute('readonly')
    expect(within(garageDialog).getByLabelText('Просроченная задолженность гаража')).toHaveValue('1\u00A0300 руб.')
    expect(within(garageDialog).getByLabelText('Стартовое значение счетчика воды')).toBeInTheDocument()
    expect(within(garageDialog).getByLabelText('Стартовое значение счетчика электричества')).toBeInTheDocument()
    expect(within(garageDialog).queryByLabelText('Счетчики гаража')).not.toBeInTheDocument()
    expect(within(garageDialog).getByRole('button', { name: 'Открыть фин. отчет' })).toHaveClass('contractors-report-button')
    const deleteGarageFromCardButton = within(garageDialog).getByRole('button', { name: 'Удалить гараж' })
    await user.click(deleteGarageFromCardButton)
    const deleteGarageFromCardDialog = await screen.findByRole('dialog', { name: 'Удалить гараж?' })
    expect(within(deleteGarageFromCardDialog).getByText('Гараж 1')).toBeInTheDocument()
    const deleteGarageFromCardCancelButton = within(deleteGarageFromCardDialog).getByRole('button', { name: 'Отмена' })
    const deleteGarageFromCardConfirmButton = within(deleteGarageFromCardDialog).getByRole('button', { name: 'Удалить' })
    expect(deleteGarageFromCardConfirmButton).toBeDisabled()
    expect(Boolean(deleteGarageFromCardCancelButton.compareDocumentPosition(deleteGarageFromCardConfirmButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(deleteGarageFromCardCancelButton).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Удалить гараж?' })).not.toBeInTheDocument()
    await waitFor(() => expect(deleteGarageFromCardButton).toHaveFocus())
    await user.clear(within(garageDialog).getByLabelText('Владелец гаража'))
    await user.type(within(garageDialog).getByLabelText('Владелец гаража'), 'Новый владелец')
    const garageSaveButton = within(garageDialog).getByRole('button', { name: /Сохранить/i })
    await user.click(garageSaveButton)
    const garageChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменения гаража' })
    expect(within(garageChangeDialog).getByText('Гараж 1')).toBeInTheDocument()
    expect(within(garageChangeDialog).getByText('Владелец')).toBeInTheDocument()
    expect(within(garageChangeDialog).getByText('Иванов Иван -> Новый владелец')).toBeInTheDocument()
    const garageChangeCancelButton = within(garageChangeDialog).getByRole('button', { name: 'Отмена' })
    const garageChangeSaveButton = within(garageChangeDialog).getByRole('button', { name: 'Сохранить' })
    expect(Boolean(garageChangeCancelButton.compareDocumentPosition(garageChangeSaveButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(garageChangeCancelButton).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Подтвердить изменения гаража' })).not.toBeInTheDocument()
    expect(garageSaveButton).toHaveFocus()
    await user.click(garageSaveButton)
    const reopenedGarageChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменения гаража' })
    await user.click(within(reopenedGarageChangeDialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(within(within(contractorsPanel).getByRole('table', { name: 'Гаражи' })).getByText('Новый владелец')).toBeInTheDocument())
    expect(savedGarageStartingBalance).toBe(100)

    const garagesTable = within(contractorsPanel).getByRole('table', { name: 'Гаражи' })
    const garageRow = within(garagesTable).getByText('Новый владелец').closest('[role="row"]')!
    await user.pointer({ keys: '[MouseRight]', target: garageRow as HTMLElement })
    const garageContextMenu = await screen.findByRole('menu', { name: 'Действия гаража 1' })
    expect(within(garageContextMenu).getByRole('menuitem', { name: 'Изменить' })).toBeInTheDocument()
    expect(within(garageContextMenu).getByRole('menuitem', { name: 'Открыть финансовый отчет' })).toBeInTheDocument()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('menu', { name: 'Действия гаража 1' })).not.toBeInTheDocument()
    const openGarageFinancialReportButton = within(garageRow as HTMLElement).getByRole('button', { name: 'Открыть финансовый отчет гаража 1' })
    await user.click(openGarageFinancialReportButton)
    const garageFinancialReportDialog = await screen.findByRole('dialog', { name: 'Гараж 1' })
    const garageFinancialReportCloseButton = within(garageFinancialReportDialog).getByRole('button', { name: 'Закрыть финансовый отчет гаража' })
    await waitFor(() => expect(garageFinancialReportCloseButton).toHaveFocus())
    expect(within(garageFinancialReportDialog).getByText('Финансовый отчет')).toBeInTheDocument()
    expect(within(garageFinancialReportDialog).getByText('Новый владелец')).toBeInTheDocument()
    expect(within(garageFinancialReportDialog).getByRole('table', { name: 'Финансовый отчет гаража' })).toBeInTheDocument()
    expect(within(garageFinancialReportDialog).getByText('07.2026')).toBeInTheDocument()
    expect(within(garageFinancialReportDialog).getAllByText('500,00').length).toBeGreaterThan(0)
    expect(requestedGarageFinancialReportId).toBe(contractorGarage.id)
    expect(requestedGarageFinancialReportPeriod?.monthFrom).toMatch(/^\d{4}-\d{2}$/)
    expect(requestedGarageFinancialReportPeriod?.monthTo).toMatch(/^\d{4}-\d{2}$/)
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Гараж 1' })).not.toBeInTheDocument()
    await waitFor(() => expect(openGarageFinancialReportButton).toHaveFocus())

    await user.pointer({ keys: '[MouseRight]', target: garageRow as HTMLElement })
    const deleteGarageContextMenu = await screen.findByRole('menu', { name: 'Действия гаража 1' })
    await user.click(within(deleteGarageContextMenu).getByRole('menuitem', { name: 'Удалить' }))
    const deleteGarageDialog = await screen.findByRole('dialog', { name: 'Удалить гараж?' })
    expect(within(deleteGarageDialog).getByText('Гараж 1')).toBeInTheDocument()
    const deleteGarageCancelButton = within(deleteGarageDialog).getByRole('button', { name: 'Отмена' })
    const deleteGarageConfirmButton = within(deleteGarageDialog).getByRole('button', { name: 'Удалить' })
    expect(deleteGarageConfirmButton).toBeDisabled()
    expect(Boolean(deleteGarageCancelButton.compareDocumentPosition(deleteGarageConfirmButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(deleteGarageCancelButton).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Удалить гараж?' })).not.toBeInTheDocument()
    expect(within(garageRow as HTMLElement).queryByText('Удален')).not.toBeInTheDocument()
    expect(archivedGarageReason).toBeNull()

    await user.pointer({ keys: '[MouseRight]', target: garageRow as HTMLElement })
    const reopenedDeleteGarageContextMenu = await screen.findByRole('menu', { name: 'Действия гаража 1' })
    await user.click(within(reopenedDeleteGarageContextMenu).getByRole('menuitem', { name: 'Удалить' }))
    const reopenedDeleteGarageDialog = await screen.findByRole('dialog', { name: 'Удалить гараж?' })
    expect(within(reopenedDeleteGarageDialog).getByRole('button', { name: 'Удалить' })).toBeDisabled()
    await user.type(within(reopenedDeleteGarageDialog).getByLabelText('Причина удаления гаража'), 'Дубликат карточки')
    await user.click(within(reopenedDeleteGarageDialog).getByRole('button', { name: 'Удалить' }))
    await waitFor(() => expect(archivedGarageReason).toBe('Дубликат карточки'))
    await waitFor(() => expect(within(garageRow as HTMLElement).getByText('Удален')).toBeInTheDocument())
    const restoreGarageButton = within(garageRow as HTMLElement).getByRole('button', { name: 'Восстановить гараж 1' })
    await user.click(restoreGarageButton)
    const restoreGarageDialog = await screen.findByRole('dialog', { name: 'Вернуть запись?' })
    expect(within(restoreGarageDialog).getByText('Гараж 1')).toBeInTheDocument()
    const restoreGarageCancelButton = within(restoreGarageDialog).getByRole('button', { name: 'Отмена' })
    const restoreGarageConfirmButton = within(restoreGarageDialog).getByRole('button', { name: 'Вернуть запись' })
    const restoreGarageCloseButton = within(restoreGarageDialog).getByRole('button', { name: 'Закрыть подтверждение восстановления контрагента' })
    await waitFor(() => expect(restoreGarageCancelButton).toHaveFocus())
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(restoreGarageCloseButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(restoreGarageCancelButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(restoreGarageConfirmButton).toHaveFocus()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Вернуть запись?' })).not.toBeInTheDocument()
    expect(within(garageRow as HTMLElement).getByText('Удален')).toBeInTheDocument()
    expect(restoreGarageButton).toHaveFocus()

    await user.click(restoreGarageButton)
    const reopenedRestoreGarageDialog = await screen.findByRole('dialog', { name: 'Вернуть запись?' })
    await user.click(within(reopenedRestoreGarageDialog).getByRole('button', { name: 'Вернуть запись' }))
    await waitFor(() => expect(within(garageRow as HTMLElement).queryByText('Удален')).not.toBeInTheDocument())
    expect(within(garageRow as HTMLElement).getByRole('button', { name: 'Изменить гараж 1' })).toBeInTheDocument()

    const numberResizeHandle = within(garagesTable).getByRole('button', { name: 'Изменить ширину столбца Номер' })
    fireEvent.mouseDown(numberResizeHandle, { clientX: 100 })
    fireEvent.mouseMove(document, { clientX: 140 })
    fireEvent.mouseUp(document)
    await waitFor(() => expect(JSON.parse(window.localStorage.getItem('garagebalance.contractors.garageColumnWidths') ?? '{}').number).toBe(136))

    await user.click(within(contractorsPanel).getByRole('tab', { name: 'Поставщики' }))
    expect(within(contractorsPanel).getByRole('table', { name: 'Поставщики' })).toBeInTheDocument()
    expect(within(contractorsPanel).getByText('Водоканал')).toBeInTheDocument()
    expect(within(contractorsPanel).getByRole('columnheader', { name: 'Задолженность' })).toBeInTheDocument()

    const addContractorServiceButton = within(contractorsPanel).getByRole('button', { name: 'Добавить услугу' })
    await user.click(addContractorServiceButton)
    let serviceDialog = await screen.findByRole('dialog', { name: 'Добавить услугу' })
    await user.type(within(serviceDialog).getByLabelText('Наименование услуги контрагента'), 'Черновая услуга')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Добавить услугу' })).not.toBeInTheDocument())
    await waitFor(() => expect(addContractorServiceButton).toHaveFocus())

    await user.click(addContractorServiceButton)
    serviceDialog = await screen.findByRole('dialog', { name: 'Добавить услугу' })
    await user.type(within(serviceDialog).getByLabelText('Наименование услуги контрагента'), 'Уборка территории')
    await user.click(within(serviceDialog).getByRole('button', { name: /Сохранить/i }))
    await waitFor(() => expect(addContractorServiceButton).toHaveFocus())

    const addSupplierButton = within(contractorsPanel).getByRole('button', { name: 'Добавить поставщика' })
    await user.click(addSupplierButton)
    let supplierDialog = await screen.findByRole('dialog', { name: 'Новый поставщик' })
    await user.type(within(supplierDialog).getByLabelText('Наименование поставщика'), 'Черновой подрядчик')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Новый поставщик' })).not.toBeInTheDocument())
    await waitFor(() => expect(addSupplierButton).toHaveFocus())
    expect(within(contractorsPanel).queryByText('Черновой подрядчик')).not.toBeInTheDocument()

    await user.click(addSupplierButton)
    supplierDialog = await screen.findByRole('dialog', { name: 'Новый поставщик' })
    await user.type(within(supplierDialog).getByLabelText('Наименование поставщика'), 'Новый подрядчик')
    await user.selectOptions(within(supplierDialog).getByLabelText('Услуга поставщика'), 'Уборка территории')
    await user.click(within(supplierDialog).getByRole('button', { name: 'Добавить контакт' }))
    await user.type(within(supplierDialog).getByLabelText('Контакт 1: ФИО'), 'Смирнов С.С.')
    await user.type(within(supplierDialog).getByLabelText('Контакт 1: должность'), 'Менеджер')
    await user.type(within(supplierDialog).getByLabelText('Контакт 1: телефон'), '+7 900 111-22-33')
    await user.type(within(supplierDialog).getByLabelText('Контакт 1: почта'), 'guard@example.test')
    await user.type(within(supplierDialog).getByLabelText('Контакт 1: комментарий'), 'Основной контакт')
    await user.click(within(supplierDialog).getByRole('button', { name: /Сохранить/i }))
    await waitFor(() => expect(within(within(contractorsPanel).getByRole('table', { name: 'Поставщики' })).getByText('Новый подрядчик')).toBeInTheDocument())
    expect(addSupplierButton).toHaveFocus()

    const suppliersTable = within(contractorsPanel).getByRole('table', { name: 'Поставщики' })
    const supplierResizeHandle = within(suppliersTable).getByRole('button', { name: 'Изменить ширину столбца Поставщик' })
    fireEvent.mouseDown(supplierResizeHandle, { clientX: 100 })
    fireEvent.mouseMove(document, { clientX: 135 })
    fireEvent.mouseUp(document)
    await waitFor(() => expect(JSON.parse(window.localStorage.getItem('garagebalance.contractors.supplierColumnWidths') ?? '{}').name).toBe(215))

    let supplierRow = within(suppliersTable).getByText('Новый подрядчик').closest('[role="row"]')!
    expect(within(supplierRow as HTMLElement).getByText('Смирнов С.С.')).toBeInTheDocument()
    expect(within(supplierRow as HTMLElement).getByRole('button', { name: 'Изменить поставщика Новый подрядчик' })).toBeInTheDocument()
    await user.click(within(supplierRow as HTMLElement).getByRole('button', { name: 'Изменить поставщика Новый подрядчик' }))
    const editSupplierDialog = await screen.findByRole('dialog', { name: 'Новый подрядчик' })
    expect(within(editSupplierDialog).getByLabelText('Услуга поставщика').tagName).toBe('SELECT')
    expect(within(editSupplierDialog).getByRole('button', { name: 'Открыть фин. отчет' })).toHaveClass('contractors-report-button')
    expect(within(editSupplierDialog).queryByRole('button', { name: 'Удалить поставщика' })).not.toBeInTheDocument()
    const supplierContactRow = within(editSupplierDialog).getByLabelText('Контакт 1: ФИО').closest('[role="row"]')!
    await user.pointer({ keys: '[MouseRight]', target: supplierContactRow as HTMLElement })
    const contactContextMenu = await screen.findByRole('menu', { name: 'Действия контакта Смирнов С.С.' })
    await user.click(within(contactContextMenu).getByRole('menuitem', { name: 'Удалить контакт' }))
    const deleteContactDialog = await screen.findByRole('dialog', { name: 'Удалить контакт?' })
    expect(within(deleteContactDialog).getByText('Смирнов С.С.')).toBeInTheDocument()
    expect(within(deleteContactDialog).getByRole('button', { name: 'Удалить' })).toBeDisabled()
    await waitFor(() => expect(within(deleteContactDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Удалить контакт?' })).not.toBeInTheDocument()
    expect(within(editSupplierDialog).getByLabelText('Контакт 1: телефон')).toBeEnabled()
    expect(deletedSupplierContactReason).toBeNull()

    await user.pointer({ keys: '[MouseRight]', target: supplierContactRow as HTMLElement })
    const reopenedInitialContactContextMenu = await screen.findByRole('menu', { name: 'Действия контакта Смирнов С.С.' })
    await user.click(within(reopenedInitialContactContextMenu).getByRole('menuitem', { name: 'Удалить контакт' }))
    const reopenedInitialDeleteContactDialog = await screen.findByRole('dialog', { name: 'Удалить контакт?' })
    expect(within(reopenedInitialDeleteContactDialog).getByRole('button', { name: 'Удалить' })).toBeDisabled()
    await user.type(within(reopenedInitialDeleteContactDialog).getByLabelText('Причина удаления контакта'), 'Контакт больше не работает')
    await user.click(within(reopenedInitialDeleteContactDialog).getByRole('button', { name: 'Удалить' }))
    expect(within(editSupplierDialog).getByLabelText('Контакт 1: телефон')).toBeDisabled()
    await user.pointer({ keys: '[MouseRight]', target: supplierContactRow as HTMLElement })
    const deletedContactContextMenu = await screen.findByRole('menu', { name: 'Действия контакта Смирнов С.С.' })
    expect(within(deletedContactContextMenu).getByText('При восстановлении контакта будет восстановлен и поставщик.')).toBeInTheDocument()
    await user.click(within(deletedContactContextMenu).getByRole('menuitem', { name: 'Восстановить контакт' }))
    const restoreContactDialog = await screen.findByRole('dialog', { name: 'Восстановить контакт?' })
    expect(within(restoreContactDialog).getByText('Смирнов С.С.')).toBeInTheDocument()
    expect(within(restoreContactDialog).getByText('Контакт снова станет активным. Если поставщик был скрыт, он тоже будет восстановлен после сохранения карточки.')).toBeInTheDocument()
    await waitFor(() => expect(within(restoreContactDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Восстановить контакт?' })).not.toBeInTheDocument()
    expect(within(editSupplierDialog).getByLabelText('Контакт 1: телефон')).toBeDisabled()

    await user.pointer({ keys: '[MouseRight]', target: supplierContactRow as HTMLElement })
    const reopenedRestoreContactContextMenu = await screen.findByRole('menu', { name: 'Действия контакта Смирнов С.С.' })
    await user.click(within(reopenedRestoreContactContextMenu).getByRole('menuitem', { name: 'Восстановить контакт' }))
    const reopenedRestoreContactDialog = await screen.findByRole('dialog', { name: 'Восстановить контакт?' })
    await user.click(within(reopenedRestoreContactDialog).getByRole('button', { name: 'Восстановить' }))
    expect(within(editSupplierDialog).getByLabelText('Контакт 1: телефон')).toBeEnabled()
    await user.clear(within(editSupplierDialog).getByLabelText('Контакт 1: телефон'))
    await user.type(within(editSupplierDialog).getByLabelText('Контакт 1: телефон'), '+7 900 111-22-44')
    await user.click(within(editSupplierDialog).getByRole('button', { name: /Сохранить/i }))
    const supplierChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменения поставщика' })
    expect(within(supplierChangeDialog).getByText('Новый подрядчик')).toBeInTheDocument()
    expect(within(supplierChangeDialog).getByText('Контакты')).toBeInTheDocument()
    expect(within(supplierChangeDialog).getByText(/22-33/)).toBeInTheDocument()
    expect(within(supplierChangeDialog).getByText(/22-44/)).toBeInTheDocument()
    const supplierChangeCancelButton = within(supplierChangeDialog).getByRole('button', { name: 'Отмена' })
    const supplierChangeSaveButton = within(supplierChangeDialog).getByRole('button', { name: 'Сохранить' })
    expect(Boolean(supplierChangeCancelButton.compareDocumentPosition(supplierChangeSaveButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await user.click(supplierChangeSaveButton)

    supplierRow = within(suppliersTable).getByText('Новый подрядчик').closest('[role="row"]')!
    await user.click(within(supplierRow as HTMLElement).getByRole('button', { name: 'Изменить поставщика Новый подрядчик' }))
    const reopenSupplierDialog = await screen.findByRole('dialog', { name: 'Новый подрядчик' })
    const reopenedSupplierContactRow = within(reopenSupplierDialog).getByLabelText('Контакт 1: ФИО').closest('[role="row"]')!
    await user.pointer({ keys: '[MouseRight]', target: reopenedSupplierContactRow as HTMLElement })
    const reopenedContactContextMenu = await screen.findByRole('menu', { name: 'Действия контакта Смирнов С.С.' })
    await user.click(within(reopenedContactContextMenu).getByRole('menuitem', { name: 'Удалить контакт' }))
    const reopenedDeleteContactDialog = await screen.findByRole('dialog', { name: 'Удалить контакт?' })
    await user.type(within(reopenedDeleteContactDialog).getByLabelText('Причина удаления контакта'), 'Контакт больше не работает')
    await user.click(within(reopenedDeleteContactDialog).getByRole('button', { name: 'Удалить' }))
    await user.click(within(reopenSupplierDialog).getByRole('button', { name: /Сохранить/i }))
    const deleteContactChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменения поставщика' })
    await user.click(within(deleteContactChangeDialog).getByRole('button', { name: 'Сохранить' }))
    expect(deletedSupplierContactReason).toBe('Контакт больше не работает')

    supplierRow = within(suppliersTable).getByText('Новый подрядчик').closest('[role="row"]')!
    await user.pointer({ keys: '[MouseRight]', target: supplierRow as HTMLElement })
    const supplierContextMenu = await screen.findByRole('menu', { name: 'Действия поставщика Новый подрядчик' })
    expect(within(supplierContextMenu).getByRole('menuitem', { name: 'Изменить' })).toBeInTheDocument()
    expect(within(supplierContextMenu).getByRole('menuitem', { name: 'Открыть финансовый отчет' })).toBeInTheDocument()
    await user.click(within(supplierContextMenu).getByRole('menuitem', { name: 'Удалить' }))
    const deleteSupplierDialog = await screen.findByRole('dialog', { name: 'Удалить поставщика?' })
    expect(within(deleteSupplierDialog).getByText('Новый подрядчик')).toBeInTheDocument()
    expect(within(deleteSupplierDialog).getByRole('button', { name: 'Удалить' })).toBeDisabled()
    await waitFor(() => expect(within(deleteSupplierDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Удалить поставщика?' })).not.toBeInTheDocument()
    expect(within(supplierRow as HTMLElement).queryByText('Удален')).not.toBeInTheDocument()
    expect(archivedSupplierReason).toBeNull()
    await user.click(within(supplierRow as HTMLElement).getByRole('button', { name: 'Удалить поставщика Новый подрядчик' }))
    const reopenedDeleteSupplierDialog = await screen.findByRole('dialog', { name: 'Удалить поставщика?' })
    expect(within(reopenedDeleteSupplierDialog).getByRole('button', { name: 'Удалить' })).toBeDisabled()
    await user.type(within(reopenedDeleteSupplierDialog).getByLabelText('Причина удаления поставщика'), 'Договор больше не действует')
    await user.click(within(reopenedDeleteSupplierDialog).getByRole('button', { name: 'Удалить' }))
    await waitFor(() => expect(archivedSupplierReason).toBe('Договор больше не действует'))
    await waitFor(() => expect(within(supplierRow as HTMLElement).getByText('Удален')).toBeInTheDocument())
    const restoreSupplierButton = within(supplierRow as HTMLElement).getByRole('button', { name: 'Восстановить поставщика Новый подрядчик' })
    await user.click(restoreSupplierButton)
    const restoreSupplierDialog = await screen.findByRole('dialog', { name: 'Вернуть запись?' })
    expect(within(restoreSupplierDialog).getByText('Новый подрядчик')).toBeInTheDocument()
    await waitFor(() => expect(within(restoreSupplierDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Вернуть запись?' })).not.toBeInTheDocument()
    expect(restoredSupplierIds).toHaveLength(0)
    expect(within(supplierRow as HTMLElement).getByText('Удален')).toBeInTheDocument()
    expect(restoreSupplierButton).toHaveFocus()

    await user.click(restoreSupplierButton)
    const reopenedRestoreSupplierDialog = await screen.findByRole('dialog', { name: 'Вернуть запись?' })
    await user.click(within(reopenedRestoreSupplierDialog).getByRole('button', { name: 'Вернуть запись' }))
    await waitFor(() => expect(restoredSupplierIds).toEqual(['22222222-2222-4222-8222-222222222222']))
    await waitFor(() => expect(within(supplierRow as HTMLElement).queryByText('Удален')).not.toBeInTheDocument())
    expect(within(supplierRow as HTMLElement).getByRole('button', { name: 'Изменить поставщика Новый подрядчик' })).toBeInTheDocument()

    await user.click(within(contractorsPanel).getByRole('tab', { name: 'Персонал' }))
    const departmentsTable = within(contractorsPanel).getByRole('table', { name: 'Отделы персонала' })
    expect(within(contractorsPanel).getByRole('table', { name: 'Персонал' })).toBeInTheDocument()
    const archivedDepartmentRow = within(departmentsTable).getByText('Архивный отдел').closest('[role="row"]')!
    expect(within(archivedDepartmentRow as HTMLElement).getByText('Удален')).toBeInTheDocument()
    const restoreDepartmentButton = within(archivedDepartmentRow as HTMLElement).getByRole('button', { name: 'Восстановить отдел Архивный отдел' })
    await user.click(restoreDepartmentButton)
    const restoreDepartmentDialog = await screen.findByRole('dialog', { name: 'Вернуть запись?' })
    expect(within(restoreDepartmentDialog).getByText('Отдел Архивный отдел')).toBeInTheDocument()
    await waitFor(() => expect(within(restoreDepartmentDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Вернуть запись?' })).not.toBeInTheDocument()
    expect(restoredStaffDepartmentIds).toHaveLength(0)
    expect(within(archivedDepartmentRow as HTMLElement).getByText('Удален')).toBeInTheDocument()
    expect(restoreDepartmentButton).toHaveFocus()

    await user.click(restoreDepartmentButton)
    const reopenedRestoreDepartmentDialog = await screen.findByRole('dialog', { name: 'Вернуть запись?' })
    await user.click(within(reopenedRestoreDepartmentDialog).getByRole('button', { name: 'Вернуть запись' }))
    await waitFor(() => expect(restoredStaffDepartmentIds).toEqual(['55555555-5555-4555-8555-555555555555']))
    await waitFor(() => expect(within(archivedDepartmentRow as HTMLElement).queryByText('Удален')).not.toBeInTheDocument())
    expect(within(archivedDepartmentRow as HTMLElement).getByText('Активен')).toBeInTheDocument()

    const activeDepartmentRow = within(departmentsTable).getByText('Бухгалтерия').closest('[role="row"]')!
    const deleteDepartmentButton = within(activeDepartmentRow as HTMLElement).getByRole('button', { name: 'Удалить отдел Бухгалтерия' })
    await user.click(deleteDepartmentButton)
    const deleteDepartmentDialog = await screen.findByRole('dialog', { name: 'Удалить отдел?' })
    expect(within(deleteDepartmentDialog).getByText('Бухгалтерия')).toBeInTheDocument()
    expect(within(deleteDepartmentDialog).getByRole('button', { name: 'Удалить' })).toBeDisabled()
    await waitFor(() => expect(within(deleteDepartmentDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Удалить отдел?' })).not.toBeInTheDocument()
    expect(archivedStaffDepartmentRequests).toHaveLength(0)
    expect(within(activeDepartmentRow as HTMLElement).getByText('Активен')).toBeInTheDocument()
    expect(deleteDepartmentButton).toHaveFocus()

    await user.click(deleteDepartmentButton)
    const reopenedDeleteDepartmentDialog = await screen.findByRole('dialog', { name: 'Удалить отдел?' })
    await user.type(within(reopenedDeleteDepartmentDialog).getByLabelText('Причина удаления отдела'), 'Отдел закрыт')
    await user.click(within(reopenedDeleteDepartmentDialog).getByRole('button', { name: 'Удалить' }))
    await waitFor(() => expect(archivedStaffDepartmentRequests).toEqual([{ id: '66666666-6666-4666-8666-666666666666', reason: 'Отдел закрыт' }]))
    await waitFor(() => expect(within(activeDepartmentRow as HTMLElement).getByText('Удален')).toBeInTheDocument())
    expect(within(activeDepartmentRow as HTMLElement).getByRole('button', { name: 'Восстановить отдел Бухгалтерия' })).toBeInTheDocument()

    const addDepartmentButton = within(contractorsPanel).getByRole('button', { name: 'Добавить отдел' })
    await user.click(addDepartmentButton)
    let departmentDialog = await screen.findByRole('dialog', { name: 'Новый отдел' })
    await user.type(within(departmentDialog).getByLabelText('Наименование отдела'), 'Черновой отдел')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Новый отдел' })).not.toBeInTheDocument())
    await waitFor(() => expect(addDepartmentButton).toHaveFocus())
    expect(within(contractorsPanel).queryByText('Черновой отдел')).not.toBeInTheDocument()

    await user.click(addDepartmentButton)
    departmentDialog = await screen.findByRole('dialog', { name: 'Новый отдел' })
    await user.type(within(departmentDialog).getByLabelText('Наименование отдела'), 'Охрана')
    await user.click(within(departmentDialog).getByRole('button', { name: 'Ок' }))
    await waitFor(() => expect(addDepartmentButton).toHaveFocus())

    const addEmployeeButton = within(contractorsPanel).getByRole('button', { name: 'Добавить сотрудника' })
    await user.click(addEmployeeButton)
    let employeeDialog = await screen.findByRole('dialog', { name: 'Новый сотрудник' })
    await user.type(within(employeeDialog).getByLabelText('ФИО сотрудника'), 'Черновой сотрудник')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Новый сотрудник' })).not.toBeInTheDocument())
    await waitFor(() => expect(addEmployeeButton).toHaveFocus())
    expect(within(contractorsPanel).queryByText('Черновой сотрудник')).not.toBeInTheDocument()

    await user.click(addEmployeeButton)
    employeeDialog = await screen.findByRole('dialog', { name: 'Новый сотрудник' })
    await user.type(within(employeeDialog).getByLabelText('ФИО сотрудника'), 'Смирнов Алексей')
    await user.selectOptions(within(employeeDialog).getByLabelText('Отдел сотрудника'), 'Охрана')
    await user.type(within(employeeDialog).getByLabelText('Ставка сотрудника'), '25000')
    await user.click(within(employeeDialog).getByRole('button', { name: /Сохранить/i }))
    await waitFor(() => expect(within(within(contractorsPanel).getByRole('table', { name: 'Персонал' })).getByText('Смирнов Алексей')).toBeInTheDocument())
    expect(addEmployeeButton).toHaveFocus()

    const staffTable = within(contractorsPanel).getByRole('table', { name: 'Персонал' })
    const staffResizeHandle = within(staffTable).getByRole('button', { name: 'Изменить ширину столбца Ставка' })
    fireEvent.mouseDown(staffResizeHandle, { clientX: 100 })
    fireEvent.mouseMove(document, { clientX: 130 })
    fireEvent.mouseUp(document)
    await waitFor(() => expect(JSON.parse(window.localStorage.getItem('garagebalance.contractors.staffColumnWidths') ?? '{}').rate).toBe(180))

    const employeeRow = within(staffTable).getByText('Смирнов Алексей').closest('[role="row"]')!
    expect(within(employeeRow as HTMLElement).getByRole('button', { name: 'Изменить сотрудника Смирнов Алексей' })).toBeInTheDocument()
    await user.click(within(employeeRow as HTMLElement).getByRole('button', { name: 'Изменить сотрудника Смирнов Алексей' }))
    const editEmployeeDialog = await screen.findByRole('dialog', { name: 'Смирнов Алексей' })
    expect(within(editEmployeeDialog).getByLabelText('Отдел сотрудника').tagName).toBe('SELECT')
    expect(within(editEmployeeDialog).getByRole('button', { name: 'Открыть фин. отчет' })).toHaveClass('contractors-report-button')
    expect(within(editEmployeeDialog).queryByRole('button', { name: 'Удалить сотрудника' })).not.toBeInTheDocument()
    await user.clear(within(editEmployeeDialog).getByLabelText('Ставка сотрудника'))
    await user.type(within(editEmployeeDialog).getByLabelText('Ставка сотрудника'), '30000')
    await user.click(within(editEmployeeDialog).getByRole('button', { name: /Сохранить/i }))
    const employeeChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменения сотрудника' })
    expect(within(employeeChangeDialog).getByText('Смирнов Алексей')).toBeInTheDocument()
    expect(within(employeeChangeDialog).getByText('Ставка')).toBeInTheDocument()
    expect(within(employeeChangeDialog).getByText(/25\s*000\s*->\s*30000/)).toBeInTheDocument()
    const employeeChangeCancelButton = within(employeeChangeDialog).getByRole('button', { name: 'Отмена' })
    const employeeChangeSaveButton = within(employeeChangeDialog).getByRole('button', { name: 'Сохранить' })
    expect(Boolean(employeeChangeCancelButton.compareDocumentPosition(employeeChangeSaveButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await user.click(employeeChangeSaveButton)

    const updatedEmployeeRow = within(staffTable).getByText('Смирнов Алексей').closest('[role="row"]')!
    await user.pointer({ keys: '[MouseRight]', target: updatedEmployeeRow as HTMLElement })
    const employeeContextMenu = await screen.findByRole('menu', { name: 'Действия сотрудника Смирнов Алексей' })
    expect(within(employeeContextMenu).getByRole('menuitem', { name: 'Изменить' })).toBeInTheDocument()
    expect(within(employeeContextMenu).getByRole('menuitem', { name: 'Открыть финансовый отчет' })).toBeInTheDocument()
    await user.click(within(employeeContextMenu).getByRole('menuitem', { name: 'Удалить' }))
    const deleteEmployeeDialog = await screen.findByRole('dialog', { name: 'Удалить сотрудника?' })
    expect(within(deleteEmployeeDialog).getByText('Смирнов Алексей')).toBeInTheDocument()
    expect(within(deleteEmployeeDialog).getByRole('button', { name: 'Удалить' })).toBeDisabled()
    await waitFor(() => expect(within(deleteEmployeeDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Удалить сотрудника?' })).not.toBeInTheDocument()
    expect(within(updatedEmployeeRow as HTMLElement).queryByText('Удален')).not.toBeInTheDocument()
    expect(archivedStaffMemberReason).toBeNull()

    await user.pointer({ keys: '[MouseRight]', target: updatedEmployeeRow as HTMLElement })
    const reopenedEmployeeContextMenu = await screen.findByRole('menu', { name: 'Действия сотрудника Смирнов Алексей' })
    await user.click(within(reopenedEmployeeContextMenu).getByRole('menuitem', { name: 'Удалить' }))
    const reopenedDeleteEmployeeDialog = await screen.findByRole('dialog', { name: 'Удалить сотрудника?' })
    expect(within(reopenedDeleteEmployeeDialog).getByRole('button', { name: 'Удалить' })).toBeDisabled()
    await user.type(within(reopenedDeleteEmployeeDialog).getByLabelText('Причина удаления сотрудника'), 'Больше не работает')
    await user.click(within(reopenedDeleteEmployeeDialog).getByRole('button', { name: 'Удалить' }))
    await waitFor(() => expect(archivedStaffMemberReason).toBe('Больше не работает'))
    await waitFor(() => expect(within(updatedEmployeeRow as HTMLElement).getByText('Удален')).toBeInTheDocument())
    const restoreEmployeeButton = within(updatedEmployeeRow as HTMLElement).getByRole('button', { name: 'Восстановить сотрудника Смирнов Алексей' })
    await user.click(restoreEmployeeButton)
    const restoreEmployeeDialog = await screen.findByRole('dialog', { name: 'Вернуть запись?' })
    expect(within(restoreEmployeeDialog).getByText('Смирнов Алексей')).toBeInTheDocument()
    await waitFor(() => expect(within(restoreEmployeeDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Вернуть запись?' })).not.toBeInTheDocument()
    expect(restoredStaffMemberIds).toHaveLength(0)
    expect(within(updatedEmployeeRow as HTMLElement).getByText('Удален')).toBeInTheDocument()
    expect(restoreEmployeeButton).toHaveFocus()

    await user.click(restoreEmployeeButton)
    const reopenedRestoreEmployeeDialog = await screen.findByRole('dialog', { name: 'Вернуть запись?' })
    await user.click(within(reopenedRestoreEmployeeDialog).getByRole('button', { name: 'Вернуть запись' }))
    await waitFor(() => expect(restoredStaffMemberIds).toEqual(['44444444-4444-4444-8444-444444444444']))
    await waitFor(() => expect(within(updatedEmployeeRow as HTMLElement).queryByText('Удален')).not.toBeInTheDocument())
    expect(within(updatedEmployeeRow as HTMLElement).getByRole('button', { name: 'Изменить сотрудника Смирнов Алексей' })).toBeInTheDocument()

    expect(within(contractorsPanel).queryByRole('table', { name: 'История изменений контрагентов', hidden: true })).not.toBeInTheDocument()
    expect(within(contractorsPanel).queryByLabelText('Раздел истории контрагентов')).not.toBeInTheDocument()
  }, 180000)

  it('confirms contractor staff edits with department and rate diff', async () => {
    const user = userEvent.setup()
    const currentDepartment = createStaffDepartment({ id: '11111111-1111-4111-8111-111111111111', name: 'Бухгалтерия' })
    const nextDepartment = createStaffDepartment({ id: '22222222-2222-4222-8222-222222222222', name: 'Охрана' })
    let staffMember = createStaffMember({
      id: '33333333-3333-4333-8333-333333333333',
      fullName: 'Петрова Ольга',
      departmentId: currentDepartment.id,
      departmentName: currentDepartment.name,
      rate: 40000,
    })
    const updateStaffMember = vi.fn(async (_token: string, id: string, request: UpsertStaffMemberRequest) => {
      const department = request.departmentId === nextDepartment.id ? nextDepartment : currentDepartment
      staffMember = createStaffMember({
        ...staffMember,
        id,
        fullName: request.fullName,
        departmentId: department.id,
        departmentName: department.name,
        rate: request.rate,
      })
      return staffMember
    })
    const dictionaryClient = createDictionaryClient({
      getStaffDepartments: async () => [currentDepartment, nextDepartment],
      getStaffMembers: async () => [staffMember],
      updateStaffMember,
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Контрагенты')
    const contractorsPanel = await screen.findByRole('region', { name: 'Контрагенты' })
    await user.click(within(contractorsPanel).getByRole('tab', { name: 'Персонал' }))
    const staffTable = await within(contractorsPanel).findByRole('table', { name: 'Персонал' })
    let staffRow = within(staffTable).getByText('Петрова Ольга').closest('[role="row"]')
    if (!staffRow) {
      throw new Error('Строка сотрудника Петрова Ольга не найдена.')
    }

    let editButton = within(staffRow as HTMLElement).getByRole('button', { name: 'Изменить сотрудника Петрова Ольга' })
    await user.click(editButton)
    let staffDialog = await screen.findByRole('dialog', { name: 'Петрова Ольга' })
    await user.click(within(staffDialog).getByRole('button', { name: /Сохранить/i }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердить изменения сотрудника' })).not.toBeInTheDocument())
    expect(updateStaffMember).not.toHaveBeenCalled()
    await waitFor(() => expect(editButton).toHaveFocus())

    staffRow = within(staffTable).getByText('Петрова Ольга').closest('[role="row"]')
    if (!staffRow) {
      throw new Error('Строка сотрудника Петрова Ольга не найдена после no-op сохранения.')
    }

    editButton = within(staffRow as HTMLElement).getByRole('button', { name: 'Изменить сотрудника Петрова Ольга' })
    await user.click(editButton)
    staffDialog = await screen.findByRole('dialog', { name: 'Петрова Ольга' })
    await user.selectOptions(within(staffDialog).getByLabelText('Отдел сотрудника'), nextDepartment.name)
    await user.clear(within(staffDialog).getByLabelText('Ставка сотрудника'))
    await user.type(within(staffDialog).getByLabelText('Ставка сотрудника'), '45000')
    const saveButton = within(staffDialog).getByRole('button', { name: /Сохранить/i })
    await user.click(saveButton)

    const confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменения сотрудника' })
    expect(within(confirmationDialog).getByText('Петрова Ольга')).toBeInTheDocument()
    expect(confirmationDialog).toHaveTextContent('Отдел')
    expect(confirmationDialog).toHaveTextContent('Бухгалтерия -> Охрана')
    expect(confirmationDialog).toHaveTextContent('Ставка')
    expect(confirmationDialog).toHaveTextContent(/40\s*000\s*->\s*45000/)
    expect(updateStaffMember).not.toHaveBeenCalled()
    await waitFor(() => expect(within(confirmationDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердить изменения сотрудника' })).not.toBeInTheDocument())
    await waitFor(() => expect(saveButton).toHaveFocus())
    expect(updateStaffMember).not.toHaveBeenCalled()

    await user.click(saveButton)
    const reopenedConfirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменения сотрудника' })
    await user.click(within(reopenedConfirmationDialog).getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => expect(updateStaffMember).toHaveBeenCalledWith('token', staffMember.id, expect.objectContaining({
      departmentId: nextDepartment.id,
      rate: 45000,
    })))
    const updatedStaffRow = within(staffTable).getByText('Петрова Ольга').closest('[role="row"]')
    if (!updatedStaffRow) {
      throw new Error('Строка сотрудника Петрова Ольга не найдена после подтверждения.')
    }
    expect(updatedStaffRow).toHaveTextContent('Охрана')
    expect(updatedStaffRow).toHaveTextContent(/45\s*000/)
  })

  it('opens financial reports for suppliers and staff from contractors tables', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const supplierId = '22222222-2222-4222-8222-222222222222'
    const staffMemberId = '33333333-3333-4333-8333-333333333333'
    const supplier = createSupplier({
      id: supplierId,
      name: 'Водоканал',
      groupId: '44444444-4444-4444-8444-444444444444',
      groupName: 'Коммунальные услуги',
      contactPerson: 'Иванов П.В.',
    })
    const department = createStaffDepartment({
      id: '55555555-5555-4555-8555-555555555555',
      name: 'Бухгалтерия',
    })
    const staffMember = createStaffMember({
      id: staffMemberId,
      fullName: 'Петрова Ольга',
      departmentId: department.id,
      departmentName: department.name,
      rate: 40000,
    })
    const getOperationsPage = vi.fn(async (_token: string, params?: Parameters<FinanceClient['getOperationsPage']>[1]) => {
      if (params?.supplierId === supplierId) {
        return {
          items: [createFinancialOperation({
            id: 'operation-supplier-report',
            operationKind: 'expense',
            operationDate: '2026-06-20',
            accountingMonth: '2026-06-01',
            amount: 600,
            documentNumber: 'RKO-1',
            supplierId,
            supplierName: supplier.name,
            expenseTypeName: 'Водоснабжение',
          })],
          totalCount: 1,
          offset: 0,
          limit: 500,
        }
      }

      if (params?.staffMemberId === staffMemberId) {
        return {
          items: [createFinancialOperation({
            id: 'operation-staff-report',
            operationKind: 'expense',
            operationDate: '2026-06-21',
            accountingMonth: '2026-06-01',
            amount: 15000,
            documentNumber: 'RKO-2',
            staffMemberId,
            staffMemberName: staffMember.fullName,
            staffDepartmentName: department.name,
            expenseTypeName: 'Зарплата',
          })],
          totalCount: 1,
          offset: 0,
          limit: 500,
        }
      }

      return { items: [], totalCount: 0, offset: 0, limit: params?.limit ?? 500 }
    })
    const getSupplierAccrualsPage = vi.fn(async (_token: string, params?: Parameters<FinanceClient['getSupplierAccrualsPage']>[1]) => ({
      items: params?.supplierId === supplierId
        ? [createSupplierAccrual({
          id: 'supplier-accrual-report',
          supplierId,
          supplierName: supplier.name,
          expenseTypeName: 'Водоснабжение',
          amount: 1000,
          documentNumber: 'INV-1',
        })]
        : [],
      totalCount: params?.supplierId === supplierId ? 1 : 0,
      offset: 0,
      limit: params?.limit ?? 500,
    }))
    const dictionaryClient = createDictionaryClient({
      getSupplierGroups: async () => [createGroup({ id: supplier.groupId, name: supplier.groupName })],
      getSuppliers: async () => [supplier],
      getSupplierContacts: async () => [],
      getStaffDepartments: async () => [department],
      getStaffMembers: async () => [staffMember],
    })
    const getEvents = vi.fn(async (_token: string, params?: Parameters<AuditClient['getEvents']>[1]) => [
      createAuditEvent({
        id: `audit-${params?.relatedCounterparty ?? 'none'}`,
        action: 'dictionary.supplier_updated',
        entityType: params?.relatedCounterparty === staffMemberId ? 'staff_member' : 'supplier',
        relatedCounterpartyId: params?.relatedCounterparty ?? null,
        relatedCounterpartyName: params?.relatedCounterparty === staffMemberId ? staffMember.fullName : supplier.name,
        summary: params?.relatedCounterparty === staffMemberId ? 'Изменена ставка сотрудника.' : 'Изменен контакт поставщика.',
        reason: 'Проверка карточки',
      }),
    ])
    const auditClient = createAuditClient({ getEvents })

    render(<App authClient={createAuthClient()} auditClient={auditClient} dictionaryClient={dictionaryClient} financeClient={createFinanceClient({ getOperationsPage, getSupplierAccrualsPage })} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Контрагенты')
    const contractorsPanel = await screen.findByRole('region', { name: 'Контрагенты' })

    await user.click(within(contractorsPanel).getByRole('tab', { name: 'Поставщики' }))
    const openSupplierReportButton = await within(contractorsPanel).findByRole('button', { name: 'Открыть финансовый отчет поставщика Водоканал' })
    await user.click(openSupplierReportButton)
    const supplierReport = await screen.findByRole('dialog', { name: 'Водоканал' })
    const supplierReportCloseButton = within(supplierReport).getByRole('button', { name: 'Закрыть финансовый отчет контрагента' })
    await waitFor(() => expect(supplierReportCloseButton).toHaveFocus())
    expect(within(supplierReport).getByRole('table', { name: 'Финансовый отчет поставщика' })).toBeInTheDocument()
    expect(within(supplierReport).getByText('INV-1')).toBeInTheDocument()
    expect(within(supplierReport).getByText('RKO-1')).toBeInTheDocument()
    expect(within(supplierReport).queryByRole('table', { name: 'История изменений контрагента' })).not.toBeInTheDocument()
    expect(within(supplierReport).getByRole('button', { name: 'Открыть в истории изменений' })).toBeInTheDocument()
    expect(getSupplierAccrualsPage).toHaveBeenCalledWith('token', expect.objectContaining({ supplierId, limit: 500 }))
    expect(getOperationsPage).toHaveBeenCalledWith('token', expect.objectContaining({ supplierId, operationKind: 'expense', limit: 500 }))
    expect(getEvents).not.toHaveBeenCalled()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Водоканал' })).not.toBeInTheDocument()
    await waitFor(() => expect(openSupplierReportButton).toHaveFocus())

    await user.click(within(contractorsPanel).getByRole('tab', { name: 'Персонал' }))
    const openStaffReportButton = await within(contractorsPanel).findByRole('button', { name: 'Открыть финансовый отчет сотрудника Петрова Ольга' })
    await user.click(openStaffReportButton)
    const staffReport = await screen.findByRole('dialog', { name: 'Петрова Ольга' })
    const staffReportCloseButton = within(staffReport).getByRole('button', { name: 'Закрыть финансовый отчет контрагента' })
    await waitFor(() => expect(staffReportCloseButton).toHaveFocus())
    expect(within(staffReport).getByRole('table', { name: 'Финансовый отчет сотрудника' })).toBeInTheDocument()
    expect(within(staffReport).getAllByText('Начисление зарплаты')).toHaveLength(6)
    expect(within(staffReport).getByText('RKO-2')).toBeInTheDocument()
    expect(within(staffReport).queryByRole('table', { name: 'История изменений контрагента' })).not.toBeInTheDocument()
    expect(getOperationsPage).toHaveBeenCalledWith('token', expect.objectContaining({ staffMemberId, operationKind: 'expense', limit: 500 }))
    await user.click(within(staffReport).getByRole('button', { name: 'Открыть в истории изменений' }))
    expect(screen.queryByRole('dialog', { name: 'Петрова Ольга' })).not.toBeInTheDocument()
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })
    await waitFor(() => expect(within(auditPanel).getByLabelText('Связанный контрагент истории изменений')).toHaveValue(staffMemberId))
    await waitFor(() => expect(getEvents).toHaveBeenCalledWith('token', expect.objectContaining({
      section: 'dictionary',
      entityType: 'staff_member',
      relatedCounterparty: staffMemberId,
      limit: 25,
    })))
  }, 30000)

  it('filters contractor debtors and sorts visible contractor rows', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const firstOwner = createOwner({ id: 'owner-garage-1', lastName: 'Иванов', firstName: 'Иван' })
    const secondOwner = createOwner({ id: 'owner-garage-12', lastName: 'Петров', firstName: 'Петр' })
    const thirdOwner = createOwner({ id: 'owner-garage-27', lastName: 'Сидорова', firstName: 'Анна' })
    const supplierGroup = createGroup({ id: 'supplier-group-services', name: 'Услуги' })
    const suppliers = [
      createSupplier({ id: 'supplier-energy', name: 'Энергосбыт', groupId: supplierGroup.id, groupName: 'Электроэнергия', startingBalance: 39000 }),
      createSupplier({ id: 'supplier-legal', name: 'Правовой центр', groupId: supplierGroup.id, groupName: 'Юридические услуги', startingBalance: 0 }),
      createSupplier({ id: 'supplier-waste', name: 'ЭкоВывоз', groupId: supplierGroup.id, groupName: 'Вывоз мусора', startingBalance: 15000 }),
    ]
    const dictionaryClient = createDictionaryClient({
      getOwners: async () => [firstOwner, secondOwner, thirdOwner],
      getGarages: async () => [
        createGarage({ id: 'garage-1', number: '1', ownerId: firstOwner.id, ownerName: firstOwner.fullName, peopleCount: 3, floorCount: 1, overdueDebt: 1300 }),
        createGarage({ id: 'garage-12', number: '12', ownerId: secondOwner.id, ownerName: secondOwner.fullName, peopleCount: 1, floorCount: 2, overdueDebt: 0 }),
        createGarage({ id: 'garage-27', number: '27', ownerId: thirdOwner.id, ownerName: thirdOwner.fullName, peopleCount: 2, floorCount: 1, overdueDebt: 1700 }),
      ],
      getSupplierGroups: async () => [supplierGroup],
      getSuppliers: async () => suppliers,
      getSupplierContacts: async () => [],
    })

    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Контрагенты')
    const contractorsPanel = await screen.findByRole('region', { name: 'Контрагенты' })

    await user.click(within(contractorsPanel).getByRole('button', { name: 'Показать должников' }))
    const garagesTable = within(contractorsPanel).getByRole('table', { name: 'Гаражи' })
    expect(within(garagesTable).getByText('Иванов Иван')).toBeInTheDocument()
    expect(within(garagesTable).getByText('Сидорова Анна')).toBeInTheDocument()
    expect(within(garagesTable).queryByText('Петров Петр')).not.toBeInTheDocument()

    await user.click(within(garagesTable).getByRole('button', { name: 'Просроченная задолженность' }))
    await user.click(within(garagesTable).getByRole('button', { name: 'Просроченная задолженность' }))
    const sortedGarageRows = within(garagesTable).getAllByRole('row').slice(1)
    expect(within(sortedGarageRows[0]).getAllByRole('cell')[0]).toHaveTextContent('27')
    expect(within(sortedGarageRows[1]).getAllByRole('cell')[0]).toHaveTextContent('1')

    await user.click(within(contractorsPanel).getByRole('tab', { name: 'Поставщики' }))
    const suppliersTable = await within(contractorsPanel).findByRole('table', { name: 'Поставщики' })
    expect(within(contractorsPanel).getByRole('button', { name: 'Показать должников' })).toBeInTheDocument()
    expect(within(suppliersTable).getByText('Энергосбыт')).toBeInTheDocument()
    expect(within(suppliersTable).getByText('ЭкоВывоз')).toBeInTheDocument()
    expect(within(suppliersTable).getByText('Правовой центр')).toBeInTheDocument()

    await user.click(within(contractorsPanel).getByRole('button', { name: 'Показать должников' }))
    expect(within(contractorsPanel).getByRole('button', { name: 'Показать всех поставщиков' })).toBeInTheDocument()
    expect(within(suppliersTable).queryByText('Правовой центр')).not.toBeInTheDocument()

    await user.click(within(suppliersTable).getByRole('button', { name: 'Задолженность' }))
    await user.click(within(suppliersTable).getByRole('button', { name: 'Задолженность' }))
    const sortedSupplierRows = within(suppliersTable).getAllByRole('row').slice(1)
    expect(within(sortedSupplierRows[0]).getByText('Энергосбыт')).toBeInTheDocument()
    expect(within(sortedSupplierRows[1]).getByText('ЭкоВывоз')).toBeInTheDocument()
  }, 30000)

  it('shows empty states for contractor debtor filters and empty staff', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const owner = createOwner({ id: 'owner-no-debt', lastName: 'Петров', firstName: 'Петр' })
    const supplierGroup = createGroup({ id: 'supplier-group-no-debt', name: 'Услуги' })
    const dictionaryClient = createDictionaryClient({
      getOwners: async () => [owner],
      getGarages: async () => [createGarage({ id: 'garage-no-debt', number: '12', ownerId: owner.id, ownerName: owner.fullName, overdueDebt: 0 })],
      getSupplierGroups: async () => [supplierGroup],
      getSuppliers: async () => [createSupplier({ id: 'supplier-no-debt', name: 'Водоканал', groupId: supplierGroup.id, groupName: supplierGroup.name, startingBalance: 0 })],
      getSupplierContacts: async () => [],
      getStaffDepartments: async () => [],
      getStaffMembers: async () => [],
    })

    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Контрагенты')
    const contractorsPanel = await screen.findByRole('region', { name: 'Контрагенты' })

    await user.click(within(contractorsPanel).getByRole('button', { name: 'Показать должников' }))
    const garagesTable = within(contractorsPanel).getByRole('table', { name: 'Гаражи' })
    expect(within(garagesTable).getByRole('cell', { name: 'Гаражей с задолженностью не найдено.' })).toBeInTheDocument()
    expect(within(garagesTable).queryByText('Петров Петр')).not.toBeInTheDocument()

    await user.click(within(contractorsPanel).getByRole('tab', { name: 'Поставщики' }))
    const suppliersTable = await within(contractorsPanel).findByRole('table', { name: 'Поставщики' })
    expect(within(suppliersTable).getByText('Водоканал')).toBeInTheDocument()

    await user.click(within(contractorsPanel).getByRole('button', { name: 'Показать должников' }))
    expect(within(suppliersTable).getByRole('cell', { name: 'Поставщиков с задолженностью не найдено.' })).toBeInTheDocument()
    expect(within(suppliersTable).queryByText('Водоканал')).not.toBeInTheDocument()

    await user.click(within(contractorsPanel).getByRole('tab', { name: 'Персонал' }))
    const staffTable = await within(contractorsPanel).findByRole('table', { name: 'Персонал' })
    expect(within(staffTable).getByRole('cell', { name: 'Сотрудники пока не настроены.' })).toBeInTheDocument()
  }, 30000)

  it('loads and saves tariff values and electricity tier names from tariffs screen', async () => {
    const user = userEvent.setup()
    let updatedTariffRequest: UpsertTariffRequest | null = null
    const electricityTariff = createTariff({
      id: 'tariff-electricity',
      name: 'Электроэнергия',
      calculationBase: 'meter_electricity',
      rate: 4,
      electricityFirstThreshold: 1,
      electricitySecondThreshold: 3,
      electricityFirstTierName: 'От 0 кВт',
      electricitySecondTierName: 'От 1 кВт',
      electricityThirdTierName: 'От 3 кВт',
      electricityFirstRate: 2,
      electricitySecondRate: 3,
      electricityThirdRate: 5,
      effectiveFrom: '2026-01-01',
    })
    const dictionaryClient = createDictionaryClient({
      getTariffs: async () => [electricityTariff],
      updateTariff: async (_token, id, request) => {
        updatedTariffRequest = request
        return createTariff({
          id,
          name: request.name,
          calculationBase: request.calculationBase,
          rate: request.rate,
          effectiveFrom: request.effectiveFrom,
          comment: request.comment ?? null,
          electricityFirstThreshold: request.electricityFirstThreshold ?? null,
          electricitySecondThreshold: request.electricitySecondThreshold ?? null,
          electricityFirstTierName: request.electricityFirstTierName ?? null,
          electricitySecondTierName: request.electricitySecondTierName ?? null,
          electricityThirdTierName: request.electricityThirdTierName ?? null,
          electricityFirstRate: request.electricityFirstRate ?? null,
          electricitySecondRate: request.electricitySecondRate ?? null,
          electricityThirdRate: request.electricityThirdRate ?? null,
        })
      },
    })

    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Тарифы и сборы')
    const tariffsPanel = await screen.findByRole('region', { name: 'Тарифы и сборы' })

    const firstTierName = within(tariffsPanel).getByLabelText('Электроэнергия: От 0 кВт: наименование')
    await user.clear(firstTierName)
    await user.type(firstTierName, 'Льготный порог{Enter}')
    const confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение?' })
    await user.click(within(confirmationDialog).getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => expect(updatedTariffRequest).toMatchObject({
      name: 'Электроэнергия',
      calculationBase: 'meter_electricity',
      electricityFirstTierName: 'Льготный порог',
      electricitySecondTierName: 'От 1 кВт',
      electricityThirdTierName: 'От 3 кВт',
      electricityFirstRate: 2,
      electricitySecondRate: 3,
      electricityThirdRate: 5,
    }))
  })

  it('creates a charge service setting from tariffs screen and renders saved rows', async () => {
    const user = userEvent.setup()
    let createdServiceRequest: unknown = null
    let updatedServiceRequest: unknown = null
    const archivedServiceRequests: Array<{ id: string; reason: string }> = []
    const restoredServiceIds: string[] = []
    let serviceSettings: ChargeServiceSettingDto[] = []
    const serviceIncomeType = createAccountingType({ id: 'income-security', name: 'Охрана', code: 'membership' })
    const serviceTariff = createTariff({ id: 'tariff-security', name: 'Тариф охраны', calculationBase: 'fixed', rate: 1200 })
    const dictionaryClient = createDictionaryClient({
      getIncomeTypes: async () => [serviceIncomeType],
      getTariffs: async () => [serviceTariff],
      getChargeServiceSettings: async (_token, _search, _limit, includeArchived = false) => (
        serviceSettings.filter((setting) => includeArchived || !setting.isArchived)
      ),
      createChargeServiceSetting: async (_token, request) => {
        createdServiceRequest = request
        const savedSetting = createChargeServiceSetting({
          id: 'service-security',
          name: request.name,
          isRegular: request.isRegular,
          periodicityMonths: request.periodicityMonths ?? null,
          accrualStartMonth: request.accrualStartMonth ?? null,
          paymentDueDay: request.paymentDueDay ?? null,
          paymentDueMonth: request.paymentDueMonth ?? null,
          overdueGraceDays: request.overdueGraceDays,
          incomeTypeId: request.incomeTypeId ?? null,
          tariffId: request.tariffId ?? null,
          isMetered: request.isMetered,
          hasTieredTariff: request.hasTieredTariff,
          unitName: request.unitName ?? null,
        })
        serviceSettings = [savedSetting]
        return savedSetting
      },
      updateChargeServiceSetting: async (_token, id, request) => {
        updatedServiceRequest = request
        const savedSetting = createChargeServiceSetting({
          id,
          name: request.name,
          isRegular: request.isRegular,
          periodicityMonths: request.periodicityMonths ?? null,
          accrualStartMonth: request.accrualStartMonth ?? null,
          paymentDueDay: request.paymentDueDay ?? null,
          paymentDueMonth: request.paymentDueMonth ?? null,
          overdueGraceDays: request.overdueGraceDays,
          incomeTypeId: request.incomeTypeId ?? null,
          tariffId: request.tariffId ?? null,
          isMetered: request.isMetered,
          hasTieredTariff: request.hasTieredTariff,
          unitName: request.unitName ?? null,
        })
        serviceSettings = serviceSettings.map((setting) => (setting.id === id ? savedSetting : setting))
        return savedSetting
      },
      archiveChargeServiceSetting: async (_token, id, reason) => {
        archivedServiceRequests.push({ id, reason })
        serviceSettings = serviceSettings.map((setting) => (setting.id === id ? { ...setting, isArchived: true } : setting))
      },
      restoreChargeServiceSetting: async (_token, id) => {
        restoredServiceIds.push(id)
        const restoredSetting = serviceSettings.find((setting) => setting.id === id)
        if (!restoredSetting) {
          throw new Error('Услуга не найдена')
        }

        const nextSetting = { ...restoredSetting, isArchived: false }
        serviceSettings = serviceSettings.map((setting) => (setting.id === id ? nextSetting : setting))
        return nextSetting
      },
    })

    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Тарифы и сборы')
    const tariffsPanel = await screen.findByRole('region', { name: 'Тарифы и сборы' })

    await user.click(within(tariffsPanel).getByRole('button', { name: 'Добавить услугу' }))
    const serviceDialog = await screen.findByRole('dialog', { name: 'Добавить услугу' })
    await user.type(within(serviceDialog).getByLabelText('Наименование услуги'), 'Охрана')
    await user.click(within(serviceDialog).getByLabelText('Регулярные платежи'))
    expect(within(serviceDialog).getByLabelText('Вид начисления регулярной услуги')).toHaveValue(serviceIncomeType.id)
    expect(within(serviceDialog).getByLabelText('Тариф регулярной услуги')).toHaveValue(serviceTariff.id)
    await user.clear(within(serviceDialog).getByLabelText('День оплаты'))
    await user.type(within(serviceDialog).getByLabelText('День оплаты'), '28')
    await user.selectOptions(within(serviceDialog).getByLabelText('Месяц оплаты'), 'фев')
    await user.clear(within(serviceDialog).getByLabelText('Единица измерения'))
    await user.type(within(serviceDialog).getByLabelText('Единица измерения'), 'руб.')
    await user.click(within(serviceDialog).getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => expect(createdServiceRequest).toMatchObject({
      name: 'Охрана',
      isRegular: true,
      periodicityMonths: 12,
      accrualStartMonth: 1,
      paymentDueDay: 28,
      paymentDueMonth: 2,
      overdueGraceDays: 30,
      incomeTypeId: serviceIncomeType.id,
      tariffId: serviceTariff.id,
      isMetered: true,
      hasTieredTariff: true,
      unitName: 'руб.',
    }))
    await waitFor(() => expect(within(tariffsPanel).getAllByText('Охрана').length).toBeGreaterThan(0))
    expect(within(tariffsPanel).getByLabelText('Охрана: Периодичность: значение')).toHaveValue('12')
    expect(within(tariffsPanel).getByLabelText('Охрана: Оплата до: день')).toHaveValue('28')

    const periodicityInput = within(tariffsPanel).getByLabelText('Охрана: Периодичность: значение')
    await user.clear(periodicityInput)
    await user.type(periodicityInput, '6')
    await user.keyboard('{Enter}')
    let confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение?' })
    expect(within(confirmationDialog).getByText('Периодичность')).toBeInTheDocument()
    const cancelConfirmationButton = within(confirmationDialog).getByRole('button', { name: 'Отмена' })
    await waitFor(() => expect(cancelConfirmationButton).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердить изменение?' })).not.toBeInTheDocument())
    await waitFor(() => expect(periodicityInput).toHaveFocus())
    expect(updatedServiceRequest).toBeNull()
    expect(periodicityInput).toHaveValue('12')

    await user.clear(periodicityInput)
    await user.type(periodicityInput, '6')
    await user.keyboard('{Enter}')
    confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение?' })
    expect(within(confirmationDialog).getByText('Периодичность')).toBeInTheDocument()
    await user.click(within(confirmationDialog).getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => expect(updatedServiceRequest).toMatchObject({
      name: 'Охрана',
      isRegular: true,
      periodicityMonths: 6,
      accrualStartMonth: 1,
      paymentDueDay: 28,
      paymentDueMonth: 2,
      overdueGraceDays: 30,
      incomeTypeId: serviceIncomeType.id,
      tariffId: serviceTariff.id,
      isMetered: true,
      hasTieredTariff: true,
      unitName: 'руб.',
    }))
    expect(within(tariffsPanel).getByLabelText('Охрана: Периодичность: значение')).toHaveValue('6')

    await user.click(within(tariffsPanel).getByRole('button', { name: 'Архивировать услугу Охрана' }))
    let archiveDialog = await screen.findByRole('dialog', { name: 'Архивировать услугу?' })
    const archiveCancelButton = within(archiveDialog).getByRole('button', { name: 'Отмена' })
    await waitFor(() => expect(archiveCancelButton).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Архивировать услугу?' })).not.toBeInTheDocument())
    expect(archivedServiceRequests).toEqual([])

    await user.click(within(tariffsPanel).getByRole('button', { name: 'Архивировать услугу Охрана' }))
    archiveDialog = await screen.findByRole('dialog', { name: 'Архивировать услугу?' })
    expect(within(archiveDialog).getByRole('button', { name: 'Архивировать' })).toBeDisabled()
    await user.type(within(archiveDialog).getByLabelText('Причина архивации услуги'), 'Услуга больше не действует')
    await user.click(within(archiveDialog).getByRole('button', { name: 'Архивировать' }))

    await waitFor(() => expect(archivedServiceRequests).toEqual([
      { id: 'service-security', reason: 'Услуга больше не действует' },
    ]))
    expect(within(tariffsPanel).getByLabelText('Охрана: Периодичность: значение')).toBeDisabled()
    expect(within(tariffsPanel).getByRole('button', { name: 'Вернуть услугу Охрана' })).toBeInTheDocument()

    await user.click(within(tariffsPanel).getByRole('button', { name: 'Вернуть услугу Охрана' }))
    let restoreDialog = await screen.findByRole('dialog', { name: 'Вернуть услугу?' })
    const restoreCancelButton = within(restoreDialog).getByRole('button', { name: 'Отмена' })
    await waitFor(() => expect(restoreCancelButton).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Вернуть услугу?' })).not.toBeInTheDocument())
    expect(restoredServiceIds).toEqual([])

    await user.click(within(tariffsPanel).getByRole('button', { name: 'Вернуть услугу Охрана' }))
    restoreDialog = await screen.findByRole('dialog', { name: 'Вернуть услугу?' })
    await user.click(within(restoreDialog).getByRole('button', { name: 'Вернуть' }))

    await waitFor(() => expect(restoredServiceIds).toEqual(['service-security']))
    expect(within(tariffsPanel).getByLabelText('Охрана: Периодичность: значение')).toBeEnabled()
    expect(within(tariffsPanel).getByRole('button', { name: 'Архивировать услугу Охрана' })).toBeInTheDocument()
  })

  it('keeps backend tariff dictionaries above stale saved tariff form state', async () => {
    const user = userEvent.setup()
    vi.mocked(formStatesApi.getState).mockImplementation(async () => ({
      scope: 'tariffs-and-fees-prototype',
      payload: {
        tariffRows: [
          {
            id: 'water-rate',
            group: 'Вода',
            category: 'Вода',
            title: 'Старый тариф воды',
            amount: '1',
            unit: 'руб.',
            byMeter: false,
            tiered: false,
            calculationBase: 'meter_water',
          },
        ],
        oneTimeRows: [
          {
            id: 'stale-fee',
            name: 'Старый сбор',
            amount: '10',
            isActive: true,
          },
        ],
      },
      updatedAtUtc: '2026-06-29T03:00:00Z',
      updatedByUserId: 'admin-user',
    }))
    const dictionaryClient = createDictionaryClient({
      getTariffs: async () => [
        createTariff({
          id: 'water-tariff',
          name: 'Тариф воды из БД',
          calculationBase: 'meter_water',
          rate: 125,
          effectiveFrom: '2026-01-01',
        }),
      ],
      getChargeServiceSettings: async () => [
        createChargeServiceSetting({
          id: 'service-security',
          name: 'Охрана из БД',
          isRegular: true,
          periodicityMonths: 12,
          accrualStartMonth: 3,
          paymentDueDay: 25,
          paymentDueMonth: 12,
          overdueGraceDays: 45,
          isMetered: false,
          hasTieredTariff: false,
          unitName: 'руб.',
        }),
      ],
      getIrregularPayments: async () => [
        createIrregularPayment({
          id: 'irregular-gate',
          name: 'Сбор на ворота из БД',
          amount: 777,
        }),
      ],
    })

    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Тарифы и сборы')
    const tariffsPanel = await screen.findByRole('region', { name: 'Тарифы и сборы' })

    await waitFor(() => expect(within(tariffsPanel).getByLabelText('Вода: Тариф воды из БД: значение')).toHaveValue('125'))
    expect(within(tariffsPanel).queryByText('Старый сбор')).not.toBeInTheDocument()
    expect(within(tariffsPanel).getByLabelText('Сумма: Сбор на ворота из БД')).toHaveValue('777')
    expect(within(tariffsPanel).getByLabelText('Охрана из БД: Периодичность: значение')).toHaveValue('12')
    expect(within(tariffsPanel).getByLabelText('Охрана из БД: Оплата до: день')).toHaveValue('25')
    expect(within(tariffsPanel).getByLabelText('Охрана из БД: Оплата до: месяц')).toHaveValue('дек')
    expect(within(tariffsPanel).getByLabelText('Охрана из БД: Перенос долга в просроченный: значение')).toHaveValue('45')
  })

  it('keeps empty backend tariff dictionaries empty despite stale saved tariff form state', async () => {
    const user = userEvent.setup()
    vi.mocked(formStatesApi.getState).mockImplementation(async (_accessToken: string, scope: string) => scope === 'tariffs-and-fees-prototype'
      ? {
        scope,
        payload: {
          tariffRows: [
            {
              id: 'stale-water-rate',
              group: 'Вода',
              category: 'Вода',
              title: 'Старый тариф без БД',
              amount: '1',
              unit: 'руб.',
              byMeter: false,
              tiered: false,
              calculationBase: 'meter_water',
            },
          ],
          oneTimeRows: [
            {
              id: 'stale-one-time',
              name: 'Старый нерегулярный платеж',
              amount: '10',
              isActive: true,
              isDeleted: false,
              isUsed: false,
            },
          ],
        },
        updatedAtUtc: '2026-06-29T03:00:00Z',
        updatedByUserId: 'admin-user',
      }
      : null)
    const getTariffs = vi.fn(async () => [])
    const dictionaryClient = createDictionaryClient({
      getTariffs,
      getIncomeTypes: async () => [],
      getIrregularPayments: async () => [],
      getChargeServiceSettings: async () => [],
    })

    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Тарифы и сборы')
    const tariffsPanel = await screen.findByRole('region', { name: 'Тарифы и сборы' })
    await waitFor(() => expect(getTariffs).toHaveBeenCalledTimes(1))

    expect(await within(tariffsPanel).findByText('Тарифы и услуги пока не настроены.')).toBeInTheDocument()
    expect(within(tariffsPanel).getByText('Нерегулярные платежи пока не настроены.')).toBeInTheDocument()
    expect(within(tariffsPanel).queryByText('Старый тариф без БД')).not.toBeInTheDocument()
    expect(within(tariffsPanel).queryByText('Старый нерегулярный платеж')).not.toBeInTheDocument()
    expect(within(tariffsPanel).queryByLabelText('Вода: Старый тариф без БД: значение')).not.toBeInTheDocument()
  })

  it('keeps backend contractor dictionaries above stale saved contractor form state', async () => {
    const user = userEvent.setup()
    vi.mocked(formStatesApi.getState).mockImplementation(async (_accessToken: string, scope: string) => scope === 'contractors-prototype'
      ? {
        scope,
        payload: {
          garages: [
            { id: 'stale-garage', ownerId: null, number: '999', peopleCount: '9', floorCount: '9', owner: 'Stale Owner', phone: '', address: '', startingBalance: '0', balance: '0', overdueDebt: '', initialWater: '', initialElectricity: '', meters: '', comment: '', isDeleted: false },
          ],
          suppliers: [
            { id: 'stale-supplier', name: 'Stale Supplier', service: 'Stale Service', inn: '', legalAddress: '', contactPerson: '', phone: '', email: '', contacts: [], debt: '', comment: '', isDeleted: false },
          ],
          staff: [
            { id: 'stale-staff', fullName: 'Stale Staff', department: 'Stale Department', rate: '1', isDeleted: false },
          ],
          departments: [
            { id: 'stale-department', name: 'Stale Department' },
          ],
          supplierServices: ['Stale Service'],
        },
        updatedAtUtc: '2026-06-29T03:00:00Z',
        updatedByUserId: 'admin-user',
      }
      : null)
    const contractorOwner = createOwner({ id: 'owner-backend', lastName: 'Backend', firstName: 'Owner' })
    const contractorGarage = createGarage({ id: 'garage-backend', number: '77', ownerId: contractorOwner.id, ownerName: contractorOwner.fullName })
    const supplierGroup = createGroup({ id: 'group-backend', name: 'Backend Service' })
    const supplier = createSupplier({ id: 'supplier-backend', name: 'Backend Supplier', groupId: supplierGroup.id, groupName: supplierGroup.name })
    const supplierContact = createSupplierContact({ id: 'contact-backend', supplierId: supplier.id, supplierName: supplier.name, fullName: 'Backend Contact' })
    const staffDepartment = createStaffDepartment({ id: 'department-backend', name: 'Backend Department' })
    const staffMember = createStaffMember({ id: 'staff-backend', fullName: 'Backend Staff', departmentId: staffDepartment.id, departmentName: staffDepartment.name })
    const dictionaryClient = createDictionaryClient({
      getOwners: async () => [contractorOwner],
      getGarages: async () => [contractorGarage],
      getSupplierGroups: async () => [supplierGroup],
      getSuppliers: async () => [supplier],
      getSupplierContacts: async () => [supplierContact],
      getStaffDepartments: async () => [staffDepartment],
      getStaffMembers: async () => [staffMember],
    })

    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Контрагенты')
    const contractorsPanel = await screen.findByRole('region', { name: 'Контрагенты' })

    await waitFor(() => expect(within(contractorsPanel).getByText('Backend Owner')).toBeInTheDocument())
    expect(within(contractorsPanel).queryByText('Stale Owner')).not.toBeInTheDocument()

    await user.click(within(contractorsPanel).getByRole('tab', { name: 'Поставщики' }))
    await waitFor(() => expect(within(contractorsPanel).getByText('Backend Supplier')).toBeInTheDocument())
    expect(within(contractorsPanel).getByText('Backend Service')).toBeInTheDocument()
    expect(within(contractorsPanel).queryByText('Stale Supplier')).not.toBeInTheDocument()
    expect(within(contractorsPanel).queryByText('Stale Service')).not.toBeInTheDocument()

    await user.click(within(contractorsPanel).getByRole('tab', { name: 'Персонал' }))
    await waitFor(() => expect(within(contractorsPanel).getByText('Backend Staff')).toBeInTheDocument())
    const staffTable = within(contractorsPanel).getByRole('table', { name: 'Персонал' })
    expect(within(staffTable).getByText('Backend Department')).toBeInTheDocument()
    expect(within(contractorsPanel).queryByText('Stale Staff')).not.toBeInTheDocument()
  })

  it('keeps empty backend contractor dictionaries empty despite stale saved contractor form state', async () => {
    const user = userEvent.setup()
    vi.mocked(formStatesApi.getState).mockImplementation(async (_accessToken: string, scope: string) => scope === 'contractors-prototype'
      ? {
        scope,
        payload: {
          garages: [
            { id: 'stale-empty-garage', ownerId: null, number: '404', peopleCount: '1', floorCount: '1', owner: 'Stale Empty Owner', phone: '', address: '', startingBalance: '0', balance: '0', overdueDebt: '', initialWater: '', initialElectricity: '', meters: '', comment: '', isDeleted: false },
          ],
          suppliers: [
            { id: 'stale-empty-supplier', name: 'Stale Empty Supplier', service: 'Stale Empty Service', inn: '', legalAddress: '', contactPerson: '', phone: '', email: '', contacts: [], debt: '', comment: '', isDeleted: false },
          ],
          staff: [
            { id: 'stale-empty-staff', fullName: 'Stale Empty Staff', department: 'Stale Empty Department', rate: '1', isDeleted: false },
          ],
          departments: [
            { id: 'stale-empty-department', name: 'Stale Empty Department' },
          ],
          supplierServices: ['Stale Empty Service'],
        },
        updatedAtUtc: '2026-06-29T03:00:00Z',
        updatedByUserId: 'admin-user',
      }
      : null)
    const getGarages = vi.fn(async () => [])
    const dictionaryClient = createDictionaryClient({
      getOwners: async () => [],
      getGarages,
      getSupplierGroups: async () => [],
      getSuppliers: async () => [],
      getSupplierContacts: async () => [],
      getStaffDepartments: async () => [],
      getStaffMembers: async () => [],
    })

    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Контрагенты')
    const contractorsPanel = await screen.findByRole('region', { name: 'Контрагенты' })
    await waitFor(() => expect(getGarages).toHaveBeenCalled())

    expect(within(contractorsPanel).queryByText('Stale Empty Owner')).not.toBeInTheDocument()
    expect(within(contractorsPanel).queryByText('Stale Empty Supplier')).not.toBeInTheDocument()
    expect(within(contractorsPanel).queryByText('Stale Empty Staff')).not.toBeInTheDocument()
  })

  it('shows meter readings prototype as a yearly garage table', async () => {
    const user = userEvent.setup()
    let createdMeterReadingRequest: CreateMeterReadingRequest | null = null
    let updatedMeterReadingRequest: CreateMeterReadingRequest | null = null
    let updatedMeterReadingId: string | null = null
    const meterReadingPageRequests: Array<Parameters<FinanceClient['getMeterReadingsPage']>[1]> = []
    const dictionaryClient = createDictionaryClient({
      getGarages: async () => [
        createGarage({ id: 'garage-27', number: '27' }),
        createGarage({ id: 'garage-12', number: '12' }),
      ],
    })
    const financeClient = createFinanceClient({
      getMeterReadingsPage: async (_token, params) => {
        meterReadingPageRequests.push(params)
        return { items: [], totalCount: 0, offset: 0, limit: 6000 }
      },
      createMeterReading: async (_token, request) => {
        createdMeterReadingRequest = request
        return createMeterReading({
          id: 'meter-reading-jan',
          garageId: request.garageId,
          garageNumber: '12',
          meterKind: request.meterKind,
          accountingMonth: request.accountingMonth,
          readingDate: request.readingDate,
          currentValue: request.currentValue,
          previousValue: 0,
          consumption: request.currentValue,
          comment: request.comment ?? null,
        })
      },
      updateMeterReading: async (_token, meterReadingId, request) => {
        updatedMeterReadingId = meterReadingId
        updatedMeterReadingRequest = request
        return createMeterReading({
          id: meterReadingId,
          garageId: request.garageId,
          garageNumber: '12',
          meterKind: request.meterKind,
          accountingMonth: request.accountingMonth,
          readingDate: request.readingDate,
          currentValue: request.currentValue,
          previousValue: 4654,
          consumption: request.currentValue - 4654,
          comment: request.comment ?? null,
        })
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: 'Счётчики' }))

    const readingsPanel = await screen.findByRole('region', { name: 'Показания' })
    expect(screen.queryByText('Поиск по гаражу, владельцу или поставщику')).not.toBeInTheDocument()
    expect(within(readingsPanel).getByLabelText('Год показаний')).toHaveValue('2026')
    expect(within(readingsPanel).queryByRole('combobox', { name: 'Тип показаний' })).not.toBeInTheDocument()
    expect(within(readingsPanel).getByLabelText('Тип показаний')).toHaveTextContent('Электроэнергия, кВт')
    expect(within(readingsPanel).getByRole('table', { name: 'Показания счетчиков за 2026 год' })).toBeInTheDocument()
    expect(within(readingsPanel).getByRole('columnheader', { name: /ЯнварькВт/i })).toBeInTheDocument()
    expect(await within(readingsPanel).findByLabelText('Гараж 12, Январь, показание')).toBeInTheDocument()
    await waitFor(() => expect(meterReadingPageRequests).toHaveLength(1))
    expect(meterReadingPageRequests[0]).toMatchObject({ monthFrom: '2026-01', monthTo: '2026-12', meterKind: 'electricity' })
    expect(within(readingsPanel).getByLabelText('Гараж 27, Декабрь, показание')).toBeInTheDocument()
    expect(within(readingsPanel).queryByLabelText('Гараж 35, Декабрь, показание')).not.toBeInTheDocument()

    const yearInput = within(readingsPanel).getByLabelText('Год показаний')
    await user.clear(yearInput)
    await user.type(yearInput, '1899')
    expect(within(readingsPanel).getByRole('alert')).toHaveTextContent('Введите год четырьмя цифрами от 1900 до 9999.')
    expect(within(readingsPanel).getByRole('table', { name: 'Показания счетчиков за 2026 год' })).toBeInTheDocument()
    expect(within(readingsPanel).queryByRole('table', { name: 'Показания счетчиков за 1899 год' })).not.toBeInTheDocument()
    expect(meterReadingPageRequests).toHaveLength(1)

    await user.clear(yearInput)
    await user.type(yearInput, '2026')
    const januaryInput = within(readingsPanel).getByLabelText('Гараж 12, Январь, показание')
    await user.type(januaryInput, '4654{Enter}')
    expect(screen.queryByRole('dialog', { name: 'Подтвердить показание?' })).not.toBeInTheDocument()
    await waitFor(() => expect(createdMeterReadingRequest?.currentValue).toBe(4654))
    expect(createdMeterReadingRequest).toMatchObject({ garageId: 'garage-12', meterKind: 'electricity', accountingMonth: '2026-01-01' })
    await waitFor(() => expect(januaryInput).toHaveValue('4654'))

    await user.clear(januaryInput)
    await user.type(januaryInput, '4660{Enter}')
    const readingConfirmDialog = await screen.findByRole('dialog', { name: 'Подтвердить показание?' })
    expect(within(readingConfirmDialog).getByText('Гараж 12, Январь')).toBeInTheDocument()
    const readingChangeList = within(readingConfirmDialog).getByRole('list', { name: 'Изменяемые поля показания' })
    expect(within(readingChangeList).getByText('Показание')).toBeInTheDocument()
    expect(within(readingChangeList).getByText('4654')).toBeInTheDocument()
    expect(within(readingChangeList).getByText('4660')).toBeInTheDocument()
    const readingCancelButton = within(readingConfirmDialog).getByRole('button', { name: 'Отмена' })
    const readingSaveButton = within(readingConfirmDialog).getByRole('button', { name: 'Сохранить' })
    const readingCloseButton = within(readingConfirmDialog).getByRole('button', { name: 'Закрыть подтверждение показания' })
    expect(Boolean(readingCancelButton.compareDocumentPosition(readingSaveButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(readingCancelButton).toHaveFocus())
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(readingCloseButton).toHaveFocus()
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(readingSaveButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(readingCloseButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(readingCancelButton).toHaveFocus()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердить показание?' })).not.toBeInTheDocument())
    expect(januaryInput).toHaveFocus()
    expect(januaryInput).toHaveValue('4654')

    await user.clear(januaryInput)
    await user.type(januaryInput, '4660{Enter}')
    const reopenedReadingConfirmDialog = await screen.findByRole('dialog', { name: 'Подтвердить показание?' })
    await user.click(within(reopenedReadingConfirmDialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(updatedMeterReadingRequest?.currentValue).toBe(4660))
    expect(updatedMeterReadingId).toBe('meter-reading-jan')
    expect(updatedMeterReadingRequest).toMatchObject({ garageId: 'garage-12', meterKind: 'electricity', accountingMonth: '2026-01-01' })
    await waitFor(() => expect(januaryInput).toHaveValue('4660'))

    expect(within(readingsPanel).queryByRole('tab', { name: 'История изменений' })).not.toBeInTheDocument()
    expect(within(readingsPanel).queryByRole('table', { name: 'История изменений показаний', hidden: true })).not.toBeInTheDocument()
  })

  it('shows payments prototype and opens payment form modals', async () => {
    const user = userEvent.setup()
    const garage = createGarage({ id: 'garage-1', number: '1', ownerName: 'Иванов Иван', peopleCount: 3, floorCount: 1, startingBalance: -5300 })
    const incomeType = createAccountingType({ id: 'income-electricity', name: 'Электроэнергия', code: 'electricity' })
    const waterIncomeType = createAccountingType({ id: 'income-water', name: 'Водоснабжение', code: 'water' })
    const incomeTypes = [incomeType, waterIncomeType]
    const savedIncomeRequests: CreateIncomeOperationRequest[] = []
    const savedAccrualRequests: CreateAccrualRequest[] = []
    const savedRegularAccrualRequests: GenerateRegularCatalogAccrualsRequest[] = []
    const savedExpenseRequests: CreateExpenseOperationRequest[] = []
    const savedStaffPaymentRequests: CreateStaffPaymentRequest[] = []
    const savedSupplierAccrualRequests: CreateSupplierAccrualRequest[] = []
    const savedSalaryAccrualRequests: GenerateSupplierGroupSalaryAccrualsRequest[] = []
    const savedFundOperationRequests: Array<{ fundId: string; request: CreateFundOperationRequest }> = []
    const dictionaryClient = createDictionaryClient({
      getGarages: async () => [garage],
      getIncomeTypes: async () => incomeTypes,
    })
    const financeClient = createFinanceClient({
      createIncome: async (_token, request) => {
        savedIncomeRequests.push(request)
        const requestIncomeType = incomeTypes.find((item) => item.id === request.incomeTypeId) ?? incomeType
        return createFinancialOperation({
          id: `income-payment-${savedIncomeRequests.length}`,
          garageId: request.garageId,
          garageNumber: garage.number,
          ownerName: garage.ownerName,
          incomeTypeId: request.incomeTypeId,
          incomeTypeName: requestIncomeType.name,
          operationDate: request.operationDate,
          accountingMonth: request.accountingMonth,
          amount: request.amount,
          garageDebtBefore: 5674,
          garageDebtAfter: 0,
        })
      },
      createAccrual: async (_token, request) => {
        savedAccrualRequests.push(request)
        const requestIncomeType = incomeTypes.find((item) => item.id === request.incomeTypeId) ?? incomeType
        return createAccrual({
          id: `garage-accrual-${savedAccrualRequests.length}`,
          garageId: request.garageId,
          garageNumber: garage.number,
          ownerName: garage.ownerName,
          incomeTypeId: request.incomeTypeId,
          incomeTypeName: requestIncomeType.name,
          accountingMonth: request.accountingMonth,
          amount: request.amount,
          source: request.source,
          comment: request.comment ?? null,
        })
      },
      generateRegularCatalogAccruals: async (_token, request) => {
        savedRegularAccrualRequests.push(request)
        return createRegularCatalogAccrualGenerationResult({
          accountingMonth: request.accountingMonth,
          serviceResults: [
            createRegularAccrualGenerationResult({
              accountingMonth: request.accountingMonth,
              incomeTypeId: waterIncomeType.id,
              incomeTypeName: 'Водоснабжение',
              tariffId: 'tariff-1',
              tariffName: 'Тариф воды',
              calculationBase: 'meter_water',
              createdCount: 1,
              totalAmount: 1250,
              createdAccruals: [
                createAccrual({
                  id: `regular-accrual-${savedRegularAccrualRequests.length}`,
                  garageId: garage.id,
                  garageNumber: garage.number,
                  ownerName: garage.ownerName,
                  incomeTypeId: waterIncomeType.id,
                  incomeTypeName: 'Водоснабжение',
                  accountingMonth: request.accountingMonth,
                  amount: 1250,
                  source: 'regular',
                  comment: request.comment ?? null,
                }),
              ],
            }),
          ],
          createdCount: 1,
          totalAmount: 1250,
        })
      },
      createExpense: async (_token, request) => {
        savedExpenseRequests.push(request)
        return createFinancialOperation({
          id: `expense-payment-${savedExpenseRequests.length}`,
          operationKind: 'expense',
          supplierId: request.supplierId,
          supplierName: 'Водоканал',
          expenseTypeId: request.expenseTypeId,
          expenseTypeName: 'Электроэнергия',
          operationDate: request.operationDate,
          accountingMonth: request.accountingMonth,
          amount: request.amount,
          documentNumber: request.documentNumber ?? null,
          comment: request.comment ?? null,
          supplierDebtBefore: 39000,
          supplierDebtAfter: 37800,
        })
      },
      createStaffPayment: async (_token, request) => {
        savedStaffPaymentRequests.push(request)
        return createFinancialOperation({
          id: `staff-payment-${savedStaffPaymentRequests.length}`,
          operationKind: 'expense',
          operationDate: request.operationDate,
          accountingMonth: request.accountingMonth,
          amount: request.amount,
          documentNumber: request.documentNumber ?? null,
          comment: request.comment ?? null,
          staffMemberId: request.staffMemberId,
          staffMemberName: 'Петрова Ольга',
          staffDepartmentName: 'Бухгалтерия',
          expenseTypeName: 'Зарплата',
        })
      },
      createSupplierAccrual: async (_token, request) => {
        savedSupplierAccrualRequests.push(request)
        return createSupplierAccrual({
          id: `supplier-accrual-${savedSupplierAccrualRequests.length}`,
          supplierId: request.supplierId,
          supplierName: 'Водоканал',
          expenseTypeId: request.expenseTypeId,
          expenseTypeName: 'Электроэнергия',
          accountingMonth: request.accountingMonth,
          amount: request.amount,
          source: request.source,
          documentNumber: request.documentNumber ?? null,
          comment: request.comment ?? null,
        })
      },
      generateSupplierGroupSalaryAccruals: async (_token, request) => {
        savedSalaryAccrualRequests.push(request)
        return createSupplierGroupSalaryAccrualGenerationResult({
          accountingMonth: request.accountingMonth,
          supplierGroupId: request.supplierGroupId,
          supplierGroupName: 'Коммунальные услуги',
          expenseTypeName: 'Зарплата',
          createdCount: 1,
          totalAmount: request.amount,
          createdAccruals: [
            createSupplierAccrual({
              id: `salary-accrual-${savedSalaryAccrualRequests.length}`,
              supplierId: 'staff-ivanov',
              supplierName: 'Иванов Сергей',
              expenseTypeId: 'expense-salary',
              expenseTypeName: 'Зарплата',
              accountingMonth: request.accountingMonth,
              amount: request.amount,
              source: 'regular',
              documentNumber: request.documentNumber ?? null,
              comment: request.comment ?? null,
            }),
          ],
        })
      },
      getGarageIncomeWorksheet: async () => createGarageIncomeWorksheet({
        garageId: garage.id,
        garageNumber: garage.number,
        ownerName: garage.ownerName,
        accrualTotal: 10174,
        incomeTotal: 0,
        debtTotal: 10174,
        closingDebt: 10174,
        rows: [
          {
            accountingMonth: '2026-06-01',
            incomeTypeId: incomeType.id,
            incomeTypeName: incomeType.name,
            meterKind: 'electricity',
            meterValue: 86,
            meterConsumption: 18,
            accrualAmount: 5674,
            incomeAmount: 0,
            debt: 5674,
          },
          {
            accountingMonth: '2026-06-01',
            incomeTypeId: waterIncomeType.id,
            incomeTypeName: waterIncomeType.name,
            meterKind: 'water',
            meterValue: 59,
            meterConsumption: 4,
            accrualAmount: 4500,
            incomeAmount: 0,
            debt: 4500,
          },
          {
            accountingMonth: '2026-05-01',
            incomeTypeId: null,
            incomeTypeName: 'Членский взнос',
            meterKind: null,
            meterValue: null,
            meterConsumption: null,
            accrualAmount: 0,
            incomeAmount: 0,
            debt: 0,
          },
        ],
      }),
      getExpenseWorksheet: async (_token, params) => createExpenseWorksheet({
        accountingMonth: params?.accountingMonth ?? '2026-06-01',
        accrualTotal: 235000,
        expenseTotal: 55500,
        balanceTotal: 0,
        collectedTotal: 257100,
        differenceTotal: 22100,
        bankAmount: 234000,
        cashAmount: 201600,
        rows: [
          {
            rowKind: 'supplier',
            supplierId: 'supplier-1',
            staffMemberId: null,
            counterpartyName: '',
            expenseTypeId: 'expense-type-1',
            expenseTypeName: 'Электроэнергия',
            accrualAmount: 39000,
            expenseAmount: 39000,
            balance: 0,
            collectedAmount: 43000,
            difference: 4000,
          },
          {
            rowKind: 'supplier',
            supplierId: 'supplier-1',
            staffMemberId: null,
            counterpartyName: '',
            expenseTypeId: 'expense-type-1',
            expenseTypeName: 'Н/о',
            accrualAmount: 4000,
            expenseAmount: 0,
            balance: 0,
            collectedAmount: 15000,
            difference: 1000,
          },
          {
            rowKind: 'supplier',
            supplierId: 'supplier-1',
            staffMemberId: null,
            counterpartyName: '',
            expenseTypeId: 'expense-type-1',
            expenseTypeName: 'Водоснабжение',
            accrualAmount: 32000,
            expenseAmount: 0,
            balance: 0,
            collectedAmount: 29000,
            difference: -3000,
          },
          {
            rowKind: 'supplier',
            supplierId: 'supplier-1',
            staffMemberId: null,
            counterpartyName: '',
            expenseTypeId: 'expense-type-1',
            expenseTypeName: 'Вывоз мусора',
            accrualAmount: 15000,
            expenseAmount: 0,
            balance: 0,
            collectedAmount: 13300,
            difference: -1700,
          },
          {
            rowKind: 'supplier',
            supplierId: 'supplier-1',
            staffMemberId: null,
            counterpartyName: '',
            expenseTypeId: 'expense-type-1',
            expenseTypeName: 'Юридические услуги',
            accrualAmount: 8500,
            expenseAmount: 0,
            balance: 0,
            collectedAmount: null,
            difference: null,
          },
          {
            rowKind: 'staff',
            supplierId: null,
            staffMemberId: 'staff-member-1',
            counterpartyName: 'Иванов',
            expenseTypeId: null,
            expenseTypeName: 'Электрик',
            accrualAmount: 20000,
            expenseAmount: 0,
            balance: 0,
            collectedAmount: 156800,
            difference: null,
          },
          {
            rowKind: 'staff',
            supplierId: null,
            staffMemberId: 'staff-member-1',
            counterpartyName: 'Петрова',
            expenseTypeId: null,
            expenseTypeName: 'Бухгалтерия',
            accrualAmount: 40000,
            expenseAmount: 0,
            balance: 0,
            collectedAmount: null,
            difference: null,
          },
          {
            rowKind: 'staff',
            supplierId: null,
            staffMemberId: 'staff-member-1',
            counterpartyName: 'Сидоров',
            expenseTypeId: null,
            expenseTypeName: 'Председатель',
            accrualAmount: 50000,
            expenseAmount: 0,
            balance: 0,
            collectedAmount: null,
            difference: null,
          },
          {
            rowKind: 'supplier',
            supplierId: 'supplier-1',
            staffMemberId: null,
            counterpartyName: '',
            expenseTypeId: 'expense-type-1',
            expenseTypeName: 'Прочие выплаты',
            accrualAmount: 10000,
            expenseAmount: 0,
            balance: 0,
            collectedAmount: null,
            difference: null,
          },
          {
            rowKind: 'supplier',
            supplierId: 'supplier-1',
            staffMemberId: null,
            counterpartyName: '',
            expenseTypeId: 'expense-type-1',
            expenseTypeName: 'Авансовые выплаты',
            accrualAmount: 0,
            expenseAmount: 16500,
            balance: 0,
            collectedAmount: null,
            difference: null,
          },
          {
            rowKind: 'supplier',
            supplierId: 'supplier-1',
            staffMemberId: null,
            counterpartyName: '',
            expenseTypeId: 'expense-type-1',
            expenseTypeName: 'Выплата без чека',
            accrualAmount: 16500,
            expenseAmount: 0,
            balance: 0,
            collectedAmount: null,
            difference: null,
          },
        ],
      }),
    })
    const fundsClient = createFundsClient({
      createOperation: async (_token, fundId, request) => {
        savedFundOperationRequests.push({ fundId, request })
        return createFundOperation({
          fundId,
          fundName: 'Электроэнергия',
          operationKind: request.operationKind,
          amount: request.amount,
          reason: request.reason,
        })
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={financeClient} fundsClient={fundsClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: 'Платежи' }))

    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    const prototype = within(financePanel).getByRole('region', { name: 'Форма платежей' })
    expect(screen.queryByPlaceholderText('Поиск по гаражу, владельцу или поставщику')).not.toBeInTheDocument()
    const garageSearchInput = within(prototype).getByLabelText('Поиск номера гаража или ФИО владельца')
    expect(garageSearchInput).toBeInTheDocument()
    expect(within(prototype).queryByRole('table', { name: /Поступления гаража/ })).not.toBeInTheDocument()
    expect(within(prototype).getByRole('status')).toHaveTextContent('Выберите гараж через поиск')

    await user.type(garageSearchInput, 'Иванов')
    const garageOption = await within(prototype).findByRole('option', { name: /Гараж\s*1\s*Иванов Иван/ })
    await user.click(garageOption)

    expect(within(prototype).getByRole('region', { name: 'Параметры выбранного гаража' })).toHaveTextContent('Просроченная задолженность')
    expect(within(prototype).getByLabelText('Выбранный гараж')).toHaveTextContent('Иванов Иван')
    expect(within(prototype).getByRole('table', { name: 'История платежей гаража' })).toBeInTheDocument()
    const incomeTable = within(prototype).getByRole('table', { name: 'Поступления гаража 1' })
    await within(incomeTable).findByText('Электроэнергия')
    expect(within(prototype).getByText('май.26')).toBeInTheDocument()

    const electricityPaymentInput = within(prototype).getByLabelText('Платеж Электроэнергия июн.26')
    expect(electricityPaymentInput).toHaveValue('')
    await user.click(electricityPaymentInput)
    await user.type(electricityPaymentInput, '5674')
    await user.keyboard('{Enter}')
    await waitFor(() => expect(savedIncomeRequests[0]).toMatchObject({
      garageId: garage.id,
      incomeTypeId: incomeType.id,
      operationDate: '2026-06-30',
      accountingMonth: '2026-06-01',
      amount: 5674,
    }))
    expect(electricityPaymentInput).toHaveValue('')
    expect(within(prototype).getAllByText('5 674').length).toBeGreaterThanOrEqual(2)

    const addGarageAccrualButton = within(prototype).getByRole('button', { name: 'Добавить начисление гаражу' })
    await user.click(addGarageAccrualButton)
    const garageAccrualDialog = await screen.findByRole('dialog', { name: 'Новое начисление' })
    expect(within(garageAccrualDialog).getByLabelText('Вид начисления гаража')).toHaveValue(incomeType.id)
    await user.selectOptions(within(garageAccrualDialog).getByLabelText('Вид начисления гаража'), waterIncomeType.id)
    await user.clear(within(garageAccrualDialog).getByLabelText('Сумма начисления гаража'))
    await user.type(within(garageAccrualDialog).getByLabelText('Сумма начисления гаража'), '750')
    expect(within(garageAccrualDialog).getByLabelText('Месяц начисления гаража')).toHaveValue('2026-06')
    await user.type(within(garageAccrualDialog).getByLabelText('Комментарий к начислению гаража'), 'Доначисление воды')
    await user.click(within(garageAccrualDialog).getByRole('button', { name: 'Ок' }))
    await waitFor(() => expect(savedAccrualRequests).toHaveLength(1))
    expect(savedAccrualRequests[0]).toMatchObject({
      garageId: garage.id,
      incomeTypeId: waterIncomeType.id,
      accountingMonth: '2026-06-01',
      amount: 750,
      source: 'manual',
      comment: 'Доначисление воды',
    })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Новое начисление' })).not.toBeInTheDocument())
    await waitFor(() => expect(addGarageAccrualButton).toHaveFocus())

    const regularAccrualButton = within(prototype).getByRole('button', { name: 'Сформировать начисления' })
    await user.click(regularAccrualButton)
    let regularAccrualDialog = await screen.findByRole('dialog', { name: 'Сформировать начисления' })
    await waitFor(() => expect(within(regularAccrualDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Сформировать начисления' })).not.toBeInTheDocument())
    expect(savedRegularAccrualRequests).toHaveLength(0)
    await waitFor(() => expect(regularAccrualButton).toHaveFocus())

    await user.click(regularAccrualButton)
    regularAccrualDialog = await screen.findByRole('dialog', { name: 'Сформировать начисления' })
    expect(within(regularAccrualDialog).queryByLabelText('Вид регулярного начисления')).not.toBeInTheDocument()
    expect(within(regularAccrualDialog).queryByLabelText('Тариф регулярного начисления')).not.toBeInTheDocument()
    expect(within(regularAccrualDialog).getByLabelText('Месяц регулярного начисления')).toHaveValue('2026-06')
    await user.type(within(regularAccrualDialog).getByLabelText('Комментарий регулярного начисления'), 'Автоначисление по каталогу')
    await user.click(within(regularAccrualDialog).getByRole('button', { name: 'Ок' }))
    await waitFor(() => expect(savedRegularAccrualRequests).toHaveLength(1))
    expect(savedRegularAccrualRequests[0]).toMatchObject({
      accountingMonth: '2026-06-01',
      comment: 'Автоначисление по каталогу',
    })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Сформировать начисления' })).not.toBeInTheDocument())
    await waitFor(() => expect(regularAccrualButton).toHaveFocus())

    const fullPaymentButton = within(prototype).getByRole('button', { name: 'Полная оплата' })
    await user.click(fullPaymentButton)
    const fullPaymentDialog = await screen.findByRole('dialog', { name: 'Полная оплата' })
    expect(within(fullPaymentDialog).getByLabelText('Период полной оплаты')).toHaveValue('full')
    const fullPaymentAmount = within(fullPaymentDialog).getByLabelText('Сумма полной оплаты')
    expect(fullPaymentAmount).toHaveValue('6500')
    expect(fullPaymentAmount).toHaveAttribute('readonly')
    await user.type(within(fullPaymentDialog).getByLabelText('Комментарий к полной оплате'), 'Оплата остатка')
    await user.click(within(fullPaymentDialog).getByRole('button', { name: 'Принять' }))
    await waitFor(() => expect(savedIncomeRequests).toHaveLength(2))
    expect(savedIncomeRequests[1]).toMatchObject({
      garageId: garage.id,
      incomeTypeId: waterIncomeType.id,
      operationDate: '2026-06-30',
      accountingMonth: '2026-06-01',
      amount: 6500,
      comment: 'Полная оплата Водоснабжение июн.26: Оплата остатка',
    })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Полная оплата' })).not.toBeInTheDocument())
    await waitFor(() => expect(fullPaymentButton).toHaveFocus())

    await user.click(within(prototype).getByRole('tab', { name: 'Выплаты' }))
    expect(within(prototype).getByRole('table', { name: 'Форма выплат за июнь 2026' })).toBeInTheDocument()
    expect(within(prototype).getAllByText('257 100')).toHaveLength(2)

    const addExpenseButton = within(prototype).getByRole('button', { name: 'Добавить выплату' })
    await user.click(addExpenseButton)
    const expenseDialog = await screen.findByRole('dialog', { name: 'Новая выплата' })
    expect(within(expenseDialog).getByLabelText('Поставщик выплаты')).toHaveValue('supplier-1')
    expect(within(expenseDialog).getByLabelText('Вид выплаты')).toHaveValue('expense-type-1')
    expect(within(expenseDialog).getByLabelText('Дата выплаты')).toHaveValue('2026-06-30')
    expect(within(expenseDialog).getByLabelText('Месяц выплаты')).toHaveValue('2026-06')
    await user.type(within(expenseDialog).getByLabelText('Сумма выплаты'), '1200')
    await user.type(within(expenseDialog).getByLabelText('Документ выплаты'), 'RKO-prototype')
    await user.type(within(expenseDialog).getByLabelText('Комментарий к выплате'), 'Оплата из формы выплат')
    await user.click(within(expenseDialog).getByRole('button', { name: 'Провести' }))
    await waitFor(() => expect(savedExpenseRequests).toHaveLength(1))
    expect(savedExpenseRequests[0]).toMatchObject({
      supplierId: 'supplier-1',
      expenseTypeId: 'expense-type-1',
      operationDate: '2026-06-30',
      accountingMonth: '2026-06-01',
      amount: 1200,
      documentNumber: 'RKO-prototype',
      comment: 'Оплата из формы выплат',
    })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Новая выплата' })).not.toBeInTheDocument())
    await waitFor(() => expect(addExpenseButton).toHaveFocus())

    const staffPaymentButton = within(prototype).getByRole('button', { name: 'Оплатить сотрудника Петрова' })
    await user.click(staffPaymentButton)
    const staffPaymentDialog = await screen.findByRole('dialog', { name: 'Выплата сотруднику' })
    expect(within(staffPaymentDialog).getByLabelText('Сотрудник выплаты')).toHaveValue('staff-member-1')
    expect(within(staffPaymentDialog).getByLabelText('Дата выплаты сотруднику')).toHaveValue('2026-06-30')
    expect(within(staffPaymentDialog).getByLabelText('Месяц выплаты сотруднику')).toHaveValue('2026-06')
    expect(within(staffPaymentDialog).getByLabelText('Сумма выплаты сотруднику')).toHaveValue('40000')
    await user.type(within(staffPaymentDialog).getByLabelText('Документ выплаты сотруднику'), 'STAFF-PAY-prototype')
    await user.type(within(staffPaymentDialog).getByLabelText('Комментарий к выплате сотруднику'), 'Выплата сотруднику из формы')
    await user.click(within(staffPaymentDialog).getByRole('button', { name: 'Провести' }))
    await waitFor(() => expect(savedStaffPaymentRequests).toHaveLength(1))
    expect(savedStaffPaymentRequests[0]).toMatchObject({
      staffMemberId: 'staff-member-1',
      operationDate: '2026-06-30',
      accountingMonth: '2026-06-01',
      amount: 40000,
      documentNumber: 'STAFF-PAY-prototype',
      comment: 'Выплата сотруднику из формы',
    })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Выплата сотруднику' })).not.toBeInTheDocument())
    await waitFor(() => expect(staffPaymentButton).toHaveFocus())

    const addAccrualButton = within(prototype).getByRole('button', { name: 'Добавить начисление' })
    await user.click(addAccrualButton)
    const accrualDialog = await screen.findByRole('dialog', { name: 'Новое начисление' })
    expect(within(accrualDialog).getByLabelText('Поставщик начисления')).toHaveValue('supplier-1')
    expect(within(accrualDialog).getByLabelText('Вид начисления поставщику')).toHaveValue('expense-type-1')
    expect(within(accrualDialog).getByLabelText('Месяц начисления поставщику')).toHaveValue('2026-06')
    await user.type(within(accrualDialog).getByLabelText('Сумма начисления поставщику'), '850')
    await user.type(within(accrualDialog).getByLabelText('Документ начисления поставщику'), 'ACT-prototype')
    await user.type(within(accrualDialog).getByLabelText('Комментарий начисления поставщику'), 'Начисление из формы выплат')
    await user.click(within(accrualDialog).getByRole('button', { name: 'Ок' }))
    await waitFor(() => expect(savedSupplierAccrualRequests).toHaveLength(1))
    expect(savedSupplierAccrualRequests[0]).toMatchObject({
      supplierId: 'supplier-1',
      expenseTypeId: 'expense-type-1',
      accountingMonth: '2026-06-01',
      amount: 850,
      source: 'manual',
      documentNumber: 'ACT-prototype',
      comment: 'Начисление из формы выплат',
    })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Новое начисление' })).not.toBeInTheDocument())
    await waitFor(() => expect(addAccrualButton).toHaveFocus())

    const salaryButton = within(prototype).getByRole('button', { name: 'Начислить зарплату' })
    await user.click(salaryButton)
    const salaryDialog = await screen.findByRole('dialog', { name: 'Начислить зарплату' })
    expect(within(salaryDialog).getByLabelText('Группа для начисления зарплаты')).toHaveValue('group-1')
    expect(within(salaryDialog).getByLabelText('Месяц начисления зарплаты')).toHaveValue('2026-06')
    await user.type(within(salaryDialog).getByLabelText('Сумма начисления зарплаты'), '20000')
    await user.type(within(salaryDialog).getByLabelText('Документ начисления зарплаты'), 'PAYROLL-prototype')
    await user.type(within(salaryDialog).getByLabelText('Комментарий начисления зарплаты'), 'Зарплата из формы выплат')
    await user.click(within(salaryDialog).getByRole('button', { name: 'Ок' }))
    await waitFor(() => expect(savedSalaryAccrualRequests).toHaveLength(1))
    expect(savedSalaryAccrualRequests[0]).toMatchObject({
      supplierGroupId: 'group-1',
      accountingMonth: '2026-06-01',
      amount: 20000,
      documentNumber: 'PAYROLL-prototype',
      comment: 'Зарплата из формы выплат',
    })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Начислить зарплату' })).not.toBeInTheDocument())
    await waitFor(() => expect(salaryButton).toHaveFocus())

    const bankButton = within(prototype).getByRole('button', { name: 'Сдать кассу в банк' })
    await user.click(bankButton)
    let bankDialog = await screen.findByRole('dialog', { name: 'Учет суммы на счете в банке' })
    await waitFor(() => expect(within(bankDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Учет суммы на счете в банке' })).not.toBeInTheDocument())
    expect(savedFundOperationRequests).toHaveLength(0)
    await waitFor(() => expect(bankButton).toHaveFocus())

    await user.click(bankButton)
    bankDialog = await screen.findByRole('dialog', { name: 'Учет суммы на счете в банке' })
    expect(await within(bankDialog).findByLabelText('Фонд для сдачи кассы')).toHaveValue('fund-electricity')
    expect(within(bankDialog).getByLabelText('Дата учета суммы в банке')).toHaveValue('2026-06-30')
    await user.type(within(bankDialog).getByLabelText('Сумма в банке'), '12300')
    await user.type(within(bankDialog).getByLabelText('Комментарий к сумме в банке'), 'Инкассация из формы')
    await user.click(within(bankDialog).getByRole('button', { name: 'Ок' }))
    await waitFor(() => expect(savedFundOperationRequests).toHaveLength(1))
    expect(savedFundOperationRequests[0]).toMatchObject({
      fundId: 'fund-electricity',
      request: {
        operationKind: 'deposit',
        amount: 12300,
        reason: 'Сдача кассы в банк 2026-06-30: Инкассация из формы',
      },
    })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Учет суммы на счете в банке' })).not.toBeInTheDocument())
    expect(bankButton).toHaveFocus()
  }, 180000)

  it('uses garages from the dictionary in the payments search', async () => {
    const user = userEvent.setup()
    const garageFromDictionary = createGarage({ id: 'garage-77', number: '77', ownerName: 'Кузнецова Мария', peopleCount: 4, floorCount: 2, startingBalance: -7200 })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient({ getGarages: async () => [garageFromDictionary] })} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: 'Платежи' }))

    const prototype = within(await screen.findByRole('region', { name: 'Платежи' })).getByRole('region', { name: 'Форма платежей' })
    const garageSearchInput = within(prototype).getByLabelText('Поиск номера гаража или ФИО владельца')
    await user.type(garageSearchInput, '77')
    const garageOption = await within(prototype).findByRole('option', { name: /Гараж\s*77\s*Кузнецова Мария/ })
    await user.click(garageOption)

    expect(within(prototype).getByLabelText('Выбранный гараж')).toHaveTextContent('Кузнецова Мария')
    expect(within(prototype).getByRole('region', { name: 'Параметры выбранного гаража' })).toHaveTextContent('4')
    expect(within(prototype).getByRole('table', { name: 'Поступления гаража 77' })).toBeInTheDocument()
  })

  it('does not show fallback garages or prototype payment history when backend garages are empty', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient({ getGarages: async () => [] })} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await openSection(user, 'Платежи')
    const prototype = within(await screen.findByRole('region', { name: 'Платежи' })).getByRole('region', { name: 'Форма платежей' })
    const garageSearchInput = within(prototype).getByLabelText('Поиск номера гаража или ФИО владельца')
    await user.type(garageSearchInput, 'Иванов')

    expect(within(prototype).queryByRole('option', { name: /Гараж\s*1\s*Иванов Иван/ })).not.toBeInTheDocument()
    expect(within(prototype).queryByRole('table', { name: 'История платежей гаража' })).not.toBeInTheDocument()
    expect(within(prototype).queryByText('19.06.2026')).not.toBeInTheDocument()
    expect(within(prototype).getByRole('status')).toHaveTextContent('Выберите гараж через поиск')
  })

  it('does not show prototype income rows while selected real garage worksheet is unavailable', async () => {
    const user = userEvent.setup()
    const garageFromDictionary = createGarage({
      id: 'garage-1',
      number: '1',
      ownerName: 'Иванов Иван',
      peopleCount: 3,
      floorCount: 1,
    })
    render(
      <App
        authClient={createAuthClient()}
        dictionaryClient={createDictionaryClient({ getGarages: async () => [garageFromDictionary] })}
        financeClient={createFinanceClient({
          getGarageIncomeWorksheet: async () => {
            throw new Error('Серверная ведомость недоступна')
          },
          getOperationsPage: async () => ({ items: [], totalCount: 0, offset: 0, limit: 500 }),
        })}
        importClient={createImportClient()}
        reportClient={createReportClient()}
        releaseClient={createReleaseClient()}
        userClient={createUserClient()}
      />,
    )

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await openSection(user, 'Платежи')
    const prototype = within(await screen.findByRole('region', { name: 'Платежи' })).getByRole('region', { name: 'Форма платежей' })
    await user.type(within(prototype).getByLabelText('Поиск номера гаража или ФИО владельца'), 'Иванов')
    await user.click(await within(prototype).findByRole('option', { name: /Гараж\s*1\s*Иванов Иван/ }))

    expect(await within(prototype).findByText('Серверная ведомость недоступна')).toBeInTheDocument()
    const incomeTable = within(prototype).getByRole('table', { name: 'Поступления гаража 1' })
    expect(within(incomeTable).getByText('Начислений и поступлений за выбранный период пока нет.')).toBeInTheDocument()
    expect(within(incomeTable).queryByText('Электроэнергия')).not.toBeInTheDocument()
    expect(within(incomeTable).queryByText('май.26')).not.toBeInTheDocument()
  })

  it('does not restore stale saved payment rows for a real garage before backend worksheet loads', async () => {
    const user = userEvent.setup()
    const garageFromDictionary = createGarage({
      id: 'garage-1',
      number: '1',
      ownerName: 'Иванов Иван',
      peopleCount: 3,
      floorCount: 1,
    })
    const getGarageIncomeWorksheet = vi.fn(async () => {
      throw new Error('Серверная ведомость недоступна')
    })
    vi.mocked(formStatesApi.getState).mockImplementation(async (_accessToken: string, scope: string) => scope === 'payments-prototype'
      ? {
          scope,
          payload: {
            selectedGarageId: garageFromDictionary.id,
            garageSearch: 'Гараж 1 - Иванов Иван',
            incomeWorksheetMonthFrom: '2026-05',
            incomeWorksheetMonthTo: '2026-06',
            garageRows: [
              {
                id: 'stale-income-row',
                month: '2026-05',
                monthLabel: 'май.26',
                service: 'Старое начисление из сохраненного состояния',
                meter: null,
                difference: null,
                payable: 9999,
                paymentDraft: '',
                paid: 0,
                debt: 9999,
              },
            ],
            historyRows: [
              {
                id: 'stale-history-row',
                date: '01.05.2026',
                time: '10:00',
                amount: 9999,
                purpose: 'Старая история из сохраненного состояния',
                debtAfter: 9999,
              },
            ],
          },
          updatedAtUtc: '2026-06-30T03:00:00Z',
          updatedByUserId: 'admin-user',
        }
      : null)
    render(
      <App
        authClient={createAuthClient()}
        dictionaryClient={createDictionaryClient({ getGarages: async () => [garageFromDictionary] })}
        financeClient={createFinanceClient({
          getGarageIncomeWorksheet,
          getOperationsPage: async () => ({ items: [], totalCount: 0, offset: 0, limit: 500 }),
        })}
        importClient={createImportClient()}
        reportClient={createReportClient()}
        releaseClient={createReleaseClient()}
        userClient={createUserClient()}
      />,
    )

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await openSection(user, 'Платежи')
    const prototype = within(await screen.findByRole('region', { name: 'Платежи' })).getByRole('region', { name: 'Форма платежей' })

    expect(await within(prototype).findByText('Серверная ведомость недоступна')).toBeInTheDocument()
    await waitFor(() => expect(getGarageIncomeWorksheet).toHaveBeenCalledWith('token', 'garage-1', {
      monthFrom: '2026-05-01',
      monthTo: '2026-06-01',
    }))
    const incomeTable = within(prototype).getByRole('table', { name: 'Поступления гаража 1' })
    expect(within(incomeTable).getByText('Начислений и поступлений за выбранный период пока нет.')).toBeInTheDocument()
    expect(within(incomeTable).queryByText('Старое начисление из сохраненного состояния')).not.toBeInTheDocument()
    expect(within(prototype).getByRole('table', { name: 'История платежей гаража' })).not.toHaveTextContent('Старая история из сохраненного состояния')
  })

  it('does not restore saved payment rows for a garage missing from the dictionary', async () => {
    const user = userEvent.setup()
    vi.mocked(formStatesApi.getState).mockImplementation(async (_accessToken: string, scope: string) => scope === 'payments-prototype'
      ? {
          scope,
          payload: {
            selectedGarageId: 'prototype-garage-1',
            garageSearch: 'Гараж 1 - Иванов Иван',
            incomeWorksheetMonthFrom: '2026-05',
            incomeWorksheetMonthTo: '2026-06',
            garageRows: [
              {
                id: 'stale-prototype-income-row',
                month: '2026-05',
                monthLabel: 'май.26',
                service: 'Старое демо-начисление',
                meter: null,
                difference: null,
                payable: 9999,
                paymentDraft: '',
                paid: 0,
                debt: 9999,
              },
            ],
            historyRows: [
              {
                id: 'stale-prototype-history-row',
                date: '19.06.2026',
                time: '10:24',
                amount: 5674,
                purpose: 'Старый демо-платеж',
                debtAfter: 0,
              },
            ],
          },
          updatedAtUtc: '2026-06-30T03:00:00Z',
          updatedByUserId: 'admin-user',
        }
      : null)
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient({ getGarages: async () => [] })} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await openSection(user, 'Платежи')
    const prototype = within(await screen.findByRole('region', { name: 'Платежи' })).getByRole('region', { name: 'Форма платежей' })

    expect(within(prototype).getByRole('status')).toHaveTextContent('Выберите гараж через поиск')
    expect(within(prototype).queryByText('Гараж 1 - Иванов Иван')).not.toBeInTheDocument()
    expect(within(prototype).queryByText('Старое демо-начисление')).not.toBeInTheDocument()
    expect(within(prototype).queryByText('Старый демо-платеж')).not.toBeInTheDocument()
  })

  it('loads selected garage income worksheet from finance backend', async () => {
    const user = userEvent.setup()
    const garageFromDictionary = createGarage({ id: 'garage-77', number: '77', ownerName: 'Кузнецова Мария', peopleCount: 4, floorCount: 2, startingBalance: -7200 })
    const getGarageIncomeWorksheet = vi.fn(async (_token: string, garageId: string) => createGarageIncomeWorksheet({
      garageId,
      garageNumber: '77',
      ownerName: 'Кузнецова Мария',
      openingDebt: 900,
      accrualTotal: 5674,
      incomeTotal: 1000,
      debtTotal: 5574,
      closingDebt: 5574,
      rows: [
        {
          accountingMonth: '2026-06-01',
          incomeTypeId: 'income-type-electricity',
          incomeTypeName: 'Серверная электроэнергия',
          meterKind: 'electricity',
          meterValue: 86,
          meterConsumption: 18,
          accrualAmount: 5674,
          incomeAmount: 1000,
          debt: 4674,
        },
      ],
    }))
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient({ getGarages: async () => [garageFromDictionary] })} financeClient={createFinanceClient({ getGarageIncomeWorksheet })} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: 'Платежи' }))

    const prototype = within(await screen.findByRole('region', { name: 'Платежи' })).getByRole('region', { name: 'Форма платежей' })
    await user.type(within(prototype).getByLabelText('Поиск номера гаража или ФИО владельца'), '77')
    await user.click(await within(prototype).findByRole('option', { name: /Гараж\s*77\s*Кузнецова Мария/ }))

    const currentMonth = getTestCurrentMonthInputValue()
    const previousMonth = addTestMonths(currentMonth, -1)
    const twoMonthsAgo = addTestMonths(currentMonth, -2)
    await waitFor(() => expect(getGarageIncomeWorksheet).toHaveBeenCalledWith('token', 'garage-77', {
      monthFrom: `${previousMonth}-01`,
      monthTo: `${currentMonth}-01`,
    }))
    await user.selectOptions(within(prototype).getByLabelText('Месяц поступлений с'), twoMonthsAgo)
    await waitFor(() => expect(getGarageIncomeWorksheet).toHaveBeenLastCalledWith('token', 'garage-77', {
      monthFrom: `${twoMonthsAgo}-01`,
      monthTo: `${currentMonth}-01`,
    }))
    const incomeTable = within(prototype).getByRole('table', { name: 'Поступления гаража 77' })
    expect(await within(incomeTable).findByText('Серверная электроэнергия')).toBeInTheDocument()
    expect(within(incomeTable).getByLabelText('Платеж Серверная электроэнергия июн.26')).toHaveValue('')
    expect(within(incomeTable).getByText('86')).toBeInTheDocument()
    expect(within(incomeTable).getByText('18')).toBeInTheDocument()
    expect(within(incomeTable).getAllByText('4 674').length).toBeGreaterThan(0)
    const periodSummary = within(prototype).getByLabelText('Итоги периода поступлений')
    expect(periodSummary).toHaveTextContent('Долг на начало')
    expect(periodSummary).toHaveTextContent('900')
    expect(periodSummary).toHaveTextContent('Начислено')
    expect(periodSummary).toHaveTextContent('5 674')
    expect(periodSummary).toHaveTextContent('Оплачено')
    expect(periodSummary).toHaveTextContent('1 000')
    expect(periodSummary).toHaveTextContent('Долг на конец')
    expect(periodSummary).toHaveTextContent('5 574')
  })

  it('pays opening debt through full payment when worksheet has no service rows', async () => {
    const user = userEvent.setup()
    const garageFromDictionary = createGarage({ id: 'garage-opening-debt', number: '88', ownerName: 'Смирнов Алексей', peopleCount: 2, floorCount: 1, startingBalance: -900 })
    const getGarageIncomeWorksheet = vi.fn(async (_token: string, garageId: string) => createGarageIncomeWorksheet({
      garageId,
      garageNumber: '88',
      ownerName: 'Смирнов Алексей',
      openingDebt: 900,
      accrualTotal: 0,
      incomeTotal: 0,
      debtTotal: 900,
      closingDebt: 900,
      rows: [],
    }))
    const createGarageDebtPayment = vi.fn(async (_token: string, request) => createFinancialOperation({
      id: 'opening-debt-payment',
      garageId: request.garageId,
      garageNumber: '88',
      ownerName: 'Смирнов Алексей',
      incomeTypeName: 'Перенос задолженности',
      operationDate: request.operationDate,
      accountingMonth: request.accountingMonth,
      amount: request.amount,
      garageDebtBefore: 900,
      garageDebtAfter: 0,
    }))
    const createIncome = vi.fn(async () => createFinancialOperation({ id: 'unexpected-income' }))
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient({ getGarages: async () => [garageFromDictionary] })} financeClient={createFinanceClient({ getGarageIncomeWorksheet, createGarageDebtPayment, createIncome })} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: 'Платежи' }))

    const prototype = within(await screen.findByRole('region', { name: 'Платежи' })).getByRole('region', { name: 'Форма платежей' })
    await user.type(within(prototype).getByLabelText('Поиск номера гаража или ФИО владельца'), '88')
    await user.click(await within(prototype).findByRole('option', { name: /Гараж\s*88\s*Смирнов Алексей/ }))

    await waitFor(() => expect(getGarageIncomeWorksheet).toHaveBeenCalled())
    const fullPaymentButton = within(prototype).getByRole('button', { name: 'Полная оплата' })
    await user.click(fullPaymentButton)
    let fullPaymentDialog = await screen.findByRole('dialog', { name: 'Полная оплата' })
    await waitFor(() => expect(within(fullPaymentDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Полная оплата' })).not.toBeInTheDocument())
    await waitFor(() => expect(fullPaymentButton).toHaveFocus())

    await user.click(fullPaymentButton)
    fullPaymentDialog = await screen.findByRole('dialog', { name: 'Полная оплата' })
    expect(within(fullPaymentDialog).getByLabelText('Сумма полной оплаты')).toHaveValue('900')
    await user.type(within(fullPaymentDialog).getByLabelText('Комментарий к полной оплате'), 'Закрываем долг на начало')
    await user.click(within(fullPaymentDialog).getByRole('button', { name: 'Принять' }))

    await waitFor(() => expect(createGarageDebtPayment).toHaveBeenCalledWith('token', expect.objectContaining({
      garageId: 'garage-opening-debt',
      accountingMonth: expect.stringMatching(/^\d{4}-\d{2}-01$/),
      amount: 900,
      comment: 'Закрываем долг на начало',
    })))
    expect(createIncome).not.toHaveBeenCalled()
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Полная оплата' })).not.toBeInTheDocument())
  })

  it('loads selected garage payment history from finance backend', async () => {
    const user = userEvent.setup()
    const garageFromDictionary = createGarage({ id: 'garage-77', number: '77', ownerName: 'Кузнецова Мария', peopleCount: 4, floorCount: 2, startingBalance: -7200 })
    const serverOperation = createFinancialOperation({
      id: 'operation-garage-77',
      garageId: 'garage-77',
      garageNumber: '77',
      ownerName: 'Кузнецова Мария',
      amount: 1234,
      incomeTypeName: 'Серверная оплата',
      garageDebtAfter: 3200,
      operationDate: '2026-06-19',
      accountingMonth: '2026-06-01',
      createdAtUtc: '2026-06-19T10:24:00',
    })
    const getOperationsPage = vi.fn(async (_token: string, params?: Parameters<FinanceClient['getOperationsPage']>[1]) => ({
      items: params?.garageId === 'garage-77'
        ? [serverOperation]
        : [],
      totalCount: params?.garageId === 'garage-77' ? 1 : 0,
      offset: 0,
      limit: params?.limit ?? 25,
    }))
    const updateIncome = vi.fn(async (_token: string, operationId: string, request: CreateIncomeOperationRequest) => ({
      ...serverOperation,
      id: operationId,
      amount: request.amount,
      operationDate: request.operationDate,
      accountingMonth: request.accountingMonth,
      documentNumber: request.documentNumber ?? null,
      comment: request.comment ?? null,
    }))
    const cancelOperation = vi.fn(async (_token: string, operationId: string, request: { reason: string }) => ({
      ...serverOperation,
      id: operationId,
      isCanceled: true,
      comment: `Отменено: ${request.reason}`,
    }))
    const registerReceiptPrintingAction = vi.fn(async (_token: string, operationId: string, request: ReceiptPrintingActionRequest) => createReceiptPrintingAction({
      financialOperationId: operationId,
      action: request.action,
      statusMessage: `Квитанция: ${request.action}`,
    }))
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient({ getGarages: async () => [garageFromDictionary] })} financeClient={createFinanceClient({ getOperationsPage, updateIncome, cancelOperation })} importClient={createImportClient()} integrationClient={createIntegrationClient({ registerReceiptPrintingAction })} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: 'Платежи' }))

    const prototype = within(await screen.findByRole('region', { name: 'Платежи' })).getByRole('region', { name: 'Форма платежей' })
    await user.type(within(prototype).getByLabelText('Поиск номера гаража или ФИО владельца'), '77')
    await user.click(await within(prototype).findByRole('option', { name: /Гараж\s*77\s*Кузнецова Мария/ }))

    await waitFor(() => expect(getOperationsPage).toHaveBeenCalledWith('token', expect.objectContaining({
      operationKind: 'income',
      garageId: 'garage-77',
      limit: 500,
    })))
    const historyTable = within(prototype).getByRole('table', { name: 'История платежей гаража' })
    expect(await within(historyTable).findByText('Серверная оплата')).toBeInTheDocument()
    expect(within(historyTable).getByText('10:24')).toBeInTheDocument()
    expect(within(historyTable).getByText('1 234')).toBeInTheDocument()
    expect(within(historyTable).getByText('3 200')).toBeInTheDocument()

    const printReceiptButton = within(historyTable).getByRole('button', { name: 'Сформировать квитанцию платежа Серверная оплата' })
    await user.click(printReceiptButton)
    let receiptDialog = await screen.findByRole('dialog', { name: 'Сформировать квитанцию?' })
    await waitFor(() => expect(within(receiptDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Сформировать квитанцию?' })).not.toBeInTheDocument())
    await waitFor(() => expect(printReceiptButton).toHaveFocus())
    expect(registerReceiptPrintingAction).not.toHaveBeenCalled()

    await user.click(printReceiptButton)
    receiptDialog = await screen.findByRole('dialog', { name: 'Сформировать квитанцию?' })
    await user.click(within(receiptDialog).getByRole('button', { name: 'Сформировать квитанцию' }))
    await waitFor(() => expect(registerReceiptPrintingAction).toHaveBeenCalledWith('token', 'operation-garage-77', expect.objectContaining({ action: 'print' })))
    expect(await screen.findByText('Квитанция: print')).toBeInTheDocument()

    const cancelReceiptButton = within(historyTable).getByRole('button', { name: 'Отменить печать квитанции платежа Серверная оплата' })
    await user.click(cancelReceiptButton)
    let receiptReasonDialog = await screen.findByRole('dialog', { name: 'Отменить печать квитанции?' })
    await user.click(within(receiptReasonDialog).getByRole('button', { name: 'Отменить печать' }))
    expect(await within(receiptReasonDialog).findByText('Укажите причину для отмены или повторной печати квитанции.')).toBeInTheDocument()
    await user.type(within(receiptReasonDialog).getByLabelText('Причина действия с квитанцией'), 'Ошибка печати')
    await user.click(within(receiptReasonDialog).getByRole('button', { name: 'Отменить печать' }))
    await waitFor(() => expect(registerReceiptPrintingAction).toHaveBeenCalledWith('token', 'operation-garage-77', { action: 'cancel', reason: 'Ошибка печати' }))

    const reprintReceiptButton = within(historyTable).getByRole('button', { name: 'Повторно напечатать квитанцию платежа Серверная оплата' })
    await user.click(reprintReceiptButton)
    receiptReasonDialog = await screen.findByRole('dialog', { name: 'Повторно напечатать квитанцию?' })
    await user.type(within(receiptReasonDialog).getByLabelText('Причина действия с квитанцией'), 'Повторная выдача')
    await user.click(within(receiptReasonDialog).getByRole('button', { name: 'Повторная печать' }))
    await waitFor(() => expect(registerReceiptPrintingAction).toHaveBeenCalledWith('token', 'operation-garage-77', { action: 'reprint', reason: 'Повторная выдача' }))

    const editPaymentButton = within(historyTable).getByRole('button', { name: 'Изменить платеж Серверная оплата' })
    await user.click(editPaymentButton)
    let editDialog = await screen.findByRole('dialog', { name: 'Изменить платеж' })
    expect(within(editDialog).getByLabelText('Сумма изменяемого платежа')).toHaveValue('1234')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Изменить платеж' })).not.toBeInTheDocument())
    await waitFor(() => expect(editPaymentButton).toHaveFocus())
    expect(updateIncome).not.toHaveBeenCalled()

    await user.click(editPaymentButton)
    editDialog = await screen.findByRole('dialog', { name: 'Изменить платеж' })
    await user.clear(within(editDialog).getByLabelText('Сумма изменяемого платежа'))
    await user.type(within(editDialog).getByLabelText('Сумма изменяемого платежа'), '1500')
    await user.clear(within(editDialog).getByLabelText('Комментарий к изменяемому платежу'))
    await user.type(within(editDialog).getByLabelText('Комментарий к изменяемому платежу'), 'Исправление суммы')
    await user.click(within(editDialog).getByRole('button', { name: 'Сохранить' }))
    const paymentChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение платежа?' })
    expect(updateIncome).not.toHaveBeenCalled()
    const paymentChangeList = within(paymentChangeDialog).getByRole('list', { name: 'Изменяемые поля платежа' })
    expect(within(paymentChangeList).getByText('Сумма')).toBeInTheDocument()
    expect(within(paymentChangeList).getByText('1 234,00')).toBeInTheDocument()
    expect(within(paymentChangeList).getByText('1 500,00')).toBeInTheDocument()
    expect(within(paymentChangeList).getByText('Комментарий')).toBeInTheDocument()
    expect(within(paymentChangeList).getByText('Исправление суммы')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердить изменение платежа?' })).not.toBeInTheDocument())
    expect(updateIncome).not.toHaveBeenCalled()
    await user.click(within(editDialog).getByRole('button', { name: 'Сохранить' }))
    const confirmedPaymentChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение платежа?' })
    await user.click(within(confirmedPaymentChangeDialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(updateIncome).toHaveBeenCalledWith('token', 'operation-garage-77', expect.objectContaining({
      garageId: 'garage-77',
      incomeTypeId: 'income-type-1',
      operationDate: '2026-06-19',
      accountingMonth: '2026-06-01',
      amount: 1500,
      comment: 'Исправление суммы',
    })))

    const cancelPaymentButton = within(historyTable).getByRole('button', { name: 'Отменить платеж Серверная оплата' })
    await user.click(cancelPaymentButton)
    const firstCancelDialog = await screen.findByRole('dialog', { name: 'Отменить платеж?' })
    expect(within(firstCancelDialog).getByLabelText('Причина отмены платежа')).toHaveValue('')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Отменить платеж?' })).not.toBeInTheDocument())
    await waitFor(() => expect(cancelPaymentButton).toHaveFocus())
    expect(cancelOperation).not.toHaveBeenCalled()

    await user.click(cancelPaymentButton)
    const cancelDialog = await screen.findByRole('dialog', { name: 'Отменить платеж?' })
    await user.click(within(cancelDialog).getByRole('button', { name: 'Отменить платеж' }))
    expect(await within(cancelDialog).findByText('Укажите причину отмены платежа.')).toBeInTheDocument()
    await user.type(within(cancelDialog).getByLabelText('Причина отмены платежа'), 'Ошибочный платеж')
    await user.click(within(cancelDialog).getByRole('button', { name: 'Отменить платеж' }))
    await waitFor(() => expect(cancelOperation).toHaveBeenCalledWith('token', 'operation-garage-77', { reason: 'Ошибочный платеж' }))
  })

  it('loads expense worksheet from finance backend', async () => {
    const user = userEvent.setup()
    const getExpenseWorksheet = vi.fn(async (_token: string, params?: { accountingMonth?: string }) => createExpenseWorksheet({
      accountingMonth: params?.accountingMonth ?? '2026-06-01',
      bankAmount: 12000,
      rows: [
        {
          rowKind: 'supplier',
          supplierId: 'supplier-water',
          staffMemberId: null,
          counterpartyName: 'Серверный водоканал',
          expenseTypeId: 'expense-water',
          expenseTypeName: 'Водоснабжение',
          accrualAmount: 32000,
          expenseAmount: 10000,
          balance: 22000,
          collectedAmount: 29000,
          difference: -3000,
        },
        {
          rowKind: 'staff',
          supplierId: null,
          staffMemberId: 'staff-accountant',
          counterpartyName: 'Петрова Ольга',
          expenseTypeId: null,
          expenseTypeName: 'Бухгалтерия',
          accrualAmount: 40000,
          expenseAmount: 15000,
          balance: 25000,
          collectedAmount: null,
          difference: null,
        },
      ],
    }))
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient({ getExpenseWorksheet })} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: 'Платежи' }))
    const prototype = within(await screen.findByRole('region', { name: 'Платежи' })).getByRole('region', { name: 'Форма платежей' })
    await user.type(within(prototype).getByLabelText('Поиск номера гаража или ФИО владельца'), '12')
    await user.click(await within(prototype).findByRole('option', { name: /Гараж\s*12\s*Иванов Иван/ }))
    await user.click(within(prototype).getByRole('tab', { name: 'Выплаты' }))

    await waitFor(() => expect(getExpenseWorksheet).toHaveBeenCalledWith('token', { accountingMonth: '2026-06-01' }))
    const expenseTable = within(prototype).getByRole('table', { name: 'Форма выплат за июнь 2026' })
    expect(await within(expenseTable).findByText('Серверный водоканал')).toBeInTheDocument()
    expect(within(expenseTable).getByText('Петрова Ольга')).toBeInTheDocument()
    expect(within(expenseTable).getAllByText('32 000').length).toBeGreaterThan(0)
    expect(within(expenseTable).getAllByText('47 000').length).toBeGreaterThan(0)
    expect(within(prototype).getByText('12 000')).toBeInTheDocument()
    expect(within(prototype).getByText('4 000')).toBeInTheDocument()
  })

  it('does not show prototype expense rows when expense worksheet is unavailable', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await openSection(user, 'Платежи')
    const prototype = within(await screen.findByRole('region', { name: 'Платежи' })).getByRole('region', { name: 'Форма платежей' })
    await user.type(within(prototype).getByLabelText('Поиск номера гаража или ФИО владельца'), '12')
    await user.click(await within(prototype).findByRole('option', { name: /Гараж\s*12\s*Иванов Иван/ }))
    await user.click(within(prototype).getByRole('tab', { name: 'Выплаты' }))

    expect(await within(prototype).findByText('Серверная форма выплат недоступна')).toBeInTheDocument()
    const expenseTable = within(prototype).getByRole('table', { name: 'Форма выплат за июнь 2026' })
    expect(within(expenseTable).getByText('Начислений и выплат за выбранный месяц пока нет.')).toBeInTheDocument()
    expect(within(expenseTable).queryByText('Электроэнергия')).not.toBeInTheDocument()
    expect(within(expenseTable).queryByText('257 100')).not.toBeInTheDocument()
    expect(within(prototype).getByText('Сумма в банке').closest('div')).toHaveTextContent('0')
  })

  it('moves garage debt to the next month and saves the transfer in form history', async () => {
    const user = userEvent.setup()
    const saveStateMock = vi.mocked(formStatesApi.saveState)
    saveStateMock.mockClear()
    const createDebtTransferMock = vi.fn(async (_token: string, request: CreateDebtTransferRequest) => createAccrual({
      id: 'debt-transfer-accrual-27',
      garageId: request.garageId,
      incomeTypeId: 'income-type-debt-transfer',
      incomeTypeName: 'Перенос задолженности',
      accountingMonth: request.targetMonth,
      amount: request.amount,
      source: 'debt_transfer',
      comment: request.comment ?? null,
    }))
    const garage = createGarage({ id: 'garage-27', number: '27', ownerName: 'Сидорова Анна', peopleCount: 2, floorCount: 1, startingBalance: -1700 })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient({ getGarages: async () => [garage] })} financeClient={createFinanceClient({
      createDebtTransfer: createDebtTransferMock,
      getGarageIncomeWorksheet: async () => createGarageIncomeWorksheet({
        garageId: garage.id,
        garageNumber: garage.number,
        ownerName: garage.ownerName,
        accrualTotal: 4210,
        incomeTotal: 2510,
        debtTotal: 1700,
        closingDebt: 1700,
        rows: [
          {
            accountingMonth: '2026-06-01',
            incomeTypeId: 'income-electricity',
            incomeTypeName: 'Электроэнергия',
            meterKind: 'electricity',
            meterValue: 74,
            meterConsumption: 13,
            accrualAmount: 4210,
            incomeAmount: 2510,
            debt: 1700,
          },
        ],
      }),
    })} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: 'Платежи' }))
    const prototype = within(await screen.findByRole('region', { name: 'Платежи' })).getByRole('region', { name: 'Форма платежей' })
    await user.type(within(prototype).getByLabelText('Поиск номера гаража или ФИО владельца'), '27')
    await user.click(await within(prototype).findByRole('option', { name: /Гараж\s*27\s*Сидорова Анна/ }))

    const transferButton = within(prototype).getByRole('button', { name: 'Перенести задолженность' })
    await user.click(transferButton)
    let transferDialog = await screen.findByRole('dialog', { name: 'Перенести задолженность' })
    await waitFor(() => expect(within(transferDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Перенести задолженность' })).not.toBeInTheDocument())
    expect(createDebtTransferMock).not.toHaveBeenCalled()
    await waitFor(() => expect(transferButton).toHaveFocus())

    await user.click(transferButton)
    transferDialog = await screen.findByRole('dialog', { name: 'Перенести задолженность' })
    expect(within(transferDialog).getByLabelText('Исходный месяц переноса задолженности')).toHaveValue('2026-06')
    expect(within(transferDialog).getByLabelText('Целевой месяц переноса задолженности')).toHaveValue('2026-07')
    expect(within(transferDialog).getByLabelText('Сумма переноса задолженности')).toHaveValue('1700')
    await user.type(within(transferDialog).getByLabelText('Комментарий к переносу задолженности'), 'Проверка переноса')
    await user.click(within(transferDialog).getByRole('button', { name: 'Принять' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Перенести задолженность' })).not.toBeInTheDocument())
    expect(createDebtTransferMock).toHaveBeenCalledWith('token', {
      garageId: 'garage-27',
      sourceMonth: '2026-06-01',
      targetMonth: '2026-07-01',
      amount: 1700,
      comment: 'Проверка переноса',
    })
    expect(transferButton).toHaveFocus()
    expect(within(prototype).getByText('июл.26')).toBeInTheDocument()
    expect(within(prototype).getByText('Перенос задолженности: Электроэнергия')).toBeInTheDocument()
    expect(within(prototype).getByRole('table', { name: 'История платежей гаража' })).toHaveTextContent('Перенос задолженности июн.26 -> июл.26: Проверка переноса')

    await waitFor(() => {
      const savedPayloads = saveStateMock.mock.calls.map((call) => call[2].payload as {
        garageRows?: Array<{ month: string; service: string; debt: number }>
        historyRows?: Array<{ purpose: string }>
      })
      expect(savedPayloads.some((payload) => payload.garageRows?.some((row) => row.month === '2026-07' && row.service === 'Перенос задолженности: Электроэнергия' && row.debt === 1700))).toBe(true)
      expect(savedPayloads.some((payload) => payload.historyRows?.some((row) => row.purpose.includes('Перенос задолженности июн.26 -> июл.26')))).toBe(true)
    })
  })

  it('shows funds management prototype from dashboard tile', async () => {
    const user = userEvent.setup()
    const fundsClient = createFundsClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} fundsClient={fundsClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: /Управление\s+фондами/i }))

    const fundsPanel = await screen.findByRole('region', { name: 'Управление фондами' })
    expect(screen.queryByPlaceholderText('Поиск по гаражу, владельцу или поставщику')).not.toBeInTheDocument()
    expect(within(fundsPanel).getByRole('table', { name: 'Фонды и собранные суммы' })).toBeInTheDocument()
    expect(await within(fundsPanel).findByText('Электроэнергия')).toBeInTheDocument()
    const withdrawElectricityButton = within(fundsPanel).getByRole('button', { name: 'Изъять из фонда Электроэнергия' })
    expect(withdrawElectricityButton).toHaveAttribute('data-tooltip', 'Изъять')
    expect(withdrawElectricityButton).toHaveAttribute('title', 'Изъять из фонда Электроэнергия')
    await user.click(withdrawElectricityButton)
    const withdrawDialog = await screen.findByRole('dialog', { name: 'Изъять из фонда' })
    expect(within(withdrawDialog).getByText('Электроэнергия')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Изъять из фонда' })).not.toBeInTheDocument())
    await waitFor(() => expect(withdrawElectricityButton).toHaveFocus())

    const depositTargetButton = within(fundsPanel).getByRole('button', { name: 'Пополнить фонд Целевые взносы' })
    expect(depositTargetButton).toHaveAttribute('data-tooltip', 'Пополнить')
    await user.click(depositTargetButton)
    const depositDialog = await screen.findByRole('dialog', { name: 'Пополнить фонд' })
    const depositCancelButton = within(depositDialog).getByRole('button', { name: 'Отмена' })
    const depositConfirmButton = within(depositDialog).getByRole('button', { name: 'Подтвердить операцию' })
    const depositCloseButton = within(depositDialog).getByRole('button', { name: 'Закрыть операцию фонда' })
    const depositAmountInput = within(depositDialog).getByLabelText('Сумма операции фонда')
    const depositReasonInput = within(depositDialog).getByLabelText('Причина операции фонда')
    expect(Boolean(depositCancelButton.compareDocumentPosition(depositConfirmButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(depositCancelButton).toHaveFocus())
    await user.keyboard('{Tab}')
    expect(depositConfirmButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(depositCloseButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(depositAmountInput).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(depositReasonInput).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(depositCancelButton).toHaveFocus()
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(depositReasonInput).toHaveFocus()
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(depositAmountInput).toHaveFocus()
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(depositCloseButton).toHaveFocus()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Пополнить фонд' })).not.toBeInTheDocument()
    expect(depositTargetButton).toHaveFocus()
    expect(within(fundsPanel).queryByText(/Пополнение по фонду "Целевые взносы" сохранено и записано в историю изменений\./)).not.toBeInTheDocument()

    await user.click(depositTargetButton)
    const reopenedDepositDialog = await screen.findByRole('dialog', { name: 'Пополнить фонд' })
    expect(within(reopenedDepositDialog).getByText(/Доступно к пополнению:\s*100[\s\u00A0]000,00 руб\./)).toBeInTheDocument()
    await user.click(within(reopenedDepositDialog).getByRole('button', { name: 'Подтвердить операцию' }))
    expect(within(reopenedDepositDialog).getByRole('alert')).toHaveTextContent('Укажите сумму больше нуля.')
    const reopenedAmountInput = within(reopenedDepositDialog).getByLabelText('Сумма операции фонда')
    await user.type(reopenedAmountInput, '100000,01')
    await user.type(within(reopenedDepositDialog).getByLabelText('Причина операции фонда'), 'Сверх лимита')
    await user.click(within(reopenedDepositDialog).getByRole('button', { name: 'Подтвердить операцию' }))
    expect(within(reopenedDepositDialog).getByRole('alert')).toHaveTextContent('Сумма пополнения не может превышать доступную к распределению сумму 100 000,00 руб.')
    await user.clear(reopenedAmountInput)
    await user.type(reopenedAmountInput, '1500')
    await user.clear(within(reopenedDepositDialog).getByLabelText('Причина операции фонда'))
    await user.type(within(reopenedDepositDialog).getByLabelText('Причина операции фонда'), 'Распределение средств')
    expect(within(reopenedDepositDialog).getByText(/1[\s\u00A0]500,00 руб\./)).toBeInTheDocument()
    await user.click(within(reopenedDepositDialog).getByRole('button', { name: 'Подтвердить операцию' }))

    expect(await within(fundsPanel).findByText(/Пополнение по фонду "Целевые взносы" сохранено и записано в историю изменений\./)).toHaveAttribute('role', 'status')
    expect(within(fundsPanel).getAllByText(/1[\s\u00A0]500,00 руб\./).length).toBeGreaterThanOrEqual(1)
    expect(within(fundsPanel).getByLabelText('Сумма к распределению')).toHaveTextContent('98 500,00 руб.')

    const fundOperationsTable = within(fundsPanel).getByRole('table', { name: 'Операции фондов' })
    expect(fundOperationsTable).toHaveTextContent('Целевые взносы')
    expect(fundOperationsTable).toHaveTextContent('Пополнение')
    expect(fundOperationsTable).toHaveTextContent('Активна')

    const editFundOperationButton = within(fundOperationsTable).getByRole('button', { name: 'Изменить операцию фонда Целевые взносы' })
    expect(editFundOperationButton).toHaveAttribute('data-tooltip', 'Изменить')
    await user.click(editFundOperationButton)
    let editFundOperationDialog = await screen.findByRole('dialog', { name: 'Изменить операцию фонда?' })
    let editCancelButton = within(editFundOperationDialog).getByRole('button', { name: 'Отмена' })
    let editSaveButton = within(editFundOperationDialog).getByRole('button', { name: 'Сохранить изменения' })
    expect(Boolean(editCancelButton.compareDocumentPosition(editSaveButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(editCancelButton).toHaveFocus())
    await user.click(editSaveButton)
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Изменить операцию фонда?' })).not.toBeInTheDocument())
    expect(screen.queryByRole('dialog', { name: 'Подтвердить изменение операции фонда?' })).not.toBeInTheDocument()
    expect(within(fundsPanel).queryByText(/Операция фонда "Целевые взносы" изменена и записана в историю изменений\./)).not.toBeInTheDocument()

    await user.click(editFundOperationButton)
    editFundOperationDialog = await screen.findByRole('dialog', { name: 'Изменить операцию фонда?' })
    editCancelButton = within(editFundOperationDialog).getByRole('button', { name: 'Отмена' })
    editSaveButton = within(editFundOperationDialog).getByRole('button', { name: 'Сохранить изменения' })
    await waitFor(() => expect(editCancelButton).toHaveFocus())
    const editAmountInput = within(editFundOperationDialog).getByLabelText('Новая сумма операции фонда')
    await user.clear(editAmountInput)
    await user.type(editAmountInput, '1750')
    await user.clear(within(editFundOperationDialog).getByLabelText('Новое основание операции фонда'))
    await user.type(within(editFundOperationDialog).getByLabelText('Новое основание операции фонда'), 'Уточненное распределение')
    expect(within(editFundOperationDialog).getByText(/1[\s\u00A0]750,00 руб\./)).toBeInTheDocument()
    await user.click(editSaveButton)
    let editConfirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение операции фонда?' })
    const editChangeList = within(editConfirmationDialog).getByRole('list', { name: 'Изменяемые поля операции фонда' })
    expect(within(editChangeList).getByText('Сумма')).toBeInTheDocument()
    expect(within(editChangeList).getByText('1 500,00')).toBeInTheDocument()
    expect(within(editChangeList).getByText('1 750,00')).toBeInTheDocument()
    expect(within(editChangeList).getByText('Основание')).toBeInTheDocument()
    expect(within(editChangeList).getByText('Распределение средств')).toBeInTheDocument()
    expect(within(editChangeList).getByText('Уточненное распределение')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердить изменение операции фонда?' })).not.toBeInTheDocument())
    expect(screen.getByRole('dialog', { name: 'Изменить операцию фонда?' })).toBeInTheDocument()
    expect(within(fundsPanel).queryByText(/Операция фонда "Целевые взносы" изменена и записана в историю изменений\./)).not.toBeInTheDocument()

    await user.click(editSaveButton)
    editConfirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение операции фонда?' })
    await user.click(within(editConfirmationDialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(fundsPanel).findByText(/Операция фонда "Целевые взносы" изменена и записана в историю изменений\./)).toHaveAttribute('role', 'status')
    expect(within(fundOperationsTable).getAllByText(/1[\s\u00A0]750,00 руб\./).length).toBeGreaterThanOrEqual(1)

    const cancelFundOperationButton = within(fundOperationsTable).getByRole('button', { name: 'Отменить операцию фонда Целевые взносы' })
    expect(cancelFundOperationButton).toHaveAttribute('data-tooltip', 'Отменить')
    await user.click(cancelFundOperationButton)
    const cancelFundOperationDialog = await screen.findByRole('dialog', { name: 'Отменить операцию фонда?' })
    await waitFor(() => expect(within(cancelFundOperationDialog).getByLabelText('Причина отмены операции фонда')).toHaveFocus())
    await user.click(within(cancelFundOperationDialog).getByRole('button', { name: 'Отменить операцию' }))
    expect(within(cancelFundOperationDialog).getByRole('alert')).toHaveTextContent('Укажите причину отмены операции фонда.')
    await user.type(within(cancelFundOperationDialog).getByLabelText('Причина отмены операции фонда'), 'Ошибочное распределение')
    await user.click(within(cancelFundOperationDialog).getByRole('button', { name: 'Отменить операцию' }))
    expect(await within(fundsPanel).findByText('Отменена')).toBeInTheDocument()
    expect(within(fundsPanel).getByText('Операция отменена и записана в историю изменений.')).toHaveAttribute('role', 'status')

    const restoreFundOperationButton = within(fundOperationsTable).getByRole('button', { name: 'Вернуть операцию фонда Целевые взносы' })
    expect(restoreFundOperationButton).toHaveAttribute('data-tooltip', 'Вернуть')
    await user.click(restoreFundOperationButton)
    const restoreFundOperationDialog = await screen.findByRole('dialog', { name: 'Вернуть операцию фонда?' })
    const restoreCancelButton = within(restoreFundOperationDialog).getByRole('button', { name: 'Отмена' })
    const restoreConfirmButton = within(restoreFundOperationDialog).getByRole('button', { name: 'Вернуть операцию' })
    expect(Boolean(restoreCancelButton.compareDocumentPosition(restoreConfirmButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(restoreCancelButton).toHaveFocus())
    await user.click(restoreConfirmButton)
    expect(await within(fundsPanel).findByText('Операция восстановлена и записана в историю изменений.')).toHaveAttribute('role', 'status')
    expect(within(fundOperationsTable).getByText('Активна')).toBeInTheDocument()

    const reverseFundOperationButton = within(fundOperationsTable).getByRole('button', { name: 'Создать обратную операцию фонда Целевые взносы' })
    expect(reverseFundOperationButton).toHaveAttribute('data-tooltip', 'Обратная')
    await user.click(reverseFundOperationButton)
    const reverseFundOperationDialog = await screen.findByRole('dialog', { name: 'Создать обратную операцию фонда?' })
    const reverseCancelButton = within(reverseFundOperationDialog).getByRole('button', { name: 'Отмена' })
    const reverseConfirmButton = within(reverseFundOperationDialog).getByRole('button', { name: 'Создать обратную операцию' })
    expect(Boolean(reverseCancelButton.compareDocumentPosition(reverseConfirmButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    await waitFor(() => expect(within(reverseFundOperationDialog).getByLabelText('Причина обратной операции фонда')).toHaveFocus())
    await user.click(reverseConfirmButton)
    expect(within(reverseFundOperationDialog).getByRole('alert')).toHaveTextContent('Укажите причину обратной операции фонда.')
    await user.type(within(reverseFundOperationDialog).getByLabelText('Причина обратной операции фонда'), 'Сторнирование распределения')
    await user.click(reverseConfirmButton)
    expect(await within(fundsPanel).findByText(/Обратная операция фонда "Целевые взносы" создана и записана в историю изменений\./)).toHaveAttribute('role', 'status')
    expect(within(fundOperationsTable).getByText('Изъятие')).toBeInTheDocument()
  })

  it('keeps empty backend funds empty instead of showing prototype fund rows', async () => {
    const user = userEvent.setup()
    const getFunds = vi.fn(async () => [])
    const fundsClient = createFundsClient({ getFunds })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} fundsClient={fundsClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: /Управление\s+фондами/i }))

    const fundsPanel = await screen.findByRole('region', { name: 'Управление фондами' })
    await waitFor(() => expect(getFunds).toHaveBeenCalledTimes(1))

    expect(await within(fundsPanel).findByText('Фонды пока не настроены.')).toBeInTheDocument()
    expect(within(fundsPanel).queryByText('Электроэнергия')).not.toBeInTheDocument()
    expect(within(fundsPanel).queryByRole('button', { name: 'Пополнить фонд Электроэнергия' })).not.toBeInTheDocument()
    expect(within(fundsPanel).getByLabelText('Сумма к распределению')).toHaveTextContent('—')
  })

  it('lets administrator expand the sidebar and remembers the choice', async () => {
    const user = userEvent.setup()
    const { unmount } = render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    expect(await screen.findByRole('button', { name: 'Развернуть панель' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Развернуть панель' }))

    expect(screen.getByRole('button', { name: 'Свернуть панель' })).toBeInTheDocument()
    expect(window.localStorage.getItem('garagebalance.sidebar.expanded')).toBe('true')

    unmount()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    expect(await screen.findByRole('button', { name: 'Свернуть панель' })).toBeInTheDocument()
  })

  it('keeps shell navigation titles current state and icon-only actions in the rendered DOM', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const mainNavigation = await screen.findByRole('navigation', { name: 'Основные разделы' })
    const dashboardNavButton = within(mainNavigation).getByRole('button', { name: 'Главное меню' })
    const tariffsNavButton = within(mainNavigation).getByRole('button', { name: 'Тарифы и сборы' })
    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    const tariffsTile = within(dashboardTiles).getByRole('button', { name: /Тарифы\s+и\s+сборы/i })

    expect(screen.getByRole('button', { name: 'Развернуть панель' })).toHaveAttribute('title', 'Развернуть панель')
    expect(dashboardNavButton).toHaveAttribute('aria-current', 'page')
    expect(dashboardNavButton).toHaveAttribute('title', 'Главное меню')
    expect(tariffsNavButton).toHaveAttribute('title', 'Тарифы и сборы')
    expect(tariffsTile).toHaveAttribute('title', 'Тарифы и сборы')
    expect(screen.getByRole('button', { name: 'Уведомления' })).toHaveAttribute('title', 'Уведомления')
    expect(screen.getByRole('button', { name: 'Выйти' })).toHaveAttribute('title', 'Выйти')

    await user.click(tariffsTile)

    expect(await screen.findByRole('region', { name: 'Тарифы и сборы' })).toBeInTheDocument()
    expect(tariffsNavButton).toHaveAttribute('aria-current', 'page')
    expect(dashboardNavButton).not.toHaveAttribute('aria-current')
    expect(screen.getByRole('button', { name: 'Назад к выбору раздела' })).toHaveAttribute('title', 'Назад к выбору раздела')
  })

  it('switches workspace sections without stacking panels', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    expect(await screen.findByRole('region', { name: 'Панель' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Пользователи' })).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Справочники' })).not.toBeInTheDocument()

    await openSection(user, 'Справочники')
    expect(screen.getByRole('button', { name: 'Справочники' })).toHaveAttribute('aria-current', 'page')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    expect(dictionaryPanel).toBeInTheDocument()
    expect(screen.queryByText('Поиск по гаражу, владельцу или поставщику')).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Панель' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Тарифы' })).not.toBeInTheDocument()
    await openDictionarySubgroup(user, dictionaryPanel, 'Тарифы')
    expect(within(dictionaryPanel).getByRole('button', { name: 'Подгруппа: Тарифы' })).toHaveAttribute('aria-current', 'page')
    expect(within(dictionaryPanel).getByRole('table', { name: 'Таблица: Тарифы' })).toBeInTheDocument()

    await openSection(user, 'Платежи')
    expect(screen.getByRole('button', { name: 'Платежи' })).toHaveAttribute('aria-current', 'page')
    expect(await screen.findByRole('region', { name: 'Платежи' })).toBeInTheDocument()
    expect(screen.queryByText('Поиск по гаражу, владельцу или поставщику')).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Справочники' })).not.toBeInTheDocument()

    await openSection(user, 'Отчеты')
    expect(screen.getByRole('button', { name: 'Отчеты' })).toHaveAttribute('aria-current', 'page')
    expect(await screen.findByRole('region', { name: 'Отчеты' })).toBeInTheDocument()
    expect(screen.queryByText('Поиск по гаражу, владельцу или поставщику')).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Платежи' })).not.toBeInTheDocument()

    await openSection(user, 'Импорт')
    expect(screen.getByRole('button', { name: 'Импорт' })).toHaveAttribute('aria-current', 'page')
    expect(await screen.findByRole('region', { name: 'Импорт Access' })).toBeInTheDocument()
    expect(screen.queryByText('Поиск по гаражу, владельцу или поставщику')).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Отчеты' })).not.toBeInTheDocument()

    await openSection(user, 'Что нового')
    expect(screen.getByRole('button', { name: 'Что нового' })).toHaveAttribute('aria-current', 'page')
    expect(await screen.findByRole('region', { name: 'Что нового' })).toBeInTheDocument()
    expect(screen.queryByText('Поиск по гаражу, владельцу или поставщику')).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Импорт Access' })).not.toBeInTheDocument()
  })

  it('shows server-paginated user counter', async () => {
    const user = userEvent.setup()
    let requestedPage: { offset: number; limit: number } | undefined
    const users = Array.from({ length: 9 }, (_item, index) =>
      createManagedUser({
        id: `user-${index + 1}`,
        email: `user-${index + 1}@example.com`,
        displayName: `Сотрудник ${index + 1}`,
      }),
    )
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient({
      getUsersPage: async (_token, _search, offset = 0, limit = 25) => {
        requestedPage = { offset, limit }
        return {
          items: users.slice(offset, offset + limit),
          totalCount: users.length,
          offset,
          limit,
        }
      },
    })} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Пользователи')
    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })
    const usersTable = within(usersPanel).getByRole('table', { name: 'Список пользователей' })

    const visibleUserCounter = await within(usersPanel).findByText('Показано 1-9 из 9')
    expect(visibleUserCounter).toHaveAttribute('role', 'status')
    expect(visibleUserCounter).toHaveAttribute('aria-live', 'polite')
    expect(within(usersTable).getByText('Сотрудник 9')).toBeInTheDocument()
    expect(requestedPage).toEqual({ offset: 0, limit: 25 })
  })

  it('announces empty user and role lists for administrators', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient({
      getUsersPage: async (_token, _search, offset = 0, limit = 25) => ({ items: [], totalCount: 0, offset, limit }),
      getRoles: async () => [],
    })} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Пользователи')
    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })
    const usersTable = within(usersPanel).getByRole('table', { name: 'Список пользователей' })
    const roleMatrix = within(usersPanel).getByRole('region', { name: 'Матрица ролей' })
    const roleTable = within(roleMatrix).getByRole('table', { name: 'Матрица ролей и прав' })

    const emptyUsersState = await within(usersTable).findByText('Пользователей пока нет')
    const emptyRolesState = within(roleTable).getByText('Роли пока не загружены')
    expect(emptyUsersState).toHaveAttribute('role', 'status')
    expect(emptyUsersState).toHaveAttribute('aria-live', 'polite')
    expect(emptyRolesState).toHaveAttribute('role', 'status')
    expect(emptyRolesState).toHaveAttribute('aria-live', 'polite')
  })

  it('changes current user password from the workspace', async () => {
    const user = userEvent.setup()
    let passwordRequest: { token: string; currentPassword: string; newPassword: string } | null = null
    const authClient = createAuthClient({
      changeOwnPassword: async (accessToken, request) => {
        passwordRequest = {
          token: accessToken,
          currentPassword: request.currentPassword,
          newPassword: request.newPassword,
        }
        return {
          id: 'user-1',
          email: 'admin@example.com',
          displayName: 'Администратор ГСК',
          roles: ['administrator'],
          permissions: ['users.manage', 'dictionaries.read', 'dictionaries.write', 'payments.read', 'payments.write', 'reports.read', 'import.run'],
        }
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} integrationClient={createIntegrationClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Настройки')
    const passwordPanel = await screen.findByRole('region', { name: 'Безопасность аккаунта' })

    await user.type(within(passwordPanel).getByLabelText('Текущий пароль'), 'StrongPass123')
    await user.type(within(passwordPanel).getByLabelText('Новый пароль'), 'NewStrongPass123')
    await user.type(within(passwordPanel).getByLabelText('Повтор нового пароля'), 'NewStrongPass123')
    await user.click(within(passwordPanel).getByRole('button', { name: 'Изменить пароль' }))

    expect(passwordRequest).toBeNull()
    const confirmation = await screen.findByRole('dialog', { name: 'Подтвердить смену пароля?' })
    expect(within(confirmation).getByText('После подтверждения пароль будет изменен, а действие появится в истории изменений как смена учетных данных без раскрытия самого пароля.')).toBeInTheDocument()
    const confirmationChanges = within(confirmation).getByRole('list', { name: 'Изменяемые поля настройки' })
    expect(within(confirmationChanges).getByText('Пароль')).toBeInTheDocument()
    expect(within(confirmationChanges).getByText('Без изменения')).toBeInTheDocument()
    expect(within(confirmationChanges).getByText('изменено')).toBeInTheDocument()
    expect(confirmation).not.toHaveTextContent('NewStrongPass123')
    expect(within(confirmation).getByRole('button', { name: 'Отмена' })).toHaveFocus()
    await user.click(within(confirmation).getByRole('button', { name: 'Подтвердить смену пароля' }))

    expect(passwordRequest).toEqual({
      token: 'token',
      currentPassword: 'StrongPass123',
      newPassword: 'NewStrongPass123',
    })
    expect(await within(passwordPanel).findByText('Пароль изменен. Используйте новый пароль при следующем входе.')).toHaveAttribute('role', 'status')
    expect(within(passwordPanel).getByLabelText('Текущий пароль')).toHaveValue('')
    expect(within(passwordPanel).getByLabelText('Новый пароль')).toHaveValue('')
    expect(within(passwordPanel).getByLabelText('Повтор нового пароля')).toHaveValue('')
  })

  it('closes password change confirmation with Escape without changing password', async () => {
    const user = userEvent.setup()
    let changeCalled = false
    const authClient = createAuthClient({
      changeOwnPassword: async () => {
        changeCalled = true
        throw new Error('Смена пароля не должна вызываться без подтверждения.')
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} integrationClient={createIntegrationClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Настройки')
    const passwordPanel = await screen.findByRole('region', { name: 'Безопасность аккаунта' })

    await user.type(within(passwordPanel).getByLabelText('Текущий пароль'), 'StrongPass123')
    await user.type(within(passwordPanel).getByLabelText('Новый пароль'), 'NewStrongPass123')
    await user.type(within(passwordPanel).getByLabelText('Повтор нового пароля'), 'NewStrongPass123')
    const submitButton = within(passwordPanel).getByRole('button', { name: 'Изменить пароль' })
    await user.click(submitButton)

    expect(await screen.findByRole('dialog', { name: 'Подтвердить смену пароля?' })).toBeInTheDocument()
    await user.keyboard('{Escape}')

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердить смену пароля?' })).not.toBeInTheDocument())
    expect(changeCalled).toBe(false)
    expect(submitButton).toHaveFocus()
    expect(within(passwordPanel).getByLabelText('Новый пароль')).toHaveValue('NewStrongPass123')
  })

  it('cancels password change confirmation without changing password', async () => {
    const user = userEvent.setup()
    let changeCalled = false
    const authClient = createAuthClient({
      changeOwnPassword: async () => {
        changeCalled = true
        throw new Error('Смена пароля не должна вызываться после отмены подтверждения.')
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} integrationClient={createIntegrationClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Настройки')
    const passwordPanel = await screen.findByRole('region', { name: 'Безопасность аккаунта' })

    await user.type(within(passwordPanel).getByLabelText('Текущий пароль'), 'StrongPass123')
    await user.type(within(passwordPanel).getByLabelText('Новый пароль'), 'NewStrongPass123')
    await user.type(within(passwordPanel).getByLabelText('Повтор нового пароля'), 'NewStrongPass123')
    const submitButton = within(passwordPanel).getByRole('button', { name: 'Изменить пароль' })
    await user.click(submitButton)

    const confirmation = await screen.findByRole('dialog', { name: 'Подтвердить смену пароля?' })
    await user.click(within(confirmation).getByRole('button', { name: 'Отмена' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердить смену пароля?' })).not.toBeInTheDocument())
    expect(changeCalled).toBe(false)
    expect(submitButton).toHaveFocus()
    expect(within(passwordPanel).getByLabelText('Новый пароль')).toHaveValue('NewStrongPass123')
  })

  it('does not call password API when repeated password differs', async () => {
    const user = userEvent.setup()
    let changeCalled = false
    const authClient = createAuthClient({
      changeOwnPassword: async () => {
        changeCalled = true
        throw new Error('Смена пароля не должна вызываться при несовпадающем повторе.')
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} integrationClient={createIntegrationClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Настройки')
    const passwordPanel = await screen.findByRole('region', { name: 'Безопасность аккаунта' })

    await user.type(within(passwordPanel).getByLabelText('Текущий пароль'), 'StrongPass123')
    await user.type(within(passwordPanel).getByLabelText('Новый пароль'), 'NewStrongPass123')
    await user.type(within(passwordPanel).getByLabelText('Повтор нового пароля'), 'AnotherStrongPass123')
    await user.click(within(passwordPanel).getByRole('button', { name: 'Изменить пароль' }))

    expect(await within(passwordPanel).findByText('Проверьте смену пароля')).toBeInTheDocument()
    expect(await within(passwordPanel).findByText('Новый пароль и повтор пароля не совпадают.')).toBeInTheDocument()
    expect(within(passwordPanel).getByRole('alert')).toBeInTheDocument()
    expect(changeCalled).toBe(false)
  })

  it('does not call password API when new password violates policy', async () => {
    const user = userEvent.setup()
    let changeCalled = false
    const authClient = createAuthClient({
      changeOwnPassword: async () => {
        changeCalled = true
        throw new Error('Смена пароля не должна вызываться при слабом новом пароле.')
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Настройки')
    const passwordPanel = await screen.findByRole('region', { name: 'Безопасность аккаунта' })

    await user.type(within(passwordPanel).getByLabelText('Текущий пароль'), 'StrongPass123')
    await user.type(within(passwordPanel).getByLabelText('Новый пароль'), 'Password')
    await user.type(within(passwordPanel).getByLabelText('Повтор нового пароля'), 'Password')
    await user.click(within(passwordPanel).getByRole('button', { name: 'Изменить пароль' }))

    expect(await within(passwordPanel).findByText('Проверьте смену пароля')).toBeInTheDocument()
    expect(within(passwordPanel).getByText('Добавьте хотя бы одну цифру в пароль.')).toBeInTheDocument()
    expect(within(passwordPanel).getByRole('alert')).toBeInTheDocument()
    expect(changeCalled).toBe(false)
  })

  it('shows safe 1C Fresh integration status in settings', async () => {
    const user = userEvent.setup()
    let tokenSeen: string | null = null
    const integrationClient = createIntegrationClient({
      getOneCFreshStatus: async (accessToken) => {
        tokenSeen = accessToken
        return createOneCFreshStatus({
          isConfigured: true,
          status: 'prepared',
          statusMessage: 'Токен 1C Fresh сохранен в защищенном хранилище. Запуск синхронизации будет доступен после подключения адаптера 1C Fresh.',
          configuredSettings: ['RefreshToken'],
          lastProtectedSettingUpdatedAtUtc: '2026-06-30T02:30:00Z',
        })
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} integrationClient={integrationClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Настройки')

    const integrationPanel = await screen.findByRole('region', { name: 'Интеграция 1C Fresh' })
    const statusStrip = within(integrationPanel).getByLabelText('Статус интеграции 1C Fresh')
    expect(within(statusStrip).getByText('Подготовлено')).toBeInTheDocument()
    expect(within(statusStrip).getByText('Ожидает адаптер')).toBeInTheDocument()
    expect(within(statusStrip).getByText('1 / 1')).toBeInTheDocument()
    expect(within(integrationPanel).getByText('Токен 1C Fresh сохранен в защищенном хранилище. Запуск синхронизации будет доступен после подключения адаптера 1C Fresh.')).toHaveAttribute('role', 'status')
    expect(integrationPanel).not.toHaveTextContent('one-c-refresh-token')
    expect(tokenSeen).toBe('token')
  })

  it('starts 1C Fresh synchronization from settings with confirmation', async () => {
    const user = userEvent.setup()
    const startOneCFreshSync = vi.fn(async (_accessToken: string, request: OneCFreshSyncRequest) => createOneCFreshSync({
      statusMessage: `Запуск 1C зарегистрирован: ${request.comment ?? ''}`,
    }))
    const retryOneCFreshSync = vi.fn(async (_accessToken: string, request: OneCFreshSyncRequest) => createOneCFreshSync({
      statusMessage: `Повтор 1C зарегистрирован: ${request.comment ?? ''}`,
    }))
    const integrationClient = createIntegrationClient({
      getOneCFreshStatus: async () => createOneCFreshStatus({
        isConfigured: true,
        status: 'prepared',
        configuredSettings: ['RefreshToken'],
      }),
      startOneCFreshSync,
      retryOneCFreshSync,
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} integrationClient={integrationClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Настройки')

    const integrationPanel = await screen.findByRole('region', { name: 'Интеграция 1C Fresh' })
    const syncButton = await within(integrationPanel).findByRole('button', { name: 'Запустить синхронизацию' })
    await user.click(syncButton)
    let syncDialog = await screen.findByRole('dialog', { name: 'Запустить синхронизацию 1C Fresh?' })
    await waitFor(() => expect(within(syncDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Запустить синхронизацию 1C Fresh?' })).not.toBeInTheDocument())
    await waitFor(() => expect(syncButton).toHaveFocus())
    expect(startOneCFreshSync).not.toHaveBeenCalled()

    await user.click(syncButton)
    syncDialog = await screen.findByRole('dialog', { name: 'Запустить синхронизацию 1C Fresh?' })
    await user.type(within(syncDialog).getByLabelText('Комментарий к запуску синхронизации 1C Fresh'), 'Проверка расписания')
    await user.click(within(syncDialog).getByRole('button', { name: 'Запустить' }))
    await waitFor(() => expect(startOneCFreshSync).toHaveBeenCalledWith('token', { comment: 'Проверка расписания' }))
    expect(await within(integrationPanel).findByText('Запуск 1C зарегистрирован: Проверка расписания')).toHaveAttribute('role', 'status')

    const retryButton = within(integrationPanel).getByRole('button', { name: 'Повторить запрос' })
    await user.click(retryButton)
    let retryDialog = await screen.findByRole('dialog', { name: 'Повторить запрос синхронизации 1C Fresh?' })
    await waitFor(() => expect(within(retryDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Повторить запрос синхронизации 1C Fresh?' })).not.toBeInTheDocument())
    expect(retryOneCFreshSync).not.toHaveBeenCalled()
    await waitFor(() => expect(retryButton).toHaveFocus())

    await user.click(retryButton)
    retryDialog = await screen.findByRole('dialog', { name: 'Повторить запрос синхронизации 1C Fresh?' })
    await user.type(within(retryDialog).getByLabelText('Комментарий к повтору синхронизации 1C Fresh'), 'Повтор после ошибки адаптера')
    await user.click(within(retryDialog).getByRole('button', { name: 'Повторить' }))
    await waitFor(() => expect(retryOneCFreshSync).toHaveBeenCalledWith('token', { comment: 'Повтор после ошибки адаптера' }))
    expect(await within(integrationPanel).findByText('Повтор 1C зарегистрирован: Повтор после ошибки адаптера')).toHaveAttribute('role', 'status')
  })

  it('does not request 1C Fresh status without import permission', async () => {
    const user = userEvent.setup()
    const authWithoutImport = createAuthResponse({
      user: {
        permissions: ['users.manage'],
      },
    })
    let integrationCalled = false
    const integrationClient = createIntegrationClient({
      getOneCFreshStatus: async () => {
        integrationCalled = true
        return createOneCFreshStatus()
      },
    })
    render(<App authClient={createAuthClient({ login: async () => authWithoutImport })} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} integrationClient={integrationClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Настройки')

    expect(await screen.findByRole('region', { name: 'Безопасность аккаунта' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Интеграция 1C Fresh' })).not.toBeInTheDocument()
    expect(integrationCalled).toBe(false)
  })

  it('shows safe receipt printing status in settings', async () => {
    const user = userEvent.setup()
    let tokenSeen: string | null = null
    const integrationClient = createIntegrationClient({
      getReceiptPrintingStatus: async (accessToken) => {
        tokenSeen = accessToken
        return createReceiptPrintingStatus({
          isConfigured: true,
          status: 'prepared',
          statusMessage: 'Защищенные настройки печати сохранены. Печать, отмена и повторная печать станут доступны после подключения адаптера фискального оборудования.',
          configuredSettings: ['DeviceConnection', 'ReceiptTemplate'],
          plannedActions: ['Печать квитанции', 'Отмена печати', 'Повторная печать'],
          lastProtectedSettingUpdatedAtUtc: '2026-06-30T03:10:00Z',
        })
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} integrationClient={integrationClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Настройки')

    const receiptPanel = await screen.findByRole('region', { name: 'Печать чеков и квитанций' })
    const statusStrip = within(receiptPanel).getByLabelText('Статус печати чеков и квитанций')
    expect(within(statusStrip).getByText('Подготовлено')).toBeInTheDocument()
    expect(within(statusStrip).getByText('Ожидает адаптер')).toBeInTheDocument()
    expect(within(statusStrip).getByText('2 / 2')).toBeInTheDocument()
    expect(within(receiptPanel).getByText('Защищенные настройки печати сохранены. Печать, отмена и повторная печать станут доступны после подключения адаптера фискального оборудования.')).toHaveAttribute('role', 'status')
    expect(within(receiptPanel).getByText('Будущие действия: Печать квитанции, Отмена печати, Повторная печать.')).toBeInTheDocument()
    expect(receiptPanel).not.toHaveTextContent('fiscal-device-connection-string')
    expect(tokenSeen).toBe('token')
  })

  it('does not request receipt printing status without payment write permission', async () => {
    const user = userEvent.setup()
    const authWithoutPaymentWrite = createAuthResponse({
      user: {
        permissions: ['users.manage', 'dictionaries.read', 'payments.read'],
      },
    })
    let receiptPrintingCalled = false
    const integrationClient = createIntegrationClient({
      getReceiptPrintingStatus: async () => {
        receiptPrintingCalled = true
        return createReceiptPrintingStatus()
      },
    })
    render(<App authClient={createAuthClient({ login: async () => authWithoutPaymentWrite })} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} integrationClient={integrationClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Настройки')

    expect(await screen.findByRole('region', { name: 'Безопасность аккаунта' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Печать чеков и квитанций' })).not.toBeInTheDocument()
    expect(receiptPrintingCalled).toBe(false)
  })

  it('adds managed user from protected workspace', async () => {
    const user = userEvent.setup()
    const userClient = createStatefulUserClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={userClient} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Пользователи')
    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })
    const addUserButton = within(usersPanel).getByRole('button', { name: 'Добавить' })

    await user.click(addUserButton)
    const dialog = await screen.findByRole('dialog', { name: 'Новый пользователь' })
    expect(within(dialog).getByText('Email')).toBeInTheDocument()
    expect(within(dialog).getByText('Имя сотрудника')).toBeInTheDocument()
    expect(within(dialog).getByText('Роль')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Email пользователя'), 'operator@example.com')
    await user.type(within(dialog).getByLabelText('Имя пользователя'), 'Оператор')
    await user.type(within(dialog).getByLabelText('Пароль пользователя'), 'StrongPass123')
    await user.selectOptions(within(dialog).getByLabelText('Роль пользователя'), 'operator')
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))

    expect((await within(usersPanel).findAllByText('Оператор')).length).toBeGreaterThan(0)
    expect(within(usersPanel).getByText('operator@example.com')).toBeInTheDocument()
    expect(within(usersPanel).getByText('Активен')).toBeInTheDocument()
    expect(await screen.findByText('Пользователь добавлен.')).toHaveAttribute('role', 'status')
    await waitFor(() => expect(addUserButton).toHaveFocus())
  })

  it('does not call user API when managed user password violates policy', async () => {
    const user = userEvent.setup()
    let createCalled = false
    const statefulUserClient = createStatefulUserClient()
    const userClient: UserManagementClient = {
      ...statefulUserClient,
      createUser: async (...args) => {
        createCalled = true
        return statefulUserClient.createUser(...args)
      },
    }
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={userClient} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Пользователи')
    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })

    await user.click(within(usersPanel).getByRole('button', { name: 'Добавить' }))
    const dialog = await screen.findByRole('dialog', { name: 'Новый пользователь' })
    await user.type(within(dialog).getByLabelText('Email пользователя'), 'operator@example.com')
    await user.type(within(dialog).getByLabelText('Имя пользователя'), 'Оператор')
    await user.type(within(dialog).getByLabelText('Пароль пользователя'), 'Password')
    await user.selectOptions(within(dialog).getByLabelText('Роль пользователя'), 'operator')
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(dialog).findByText('Проверьте нового пользователя')).toBeInTheDocument()
    expect(within(dialog).getByText('Добавьте хотя бы одну цифру в пароль.')).toBeInTheDocument()
    expect(within(dialog).getByRole('alert')).toBeInTheDocument()
    expect(createCalled).toBe(false)
    expect(within(usersPanel).queryByText('operator@example.com')).not.toBeInTheDocument()
  })

  it('opens user edit and delete operations from context menu modals', async () => {
    const user = userEvent.setup()
    const statefulUserClient = createStatefulUserClient()
    let deactivationReason: string | null = null
    let updateCalls = 0
    const userClient: UserManagementClient = {
      ...statefulUserClient,
      updateUser: async (...args) => {
        updateCalls += 1
        const request = args[2]
        if (!request.isActive) {
          deactivationReason = request.deactivationReason ?? null
        }

        return statefulUserClient.updateUser(...args)
      },
    }
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={userClient} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Пользователи')
    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })

    await user.click(within(usersPanel).getByRole('button', { name: 'Добавить' }))
    const createDialog = await screen.findByRole('dialog', { name: 'Новый пользователь' })
    await user.type(within(createDialog).getByLabelText('Email пользователя'), 'operator@example.com')
    await user.type(within(createDialog).getByLabelText('Имя пользователя'), 'Оператор')
    await user.type(within(createDialog).getByLabelText('Пароль пользователя'), 'StrongPass123')
    await user.selectOptions(within(createDialog).getByLabelText('Роль пользователя'), 'operator')
    await user.click(within(createDialog).getByRole('button', { name: 'Сохранить' }))

    const row = await within(usersPanel).findByText('operator@example.com')
    fireEvent.contextMenu(row.closest('tr')!)
    await user.click(await screen.findByRole('menuitem', { name: 'Изменить' }))
    const noChangeDialog = await screen.findByRole('dialog', { name: 'Изменить пользователя' })
    await user.click(within(noChangeDialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Изменить пользователя' })).not.toBeInTheDocument()
    })
    expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения пользователя' })).not.toBeInTheDocument()
    expect(updateCalls).toBe(0)

    fireEvent.contextMenu(within(usersPanel).getByText('operator@example.com').closest('tr')!)
    await user.click(await screen.findByRole('menuitem', { name: 'Изменить' }))
    const editDialog = await screen.findByRole('dialog', { name: 'Изменить пользователя' })
    await user.clear(within(editDialog).getByLabelText('Имя пользователя'))
    await user.type(within(editDialog).getByLabelText('Имя пользователя'), 'Старший оператор')
    const editSaveButton = within(editDialog).getByRole('button', { name: 'Сохранить' })
    await user.click(editSaveButton)

    const saveConfirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения пользователя' })
    expect(within(saveConfirmationDialog).getByText('Имя')).toBeInTheDocument()
    const saveConfirmationChanges = within(saveConfirmationDialog).getByRole('list', { name: 'Изменяемые поля пользователя' })
    expect(within(saveConfirmationChanges).getByText('Оператор')).toBeInTheDocument()
    expect(within(saveConfirmationChanges).getByText('Старший оператор')).toBeInTheDocument()
    expect(updateCalls).toBe(0)
    const saveConfirmationCancelButton = within(saveConfirmationDialog).getByRole('button', { name: 'Отмена' })
    const saveConfirmationConfirmButton = within(saveConfirmationDialog).getByRole('button', { name: 'Сохранить изменения' })
    const saveConfirmationCloseButton = within(saveConfirmationDialog).getByRole('button', { name: 'Отменить подтверждение изменений пользователя' })
    await waitFor(() => expect(saveConfirmationCancelButton).toHaveFocus())
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(saveConfirmationCloseButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(saveConfirmationCancelButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(saveConfirmationConfirmButton).toHaveFocus()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения пользователя' })).not.toBeInTheDocument())
    await waitFor(() => expect(editSaveButton).toHaveFocus())
    expect(updateCalls).toBe(0)

    await user.click(editSaveButton)
    const reopenedSaveConfirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения пользователя' })
    await user.click(within(reopenedSaveConfirmationDialog).getByRole('button', { name: 'Сохранить изменения' }))

    expect(await within(usersPanel).findByText('Старший оператор')).toBeInTheDocument()
    expect(updateCalls).toBe(1)
    expect(await screen.findByText('Пользователь изменен.')).toHaveAttribute('role', 'status')

    fireEvent.contextMenu(within(usersPanel).getByText('operator@example.com').closest('tr')!)
    await user.click(await screen.findByRole('menuitem', { name: 'Удалить' }))
    const deleteDialog = await screen.findByRole('dialog', { name: 'Удалить пользователя' })
    const deleteButton = within(deleteDialog).getByRole('button', { name: 'Удалить' })
    const deleteCloseButton = within(deleteDialog).getByRole('button', { name: 'Закрыть подтверждение удаления' })
    const deleteReasonInput = within(deleteDialog).getByLabelText('Причина отключения пользователя')
    const deleteCancelButton = within(deleteDialog).getByRole('button', { name: 'Отмена' })
    expect(deleteButton).toBeDisabled()
    await waitFor(() => expect(deleteCancelButton).toHaveFocus())
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(deleteReasonInput).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(deleteCancelButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(deleteCloseButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(deleteReasonInput).toHaveFocus()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Удалить пользователя' })).not.toBeInTheDocument())
    expect(updateCalls).toBe(1)
    expect(within(usersPanel).queryByText('Отключен')).not.toBeInTheDocument()

    fireEvent.contextMenu(within(usersPanel).getByText('operator@example.com').closest('tr')!)
    await user.click(await screen.findByRole('menuitem', { name: 'Удалить' }))
    const reopenedDeleteDialog = await screen.findByRole('dialog', { name: 'Удалить пользователя' })
    const reopenedDeleteButton = within(reopenedDeleteDialog).getByRole('button', { name: 'Удалить' })
    const reopenedDeleteReasonInput = within(reopenedDeleteDialog).getByLabelText('Причина отключения пользователя')
    await user.type(reopenedDeleteReasonInput, 'Access no longer needed')
    await user.click(reopenedDeleteButton)

    expect(await within(usersPanel).findByText('Отключен')).toBeInTheDocument()
    expect(deactivationReason).toBe('Access no longer needed')
    expect(await screen.findByText('Пользователь отключен.')).toHaveAttribute('role', 'status')

    fireEvent.contextMenu(within(usersPanel).getByText('operator@example.com').closest('tr')!)
    await user.click(await screen.findByRole('menuitem', { name: 'Вернуть' }))
    const restoreDialog = await screen.findByRole('dialog', { name: 'Вернуть пользователя?' })
    expect(within(restoreDialog).getByText('Действие будет записано в историю изменений.')).toBeInTheDocument()
    const restoreCancelButton = within(restoreDialog).getByRole('button', { name: 'Отмена' })
    const restoreConfirmButton = within(restoreDialog).getByRole('button', { name: 'Вернуть' })
    const restoreCloseButton = within(restoreDialog).getByRole('button', { name: 'Отменить восстановление пользователя' })
    await waitFor(() => expect(restoreCancelButton).toHaveFocus())
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(restoreCloseButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(restoreCancelButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(restoreConfirmButton).toHaveFocus()
    await user.click(within(restoreDialog).getByRole('button', { name: 'Вернуть' }))

    expect(await within(usersPanel).findByText('Активен')).toBeInTheDocument()
    expect(await screen.findByText('Пользователь восстановлен.')).toHaveAttribute('role', 'status')
  })

  it('keeps restricted sections closed after administrator creates an operator', async () => {
    const user = userEvent.setup()
    let operatorSession = false
    const authClient = createAuthClient({
      login: async (request) => {
        if (request.email !== 'operator@example.com') {
          operatorSession = false
          return createAuthResponse()
        }

        operatorSession = true
        return createAuthResponse({
          accessToken: 'operator-token',
          user: {
            email: 'operator@example.com',
            displayName: 'Оператор',
            roles: ['operator'],
            permissions: ['dictionaries.read', 'payments.read', 'payments.write'],
          },
        })
      },
    })
    const statefulUserClient = createStatefulUserClient()
    const userClient: UserManagementClient = {
      ...statefulUserClient,
      getRoles: async (...args) => {
        if (operatorSession) {
          throw new Error('Панель пользователей не должна загружаться для оператора.')
        }
        return statefulUserClient.getRoles(...args)
      },
      getUsers: async (...args) => {
        if (operatorSession) {
          throw new Error('Панель пользователей не должна загружаться для оператора.')
        }
        return statefulUserClient.getUsers(...args)
      },
      getUsersPage: async (...args) => {
        if (operatorSession) {
          throw new Error('Панель пользователей не должна загружаться для оператора.')
        }
        return statefulUserClient.getUsersPage(...args)
      },
    }
    const importClient = createImportClient({
      getAccessRuns: async () => {
        if (operatorSession) {
          throw new Error('Импорт не должен загружаться для оператора.')
        }
        return []
      },
    })
    const reportClient = createReportClient({
      getConsolidatedReport: async (accessToken, params) => {
        if (operatorSession) {
          throw new Error('Отчеты не должны загружаться для оператора.')
        }
        return createReportClient().getConsolidatedReport(accessToken, params)
      },
      getIncomeReport: async (accessToken, params) => {
        if (operatorSession) {
          throw new Error('Отчеты не должны загружаться для оператора.')
        }
        return createReportClient().getIncomeReport(accessToken, params)
      },
      getExpenseReport: async (accessToken, params) => {
        if (operatorSession) {
          throw new Error('Отчеты не должны загружаться для оператора.')
        }
        return createReportClient().getExpenseReport(accessToken, params)
      },
    })
    const auditClient = createAuditClient({
      getEvents: async () => {
        if (operatorSession) {
          throw new Error('Аудит не должен загружаться для оператора.')
        }
        return [createAuditEvent({})]
      },
    })

    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={importClient} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={userClient} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Пользователи')
    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })

    await user.click(within(usersPanel).getByRole('button', { name: 'Добавить' }))
    const dialog = await screen.findByRole('dialog', { name: 'Новый пользователь' })
    await user.type(within(dialog).getByLabelText('Email пользователя'), 'operator@example.com')
    await user.type(within(dialog).getByLabelText('Имя пользователя'), 'Оператор')
    await user.type(within(dialog).getByLabelText('Пароль пользователя'), 'StrongPass123')
    await user.selectOptions(within(dialog).getByLabelText('Роль пользователя'), 'operator')
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    expect((await within(usersPanel).findAllByText('Оператор')).length).toBeGreaterThan(0)

    await user.click(screen.getByRole('button', { name: 'Выйти' }))
    await user.clear(screen.getByLabelText('Email'))
    await user.type(screen.getByLabelText('Email'), 'operator@example.com')
    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    expect(await screen.findByText('Оператор')).toBeInTheDocument()
    expect(await screen.findByRole('region', { name: 'Панель' })).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Основные разделы' })).not.toBeInTheDocument()
    expect(screen.queryByText('Поиск по гаражу, владельцу или поставщику')).not.toBeInTheDocument()
    const operatorTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    expect(within(operatorTiles).getByRole('button', { name: 'Контрагенты' })).toBeEnabled()
    expect(within(operatorTiles).getByRole('button', { name: 'Платежи' })).toBeEnabled()
    expect(within(operatorTiles).getByRole('button', { name: 'Отчёты' })).toBeDisabled()
    expect(within(operatorTiles).getByRole('button', { name: /Управление\s+фондами/i })).toBeDisabled()

    await user.click(within(operatorTiles).getByRole('button', { name: 'Контрагенты' }))
    expect(await screen.findByRole('region', { name: 'Контрагенты' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Панель' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Назад к выбору раздела' }))
    const operatorTilesAgain = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(operatorTilesAgain).getByRole('button', { name: 'Платежи' }))
    expect(await screen.findByRole('region', { name: 'Платежи' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Контрагенты' })).not.toBeInTheDocument()

    expect(screen.queryByText('Панель пользователей не должна загружаться для оператора.')).not.toBeInTheDocument()
    expect(screen.queryByText('Импорт не должен загружаться для оператора.')).not.toBeInTheDocument()
    expect(screen.queryByText('Отчеты не должны загружаться для оператора.')).not.toBeInTheDocument()
    expect(screen.queryByText('Аудит не должен загружаться для оператора.')).not.toBeInTheDocument()
  }, 30_000)

  it('keeps dictionary and payment actions read-only without write permissions', async () => {
    const user = userEvent.setup()
    const authClient = createAuthClient({
      login: async () =>
        createAuthResponse({
          user: {
            email: 'viewer@example.com',
            displayName: 'Наблюдатель',
            roles: ['read_only'],
            permissions: ['users.manage', 'dictionaries.read', 'payments.read', 'reports.read'],
          },
        }),
    })
    const dictionaryClient = createDictionaryClient({
      createOwner: async () => {
        throw new Error('Создание владельца не должно вызываться без dictionaries.write.')
      },
      archiveOwner: async () => {
        throw new Error('Архивирование владельца не должно вызываться без dictionaries.write.')
      },
    })
    const financeClient = createFinanceClient({
      createIncome: async () => {
        throw new Error('Поступление не должно вызываться без payments.write.')
      },
      createExpense: async () => {
        throw new Error('Выплата не должна вызываться без payments.write.')
      },
      createAccrual: async () => {
        throw new Error('Начисление не должно вызываться без payments.write.')
      },
      createMeterReading: async () => {
        throw new Error('Показание не должно вызываться без payments.write.')
      },
    })

    render(<App authClient={authClient} dictionaryClient={dictionaryClient} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await openSection(user, 'Справочники')

    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    expect(within(dictionaryPanel).getByText('Режим просмотра: для добавления, изменения и удаления справочников нужно право dictionaries.write.')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByRole('button', { name: 'Добавить' })).toBeDisabled()
    expect(within(dictionaryPanel).getByLabelText('Поиск: Владельцы')).toBeEnabled()
    await openDictionarySubgroup(user, dictionaryPanel, 'Гаражи')
    expect(within(dictionaryPanel).getByLabelText('Поиск: Гаражи')).toBeEnabled()

    await openSection(user, 'Платежи')

    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    expect(within(financePanel).getByText('Режим просмотра: для записи платежей, начислений и показаний нужно право payments.write.')).toBeInTheDocument()
    for (const button of within(financePanel).getAllByRole('button', { name: 'Провести' })) {
      expect(button).toBeDisabled()
    }
    for (const button of within(financePanel).getAllByRole('button', { name: 'Начислить' })) {
      expect(button).toBeDisabled()
    }
    expect(within(financePanel).getByRole('button', { name: 'Создать месяц' })).toBeDisabled()
    expect(within(financePanel).getByRole('button', { name: 'Внести' })).toBeDisabled()
    const incomeRow = within(financePanel).getAllByText('Членский взнос')[0].closest('tr')!
    fireEvent.contextMenu(incomeRow)
    const financeMenu = await screen.findByRole('menu', { name: 'Операции с платежами' })
    expect(within(financeMenu).getByRole('menuitem', { name: 'Добавить' })).toBeDisabled()
    expect(within(financeMenu).getByRole('menuitem', { name: 'Изменить' })).toBeDisabled()
    expect(within(financeMenu).getByRole('menuitem', { name: 'Удалить' })).toBeDisabled()
    await user.click(incomeRow)
    expect(screen.queryByRole('dialog', { name: 'Новое поступление' })).not.toBeInTheDocument()
    expect(within(financePanel).getByRole('alert')).toHaveTextContent('Для записи платежей нужно право payments.write.')
    expect(screen.queryByText('Создание владельца не должно вызываться без dictionaries.write.')).not.toBeInTheDocument()
    expect(screen.queryByText('Поступление не должно вызываться без payments.write.')).not.toBeInTheDocument()
  })

  it('keeps reports closed without dictionary read permission for filters', async () => {
    const user = userEvent.setup()
    let reportCalls = 0
    const authClient = createAuthClient({
      login: async () =>
        createAuthResponse({
          user: {
            email: 'reports-only@example.com',
            displayName: 'Только отчеты',
            roles: ['reports_partial'],
            permissions: ['reports.read'],
          },
        }),
    })
    const reportClient = createReportClient({
      getConsolidatedReport: async () => {
        reportCalls += 1
        throw new Error('Отчеты не должны загружаться без dictionaries.read.')
      },
      getIncomeReport: async () => {
        reportCalls += 1
        throw new Error('Отчеты не должны загружаться без dictionaries.read.')
      },
      getExpenseReport: async () => {
        reportCalls += 1
        throw new Error('Отчеты не должны загружаться без dictionaries.read.')
      },
    })

    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Отчеты')

    const reportsNotice = await screen.findByLabelText('Отчеты недоступны')
    expect(within(reportsNotice).getByText('Для фильтров отчетов нужно право чтения справочников.')).toBeInTheDocument()
    expect(within(reportsNotice).getByText('Требуется право: dictionaries.read')).toBeInTheDocument()
    expect(screen.queryByText('Консолидированный отчет за период')).not.toBeInTheDocument()
    expect(screen.queryByText('Отчеты не должны загружаться без dictionaries.read.')).not.toBeInTheDocument()
    expect(reportCalls).toBe(0)
  })

  it('allows tariff management without broad dictionary write permission', async () => {
    const user = userEvent.setup()
    let createdTariffs = 0
    const authClient = createAuthClient({
      login: async () =>
        createAuthResponse({
          user: {
            email: 'tariff@example.com',
            displayName: 'Тарифный специалист',
            roles: ['tariff_manager'],
            permissions: ['users.manage', 'dictionaries.read', 'tariffs.manage'],
          },
        }),
    })
    const dictionaryClient = createDictionaryClient({
      createOwner: async () => {
        throw new Error('Создание владельца не должно вызываться без dictionaries.write.')
      },
      createTariff: async (_token, request) => {
        createdTariffs += 1
        return createTariff({
          id: `tariff-new-${createdTariffs}`,
          name: request.name,
          calculationBase: request.calculationBase,
          rate: request.rate,
          effectiveFrom: request.effectiveFrom,
          electricityFirstThreshold: request.electricityFirstThreshold ?? null,
          electricitySecondThreshold: request.electricitySecondThreshold ?? null,
          electricityFirstRate: request.electricityFirstRate ?? null,
          electricitySecondRate: request.electricitySecondRate ?? null,
          electricityThirdRate: request.electricityThirdRate ?? null,
        })
      },
      updateTariff: async (_token, id, request) => {
        return createTariff({
          id,
          name: request.name,
          calculationBase: request.calculationBase,
          rate: request.rate,
          effectiveFrom: request.effectiveFrom,
          comment: request.comment ?? null,
          electricityFirstThreshold: request.electricityFirstThreshold ?? null,
          electricitySecondThreshold: request.electricitySecondThreshold ?? null,
          electricityFirstRate: request.electricityFirstRate ?? null,
          electricitySecondRate: request.electricitySecondRate ?? null,
          electricityThirdRate: request.electricityThirdRate ?? null,
        })
      },
      archiveTariff: async () => undefined,
    })

    render(<App authClient={authClient} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    expect(within(dictionaryPanel).getByText('Режим просмотра: для добавления, изменения и удаления справочников нужно право dictionaries.write.')).toBeInTheDocument()
    expect(within(dictionaryPanel).queryByText('Режим просмотра тарифов: для изменения тарифов нужно право tariffs.manage.')).not.toBeInTheDocument()
    expect(within(dictionaryPanel).getByRole('button', { name: 'Добавить' })).toBeDisabled()

    await openDictionarySubgroup(user, dictionaryPanel, 'Тарифы')
    expect(within(dictionaryPanel).getByRole('button', { name: 'Добавить' })).toBeEnabled()
    const tariffDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    expect(within(tariffDialog).getByText('База расчета')).toBeInTheDocument()
    expect(within(tariffDialog).getByText('Дата начала')).toBeInTheDocument()
    await user.clear(within(tariffDialog).getByLabelText('Название тарифа'))
    await user.type(within(tariffDialog).getByLabelText('Название тарифа'), 'Тариф обслуживания')
    await user.click(within(tariffDialog).getByRole('button', { name: 'Сохранить' }))

    expect(createdTariffs).toBe(1)
    expect(await screen.findByText('Запись добавлена.')).toBeInTheDocument()
    expect(screen.queryByText('Создание владельца не должно вызываться без dictionaries.write.')).not.toBeInTheDocument()
    return

    const tariffForm = within(dictionaryPanel).getByLabelText('Название тарифа').closest('form')!
    const tariffSubmit = within(tariffForm as HTMLElement).getByRole('button', { name: 'Добавить' })
    expect(tariffSubmit).toBeEnabled()

    await user.clear(within(tariffForm as HTMLElement).getByLabelText('Название тарифа'))
    await user.type(within(tariffForm as HTMLElement).getByLabelText('Название тарифа'), 'Тариф обслуживания')
    await user.click(tariffSubmit)

    expect(createdTariffs).toBe(1)
    expect(await within(dictionaryPanel).findByText('Тариф обслуживания')).toBeInTheDocument()

    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф воды' }))
    expect(within(dictionaryPanel).getByText('Изменение тарифа')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Редактируется')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф воды' })).toBeDisabled()
    await user.clear(within(tariffForm as HTMLElement).getByLabelText('Название тарифа'))
    await user.type(within(tariffForm as HTMLElement).getByLabelText('Название тарифа'), 'Вода черновик')
    expect(within(tariffForm as HTMLElement).getByText('Есть несохраненные изменения тарифа.')).toHaveAttribute('role', 'status')
    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф обслуживания' }))
    const switchDialog = await screen.findByRole('dialog', { name: 'Перейти к другому тарифу?' })
    await user.click(within(switchDialog).getByRole('button', { name: 'Остаться' }))
    expect(within(tariffForm as HTMLElement).getByLabelText('Название тарифа')).toHaveValue('Вода черновик')
    expect(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф воды' })).toBeDisabled()

    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф обслуживания' }))
    const secondSwitchDialog = await screen.findByRole('dialog', { name: 'Перейти к другому тарифу?' })
    await user.click(within(secondSwitchDialog).getByRole('button', { name: 'Перейти без сохранения' }))
    expect(within(tariffForm as HTMLElement).getByLabelText('Название тарифа')).toHaveValue('Тариф обслуживания')
    expect(within(tariffForm as HTMLElement).queryByText('Есть несохраненные изменения тарифа.')).not.toBeInTheDocument()
    expect(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф воды' })).toBeEnabled()
    expect(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф обслуживания' })).toBeDisabled()
    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф воды' }))
    expect(within(tariffForm as HTMLElement).getByLabelText('Название тарифа')).toHaveValue('Тариф воды')
    expect(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф воды' })).toBeDisabled()

    await user.clear(within(tariffForm as HTMLElement).getByLabelText('Название тарифа'))
    await user.type(within(tariffForm as HTMLElement).getByLabelText('Название тарифа'), 'Вода черновик')
    await user.click(within(tariffForm as HTMLElement).getByRole('button', { name: 'Отменить' }))
    const cancelDialog = await screen.findByRole('dialog', { name: 'Отменить редактирование тарифа?' })
    await user.click(within(cancelDialog).getByRole('button', { name: 'Остаться' }))
    expect(within(dictionaryPanel).getByText('Изменение тарифа')).toBeInTheDocument()
    expect(within(tariffForm as HTMLElement).getByLabelText('Название тарифа')).toHaveValue('Вода черновик')

    await user.click(within(tariffForm as HTMLElement).getByRole('button', { name: 'Отменить' }))
    const secondCancelDialog = await screen.findByRole('dialog', { name: 'Отменить редактирование тарифа?' })
    await user.click(within(secondCancelDialog).getByRole('button', { name: 'Отменить без сохранения' }))
    expect(within(dictionaryPanel).queryByText('Изменение тарифа')).not.toBeInTheDocument()
    expect(within(tariffForm as HTMLElement).getByLabelText('Название тарифа')).toHaveValue('')

    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф воды' }))
    expect(within(dictionaryPanel).getByText('Изменение тарифа')).toBeInTheDocument()
    await user.clear(within(tariffForm as HTMLElement).getByLabelText('Название тарифа'))
    await user.type(within(tariffForm as HTMLElement).getByLabelText('Название тарифа'), 'Вода после собрания')
    await user.clear(within(tariffForm as HTMLElement).getByLabelText('Ставка тарифа'))
    await user.type(within(tariffForm as HTMLElement).getByLabelText('Ставка тарифа'), '72.5')
    await user.type(within(tariffForm as HTMLElement).getByLabelText('Комментарий тарифа'), 'Протокол 2')
    await user.click(within(tariffForm as HTMLElement).getByRole('button', { name: 'Сохранить' }))

    expect(updatedTariffRequest).toEqual({ id: 'tariff-1', name: 'Вода после собрания', rate: 72.5, comment: 'Протокол 2' })
    expect(await within(dictionaryPanel).findByText('Вода после собрания')).toBeInTheDocument()
    expect(within(dictionaryPanel).queryByText('Изменение тарифа')).not.toBeInTheDocument()

    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Архивировать тариф Вода после собрания' }))
    expect(archivedTariffId).toBeNull()
    const archiveDialog = await screen.findByRole('dialog', { name: 'Подтвердите архивирование' })
    await user.click(within(archiveDialog).getByRole('button', { name: 'Архивировать запись' }))

    expect(archivedTariffId).toBe('tariff-1')
    expect(screen.queryByText('Создание владельца не должно вызываться без dictionaries.write.')).not.toBeInTheDocument()
  })

  it('shows backend error when tariff effective date moves after existing accruals', async () => {
    const user = userEvent.setup()
    let updateTariffCalls = 0
    const dictionaryClient = createDictionaryClient({
      updateTariff: async () => {
        updateTariffCalls += 1
        throw new Error('Дата начала тарифа не может быть позже уже созданного начисления за 06.2026.')
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    await openDictionarySubgroup(user, dictionaryPanel, 'Тарифы')

    fireEvent.contextMenu(within(dictionaryPanel).getByText('Тариф воды').closest('tr')!)
    await user.click(await screen.findByRole('menuitem', { name: 'Изменить' }))
    const dialog = await screen.findByRole('dialog', { name: 'Тарифы' })
    await user.clear(within(dialog).getByLabelText('Дата начала тарифа'))
    await user.type(within(dialog).getByLabelText('Дата начала тарифа'), '2026-08-01')
    const saveButton = within(dialog).getByRole('button', { name: 'Сохранить' })
    await user.click(saveButton)
    let confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения' })
    expect(within(confirmationDialog).getByText('Дата начала')).toBeInTheDocument()
    await waitFor(() => expect(within(confirmationDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения' })).not.toBeInTheDocument())
    await waitFor(() => expect(saveButton).toHaveFocus())
    expect(updateTariffCalls).toBe(0)

    await user.click(saveButton)
    confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения' })
    expect(within(confirmationDialog).getByText('Дата начала')).toBeInTheDocument()
    await user.click(within(confirmationDialog).getByRole('button', { name: 'Сохранить изменения' }))

    expect(updateTariffCalls).toBe(1)
    const alerts = await screen.findAllByRole('alert')
    expect(alerts.some((alert) => alert.textContent?.includes('Дата начала тарифа не может быть позже уже созданного начисления за 06.2026.'))).toBe(true)
    expect(screen.getByRole('dialog', { name: 'Тарифы' })).toBeInTheDocument()
  })

  it('creates electricity tariff with editable thresholds and three rates', async () => {
    const user = userEvent.setup()
    let createdRequest: unknown = null
    let tariffs: TariffDto[] = []
    const dictionaryClient = createDictionaryClient({
      getTariffs: async () => tariffs,
      createTariff: async (_token, request) => {
        createdRequest = request
        const tariff = createTariff({
          id: 'tariff-electricity',
          name: request.name,
          calculationBase: request.calculationBase,
          rate: request.rate,
          effectiveFrom: request.effectiveFrom,
          comment: request.comment ?? null,
          electricityFirstThreshold: request.electricityFirstThreshold ?? null,
          electricitySecondThreshold: request.electricitySecondThreshold ?? null,
          electricityFirstRate: request.electricityFirstRate ?? null,
          electricitySecondRate: request.electricitySecondRate ?? null,
          electricityThirdRate: request.electricityThirdRate ?? null,
        })
        tariffs = [tariff]
        return tariff
      },
    })

    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    await openDictionarySubgroup(user, dictionaryPanel, 'Тарифы')
    const tariffDialog = await openDictionaryCreateDialog(user, dictionaryPanel)

    await user.clear(within(tariffDialog).getByLabelText('Название тарифа'))
    await user.type(within(tariffDialog).getByLabelText('Название тарифа'), 'Электроэнергия 3 зоны')
    await user.selectOptions(within(tariffDialog).getByLabelText('База расчета тарифа'), 'meter_electricity')
    await user.clear(within(tariffDialog).getByLabelText('Ставка тарифа'))
    await user.type(within(tariffDialog).getByLabelText('Ставка тарифа'), '4')
    await user.type(within(tariffDialog).getByLabelText('Первый порог электроэнергии'), '50')
    await user.type(within(tariffDialog).getByLabelText('Второй порог электроэнергии'), '100')
    await user.type(within(tariffDialog).getByLabelText('Первая ставка электроэнергии'), '2')
    await user.type(within(tariffDialog).getByLabelText('Вторая ставка электроэнергии'), '3')
    await user.type(within(tariffDialog).getByLabelText('Третья ставка электроэнергии'), '5')
    await user.click(within(tariffDialog).getByRole('button', { name: 'Сохранить' }))

    expect(createdRequest).toMatchObject({
      name: 'Электроэнергия 3 зоны',
      calculationBase: 'meter_electricity',
      rate: 4,
      electricityFirstThreshold: 50,
      electricitySecondThreshold: 100,
      electricityFirstRate: 2,
      electricitySecondRate: 3,
      electricityThirdRate: 5,
    })
    expect(await screen.findByText('Запись добавлена.')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText(/до 50,00 кВт: 2,00/)).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText(/до 100,00 кВт: 3,00/)).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText(/выше: 5,00/)).toBeInTheDocument()
  })

  it('adds owner, garage, supplier group and supplier from protected workspace', async () => {
    const user = userEvent.setup()
    const dictionaryClient = createStatefulDictionaryClient()
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    const addDictionaryRecordButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    let ownerDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await waitFor(() => expect(within(ownerDialog).getByRole('button', { name: 'Закрыть окно справочника' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Владельцы' })).not.toBeInTheDocument())
    await waitFor(() => expect(addDictionaryRecordButton).toHaveFocus())
    expect(within(dictionaryPanel).queryByText('Петров Петр')).not.toBeInTheDocument()

    ownerDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(ownerDialog).getByLabelText('Фамилия владельца'), 'Петров')
    await user.type(within(ownerDialog).getByLabelText('Имя владельца'), 'Петр')
    await user.type(within(ownerDialog).getByLabelText('Телефон владельца'), '+7 913')
    await user.click(within(ownerDialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dictionaryPanel).findByText('Петров Петр')).toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Гаражи')
    const addGarageButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    let garageCreateDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await waitFor(() => expect(within(garageCreateDialog).getByRole('button', { name: 'Закрыть окно справочника' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Гаражи' })).not.toBeInTheDocument())
    await waitFor(() => expect(addGarageButton).toHaveFocus())
    expect(within(dictionaryPanel).queryByText('21')).not.toBeInTheDocument()

    garageCreateDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(garageCreateDialog).getByLabelText('Номер гаража'), '21')
    await user.clear(within(garageCreateDialog).getByLabelText('Количество людей'))
    await user.type(within(garageCreateDialog).getByLabelText('Количество людей'), '2')
    await user.clear(within(garageCreateDialog).getByLabelText('Количество этажей'))
    await user.type(within(garageCreateDialog).getByLabelText('Количество этажей'), '3')
    await user.clear(within(garageCreateDialog).getByLabelText('Стартовый баланс гаража'))
    await user.type(within(garageCreateDialog).getByLabelText('Стартовый баланс гаража'), '350')
    await user.type(within(garageCreateDialog).getByLabelText('Стартовый счетчик воды'), '18.5')
    await user.type(within(garageCreateDialog).getByLabelText('Стартовый счетчик электричества'), '412.75')
    await user.type(within(garageCreateDialog).getByLabelText('Комментарий по гаражу'), 'Старые счетчики внесены из Access')
    await user.selectOptions(within(garageCreateDialog).getByLabelText('Владелец гаража'), within(garageCreateDialog).getByRole('option', { name: 'Петров Петр' }))
    await user.click(within(garageCreateDialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dictionaryPanel).findByText('21')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Петров Петр')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('350,00')).toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Группы поставщиков')
    const addSupplierGroupButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    let groupDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await waitFor(() => expect(within(groupDialog).getByRole('button', { name: 'Закрыть окно справочника' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Группы поставщиков' })).not.toBeInTheDocument())
    await waitFor(() => expect(addSupplierGroupButton).toHaveFocus())
    expect(within(dictionaryPanel).queryByText('Связь')).not.toBeInTheDocument()

    groupDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(groupDialog).getByLabelText('Группа поставщиков'), 'Связь')
    await user.click(within(groupDialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dictionaryPanel).findByText('Связь')).toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Поставщики')
    const addSupplierButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    let supplierDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await waitFor(() => expect(within(supplierDialog).getByRole('button', { name: 'Закрыть окно справочника' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Поставщики' })).not.toBeInTheDocument())
    await waitFor(() => expect(addSupplierButton).toHaveFocus())
    expect(within(dictionaryPanel).queryByText('Сибирь Онлайн')).not.toBeInTheDocument()

    supplierDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(supplierDialog).getByLabelText('Название поставщика'), 'Сибирь Онлайн')
    await user.selectOptions(within(supplierDialog).getByLabelText('Группа для поставщика'), within(supplierDialog).getByRole('option', { name: 'Связь' }))
    await user.type(within(supplierDialog).getByLabelText('ИНН поставщика'), '5401000000')
    await user.clear(within(supplierDialog).getByLabelText('Стартовый баланс поставщика'))
    await user.type(within(supplierDialog).getByLabelText('Стартовый баланс поставщика'), '1200')
    await user.click(within(supplierDialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dictionaryPanel).findByText('Сибирь Онлайн')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Связь')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('1 200,00')).toBeInTheDocument()
    return

    await user.type(within(dictionaryPanel).getByLabelText('Фамилия владельца'), 'Петров')
    await user.type(within(dictionaryPanel).getByLabelText('Имя владельца'), 'Петр')
    await user.type(within(dictionaryPanel).getByLabelText('Телефон владельца'), '+7 913')
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[0])
    expect((await within(dictionaryPanel).findAllByText('Петров Петр')).length).toBeGreaterThan(0)

    await user.type(within(dictionaryPanel).getByLabelText('Номер гаража'), '21')
    await user.clear(within(dictionaryPanel).getByLabelText('Количество людей'))
    await user.type(within(dictionaryPanel).getByLabelText('Количество людей'), '2')
    await user.clear(within(dictionaryPanel).getByLabelText('Количество этажей'))
    await user.type(within(dictionaryPanel).getByLabelText('Количество этажей'), '3')
    await user.clear(within(dictionaryPanel).getByLabelText('Стартовый баланс гаража'))
    await user.type(within(dictionaryPanel).getByLabelText('Стартовый баланс гаража'), '350')
    await user.type(within(dictionaryPanel).getByLabelText('Стартовый счетчик воды'), '18.5')
    await user.type(within(dictionaryPanel).getByLabelText('Стартовый счетчик электричества'), '412.75')
    await user.type(within(dictionaryPanel).getByLabelText('Комментарий по гаражу'), 'Старые счетчики внесены из Access')
    await user.selectOptions(within(dictionaryPanel).getByLabelText('Владелец гаража'), within(dictionaryPanel).getByRole('option', { name: 'Петров Петр' }))
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[1])
    expect(await within(dictionaryPanel).findByText('Гараж 21')).toBeInTheDocument()
    expect(within(dictionaryPanel).getAllByText('Петров Петр').length).toBeGreaterThan(0)
    expect(within(dictionaryPanel).getByText(/старт 350,00/)).toBeInTheDocument()

    const openGarageButton = within(dictionaryPanel).getByRole('button', { name: 'Открыть карточку гаража 21' })
    await user.click(openGarageButton)
    const garageDialog = await screen.findByRole('dialog', { name: 'Гараж 21' })
    const garageCloseButton = within(garageDialog).getByRole('button', { name: 'Закрыть карточку гаража' })
    await waitFor(() => expect(garageCloseButton).toHaveFocus())
    await user.tab()
    expect(garageCloseButton).toHaveFocus()
    expect(within(garageDialog).getByText('Карточка гаража')).toBeInTheDocument()
    const garageOwnerDescription = garageDialog.querySelector('#garage-card-owner') as HTMLElement
    expect(garageOwnerDescription).toHaveTextContent('Петров Петр')
    expect(garageDialog).toHaveAttribute('aria-describedby', garageOwnerDescription.id)
    expect(within(garageDialog).getAllByText('Петров Петр').length).toBeGreaterThan(0)
    expect(within(garageDialog).getByText('2')).toBeInTheDocument()
    expect(within(garageDialog).getByText('3')).toBeInTheDocument()
    expect(within(garageDialog).getByText('350,00')).toBeInTheDocument()
    expect(within(garageDialog).getByText('18,5')).toBeInTheDocument()
    expect(within(garageDialog).getByText('412,75')).toBeInTheDocument()
    expect(within(garageDialog).getByText('Старые счетчики внесены из Access')).toBeInTheDocument()
    fireEvent.mouseDown(garageDialog.parentElement!)
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Гараж 21' })).not.toBeInTheDocument()
    })
    expect(openGarageButton).toHaveFocus()

    await user.type(within(dictionaryPanel).getByLabelText('Группа поставщиков'), 'Связь')
    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Добавить группу' }))
    await user.type(within(dictionaryPanel).getByLabelText('Название поставщика'), 'Сибирь Онлайн')
    await user.type(within(dictionaryPanel).getByLabelText('ИНН поставщика'), '5401000000')
    await user.clear(within(dictionaryPanel).getByLabelText('Стартовый баланс поставщика'))
    await user.type(within(dictionaryPanel).getByLabelText('Стартовый баланс поставщика'), '1200')
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[2])
    expect(await within(dictionaryPanel).findByText('Сибирь Онлайн')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Связь, ИНН 5401000000 · старт 1 200,00')).toBeInTheDocument()
  }, 30_000)

  it('confirms garage dictionary edits with owner label and money diff', async () => {
    const user = userEvent.setup()
    const currentOwner = createOwner({ id: 'owner-current', lastName: 'Иванов', firstName: 'Иван' })
    const nextOwner = createOwner({ id: 'owner-next', lastName: 'Петров', firstName: 'Петр' })
    let garage = createGarage({
      id: 'garage-12',
      number: '12',
      ownerId: currentOwner.id,
      ownerName: currentOwner.fullName,
      startingBalance: 100,
    })
    const updateGarage = vi.fn(async (_token: string, id: string, request: UpsertGarageRequest) => {
      const owner = request.ownerId === nextOwner.id ? nextOwner : currentOwner
      garage = createGarage({
        ...garage,
        id,
        ownerId: owner.id,
        ownerName: owner.fullName,
        startingBalance: request.startingBalance,
      })
      return garage
    })
    const dictionaryClient = createDictionaryClient({
      getOwners: async () => [currentOwner, nextOwner],
      getGarages: async () => [garage],
      updateGarage,
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    let garageTable = await openDictionarySubgroup(user, dictionaryPanel, 'Гаражи')
    let garageRow = within(garageTable).getByText('12').closest('tr')
    if (!garageRow) {
      throw new Error('Строка гаража 12 не найдена.')
    }

    fireEvent.doubleClick(garageRow)
    let garageDialog = await screen.findByRole('dialog', { name: 'Гаражи' })
    await user.click(within(garageDialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения' })).not.toBeInTheDocument())
    expect(updateGarage).not.toHaveBeenCalled()

    garageTable = await within(dictionaryPanel).findByRole('table', { name: /Таблица: Гаражи/ })
    garageRow = within(garageTable).getByText('12').closest('tr')
    if (!garageRow) {
      throw new Error('Строка гаража 12 не найдена после no-op сохранения.')
    }

    fireEvent.doubleClick(garageRow)
    garageDialog = await screen.findByRole('dialog', { name: 'Гаражи' })
    await user.selectOptions(within(garageDialog).getByLabelText('Владелец гаража'), nextOwner.id)
    await user.clear(within(garageDialog).getByLabelText('Стартовый баланс гаража'))
    await user.type(within(garageDialog).getByLabelText('Стартовый баланс гаража'), '350')
    const saveButton = within(garageDialog).getByRole('button', { name: 'Сохранить' })
    await user.click(saveButton)

    const confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения' })
    const changeList = within(confirmationDialog).getByRole('list', { name: 'Изменяемые поля' })
    expect(changeList).toHaveTextContent('Владелец')
    expect(changeList).toHaveTextContent('Иванов Иван')
    expect(changeList).toHaveTextContent('Петров Петр')
    expect(changeList).toHaveTextContent('Стартовый баланс')
    expect(changeList).toHaveTextContent('100,00')
    expect(changeList).toHaveTextContent('350,00')
    expect(updateGarage).not.toHaveBeenCalled()
    await waitFor(() => expect(within(confirmationDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения' })).not.toBeInTheDocument())
    await waitFor(() => expect(saveButton).toHaveFocus())
    expect(updateGarage).not.toHaveBeenCalled()

    await user.click(saveButton)
    const reopenedConfirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения' })
    await user.click(within(reopenedConfirmationDialog).getByRole('button', { name: 'Сохранить изменения' }))

    await waitFor(() => expect(updateGarage).toHaveBeenCalledWith('token', garage.id, expect.objectContaining({
      ownerId: nextOwner.id,
      startingBalance: 350,
    })))
    expect(await within(dictionaryPanel).findByText('Петров Петр')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('350,00')).toBeInTheDocument()
  })

  it('confirms supplier dictionary edits with group label and money diff', async () => {
    const user = userEvent.setup()
    const currentGroup = createGroup({ id: 'group-current', name: 'Коммунальные услуги' })
    const nextGroup = createGroup({ id: 'group-next', name: 'Ремонтные работы' })
    let supplier = createSupplier({
      id: 'supplier-water',
      name: 'Водоканал',
      groupId: currentGroup.id,
      groupName: currentGroup.name,
      inn: '5401000000',
      startingBalance: 1200,
    })
    const updateSupplier = vi.fn(async (_token: string, id: string, request: UpsertSupplierRequest) => {
      const group = request.groupId === nextGroup.id ? nextGroup : currentGroup
      supplier = createSupplier({
        ...supplier,
        id,
        name: request.name,
        groupId: group.id,
        groupName: group.name,
        inn: request.inn ?? null,
        startingBalance: request.startingBalance,
      })
      return supplier
    })
    const dictionaryClient = createDictionaryClient({
      getSupplierGroups: async () => [currentGroup, nextGroup],
      getSuppliers: async () => [supplier],
      updateSupplier,
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    let supplierTable = await openDictionarySubgroup(user, dictionaryPanel, 'Поставщики')
    let supplierRow = within(supplierTable).getByText('Водоканал').closest('tr')
    if (!supplierRow) {
      throw new Error('Строка поставщика Водоканал не найдена.')
    }

    fireEvent.doubleClick(supplierRow)
    let supplierDialog = await screen.findByRole('dialog', { name: /Поставщики/ })
    await user.click(within(supplierDialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения' })).not.toBeInTheDocument())
    expect(updateSupplier).not.toHaveBeenCalled()

    supplierTable = await within(dictionaryPanel).findByRole('table', { name: /Таблица: Поставщики/ })
    supplierRow = within(supplierTable).getByText('Водоканал').closest('tr')
    if (!supplierRow) {
      throw new Error('Строка поставщика Водоканал не найдена после no-op сохранения.')
    }

    fireEvent.doubleClick(supplierRow)
    supplierDialog = await screen.findByRole('dialog', { name: /Поставщики/ })
    await user.selectOptions(within(supplierDialog).getByLabelText('Группа для поставщика'), nextGroup.id)
    await user.clear(within(supplierDialog).getByLabelText('Стартовый баланс поставщика'))
    await user.type(within(supplierDialog).getByLabelText('Стартовый баланс поставщика'), '2500')
    const saveButton = within(supplierDialog).getByRole('button', { name: 'Сохранить' })
    await user.click(saveButton)

    const confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения' })
    const changeList = within(confirmationDialog).getByRole('list', { name: 'Изменяемые поля' })
    expect(changeList).toHaveTextContent('Группа')
    expect(changeList).toHaveTextContent('Коммунальные услуги')
    expect(changeList).toHaveTextContent('Ремонтные работы')
    expect(changeList).toHaveTextContent('Стартовый баланс')
    expect(changeList).toHaveTextContent('1 200,00')
    expect(changeList).toHaveTextContent('2 500,00')
    expect(updateSupplier).not.toHaveBeenCalled()
    await waitFor(() => expect(within(confirmationDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения' })).not.toBeInTheDocument())
    await waitFor(() => expect(saveButton).toHaveFocus())
    expect(updateSupplier).not.toHaveBeenCalled()

    await user.click(saveButton)
    const reopenedConfirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения' })
    await user.click(within(reopenedConfirmationDialog).getByRole('button', { name: 'Сохранить изменения' }))

    await waitFor(() => expect(updateSupplier).toHaveBeenCalledWith('token', supplier.id, expect.objectContaining({
      groupId: nextGroup.id,
      startingBalance: 2500,
    })))
    expect(await within(dictionaryPanel).findByText('Ремонтные работы')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('2 500,00')).toBeInTheDocument()
  })

  it('confirms tariff dictionary edits with labels dates and electricity tier diff', async () => {
    const user = userEvent.setup()
    let tariff = createTariff({
      id: 'tariff-water',
      name: 'Тариф воды',
      calculationBase: 'meter_water',
      rate: 50,
      effectiveFrom: '2026-07-01',
      comment: 'После собрания',
    })
    const updateTariff = vi.fn(async (_token: string, id: string, request: UpsertTariffRequest) => {
      tariff = createTariff({
        ...tariff,
        id,
        name: request.name,
        calculationBase: request.calculationBase,
        rate: request.rate,
        effectiveFrom: request.effectiveFrom,
        comment: request.comment ?? null,
        electricityFirstThreshold: request.electricityFirstThreshold ?? null,
        electricitySecondThreshold: request.electricitySecondThreshold ?? null,
        electricityFirstRate: request.electricityFirstRate ?? null,
        electricitySecondRate: request.electricitySecondRate ?? null,
        electricityThirdRate: request.electricityThirdRate ?? null,
      })
      return tariff
    })
    const dictionaryClient = createDictionaryClient({
      getTariffs: async () => [tariff],
      updateTariff,
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    let tariffTable = await openDictionarySubgroup(user, dictionaryPanel, 'Тарифы')
    let tariffRow = within(tariffTable).getByText('Тариф воды').closest('tr')
    if (!tariffRow) {
      throw new Error('Строка тарифа воды не найдена.')
    }

    fireEvent.doubleClick(tariffRow)
    let tariffDialog = await screen.findByRole('dialog', { name: 'Тарифы' })
    await user.click(within(tariffDialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения' })).not.toBeInTheDocument())
    expect(updateTariff).not.toHaveBeenCalled()

    tariffTable = await within(dictionaryPanel).findByRole('table', { name: /Таблица: Тарифы/ })
    tariffRow = within(tariffTable).getByText('Тариф воды').closest('tr')
    if (!tariffRow) {
      throw new Error('Строка тарифа воды не найдена после no-op сохранения.')
    }

    fireEvent.doubleClick(tariffRow)
    tariffDialog = await screen.findByRole('dialog', { name: 'Тарифы' })
    await user.selectOptions(within(tariffDialog).getByLabelText('База расчета тарифа'), 'meter_electricity')
    await user.clear(within(tariffDialog).getByLabelText('Ставка тарифа'))
    await user.type(within(tariffDialog).getByLabelText('Ставка тарифа'), '6.5')
    await user.clear(within(tariffDialog).getByLabelText('Дата начала тарифа'))
    await user.type(within(tariffDialog).getByLabelText('Дата начала тарифа'), '2026-08-15')
    await user.type(within(tariffDialog).getByLabelText('Первый порог электроэнергии'), '100')
    await user.type(within(tariffDialog).getByLabelText('Второй порог электроэнергии'), '200')
    await user.type(within(tariffDialog).getByLabelText('Первая ставка электроэнергии'), '4.25')
    await user.type(within(tariffDialog).getByLabelText('Вторая ставка электроэнергии'), '5.25')
    await user.type(within(tariffDialog).getByLabelText('Третья ставка электроэнергии'), '6.75')
    const saveButton = within(tariffDialog).getByRole('button', { name: 'Сохранить' })
    await user.click(saveButton)

    const confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения' })
    const changeList = within(confirmationDialog).getByRole('list', { name: 'Изменяемые поля' })
    expect(changeList).toHaveTextContent('База расчета')
    expect(changeList).toHaveTextContent('По счетчику воды')
    expect(changeList).toHaveTextContent('По счетчику электричества')
    expect(changeList).toHaveTextContent('Ставка')
    expect(changeList).toHaveTextContent('50')
    expect(changeList).toHaveTextContent('6.5')
    expect(changeList).toHaveTextContent('Дата начала')
    expect(changeList).toHaveTextContent('01.07.2026')
    expect(changeList).toHaveTextContent('15.08.2026')
    expect(changeList).toHaveTextContent('Первый порог электроэнергии')
    expect(changeList).toHaveTextContent('100')
    expect(changeList).toHaveTextContent('Второй порог электроэнергии')
    expect(changeList).toHaveTextContent('200')
    expect(changeList).toHaveTextContent('Первая ставка электроэнергии')
    expect(changeList).toHaveTextContent('4.25')
    expect(changeList).toHaveTextContent('Вторая ставка электроэнергии')
    expect(changeList).toHaveTextContent('5.25')
    expect(changeList).toHaveTextContent('Третья ставка электроэнергии')
    expect(changeList).toHaveTextContent('6.75')
    expect(updateTariff).not.toHaveBeenCalled()
    await waitFor(() => expect(within(confirmationDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения' })).not.toBeInTheDocument())
    await waitFor(() => expect(saveButton).toHaveFocus())
    expect(updateTariff).not.toHaveBeenCalled()

    await user.click(saveButton)
    const reopenedConfirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения' })
    await user.click(within(reopenedConfirmationDialog).getByRole('button', { name: 'Сохранить изменения' }))

    await waitFor(() => expect(updateTariff).toHaveBeenCalledWith('token', tariff.id, expect.objectContaining({
      calculationBase: 'meter_electricity',
      rate: 6.5,
      effectiveFrom: '2026-08-15',
      electricityFirstThreshold: 100,
      electricitySecondThreshold: 200,
      electricityFirstRate: 4.25,
      electricitySecondRate: 5.25,
      electricityThirdRate: 6.75,
    })))
    const updatedTariffRow = within(tariffTable).getByText('Тариф воды').closest('tr')
    if (!updatedTariffRow) {
      throw new Error('Строка тарифа воды не найдена после подтверждения.')
    }
    expect(updatedTariffRow).toHaveTextContent('до 100,00 кВт: 4,25, до 200,00 кВт: 5,25, выше: 6,75')
    expect(updatedTariffRow).toHaveTextContent('15.08.2026')
  })

  it('edits supplier groups and accounting operation types from dictionary dialogs', async () => {
    const user = userEvent.setup()
    const statefulDictionaryClient = createStatefulDictionaryClient()
    const updatedSupplierGroups: UpsertSupplierGroupRequest[] = []
    const updatedIncomeTypes: UpsertAccountingTypeRequest[] = []
    const updatedExpenseTypes: UpsertAccountingTypeRequest[] = []
    const dictionaryClient: DictionaryClient = {
      ...statefulDictionaryClient,
      updateSupplierGroup: async (...args) => {
        updatedSupplierGroups.push(args[2])
        return statefulDictionaryClient.updateSupplierGroup(...args)
      },
      updateIncomeType: async (...args) => {
        updatedIncomeTypes.push(args[2])
        return statefulDictionaryClient.updateIncomeType(...args)
      },
      updateExpenseType: async (...args) => {
        updatedExpenseTypes.push(args[2])
        return statefulDictionaryClient.updateExpenseType(...args)
      },
    }
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    const createDictionaryRecord = async (fill: (dialog: HTMLElement) => Promise<void>) => {
      const dialog = await openDictionaryCreateDialog(user, dictionaryPanel)
      await fill(dialog)
      await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    }

    const openRowEditor = async (tableLabel: RegExp, rowText: string) => {
      const table = await within(dictionaryPanel).findByRole('table', { name: tableLabel })
      const row = within(table).getByText(rowText).closest('tr')
      if (!row) {
        throw new Error(`Строка справочника "${rowText}" не найдена.`)
      }

      fireEvent.doubleClick(row)
      return screen.findByRole('dialog')
    }

    const saveEditorChange = async () => {
      await user.click(screen.getByRole('button', { name: 'Сохранить' }))
      const confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения' })
      expect(within(confirmationDialog).getByText('Название')).toBeInTheDocument()
      await user.click(within(confirmationDialog).getByRole('button', { name: 'Сохранить изменения' }))
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения' })).not.toBeInTheDocument())
    }

    await openDictionarySubgroup(user, dictionaryPanel, 'Группы поставщиков')
    await createDictionaryRecord(async (dialog) => {
      await user.type(within(dialog).getByLabelText('Группа поставщиков'), 'Коммунальные услуги')
    })
    let editDialog = await openRowEditor(/Таблица: Группы поставщиков/, 'Коммунальные услуги')
    await waitFor(() => expect(within(editDialog).getByRole('button', { name: 'Закрыть окно справочника' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(updatedSupplierGroups).toEqual([])
    expect(await within(dictionaryPanel).findByText('Коммунальные услуги')).toBeInTheDocument()

    editDialog = await openRowEditor(/Таблица: Группы поставщиков/, 'Коммунальные услуги')
    await user.clear(within(editDialog).getByLabelText('Группа поставщиков'))
    await user.type(within(editDialog).getByLabelText('Группа поставщиков'), 'Коммунальные подрядчики')
    await saveEditorChange()
    expect(updatedSupplierGroups).toEqual([{ name: 'Коммунальные подрядчики' }])
    expect(await within(dictionaryPanel).findByText('Коммунальные подрядчики')).toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Виды поступлений')
    await createDictionaryRecord(async (dialog) => {
      await user.type(within(dialog).getByLabelText('Название вида операции'), 'Членский взнос')
      await user.type(within(dialog).getByLabelText('Код вида операции'), 'membership')
    })
    editDialog = await openRowEditor(/Таблица: Виды поступлений/, 'Членский взнос')
    await waitFor(() => expect(within(editDialog).getByRole('button', { name: 'Закрыть окно справочника' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(updatedIncomeTypes).toEqual([])
    expect(await within(dictionaryPanel).findByText('Членский взнос')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('membership')).toBeInTheDocument()

    editDialog = await openRowEditor(/Таблица: Виды поступлений/, 'Членский взнос')
    await user.clear(within(editDialog).getByLabelText('Название вида операции'))
    await user.type(within(editDialog).getByLabelText('Название вида операции'), 'Членский сбор')
    await user.clear(within(editDialog).getByLabelText('Код вида операции'))
    await user.type(within(editDialog).getByLabelText('Код вида операции'), 'membership_fee')
    await saveEditorChange()
    expect(updatedIncomeTypes).toEqual([{ name: 'Членский сбор', code: 'membership_fee' }])
    expect(await within(dictionaryPanel).findByText('Членский сбор')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('membership_fee')).toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Виды выплат')
    await createDictionaryRecord(async (dialog) => {
      await user.type(within(dialog).getByLabelText('Название вида операции'), 'Электроэнергия')
      await user.type(within(dialog).getByLabelText('Код вида операции'), 'electricity')
    })
    editDialog = await openRowEditor(/Таблица: Виды выплат/, 'Электроэнергия')
    await waitFor(() => expect(within(editDialog).getByRole('button', { name: 'Закрыть окно справочника' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(updatedExpenseTypes).toEqual([])
    expect(await within(dictionaryPanel).findByText('Электроэнергия')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('electricity')).toBeInTheDocument()

    editDialog = await openRowEditor(/Таблица: Виды выплат/, 'Электроэнергия')
    await user.clear(within(editDialog).getByLabelText('Название вида операции'))
    await user.type(within(editDialog).getByLabelText('Название вида операции'), 'Электроэнергия поставщику')
    await user.clear(within(editDialog).getByLabelText('Код вида операции'))
    await user.type(within(editDialog).getByLabelText('Код вида операции'), 'electricity_supplier')
    await saveEditorChange()
    expect(updatedExpenseTypes).toEqual([{ name: 'Электроэнергия поставщику', code: 'electricity_supplier' }])
    expect(await within(dictionaryPanel).findByText('Электроэнергия поставщику')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('electricity_supplier')).toBeInTheDocument()
  }, 30_000)

  it('shows dictionary list truncation counter when there are more rows', async () => {
    const user = userEvent.setup()
    const owners = Array.from({ length: 6 }, (_, index) => createOwner({ id: `owner-${index + 1}`, lastName: `Владелец${index + 1}`, firstName: 'Тест' }))
    const dictionaryClient: DictionaryClient = {
      ...createStatefulDictionaryClient(),
      getOwners: async () => owners,
    }
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    expect(await within(dictionaryPanel).findByText('Владелец1 Тест')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Владелец6 Тест')).toBeInTheDocument()
    const paginationCounter = within(dictionaryPanel).getByText('Показано 1-6 из 6')
    expect(paginationCounter).toHaveAttribute('role', 'status')
    expect(paginationCounter).toHaveAttribute('aria-live', 'polite')
    expect(within(dictionaryPanel).getByRole('combobox', { name: 'Количество строк справочника' })).toHaveValue('25')
  })

  it('announces empty dictionary lists', async () => {
    const user = userEvent.setup()
    const dictionaryClient = createDictionaryClient({
      getOwners: async () => [],
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    const emptyDictionaryState = await within(dictionaryPanel).findByText('В этом справочнике пока нет записей')
    expect(emptyDictionaryState).toHaveAttribute('role', 'status')
    expect(emptyDictionaryState).toHaveAttribute('aria-live', 'polite')
  })

  it('requests bounded dictionary lists from dictionaries workspace', async () => {
    const user = userEvent.setup()
    const requestedLimits: Record<string, number | undefined> = {}
    const dictionaryClient = createDictionaryClient({
      getOwnersPage: async (_token, _search, _offset, limit) => {
        requestedLimits.owners = limit
        return { items: [createOwner()], totalCount: 1, offset: 0, limit: limit ?? 25 }
      },
      getGaragesPage: async (_token, _search, _offset, limit) => {
        requestedLimits.garages = limit
        return { items: [createGarage()], totalCount: 1, offset: 0, limit: limit ?? 25 }
      },
      getSupplierGroupsPage: async (_token, _search, _offset, limit) => {
        requestedLimits.supplierGroups = limit
        return { items: [createGroup()], totalCount: 1, offset: 0, limit: limit ?? 25 }
      },
      getSuppliersPage: async (_token, _groupId, _search, _offset, limit) => {
        requestedLimits.suppliers = limit
        return { items: [createSupplier()], totalCount: 1, offset: 0, limit: limit ?? 25 }
      },
      getIncomeTypesPage: async (_token, _search, _offset, limit) => {
        requestedLimits.incomeTypes = limit
        return { items: [createAccountingType({ name: 'Членский взнос' })], totalCount: 1, offset: 0, limit: limit ?? 25 }
      },
      getExpenseTypesPage: async (_token, _search, _offset, limit) => {
        requestedLimits.expenseTypes = limit
        return { items: [createAccountingType({ name: 'Электроэнергия' })], totalCount: 1, offset: 0, limit: limit ?? 25 }
      },
      getTariffsPage: async (_token, _search, _offset, limit) => {
        requestedLimits.tariffs = limit
        return { items: [createTariff()], totalCount: 1, offset: 0, limit: limit ?? 25 }
      },
      getOwners: async (_token, _search, limit) => {
        requestedLimits.ownerReferences = limit
        return [createOwner()]
      },
      getGarages: async (_token, _search, limit) => {
        requestedLimits.garages = limit
        return [createGarage()]
      },
      getSupplierGroups: async (_token, _search, limit) => {
        requestedLimits.supplierGroups = limit
        return [createGroup()]
      },
      getSuppliers: async (_token, _groupId, _search, limit) => {
        requestedLimits.suppliers = limit
        return [createSupplier()]
      },
      getIncomeTypes: async (_token, _search, limit) => {
        requestedLimits.incomeTypes = limit
        return [createAccountingType({ name: 'Членский взнос' })]
      },
      getExpenseTypes: async (_token, _search, limit) => {
        requestedLimits.expenseTypes = limit
        return [createAccountingType({ name: 'Электроэнергия' })]
      },
      getTariffs: async (_token, _search, limit) => {
        requestedLimits.tariffs = limit
        return [createTariff()]
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    await openDictionarySubgroup(user, dictionaryPanel, 'Гаражи')
    await openDictionarySubgroup(user, dictionaryPanel, 'Группы поставщиков')
    await openDictionarySubgroup(user, dictionaryPanel, 'Поставщики')
    await openDictionarySubgroup(user, dictionaryPanel, 'Виды поступлений')
    await openDictionarySubgroup(user, dictionaryPanel, 'Виды выплат')
    await openDictionarySubgroup(user, dictionaryPanel, 'Тарифы')

    await waitFor(() => expect(requestedLimits).toMatchObject({
      owners: 25,
      garages: 25,
      supplierGroups: 25,
      suppliers: 25,
      incomeTypes: 25,
      expenseTypes: 25,
      tariffs: 25,
    }))
  })

  it('does not call dictionary APIs when owner and garage forms fail client validation', async () => {
    const user = userEvent.setup()
    let createOwnerCalled = false
    let createGarageCalled = false
    const statefulDictionaryClient = createStatefulDictionaryClient()
    const dictionaryClient: DictionaryClient = {
      ...statefulDictionaryClient,
      createOwner: async (...args) => {
        createOwnerCalled = true
        return statefulDictionaryClient.createOwner(...args)
      },
      createGarage: async (...args) => {
        createGarageCalled = true
        return statefulDictionaryClient.createGarage(...args)
      },
    }
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    const addOwnerButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    let validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Фамилия владельца'), '   ')
    await user.type(within(validationDialog).getByLabelText('Имя владельца'), 'Петр')
    await user.type(within(validationDialog).getByLabelText('Телефон владельца'), '1')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите фамилию владельца.')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Проверьте телефон владельца.')).toBeInTheDocument()
    expect(createOwnerCalled).toBe(false)
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Владельцы' })).not.toBeInTheDocument())
    await waitFor(() => expect(addOwnerButton).toHaveFocus())

    await openDictionarySubgroup(user, dictionaryPanel, 'Гаражи')
    const addGarageButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Номер гаража'), '   ')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите номер гаража.')).toBeInTheDocument()
    expect(createGarageCalled).toBe(false)
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Гаражи' })).not.toBeInTheDocument())
    await waitFor(() => expect(addGarageButton).toHaveFocus())
    return

    await user.type(within(dictionaryPanel).getByLabelText('Фамилия владельца'), '   ')
    await user.type(within(dictionaryPanel).getByLabelText('Имя владельца'), 'Петр')
    await user.type(within(dictionaryPanel).getByLabelText('Телефон владельца'), '1')
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[0])

    expect(await within(dictionaryPanel).findByText('Проверьте владельца')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Укажите фамилию владельца.')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Проверьте телефон владельца.')).toBeInTheDocument()
    expect(createOwnerCalled).toBe(false)

    await user.type(within(dictionaryPanel).getByLabelText('Номер гаража'), '   ')
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[1])

    expect(await within(dictionaryPanel).findByText('Проверьте гараж')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Укажите номер гаража.')).toBeInTheDocument()
    expect(createGarageCalled).toBe(false)
  })

  it('searches garages by number or owner from dictionaries workspace', async () => {
    const user = userEvent.setup()
    let garageSearch: string | undefined
    const ivan = createOwner({ id: 'owner-1', lastName: 'Иванов', firstName: 'Иван' })
    const petr = createOwner({ id: 'owner-2', lastName: 'Петров', firstName: 'Петр' })
    const garage12 = createGarage({ id: 'garage-1', number: '12', ownerId: ivan.id, ownerName: ivan.fullName })
    const garage21 = createGarage({ id: 'garage-2', number: '21', ownerId: petr.id, ownerName: petr.fullName })
    const dictionaryClient = createDictionaryClient({
      getOwners: async () => [ivan, petr],
      getGarages: async (_token, search) => {
        garageSearch = search
        return search?.toLowerCase().includes('петров') ? [garage21] : [garage12, garage21]
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    await openDictionarySubgroup(user, dictionaryPanel, 'Гаражи')
    expect(within(dictionaryPanel).getByText('12')).toBeInTheDocument()
    await user.type(within(dictionaryPanel).getByLabelText('Поиск: Гаражи'), 'Петров')

    await waitFor(() => {
      expect(garageSearch).toBe('Петров')
    })
    expect(await within(dictionaryPanel).findByText('21')).toBeInTheDocument()
    expect(within(dictionaryPanel).queryByText('12')).not.toBeInTheDocument()

    await user.clear(within(dictionaryPanel).getByLabelText('Поиск: Гаражи'))

    await waitFor(() => {
      expect(garageSearch).toBeUndefined()
    })
    expect(await within(dictionaryPanel).findByText('12')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('21')).toBeInTheDocument()
  })

  it('opens garage balance history from dictionaries context menu', async () => {
    const user = userEvent.setup()
    let requestedGarageId: string | null = null
    let requestedPeriod: { monthFrom?: string; monthTo?: string } | null = null
    const history = createGarageBalanceHistory({
      garageId: 'garage-1',
      rows: [
        { accountingMonth: '2026-06-01', openingDebt: 100, accrualAmount: 500, incomeAmount: 200, closingDebt: 400 },
        { accountingMonth: '2026-07-01', openingDebt: 400, accrualAmount: 700, incomeAmount: 300, closingDebt: 800 },
      ],
    })
    const financeClient = createFinanceClient({
      getGarageBalanceHistory: async (_token, garageId, params) => {
        requestedGarageId = garageId
        requestedPeriod = params ?? null
        return history
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    await openDictionarySubgroup(user, dictionaryPanel, 'Гаражи')

    const garageRow = within(dictionaryPanel).getByText('12').closest('tr') as HTMLTableRowElement
    fireEvent.contextMenu(garageRow)
    await user.click(await screen.findByRole('menuitem', { name: 'История баланса' }))

    const dialog = await screen.findByRole('dialog', { name: 'Гараж 12' })
    const closeHistoryButton = within(dialog).getByRole('button', { name: 'Закрыть историю баланса' })
    await waitFor(() => expect(closeHistoryButton).toHaveFocus())
    expect(within(dialog).getByText('История баланса')).toBeInTheDocument()
    expect(within(dialog).getAllByText('Начислено').length).toBeGreaterThan(0)
    expect(within(dialog).getByText(/1\s200,00/)).toBeInTheDocument()
    expect(within(dialog).getAllByText('Поступило').length).toBeGreaterThan(0)
    expect(within(dialog).getAllByText('500,00').length).toBeGreaterThan(0)
    expect(within(dialog).getByText('07.2026')).toBeInTheDocument()
    expect(within(dialog).getAllByText('800,00').length).toBeGreaterThan(0)
    expect(requestedGarageId).toBe('garage-1')
    expect(requestedPeriod?.monthFrom).toMatch(/^\d{4}-\d{2}$/)
    expect(requestedPeriod?.monthTo).toMatch(/^\d{4}-\d{2}$/)
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Гараж 12' })).not.toBeInTheDocument())
    await waitFor(() => expect(garageRow).toHaveFocus())
    expect(within(dictionaryPanel).getByRole('table', { name: 'Таблица: Гаражи' })).toBeInTheDocument()
  })

  it('searches suppliers by name or inn from dictionaries workspace', async () => {
    const user = userEvent.setup()
    let supplierSearch: string | undefined
    const group = createGroup({ id: 'group-1', name: 'Коммунальные услуги' })
    const waterSupplier = createSupplier({ id: 'supplier-1', name: 'Водоканал', groupId: group.id, groupName: group.name, inn: '5401' })
    const bankSupplier = createSupplier({ id: 'supplier-2', name: 'Альфа-Банк', groupId: group.id, groupName: group.name, inn: '7728' })
    const dictionaryClient = createDictionaryClient({
      getSupplierGroups: async () => [group],
      getSuppliers: async (_token, _groupId, search) => {
        supplierSearch = search
        return search?.includes('7728') ? [bankSupplier] : [waterSupplier, bankSupplier]
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    await openDictionarySubgroup(user, dictionaryPanel, 'Поставщики')
    expect(within(dictionaryPanel).getByText('Водоканал')).toBeInTheDocument()
    await user.type(within(dictionaryPanel).getByLabelText('Поиск: Поставщики и персонал'), '7728')

    await waitFor(() => {
      expect(supplierSearch).toBe('7728')
    })
    expect(await within(dictionaryPanel).findByText('Альфа-Банк')).toBeInTheDocument()
    expect(within(dictionaryPanel).queryByText('Водоканал')).not.toBeInTheDocument()

    await user.clear(within(dictionaryPanel).getByLabelText('Поиск: Поставщики и персонал'))

    await waitFor(() => {
      expect(supplierSearch).toBeUndefined()
    })
    expect(await within(dictionaryPanel).findByText('Водоканал')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Альфа-Банк')).toBeInTheDocument()
  })

  it('searches supplier groups and operation types from dictionaries workspace', async () => {
    const user = userEvent.setup()
    let groupSearch: string | undefined
    let incomeSearch: string | undefined
    let expenseSearch: string | undefined
    const utilityGroup = createGroup({ id: 'group-1', name: 'Коммунальные услуги' })
    const bankGroup = createGroup({ id: 'group-2', name: 'Банковские услуги' })
    const membershipType = createAccountingType({ id: 'income-type-1', name: 'Членский взнос', code: 'membership_fee' })
    const targetType = createAccountingType({ id: 'income-type-2', name: 'Целевой сбор', code: 'target_fee' })
    const electricityExpense = createAccountingType({ id: 'expense-type-1', name: 'Электроэнергия поставщику', code: 'electricity_supplier' })
    const salaryExpense = createAccountingType({ id: 'expense-type-2', name: 'Зарплата бухгалтера', code: 'salary_accountant' })
    const dictionaryClient = createDictionaryClient({
      getSupplierGroups: async (_token, search) => {
        groupSearch = search
        return search?.toLocaleLowerCase('ru-RU').includes('банк') ? [bankGroup] : [utilityGroup, bankGroup]
      },
      getIncomeTypes: async (_token, search) => {
        incomeSearch = search
        return search?.toLocaleLowerCase('ru-RU').includes('target') ? [targetType] : [membershipType, targetType]
      },
      getExpenseTypes: async (_token, search) => {
        expenseSearch = search
        return search?.toLocaleLowerCase('ru-RU').includes('электро') ? [electricityExpense] : [electricityExpense, salaryExpense]
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    await openDictionarySubgroup(user, dictionaryPanel, 'Группы поставщиков')
    await user.type(within(dictionaryPanel).getByLabelText('Поиск: Группы поставщиков и персонала'), 'банк')
    await waitFor(() => expect(groupSearch).toBe('банк'))
    expect(await within(dictionaryPanel).findByText('Банковские услуги')).toBeInTheDocument()
    expect(within(dictionaryPanel).queryByText('Коммунальные услуги')).not.toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Виды поступлений')
    const incomeSearchInput = within(dictionaryPanel).getByLabelText('Поиск: Виды поступлений')
    await user.clear(incomeSearchInput)
    await user.type(incomeSearchInput, 'target')
    await waitFor(() => expect(incomeSearch).toBe('target'))
    expect(await within(dictionaryPanel).findByText('Целевой сбор')).toBeInTheDocument()
    expect(within(dictionaryPanel).queryByText('Членский взнос')).not.toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Виды выплат')
    const expenseSearchInput = within(dictionaryPanel).getByLabelText('Поиск: Виды выплат')
    await user.clear(expenseSearchInput)
    await user.type(expenseSearchInput, 'электро')
    await waitFor(() => expect(expenseSearch).toBe('электро'))
    expect(await within(dictionaryPanel).findByText('Электроэнергия поставщику')).toBeInTheDocument()
    expect(within(dictionaryPanel).queryByText('Зарплата бухгалтера')).not.toBeInTheDocument()
  })

  it('archives owner from dictionaries workspace', async () => {
    const user = userEvent.setup()
    let archivedOwnerId: string | null = null
    let archiveReason: string | null = null
    const dictionaryClient = createDictionaryClient({
      archiveOwner: async (_token, id, reason) => {
        archivedOwnerId = id
        archiveReason = reason
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    const ownerRow = within(dictionaryPanel).getByText('Иванов Иван').closest('tr')!
    fireEvent.contextMenu(ownerRow)
    await user.click(await screen.findByRole('menuitem', { name: 'Удалить' }))
    expect(archivedOwnerId).toBeNull()
    const deleteDialog = await screen.findByRole('dialog', { name: 'Подтвердите удаление' })
    expect(within(deleteDialog).getByText('Запись будет скрыта из рабочих таблиц, но останется в истории изменений и связанной финансовой истории.')).toBeInTheDocument()
    const deleteCancelButton = within(deleteDialog).getByRole('button', { name: 'Отмена' })
    const deleteConfirmButton = within(deleteDialog).getByRole('button', { name: 'Удалить запись' })
    const deleteCloseButton = within(deleteDialog).getByRole('button', { name: 'Отменить удаление' })
    const deleteReasonInput = within(deleteDialog).getByLabelText('Причина удаления')
    expect(deleteConfirmButton).toBeDisabled()
    await waitFor(() => expect(deleteCancelButton).toHaveFocus())
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(deleteReasonInput).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(deleteCancelButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(deleteCloseButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(deleteReasonInput).toHaveFocus()
    await user.keyboard('{Escape}')
    expect(archivedOwnerId).toBeNull()
    expect(screen.queryByRole('dialog', { name: 'Подтвердите удаление' })).not.toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Иванов Иван')).toBeInTheDocument()

    fireEvent.contextMenu(ownerRow)
    await user.click(await screen.findByRole('menuitem', { name: 'Удалить' }))
    const cancelDialog = await screen.findByRole('dialog', { name: 'Подтвердите удаление' })
    await user.click(within(cancelDialog).getByRole('button', { name: 'Отмена' }))
    expect(archivedOwnerId).toBeNull()
    expect(screen.queryByRole('dialog', { name: 'Подтвердите удаление' })).not.toBeInTheDocument()

    fireEvent.contextMenu(ownerRow)
    await user.click(await screen.findByRole('menuitem', { name: 'Удалить' }))
    const confirmDialog = await screen.findByRole('dialog', { name: 'Подтвердите удаление' })
    const confirmButton = within(confirmDialog).getByRole('button', { name: 'Удалить запись' })
    expect(confirmButton).toBeDisabled()
    await user.type(within(confirmDialog).getByLabelText('Причина удаления'), 'Дубликат владельца')
    await user.click(confirmButton)
    expect(archivedOwnerId).toBe('owner-1')
    expect(archiveReason).toBe('Дубликат владельца')
    expect(await screen.findByText('Запись удалена из рабочего списка.')).toBeInTheDocument()
  })

  it('shows archived dictionary records and restores them after confirmation', async () => {
    const user = userEvent.setup()
    const activeOwner = createOwner({ id: 'owner-active', lastName: 'Иванов', firstName: 'Иван' })
    const archivedOwner = createOwner({ id: 'owner-archived', lastName: 'Петров', firstName: 'Петр', isArchived: true })
    let owners = [activeOwner, archivedOwner]
    let restoredOwnerId: string | null = null
    const dictionaryClient = createDictionaryClient({
      getOwners: async (_token, _search, _limit, includeArchived) => owners.filter((owner) => includeArchived || !owner.isArchived),
      restoreOwner: async (_token, id) => {
        restoredOwnerId = id
        owners = owners.map((owner) => owner.id === id ? { ...owner, isArchived: false } : owner)
        return owners.find((owner) => owner.id === id)!
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    expect(await within(dictionaryPanel).findByText('Иванов Иван')).toBeInTheDocument()
    expect(within(dictionaryPanel).queryByText('Петров Петр')).not.toBeInTheDocument()

    await user.click(within(dictionaryPanel).getByLabelText('Показывать архивные'))
    expect(await within(dictionaryPanel).findByText('Петров Петр')).toBeInTheDocument()
    const archivedRow = within(dictionaryPanel).getByText('Петров Петр').closest('tr')!
    expect(within(archivedRow).getByText('Архив')).toBeInTheDocument()

    const restoreButton = within(archivedRow).getByRole('button', { name: 'Вернуть' })
    await user.click(restoreButton)
    expect(restoredOwnerId).toBeNull()
    const restoreDialog = await screen.findByRole('dialog', { name: 'Вернуть запись из архива?' })
    expect(within(restoreDialog).getByText('Запись снова появится в рабочих списках. Действие будет записано в историю изменений.')).toBeInTheDocument()
    const restoreCancelButton = within(restoreDialog).getByRole('button', { name: 'Отмена' })
    const restoreConfirmButton = within(restoreDialog).getByRole('button', { name: 'Вернуть запись' })
    const restoreCloseButton = within(restoreDialog).getByRole('button', { name: 'Отменить восстановление' })
    await waitFor(() => expect(restoreCancelButton).toHaveFocus())
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(restoreCloseButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(restoreCancelButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(restoreConfirmButton).toHaveFocus()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Вернуть запись из архива?' })).not.toBeInTheDocument()
    expect(restoredOwnerId).toBeNull()
    expect(restoreButton).toHaveFocus()

    await user.click(restoreButton)
    const reopenedRestoreDialog = await screen.findByRole('dialog', { name: 'Вернуть запись из архива?' })
    await user.click(within(reopenedRestoreDialog).getByRole('button', { name: 'Вернуть запись' }))

    expect(restoredOwnerId).toBe('owner-archived')
    expect(await screen.findByText('Запись восстановлена и снова доступна в рабочих списках.')).toBeInTheDocument()
    const restoredRow = within(dictionaryPanel).getByText('Петров Петр').closest('tr')!
    expect(within(restoredRow).getByText('Активна')).toBeInTheDocument()
  })

  it('shows a clear conflict message when archived garage restore collides with an active number', async () => {
    const user = userEvent.setup()
    const activeGarage = createGarage({ id: 'garage-active', number: '12' })
    const archivedGarage = createGarage({ id: 'garage-archived', number: '12', isArchived: true })
    const restoreGarage = vi.fn(async () => {
      throw new DictionaryApiError('garage_number_duplicate', 'Raw duplicate message from backend.', 409)
    })
    const dictionaryClient = createDictionaryClient({
      getGarages: async (_token, _search, _limit, includeArchived) => [activeGarage, archivedGarage].filter((garage) => includeArchived || !garage.isArchived),
      restoreGarage,
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    await openDictionarySubgroup(user, dictionaryPanel, 'Гаражи')

    await user.click(within(dictionaryPanel).getByLabelText('Показывать архивные'))
    const archivedRow = (await within(dictionaryPanel).findAllByText('12'))
      .map((node) => node.closest('tr'))
      .find((row): row is HTMLTableRowElement => row !== null && within(row).queryByText('Архив') !== null)
    expect(archivedRow).toBeTruthy()

    await user.click(within(archivedRow!).getByRole('button', { name: 'Вернуть' }))
    const restoreDialog = await screen.findByRole('dialog', { name: 'Вернуть запись из архива?' })
    await user.click(within(restoreDialog).getByRole('button', { name: 'Вернуть запись' }))

    expect(restoreGarage).toHaveBeenCalledWith(expect.any(String), 'garage-archived')
    expect(await screen.findAllByText('Гараж нельзя восстановить: активный гараж с таким номером уже есть. Проверьте рабочий список и архив.')).not.toHaveLength(0)
    expect(screen.queryByText('Raw duplicate message from backend.')).not.toBeInTheDocument()
  })

  it.each([
    {
      name: 'supplier group duplicate',
      subgroup: 'Группы поставщиков',
      rowText: 'Коммунальные услуги',
      archivedId: 'supplier-group-archived',
      expectedMessage: 'Группу поставщиков нельзя восстановить: активная группа с таким названием уже есть.',
      client: () => {
        const activeGroup = createGroup({ id: 'supplier-group-active', name: 'Коммунальные услуги' })
        const archivedGroup = createGroup({ id: 'supplier-group-archived', name: 'Коммунальные услуги', isArchived: true })
        const restoreSupplierGroup = vi.fn(async () => {
          throw new DictionaryApiError('supplier_group_duplicate', 'Raw duplicate message from backend.', 409)
        })

        return {
          dictionaryClient: createDictionaryClient({
            getSupplierGroups: async (_token, _search, _limit, includeArchived) => [activeGroup, archivedGroup].filter((group) => includeArchived || !group.isArchived),
            restoreSupplierGroup,
          }),
          restore: restoreSupplierGroup,
        }
      },
    },
    {
      name: 'supplier archived group',
      subgroup: 'Поставщики',
      rowText: 'Водоканал',
      archivedId: 'supplier-archived',
      expectedMessage: 'Поставщика нельзя восстановить: сначала верните его группу поставщиков.',
      client: () => {
        const group = createGroup({ id: 'supplier-group-1', name: 'Коммунальные услуги' })
        const activeSupplier = createSupplier({ id: 'supplier-active', name: 'Водоканал', groupId: group.id, groupName: group.name })
        const archivedSupplier = createSupplier({ id: 'supplier-archived', name: 'Водоканал', groupId: group.id, groupName: group.name, isArchived: true })
        const restoreSupplier = vi.fn(async () => {
          throw new DictionaryApiError('supplier_group_not_found', 'Raw duplicate message from backend.', 409)
        })

        return {
          dictionaryClient: createDictionaryClient({
            getSupplierGroups: async () => [group],
            getSuppliers: async (_token, _groupId, _search, _limit, includeArchived) => [activeSupplier, archivedSupplier].filter((supplier) => includeArchived || !supplier.isArchived),
            restoreSupplier,
          }),
          restore: restoreSupplier,
        }
      },
    },
    {
      name: 'income type duplicate',
      subgroup: 'Виды поступлений',
      rowText: 'Членский взнос',
      archivedId: 'income-type-archived',
      expectedMessage: 'Вид поступления нельзя восстановить: активный вид с таким названием уже есть.',
      client: () => {
        const activeType = createAccountingType({ id: 'income-type-active', name: 'Членский взнос', code: 'membership' })
        const archivedType = createAccountingType({ id: 'income-type-archived', name: 'Членский взнос', code: 'membership_old', isArchived: true })
        const restoreIncomeType = vi.fn(async () => {
          throw new DictionaryApiError('income_type_duplicate', 'Raw duplicate message from backend.', 409)
        })

        return {
          dictionaryClient: createDictionaryClient({
            getIncomeTypes: async (_token, _search, _limit, includeArchived) => [activeType, archivedType].filter((type) => includeArchived || !type.isArchived),
            restoreIncomeType,
          }),
          restore: restoreIncomeType,
        }
      },
    },
    {
      name: 'expense type duplicate',
      subgroup: 'Виды выплат',
      rowText: 'Электроэнергия',
      archivedId: 'expense-type-archived',
      expectedMessage: 'Вид выплаты нельзя восстановить: активный вид с таким названием уже есть.',
      client: () => {
        const activeType = createAccountingType({ id: 'expense-type-active', name: 'Электроэнергия', code: 'electricity' })
        const archivedType = createAccountingType({ id: 'expense-type-archived', name: 'Электроэнергия', code: 'electricity_old', isArchived: true })
        const restoreExpenseType = vi.fn(async () => {
          throw new DictionaryApiError('expense_type_duplicate', 'Raw duplicate message from backend.', 409)
        })

        return {
          dictionaryClient: createDictionaryClient({
            getExpenseTypes: async (_token, _search, _limit, includeArchived) => [activeType, archivedType].filter((type) => includeArchived || !type.isArchived),
            restoreExpenseType,
          }),
          restore: restoreExpenseType,
        }
      },
    },
    {
      name: 'tariff duplicate',
      subgroup: 'Тарифы',
      rowText: 'Тариф воды',
      archivedId: 'tariff-archived',
      expectedMessage: 'Тариф нельзя восстановить: активный тариф с таким названием и датой начала уже есть.',
      client: () => {
        const activeTariff = createTariff({ id: 'tariff-active', name: 'Тариф воды', calculationBase: 'meter_water', rate: 50, effectiveFrom: '2026-07-01' })
        const archivedTariff = createTariff({ id: 'tariff-archived', name: 'Тариф воды', calculationBase: 'meter_water', rate: 45, effectiveFrom: '2026-07-01', isArchived: true })
        const restoreTariff = vi.fn(async () => {
          throw new DictionaryApiError('tariff_duplicate', 'Raw duplicate message from backend.', 409)
        })

        return {
          dictionaryClient: createDictionaryClient({
            getTariffs: async (_token, _search, _limit, includeArchived) => [activeTariff, archivedTariff].filter((tariff) => includeArchived || !tariff.isArchived),
            restoreTariff,
          }),
          restore: restoreTariff,
        }
      },
    },
  ])('shows clear conflict message for $name restore conflicts', async ({ subgroup, rowText, archivedId, expectedMessage, client }) => {
    const user = userEvent.setup()
    const { dictionaryClient, restore } = client()
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    await openDictionarySubgroup(user, dictionaryPanel, subgroup)

    await user.click(within(dictionaryPanel).getByLabelText('Показывать архивные'))
    const archivedRow = (await within(dictionaryPanel).findAllByText(rowText))
      .map((node) => node.closest('tr'))
      .find((row): row is HTMLTableRowElement => row !== null && within(row).queryByText('Архив') !== null)
    expect(archivedRow).toBeTruthy()

    await user.click(within(archivedRow!).getByRole('button', { name: 'Вернуть' }))
    const restoreDialog = await screen.findByRole('dialog', { name: 'Вернуть запись из архива?' })
    await user.click(within(restoreDialog).getByRole('button', { name: 'Вернуть запись' }))

    expect(restore).toHaveBeenCalledWith(expect.any(String), archivedId)
    expect(await screen.findAllByText(expectedMessage)).not.toHaveLength(0)
    expect(screen.queryByText('Raw duplicate message from backend.')).not.toBeInTheDocument()
  })

  it('confirms owner dictionary edits with before and after values', async () => {
    const user = userEvent.setup()
    let owner = createOwner({ id: 'owner-1', lastName: 'Иванов', firstName: 'Иван', phone: '+7 900' })
    const updateOwner = vi.fn(async (_token, id, request) => {
      owner = createOwner({
        id,
        lastName: request.lastName,
        firstName: request.firstName,
        middleName: request.middleName ?? null,
        phone: request.phone ?? null,
        address: request.address ?? null,
        meterNotes: request.meterNotes ?? null,
      })
      return owner
    })
    const dictionaryClient = createDictionaryClient({
      getOwners: async () => [owner],
      getGarages: async () => [],
      updateOwner,
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    const ownerRow = within(dictionaryPanel).getByText('Иванов Иван').closest('tr')!

    fireEvent.doubleClick(ownerRow)
    let editorDialog = await screen.findByRole('dialog', { name: 'Владельцы' })
    await user.clear(within(editorDialog).getByLabelText('Телефон владельца'))
    await user.type(within(editorDialog).getByLabelText('Телефон владельца'), '+7 901')
    const editorSaveButton = within(editorDialog).getByRole('button', { name: 'Сохранить' })
    await user.click(editorSaveButton)

    expect(updateOwner).not.toHaveBeenCalled()
    const confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения' })
    expect(within(confirmationDialog).getByText('Телефон')).toBeInTheDocument()
    expect(within(confirmationDialog).getByText('+7 900')).toBeInTheDocument()
    expect(within(confirmationDialog).getByText('+7 901')).toBeInTheDocument()

    const confirmationCancelButton = within(confirmationDialog).getByRole('button', { name: 'Отмена' })
    const confirmationSaveButton = within(confirmationDialog).getByRole('button', { name: 'Сохранить изменения' })
    const confirmationCloseButton = within(confirmationDialog).getByRole('button', { name: 'Отменить подтверждение изменений' })
    await waitFor(() => expect(confirmationCancelButton).toHaveFocus())
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(confirmationCloseButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(confirmationCancelButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(confirmationSaveButton).toHaveFocus()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения' })).not.toBeInTheDocument()
    await waitFor(() => expect(editorSaveButton).toHaveFocus())
    expect(updateOwner).not.toHaveBeenCalled()

    editorDialog = screen.getByRole('dialog', { name: 'Владельцы' })
    await user.click(within(editorDialog).getByRole('button', { name: 'Сохранить' }))
    const secondConfirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения' })
    await user.click(within(secondConfirmationDialog).getByRole('button', { name: 'Сохранить изменения' }))

    await waitFor(() => expect(updateOwner).toHaveBeenCalledTimes(1))
    expect(updateOwner.mock.calls[0][1]).toBe('owner-1')
    expect(updateOwner.mock.calls[0][2].phone).toBe('+7 901')
    expect(await screen.findByText('Изменения сохранены.')).toBeInTheDocument()
  })

  it('closes owner dictionary editor without api call when nothing changed', async () => {
    const user = userEvent.setup()
    const owner = createOwner({ id: 'owner-1', lastName: 'Иванов', firstName: 'Иван', phone: '+7 900' })
    const updateOwner = vi.fn(async () => owner)
    const dictionaryClient = createDictionaryClient({
      getOwners: async () => [owner],
      getGarages: async () => [],
      updateOwner,
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    const ownerRow = within(dictionaryPanel).getByText('Иванов Иван').closest('tr')!

    fireEvent.doubleClick(ownerRow)
    const editorDialog = await screen.findByRole('dialog', { name: 'Владельцы' })
    await user.click(within(editorDialog).getByRole('button', { name: 'Сохранить' }))

    expect(updateOwner).not.toHaveBeenCalled()
    expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения' })).not.toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Владельцы' })).not.toBeInTheDocument()
    expect(await screen.findByText('Изменений нет.')).toBeInTheDocument()
  })

  it('adds income type, expense type and tariff from dictionaries workspace', async () => {
    const user = userEvent.setup()
    const dictionaryClient = createStatefulDictionaryClient()
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    await openDictionarySubgroup(user, dictionaryPanel, 'Виды поступлений')
    const addIncomeTypeButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    await user.click(addIncomeTypeButton)
    let typeDialog = await screen.findByRole('dialog')
    await waitFor(() => expect(within(typeDialog).getByRole('button', { name: 'Закрыть окно справочника' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Виды поступлений' })).not.toBeInTheDocument())
    await waitFor(() => expect(addIncomeTypeButton).toHaveFocus())
    expect(within(dictionaryPanel).queryByText('Целевой взнос')).not.toBeInTheDocument()

    await user.click(addIncomeTypeButton)
    typeDialog = await screen.findByRole('dialog')
    await user.type(within(typeDialog).getByLabelText('Название вида операции'), 'Целевой взнос')
    await user.type(within(typeDialog).getByLabelText('Код вида операции'), 'target')
    await user.click(within(typeDialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dictionaryPanel).findByText('Целевой взнос')).toBeInTheDocument()
    await waitFor(() => expect(addIncomeTypeButton).toHaveFocus())

    await openDictionarySubgroup(user, dictionaryPanel, 'Виды выплат')
    const addExpenseTypeButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    typeDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await waitFor(() => expect(within(typeDialog).getByRole('button', { name: 'Закрыть окно справочника' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Виды выплат' })).not.toBeInTheDocument())
    await waitFor(() => expect(addExpenseTypeButton).toHaveFocus())
    expect(within(dictionaryPanel).queryByText('Вывоз мусора')).not.toBeInTheDocument()

    typeDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(typeDialog).getByLabelText('Название вида операции'), 'Вывоз мусора')
    await user.type(within(typeDialog).getByLabelText('Код вида операции'), 'trash')
    await user.click(within(typeDialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dictionaryPanel).findByText('Вывоз мусора')).toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Тарифы')
    const addTariffButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    let tariffDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await waitFor(() => expect(within(tariffDialog).getByRole('button', { name: 'Закрыть окно справочника' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Тарифы' })).not.toBeInTheDocument())
    await waitFor(() => expect(addTariffButton).toHaveFocus())
    expect(within(dictionaryPanel).queryByText('Мусор')).not.toBeInTheDocument()

    tariffDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(tariffDialog).getByLabelText('Название тарифа'), 'Мусор')
    await user.selectOptions(within(tariffDialog).getByLabelText('База расчета тарифа'), 'people')
    await user.clear(within(tariffDialog).getByLabelText('Ставка тарифа'))
    await user.type(within(tariffDialog).getByLabelText('Ставка тарифа'), '150')
    await user.click(within(tariffDialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dictionaryPanel).findByText('Мусор')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('150,00')).toBeInTheDocument()
    return

    await user.type(within(dictionaryPanel).getByLabelText('Название вида поступления'), 'Целевой взнос')
    await user.type(within(dictionaryPanel).getByLabelText('Код вида поступления'), 'target')
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[3])
    expect(await within(dictionaryPanel).findByText('Целевой взнос')).toBeInTheDocument()

    await user.type(within(dictionaryPanel).getByLabelText('Название вида выплаты'), 'Вывоз мусора')
    await user.type(within(dictionaryPanel).getByLabelText('Код вида выплаты'), 'trash')
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[4])
    expect(await within(dictionaryPanel).findByText('Вывоз мусора')).toBeInTheDocument()

    await user.type(within(dictionaryPanel).getByLabelText('Название тарифа'), 'Мусор')
    await user.selectOptions(within(dictionaryPanel).getByLabelText('База расчета тарифа'), 'people')
    await user.clear(within(dictionaryPanel).getByLabelText('Ставка тарифа'))
    await user.type(within(dictionaryPanel).getByLabelText('Ставка тарифа'), '150')
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[5])
    expect(await within(dictionaryPanel).findByText('Мусор')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('150,00 с 01.07.2026')).toBeInTheDocument()
  })

  it('does not call dictionary APIs when supplier and finance dictionary forms fail client validation', async () => {
    const user = userEvent.setup()
    let createSupplierGroupCalls = 0
    let createSupplierCalled = false
    let createIncomeTypeCalled = false
    let createExpenseTypeCalled = false
    let createTariffCalled = false
    const statefulDictionaryClient = createStatefulDictionaryClient()
    const dictionaryClient: DictionaryClient = {
      ...statefulDictionaryClient,
      createSupplierGroup: async (...args) => {
        createSupplierGroupCalls += 1
        return statefulDictionaryClient.createSupplierGroup(...args)
      },
      createSupplier: async (...args) => {
        createSupplierCalled = true
        return statefulDictionaryClient.createSupplier(...args)
      },
      createIncomeType: async (...args) => {
        createIncomeTypeCalled = true
        return statefulDictionaryClient.createIncomeType(...args)
      },
      createExpenseType: async (...args) => {
        createExpenseTypeCalled = true
        return statefulDictionaryClient.createExpenseType(...args)
      },
      createTariff: async (...args) => {
        createTariffCalled = true
        return statefulDictionaryClient.createTariff(...args)
      },
    }
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Справочники')
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    await openDictionarySubgroup(user, dictionaryPanel, 'Группы поставщиков')
    const addSupplierGroupButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    let validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Группа поставщиков'), '   ')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите группу поставщиков.')).toBeInTheDocument()
    expect(createSupplierGroupCalls).toBe(0)
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Группы поставщиков' })).not.toBeInTheDocument())
    await waitFor(() => expect(addSupplierGroupButton).toHaveFocus())
    expect(within(dictionaryPanel).queryByText('Связь')).not.toBeInTheDocument()

    validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Группа поставщиков'), 'Связь')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))
    expect(createSupplierGroupCalls).toBe(1)

    await openDictionarySubgroup(user, dictionaryPanel, 'Поставщики')
    const addSupplierButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Название поставщика'), '   ')
    await user.type(within(validationDialog).getByLabelText('ИНН поставщика'), 'abc')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите название поставщика.')).toBeInTheDocument()
    expect(within(validationDialog).getByText('ИНН поставщика должен содержать 10 или 12 цифр.')).toBeInTheDocument()
    expect(createSupplierCalled).toBe(false)
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Поставщики' })).not.toBeInTheDocument())
    await waitFor(() => expect(addSupplierButton).toHaveFocus())
    expect(within(dictionaryPanel).queryByText('abc')).not.toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Виды поступлений')
    const addIncomeTypeButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Название вида операции'), '   ')
    await user.type(within(validationDialog).getByLabelText('Код вида операции'), 'членский')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите название вида поступления.')).toBeInTheDocument()
    expect(createIncomeTypeCalled).toBe(false)
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Виды поступлений' })).not.toBeInTheDocument())
    await waitFor(() => expect(addIncomeTypeButton).toHaveFocus())
    expect(within(dictionaryPanel).queryByText('членский')).not.toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Виды выплат')
    const addExpenseTypeButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Название вида операции'), '   ')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите название вида выплаты.')).toBeInTheDocument()
    expect(createExpenseTypeCalled).toBe(false)
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Виды выплат' })).not.toBeInTheDocument())
    await waitFor(() => expect(addExpenseTypeButton).toHaveFocus())
    expect(within(dictionaryPanel).queryByText('Вывоз мусора')).not.toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Тарифы')
    const addTariffButton = within(dictionaryPanel).getByRole('button', { name: 'Добавить' })
    validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Название тарифа'), '   ')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите название тарифа.')).toBeInTheDocument()
    expect(createTariffCalled).toBe(false)
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Тарифы' })).not.toBeInTheDocument())
    await waitFor(() => expect(addTariffButton).toHaveFocus())
    return

    await user.type(within(dictionaryPanel).getByLabelText('Группа поставщиков'), '   ')
    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Добавить группу' }))

    expect(await within(dictionaryPanel).findByText('Проверьте группу поставщиков')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Укажите группу поставщиков.')).toBeInTheDocument()
    expect(createSupplierGroupCalls).toBe(0)

    await user.clear(within(dictionaryPanel).getByLabelText('Группа поставщиков'))
    await user.type(within(dictionaryPanel).getByLabelText('Группа поставщиков'), 'Связь')
    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Добавить группу' }))
    await screen.findByRole('option', { name: 'Связь' })

    await user.type(within(dictionaryPanel).getByLabelText('Название поставщика'), '   ')
    await user.type(within(dictionaryPanel).getByLabelText('ИНН поставщика'), 'abc')
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[2])

    expect(await within(dictionaryPanel).findByText('Проверьте поставщика')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Укажите название поставщика.')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('ИНН поставщика должен содержать 10 или 12 цифр.')).toBeInTheDocument()
    expect(createSupplierCalled).toBe(false)

    await user.type(within(dictionaryPanel).getByLabelText('Название вида поступления'), '   ')
    await user.type(within(dictionaryPanel).getByLabelText('Код вида поступления'), 'членский')
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[3])

    expect(await within(dictionaryPanel).findByText('Проверьте вид поступления')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Укажите название вида поступления.')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Код вида поступления должен содержать только латиницу, цифры, дефис или подчеркивание.')).toBeInTheDocument()
    expect(createIncomeTypeCalled).toBe(false)

    await user.type(within(dictionaryPanel).getByLabelText('Название вида выплаты'), '   ')
    await user.type(within(dictionaryPanel).getByLabelText('Код вида выплаты'), 'вода')
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[4])

    expect(await within(dictionaryPanel).findByText('Проверьте вид выплаты')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Укажите название вида выплаты.')).toBeInTheDocument()
    expect(createExpenseTypeCalled).toBe(false)

    await user.type(within(dictionaryPanel).getByLabelText('Название тарифа'), '   ')
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[5])

    expect(await within(dictionaryPanel).findByText('Проверьте тариф')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Укажите название тарифа.')).toBeInTheDocument()
    expect(createTariffCalled).toBe(false)
  })

  it('creates income and expense operations from payments workspace', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.clear(within(financePanel).getByLabelText('Сумма поступления'))
    await user.type(within(financePanel).getByLabelText('Сумма поступления'), '2000')
    await user.type(within(financePanel).getByLabelText('Документ поступления'), 'PKO-1')
    await user.type(within(financePanel).getByLabelText('Комментарий поступления'), 'Оплата за июнь')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Провести' })[0])

    expect(await within(financePanel).findByText('+2 000,00')).toBeInTheDocument()
    expect(within(financePanel).getAllByText('2 000,00').length).toBeGreaterThan(0)
    expect(within(financePanel).getByText('Оплата за июнь')).toBeInTheDocument()

    await user.clear(within(financePanel).getByLabelText('Сумма выплаты'))
    await user.type(within(financePanel).getByLabelText('Сумма выплаты'), '500')
    await user.type(within(financePanel).getByLabelText('Документ выплаты'), 'RKO-1')
    await user.type(within(financePanel).getByLabelText('Комментарий выплаты'), 'Оплата счета поставщика')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Провести' })[1])

    expect(await within(financePanel).findByText('-500,00')).toBeInTheDocument()
    expect(within(financePanel).getByText('1 500,00')).toBeInTheDocument()
    expect(within(financePanel).getByText('Переплата')).toBeInTheDocument()
    await user.click(within(financePanel).getByRole('tab', { name: /Расходы/ }))
    expect(within(financePanel).getByText('Оплата счета поставщика')).toBeInTheDocument()
  })

  it('searches garage by owner before creating income operation', async () => {
    const user = userEvent.setup()
    const defaultGarage = createGarage({ id: 'garage-1', number: '12', ownerName: 'Иванов Иван' })
    const foundGarage = createGarage({ id: 'garage-77', number: '77', ownerName: 'Петров Петр' })
    const garageSearches: Array<string | undefined> = []
    let incomeGarageId: string | null = null
    const dictionaryClient = createDictionaryClient({
      getGarages: async (_token, search) => {
        garageSearches.push(search)
        return search?.toLowerCase().includes('петров') ? [foundGarage] : [defaultGarage]
      },
    })
    const financeClient = createFinanceClient({
      createIncome: async (_token, request) => {
        incomeGarageId = request.garageId
        return createFinancialOperation({ id: 'income-search', garageId: request.garageId, garageNumber: foundGarage.number, ownerName: foundGarage.ownerName, amount: request.amount })
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.type(within(financePanel).getByLabelText('Поиск гаража для поступления'), 'Петров')
    await user.click(within(financePanel).getByRole('button', { name: 'Найти гараж для поступления' }))

    expect(await within(financePanel).findByText('Найдено гаражей: 1')).toBeInTheDocument()
    expect(within(financePanel).getByRole('option', { name: 'Гараж 77 - Петров Петр' })).toBeInTheDocument()
    expect(garageSearches).toContain('Петров')

    await user.clear(within(financePanel).getByLabelText('Сумма поступления'))
    await user.type(within(financePanel).getByLabelText('Сумма поступления'), '1000')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Провести' })[0])

    await waitFor(() => expect(incomeGarageId).toBe(foundGarage.id))
  })

  it('edits income operation from payments table', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.clear(within(financePanel).getByLabelText('Сумма поступления'))
    await user.type(within(financePanel).getByLabelText('Сумма поступления'), '2000')
    await user.type(within(financePanel).getByLabelText('Документ поступления'), 'PKO-edit')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Провести' })[0])

    const paymentCell = await within(financePanel).findByText('PKO-edit')
    const paymentRow = paymentCell.closest('tr')!
    paymentRow.focus()
    expect(paymentRow).toHaveFocus()
    await user.keyboard(' ')
    const dialog = await screen.findByRole('dialog', { name: 'Новое поступление' })
    expect(within(dialog).getByText('Изменение')).toBeInTheDocument()

    await user.clear(within(dialog).getByLabelText('Сумма поступления'))
    await user.type(within(dialog).getByLabelText('Сумма поступления'), '2400')
    await user.clear(within(dialog).getByLabelText('Документ поступления'))
    await user.type(within(dialog).getByLabelText('Документ поступления'), 'PKO-fixed')
    await user.type(within(dialog).getByLabelText('Комментарий поступления'), 'После сверки')
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))

    let paymentChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение платежа?' })
    const paymentChangeList = within(paymentChangeDialog).getByRole('list', { name: 'Изменяемые поля платежа' })
    expect(within(paymentChangeList).getByText('Сумма')).toBeInTheDocument()
    expect(within(paymentChangeList).getByText('2 000,00')).toBeInTheDocument()
    expect(within(paymentChangeList).getByText('2 400,00')).toBeInTheDocument()
    expect(within(paymentChangeList).getByText('Документ')).toBeInTheDocument()
    expect(within(paymentChangeList).getByText('PKO-fixed')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердить изменение платежа?' })).not.toBeInTheDocument())
    expect(screen.getByRole('dialog', { name: 'Новое поступление' })).toBeInTheDocument()
    paymentChangeDialog = await screen.findByRole('dialog', { name: 'Новое поступление' })
    await user.click(within(paymentChangeDialog).getByRole('button', { name: 'Сохранить' }))
    paymentChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение платежа?' })
    await user.click(within(paymentChangeDialog).getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Новое поступление' })).not.toBeInTheDocument())
    expect(await within(financePanel).findByText('PKO-fixed')).toBeInTheDocument()
    expect(within(financePanel).getByText('После сверки')).toBeInTheDocument()
    expect(within(financePanel).getAllByText('2 400,00').length).toBeGreaterThan(0)
    expect(within(financePanel).queryByText('PKO-edit')).not.toBeInTheDocument()
  })

  it('edits expense operation from payments table with confirmation', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    await user.click(within(financePanel).getByRole('tab', { name: /Расходы/ }))

    await user.clear(within(financePanel).getByLabelText('Сумма выплаты'))
    await user.type(within(financePanel).getByLabelText('Сумма выплаты'), '700')
    await user.type(within(financePanel).getByLabelText('Документ выплаты'), 'RKO-edit')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Провести' })[1])

    const expenseCell = await within(financePanel).findByText('RKO-edit')
    const expenseRow = expenseCell.closest('tr')!
    expenseRow.focus()
    await user.keyboard(' ')
    const dialog = await screen.findByRole('dialog', { name: 'Новая выплата' })
    expect(within(dialog).getByText('Изменение')).toBeInTheDocument()

    await user.clear(within(dialog).getByLabelText('Сумма выплаты'))
    await user.type(within(dialog).getByLabelText('Сумма выплаты'), '950')
    await user.clear(within(dialog).getByLabelText('Документ выплаты'))
    await user.type(within(dialog).getByLabelText('Документ выплаты'), 'RKO-fixed')
    await user.type(within(dialog).getByLabelText('Комментарий выплаты'), 'После сверки выплаты')
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))

    let expenseChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение платежа?' })
    const expenseChangeList = within(expenseChangeDialog).getByRole('list', { name: 'Изменяемые поля платежа' })
    expect(within(expenseChangeList).getByText('Сумма')).toBeInTheDocument()
    expect(within(expenseChangeList).getByText('700,00')).toBeInTheDocument()
    expect(within(expenseChangeList).getByText('950,00')).toBeInTheDocument()
    expect(within(expenseChangeList).getByText('Документ')).toBeInTheDocument()
    expect(within(expenseChangeList).getByText('RKO-fixed')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердить изменение платежа?' })).not.toBeInTheDocument())
    expect(screen.getByRole('dialog', { name: 'Новая выплата' })).toBeInTheDocument()
    expect(within(financePanel).queryByText('RKO-fixed')).not.toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    expenseChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение платежа?' })
    await user.click(within(expenseChangeDialog).getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Новая выплата' })).not.toBeInTheDocument())
    expect(await within(financePanel).findByText('RKO-fixed')).toBeInTheDocument()
    expect(within(financePanel).getByText('После сверки выплаты')).toBeInTheDocument()
    expect(within(financePanel).getAllByText('950,00').length).toBeGreaterThan(0)
    expect(within(financePanel).queryByText('RKO-edit')).not.toBeInTheDocument()
  })

  it('edits owner accrual from payments table with confirmation', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    await user.click(within(financePanel).getByRole('tab', { name: /Начисления владельцам/ }))

    await user.clear(within(financePanel).getByLabelText('Сумма начисления'))
    await user.type(within(financePanel).getByLabelText('Сумма начисления'), '1100')
    await user.type(within(financePanel).getByLabelText('Комментарий начисления'), 'Начисление edit')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Начислить' })[0])

    const accrualCell = await within(financePanel).findByText('Начисление edit')
    const accrualRow = accrualCell.closest('tr')!
    accrualRow.focus()
    await user.keyboard(' ')
    const dialog = await screen.findByRole('dialog', { name: 'Ручное начисление' })
    expect(within(dialog).getByText('Изменение')).toBeInTheDocument()

    await user.clear(within(dialog).getByLabelText('Сумма начисления'))
    await user.type(within(dialog).getByLabelText('Сумма начисления'), '1350')
    await user.clear(within(dialog).getByLabelText('Комментарий начисления'))
    await user.type(within(dialog).getByLabelText('Комментарий начисления'), 'Начисление после сверки')
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))

    let accrualChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение платежа?' })
    const accrualChangeList = within(accrualChangeDialog).getByRole('list', { name: 'Изменяемые поля платежа' })
    expect(within(accrualChangeList).getByText('Сумма')).toBeInTheDocument()
    expect(within(accrualChangeList).getByText('1 100,00')).toBeInTheDocument()
    expect(within(accrualChangeList).getByText('1 350,00')).toBeInTheDocument()
    expect(within(accrualChangeList).getByText('Комментарий')).toBeInTheDocument()
    expect(within(accrualChangeList).getByText('Начисление после сверки')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердить изменение платежа?' })).not.toBeInTheDocument())
    expect(screen.getByRole('dialog', { name: 'Ручное начисление' })).toBeInTheDocument()
    expect(within(financePanel).queryByText('Начисление после сверки')).not.toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    accrualChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение платежа?' })
    await user.click(within(accrualChangeDialog).getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Ручное начисление' })).not.toBeInTheDocument())
    expect(await within(financePanel).findByText('Начисление после сверки')).toBeInTheDocument()
    expect(within(financePanel).getAllByText('1 350,00').length).toBeGreaterThan(0)
    expect(within(financePanel).queryByText('Начисление edit')).not.toBeInTheDocument()
  })

  it('edits supplier accrual from payments table with confirmation', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    await user.click(within(financePanel).getByRole('tab', { name: /Начисления поставщикам/ }))

    await user.clear(within(financePanel).getByLabelText('Сумма начисления поставщику'))
    await user.type(within(financePanel).getByLabelText('Сумма начисления поставщику'), '650')
    await user.type(within(financePanel).getByLabelText('Документ начисления поставщику'), 'BILL-edit')
    await user.type(within(financePanel).getByLabelText('Комментарий начисления поставщику'), 'Начисление поставщику edit')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Начислить' })[1])

    const supplierAccrualCell = await within(financePanel).findByText('BILL-edit')
    const supplierAccrualRow = supplierAccrualCell.closest('tr')!
    supplierAccrualRow.focus()
    await user.keyboard(' ')
    const dialog = await screen.findByRole('dialog', { name: 'Начисление поставщику' })
    expect(within(dialog).getByText('Изменение')).toBeInTheDocument()

    await user.clear(within(dialog).getByLabelText('Сумма начисления поставщику'))
    await user.type(within(dialog).getByLabelText('Сумма начисления поставщику'), '820')
    await user.clear(within(dialog).getByLabelText('Документ начисления поставщику'))
    await user.type(within(dialog).getByLabelText('Документ начисления поставщику'), 'BILL-fixed')
    await user.clear(within(dialog).getByLabelText('Комментарий начисления поставщику'))
    await user.type(within(dialog).getByLabelText('Комментарий начисления поставщику'), 'Начисление поставщику после сверки')
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))

    let supplierAccrualChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение платежа?' })
    const supplierAccrualChangeList = within(supplierAccrualChangeDialog).getByRole('list', { name: 'Изменяемые поля платежа' })
    expect(within(supplierAccrualChangeList).getByText('Сумма')).toBeInTheDocument()
    expect(within(supplierAccrualChangeList).getByText('650,00')).toBeInTheDocument()
    expect(within(supplierAccrualChangeList).getByText('820,00')).toBeInTheDocument()
    expect(within(supplierAccrualChangeList).getByText('Документ')).toBeInTheDocument()
    expect(within(supplierAccrualChangeList).getByText('BILL-fixed')).toBeInTheDocument()
    expect(within(supplierAccrualChangeList).getByText('Комментарий')).toBeInTheDocument()
    expect(within(supplierAccrualChangeList).getByText('Начисление поставщику после сверки')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Подтвердить изменение платежа?' })).not.toBeInTheDocument())
    expect(screen.getByRole('dialog', { name: 'Начисление поставщику' })).toBeInTheDocument()
    expect(within(financePanel).queryByText('BILL-fixed')).not.toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    supplierAccrualChangeDialog = await screen.findByRole('dialog', { name: 'Подтвердить изменение платежа?' })
    await user.click(within(supplierAccrualChangeDialog).getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Начисление поставщику' })).not.toBeInTheDocument())
    expect(await within(financePanel).findByText('BILL-fixed')).toBeInTheDocument()
    expect(within(financePanel).getByText('Начисление поставщику после сверки')).toBeInTheDocument()
    expect(within(financePanel).getAllByText('820,00').length).toBeGreaterThan(0)
    expect(within(financePanel).queryByText('BILL-edit')).not.toBeInTheDocument()
  })

  it('opens new income dialog from payment context menu', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    const paymentActionButtons = [
      ...within(financePanel).getAllByRole('button', { name: 'Провести' }),
      ...within(financePanel).getAllByRole('button', { name: 'Начислить' }),
      within(financePanel).getByRole('button', { name: 'Создать месяц' }),
      within(financePanel).getByRole('button', { name: 'Внести' }),
    ]

    expect(within(financePanel).queryByRole('button', { name: 'Провести поступление' })).not.toBeInTheDocument()
    for (const button of paymentActionButtons) {
      expect(button.querySelector('svg')).not.toBeInTheDocument()
    }
    fireEvent.contextMenu(within(financePanel).getAllByText('Членский взнос')[0].closest('tr')!)
    const menu = await screen.findByRole('menu', { name: 'Операции с платежами' })
    expect(menu.querySelector('svg')).not.toBeInTheDocument()
    expect(within(menu).getByRole('menuitem', { name: 'Добавить' })).toBeEnabled()
    expect(within(menu).getByRole('menuitem', { name: 'Изменить' })).toBeEnabled()
    expect(within(menu).getByRole('menuitem', { name: 'Удалить' })).toBeEnabled()
    await user.click(within(menu).getByRole('menuitem', { name: 'Добавить' }))

    const dialog = await screen.findByRole('dialog', { name: 'Новое поступление' })
    expect(within(dialog).getByText('Платежи')).toBeInTheDocument()
    expect(within(dialog).queryByText('Изменение')).not.toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Провести' }).querySelector('svg')).not.toBeInTheDocument()

    await user.click(within(financePanel).getByRole('tab', { name: /Начисления владельцам/ }))
    expect(within(financePanel).getByRole('button', { name: 'Регулярные' }).querySelector('svg')).not.toBeInTheDocument()
    await user.click(within(financePanel).getByRole('tab', { name: /Начисления поставщикам/ }))
    expect(within(financePanel).getByRole('button', { name: 'Зарплата группы' }).querySelector('svg')).not.toBeInTheDocument()
  })

  it('warns before closing changed payment editor', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    fireEvent.contextMenu(within(financePanel).getAllByText('Членский взнос')[0].closest('tr')!)
    const menu = await screen.findByRole('menu', { name: 'Операции с платежами' })
    await user.click(within(menu).getByRole('menuitem', { name: 'Добавить' }))
    const dialog = await screen.findByRole('dialog', { name: 'Новое поступление' })

    await user.type(within(dialog).getByLabelText('Документ поступления'), 'PKO-draft')
    expect(within(dialog).getByText('Есть несохраненные изменения формы платежа.')).toHaveAttribute('role', 'status')
    expect(dialog).toHaveAccessibleDescription('Есть несохраненные изменения формы платежа.')
    await user.keyboard('{Escape}')
    let closeDialog = await screen.findByRole('dialog', { name: 'Закрыть форму без сохранения?' })
    expect(within(closeDialog).getByText('Закрыть форму платежа без сохранения изменений?')).toBeInTheDocument()
    await user.click(within(closeDialog).getByRole('button', { name: 'Остаться' }))
    expect(screen.getByRole('dialog', { name: 'Новое поступление' })).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Закрыть форму платежа' }))
    closeDialog = await screen.findByRole('dialog', { name: 'Закрыть форму без сохранения?' })
    await user.click(within(closeDialog).getByRole('button', { name: 'Остаться' }))
    expect(screen.getByRole('dialog', { name: 'Новое поступление' })).toBeInTheDocument()
    fireEvent.mouseDown(screen.getByTestId('finance-editor-backdrop'))
    closeDialog = await screen.findByRole('dialog', { name: 'Закрыть форму без сохранения?' })
    await user.click(within(closeDialog).getByRole('button', { name: 'Остаться' }))
    expect(screen.getByRole('dialog', { name: 'Новое поступление' })).toBeInTheDocument()
    const editorCancelButton = within(dialog).getByRole('button', { name: 'Отмена' })
    await user.click(editorCancelButton)
    closeDialog = await screen.findByRole('dialog', { name: 'Закрыть форму без сохранения?' })
    const stayInEditorButton = within(closeDialog).getByRole('button', { name: 'Остаться' })
    const discardDraftButton = within(closeDialog).getByRole('button', { name: 'Закрыть без сохранения' })
    const closeDraftConfirmationButton = within(closeDialog).getByRole('button', { name: 'Остаться в форме платежа' })
    await waitFor(() => expect(stayInEditorButton).toHaveFocus())
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(closeDraftConfirmationButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(stayInEditorButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(discardDraftButton).toHaveFocus()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Закрыть форму без сохранения?' })).not.toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Новое поступление' })).toBeInTheDocument()
    await waitFor(() => expect(editorCancelButton).toHaveFocus())

    await user.click(editorCancelButton)
    closeDialog = await screen.findByRole('dialog', { name: 'Закрыть форму без сохранения?' })
    await user.click(within(closeDialog).getByRole('button', { name: 'Остаться' }))
    expect(screen.getByRole('dialog', { name: 'Новое поступление' })).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Документ поступления')).toHaveValue('PKO-draft')

    await user.click(editorCancelButton)
    closeDialog = await screen.findByRole('dialog', { name: 'Закрыть форму без сохранения?' })
    await user.click(within(closeDialog).getByRole('button', { name: 'Закрыть без сохранения' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Новое поступление' })).not.toBeInTheDocument())
  })

  it('keeps changed payment editor drafts for every editor type', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    const tableArea = await within(financePanel).findByRole('group', { name: 'Рабочая область платежной таблицы' })

    async function selectFinanceTab(name: RegExp) {
      const tab = within(financePanel).getByRole('tab', { name })
      await user.click(tab)
      await waitFor(() => expect(tab).toHaveAttribute('aria-selected', 'true'))
    }

    async function openCreateDialogFromTable(tabName: RegExp) {
      await selectFinanceTab(tabName)
      fireEvent.contextMenu(tableArea)
      const menu = await screen.findByRole('menu', { name: 'Операции с платежами' })
      await user.click(within(menu).getByRole('menuitem', { name: 'Добавить' }))
    }

    const cases = [
      { dialog: 'Новая выплата', field: 'Документ выплаты', draft: 'RKO-draft', open: () => openCreateDialogFromTable(/Расходы/) },
      { dialog: 'Ручное начисление', field: 'Комментарий к начислению', draft: 'Начисление draft', open: () => openCreateDialogFromTable(/Начисления владельцам/) },
      {
        dialog: 'Регулярные начисления',
        field: 'Комментарий к регулярному начислению',
        draft: 'Регулярное draft',
        open: async () => {
          await selectFinanceTab(/Начисления владельцам/)
          await user.click(within(financePanel).getByRole('button', { name: 'Регулярные' }))
        },
      },
      { dialog: 'Начисление поставщику', field: 'Документ начисления поставщику', draft: 'BILL-draft', open: () => openCreateDialogFromTable(/Начисления поставщикам/) },
      {
        dialog: 'Зарплата группы',
        field: 'Документ зарплаты',
        draft: 'SALARY-draft',
        open: async () => {
          await selectFinanceTab(/Начисления поставщикам/)
          await user.click(within(financePanel).getByRole('button', { name: 'Зарплата группы' }))
        },
      },
      { dialog: 'Показание счетчика', field: 'Комментарий к показанию', draft: 'Счетчик draft', open: () => openCreateDialogFromTable(/Счетчики/) },
    ]

    for (const item of cases) {
      await item.open()
      const dialog = await screen.findByRole('dialog', { name: item.dialog })
      const field = within(dialog).getByLabelText(item.field)
      await user.type(field, item.draft)
      expect(within(dialog).getByText('Есть несохраненные изменения формы платежа.')).toHaveAttribute('role', 'status')
      const editorCancelButton = within(dialog).getByRole('button', { name: 'Отмена' })
      await user.click(editorCancelButton)
      let closeDialog = await screen.findByRole('dialog', { name: 'Закрыть форму без сохранения?' })
      await waitFor(() => expect(within(closeDialog).getByRole('button', { name: 'Остаться' })).toHaveFocus())
      await user.keyboard('{Escape}')
      expect(screen.queryByRole('dialog', { name: 'Закрыть форму без сохранения?' })).not.toBeInTheDocument()
      expect(screen.getByRole('dialog', { name: item.dialog })).toBeInTheDocument()
      await waitFor(() => expect(editorCancelButton).toHaveFocus())

      await user.click(editorCancelButton)
      closeDialog = await screen.findByRole('dialog', { name: 'Закрыть форму без сохранения?' })
      await waitFor(() => expect(within(closeDialog).getByRole('button', { name: 'Остаться' })).toHaveFocus())
      await user.click(within(closeDialog).getByRole('button', { name: 'Остаться' }))
      expect(screen.getByRole('dialog', { name: item.dialog })).toBeInTheDocument()
      expect(field).toHaveValue(item.draft)

      await user.click(editorCancelButton)
      closeDialog = await screen.findByRole('dialog', { name: 'Закрыть форму без сохранения?' })
      await user.click(within(closeDialog).getByRole('button', { name: 'Закрыть без сохранения' }))
      await waitFor(() => expect(screen.queryByRole('dialog', { name: item.dialog })).not.toBeInTheDocument())
    }
  }, 30_000)

  it('opens payment context menu from focused row keyboard shortcut', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    const paymentRow = within(financePanel).getAllByText('Членский взнос')[0].closest('tr')!

    paymentRow.focus()
    expect(paymentRow).toHaveFocus()
    fireEvent.keyDown(paymentRow, { key: 'F10', shiftKey: true })
    const menu = await screen.findByRole('menu', { name: 'Операции с платежами' })
    const addItem = within(menu).getByRole('menuitem', { name: 'Добавить' })
    await waitFor(() => expect(addItem).toHaveFocus())
    expect(addItem).toBeEnabled()
    const editItem = within(menu).getByRole('menuitem', { name: 'Изменить' })
    const deleteItem = within(menu).getByRole('menuitem', { name: 'Удалить' })
    expect(editItem).toBeEnabled()
    expect(deleteItem).toBeEnabled()
    await user.keyboard('{ArrowDown}')
    expect(editItem).toHaveFocus()
    await user.keyboard('{ArrowDown}')
    expect(deleteItem).toHaveFocus()
    await user.keyboard('{ArrowDown}')
    expect(addItem).toHaveFocus()
    await user.keyboard('{ArrowUp}')
    expect(deleteItem).toHaveFocus()

    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('menu', { name: 'Операции с платежами' })).not.toBeInTheDocument())
    await waitFor(() => expect(paymentRow).toHaveFocus())

    fireEvent.keyDown(paymentRow, { key: 'F10', shiftKey: true })
    const reopenedMenu = await screen.findByRole('menu', { name: 'Операции с платежами' })
    await user.click(within(reopenedMenu).getByRole('menuitem', { name: 'Изменить' }))
    const dialog = await screen.findByRole('dialog', { name: 'Новое поступление' })
    expect(within(dialog).getByText('Изменение')).toBeInTheDocument()
  })

  it('restores canceled meter reading from payment context menu', async () => {
    const user = userEvent.setup()
    const canceledReading = createMeterReading({
      id: 'meter-reading-canceled',
      garageNumber: '12',
      meterKind: 'electricity',
      accountingMonth: '2026-06-01',
      readingDate: '2026-06-20',
      previousValue: 100,
      currentValue: 128,
      consumption: 28,
      isCanceled: true,
      comment: 'Отменено: ошибочное показание',
    })
    const activeReading = { ...canceledReading, isCanceled: false, comment: null }
    let pageItems: MeterReadingDto[] = [canceledReading]
    const getMeterReadingsPage = vi.fn(async (_token: string, params?: Parameters<FinanceClient['getMeterReadingsPage']>[1]) => ({
      items: pageItems,
      totalCount: pageItems.length,
      offset: params?.offset ?? 0,
      limit: params?.limit ?? 25,
    }))
    const restoreMeterReading = vi.fn(async (_token: string, meterReadingId: string) => {
      pageItems = [activeReading]
      return { ...activeReading, id: meterReadingId }
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient({ getMeterReadingsPage, restoreMeterReading })} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.click(within(financePanel).getByRole('tab', { name: /Счетчики/ }))
    await waitFor(() => expect(getMeterReadingsPage).toHaveBeenCalledWith('token', expect.objectContaining({ limit: 25, offset: 0 })))
    const menu = await openFinanceContextMenuByCellText(financePanel, '28')
    expect(within(menu).getByRole('menuitem', { name: 'Изменить' })).toBeDisabled()
    expect(within(menu).getByRole('menuitem', { name: 'Удалить' })).toBeDisabled()
    const restoreItem = within(menu).getByRole('menuitem', { name: 'Вернуть' })
    expect(restoreItem).toBeEnabled()
    await user.click(restoreItem)

    let restoreDialog = await screen.findByRole('dialog', { name: 'Вернуть показание счетчика?' })
    await waitFor(() => expect(within(restoreDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Вернуть показание счетчика?' })).not.toBeInTheDocument())
    expect(restoreMeterReading).not.toHaveBeenCalled()

    const reopenedMenu = await openFinanceContextMenuByCellText(financePanel, '28')
    await user.click(within(reopenedMenu).getByRole('menuitem', { name: 'Вернуть' }))
    restoreDialog = await screen.findByRole('dialog', { name: 'Вернуть показание счетчика?' })
    await user.click(within(restoreDialog).getByRole('button', { name: 'Вернуть запись' }))
    await waitFor(() => expect(restoreMeterReading).toHaveBeenCalledWith('token', 'meter-reading-canceled'))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Вернуть показание счетчика?' })).not.toBeInTheDocument())
    await waitFor(() => expect(getMeterReadingsPage).toHaveBeenCalledTimes(3))
  })

  it('restores canceled payment and accrual records from payment context menu', async () => {
    const user = userEvent.setup()
    let operations: FinancialOperationDto[] = [
      createFinancialOperation({
        id: 'income-canceled',
        operationKind: 'income',
        amount: 111,
        documentNumber: 'PKO-restore',
        isCanceled: true,
        comment: 'Отменено: ошибочное поступление',
      }),
      createFinancialOperation({
        id: 'expense-canceled',
        operationKind: 'expense',
        amount: 222,
        documentNumber: 'RKO-restore',
        supplierName: 'Водоканал',
        expenseTypeName: 'Вода',
        isCanceled: true,
        comment: 'Отменено: ошибочная выплата',
      }),
    ]
    let accruals: AccrualDto[] = [
      createAccrual({
        id: 'accrual-canceled',
        amount: 333,
        comment: 'Начисление к возврату',
        isCanceled: true,
      }),
    ]
    let supplierAccruals: SupplierAccrualDto[] = [
      createSupplierAccrual({
        id: 'supplier-accrual-canceled',
        amount: 444,
        documentNumber: 'SUP-restore',
        comment: 'Начисление поставщику к возврату',
        isCanceled: true,
      }),
    ]
    const restoreOperation = vi.fn(async (_token: string, operationId: string) => {
      const operation = operations.find((item) => item.id === operationId)
      if (!operation) {
        throw new Error('Финансовая операция не найдена.')
      }

      const restored = { ...operation, isCanceled: false, comment: null }
      operations = operations.map((item) => (item.id === operationId ? restored : item))
      return restored
    })
    const restoreAccrual = vi.fn(async (_token: string, accrualId: string) => {
      const accrual = accruals.find((item) => item.id === accrualId)
      if (!accrual) {
        throw new Error('Начисление не найдено.')
      }

      const restored = { ...accrual, isCanceled: false, comment: null }
      accruals = accruals.map((item) => (item.id === accrualId ? restored : item))
      return restored
    })
    const restoreSupplierAccrual = vi.fn(async (_token: string, supplierAccrualId: string) => {
      const accrual = supplierAccruals.find((item) => item.id === supplierAccrualId)
      if (!accrual) {
        throw new Error('Начисление поставщику не найдено.')
      }

      const restored = { ...accrual, isCanceled: false, comment: null }
      supplierAccruals = supplierAccruals.map((item) => (item.id === supplierAccrualId ? restored : item))
      return restored
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient({
      getOperations: async () => operations,
      getAccruals: async () => accruals,
      getSupplierAccruals: async () => supplierAccruals,
      restoreOperation,
      restoreAccrual,
      restoreSupplierAccrual,
    })} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    async function restoreRow(options: { tabName?: RegExp; rowText: string; restoredText?: string; dialogName: string; assertRestored: () => void }) {
      if (options.tabName) {
        await user.click(within(financePanel).getByRole('tab', { name: options.tabName }))
      }

      expect(await within(financePanel).findByText(options.rowText)).toBeInTheDocument()
      const menu = await openFinanceContextMenuByCellText(financePanel, options.rowText)
      expect(within(menu).getByRole('menuitem', { name: 'Изменить' })).toBeDisabled()
      expect(within(menu).getByRole('menuitem', { name: 'Удалить' })).toBeDisabled()
      const restoreItem = within(menu).getByRole('menuitem', { name: 'Вернуть' })
      expect(restoreItem).toBeEnabled()
      await user.click(restoreItem)

      const restoreDialog = await screen.findByRole('dialog', { name: options.dialogName })
      await waitFor(() => expect(within(restoreDialog).getByRole('button', { name: 'Отмена' })).toHaveFocus())
      await user.click(within(restoreDialog).getByRole('button', { name: 'Вернуть запись' }))
      options.assertRestored()
      await waitFor(() => expect(screen.queryByRole('dialog', { name: options.dialogName })).not.toBeInTheDocument())
      expect((await within(financePanel).findAllByText(options.restoredText ?? options.rowText)).length).toBeGreaterThan(0)
    }

    await restoreRow({
      rowText: 'PKO-restore',
      dialogName: 'Вернуть поступление?',
      assertRestored: () => expect(restoreOperation).toHaveBeenLastCalledWith('token', 'income-canceled'),
    })
    await restoreRow({
      tabName: /Расходы/,
      rowText: 'RKO-restore',
      dialogName: 'Вернуть выплату?',
      assertRestored: () => expect(restoreOperation).toHaveBeenLastCalledWith('token', 'expense-canceled'),
    })
    await restoreRow({
      tabName: /Начисления владельцам/,
      rowText: 'Начисление к возврату',
      restoredText: '333,00',
      dialogName: 'Вернуть начисление владельцу?',
      assertRestored: () => expect(restoreAccrual).toHaveBeenLastCalledWith('token', 'accrual-canceled'),
    })
    await restoreRow({
      tabName: /Начисления поставщикам/,
      rowText: 'SUP-restore',
      dialogName: 'Вернуть начисление поставщику?',
      assertRestored: () => expect(restoreSupplierAccrual).toHaveBeenLastCalledWith('token', 'supplier-accrual-canceled'),
    })
  })

  it('closes payment context menu when switching payment table tabs', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    fireEvent.contextMenu(within(financePanel).getAllByText('Членский взнос')[0].closest('tr')!)
    const menu = await screen.findByRole('menu', { name: 'Операции с платежами' })
    expect(within(menu).getByRole('menuitem', { name: 'Удалить' })).toBeEnabled()

    await user.click(within(financePanel).getByRole('tab', { name: /Расходы/ }))
    await waitFor(() => expect(screen.queryByRole('menu', { name: 'Операции с платежами' })).not.toBeInTheDocument())
  })

  it('closes payment context menu when payment table filters reload data', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    fireEvent.contextMenu(within(financePanel).getAllByText('Членский взнос')[0].closest('tr')!)
    expect(await screen.findByRole('menu', { name: 'Операции с платежами' })).toBeInTheDocument()

    fireEvent.change(within(financePanel).getByLabelText('Период с'), { target: { value: '2026-06' } })
    await waitFor(() => expect(screen.queryByRole('menu', { name: 'Операции с платежами' })).not.toBeInTheDocument())
  })

  it('opens new income dialog from empty payment table context menu', async () => {
    const user = userEvent.setup()
    const financeClient = createFinanceClient({
      getOperationsPage: async (_token, params) => ({ items: [], totalCount: 0, offset: params?.offset ?? 0, limit: params?.limit ?? 25 }),
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    const tableArea = await within(financePanel).findByRole('group', { name: 'Рабочая область платежной таблицы' })

    expect(await within(tableArea).findByText('По выбранным условиям записей нет')).toHaveAttribute('role', 'status')
    tableArea.focus()
    expect(tableArea).toHaveFocus()
    fireEvent.keyDown(tableArea, { key: 'F10', shiftKey: true })
    const keyboardMenu = await screen.findByRole('menu', { name: 'Операции с платежами' })
    const keyboardAddItem = within(keyboardMenu).getByRole('menuitem', { name: 'Добавить' })
    await waitFor(() => expect(keyboardAddItem).toHaveFocus())
    expect(keyboardAddItem).toBeEnabled()
    expect(within(keyboardMenu).getByRole('menuitem', { name: 'Изменить' })).toBeDisabled()
    expect(within(keyboardMenu).getByRole('menuitem', { name: 'Удалить' })).toBeDisabled()
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('menu', { name: 'Операции с платежами' })).not.toBeInTheDocument())
    await waitFor(() => expect(tableArea).toHaveFocus())

    fireEvent.contextMenu(tableArea)
    const menu = await screen.findByRole('menu', { name: 'Операции с платежами' })
    expect(within(menu).getByRole('menuitem', { name: 'Добавить' })).toBeEnabled()
    expect(within(menu).getByRole('menuitem', { name: 'Изменить' })).toBeDisabled()
    expect(within(menu).getByRole('menuitem', { name: 'Удалить' })).toBeDisabled()
    await user.click(within(menu).getByRole('menuitem', { name: 'Добавить' }))

    const dialog = await screen.findByRole('dialog', { name: 'Новое поступление' })
    expect(within(dialog).getByText('Платежи')).toBeInTheDocument()
  })

  it('opens create dialogs from every payment table context menu', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    const tableArea = await within(financePanel).findByRole('group', { name: 'Рабочая область платежной таблицы' })
    const cases = [
      { tab: /Расходы/, dialog: 'Новая выплата' },
      { tab: /Начисления владельцам/, dialog: 'Ручное начисление' },
      { tab: /Начисления поставщикам/, dialog: 'Начисление поставщику' },
      { tab: /Счетчики/, dialog: 'Показание счетчика' },
    ]

    for (const item of cases) {
      const tab = within(financePanel).getByRole('tab', { name: item.tab })
      await user.click(tab)
      await waitFor(() => expect(tab).toHaveAttribute('aria-selected', 'true'))
      fireEvent.contextMenu(tableArea)
      const menu = await screen.findByRole('menu', { name: 'Операции с платежами' })
      await user.click(within(menu).getByRole('menuitem', { name: 'Добавить' }))

      const dialog = await screen.findByRole('dialog', { name: item.dialog })
      expect(within(dialog).getByText('Платежи')).toBeInTheDocument()
      expect(within(dialog).queryByText('Изменение')).not.toBeInTheDocument()
      await user.click(within(dialog).getByRole('button', { name: 'Закрыть форму платежа' }))
      await waitFor(() => expect(screen.queryByRole('dialog', { name: item.dialog })).not.toBeInTheDocument())
    }
  })

  it('opens edit dialogs from every payment table context menu', async () => {
    const user = userEvent.setup()
    const financeClient = createFinanceClient({
      getOperations: async () => [
        createFinancialOperation({ id: 'income-context-edit', documentNumber: 'PKO-context-edit', incomeTypeName: 'Членский взнос' }),
        createFinancialOperation({ id: 'expense-context-edit', operationKind: 'expense', documentNumber: 'RKO-context-edit', supplierName: 'Водоканал', expenseTypeName: 'Вода', amount: 500 }),
      ],
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    const cases = [
      { tab: /Расходы/, rowText: 'RKO-context-edit', dialog: 'Новая выплата' },
      { tab: /Начисления владельцам/, rowText: '2 000,00', dialog: 'Ручное начисление' },
      { tab: /Начисления поставщикам/, rowText: '650,00', dialog: 'Начисление поставщику' },
      { tab: /Счетчики/, rowText: '5.5', dialog: 'Показание счетчика' },
    ]

    for (const item of cases) {
      const tab = within(financePanel).getByRole('tab', { name: item.tab })
      await user.click(tab)
      await waitFor(() => expect(tab).toHaveAttribute('aria-selected', 'true'))
      const editedRow = within(financePanel).getAllByText(item.rowText)
        .map((node) => node.closest('tr'))
        .find((node): node is HTMLTableRowElement => node !== null)
      if (!editedRow) {
        throw new Error(`Payment row "${item.rowText}" was not rendered for focus restoration check.`)
      }
      const menu = await openFinanceContextMenuByCellText(financePanel, item.rowText)
      expect(within(menu).getByRole('menuitem', { name: 'Изменить' })).toBeEnabled()
      await user.click(within(menu).getByRole('menuitem', { name: 'Изменить' }))

      const dialog = await screen.findByRole('dialog', { name: item.dialog })
      expect(within(dialog).getByText('Изменение')).toBeInTheDocument()
      await user.click(within(dialog).getByRole('button', { name: 'Закрыть форму платежа' }))
      await waitFor(() => expect(screen.queryByRole('dialog', { name: item.dialog })).not.toBeInTheDocument())
      await waitFor(() => expect(editedRow).toHaveFocus())
    }
  })

  it('does not call finance APIs when payment forms fail client validation', async () => {
    const user = userEvent.setup()
    const financeCalls = {
      income: false,
      expense: false,
      accrual: false,
      supplierAccrual: false,
      regular: false,
      meter: false,
    }
    const financeClient = createFinanceClient({
      createIncome: async () => {
        financeCalls.income = true
        return createFinancialOperation({})
      },
      createExpense: async () => {
        financeCalls.expense = true
        return createFinancialOperation({ operationKind: 'expense' })
      },
      createAccrual: async () => {
        financeCalls.accrual = true
        return createAccrual({})
      },
      createSupplierAccrual: async () => {
        financeCalls.supplierAccrual = true
        return createSupplierAccrual({})
      },
      generateRegularAccruals: async () => {
        financeCalls.regular = true
        return createRegularAccrualGenerationResult({})
      },
      createMeterReading: async () => {
        financeCalls.meter = true
        return createMeterReading({})
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    fireEvent.submit(within(financePanel).getByLabelText('Сумма поступления').closest('form')!)
    expect(await within(financePanel).findByText('Проверьте поступление')).toBeInTheDocument()
    expect(within(financePanel).getByText('Сумма поступления должна быть больше 0.')).toBeInTheDocument()
    expect(financeCalls.income).toBe(false)

    fireEvent.submit(within(financePanel).getByLabelText('Сумма выплаты').closest('form')!)
    expect(await within(financePanel).findByText('Проверьте выплату')).toBeInTheDocument()
    expect(within(financePanel).getByText('Сумма выплаты должна быть больше 0.')).toBeInTheDocument()
    expect(financeCalls.expense).toBe(false)

    fireEvent.submit(within(financePanel).getByLabelText('Сумма начисления').closest('form')!)
    expect(await within(financePanel).findByText('Проверьте начисление')).toBeInTheDocument()
    expect(within(financePanel).getByText('Сумма начисления должна быть больше 0.')).toBeInTheDocument()
    expect(within(financePanel).getByText('Укажите комментарий начисления.')).toBeInTheDocument()
    expect(financeCalls.accrual).toBe(false)

    fireEvent.submit(within(financePanel).getByLabelText('Сумма начисления поставщику').closest('form')!)
    expect(await within(financePanel).findByText('Проверьте начисление поставщику')).toBeInTheDocument()
    expect(within(financePanel).getByText('Сумма начисления поставщику должна быть больше 0.')).toBeInTheDocument()
    expect(within(financePanel).getByText('Укажите комментарий начисления поставщику.')).toBeInTheDocument()
    expect(financeCalls.supplierAccrual).toBe(false)

    fireEvent.change(within(financePanel).getByLabelText('Месяц регулярных начислений'), { target: { value: '' } })
    fireEvent.submit(within(financePanel).getByLabelText('Месяц регулярных начислений').closest('form')!)
    expect(await within(financePanel).findByText('Проверьте регулярные начисления')).toBeInTheDocument()
    expect(within(financePanel).getByText('Укажите месяц регулярных начислений.')).toBeInTheDocument()
    expect(financeCalls.regular).toBe(false)

    fireEvent.change(within(financePanel).getByLabelText('Новое показание'), { target: { value: '-1' } })
    fireEvent.submit(within(financePanel).getByLabelText('Новое показание').closest('form')!)
    expect(await within(financePanel).findByText('Проверьте показание счетчика')).toBeInTheDocument()
    expect(within(financePanel).getByText('Новое показание должно быть 0 или больше.')).toBeInTheDocument()
    expect(financeCalls.meter).toBe(false)
  })

  it('shows visible counters for long payment workspace lists', async () => {
    const user = userEvent.setup()
    const operations = Array.from({ length: 9 }, (_, index) => createFinancialOperation({
      id: `operation-${index}`,
      amount: 101 + index,
      incomeTypeName: `Поступление ${index + 1}`,
    }))
    const accruals = Array.from({ length: 9 }, (_, index) => createAccrual({
      id: `accrual-${index}`,
      amount: 201 + index,
      incomeTypeName: `Начисление ${index + 1}`,
    }))
    const supplierAccruals = Array.from({ length: 9 }, (_, index) => createSupplierAccrual({
      id: `supplier-accrual-${index}`,
      amount: 301 + index,
      supplierName: `Поставщик ${index + 1}`,
    }))
    const meterReadings = Array.from({ length: 9 }, (_, index) => createMeterReading({
      id: `meter-reading-${index}`,
      consumption: 401 + index,
    }))
    const requestedLimits: Record<string, number | undefined> = {}
    const toPage = <TItem,>(items: TItem[], offset = 0, limit = 25) => ({ items: items.slice(offset, offset + limit), totalCount: items.length, offset, limit })
    const financeClient = createFinanceClient({
      getOperations: async () => operations,
      getAccruals: async () => accruals,
      getSupplierAccruals: async () => supplierAccruals,
      getMeterReadings: async () => meterReadings,
      getOperationsPage: async (_token, params) => {
        const key = params?.operationKind === 'expense' ? 'expenseOperations' : 'incomeOperations'
        requestedLimits[key] = params?.limit
        return toPage(params?.operationKind === 'expense' ? [] : operations, params?.offset, params?.limit)
      },
      getAccrualsPage: async (_token, params) => {
        requestedLimits.accruals = params?.limit
        return toPage(accruals, params?.offset, params?.limit)
      },
      getSupplierAccrualsPage: async (_token, params) => {
        requestedLimits.supplierAccruals = params?.limit
        return toPage(supplierAccruals, params?.offset, params?.limit)
      },
      getMeterReadingsPage: async (_token, params) => {
        requestedLimits.meterReadings = params?.limit
        return toPage(meterReadings, params?.offset, params?.limit)
      },
      getSummary: async () => ({ incomeTotal: 0, expenseTotal: 0, accrualTotal: 0, balance: 0, debt: 0, operationCount: operations.length, accrualCount: accruals.length, meterReadingCount: meterReadings.length }),
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    const operationsTable = within(financePanel).getByRole('table', { name: 'Последние платежи' })
    const accrualsTable = within(financePanel).getByRole('table', { name: 'Последние начисления' })
    const supplierAccrualsTable = within(financePanel).getByRole('table', { name: 'Последние начисления поставщикам' })
    const meterReadingsTable = within(financePanel).getByRole('table', { name: 'Последние показания' })

    expect(await within(operationsTable).findByText('Показано 8 из 9 операций')).toHaveAttribute('role', 'status')
    expect(within(accrualsTable).getByText('Показано 8 из 9 начислений')).toHaveAttribute('role', 'status')
    expect(within(supplierAccrualsTable).getByText('Показано 8 из 9 начислений поставщикам')).toHaveAttribute('role', 'status')
    expect(within(meterReadingsTable).getByText('Показано 8 из 9 показаний')).toHaveAttribute('role', 'status')
    const paymentPaginationCounter = within(financePanel).getByText('Показано 1-9 из 9')
    expect(paymentPaginationCounter).toHaveAttribute('role', 'status')
    expect(paymentPaginationCounter).toHaveAttribute('aria-live', 'polite')
    expect(within(financePanel).getByRole('navigation', { name: 'Пагинация платежей' })).toBeInTheDocument()
    expect(within(operationsTable).getByText('Поступление 8')).toBeInTheDocument()
    expect(within(operationsTable).queryByText('Поступление 9')).not.toBeInTheDocument()
    expect(requestedLimits).toEqual({ incomeOperations: 25, expenseOperations: 1, accruals: 1, supplierAccruals: 1, meterReadings: 1 })
  })

  it('refreshes payment summary totals from server when period filter changes', async () => {
    const user = userEvent.setup()
    const summaryRequests: Array<{ monthFrom?: string; monthTo?: string; search?: string } | undefined> = []
    const financeClient = createFinanceClient({
      getSummary: async (_token, params) => {
        summaryRequests.push(params)
        if (params?.monthFrom === '2026-01' && params.monthTo === '2026-02') {
          return { incomeTotal: 1200, expenseTotal: 300, accrualTotal: 1700, balance: 900, debt: 500, operationCount: 7, accrualCount: 4, meterReadingCount: 11 }
        }

        return { incomeTotal: 1500, expenseTotal: 0, accrualTotal: 2000, balance: 1500, debt: 500, operationCount: 1, accrualCount: 1, meterReadingCount: 1 }
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.type(within(financePanel).getByLabelText('Период с'), '2026-01')
    await user.type(within(financePanel).getByLabelText('Период по'), '2026-02')

    await waitFor(() => expect(summaryRequests).toContainEqual({ monthFrom: '2026-01', monthTo: '2026-02', search: '' }))
    const summaryStrip = within(financePanel).getByLabelText('Итоги платежей')
    expect(within(summaryStrip).getByText('1 200,00')).toBeInTheDocument()
    expect(within(summaryStrip).getByText('1 700,00')).toBeInTheDocument()
    expect(within(summaryStrip).getByText('300,00')).toBeInTheDocument()
    expect(within(summaryStrip).getByText('900,00')).toBeInTheDocument()
    expect(within(summaryStrip).getByText('11')).toBeInTheDocument()
    expect(within(financePanel).getByText('7 операций')).toBeInTheDocument()
  })

  it('debounces server search in payment tables', async () => {
    const user = userEvent.setup()
    const operationSearches: string[] = []
    const financeClient = createFinanceClient({
      getOperationsPage: async (_token, params) => {
        operationSearches.push(params?.search ?? '')
        return {
          items: params?.search === 'PKO' ? [createFinancialOperation({ id: 'operation-search', documentNumber: 'PKO-77', amount: 770 })] : [],
          totalCount: params?.search === 'PKO' ? 1 : 0,
          offset: params?.offset ?? 0,
          limit: params?.limit ?? 25,
        }
      },
      getSummary: async () => ({ incomeTotal: 770, expenseTotal: 0, accrualTotal: 0, balance: 770, debt: -770, operationCount: 1, accrualCount: 0, meterReadingCount: 0 }),
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.type(within(financePanel).getByLabelText('Поиск по платежам'), 'PKO')

    await within(financePanel).findByText('PKO-77')
    const appliedSearches = operationSearches.filter(Boolean)
    expect(new Set(appliedSearches)).toEqual(new Set(['PKO']))
  })

  it('cancels income operation with required reason from payments workspace', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    const incomeAmountInput = within(financePanel).getByLabelText('Сумма поступления')
    const incomeForm = incomeAmountInput.closest('form')!
    await user.clear(incomeAmountInput)
    await user.type(incomeAmountInput, '700')
    await user.type(within(incomeForm).getByLabelText('Документ поступления'), 'PKO-cancel')
    await user.click(within(incomeForm).getByRole('button', { name: 'Провести' }))

    expect(await within(financePanel).findByText('+700,00')).toBeInTheDocument()
    expect(within(financePanel).getByText('1 операций')).toBeInTheDocument()
    expect(within(financePanel).queryByRole('button', { name: /Отменить операцию/i })).not.toBeInTheDocument()
    const operationRow = within(financePanel).getByText('PKO-cancel').closest('tr')
    if (!operationRow) {
      throw new Error('Payment row was not rendered for focus restoration check.')
    }
    const operationMenu = await openFinanceContextMenuByCellText(financePanel, 'PKO-cancel')
    await user.click(within(operationMenu).getByRole('menuitem', { name: 'Удалить' }))

    const cancelDialog = await screen.findByRole('dialog', { name: 'Отменить поступление?' })
    await waitFor(() => expect(within(cancelDialog).getByLabelText('Причина отмены финансовой записи')).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Отменить поступление?' })).not.toBeInTheDocument()
    await waitFor(() => expect(operationRow).toHaveFocus())
    expect(within(financePanel).getByText('+700,00')).toBeInTheDocument()

    const reopenedOperationMenu = await openFinanceContextMenuByCellText(financePanel, 'PKO-cancel')
    await user.click(within(reopenedOperationMenu).getByRole('menuitem', { name: 'Удалить' }))
    const reopenedCancelDialog = await screen.findByRole('dialog', { name: 'Отменить поступление?' })
    await user.click(within(reopenedCancelDialog).getByRole('button', { name: 'Отменить запись' }))
    expect(within(reopenedCancelDialog).getByRole('alert')).toHaveTextContent('Укажите причину отмены.')
    await user.type(within(reopenedCancelDialog).getByLabelText('Причина отмены финансовой записи'), 'Ошибочный документ')
    await user.click(within(reopenedCancelDialog).getByRole('button', { name: 'Отменить запись' }))
    await waitFor(() => expect(within(financePanel).queryByText('+700,00')).not.toBeInTheDocument())
    expect(within(financePanel).getByText('0 операций')).toBeInTheDocument()
    expect(within(within(financePanel).getByRole('table', { name: 'Последние платежи' })).getByText('Операций пока нет')).toHaveAttribute('role', 'status')
  })

  it('cancels expense operation with required reason from payments table context menu', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    const expenseAmountInput = within(financePanel).getByLabelText('Сумма выплаты')
    const expenseForm = expenseAmountInput.closest('form')!
    await user.clear(expenseAmountInput)
    await user.type(expenseAmountInput, '500')
    await user.type(within(expenseForm).getByLabelText('Документ выплаты'), 'RKO-cancel')
    await user.type(within(expenseForm).getByLabelText('Комментарий выплаты'), 'Ошибочный расход')
    await user.click(within(expenseForm).getByRole('button', { name: 'Провести' }))

    const expenseTab = within(financePanel).getByRole('tab', { name: /Расходы/ })
    await user.click(expenseTab)
    await waitFor(() => expect(expenseTab).toHaveAttribute('aria-selected', 'true'))
    expect(await within(financePanel).findByText('RKO-cancel')).toBeInTheDocument()
    expect(within(financePanel).queryByRole('button', { name: /Отменить операцию/i })).not.toBeInTheDocument()

    const operationMenu = await openFinanceContextMenuByCellText(financePanel, 'RKO-cancel')
    await user.click(within(operationMenu).getByRole('menuitem', { name: 'Удалить' }))

    const cancelDialog = await screen.findByRole('dialog', { name: 'Отменить выплату?' })
    await waitFor(() => expect(within(cancelDialog).getByLabelText('Причина отмены финансовой записи')).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Отменить выплату?' })).not.toBeInTheDocument()
    expect(within(financePanel).getByText('RKO-cancel')).toBeInTheDocument()

    const reopenedOperationMenu = await openFinanceContextMenuByCellText(financePanel, 'RKO-cancel')
    await user.click(within(reopenedOperationMenu).getByRole('menuitem', { name: 'Удалить' }))
    const reopenedCancelDialog = await screen.findByRole('dialog', { name: 'Отменить выплату?' })
    await user.type(within(reopenedCancelDialog).getByLabelText('Причина отмены финансовой записи'), 'Ошибочная выплата')
    await user.click(within(reopenedCancelDialog).getByRole('button', { name: 'Отменить запись' }))
    await waitFor(() => expect(within(financePanel).queryByText('RKO-cancel')).not.toBeInTheDocument())
    expect(within(financePanel).getByText('0 операций')).toBeInTheDocument()
    expect(within(within(financePanel).getByRole('table', { name: 'Последние платежи' })).getByText('Операций пока нет')).toHaveAttribute('role', 'status')
  })

  it('cancels accruals and meter readings with required reasons from payments workspace', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    const accrualAmountInput = within(financePanel).getByLabelText('Сумма начисления')
    const accrualForm = accrualAmountInput.closest('form')!
    await user.clear(accrualAmountInput)
    await user.type(accrualAmountInput, '900')
    await user.type(within(accrualForm).getByLabelText('Комментарий начисления'), 'Ручная корректировка')
    await user.click(within(accrualForm).getByRole('button', { name: 'Начислить' }))
    const accrualTable = within(financePanel).getByRole('table', { name: 'Последние начисления' })
    expect(await within(accrualTable).findByText('900,00')).toBeInTheDocument()
    expect(within(accrualTable).queryByRole('button', { name: /Отменить начисление/i })).not.toBeInTheDocument()
    await user.click(within(financePanel).getByRole('tab', { name: /Начисления владельцам/ }))
    const accrualMenu = await openFinanceContextMenuByCellText(financePanel, '900,00')
    await user.click(within(accrualMenu).getByRole('menuitem', { name: 'Удалить' }))
    let cancelDialog = await screen.findByRole('dialog', { name: 'Отменить начисление владельцу?' })
    await waitFor(() => expect(within(cancelDialog).getByLabelText('Причина отмены финансовой записи')).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Отменить начисление владельцу?' })).not.toBeInTheDocument()
    expect(within(accrualTable).getByText('900,00')).toBeInTheDocument()

    const reopenedAccrualMenu = await openFinanceContextMenuByCellText(financePanel, '900,00')
    await user.click(within(reopenedAccrualMenu).getByRole('menuitem', { name: 'Удалить' }))
    cancelDialog = await screen.findByRole('dialog', { name: 'Отменить начисление владельцу?' })
    await user.type(within(cancelDialog).getByLabelText('Причина отмены финансовой записи'), 'Ошибочный ввод')
    await user.click(within(cancelDialog).getByRole('button', { name: 'Отменить запись' }))
    await waitFor(() => expect(within(accrualTable).queryByText('900,00')).not.toBeInTheDocument())
    expect(within(accrualTable).getByText('Начислений пока нет')).toHaveAttribute('role', 'status')

    const supplierAccrualAmountInput = within(financePanel).getByLabelText('Сумма начисления поставщику')
    const supplierAccrualForm = supplierAccrualAmountInput.closest('form')!
    await user.clear(supplierAccrualAmountInput)
    await user.type(supplierAccrualAmountInput, '650')
    await user.type(within(supplierAccrualForm).getByLabelText('Комментарий начисления поставщику'), 'Счет за воду')
    await user.click(within(supplierAccrualForm).getByRole('button', { name: 'Начислить' }))
    const supplierAccrualTable = within(financePanel).getByRole('table', { name: 'Последние начисления поставщикам' })
    expect(await within(supplierAccrualTable).findByText('650,00')).toBeInTheDocument()
    expect(within(supplierAccrualTable).queryByRole('button', { name: /Отменить начисление поставщику/i })).not.toBeInTheDocument()
    await user.click(within(financePanel).getByRole('tab', { name: /Начисления поставщикам/ }))
    const supplierAccrualMenu = await openFinanceContextMenuByCellText(financePanel, '650,00')
    await user.click(within(supplierAccrualMenu).getByRole('menuitem', { name: 'Удалить' }))
    cancelDialog = await screen.findByRole('dialog', { name: 'Отменить начисление поставщику?' })
    await waitFor(() => expect(within(cancelDialog).getByLabelText('Причина отмены финансовой записи')).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Отменить начисление поставщику?' })).not.toBeInTheDocument()
    expect(within(supplierAccrualTable).getByText('650,00')).toBeInTheDocument()

    const reopenedSupplierAccrualMenu = await openFinanceContextMenuByCellText(financePanel, '650,00')
    await user.click(within(reopenedSupplierAccrualMenu).getByRole('menuitem', { name: 'Удалить' }))
    cancelDialog = await screen.findByRole('dialog', { name: 'Отменить начисление поставщику?' })
    await user.type(within(cancelDialog).getByLabelText('Причина отмены финансовой записи'), 'Ошибочный ввод')
    await user.click(within(cancelDialog).getByRole('button', { name: 'Отменить запись' }))
    await waitFor(() => expect(within(supplierAccrualTable).queryByText('650,00')).not.toBeInTheDocument())
    expect(within(supplierAccrualTable).getByText('Начислений поставщикам пока нет')).toHaveAttribute('role', 'status')

    const meterValueInput = within(financePanel).getByLabelText('Новое показание')
    const meterForm = meterValueInput.closest('form')!
    await user.clear(meterValueInput)
    await user.type(meterValueInput, '15.5')
    await user.click(within(meterForm).getByRole('button', { name: 'Внести' }))
    const meterReadingTable = within(financePanel).getByRole('table', { name: 'Последние показания' })
    expect(await within(meterReadingTable).findByText('5.5')).toBeInTheDocument()
    expect(within(meterReadingTable).queryByRole('button', { name: /Отменить показание/i })).not.toBeInTheDocument()
    await user.click(within(financePanel).getByRole('tab', { name: /Счетчики/ }))
    const meterReadingMenu = await openFinanceContextMenuByCellText(financePanel, '5.5')
    await user.click(within(meterReadingMenu).getByRole('menuitem', { name: 'Удалить' }))
    cancelDialog = await screen.findByRole('dialog', { name: 'Отменить показание счетчика?' })
    await waitFor(() => expect(within(cancelDialog).getByLabelText('Причина отмены финансовой записи')).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Отменить показание счетчика?' })).not.toBeInTheDocument()
    expect(within(meterReadingTable).getByText('5.5')).toBeInTheDocument()

    const reopenedMeterReadingMenu = await openFinanceContextMenuByCellText(financePanel, '5.5')
    await user.click(within(reopenedMeterReadingMenu).getByRole('menuitem', { name: 'Удалить' }))
    cancelDialog = await screen.findByRole('dialog', { name: 'Отменить показание счетчика?' })
    await user.type(within(cancelDialog).getByLabelText('Причина отмены финансовой записи'), 'Ошибочный ввод')
    await user.click(within(cancelDialog).getByRole('button', { name: 'Отменить запись' }))
    await waitFor(() => expect(within(meterReadingTable).queryByText('5.5')).not.toBeInTheDocument())
    expect(within(meterReadingTable).getByText('Показаний пока нет')).toHaveAttribute('role', 'status')
  })

  it('creates manual accrual and updates debt from payments workspace', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.clear(within(financePanel).getByLabelText('Сумма начисления'))
    await user.type(within(financePanel).getByLabelText('Сумма начисления'), '900')
    await user.type(within(financePanel).getByLabelText('Комментарий начисления'), 'Ручная корректировка')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Начислить' })[0])

    expect((await within(financePanel).findAllByText('900,00')).length).toBeGreaterThan(0)
    const accrualTable = within(financePanel).getByRole('table', { name: 'Последние начисления' })
    expect(accrualTable).toBeInTheDocument()
    expect(within(accrualTable).getByText('Ручное')).toBeInTheDocument()

    const openBreakdownButton = within(accrualTable).getByLabelText(/Разбивка начисления/i)
    await user.dblClick(openBreakdownButton)

    const dialog = await screen.findByRole('dialog', { name: 'Разбивка начисления' })
    const closeBreakdownButton = within(dialog).getByRole('button', { name: 'Закрыть разбивку' })
    const accrualPeriodDescription = dialog.querySelector('#accrual-breakdown-period') as HTMLElement
    expect(accrualPeriodDescription).toHaveTextContent('06.2026')
    expect(dialog).toHaveAttribute('aria-describedby', accrualPeriodDescription.id)
    await waitFor(() => expect(closeBreakdownButton).toHaveFocus())
    await user.tab()
    expect(closeBreakdownButton).toHaveFocus()
    expect(within(dialog).getByText('Ручная корректировка')).toBeInTheDocument()
    expect(within(dialog).getByText('Ручное')).toBeInTheDocument()
    expect(within(dialog).getByText('Гараж')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Разбивка начисления' })).not.toBeInTheDocument()
    expect(openBreakdownButton).toHaveFocus()
  })

  it('shows garage debt before and after owner payment', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.clear(within(financePanel).getByLabelText('Сумма начисления'))
    await user.type(within(financePanel).getByLabelText('Сумма начисления'), '900')
    await user.type(within(financePanel).getByLabelText('Комментарий начисления'), 'Начисление месяца')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Начислить' })[0])

    await user.clear(within(financePanel).getByLabelText('Сумма поступления'))
    await user.type(within(financePanel).getByLabelText('Сумма поступления'), '300')
    await user.type(within(financePanel).getByLabelText('Документ поступления'), 'PKO-2')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Провести' })[0])

    expect(await within(financePanel).findByText('Долг: 900,00 → 600,00')).toBeInTheDocument()
    expect(within(financePanel).getByText('Разбивка: 06.2026 300,00')).toBeInTheDocument()
  })

  it('creates supplier accrual from payments workspace', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.clear(within(financePanel).getByLabelText('Сумма начисления поставщику'))
    await user.type(within(financePanel).getByLabelText('Сумма начисления поставщику'), '650')
    await user.type(within(financePanel).getByLabelText('Документ начисления поставщику'), 'INV-1')
    await user.type(within(financePanel).getByLabelText('Комментарий начисления поставщику'), 'Счет за воду')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Начислить' })[1])

    expect((await within(financePanel).findAllByText('650,00')).length).toBeGreaterThan(0)
    const supplierAccrualTable = within(financePanel).getByRole('table', { name: 'Последние начисления поставщикам' })
    expect(supplierAccrualTable).toBeInTheDocument()
    expect(within(supplierAccrualTable).getByText('Водоканал')).toBeInTheDocument()
    expect(within(supplierAccrualTable).getByText('Ручное')).toBeInTheDocument()

    const openBreakdownButton = within(supplierAccrualTable).getByLabelText(/Разбивка начисления поставщику/i)
    await user.dblClick(openBreakdownButton)

    const dialog = await screen.findByRole('dialog', { name: 'Разбивка начисления поставщику' })
    const closeBreakdownButton = within(dialog).getByRole('button', { name: 'Закрыть разбивку' })
    expect(within(dialog).getByText('INV-1')).toBeInTheDocument()
    expect(within(dialog).getByText('Счет за воду')).toBeInTheDocument()
    expect(within(dialog).getByText('Ручное')).toBeInTheDocument()
    await waitFor(() => expect(closeBreakdownButton).toHaveFocus())
    await user.tab()
    expect(closeBreakdownButton).toHaveFocus()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Разбивка начисления поставщику' })).not.toBeInTheDocument()
    expect(openBreakdownButton).toHaveFocus()
  })

  it('generates supplier group salary accruals from payments workspace', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.click(within(financePanel).getByRole('tab', { name: /Начисления поставщикам/ }))
    const salaryButton = within(financePanel).getByRole('button', { name: 'Зарплата группы' })
    await user.click(salaryButton)
    let dialog = await screen.findByRole('dialog', { name: 'Зарплата группы' })
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Закрыть форму платежа' })).toHaveFocus())
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Зарплата группы' })).not.toBeInTheDocument()
    expect(salaryButton).toHaveFocus()

    await user.click(salaryButton)
    dialog = await screen.findByRole('dialog', { name: 'Зарплата группы' })
    expect(within(dialog).getByLabelText('Группа для зарплаты')).toHaveValue('group-1')
    await user.clear(within(dialog).getByLabelText('Сумма зарплаты'))
    await user.type(within(dialog).getByLabelText('Сумма зарплаты'), '7000')
    await user.type(within(dialog).getByLabelText('Документ зарплаты'), 'PAY-06')
    await user.type(within(dialog).getByLabelText('Комментарий зарплаты'), 'Июнь')
    await user.click(within(dialog).getByRole('button', { name: 'Начислить зарплату' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Зарплата группы' })).not.toBeInTheDocument())
    expect((await within(financePanel).findAllByText('Зарплата')).length).toBeGreaterThan(0)
    expect(within(financePanel).getByText('PAY-06')).toBeInTheDocument()
    expect(within(financePanel).getAllByText('Авто').length).toBeGreaterThan(0)
  })

  it('shows supplier obligation before and after expense payment', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.clear(within(financePanel).getByLabelText('Сумма начисления поставщику'))
    await user.type(within(financePanel).getByLabelText('Сумма начисления поставщику'), '650')
    await user.type(within(financePanel).getByLabelText('Документ начисления поставщику'), 'INV-2')
    await user.type(within(financePanel).getByLabelText('Комментарий начисления поставщику'), 'Счет месяца')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Начислить' })[1])

    await user.clear(within(financePanel).getByLabelText('Сумма выплаты'))
    await user.type(within(financePanel).getByLabelText('Сумма выплаты'), '250')
    await user.type(within(financePanel).getByLabelText('Документ выплаты'), 'RKO-2')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Провести' })[1])

    expect(await within(financePanel).findByText('Обязательство: 650,00 → 400,00')).toBeInTheDocument()
    expect(within(financePanel).getByText('Разбивка: 06.2026 250,00')).toBeInTheDocument()
  })

  it('generates regular accruals from tariff in payments workspace', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    const dictionaryClient = createDictionaryClient({
      getTariffs: async () => [createTariff({ id: 'tariff-fixed', name: 'Членский тариф', calculationBase: 'fixed', rate: 300, effectiveFrom: '2026-01-01' })],
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.type(within(financePanel).getByLabelText('Комментарий регулярных начислений'), 'Начисление за месяц')
    await user.click(within(financePanel).getByRole('button', { name: 'Создать месяц' }))

    expect(await within(financePanel).findByText('Создано 1, пропущено 0')).toHaveAttribute('role', 'status')
    expect((await within(financePanel).findAllByText('300,00')).length).toBeGreaterThan(0)
    expect(within(financePanel).getByText('Членский тариф · 300,00')).toBeInTheDocument()
    const accrualTable = within(financePanel).getByRole('table', { name: 'Последние начисления' })
    expect(within(accrualTable).getByText('Авто')).toBeInTheDocument()
    const openBreakdownButton = within(accrualTable).getByLabelText(/Разбивка начисления/i)
    await user.dblClick(openBreakdownButton)
    const dialog = await screen.findByRole('dialog', { name: 'Разбивка начисления' })
    const closeBreakdownButton = within(dialog).getByRole('button', { name: 'Закрыть разбивку' })
    expect(within(dialog).getByText('Начисление за месяц')).toBeInTheDocument()
    await waitFor(() => expect(closeBreakdownButton).toHaveFocus())
    await user.tab()
    expect(closeBreakdownButton).toHaveFocus()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Разбивка начисления' })).not.toBeInTheDocument()
    expect(openBreakdownButton).toHaveFocus()
  })

  it('uses only fixed tariffs for membership regular accruals', async () => {
    const user = userEvent.setup()
    let regularRequest: GenerateRegularAccrualsRequest | null = null
    const incomeType = createAccountingType({ id: 'income-membership', name: 'Членский взнос', code: 'membership', isSystem: true })
    const waterTariff = createTariff({ id: 'tariff-water', name: 'Вода', calculationBase: 'meter_water', rate: 50, effectiveFrom: '2026-01-01' })
    const fixedTariff = createTariff({ id: 'tariff-membership', name: 'Членский тариф', calculationBase: 'fixed', rate: 300, effectiveFrom: '2026-01-01' })
    const dictionaryClient = createDictionaryClient({
      getIncomeTypes: async () => [incomeType],
      getTariffs: async () => [waterTariff, fixedTariff],
    })
    const financeClient = createFinanceClient({
      generateRegularAccruals: async (_token, request) => {
        regularRequest = request
        return createRegularAccrualGenerationResult({
          incomeTypeId: request.incomeTypeId,
          incomeTypeName: incomeType.name,
          tariffId: request.tariffId,
          tariffName: fixedTariff.name,
          createdAccruals: [createAccrual({ incomeTypeId: incomeType.id, incomeTypeName: incomeType.name, tariffId: fixedTariff.id, tariffName: fixedTariff.name, amount: fixedTariff.rate })],
          totalAmount: fixedTariff.rate,
        })
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    const tariffSelect = within(financePanel).getByLabelText('Тариф регулярного начисления')

    expect(within(tariffSelect).queryByRole('option', { name: 'Вода · 50,00' })).not.toBeInTheDocument()
    expect(within(tariffSelect).getByRole('option', { name: 'Членский тариф · 300,00' })).toBeInTheDocument()
    await user.click(within(financePanel).getByRole('button', { name: 'Создать месяц' }))

    await within(financePanel).findByText('Создано 1, пропущено 0')
    expect(regularRequest?.incomeTypeId).toBe(incomeType.id)
    expect(regularRequest?.tariffId).toBe(fixedTariff.id)
  })

  it('creates meter reading and shows calculated consumption', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.selectOptions(within(financePanel).getByLabelText('Тип счетчика'), 'water')
    await user.clear(within(financePanel).getByLabelText('Новое показание'))
    await user.type(within(financePanel).getByLabelText('Новое показание'), '15.5')
    await user.click(within(financePanel).getByRole('button', { name: 'Внести' }))

    expect(await within(financePanel).findByText('5.5')).toBeInTheDocument()
    expect(within(financePanel).getByRole('table', { name: 'Последние показания' })).toBeInTheDocument()
  })

  it('shows electricity gap warning returned by API', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.selectOptions(within(financePanel).getByLabelText('Тип счетчика'), 'electricity')
    await user.clear(within(financePanel).getByLabelText('Новое показание'))
    await user.type(within(financePanel).getByLabelText('Новое показание'), '125')
    await user.click(within(financePanel).getByRole('button', { name: 'Внести' }))

    expect(await within(financePanel).findByText('проверьте предыдущий месяц')).toBeInTheDocument()
  })

  it('highlights garages without meter readings for selected month', async () => {
    const user = userEvent.setup()
    const missingReadings = [
      createMissingMeterReading({ garageNumber: '12', meterKind: 'water', accountingMonth: '2026-06-01' }),
      createMissingMeterReading({ garageNumber: '13', meterKind: 'electricity', accountingMonth: '2026-06-01' }),
    ]
    const financeClient = createFinanceClient({
      getMissingMeterReadings: async () => missingReadings,
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Платежи')
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    await user.click(within(financePanel).getByRole('tab', { name: /Счетчики/ }))

    const warning = await within(financePanel).findByText(/Нет показаний за 06\.2026/)
    expect(warning).toHaveAttribute('role', 'status')
    expect(warning).toHaveTextContent('Гараж 12 - Вода')
    expect(warning).toHaveTextContent('Гараж 13 - Электричество')
  })

  it('runs Access import dry-run and shows checks history', async () => {
    const user = userEvent.setup()
    const importClient = createStatefulImportClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={importClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Импорт')
    const importPanel = await screen.findByRole('region', { name: 'Импорт Access' })
    const file = new File(['garage owner payment'], 'ГСК.accdb', { type: 'application/octet-stream' })

    expect(within(importPanel).getByText('Reader Access')).toBeInTheDocument()
    expect(await within(importPanel).findByText('Не настроен')).toHaveAttribute('role', 'status')
    expect(within(importPanel).getByText('Фактическое чтение Access не подключено.')).toBeInTheDocument()
    expect(within(importPanel).getByLabelText('Требования reader Access')).toHaveTextContent('ACE OLE DB driver')

    const filePickerButton = within(importPanel).getByText('Выбрать .accdb или .mdb').closest('label')
    expect(filePickerButton).toHaveAttribute('title', 'Выбрать файл Access .accdb или .mdb')
    expect(filePickerButton).toHaveAttribute('data-tooltip', 'Выбрать файл Access .accdb или .mdb')
    expect(within(importPanel).getByText('Файл не выбран')).toHaveAttribute('role', 'status')
    await user.upload(within(importPanel).getByLabelText('Файл Access'), file)
    expect(await within(importPanel).findByText('ГСК.accdb')).toHaveAttribute('role', 'status')
    const dryRunButton = within(importPanel).getByRole('button', { name: 'Проверить файл Access ГСК.accdb' })
    expect(dryRunButton).toHaveAttribute('title', 'Проверить файл Access ГСК.accdb')
    expect(dryRunButton).toHaveAttribute('data-tooltip', 'Проверить файл Access ГСК.accdb')
    expect(dryRunButton.querySelector('svg')).toBeInTheDocument()
    await user.click(dryRunButton)

    const importSummary = within(importPanel).getByLabelText('Итоги dry-run импорта')
    expect(within(importSummary).getByText('Завершен')).toBeInTheDocument()
    expect(within(importSummary).getByText('Успешно')).toBeInTheDocument()
    expect(within(importSummary).getByText('Предупреждения')).toBeInTheDocument()
    expect(within(importSummary).getByText('Ошибки')).toBeInTheDocument()
    expect(within(importSummary).getByText('2')).toBeInTheDocument()
    expect(within(importSummary).getByText('1')).toBeInTheDocument()
    expect(within(importSummary).getByText('0')).toBeInTheDocument()
    expect(within(importPanel).getByRole('table', { name: 'Проверки импорта' })).toBeInTheDocument()
    expect(within(importPanel).getByText('Формат файла')).toBeInTheDocument()
    expect(within(importPanel).getAllByText('Пройдено').length).toBeGreaterThan(0)

    await user.click(within(importPanel).getByRole('tab', { name: /Лог/ }))
    expect(await within(importPanel).findByRole('table', { name: 'Лог запуска Access' })).toBeInTheDocument()
    expect(within(importPanel).getByText('file_received')).toBeInTheDocument()
    expect(within(importPanel).getByText('dry_run_finished')).toBeInTheDocument()
    expect(within(importPanel).getAllByText('Предупреждение').length).toBeGreaterThan(0)

    await user.click(within(importPanel).getByRole('tab', { name: /История/ }))
    expect((await within(importPanel).findAllByText('Dry-run завершен с предупреждениями.')).length).toBeGreaterThan(0)
    expect(within(importPanel).getAllByText('Завершен').length).toBeGreaterThan(0)
    expect(within(importPanel).getAllByText('2/3 · 1 предупреждение · 0 ошибок').length).toBeGreaterThan(0)
    expect(within(importPanel).getByText('ГСК.accdb · 2/3 · 1 предупреждение · 0 ошибок')).toHaveAttribute('role', 'status')
    expect(within(importPanel).getAllByText('ГСК.accdb').length).toBeGreaterThan(0)

    const reportDownloadButton = within(importPanel).getByRole('button', { name: 'Скачать JSON-отчет dry-run ГСК.accdb' })
    expect(reportDownloadButton).toHaveAttribute('title', 'Скачать JSON-отчет dry-run ГСК.accdb')
    expect(reportDownloadButton).toHaveAttribute('data-tooltip', 'Скачать JSON-отчет dry-run ГСК.accdb')
    expect(reportDownloadButton.querySelector('svg')).toBeInTheDocument()
    await user.click(reportDownloadButton)

    const exportReadyMessage = await within(importPanel).findByText('Отчет dry-run импорта готов.')
    expect(exportReadyMessage).toHaveAttribute('role', 'status')
    expect(exportReadyMessage).toHaveAttribute('aria-live', 'polite')
  })

  it('shows duplicate Access file warning in dry-run checks', async () => {
    const user = userEvent.setup()
    const run = createAccessImportRun({
      totalChecks: 4,
      passedChecks: 2,
      warningCount: 2,
      checks: [
        { code: 'extension', title: 'Формат файла', status: 'passed', message: 'Расширение поддерживается.' },
        { code: 'signature', title: 'Сигнатура Access', status: 'passed', message: 'Файл похож на Access.' },
        { code: 'native_reader', title: 'Драйвер чтения .accdb', status: 'warning', message: 'Нужен ACE-драйвер или конвертация.' },
        { code: 'duplicate_content', title: 'Повторная проверка файла', status: 'warning', message: 'Файл уже проверялся в запуске first.accdb от 10.07.2026 10:00. Перед фактическим импортом убедитесь, что это осознанная повторная загрузка.' },
      ],
    })
    const importClient = createImportClient({
      getAccessRuns: async () => [run],
      getAccessRunLog: async () => [
        createAccessImportRunLogEntry({ accessImportRunId: run.id, stepCode: 'duplicate_content_detected', level: 'warning', message: 'Найден предыдущий dry-run с тем же содержимым файла Access.' }),
      ],
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={importClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Импорт')
    const importPanel = await screen.findByRole('region', { name: 'Импорт Access' })

    expect(within(importPanel).getByRole('table', { name: 'Проверки импорта' })).toBeInTheDocument()
    expect(within(importPanel).getByText('Повторная проверка файла')).toBeInTheDocument()
    expect(within(importPanel).getByText(/осознанная повторная загрузка/)).toBeInTheDocument()
    expect(within(importPanel).getAllByText('Предупреждение').length).toBeGreaterThan(1)
  })

  it('requests Access import rollback through confirmation with reason', async () => {
    const user = userEvent.setup()
    let rollbackReason: string | undefined
    const run = createAccessImportRun()
    const importClient = createImportClient({
      getAccessRuns: async () => [run],
      getAccessRunLog: async () => [
        createAccessImportRunLogEntry({ accessImportRunId: run.id, stepCode: 'dry_run_finished', message: run.summary }),
      ],
      requestAccessImportRollback: async (_token, runId, reason) => {
        rollbackReason = reason
        return createAccessImportRun({
          ...run,
          id: runId,
          status: 'rollback_requested',
          summary: 'Rollback запрошен: фактический откат данных не выполнялся для dry-run запуска.',
        })
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={importClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Импорт')
    const importPanel = await screen.findByRole('region', { name: 'Импорт Access' })

    const rollbackButton = within(importPanel).getByRole('button', { name: 'Запросить rollback импорта ГСК.accdb' })
    expect(rollbackButton).toHaveAttribute('title', 'Запросить rollback импорта ГСК.accdb')
    expect(rollbackButton).toHaveAttribute('data-tooltip', 'Запросить rollback импорта ГСК.accdb')
    await user.click(rollbackButton)

    const rollbackDialog = await screen.findByRole('dialog', { name: 'Запросить rollback импорта?' })
    await waitFor(() => expect(within(rollbackDialog).getByLabelText('Причина rollback импорта')).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Запросить rollback импорта?' })).not.toBeInTheDocument())
    expect(rollbackReason).toBeUndefined()
    expect(rollbackButton).toHaveFocus()

    await user.click(rollbackButton)
    const reopenedDialog = await screen.findByRole('dialog', { name: 'Запросить rollback импорта?' })
    await user.click(within(reopenedDialog).getByRole('button', { name: 'Запросить rollback' }))
    expect(within(reopenedDialog).getByRole('alert')).toHaveTextContent('Укажите причину rollback импорта.')
    await user.type(within(reopenedDialog).getByLabelText('Причина rollback импорта'), 'Выбран неверный файл старой базы')
    await user.click(within(reopenedDialog).getByRole('button', { name: 'Запросить rollback' }))

    expect(await within(importPanel).findByText('Rollback импорта запрошен. Фактический откат данных не выполнялся для dry-run запуска.')).toHaveAttribute('role', 'status')
    expect(rollbackReason).toBe('Выбран неверный файл старой базы')
    expect(within(importPanel).getAllByText('Rollback запрошен').length).toBeGreaterThan(0)
    expect(within(importPanel).getByRole('button', { name: 'Запросить rollback импорта ГСК.accdb' })).toBeDisabled()
  })

  it('requests Access import apply through confirmation with backup acknowledgement', async () => {
    const user = userEvent.setup()
    let applyRequest: { reason: string; backupConfirmed: boolean } | undefined
    const run = createAccessImportRun()
    const importClient = createImportClient({
      getAccessRuns: async () => [run],
      getAccessRunLog: async () => [
        createAccessImportRunLogEntry({ accessImportRunId: run.id, stepCode: 'dry_run_finished', message: run.summary }),
      ],
      requestAccessImportApply: async (_token, runId, reason, backupConfirmed) => {
        applyRequest = { reason, backupConfirmed }
        return createAccessImportRun({
          ...run,
          id: runId,
          status: 'import_requested',
          summary: 'Фактический импорт запрошен: перенос будет выполнен после подключения reader Access.',
        })
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={importClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Импорт')
    const importPanel = await screen.findByRole('region', { name: 'Импорт Access' })

    const applyButton = within(importPanel).getByRole('button', { name: 'Запросить фактический импорт ГСК.accdb' })
    expect(applyButton).toHaveAttribute('title', 'Запросить фактический импорт ГСК.accdb')
    expect(applyButton).toHaveAttribute('data-tooltip', 'Запросить фактический импорт ГСК.accdb')
    await user.click(applyButton)

    const applyDialog = await screen.findByRole('dialog', { name: 'Запросить фактический импорт?' })
    await waitFor(() => expect(within(applyDialog).getByLabelText('Причина фактического импорта')).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Запросить фактический импорт?' })).not.toBeInTheDocument())
    expect(applyRequest).toBeUndefined()
    expect(applyButton).toHaveFocus()

    await user.click(applyButton)
    const reopenedDialog = await screen.findByRole('dialog', { name: 'Запросить фактический импорт?' })
    await user.click(within(reopenedDialog).getByRole('button', { name: 'Запросить импорт' }))
    expect(within(reopenedDialog).getByRole('alert')).toHaveTextContent('Укажите причину фактического импорта.')
    await user.type(within(reopenedDialog).getByLabelText('Причина фактического импорта'), 'Dry-run проверен, backup создан')
    await user.click(within(reopenedDialog).getByRole('button', { name: 'Запросить импорт' }))
    expect(within(reopenedDialog).getByRole('alert')).toHaveTextContent('Подтвердите, что backup PostgreSQL создан перед импортом.')
    await user.click(within(reopenedDialog).getByLabelText('Backup PostgreSQL создан перед фактическим импортом'))
    await user.click(within(reopenedDialog).getByRole('button', { name: 'Запросить импорт' }))

    expect(await within(importPanel).findByText('Фактический импорт запрошен. Данные не переносились до подключения reader Access.')).toHaveAttribute('role', 'status')
    expect(applyRequest).toEqual({ reason: 'Dry-run проверен, backup создан', backupConfirmed: true })
    expect(within(importPanel).getAllByText('Импорт запрошен').length).toBeGreaterThan(0)
    expect(within(importPanel).getByRole('button', { name: 'Запросить фактический импорт ГСК.accdb' })).toBeDisabled()
    expect(within(importPanel).getByRole('button', { name: 'Запросить rollback импорта ГСК.accdb' })).toBeDisabled()
  })

  it('cancels Access import apply request through confirmation with reason', async () => {
    const user = userEvent.setup()
    let cancelReason: string | undefined
    const run = createAccessImportRun({
      status: 'import_requested',
      summary: 'Фактический импорт запрошен: перенос будет выполнен после подключения reader Access.',
    })
    const importClient = createImportClient({
      getAccessRuns: async () => [run],
      getAccessRunLog: async () => [
        createAccessImportRunLogEntry({ accessImportRunId: run.id, stepCode: 'import_requested', level: 'warning', message: run.summary }),
      ],
      cancelAccessImportApplyRequest: async (_token, runId, reason) => {
        cancelReason = reason
        return createAccessImportRun({
          ...run,
          id: runId,
          status: 'import_request_cancelled',
          summary: 'Заявка на фактический импорт отменена. Dry-run остается доступным.',
        })
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={importClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Импорт')
    const importPanel = await screen.findByRole('region', { name: 'Импорт Access' })

    expect(within(importPanel).getByRole('button', { name: 'Запросить фактический импорт ГСК.accdb' })).toBeDisabled()
    expect(within(importPanel).getByRole('button', { name: 'Запросить rollback импорта ГСК.accdb' })).toBeDisabled()
    const cancelButton = within(importPanel).getByRole('button', { name: 'Отменить заявку на импорт ГСК.accdb' })
    expect(cancelButton).toHaveAttribute('title', 'Отменить заявку на импорт ГСК.accdb')
    expect(cancelButton).toHaveAttribute('data-tooltip', 'Отменить заявку на импорт ГСК.accdb')
    await user.click(cancelButton)

    const cancelDialog = await screen.findByRole('dialog', { name: 'Отменить заявку на импорт?' })
    await waitFor(() => expect(within(cancelDialog).getByLabelText('Причина отмены заявки на импорт')).toHaveFocus())
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Отменить заявку на импорт?' })).not.toBeInTheDocument())
    expect(cancelReason).toBeUndefined()
    expect(cancelButton).toHaveFocus()

    await user.click(cancelButton)
    const reopenedDialog = await screen.findByRole('dialog', { name: 'Отменить заявку на импорт?' })
    await user.click(within(reopenedDialog).getByRole('button', { name: 'Отменить заявку' }))
    expect(within(reopenedDialog).getByRole('alert')).toHaveTextContent('Укажите причину отмены заявки на импорт.')
    await user.type(within(reopenedDialog).getByLabelText('Причина отмены заявки на импорт'), 'Нужно перепроверить backup')
    await user.click(within(reopenedDialog).getByRole('button', { name: 'Отменить заявку' }))

    expect(await within(importPanel).findByText('Заявка на фактический импорт отменена. Данные не переносились.')).toHaveAttribute('role', 'status')
    expect(cancelReason).toBe('Нужно перепроверить backup')
    expect(within(importPanel).getAllByText('Заявка отменена').length).toBeGreaterThan(0)
    expect(within(importPanel).getByRole('button', { name: 'Отменить заявку на импорт ГСК.accdb' })).toBeDisabled()
    expect(within(importPanel).getByRole('button', { name: 'Запросить фактический импорт ГСК.accdb' })).not.toBeDisabled()
  })

  it('shows visible counters for long Access import lists', async () => {
    const user = userEvent.setup()
    const runs = Array.from({ length: 9 }, (_, index) => createAccessImportRun({
      id: `access-run-${index}`,
      originalFileName: `ГСК-${index + 1}.accdb`,
    }))
    const logEntries = Array.from({ length: 11 }, (_, index) => createAccessImportRunLogEntry({
      id: `log-${index}`,
      accessImportRunId: runs[0].id,
      stepCode: `step_${index + 1}`,
    }))
    const createdRecords = Array.from({ length: 11 }, (_, index) => createAccessImportCreatedRecord({
      id: `created-${index}`,
      accessImportRunId: runs[0].id,
      targetEntityId: `garage-${index + 1}`,
      targetDisplayName: `Гараж ${index + 1}`,
    }))
    const quarantineItems = Array.from({ length: 9 }, (_, index) => createAccessImportQuarantineItem({
      id: `quarantine-${index}`,
      externalId: `${index + 1}`,
    }))
    let quarantineLimit: number | undefined
    let createdLimit: number | undefined
    const importClient = createImportClient({
      getAccessRuns: async () => runs,
      getAccessRunLog: async () => logEntries,
      getAccessCreatedRecords: async (_token, _runId, limit) => {
        createdLimit = limit
        return createdRecords
      },
      getOpenQuarantineItems: async (_token, _accessImportRunId, limit) => {
        quarantineLimit = limit
        return quarantineItems
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={importClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Импорт')
    const importPanel = await screen.findByRole('region', { name: 'Импорт Access' })

    await user.click(within(importPanel).getByRole('tab', { name: /Лог/ }))
    const logCounter = await within(importPanel).findByText('Показано 10 из 11 строк лога')
    expect(within(importPanel).getByText('step_10')).toBeInTheDocument()
    expect(within(importPanel).queryByText('step_11')).not.toBeInTheDocument()

    await user.click(within(importPanel).getByRole('tab', { name: /Создано/ }))
    const createdCounter = await within(importPanel).findByText('Показано 10 из 11 созданных записей')
    expect(within(importPanel).getByText('Гараж 10')).toBeInTheDocument()
    expect(within(importPanel).queryByText('Гараж 11')).not.toBeInTheDocument()

    await user.click(within(importPanel).getByRole('tab', { name: /История/ }))
    const runCounter = within(importPanel).getByText('Показано 8 из 9 запусков')

    await user.click(within(importPanel).getByRole('tab', { name: /Карантин/ }))
    const quarantineCounter = within(importPanel).getByText('Показано 8 из 9 строк карантина')
    expect(logCounter).toHaveAttribute('role', 'status')
    expect(logCounter).toHaveAttribute('aria-live', 'polite')
    expect(createdCounter).toHaveAttribute('role', 'status')
    expect(createdCounter).toHaveAttribute('aria-live', 'polite')
    expect(runCounter).toHaveAttribute('role', 'status')
    expect(runCounter).toHaveAttribute('aria-live', 'polite')
    expect(quarantineCounter).toHaveAttribute('role', 'status')
    expect(quarantineCounter).toHaveAttribute('aria-live', 'polite')
    expect(createdLimit).toBe(100)
    expect(quarantineLimit).toBe(50)
  })

  it('shows accessible empty states for Access import lists', async () => {
    const user = userEvent.setup()
    const importClient = createImportClient({
      getAccessRuns: async () => [],
      getOpenQuarantineItems: async () => [],
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={importClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Импорт')
    const importPanel = await screen.findByRole('region', { name: 'Импорт Access' })

    const emptyRunState = await within(importPanel).findByText('Выберите запуск dry-run')
    const emptyCheckState = within(importPanel).getByText('Проверок пока нет')

    await user.click(within(importPanel).getByRole('tab', { name: /Лог/ }))
    const emptyLogState = within(importPanel).getByText('Лог выбранного запуска пока пуст')

    await user.click(within(importPanel).getByRole('tab', { name: /Создано/ }))
    const emptyCreatedState = within(importPanel).getByText('Созданные записи появятся после фактического переноса Access')

    await user.click(within(importPanel).getByRole('tab', { name: /История/ }))
    const emptyHistoryState = within(importPanel).getByText('Истории импорта пока нет')

    await user.click(within(importPanel).getByRole('tab', { name: /Карантин/ }))
    const emptyQuarantineState = within(importPanel).getByText('Открытых строк карантина нет')
    for (const state of [emptyRunState, emptyCheckState, emptyLogState, emptyCreatedState, emptyHistoryState, emptyQuarantineState]) {
      expect(state).toHaveAttribute('role', 'status')
      expect(state).toHaveAttribute('aria-live', 'polite')
    }
  })

  it('shows Access import created records for selected run', async () => {
    const user = userEvent.setup()
    const run = createAccessImportRun()
    let requestedRunId: string | undefined
    const importClient = createImportClient({
      getAccessRuns: async () => [run],
      getAccessCreatedRecords: async (_token, runId) => {
        requestedRunId = runId
        return [
          createAccessImportCreatedRecord({
            accessImportRunId: runId,
            sourceEntityType: 'Garage',
            sourceExternalId: '12',
            targetEntityType: 'garage',
            targetEntityId: 'garage-12',
            targetDisplayName: 'Гараж 12',
          }),
          createAccessImportCreatedRecord({
            id: 'created-payment',
            accessImportRunId: runId,
            sourceEntityType: 'Payment',
            sourceExternalId: 'PAY-12',
            targetEntityType: 'financial_operation',
            targetEntityId: 'operation-12',
            targetDisplayName: 'Платеж PAY-12',
            rollbackStatus: 'rolled_back',
          }),
        ]
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={importClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Импорт')
    const importPanel = await screen.findByRole('region', { name: 'Импорт Access' })

    await user.click(within(importPanel).getByRole('tab', { name: /Создано/ }))
    const createdTable = await within(importPanel).findByRole('table', { name: 'Созданные импортом записи Access' })

    expect(requestedRunId).toBe(run.id)
    expect(within(createdTable).getByText('Гараж 12')).toBeInTheDocument()
    expect(within(createdTable).getByText('Garage #12')).toBeInTheDocument()
    expect(within(createdTable).getByText('Ожидает rollback')).toBeInTheDocument()
    expect(within(createdTable).getByText('Платеж PAY-12')).toBeInTheDocument()
    expect(within(createdTable).getByText('Payment #PAY-12')).toBeInTheDocument()
    expect(within(createdTable).getByText('Откат выполнен')).toBeInTheDocument()
  })

  it('shows and resolves Access import quarantine rows', async () => {
    const user = userEvent.setup()
    let quarantineItems = [createAccessImportQuarantineItem()]
    let lastResolutionComment: string | undefined
    const importClient = createImportClient({
      getOpenQuarantineItems: async () => quarantineItems,
      resolveQuarantineItem: async (_token, itemId, resolutionComment) => {
        lastResolutionComment = resolutionComment
        quarantineItems = quarantineItems.filter((item) => item.id !== itemId)
        return createAccessImportQuarantineItem({
          id: itemId,
          status: 'resolved',
          resolutionComment: resolutionComment ?? null,
        })
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={importClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Импорт')
    const importPanel = await screen.findByRole('region', { name: /Access/ })
    await user.click(within(importPanel).getByRole('tab', { name: /Карантин/ }))
    const quarantineTable = await within(importPanel).findByRole('table', { name: 'Карантин импорта Access' })

    expect(within(quarantineTable).getByText('Garage #42')).toBeInTheDocument()
    expect(within(quarantineTable).getByText('missing-owner')).toBeInTheDocument()

    const resolveButton = within(quarantineTable).getByRole('button', { name: 'Закрыть' })
    expect(resolveButton).toHaveAttribute('title', 'Закрыть строку карантина Garage #42')
    expect(resolveButton).toHaveAttribute('data-tooltip', 'Закрыть')
    await user.click(resolveButton)
    const resolveDialog = await screen.findByRole('dialog', { name: 'Закрыть строку карантина?' })
    const closeResolveDialogButton = within(resolveDialog).getByRole('button', { name: 'Закрыть подтверждение карантина' })
    expect(closeResolveDialogButton).toHaveAttribute('title', 'Закрыть подтверждение карантина')
    expect(closeResolveDialogButton).toHaveAttribute('data-tooltip', 'Закрыть')
    await waitFor(() => expect(within(resolveDialog).getByLabelText('Комментарий к закрытию строки карантина')).toHaveFocus())
    await user.click(within(resolveDialog).getByRole('button', { name: 'Закрыть строку' }))
    expect(within(resolveDialog).getByRole('alert')).toHaveTextContent('Укажите комментарий к закрытию строки карантина.')
    await user.type(within(resolveDialog).getByLabelText('Комментарий к закрытию строки карантина'), 'Владелец найден и сопоставлен вручную')
    await user.click(within(resolveDialog).getByRole('button', { name: 'Закрыть строку' }))

    expect(await within(importPanel).findByText('Строка карантина закрыта.')).toHaveAttribute('role', 'status')
    expect(lastResolutionComment).toBe('Владелец найден и сопоставлен вручную')
    expect(within(quarantineTable).queryByText('Garage #42')).not.toBeInTheDocument()
    const emptyQuarantineState = within(quarantineTable).getByText('Открытых строк карантина нет')
    expect(emptyQuarantineState).toHaveAttribute('role', 'status')
    expect(emptyQuarantineState).toHaveAttribute('aria-live', 'polite')
  })

  it('shows audit journal for users with audit permission', async () => {
    const user = userEvent.setup()
    let auditRequest: Parameters<AuditClient['getEvents']>[1] = undefined
    let auditExportRequest: Parameters<AuditClient['exportEvents']>[1] = undefined
    let auditXlsxExportRequest: Parameters<AuditClient['exportEventsXlsx']>[1] = undefined
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      login: async () => ({
        ...auth,
        user: {
          ...auth.user,
          permissions: [...auth.user.permissions, 'audit.read'],
        },
      }),
    })
    const auditClient = createAuditClient({
      getEvents: async (_token, params) => {
        auditRequest = params
        if (params?.search?.toLowerCase().includes('import')) {
          return [createAuditEvent({ action: 'import.access_dry_run', entityType: 'access_import_run', summary: 'Проверка Access.' })]
        }
        return [
          createAuditEvent({ action: 'auth.login_success', entityType: 'user', summary: 'Вход пользователя.' }),
          createAuditEvent({ action: 'finance.income_created', entityType: 'financial_operation', summary: 'Создано поступление.' }),
        ]
      },
      exportEvents: async (_token, params) => {
        auditExportRequest = params
        return new Blob(['createdAtUtc,action\n2026-06-23T10:00:00Z,import.access_dry_run\n'], { type: 'text/csv' })
      },
      exportEventsXlsx: async (_token, params) => {
        auditXlsxExportRequest = params
        return new Blob(['xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
      },
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })

    const auditTable = within(auditPanel).getByRole('table', { name: 'События истории изменений' })
    expect(await within(auditTable).findByText('Вход и безопасность')).toBeInTheDocument()
    expect(within(auditTable).getByText('Финансы')).toBeInTheDocument()
    expect(within(auditTable).getByText('Платеж или выплата')).toBeInTheDocument()

    await user.type(within(auditPanel).getByLabelText('Поиск в истории изменений'), 'import')

    const filteredAuditTable = within(auditPanel).getByRole('table', { name: 'События истории изменений' })
    expect(await within(filteredAuditTable).findByText('Импорт Access')).toBeInTheDocument()
    expect(auditRequest?.search).toBe('import')
    expect(auditRequest?.limit).toBe(25)

    await user.click(within(auditPanel).getByRole('button', { name: /CSV/ }))

    expect(auditExportRequest?.search).toBe('import')
    expect(auditExportRequest?.limit).toBeUndefined()
    expect(await within(auditPanel).findByText('История изменений CSV готова.')).toHaveAttribute('role', 'status')

    await user.click(within(auditPanel).getByRole('button', { name: /XLSX/ }))

    expect(auditXlsxExportRequest?.search).toBe('import')
    expect(auditXlsxExportRequest?.limit).toBeUndefined()
    expect(await within(auditPanel).findByText('История изменений XLSX готова.')).toHaveAttribute('role', 'status')
  })

  it('filters audit journal by section action kind entity type actor quick filter and date range', async () => {
    const user = userEvent.setup()
    let auditRequest: Parameters<AuditClient['getEvents']>[1] = undefined
    let auditExportRequest: Parameters<AuditClient['exportEvents']>[1] = undefined
    const actorUserId = '5df20dec-2959-4726-a1cb-0e6ec6b28674'
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      login: async () => ({
        ...auth,
        user: {
          ...auth.user,
          permissions: [...auth.user.permissions, 'audit.read'],
        },
      }),
    })
    const auditClient = createAuditClient({
      getEvents: async (_token, params) => {
        auditRequest = params
        return [
          createAuditEvent({
            action: 'dictionary.owner_updated',
            entityType: 'owner',
            entityId: 'owner-1',
            entityDisplayName: 'Garage 12',
            summary: 'Изменен владелец.',
            section: 'dictionary',
            actionKind: 'update',
            fieldName: 'Владелец',
            oldValue: 'Иванов Иван',
            newValue: 'Петров Петр',
            reason: 'Смена собственника',
          }),
        ]
      },
      exportEvents: async (_token, params) => {
        auditExportRequest = params
        return new Blob(['createdAtUtc,action\n2026-06-23T10:00:00Z,dictionary.owner_updated\n'], { type: 'text/csv' })
      },
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })

    await user.selectOptions(within(auditPanel).getByLabelText('Раздел истории изменений'), 'dictionary')
    await user.selectOptions(within(auditPanel).getByLabelText('Тип действия истории изменений'), 'update')
    await user.selectOptions(within(auditPanel).getByLabelText('Тип объекта истории изменений'), 'owner')
    await user.type(within(auditPanel).getByLabelText('ID пользователя истории изменений'), actorUserId)
    await user.selectOptions(within(auditPanel).getByLabelText('Быстрый фильтр истории изменений'), 'restores')
    await user.type(within(auditPanel).getByLabelText('Связанный гараж истории изменений'), '12')
    await user.type(within(auditPanel).getByLabelText('Связанный месяц истории изменений'), '2026-06')
    await user.type(within(auditPanel).getByLabelText('Связанный контрагент истории изменений'), 'Energy')
    await user.type(within(auditPanel).getByLabelText('Связанный документ истории изменений'), 'PAY-2026')
    await user.type(within(auditPanel).getByLabelText('Начало периода истории изменений'), '2026-06-01')
    await user.type(within(auditPanel).getByLabelText('Конец периода истории изменений'), '2026-06-30')

    await waitFor(() => {
      expect(auditRequest?.section).toBe('dictionary')
      expect(auditRequest?.actionKind).toBe('update')
      expect(auditRequest?.entityType).toBe('owner')
      expect(auditRequest?.actorUserId).toBe(actorUserId)
      expect(auditRequest?.quickFilter).toBe('restores')
      expect(auditRequest?.relatedGarage).toBe('12')
      expect(auditRequest?.relatedAccountingMonth).toBe('2026-06')
      expect(auditRequest?.relatedCounterparty).toBe('Energy')
      expect(auditRequest?.relatedDocument).toBe('PAY-2026')
      expect(auditRequest?.dateFrom).toBe('2026-06-01')
      expect(auditRequest?.dateTo).toBe('2026-06-30')
      expect(auditRequest?.limit).toBe(25)
    })

    const auditTable = within(auditPanel).getByRole('table', { name: 'События истории изменений' })
    expect(await within(auditTable).findByText('Справочники')).toBeInTheDocument()
    expect(within(auditTable).getByText('Изменение')).toBeInTheDocument()
    expect(within(auditTable).getByText('Garage 12')).toBeInTheDocument()
    expect(within(auditTable).getAllByText('Владелец')).toHaveLength(2)
    expect(within(auditTable).getByText('Иванов Иван')).toBeInTheDocument()
    expect(within(auditTable).getByText('Петров Петр')).toBeInTheDocument()
    expect(within(auditTable).getByText('Смена собственника')).toBeInTheDocument()

    await user.click(within(auditPanel).getByRole('button', { name: /CSV/ }))

    expect(auditExportRequest?.section).toBe('dictionary')
    expect(auditExportRequest?.actionKind).toBe('update')
    expect(auditExportRequest?.entityType).toBe('owner')
    expect(auditExportRequest?.actorUserId).toBe(actorUserId)
    expect(auditExportRequest?.quickFilter).toBe('restores')
    expect(auditExportRequest?.relatedGarage).toBe('12')
    expect(auditExportRequest?.relatedAccountingMonth).toBe('2026-06')
    expect(auditExportRequest?.relatedCounterparty).toBe('Energy')
    expect(auditExportRequest?.relatedDocument).toBe('PAY-2026')
    expect(auditExportRequest?.dateFrom).toBe('2026-06-01')
    expect(auditExportRequest?.dateTo).toBe('2026-06-30')
    expect(auditExportRequest?.limit).toBeUndefined()
  })

  it('filters audit journal by reports section and report entity type', async () => {
    const user = userEvent.setup()
    let auditRequest: Parameters<AuditClient['getEvents']>[1] = undefined
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      login: async () => ({
        ...auth,
        user: {
          ...auth.user,
          permissions: [...auth.user.permissions, 'audit.read'],
        },
      }),
    })
    const auditClient = createAuditClient({
      getEvents: async (_token, params) => {
        auditRequest = params
        return [
          createAuditEvent({
            id: 'audit-report-filter',
            action: 'reports.consolidated_generated',
            entityType: 'report',
            entityId: 'consolidated',
            entityDisplayName: 'Сводный отчет',
            summary: 'Сформирован сводный отчет.',
            section: 'reports',
            actionKind: 'generate',
          }),
        ]
      },
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })

    await user.selectOptions(within(auditPanel).getByLabelText('Раздел истории изменений'), 'reports')
    await user.selectOptions(within(auditPanel).getByLabelText('Тип объекта истории изменений'), 'report')

    await waitFor(() => {
      expect(auditRequest?.section).toBe('reports')
      expect(auditRequest?.entityType).toBe('report')
    })

    const auditTable = within(auditPanel).getByRole('table', { name: 'События истории изменений' })
    expect(await within(auditTable).findByText('Отчеты')).toBeInTheDocument()
    expect(within(auditTable).getByText('Отчет')).toBeInTheDocument()
    expect(within(auditTable).getByText('Сводный отчет')).toBeInTheDocument()
  })

  it('filters audit journal by report export action kind', async () => {
    const user = userEvent.setup()
    let auditRequest: Parameters<AuditClient['getEvents']>[1] = undefined
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      login: async () => ({
        ...auth,
        user: {
          ...auth.user,
          permissions: [...auth.user.permissions, 'audit.read'],
        },
      }),
    })
    const auditClient = createAuditClient({
      getEvents: async (_token, params) => {
        auditRequest = params
        return [
          createAuditEvent({
            id: 'audit-report-export-filter',
            action: 'reports.consolidated_exported',
            entityType: 'report',
            entityId: 'consolidated',
            entityDisplayName: 'Сводный отчет XLSX',
            summary: 'Выгружен сводный отчет.',
            section: 'reports',
            actionKind: 'export',
          }),
        ]
      },
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })

    await user.selectOptions(within(auditPanel).getByLabelText('Раздел истории изменений'), 'reports')
    await user.selectOptions(within(auditPanel).getByLabelText('Тип действия истории изменений'), 'export')
    await user.selectOptions(within(auditPanel).getByLabelText('Тип объекта истории изменений'), 'report')

    await waitFor(() => {
      expect(auditRequest?.section).toBe('reports')
      expect(auditRequest?.actionKind).toBe('export')
      expect(auditRequest?.entityType).toBe('report')
    })

    const auditTable = within(auditPanel).getByRole('table', { name: 'События истории изменений' })
    expect(await within(auditTable).findByText('Отчеты')).toBeInTheDocument()
    expect(within(auditTable).getByText('Выгрузка')).toBeInTheDocument()
    expect(within(auditTable).getByText('Сводный отчет XLSX')).toBeInTheDocument()
  })

  it('does not call audit APIs when the date range is invalid', async () => {
    const user = userEvent.setup()
    let pageCalls = 0
    const pageRequests: Array<Parameters<AuditClient['getEventsPage']>[1]> = []
    let csvExportCalls = 0
    let xlsxExportCalls = 0
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      login: async () => ({
        ...auth,
        user: {
          ...auth.user,
          permissions: [...auth.user.permissions, 'audit.read'],
        },
      }),
    })
    const auditClient = createAuditClient({
      getEventsPage: async (_token, params) => {
        pageCalls += 1
        pageRequests.push(params)
        const event = createAuditEvent({
          id: 'audit-date-validation',
          action: 'reports.consolidated_generated',
          entityType: 'report',
          summary: 'Сформирован сводный отчет.',
          section: 'reports',
          actionKind: 'generate',
        })
        return {
          items: [event],
          totalCount: 1,
          offset: params?.offset ?? 0,
          limit: params?.limit ?? 25,
        }
      },
      exportEvents: async () => {
        csvExportCalls += 1
        return new Blob(['createdAtUtc,action\n'], { type: 'text/csv' })
      },
      exportEventsXlsx: async () => {
        xlsxExportCalls += 1
        return new Blob(['xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
      },
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })
    await waitFor(() => expect(pageCalls).toBeGreaterThan(0))

    fireEvent.change(within(auditPanel).getByLabelText('Конец периода истории изменений'), { target: { value: '2026-06-30' } })
    fireEvent.change(within(auditPanel).getByLabelText('Начало периода истории изменений'), { target: { value: '2026-07-01' } })

    expect(await within(auditPanel).findByText('Проверьте период истории')).toBeInTheDocument()
    expect(within(auditPanel).getByText('Начало периода истории изменений не может быть позже конца.')).toBeInTheDocument()
    expect(pageRequests.some((request) => request?.dateFrom === '2026-07-01' && request?.dateTo === '2026-06-30')).toBe(false)
    expect(within(auditPanel).getByRole('button', { name: /CSV/ })).toBeDisabled()
    expect(within(auditPanel).getByRole('button', { name: /XLSX/ })).toBeDisabled()
    expect(csvExportCalls).toBe(0)
    expect(xlsxExportCalls).toBe(0)

    fireEvent.change(within(auditPanel).getByLabelText('Конец периода истории изменений'), { target: { value: '2026-07-31' } })

    await waitFor(() => expect(pageRequests.some((request) => request?.dateFrom === '2026-07-01' && request?.dateTo === '2026-07-31')).toBe(true))
    expect(within(auditPanel).queryByText('Проверьте период истории')).not.toBeInTheDocument()
  })

  it('opens audit event detail dialog from the journal table', async () => {
    const user = userEvent.setup()
    let loadedEventId: string | null = null
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      login: async () => ({
        ...auth,
        user: {
          ...auth.user,
          permissions: [...auth.user.permissions, 'audit.read'],
        },
      }),
    })
    const auditClient = createAuditClient({
      getEvents: async () => [
        createAuditEvent({
          id: 'audit-detail-1',
          action: 'dictionary.owner_updated',
          entityType: 'owner',
          entityId: 'owner-1',
          entityDisplayName: 'Garage 12',
          relatedGarageNumber: '12',
          relatedAccountingMonth: '2026-06',
          relatedCounterpartyName: 'Иванов Иван',
          relatedDocumentNumber: 'PAY-2026-06-12',
          summary: 'Изменен владелец.',
          section: 'dictionary',
          actionKind: 'update',
        }),
      ],
      getEvent: async (_token, id) => {
        loadedEventId = id
        return createAuditEvent({
          id,
          action: 'dictionary.owner_updated',
          entityType: 'owner',
          entityId: 'owner-1',
          entityDisplayName: 'Garage 12',
          relatedGarageNumber: '12',
          relatedAccountingMonth: '2026-06',
          relatedCounterpartyName: 'Иванов Иван',
          relatedDocumentNumber: 'PAY-2026-06-12',
          summary: 'Изменен владелец.',
          section: 'dictionary',
          actionKind: 'update',
          fieldName: 'Владелец',
          oldValue: 'Иванов Иван',
          newValue: 'Петров Петр',
          reason: 'Смена собственника',
          metadata: {
            reason: 'manual_fix',
            email: '[email скрыт]',
            apiToken: '[секрет скрыт]',
          },
        })
      },
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })

    const openDetailButton = await within(auditPanel).findByRole('button', { name: 'Открыть' })
    await user.click(openDetailButton)

    const detailDialog = await screen.findByRole('dialog', { name: 'Изменение' })
    expect(loadedEventId).toBe('audit-detail-1')
    expect(within(detailDialog).getByText('Карточка события')).toBeInTheDocument()
    expect(within(detailDialog).getByText('dictionary.owner_updated')).toBeInTheDocument()
    expect(within(detailDialog).getByText('owner-1')).toBeInTheDocument()
    expect(within(detailDialog).getByText('Garage 12')).toBeInTheDocument()
    expect(within(detailDialog).getByText('Связанные данные')).toBeInTheDocument()
    expect(within(detailDialog).getByText('№ 12')).toBeInTheDocument()
    expect(within(detailDialog).getByText('2026-06')).toBeInTheDocument()
    expect(within(detailDialog).getByText('PAY-2026-06-12')).toBeInTheDocument()
    expect(within(detailDialog).getAllByText('Владелец')).toHaveLength(2)
    expect(within(detailDialog).getAllByText('Иванов Иван')).toHaveLength(2)
    expect(within(detailDialog).getByText('Петров Петр')).toBeInTheDocument()
    expect(within(detailDialog).getByText('Смена собственника')).toBeInTheDocument()
    expect(within(detailDialog).getByText('Служебные данные')).toBeInTheDocument()
    expect(within(detailDialog).getByText('manual_fix')).toBeInTheDocument()
    expect(within(detailDialog).getByText('[email скрыт]')).toBeInTheDocument()
    expect(within(detailDialog).getByText('[секрет скрыт]')).toBeInTheDocument()
    expect(detailDialog).toHaveAttribute('aria-modal', 'true')

    const detailCloseIconButton = within(detailDialog).getByRole('button', { name: 'Закрыть карточку события' })
    const detailWorkspaceButton = within(detailDialog).getByRole('button', { name: 'Открыть раздел: Контрагенты' })
    const detailCloseButton = within(detailDialog).getByRole('button', { name: 'Закрыть' })
    await waitFor(() => expect(detailCloseIconButton).toHaveFocus())
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(detailCloseButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(detailCloseIconButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(detailWorkspaceButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(detailCloseButton).toHaveFocus()
    await user.keyboard('{Escape}')

    expect(screen.queryByRole('dialog', { name: 'Изменение' })).not.toBeInTheDocument()
    await waitFor(() => expect(openDetailButton).toHaveFocus())

    await user.click(openDetailButton)
    const reopenedDetailDialog = await screen.findByRole('dialog', { name: 'Изменение' })
    await user.click(within(reopenedDetailDialog).getByRole('button', { name: 'Открыть раздел: Контрагенты' }))
    expect(screen.queryByRole('dialog', { name: 'Изменение' })).not.toBeInTheDocument()
    expect(await screen.findByRole('region', { name: 'Контрагенты' })).toBeInTheDocument()
  })

  it('hides audit event workspace links when the user cannot open the target section', async () => {
    const user = userEvent.setup()
    const auth = createAuthResponse({
      user: {
        permissions: ['users.manage', 'audit.read'],
      },
    })
    const authClient = createAuthClient({
      login: async () => auth,
    })
    const auditClient = createAuditClient({
      getEvents: async () => [
        createAuditEvent({
          id: 'audit-detail-no-dictionary-access',
          action: 'dictionary.owner_updated',
          entityType: 'owner',
          entityId: 'owner-1',
          entityDisplayName: 'Garage 12',
          summary: 'Изменен владелец.',
          section: 'dictionary',
          actionKind: 'update',
        }),
      ],
      getEvent: async (_token, id) => createAuditEvent({
        id,
        action: 'dictionary.owner_updated',
        entityType: 'owner',
        entityId: 'owner-1',
        entityDisplayName: 'Garage 12',
        summary: 'Изменен владелец.',
        section: 'dictionary',
        actionKind: 'update',
      }),
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })

    await user.click(await within(auditPanel).findByRole('button', { name: 'Открыть' }))
    const detailDialog = await screen.findByRole('dialog', { name: 'Изменение' })

    expect(within(detailDialog).getByText('Garage 12')).toBeInTheDocument()
    expect(within(detailDialog).queryByRole('button', { name: 'Открыть раздел: Контрагенты' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Контрагенты' })).toBeDisabled()
  })

  it('opens reports workspace from audit event detail when the user has report access', async () => {
    const user = userEvent.setup()
    const auth = createAuthResponse({
      user: {
        permissions: ['users.manage', 'dictionaries.read', 'reports.read', 'audit.read'],
      },
    })
    const authClient = createAuthClient({
      login: async () => auth,
    })
    const reportAuditEvent = createAuditEvent({
      id: 'audit-report-detail',
      action: 'reports.consolidated_generated',
      entityType: 'report',
      entityId: 'consolidated',
      entityDisplayName: 'Сводный отчет',
      summary: 'Сформирован сводный отчет.',
      section: 'reports',
      actionKind: 'generate',
    })
    const auditClient = createAuditClient({
      getEvents: async () => [reportAuditEvent],
      getEvent: async (_token, id) => ({
        ...reportAuditEvent,
        id,
      }),
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })

    const openDetailButton = await within(auditPanel).findByRole('button', { name: 'Открыть' })
    await user.click(openDetailButton)
    const detailDialog = await screen.findByRole('dialog', { name: 'Формирование' })
    const detailCloseIconButton = within(detailDialog).getByRole('button', { name: 'Закрыть карточку события' })
    const openReportsButton = within(detailDialog).getByRole('button', { name: 'Открыть раздел: Отчеты' })
    const detailCloseButton = within(detailDialog).getByRole('button', { name: 'Закрыть' })

    expect(within(detailDialog).getByText('Сводный отчет')).toBeInTheDocument()
    await waitFor(() => expect(detailCloseIconButton).toHaveFocus())
    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(detailCloseButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(detailCloseIconButton).toHaveFocus()
    await user.keyboard('{Tab}')
    expect(openReportsButton).toHaveFocus()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Формирование' })).not.toBeInTheDocument()
    await waitFor(() => expect(openDetailButton).toHaveFocus())

    await user.click(openDetailButton)
    const reopenedDetailDialog = await screen.findByRole('dialog', { name: 'Формирование' })
    await user.click(within(reopenedDetailDialog).getByRole('button', { name: 'Открыть раздел: Отчеты' }))

    expect(screen.queryByRole('dialog', { name: 'Формирование' })).not.toBeInTheDocument()
    expect(await screen.findByRole('region', { name: 'Отчеты' })).toBeInTheDocument()
  })

  it('retries audit event detail loading inside the dialog', async () => {
    const user = userEvent.setup()
    let detailLoadCount = 0
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      login: async () => ({
        ...auth,
        user: {
          ...auth.user,
          permissions: [...auth.user.permissions, 'audit.read'],
        },
      }),
    })
    const auditClient = createAuditClient({
      getEvents: async () => [
        createAuditEvent({
          id: 'audit-detail-retry',
          action: 'dictionary.owner_updated',
          entityType: 'owner',
          entityId: 'owner-1',
          summary: 'Изменен владелец.',
          section: 'dictionary',
          actionKind: 'update',
        }),
      ],
      getEvent: async (_token, id) => {
        detailLoadCount += 1
        if (detailLoadCount === 1) {
          throw new Error('Карточка временно недоступна')
        }

        return createAuditEvent({
          id,
          action: 'dictionary.owner_updated',
          entityType: 'owner',
          entityId: 'owner-1',
          summary: 'Изменен владелец.',
          section: 'dictionary',
          actionKind: 'update',
          fieldName: 'Владелец',
          oldValue: 'Иванов Иван',
          newValue: 'Петров Петр',
        })
      },
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })

    const openDetailButton = await within(auditPanel).findByRole('button', { name: 'Открыть' })
    await user.click(openDetailButton)
    const detailDialog = await screen.findByRole('dialog', { name: 'Изменение' })
    const detailCloseIconButton = within(detailDialog).getByRole('button', { name: 'Закрыть карточку события' })
    await waitFor(() => expect(detailCloseIconButton).toHaveFocus())
    expect(await within(detailDialog).findByText('Карточка временно недоступна')).toHaveAttribute('role', 'alert')

    await user.keyboard('{Tab}')
    const retryButton = within(detailDialog).getByRole('button', { name: 'Повторить загрузку карточки' })
    expect(retryButton).toHaveFocus()
    await user.click(retryButton)

    expect(await within(detailDialog).findByText('Петров Петр')).toBeInTheDocument()
    expect(within(detailDialog).queryByText('Карточка временно недоступна')).not.toBeInTheDocument()
    expect(detailLoadCount).toBe(2)
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Изменение' })).not.toBeInTheDocument()
    await waitFor(() => expect(openDetailButton).toHaveFocus())
  })

  it('paginates audit journal on the server', async () => {
    const user = userEvent.setup()
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      login: async () => ({
        ...auth,
        user: {
          ...auth.user,
          permissions: [...auth.user.permissions, 'audit.read'],
        },
      }),
    })
    const auditEvents = Array.from({ length: 30 }, (_item, index) =>
      createAuditEvent({
        id: `audit-event-${index + 1}`,
        action: `audit.event_${index + 1}`,
        entityType: 'audit_event',
        summary: `Событие ${index + 1}.`,
      }),
    )
    const auditClient = createAuditClient({
      getEvents: async () => auditEvents,
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })
    const auditTable = within(auditPanel).getByRole('table', { name: 'События истории изменений' })

    expect(await within(auditPanel).findByText('Показано 1-25 из 30')).toHaveAttribute('role', 'status')
    expect(within(auditTable).getByText('audit.event_25')).toBeInTheDocument()
    expect(within(auditTable).queryByText('audit.event_26')).not.toBeInTheDocument()

    await user.click(within(auditPanel).getByRole('button', { name: 'Вперед' }))

    expect(await within(auditPanel).findByText('Показано 26-30 из 30')).toHaveAttribute('role', 'status')
    expect(within(auditTable).getByText('audit.event_30')).toBeInTheDocument()
    expect(within(auditTable).queryByText('audit.event_25')).not.toBeInTheDocument()
  })

  it('announces empty audit journal for users with audit permission', async () => {
    const user = userEvent.setup()
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      login: async () => ({
        ...auth,
        user: {
          ...auth.user,
          permissions: [...auth.user.permissions, 'audit.read'],
        },
      }),
    })
    const auditClient = createAuditClient({
      getEvents: async () => [],
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })
    const auditTable = within(auditPanel).getByRole('table', { name: 'События истории изменений' })

    expect(await within(auditTable).findByText('Событий пока нет')).toHaveAttribute('role', 'status')
  })

  it('retries audit journal loading after an error', async () => {
    const user = userEvent.setup()
    let loadCount = 0
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      login: async () => ({
        ...auth,
        user: {
          ...auth.user,
          permissions: [...auth.user.permissions, 'audit.read'],
        },
      }),
    })
    const auditClient = createAuditClient({
      getEventsPage: async (_token, params) => {
        loadCount += 1
        if (loadCount === 1) {
          throw new Error('Журнал временно недоступен')
        }

        return {
          items: [createAuditEvent({ action: 'dictionary.owner_restored', entityType: 'owner', summary: 'Владелец восстановлен.' })],
          totalCount: 1,
          offset: params?.offset ?? 0,
          limit: params?.limit ?? 25,
        }
      },
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })

    expect(await within(auditPanel).findByText('Журнал временно недоступен')).toHaveAttribute('role', 'alert')
    await user.click(within(auditPanel).getByRole('button', { name: 'Повторить загрузку' }))

    const auditTable = within(auditPanel).getByRole('table', { name: 'События истории изменений' })
    expect(await within(auditTable).findByText('Владелец восстановлен.')).toBeInTheDocument()
    expect(within(auditPanel).queryByText('Журнал временно недоступен')).not.toBeInTheDocument()
    expect(loadCount).toBe(2)
  })

  it('retries audit CSV export after an error', async () => {
    const user = userEvent.setup()
    let exportCount = 0
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      login: async () => ({
        ...auth,
        user: {
          ...auth.user,
          permissions: [...auth.user.permissions, 'audit.read'],
        },
      }),
    })
    const auditClient = createAuditClient({
      getEventsPage: async (_token, params) => ({
        items: [createAuditEvent({ action: 'dictionary.owner_updated', entityType: 'owner', summary: 'Изменен владелец.' })],
        totalCount: 1,
        offset: params?.offset ?? 0,
        limit: params?.limit ?? 25,
      }),
      exportEvents: async () => {
        exportCount += 1
        if (exportCount === 1) {
          throw new Error('CSV временно недоступен')
        }

        return new Blob(['createdAtUtc,action\n'], { type: 'text/csv' })
      },
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })
    expect(await within(auditPanel).findByText('Изменен владелец.')).toBeInTheDocument()

    await user.click(within(auditPanel).getByRole('button', { name: 'Скачать CSV' }))
    expect(await within(auditPanel).findByText('CSV временно недоступен')).toHaveAttribute('role', 'alert')
    expect(within(auditPanel).queryByRole('button', { name: 'Повторить загрузку' })).not.toBeInTheDocument()

    await user.click(within(auditPanel).getByRole('button', { name: 'Повторить выгрузку CSV' }))

    expect(await within(auditPanel).findByText('История изменений CSV готова.')).toHaveAttribute('role', 'status')
    expect(within(auditPanel).queryByText('CSV временно недоступен')).not.toBeInTheDocument()
    expect(exportCount).toBe(2)
  })

  it('retries audit XLSX export after an error', async () => {
    const user = userEvent.setup()
    let exportCount = 0
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      login: async () => ({
        ...auth,
        user: {
          ...auth.user,
          permissions: [...auth.user.permissions, 'audit.read'],
        },
      }),
    })
    const auditClient = createAuditClient({
      getEventsPage: async (_token, params) => ({
        items: [createAuditEvent({ action: 'dictionary.owner_updated', entityType: 'owner', summary: 'Изменен владелец.' })],
        totalCount: 1,
        offset: params?.offset ?? 0,
        limit: params?.limit ?? 25,
      }),
      exportEventsXlsx: async () => {
        exportCount += 1
        if (exportCount === 1) {
          throw new Error('XLSX временно недоступен')
        }

        return new Blob(['xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
      },
    })
    render(<App authClient={authClient} auditClient={auditClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'История изменений')
    const auditPanel = await screen.findByRole('region', { name: 'История изменений' })
    expect(await within(auditPanel).findByText('Изменен владелец.')).toBeInTheDocument()

    await user.click(within(auditPanel).getByRole('button', { name: 'Скачать XLSX' }))
    expect(await within(auditPanel).findByText('XLSX временно недоступен')).toHaveAttribute('role', 'alert')
    expect(within(auditPanel).queryByRole('button', { name: 'Повторить выгрузку CSV' })).not.toBeInTheDocument()

    await user.click(within(auditPanel).getByRole('button', { name: 'Повторить выгрузку XLSX' }))

    expect(await within(auditPanel).findByText('История изменений XLSX готова.')).toHaveAttribute('role', 'status')
    expect(within(auditPanel).queryByText('XLSX временно недоступен')).not.toBeInTheDocument()
    expect(exportCount).toBe(2)
  })

  it('shows report workbook tabs with Excel-like filters and tables', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Отчеты')
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    expect(screen.queryByText('Поиск по гаражу, владельцу или поставщику')).not.toBeInTheDocument()
    expect(within(reportsPanel).getByRole('tab', { name: /Консолидированный/ })).toHaveAttribute('aria-selected', 'true')
    for (const tabName of ['По гаражам', 'По выплатам', 'Поступления', 'Оплаты из кассы', 'Сдача кассы в банк', 'Сборы', 'Изменение фондов']) {
      expect(within(reportsPanel).getByRole('tab', { name: new RegExp(tabName) })).toBeInTheDocument()
    }

    expect(within(reportsPanel).getByText('Консолидированный отчёт')).toBeInTheDocument()
    const consolidatedTable = within(reportsPanel).getByRole('table', { name: 'Консолидированный отчет' })
    expect(consolidatedTable).toBeInTheDocument()
    await waitFor(() => expect(consolidatedTable).toHaveTextContent('Членский взнос'))
    expect(consolidatedTable).toHaveTextContent('Вода')
    expect(consolidatedTable).toHaveTextContent('На начало месяца')
    expect(consolidatedTable).toHaveTextContent('На конец месяца')
    expect(within(reportsPanel).queryByRole('button', { name: /Скачать сводный/ })).not.toBeInTheDocument()

    const consolidatedMonthFrom = within(reportsPanel).getByLabelText('Месяц с') as HTMLInputElement
    const consolidatedMonthTo = within(reportsPanel).getByLabelText('Месяц по') as HTMLInputElement
    const initialMonth = consolidatedMonthFrom.value
    const [yearText, monthText] = initialMonth.split('-')
    const previousDate = new Date(Number(yearText), Number(monthText) - 2, 1)
    const previousMonth = `${previousDate.getFullYear()}-${String(previousDate.getMonth() + 1).padStart(2, '0')}`
    await user.click(within(reportsPanel).getByRole('button', { name: 'Предыдущий' }))
    expect(consolidatedMonthFrom).toHaveValue(previousMonth)
    expect(consolidatedMonthTo).toHaveValue(previousMonth)

    await openReportTab(user, reportsPanel, 'По гаражам')
    expect(within(reportsPanel).getByText('Отчёт по гаражам')).toBeInTheDocument()
    const garageFilter = within(reportsPanel).getByLabelText('Гаражи') as HTMLInputElement
    await waitFor(() => expect(reportsPanel.querySelector('datalist option[value="Гараж 12"]')).not.toBeNull())
    await user.type(garageFilter, 'Гараж 99')
    const garageReportTable = within(reportsPanel).getByRole('table', { name: 'Отчет по гаражам' })
    await waitFor(() => expect(garageReportTable).toHaveTextContent('99'))
    expect(garageReportTable).toHaveTextContent('Членский взнос')
    expect(garageReportTable).toHaveTextContent('Начисления')
    expect(garageReportTable).toHaveTextContent('Поступления')
    const groupGarageAccrualsButton = within(reportsPanel).getByRole('button', { name: 'Сгруппировать начисления' })
    expect(groupGarageAccrualsButton).toHaveAttribute('aria-pressed', 'false')
    await user.click(groupGarageAccrualsButton)
    expect(within(reportsPanel).getByRole('button', { name: 'Разгруппировать начисления' })).toHaveAttribute('aria-pressed', 'true')
    const groupedGarageReportTable = within(reportsPanel).getByRole('table', { name: 'Отчет по гаражам' })
    expect(groupedGarageReportTable).not.toHaveTextContent('Услуга')
    expect(groupedGarageReportTable).not.toHaveTextContent('Членский взнос')
    await user.click(within(reportsPanel).getByRole('button', { name: 'Разгруппировать начисления' }))
    expect(within(reportsPanel).getByRole('table', { name: 'Отчет по гаражам' })).toHaveTextContent('Членский взнос')

    await openReportTab(user, reportsPanel, 'По выплатам')
    expect(within(reportsPanel).getByText('Отчёт по выплатам')).toBeInTheDocument()
    const counterpartyFilter = within(reportsPanel).getByLabelText('Поставщики или сотрудники') as HTMLInputElement
    await waitFor(() => expect(reportsPanel.querySelector('datalist option[value="Водоканал"]')).not.toBeNull())
    await user.type(counterpartyFilter, 'Водоканал')
    const payoutReportTable = within(reportsPanel).getByRole('table', { name: 'Отчет по выплатам' })
    expect(payoutReportTable).toHaveTextContent('Поставщик/сотрудник')
    expect(payoutReportTable).toHaveTextContent('Водоканал')
  })

  it('shows daily, fee and fund report filters with quick period buttons', async () => {
    const user = userEvent.setup()
    const exportCashPaymentReportXlsx = vi.fn(async () => new Blob(['cash xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }))
    const exportBankDepositReportPdf = vi.fn(async () => new Blob(['bank pdf'], { type: 'application/pdf' }))
    const exportFeeReportXlsx = vi.fn(async () => new Blob(['fees xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }))
    const exportFundChangeReportXlsx = vi.fn(async () => new Blob(['fund changes xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }))
    const reportClient = createReportClient({
      exportCashPaymentReportXlsx,
      exportBankDepositReportPdf,
      exportFeeReportXlsx,
      exportFundChangeReportXlsx,
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Отчеты')
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    await openReportTab(user, reportsPanel, 'Поступления')
    expect(within(reportsPanel).getByText('Отчет по поступлениям')).toBeInTheDocument()
    const incomeDateFrom = within(reportsPanel).getByLabelText('С') as HTMLInputElement
    const incomeDateTo = within(reportsPanel).getByLabelText('По') as HTMLInputElement
    const today = incomeDateFrom.value
    fireEvent.change(incomeDateFrom, { target: { value: '2026-01-01' } })
    fireEvent.change(incomeDateTo, { target: { value: '2026-01-02' } })
    await user.click(within(reportsPanel).getByRole('button', { name: 'Сегодня' }))
    expect(incomeDateFrom).toHaveValue(today)
    expect(incomeDateTo).toHaveValue(today)
    const incomeReportTable = within(reportsPanel).getByRole('table', { name: 'Отчет по поступлениям' })
    expect(incomeReportTable).toBeInTheDocument()
    expect(incomeReportTable).toHaveTextContent('10:24')
    expect(incomeReportTable).toHaveTextContent('1 500,00')
    expect(incomeReportTable).toHaveTextContent('Остаток долга после платежа')
    expect(incomeReportTable).toHaveTextContent('500,00')
    expect(incomeReportTable).not.toHaveTextContent('Начисление за июнь')

    await openReportTab(user, reportsPanel, 'Оплаты из кассы')
    expect(within(reportsPanel).getByText('Отчёт по оплатам из кассы')).toBeInTheDocument()
    const cashPaymentsTable = within(reportsPanel).getByRole('table', { name: 'Отчет по оплатам из кассы' })
    expect(cashPaymentsTable).toHaveTextContent('Вода: Водоканал')
    expect(cashPaymentsTable).toHaveTextContent('Оплата воды')
    expect(cashPaymentsTable).toHaveTextContent('400,00')
    expect(cashPaymentsTable).not.toHaveTextContent('Назначение платежа')
    const cashXlsxButton = within(reportsPanel).getByRole('button', { name: 'Скачать XLSX' })
    expect(cashXlsxButton).toHaveAttribute('title', 'Скачать XLSX')
    expect(cashXlsxButton).toHaveAttribute('data-tooltip', 'Скачать XLSX')
    expect(cashXlsxButton.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
    await user.click(cashXlsxButton)
    await waitFor(() => expect(exportCashPaymentReportXlsx).toHaveBeenCalledWith(expect.any(String), { dateFrom: today, dateTo: today }))

    await openReportTab(user, reportsPanel, 'Сдача кассы в банк')
    expect(within(reportsPanel).getByText('Отчёт по сдаче кассы в банк')).toBeInTheDocument()
    const bankDepositsTable = within(reportsPanel).getByRole('table', { name: 'Отчет по сдаче кассы в банк' })
    expect(bankDepositsTable).toHaveTextContent('Сдача наличных в банк')
    expect(bankDepositsTable).toHaveTextContent('3 000,00')
    const bankPdfButton = within(reportsPanel).getByRole('button', { name: 'Скачать PDF' })
    expect(bankPdfButton).toHaveAttribute('title', 'Скачать PDF')
    expect(bankPdfButton).toHaveAttribute('data-tooltip', 'Скачать PDF')
    expect(bankPdfButton.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
    await user.click(bankPdfButton)
    await waitFor(() => expect(exportBankDepositReportPdf).toHaveBeenCalledWith(expect.any(String), { dateFrom: today, dateTo: today }))

    await openReportTab(user, reportsPanel, 'Сборы')
    expect(within(reportsPanel).getByText('Отчёт по сборам')).toBeInTheDocument()
    const feeFilter = within(reportsPanel).getByLabelText('Вариация сбора') as HTMLInputElement
    await waitFor(() => expect(reportsPanel.querySelector('datalist option[value="Членский взнос"]')).not.toBeNull())
    await user.clear(feeFilter)
    await user.type(feeFilter, 'Членский взнос')
    const feeSummaryTable = within(reportsPanel).getByRole('table', { name: 'Отчет по сборам' })
    await waitFor(() => expect(feeSummaryTable).toHaveTextContent('Членский взнос'))
    const feeXlsxButton = within(reportsPanel).getByRole('button', { name: 'Скачать XLSX' })
    expect(feeXlsxButton.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
    await user.click(feeXlsxButton)
    await waitFor(() => expect(exportFeeReportXlsx).toHaveBeenCalledWith(expect.any(String), { variation: 'Членский взнос' }))
    expect(within(reportsPanel).queryByRole('table', { name: 'Должники по сбору' })).not.toBeInTheDocument()
    const showFeeDebtorsButton = within(reportsPanel).getByRole('button', { name: 'Показать должников' })
    await user.click(within(feeSummaryTable).getByRole('button', { name: 'Открыть детализацию сбора Членский взнос' }))
    const allFeeGaragesTable = within(reportsPanel).getByRole('table', { name: 'Гаражи по сбору' })
    expect(allFeeGaragesTable).toHaveTextContent('12')
    expect(allFeeGaragesTable).toHaveTextContent('1 000,00')
    expect(showFeeDebtorsButton).toHaveAttribute('aria-expanded', 'true')
    expect(showFeeDebtorsButton).toHaveTextContent('Скрыть должников')
    await user.click(within(reportsPanel).getByRole('button', { name: 'Только должники' }))
    const feeDebtorsTableFromSelectedFee = within(reportsPanel).getByRole('table', { name: 'Должники по сбору' })
    expect(feeDebtorsTableFromSelectedFee).toHaveTextContent('12')
    expect(feeDebtorsTableFromSelectedFee).toHaveTextContent('800,00')
    await user.click(showFeeDebtorsButton)
    expect(showFeeDebtorsButton).toHaveAttribute('aria-expanded', 'false')
    await user.click(showFeeDebtorsButton)
    expect(showFeeDebtorsButton).toHaveAttribute('aria-expanded', 'true')
    expect(showFeeDebtorsButton).toHaveTextContent('Скрыть должников')
    const feeDebtorsTable = within(reportsPanel).getByRole('table', { name: 'Должники по сбору' })
    expect(feeDebtorsTable).toHaveTextContent('12')
    expect(feeDebtorsTable).toHaveTextContent('800,00')

    await openReportTab(user, reportsPanel, 'Изменение фондов')
    expect(within(reportsPanel).getByText('Отчёт по изменению фондов')).toBeInTheDocument()
    expect(await within(reportsPanel).findByText(/Пополнено:\s*1\s*500,00/)).toBeInTheDocument()
    expect(within(reportsPanel).getByText(/Изъято:\s*300,00/)).toBeInTheDocument()
    const fundChangesTable = within(reportsPanel).getByRole('table', { name: 'Отчет по изменению фондов' })
    expect(fundChangesTable).toHaveTextContent('Электроэнергия')
    expect(fundChangesTable).toHaveTextContent('Пополнение')
    expect(fundChangesTable).toHaveTextContent('Изъятие')
    expect(fundChangesTable).toHaveTextContent('1 500,00')
    expect(fundChangesTable).toHaveTextContent('Распределение средств')
    expect(fundChangesTable).toHaveTextContent('Администратор ГСК')
    expect(fundChangesTable).not.toHaveTextContent('Резервный фонд')
    const fundXlsxButton = within(reportsPanel).getByRole('button', { name: 'Скачать XLSX' })
    expect(fundXlsxButton).toHaveAttribute('title', 'Скачать XLSX')
    expect(fundXlsxButton.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
    await user.click(fundXlsxButton)
    await waitFor(() => expect(exportFundChangeReportXlsx).toHaveBeenCalledWith(expect.any(String), { dateFrom: today, dateTo: today }))
  })
  it('shows login errors without opening protected workspace', async () => {
    const user = userEvent.setup()
    const authClient = createAuthClient({
      login: async () => {
        throw new Error('Неверный email или пароль.')
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)
    await user.type(screen.getByLabelText('Пароль'), 'WrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    expect(await screen.findByText('Неверный email или пароль.')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /финансовый учет гск/i })).not.toBeInTheDocument()
  })

  it('shows password policy error without opening workspace', async () => {
    const user = userEvent.setup()
    const authClient = createAuthClient({
      login: async () => {
        throw new Error('Пароль должен содержать хотя бы одну цифру.')
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'Password1')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    expect(await screen.findByText('Пароль должен содержать хотя бы одну цифру.')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /финансовый учет гск/i })).not.toBeInTheDocument()
  })

  it('shows auth validation summary before calling backend', async () => {
    const user = userEvent.setup()
    let loginCalled = false
    const authClient = createAuthClient({
      login: async () => {
        loginCalled = true
        return createAuthResponse()
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'Password')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    expect(await screen.findByText('Проверьте форму входа')).toBeInTheDocument()
    expect(screen.getByText('Добавьте хотя бы одну цифру в пароль.')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(loginCalled).toBe(false)
    expect(screen.queryByRole('heading', { name: /финансовый учет гск/i })).not.toBeInTheDocument()
  })

  it('shows rate limit message without opening protected workspace', async () => {
    const user = userEvent.setup()
    const authClient = createAuthClient({
      login: async () => {
        throw new Error('Слишком много неуспешных попыток входа. Повторите позже.')
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)
    await user.type(screen.getByLabelText('Пароль'), 'WrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    expect(await screen.findByText('Слишком много неуспешных попыток входа. Повторите позже.')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /финансовый учет гск/i })).not.toBeInTheDocument()
  })

  it('shows first release notes for authenticated users', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await openSection(user, 'Что нового')

    const releasePanel = await screen.findByRole('region', { name: 'Что нового' })
    expect(await within(releasePanel).findByText('История обновлений')).toBeInTheDocument()
    expect(within(releasePanel).getByText('Добавлен консолидированный отчет')).toBeInTheDocument()
    expect(within(releasePanel).getByText(/первый отчет для месячной сверки/i)).toBeInTheDocument()
    expect(within(releasePanel).getByText(/панель "Отчеты"/i)).toBeInTheDocument()
    expect(within(releasePanel).getByText(/v0.11.0/)).toBeInTheDocument()
  })

  it('announces empty release notes for authenticated users', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient({ getReleases: async () => [] })} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await openSection(user, 'Что нового')

    const releasePanel = await screen.findByRole('region', { name: 'Что нового' })
    expect(await within(releasePanel).findByText('Пока нет опубликованных изменений.')).toHaveAttribute('role', 'status')
  })

  it('announces release notes loading status for authenticated users', async () => {
    const user = userEvent.setup()
    let resolveReleases: (releases: AppReleaseDto[]) => void = () => {}
    const releasePromise = new Promise<AppReleaseDto[]>((resolve) => {
      resolveReleases = resolve
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient({ getReleases: async () => releasePromise })} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await openSection(user, 'Что нового')

    const releasePanel = await screen.findByRole('region', { name: 'Что нового' })
    expect(await within(releasePanel).findByText('Загружаем историю обновлений...')).toHaveAttribute('role', 'status')

    resolveReleases([createAppRelease()])
    expect(await within(releasePanel).findByText('Добавлен консолидированный отчет')).toBeInTheDocument()
  })

  it('allows administrators to create draft release notes and publish them', async () => {
    const user = userEvent.setup()
    let storedReleases = [
      createAppRelease({
        releaseId: 'draft-release',
        version: '0.484.0',
        title: 'Черновик обновления',
        summary: 'Пока виден только администратору.',
        isPublished: false,
      }),
    ]
    const releaseClient = createReleaseClient({
      getReleases: async () => storedReleases.filter((release) => release.isPublished !== false),
      getManageableReleases: async () => storedReleases,
      createRelease: async (_token, request) => {
        const created = createAppRelease({
          releaseId: request.releaseId ?? 'created-release',
          version: request.version,
          publishedAt: request.publishedAt ?? '2026-07-09T14:00:00+07:00',
          title: request.title,
          summary: request.summary,
          items: request.items,
          isPublished: request.isPublished ?? false,
        })
        storedReleases = [created, ...storedReleases]
        return created
      },
      publishRelease: async (_token, releaseId) => {
        const published = storedReleases.find((release) => release.releaseId === releaseId)
        if (!published) {
          throw new Error('Запись не найдена.')
        }

        storedReleases = storedReleases.map((release) => release.releaseId === releaseId ? { ...release, isPublished: true } : release)
        return { ...published, isPublished: true }
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={releaseClient} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await openSection(user, 'Что нового')

    const releasePanel = await screen.findByRole('region', { name: 'Что нового' })
    expect(await within(releasePanel).findByText('Черновик')).toBeInTheDocument()

    await user.click(within(releasePanel).getByRole('button', { name: 'Добавить запись' }))
    const editor = within(releasePanel).getByRole('form', { name: 'Новая запись Что нового' })
    await user.type(within(editor).getByLabelText('Версия записи Что нового'), '0.485.0')
    await user.type(within(editor).getByLabelText('Заголовок записи Что нового'), 'Управление обновлениями')
    await user.type(within(editor).getByLabelText('Краткое описание записи Что нового'), 'Администратор может готовить записи.')
    await user.type(within(editor).getByLabelText('Текст пункта обновления 1'), 'Добавлена форма записи в разделе Что нового.')
    await user.click(within(editor).getByRole('button', { name: 'Сохранить' }))

    expect(await within(releasePanel).findByText('Черновик добавлен.')).toBeInTheDocument()
    expect(within(releasePanel).getByText('Управление обновлениями')).toBeInTheDocument()

    await user.click(within(releasePanel).getAllByRole('button', { name: 'Опубликовать' })[0])

    expect(await within(releasePanel).findByText(/Запись 0\.485\.0 опубликована\./)).toBeInTheDocument()
    expect(within(releasePanel).getByText(/v0\.485\.0/)).toBeInTheDocument()
  })

  it('shows workspace loading errors inside the related panel', async () => {
    const user = userEvent.setup()
    let failReportDictionaries = false
    render(
      <App
        authClient={createAuthClient()}
        dictionaryClient={createDictionaryClient({
          getOwners: async () => {
            throw new Error('Нет доступа к справочникам.')
          },
          getGarages: async () => {
            if (failReportDictionaries) {
              throw new Error('Нет доступа к отчетам.')
            }

            return [createGarage({ id: 'garage-1', number: '12' })]
          },
        })}
        financeClient={createFinanceClient({
          getOperations: async () => {
            throw new Error('Нет доступа к платежам.')
          },
        })}
        importClient={createImportClient({
          getAccessRuns: async () => {
            throw new Error('Нет доступа к импорту.')
          },
        })}
        reportClient={createReportClient()}
        releaseClient={createReleaseClient({
          getReleases: async () => {
            throw new Error('Нет доступа к истории обновлений.')
          },
        })}
        userClient={createUserClient({
          getUsers: async () => {
            throw new Error('Нет доступа к пользователям.')
          },
          getUsersPage: async () => {
            throw new Error('Нет доступа к пользователям.')
          },
        })}
      />,
    )

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await openSection(user, 'Пользователи')
    expect(await screen.findByText('Нет доступа к пользователям.')).toBeInTheDocument()
    expect(screen.getAllByRole('alert').map((alert) => alert.textContent)).toEqual(expect.arrayContaining(['Нет доступа к пользователям.']))

    await openSection(user, 'Справочники')
    expect((await screen.findAllByText('Нет доступа к справочникам.')).length).toBeGreaterThan(0)
    expect(screen.getAllByRole('alert').map((alert) => alert.textContent)).toEqual(expect.arrayContaining(['Нет доступа к справочникам.']))

    await openSection(user, 'Платежи')
    expect(await screen.findByText('Нет доступа к платежам.')).toBeInTheDocument()
    expect(screen.getAllByRole('alert').map((alert) => alert.textContent)).toEqual(expect.arrayContaining(['Нет доступа к платежам.']))

    await openSection(user, 'Импорт')
    expect(await screen.findByText('Нет доступа к импорту.')).toBeInTheDocument()
    expect(screen.getAllByRole('alert').map((alert) => alert.textContent)).toEqual(expect.arrayContaining(['Нет доступа к импорту.']))

    failReportDictionaries = true
    await openSection(user, 'Отчеты')
    expect(await screen.findByText('Нет доступа к отчетам.')).toBeInTheDocument()
    expect(screen.getAllByRole('alert').map((alert) => alert.textContent)).toEqual(expect.arrayContaining(['Нет доступа к отчетам.']))

    await openSection(user, 'Что нового')
    expect(await screen.findByText('Нет доступа к истории обновлений.')).toBeInTheDocument()
    expect(screen.getAllByRole('alert').map((alert) => alert.textContent)).toEqual(expect.arrayContaining(['Нет доступа к истории обновлений.']))
  })
})

function createAuthClient(overrides: Partial<AuthClient> = {}): AuthClient {
  return {
    bootstrapAdmin: async () => createAuthResponse(),
    login: async () => createAuthResponse(),
    changeOwnPassword: async () => createAuthResponse().user,
    ...overrides,
  }
}

function createThrowingClient<TClient extends object>(): TClient {
  return new Proxy(
    {},
    {
      get: (_target, property) => {
        throw new Error(`Protected client was called before authentication: ${String(property)}`)
      },
    },
  ) as TClient
}

function createReleaseClient(overrides: Partial<ReleaseClient> = {}): ReleaseClient {
  const releases = [createAppRelease()]
  const getReleases = overrides.getReleases ?? (async () => releases)
  const getManageableReleases = overrides.getManageableReleases ?? getReleases
  return {
    getReleases,
    getManageableReleases,
    createRelease: overrides.createRelease ?? (async (_token, request) => createAppRelease({
      releaseId: request.releaseId ?? 'created-release',
      version: request.version,
      publishedAt: request.publishedAt ?? '2026-06-24T06:15:00+07:00',
      title: request.title,
      summary: request.summary,
      items: request.items,
      isPublished: request.isPublished ?? false,
    })),
    updateRelease: overrides.updateRelease ?? (async (_token, releaseId, request) => createAppRelease({
      releaseId,
      version: request.version,
      publishedAt: request.publishedAt ?? '2026-06-24T06:15:00+07:00',
      title: request.title,
      summary: request.summary,
      items: request.items,
      isPublished: request.isPublished ?? false,
    })),
    publishRelease: overrides.publishRelease ?? (async (_token, releaseId) => createAppRelease({
      releaseId,
      isPublished: true,
    })),
  }
}

function createAuditClient(overrides: Partial<AuditClient> = {}): AuditClient {
  const getEvents = overrides.getEvents ?? (async () => [createAuditEvent({})])
  const getEventsPage = overrides.getEventsPage ?? (async (accessToken, params) => {
    const events = await getEvents(accessToken, params)
    const offset = params?.offset ?? 0
    const limit = params?.limit ?? 25
    return {
      items: events.slice(offset, offset + limit),
      totalCount: events.length,
      offset,
      limit,
    }
  })

  return {
    getEvents,
    getEventsPage,
    getEvent: async (_token, id) => createAuditEvent({ id }),
    exportEvents: async () => new Blob(['createdAtUtc,action\n'], { type: 'text/csv' }),
    exportEventsXlsx: async () => new Blob(['xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }),
    ...overrides,
  }
}

function createUserClient(overrides: Partial<UserManagementClient> = {}): UserManagementClient {
  const roles = createRoles()
  const admin = createManagedUser({
    id: 'admin-user',
    email: 'admin@example.com',
    displayName: 'Администратор ГСК',
    roles: ['administrator'],
    permissions: ['users.manage'],
  })

  return {
    getRoles: async () => roles,
    getUsers: async () => [admin],
    getUsersPage: async (_token, search, offset = 0, limit = 25) => {
      const filteredUsers = search
        ? [admin].filter((item) => `${item.email} ${item.displayName} ${item.roles.join(' ')}`.toLowerCase().includes(search.toLowerCase()))
        : [admin]
      return {
        items: filteredUsers.slice(offset, offset + limit),
        totalCount: filteredUsers.length,
        offset,
        limit,
      }
    },
    createUser: async () => admin,
    updateUser: async () => admin,
    restoreUser: async () => admin,
    updateRolePermissions: async (_token, roleCode, request) => ({
      ...(roles.find((role) => role.code === roleCode) ?? roles[0]),
      permissions: request.permissions,
    }),
    ...overrides,
  }
}

function createStatefulUserClient(): UserManagementClient {
  const roles = createRoles()
  let users: ManagedUserDto[] = []

  return {
    getRoles: async () => roles,
    getUsers: async (_token, search) => {
      return search ? users.filter((item) => `${item.email} ${item.displayName} ${item.roles.join(' ')}`.toLowerCase().includes(search.toLowerCase())) : users
    },
    getUsersPage: async (_token, search, offset = 0, limit = 25) => {
      const filteredUsers = search ? users.filter((item) => `${item.email} ${item.displayName} ${item.roles.join(' ')}`.toLowerCase().includes(search.toLowerCase())) : users
      return {
        items: filteredUsers.slice(offset, offset + limit),
        totalCount: filteredUsers.length,
        offset,
        limit,
      }
    },
    createUser: async (_token, request) => {
      const createdUser = createManagedUser({
        id: crypto.randomUUID(),
        email: request.email,
        displayName: request.displayName,
        isActive: request.isActive,
        roles: request.roleCodes,
        permissions: roles.find((role) => role.code === request.roleCodes[0])?.permissions ?? [],
      })
      users = [createdUser, ...users]
      return createdUser
    },
    updateUser: async (_token, userId, request) => {
      const existingUser = users.find((item) => item.id === userId)
      const updatedUser = createManagedUser({
        id: userId,
        email: existingUser?.email ?? 'updated@example.com',
        displayName: request.displayName,
        isActive: request.isActive,
        roles: request.roleCodes,
        permissions: roles.find((role) => role.code === request.roleCodes[0])?.permissions ?? [],
      })
      users = users.map((item) => (item.id === userId ? updatedUser : item))
      return updatedUser
    },
    restoreUser: async (_token, userId) => {
      const existingUser = users.find((item) => item.id === userId)
      const restoredUser = createManagedUser({
        id: userId,
        email: existingUser?.email ?? 'restored@example.com',
        displayName: existingUser?.displayName ?? 'Restored user',
        isActive: true,
        roles: existingUser?.roles ?? ['operator'],
        permissions: existingUser?.permissions ?? roles.find((role) => role.code === 'operator')?.permissions ?? [],
      })
      users = users.map((item) => (item.id === userId ? restoredUser : item))
      return restoredUser
    },
    updateRolePermissions: async (_token, roleCode, request) => {
      const role = roles.find((item) => item.code === roleCode) ?? roles[0]
      const updatedRole = { ...role, permissions: request.permissions }
      const roleIndex = roles.findIndex((item) => item.code === roleCode)
      if (roleIndex >= 0) {
        roles[roleIndex] = updatedRole
      }
      users = users.map((item) => item.roles.includes(roleCode)
        ? createManagedUser({ ...item, permissions: request.permissions })
        : item)
      return updatedRole
    },
  }
}

function createDictionaryClient(overrides: Partial<DictionaryClient> = {}): DictionaryClient {
  const owner = createOwner({ id: 'owner-1', lastName: 'Иванов', firstName: 'Иван', phone: '+7 900' })
  const garage = createGarage({ id: 'garage-1', number: '12', ownerId: owner.id, ownerName: owner.fullName })
  const group = createGroup({ id: 'group-1', name: 'Коммунальные услуги' })
  const supplier = createSupplier({ id: 'supplier-1', name: 'Водоканал', groupId: group.id, groupName: group.name, inn: '5401' })
  const supplierContact = createSupplierContact({ id: 'supplier-contact-1', supplierId: supplier.id, supplierName: supplier.name, fullName: 'Иванов П.В.' })
  const staffDepartment = createStaffDepartment({ id: 'staff-department-1', name: 'Бухгалтерия' })
  const staffMember = createStaffMember({ id: 'staff-member-1', fullName: 'Петрова Ольга', departmentId: staffDepartment.id, departmentName: staffDepartment.name, rate: 40000 })
  const incomeType = createAccountingType({ id: 'income-type-1', name: 'Членский взнос', code: 'membership' })
  const expenseType = createAccountingType({ id: 'expense-type-1', name: 'Электроэнергия', code: 'electricity' })
  const tariff = createTariff({ id: 'tariff-1', name: 'Тариф воды', calculationBase: 'meter_water', rate: 50, effectiveFrom: '2026-07-01' })
  let owners = [owner]
  let garages = [garage]
  let supplierGroups = [group]
  let suppliers = [supplier]
  let supplierContacts = [supplierContact]
  let staffDepartments = [staffDepartment]
  let staffMembers = [staffMember]
  let feeCampaigns: FeeCampaignDto[] = []

  return {
    getOwners: async () => owners,
    createOwner: async (_token, request) => {
      const createdOwner = createOwner({
        id: `owner-${owners.length + 1}`,
        lastName: request.lastName,
        firstName: request.firstName,
        middleName: request.middleName || null,
        phone: request.phone || null,
        address: request.address || null,
        meterNotes: request.meterNotes || null,
      })
      owners = [createdOwner, ...owners]
      return createdOwner
    },
    updateOwner: async (_token, id, request) => {
      const updatedOwner = createOwner({
        id,
        lastName: request.lastName,
        firstName: request.firstName,
        middleName: request.middleName || null,
        phone: request.phone || null,
        address: request.address || null,
        meterNotes: request.meterNotes || null,
      })
      owners = owners.map((item) => (item.id === id ? updatedOwner : item))
      return updatedOwner
    },
    archiveOwner: async () => undefined,
    restoreOwner: async () => owner,
    getGarages: async () => garages,
    createGarage: async (_token, request) => {
      const garageOwner = owners.find((item) => item.id === request.ownerId) ?? null
      const createdGarage = createGarage({
        id: `garage-${garages.length + 1}`,
        number: request.number,
        peopleCount: request.peopleCount,
        floorCount: request.floorCount,
        ownerId: garageOwner?.id ?? null,
        ownerName: garageOwner?.fullName ?? null,
        startingBalance: request.startingBalance,
        balance: request.startingBalance,
        overdueDebt: Math.max(request.startingBalance, 0),
        initialWaterMeterValue: request.initialWaterMeterValue ?? null,
        initialElectricityMeterValue: request.initialElectricityMeterValue ?? null,
        comment: request.comment ?? null,
      })
      garages = [createdGarage, ...garages]
      return createdGarage
    },
    updateGarage: async (_token, id, request) => {
      const garageOwner = owners.find((item) => item.id === request.ownerId) ?? null
      const updatedGarage = createGarage({
        id,
        number: request.number,
        peopleCount: request.peopleCount,
        floorCount: request.floorCount,
        ownerId: garageOwner?.id ?? null,
        ownerName: garageOwner?.fullName ?? null,
        startingBalance: request.startingBalance,
        balance: request.startingBalance,
        overdueDebt: Math.max(request.startingBalance, 0),
        initialWaterMeterValue: request.initialWaterMeterValue ?? null,
        initialElectricityMeterValue: request.initialElectricityMeterValue ?? null,
        comment: request.comment ?? null,
      })
      garages = garages.map((item) => (item.id === id ? updatedGarage : item))
      return updatedGarage
    },
    archiveGarage: async (_token, id) => {
      garages = garages.map((item) => (item.id === id ? { ...item, isArchived: true } : item))
    },
    restoreGarage: async (_token, id) => {
      const restoredGarage = garages.find((item) => item.id === id) ?? createGarage({ id, isArchived: false })
      garages = garages.some((item) => item.id === id)
        ? garages.map((item) => (item.id === id ? { ...restoredGarage, isArchived: false } : item))
        : [{ ...restoredGarage, isArchived: false }, ...garages]
      return { ...restoredGarage, isArchived: false }
    },
    getSupplierGroups: async () => supplierGroups,
    createSupplierGroup: async (_token, request) => {
      const createdGroup = createGroup({ id: `group-${supplierGroups.length + 1}`, name: request.name })
      supplierGroups = [createdGroup, ...supplierGroups]
      return createdGroup
    },
    updateSupplierGroup: async (_token, id, request) => {
      const updatedGroup = createGroup({ id, name: request.name })
      supplierGroups = supplierGroups.map((item) => (item.id === id ? updatedGroup : item))
      return updatedGroup
    },
    archiveSupplierGroup: async () => undefined,
    restoreSupplierGroup: async () => group,
    getSuppliers: async () => suppliers,
    createSupplier: async (_token, request) => {
      const supplierGroup = supplierGroups.find((item) => item.id === request.groupId) ?? group
      const createdSupplier = createSupplier({
        id: `supplier-${suppliers.length + 1}`,
        name: request.name,
        groupId: supplierGroup.id,
        groupName: supplierGroup.name,
        inn: request.inn ?? null,
        legalAddress: request.legalAddress ?? null,
        contactPerson: request.contactPerson ?? null,
        phone: request.phone ?? null,
        email: request.email ?? null,
        startingBalance: request.startingBalance,
        comment: request.comment ?? null,
      })
      suppliers = [createdSupplier, ...suppliers]
      return createdSupplier
    },
    updateSupplier: async (_token, id, request) => {
      const supplierGroup = supplierGroups.find((item) => item.id === request.groupId) ?? group
      const updatedSupplier = createSupplier({
        id,
        name: request.name,
        groupId: supplierGroup.id,
        groupName: supplierGroup.name,
        inn: request.inn ?? null,
        legalAddress: request.legalAddress ?? null,
        contactPerson: request.contactPerson ?? null,
        phone: request.phone ?? null,
        email: request.email ?? null,
        startingBalance: request.startingBalance,
        comment: request.comment ?? null,
      })
      suppliers = suppliers.map((item) => (item.id === id ? updatedSupplier : item))
      return updatedSupplier
    },
    archiveSupplier: async () => undefined,
    restoreSupplier: async (_token, id) => {
      const restoredSupplier = suppliers.find((item) => item.id === id) ?? createSupplier({ id, isArchived: false })
      suppliers = suppliers.some((item) => item.id === id)
        ? suppliers.map((item) => (item.id === id ? { ...restoredSupplier, isArchived: false } : item))
        : [{ ...restoredSupplier, isArchived: false }, ...suppliers]
      return { ...restoredSupplier, isArchived: false }
    },
    getSupplierContacts: async (_token, supplierId) => supplierId ? supplierContacts.filter((item) => item.supplierId === supplierId) : supplierContacts,
    createSupplierContact: async (_token, request) => {
      const contact = createSupplierContact({
        id: `supplier-contact-${supplierContacts.length + 1}`,
        supplierId: request.supplierId,
        supplierName: suppliers.find((item) => item.id === request.supplierId)?.name ?? 'Поставщик',
        fullName: request.fullName,
        position: request.position ?? null,
        phone: request.phone ?? null,
        email: request.email ?? null,
        status: request.status,
        comment: request.comment ?? null,
      })
      supplierContacts = [contact, ...supplierContacts]
      return contact
    },
    updateSupplierContact: async (_token, id, request) => {
      const updatedContact = createSupplierContact({
        id,
        supplierId: request.supplierId,
        supplierName: suppliers.find((item) => item.id === request.supplierId)?.name ?? 'Поставщик',
        fullName: request.fullName,
        position: request.position ?? null,
        phone: request.phone ?? null,
        email: request.email ?? null,
        status: request.status,
        comment: request.comment ?? null,
      })
      supplierContacts = supplierContacts.map((item) => (item.id === id ? updatedContact : item))
      return updatedContact
    },
    archiveSupplierContact: async (_token, id) => {
      supplierContacts = supplierContacts.map((item) => (item.id === id ? { ...item, isArchived: true } : item))
    },
    restoreSupplierContact: async (_token, id) => {
      const contact = supplierContacts.find((item) => item.id === id) ?? createSupplierContact({ id, isArchived: false })
      supplierContacts = supplierContacts.some((item) => item.id === id)
        ? supplierContacts.map((item) => (item.id === id ? { ...contact, isArchived: false } : item))
        : [{ ...contact, isArchived: false }, ...supplierContacts]
      return { ...contact, isArchived: false }
    },
    getStaffDepartments: async () => staffDepartments,
    createStaffDepartment: async (_token, request) => {
      const department = createStaffDepartment({ id: `staff-department-${staffDepartments.length + 1}`, name: request.name })
      staffDepartments = [department, ...staffDepartments]
      return department
    },
    updateStaffDepartment: async (_token, id, request) => {
      const department = createStaffDepartment({ id, name: request.name })
      staffDepartments = staffDepartments.map((item) => (item.id === id ? department : item))
      return department
    },
    archiveStaffDepartment: async () => undefined,
    restoreStaffDepartment: async (_token, id) => createStaffDepartment({ id, isArchived: false }),
    getStaffMembers: async () => staffMembers,
    createStaffMember: async (_token, request) => {
      const department = staffDepartments.find((item) => item.id === request.departmentId) ?? staffDepartment
      const member = createStaffMember({ id: `staff-member-${staffMembers.length + 1}`, fullName: request.fullName, departmentId: department.id, departmentName: department.name, rate: request.rate })
      staffMembers = [member, ...staffMembers]
      return member
    },
    updateStaffMember: async (_token, id, request) => {
      const department = staffDepartments.find((item) => item.id === request.departmentId) ?? staffDepartment
      const member = createStaffMember({ id, fullName: request.fullName, departmentId: department.id, departmentName: department.name, rate: request.rate })
      staffMembers = staffMembers.map((item) => (item.id === id ? member : item))
      return member
    },
    archiveStaffMember: async (_token, id) => {
      staffMembers = staffMembers.map((item) => (item.id === id ? { ...item, isArchived: true } : item))
    },
    restoreStaffMember: async (_token, id) => {
      const member = staffMembers.find((item) => item.id === id) ?? createStaffMember({ id, isArchived: false })
      staffMembers = staffMembers.some((item) => item.id === id)
        ? staffMembers.map((item) => (item.id === id ? { ...member, isArchived: false } : item))
        : [{ ...member, isArchived: false }, ...staffMembers]
      return { ...member, isArchived: false }
    },
    getIncomeTypes: async () => [incomeType],
    createIncomeType: async () => incomeType,
    updateIncomeType: async (_token, id, request) => createAccountingType({ id, name: request.name, code: request.code ?? null }),
    archiveIncomeType: async () => undefined,
    restoreIncomeType: async () => incomeType,
    getExpenseTypes: async () => [expenseType],
    createExpenseType: async () => expenseType,
    updateExpenseType: async (_token, id, request) => createAccountingType({ id, name: request.name, code: request.code ?? null }),
    archiveExpenseType: async () => undefined,
    restoreExpenseType: async () => expenseType,
    getTariffs: async () => [tariff],
    createTariff: async () => tariff,
    updateTariff: async (_token, id, request) => createTariff({
      id,
      name: request.name,
      calculationBase: request.calculationBase,
      rate: request.rate,
      effectiveFrom: request.effectiveFrom,
      comment: request.comment ?? null,
      electricityFirstThreshold: request.electricityFirstThreshold ?? null,
      electricitySecondThreshold: request.electricitySecondThreshold ?? null,
      electricityFirstTierName: request.electricityFirstTierName ?? null,
      electricitySecondTierName: request.electricitySecondTierName ?? null,
      electricityThirdTierName: request.electricityThirdTierName ?? null,
      electricityFirstRate: request.electricityFirstRate ?? null,
      electricitySecondRate: request.electricitySecondRate ?? null,
      electricityThirdRate: request.electricityThirdRate ?? null,
    }),
    archiveTariff: async () => undefined,
    restoreTariff: async () => tariff,
    getChargeServiceSettings: async () => [],
    createChargeServiceSetting: async (_token, request) => createChargeServiceSetting({
      id: 'charge-service-new',
      name: request.name,
      isRegular: request.isRegular,
      periodicityMonths: request.periodicityMonths ?? null,
      accrualStartMonth: request.accrualStartMonth ?? null,
      paymentDueDay: request.paymentDueDay ?? null,
      paymentDueMonth: request.paymentDueMonth ?? null,
      overdueGraceDays: request.overdueGraceDays,
      incomeTypeId: request.incomeTypeId ?? null,
      tariffId: request.tariffId ?? null,
      isMetered: request.isMetered,
      hasTieredTariff: request.hasTieredTariff,
      unitName: request.unitName ?? null,
    }),
    updateChargeServiceSetting: async (_token, id, request) => createChargeServiceSetting({
      id,
      name: request.name,
      isRegular: request.isRegular,
      periodicityMonths: request.periodicityMonths ?? null,
      accrualStartMonth: request.accrualStartMonth ?? null,
      paymentDueDay: request.paymentDueDay ?? null,
      paymentDueMonth: request.paymentDueMonth ?? null,
      overdueGraceDays: request.overdueGraceDays,
      incomeTypeId: request.incomeTypeId ?? null,
      tariffId: request.tariffId ?? null,
      isMetered: request.isMetered,
      hasTieredTariff: request.hasTieredTariff,
      unitName: request.unitName ?? null,
    }),
    archiveChargeServiceSetting: async () => undefined,
    restoreChargeServiceSetting: async (_token, id) => createChargeServiceSetting({ id, isArchived: false }),
    getFeeCampaigns: async () => feeCampaigns,
    createFeeCampaign: async (_token, request) => {
      const income = [incomeType].find((item) => item.id === request.incomeTypeId) ?? incomeType
      const campaign = createFeeCampaign({
        id: `fee-campaign-${feeCampaigns.length + 1}`,
        name: request.name,
        incomeTypeId: income.id,
        incomeTypeName: income.name,
        goal: request.goal ?? null,
        contributionAmount: request.contributionAmount,
        targetAmount: request.targetAmount,
        startsOn: request.startsOn,
        endsOn: request.endsOn ?? null,
        appliesToAllGarages: request.appliesToAllGarages,
        participantGarageIds: request.participantGarageIds ?? [],
        overdueGraceDays: request.overdueGraceDays,
      })
      feeCampaigns = [campaign, ...feeCampaigns]
      return campaign
    },
    updateFeeCampaign: async (_token, id, request) => {
      const income = [incomeType].find((item) => item.id === request.incomeTypeId) ?? incomeType
      const campaign = createFeeCampaign({
        id,
        name: request.name,
        incomeTypeId: income.id,
        incomeTypeName: income.name,
        goal: request.goal ?? null,
        contributionAmount: request.contributionAmount,
        targetAmount: request.targetAmount,
        startsOn: request.startsOn,
        endsOn: request.endsOn ?? null,
        appliesToAllGarages: request.appliesToAllGarages,
        participantGarageIds: request.participantGarageIds ?? [],
        overdueGraceDays: request.overdueGraceDays,
      })
      feeCampaigns = feeCampaigns.map((item) => (item.id === id ? campaign : item))
      return campaign
    },
    archiveFeeCampaign: async (_token, id) => {
      feeCampaigns = feeCampaigns.map((item) => (item.id === id ? { ...item, isArchived: true } : item))
    },
    restoreFeeCampaign: async (_token, id) => {
      const campaign = feeCampaigns.find((item) => item.id === id) ?? createFeeCampaign({ id, isArchived: false })
      feeCampaigns = feeCampaigns.some((item) => item.id === id)
        ? feeCampaigns.map((item) => (item.id === id ? { ...campaign, isArchived: false } : item))
        : [{ ...campaign, isArchived: false }, ...feeCampaigns]
      return { ...campaign, isArchived: false }
    },
    getIrregularPayments: async () => [],
    createIrregularPayment: async (_token, request) => createIrregularPayment({ id: 'irregular-payment-new', name: request.name, amount: request.amount, isActive: request.isActive ?? true }),
    updateIrregularPayment: async (_token, id, request) => createIrregularPayment({ id, name: request.name, amount: request.amount, isActive: request.isActive ?? true }),
    setIrregularPaymentStatus: async (_token, id, request) => createIrregularPayment({ id, isActive: request.isActive }),
    archiveIrregularPayment: async () => undefined,
    restoreIrregularPayment: async (_token, id) => createIrregularPayment({ id, isArchived: false }),
    ...overrides,
  }
}

function createFund(overrides: Partial<FundDto> = {}): FundDto {
  return {
    id: 'fund-electricity',
    name: 'Электроэнергия',
    balance: 0,
    availableToDistribute: 100000,
    sortOrder: 10,
    allowOperations: true,
    isSystem: true,
    ...overrides,
  }
}

function createFundOperation(overrides: Partial<FundOperationDto> = {}): FundOperationDto {
  return {
    id: 'fund-operation-1',
    fundId: 'fund-electricity',
    fundName: 'Электроэнергия',
    operationKind: 'deposit',
    amount: 1500,
    balanceBefore: 0,
    balanceAfter: 1500,
    reason: 'Распределение средств',
    createdAtUtc: '2026-06-30T10:00:00Z',
    isCanceled: false,
    ...overrides,
  }
}

function createFundsClient(overrides: Partial<FundsClient> = {}): FundsClient {
  let funds = [
    createFund({ id: 'fund-electricity', name: 'Электроэнергия', sortOrder: 10 }),
    createFund({ id: 'fund-water', name: 'Водоснабжение', sortOrder: 20 }),
    createFund({ id: 'fund-trash', name: 'Вывоз мусора', sortOrder: 30 }),
    createFund({ id: 'fund-lighting', name: 'Наружное освещение', sortOrder: 40 }),
    createFund({ id: 'fund-membership', name: 'Членские взносы', sortOrder: 50, allowOperations: false }),
    createFund({ id: 'fund-target', name: 'Целевые взносы', sortOrder: 60 }),
    createFund({ id: 'fund-other', name: 'Прочее', sortOrder: 70, allowOperations: false }),
  ]
  let operationSequence = 1
  let operations: FundOperationDto[] = []

  return {
    getFunds: async () => funds,
    getOperations: async (_token, query) => {
      const includeCanceled = query?.includeCanceled ?? false
      const limit = query?.limit ?? 25
      return operations
        .filter((operation) => includeCanceled || !operation.isCanceled)
        .slice(0, limit)
    },
    createOperation: async (_token: string, fundId: string, request: CreateFundOperationRequest) => {
      const fund = funds.find((item) => item.id === fundId)
      if (!fund) {
        throw new Error('Фонд не найден.')
      }

      const balanceBefore = fund.balance
      const balanceAfter = request.operationKind === 'deposit' ? balanceBefore + request.amount : balanceBefore - request.amount
      const availableToDistribute = fund.availableToDistribute
      if (request.operationKind === 'deposit' && request.amount > availableToDistribute) {
        throw new Error(`Сумма пополнения не может превышать доступную к распределению сумму ${formatMoney(availableToDistribute)} руб.`)
      }
      if (balanceAfter < 0) {
        throw new Error('Недостаточно средств фонда.')
      }

      const nextAvailableToDistribute = request.operationKind === 'deposit'
        ? Math.max(0, availableToDistribute - request.amount)
        : availableToDistribute + request.amount
      funds = funds.map((item) => ({
        ...item,
        availableToDistribute: nextAvailableToDistribute,
        ...(item.id === fundId ? { balance: balanceAfter } : {}),
      }))
      const operation = createFundOperation({
        id: `fund-operation-${operationSequence++}`,
        fundId,
        fundName: fund.name,
        operationKind: request.operationKind,
        amount: request.amount,
        balanceBefore,
        balanceAfter,
        reason: request.reason,
      })
      operations = [operation, ...operations]
      return operation
    },
    updateOperation: async (_token, operationId, request) => {
      const operation = operations.find((item) => item.id === operationId)
      if (!operation) {
        throw new Error('Операция фонда не найдена.')
      }

      const fund = funds.find((item) => item.id === operation.fundId)
      const balanceBefore = operation.balanceBefore
      const balanceAfter = operation.operationKind === 'deposit' ? balanceBefore + request.amount : balanceBefore - request.amount
      if (fund) {
        funds = funds.map((item) => item.id === fund.id ? { ...item, balance: balanceAfter } : item)
      }
      const updated = { ...operation, amount: request.amount, balanceAfter, reason: request.reason }
      operations = operations.map((item) => item.id === operationId ? updated : item)
      return updated
    },
    cancelOperation: async (_token, operationId, request) => {
      const operation = operations.find((item) => item.id === operationId)
      if (!operation) {
        throw new Error('Операция фонда не найдена.')
      }

      const canceled = { ...operation, isCanceled: true, reason: `${operation.reason}\nОтменено: ${request.reason}` }
      operations = operations.map((item) => item.id === operationId ? canceled : item)
      return canceled
    },
    restoreOperation: async (_token, operationId) => {
      const operation = operations.find((item) => item.id === operationId)
      if (!operation) {
        throw new Error('Операция фонда не найдена.')
      }

      const restored = { ...operation, isCanceled: false }
      operations = operations.map((item) => item.id === operationId ? restored : item)
      return restored
    },
    ...overrides,
  }
}

function createFinanceClient(overrides: Partial<FinanceClient> = {}): FinanceClient {
  const operation = createFinancialOperation({
    id: 'operation-1',
    amount: 1500,
    garageNumber: '12',
    incomeTypeName: 'Членский взнос',
  })
  const accrual = createAccrual({ id: 'accrual-1', amount: 2000, garageNumber: '12', incomeTypeName: 'Членский взнос' })
  const supplierAccrual = createSupplierAccrual({ id: 'supplier-accrual-1', amount: 650 })
  const meterReading = createMeterReading({ id: 'meter-reading-1', consumption: 5.5, currentValue: 15.5, previousValue: 10 })
  const missingMeterReading = createMissingMeterReading({})
  const garageBalanceHistory = createGarageBalanceHistory({})
  const garageIncomeWorksheet = createGarageIncomeWorksheet({
    accrualTotal: 0,
    incomeTotal: 0,
    debtTotal: 0,
    rows: [],
  })

  const client: FinanceClient = {
    getOperations: async () => [operation],
    getOperationsPage: async () => ({ items: [operation], totalCount: 1, offset: 0, limit: 25 }),
    getAccruals: async () => [accrual],
    getAccrualsPage: async () => ({ items: [accrual], totalCount: 1, offset: 0, limit: 25 }),
    getSupplierAccruals: async () => [supplierAccrual],
    getSupplierAccrualsPage: async () => ({ items: [supplierAccrual], totalCount: 1, offset: 0, limit: 25 }),
    getMeterReadings: async () => [meterReading],
    getMeterReadingsPage: async () => ({ items: [meterReading], totalCount: 1, offset: 0, limit: 25 }),
    getMissingMeterReadings: async () => [missingMeterReading],
    getGarageBalanceHistory: async () => garageBalanceHistory,
    getGarageIncomeWorksheet: async () => garageIncomeWorksheet,
    getExpenseWorksheet: async () => {
      throw new Error('Серверная форма выплат недоступна')
    },
    getSummary: async () => ({ incomeTotal: 1500, expenseTotal: 0, accrualTotal: 2000, balance: 1500, debt: 500, operationCount: 1, accrualCount: 1, meterReadingCount: 1 }),
    createIncome: async () => operation,
    createGarageDebtPayment: async () => createFinancialOperation({ id: 'operation-debt-payment', amount: 500, incomeTypeName: 'Перенос задолженности' }),
    updateIncome: async (_token, operationId) => ({ ...operation, id: operationId }),
    createExpense: async () => createFinancialOperation({ id: 'operation-2', operationKind: 'expense', amount: 500, supplierName: 'Водоканал', expenseTypeName: 'Вода' }),
    createStaffPayment: async () => createFinancialOperation({ id: 'operation-staff', operationKind: 'expense', amount: 500, staffMemberId: 'staff-1', staffMemberName: 'Петрова Ольга', staffDepartmentName: 'Бухгалтерия', expenseTypeName: 'Зарплата' }),
    updateExpense: async (_token, operationId) => createFinancialOperation({ id: operationId, operationKind: 'expense', amount: 500, supplierName: 'Водоканал', expenseTypeName: 'Вода' }),
    cancelOperation: async (_token, operationId, request) => {
      const target = operation.id === operationId ? operation : createFinancialOperation({ id: operationId })
      return { ...target, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    restoreOperation: async (_token, operationId) => {
      const target = operation.id === operationId ? operation : createFinancialOperation({ id: operationId })
      return { ...target, isCanceled: false }
    },
    createAccrual: async () => accrual,
    createDebtTransfer: async (_token, request) => createAccrual({
      id: 'debt-transfer-accrual',
      garageId: request.garageId,
      incomeTypeId: 'income-type-debt-transfer',
      incomeTypeName: 'Перенос задолженности',
      accountingMonth: request.targetMonth,
      amount: request.amount,
      source: 'debt_transfer',
      comment: request.comment ?? null,
    }),
    updateAccrual: async (_token, accrualId) => ({ ...accrual, id: accrualId }),
    cancelAccrual: async (_token, accrualId, request) => {
      const target = accrual.id === accrualId ? accrual : createAccrual({ id: accrualId })
      return { ...target, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    restoreAccrual: async (_token, accrualId) => {
      const target = accrual.id === accrualId ? accrual : createAccrual({ id: accrualId })
      return { ...target, isCanceled: false }
    },
    createSupplierAccrual: async () => supplierAccrual,
    updateSupplierAccrual: async (_token, supplierAccrualId) => ({ ...supplierAccrual, id: supplierAccrualId }),
    cancelSupplierAccrual: async (_token, supplierAccrualId, request) => {
      const target = supplierAccrual.id === supplierAccrualId ? supplierAccrual : createSupplierAccrual({ id: supplierAccrualId })
      return { ...target, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    restoreSupplierAccrual: async (_token, supplierAccrualId) => {
      const target = supplierAccrual.id === supplierAccrualId ? supplierAccrual : createSupplierAccrual({ id: supplierAccrualId })
      return { ...target, isCanceled: false }
    },
    generateRegularAccruals: async () => createRegularAccrualGenerationResult({ createdAccruals: [accrual], totalAmount: accrual.amount }),
    generateRegularCatalogAccruals: async () => createRegularCatalogAccrualGenerationResult({
      createdCount: 1,
      totalAmount: accrual.amount,
      serviceResults: [createRegularAccrualGenerationResult({ createdAccruals: [accrual], totalAmount: accrual.amount })],
    }),
    generateFeeCampaignAccruals: async () => createFeeCampaignAccrualGenerationResult({ createdAccruals: [accrual], totalAmount: accrual.amount }),
    generateSupplierGroupSalaryAccruals: async () => createSupplierGroupSalaryAccrualGenerationResult({ createdAccruals: [supplierAccrual], totalAmount: supplierAccrual.amount }),
    createMeterReading: async () => meterReading,
    updateMeterReading: async (_token, meterReadingId) => ({ ...meterReading, id: meterReadingId }),
    cancelMeterReading: async (_token, meterReadingId, request) => {
      const target = meterReading.id === meterReadingId ? meterReading : createMeterReading({ id: meterReadingId })
      return { ...target, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    restoreMeterReading: async (_token, meterReadingId) => {
      const target = meterReading.id === meterReadingId ? meterReading : createMeterReading({ id: meterReadingId })
      return { ...target, isCanceled: false }
    },
    ...overrides,
  }

  if (!overrides.getOperationsPage) {
    client.getOperationsPage = async (token, params) => {
      const allItems = await client.getOperations(token, params?.limit)
      const filteredItems = params?.operationKind ? allItems.filter((item) => item.operationKind === params.operationKind) : allItems
      const offset = params?.offset ?? 0
      const limit = params?.limit ?? 25
      return { items: filteredItems.slice(offset, offset + limit), totalCount: filteredItems.length, offset, limit }
    }
  }

  if (!overrides.getAccrualsPage) {
    client.getAccrualsPage = async (token, params) => {
      const allItems = await client.getAccruals(token, params?.garageId, params?.limit)
      const offset = params?.offset ?? 0
      const limit = params?.limit ?? 25
      return { items: allItems.slice(offset, offset + limit), totalCount: allItems.length, offset, limit }
    }
  }

  if (!overrides.getSupplierAccrualsPage) {
    client.getSupplierAccrualsPage = async (token, params) => {
      const allItems = await client.getSupplierAccruals(token, params?.supplierId, params?.limit)
      const offset = params?.offset ?? 0
      const limit = params?.limit ?? 25
      return { items: allItems.slice(offset, offset + limit), totalCount: allItems.length, offset, limit }
    }
  }

  if (!overrides.getMeterReadingsPage) {
    client.getMeterReadingsPage = async (token, params) => {
      const allItems = await client.getMeterReadings(token, params?.garageId, params?.meterKind, params?.limit)
      const offset = params?.offset ?? 0
      const limit = params?.limit ?? 25
      return { items: allItems.slice(offset, offset + limit), totalCount: allItems.length, offset, limit }
    }
  }

  return client
}

function createImportClient(overrides: Partial<ImportClient> = {}): ImportClient {
  const run = createAccessImportRun()

  return {
    getAccessReaderStatus: async () => createAccessImportReaderStatus(),
    getAccessRuns: async () => [],
    getAccessRunLog: async () => [],
    getAccessCreatedRecords: async () => [],
    getOpenQuarantineItems: async () => [],
    dryRunAccess: async () => run,
    downloadAccessRunReport: async () => new Blob(['{}'], { type: 'application/json' }),
    requestAccessImportApply: async (_token, runId) => createAccessImportRun({ id: runId, status: 'import_requested' }),
    cancelAccessImportApplyRequest: async (_token, runId) => createAccessImportRun({ id: runId, status: 'import_request_cancelled' }),
    requestAccessImportRollback: async (_token, runId) => createAccessImportRun({ id: runId, status: 'rollback_requested' }),
    resolveQuarantineItem: async (_token, itemId) => createAccessImportQuarantineItem({ id: itemId, status: 'resolved' }),
    ...overrides,
  }
}

function createIntegrationClient(overrides: Partial<IntegrationClient> = {}): IntegrationClient {
  return {
    getOneCFreshStatus: async () => createOneCFreshStatus(),
    startOneCFreshSync: async () => createOneCFreshSync(),
    retryOneCFreshSync: async () => createOneCFreshSync(),
    getReceiptPrintingStatus: async () => createReceiptPrintingStatus(),
    registerReceiptPrintingAction: async (_token, operationId, request) => createReceiptPrintingAction({
      financialOperationId: operationId,
      action: request.action,
      statusMessage: 'Действие квитанции зарегистрировано.',
    }),
    ...overrides,
  }
}

function createReportClient(overrides: Partial<ReportClient> = {}): ReportClient {
  return {
    getConsolidatedReport: async (_token, params) => {
      const report = createConsolidatedReport()
      const search = params?.search?.toLowerCase() ?? ''
      if (search.includes('петров') || search.includes('99')) {
        return createConsolidatedReport({
          garageRows: [
            {
              garageId: 'garage-21',
              garageNumber: search.includes('99') ? '99' : '21',
              ownerName: 'Петров Петр',
              incomeTotal: 0,
              accrualTotal: 1000,
              debt: 1000,
              meterReadingCount: 0,
            },
          ],
        })
      }
      return report
    },
    exportConsolidatedReportXlsx: async () => new Blob(['consolidated xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }),
    exportConsolidatedReportPdf: async () => new Blob(['consolidated pdf'], { type: 'application/pdf' }),
    getIncomeReport: async (_token, params) => {
      const report = createIncomeReport()
      const search = params?.search?.toLowerCase() ?? ''
      if (search.includes('петров') || search.includes('99')) {
        return createIncomeReport({
          rows: report.rows.map((row) => ({
            ...row,
            garageId: 'garage-21',
            garageNumber: search.includes('99') ? '99' : '21',
            ownerId: 'owner-21',
            ownerName: 'Петров Петр',
          })),
        })
      }
      if (params?.rowMode === 'payments') {
        return createIncomeReport({
          accrualTotal: 0,
          incomeTotal: 1500,
          debt: -1500,
          rowCount: 1,
          rows: report.rows.filter((row) => row.rowType === 'payments'),
        })
      }
      return report
    },
    exportIncomeReportXlsx: async () => new Blob(['income xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }),
    exportIncomeReportPdf: async () => new Blob(['income pdf'], { type: 'application/pdf' }),
    getExpenseReport: async (_token, params) => {
      const report = createExpenseReport()
      if (params?.rowMode === 'payments') {
        return createExpenseReport({
          accrualTotal: 0,
          expenseTotal: 400,
          difference: -400,
          rowCount: 1,
          rows: report.rows.filter((row) => row.rowType === 'payments'),
        })
      }
      return report
    },
    exportExpenseReportXlsx: async () => new Blob(['expense xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }),
    exportExpenseReportPdf: async () => new Blob(['expense pdf'], { type: 'application/pdf' }),
    getFundChangeReport: async () => createFundChangeReport(),
    exportFundChangeReportXlsx: async () => new Blob(['fund changes xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }),
    exportFundChangeReportPdf: async () => new Blob(['fund changes pdf'], { type: 'application/pdf' }),
    getCashPaymentReport: async () => createCashPaymentReport(),
    exportCashPaymentReportXlsx: async () => new Blob(['cash payments xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }),
    exportCashPaymentReportPdf: async () => new Blob(['cash payments pdf'], { type: 'application/pdf' }),
    getBankDepositReport: async () => createBankDepositReport(),
    exportBankDepositReportXlsx: async () => new Blob(['bank deposits xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }),
    exportBankDepositReportPdf: async () => new Blob(['bank deposits pdf'], { type: 'application/pdf' }),
    exportFeeReportXlsx: async () => new Blob(['fees xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }),
    exportFeeReportPdf: async () => new Blob(['fees pdf'], { type: 'application/pdf' }),
    getFeeReport: async (_token, params) => {
      const variation = params?.variation ?? 'Сбор на ворота'
      return variation.toLowerCase().includes('член')
        ? createFeeReport({
            variation,
            accruedTotal: 1000,
            collectedTotal: 200,
            debtTotal: 800,
            summaryRows: [{ incomeTypeId: 'income-type-membership', name: 'Членский взнос', goal: 'Членский взнос', feeAmount: 1000, collected: 200 }],
            garageRows: [{ garageId: 'garage-12', garageNumber: '12', ownerName: 'Иванов Иван', incomeTypeId: 'income-type-membership', feeName: 'Членский взнос', accrued: 1000, paid: 200, lastPaymentDate: '2026-06-10', debt: 800 }],
            debtorRows: [{ garageId: 'garage-12', garageNumber: '12', ownerName: 'Иванов Иван', incomeTypeId: 'income-type-membership', feeName: 'Членский взнос', paid: 200, lastPaymentDate: '2026-06-10', debt: 800 }],
          })
        : createFeeReport({ variation })
    },
    ...overrides,
  }
}

function createStatefulImportClient(): ImportClient {
  let runs: AccessImportRunDto[] = []
  let logsByRunId = new Map<string, AccessImportRunLogEntryDto[]>()

  return {
    getAccessReaderStatus: async () => createAccessImportReaderStatus(),
    getAccessRuns: async () => runs,
    getAccessRunLog: async (_token, runId) => logsByRunId.get(runId) ?? [],
    getAccessCreatedRecords: async () => [],
    getOpenQuarantineItems: async () => [],
    dryRunAccess: async (_token, file) => {
      const run = createAccessImportRun({
        id: crypto.randomUUID(),
        originalFileName: file.name,
        fileSizeBytes: file.size,
      })
      runs = [run, ...runs]
      logsByRunId = new Map(logsByRunId).set(run.id, [
        createAccessImportRunLogEntry({ accessImportRunId: run.id, stepCode: 'file_received', message: `Файл ${file.name} получен для dry-run проверки.` }),
        createAccessImportRunLogEntry({ accessImportRunId: run.id, stepCode: 'dry_run_finished', level: 'warning', message: run.summary }),
      ])
      return run
    },
    downloadAccessRunReport: async (_token, runId) => {
      const run = runs.find((item) => item.id === runId)
      return new Blob([JSON.stringify(run ?? {})], { type: 'application/json' })
    },
    requestAccessImportApply: async (_token, runId, reason) => {
      const run = runs.find((item) => item.id === runId) ?? createAccessImportRun({ id: runId })
      const updatedRun = {
        ...run,
        status: 'import_requested' as const,
        summary: 'Фактический импорт запрошен: перенос будет выполнен после подключения reader Access.',
      }
      runs = runs.map((item) => item.id === runId ? updatedRun : item)
      logsByRunId = new Map(logsByRunId).set(runId, [
        ...(logsByRunId.get(runId) ?? []),
        createAccessImportRunLogEntry({ accessImportRunId: runId, stepCode: 'import_requested', level: 'warning', message: `Фактический импорт запрошен: ${reason}` }),
      ])
      return updatedRun
    },
    cancelAccessImportApplyRequest: async (_token, runId, reason) => {
      const run = runs.find((item) => item.id === runId) ?? createAccessImportRun({ id: runId })
      const updatedRun = {
        ...run,
        status: 'import_request_cancelled' as const,
        summary: 'Заявка на фактический импорт отменена. Dry-run остается доступным.',
      }
      runs = runs.map((item) => item.id === runId ? updatedRun : item)
      logsByRunId = new Map(logsByRunId).set(runId, [
        ...(logsByRunId.get(runId) ?? []),
        createAccessImportRunLogEntry({ accessImportRunId: runId, stepCode: 'import_request_cancelled', level: 'warning', message: `Заявка на импорт отменена: ${reason}` }),
      ])
      return updatedRun
    },
    requestAccessImportRollback: async (_token, runId, reason) => {
      const run = runs.find((item) => item.id === runId) ?? createAccessImportRun({ id: runId })
      const updatedRun = {
        ...run,
        status: 'rollback_requested' as const,
        summary: 'Rollback запрошен: фактический откат данных не выполнялся для dry-run запуска.',
      }
      runs = runs.map((item) => item.id === runId ? updatedRun : item)
      logsByRunId = new Map(logsByRunId).set(runId, [
        ...(logsByRunId.get(runId) ?? []),
        createAccessImportRunLogEntry({ accessImportRunId: runId, stepCode: 'rollback_requested', level: 'warning', message: `Rollback запрошен: ${reason}` }),
      ])
      return updatedRun
    },
    resolveQuarantineItem: async (_token, itemId) => createAccessImportQuarantineItem({ id: itemId, status: 'resolved' }),
  }
}

function createStatefulFinanceClient(): FinanceClient {
  let operations: FinancialOperationDto[] = []
  let accruals: AccrualDto[] = []
  let supplierAccruals: SupplierAccrualDto[] = []
  let meterReadings: MeterReadingDto[] = []

  function summary(): FinanceSummaryDto {
    const incomeTotal = operations.filter((item) => item.operationKind === 'income').reduce((sum, item) => sum + item.amount, 0)
    const expenseTotal = operations.filter((item) => item.operationKind === 'expense').reduce((sum, item) => sum + item.amount, 0)
    const accrualTotal = accruals.reduce((sum, item) => sum + item.amount, 0)
    return { incomeTotal, expenseTotal, accrualTotal, balance: incomeTotal - expenseTotal, debt: accrualTotal - incomeTotal, operationCount: operations.length, accrualCount: accruals.length, meterReadingCount: meterReadings.length }
  }

  return createFinanceClient({
    getOperations: async () => operations,
    getAccruals: async () => accruals,
    getSupplierAccruals: async () => supplierAccruals,
    getMeterReadings: async () => meterReadings,
    getSummary: async () => summary(),
    createIncome: async (_token, request) => {
      const debtBefore = summary().debt
      const operation = createFinancialOperation({
        id: crypto.randomUUID(),
        operationDate: request.operationDate,
        accountingMonth: request.accountingMonth,
        amount: request.amount,
        documentNumber: request.documentNumber ?? null,
        comment: request.comment ?? null,
        garageDebtBefore: debtBefore,
        garageDebtAfter: debtBefore - request.amount,
        paymentAllocations: [{
          allocationKind: debtBefore > 0 ? 'month' : 'overpayment',
          accountingMonth: debtBefore > 0 ? request.accountingMonth : null,
          label: debtBefore > 0 ? request.accountingMonth.slice(0, 7) : 'Переплата',
          debtBefore: Math.max(debtBefore, 0),
          paidAmount: request.amount,
          debtAfter: debtBefore - request.amount,
        }],
      })
      operations = [operation, ...operations]
      return operation
    },
    createExpense: async (_token, request) => {
      const supplierDebtBefore = supplierAccruals.reduce((sum, item) => sum + item.amount, 0) - operations.filter((item) => item.operationKind === 'expense').reduce((sum, item) => sum + item.amount, 0)
      const operation = createFinancialOperation({
        id: crypto.randomUUID(),
        operationKind: 'expense',
        supplierId: request.supplierId,
        expenseTypeId: request.expenseTypeId,
        operationDate: request.operationDate,
        accountingMonth: request.accountingMonth,
        amount: request.amount,
        documentNumber: request.documentNumber ?? null,
        comment: request.comment ?? null,
        supplierName: 'Водоканал',
        expenseTypeName: 'Вода',
        supplierDebtBefore,
        supplierDebtAfter: supplierDebtBefore - request.amount,
        paymentAllocations: [{
          allocationKind: supplierDebtBefore > 0 ? 'month' : 'overpayment',
          accountingMonth: supplierDebtBefore > 0 ? request.accountingMonth : null,
          label: supplierDebtBefore > 0 ? request.accountingMonth.slice(0, 7) : 'Переплата',
          debtBefore: Math.max(supplierDebtBefore, 0),
          paidAmount: request.amount,
          debtAfter: supplierDebtBefore - request.amount,
        }],
      })
      operations = [operation, ...operations]
      return operation
    },
    cancelOperation: async (_token, operationId, request) => {
      const operation = operations.find((item) => item.id === operationId)
      if (!operation) {
        throw new Error('Финансовая операция не найдена.')
      }

      operations = operations.filter((item) => item.id !== operationId)
      return { ...operation, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    restoreOperation: async (_token, operationId) => {
      const operation = createFinancialOperation({ id: operationId, isCanceled: false })
      operations = [operation, ...operations.filter((item) => item.id !== operationId)]
      return operation
    },
    updateIncome: async (_token, operationId, request) => {
      const operation = operations.find((item) => item.id === operationId && item.operationKind === 'income')
      if (!operation) {
        throw new Error('Поступление не найдено.')
      }

      const updated = {
        ...operation,
        garageId: request.garageId,
        incomeTypeId: request.incomeTypeId,
        operationDate: request.operationDate,
        accountingMonth: request.accountingMonth,
        amount: request.amount,
        documentNumber: request.documentNumber ?? null,
        comment: request.comment ?? null,
        garageDebtAfter: operation.garageDebtBefore !== null ? operation.garageDebtBefore - request.amount : operation.garageDebtAfter,
      }
      operations = operations.map((item) => (item.id === operationId ? updated : item))
      return updated
    },
    updateExpense: async (_token, operationId, request) => {
      const operation = operations.find((item) => item.id === operationId && item.operationKind === 'expense')
      if (!operation) {
        throw new Error('Выплата не найдена.')
      }

      const updated = {
        ...operation,
        supplierId: request.supplierId,
        expenseTypeId: request.expenseTypeId,
        operationDate: request.operationDate,
        accountingMonth: request.accountingMonth,
        amount: request.amount,
        documentNumber: request.documentNumber ?? null,
        comment: request.comment ?? null,
        supplierDebtAfter: operation.supplierDebtBefore !== null ? operation.supplierDebtBefore - request.amount : operation.supplierDebtAfter,
      }
      operations = operations.map((item) => (item.id === operationId ? updated : item))
      return updated
    },
    createAccrual: async (_token, request) => {
      const accrual = createAccrual({
        id: crypto.randomUUID(),
        garageId: request.garageId,
        incomeTypeId: request.incomeTypeId,
        accountingMonth: request.accountingMonth,
        amount: request.amount,
        source: request.source,
        comment: request.comment ?? null,
      })
      accruals = [accrual, ...accruals]
      return accrual
    },
    updateAccrual: async (_token, accrualId, request) => {
      const accrual = accruals.find((item) => item.id === accrualId)
      if (!accrual) {
        throw new Error('Начисление не найдено.')
      }

      const updated = {
        ...accrual,
        garageId: request.garageId,
        incomeTypeId: request.incomeTypeId,
        accountingMonth: request.accountingMonth,
        amount: request.amount,
        source: request.source,
        comment: request.comment ?? null,
      }
      accruals = accruals.map((item) => (item.id === accrualId ? updated : item))
      return updated
    },
    cancelAccrual: async (_token, accrualId, request) => {
      const accrual = accruals.find((item) => item.id === accrualId)
      if (!accrual) {
        throw new Error('Начисление не найдено.')
      }

      accruals = accruals.filter((item) => item.id !== accrualId)
      return { ...accrual, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    restoreAccrual: async (_token, accrualId) => {
      const accrual = createAccrual({ id: accrualId, isCanceled: false })
      accruals = [accrual, ...accruals.filter((item) => item.id !== accrualId)]
      return accrual
    },
    createSupplierAccrual: async (_token, request) => {
      const accrual = createSupplierAccrual({
        id: crypto.randomUUID(),
        supplierId: request.supplierId,
        expenseTypeId: request.expenseTypeId,
        accountingMonth: request.accountingMonth,
        amount: request.amount,
        source: request.source,
        documentNumber: request.documentNumber ?? null,
        comment: request.comment ?? null,
      })
      supplierAccruals = [accrual, ...supplierAccruals]
      return accrual
    },
    updateSupplierAccrual: async (_token, supplierAccrualId, request) => {
      const accrual = supplierAccruals.find((item) => item.id === supplierAccrualId)
      if (!accrual) {
        throw new Error('Начисление поставщику не найдено.')
      }

      const updated = {
        ...accrual,
        supplierId: request.supplierId,
        expenseTypeId: request.expenseTypeId,
        accountingMonth: request.accountingMonth,
        amount: request.amount,
        source: request.source,
        documentNumber: request.documentNumber ?? null,
        comment: request.comment ?? null,
      }
      supplierAccruals = supplierAccruals.map((item) => (item.id === supplierAccrualId ? updated : item))
      return updated
    },
    cancelSupplierAccrual: async (_token, supplierAccrualId, request) => {
      const accrual = supplierAccruals.find((item) => item.id === supplierAccrualId)
      if (!accrual) {
        throw new Error('Начисление поставщику не найдено.')
      }

      supplierAccruals = supplierAccruals.filter((item) => item.id !== supplierAccrualId)
      return { ...accrual, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    restoreSupplierAccrual: async (_token, supplierAccrualId) => {
      const accrual = createSupplierAccrual({ id: supplierAccrualId, isCanceled: false })
      supplierAccruals = [accrual, ...supplierAccruals.filter((item) => item.id !== supplierAccrualId)]
      return accrual
    },
    generateRegularAccruals: async (_token, request) => {
      const accrual = createAccrual({
        id: crypto.randomUUID(),
        incomeTypeId: request.incomeTypeId,
        accountingMonth: request.accountingMonth,
        amount: 300,
        source: 'regular',
        comment: request.comment ?? null,
      })
      accruals = [accrual, ...accruals]
      return createRegularAccrualGenerationResult({
        accountingMonth: request.accountingMonth,
        incomeTypeId: request.incomeTypeId,
        tariffId: request.tariffId,
        createdAccruals: [accrual],
        totalAmount: accrual.amount,
      })
    },
    generateSupplierGroupSalaryAccruals: async (_token, request) => {
      const accrual = createSupplierAccrual({
        id: crypto.randomUUID(),
        supplierId: 'supplier-1',
        supplierName: 'Водоканал',
        expenseTypeId: 'expense-salary',
        expenseTypeName: 'Зарплата',
        accountingMonth: request.accountingMonth,
        amount: request.amount,
        source: 'regular',
        documentNumber: request.documentNumber ?? null,
        comment: request.comment ? `Зарплата по группе. ${request.comment}` : 'Зарплата по группе',
      })
      supplierAccruals = [accrual, ...supplierAccruals]
      return createSupplierGroupSalaryAccrualGenerationResult({
        accountingMonth: request.accountingMonth,
        supplierGroupId: request.supplierGroupId,
        supplierGroupName: 'Коммунальные услуги',
        expenseTypeId: accrual.expenseTypeId,
        expenseTypeName: accrual.expenseTypeName,
        createdAccruals: [accrual],
        totalAmount: accrual.amount,
      })
    },
    createMeterReading: async (_token, request) => {
      const previousValue = request.meterKind === 'water' ? 10 : 100
      const reading = createMeterReading({
        id: crypto.randomUUID(),
        garageId: request.garageId,
        meterKind: request.meterKind,
        accountingMonth: request.accountingMonth,
        readingDate: request.readingDate,
        currentValue: request.currentValue,
        previousValue,
        consumption: request.currentValue - previousValue,
        hasGapWarning: request.meterKind === 'electricity',
        comment: request.comment ?? null,
      })
      meterReadings = [reading, ...meterReadings]
      return reading
    },
    cancelMeterReading: async (_token, meterReadingId, request) => {
      const reading = meterReadings.find((item) => item.id === meterReadingId)
      if (!reading) {
        throw new Error('Показание счетчика не найдено.')
      }

      meterReadings = meterReadings.filter((item) => item.id !== meterReadingId)
      return { ...reading, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    restoreMeterReading: async (_token, meterReadingId) => {
      const reading = createMeterReading({ id: meterReadingId, isCanceled: false })
      meterReadings = [reading, ...meterReadings.filter((item) => item.id !== meterReadingId)]
      return reading
    },
  })
}

function createStatefulDictionaryClient(): DictionaryClient {
  let lastOwner: OwnerDto | null = null
  let lastGroup: SupplierGroupDto | null = null
  let garages: GarageDto[] = []
  let suppliers: SupplierDto[] = []
  let supplierContacts: SupplierContactDto[] = []
  let staffDepartments: StaffDepartmentDto[] = []
  let staffMembers: StaffMemberDto[] = []
  let incomeTypes: AccountingTypeDto[] = []
  let expenseTypes: AccountingTypeDto[] = []
  let tariffs: TariffDto[] = []
  let irregularPayments: IrregularPaymentDto[] = []

  return {
    getOwners: async () => lastOwner ? [lastOwner] : [],
    createOwner: async (_token, request) => {
      const owner = createOwner({ id: crypto.randomUUID(), lastName: request.lastName, firstName: request.firstName, phone: request.phone ?? null })
      lastOwner = owner
      return owner
    },
    updateOwner: async (_token, id, request) => {
      const owner = createOwner({
        id,
        lastName: request.lastName,
        firstName: request.firstName,
        middleName: request.middleName || null,
        phone: request.phone ?? null,
        address: request.address ?? null,
        meterNotes: request.meterNotes ?? null,
      })
      lastOwner = owner
      return owner
    },
    archiveOwner: async () => undefined,
    restoreOwner: async (_token, id) => {
      const owner = lastOwner?.id === id ? { ...lastOwner, isArchived: false } : createOwner({ id, isArchived: false })
      lastOwner = owner
      return owner
    },
    getGarages: async (_token, search) => {
      const normalized = search?.trim().toLowerCase()
      if (!normalized) {
        return garages
      }

      return garages.filter((garage) => garage.number.toLowerCase().includes(normalized) || (garage.ownerName?.toLowerCase().includes(normalized) ?? false))
    },
    createGarage: async (_token, request) => {
      const owner = lastOwner?.id === request.ownerId ? lastOwner : null
      const garage = createGarage({
        id: crypto.randomUUID(),
        number: request.number,
        peopleCount: request.peopleCount,
        floorCount: request.floorCount,
        ownerId: owner?.id ?? null,
        ownerName: owner?.fullName ?? null,
        startingBalance: request.startingBalance,
        balance: request.startingBalance,
        overdueDebt: Math.max(request.startingBalance, 0),
        initialWaterMeterValue: request.initialWaterMeterValue ?? null,
        initialElectricityMeterValue: request.initialElectricityMeterValue ?? null,
        comment: request.comment ?? null,
      })
      garages = [garage, ...garages]
      return garage
    },
    updateGarage: async (_token, id, request) => {
      const owner = lastOwner?.id === request.ownerId ? lastOwner : null
      const garage = createGarage({
        id,
        number: request.number,
        peopleCount: request.peopleCount,
        floorCount: request.floorCount,
        ownerId: owner?.id ?? null,
        ownerName: owner?.fullName ?? null,
        startingBalance: request.startingBalance,
        balance: request.startingBalance,
        overdueDebt: Math.max(request.startingBalance, 0),
        initialWaterMeterValue: request.initialWaterMeterValue ?? null,
        initialElectricityMeterValue: request.initialElectricityMeterValue ?? null,
        comment: request.comment ?? null,
      })
      garages = garages.map((item) => (item.id === id ? garage : item))
      return garage
    },
    archiveGarage: async (_token, id) => {
      garages = garages.map((item) => (item.id === id ? { ...item, isArchived: true } : item))
    },
    restoreGarage: async (_token, id) => {
      const garage = garages.find((item) => item.id === id) ?? createGarage({ id, isArchived: false })
      const restored = { ...garage, isArchived: false }
      garages = garages.some((item) => item.id === id)
        ? garages.map((item) => (item.id === id ? restored : item))
        : [restored, ...garages]
      return restored
    },
    getSupplierGroups: async () => lastGroup ? [lastGroup] : [],
    createSupplierGroup: async (_token, request) => {
      const group = createGroup({ id: crypto.randomUUID(), name: request.name })
      lastGroup = group
      return group
    },
    updateSupplierGroup: async (_token, id, request) => {
      const group = createGroup({ id, name: request.name })
      lastGroup = group
      return group
    },
    archiveSupplierGroup: async () => undefined,
    restoreSupplierGroup: async (_token, id) => {
      const group = lastGroup?.id === id ? { ...lastGroup, isArchived: false } : createGroup({ id, isArchived: false })
      lastGroup = group
      return group
    },
    getSuppliers: async () => suppliers,
    createSupplier: async (_token, request) => {
      const group = lastGroup?.id === request.groupId ? lastGroup : createGroup({ id: request.groupId, name: 'Поставщики' })
      const supplier = createSupplier({
        id: crypto.randomUUID(),
        name: request.name,
        groupId: group.id,
        groupName: group.name,
        inn: request.inn ?? null,
        startingBalance: request.startingBalance,
      })
      suppliers = [supplier, ...suppliers]
      return supplier
    },
    updateSupplier: async (_token, id, request) => {
      const group = lastGroup?.id === request.groupId ? lastGroup : createGroup({ id: request.groupId, name: 'Поставщики' })
      const supplier = createSupplier({
        id,
        name: request.name,
        groupId: group.id,
        groupName: group.name,
        inn: request.inn ?? null,
        legalAddress: request.legalAddress ?? null,
        contactPerson: request.contactPerson ?? null,
        phone: request.phone ?? null,
        email: request.email ?? null,
        startingBalance: request.startingBalance,
        comment: request.comment ?? null,
      })
      suppliers = suppliers.map((item) => (item.id === id ? supplier : item))
      return supplier
    },
    archiveSupplier: async () => undefined,
    restoreSupplier: async (_token, id) => {
      const supplier = suppliers.find((item) => item.id === id) ?? createSupplier({ id, isArchived: false })
      const restored = { ...supplier, isArchived: false }
      suppliers = suppliers.some((item) => item.id === id)
        ? suppliers.map((item) => (item.id === id ? restored : item))
        : [restored, ...suppliers]
      return restored
    },
    getSupplierContacts: async (_token, supplierId) => supplierId ? supplierContacts.filter((item) => item.supplierId === supplierId) : supplierContacts,
    createSupplierContact: async (_token, request) => {
      const supplier = suppliers.find((item) => item.id === request.supplierId)
      const contact = createSupplierContact({
        id: crypto.randomUUID(),
        supplierId: request.supplierId,
        supplierName: supplier?.name ?? 'Поставщик',
        fullName: request.fullName,
        position: request.position ?? null,
        phone: request.phone ?? null,
        email: request.email ?? null,
        status: request.status,
        comment: request.comment ?? null,
      })
      supplierContacts = [contact, ...supplierContacts]
      return contact
    },
    updateSupplierContact: async (_token, id, request) => {
      const supplier = suppliers.find((item) => item.id === request.supplierId)
      const contact = createSupplierContact({
        id,
        supplierId: request.supplierId,
        supplierName: supplier?.name ?? 'Поставщик',
        fullName: request.fullName,
        position: request.position ?? null,
        phone: request.phone ?? null,
        email: request.email ?? null,
        status: request.status,
        comment: request.comment ?? null,
      })
      supplierContacts = supplierContacts.map((item) => (item.id === id ? contact : item))
      return contact
    },
    archiveSupplierContact: async (_token, id) => {
      supplierContacts = supplierContacts.map((item) => (item.id === id ? { ...item, isArchived: true } : item))
    },
    restoreSupplierContact: async (_token, id) => {
      const contact = supplierContacts.find((item) => item.id === id) ?? createSupplierContact({ id, isArchived: false })
      const restored = { ...contact, isArchived: false }
      supplierContacts = supplierContacts.some((item) => item.id === id)
        ? supplierContacts.map((item) => (item.id === id ? restored : item))
        : [restored, ...supplierContacts]
      return restored
    },
    getStaffDepartments: async () => staffDepartments,
    createStaffDepartment: async (_token, request) => {
      const department = createStaffDepartment({ id: crypto.randomUUID(), name: request.name })
      staffDepartments = [department, ...staffDepartments]
      return department
    },
    updateStaffDepartment: async (_token, id, request) => {
      const department = createStaffDepartment({ id, name: request.name })
      staffDepartments = staffDepartments.map((item) => (item.id === id ? department : item))
      return department
    },
    archiveStaffDepartment: async (_token, id) => {
      staffDepartments = staffDepartments.map((item) => (item.id === id ? { ...item, isArchived: true } : item))
    },
    restoreStaffDepartment: async (_token, id) => {
      const department = staffDepartments.find((item) => item.id === id) ?? createStaffDepartment({ id, isArchived: false })
      const restored = { ...department, isArchived: false }
      staffDepartments = staffDepartments.some((item) => item.id === id)
        ? staffDepartments.map((item) => (item.id === id ? restored : item))
        : [restored, ...staffDepartments]
      return restored
    },
    getStaffMembers: async (_token, departmentId) => departmentId ? staffMembers.filter((item) => item.departmentId === departmentId) : staffMembers,
    createStaffMember: async (_token, request) => {
      const department = staffDepartments.find((item) => item.id === request.departmentId) ?? createStaffDepartment({ id: request.departmentId, name: 'Отдел' })
      const member = createStaffMember({
        id: crypto.randomUUID(),
        fullName: request.fullName,
        departmentId: department.id,
        departmentName: department.name,
        rate: request.rate,
      })
      staffMembers = [member, ...staffMembers]
      return member
    },
    updateStaffMember: async (_token, id, request) => {
      const department = staffDepartments.find((item) => item.id === request.departmentId) ?? createStaffDepartment({ id: request.departmentId, name: 'Отдел' })
      const member = createStaffMember({
        id,
        fullName: request.fullName,
        departmentId: department.id,
        departmentName: department.name,
        rate: request.rate,
      })
      staffMembers = staffMembers.map((item) => (item.id === id ? member : item))
      return member
    },
    archiveStaffMember: async (_token, id) => {
      staffMembers = staffMembers.map((item) => (item.id === id ? { ...item, isArchived: true } : item))
    },
    restoreStaffMember: async (_token, id) => {
      const member = staffMembers.find((item) => item.id === id) ?? createStaffMember({ id, isArchived: false })
      const restored = { ...member, isArchived: false }
      staffMembers = staffMembers.some((item) => item.id === id)
        ? staffMembers.map((item) => (item.id === id ? restored : item))
        : [restored, ...staffMembers]
      return restored
    },
    getIncomeTypes: async () => incomeTypes,
    createIncomeType: async (_token, request) => {
      const incomeType = createAccountingType({ id: crypto.randomUUID(), name: request.name, code: request.code ?? null })
      incomeTypes = [incomeType, ...incomeTypes]
      return incomeType
    },
    updateIncomeType: async (_token, id, request) => {
      const incomeType = createAccountingType({ id, name: request.name, code: request.code ?? null })
      incomeTypes = incomeTypes.map((item) => (item.id === id ? incomeType : item))
      return incomeType
    },
    archiveIncomeType: async () => undefined,
    restoreIncomeType: async (_token, id) => {
      const incomeType = incomeTypes.find((item) => item.id === id) ?? createAccountingType({ id, isArchived: false })
      const restored = { ...incomeType, isArchived: false }
      incomeTypes = incomeTypes.some((item) => item.id === id)
        ? incomeTypes.map((item) => (item.id === id ? restored : item))
        : [restored, ...incomeTypes]
      return restored
    },
    getExpenseTypes: async () => expenseTypes,
    createExpenseType: async (_token, request) => {
      const expenseType = createAccountingType({ id: crypto.randomUUID(), name: request.name, code: request.code ?? null })
      expenseTypes = [expenseType, ...expenseTypes]
      return expenseType
    },
    updateExpenseType: async (_token, id, request) => {
      const expenseType = createAccountingType({ id, name: request.name, code: request.code ?? null })
      expenseTypes = expenseTypes.map((item) => (item.id === id ? expenseType : item))
      return expenseType
    },
    archiveExpenseType: async () => undefined,
    restoreExpenseType: async (_token, id) => {
      const expenseType = expenseTypes.find((item) => item.id === id) ?? createAccountingType({ id, isArchived: false })
      const restored = { ...expenseType, isArchived: false }
      expenseTypes = expenseTypes.some((item) => item.id === id)
        ? expenseTypes.map((item) => (item.id === id ? restored : item))
        : [restored, ...expenseTypes]
      return restored
    },
    getTariffs: async () => tariffs,
    createTariff: async (_token, request) => {
      const tariff = createTariff({
        id: crypto.randomUUID(),
        name: request.name,
        calculationBase: request.calculationBase,
        rate: request.rate,
        effectiveFrom: request.effectiveFrom,
        comment: request.comment ?? null,
        electricityFirstThreshold: request.electricityFirstThreshold ?? null,
        electricitySecondThreshold: request.electricitySecondThreshold ?? null,
        electricityFirstRate: request.electricityFirstRate ?? null,
        electricitySecondRate: request.electricitySecondRate ?? null,
        electricityThirdRate: request.electricityThirdRate ?? null,
      })
      tariffs = [tariff, ...tariffs]
      return tariff
    },
    updateTariff: async (_token, id, request) => {
      const tariff = createTariff({
        id,
        name: request.name,
        calculationBase: request.calculationBase,
        rate: request.rate,
        effectiveFrom: request.effectiveFrom,
        comment: request.comment ?? null,
        electricityFirstThreshold: request.electricityFirstThreshold ?? null,
        electricitySecondThreshold: request.electricitySecondThreshold ?? null,
        electricityFirstRate: request.electricityFirstRate ?? null,
        electricitySecondRate: request.electricitySecondRate ?? null,
        electricityThirdRate: request.electricityThirdRate ?? null,
      })
      tariffs = tariffs.map((item) => (item.id === id ? tariff : item))
      return tariff
    },
    archiveTariff: async () => undefined,
    getIrregularPayments: async () => irregularPayments,
    createIrregularPayment: async (_token, request) => {
      const payment = createIrregularPayment({ id: crypto.randomUUID(), name: request.name, amount: request.amount, isActive: request.isActive ?? true })
      irregularPayments = [payment, ...irregularPayments]
      return payment
    },
    updateIrregularPayment: async (_token, id, request) => {
      const payment = createIrregularPayment({ id, name: request.name, amount: request.amount, isActive: request.isActive ?? true })
      irregularPayments = irregularPayments.map((item) => (item.id === id ? payment : item))
      return payment
    },
    setIrregularPaymentStatus: async (_token, id, request) => {
      const existing = irregularPayments.find((item) => item.id === id) ?? createIrregularPayment({ id })
      const payment = { ...existing, isActive: request.isActive }
      irregularPayments = irregularPayments.map((item) => (item.id === id ? payment : item))
      return payment
    },
    archiveIrregularPayment: async (_token, id) => {
      irregularPayments = irregularPayments.map((item) => (item.id === id ? { ...item, isArchived: true } : item))
    },
    restoreIrregularPayment: async (_token, id) => {
      const existing = irregularPayments.find((item) => item.id === id) ?? createIrregularPayment({ id })
      const payment = { ...existing, isArchived: false }
      irregularPayments = irregularPayments.some((item) => item.id === id)
        ? irregularPayments.map((item) => (item.id === id ? payment : item))
        : [payment, ...irregularPayments]
      return payment
    },
  }
}

function createAuthResponse(overrides: Partial<AuthResponse> & { user?: Partial<AuthResponse['user']> } = {}): AuthResponse {
  const response: AuthResponse = {
    accessToken: 'token',
    expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
    user: {
      id: '5df20dec-2959-4726-a1cb-0e6ec6b28674',
      email: 'admin@example.com',
      displayName: 'Администратор',
      roles: ['administrator'],
      permissions: ['users.manage', 'dictionaries.read', 'dictionaries.write', 'tariffs.manage', 'payments.read', 'payments.write', 'reports.read', 'import.run', 'app_releases.manage', 'audit.read'],
    },
  }

  return {
    ...response,
    ...overrides,
    user: {
      ...response.user,
      ...overrides.user,
    },
  }
}

function createAppRelease(overrides: Partial<AppReleaseDto> = {}): AppReleaseDto {
  return {
    releaseId: '2026-06-23-consolidated-report',
    version: '0.11.0',
    publishedAt: '2026-06-23T06:15:00+07:00',
    title: 'Добавлен консолидированный отчет',
    summary: 'Появился первый отчет для месячной сверки: итоги за период, помесячные строки и детализация по гаражам.',
    items: [
      {
        type: 'new',
        text: 'Добавлена панель "Отчеты" с выбором периода, поиском по гаражу или владельцу и итогами.',
      },
    ],
    ...overrides,
  }
}

function createAuditEvent(overrides: Partial<AuditEventDto>): AuditEventDto {
  return {
    id: overrides.id ?? `audit-${overrides.action ?? 'event'}`,
    createdAtUtc: '2026-06-23T04:00:00Z',
    actorUserId: '5df20dec-2959-4726-a1cb-0e6ec6b28674',
    action: 'auth.login_success',
    entityType: 'user',
    entityId: '5df20dec-2959-4726-a1cb-0e6ec6b28674',
    summary: 'Вход пользователя.',
    ...overrides,
  }
}

function createRoles(): ManagedRoleDto[] {
  return [
    { code: 'administrator', name: 'Администратор', permissions: ['users.manage', 'dictionaries.read', 'dictionaries.write', 'tariffs.manage', 'payments.read', 'payments.write', 'reports.read', 'import.run', 'app_releases.manage', 'audit.read'] },
    { code: 'operator', name: 'Оператор', permissions: ['dictionaries.read', 'payments.read', 'payments.write'] },
    { code: 'accountant', name: 'Бухгалтер', permissions: ['dictionaries.read', 'dictionaries.write', 'tariffs.manage', 'payments.read', 'payments.write', 'reports.read', 'import.run'] },
    { code: 'reports_viewer', name: 'Просмотр отчетов', permissions: ['dictionaries.read', 'reports.read'] },
  ]
}

function createManagedUser(overrides: Partial<ManagedUserDto>): ManagedUserDto {
  return {
    id: 'user',
    email: 'user@example.com',
    displayName: 'Пользователь',
    isActive: true,
    createdAtUtc: new Date(Date.now() - 60_000).toISOString(),
    lastLoginAtUtc: null,
    roles: ['operator'],
    permissions: ['dictionaries.read'],
    ...overrides,
  }
}

function createOwner(overrides: Partial<OwnerDto>): OwnerDto {
  const owner = {
    id: 'owner',
    lastName: 'Иванов',
    firstName: 'Иван',
    middleName: null,
    fullName: '',
    phone: null,
    address: null,
    meterNotes: null,
    isArchived: false,
    ...overrides,
  }

  return { ...owner, fullName: owner.fullName || `${owner.lastName} ${owner.firstName}` }
}

function createGarage(overrides: Partial<GarageDto>): GarageDto {
  const garage = {
    id: 'garage',
    number: '1',
    peopleCount: 1,
    floorCount: 1,
    ownerId: null,
    ownerName: null,
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

function createGroup(overrides: Partial<SupplierGroupDto>): SupplierGroupDto {
  return {
    id: 'group',
    name: 'Поставщики',
    isSystem: false,
    isArchived: false,
    ...overrides,
  }
}

function createSupplier(overrides: Partial<SupplierDto>): SupplierDto {
  return {
    id: 'supplier',
    name: 'Поставщик',
    groupId: 'group',
    groupName: 'Поставщики',
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

function createSupplierContact(overrides: Partial<SupplierContactDto>): SupplierContactDto {
  return {
    id: 'supplier-contact',
    supplierId: 'supplier',
    supplierName: 'Поставщик',
    fullName: 'Контакт',
    position: null,
    phone: null,
    email: null,
    status: 'Работает',
    comment: null,
    isArchived: false,
    ...overrides,
  }
}

function createStaffDepartment(overrides: Partial<StaffDepartmentDto>): StaffDepartmentDto {
  return {
    id: 'staff-department',
    name: 'Отдел',
    isArchived: false,
    ...overrides,
  }
}

function createStaffMember(overrides: Partial<StaffMemberDto>): StaffMemberDto {
  return {
    id: 'staff-member',
    fullName: 'Сотрудник',
    departmentId: 'staff-department',
    departmentName: 'Отдел',
    rate: 0,
    isArchived: false,
    ...overrides,
  }
}

function createAccountingType(overrides: Partial<AccountingTypeDto>): AccountingTypeDto {
  return {
    id: 'accounting-type',
    name: 'Тип',
    code: null,
    isSystem: false,
    isArchived: false,
    ...overrides,
  }
}

function createTariff(overrides: Partial<TariffDto>): TariffDto {
  return {
    id: 'tariff',
    name: 'Тариф',
    calculationBase: 'fixed',
    rate: 1,
    electricityFirstThreshold: null,
    electricitySecondThreshold: null,
    electricityFirstTierName: null,
    electricitySecondTierName: null,
    electricityThirdTierName: null,
    electricityFirstRate: null,
    electricitySecondRate: null,
    electricityThirdRate: null,
    effectiveFrom: '2026-07-01',
    comment: null,
    isArchived: false,
    ...overrides,
  }
}

function createIrregularPayment(overrides: Partial<IrregularPaymentDto> = {}): IrregularPaymentDto {
  return {
    id: 'irregular-payment',
    name: 'Нерегулярный платеж',
    amount: 0,
    isActive: true,
    isArchived: false,
    isUsed: false,
    ...overrides,
  }
}

function createFeeCampaign(overrides: Partial<FeeCampaignDto> = {}): FeeCampaignDto {
  return {
    id: 'fee-campaign',
    name: 'Сбор на ворота',
    incomeTypeId: 'income-type-1',
    incomeTypeName: 'Членский взнос',
    goal: 'Ремонт ворот',
    contributionAmount: 500,
    targetAmount: 33500,
    startsOn: '2026-06-01',
    endsOn: null,
    appliesToAllGarages: true,
    participantGarageIds: [],
    overdueGraceDays: 30,
    isArchived: false,
    ...overrides,
  }
}

function getTestCurrentMonthInputValue(date = new Date()) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`
}

function addTestMonths(value: string, offset: number) {
  const [yearText, monthText] = value.split('-')
  const date = new Date(Number(yearText), Number(monthText) - 1 + offset, 1)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`
}

function createChargeServiceSetting(overrides: Partial<ChargeServiceSettingDto> = {}): ChargeServiceSettingDto {
  return {
    id: 'charge-service',
    name: 'Услуга',
    isRegular: false,
    periodicityMonths: null,
    accrualStartMonth: null,
    paymentDueDay: null,
    paymentDueMonth: null,
    overdueGraceDays: 0,
    incomeTypeId: null,
    tariffId: null,
    isMetered: false,
    hasTieredTariff: false,
    unitName: 'руб.',
    isArchived: false,
    ...overrides,
  }
}

function createFinancialOperation(overrides: Partial<FinancialOperationDto>): FinancialOperationDto {
  return {
    id: 'operation',
    operationKind: 'income',
    operationDate: '2026-06-19',
    accountingMonth: '2026-06-01',
    amount: 100,
    documentNumber: null,
    comment: null,
    garageId: 'garage-1',
    garageNumber: '12',
    ownerName: 'Иванов Иван',
    incomeTypeId: 'income-type-1',
    incomeTypeName: 'Членский взнос',
    supplierId: null,
    supplierName: null,
    expenseTypeId: null,
    expenseTypeName: null,
    garageDebtBefore: null,
    garageDebtAfter: null,
    supplierDebtBefore: null,
    supplierDebtAfter: null,
    paymentAllocations: [],
    isCanceled: false,
    staffMemberId: null,
    staffMemberName: null,
    staffDepartmentName: null,
    createdAtUtc: '2026-06-19T10:24:00Z',
    ...overrides,
  }
}

function createGarageBalanceHistory(overrides: Partial<GarageBalanceHistoryDto>): GarageBalanceHistoryDto {
  return {
    garageId: 'garage-1',
    garageNumber: '12',
    ownerName: 'Иванов Иван',
    monthFrom: '2026-06-01',
    monthTo: '2026-07-01',
    startingBalance: 100,
    accrualTotal: 1200,
    incomeTotal: 500,
    debt: 800,
    rows: [
      { accountingMonth: '2026-06-01', openingDebt: 100, accrualAmount: 500, incomeAmount: 200, closingDebt: 400 },
      { accountingMonth: '2026-07-01', openingDebt: 400, accrualAmount: 700, incomeAmount: 300, closingDebt: 800 },
    ],
    ...overrides,
  }
}

function createGarageIncomeWorksheet(overrides: Partial<GarageIncomeWorksheetDto>): GarageIncomeWorksheetDto {
  return {
    garageId: 'garage-1',
    garageNumber: '12',
    ownerName: 'Иванов Иван',
    monthFrom: '2026-06-01',
    monthTo: '2026-06-01',
    openingDebt: 0,
    accrualTotal: 5674,
    incomeTotal: 1000,
    debtTotal: 4674,
    closingDebt: 4674,
    rows: [
      {
        accountingMonth: '2026-06-01',
        incomeTypeId: 'income-type-electricity',
        incomeTypeName: 'Электроэнергия',
        meterKind: 'electricity',
        meterValue: 86,
        meterConsumption: 18,
        accrualAmount: 5674,
        incomeAmount: 1000,
        debt: 4674,
      },
    ],
    ...overrides,
  }
}

function createExpenseWorksheet(overrides: Partial<ExpenseWorksheetDto>): ExpenseWorksheetDto {
  return {
    accountingMonth: '2026-06-01',
    accrualTotal: 72000,
    expenseTotal: 25000,
    balanceTotal: 47000,
    collectedTotal: 29000,
    differenceTotal: -3000,
    bankAmount: 12000,
    cashAmount: 4000,
    rows: [
      {
        rowKind: 'supplier',
        supplierId: 'supplier-water',
        staffMemberId: null,
        counterpartyName: 'Водоканал',
        expenseTypeId: 'expense-water',
        expenseTypeName: 'Водоснабжение',
        accrualAmount: 32000,
        expenseAmount: 10000,
        balance: 22000,
        collectedAmount: 29000,
        difference: -3000,
      },
      {
        rowKind: 'staff',
        supplierId: null,
        staffMemberId: 'staff-accountant',
        counterpartyName: 'Петрова Ольга',
        expenseTypeId: null,
        expenseTypeName: 'Бухгалтерия',
        accrualAmount: 40000,
        expenseAmount: 15000,
        balance: 25000,
        collectedAmount: null,
        difference: null,
      },
    ],
    ...overrides,
  }
}

function createAccrual(overrides: Partial<AccrualDto>): AccrualDto {
  return {
    id: 'accrual',
    garageId: 'garage-1',
    garageNumber: '12',
    ownerName: 'Иванов Иван',
    incomeTypeId: 'income-type-1',
    incomeTypeName: 'Членский взнос',
    accountingMonth: '2026-06-01',
    amount: 100,
    source: 'manual',
    comment: null,
    isCanceled: false,
    ...overrides,
  }
}

function createSupplierAccrual(overrides: Partial<SupplierAccrualDto>): SupplierAccrualDto {
  return {
    id: 'supplier-accrual',
    supplierId: 'supplier-1',
    supplierName: 'Водоканал',
    expenseTypeId: 'expense-type-1',
    expenseTypeName: 'Вода',
    accountingMonth: '2026-06-01',
    amount: 100,
    source: 'manual',
    documentNumber: null,
    comment: null,
    isCanceled: false,
    ...overrides,
  }
}

function createRegularAccrualGenerationResult(overrides: Partial<RegularAccrualGenerationResultDto>): RegularAccrualGenerationResultDto {
  return {
    accountingMonth: '2026-06-01',
    incomeTypeId: 'income-type-1',
    incomeTypeName: 'Членский взнос',
    tariffId: 'tariff-fixed',
    tariffName: 'Членский тариф',
    calculationBase: 'fixed',
    createdCount: overrides.createdAccruals?.length ?? 1,
    skippedCount: 0,
    totalAmount: 300,
    createdAccruals: [createAccrual({ amount: 300, source: 'regular' })],
    skippedGarages: [],
    ...overrides,
  }
}

function createRegularCatalogAccrualGenerationResult(overrides: Partial<RegularCatalogAccrualGenerationResultDto>): RegularCatalogAccrualGenerationResultDto {
  const serviceResults = overrides.serviceResults ?? [createRegularAccrualGenerationResult({})]
  return {
    accountingMonth: '2026-06-01',
    serviceCount: serviceResults.length,
    createdCount: serviceResults.reduce((total, result) => total + result.createdCount, 0),
    skippedCount: 0,
    totalAmount: serviceResults.reduce((total, result) => total + result.totalAmount, 0),
    serviceResults,
    skippedServices: [],
    ...overrides,
  }
}

function createFeeCampaignAccrualGenerationResult(overrides: Partial<FeeCampaignAccrualGenerationResultDto>): FeeCampaignAccrualGenerationResultDto {
  return {
    accountingMonth: '2026-06-01',
    feeCampaignId: 'fee-campaign',
    feeCampaignName: 'Сбор на ворота',
    incomeTypeId: 'income-type-1',
    incomeTypeName: 'Членский взнос',
    contributionAmount: 500,
    createdCount: overrides.createdAccruals?.length ?? 1,
    skippedCount: 0,
    totalAmount: 500,
    createdAccruals: [createAccrual({ amount: 500, source: 'fee_campaign' })],
    skippedGarages: [],
    ...overrides,
  }
}

function createSupplierGroupSalaryAccrualGenerationResult(overrides: Partial<SupplierGroupSalaryAccrualGenerationResultDto>): SupplierGroupSalaryAccrualGenerationResultDto {
  return {
    accountingMonth: '2026-06-01',
    supplierGroupId: 'group-1',
    supplierGroupName: 'Коммунальные услуги',
    expenseTypeId: 'expense-salary',
    expenseTypeName: 'Зарплата',
    createdCount: overrides.createdAccruals?.length ?? 1,
    skippedCount: 0,
    totalAmount: 7000,
    createdAccruals: [createSupplierAccrual({ amount: 7000, source: 'regular', expenseTypeName: 'Зарплата' })],
    skippedSuppliers: [],
    ...overrides,
  }
}

function createAccessImportRun(overrides: Partial<AccessImportRunDto> = {}): AccessImportRunDto {
  return {
    id: 'access-run',
    mode: 'dry_run',
    status: 'completed',
    originalFileName: 'ГСК.accdb',
    fileExtension: '.accdb',
    fileSizeBytes: 1024,
    contentSha256: 'a'.repeat(64),
    startedAtUtc: new Date(Date.now() - 60_000).toISOString(),
    finishedAtUtc: new Date().toISOString(),
    totalChecks: 3,
    passedChecks: 2,
    warningCount: 1,
    errorCount: 0,
    summary: 'Dry-run завершен с предупреждениями.',
    checks: [
      { code: 'extension', title: 'Формат файла', status: 'passed', message: 'Расширение поддерживается.' },
      { code: 'signature', title: 'Сигнатура Access', status: 'passed', message: 'Файл похож на Access.' },
      { code: 'native_reader', title: 'Драйвер чтения .accdb', status: 'warning', message: 'Нужен ACE-драйвер или конвертация.' },
    ],
    ...overrides,
  }
}

function createAccessImportReaderStatus(overrides: Partial<AccessImportReaderStatusDto> = {}): AccessImportReaderStatusDto {
  return {
    provider: 'disabled',
    displayName: 'Reader Access',
    isAvailable: false,
    status: 'not_configured',
    statusMessage: 'Фактическое чтение Access не подключено.',
    requiredComponents: ['ACE OLE DB driver'],
    checkedAtUtc: new Date().toISOString(),
    ...overrides,
  }
}

function createAccessImportQuarantineItem(overrides: Partial<AccessImportQuarantineItemDto> = {}): AccessImportQuarantineItemDto {
  return {
    id: 'quarantine-1',
    accessImportRunId: 'access-run',
    sourceSystem: 'Access',
    entityType: 'Garage',
    externalId: '42',
    rowHash: 'b'.repeat(64),
    reasonCode: 'missing-owner',
    reasonMessage: 'Не найден владелец гаража.',
    severity: 'error',
    status: 'open',
    createdAtUtc: new Date().toISOString(),
    createdByUserId: null,
    resolvedAtUtc: null,
    resolvedByUserId: null,
    resolutionComment: null,
    ...overrides,
  }
}

function createAccessImportCreatedRecord(overrides: Partial<AccessImportCreatedRecordDto> = {}): AccessImportCreatedRecordDto {
  return {
    id: 'created-record-1',
    accessImportRunId: 'access-run',
    sourceSystem: 'Access',
    sourceEntityType: 'Garage',
    sourceExternalId: '42',
    sourceRowHash: 'c'.repeat(64),
    targetEntityType: 'garage',
    targetEntityId: 'garage-42',
    targetDisplayName: 'Гараж 42',
    rollbackStatus: 'created',
    createdAtUtc: new Date().toISOString(),
    createdByUserId: null,
    rolledBackAtUtc: null,
    rolledBackByUserId: null,
    rollbackReason: null,
    ...overrides,
  }
}

function createAccessImportRunLogEntry(overrides: Partial<AccessImportRunLogEntryDto> = {}): AccessImportRunLogEntryDto {
  return {
    id: crypto.randomUUID(),
    accessImportRunId: 'access-run',
    createdAtUtc: new Date().toISOString(),
    level: 'info',
    stepCode: 'file_received',
    message: 'Файл получен для dry-run проверки.',
    ...overrides,
  }
}

function createOneCFreshStatus(overrides: Partial<OneCFreshIntegrationStatusDto> = {}): OneCFreshIntegrationStatusDto {
  return {
    provider: 'OneCFresh',
    displayName: '1C Fresh',
    isConfigured: false,
    canSynchronize: false,
    status: 'not_configured',
    statusMessage: 'Для будущей синхронизации нужно сохранить защищенную настройку OneCFresh:RefreshToken.',
    requiredSettings: ['RefreshToken'],
    configuredSettings: [],
    lastProtectedSettingUpdatedAtUtc: null,
    ...overrides,
  }
}

function createOneCFreshSync(overrides: Partial<OneCFreshSyncDto> = {}): OneCFreshSyncDto {
  return {
    auditEventId: 'audit-one-c-fresh-sync',
    provider: 'OneCFresh',
    status: 'pending_adapter',
    statusMessage: 'Запуск синхронизации зарегистрирован в истории.',
    requestedAtUtc: '2026-07-10T00:00:00Z',
    ...overrides,
  }
}

function createReceiptPrintingStatus(overrides: Partial<ReceiptPrintingIntegrationStatusDto> = {}): ReceiptPrintingIntegrationStatusDto {
  return {
    provider: 'ReceiptPrinting',
    displayName: 'Печать чеков и квитанций',
    isConfigured: false,
    canPrint: false,
    status: 'not_configured',
    statusMessage: 'Для будущей печати нужно сохранить защищенные настройки ReceiptPrinting:DeviceConnection и ReceiptPrinting:ReceiptTemplate.',
    requiredSettings: ['DeviceConnection', 'ReceiptTemplate'],
    configuredSettings: [],
    plannedActions: ['Печать квитанции', 'Отмена печати', 'Повторная печать'],
    lastProtectedSettingUpdatedAtUtc: null,
    ...overrides,
  }
}

function createReceiptPrintingAction(overrides: Partial<ReceiptPrintingActionDto> = {}): ReceiptPrintingActionDto {
  return {
    auditEventId: 'audit-receipt-action',
    financialOperationId: 'operation-garage-77',
    action: 'print',
    status: 'pending_adapter',
    statusMessage: 'Действие квитанции зарегистрировано.',
    documentNumber: null,
    registeredAtUtc: '2026-07-09T10:00:00Z',
    ...overrides,
  }
}

function createConsolidatedReport(overrides: Partial<ConsolidatedReportDto> = {}): ConsolidatedReportDto {
  return {
    periodFrom: '2026-06-01',
    periodTo: '2026-06-01',
    incomeTotal: 1500,
    expenseTotal: 400,
    accrualTotal: 2000,
    balance: 1100,
    debt: 500,
    operationCount: 2,
    accrualCount: 1,
    meterReadingCount: 1,
    monthlyRows: [
      {
        accountingMonth: '2026-06-01',
        incomeTotal: 1500,
        expenseTotal: 400,
        accrualTotal: 2000,
        balance: 1100,
        debt: 500,
        operationCount: 2,
        accrualCount: 1,
        meterReadingCount: 1,
      },
    ],
    garageRowCount: 1,
    garageRows: [
      {
        garageId: 'garage-1',
        garageNumber: '12',
        ownerName: 'Иванов Иван',
        incomeTotal: 1500,
        accrualTotal: 2000,
        debt: 500,
        meterReadingCount: 1,
      },
    ],
    ...overrides,
  }
}

function createIncomeReport(overrides: Partial<IncomeReportDto> = {}): IncomeReportDto {
  return {
    dateFrom: '2026-06-01',
    dateTo: '2026-06-30',
    accrualTotal: 2000,
    incomeTotal: 1500,
    debt: 500,
    rowCount: 2,
    rows: [
      {
        rowType: 'accruals',
        date: '2026-06-01',
        accountingMonth: '2026-06-01',
        garageId: 'garage-1',
        garageNumber: '12',
        ownerId: 'owner-1',
        ownerName: 'Иванов Иван',
        incomeTypeId: 'income-type-1',
        incomeTypeName: 'Членский взнос',
        accrualAmount: 2000,
        incomeAmount: 0,
        debt: 2000,
        documentNumber: null,
        comment: 'Начисление за июнь',
        createdAtUtc: null,
      },
      {
        rowType: 'payments',
        date: '2026-06-10',
        accountingMonth: '2026-06-01',
        garageId: 'garage-1',
        garageNumber: '12',
        ownerId: 'owner-1',
        ownerName: 'Иванов Иван',
        incomeTypeId: 'income-type-1',
        incomeTypeName: 'Членский взнос',
        accrualAmount: 0,
        incomeAmount: 1500,
        debt: -1500,
        documentNumber: 'PKO-1',
        comment: 'Оплата за июнь',
        createdAtUtc: '2026-06-10T10:24:00+07:00',
        debtAfterPayment: 500,
      },
    ],
    ...overrides,
  }
}

function createExpenseReport(overrides: Partial<ExpenseReportDto> = {}): ExpenseReportDto {
  return {
    dateFrom: '2026-06-01',
    dateTo: '2026-06-30',
    accrualTotal: 0,
    expenseTotal: 400,
    difference: -400,
    rowCount: 1,
    rows: [
      {
        rowType: 'payments',
        date: '2026-06-12',
        accountingMonth: '2026-06-01',
        supplierId: 'supplier-1',
        supplierName: 'Водоканал',
        expenseTypeId: 'expense-type-1',
        expenseTypeName: 'Вода',
        accrualAmount: 0,
        expenseAmount: 400,
        difference: -400,
        documentNumber: 'RKO-1',
        comment: 'Оплата воды',
      },
    ],
    ...overrides,
  }
}

function createFundChangeReport(overrides: Partial<FundChangeReportDto> = {}): FundChangeReportDto {
  return {
    dateFrom: '2026-06-01',
    dateTo: '2026-06-30',
    depositTotal: 1500,
    withdrawalTotal: 300,
    rowCount: 2,
    rows: [
      {
        operationId: 'fund-operation-1',
        fundId: 'fund-electricity',
        fundName: 'Электроэнергия',
        date: '2026-06-10',
        changeKind: 'deposit',
        changeName: 'Пополнение',
        amount: 1500,
        balanceBefore: 0,
        balanceAfter: 1500,
        actorUserId: 'user-admin',
        actorDisplayName: 'Администратор ГСК',
        reason: 'Распределение средств',
      },
      {
        operationId: 'fund-operation-2',
        fundId: 'fund-electricity',
        fundName: 'Электроэнергия',
        date: '2026-06-11',
        changeKind: 'withdraw',
        changeName: 'Изъятие',
        amount: 300,
        balanceBefore: 1500,
        balanceAfter: 1200,
        actorUserId: 'user-admin',
        actorDisplayName: 'Администратор ГСК',
        reason: 'Оплата счета',
      },
    ],
    ...overrides,
  }
}

function createCashPaymentReport(overrides: Partial<CashPaymentReportDto> = {}): CashPaymentReportDto {
  return {
    dateFrom: '2026-06-01',
    dateTo: '2026-06-30',
    total: 400,
    rowCount: 1,
    rows: [
      {
        operationId: 'cash-payment-1',
        date: '2026-06-12',
        amount: 400,
        hasReceipt: true,
        purpose: 'Вода: Водоканал',
        supplierName: 'Водоканал',
        expenseTypeName: 'Вода',
        documentNumber: 'RKO-1',
        comment: 'Оплата воды',
      },
    ],
    ...overrides,
  }
}

function createBankDepositReport(overrides: Partial<BankDepositReportDto> = {}): BankDepositReportDto {
  return {
    dateFrom: '2026-06-01',
    dateTo: '2026-06-30',
    total: 3000,
    rowCount: 1,
    rows: [
      {
        operationId: 'bank-deposit-1',
        date: '2026-06-15',
        amount: 3000,
        fundName: 'Прочее',
        comment: 'Сдача наличных в банк',
      },
    ],
    ...overrides,
  }
}

function createFeeReport(overrides: Partial<FeeReportDto> = {}): FeeReportDto {
  return {
    variation: 'Сбор на ворота',
    accruedTotal: 500,
    collectedTotal: 200,
    debtTotal: 300,
    rowCount: 2,
    summaryRows: [
      {
        incomeTypeId: 'income-type-fee',
        name: 'Сбор на ворота',
        goal: 'Сбор',
        feeAmount: 500,
        collected: 200,
      },
    ],
    garageRows: [
      {
        garageId: 'garage-12',
        garageNumber: '12',
        ownerName: 'Иванов Иван',
        incomeTypeId: 'income-type-fee',
        feeName: 'Сбор на ворота',
        accrued: 500,
        paid: 200,
        lastPaymentDate: '2026-06-10',
        debt: 300,
      },
      {
        garageId: 'garage-21',
        garageNumber: '21',
        ownerName: 'Петров Петр',
        incomeTypeId: 'income-type-fee',
        feeName: 'Сбор на ворота',
        accrued: 500,
        paid: 500,
        lastPaymentDate: '2026-06-11',
        debt: 0,
      },
    ],
    debtorRows: [
      {
        garageId: 'garage-12',
        garageNumber: '12',
        ownerName: 'Иванов Иван',
        incomeTypeId: 'income-type-fee',
        feeName: 'Сбор на ворота',
        paid: 200,
        lastPaymentDate: '2026-06-10',
        debt: 300,
      },
    ],
    ...overrides,
  }
}

function createMeterReading(overrides: Partial<MeterReadingDto>): MeterReadingDto {
  return {
    id: 'meter-reading',
    garageId: 'garage-1',
    garageNumber: '12',
    ownerName: 'Иванов Иван',
    meterKind: 'water',
    accountingMonth: '2026-06-01',
    readingDate: '2026-06-20',
    currentValue: 15,
    previousValue: 10,
    consumption: 5,
    hasGapWarning: false,
    comment: null,
    isCanceled: false,
    ...overrides,
  }
}

function createMissingMeterReading(overrides: Partial<MissingMeterReadingDto>): MissingMeterReadingDto {
  return {
    garageId: 'garage-1',
    garageNumber: '12',
    ownerName: 'Иванов Иван',
    meterKind: 'electricity',
    accountingMonth: '2026-06-01',
    ...overrides,
  }
}
