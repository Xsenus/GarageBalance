import type { ChangePreview } from './changePreview'

type ChangePreviewListProps = {
  ariaLabel: string
  changes: ChangePreview[]
}

export function ChangePreviewList({ ariaLabel, changes }: ChangePreviewListProps) {
  return (
    <ul className="dictionary-change-list" aria-label={ariaLabel}>
      {changes.map((change) => (
        <li key={`${change.field}-${change.before}-${change.after}`}>
          <span className="dictionary-change-field">{change.field}</span>
          <span className="dictionary-change-values">
            <span className="dictionary-change-value">{change.before}</span>
            <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
            <span className="dictionary-change-value dictionary-change-value-after">{change.after}</span>
          </span>
        </li>
      ))}
    </ul>
  )
}
