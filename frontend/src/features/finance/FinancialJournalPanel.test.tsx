import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { AuthResponse } from '../../services/authApi'
import type { FinanceClient, FinancialJournalEntryDto } from '../../services/financeApi'
import type { FundsClient } from '../../services/fundsApi'
import type { ApplicationSettingsClient } from '../../services/settingsApi'
import { ActionCommentSettingsProvider } from '../../shared/ActionCommentSettings'
import { FinancialJournalPanel } from './FinancialJournalPanel'

const writableAuth: AuthResponse = {
  accessToken: 'journal-token',
  expiresAtUtc: '2026-09-04T00:00:00Z',
  user: {
    id: 'user-1',
    email: 'operator@example.test',
    displayName: 'Оператор',
    roles: ['operator'],
    permissions: ['payments.read', 'payments.write', 'audit.read'],
  },
}

function entry(overrides: Partial<FinancialJournalEntryDto> = {}): FinancialJournalEntryDto {
  return {
    id: 'operation-1',
    entityType: 'financial_operation',
    operationType: 'income',
    operationDate: '2026-09-15',
    accountingMonth: '2026-09-01',
    amount: 500,
    counterparty: 'Гараж 103 · Тестовый владелец',
    category: 'Членский взнос',
    documentNumber: 'ПКО-7',
    comment: null,
    source: 'manual',
    isCanceled: false,
    createdAtUtc: '2026-09-15T08:00:00Z',
    version: null,
    canEdit: true,
    canCancel: true,
    canRestore: true,
    protectionReason: null,
    correctionHint: null,
    ...overrides,
  }
}

function clients(items = [entry()]) {
  const getFinancialJournalPage = vi.fn(async (_token: string, params?: { offset?: number; limit?: number }) => ({
    items,
    totalCount: 30,
    offset: params?.offset ?? 0,
    limit: params?.limit ?? 25,
  }))
  const cancelOperation = vi.fn(async () => ({}))
  const restoreOperation = vi.fn(async () => ({}))
  return {
    finance: { getFinancialJournalPage, cancelOperation, restoreOperation } as unknown as FinanceClient,
    funds: { cancelOperation: vi.fn(), restoreOperation: vi.fn() } as unknown as FundsClient,
    getFinancialJournalPage,
    cancelOperation,
    restoreOperation,
  }
}

