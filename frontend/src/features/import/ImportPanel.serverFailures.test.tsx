import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { AuthResponse } from '../../services/authApi'
import type {
  AccessImportQuarantineItemDto,
  AccessImportReaderStatusDto,
  AccessImportRunDto,
  ImportClient,
} from '../../services/importApi'
import { ImportPanel } from './ImportPanel'

const auth: AuthResponse = {
  accessToken: 'access-token',
  expiresAtUtc: '2026-08-06T00:00:00Z',
  user: {
    id: 'admin-user',
    email: 'admin@example.test',
    displayName: 'Администратор',
    roles: ['administrator'],
    permissions: ['import.read', 'import.write'],
  },
}

function createRun(overrides: Partial<AccessImportRunDto> = {}): AccessImportRunDto {
  return {
    id: 'access-run',
    mode: 'dry-run',
    status: 'completed',
    originalFileName: 'ГСК.accdb',
    fileExtension: '.accdb',
    fileSizeBytes: 1024,
    contentSha256: 'a'.repeat(64),
    startedAtUtc: '2026-08-05T00:00:00Z',
    finishedAtUtc: '2026-08-05T00:01:00Z',
    totalChecks: 1,
    passedChecks: 1,
    warningCount: 0,
    errorCount: 0,
    summary: 'Dry-run завершён.',
    checks: [],
    ...overrides,
  }
}

function createQuarantineItem(overrides: Partial<AccessImportQuarantineItemDto> = {}): AccessImportQuarantineItemDto {
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
    createdAtUtc: '2026-08-05T00:00:00Z',
    createdByUserId: null,
    resolvedAtUtc: null,
    resolvedByUserId: null,
    resolutionComment: null,
    ...overrides,
  }
}

function createReaderStatus(): AccessImportReaderStatusDto {
  return {
    provider: 'disabled',
    displayName: 'Reader Access',
    isAvailable: false,
    status: 'not_configured',
    statusMessage: 'Reader не настроен.',
    requiredComponents: ['ACE OLE DB driver'],
    checkedAtUtc: '2026-08-05T00:00:00Z',
  }
}

function createClient(overrides: Partial<ImportClient> = {}): ImportClient {
  const run = createRun()
  return {
    getAccessReaderStatus: async () => createReaderStatus(),
    getAccessRuns: async () => [run],
    getAccessRun: async (_token, runId) => createRun({ id: runId }),
    getAccessRunStatus: async (_token, runId) => createRun({ id: runId }),
    getAccessRunLog: async () => [],
    getAccessCreatedRecords: async () => [],
    getOpenQuarantineItems: async () => [],
    dryRunAccess: async () => run,
    downloadAccessRunReport: async () => new Blob(['{}'], { type: 'application/json' }),
    requestAccessImportApply: async (_token, runId) => createRun({ id: runId, status: 'import_requested' }),
    cancelAccessImportApplyRequest: async (_token, runId) => createRun({ id: runId, status: 'import_request_cancelled' }),
    requestAccessImportRollback: async (_token, runId) => createRun({ id: runId, status: 'rollback_requested' }),
    resolveQuarantineItem: async (_token, itemId) => createQuarantineItem({ id: itemId, status: 'resolved' }),
    ...overrides,
  }
}

