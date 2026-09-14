import { Pagination } from './PageNavigator'
import { pageSizeOptions } from './pagination'
import { SelectControl } from './SelectControl'

export function TablePagination({
  ariaLabel,
  totalCount,
  offset,
  limit,
  visibleCount,
  disabled = false,
  statusText,
  pageSizeLabel = 'Количество строк',
  compactPageSizeSelect = false,
  onPageChange,
  onPageSizeChange,
}: {
  ariaLabel: string
  totalCount: number
  offset: number
  limit: number
  visibleCount: number
  disabled?: boolean
  statusText?: string
  pageSizeLabel?: string
  compactPageSizeSelect?: boolean
  onPageChange: (page: number) => void
  onPageSizeChange: (limit: number) => void
}) {
  const totalPages = Math.max(1, Math.ceil(totalCount / limit))
  const currentPage = Math.min(totalPages, Math.floor(offset / limit) + 1)
  const from = totalCount === 0 || visibleCount === 0 ? 0 : offset + 1
  const to = totalCount === 0 || visibleCount === 0 ? 0 : Math.min(offset + visibleCount, totalCount)

  return (
    <div className={`dictionary-pagination${compactPageSizeSelect ? ' dictionary-pagination--compact' : ''}`} role="navigation" aria-label={ariaLabel}>
      <div className="pagination-primary">
        {compactPageSizeSelect ? (
          <SelectControl
            aria-label={pageSizeLabel}
            className="pagination-page-size-select"
            value={String(limit)}
            options={pageSizeOptions.map((size) => ({ value: String(size), label: String(size) }))}
            disabled={disabled}
            placement="above"
            onChange={(value) => onPageSizeChange(Number(value))}
          />
        ) : (
          <div className="pagination-page-sizes" role="group" aria-label={pageSizeLabel}>
            {pageSizeOptions.map((size) => (
              <button
                className={size === limit ? 'pagination-size is-active' : 'pagination-size'}
                type="button"
                aria-pressed={size === limit}
                disabled={disabled}
                onClick={() => onPageSizeChange(size)}
                key={size}
              >
                {size}
              </button>
            ))}
          </div>
        )}
        <Pagination currentPage={currentPage} totalPages={totalPages} disabled={disabled} showQuickJump onPageChange={onPageChange} />
      </div>
      <div className="pagination-meta">
        {compactPageSizeSelect ? (
          <span role="status" aria-live="polite">{from}-{to} из {totalCount} · стр. {currentPage}/{totalPages}{statusText ? ` · ${statusText}` : ''}</span>
        ) : (
          <>
            <span role="status" aria-live="polite">Показано {from}-{to} из {totalCount}</span>
            <span>Страница {currentPage} из {totalPages} · Найдено: {totalCount}{statusText ? ` · ${statusText}` : ''}</span>
          </>
        )}
      </div>
    </div>
  )
}
