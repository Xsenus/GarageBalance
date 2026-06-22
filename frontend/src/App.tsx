import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import {
  Bell,
  BookOpenCheck,
  CircleDollarSign,
  DatabaseZap,
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
import { usersApi } from './services/usersApi'
import type { ManagedRoleDto, ManagedUserDto, UserManagementClient } from './services/usersApi'
import './App.css'

type AppProps = {
  authClient?: AuthClient
  dictionaryClient?: DictionaryClient
  userClient?: UserManagementClient
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

const updates = [
  'Добавлены финансовые справочники: виды поступлений, виды выплат и тарифы с датой начала действия.',
  'Добавлено управление пользователями: администратор может создать сотрудника, назначить роль и увидеть статус доступа.',
  'Добавлен контур входа: создание администратора, обычный вход и отображение текущего пользователя.',
  'Backend получил контроллеры авторизации, JWT, роли, права и audit-события.',
  'Добавлены справочники владельцев, гаражей, групп поставщиков и поставщиков с тестами и миграцией.',
]

function App({ authClient = authApi, dictionaryClient = dictionariesApi, userClient = usersApi }: AppProps) {
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
          <Workspace auth={auth} dictionaryClient={dictionaryClient} userClient={userClient} onLogout={() => setAuth(null)} />
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
  userClient,
  onLogout,
}: {
  auth: AuthResponse
  dictionaryClient: DictionaryClient
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

      <section className="release-panel" aria-label="Что нового">
        <div>
          <p className="eyebrow">Что нового</p>
          <h2>Журнал обновлений включен с первого дня</h2>
        </div>
        <ul>
          {updates.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      </section>
    </>
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

export default App
