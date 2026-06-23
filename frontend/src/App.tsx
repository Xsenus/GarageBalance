import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import {
  Bell,
  BookOpenCheck,
  CircleDollarSign,
  DatabaseZap,
  FileText,
  FileSpreadsheet,
  Gauge,
  LockKeyhole,
  LogOut,
  Plus,
  Search,
  Settings,
  ShieldCheck,
  UsersRound,
  WalletCards,
} from 'lucide-react'
import { authApi } from './services/authApi'
import type { AuthClient, AuthResponse } from './services/authApi'
import { dictionariesApi } from './services/dictionariesApi'
import type { AccountingTypeDto, DictionaryClient, GarageDto, OwnerDto, SupplierDto, SupplierGroupDto, TariffDto } from './services/dictionariesApi'
import { financeApi } from './services/financeApi'
import type { AccrualDto, FinanceClient, FinanceSummaryDto, FinancialOperationDto, MeterReadingDto, SupplierAccrualDto } from './services/financeApi'
import { importApi } from './services/importApi'
import type { AccessImportRunDto, ImportClient } from './services/importApi'
import { reportsApi } from './services/reportsApi'
import type { ConsolidatedReportDto, ExpenseReportDto, IncomeReportDto, ReportClient } from './services/reportsApi'
import { releasesApi } from './services/releasesApi'
import type { AppReleaseDto, ReleaseClient } from './services/releasesApi'
import { usersApi } from './services/usersApi'
import type { ManagedRoleDto, ManagedUserDto, UserManagementClient } from './services/usersApi'
import './App.css'

type AppProps = {
  authClient?: AuthClient
  dictionaryClient?: DictionaryClient
  financeClient?: FinanceClient
  importClient?: ImportClient
  reportClient?: ReportClient
  releaseClient?: ReleaseClient
  userClient?: UserManagementClient
}

type IncomeReportFilters = {
  dateFrom: string
  dateTo: string
  search: string
  garageIds: string[]
  ownerIds: string[]
  incomeTypeIds: string[]
  rowMode: string
}

type ExpenseReportFilters = {
  dateFrom: string
  dateTo: string
  search: string
  supplierIds: string[]
  expenseTypeIds: string[]
  rowMode: string
}

const navigation = [
  { label: 'Панель', icon: Gauge, active: true },
  { label: 'Справочники', icon: UsersRound },
  { label: 'Тарифы', icon: Settings },
  { label: 'Платежи', icon: WalletCards },
  { label: 'Отчеты', icon: FileSpreadsheet },
  { label: 'Импорт', icon: DatabaseZap },
]

const roadmap = [
  {
    title: 'Пользователи и права',
    text: 'Вход только для разрешенных сотрудников, роли, журнал действий и защита финансовых разделов.',
    icon: ShieldCheck,
  },
  {
    title: 'Справочники',
    text: 'Гаражи, владельцы и поставщики доступны в защищенной рабочей области с проверками дублей и связей.',
    icon: BookOpenCheck,
  },
  {
    title: 'Импорт Access',
    text: 'Следующий шаг: сопоставление таблиц старой базы и перенос без ручного набора данных.',
    icon: DatabaseZap,
  },
  {
    title: 'Платежи и начисления',
    text: 'Помесячные строки, счетчики, регулярные начисления, корректировки и задолженность.',
    icon: CircleDollarSign,
  },
]

function App({ authClient = authApi, dictionaryClient = dictionariesApi, financeClient = financeApi, importClient = importApi, reportClient = reportsApi, releaseClient = releasesApi, userClient = usersApi }: AppProps) {
  const [auth, setAuth] = useState<AuthResponse | null>(null)

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark">G</div>
          <div>
            <strong>GarageBalance</strong>
            <span>учет гаражного кооператива</span>
          </div>
        </div>

        <nav className="nav-list" aria-label="Основные разделы" aria-disabled={!auth}>
          {navigation.map((item) => {
            const Icon = item.icon
            return (
              <button className={item.active ? 'nav-item active' : 'nav-item'} type="button" key={item.label} disabled={!auth}>
                <Icon size={18} />
                <span>{item.label}</span>
              </button>
            )
          })}
        </nav>

        <div className="sidebar-footer">
          <LockKeyhole size={18} />
          <div>
            <strong>Безопасный старт</strong>
            <span>первый этап начинается с ролей и доступа</span>
          </div>
        </div>
      </aside>

      <section className="workspace">
        {auth ? (
          <Workspace auth={auth} dictionaryClient={dictionaryClient} financeClient={financeClient} importClient={importClient} reportClient={reportClient} releaseClient={releaseClient} userClient={userClient} onLogout={() => setAuth(null)} />
        ) : (
          <AuthGate authClient={authClient} onAuthenticated={setAuth} />
        )}
      </section>
    </main>
  )
}

function AuthGate({ authClient, onAuthenticated }: { authClient: AuthClient; onAuthenticated: (auth: AuthResponse) => void }) {
  const [mode, setMode] = useState<'bootstrap' | 'login'>('bootstrap')
  const [email, setEmail] = useState('admin@example.com')
  const [displayName, setDisplayName] = useState('Администратор')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setLoading(true)

    try {
      const response =
        mode === 'bootstrap'
          ? await authClient.bootstrapAdmin({ email, displayName, password })
          : await authClient.login({ email, password })
      setPassword('')
      onAuthenticated(response)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось выполнить вход.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <section className="auth-layout" aria-label="Вход в систему">
      <div className="auth-copy">
        <p className="eyebrow">Безопасность</p>
        <h1>Сначала вход и права, потом деньги и импорт</h1>
        <p className="lead">
          Система не открывает рабочие разделы без пользователя. Первый запуск создает администратора, дальше вход идет по учетной записи.
        </p>
      </div>

      <form className="auth-card" onSubmit={handleSubmit}>
        <div className="auth-tabs" role="tablist" aria-label="Режим входа">
          <button type="button" className={mode === 'bootstrap' ? 'active' : ''} onClick={() => setMode('bootstrap')}>
            Первый администратор
          </button>
          <button type="button" className={mode === 'login' ? 'active' : ''} onClick={() => setMode('login')}>
            Вход
          </button>
        </div>

        <label>
          Email
          <input value={email} onChange={(event) => setEmail(event.target.value)} type="email" required />
        </label>

        {mode === 'bootstrap' ? (
          <label>
            Имя пользователя
            <input value={displayName} onChange={(event) => setDisplayName(event.target.value)} required />
          </label>
        ) : null}

        <label>
          Пароль
          <input value={password} onChange={(event) => setPassword(event.target.value)} type="password" minLength={8} required />
        </label>

        {error ? <div className="form-error">{error}</div> : null}

        <button className="primary-button" type="submit" disabled={loading}>
          {loading ? 'Проверяем...' : mode === 'bootstrap' ? 'Создать администратора' : 'Войти'}
        </button>
      </form>
    </section>
  )
}

function Workspace({
  auth,
  dictionaryClient,
  financeClient,
  importClient,
  reportClient,
  releaseClient,
  userClient,
  onLogout,
}: {
  auth: AuthResponse
  dictionaryClient: DictionaryClient
  financeClient: FinanceClient
  importClient: ImportClient
  reportClient: ReportClient
  releaseClient: ReleaseClient
  userClient: UserManagementClient
  onLogout: () => void
}) {
  return (
    <>
      <header className="topbar">
        <div className="search">
          <Search size={18} />
          <span>Поиск по гаражу, владельцу или поставщику</span>
        </div>
        <div className="user-panel">
          <div>
            <strong>{auth.user.displayName}</strong>
            <span>{auth.user.roles.join(', ')}</span>
          </div>
          <button className="icon-button" type="button" aria-label="Уведомления">
            <Bell size={19} />
          </button>
          <button className="icon-button" type="button" aria-label="Выйти" onClick={onLogout}>
            <LogOut size={19} />
          </button>
        </div>
      </header>

      <section className="hero-panel">
        <div>
          <p className="eyebrow">Старт проекта</p>
          <h1>Финансовый учет ГСК без ручного переноса старой базы</h1>
          <p className="lead">
            Основа проекта уже разложена под пользователей, справочники, тарифы, платежи, отчеты и импорт Access.
          </p>
        </div>
        <div className="status-stack" aria-label="Ключевые статусы">
          <div>
            <span>Этап 1</span>
            <strong>ядро учета</strong>
          </div>
          <div>
            <span>Права</span>
            <strong>{auth.user.permissions.length} доступов</strong>
          </div>
          <div>
            <span>Docker</span>
            <strong>готовится сразу</strong>
          </div>
        </div>
      </section>

      <UserManagementPanel auth={auth} userClient={userClient} />

      <DictionaryPanel auth={auth} dictionaryClient={dictionaryClient} />

      <FinancePanel auth={auth} dictionaryClient={dictionaryClient} financeClient={financeClient} />

      <ImportPanel auth={auth} importClient={importClient} />

      <ReportPanel auth={auth} dictionaryClient={dictionaryClient} reportClient={reportClient} />

      <section className="roadmap-grid" aria-label="Ближайшая очередь">
        {roadmap.map((item) => {
          const Icon = item.icon
          return (
            <article className="work-card" key={item.title}>
              <Icon size={22} />
              <h2>{item.title}</h2>
              <p>{item.text}</p>
            </article>
          )
        })}
      </section>

      <ReleasePanel auth={auth} releaseClient={releaseClient} />
    </>
  )
}

