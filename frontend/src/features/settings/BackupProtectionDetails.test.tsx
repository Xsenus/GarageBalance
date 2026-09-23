import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { DatabaseBackupFileDto } from '../../services/settingsApi'
import { BackupProtectionDetails } from './BackupProtectionDetails'
import { BackupRestoreStatus } from './BackupRestoreStatus'

const backup: DatabaseBackupFileDto = {
  fileName: 'garagebalance_manual_20260922_100000_000.pgdump', sizeBytes: 1024,
  createdAtUtc: '2026-09-22T10:00:00Z', kind: 'manual', sha256: null,
  protectionState: 'protection_degraded', lastVerifiedAtUtc: '2026-09-22T10:01:00Z',
  protectionLabel: 'Защита ослаблена', protectionTone: 'warning',
  requiredCopies: 2, availableCopies: 1, desiredCopies: 3, requiredOffsiteCopies: 1,
  availableOffsiteCopies: 0, protectionLagSeconds: 61,
  replicas: [
    { destinationId: 'local-hot', location: 'local', state: 'available', stateLabel: 'Проверена', lastVerifiedAtUtc: '2026-09-22T10:01:00Z', error: null },
    { destinationId: 'remote-a', location: 'remote', state: 'failed', stateLabel: 'Ошибка', lastVerifiedAtUtc: null, error: 'Проверьте разрешения доступа к хранилищу.' },
  ],
}

describe('BackupProtectionDetails', () => {
  it('is keyboard-focusable and expands native details with independent copies, lag and destination errors', async () => {
    const user = userEvent.setup()
    render(<BackupProtectionDetails backup={backup} />)
    expect(screen.getByText('Защита ослаблена')).toBeVisible()
    expect(screen.getByText('Копий: 1, нужно 2')).toBeVisible()
    const summary = screen.getByLabelText(`Состояние защиты ${backup.fileName}`)
    expect(summary.closest('details')).not.toHaveAttribute('open')
    await user.tab()
    expect(summary).toHaveFocus()
    // jsdom does not implement summary's native Enter activation; real-browser smoke covers it.
    await user.click(summary)
    expect(summary.closest('details')).toHaveAttribute('open')
    expect(screen.getByText('Внешние копии: 0 из 1')).toBeVisible()
    expect(screen.getByText('Цель: 3')).toBeVisible()
    expect(screen.getByText('Задержка: 2 мин.')).toBeVisible()
    expect(screen.getByText('Проверьте разрешения доступа к хранилищу.')).toBeVisible()
    expect(screen.getByRole('list', { name: 'Хранилища резервной копии' })).toBeVisible()
  })

  it.each([
    ['protected', 'Защищена', 'active'], ['protection_pending', 'Ожидает копирования', 'archived'],
    ['failed', 'Требует внимания', 'danger'], ['manifest_missing', 'Нет манифеста', 'warning'],
    ['local_verified', 'Проверена локально', 'active'], ['local_only', 'Только локально', 'archived'],
    ['deleting', 'Удаляется', 'archived'], ['deleted', 'Удалена', 'archived'],
  ] as const)('renders %s without inferring success', (protectionState, protectionLabel, protectionTone) => {
    render(<BackupProtectionDetails backup={{ ...backup, protectionState, protectionLabel, protectionTone }} />)
    expect(screen.getByText(protectionLabel)).toBeVisible()
    expect(screen.getByText(protectionLabel)).toHaveClass(`dictionary-status-pill-${protectionTone}`)
  })

  it('supports old local responses with no replica details or verification date', async () => {
    const user = userEvent.setup()
    render(<BackupProtectionDetails backup={{ ...backup, protectionLabel: undefined, protectionTone: undefined, requiredCopies: undefined, replicas: null, lastVerifiedAtUtc: null, protectionLagSeconds: 0 }} />)
    expect(screen.getByText('Требует проверки')).toHaveClass('dictionary-status-pill-archived')
    expect(screen.queryByText(/Копий:/)).not.toBeInTheDocument()
    await user.click(screen.getByLabelText(`Состояние защиты ${backup.fileName}`))
    expect(screen.getByText('Последняя проверка: ещё не выполнена')).toBeVisible()
    expect(screen.getByText('Нет данных о копиях.')).toBeVisible()
    expect(screen.queryByText(/Задержка:/)).not.toBeInTheDocument()
  })
})

describe('BackupRestoreStatus', () => {
  it.each([
    ['not_configured', 'Проверка восстановления не настроена'],
    ['not_run', 'Проверка восстановления ещё не запускалась'],
    ['invalid', 'Результат проверки восстановления недоступен'],
    ['running', 'Выполняется проверка восстановления'],
    ['stale', 'Проверку восстановления пора повторить'],
    ['failed', 'Проверка восстановления завершилась ошибкой'],
    ['verified', 'Восстановление проверено'],
  ] as const)('reports %s separately from checksum verification', (state, text) => {
    render(<BackupRestoreStatus verification={{ state, message: text, completedAtUtc: '2026-09-22T10:01:00Z', backupCreatedAtUtc: null, rtoSeconds: 1.2, maximumAgeHours: 168 }} />)
    expect(screen.getByRole('status')).toHaveTextContent(text)
    expect(screen.getByRole('status')).toHaveTextContent('длительность 2 с.')
    if (state === 'stale') expect(screen.getByRole('status')).toHaveTextContent('168 ч.')
  })
  it('omits an unavailable old-server field', () => {
    render(<BackupRestoreStatus verification={null} />)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })
})