describe('FinancialJournalPanel', () => {
  it('loads a server page, applies filters, and uses the shared pagination', async () => {
    const client = clients()
    const user = userEvent.setup()
    render(<FinancialJournalPanel auth={writableAuth} financeClient={client.finance} fundsClient={client.funds} onEdit={vi.fn()} />)

    expect(await screen.findByText('Гараж 103 · Тестовый владелец')).toBeInTheDocument()
    expect(screen.getByText('Ручная запись')).toBeInTheDocument()
    await user.type(screen.getByRole('textbox', { name: 'Контрагент журнала' }), '103')
    await user.type(screen.getByRole('textbox', { name: 'Документ журнала' }), 'ПКО')
    await user.click(screen.getByRole('button', { name: 'Применить' }))

    await waitFor(() => expect(client.getFinancialJournalPage).toHaveBeenLastCalledWith('journal-token', expect.objectContaining({ counterparty: '103', document: 'ПКО', offset: 0, limit: 25 })))
    await user.click(screen.getByRole('button', { name: 'Следующая страница' }))
    await waitFor(() => expect(client.getFinancialJournalPage).toHaveBeenLastCalledWith('journal-token', expect.objectContaining({ offset: 25, limit: 25 })))
  })

  it('opens the row menu from the keyboard, validates cancellation, and restores focus', async () => {
    const client = clients()
    const user = userEvent.setup()
    render(<FinancialJournalPanel auth={writableAuth} financeClient={client.finance} fundsClient={client.funds} onEdit={vi.fn()} />)
    const row = (await screen.findByText('ПКО-7')).closest('tr') as HTMLTableRowElement
    row.focus()
    fireEvent.keyDown(row, { key: 'F10', shiftKey: true })
    const menu = await screen.findByRole('menu', { name: /Действия записи журнала/ })
    expect(within(menu).getByRole('menuitem', { name: 'Редактировать' })).toHaveFocus()
    await user.click(within(menu).getByRole('menuitem', { name: 'Отменить' }))

    const dialog = screen.getByRole('dialog', { name: 'Отменить запись?' })
    expect(within(dialog).getByRole('textbox', { name: 'Причина отмены записи журнала' })).toHaveFocus()
    await user.click(within(dialog).getByRole('button', { name: 'Отменить запись' }))
    expect(screen.getByText('Укажите причину отмены.')).toBeInTheDocument()
    await user.type(within(dialog).getByRole('textbox', { name: 'Причина отмены записи журнала' }), 'Исправление документа')
    await user.click(within(dialog).getByRole('button', { name: 'Отменить запись' }))

    await waitFor(() => expect(client.cancelOperation).toHaveBeenCalledWith('journal-token', 'operation-1', { reason: 'Исправление документа' }))
    await waitFor(() => expect(row).toHaveFocus())
  })

  it('allows cancellation without a reason when operation comments are optional', async () => {
    const client = clients()
    const settings = {
      getActionCommentSettings: vi.fn(async () => ({ required: false, version: 'optional-comments' })),
      updateActionCommentSettings: vi.fn(),
    } as unknown as ApplicationSettingsClient
    const user = userEvent.setup()
    render(
      <ActionCommentSettingsProvider accessToken={writableAuth.accessToken} client={settings}>
        <FinancialJournalPanel auth={writableAuth} financeClient={client.finance} fundsClient={client.funds} onEdit={vi.fn()} />
      </ActionCommentSettingsProvider>,
    )

    const row = (await screen.findByText('ПКО-7')).closest('tr') as HTMLTableRowElement
    fireEvent.contextMenu(row)
    await user.click(screen.getByRole('menuitem', { name: 'Отменить' }))
    const dialog = screen.getByRole('dialog', { name: 'Отменить запись?' })
    expect(within(dialog).getByLabelText('Причина отмены записи журнала')).not.toBeRequired()
    await user.click(within(dialog).getByRole('button', { name: 'Отменить запись' }))

    await waitFor(() => expect(client.cancelOperation).toHaveBeenCalledWith('journal-token', 'operation-1', { reason: '' }))
  })

  it('shows protected rows without mutation actions and reports loading failures', async () => {
    const protectedRow = entry({
      entityType: 'cash_bank_balance_operation',
      canEdit: false,
      canCancel: false,
      canRestore: false,
      protectionReason: 'Запись защищена.',
      correctionHint: 'Создайте компенсирующую корректировку.',
    })
    const getFinancialJournalPage = vi.fn().mockRejectedValueOnce(new Error('Сервис временно недоступен')).mockResolvedValueOnce({ items: [protectedRow], totalCount: 1, offset: 0, limit: 25 })
    const finance = { getFinancialJournalPage } as unknown as FinanceClient
    const funds = {} as FundsClient
    const user = userEvent.setup()
    render(<FinancialJournalPanel auth={writableAuth} financeClient={finance} fundsClient={funds} onEdit={vi.fn()} onOpenAudit={vi.fn()} />)

    expect(await screen.findByText('Сервис временно недоступен')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Повторить' }))
    const protectedText = await screen.findByText('Защищено: Запись защищена.')
    const row = protectedText.closest('tr') as HTMLTableRowElement
    fireEvent.contextMenu(row, { clientX: 12, clientY: 12 })
    const menu = screen.getByRole('menu')
    expect(within(menu).queryByRole('menuitem', { name: 'Редактировать' })).not.toBeInTheDocument()
    expect(within(menu).queryByRole('menuitem', { name: 'Отменить' })).not.toBeInTheDocument()
    expect(within(menu).getByRole('menuitem', { name: 'История' })).toBeInTheDocument()
  })
})