function ReleasePanel({ auth, releaseClient }: { auth: AuthResponse; releaseClient: ReleaseClient }) {
  const [releases, setReleases] = useState<AppReleaseDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let ignore = false

    async function loadReleases() {
      setLoading(true)
      setError(null)

      try {
        const nextReleases = await releaseClient.getReleases(auth.accessToken, 10)
        if (!ignore) {
          setReleases(nextReleases)
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить историю обновлений.')
        }
      } finally {
        if (!ignore) {
          setLoading(false)
        }
      }
    }

    void loadReleases()

    return () => {
      ignore = true
    }
  }, [auth.accessToken, releaseClient])

  return (
    <section className="release-panel" aria-label="Что нового">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">Что нового</p>
          <h2>История обновлений</h2>
        </div>
        <span>{releases.length} версий</span>
      </div>

      {loading ? <p className="muted">Загружаем историю обновлений...</p> : null}
      {error ? <div className="form-error">{error}</div> : null}
      {!loading && !error && releases.length === 0 ? <p className="muted">Пока нет опубликованных изменений.</p> : null}

      {!loading && !error && releases.length > 0 ? (
        <div className="release-list">
          {releases.map((release) => (
            <article className="release-entry" key={release.releaseId}>
              <div className="release-entry__header">
                <div>
                  <h3>{release.title}</h3>
                  <p>{release.summary}</p>
                </div>
                <span>
                  v{release.version} · {formatReleaseDate(release.publishedAt)}
                </span>
              </div>
              <ul>
                {release.items.map((item) => (
                  <li className={`release-item release-item--${item.type}`} key={`${release.releaseId}-${item.type}-${item.text}`}>
                    {item.text}
                  </li>
                ))}
              </ul>
            </article>
          ))}
        </div>
      ) : null}
    </section>
  )
}

