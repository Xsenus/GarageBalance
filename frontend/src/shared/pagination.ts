export type PagedItems<TItem> = {
  items: TItem[]
  totalCount: number
  offset: number
  limit: number
}

export const pageSizeOptions = [10, 25, 50, 100] as const

export function createEmptyPage<TItem>(limit = 25): PagedItems<TItem> {
  return { items: [], totalCount: 0, offset: 0, limit }
}

export function createFallbackPage<TItem>(items: TItem[], offset: number, limit: number): PagedItems<TItem> {
  return { items: items.slice(offset, offset + limit), totalCount: items.length, offset, limit }
}

export function createClientPage<TItem>(items: TItem[], pageNumber: number, limit: number): PagedItems<TItem> {
  const safeLimit = Math.max(1, limit)
  const totalPages = Math.max(1, Math.ceil(items.length / safeLimit))
  const safePageNumber = Math.min(Math.max(1, pageNumber), totalPages)
  const offset = (safePageNumber - 1) * safeLimit
  return { items: items.slice(offset, offset + safeLimit), totalCount: items.length, offset, limit: safeLimit }
}

export function getPageVisibleRange(page: PagedItems<unknown>) {
  if (page.totalCount === 0 || page.items.length === 0) {
    return { from: 0, to: 0 }
  }

  return {
    from: page.offset + 1,
    to: Math.min(page.offset + page.items.length, page.totalCount),
  }
}

export function getPageNavigation(page: PagedItems<unknown>) {
  const canGoPrevious = page.offset > 0
  const canGoNext = page.offset + page.limit < page.totalCount

  return {
    canGoPrevious,
    canGoNext,
    previousOffset: Math.max(0, page.offset - page.limit),
    nextOffset: page.offset + page.limit,
  }
}
