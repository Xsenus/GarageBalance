import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from './App'
import type { AuditClient, AuditEventDto } from './services/auditApi'
import type { AuthClient, AuthResponse } from './services/authApi'
import type { AccountingTypeDto, DictionaryClient, GarageDto, OwnerDto, SupplierDto, SupplierGroupDto, TariffDto } from './services/dictionariesApi'
import type { AccrualDto, FinanceClient, FinanceSummaryDto, FinancialOperationDto, MeterReadingDto, RegularAccrualGenerationResultDto, SupplierAccrualDto } from './services/financeApi'
import type { AccessImportQuarantineItemDto, AccessImportRunDto, AccessImportRunLogEntryDto, ImportClient } from './services/importApi'
import type { ConsolidatedReportDto, ExpenseReportDto, IncomeReportDto, ReportClient } from './services/reportsApi'
import type { AppReleaseDto, ReleaseClient } from './services/releasesApi'
import type { ManagedRoleDto, ManagedUserDto, UserManagementClient } from './services/usersApi'

describe('App', () => {
  beforeEach(() => {
    window.sessionStorage.clear()
  })

  it('shows auth gate before workspace is available', () => {
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    expect(screen.getByText('GarageBalance')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Вход в систему' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /сначала вход и права/i })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /финансовый учет гск/i })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Панель' })).toBeDisabled()
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))

    expect(await screen.findByRole('heading', { name: /финансовый учет гск/i })).toBeInTheDocument()
    expect(screen.getAllByText('Администратор').length).toBeGreaterThan(0)
    expect(screen.getAllByText('administrator').length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: 'Панель' })).toBeEnabled()

    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })
    expect(within(usersPanel).getByText('Администратор ГСК')).toBeInTheDocument()
    expect(within(usersPanel).getByText('admin@example.com')).toBeInTheDocument()
    const roleMatrix = within(usersPanel).getByRole('region', { name: 'Матрица ролей' })
    expect(within(roleMatrix).getByRole('table', { name: 'Матрица ролей и прав' })).toBeInTheDocument()
    expect(within(roleMatrix).getByText('Администратор')).toBeInTheDocument()
    expect(within(roleMatrix).getByText('Бухгалтер')).toBeInTheDocument()
    expect(within(roleMatrix).getByRole('cell', { name: 'Бухгалтер: Тарифы - разрешено' })).toHaveTextContent('Да')
    expect(within(roleMatrix).getByRole('cell', { name: 'Оператор: Отчеты - нет доступа' })).toHaveTextContent('Нет')

    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    expect(within(dictionaryPanel).getAllByText('Иванов Иван').length).toBeGreaterThan(0)
    expect(within(dictionaryPanel).getByText('Гараж 12')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Водоканал')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Членский взнос')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Тариф воды')).toBeInTheDocument()

    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    expect(within(financePanel).getAllByText('1 500,00').length).toBeGreaterThan(0)
    expect(within(financePanel).getAllByText('2 000,00').length).toBeGreaterThan(0)
    expect(within(financePanel).getByText('500,00')).toBeInTheDocument()
    expect(within(financePanel).getByText('1')).toBeInTheDocument()
    expect(within(financePanel).getByText('19.06.2026')).toBeInTheDocument()
    expect(within(financePanel).getAllByText('06.2026').length).toBeGreaterThan(0)
    expect(within(financePanel).queryByText('2026-06-19')).not.toBeInTheDocument()
    expect(within(financePanel).getAllByText('Гараж 12').length).toBeGreaterThan(0)
  })

  it('shows visible user counter when the user list is compacted', async () => {
    const user = userEvent.setup()
    let requestedLimit: number | undefined
    const users = Array.from({ length: 9 }, (_item, index) =>
      createManagedUser({
        id: `user-${index + 1}`,
        email: `user-${index + 1}@example.com`,
        displayName: `Сотрудник ${index + 1}`,
      }),
    )
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient({
      getUsers: async (_token, _search, limit) => {
        requestedLimit = limit
        return users
      },
    })} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })
    const usersTable = within(usersPanel).getByRole('table', { name: 'Список пользователей' })

    expect(await within(usersTable).findByText('Показано 8 из 9 пользователей')).toHaveAttribute('aria-live', 'polite')
    expect(within(usersTable).getByText('Сотрудник 8')).toBeInTheDocument()
    expect(within(usersTable).queryByText('Сотрудник 9')).not.toBeInTheDocument()
    expect(requestedLimit).toBe(50)
  })

  it('announces empty user and role lists for administrators', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient({
      getUsers: async () => [],
      getRoles: async () => [],
    })} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })
    const usersTable = within(usersPanel).getByRole('table', { name: 'Список пользователей' })
    const roleMatrix = within(usersPanel).getByRole('region', { name: 'Матрица ролей' })
    const roleTable = within(roleMatrix).getByRole('table', { name: 'Матрица ролей и прав' })

    expect(await within(usersTable).findByText('Пользователей пока нет')).toHaveAttribute('aria-live', 'polite')
    expect(within(roleTable).getByText('Роли пока не загружены')).toHaveAttribute('aria-live', 'polite')
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
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
    expect(await within(passwordPanel).findByText('Пароль изменен. Используйте новый пароль при следующем входе.')).toBeInTheDocument()
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })

    await user.type(within(usersPanel).getByLabelText('Email пользователя'), 'operator@example.com')
    await user.type(within(usersPanel).getByLabelText('Имя пользователя'), 'Оператор')
    await user.type(within(usersPanel).getByLabelText('Пароль пользователя'), 'StrongPass123')
    await user.selectOptions(within(usersPanel).getByLabelText('Роль пользователя'), 'operator')
    await user.click(within(usersPanel).getByRole('button', { name: 'Добавить' }))

    expect((await within(usersPanel).findAllByText('Оператор')).length).toBeGreaterThan(0)
    expect(within(usersPanel).getByText('operator@example.com')).toBeInTheDocument()
    expect(within(usersPanel).getByText('Активен')).toBeInTheDocument()
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })

    await user.type(within(usersPanel).getByLabelText('Email пользователя'), 'operator@example.com')
    await user.type(within(usersPanel).getByLabelText('Имя пользователя'), 'Оператор')
    await user.type(within(usersPanel).getByLabelText('Пароль пользователя'), 'Password')
    await user.selectOptions(within(usersPanel).getByLabelText('Роль пользователя'), 'operator')
    await user.click(within(usersPanel).getByRole('button', { name: 'Добавить' }))

    expect(await within(usersPanel).findByText('Проверьте нового пользователя')).toBeInTheDocument()
    expect(within(usersPanel).getByText('Добавьте хотя бы одну цифру в пароль.')).toBeInTheDocument()
    expect(within(usersPanel).getByRole('alert')).toBeInTheDocument()
    expect(createCalled).toBe(false)
    expect(within(usersPanel).queryByText('operator@example.com')).not.toBeInTheDocument()
  })

  it('keeps restricted sections closed after administrator creates an operator', async () => {
    const user = userEvent.setup()
    let operatorSession = false
    const authClient = createAuthClient({
      login: async () => {
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const usersPanel = await screen.findByRole('region', { name: 'Пользователи' })

    await user.type(within(usersPanel).getByLabelText('Email пользователя'), 'operator@example.com')
    await user.type(within(usersPanel).getByLabelText('Имя пользователя'), 'Оператор')
    await user.type(within(usersPanel).getByLabelText('Пароль пользователя'), 'StrongPass123')
    await user.selectOptions(within(usersPanel).getByLabelText('Роль пользователя'), 'operator')
    await user.click(within(usersPanel).getByRole('button', { name: 'Добавить' }))
    expect((await within(usersPanel).findAllByText('Оператор')).length).toBeGreaterThan(0)

    await user.click(screen.getByRole('button', { name: 'Выйти' }))
    await user.click(screen.getByRole('button', { name: 'Вход' }))
    await user.clear(screen.getByLabelText('Email'))
    await user.type(screen.getByLabelText('Email'), 'operator@example.com')
    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    expect(await screen.findByText('Оператор')).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Пользователи' })).not.toBeInTheDocument()
    expect(await screen.findByRole('region', { name: 'Пользователи недоступны' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Справочники' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Платежи' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Импорт недоступен' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Отчеты недоступны' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Аудит недоступен' })).toBeInTheDocument()
    expect(screen.queryByText('Панель пользователей не должна загружаться для оператора.')).not.toBeInTheDocument()
    expect(screen.queryByText('Импорт не должен загружаться для оператора.')).not.toBeInTheDocument()
    expect(screen.queryByText('Отчеты не должны загружаться для оператора.')).not.toBeInTheDocument()
    expect(screen.queryByText('Аудит не должен загружаться для оператора.')).not.toBeInTheDocument()
  })

  it('keeps dictionary and payment actions read-only without write permissions', async () => {
    const user = userEvent.setup()
    const authClient = createAuthClient({
      bootstrapAdmin: async () =>
        createAuthResponse({
          user: {
            email: 'viewer@example.com',
            displayName: 'Наблюдатель',
            roles: ['read_only'],
            permissions: ['dictionaries.read', 'payments.read', 'reports.read'],
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))

    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    expect(within(dictionaryPanel).getByText('Режим просмотра: для добавления и архивирования справочников нужно право dictionaries.write.')).toBeInTheDocument()
    for (const button of within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })) {
      expect(button).toBeDisabled()
    }
    expect(within(dictionaryPanel).getByRole('button', { name: 'Добавить группу' })).toBeDisabled()
    expect(within(dictionaryPanel).getByRole('button', { name: 'Найти гараж' })).toBeEnabled()
    expect(within(dictionaryPanel).queryByRole('button', { name: /Архивировать/ })).not.toBeInTheDocument()

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
    expect(screen.queryByText('Создание владельца не должно вызываться без dictionaries.write.')).not.toBeInTheDocument()
    expect(screen.queryByText('Поступление не должно вызываться без payments.write.')).not.toBeInTheDocument()
  })

  it('allows tariff management without broad dictionary write permission', async () => {
    const user = userEvent.setup()
    let createdTariffs = 0
    let updatedTariffRequest: { id: string; name: string; rate: number; comment?: string } | null = null
    let archivedTariffId: string | null = null
    const authClient = createAuthClient({
      bootstrapAdmin: async () =>
        createAuthResponse({
          user: {
            email: 'tariff@example.com',
            displayName: 'Тарифный специалист',
            roles: ['tariff_manager'],
            permissions: ['dictionaries.read', 'tariffs.manage'],
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
        })
      },
      updateTariff: async (_token, id, request) => {
        updatedTariffRequest = { id, name: request.name, rate: request.rate, comment: request.comment }
        return createTariff({
          id,
          name: request.name,
          calculationBase: request.calculationBase,
          rate: request.rate,
          effectiveFrom: request.effectiveFrom,
          comment: request.comment ?? null,
        })
      },
      archiveTariff: async (_token, id) => {
        archivedTariffId = id
      },
    })

    render(<App authClient={authClient} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    expect(within(dictionaryPanel).getByText('Режим просмотра: для добавления и архивирования справочников нужно право dictionaries.write.')).toBeInTheDocument()
    expect(within(dictionaryPanel).queryByText('Режим просмотра тарифов: для добавления и архивирования тарифов нужно право tariffs.manage.')).not.toBeInTheDocument()
    expect(within(dictionaryPanel).getByLabelText('Фамилия владельца').closest('form')!.querySelector('button[type="submit"]')).toBeDisabled()

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
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false)
    await user.clear(within(tariffForm as HTMLElement).getByLabelText('Название тарифа'))
    await user.type(within(tariffForm as HTMLElement).getByLabelText('Название тарифа'), 'Вода черновик')
    expect(within(tariffForm as HTMLElement).getByText('Есть несохраненные изменения тарифа.')).toBeInTheDocument()
    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф обслуживания' }))
    expect(confirmSpy).toHaveBeenCalledWith('Перейти к другому тарифу без сохранения изменений?')
    expect(within(tariffForm as HTMLElement).getByLabelText('Название тарифа')).toHaveValue('Вода черновик')
    expect(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф воды' })).toBeDisabled()

    confirmSpy.mockReturnValue(true)
    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф обслуживания' }))
    expect(within(tariffForm as HTMLElement).getByLabelText('Название тарифа')).toHaveValue('Тариф обслуживания')
    expect(within(tariffForm as HTMLElement).queryByText('Есть несохраненные изменения тарифа.')).not.toBeInTheDocument()
    expect(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф воды' })).toBeEnabled()
    expect(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф обслуживания' })).toBeDisabled()
    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф воды' }))
    expect(within(tariffForm as HTMLElement).getByLabelText('Название тарифа')).toHaveValue('Тариф воды')
    expect(within(dictionaryPanel).getByRole('button', { name: 'Изменить тариф Тариф воды' })).toBeDisabled()

    confirmSpy.mockReturnValue(false)
    await user.clear(within(tariffForm as HTMLElement).getByLabelText('Название тарифа'))
    await user.type(within(tariffForm as HTMLElement).getByLabelText('Название тарифа'), 'Вода черновик')
    await user.click(within(tariffForm as HTMLElement).getByRole('button', { name: 'Отменить' }))
    expect(confirmSpy).toHaveBeenCalledWith('Отменить редактирование тарифа без сохранения изменений?')
    expect(within(dictionaryPanel).getByText('Изменение тарифа')).toBeInTheDocument()
    expect(within(tariffForm as HTMLElement).getByLabelText('Название тарифа')).toHaveValue('Вода черновик')

    confirmSpy.mockReturnValue(true)
    await user.click(within(tariffForm as HTMLElement).getByRole('button', { name: 'Отменить' }))
    expect(within(dictionaryPanel).queryByText('Изменение тарифа')).not.toBeInTheDocument()
    expect(within(tariffForm as HTMLElement).getByLabelText('Название тарифа')).toHaveValue('')
    confirmSpy.mockRestore()

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

  it('adds owner, garage, supplier group and supplier from protected workspace', async () => {
    const user = userEvent.setup()
    const dictionaryClient = createStatefulDictionaryClient()
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

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
  })

  it('shows dictionary list truncation counter when there are more rows', async () => {
    const user = userEvent.setup()
    const owners = Array.from({ length: 6 }, (_, index) => createOwner({ id: `owner-${index + 1}`, lastName: `Владелец${index + 1}`, firstName: 'Тест' }))
    const dictionaryClient: DictionaryClient = {
      ...createStatefulDictionaryClient(),
      getOwners: async () => owners,
    }
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    const ownerForm = within(dictionaryPanel).getByLabelText('Фамилия владельца').closest('form')!

    expect(await within(ownerForm as HTMLElement).findByText('Владелец1 Тест')).toBeInTheDocument()
    expect(within(ownerForm as HTMLElement).getByText('Показано 5 из 6 записей')).toHaveAttribute('aria-live', 'polite')
    expect(within(ownerForm as HTMLElement).queryByText('Владелец6 Тест')).not.toBeInTheDocument()
    const compactList = within(ownerForm as HTMLElement).getByRole('list')
    const expandButton = within(ownerForm as HTMLElement).getByRole('button', { name: 'Показать все записи' })
    expect(expandButton).toHaveAttribute('aria-expanded', 'false')
    expect(expandButton).toHaveAttribute('aria-controls', compactList.id)

    await user.click(expandButton)
    expect(within(ownerForm as HTMLElement).getByText('Показано 6 из 6 записей')).toHaveAttribute('aria-live', 'polite')
    expect(within(ownerForm as HTMLElement).getByText('Владелец6 Тест')).toBeInTheDocument()
    const collapseButton = within(ownerForm as HTMLElement).getByRole('button', { name: 'Свернуть список' })
    expect(collapseButton).toHaveAttribute('aria-expanded', 'true')
    expect(collapseButton).toHaveAttribute('aria-controls', compactList.id)

    await user.click(collapseButton)
    expect(within(ownerForm as HTMLElement).getByText('Показано 5 из 6 записей')).toBeInTheDocument()
    expect(within(ownerForm as HTMLElement).queryByText('Владелец6 Тест')).not.toBeInTheDocument()
  })

  it('announces empty dictionary lists', async () => {
    const user = userEvent.setup()
    const dictionaryClient = createDictionaryClient({
      getOwners: async () => [],
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    const ownerForm = within(dictionaryPanel).getByLabelText('Фамилия владельца').closest('form')!

    expect(await within(ownerForm as HTMLElement).findByText('Владельцев пока нет')).toHaveAttribute('aria-live', 'polite')
  })

  it('requests bounded dictionary lists from dictionaries workspace', async () => {
    const user = userEvent.setup()
    const requestedLimits: Record<string, number | undefined> = {}
    const dictionaryClient = createDictionaryClient({
      getOwners: async (_token, _search, limit) => {
        requestedLimits.owners = limit
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    await screen.findByRole('region', { name: 'Справочники' })

    await waitFor(() => expect(requestedLimits).toMatchObject({
      owners: 100,
      garages: 100,
      supplierGroups: 100,
      suppliers: 100,
      incomeTypes: 100,
      expenseTypes: 100,
      tariffs: 100,
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    expect(within(dictionaryPanel).getByText('Гараж 12')).toBeInTheDocument()
    await user.type(within(dictionaryPanel).getByLabelText('Поиск гаража или владельца'), 'Петров')

    await waitFor(() => {
      expect(garageSearch).toBe('Петров')
    })
    expect(await within(dictionaryPanel).findByText('Гараж 21')).toBeInTheDocument()
    expect(within(dictionaryPanel).queryByText('Гараж 12')).not.toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Найдено гаражей: 1')).toBeInTheDocument()

    await user.clear(within(dictionaryPanel).getByLabelText('Поиск гаража или владельца'))

    await waitFor(() => {
      expect(garageSearch).toBeUndefined()
    })
    expect(await within(dictionaryPanel).findByText('Гараж 12')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Гараж 21')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Показаны все гаражи')).toBeInTheDocument()
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

    expect(within(dictionaryPanel).getByText('Водоканал')).toBeInTheDocument()
    await user.type(within(dictionaryPanel).getByLabelText('Поиск поставщика'), '7728')

    await waitFor(() => {
      expect(supplierSearch).toBe('7728')
    })
    expect(await within(dictionaryPanel).findByText('Альфа-Банк')).toBeInTheDocument()
    expect(within(dictionaryPanel).queryByText('Водоканал')).not.toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Найдено поставщиков: 1')).toBeInTheDocument()

    await user.clear(within(dictionaryPanel).getByLabelText('Поиск поставщика'))

    await waitFor(() => {
      expect(supplierSearch).toBeUndefined()
    })
    expect(await within(dictionaryPanel).findByText('Водоканал')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Альфа-Банк')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Показаны все поставщики')).toBeInTheDocument()
  })

  it('archives owner from dictionaries workspace', async () => {
    const user = userEvent.setup()
    let archivedOwnerId: string | null = null
    const dictionaryClient = createDictionaryClient({
      archiveOwner: async (_token, id) => {
        archivedOwnerId = id
      },
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

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

  it('adds income type, expense type and tariff from dictionaries workspace', async () => {
    const user = userEvent.setup()
    const dictionaryClient = createStatefulDictionaryClient()
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })

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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.clear(within(financePanel).getByLabelText('Сумма поступления'))
    await user.type(within(financePanel).getByLabelText('Сумма поступления'), '2000')
    await user.type(within(financePanel).getByLabelText('Документ поступления'), 'PKO-1')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Провести' })[0])

    expect(await within(financePanel).findByText('+2 000,00')).toBeInTheDocument()
    expect(within(financePanel).getAllByText('2 000,00').length).toBeGreaterThan(0)

    await user.clear(within(financePanel).getByLabelText('Сумма выплаты'))
    await user.type(within(financePanel).getByLabelText('Сумма выплаты'), '500')
    await user.type(within(financePanel).getByLabelText('Документ выплаты'), 'RKO-1')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Провести' })[1])

    expect(await within(financePanel).findByText('-500,00')).toBeInTheDocument()
    expect(within(financePanel).getByText('1 500,00')).toBeInTheDocument()
    expect(within(financePanel).getByText('Переплата')).toBeInTheDocument()
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
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
    const financeClient = createFinanceClient({
      getOperations: async (_token, limit) => {
        requestedLimits.operations = limit
        return operations
      },
      getAccruals: async (_token, limit) => {
        requestedLimits.accruals = limit
        return accruals
      },
      getSupplierAccruals: async (_token, limit) => {
        requestedLimits.supplierAccruals = limit
        return supplierAccruals
      },
      getMeterReadings: async (_token, limit) => {
        requestedLimits.meterReadings = limit
        return meterReadings
      },
      getSummary: async () => ({ incomeTotal: 0, expenseTotal: 0, accrualTotal: 0, balance: 0, debt: 0, operationCount: operations.length, accrualCount: accruals.length, meterReadingCount: meterReadings.length }),
    })
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })
    const operationsTable = within(financePanel).getByRole('table', { name: 'Последние платежи' })
    const accrualsTable = within(financePanel).getByRole('table', { name: 'Последние начисления' })
    const supplierAccrualsTable = within(financePanel).getByRole('table', { name: 'Последние начисления поставщикам' })
    const meterReadingsTable = within(financePanel).getByRole('table', { name: 'Последние показания' })

    expect(await within(operationsTable).findByText('Показано 8 из 9 операций')).toHaveAttribute('aria-live', 'polite')
    expect(within(accrualsTable).getByText('Показано 8 из 9 начислений')).toHaveAttribute('aria-live', 'polite')
    expect(within(supplierAccrualsTable).getByText('Показано 8 из 9 начислений поставщикам')).toHaveAttribute('aria-live', 'polite')
    expect(within(meterReadingsTable).getByText('Показано 8 из 9 показаний')).toHaveAttribute('aria-live', 'polite')
    expect(within(operationsTable).getByText('Поступление 8')).toBeInTheDocument()
    expect(within(operationsTable).queryByText('Поступление 9')).not.toBeInTheDocument()
    expect(requestedLimits).toEqual({ operations: 50, accruals: 50, supplierAccruals: 50, meterReadings: 50 })
  })

  it('cancels income operation with required reason from payments workspace', async () => {
    const user = userEvent.setup()
    const promptSpy = vi.spyOn(window, 'prompt').mockReturnValue('Ошибочный документ')
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.clear(within(financePanel).getByLabelText('Сумма поступления'))
    await user.type(within(financePanel).getByLabelText('Сумма поступления'), '700')
    await user.type(within(financePanel).getByLabelText('Документ поступления'), 'PKO-cancel')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Провести' })[0])

    expect(await within(financePanel).findByText('+700,00')).toBeInTheDocument()
    expect(within(financePanel).getByText('1 операций')).toBeInTheDocument()
    await user.click(within(financePanel).getByRole('button', { name: 'Отменить операцию PKO-cancel' }))

    expect(promptSpy).toHaveBeenCalledWith('Укажите причину отмены операции')
    await waitFor(() => expect(within(financePanel).queryByText('+700,00')).not.toBeInTheDocument())
    expect(within(financePanel).getByText('0 операций')).toBeInTheDocument()
    expect(within(within(financePanel).getByRole('table', { name: 'Последние платежи' })).getByText('Операций пока нет')).toHaveAttribute('aria-live', 'polite')
    promptSpy.mockRestore()
  })

  it('cancels accruals and meter readings with required reasons from payments workspace', async () => {
    const user = userEvent.setup()
    const promptSpy = vi.spyOn(window, 'prompt').mockReturnValue('Ошибочный ввод')
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.clear(within(financePanel).getByLabelText('Сумма начисления'))
    await user.type(within(financePanel).getByLabelText('Сумма начисления'), '900')
    await user.type(within(financePanel).getByLabelText('Комментарий начисления'), 'Ручная корректировка')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Начислить' })[0])
    const accrualTable = within(financePanel).getByRole('table', { name: 'Последние начисления' })
    expect(await within(accrualTable).findByText('900,00')).toBeInTheDocument()
    await user.click(within(accrualTable).getByRole('button', { name: 'Отменить начисление Членский взнос гараж 12' }))
    await waitFor(() => expect(within(accrualTable).queryByText('900,00')).not.toBeInTheDocument())
    expect(within(accrualTable).getByText('Начислений пока нет')).toHaveAttribute('aria-live', 'polite')

    await user.clear(within(financePanel).getByLabelText('Сумма начисления поставщику'))
    await user.type(within(financePanel).getByLabelText('Сумма начисления поставщику'), '650')
    await user.type(within(financePanel).getByLabelText('Комментарий начисления поставщику'), 'Счет за воду')
    await user.click(within(financePanel).getAllByRole('button', { name: 'Начислить' })[1])
    const supplierAccrualTable = within(financePanel).getByRole('table', { name: 'Последние начисления поставщикам' })
    expect(await within(supplierAccrualTable).findByText('650,00')).toBeInTheDocument()
    await user.click(within(supplierAccrualTable).getByRole('button', { name: 'Отменить начисление поставщику Водоканал' }))
    await waitFor(() => expect(within(supplierAccrualTable).queryByText('650,00')).not.toBeInTheDocument())
    expect(within(supplierAccrualTable).getByText('Начислений поставщикам пока нет')).toHaveAttribute('aria-live', 'polite')

    await user.clear(within(financePanel).getByLabelText('Новое показание'))
    await user.type(within(financePanel).getByLabelText('Новое показание'), '15.5')
    await user.click(within(financePanel).getByRole('button', { name: 'Внести' }))
    const meterReadingTable = within(financePanel).getByRole('table', { name: 'Последние показания' })
    expect(await within(meterReadingTable).findByText('5.5')).toBeInTheDocument()
    await user.click(within(meterReadingTable).getByRole('button', { name: 'Отменить показание Вода гараж 12' }))
    await waitFor(() => expect(within(meterReadingTable).queryByText('5.5')).not.toBeInTheDocument())
    expect(within(meterReadingTable).getByText('Показаний пока нет')).toHaveAttribute('aria-live', 'polite')
    expect(promptSpy).toHaveBeenCalledTimes(3)
    promptSpy.mockRestore()
  })

  it('creates manual accrual and updates debt from payments workspace', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
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
  })

  it('creates supplier accrual from payments workspace', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
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

  it('shows supplier obligation before and after expense payment', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
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
  })

  it('generates regular accruals from tariff in payments workspace', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    const dictionaryClient = createDictionaryClient({
      getTariffs: async () => [createTariff({ id: 'tariff-fixed', name: 'Членский тариф', calculationBase: 'fixed', rate: 300, effectiveFrom: '2026-01-01' })],
    })
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.type(within(financePanel).getByLabelText('Комментарий регулярных начислений'), 'Начисление за месяц')
    await user.click(within(financePanel).getByRole('button', { name: 'Создать месяц' }))

    expect(await within(financePanel).findByText('Создано 1, пропущено 0')).toHaveAttribute('aria-live', 'polite')
    expect((await within(financePanel).findAllByText('300,00')).length).toBeGreaterThan(0)
    expect(within(financePanel).getByText('Членский тариф · 300,00')).toBeInTheDocument()
    const accrualTable = within(financePanel).getByRole('table', { name: 'Последние начисления' })
    expect(within(accrualTable).getByText('Авто')).toBeInTheDocument()
    await user.dblClick(within(accrualTable).getByLabelText(/Разбивка начисления/i))
    const dialog = await screen.findByRole('dialog', { name: 'Разбивка начисления' })
    expect(within(dialog).getByText('Начисление за месяц')).toBeInTheDocument()
  })

  it('creates meter reading and shows calculated consumption', async () => {
    const user = userEvent.setup()
    const financeClient = createStatefulFinanceClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={financeClient} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const financePanel = await screen.findByRole('region', { name: 'Платежи' })

    await user.selectOptions(within(financePanel).getByLabelText('Тип счетчика'), 'electricity')
    await user.clear(within(financePanel).getByLabelText('Новое показание'))
    await user.type(within(financePanel).getByLabelText('Новое показание'), '125')
    await user.click(within(financePanel).getByRole('button', { name: 'Внести' }))

    expect(await within(financePanel).findByText('проверьте предыдущий месяц')).toBeInTheDocument()
  })

  it('runs Access import dry-run and shows checks history', async () => {
    const user = userEvent.setup()
    const importClient = createStatefulImportClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={importClient} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const importPanel = await screen.findByRole('region', { name: 'Импорт Access' })
    const file = new File(['garage owner payment'], 'ГСК.accdb', { type: 'application/octet-stream' })

    await user.upload(within(importPanel).getByLabelText('Файл Access'), file)
    await user.click(within(importPanel).getByRole('button', { name: 'Проверить файл' }))

    expect((await within(importPanel).findAllByText('Dry-run завершен с предупреждениями.')).length).toBeGreaterThan(0)
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
    expect(await within(importPanel).findByRole('table', { name: 'Лог запуска Access' })).toBeInTheDocument()
    expect(within(importPanel).getByText('file_received')).toBeInTheDocument()
    expect(within(importPanel).getByText('dry_run_finished')).toBeInTheDocument()
    expect(within(importPanel).getAllByText('Пройдено').length).toBeGreaterThan(0)
    expect(within(importPanel).getAllByText('Предупреждение').length).toBeGreaterThan(0)
    expect(within(importPanel).getAllByText('Завершен').length).toBeGreaterThan(0)
    expect(within(importPanel).getAllByText('2/3 · 1 предупреждение · 0 ошибок').length).toBeGreaterThan(0)
    expect(within(importPanel).getAllByText('ГСК.accdb').length).toBeGreaterThan(0)

    await user.click(within(importPanel).getByRole('button', { name: 'Скачать отчет JSON' }))

    expect(await within(importPanel).findByText('Отчет dry-run импорта готов.')).toHaveAttribute('aria-live', 'polite')
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const importPanel = await screen.findByRole('region', { name: 'Импорт Access' })

    expect(await within(importPanel).findByText('Показано 10 из 11 строк лога')).toHaveAttribute('aria-live', 'polite')
    expect(within(importPanel).getByText('Показано 8 из 9 запусков')).toHaveAttribute('aria-live', 'polite')
    expect(within(importPanel).getByText('Показано 8 из 9 строк карантина')).toHaveAttribute('aria-live', 'polite')
    expect(within(importPanel).getByText('step_10')).toBeInTheDocument()
    expect(within(importPanel).queryByText('step_11')).not.toBeInTheDocument()
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const importPanel = await screen.findByRole('region', { name: 'Импорт Access' })

    expect(await within(importPanel).findByText('Выберите запуск dry-run')).toHaveAttribute('aria-live', 'polite')
    expect(within(importPanel).getByText('Проверок пока нет')).toHaveAttribute('aria-live', 'polite')
    expect(within(importPanel).getByText('Лог выбранного запуска пока пуст')).toHaveAttribute('aria-live', 'polite')
    expect(within(importPanel).getByText('Истории импорта пока нет')).toHaveAttribute('aria-live', 'polite')
    expect(within(importPanel).getByText('Открытых строк карантина нет')).toHaveAttribute('aria-live', 'polite')
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const importPanel = await screen.findByRole('region', { name: /Access/ })
    const quarantineTable = await within(importPanel).findByRole('table', { name: 'Карантин импорта Access' })

    expect(within(quarantineTable).getByText('Garage #42')).toBeInTheDocument()
    expect(within(quarantineTable).getByText('missing-owner')).toBeInTheDocument()

    await user.click(within(quarantineTable).getByRole('button', { name: 'Закрыть' }))

    expect(await within(importPanel).findByText('Строка карантина закрыта.')).toBeInTheDocument()
    expect(within(quarantineTable).queryByText('Garage #42')).not.toBeInTheDocument()
    expect(within(quarantineTable).getByText('Открытых строк карантина нет')).toHaveAttribute('aria-live', 'polite')
  })

  it('shows audit journal for users with audit permission', async () => {
    const user = userEvent.setup()
    let auditRequest: Parameters<AuditClient['getEvents']>[1] = undefined
    let auditExportRequest: Parameters<AuditClient['exportEvents']>[1] = undefined
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      bootstrapAdmin: async () => ({
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const auditPanel = await screen.findByRole('region', { name: 'Audit-журнал' })

    expect(await within(auditPanel).findByText('auth.login_success')).toBeInTheDocument()
    expect(within(auditPanel).getByText('finance.income_created')).toBeInTheDocument()

    await user.type(within(auditPanel).getByLabelText('Поиск в audit-журнале'), 'import')

    expect(await within(auditPanel).findByText('import.access_dry_run')).toBeInTheDocument()
    expect(auditRequest?.search).toBe('import')
    expect(auditRequest?.limit).toBe(50)

    await user.click(within(auditPanel).getByRole('button', { name: /CSV/ }))

    expect(auditExportRequest?.search).toBe('import')
    expect(auditExportRequest?.limit).toBeUndefined()
    expect(await within(auditPanel).findByText('Audit-журнал CSV готов.')).toHaveAttribute('aria-live', 'polite')
  })

  it('shows visible audit event counter when audit log is compacted', async () => {
    const user = userEvent.setup()
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      bootstrapAdmin: async () => ({
        ...auth,
        user: {
          ...auth.user,
          permissions: [...auth.user.permissions, 'audit.read'],
        },
      }),
    })
    const auditEvents = Array.from({ length: 13 }, (_item, index) =>
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const auditPanel = await screen.findByRole('region', { name: 'Audit-журнал' })
    const auditTable = within(auditPanel).getByRole('table', { name: 'События audit-журнала' })

    expect(await within(auditTable).findByText('Показано 12 из 13 событий')).toHaveAttribute('aria-live', 'polite')
    expect(within(auditTable).getByText('audit.event_12')).toBeInTheDocument()
    expect(within(auditTable).queryByText('audit.event_13')).not.toBeInTheDocument()
  })

  it('announces empty audit journal for users with audit permission', async () => {
    const user = userEvent.setup()
    const auth = createAuthResponse()
    const authClient = createAuthClient({
      bootstrapAdmin: async () => ({
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const auditPanel = await screen.findByRole('region', { name: 'Audit-журнал' })
    const auditTable = within(auditPanel).getByRole('table', { name: 'События audit-журнала' })

    expect(await within(auditTable).findByText('Событий пока нет')).toHaveAttribute('aria-live', 'polite')
  })

  it('shows consolidated report and applies garage search', async () => {
    const user = userEvent.setup()
    const reportClient = createReportClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    expect(await within(reportsPanel).findByText('Консолидированный отчет за период')).toBeInTheDocument()
    expect(within(reportsPanel).getByText('Формат периода сводного отчета: ММ.ГГГГ.')).toBeInTheDocument()
    expect(within(reportsPanel).getByText('Формат дат поступлений: ДД.ММ.ГГГГ.')).toBeInTheDocument()
    expect(within(reportsPanel).getByText('Формат дат выплат: ДД.ММ.ГГГГ.')).toBeInTheDocument()
    expect(within(reportsPanel).getByLabelText('Начало периода отчета')).toHaveAccessibleDescription('Формат периода сводного отчета: ММ.ГГГГ.')
    expect(within(reportsPanel).getByLabelText('Начало отчета по поступлениям')).toHaveAccessibleDescription('Формат дат поступлений: ДД.ММ.ГГГГ.')
    expect(within(reportsPanel).getByLabelText('Начало отчета по выплатам')).toHaveAccessibleDescription('Формат дат выплат: ДД.ММ.ГГГГ.')
    expect(within(reportsPanel).getByText('Начисления попадают в сводный отчет по учетному месяцу, поступления и выплаты - по фактической дате операции.')).toBeInTheDocument()
    expect(within(reportsPanel).getByText('В поступлениях начисления считаются по учетному месяцу, оплаты - по фактической дате поступления.')).toBeInTheDocument()
    expect(within(reportsPanel).getByText('В выплатах начисления поставщикам считаются по учетному месяцу, фактические выплаты - по дате оплаты.')).toBeInTheDocument()
    expect(within(reportsPanel).getAllByText('2 000,00').length).toBeGreaterThan(0)
    expect(within(reportsPanel).getByRole('table', { name: 'Помесячный отчет' })).toBeInTheDocument()
    expect(within(reportsPanel).getAllByText('Гараж 12').length).toBeGreaterThan(0)

    await user.type(within(reportsPanel).getByLabelText('Поиск в отчете'), 'Петров')
    await user.click(within(reportsPanel).getByRole('button', { name: 'Сформировать' }))

    expect(await within(reportsPanel).findByText('Гараж 21')).toBeInTheDocument()

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать сводный XLSX' }))

    expect(await within(reportsPanel).findByText('XLSX по сводному отчету готов.')).toHaveAttribute('aria-live', 'polite')

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать сводный PDF' }))

    expect(await within(reportsPanel).findByText('PDF по сводному отчету готов.')).toHaveAttribute('aria-live', 'polite')
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
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

    fireEvent.change(within(reportsPanel).getByLabelText('Начало отчета по поступлениям'), { target: { value: '2026-07-01' } })
    fireEvent.change(within(reportsPanel).getByLabelText('Конец отчета по поступлениям'), { target: { value: '2026-06-01' } })
    await user.click(within(reportsPanel).getAllByRole('button', { name: 'Показать' })[0])

    expect(await within(reportsPanel).findByText('Проверьте отчет по поступлениям')).toBeInTheDocument()
    expect(within(reportsPanel).getByText('Начало отчета по поступлениям не может быть позже конца.')).toBeInTheDocument()
    expect(incomeCalls).toBe(initialIncomeCalls)

    fireEvent.change(within(reportsPanel).getByLabelText('Начало отчета по выплатам'), { target: { value: '2026-07-01' } })
    fireEvent.change(within(reportsPanel).getByLabelText('Конец отчета по выплатам'), { target: { value: '2026-06-01' } })
    await user.click(within(reportsPanel).getAllByRole('button', { name: 'Показать' })[1])

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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))

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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    expect(await within(reportsPanel).findByText('Показано 12 из 13 строк')).toHaveAttribute('aria-live', 'polite')
    const reportRowCounters = await within(reportsPanel).findAllByText('Показано 16 из 17 строк')
    expect(reportRowCounters).toHaveLength(2)
    expect(reportRowCounters[0]).toHaveAttribute('aria-live', 'polite')
    expect(reportRowCounters[1]).toHaveAttribute('aria-live', 'polite')
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    expect(await within(reportsPanel).findByText('Помесячных строк отчета пока нет')).toHaveAttribute('aria-live', 'polite')
    expect(await within(reportsPanel).findByText('По выбранному фильтру гаражей нет')).toHaveAttribute('aria-live', 'polite')
    expect(await within(reportsPanel).findByText('По выбранному фильтру поступлений нет')).toHaveAttribute('aria-live', 'polite')
    expect(await within(reportsPanel).findByText('По выбранному фильтру выплат нет')).toHaveAttribute('aria-live', 'polite')
    expect(within(within(reportsPanel).getByRole('table', { name: 'Помесячный отчет' })).queryByText(/Июнь 2026/)).not.toBeInTheDocument()
    expect(within(within(reportsPanel).getByRole('table', { name: 'Отчет по гаражам' })).queryByText(/Иванов Иван/)).not.toBeInTheDocument()
    expect(within(reportsPanel).queryByText(/PKO-1/)).not.toBeInTheDocument()
    expect(within(reportsPanel).queryByText(/RKO-1/)).not.toBeInTheDocument()
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    expect(await within(reportsPanel).findByText('Отчет по поступлениям')).toBeInTheDocument()
    expect(within(reportsPanel).getByRole('table', { name: 'Отчет по поступлениям' })).toBeInTheDocument()
    expect(within(reportsPanel).getAllByText(/Гараж 12 · Членский взнос/)).toHaveLength(2)
    expect(within(reportsPanel).getByText(/PKO-1/)).toBeInTheDocument()

    await user.selectOptions(within(reportsPanel).getByLabelText('Тип строк отчета по поступлениям'), 'payments')
    await user.selectOptions(within(reportsPanel).getByLabelText('Гаражи в отчете по поступлениям'), ['garage-1'])
    await user.selectOptions(within(reportsPanel).getByLabelText('Владельцы в отчете по поступлениям'), ['owner-1'])
    await user.selectOptions(within(reportsPanel).getByLabelText('Виды поступлений в отчете'), ['income-type-1'])
    await user.click(within(reportsPanel).getAllByRole('button', { name: 'Показать' })[0])

    expect((await within(reportsPanel).findAllByText('1 строк')).length).toBeGreaterThan(0)
    expect((await within(reportsPanel).findAllByText('1 500,00')).length).toBeGreaterThan(0)
    expect(within(reportsPanel).getByText('Переплата')).toBeInTheDocument()
    expect(incomeRequest?.garageIds).toEqual(['garage-1'])
    expect(incomeRequest?.ownerIds).toEqual(['owner-1'])
    expect(incomeRequest?.incomeTypeIds).toEqual(['income-type-1'])
    expect(incomeRequest?.limit).toBe(16)

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать поступления XLSX' }))

    expect(await within(reportsPanel).findByText('XLSX по поступлениям готов.')).toHaveAttribute('aria-live', 'polite')

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать поступления PDF' }))

    expect(await within(reportsPanel).findByText('PDF по поступлениям готов.')).toHaveAttribute('aria-live', 'polite')
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    expect(await within(reportsPanel).findByText('Отчет по выплатам')).toBeInTheDocument()
    expect(within(reportsPanel).getByRole('table', { name: 'Отчет по выплатам' })).toBeInTheDocument()
    expect(within(reportsPanel).getByText(/Водоканал · Вода/)).toBeInTheDocument()
    expect(within(reportsPanel).getByText('RKO-1')).toBeInTheDocument()

    await user.selectOptions(within(reportsPanel).getByLabelText('Тип строк отчета по выплатам'), 'payments')
    await user.selectOptions(within(reportsPanel).getByLabelText('Поставщики в отчете по выплатам'), ['supplier-1'])
    await user.selectOptions(within(reportsPanel).getByLabelText('Виды выплат в отчете'), ['expense-type-1'])
    await user.click(within(reportsPanel).getAllByRole('button', { name: 'Показать' })[1])

    expect((await within(reportsPanel).findAllByText('400,00')).length).toBeGreaterThan(0)
    expect(expenseRequest?.supplierIds).toEqual(['supplier-1'])
    expect(expenseRequest?.expenseTypeIds).toEqual(['expense-type-1'])
    expect(expenseRequest?.limit).toBe(16)

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать выплаты XLSX' }))

    expect(await within(reportsPanel).findByText('XLSX по выплатам готов.')).toHaveAttribute('aria-live', 'polite')

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать выплаты PDF' }))

    expect(await within(reportsPanel).findByText('PDF по выплатам готов.')).toHaveAttribute('aria-live', 'polite')
  })

  it('shows login errors without opening protected workspace', async () => {
    const user = userEvent.setup()
    const authClient = createAuthClient({
      login: async () => {
        throw new Error('Неверный email или пароль.')
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.click(screen.getByRole('button', { name: 'Вход' }))
    await user.type(screen.getByLabelText('Пароль'), 'WrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    expect(await screen.findByText('Неверный email или пароль.')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /финансовый учет гск/i })).not.toBeInTheDocument()
  })

  it('shows password policy error without opening workspace', async () => {
    const user = userEvent.setup()
    const authClient = createAuthClient({
      bootstrapAdmin: async () => {
        throw new Error('Пароль должен содержать хотя бы одну цифру.')
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    expect(screen.getByText('Минимум 8 символов: заглавная буква, строчная буква и цифра.')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Пароль'), 'Password1')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))

    expect(await screen.findByText('Пароль должен содержать хотя бы одну цифру.')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /финансовый учет гск/i })).not.toBeInTheDocument()
  })

  it('shows auth validation summary before calling backend', async () => {
    const user = userEvent.setup()
    let bootstrapCalled = false
    const authClient = createAuthClient({
      bootstrapAdmin: async () => {
        bootstrapCalled = true
        return createAuthResponse()
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={createReportClient()} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'Password')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))

    expect(await screen.findByText('Проверьте форму входа')).toBeInTheDocument()
    expect(screen.getByText('Добавьте хотя бы одну цифру в пароль.')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(bootstrapCalled).toBe(false)
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

    await user.click(screen.getByRole('button', { name: 'Вход' }))
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
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))

    const releasePanel = await screen.findByRole('region', { name: 'Что нового' })
    expect(await within(releasePanel).findByText('История обновлений')).toBeInTheDocument()
    expect(within(releasePanel).getByText('Добавлен консолидированный отчет')).toBeInTheDocument()
    expect(within(releasePanel).getByText(/первый отчет для месячной сверки/i)).toBeInTheDocument()
    expect(within(releasePanel).getByText(/панель "Отчеты"/i)).toBeInTheDocument()
    expect(within(releasePanel).getByText(/v0.11.0/)).toBeInTheDocument()
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
        })}
      />,
    )

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))

    expect(await screen.findByText('Нет доступа к пользователям.')).toBeInTheDocument()
    expect((await screen.findAllByText('Нет доступа к справочникам.')).length).toBeGreaterThan(0)
    expect(await screen.findByText('Нет доступа к платежам.')).toBeInTheDocument()
    expect(await screen.findByText('Нет доступа к импорту.')).toBeInTheDocument()
    expect(await screen.findByText('Нет доступа к отчетам.')).toBeInTheDocument()
    expect(await screen.findByText('Нет доступа к истории обновлений.')).toBeInTheDocument()
    expect(screen.getAllByRole('alert').map((alert) => alert.textContent)).toEqual(
      expect.arrayContaining([
        'Нет доступа к пользователям.',
        'Нет доступа к справочникам.',
        'Нет доступа к платежам.',
        'Нет доступа к импорту.',
        'Нет доступа к отчетам.',
        'Нет доступа к истории обновлений.',
      ]),
    )
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
  return {
    getEvents: async () => [createAuditEvent({})],
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
    createUser: async () => admin,
    updateUser: async () => admin,
    ...overrides,
  }
}

function createStatefulUserClient(): UserManagementClient {
  const roles = createRoles()

  return {
    getRoles: async () => roles,
    getUsers: async () => [],
    createUser: async (_token, request) =>
      createManagedUser({
        id: crypto.randomUUID(),
        email: request.email,
        displayName: request.displayName,
        roles: request.roleCodes,
        permissions: roles.find((role) => role.code === request.roleCodes[0])?.permissions ?? [],
      }),
    updateUser: async (_token, userId, request) =>
      createManagedUser({
        id: userId,
        email: 'updated@example.com',
        displayName: request.displayName,
        isActive: request.isActive,
        roles: request.roleCodes,
      }),
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
    getGarages: async () => [garage],
    createGarage: async () => garage,
    archiveGarage: async () => undefined,
    getSupplierGroups: async () => [group],
    createSupplierGroup: async () => group,
    archiveSupplierGroup: async () => undefined,
    getSuppliers: async () => [supplier],
    createSupplier: async () => supplier,
    archiveSupplier: async () => undefined,
    getIncomeTypes: async () => [incomeType],
    createIncomeType: async () => incomeType,
    archiveIncomeType: async () => undefined,
    getExpenseTypes: async () => [expenseType],
    createExpenseType: async () => expenseType,
    archiveExpenseType: async () => undefined,
    getTariffs: async () => [tariff],
    createTariff: async () => tariff,
    updateTariff: async (_token, id, request) => createTariff({ id, name: request.name, calculationBase: request.calculationBase, rate: request.rate, effectiveFrom: request.effectiveFrom, comment: request.comment ?? null }),
    archiveTariff: async () => undefined,
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

  return {
    getOperations: async () => [operation],
    getAccruals: async () => [accrual],
    getSupplierAccruals: async () => [supplierAccrual],
    getMeterReadings: async () => [meterReading],
    getSummary: async () => ({ incomeTotal: 1500, expenseTotal: 0, accrualTotal: 2000, balance: 1500, debt: 500, operationCount: 1, accrualCount: 1, meterReadingCount: 1 }),
    createIncome: async () => operation,
    createExpense: async () => createFinancialOperation({ id: 'operation-2', operationKind: 'expense', amount: 500, supplierName: 'Водоканал', expenseTypeName: 'Вода' }),
    cancelOperation: async (_token, operationId, request) => {
      const target = operation.id === operationId ? operation : createFinancialOperation({ id: operationId })
      return { ...target, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    createAccrual: async () => accrual,
    cancelAccrual: async (_token, accrualId, request) => {
      const target = accrual.id === accrualId ? accrual : createAccrual({ id: accrualId })
      return { ...target, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    createSupplierAccrual: async () => supplierAccrual,
    cancelSupplierAccrual: async (_token, supplierAccrualId, request) => {
      const target = supplierAccrual.id === supplierAccrualId ? supplierAccrual : createSupplierAccrual({ id: supplierAccrualId })
      return { ...target, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    generateRegularAccruals: async () => createRegularAccrualGenerationResult({ createdAccruals: [accrual], totalAmount: accrual.amount }),
    createMeterReading: async () => meterReading,
    cancelMeterReading: async (_token, meterReadingId, request) => {
      const target = meterReading.id === meterReadingId ? meterReading : createMeterReading({ id: meterReadingId })
      return { ...target, isCanceled: true, comment: `Отменено: ${request.reason}` }
    },
    ...overrides,
  }
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

  return {
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
        garageDebtBefore: debtBefore,
        garageDebtAfter: debtBefore - request.amount,
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
        supplierName: 'Водоканал',
        expenseTypeName: 'Вода',
        supplierDebtBefore,
        supplierDebtAfter: supplierDebtBefore - request.amount,
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
  }
}

function createStatefulDictionaryClient(): DictionaryClient {
  let lastOwner: OwnerDto | null = null
  let lastGroup: SupplierGroupDto | null = null
  let garages: GarageDto[] = []

  return {
    getOwners: async () => [],
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
    getSupplierGroups: async () => [],
    createSupplierGroup: async (_token, request) => {
      const group = createGroup({ id: crypto.randomUUID(), name: request.name })
      lastGroup = group
      return group
    },
    archiveSupplierGroup: async () => undefined,
    getSuppliers: async () => [],
    createSupplier: async (_token, request) => {
      const group = lastGroup?.id === request.groupId ? lastGroup : createGroup({ id: request.groupId, name: 'Поставщики' })
      return createSupplier({
        id: crypto.randomUUID(),
        name: request.name,
        groupId: group.id,
        groupName: group.name,
        inn: request.inn ?? null,
        startingBalance: request.startingBalance,
      })
    },
    archiveSupplier: async () => undefined,
    getIncomeTypes: async () => [],
    createIncomeType: async (_token, request) => createAccountingType({ id: crypto.randomUUID(), name: request.name, code: request.code ?? null }),
    archiveIncomeType: async () => undefined,
    getExpenseTypes: async () => [],
    createExpenseType: async (_token, request) => createAccountingType({ id: crypto.randomUUID(), name: request.name, code: request.code ?? null }),
    archiveExpenseType: async () => undefined,
    getTariffs: async () => [],
    createTariff: async (_token, request) =>
      createTariff({
        id: crypto.randomUUID(),
        name: request.name,
        calculationBase: request.calculationBase,
        rate: request.rate,
        effectiveFrom: request.effectiveFrom,
        comment: request.comment ?? null,
      }),
    updateTariff: async (_token, id, request) =>
      createTariff({
        id,
        name: request.name,
        calculationBase: request.calculationBase,
        rate: request.rate,
        effectiveFrom: request.effectiveFrom,
        comment: request.comment ?? null,
      }),
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
    isCanceled: false,
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
