import type { DatabaseBackupFileDto } from '../../services/settingsApi'
import { formatDateTime } from '../../shared/formatters'

export function BackupProtectionDetails({ backup }: { backup: DatabaseBackupFileDto }) {
  return <details className="backup-protection-details">
    <summary tabIndex={0} aria-label={`Состояние защиты ${backup.fileName}`}>
      <span className={`dictionary-status-pill dictionary-status-pill-${backup.protectionTone ?? 'archived'}`}>{backup.protectionLabel ?? 'Требует проверки'}</span>
      {backup.requiredCopies !== undefined ? <span className="form-hint"><br />Копий: {backup.availableCopies ?? 0}, нужно {backup.requiredCopies}</span> : null}
    </summary>
    <div className="form-hint">
      <p>Последняя проверка: {backup.lastVerifiedAtUtc ? formatDateTime(backup.lastVerifiedAtUtc) : 'ещё не выполнена'}</p>
      {backup.desiredCopies !== undefined ? <p>Цель: {backup.desiredCopies}</p> : null}
      {backup.requiredOffsiteCopies !== undefined ? <p>Внешние копии: {backup.availableOffsiteCopies ?? 0} из {backup.requiredOffsiteCopies}</p> : null}
      {(backup.protectionLagSeconds ?? 0) > 0 ? <p>Задержка: {Math.ceil(backup.protectionLagSeconds! / 60)} мин.</p> : null}
      {backup.replicas?.length ? <ul aria-label="Хранилища резервной копии">
        {backup.replicas.map((replica) => <li key={replica.destinationId}>
          {replica.destinationId} · {replica.location === 'local' ? 'локально' : 'внешнее'} — {replica.stateLabel ?? 'Требует проверки'}
          {replica.lastVerifiedAtUtc ? <div>Проверка: {formatDateTime(replica.lastVerifiedAtUtc)}</div> : null}
          {replica.error ? <div className="status-disabled">{replica.error}</div> : null}
        </li>)}
      </ul> : <p>Нет данных о копиях.</p>}
    </div>
  </details>
}