function FinancePanel({
  auth,
  dictionaryClient,
  financeClient,
}: {
  auth: AuthResponse
  dictionaryClient: DictionaryClient
  financeClient: FinanceClient
}) {
  const today = new Date().toISOString().slice(0, 10)
  const month = `${today.slice(0, 7)}-01`
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [tariffs, setTariffs] = useState<TariffDto[]>([])
  const [operations, setOperations] = useState<FinancialOperationDto[]>([])
  const [accruals, setAccruals] = useState<AccrualDto[]>([])
  const [supplierAccruals, setSupplierAccruals] = useState<SupplierAccrualDto[]>([])
  const [meterReadings, setMeterReadings] = useState<MeterReadingDto[]>([])
  const [summary, setSummary] = useState<FinanceSummaryDto>({ incomeTotal: 0, expenseTotal: 0, accrualTotal: 0, balance: 0, debt: 0, operationCount: 0, accrualCount: 0, meterReadingCount: 0 })
  const [incomeForm, setIncomeForm] = useState({ garageId: '', incomeTypeId: '', operationDate: today, accountingMonth: month, amount: 0, documentNumber: '' })
  const [expenseForm, setExpenseForm] = useState({ supplierId: '', expenseTypeId: '', operationDate: today, accountingMonth: month, amount: 0, documentNumber: '' })
  const [accrualForm, setAccrualForm] = useState({ garageId: '', incomeTypeId: '', accountingMonth: month, amount: 0, comment: '' })
  const [supplierAccrualForm, setSupplierAccrualForm] = useState({ supplierId: '', expenseTypeId: '', accountingMonth: month, amount: 0, documentNumber: '', comment: '' })
  const [regularForm, setRegularForm] = useState({ incomeTypeId: '', tariffId: '', accountingMonth: month, comment: '' })
  const [regularStatus, setRegularStatus] = useState<string | null>(null)
  const [meterForm, setMeterForm] = useState({ garageId: '', meterKind: 'water' as 'water' | 'electricity', accountingMonth: month, readingDate: today, currentValue: 0, comment: '' })
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let ignore = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const [loadedGarages, loadedSuppliers, loadedIncomeTypes, loadedExpenseTypes, loadedTariffs, loadedOperations, loadedAccruals, loadedSupplierAccruals, loadedMeterReadings, loadedSummary] = await Promise.all([
          dictionaryClient.getGarages(auth.accessToken),
          dictionaryClient.getSuppliers(auth.accessToken),
          dictionaryClient.getIncomeTypes(auth.accessToken),
          dictionaryClient.getExpenseTypes(auth.accessToken),
          dictionaryClient.getTariffs(auth.accessToken),
          financeClient.getOperations(auth.accessToken),
          financeClient.getAccruals(auth.accessToken),
          financeClient.getSupplierAccruals(auth.accessToken),
          financeClient.getMeterReadings(auth.accessToken),
          financeClient.getSummary(auth.accessToken),
        ])
        if (!ignore) {
          setGarages(loadedGarages)
          setSuppliers(loadedSuppliers)
          setIncomeTypes(loadedIncomeTypes)
          setExpenseTypes(loadedExpenseTypes)
          setTariffs(loadedTariffs)
          setOperations(loadedOperations)
          setAccruals(loadedAccruals)
          setSupplierAccruals(loadedSupplierAccruals)
          setMeterReadings(loadedMeterReadings)
          setSummary(loadedSummary)
          setIncomeForm((value) => ({ ...value, garageId: value.garageId || loadedGarages[0]?.id || '', incomeTypeId: value.incomeTypeId || loadedIncomeTypes[0]?.id || '' }))
          setExpenseForm((value) => ({ ...value, supplierId: value.supplierId || loadedSuppliers[0]?.id || '', expenseTypeId: value.expenseTypeId || loadedExpenseTypes[0]?.id || '' }))
          setAccrualForm((value) => ({ ...value, garageId: value.garageId || loadedGarages[0]?.id || '', incomeTypeId: value.incomeTypeId || loadedIncomeTypes[0]?.id || '' }))
          setSupplierAccrualForm((value) => ({ ...value, supplierId: value.supplierId || loadedSuppliers[0]?.id || '', expenseTypeId: value.expenseTypeId || loadedExpenseTypes[0]?.id || '' }))
          setRegularForm((value) => ({ ...value, incomeTypeId: value.incomeTypeId || loadedIncomeTypes[0]?.id || '', tariffId: value.tariffId || loadedTariffs[0]?.id || '' }))
          setMeterForm((value) => ({ ...value, garageId: value.garageId || loadedGarages[0]?.id || '' }))
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить платежи.')
        }
      } finally {
        if (!ignore) {
          setLoading(false)
        }
      }
    }

    void load()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, dictionaryClient, financeClient])

  async function saveIncome(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await runSaving('income', async () => {
      const operation = await financeClient.createIncome(auth.accessToken, {
        garageId: incomeForm.garageId,
        incomeTypeId: incomeForm.incomeTypeId,
        operationDate: incomeForm.operationDate,
        accountingMonth: incomeForm.accountingMonth,
        amount: incomeForm.amount,
        documentNumber: incomeForm.documentNumber,
      })
      addOperation(operation)
      setIncomeForm((value) => ({ ...value, amount: 0, documentNumber: '' }))
    })
  }

  async function saveExpense(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await runSaving('expense', async () => {
      const operation = await financeClient.createExpense(auth.accessToken, {
        supplierId: expenseForm.supplierId,
        expenseTypeId: expenseForm.expenseTypeId,
        operationDate: expenseForm.operationDate,
        accountingMonth: expenseForm.accountingMonth,
        amount: expenseForm.amount,
        documentNumber: expenseForm.documentNumber,
      })
      addOperation(operation)
      setExpenseForm((value) => ({ ...value, amount: 0, documentNumber: '' }))
    })
  }

  async function saveAccrual(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await runSaving('accrual', async () => {
      const accrual = await financeClient.createAccrual(auth.accessToken, {
        garageId: accrualForm.garageId,
        incomeTypeId: accrualForm.incomeTypeId,
        accountingMonth: accrualForm.accountingMonth,
        amount: accrualForm.amount,
        source: 'manual',
        comment: accrualForm.comment,
      })
      setAccruals((items) => [accrual, ...items])
      setSummary((value) => ({
        ...value,
        accrualTotal: value.accrualTotal + accrual.amount,
        debt: value.debt + accrual.amount,
        accrualCount: value.accrualCount + 1,
      }))
      setAccrualForm((value) => ({ ...value, amount: 0, comment: '' }))
    })
  }

  async function saveRegularAccruals(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await runSaving('regular-accruals', async () => {
      const result = await financeClient.generateRegularAccruals(auth.accessToken, {
        incomeTypeId: regularForm.incomeTypeId,
        tariffId: regularForm.tariffId,
        accountingMonth: regularForm.accountingMonth,
        comment: regularForm.comment,
      })
      setAccruals((items) => [...result.createdAccruals, ...items])
      setSummary((value) => ({
        ...value,
        accrualTotal: value.accrualTotal + result.totalAmount,
        debt: value.debt + result.totalAmount,
        accrualCount: value.accrualCount + result.createdCount,
      }))
      setRegularStatus(`Создано ${result.createdCount}, пропущено ${result.skippedCount}`)
      setRegularForm((value) => ({ ...value, comment: '' }))
    })
  }

  async function saveSupplierAccrual(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await runSaving('supplier-accrual', async () => {
      const accrual = await financeClient.createSupplierAccrual(auth.accessToken, {
        supplierId: supplierAccrualForm.supplierId,
        expenseTypeId: supplierAccrualForm.expenseTypeId,
        accountingMonth: supplierAccrualForm.accountingMonth,
        amount: supplierAccrualForm.amount,
        source: 'manual',
        documentNumber: supplierAccrualForm.documentNumber,
        comment: supplierAccrualForm.comment,
      })
      setSupplierAccruals((items) => [accrual, ...items])
      setSupplierAccrualForm((value) => ({ ...value, amount: 0, documentNumber: '', comment: '' }))
    })
  }

  async function saveMeterReading(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await runSaving('meter-reading', async () => {
      const reading = await financeClient.createMeterReading(auth.accessToken, {
        garageId: meterForm.garageId,
        meterKind: meterForm.meterKind,
        accountingMonth: meterForm.accountingMonth,
        readingDate: meterForm.readingDate,
        currentValue: meterForm.currentValue,
        comment: meterForm.comment,
      })
      setMeterReadings((items) => [reading, ...items])
      setSummary((value) => ({ ...value, meterReadingCount: value.meterReadingCount + 1 }))
      setMeterForm((value) => ({ ...value, currentValue: 0, comment: '' }))
    })
  }

  function addOperation(operation: FinancialOperationDto) {
    setOperations((items) => [operation, ...items])
    setSummary((value) => {
      const incomeDelta = operation.operationKind === 'income' ? operation.amount : 0
      const expenseDelta = operation.operationKind === 'expense' ? operation.amount : 0
      return {
        incomeTotal: value.incomeTotal + incomeDelta,
        expenseTotal: value.expenseTotal + expenseDelta,
        balance: value.balance + incomeDelta - expenseDelta,
        debt: value.debt - incomeDelta,
        operationCount: value.operationCount + 1,
        accrualTotal: value.accrualTotal,
        accrualCount: value.accrualCount,
        meterReadingCount: value.meterReadingCount,
      }
    })
  }

  async function runSaving(scope: string, action: () => Promise<void>) {
    setSaving(scope)
    setError(null)
    try {
      await action()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось сохранить финансовую операцию.')
    } finally {
      setSaving(null)
    }
  }

  return (
    <section className="finance-panel" aria-label="Платежи">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Платежи</p>
          <h2>Поступления владельцев и выплаты поставщикам</h2>
        </div>
        <span>{loading ? 'Загрузка...' : `${summary.operationCount} операций`}</span>
      </div>

      {error ? <div className="form-error">{error}</div> : null}

      <div className="summary-strip" aria-label="Итоги платежей">
        <div>
          <span>Поступления</span>
          <strong>{formatMoney(summary.incomeTotal)}</strong>
        </div>
        <div>
          <span>Начислено</span>
          <strong>{formatMoney(summary.accrualTotal)}</strong>
        </div>
        <div>
          <span>Задолженность</span>
          <strong>{formatMoney(summary.debt)}</strong>
        </div>
        <div>
          <span>Выплаты</span>
          <strong>{formatMoney(summary.expenseTotal)}</strong>
        </div>
        <div>
          <span>Баланс</span>
          <strong>{formatMoney(summary.balance)}</strong>
        </div>
        <div>
          <span>Счетчики</span>
          <strong>{summary.meterReadingCount}</strong>
        </div>
      </div>

      <div className="finance-grid">
        <form className="dictionary-form" onSubmit={saveIncome}>
          <h3>Новое поступление</h3>
          <select aria-label="Гараж для поступления" value={incomeForm.garageId} onChange={(event) => setIncomeForm({ ...incomeForm, garageId: event.target.value })} required>
            <option value="" disabled>
              Выберите гараж
            </option>
            {garages.map((garage) => (
              <option value={garage.id} key={garage.id}>
                Гараж {garage.number}
              </option>
            ))}
          </select>
          <select aria-label="Вид поступления для платежа" value={incomeForm.incomeTypeId} onChange={(event) => setIncomeForm({ ...incomeForm, incomeTypeId: event.target.value })} required>
            <option value="" disabled>
              Выберите вид
            </option>
            {incomeTypes.map((item) => (
              <option value={item.id} key={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          <div className="inline-fields">
            <input aria-label="Дата поступления" type="date" value={incomeForm.operationDate} onChange={(event) => setIncomeForm({ ...incomeForm, operationDate: event.target.value })} required />
            <input aria-label="Месяц поступления" type="month" value={incomeForm.accountingMonth.slice(0, 7)} onChange={(event) => setIncomeForm({ ...incomeForm, accountingMonth: `${event.target.value}-01` })} required />
          </div>
          <div className="inline-fields">
            <input aria-label="Сумма поступления" type="number" min="0.01" step="0.01" value={incomeForm.amount} onChange={(event) => setIncomeForm({ ...incomeForm, amount: Number(event.target.value) })} required />
            <input aria-label="Документ поступления" placeholder="Документ" value={incomeForm.documentNumber} onChange={(event) => setIncomeForm({ ...incomeForm, documentNumber: event.target.value })} />
          </div>
          <button className="secondary-button" type="submit" disabled={saving === 'income' || !incomeForm.garageId || !incomeForm.incomeTypeId}>
            <Plus size={16} />
            <span>Провести</span>
          </button>
        </form>

        <form className="dictionary-form" onSubmit={saveExpense}>
          <h3>Новая выплата</h3>
          <select aria-label="Поставщик для выплаты" value={expenseForm.supplierId} onChange={(event) => setExpenseForm({ ...expenseForm, supplierId: event.target.value })} required>
            <option value="" disabled>
              Выберите поставщика
            </option>
            {suppliers.map((supplier) => (
              <option value={supplier.id} key={supplier.id}>
                {supplier.name}
              </option>
            ))}
          </select>
          <select aria-label="Вид выплаты для платежа" value={expenseForm.expenseTypeId} onChange={(event) => setExpenseForm({ ...expenseForm, expenseTypeId: event.target.value })} required>
            <option value="" disabled>
              Выберите вид
            </option>
            {expenseTypes.map((item) => (
              <option value={item.id} key={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          <div className="inline-fields">
            <input aria-label="Дата выплаты" type="date" value={expenseForm.operationDate} onChange={(event) => setExpenseForm({ ...expenseForm, operationDate: event.target.value })} required />
            <input aria-label="Месяц выплаты" type="month" value={expenseForm.accountingMonth.slice(0, 7)} onChange={(event) => setExpenseForm({ ...expenseForm, accountingMonth: `${event.target.value}-01` })} required />
          </div>
          <div className="inline-fields">
            <input aria-label="Сумма выплаты" type="number" min="0.01" step="0.01" value={expenseForm.amount} onChange={(event) => setExpenseForm({ ...expenseForm, amount: Number(event.target.value) })} required />
            <input aria-label="Документ выплаты" placeholder="Документ" value={expenseForm.documentNumber} onChange={(event) => setExpenseForm({ ...expenseForm, documentNumber: event.target.value })} />
          </div>
          <button className="secondary-button" type="submit" disabled={saving === 'expense' || !expenseForm.supplierId || !expenseForm.expenseTypeId}>
            <Plus size={16} />
            <span>Провести</span>
          </button>
        </form>

        <form className="dictionary-form" onSubmit={saveAccrual}>
          <h3>Ручное начисление</h3>
          <select aria-label="Гараж для начисления" value={accrualForm.garageId} onChange={(event) => setAccrualForm({ ...accrualForm, garageId: event.target.value })} required>
            <option value="" disabled>
              Выберите гараж
            </option>
            {garages.map((garage) => (
              <option value={garage.id} key={garage.id}>
                Гараж {garage.number}
              </option>
            ))}
          </select>
          <select aria-label="Вид начисления" value={accrualForm.incomeTypeId} onChange={(event) => setAccrualForm({ ...accrualForm, incomeTypeId: event.target.value })} required>
            <option value="" disabled>
              Выберите вид
            </option>
            {incomeTypes.map((item) => (
              <option value={item.id} key={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          <div className="inline-fields">
            <input aria-label="Месяц начисления" type="month" value={accrualForm.accountingMonth.slice(0, 7)} onChange={(event) => setAccrualForm({ ...accrualForm, accountingMonth: `${event.target.value}-01` })} required />
            <input aria-label="Сумма начисления" type="number" min="0.01" step="0.01" value={accrualForm.amount} onChange={(event) => setAccrualForm({ ...accrualForm, amount: Number(event.target.value) })} required />
          </div>
          <input aria-label="Комментарий начисления" placeholder="Комментарий" value={accrualForm.comment} onChange={(event) => setAccrualForm({ ...accrualForm, comment: event.target.value })} required />
          <button className="secondary-button" type="submit" disabled={saving === 'accrual' || !accrualForm.garageId || !accrualForm.incomeTypeId}>
            <Plus size={16} />
            <span>Начислить</span>
          </button>
        </form>

        <form className="dictionary-form" onSubmit={saveSupplierAccrual}>
          <h3>Начисление поставщику</h3>
          <select aria-label="Поставщик для начисления" value={supplierAccrualForm.supplierId} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, supplierId: event.target.value })} required>
            <option value="" disabled>
              Выберите поставщика
            </option>
            {suppliers.map((supplier) => (
              <option value={supplier.id} key={supplier.id}>
                {supplier.name}
              </option>
            ))}
          </select>
          <select aria-label="Вид начисления поставщику" value={supplierAccrualForm.expenseTypeId} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, expenseTypeId: event.target.value })} required>
            <option value="" disabled>
              Выберите вид
            </option>
            {expenseTypes.map((item) => (
              <option value={item.id} key={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          <div className="inline-fields">
            <input aria-label="Месяц начисления поставщику" type="month" value={supplierAccrualForm.accountingMonth.slice(0, 7)} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, accountingMonth: `${event.target.value}-01` })} required />
            <input aria-label="Сумма начисления поставщику" type="number" min="0.01" step="0.01" value={supplierAccrualForm.amount} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, amount: Number(event.target.value) })} required />
          </div>
          <div className="inline-fields">
            <input aria-label="Документ начисления поставщику" placeholder="Документ" value={supplierAccrualForm.documentNumber} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, documentNumber: event.target.value })} />
            <input aria-label="Комментарий начисления поставщику" placeholder="Комментарий" value={supplierAccrualForm.comment} onChange={(event) => setSupplierAccrualForm({ ...supplierAccrualForm, comment: event.target.value })} required />
          </div>
          <button className="secondary-button" type="submit" disabled={saving === 'supplier-accrual' || !supplierAccrualForm.supplierId || !supplierAccrualForm.expenseTypeId}>
            <Plus size={16} />
            <span>Начислить</span>
          </button>
        </form>

        <form className="dictionary-form" onSubmit={saveRegularAccruals}>
          <h3>Регулярные начисления</h3>
          <select aria-label="Вид регулярного начисления" value={regularForm.incomeTypeId} onChange={(event) => setRegularForm({ ...regularForm, incomeTypeId: event.target.value })} required>
            <option value="" disabled>
              Выберите вид
            </option>
            {incomeTypes.map((item) => (
              <option value={item.id} key={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          <select aria-label="Тариф регулярного начисления" value={regularForm.tariffId} onChange={(event) => setRegularForm({ ...regularForm, tariffId: event.target.value })} required>
            <option value="" disabled>
              Выберите тариф
            </option>
            {tariffs.map((tariff) => (
              <option value={tariff.id} key={tariff.id}>
                {tariff.name} · {formatMoney(tariff.rate)}
              </option>
            ))}
          </select>
          <input aria-label="Месяц регулярных начислений" type="month" value={regularForm.accountingMonth.slice(0, 7)} onChange={(event) => setRegularForm({ ...regularForm, accountingMonth: `${event.target.value}-01` })} required />
          <input aria-label="Комментарий регулярных начислений" placeholder="Комментарий" value={regularForm.comment} onChange={(event) => setRegularForm({ ...regularForm, comment: event.target.value })} />
          <button className="secondary-button" type="submit" disabled={saving === 'regular-accruals' || !regularForm.incomeTypeId || !regularForm.tariffId}>
            <Plus size={16} />
            <span>Создать месяц</span>
          </button>
          {regularStatus ? <p className="empty-state">{regularStatus}</p> : null}
        </form>

        <form className="dictionary-form" onSubmit={saveMeterReading}>
          <h3>Показание счетчика</h3>
          <select aria-label="Гараж для счетчика" value={meterForm.garageId} onChange={(event) => setMeterForm({ ...meterForm, garageId: event.target.value })} required>
            <option value="" disabled>
              Выберите гараж
            </option>
            {garages.map((garage) => (
              <option value={garage.id} key={garage.id}>
                Гараж {garage.number}
              </option>
            ))}
          </select>
          <select aria-label="Тип счетчика" value={meterForm.meterKind} onChange={(event) => setMeterForm({ ...meterForm, meterKind: event.target.value as 'water' | 'electricity' })} required>
            <option value="water">Вода</option>
            <option value="electricity">Электричество</option>
          </select>
          <div className="inline-fields">
            <input aria-label="Месяц показания" type="month" value={meterForm.accountingMonth.slice(0, 7)} onChange={(event) => setMeterForm({ ...meterForm, accountingMonth: `${event.target.value}-01` })} required />
            <input aria-label="Дата показания" type="date" value={meterForm.readingDate} onChange={(event) => setMeterForm({ ...meterForm, readingDate: event.target.value })} required />
          </div>
          <div className="inline-fields">
            <input aria-label="Новое показание" type="number" min="0" step="0.001" value={meterForm.currentValue} onChange={(event) => setMeterForm({ ...meterForm, currentValue: Number(event.target.value) })} required />
            <input aria-label="Комментарий счетчика" placeholder="Комментарий" value={meterForm.comment} onChange={(event) => setMeterForm({ ...meterForm, comment: event.target.value })} />
          </div>
          <button className="secondary-button" type="submit" disabled={saving === 'meter-reading' || !meterForm.garageId}>
            <Plus size={16} />
            <span>Внести</span>
          </button>
        </form>

        <div className="operation-list" role="table" aria-label="Последние платежи">
          <div className="operation-row header" role="row">
            <span role="columnheader">Дата</span>
            <span role="columnheader">Операция</span>
            <span role="columnheader">Сумма</span>
          </div>
          {operations.length === 0 ? <p className="empty-state">Операций пока нет</p> : null}
          {operations.slice(0, 8).map((operation) => (
            <div className="operation-row" role="row" key={operation.id}>
              <span role="cell">{operation.operationDate}</span>
              <span role="cell">
                <strong>{operation.operationKind === 'income' ? operation.incomeTypeName : operation.expenseTypeName}</strong>
                <small>{operation.operationKind === 'income' ? `Гараж ${operation.garageNumber}` : operation.supplierName}</small>
              </span>
              <span role="cell" className={operation.operationKind === 'income' ? 'money-income' : 'money-expense'}>
                {operation.operationKind === 'income' ? '+' : '-'}
                {formatMoney(operation.amount)}
              </span>
            </div>
          ))}
        </div>

        <div className="operation-list" role="table" aria-label="Последние начисления">
          <div className="operation-row header" role="row">
            <span role="columnheader">Месяц</span>
            <span role="columnheader">Начисление</span>
            <span role="columnheader">Сумма</span>
          </div>
          {accruals.length === 0 ? <p className="empty-state">Начислений пока нет</p> : null}
          {accruals.slice(0, 8).map((accrual) => (
            <div className="operation-row" role="row" key={accrual.id}>
              <span role="cell">{accrual.accountingMonth.slice(0, 7)}</span>
              <span role="cell">
                <strong>{accrual.incomeTypeName}</strong>
                <small>Гараж {accrual.garageNumber}</small>
              </span>
              <span role="cell" className="money-accrual">
                {formatMoney(accrual.amount)}
              </span>
            </div>
          ))}
        </div>

        <div className="operation-list" role="table" aria-label="Последние начисления поставщикам">
          <div className="operation-row header" role="row">
            <span role="columnheader">Месяц</span>
            <span role="columnheader">Поставщик</span>
            <span role="columnheader">Сумма</span>
          </div>
          {supplierAccruals.length === 0 ? <p className="empty-state">Начислений поставщикам пока нет</p> : null}
          {supplierAccruals.slice(0, 8).map((accrual) => (
            <div className="operation-row" role="row" key={accrual.id}>
              <span role="cell">{accrual.accountingMonth.slice(0, 7)}</span>
              <span role="cell">
                <strong>{accrual.supplierName}</strong>
                <small>{accrual.expenseTypeName}</small>
              </span>
              <span role="cell" className="money-expense">
                {formatMoney(accrual.amount)}
              </span>
            </div>
          ))}
        </div>

        <div className="operation-list" role="table" aria-label="Последние показания">
          <div className="operation-row header" role="row">
            <span role="columnheader">Месяц</span>
            <span role="columnheader">Счетчик</span>
            <span role="columnheader">Расход</span>
          </div>
          {meterReadings.length === 0 ? <p className="empty-state">Показаний пока нет</p> : null}
          {meterReadings.slice(0, 8).map((reading) => (
            <div className="operation-row" role="row" key={reading.id}>
              <span role="cell">{reading.accountingMonth.slice(0, 7)}</span>
              <span role="cell">
                <strong>{reading.meterKind === 'water' ? 'Вода' : 'Электричество'}</strong>
                <small>
                  Гараж {reading.garageNumber}: {reading.previousValue} → {reading.currentValue}
                </small>
                {reading.hasGapWarning ? <small className="warning-text">нет предыдущего периода</small> : null}
              </span>
              <span role="cell" className="money-accrual">
                {reading.consumption}
              </span>
            </div>
          ))}
        </div>
      </div>
    </section>
  )
}

function ImportPanel({ auth, importClient }: { auth: AuthResponse; importClient: ImportClient }) {
  const [runs, setRuns] = useState<AccessImportRunDto[]>([])
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [currentRun, setCurrentRun] = useState<AccessImportRunDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let ignore = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const loadedRuns = await importClient.getAccessRuns(auth.accessToken)
        if (!ignore) {
          setRuns(loadedRuns)
          setCurrentRun(loadedRuns[0] ?? null)
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить историю импорта.')
        }
      } finally {
        if (!ignore) {
          setLoading(false)
        }
      }
    }

    void load()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, importClient])

  async function runDryRun(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    if (!selectedFile) {
      setError('Выберите файл Access для проверки.')
      return
    }

    setSaving(true)
    setError(null)
    try {
      const run = await importClient.dryRunAccess(auth.accessToken, selectedFile)
      setCurrentRun(run)
      setRuns((items) => [run, ...items.filter((item) => item.id !== run.id)])
      setSelectedFile(null)
      form.reset()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось выполнить dry-run импорта.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="dictionary-panel" aria-label="Импорт Access">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Импорт</p>
          <h2>Проверка старой базы Access перед переносом</h2>
        </div>
        <span>{loading ? 'Загрузка...' : `${runs.length} запусков`}</span>
      </div>

      {error ? <div className="form-error">{error}</div> : null}

      <div className="finance-grid">
        <form className="dictionary-form" onSubmit={runDryRun}>
          <h3>Dry-run Access</h3>
          <input aria-label="Файл Access" type="file" accept=".accdb,.mdb" onChange={(event) => setSelectedFile(event.target.files?.[0] ?? null)} />
          <button className="secondary-button" type="submit" disabled={saving || !selectedFile}>
            <DatabaseZap size={16} />
            <span>Проверить файл</span>
          </button>
          {selectedFile ? <p className="empty-state">{selectedFile.name}</p> : null}
        </form>

        <div className="operation-list" role="table" aria-label="Проверки импорта">
          <div className="operation-row header" role="row">
            <span role="columnheader">Проверка</span>
            <span role="columnheader">Статус</span>
            <span role="columnheader">Итог</span>
          </div>
          {!currentRun ? <p className="empty-state">Проверок пока нет</p> : null}
          {currentRun?.checks.map((check) => (
            <div className="operation-row" role="row" key={check.code}>
              <span role="cell">
                <strong>{check.title}</strong>
                <small>{check.message}</small>
              </span>
              <span role="cell" className={check.status === 'passed' ? 'status-active' : check.status === 'warning' ? 'warning-text' : 'status-disabled'}>
                {check.status}
              </span>
              <span role="cell">{currentRun.originalFileName}</span>
            </div>
          ))}
        </div>

        <div className="operation-list" role="table" aria-label="История импорта Access">
          <div className="operation-row header" role="row">
            <span role="columnheader">Файл</span>
            <span role="columnheader">Статус</span>
            <span role="columnheader">Проверки</span>
          </div>
          {runs.length === 0 ? <p className="empty-state">Истории импорта пока нет</p> : null}
          {runs.slice(0, 8).map((run) => (
            <button className="operation-row" role="row" type="button" key={run.id} onClick={() => setCurrentRun(run)}>
              <span role="cell">
                <strong>{run.originalFileName}</strong>
                <small>{run.summary}</small>
              </span>
              <span role="cell" className={run.status === 'completed' ? 'status-active' : 'status-disabled'}>
                {run.status}
              </span>
              <span role="cell">
                {run.passedChecks}/{run.totalChecks}
              </span>
            </button>
          ))}
        </div>
      </div>
    </section>
  )
}

function ReportPanel({ auth, dictionaryClient, reportClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient; reportClient: ReportClient }) {
  const today = new Date().toISOString().slice(0, 10)
  const month = `${today.slice(0, 7)}-01`
  const [filters, setFilters] = useState({ monthFrom: month, monthTo: month, search: '' })
  const [incomeFilters, setIncomeFilters] = useState<IncomeReportFilters>({ dateFrom: month, dateTo: today, search: '', garageIds: [], ownerIds: [], incomeTypeIds: [], rowMode: 'all' })
  const [expenseFilters, setExpenseFilters] = useState<ExpenseReportFilters>({ dateFrom: month, dateTo: today, search: '', supplierIds: [], expenseTypeIds: [], rowMode: 'all' })
  const [report, setReport] = useState<ConsolidatedReportDto | null>(null)
  const [incomeReport, setIncomeReport] = useState<IncomeReportDto | null>(null)
  const [expenseReport, setExpenseReport] = useState<ExpenseReportDto | null>(null)
  const [incomeGarages, setIncomeGarages] = useState<GarageDto[]>([])
  const [incomeOwners, setIncomeOwners] = useState<OwnerDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [loading, setLoading] = useState(true)
  const [exporting, setExporting] = useState(false)
  const [incomeLoading, setIncomeLoading] = useState(true)
  const [expenseLoading, setExpenseLoading] = useState(true)
  const [incomeExporting, setIncomeExporting] = useState(false)
  const [expenseExporting, setExpenseExporting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [incomeError, setIncomeError] = useState<string | null>(null)
  const [expenseError, setExpenseError] = useState<string | null>(null)
  const [exportMessage, setExportMessage] = useState<string | null>(null)
  const [exportError, setExportError] = useState<string | null>(null)

  useEffect(() => {
    let ignore = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const loadedReport = await reportClient.getConsolidatedReport(auth.accessToken, filters)
        if (!ignore) {
          setReport(loadedReport)
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось сформировать отчет.')
        }
      } finally {
        if (!ignore) {
          setLoading(false)
        }
      }
    }

    void load()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, filters, reportClient])

  useEffect(() => {
    let ignore = false
    async function loadIncomeReport() {
      setIncomeLoading(true)
      setIncomeError(null)
      try {
        const [loadedGarages, loadedOwners, loadedIncomeTypes, loadedIncomeReport] = await Promise.all([
          dictionaryClient.getGarages(auth.accessToken),
          dictionaryClient.getOwners(auth.accessToken),
          dictionaryClient.getIncomeTypes(auth.accessToken),
          reportClient.getIncomeReport(auth.accessToken, {
            dateFrom: incomeFilters.dateFrom,
            dateTo: incomeFilters.dateTo,
            search: incomeFilters.search,
            garageIds: incomeFilters.garageIds,
            ownerIds: incomeFilters.ownerIds,
            incomeTypeIds: incomeFilters.incomeTypeIds,
            rowMode: incomeFilters.rowMode,
          }),
        ])
        if (!ignore) {
          setIncomeGarages(loadedGarages)
          setIncomeOwners(loadedOwners)
          setIncomeTypes(loadedIncomeTypes)
          setIncomeReport(loadedIncomeReport)
        }
      } catch (caught) {
        if (!ignore) {
          setIncomeError(caught instanceof Error ? caught.message : 'Не удалось сформировать отчет по поступлениям.')
        }
      } finally {
        if (!ignore) {
          setIncomeLoading(false)
        }
      }
    }

    void loadIncomeReport()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, dictionaryClient, incomeFilters, reportClient])

  useEffect(() => {
    let ignore = false
    async function loadExpenseReport() {
      setExpenseLoading(true)
      setExpenseError(null)
      try {
        const [loadedSuppliers, loadedExpenseTypes, loadedExpenseReport] = await Promise.all([
          dictionaryClient.getSuppliers(auth.accessToken),
          dictionaryClient.getExpenseTypes(auth.accessToken),
          reportClient.getExpenseReport(auth.accessToken, {
            dateFrom: expenseFilters.dateFrom,
            dateTo: expenseFilters.dateTo,
            search: expenseFilters.search,
            supplierIds: expenseFilters.supplierIds,
            expenseTypeIds: expenseFilters.expenseTypeIds,
            rowMode: expenseFilters.rowMode,
          }),
        ])
        if (!ignore) {
          setSuppliers(loadedSuppliers)
          setExpenseTypes(loadedExpenseTypes)
          setExpenseReport(loadedExpenseReport)
        }
      } catch (caught) {
        if (!ignore) {
          setExpenseError(caught instanceof Error ? caught.message : 'Не удалось сформировать отчет по выплатам.')
        }
      } finally {
        if (!ignore) {
          setExpenseLoading(false)
        }
      }
    }

    void loadExpenseReport()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, dictionaryClient, expenseFilters, reportClient])

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setFilters({
      monthFrom: `${form.get('monthFrom')}-01`,
      monthTo: `${form.get('monthTo')}-01`,
      search: String(form.get('search') ?? ''),
    })
  }

  function applyIncomeFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setIncomeFilters({
      dateFrom: String(form.get('dateFrom') ?? today),
      dateTo: String(form.get('dateTo') ?? today),
      search: String(form.get('search') ?? ''),
      garageIds: getFormValues(form, 'garageIds'),
      ownerIds: getFormValues(form, 'ownerIds'),
      incomeTypeIds: getFormValues(form, 'incomeTypeIds'),
      rowMode: String(form.get('rowMode') ?? 'all'),
    })
  }

  function applyExpenseFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setExpenseFilters({
      dateFrom: String(form.get('dateFrom') ?? today),
      dateTo: String(form.get('dateTo') ?? today),
      search: String(form.get('search') ?? ''),
      supplierIds: getFormValues(form, 'supplierIds'),
      expenseTypeIds: getFormValues(form, 'expenseTypeIds'),
      rowMode: String(form.get('rowMode') ?? 'all'),
    })
  }

  async function exportConsolidatedXlsx() {
    setExporting(true)
    setExportError(null)
    setExportMessage(null)
    try {
      const blob = await reportClient.exportConsolidatedReportXlsx(auth.accessToken, filters)
      downloadBlob(blob, buildReportFileName('consolidated', filters.monthFrom, filters.monthTo, 'xlsx'))
      setExportMessage('XLSX по сводному отчету готов.')
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить XLSX по сводному отчету.')
    } finally {
      setExporting(false)
    }
  }

  async function exportConsolidatedPdf() {
    setExporting(true)
    setExportError(null)
    setExportMessage(null)
    try {
      const blob = await reportClient.exportConsolidatedReportPdf(auth.accessToken, filters)
      downloadBlob(blob, buildReportFileName('consolidated', filters.monthFrom, filters.monthTo, 'pdf'))
      setExportMessage('PDF по сводному отчету готов.')
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить PDF по сводному отчету.')
    } finally {
      setExporting(false)
    }
  }

  async function exportIncomeXlsx() {
    setIncomeExporting(true)
    setExportError(null)
    setExportMessage(null)
    try {
      const blob = await reportClient.exportIncomeReportXlsx(auth.accessToken, {
        dateFrom: incomeFilters.dateFrom,
        dateTo: incomeFilters.dateTo,
        search: incomeFilters.search,
        garageIds: incomeFilters.garageIds,
        ownerIds: incomeFilters.ownerIds,
        incomeTypeIds: incomeFilters.incomeTypeIds,
        rowMode: incomeFilters.rowMode,
      })
      downloadBlob(blob, buildReportFileName('income', incomeFilters.dateFrom, incomeFilters.dateTo, 'xlsx'))
      setExportMessage('XLSX по поступлениям готов.')
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчет по поступлениям.')
    } finally {
      setIncomeExporting(false)
    }
  }

  async function exportIncomePdf() {
    setIncomeExporting(true)
    setExportError(null)
    setExportMessage(null)
    try {
      const blob = await reportClient.exportIncomeReportPdf(auth.accessToken, {
        dateFrom: incomeFilters.dateFrom,
        dateTo: incomeFilters.dateTo,
        search: incomeFilters.search,
        garageIds: incomeFilters.garageIds,
        ownerIds: incomeFilters.ownerIds,
        incomeTypeIds: incomeFilters.incomeTypeIds,
        rowMode: incomeFilters.rowMode,
      })
      downloadBlob(blob, buildReportFileName('income', incomeFilters.dateFrom, incomeFilters.dateTo, 'pdf'))
      setExportMessage('PDF по поступлениям готов.')
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить PDF по поступлениям.')
    } finally {
      setIncomeExporting(false)
    }
  }

  async function exportExpenseXlsx() {
    setExpenseExporting(true)
    setExportError(null)
    setExportMessage(null)
    try {
      const blob = await reportClient.exportExpenseReportXlsx(auth.accessToken, {
        dateFrom: expenseFilters.dateFrom,
        dateTo: expenseFilters.dateTo,
        search: expenseFilters.search,
        supplierIds: expenseFilters.supplierIds,
        expenseTypeIds: expenseFilters.expenseTypeIds,
        rowMode: expenseFilters.rowMode,
      })
      downloadBlob(blob, buildReportFileName('expense', expenseFilters.dateFrom, expenseFilters.dateTo, 'xlsx'))
      setExportMessage('XLSX по выплатам готов.')
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить отчет по выплатам.')
    } finally {
      setExpenseExporting(false)
    }
  }

  async function exportExpensePdf() {
    setExpenseExporting(true)
    setExportError(null)
    setExportMessage(null)
    try {
      const blob = await reportClient.exportExpenseReportPdf(auth.accessToken, {
        dateFrom: expenseFilters.dateFrom,
        dateTo: expenseFilters.dateTo,
        search: expenseFilters.search,
        supplierIds: expenseFilters.supplierIds,
        expenseTypeIds: expenseFilters.expenseTypeIds,
        rowMode: expenseFilters.rowMode,
      })
      downloadBlob(blob, buildReportFileName('expense', expenseFilters.dateFrom, expenseFilters.dateTo, 'pdf'))
      setExportMessage('PDF по выплатам готов.')
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'Не удалось выгрузить PDF по выплатам.')
    } finally {
      setExpenseExporting(false)
    }
  }

  return (
    <section className="dictionary-panel" aria-label="Отчеты">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Отчеты</p>
          <h2>Консолидированный отчет за период</h2>
        </div>
        <span>{loading ? 'Формируем...' : `${report?.monthlyRows.length ?? 0} месяцев`}</span>
      </div>

      {error ? <div className="form-error">{error}</div> : null}
      {exportError ? <div className="form-error">{exportError}</div> : null}
      {exportMessage ? <div className="form-note">{exportMessage}</div> : null}

      <form className="compact-form report-filter" onSubmit={applyFilters}>
        <input aria-label="Начало периода отчета" name="monthFrom" type="month" defaultValue={filters.monthFrom.slice(0, 7)} required />
        <input aria-label="Конец периода отчета" name="monthTo" type="month" defaultValue={filters.monthTo.slice(0, 7)} required />
        <input aria-label="Поиск в отчете" name="search" placeholder="Гараж или владелец" defaultValue={filters.search} />
        <button className="secondary-button" type="submit">
          <Search size={16} />
          <span>Сформировать</span>
        </button>
        <button className="secondary-button" type="button" onClick={exportConsolidatedXlsx} disabled={loading || exporting}>
          <FileSpreadsheet size={16} />
          <span>{exporting ? 'Готовим XLSX' : 'Скачать сводный XLSX'}</span>
        </button>
        <button className="secondary-button" type="button" onClick={exportConsolidatedPdf} disabled={loading || exporting}>
          <FileText size={16} />
          <span>{exporting ? 'Готовим PDF' : 'Скачать сводный PDF'}</span>
        </button>
      </form>

      <div className="summary-strip" aria-label="Итоги отчета">
        <div>
          <span>Начислено</span>
          <strong>{formatMoney(report?.accrualTotal ?? 0)}</strong>
        </div>
        <div>
          <span>Поступило</span>
          <strong>{formatMoney(report?.incomeTotal ?? 0)}</strong>
        </div>
        <div>
          <span>Задолженность</span>
          <strong>{formatMoney(report?.debt ?? 0)}</strong>
        </div>
        <div>
          <span>Выплаты</span>
          <strong>{formatMoney(report?.expenseTotal ?? 0)}</strong>
        </div>
        <div>
          <span>Баланс</span>
          <strong>{formatMoney(report?.balance ?? 0)}</strong>
        </div>
      </div>

      <div className="finance-grid">
        <div className="operation-list" role="table" aria-label="Помесячный отчет">
          <div className="operation-row header" role="row">
            <span role="columnheader">Месяц</span>
            <span role="columnheader">Итоги</span>
            <span role="columnheader">Долг</span>
          </div>
          {report?.monthlyRows.length === 0 ? <p className="empty-state">Строк отчета пока нет</p> : null}
          {report?.monthlyRows.map((row) => (
            <div className="operation-row" role="row" key={row.accountingMonth}>
              <span role="cell">{row.accountingMonth.slice(0, 7)}</span>
              <span role="cell">
                <strong>{formatMoney(row.accrualTotal)} начислено</strong>
                <small>
                  {formatMoney(row.incomeTotal)} поступило, {formatMoney(row.expenseTotal)} выплат
                </small>
              </span>
              <span role="cell" className="money-accrual">
                {formatMoney(row.debt)}
              </span>
            </div>
          ))}
        </div>

        <div className="operation-list" role="table" aria-label="Отчет по гаражам">
          <div className="operation-row header" role="row">
            <span role="columnheader">Гараж</span>
            <span role="columnheader">Начисления</span>
            <span role="columnheader">Долг</span>
          </div>
          {report?.garageRows.length === 0 ? <p className="empty-state">По выбранному фильтру строк нет</p> : null}
          {report?.garageRows.slice(0, 12).map((row) => (
            <div className="operation-row" role="row" key={row.garageId}>
              <span role="cell">
                <strong>Гараж {row.garageNumber}</strong>
                <small>{row.ownerName ?? 'владелец не указан'}</small>
              </span>
              <span role="cell">
                <strong>{formatMoney(row.accrualTotal)}</strong>
                <small>{formatMoney(row.incomeTotal)} оплачено</small>
              </span>
              <span role="cell" className="money-accrual">
                {formatMoney(row.debt)}
              </span>
            </div>
          ))}
        </div>
      </div>

      <div className="subsection-heading">
        <div>
          <h3>Отчет по поступлениям</h3>
          <p>Начисления и оплаты по гаражам, владельцам и видам поступлений.</p>
        </div>
        <span>{incomeLoading ? 'Формируем...' : `${incomeReport?.rowCount ?? 0} строк`}</span>
      </div>

      {incomeError ? <div className="form-error">{incomeError}</div> : null}

      <form className="compact-form report-filter" onSubmit={applyIncomeFilters}>
        <input aria-label="Начало отчета по поступлениям" name="dateFrom" type="date" defaultValue={incomeFilters.dateFrom} required />
        <input aria-label="Конец отчета по поступлениям" name="dateTo" type="date" defaultValue={incomeFilters.dateTo} required />
        <input aria-label="Поиск в поступлениях" name="search" placeholder="Гараж, владелец, документ" defaultValue={incomeFilters.search} />
        <select aria-label="Гаражи в отчете по поступлениям" name="garageIds" multiple defaultValue={incomeFilters.garageIds} size={Math.min(4, Math.max(2, incomeGarages.length))}>
          {incomeGarages.map((garage) => (
            <option value={garage.id} key={garage.id}>
              Гараж {garage.number}
            </option>
          ))}
        </select>
        <select aria-label="Владельцы в отчете по поступлениям" name="ownerIds" multiple defaultValue={incomeFilters.ownerIds} size={Math.min(4, Math.max(2, incomeOwners.length))}>
          {incomeOwners.map((owner) => (
            <option value={owner.id} key={owner.id}>
              {owner.fullName}
            </option>
          ))}
        </select>
        <select aria-label="Виды поступлений в отчете" name="incomeTypeIds" multiple defaultValue={incomeFilters.incomeTypeIds} size={Math.min(4, Math.max(2, incomeTypes.length))}>
          {incomeTypes.map((incomeType) => (
            <option value={incomeType.id} key={incomeType.id}>
              {incomeType.name}
            </option>
          ))}
        </select>
        <select aria-label="Тип строк отчета по поступлениям" name="rowMode" defaultValue={incomeFilters.rowMode}>
          <option value="all">Начисления и оплаты</option>
          <option value="accruals">Только начисления</option>
          <option value="payments">Только оплаты</option>
        </select>
        <button className="secondary-button" type="submit">
          <Search size={16} />
          <span>Показать</span>
        </button>
        <button className="secondary-button" type="button" onClick={exportIncomeXlsx} disabled={incomeLoading || incomeExporting}>
          <FileSpreadsheet size={16} />
          <span>{incomeExporting ? 'Готовим XLSX' : 'Скачать поступления XLSX'}</span>
        </button>
        <button className="secondary-button" type="button" onClick={exportIncomePdf} disabled={incomeLoading || incomeExporting}>
          <FileText size={16} />
          <span>{incomeExporting ? 'Готовим PDF' : 'Скачать поступления PDF'}</span>
        </button>
      </form>

      <div className="summary-strip" aria-label="Итоги отчета по поступлениям">
        <div>
          <span>Начислено</span>
          <strong>{formatMoney(incomeReport?.accrualTotal ?? 0)}</strong>
        </div>
        <div>
          <span>Оплачено</span>
          <strong>{formatMoney(incomeReport?.incomeTotal ?? 0)}</strong>
        </div>
        <div>
          <span>Разница</span>
          <strong>{formatMoney(incomeReport?.debt ?? 0)}</strong>
        </div>
      </div>

      <div className="operation-list" role="table" aria-label="Отчет по поступлениям">
        <div className="operation-row header" role="row">
          <span role="columnheader">Дата</span>
          <span role="columnheader">Гараж и вид</span>
          <span role="columnheader">Сумма</span>
        </div>
        {incomeReport?.rows.length === 0 ? <p className="empty-state">По выбранному фильтру поступлений нет</p> : null}
        {incomeReport?.rows.slice(0, 16).map((row) => (
          <div className="operation-row" role="row" key={`${row.rowType}-${row.date}-${row.garageId}-${row.documentNumber ?? row.incomeTypeId}`}>
            <span role="cell">
              <strong>{row.date}</strong>
              <small>{row.rowType === 'accruals' ? 'начисление' : 'оплата'}</small>
            </span>
            <span role="cell">
              <strong>Гараж {row.garageNumber} · {row.incomeTypeName}</strong>
              <small>{row.ownerName ?? 'владелец не указан'}{row.documentNumber ? ` · ${row.documentNumber}` : ''}</small>
            </span>
            <span role="cell" className={row.rowType === 'payments' ? 'money-income' : 'money-accrual'}>
              {row.rowType === 'payments' ? '+' : ''}{formatMoney(row.rowType === 'payments' ? row.incomeAmount : row.accrualAmount)}
            </span>
          </div>
        ))}
      </div>

      <div className="subsection-heading">
        <div>
          <h3>Отчет по выплатам</h3>
          <p>Фактические выплаты поставщикам по датам, видам расходов и документам.</p>
        </div>
        <span>{expenseLoading ? 'Формируем...' : `${expenseReport?.rowCount ?? 0} строк`}</span>
      </div>

      {expenseError ? <div className="form-error">{expenseError}</div> : null}

      <form className="compact-form report-filter" onSubmit={applyExpenseFilters}>
        <input aria-label="Начало отчета по выплатам" name="dateFrom" type="date" defaultValue={expenseFilters.dateFrom} required />
        <input aria-label="Конец отчета по выплатам" name="dateTo" type="date" defaultValue={expenseFilters.dateTo} required />
        <input aria-label="Поиск в выплатах" name="search" placeholder="Поставщик, вид, документ" defaultValue={expenseFilters.search} />
        <select aria-label="Поставщики в отчете по выплатам" name="supplierIds" multiple defaultValue={expenseFilters.supplierIds} size={Math.min(4, Math.max(2, suppliers.length))}>
          {suppliers.map((supplier) => (
            <option value={supplier.id} key={supplier.id}>
              {supplier.name}
            </option>
          ))}
        </select>
        <select aria-label="Виды выплат в отчете" name="expenseTypeIds" multiple defaultValue={expenseFilters.expenseTypeIds} size={Math.min(4, Math.max(2, expenseTypes.length))}>
          {expenseTypes.map((expenseType) => (
            <option value={expenseType.id} key={expenseType.id}>
              {expenseType.name}
            </option>
          ))}
        </select>
        <select aria-label="Тип строк отчета по выплатам" name="rowMode" defaultValue={expenseFilters.rowMode}>
          <option value="all">Начисления и выплаты</option>
          <option value="accruals">Только начисления</option>
          <option value="payments">Только выплаты</option>
        </select>
        <button className="secondary-button" type="submit">
          <Search size={16} />
          <span>Показать</span>
        </button>
        <button className="secondary-button" type="button" onClick={exportExpenseXlsx} disabled={expenseLoading || expenseExporting}>
          <FileSpreadsheet size={16} />
          <span>{expenseExporting ? 'Готовим XLSX' : 'Скачать выплаты XLSX'}</span>
        </button>
        <button className="secondary-button" type="button" onClick={exportExpensePdf} disabled={expenseLoading || expenseExporting}>
          <FileText size={16} />
          <span>{expenseExporting ? 'Готовим PDF' : 'Скачать выплаты PDF'}</span>
        </button>
      </form>

      <div className="summary-strip" aria-label="Итоги отчета по выплатам">
        <div>
          <span>Начислено</span>
          <strong>{formatMoney(expenseReport?.accrualTotal ?? 0)}</strong>
        </div>
        <div>
          <span>Выплачено</span>
          <strong>{formatMoney(expenseReport?.expenseTotal ?? 0)}</strong>
        </div>
        <div>
          <span>Разница</span>
          <strong>{formatMoney(expenseReport?.difference ?? 0)}</strong>
        </div>
      </div>

      <div className="operation-list" role="table" aria-label="Отчет по выплатам">
        <div className="operation-row header" role="row">
          <span role="columnheader">Дата</span>
          <span role="columnheader">Поставщик и вид</span>
          <span role="columnheader">Сумма</span>
        </div>
        {expenseReport?.rows.length === 0 ? <p className="empty-state">По выбранному фильтру выплат нет</p> : null}
        {expenseReport?.rows.slice(0, 16).map((row) => (
          <div className="operation-row" role="row" key={`${row.rowType}-${row.date}-${row.supplierId}-${row.documentNumber ?? row.expenseTypeId}`}>
            <span role="cell">
              <strong>{row.date}</strong>
              <small>{row.rowType === 'accruals' ? 'начисление' : 'выплата'}</small>
            </span>
            <span role="cell">
              <strong>{row.supplierName} · {row.expenseTypeName}</strong>
              <small>{row.documentNumber ?? 'документ не указан'}</small>
            </span>
            <span role="cell" className={row.rowType === 'payments' ? 'money-expense' : 'money-accrual'}>
              {row.rowType === 'payments' ? '-' : ''}{formatMoney(row.rowType === 'payments' ? row.expenseAmount : row.accrualAmount)}
            </span>
          </div>
        ))}
      </div>
    </section>
  )
}

