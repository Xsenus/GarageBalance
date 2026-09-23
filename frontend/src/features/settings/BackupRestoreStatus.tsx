import type { DatabaseBackupStatusDto } from '../../services/settingsApi'
import { formatDateTime } from '../../shared/formatters'

export function BackupRestoreStatus({ verification }: { verification: DatabaseBackupStatusDto['restoreVerification'] }) {
  if (!verification) return null
  return <p className="form-hint" role="status">
    <strong className={verification.state === 'verified' ? 'status-active' : 'status-disabled'}>{verification.message}</strong>
    {verification.completedAtUtc ? ` · ${formatDateTime(verification.completedAtUtc)}` : null}
    {verification.rtoSeconds !== null ? ` · длительность ${Math.ceil(verification.rtoSeconds)} с.` : null}
    {verification.state === 'stale' ? ` Порог: ${verification.maximumAgeHours} ч.` : null}
  </p>
}