describe('ImportPanel server failures', () => {
  it('reports a lightweight status polling failure without reloading the run list', async () => {
    const queuedRun = createRun({ status: 'queued', finishedAtUtc: null })
    const getAccessRuns = vi.fn(async () => [queuedRun])
    const getAccessRunStatus = vi.fn(async () => {
      throw new Error('Точечное обновление запуска недоступно.')
    })

    render(<ImportPanel auth={auth} importClient={createClient({
      getAccessRuns,
      getAccessRun: async () => queuedRun,
      getAccessRunStatus,
    })} />)

    expect(await screen.findByText('Reader не настроен.')).toBeInTheDocument()
    expect(await screen.findByText('Точечное обновление запуска недоступно.', {}, { timeout: 2500 })).toHaveAttribute('role', 'alert')
    expect(getAccessRuns).toHaveBeenCalledTimes(1)
    expect(getAccessRunStatus).toHaveBeenCalledWith(auth.accessToken, queuedRun.id, expect.any(AbortSignal))
  })

  it('loads hidden import lists only when their tabs open and reuses successful results', async () => {
    const user = userEvent.setup()
    const getAccessRunLog = vi.fn(async () => [])
    const getAccessCreatedRecords = vi.fn(async () => [])
    const getOpenQuarantineItems = vi.fn(async () => [])
    const client = createClient({ getAccessRunLog, getAccessCreatedRecords, getOpenQuarantineItems })

    render(<ImportPanel auth={auth} importClient={client} />)

    expect(await screen.findByText('Reader не настроен.')).toBeInTheDocument()
    expect(getAccessRunLog).not.toHaveBeenCalled()
    expect(getAccessCreatedRecords).not.toHaveBeenCalled()
    expect(getOpenQuarantineItems).not.toHaveBeenCalled()

    await user.click(screen.getByRole('tab', { name: /Лог/ }))
    expect(await screen.findByText('Лог выбранного запуска пока пуст')).toBeInTheDocument()
    expect(getAccessRunLog).toHaveBeenCalledTimes(1)

    await user.click(screen.getByRole('tab', { name: /Создано/ }))
    expect(await screen.findByText('Созданные записи появятся после фактического переноса Access')).toBeInTheDocument()
    expect(getAccessCreatedRecords).toHaveBeenCalledTimes(1)

    await user.click(screen.getByRole('tab', { name: /Карантин/ }))
    expect(await screen.findByText('Открытых строк карантина нет')).toBeInTheDocument()
    expect(getOpenQuarantineItems).toHaveBeenCalledTimes(1)

    await user.click(screen.getByRole('tab', { name: /Лог/ }))
    expect(screen.getByText('Лог выбранного запуска пока пуст')).toBeInTheDocument()
    expect(getAccessRunLog).toHaveBeenCalledTimes(1)
  })

  it('aborts a hidden import-tab request when the user switches tabs', async () => {
    const user = userEvent.setup()
    let requestSignal: AbortSignal | undefined
    const getAccessRunLog = vi.fn((_token: string, _runId: string, _limit?: number, signal?: AbortSignal) => {
      requestSignal = signal
      return new Promise<never>((_resolve, reject) => {
        signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true })
      })
    })
    const client = createClient({ getAccessRunLog })

    render(<ImportPanel auth={auth} importClient={client} />)
    expect(await screen.findByText('Reader не настроен.')).toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: /Лог/ }))
    await waitFor(() => expect(requestSignal).toBeDefined())
    expect(requestSignal?.aborted).toBe(false)

    await user.click(screen.getByRole('tab', { name: /Проверки/ }))
    expect(requestSignal?.aborted).toBe(true)
  })

  it('aborts stale run details and keeps the latest selected history run', async () => {
    const user = userEvent.setup()
    const firstRun = createRun({ id: 'first-run', originalFileName: 'Первый.accdb' })
    const secondRun = createRun({ id: 'second-run', originalFileName: 'Второй.accdb' })
    const thirdRun = createRun({ id: 'third-run', originalFileName: 'Третий.accdb' })
    let secondSignal: AbortSignal | undefined
    let resolveSecond!: (run: AccessImportRunDto) => void
    const secondDetails = new Promise<AccessImportRunDto>((resolve) => { resolveSecond = resolve })
    const getAccessRun = vi.fn((_token: string, runId: string, signal?: AbortSignal) => {
      if (runId === secondRun.id) {
        secondSignal = signal
        return secondDetails
      }
      return Promise.resolve(runId === firstRun.id ? firstRun : thirdRun)
    })

    render(<ImportPanel auth={auth} importClient={createClient({
      getAccessRuns: async () => [firstRun, secondRun, thirdRun],
      getAccessRun,
    })} />)

    expect(await screen.findByText('Reader не настроен.')).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: /История/ }))
    await user.click(screen.getByText('Второй.accdb').closest('button')!)
    await waitFor(() => expect(secondSignal).toBeDefined())
    await user.click(screen.getByText('Третий.accdb').closest('button')!)

    expect(secondSignal?.aborted).toBe(true)
    expect(await within(screen.getByLabelText('Проверенный файл и результат')).findByText('Третий.accdb')).toBeInTheDocument()
    resolveSecond(secondRun)
    await user.click(screen.getByRole('tab', { name: /Проверки/ }))
    expect(within(screen.getByLabelText('Проверенный файл и результат')).getByText('Третий.accdb')).toBeInTheDocument()
    expect(getAccessRun).toHaveBeenCalledTimes(3)
  })

  it('shows a selected history run failure and allows another selection', async () => {
    const user = userEvent.setup()
    const firstRun = createRun({ id: 'first-run', originalFileName: 'Первый.accdb' })
    const failedRun = createRun({ id: 'failed-run', originalFileName: 'Недоступный.accdb' })
    const lastRun = createRun({ id: 'last-run', originalFileName: 'Доступный.accdb' })
    const getAccessRun = vi.fn(async (_token: string, runId: string) => {
      if (runId === failedRun.id) throw new Error('Детали запуска временно недоступны.')
      return runId === firstRun.id ? firstRun : lastRun
    })

    render(<ImportPanel auth={auth} importClient={createClient({
      getAccessRuns: async () => [firstRun, failedRun, lastRun],
      getAccessRun,
    })} />)

    expect(await screen.findByText('Reader не настроен.')).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: /История/ }))
    await user.click(screen.getByText('Недоступный.accdb').closest('button')!)
    expect(await screen.findByRole('alert')).toHaveTextContent('Детали запуска временно недоступны.')
    await user.click(screen.getByText('Доступный.accdb').closest('button')!)
    await user.click(screen.getByRole('tab', { name: /Проверки/ }))
    expect(await screen.findByText('Доступный.accdb')).toBeInTheDocument()
  })

  it('shows an apply failure inside the dialog, preserves input and allows retry', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const run = createRun()
    const client = createClient({
      getAccessRuns: async () => [run],
      requestAccessImportApply: async (_token, runId) => {
        attempts += 1
        if (attempts === 1) {
          throw new Error('Сервер временно не принял заявку на импорт.')
        }
        return createRun({ id: runId, status: 'import_requested' })
      },
    })
    render(<ImportPanel auth={auth} importClient={client} />)

    await user.click(await screen.findByRole('button', { name: 'Запросить фактический импорт ГСК.accdb' }))
    const dialog = await screen.findByRole('dialog', { name: 'Запросить фактический импорт?' })
    const reason = within(dialog).getByLabelText('Причина фактического импорта')
    const backup = within(dialog).getByLabelText('Backup PostgreSQL создан перед фактическим импортом')
    await user.type(reason, 'Dry-run проверен, backup создан')
    await user.click(backup)
    await user.click(within(dialog).getByRole('button', { name: 'Запросить импорт' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Сервер временно не принял заявку на импорт.')
    expect(screen.getAllByText('Сервер временно не принял заявку на импорт.')).toHaveLength(1)
    expect(reason).toHaveValue('Dry-run проверен, backup создан')
    expect(backup).toBeChecked()

    await user.click(within(dialog).getByRole('button', { name: 'Запросить импорт' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Запросить фактический импорт?' })).not.toBeInTheDocument())
    expect(attempts).toBe(2)
  })

  it('does not refresh a hidden log after apply and keeps a later log failure separate', async () => {
    const user = userEvent.setup()
    let applyAttempts = 0
    let logRequests = 0
    const run = createRun()
    const client = createClient({
      getAccessRuns: async () => [run],
      getAccessRunLog: async () => {
        logRequests += 1
        throw new Error('Журнал импорта временно недоступен.')
      },
      requestAccessImportApply: async (_token, runId) => {
        applyAttempts += 1
        return createRun({ id: runId, status: 'import_requested' })
      },
    })
    render(<ImportPanel auth={auth} importClient={client} />)

    await user.click(await screen.findByRole('button', { name: 'Запросить фактический импорт ГСК.accdb' }))
    const dialog = await screen.findByRole('dialog', { name: 'Запросить фактический импорт?' })
    await user.type(within(dialog).getByLabelText('Причина фактического импорта'), 'Dry-run проверен, backup создан')
    await user.click(within(dialog).getByLabelText('Backup PostgreSQL создан перед фактическим импортом'))
    await user.click(within(dialog).getByRole('button', { name: 'Запросить импорт' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Запросить фактический импорт?' })).not.toBeInTheDocument())
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(logRequests).toBe(0)
    expect(applyAttempts).toBe(1)
    await user.click(screen.getByRole('tab', { name: /Лог/ }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Журнал импорта временно недоступен.')
    expect(screen.getByText('Фактический импорт запрошен. Данные не переносились до подключения reader Access.')).toHaveAttribute('role', 'status')
    expect(applyAttempts).toBe(1)
    expect(logRequests).toBe(1)
  })

  it('shows an apply-cancel failure inside the dialog, preserves the reason and allows retry', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const run = createRun({ status: 'import_requested' })
    const client = createClient({
      getAccessRuns: async () => [run],
      getAccessRun: async () => run,
      cancelAccessImportApplyRequest: async (_token, runId) => {
        attempts += 1
        if (attempts === 1) {
          throw new Error('Сервер временно не отменил заявку на импорт.')
        }
        return createRun({ id: runId, status: 'import_request_cancelled' })
      },
    })
    render(<ImportPanel auth={auth} importClient={client} />)

    await user.click(await screen.findByRole('button', { name: 'Отменить заявку на импорт ГСК.accdb' }))
    const dialog = await screen.findByRole('dialog', { name: 'Отменить заявку на импорт?' })
    const reason = within(dialog).getByLabelText('Причина отмены заявки на импорт')
    await user.type(reason, 'Нужно перепроверить backup')
    await user.click(within(dialog).getByRole('button', { name: 'Отменить заявку' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Сервер временно не отменил заявку на импорт.')
    expect(screen.getAllByText('Сервер временно не отменил заявку на импорт.')).toHaveLength(1)
    expect(reason).toHaveValue('Нужно перепроверить backup')

    await user.click(within(dialog).getByRole('button', { name: 'Отменить заявку' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Отменить заявку на импорт?' })).not.toBeInTheDocument())
    expect(attempts).toBe(2)
  })

  it('shows a rollback failure inside the dialog, preserves the reason and allows retry', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const run = createRun()
    const client = createClient({
      getAccessRuns: async () => [run],
      requestAccessImportRollback: async (_token, runId) => {
        attempts += 1
        if (attempts === 1) {
          throw new Error('Сервер временно не принял rollback.')
        }
        return createRun({ id: runId, status: 'rollback_requested' })
      },
    })
    render(<ImportPanel auth={auth} importClient={client} />)

    await user.click(await screen.findByRole('button', { name: 'Запросить rollback импорта ГСК.accdb' }))
    const dialog = await screen.findByRole('dialog', { name: 'Запросить rollback импорта?' })
    const reason = within(dialog).getByLabelText('Причина rollback импорта')
    await user.type(reason, 'Выбран неверный файл старой базы')
    await user.click(within(dialog).getByRole('button', { name: 'Запросить rollback' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Сервер временно не принял rollback.')
    expect(screen.getAllByText('Сервер временно не принял rollback.')).toHaveLength(1)
    expect(reason).toHaveValue('Выбран неверный файл старой базы')

    await user.click(within(dialog).getByRole('button', { name: 'Запросить rollback' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Запросить rollback импорта?' })).not.toBeInTheDocument())
    expect(attempts).toBe(2)
  })

  it('shows a quarantine resolution failure inside the dialog, preserves the comment and allows retry', async () => {
    const user = userEvent.setup()
    let attempts = 0
    const item = createQuarantineItem()
    const client = createClient({
      getAccessRuns: async () => [],
      getOpenQuarantineItems: async () => [item],
      resolveQuarantineItem: async (_token, itemId) => {
        attempts += 1
        if (attempts === 1) {
          throw new Error('Сервер временно не закрыл строку карантина.')
        }
        return createQuarantineItem({ id: itemId, status: 'resolved' })
      },
    })
    render(<ImportPanel auth={auth} importClient={client} />)

    await user.click(await screen.findByRole('tab', { name: /Карантин/ }))
    await user.click(await screen.findByRole('button', { name: 'Закрыть' }))
    const dialog = await screen.findByRole('dialog', { name: 'Закрыть строку карантина?' })
    const comment = within(dialog).getByLabelText('Комментарий к закрытию строки карантина')
    await user.type(comment, 'Владелец найден и сопоставлен вручную')
    await user.click(within(dialog).getByRole('button', { name: 'Закрыть строку' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Сервер временно не закрыл строку карантина.')
    expect(screen.getAllByText('Сервер временно не закрыл строку карантина.')).toHaveLength(1)
    expect(comment).toHaveValue('Владелец найден и сопоставлен вручную')

    await user.click(within(dialog).getByRole('button', { name: 'Закрыть строку' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Закрыть строку карантина?' })).not.toBeInTheDocument())
    expect(attempts).toBe(2)
  })
})