function UserManagementPanel({ auth, userClient }: { auth: AuthResponse; userClient: UserManagementClient }) {
  const [roles, setRoles] = useState<ManagedRoleDto[]>([])
  const [users, setUsers] = useState<ManagedUserDto[]>([])
  const [form, setForm] = useState({ email: '', displayName: '', password: '', roleCode: 'operator' })
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let ignore = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const [loadedRoles, loadedUsers] = await Promise.all([
          userClient.getRoles(auth.accessToken),
          userClient.getUsers(auth.accessToken),
        ])
        if (!ignore) {
          setRoles(loadedRoles)
          setUsers(loadedUsers)
          setForm((value) => ({ ...value, roleCode: loadedRoles.find((role) => role.code === value.roleCode)?.code ?? loadedRoles[0]?.code ?? '' }))
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить пользователей.')
        }
      } finally {
        if (!ignore) {
          setLoading(false)
        }
      }
    }

    void load()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, userClient])

  async function saveUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const user = await userClient.createUser(auth.accessToken, {
        email: form.email,
        displayName: form.displayName,
        password: form.password,
        roleCodes: [form.roleCode],
        isActive: true,
      })
      setUsers((items) => [user, ...items])
      setForm((value) => ({ email: '', displayName: '', password: '', roleCode: value.roleCode }))
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось создать пользователя.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="dictionary-panel" aria-label="Пользователи">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Пользователи</p>
          <h2>Доступ в систему и роли сотрудников</h2>
        </div>
        <span>{loading ? 'Загрузка...' : `${users.length} пользователей`}</span>
      </div>

      {error ? <div className="form-error">{error}</div> : null}

      <div className="user-management-grid">
        <form className="dictionary-form" onSubmit={saveUser}>
          <h3>Новый сотрудник</h3>
          <input aria-label="Email пользователя" placeholder="email@example.com" value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} type="email" required />
          <input aria-label="Имя пользователя" placeholder="Имя" value={form.displayName} onChange={(event) => setForm({ ...form, displayName: event.target.value })} required />
          <input aria-label="Пароль пользователя" placeholder="Пароль" value={form.password} onChange={(event) => setForm({ ...form, password: event.target.value })} type="password" minLength={8} required />
          <select aria-label="Роль пользователя" value={form.roleCode} onChange={(event) => setForm({ ...form, roleCode: event.target.value })} required>
            {roles.map((role) => (
              <option value={role.code} key={role.code}>
                {role.name}
              </option>
            ))}
          </select>
          <button className="secondary-button" type="submit" disabled={saving || roles.length === 0}>
            <Plus size={16} />
            <span>Добавить</span>
          </button>
        </form>

        <div className="user-table" role="table" aria-label="Список пользователей">
          <div className="user-table-row header" role="row">
            <span role="columnheader">Сотрудник</span>
            <span role="columnheader">Роли</span>
            <span role="columnheader">Статус</span>
          </div>
          {users.length === 0 ? <p className="empty-state">Пользователей пока нет</p> : null}
          {users.slice(0, 8).map((user) => (
            <div className="user-table-row" role="row" key={user.id}>
              <span role="cell">
                <strong>{user.displayName}</strong>
                <small>{user.email}</small>
              </span>
              <span role="cell">{user.roles.join(', ')}</span>
              <span role="cell" className={user.isActive ? 'status-active' : 'status-disabled'}>
                {user.isActive ? 'Активен' : 'Отключен'}
              </span>
            </div>
          ))}
        </div>
      </div>
    </section>
  )
}

