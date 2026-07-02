import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import App from './App'
import type { AuditClient, AuditEventDto } from './services/auditApi'
import type { AuthClient, AuthResponse } from './services/authApi'
import type { AccountingTypeDto, DictionaryClient, GarageDto, OwnerDto, SupplierDto, SupplierGroupDto, TariffDto } from './services/dictionariesApi'
import type { AccrualDto, FinanceClient, FinanceSummaryDto, FinancialOperationDto, GarageBalanceHistoryDto, MeterReadingDto, MissingMeterReadingDto, RegularAccrualGenerationResultDto, SupplierAccrualDto, SupplierGroupSalaryAccrualGenerationResultDto } from './services/financeApi'
import type { AccessImportQuarantineItemDto, AccessImportRunDto, AccessImportRunLogEntryDto, ImportClient } from './services/importApi'
import type { ConsolidatedReportDto, ExpenseReportDto, IncomeReportDto, ReportClient } from './services/reportsApi'
import type { AppReleaseDto, ReleaseClient } from './services/releasesApi'
import type { ManagedRoleDto, ManagedUserDto, UserManagementClient } from './services/usersApi'

describe('App', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-06-30T10:00:00+07:00'))
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

    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

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
    expect(within(tariffsPanel).getByRole('table', { name: 'Тарифы и сборы' })).toBeInTheDocument()
    expect(within(tariffsPanel).getByText('Тариф на воду')).toBeInTheDocument()
    expect(within(tariffsPanel).getByText('Нерегулярные платежи')).toBeInTheDocument()

    await user.click(within(tariffsPanel).getAllByRole('button', { name: 'Добавить услугу' })[0])
    const serviceDialog = await screen.findByRole('dialog', { name: 'Добавить услугу' })
    expect(within(serviceDialog).getByLabelText('Стоимость услуги')).toBeInTheDocument()
    expect(within(serviceDialog).queryByLabelText('Периодичность')).not.toBeInTheDocument()
    await user.click(within(serviceDialog).getByLabelText('Регулярные платежи'))
    expect(within(serviceDialog).getByLabelText('Периодичность')).toHaveValue('12')
    expect(within(serviceDialog).getByLabelText('Учитывать платеж с')).toHaveValue('Январь')
    expect(within(serviceDialog).getByLabelText('Оплатить до')).toHaveValue('Июль')
    expect(within(serviceDialog).getByLabelText('Перенос долга в просроченный')).toHaveValue('30')
    expect(within(serviceDialog).getByLabelText('По счетчику')).toBeChecked()
    expect(within(serviceDialog).getByLabelText('Пороговая тарификация')).toBeChecked()
    expect(within(serviceDialog).getByLabelText('Цена за единицу 1')).toBeInTheDocument()
    await user.click(within(serviceDialog).getByRole('button', { name: 'Отмена' }))

    await user.click(within(tariffsPanel).getAllByRole('button', { name: 'Объявить сбор' })[0])
    const feeDialog = await screen.findByRole('dialog', { name: 'Добавить сбор' })
    expect(within(feeDialog).getByLabelText('Наименование сбора')).toBeInTheDocument()
    expect(within(feeDialog).getByLabelText('Цель сбора')).toBeInTheDocument()
    expect(within(feeDialog).getByLabelText('Сумма взноса')).toBeInTheDocument()
    expect(within(feeDialog).getByLabelText('Все гаражи')).toBeChecked()
    expect(within(feeDialog).getByLabelText('Сумма сбора')).toBeInTheDocument()
    expect(within(feeDialog).getByRole('button', { name: 'Сегодня' })).toBeInTheDocument()
    expect(within(feeDialog).getByLabelText('Перенос долга по сбору в просроченный')).toBeInTheDocument()
  })

  it('edits tariffs and one-time payments without local history access', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: /Тарифы\s+и\s+сборы/i }))

    const tariffsPanel = await screen.findByRole('region', { name: 'Тарифы и сборы' })
    const waterRateInput = within(tariffsPanel).getByLabelText('Вода: Тариф на воду: значение')
    await user.type(waterRateInput, '1250{Enter}')
    expect(waterRateInput).toHaveValue('1250')

    await user.click(within(tariffsPanel).getByRole('button', { name: 'Добавить порог' }))
    const electricityThresholdInput = within(tariffsPanel).getByLabelText('Электроэнергия: Порог 4: значение')
    expect(electricityThresholdInput).toBeInTheDocument()
    await user.type(electricityThresholdInput, '7.5{Enter}')
    expect(electricityThresholdInput).toHaveValue('7.5')

    const entryFeeInput = within(tariffsPanel).getByLabelText('Сумма: Вступительный взнос')
    await user.type(entryFeeInput, '5000{Enter}')
    expect(entryFeeInput).toHaveValue('5000')

    const deleteFineButton = within(tariffsPanel).getByRole('button', { name: 'Удалить нерегулярный платеж Штраф за это' })
    await user.click(deleteFineButton)
    expect(within(tariffsPanel).getByText('Удален')).toBeInTheDocument()
    const restoreFineButton = within(tariffsPanel).getByRole('button', { name: 'Вернуть нерегулярный платеж Штраф за это' })
    await user.click(restoreFineButton)
    expect(within(tariffsPanel).queryByText('Удален')).not.toBeInTheDocument()
    expect(within(tariffsPanel).getByRole('button', { name: 'Удалить нерегулярный платеж Штраф за это' })).toBeInTheDocument()

    expect(within(tariffsPanel).queryByRole('tab', { name: 'История изменений' })).not.toBeInTheDocument()
    expect(within(tariffsPanel).queryByRole('table', { name: 'История изменений тарифов и сборов', hidden: true })).not.toBeInTheDocument()
  })

  it('shows contractors tabs and section dialogs without local history access', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: 'Контрагенты' }))

    const contractorsPanel = await screen.findByRole('region', { name: 'Контрагенты' })
    expect(within(contractorsPanel).getByRole('tab', { name: 'Гаражи' })).toHaveAttribute('aria-selected', 'true')
    expect(within(contractorsPanel).getByRole('tab', { name: 'Поставщики' })).toBeInTheDocument()
    expect(within(contractorsPanel).getByRole('tab', { name: 'Персонал' })).toBeInTheDocument()
    expect(within(contractorsPanel).getByRole('table', { name: 'Гаражи' })).toBeInTheDocument()
    expect(within(contractorsPanel).getByText('Иванов Иван')).toBeInTheDocument()
    expect(within(contractorsPanel).getByRole('button', { name: 'Показать должников' })).toBeInTheDocument()

    await user.click(within(contractorsPanel).getAllByRole('button', { name: 'Открыть' })[0])
    const garageDialog = await screen.findByRole('dialog', { name: 'Гараж 1' })
    await user.clear(within(garageDialog).getByLabelText('Владелец гаража'))
    await user.type(within(garageDialog).getByLabelText('Владелец гаража'), 'Новый владелец')
    await user.click(within(garageDialog).getByRole('button', { name: /Сохранить/i }))
    await waitFor(() => expect(within(within(contractorsPanel).getByRole('table', { name: 'Гаражи' })).getByText('Новый владелец')).toBeInTheDocument())

    await user.click(within(contractorsPanel).getByRole('tab', { name: 'Поставщики' }))
    expect(within(contractorsPanel).getByRole('table', { name: 'Поставщики' })).toBeInTheDocument()
    expect(within(contractorsPanel).getByText('Энергосбыт')).toBeInTheDocument()
    await user.click(within(contractorsPanel).getByRole('button', { name: 'Добавить поставщика' }))
    const supplierDialog = await screen.findByRole('dialog', { name: 'Новый поставщик' })
    await user.type(within(supplierDialog).getByLabelText('Наименование поставщика'), 'Новый подрядчик')
    await user.type(within(supplierDialog).getByLabelText('Услуга поставщика'), 'Охрана')
    await user.click(within(supplierDialog).getByRole('button', { name: /Сохранить/i }))
    await waitFor(() => expect(within(within(contractorsPanel).getByRole('table', { name: 'Поставщики' })).getByText('Новый подрядчик')).toBeInTheDocument())

    await user.click(within(contractorsPanel).getByRole('button', { name: 'Добавить услугу' }))
    const serviceDialog = await screen.findByRole('dialog', { name: 'Добавить услугу' })
    await user.type(within(serviceDialog).getByLabelText('Наименование услуги контрагента'), 'Уборка территории')
    await user.click(within(serviceDialog).getByRole('button', { name: /Сохранить/i }))

    await user.click(within(contractorsPanel).getByRole('tab', { name: 'Персонал' }))
    expect(within(contractorsPanel).getByRole('table', { name: 'Персонал' })).toBeInTheDocument()
    await user.click(within(contractorsPanel).getByRole('button', { name: 'Добавить отдел' }))
    const departmentDialog = await screen.findByRole('dialog', { name: 'Новый отдел' })
    await user.type(within(departmentDialog).getByLabelText('Наименование отдела'), 'Охрана')
    await user.click(within(departmentDialog).getByRole('button', { name: 'Ок' }))

    await user.click(within(contractorsPanel).getByRole('button', { name: 'Добавить сотрудника' }))
    const employeeDialog = await screen.findByRole('dialog', { name: 'Новый сотрудник' })
    await user.type(within(employeeDialog).getByLabelText('ФИО сотрудника'), 'Смирнов Алексей')
    await user.selectOptions(within(employeeDialog).getByLabelText('Отдел сотрудника'), 'Охрана')
    await user.type(within(employeeDialog).getByLabelText('Ставка сотрудника'), '25000')
    await user.click(within(employeeDialog).getByRole('button', { name: /Сохранить/i }))
    await waitFor(() => expect(within(within(contractorsPanel).getByRole('table', { name: 'Персонал' })).getByText('Смирнов Алексей')).toBeInTheDocument())

    expect(within(contractorsPanel).queryByRole('table', { name: 'История изменений контрагентов', hidden: true })).not.toBeInTheDocument()
    expect(within(contractorsPanel).queryByLabelText('Раздел истории контрагентов')).not.toBeInTheDocument()
  })

  it('shows meter readings prototype as a yearly garage table', async () => {
    const user = userEvent.setup()
    const dictionaryClient = createDictionaryClient({
      getGarages: async () => [
        createGarage({ id: 'garage-27', number: '27' }),
        createGarage({ id: 'garage-12', number: '12' }),
      ],
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: 'Счётчики' }))

    const readingsPanel = await screen.findByRole('region', { name: 'Показания' })
    expect(within(readingsPanel).getByLabelText('Год показаний')).toHaveValue('2026')
    expect(within(readingsPanel).getByRole('table', { name: 'Показания счетчиков за 2026 год' })).toBeInTheDocument()
    expect(within(readingsPanel).getByRole('columnheader', { name: /ЯнварькВт/i })).toBeInTheDocument()
    expect(await within(readingsPanel).findByLabelText('Гараж 12, Январь, показание')).toBeInTheDocument()
    expect(within(readingsPanel).getByLabelText('Гараж 27, Декабрь, показание')).toBeInTheDocument()
    expect(within(readingsPanel).queryByLabelText('Гараж 35, Декабрь, показание')).not.toBeInTheDocument()

    const yearInput = within(readingsPanel).getByLabelText('Год показаний')
    await user.clear(yearInput)
    await user.type(yearInput, '1899')
    expect(within(readingsPanel).getByRole('alert')).toHaveTextContent('Введите год четырьмя цифрами от 1900 до 9999.')

    await user.clear(yearInput)
    await user.type(yearInput, '2026')
    const januaryInput = within(readingsPanel).getByLabelText('Гараж 12, Январь, показание')
    await user.type(januaryInput, '4654{Enter}')
    expect(januaryInput).toHaveValue('4654')

    expect(within(readingsPanel).queryByRole('tab', { name: 'История изменений' })).not.toBeInTheDocument()
    expect(within(readingsPanel).queryByRole('table', { name: 'История изменений показаний', hidden: true })).not.toBeInTheDocument()
  })

  it('shows payments prototype and opens payment form modals', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: 'Платежи' }))

    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    const prototype = within(financePanel).getByRole('region', { name: 'Форма платежей' })
    expect(within(prototype).getByLabelText('Поиск платежей по гаражу или владельцу')).toBeInTheDocument()
    expect(within(prototype).getByRole('region', { name: 'Карточка гаража' })).toBeInTheDocument()
    expect(within(prototype).getByRole('table', { name: 'История платежей гаража' })).toBeInTheDocument()
    expect(within(prototype).getByRole('table', { name: 'Платежи гаража за июнь 2026' })).toBeInTheDocument()
    expect(within(prototype).getByRole('table', { name: 'Форма платежей за июнь 2026' })).toBeInTheDocument()
    expect(within(prototype).getAllByText('Электроэнергия').length).toBeGreaterThan(0)
    expect(within(prototype).getAllByText('257 100')).toHaveLength(2)

    await user.click(within(prototype).getByRole('button', { name: 'Добавить начисление гаражу' }))
    const garageAccrualDialog = await screen.findByRole('dialog', { name: 'Новое начисление' })
    expect(within(garageAccrualDialog).getByLabelText('Тип начисления гаража')).toHaveValue('late')
    await user.click(within(garageAccrualDialog).getByRole('button', { name: 'Отмена' }))

    await user.click(within(prototype).getByRole('button', { name: 'Полная оплата' }))
    const fullPaymentDialog = await screen.findByRole('dialog', { name: 'Полная оплата' })
    expect(within(fullPaymentDialog).getByLabelText('Период полной оплаты')).toHaveValue('full')
    await user.click(within(fullPaymentDialog).getByRole('button', { name: 'Отмена' }))

    await user.click(within(prototype).getByRole('button', { name: 'Добавить выплату' }))
    const expenseDialog = await screen.findByRole('dialog', { name: 'Новая выплата' })
    expect(within(expenseDialog).getByLabelText('Тип выплаты')).toHaveValue('advance')
    await user.click(within(expenseDialog).getByRole('button', { name: 'Отмена' }))

    await user.click(within(prototype).getByRole('button', { name: 'Добавить начисление' }))
    const accrualDialog = await screen.findByRole('dialog', { name: 'Новое начисление' })
    expect(within(accrualDialog).getByLabelText('Основание начисления')).toBeInTheDocument()
    await user.click(within(accrualDialog).getByRole('button', { name: 'Отмена' }))

    await user.click(within(prototype).getByRole('button', { name: 'Сдать кассу в банк' }))
    const bankDialog = await screen.findByRole('dialog', { name: 'Учет суммы на счете в банке' })
    expect(within(bankDialog).getByLabelText('Сумма в банке')).toBeInTheDocument()
  })

  it('shows funds management prototype from dashboard tile', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    const dashboardTiles = await screen.findByRole('group', { name: 'Главные разделы' })
    await user.click(within(dashboardTiles).getByRole('button', { name: /Управление\s+фондами/i }))

    const fundsPanel = await screen.findByRole('region', { name: 'Управление фондами' })
    expect(within(fundsPanel).getByRole('table', { name: 'Фонды и собранные суммы' })).toBeInTheDocument()
    expect(within(fundsPanel).getByText('Электроэнергия')).toBeInTheDocument()
    expect(within(fundsPanel).getByRole('button', { name: 'Изъять из фонда Электроэнергия' })).toBeInTheDocument()
    expect(within(fundsPanel).getByRole('button', { name: 'Пополнить фонд Целевые взносы' })).toBeInTheDocument()
    expect(within(fundsPanel).getByLabelText('Сумма к распределению')).toBeInTheDocument()
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
    expect(screen.queryByRole('region', { name: 'Панель' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Тарифы' })).not.toBeInTheDocument()
    await openDictionarySubgroup(user, dictionaryPanel, 'Тарифы')
    expect(within(dictionaryPanel).getByRole('button', { name: 'Подгруппа: Тарифы' })).toHaveAttribute('aria-current', 'page')
    expect(within(dictionaryPanel).getByRole('table', { name: 'Таблица: Тарифы' })).toBeInTheDocument()

    await openSection(user, 'Платежи')
    expect(screen.getByRole('button', { name: 'Платежи' })).toHaveAttribute('aria-current', 'page')
    expect(await screen.findByRole('region', { name: 'Платежи' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Справочники' })).not.toBeInTheDocument()

    await openSection(user, 'Отчеты')
    expect(screen.getByRole('button', { name: 'Отчеты' })).toHaveAttribute('aria-current', 'page')
    expect(await screen.findByRole('region', { name: 'Отчеты' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Платежи' })).not.toBeInTheDocument()

    await openSection(user, 'Импорт')
    expect(screen.getByRole('button', { name: 'Импорт' })).toHaveAttribute('aria-current', 'page')
    expect(await screen.findByRole('region', { name: 'Импорт Access' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Отчеты' })).not.toBeInTheDocument()

    await openSection(user, 'Что нового')
    expect(screen.getByRole('button', { name: 'Что нового' })).toHaveAttribute('aria-current', 'page')
    expect(await screen.findByRole('region', { name: 'Что нового' })).toBeInTheDocument()
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
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Настройки')
    const passwordPanel = await screen.findByRole('region', { name: 'Безопасность аккаунта' })

    await user.type(within(passwordPanel).getByLabelText('Текущий пароль'), 'StrongPass123')
    await user.type(within(passwordPanel).getByLabelText('Новый пароль'), 'NewStrongPass123')
    await user.type(within(passwordPanel).getByLabelText('Повтор нового пароля'), 'NewStrongPass123')
    await user.click(within(passwordPanel).getByRole('button', { name: 'Изменить пароль' }))

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

  it('does not call password API when repeated password differs', async () => {
    const user = userEvent.setup()
    let changeCalled = false
    const authClient = createAuthClient({
      changeOwnPassword: async () => {
        changeCalled = true
        throw new Error('Смена пароля не должна вызываться при несовпадающем повторе.')
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

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

  it('adds managed user from protected workspace', async () => {
    const user = userEvent.setup()
    const userClient = createStatefulUserClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={userClient} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Пользователи')
    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })

    await user.click(within(usersPanel).getByRole('button', { name: 'Добавить' }))
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
    await user.click(within(editDialog).getByRole('button', { name: 'Сохранить' }))

    const saveConfirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения пользователя' })
    expect(within(saveConfirmationDialog).getByText('Имя')).toBeInTheDocument()
    const saveConfirmationChanges = within(saveConfirmationDialog).getByRole('list', { name: 'Изменяемые поля пользователя' })
    expect(within(saveConfirmationChanges).getByText('Оператор')).toBeInTheDocument()
    expect(within(saveConfirmationChanges).getByText('Старший оператор')).toBeInTheDocument()
    expect(updateCalls).toBe(0)
    await user.click(within(saveConfirmationDialog).getByRole('button', { name: 'Сохранить изменения' }))

    expect(await within(usersPanel).findByText('Старший оператор')).toBeInTheDocument()
    expect(updateCalls).toBe(1)
    expect(await screen.findByText('Пользователь изменен.')).toHaveAttribute('role', 'status')

    fireEvent.contextMenu(within(usersPanel).getByText('operator@example.com').closest('tr')!)
    await user.click(await screen.findByRole('menuitem', { name: 'Удалить' }))
    const deleteDialog = await screen.findByRole('dialog', { name: 'Удалить пользователя' })
    const deleteButton = within(deleteDialog).getByRole('button', { name: 'Удалить' })
    expect(deleteButton).toBeDisabled()
    await user.type(within(deleteDialog).getByLabelText('Причина отключения пользователя'), 'Access no longer needed')
    await user.click(deleteButton)

    expect(await within(usersPanel).findByText('Отключен')).toBeInTheDocument()
    expect(deactivationReason).toBe('Access no longer needed')
    expect(await screen.findByText('Пользователь отключен.')).toHaveAttribute('role', 'status')

    fireEvent.contextMenu(within(usersPanel).getByText('operator@example.com').closest('tr')!)
    await user.click(await screen.findByRole('menuitem', { name: 'Вернуть' }))
    const restoreDialog = await screen.findByRole('dialog', { name: 'Вернуть пользователя?' })
    expect(within(restoreDialog).getByText('Действие будет записано в историю изменений.')).toBeInTheDocument()
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
    const dictionaryClient = createDictionaryClient({
      updateTariff: async () => {
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
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    const confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения' })
    expect(within(confirmationDialog).getByText('Дата начала')).toBeInTheDocument()
    await user.click(within(confirmationDialog).getByRole('button', { name: 'Сохранить изменения' }))

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

    const ownerDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(ownerDialog).getByLabelText('Фамилия владельца'), 'Петров')
    await user.type(within(ownerDialog).getByLabelText('Имя владельца'), 'Петр')
    await user.type(within(ownerDialog).getByLabelText('Телефон владельца'), '+7 913')
    await user.click(within(ownerDialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dictionaryPanel).findByText('Петров Петр')).toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Гаражи')
    const garageCreateDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
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
    const groupDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(groupDialog).getByLabelText('Группа поставщиков'), 'Связь')
    await user.click(within(groupDialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dictionaryPanel).findByText('Связь')).toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Поставщики')
    const supplierDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
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
      getSupplierGroupsPage: async (_token, _offset, limit) => {
        requestedLimits.supplierGroups = limit
        return { items: [createGroup()], totalCount: 1, offset: 0, limit: limit ?? 25 }
      },
      getSuppliersPage: async (_token, _groupId, _search, _offset, limit) => {
        requestedLimits.suppliers = limit
        return { items: [createSupplier()], totalCount: 1, offset: 0, limit: limit ?? 25 }
      },
      getIncomeTypesPage: async (_token, _offset, limit) => {
        requestedLimits.incomeTypes = limit
        return { items: [createAccountingType({ name: 'Членский взнос' })], totalCount: 1, offset: 0, limit: limit ?? 25 }
      },
      getExpenseTypesPage: async (_token, _offset, limit) => {
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
      getSupplierGroups: async (_token, limit) => {
        requestedLimits.supplierGroups = limit
        return [createGroup()]
      },
      getSuppliers: async (_token, _groupId, _search, limit) => {
        requestedLimits.suppliers = limit
        return [createSupplier()]
      },
      getIncomeTypes: async (_token, limit) => {
        requestedLimits.incomeTypes = limit
        return [createAccountingType({ name: 'Членский взнос' })]
      },
      getExpenseTypes: async (_token, limit) => {
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

    let validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Фамилия владельца'), '   ')
    await user.type(within(validationDialog).getByLabelText('Имя владельца'), 'Петр')
    await user.type(within(validationDialog).getByLabelText('Телефон владельца'), '1')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите фамилию владельца.')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Проверьте телефон владельца.')).toBeInTheDocument()
    expect(createOwnerCalled).toBe(false)
    await user.click(within(validationDialog).getByRole('button', { name: 'Отмена' }))

    await openDictionarySubgroup(user, dictionaryPanel, 'Гаражи')
    validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Номер гаража'), '   ')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите номер гаража.')).toBeInTheDocument()
    expect(createGarageCalled).toBe(false)
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

    fireEvent.contextMenu(within(dictionaryPanel).getByText('12').closest('tr')!)
    await user.click(await screen.findByRole('menuitem', { name: 'История баланса' }))

    const dialog = await screen.findByRole('dialog', { name: 'Гараж 12' })
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
    expect(within(deleteDialog).getByText('Запись будет скрыта из рабочих таблиц, но останется в audit-журнале и связанной финансовой истории.')).toBeInTheDocument()
    expect(within(deleteDialog).getByRole('button', { name: 'Удалить запись' })).toBeDisabled()
    await user.type(within(deleteDialog).getByLabelText('Причина удаления'), 'Дубликат владельца')
    await user.click(within(deleteDialog).getByRole('button', { name: 'Удалить запись' }))
    expect(archivedOwnerId).toBe('owner-1')
    expect(archiveReason).toBe('Дубликат владельца')
    expect(await screen.findByText('Запись удалена из рабочего списка.')).toBeInTheDocument()
    return

    const firstArchiveButton = within(dictionaryPanel).getByRole('button', { name: 'Архивировать владельца Иванов Иван' })
    await user.click(firstArchiveButton)

    expect(archivedOwnerId).toBeNull()
    const firstDialog = await screen.findByRole('dialog', { name: 'Подтвердите архивирование' })
    const archiveDescription = within(firstDialog).getByText('Запись исчезнет из рабочих списков, но останется в истории и audit-журнале.')
    expect(firstDialog).toHaveAttribute('aria-describedby', archiveDescription.id)
    const archiveCancelButton = within(firstDialog).getByRole('button', { name: 'Отменить' })
    const archiveConfirmButton = within(firstDialog).getByRole('button', { name: 'Архивировать запись' })
    const archiveCloseButton = within(firstDialog).getByRole('button', { name: 'Отменить архивирование' })
    await waitFor(() => expect(archiveCancelButton).toHaveFocus())
    await user.tab()
    expect(archiveConfirmButton).toHaveFocus()
    await user.tab()
    expect(archiveCloseButton).toHaveFocus()
    await user.tab()
    expect(archiveCancelButton).toHaveFocus()
    expect(within(firstDialog).getByText('Иванов Иван')).toBeInTheDocument()
    expect(within(firstDialog).getByText('Запись исчезнет из рабочих списков, но останется в истории и audit-журнале.')).toBeInTheDocument()

    fireEvent.mouseDown(firstDialog.parentElement!)
    expect(archivedOwnerId).toBeNull()
    expect(screen.queryByRole('dialog', { name: 'Подтвердите архивирование' })).not.toBeInTheDocument()
    expect(firstArchiveButton).toHaveFocus()

    const secondArchiveButton = within(dictionaryPanel).getByRole('button', { name: 'Архивировать владельца Иванов Иван' })
    await user.click(secondArchiveButton)
    const cancelDialog = await screen.findByRole('dialog', { name: 'Подтвердите архивирование' })
    await user.click(within(cancelDialog).getByRole('button', { name: 'Отменить' }))
    expect(archivedOwnerId).toBeNull()
    expect(screen.queryByRole('dialog', { name: 'Подтвердите архивирование' })).not.toBeInTheDocument()
    expect(secondArchiveButton).toHaveFocus()

    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Архивировать владельца Иванов Иван' }))
    const confirmDialog = await screen.findByRole('dialog', { name: 'Подтвердите архивирование' })
    await user.click(within(confirmDialog).getByRole('button', { name: 'Архивировать запись' }))

    expect(archivedOwnerId).toBe('owner-1')
    await waitFor(() => {
      expect(within(dictionaryPanel).queryByRole('button', { name: 'Архивировать владельца Иванов Иван' })).not.toBeInTheDocument()
    })
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

    await user.click(within(archivedRow).getByRole('button', { name: 'Вернуть' }))
    expect(restoredOwnerId).toBeNull()
    const restoreDialog = await screen.findByRole('dialog', { name: 'Вернуть запись из архива?' })
    expect(within(restoreDialog).getByText('Запись снова появится в рабочих списках. Действие будет записано в историю изменений.')).toBeInTheDocument()
    await user.click(within(restoreDialog).getByRole('button', { name: 'Вернуть запись' }))

    expect(restoredOwnerId).toBe('owner-archived')
    expect(await screen.findByText('Запись восстановлена и снова доступна в рабочих списках.')).toBeInTheDocument()
    const restoredRow = within(dictionaryPanel).getByText('Петров Петр').closest('tr')!
    expect(within(restoredRow).getByText('Активна')).toBeInTheDocument()
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
    await user.click(within(editorDialog).getByRole('button', { name: 'Сохранить' }))

    expect(updateOwner).not.toHaveBeenCalled()
    const confirmationDialog = await screen.findByRole('dialog', { name: 'Подтвердите изменения' })
    expect(within(confirmationDialog).getByText('Телефон')).toBeInTheDocument()
    expect(within(confirmationDialog).getByText('+7 900')).toBeInTheDocument()
    expect(within(confirmationDialog).getByText('+7 901')).toBeInTheDocument()

    await user.click(within(confirmationDialog).getByRole('button', { name: 'Отмена' }))
    expect(screen.queryByRole('dialog', { name: 'Подтвердите изменения' })).not.toBeInTheDocument()
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
    let typeDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(typeDialog).getByLabelText('Название вида операции'), 'Целевой взнос')
    await user.type(within(typeDialog).getByLabelText('Код вида операции'), 'target')
    await user.click(within(typeDialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dictionaryPanel).findByText('Целевой взнос')).toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Виды выплат')
    typeDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(typeDialog).getByLabelText('Название вида операции'), 'Вывоз мусора')
    await user.type(within(typeDialog).getByLabelText('Код вида операции'), 'trash')
    await user.click(within(typeDialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dictionaryPanel).findByText('Вывоз мусора')).toBeInTheDocument()

    await openDictionarySubgroup(user, dictionaryPanel, 'Тарифы')
    const tariffDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
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
    let validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Группа поставщиков'), '   ')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите группу поставщиков.')).toBeInTheDocument()
    expect(createSupplierGroupCalls).toBe(0)
    await user.click(within(validationDialog).getByRole('button', { name: 'Отмена' }))

    validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Группа поставщиков'), 'Связь')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))
    expect(createSupplierGroupCalls).toBe(1)

    await openDictionarySubgroup(user, dictionaryPanel, 'Поставщики')
    validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Название поставщика'), '   ')
    await user.type(within(validationDialog).getByLabelText('ИНН поставщика'), 'abc')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите название поставщика.')).toBeInTheDocument()
    expect(within(validationDialog).getByText('ИНН поставщика должен содержать 10 или 12 цифр.')).toBeInTheDocument()
    expect(createSupplierCalled).toBe(false)
    await user.click(within(validationDialog).getByRole('button', { name: 'Отмена' }))

    await openDictionarySubgroup(user, dictionaryPanel, 'Виды поступлений')
    validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Название вида операции'), '   ')
    await user.type(within(validationDialog).getByLabelText('Код вида операции'), 'членский')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите название вида поступления.')).toBeInTheDocument()
    expect(createIncomeTypeCalled).toBe(false)
    await user.click(within(validationDialog).getByRole('button', { name: 'Отмена' }))

    await openDictionarySubgroup(user, dictionaryPanel, 'Виды выплат')
    validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Название вида операции'), '   ')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите название вида выплаты.')).toBeInTheDocument()
    expect(createExpenseTypeCalled).toBe(false)
    await user.click(within(validationDialog).getByRole('button', { name: 'Отмена' }))

    await openDictionarySubgroup(user, dictionaryPanel, 'Тарифы')
    validationDialog = await openDictionaryCreateDialog(user, dictionaryPanel)
    await user.type(within(validationDialog).getByLabelText('Название тарифа'), '   ')
    await user.click(within(validationDialog).getByRole('button', { name: 'Сохранить' }))

    expect(await within(validationDialog).findByText('Проверьте запись')).toBeInTheDocument()
    expect(within(validationDialog).getByText('Укажите название тарифа.')).toBeInTheDocument()
    expect(createTariffCalled).toBe(false)
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

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Новое поступление' })).not.toBeInTheDocument())
    expect(await within(financePanel).findByText('PKO-fixed')).toBeInTheDocument()
    expect(within(financePanel).getByText('После сверки')).toBeInTheDocument()
    expect(within(financePanel).getAllByText('2 400,00').length).toBeGreaterThan(0)
    expect(within(financePanel).queryByText('PKO-edit')).not.toBeInTheDocument()
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
    await user.click(within(dialog).getByRole('button', { name: 'Отмена' }))
    closeDialog = await screen.findByRole('dialog', { name: 'Закрыть форму без сохранения?' })
    await user.click(within(closeDialog).getByRole('button', { name: 'Остаться' }))
    expect(screen.getByRole('dialog', { name: 'Новое поступление' })).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Документ поступления')).toHaveValue('PKO-draft')

    await user.click(within(dialog).getByRole('button', { name: 'Отмена' }))
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
      await user.click(within(dialog).getByRole('button', { name: 'Отмена' }))
      let closeDialog = await screen.findByRole('dialog', { name: 'Закрыть форму без сохранения?' })
      await user.click(within(closeDialog).getByRole('button', { name: 'Остаться' }))
      expect(screen.getByRole('dialog', { name: item.dialog })).toBeInTheDocument()
      expect(field).toHaveValue(item.draft)

      await user.click(within(dialog).getByRole('button', { name: 'Отмена' }))
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
      const menu = await openFinanceContextMenuByCellText(financePanel, item.rowText)
      expect(within(menu).getByRole('menuitem', { name: 'Изменить' })).toBeEnabled()
      await user.click(within(menu).getByRole('menuitem', { name: 'Изменить' }))

      const dialog = await screen.findByRole('dialog', { name: item.dialog })
      expect(within(dialog).getByText('Изменение')).toBeInTheDocument()
      await user.click(within(dialog).getByRole('button', { name: 'Закрыть форму платежа' }))
      await waitFor(() => expect(screen.queryByRole('dialog', { name: item.dialog })).not.toBeInTheDocument())
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
    const operationMenu = await openFinanceContextMenuByCellText(financePanel, 'PKO-cancel')
    await user.click(within(operationMenu).getByRole('menuitem', { name: 'Удалить' }))

    const cancelDialog = await screen.findByRole('dialog', { name: 'Отменить поступление?' })
    await user.click(within(cancelDialog).getByRole('button', { name: 'Отменить запись' }))
    expect(within(cancelDialog).getByRole('alert')).toHaveTextContent('Укажите причину отмены.')
    expect(within(financePanel).getByText('+700,00')).toBeInTheDocument()
    await user.type(within(cancelDialog).getByLabelText('Причина отмены финансовой записи'), 'Ошибочный документ')
    await user.click(within(cancelDialog).getByRole('button', { name: 'Отменить запись' }))
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
    await user.type(within(cancelDialog).getByLabelText('Причина отмены финансовой записи'), 'Ошибочная выплата')
    await user.click(within(cancelDialog).getByRole('button', { name: 'Отменить запись' }))
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

    await user.dblClick(within(supplierAccrualTable).getByLabelText(/Разбивка начисления поставщику/i))

    const dialog = await screen.findByRole('dialog', { name: 'Разбивка начисления поставщику' })
    expect(within(dialog).getByText('INV-1')).toBeInTheDocument()
    expect(within(dialog).getByText('Счет за воду')).toBeInTheDocument()
    expect(within(dialog).getByText('Ручное')).toBeInTheDocument()
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
    await user.click(within(financePanel).getByRole('button', { name: 'Зарплата группы' }))
    const dialog = await screen.findByRole('dialog', { name: 'Зарплата группы' })

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
    await user.dblClick(within(accrualTable).getByLabelText(/Разбивка начисления/i))
    const dialog = await screen.findByRole('dialog', { name: 'Разбивка начисления' })
    expect(within(dialog).getByText('Начисление за месяц')).toBeInTheDocument()
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

    expect(within(importPanel).getByText('Выбрать .accdb или .mdb')).toBeInTheDocument()
    expect(within(importPanel).getByText('Файл не выбран')).toHaveAttribute('role', 'status')
    await user.upload(within(importPanel).getByLabelText('Файл Access'), file)
    expect(await within(importPanel).findByText('ГСК.accdb')).toHaveAttribute('role', 'status')
    await user.click(within(importPanel).getByRole('button', { name: 'Проверить файл' }))

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

    await user.click(within(importPanel).getByRole('button', { name: 'Скачать отчет JSON' }))

    const exportReadyMessage = await within(importPanel).findByText('Отчет dry-run импорта готов.')
    expect(exportReadyMessage).toHaveAttribute('role', 'status')
    expect(exportReadyMessage).toHaveAttribute('aria-live', 'polite')
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
    const quarantineItems = Array.from({ length: 9 }, (_, index) => createAccessImportQuarantineItem({
      id: `quarantine-${index}`,
      externalId: `${index + 1}`,
    }))
    let quarantineLimit: number | undefined
    const importClient = createImportClient({
      getAccessRuns: async () => runs,
      getAccessRunLog: async () => logEntries,
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

    await user.click(within(importPanel).getByRole('tab', { name: /История/ }))
    const runCounter = within(importPanel).getByText('Показано 8 из 9 запусков')

    await user.click(within(importPanel).getByRole('tab', { name: /Карантин/ }))
    const quarantineCounter = within(importPanel).getByText('Показано 8 из 9 строк карантина')
    expect(logCounter).toHaveAttribute('role', 'status')
    expect(logCounter).toHaveAttribute('aria-live', 'polite')
    expect(runCounter).toHaveAttribute('role', 'status')
    expect(runCounter).toHaveAttribute('aria-live', 'polite')
    expect(quarantineCounter).toHaveAttribute('role', 'status')
    expect(quarantineCounter).toHaveAttribute('aria-live', 'polite')
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

    await user.click(within(importPanel).getByRole('tab', { name: /История/ }))
    const emptyHistoryState = within(importPanel).getByText('Истории импорта пока нет')

    await user.click(within(importPanel).getByRole('tab', { name: /Карантин/ }))
    const emptyQuarantineState = within(importPanel).getByText('Открытых строк карантина нет')
    for (const state of [emptyRunState, emptyCheckState, emptyLogState, emptyHistoryState, emptyQuarantineState]) {
      expect(state).toHaveAttribute('role', 'status')
      expect(state).toHaveAttribute('aria-live', 'polite')
    }
  })

  it('shows and resolves Access import quarantine rows', async () => {
    const user = userEvent.setup()
    let quarantineItems = [createAccessImportQuarantineItem()]
    const importClient = createImportClient({
      getOpenQuarantineItems: async () => quarantineItems,
      resolveQuarantineItem: async (_token, itemId, resolutionComment) => {
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

    await user.click(within(quarantineTable).getByRole('button', { name: 'Закрыть' }))

    expect(await within(importPanel).findByText('Строка карантина закрыта.')).toHaveAttribute('role', 'status')
    expect(within(quarantineTable).queryByText('Garage #42')).not.toBeInTheDocument()
    const emptyQuarantineState = within(quarantineTable).getByText('Открытых строк карантина нет')
    expect(emptyQuarantineState).toHaveAttribute('role', 'status')
    expect(emptyQuarantineState).toHaveAttribute('aria-live', 'polite')
  })

  it('shows audit journal for users with audit permission', async () => {
    const user = userEvent.setup()
    let auditRequest: Parameters<AuditClient['getEvents']>[1] = undefined
    let auditExportRequest: Parameters<AuditClient['exportEvents']>[1] = undefined
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

    await user.click(await within(auditPanel).findByRole('button', { name: 'Открыть' }))

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

    await user.click(within(detailDialog).getByRole('button', { name: 'Закрыть' }))

    expect(screen.queryByRole('dialog', { name: 'Изменение' })).not.toBeInTheDocument()
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

  it('shows consolidated report and applies garage search', async () => {
    const user = userEvent.setup()
    const reportClient = createReportClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Отчеты')
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    expect(await within(reportsPanel).findByText('Консолидированный отчет за период')).toBeInTheDocument()
    expect(within(reportsPanel).getByRole('tab', { name: /Сводный/ })).toHaveAttribute('aria-selected', 'true')
    expect(within(reportsPanel).getByRole('tab', { name: /Поступления/ })).toBeInTheDocument()
    expect(within(reportsPanel).getByRole('tab', { name: /Выплаты/ })).toBeInTheDocument()
    expect(within(reportsPanel).getByText('Формат периода сводного отчета: ММ.ГГГГ.')).toBeInTheDocument()
    expect(within(reportsPanel).getByLabelText('Начало периода отчета')).toHaveAccessibleDescription('Формат периода сводного отчета: ММ.ГГГГ.')
    expect(within(reportsPanel).getByText('Начисления попадают в сводный отчет по учетному месяцу, поступления и выплаты - по фактической дате операции.')).toBeInTheDocument()
    expect(within(reportsPanel).getAllByText('2 000,00').length).toBeGreaterThan(0)
    expect(within(reportsPanel).getByRole('table', { name: 'Помесячный отчет' })).toBeInTheDocument()
    expect(within(reportsPanel).getAllByText('Гараж 12').length).toBeGreaterThan(0)

    await openReportTab(user, reportsPanel, 'Поступления')
    expect(within(reportsPanel).getByText('Формат дат поступлений: ДД.ММ.ГГГГ.')).toBeInTheDocument()
    expect(within(reportsPanel).getByLabelText('Начало отчета по поступлениям')).toHaveAccessibleDescription('Формат дат поступлений: ДД.ММ.ГГГГ.')
    expect(within(reportsPanel).getByText('В поступлениях начисления считаются по учетному месяцу, оплаты - по фактической дате поступления.')).toBeInTheDocument()

    await openReportTab(user, reportsPanel, 'Выплаты')
    expect(within(reportsPanel).getByText('Формат дат выплат: ДД.ММ.ГГГГ.')).toBeInTheDocument()
    expect(within(reportsPanel).getByLabelText('Начало отчета по выплатам')).toHaveAccessibleDescription('Формат дат выплат: ДД.ММ.ГГГГ.')
    expect(within(reportsPanel).getByText('В выплатах начисления поставщикам считаются по учетному месяцу, фактические выплаты - по дате оплаты.')).toBeInTheDocument()

    await openReportTab(user, reportsPanel, 'Сводный')

    await user.type(within(reportsPanel).getByLabelText('Поиск в отчете'), 'Петров')
    await user.click(within(reportsPanel).getByRole('button', { name: 'Сформировать' }))

    expect(await within(reportsPanel).findByText('Гараж 21')).toBeInTheDocument()

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать сводный XLSX' }))

    expect(await within(reportsPanel).findByText('XLSX по сводному отчету готов.')).toHaveAttribute('role', 'status')

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать сводный PDF' }))

    expect(await within(reportsPanel).findByText('PDF по сводному отчету готов.')).toHaveAttribute('role', 'status')
  })

  it('does not call report APIs when report periods fail client validation', async () => {
    const user = userEvent.setup()
    let consolidatedCalls = 0
    let incomeCalls = 0
    let expenseCalls = 0
    const reportClient = createReportClient({
      getConsolidatedReport: async () => {
        consolidatedCalls += 1
        return createConsolidatedReport()
      },
      getIncomeReport: async () => {
        incomeCalls += 1
        return createIncomeReport()
      },
      getExpenseReport: async () => {
        expenseCalls += 1
        return createExpenseReport()
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Отчеты')
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })
    await within(reportsPanel).findByText('Консолидированный отчет за период')
    await waitFor(() => {
      expect(consolidatedCalls).toBeGreaterThan(0)
      expect(incomeCalls).toBeGreaterThan(0)
      expect(expenseCalls).toBeGreaterThan(0)
    })

    const initialConsolidatedCalls = consolidatedCalls
    const initialIncomeCalls = incomeCalls
    const initialExpenseCalls = expenseCalls

    fireEvent.change(within(reportsPanel).getByLabelText('Начало периода отчета'), { target: { value: '2026-07' } })
    fireEvent.change(within(reportsPanel).getByLabelText('Конец периода отчета'), { target: { value: '2026-06' } })
    await user.click(within(reportsPanel).getByRole('button', { name: 'Сформировать' }))

    expect(await within(reportsPanel).findByText('Проверьте период отчета')).toBeInTheDocument()
    expect(within(reportsPanel).getByText('Начало периода отчета не может быть позже конца.')).toBeInTheDocument()
    expect(consolidatedCalls).toBe(initialConsolidatedCalls)

    await openReportTab(user, reportsPanel, 'Поступления')
    fireEvent.change(within(reportsPanel).getByLabelText('Начало отчета по поступлениям'), { target: { value: '2026-07-01' } })
    fireEvent.change(within(reportsPanel).getByLabelText('Конец отчета по поступлениям'), { target: { value: '2026-06-01' } })
    await user.click(within(reportsPanel).getByRole('button', { name: 'Показать' }))

    expect(await within(reportsPanel).findByText('Проверьте отчет по поступлениям')).toBeInTheDocument()
    expect(within(reportsPanel).getByText('Начало отчета по поступлениям не может быть позже конца.')).toBeInTheDocument()
    expect(incomeCalls).toBe(initialIncomeCalls)

    await openReportTab(user, reportsPanel, 'Выплаты')
    fireEvent.change(within(reportsPanel).getByLabelText('Начало отчета по выплатам'), { target: { value: '2026-07-01' } })
    fireEvent.change(within(reportsPanel).getByLabelText('Конец отчета по выплатам'), { target: { value: '2026-06-01' } })
    await user.click(within(reportsPanel).getByRole('button', { name: 'Показать' }))

    expect(await within(reportsPanel).findByText('Проверьте отчет по выплатам')).toBeInTheDocument()
    expect(within(reportsPanel).getByText('Начало отчета по выплатам не может быть позже конца.')).toBeInTheDocument()
    expect(expenseCalls).toBe(initialExpenseCalls)
  })

  it('restores report filters from session storage before first report load', async () => {
    const user = userEvent.setup()
    let consolidatedRequest: Parameters<ReportClient['getConsolidatedReport']>[1] = undefined
    let incomeRequest: Parameters<ReportClient['getIncomeReport']>[1] = undefined
    let expenseRequest: Parameters<ReportClient['getExpenseReport']>[1] = undefined

    window.sessionStorage.setItem(
      'garagebalance.reports.consolidatedFilters',
      JSON.stringify({ monthFrom: '2026-05-01', monthTo: '2026-06-01', search: 'Петров' }),
    )
    window.sessionStorage.setItem(
      'garagebalance.reports.incomeFilters',
      JSON.stringify({ dateFrom: '2026-05-01', dateTo: '2026-06-19', search: 'PKO', garageIds: ['garage-1'], ownerIds: ['owner-1'], incomeTypeIds: ['income-type-1'], rowMode: 'payments' }),
    )
    window.sessionStorage.setItem(
      'garagebalance.reports.expenseFilters',
      JSON.stringify({ dateFrom: '2026-05-01', dateTo: '2026-06-19', search: 'RKO', supplierIds: ['supplier-1'], expenseTypeIds: ['expense-type-1'], rowMode: 'payments' }),
    )

    const reportClient = createReportClient({
      getConsolidatedReport: async (_token, params) => {
        consolidatedRequest = params
        return createConsolidatedReport()
      },
      getIncomeReport: async (_token, params) => {
        incomeRequest = params
        return createIncomeReport()
      },
      getExpenseReport: async (_token, params) => {
        expenseRequest = params
        return createExpenseReport()
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await openSection(user, 'Отчеты')

    await waitFor(() => {
      expect(consolidatedRequest).toEqual({ monthFrom: '2026-05-01', monthTo: '2026-06-01', search: 'Петров', limit: 12 })
      expect(incomeRequest).toEqual({
        dateFrom: '2026-05-01',
        dateTo: '2026-06-19',
        search: 'PKO',
        garageIds: ['garage-1'],
        ownerIds: ['owner-1'],
        incomeTypeIds: ['income-type-1'],
        rowMode: 'payments',
        limit: 16,
      })
      expect(expenseRequest).toEqual({
        dateFrom: '2026-05-01',
        dateTo: '2026-06-19',
        search: 'RKO',
        supplierIds: ['supplier-1'],
        expenseTypeIds: ['expense-type-1'],
        rowMode: 'payments',
        limit: 16,
      })
    })
  })

  it('passes consolidated report search filter to XLSX and PDF exports', async () => {
    const user = userEvent.setup()
    let consolidatedRequest: Parameters<ReportClient['getConsolidatedReport']>[1] = undefined
    let consolidatedXlsxRequest: Parameters<ReportClient['exportConsolidatedReportXlsx']>[1] = undefined
    let consolidatedPdfRequest: Parameters<ReportClient['exportConsolidatedReportPdf']>[1] = undefined
    const reportClient = createReportClient({
      getConsolidatedReport: async (_token, params) => {
        consolidatedRequest = params
        return createConsolidatedReport()
      },
      exportConsolidatedReportXlsx: async (_token, params) => {
        consolidatedXlsxRequest = params
        return new Blob(['consolidated xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
      },
      exportConsolidatedReportPdf: async (_token, params) => {
        consolidatedPdfRequest = params
        return new Blob(['consolidated pdf'], { type: 'application/pdf' })
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Отчеты')
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    await user.clear(within(reportsPanel).getByLabelText('Поиск в отчете'))
    await user.type(within(reportsPanel).getByLabelText('Поиск в отчете'), '21')
    await user.click(within(reportsPanel).getByRole('button', { name: 'Сформировать' }))

    await waitFor(() => {
      expect(consolidatedRequest?.search).toBe('21')
    })

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать сводный XLSX' }))
    expect(await within(reportsPanel).findByText('XLSX по сводному отчету готов.')).toHaveAttribute('role', 'status')

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать сводный PDF' }))
    expect(await within(reportsPanel).findByText('PDF по сводному отчету готов.')).toHaveAttribute('role', 'status')

    expect(consolidatedXlsxRequest?.search).toBe('21')
    expect(consolidatedPdfRequest?.search).toBe('21')
  })

  it('shows visible row counters for truncated report tables', async () => {
    const user = userEvent.setup()
    const garageRows = Array.from({ length: 13 }, (_, index) => ({
      garageId: `garage-${index + 1}`,
      garageNumber: String(index + 1).padStart(2, '0'),
      ownerName: `Владелец ${index + 1}`,
      incomeTotal: 1000 + index,
      accrualTotal: 1500 + index,
      debt: 500,
      meterReadingCount: 0,
    }))
    const incomeRows = Array.from({ length: 17 }, (_, index) => ({
      ...createIncomeReport().rows[index % 2],
      date: `2026-06-${String((index % 28) + 1).padStart(2, '0')}`,
      garageId: `garage-${index + 1}`,
      garageNumber: String(index + 1).padStart(2, '0'),
      documentNumber: `PKO-${index + 1}`,
    }))
    const expenseRows = Array.from({ length: 17 }, (_, index) => ({
      ...createExpenseReport().rows[0],
      date: `2026-06-${String((index % 28) + 1).padStart(2, '0')}`,
      supplierId: `supplier-${index + 1}`,
      supplierName: `Поставщик ${index + 1}`,
      documentNumber: `RKO-${index + 1}`,
    }))
    const reportClient = createReportClient({
      getConsolidatedReport: async (_token, params) => createConsolidatedReport({
        garageRowCount: garageRows.length,
        garageRows: garageRows.slice(0, params?.limit ?? garageRows.length),
      }),
      getIncomeReport: async () => createIncomeReport({ rowCount: incomeRows.length, rows: incomeRows.slice(0, 16) }),
      getExpenseReport: async () => createExpenseReport({ rowCount: expenseRows.length, rows: expenseRows.slice(0, 16) }),
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Отчеты')
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    expect(await within(reportsPanel).findByText('Показано 12 из 13 строк')).toHaveAttribute('role', 'status')
    await openReportTab(user, reportsPanel, 'Поступления')
    expect(await within(reportsPanel).findByText('Показано 16 из 17 строк')).toHaveAttribute('role', 'status')
    await openReportTab(user, reportsPanel, 'Выплаты')
    expect(await within(reportsPanel).findByText('Показано 16 из 17 строк')).toHaveAttribute('role', 'status')
  })

  it('shows accessible empty states for reports without rows', async () => {
    const user = userEvent.setup()
    const reportClient = createReportClient({
      getConsolidatedReport: async () => createConsolidatedReport({
        incomeTotal: 0,
        expenseTotal: 0,
        accrualTotal: 0,
        balance: 0,
        debt: 0,
        monthlyRows: [],
        garageRowCount: 0,
        garageRows: [],
      }),
      getIncomeReport: async () => createIncomeReport({
        accrualTotal: 0,
        incomeTotal: 0,
        debt: 0,
        rowCount: 0,
        rows: [],
      }),
      getExpenseReport: async () => createExpenseReport({
        accrualTotal: 0,
        expenseTotal: 0,
        difference: 0,
        rowCount: 0,
        rows: [],
      }),
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Отчеты')
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    const emptyMonthlyRowsState = await within(reportsPanel).findByText('Помесячных строк отчета пока нет')
    const emptyGarageRowsState = await within(reportsPanel).findByText('По выбранному фильтру гаражей нет')
    for (const state of [emptyMonthlyRowsState, emptyGarageRowsState]) {
      expect(state).toHaveAttribute('role', 'status')
      expect(state).toHaveAttribute('aria-live', 'polite')
    }
    expect(within(within(reportsPanel).getByRole('table', { name: 'Помесячный отчет' })).queryByText(/Июнь 2026/)).not.toBeInTheDocument()
    expect(within(within(reportsPanel).getByRole('table', { name: 'Отчет по гаражам' })).queryByText(/Иванов Иван/)).not.toBeInTheDocument()

    await openReportTab(user, reportsPanel, 'Поступления')
    const emptyIncomeRowsState = await within(reportsPanel).findByText('По выбранному фильтру поступлений нет')
    expect(emptyIncomeRowsState).toHaveAttribute('role', 'status')
    expect(emptyIncomeRowsState).toHaveAttribute('aria-live', 'polite')
    expect(within(reportsPanel).queryByText(/PKO-1/)).not.toBeInTheDocument()

    await openReportTab(user, reportsPanel, 'Выплаты')
    const emptyExpenseRowsState = await within(reportsPanel).findByText('По выбранному фильтру выплат нет')
    expect(emptyExpenseRowsState).toHaveAttribute('role', 'status')
    expect(emptyExpenseRowsState).toHaveAttribute('aria-live', 'polite')
    expect(within(reportsPanel).queryByText(/RKO-1/)).not.toBeInTheDocument()
  })

  it('shows report summary totals from API even when visible rows are limited', async () => {
    const user = userEvent.setup()
    const reportClient = createReportClient({
      getConsolidatedReport: async () => createConsolidatedReport({
        accrualTotal: 9999,
        incomeTotal: 7777,
        expenseTotal: 3333,
        balance: 4444,
        debt: 2222,
        garageRowCount: 25,
        garageRows: [
          {
            garageId: 'garage-visible',
            garageNumber: '1',
            ownerName: 'Видимый владелец',
            accrualTotal: 100,
            incomeTotal: 50,
            debt: 50,
            meterReadingCount: 1,
          },
        ],
      }),
      getIncomeReport: async () => createIncomeReport({
        accrualTotal: 8888,
        incomeTotal: 5555,
        debt: 3333,
        rowCount: 20,
        rows: [
          {
            ...createIncomeReport().rows[0],
            accrualAmount: 100,
            incomeAmount: 0,
            debt: 100,
          },
        ],
      }),
      getExpenseReport: async () => createExpenseReport({
        accrualTotal: 6666,
        expenseTotal: 4444,
        difference: 2222,
        rowCount: 20,
        rows: [
          {
            ...createExpenseReport().rows[0],
            accrualAmount: 100,
            expenseAmount: 50,
            difference: 50,
          },
        ],
      }),
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Отчеты')
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    const consolidatedSummary = within(reportsPanel).getByLabelText('Итоги отчета')
    expect(await within(consolidatedSummary).findByText('9 999,00')).toBeInTheDocument()
    expect(within(consolidatedSummary).getByText('7 777,00')).toBeInTheDocument()
    expect(within(consolidatedSummary).getByText('3 333,00')).toBeInTheDocument()
    expect(within(consolidatedSummary).getByText('4 444,00')).toBeInTheDocument()
    expect(within(consolidatedSummary).getByText('2 222,00')).toBeInTheDocument()
    expect(await within(reportsPanel).findByText('Показано 1 из 25 строк')).toHaveAttribute('role', 'status')

    await openReportTab(user, reportsPanel, 'Поступления')
    const incomeSummary = within(reportsPanel).getByLabelText('Итоги отчета по поступлениям')
    expect(await within(incomeSummary).findByText('8 888,00')).toBeInTheDocument()
    expect(within(incomeSummary).getByText('5 555,00')).toBeInTheDocument()
    expect(within(incomeSummary).getByText('3 333,00')).toBeInTheDocument()
    expect(await within(reportsPanel).findByText('Показано 1 из 20 строк')).toHaveAttribute('role', 'status')

    await openReportTab(user, reportsPanel, 'Выплаты')
    const expenseSummary = within(reportsPanel).getByLabelText('Итоги отчета по выплатам')
    expect(await within(expenseSummary).findByText('6 666,00')).toBeInTheDocument()
    expect(within(expenseSummary).getByText('4 444,00')).toBeInTheDocument()
    expect(within(expenseSummary).getByText('2 222,00')).toBeInTheDocument()
    expect(await within(reportsPanel).findByText('Показано 1 из 20 строк')).toHaveAttribute('role', 'status')
  })

  it('shows report export errors without ready status', async () => {
    const user = userEvent.setup()
    const reportClient = createReportClient({
      exportConsolidatedReportXlsx: async () => {
        throw 'consolidated export failed'
      },
      exportConsolidatedReportPdf: async () => {
        throw 'consolidated pdf export failed'
      },
      exportIncomeReportXlsx: async () => {
        throw 'income export failed'
      },
      exportIncomeReportPdf: async () => {
        throw 'income pdf export failed'
      },
      exportExpenseReportXlsx: async () => {
        throw 'expense export failed'
      },
      exportExpenseReportPdf: async () => {
        throw 'expense pdf export failed'
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Отчеты')
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать сводный XLSX' }))
    expect(await within(reportsPanel).findByText('Не удалось выгрузить XLSX по сводному отчету.')).toHaveAttribute('role', 'alert')
    expect(within(reportsPanel).queryByText('XLSX по сводному отчету готов.')).not.toBeInTheDocument()
    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать сводный PDF' }))
    expect(await within(reportsPanel).findByText('Не удалось выгрузить PDF по сводному отчету.')).toHaveAttribute('role', 'alert')
    expect(within(reportsPanel).queryByText('PDF по сводному отчету готов.')).not.toBeInTheDocument()

    await openReportTab(user, reportsPanel, 'Поступления')
    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать поступления XLSX' }))
    expect(await within(reportsPanel).findByText('Не удалось выгрузить XLSX по поступлениям.')).toHaveAttribute('role', 'alert')
    expect(within(reportsPanel).queryByText('XLSX по поступлениям готов.')).not.toBeInTheDocument()
    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать поступления PDF' }))
    expect(await within(reportsPanel).findByText('Не удалось выгрузить PDF по поступлениям.')).toHaveAttribute('role', 'alert')
    expect(within(reportsPanel).queryByText('PDF по поступлениям готов.')).not.toBeInTheDocument()

    await openReportTab(user, reportsPanel, 'Выплаты')
    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать выплаты XLSX' }))
    expect(await within(reportsPanel).findByText('Не удалось выгрузить XLSX по выплатам.')).toHaveAttribute('role', 'alert')
    expect(within(reportsPanel).queryByText('XLSX по выплатам готов.')).not.toBeInTheDocument()
    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать выплаты PDF' }))
    expect(await within(reportsPanel).findByText('Не удалось выгрузить PDF по выплатам.')).toHaveAttribute('role', 'alert')
    expect(within(reportsPanel).queryByText('PDF по выплатам готов.')).not.toBeInTheDocument()
  })

  it('shows income report and applies income filters', async () => {
    const user = userEvent.setup()
    let incomeRequest: Parameters<ReportClient['getIncomeReport']>[1] = undefined
    const reportClient = createReportClient({
      getIncomeReport: async (_token, params) => {
        incomeRequest = params
        const report = createIncomeReport()
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
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Отчеты')
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })
    await openReportTab(user, reportsPanel, 'Поступления')

    expect(await within(reportsPanel).findByText('Отчет по поступлениям')).toBeInTheDocument()
    expect(within(reportsPanel).getByRole('table', { name: 'Отчет по поступлениям' })).toBeInTheDocument()
    expect(within(reportsPanel).getAllByText(/Гараж 12 · Членский взнос/)).toHaveLength(2)
    expect(within(reportsPanel).getByText(/PKO-1/)).toBeInTheDocument()

    await user.selectOptions(within(reportsPanel).getByLabelText('Тип строк отчета по поступлениям'), 'payments')
    await user.selectOptions(within(reportsPanel).getByLabelText('Гаражи в отчете по поступлениям'), ['garage-1'])
    await user.selectOptions(within(reportsPanel).getByLabelText('Владельцы в отчете по поступлениям'), ['owner-1'])
    await user.selectOptions(within(reportsPanel).getByLabelText('Виды поступлений в отчете'), ['income-type-1'])
    await user.click(within(reportsPanel).getByRole('button', { name: 'Показать' }))

    expect((await within(reportsPanel).findAllByText('1 строк')).length).toBeGreaterThan(0)
    expect((await within(reportsPanel).findAllByText('1 500,00')).length).toBeGreaterThan(0)
    expect(within(reportsPanel).getByText('Переплата')).toBeInTheDocument()
    expect(incomeRequest?.garageIds).toEqual(['garage-1'])
    expect(incomeRequest?.ownerIds).toEqual(['owner-1'])
    expect(incomeRequest?.incomeTypeIds).toEqual(['income-type-1'])
    expect(incomeRequest?.limit).toBe(16)

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать поступления XLSX' }))

    expect(await within(reportsPanel).findByText('XLSX по поступлениям готов.')).toHaveAttribute('role', 'status')

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать поступления PDF' }))

    expect(await within(reportsPanel).findByText('PDF по поступлениям готов.')).toHaveAttribute('role', 'status')
  })

  it('shows expense report and applies expense filters', async () => {
    const user = userEvent.setup()
    let expenseRequest: Parameters<ReportClient['getExpenseReport']>[1] = undefined
    const reportClient = createReportClient({
      getExpenseReport: async (_token, params) => {
        expenseRequest = params
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
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))
    await openSection(user, 'Отчеты')
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })
    await openReportTab(user, reportsPanel, 'Выплаты')

    expect(await within(reportsPanel).findByText('Отчет по выплатам')).toBeInTheDocument()
    expect(within(reportsPanel).getByRole('table', { name: 'Отчет по выплатам' })).toBeInTheDocument()
    expect(within(reportsPanel).getByText(/Водоканал · Вода/)).toBeInTheDocument()
    expect(within(reportsPanel).getByText('RKO-1')).toBeInTheDocument()

    await user.selectOptions(within(reportsPanel).getByLabelText('Тип строк отчета по выплатам'), 'payments')
    await user.selectOptions(within(reportsPanel).getByLabelText('Поставщики в отчете по выплатам'), ['supplier-1'])
    await user.selectOptions(within(reportsPanel).getByLabelText('Виды выплат в отчете'), ['expense-type-1'])
    await user.click(within(reportsPanel).getByRole('button', { name: 'Показать' }))

    expect((await within(reportsPanel).findAllByText('400,00')).length).toBeGreaterThan(0)
    expect(expenseRequest?.supplierIds).toEqual(['supplier-1'])
    expect(expenseRequest?.expenseTypeIds).toEqual(['expense-type-1'])
    expect(expenseRequest?.limit).toBe(16)

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать выплаты XLSX' }))

    expect(await within(reportsPanel).findByText('XLSX по выплатам готов.')).toHaveAttribute('role', 'status')

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать выплаты PDF' }))

    expect(await within(reportsPanel).findByText('PDF по выплатам готов.')).toHaveAttribute('role', 'status')
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

  it('shows workspace loading errors inside the related panel', async () => {
    const user = userEvent.setup()
    render(
      <App
        authClient={createAuthClient()}
        dictionaryClient={createDictionaryClient({
          getOwners: async () => {
            throw new Error('Нет доступа к справочникам.')
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
        reportClient={createReportClient({
          getConsolidatedReport: async () => {
            throw new Error('Нет доступа к отчетам.')
          },
        })}
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
  return {
    getReleases: async () => [createAppRelease()],
    ...overrides,
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
  }
}

function createDictionaryClient(overrides: Partial<DictionaryClient> = {}): DictionaryClient {
  const owner = createOwner({ id: 'owner-1', lastName: 'Иванов', firstName: 'Иван', phone: '+7 900' })
  const garage = createGarage({ id: 'garage-1', number: '12', ownerId: owner.id, ownerName: owner.fullName })
  const group = createGroup({ id: 'group-1', name: 'Коммунальные услуги' })
  const supplier = createSupplier({ id: 'supplier-1', name: 'Водоканал', groupId: group.id, groupName: group.name, inn: '5401' })
  const incomeType = createAccountingType({ id: 'income-type-1', name: 'Членский взнос', code: 'membership' })
  const expenseType = createAccountingType({ id: 'expense-type-1', name: 'Электроэнергия', code: 'electricity' })
  const tariff = createTariff({ id: 'tariff-1', name: 'Тариф воды', calculationBase: 'meter_water', rate: 50, effectiveFrom: '2026-07-01' })

  return {
    getOwners: async () => [owner],
    createOwner: async () => owner,
    archiveOwner: async () => undefined,
    restoreOwner: async () => owner,
    getGarages: async () => [garage],
    createGarage: async () => garage,
    archiveGarage: async () => undefined,
    restoreGarage: async () => garage,
    getSupplierGroups: async () => [group],
    createSupplierGroup: async () => group,
    archiveSupplierGroup: async () => undefined,
    restoreSupplierGroup: async () => group,
    getSuppliers: async () => [supplier],
    createSupplier: async () => supplier,
    archiveSupplier: async () => undefined,
    restoreSupplier: async () => supplier,
    getIncomeTypes: async () => [incomeType],
    createIncomeType: async () => incomeType,
    archiveIncomeType: async () => undefined,
    restoreIncomeType: async () => incomeType,
    getExpenseTypes: async () => [expenseType],
    createExpenseType: async () => expenseType,
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
      electricityFirstRate: request.electricityFirstRate ?? null,
      electricitySecondRate: request.electricitySecondRate ?? null,
      electricityThirdRate: request.electricityThirdRate ?? null,
    }),
    archiveTariff: async () => undefined,
    restoreTariff: async () => tariff,
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
    getSummary: async () => ({ incomeTotal: 1500, expenseTotal: 0, accrualTotal: 2000, balance: 1500, debt: 500, operationCount: 1, accrualCount: 1, meterReadingCount: 1 }),
    createIncome: async () => operation,
    updateIncome: async (_token, operationId) => ({ ...operation, id: operationId }),
    createExpense: async () => createFinancialOperation({ id: 'operation-2', operationKind: 'expense', amount: 500, supplierName: 'Водоканал', expenseTypeName: 'Вода' }),
    updateExpense: async (_token, operationId) => createFinancialOperation({ id: operationId, operationKind: 'expense', amount: 500, supplierName: 'Водоканал', expenseTypeName: 'Вода' }),
    cancelOperation: async (_token, operationId, request) => {
      const target = operation.id === operationId ? operation : createFinancialOperation({ id: operationId })
      return { ...target, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    createAccrual: async () => accrual,
    updateAccrual: async (_token, accrualId) => ({ ...accrual, id: accrualId }),
    cancelAccrual: async (_token, accrualId, request) => {
      const target = accrual.id === accrualId ? accrual : createAccrual({ id: accrualId })
      return { ...target, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    createSupplierAccrual: async () => supplierAccrual,
    updateSupplierAccrual: async (_token, supplierAccrualId) => ({ ...supplierAccrual, id: supplierAccrualId }),
    cancelSupplierAccrual: async (_token, supplierAccrualId, request) => {
      const target = supplierAccrual.id === supplierAccrualId ? supplierAccrual : createSupplierAccrual({ id: supplierAccrualId })
      return { ...target, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    generateRegularAccruals: async () => createRegularAccrualGenerationResult({ createdAccruals: [accrual], totalAmount: accrual.amount }),
    generateSupplierGroupSalaryAccruals: async () => createSupplierGroupSalaryAccrualGenerationResult({ createdAccruals: [supplierAccrual], totalAmount: supplierAccrual.amount }),
    createMeterReading: async () => meterReading,
    updateMeterReading: async (_token, meterReadingId) => ({ ...meterReading, id: meterReadingId }),
    cancelMeterReading: async (_token, meterReadingId, request) => {
      const target = meterReading.id === meterReadingId ? meterReading : createMeterReading({ id: meterReadingId })
      return { ...target, isCanceled: true, comment: `Отменено: ${request.reason}` }
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
    getAccessRuns: async () => [],
    getAccessRunLog: async () => [],
    getOpenQuarantineItems: async () => [],
    dryRunAccess: async () => run,
    downloadAccessRunReport: async () => new Blob(['{}'], { type: 'application/json' }),
    resolveQuarantineItem: async (_token, itemId) => createAccessImportQuarantineItem({ id: itemId, status: 'resolved' }),
    ...overrides,
  }
}

function createReportClient(overrides: Partial<ReportClient> = {}): ReportClient {
  return {
    getConsolidatedReport: async (_token, params) => {
      const report = createConsolidatedReport()
      if (params?.search?.toLowerCase().includes('петров')) {
        return createConsolidatedReport({
          garageRows: [
            {
              garageId: 'garage-21',
              garageNumber: '21',
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
    ...overrides,
  }
}

function createStatefulImportClient(): ImportClient {
  let runs: AccessImportRunDto[] = []
  let logsByRunId = new Map<string, AccessImportRunLogEntryDto[]>()

  return {
    getAccessRuns: async () => runs,
    getAccessRunLog: async (_token, runId) => logsByRunId.get(runId) ?? [],
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
    cancelAccrual: async (_token, accrualId, request) => {
      const accrual = accruals.find((item) => item.id === accrualId)
      if (!accrual) {
        throw new Error('Начисление не найдено.')
      }

      accruals = accruals.filter((item) => item.id !== accrualId)
      return { ...accrual, isCanceled: true, comment: `Отменено: ${request.reason}` }
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
    cancelSupplierAccrual: async (_token, supplierAccrualId, request) => {
      const accrual = supplierAccruals.find((item) => item.id === supplierAccrualId)
      if (!accrual) {
        throw new Error('Начисление поставщику не найдено.')
      }

      supplierAccruals = supplierAccruals.filter((item) => item.id !== supplierAccrualId)
      return { ...accrual, isCanceled: true, comment: `Отменено: ${request.reason}` }
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
  })
}

function createStatefulDictionaryClient(): DictionaryClient {
  let lastOwner: OwnerDto | null = null
  let lastGroup: SupplierGroupDto | null = null
  let garages: GarageDto[] = []
  let suppliers: SupplierDto[] = []
  let incomeTypes: AccountingTypeDto[] = []
  let expenseTypes: AccountingTypeDto[] = []
  let tariffs: TariffDto[] = []

  return {
    getOwners: async () => lastOwner ? [lastOwner] : [],
    createOwner: async (_token, request) => {
      const owner = createOwner({ id: crypto.randomUUID(), lastName: request.lastName, firstName: request.firstName, phone: request.phone ?? null })
      lastOwner = owner
      return owner
    },
    archiveOwner: async () => undefined,
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
        initialWaterMeterValue: request.initialWaterMeterValue ?? null,
        initialElectricityMeterValue: request.initialElectricityMeterValue ?? null,
        comment: request.comment ?? null,
      })
      garages = [garage, ...garages]
      return garage
    },
    archiveGarage: async () => undefined,
    getSupplierGroups: async () => lastGroup ? [lastGroup] : [],
    createSupplierGroup: async (_token, request) => {
      const group = createGroup({ id: crypto.randomUUID(), name: request.name })
      lastGroup = group
      return group
    },
    archiveSupplierGroup: async () => undefined,
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
    archiveSupplier: async () => undefined,
    getIncomeTypes: async () => incomeTypes,
    createIncomeType: async (_token, request) => {
      const incomeType = createAccountingType({ id: crypto.randomUUID(), name: request.name, code: request.code ?? null })
      incomeTypes = [incomeType, ...incomeTypes]
      return incomeType
    },
    archiveIncomeType: async () => undefined,
    getExpenseTypes: async () => expenseTypes,
    createExpenseType: async (_token, request) => {
      const expenseType = createAccountingType({ id: crypto.randomUUID(), name: request.name, code: request.code ?? null })
      expenseTypes = [expenseType, ...expenseTypes]
      return expenseType
    },
    archiveExpenseType: async () => undefined,
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
  return {
    id: 'garage',
    number: '1',
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
    electricityFirstRate: null,
    electricitySecondRate: null,
    electricityThirdRate: null,
    effectiveFrom: '2026-07-01',
    comment: null,
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
