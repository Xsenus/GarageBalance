import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from './App'
import type { AuditClient, AuditEventDto } from './services/auditApi'
import type { AuthClient, AuthResponse } from './services/authApi'
import type { AccountingTypeDto, DictionaryClient, GarageDto, OwnerDto, SupplierDto, SupplierGroupDto, TariffDto } from './services/dictionariesApi'
import type { AccrualDto, FinanceClient, FinanceSummaryDto, FinancialOperationDto, MeterReadingDto, RegularAccrualGenerationResultDto, SupplierAccrualDto } from './services/financeApi'
import type { AccessImportRunDto, ImportClient } from './services/importApi'
import type { ConsolidatedReportDto, ExpenseReportDto, IncomeReportDto, ReportClient } from './services/reportsApi'
import type { AppReleaseDto, ReleaseClient } from './services/releasesApi'
import type { ManagedRoleDto, ManagedUserDto, UserManagementClient } from './services/usersApi'

describe('App', () => {
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
    expect(within(financePanel).getAllByText('Гараж 12').length).toBeGreaterThan(0)
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
    await user.selectOptions(within(dictionaryPanel).getByLabelText('Владелец гаража'), within(dictionaryPanel).getByRole('option', { name: 'Петров Петр' }))
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[1])
    expect(await within(dictionaryPanel).findByText('Гараж 21')).toBeInTheDocument()
    expect(within(dictionaryPanel).getAllByText('Петров Петр').length).toBeGreaterThan(0)

    await user.type(within(dictionaryPanel).getByLabelText('Группа поставщиков'), 'Связь')
    await user.click(within(dictionaryPanel).getByRole('button', { name: 'Добавить группу' }))
    await user.type(within(dictionaryPanel).getByLabelText('Название поставщика'), 'Сибирь Онлайн')
    await user.type(within(dictionaryPanel).getByLabelText('ИНН поставщика'), '5401000000')
    await user.click(within(dictionaryPanel).getAllByRole('button', { name: 'Добавить' })[2])
    expect(await within(dictionaryPanel).findByText('Сибирь Онлайн')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Связь, ИНН 5401000000')).toBeInTheDocument()
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
    expect(within(dictionaryPanel).getByText('150 с 2026-07-01')).toBeInTheDocument()
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
    expect(within(financePanel).getByRole('table', { name: 'Последние начисления' })).toBeInTheDocument()
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

    expect(await within(financePanel).findByText('Создано 1, пропущено 0')).toBeInTheDocument()
    expect((await within(financePanel).findAllByText('300,00')).length).toBeGreaterThan(0)
    expect(within(financePanel).getByText('Членский тариф · 300,00')).toBeInTheDocument()
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

    expect(await within(importPanel).findByText('Dry-run завершен с предупреждениями.')).toBeInTheDocument()
    expect(within(importPanel).getByRole('table', { name: 'Проверки импорта' })).toBeInTheDocument()
    expect(within(importPanel).getByText('Формат файла')).toBeInTheDocument()
    expect(within(importPanel).getAllByText('ГСК.accdb').length).toBeGreaterThan(0)

    await user.click(within(importPanel).getByRole('button', { name: 'Скачать отчет JSON' }))

    expect(await within(importPanel).findByText('Отчет dry-run импорта готов.')).toBeInTheDocument()
  })

  it('shows audit journal for users with audit permission', async () => {
    const user = userEvent.setup()
    let auditRequest: Parameters<AuditClient['getEvents']>[1] = undefined
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
  })

  it('shows consolidated report and applies garage search', async () => {
    const user = userEvent.setup()
    const reportClient = createReportClient()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} financeClient={createFinanceClient()} importClient={createImportClient()} reportClient={reportClient} releaseClient={createReleaseClient()} userClient={createUserClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    const reportsPanel = await screen.findByRole('region', { name: 'Отчеты' })

    expect(await within(reportsPanel).findByText('Консолидированный отчет за период')).toBeInTheDocument()
    expect(within(reportsPanel).getAllByText('2 000,00').length).toBeGreaterThan(0)
    expect(within(reportsPanel).getByRole('table', { name: 'Помесячный отчет' })).toBeInTheDocument()
    expect(within(reportsPanel).getAllByText('Гараж 12').length).toBeGreaterThan(0)

    await user.type(within(reportsPanel).getByLabelText('Поиск в отчете'), 'Петров')
    await user.click(within(reportsPanel).getByRole('button', { name: 'Сформировать' }))

    expect(await within(reportsPanel).findByText('Гараж 21')).toBeInTheDocument()

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать сводный XLSX' }))

    expect(await within(reportsPanel).findByText('XLSX по сводному отчету готов.')).toBeInTheDocument()

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать сводный PDF' }))

    expect(await within(reportsPanel).findByText('PDF по сводному отчету готов.')).toBeInTheDocument()
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
    expect(incomeRequest?.garageIds).toEqual(['garage-1'])
    expect(incomeRequest?.ownerIds).toEqual(['owner-1'])
    expect(incomeRequest?.incomeTypeIds).toEqual(['income-type-1'])

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать поступления XLSX' }))

    expect(await within(reportsPanel).findByText('XLSX по поступлениям готов.')).toBeInTheDocument()

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать поступления PDF' }))

    expect(await within(reportsPanel).findByText('PDF по поступлениям готов.')).toBeInTheDocument()
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

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать выплаты XLSX' }))

    expect(await within(reportsPanel).findByText('XLSX по выплатам готов.')).toBeInTheDocument()

    await user.click(within(reportsPanel).getByRole('button', { name: 'Скачать выплаты PDF' }))

    expect(await within(reportsPanel).findByText('PDF по выплатам готов.')).toBeInTheDocument()
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
  })
})