function DictionaryPanel({ auth, dictionaryClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient }) {
  const [owners, setOwners] = useState<OwnerDto[]>([])
  const [garages, setGarages] = useState<GarageDto[]>([])
  const [groups, setGroups] = useState<SupplierGroupDto[]>([])
  const [suppliers, setSuppliers] = useState<SupplierDto[]>([])
  const [incomeTypes, setIncomeTypes] = useState<AccountingTypeDto[]>([])
  const [expenseTypes, setExpenseTypes] = useState<AccountingTypeDto[]>([])
  const [tariffs, setTariffs] = useState<TariffDto[]>([])
  const [ownerForm, setOwnerForm] = useState({ lastName: '', firstName: '', phone: '' })
  const [garageForm, setGarageForm] = useState({ number: '', peopleCount: 1, floorCount: 1, ownerId: '' })
  const [supplierGroupName, setSupplierGroupName] = useState('')
  const [supplierForm, setSupplierForm] = useState({ name: '', groupId: '', inn: '', startingBalance: 0 })
  const [incomeTypeForm, setIncomeTypeForm] = useState({ name: '', code: '' })
  const [expenseTypeForm, setExpenseTypeForm] = useState({ name: '', code: '' })
  const [tariffForm, setTariffForm] = useState({ name: '', calculationBase: 'fixed', rate: 1, effectiveFrom: '2026-07-01' })
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const defaultGroupId = useMemo(() => supplierForm.groupId || groups[0]?.id || '', [groups, supplierForm.groupId])

  useEffect(() => {
    let ignore = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const [loadedOwners, loadedGarages, loadedGroups, loadedSuppliers, loadedIncomeTypes, loadedExpenseTypes, loadedTariffs] = await Promise.all([
          dictionaryClient.getOwners(auth.accessToken),
          dictionaryClient.getGarages(auth.accessToken),
          dictionaryClient.getSupplierGroups(auth.accessToken),
          dictionaryClient.getSuppliers(auth.accessToken),
          dictionaryClient.getIncomeTypes(auth.accessToken),
          dictionaryClient.getExpenseTypes(auth.accessToken),
          dictionaryClient.getTariffs(auth.accessToken),
        ])
        if (!ignore) {
          setOwners(loadedOwners)
          setGarages(loadedGarages)
          setGroups(loadedGroups)
          setSuppliers(loadedSuppliers)
          setIncomeTypes(loadedIncomeTypes)
          setExpenseTypes(loadedExpenseTypes)
          setTariffs(loadedTariffs)
        }
      } catch (caught) {
        if (!ignore) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить справочники.')
        }
      } finally {
        if (!ignore) {
          setLoading(false)
        }
      }
    }

    void load()
    return () => {
      ignore = true
    }
  }, [auth.accessToken, dictionaryClient])

  async function saveOwner(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await runSaving('owner', async () => {
      const owner = await dictionaryClient.createOwner(auth.accessToken, ownerForm)
      setOwners((items) => [owner, ...items])
      setOwnerForm({ lastName: '', firstName: '', phone: '' })
    })
  }

  async function saveGarage(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await runSaving('garage', async () => {
      const garage = await dictionaryClient.createGarage(auth.accessToken, {
        number: garageForm.number,
        peopleCount: garageForm.peopleCount,
        floorCount: garageForm.floorCount,
        ownerId: garageForm.ownerId || null,
      })
      setGarages((items) => [garage, ...items])
      setGarageForm({ number: '', peopleCount: 1, floorCount: 1, ownerId: '' })
    })
  }

  async function saveSupplierGroup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await runSaving('group', async () => {
      const group = await dictionaryClient.createSupplierGroup(auth.accessToken, { name: supplierGroupName })
      setGroups((items) => [...items, group])
      setSupplierGroupName('')
      setSupplierForm((value) => ({ ...value, groupId: group.id }))
    })
  }

  async function saveSupplier(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await runSaving('supplier', async () => {
      const supplier = await dictionaryClient.createSupplier(auth.accessToken, {
        name: supplierForm.name,
        groupId: defaultGroupId,
        inn: supplierForm.inn,
        startingBalance: supplierForm.startingBalance,
      })
      setSuppliers((items) => [supplier, ...items])
      setSupplierForm({ name: '', groupId: defaultGroupId, inn: '', startingBalance: 0 })
    })
  }

  async function saveIncomeType(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await runSaving('income-type', async () => {
      const incomeType = await dictionaryClient.createIncomeType(auth.accessToken, incomeTypeForm)
      setIncomeTypes((items) => [incomeType, ...items])
      setIncomeTypeForm({ name: '', code: '' })
    })
  }

  async function saveExpenseType(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await runSaving('expense-type', async () => {
      const expenseType = await dictionaryClient.createExpenseType(auth.accessToken, expenseTypeForm)
      setExpenseTypes((items) => [expenseType, ...items])
      setExpenseTypeForm({ name: '', code: '' })
    })
  }

  async function saveTariff(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await runSaving('tariff', async () => {
      const tariff = await dictionaryClient.createTariff(auth.accessToken, tariffForm)
      setTariffs((items) => [tariff, ...items])
      setTariffForm((value) => ({ ...value, name: '', rate: 1 }))
    })
  }

  async function runSaving(scope: string, action: () => Promise<void>) {
    setSaving(scope)
    setError(null)
    try {
      await action()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось сохранить запись.')
    } finally {
      setSaving(null)
    }
  }

  return (
    <section className="dictionary-panel" aria-label="Справочники">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Справочники</p>
          <h2>База для импорта, начислений и отчетов</h2>
        </div>
        <span>{loading ? 'Загрузка...' : `${owners.length + garages.length + suppliers.length} записей`}</span>
      </div>

      {error ? <div className="form-error">{error}</div> : null}

      <div className="dictionary-grid">
        <form className="dictionary-form" onSubmit={saveOwner}>
          <h3>Владельцы</h3>
          <input aria-label="Фамилия владельца" placeholder="Фамилия" value={ownerForm.lastName} onChange={(event) => setOwnerForm({ ...ownerForm, lastName: event.target.value })} required />
          <input aria-label="Имя владельца" placeholder="Имя" value={ownerForm.firstName} onChange={(event) => setOwnerForm({ ...ownerForm, firstName: event.target.value })} required />
          <input aria-label="Телефон владельца" placeholder="Телефон" value={ownerForm.phone} onChange={(event) => setOwnerForm({ ...ownerForm, phone: event.target.value })} />
          <button className="secondary-button" type="submit" disabled={saving === 'owner'}>
            <Plus size={16} />
            <span>Добавить</span>
          </button>
          <DictionaryList items={owners.map((owner) => ({ id: owner.id, title: owner.fullName, meta: owner.phone ?? 'телефон не указан' }))} emptyText="Владельцев пока нет" />
        </form>

        <form className="dictionary-form" onSubmit={saveGarage}>
          <h3>Гаражи</h3>
          <input aria-label="Номер гаража" placeholder="Номер" value={garageForm.number} onChange={(event) => setGarageForm({ ...garageForm, number: event.target.value })} required />
          <div className="inline-fields">
            <input aria-label="Количество людей" type="number" min="0" value={garageForm.peopleCount} onChange={(event) => setGarageForm({ ...garageForm, peopleCount: Number(event.target.value) })} />
            <input aria-label="Количество этажей" type="number" min="0" value={garageForm.floorCount} onChange={(event) => setGarageForm({ ...garageForm, floorCount: Number(event.target.value) })} />
          </div>
          <select aria-label="Владелец гаража" value={garageForm.ownerId} onChange={(event) => setGarageForm({ ...garageForm, ownerId: event.target.value })}>
            <option value="">Без владельца</option>
            {owners.map((owner) => (
              <option value={owner.id} key={owner.id}>
                {owner.fullName}
              </option>
            ))}
          </select>
          <button className="secondary-button" type="submit" disabled={saving === 'garage'}>
            <Plus size={16} />
            <span>Добавить</span>
          </button>
          <DictionaryList items={garages.map((garage) => ({ id: garage.id, title: `Гараж ${garage.number}`, meta: garage.ownerName ?? 'владелец не указан' }))} emptyText="Гаражей пока нет" />
        </form>

        <div className="dictionary-form">
          <h3>Поставщики</h3>
          <form className="compact-form" onSubmit={saveSupplierGroup}>
            <input aria-label="Группа поставщиков" placeholder="Группа" value={supplierGroupName} onChange={(event) => setSupplierGroupName(event.target.value)} required />
            <button className="icon-button" type="submit" aria-label="Добавить группу" disabled={saving === 'group'}>
              <Plus size={17} />
            </button>
          </form>
          <form className="compact-stack" onSubmit={saveSupplier}>
            <input aria-label="Название поставщика" placeholder="Название" value={supplierForm.name} onChange={(event) => setSupplierForm({ ...supplierForm, name: event.target.value })} required />
            <select aria-label="Группа для поставщика" value={defaultGroupId} onChange={(event) => setSupplierForm({ ...supplierForm, groupId: event.target.value })} required>
              <option value="" disabled>
                Выберите группу
              </option>
              {groups.map((group) => (
                <option value={group.id} key={group.id}>
                  {group.name}
                </option>
              ))}
            </select>
            <input aria-label="ИНН поставщика" placeholder="ИНН" value={supplierForm.inn} onChange={(event) => setSupplierForm({ ...supplierForm, inn: event.target.value })} />
            <button className="secondary-button" type="submit" disabled={!defaultGroupId || saving === 'supplier'}>
              <Plus size={16} />
              <span>Добавить</span>
            </button>
          </form>
          <DictionaryList items={suppliers.map((supplier) => ({ id: supplier.id, title: supplier.name, meta: `${supplier.groupName}${supplier.inn ? `, ИНН ${supplier.inn}` : ''}` }))} emptyText="Поставщиков пока нет" />
        </div>
      </div>

      <div className="finance-settings-grid" aria-label="Финансовые настройки">
        <form className="dictionary-form" onSubmit={saveIncomeType}>
          <h3>Виды поступлений</h3>
          <input aria-label="Название вида поступления" placeholder="Членский взнос" value={incomeTypeForm.name} onChange={(event) => setIncomeTypeForm({ ...incomeTypeForm, name: event.target.value })} required />
          <input aria-label="Код вида поступления" placeholder="Код" value={incomeTypeForm.code} onChange={(event) => setIncomeTypeForm({ ...incomeTypeForm, code: event.target.value })} />
          <button className="secondary-button" type="submit" disabled={saving === 'income-type'}>
            <Plus size={16} />
            <span>Добавить</span>
          </button>
          <DictionaryList items={incomeTypes.map((item) => ({ id: item.id, title: item.name, meta: item.code ?? 'код не указан' }))} emptyText="Видов поступлений пока нет" />
        </form>

        <form className="dictionary-form" onSubmit={saveExpenseType}>
          <h3>Виды выплат</h3>
          <input aria-label="Название вида выплаты" placeholder="Электроэнергия" value={expenseTypeForm.name} onChange={(event) => setExpenseTypeForm({ ...expenseTypeForm, name: event.target.value })} required />
          <input aria-label="Код вида выплаты" placeholder="Код" value={expenseTypeForm.code} onChange={(event) => setExpenseTypeForm({ ...expenseTypeForm, code: event.target.value })} />
          <button className="secondary-button" type="submit" disabled={saving === 'expense-type'}>
            <Plus size={16} />
            <span>Добавить</span>
          </button>
          <DictionaryList items={expenseTypes.map((item) => ({ id: item.id, title: item.name, meta: item.code ?? 'код не указан' }))} emptyText="Видов выплат пока нет" />
        </form>

        <form className="dictionary-form" onSubmit={saveTariff}>
          <h3>Тарифы</h3>
          <input aria-label="Название тарифа" placeholder="Вода" value={tariffForm.name} onChange={(event) => setTariffForm({ ...tariffForm, name: event.target.value })} required />
          <select aria-label="База расчета тарифа" value={tariffForm.calculationBase} onChange={(event) => setTariffForm({ ...tariffForm, calculationBase: event.target.value })}>
            <option value="fixed">Фиксированно</option>
            <option value="people">По людям</option>
            <option value="meter_water">По счетчику воды</option>
            <option value="meter_electricity">По счетчику электричества</option>
          </select>
          <div className="inline-fields">
            <input aria-label="Ставка тарифа" type="number" min="0.0001" step="0.0001" value={tariffForm.rate} onChange={(event) => setTariffForm({ ...tariffForm, rate: Number(event.target.value) })} />
            <input aria-label="Дата начала тарифа" type="date" value={tariffForm.effectiveFrom} onChange={(event) => setTariffForm({ ...tariffForm, effectiveFrom: event.target.value })} />
          </div>
          <button className="secondary-button" type="submit" disabled={saving === 'tariff'}>
            <Plus size={16} />
            <span>Добавить</span>
          </button>
          <DictionaryList items={tariffs.map((item) => ({ id: item.id, title: item.name, meta: `${item.rate} с ${item.effectiveFrom}` }))} emptyText="Тарифов пока нет" />
        </form>
      </div>
    </section>
  )
}

function DictionaryList({ items, emptyText }: { items: { id: string; title: string; meta: string }[]; emptyText: string }) {
  if (items.length === 0) {
    return <p className="empty-state">{emptyText}</p>
  }

  return (
    <ul className="dictionary-list">
      {items.slice(0, 5).map((item) => (
        <li key={item.id}>
          <strong>{item.title}</strong>
          <span>{item.meta}</span>
        </li>
      ))}
    </ul>
  )
}

function formatMoney(value: number): string {
  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2, minimumFractionDigits: 2 }).format(value)
}

function buildReportFileName(type: 'consolidated' | 'income' | 'expense', dateFrom: string, dateTo: string, extension: 'xlsx' | 'pdf'): string {
  return `garagebalance-${type}-${dateFrom.replaceAll('-', '')}-${dateTo.replaceAll('-', '')}.${extension}`
}

function getFormValues(form: FormData, name: string): string[] {
  return form
    .getAll(name)
    .map((value) => String(value))
    .filter(Boolean)
}

function downloadBlob(blob: Blob, fileName: string) {
  if (typeof URL.createObjectURL !== 'function') {
    return
  }

  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.append(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

function formatReleaseDate(value: string): string {
  return new Intl.DateTimeFormat('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(new Date(value))
}

export default App
