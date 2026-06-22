import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from './App'
import type { AuthClient, AuthResponse } from './services/authApi'
import type { DictionaryClient, GarageDto, OwnerDto, SupplierDto, SupplierGroupDto } from './services/dictionariesApi'

describe('App', () => {
  it('shows auth gate before workspace is available', () => {
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} />)

    expect(screen.getByText('GarageBalance')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Вход в систему' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /сначала вход и права/i })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /финансовый учет гск/i })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Панель' })).toBeDisabled()
  })

  it('creates first administrator and opens the workspace with dictionaries', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} />)

    await user.clear(screen.getByLabelText('Пароль'))
    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))

    expect(await screen.findByRole('heading', { name: /финансовый учет гск/i })).toBeInTheDocument()
    expect(screen.getByText('Администратор')).toBeInTheDocument()
    expect(screen.getByText('administrator')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Панель' })).toBeEnabled()

    const dictionaryPanel = await screen.findByRole('region', { name: 'Справочники' })
    expect(within(dictionaryPanel).getAllByText('Иванов Иван').length).toBeGreaterThan(0)
    expect(within(dictionaryPanel).getByText('Гараж 12')).toBeInTheDocument()
    expect(within(dictionaryPanel).getByText('Водоканал')).toBeInTheDocument()
  })

  it('adds owner, garage, supplier group and supplier from protected workspace', async () => {
    const user = userEvent.setup()
    const dictionaryClient = createStatefulDictionaryClient()
    render(<App authClient={createAuthClient()} dictionaryClient={dictionaryClient} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))
    await screen.findByRole('region', { name: 'Справочники' })

    await user.type(screen.getByLabelText('Фамилия владельца'), 'Петров')
    await user.type(screen.getByLabelText('Имя владельца'), 'Петр')
    await user.type(screen.getByLabelText('Телефон владельца'), '+7 913')
    await user.click(screen.getAllByRole('button', { name: 'Добавить' })[0])
    expect((await screen.findAllByText('Петров Петр')).length).toBeGreaterThan(0)

    await user.type(screen.getByLabelText('Номер гаража'), '21')
    await user.selectOptions(screen.getByLabelText('Владелец гаража'), screen.getByRole('option', { name: 'Петров Петр' }))
    await user.click(screen.getAllByRole('button', { name: 'Добавить' })[1])
    expect(await screen.findByText('Гараж 21')).toBeInTheDocument()
    expect(screen.getAllByText('Петров Петр').length).toBeGreaterThan(0)

    await user.type(screen.getByLabelText('Группа поставщиков'), 'Связь')
    await user.click(screen.getByRole('button', { name: 'Добавить группу' }))
    await user.type(screen.getByLabelText('Название поставщика'), 'Сибирь Онлайн')
    await user.type(screen.getByLabelText('ИНН поставщика'), '5401000000')
    await user.click(screen.getAllByRole('button', { name: 'Добавить' })[2])
    expect(await screen.findByText('Сибирь Онлайн')).toBeInTheDocument()
    expect(screen.getByText('Связь, ИНН 5401000000')).toBeInTheDocument()
  })

  it('shows login errors without opening protected workspace', async () => {
    const user = userEvent.setup()
    const authClient = createAuthClient({
      login: async () => {
        throw new Error('Неверный email или пароль.')
      },
    })
    render(<App authClient={authClient} dictionaryClient={createDictionaryClient()} />)

    await user.click(screen.getByRole('button', { name: 'Вход' }))
    await user.type(screen.getByLabelText('Пароль'), 'WrongPass123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    expect(await screen.findByText('Неверный email или пароль.')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /финансовый учет гск/i })).not.toBeInTheDocument()
  })

  it('shows first release notes for authenticated users', async () => {
    const user = userEvent.setup()
    render(<App authClient={createAuthClient()} dictionaryClient={createDictionaryClient()} />)

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))

    const releasePanel = await screen.findByRole('region', { name: 'Что нового' })
    expect(within(releasePanel).getByText('Журнал обновлений включен с первого дня')).toBeInTheDocument()
    expect(within(releasePanel).getByText(/контур входа/)).toBeInTheDocument()
    expect(within(releasePanel).getByText(/справочники владельцев/)).toBeInTheDocument()
  })

  it('shows dictionary loading errors inside workspace', async () => {
    const user = userEvent.setup()
    render(
      <App
        authClient={createAuthClient()}
        dictionaryClient={createDictionaryClient({
          getOwners: async () => {
            throw new Error('Нет доступа к справочникам.')
          },
        })}
      />,
    )

    await user.type(screen.getByLabelText('Пароль'), 'StrongPass123')
    await user.click(screen.getByRole('button', { name: 'Создать администратора' }))

    expect(await screen.findByText('Нет доступа к справочникам.')).toBeInTheDocument()
  })
})

function createAuthClient(overrides: Partial<AuthClient> = {}): AuthClient {
  return {
    bootstrapAdmin: async () => createAuthResponse(),
    login: async () => createAuthResponse(),
    ...overrides,
  }
}

function createDictionaryClient(overrides: Partial<DictionaryClient> = {}): DictionaryClient {
  const owner = createOwner({ id: 'owner-1', lastName: 'Иванов', firstName: 'Иван', phone: '+7 900' })
  const garage = createGarage({ id: 'garage-1', number: '12', ownerId: owner.id, ownerName: owner.fullName })
  const group = createGroup({ id: 'group-1', name: 'Коммунальные услуги' })
  const supplier = createSupplier({ id: 'supplier-1', name: 'Водоканал', groupId: group.id, groupName: group.name, inn: '5401' })

  return {
    getOwners: async () => [owner],
    createOwner: async () => owner,
    getGarages: async () => [garage],
    createGarage: async () => garage,
    getSupplierGroups: async () => [group],
    createSupplierGroup: async () => group,
    getSuppliers: async () => [supplier],
    createSupplier: async () => supplier,
    ...overrides,
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
      const garage = createGarage({
        id: crypto.randomUUID(),
        number: request.number,
        ownerId: owner?.id ?? null,
        ownerName: owner?.fullName ?? null,
      })
      return garage
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
      const supplier = createSupplier({
        id: crypto.randomUUID(),
        name: request.name,
        groupId: group.id,
        groupName: group.name,
        inn: request.inn ?? null,
      })
      return supplier
    },
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
