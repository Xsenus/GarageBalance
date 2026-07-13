import { useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { clampPage, getVisiblePaginationItems } from './pageNavigatorModel'

function QuickPageJump({ currentPage, totalPages, disabled, onPageChange }: { currentPage: number; totalPages: number; disabled: boolean; onPageChange: (page: number) => void }) {
  const [pageInput, setPageInput] = useState(() => String(currentPage))

  function jumpToPage(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedPage = Number.parseInt(pageInput.replace(/\s+/g, ''), 10)
    if (!Number.isFinite(parsedPage)) {
      setPageInput(String(currentPage))
      return
    }

    const nextPage = clampPage(parsedPage, totalPages)
    setPageInput(String(nextPage))
    onPageChange(nextPage)
  }

  return (
    <form className="pagination-quick-jump" onSubmit={jumpToPage}>
      <span>Стр.</span>
      <input aria-label="Перейти к странице" inputMode="numeric" value={pageInput} disabled={disabled} onChange={(event) => setPageInput(event.target.value)} onBlur={() => setPageInput(String(currentPage))} />
      <span>из {totalPages}</span>
    </form>
  )
}

export function Pagination({
  currentPage,
  totalPages,
  onPageChange,
  showQuickJump = false,
  quickJumpThreshold = 100,
  disabled = false,
}: {
  currentPage: number
  totalPages: number
  onPageChange: (page: number) => void
  showQuickJump?: boolean
  quickJumpThreshold?: number
  disabled?: boolean
}) {
  const safeTotalPages = Math.max(1, totalPages)
  const safeCurrentPage = clampPage(currentPage, safeTotalPages)
  const pages = useMemo(() => getVisiblePaginationItems(safeCurrentPage, safeTotalPages), [safeCurrentPage, safeTotalPages])

  function changePage(page: number) {
    if (disabled) {
      return
    }

    const nextPage = clampPage(page, safeTotalPages)
    if (nextPage !== safeCurrentPage) {
      onPageChange(nextPage)
    }
  }

  return (
    <div className="pagination-controls">
      <button className="pagination-arrow" type="button" aria-label="Предыдущая страница" title="Предыдущая страница" disabled={disabled || safeCurrentPage === 1} onClick={() => changePage(safeCurrentPage - 1)}>
        <ChevronLeft size={16} />
      </button>
      {pages.map((page, index) => page === 'dots' ? (
        <span className="pagination-dots" aria-hidden="true" key={`dots-${index}`}>…</span>
      ) : (
        <button
          className={page === safeCurrentPage ? 'pagination-page is-active' : 'pagination-page'}
          type="button"
          aria-label={`Страница ${page}`}
          aria-current={page === safeCurrentPage ? 'page' : undefined}
          disabled={disabled}
          onClick={() => changePage(page)}
          key={page}
        >
          {page}
        </button>
      ))}
      <button className="pagination-arrow" type="button" aria-label="Следующая страница" title="Следующая страница" disabled={disabled || safeCurrentPage === safeTotalPages} onClick={() => changePage(safeCurrentPage + 1)}>
        <ChevronRight size={16} />
      </button>
      {showQuickJump && safeTotalPages > quickJumpThreshold ? (
        <QuickPageJump currentPage={safeCurrentPage} totalPages={safeTotalPages} disabled={disabled} onPageChange={changePage} key={safeCurrentPage} />
      ) : null}
    </div>
  )
}
