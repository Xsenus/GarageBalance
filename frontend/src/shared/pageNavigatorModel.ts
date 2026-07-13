export type PaginationItem = number | 'dots'

export function clampPage(page: number, totalPages: number): number {
  return Math.min(Math.max(page, 1), Math.max(1, totalPages))
}

export function getVisiblePaginationItems(currentPage: number, totalPages: number): PaginationItem[] {
  const safeTotalPages = Math.max(1, totalPages)
  const safeCurrentPage = clampPage(currentPage, safeTotalPages)
  const maxVisibleMiddlePages = 5

  if (safeTotalPages <= maxVisibleMiddlePages + 2) {
    return Array.from({ length: safeTotalPages }, (_, index) => index + 1)
  }

  const result: PaginationItem[] = [1]
  let start = Math.max(2, safeCurrentPage - 2)
  let end = Math.min(safeTotalPages - 1, safeCurrentPage + 2)
  const visibleMiddleCount = end - start + 1

  if (visibleMiddleCount < maxVisibleMiddlePages) {
    if (start === 2) {
      end = Math.min(safeTotalPages - 1, start + maxVisibleMiddlePages - 1)
    } else if (end === safeTotalPages - 1) {
      start = Math.max(2, end - maxVisibleMiddlePages + 1)
    }
  }

  if (start > 2) {
    result.push('dots')
  }

  for (let page = start; page <= end; page += 1) {
    result.push(page)
  }

  if (end < safeTotalPages - 1) {
    result.push('dots')
  }

  result.push(safeTotalPages)
  return result
}