function createAuthClient(overrides: Partial<AuthClient> = {}): AuthClient {
  return {
    bootstrapAdmin: async () => createAuthResponse(),
    login: async () => createAuthResponse(),
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
    getGarages: async () => [garage],
    createGarage: async () => garage,
    getSupplierGroups: async () => [group],
    createSupplierGroup: async () => group,
    getSuppliers: async () => [supplier],
    createSupplier: async () => supplier,
    getIncomeTypes: async () => [incomeType],
    createIncomeType: async () => incomeType,
    getExpenseTypes: async () => [expenseType],
    createExpenseType: async () => expenseType,
    getTariffs: async () => [tariff],
    createTariff: async () => tariff,
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
    createAccrual: async () => accrual,
    createSupplierAccrual: async () => supplierAccrual,
    generateRegularAccruals: async () => createRegularAccrualGenerationResult({ createdAccruals: [accrual], totalAmount: accrual.amount }),
    createMeterReading: async () => meterReading,
    ...overrides,
  }
}

function createImportClient(overrides: Partial<ImportClient> = {}): ImportClient {
  const run = createAccessImportRun()

  return {
    getAccessRuns: async () => [],
    dryRunAccess: async () => run,
    downloadAccessRunReport: async () => new Blob(['{}'], { type: 'application/json' }),
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

  return {
    getAccessRuns: async () => runs,
    dryRunAccess: async (_token, file) => {
      const run = createAccessImportRun({
        id: crypto.randomUUID(),
        originalFileName: file.name,
        fileSizeBytes: file.size,
      })
      runs = [run, ...runs]
      return run
    },
    downloadAccessRunReport: async (_token, runId) => {
      const run = runs.find((item) => item.id === runId)
      return new Blob([JSON.stringify(run ?? {})], { type: 'application/json' })
    },
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
      const operation = createFinancialOperation({
        id: crypto.randomUUID(),
        operationDate: request.operationDate,
        accountingMonth: request.accountingMonth,
        amount: request.amount,
        documentNumber: request.documentNumber ?? null,
      })
      operations = [operation, ...operations]
      return operation
    },
    createExpense: async (_token, request) => {
      const operation = createFinancialOperation({
        id: crypto.randomUUID(),
        operationKind: 'expense',
        operationDate: request.operationDate,
        accountingMonth: request.accountingMonth,
        amount: request.amount,
        documentNumber: request.documentNumber ?? null,
        supplierName: 'Водоканал',
        expenseTypeName: 'Вода',
      })
      operations = [operation, ...operations]
      return operation
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
        comment: request.comment ?? null,
      })
      meterReadings = [reading, ...meterReadings]
      return reading
    },
  }
}

function createStatefulDictionaryClient(): DictionaryClient {
  let lastOwner: OwnerDto | null = null
  let lastGroup: SupplierGroupDto | null = null

  return {
    getOwners: async () => [],
    createOwner: async (_token, request) => {
      const owner = createOwner({ id: crypto.randomUUID(), lastName: request.lastName, firstName: request.firstName, phone: request.phone ?? null })
      lastOwner = owner
      return owner
    },
    getGarages: async () => [],
    createGarage: async (_token, request) => {
      const owner = lastOwner?.id === request.ownerId ? lastOwner : null
      return createGarage({
        id: crypto.randomUUID(),
        number: request.number,
        ownerId: owner?.id ?? null,
        ownerName: owner?.fullName ?? null,
      })
    },
    getSupplierGroups: async () => [],
    createSupplierGroup: async (_token, request) => {
      const group = createGroup({ id: crypto.randomUUID(), name: request.name })
      lastGroup = group
      return group
    },
    getSuppliers: async () => [],
    createSupplier: async (_token, request) => {
      const group = lastGroup?.id === request.groupId ? lastGroup : createGroup({ id: request.groupId, name: 'Поставщики' })
      return createSupplier({
        id: crypto.randomUUID(),
        name: request.name,
        groupId: group.id,
        groupName: group.name,
        inn: request.inn ?? null,
      })
    },
    getIncomeTypes: async () => [],
    createIncomeType: async (_token, request) => createAccountingType({ id: crypto.randomUUID(), name: request.name, code: request.code ?? null }),
    getExpenseTypes: async () => [],
    createExpenseType: async (_token, request) => createAccountingType({ id: crypto.randomUUID(), name: request.name, code: request.code ?? null }),
    getTariffs: async () => [],
    createTariff: async (_token, request) =>
      createTariff({
        id: crypto.randomUUID(),
        name: request.name,
        calculationBase: request.calculationBase,
        rate: request.rate,
        effectiveFrom: request.effectiveFrom,
      }),
  }
}

function createAuthResponse(): AuthResponse {
  return {
    accessToken: 'token',
    expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
    user: {
      id: '5df20dec-2959-4726-a1cb-0e6ec6b28674',
      email: 'admin@example.com',
      displayName: 'Администратор',
      roles: ['administrator'],
      permissions: ['users.manage', 'dictionaries.read', 'dictionaries.write', 'payments.read'],
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
    { code: 'administrator', name: 'Администратор', permissions: ['users.manage'] },
    { code: 'operator', name: 'Оператор', permissions: ['dictionaries.read', 'payments.write'] },
    { code: 'accountant', name: 'Бухгалтер', permissions: ['dictionaries.write', 'payments.write'] },
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
