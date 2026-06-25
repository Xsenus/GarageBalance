import { describe, expect, it } from 'vitest'
import { createEmptyPage, createFallbackPage, getPageNavigation, getPageVisibleRange, pageSizeOptions } from './pagination'

describe('pagination helpers', () => {
  it('keeps shared page size options stable', () => {
    expect(pageSizeOptions).toEqual([10, 25, 50, 100])
  })

  it('creates an empty page with default and custom limits', () => {
    expect(createEmptyPage<string>()).toEqual({ items: [], totalCount: 0, offset: 0, limit: 25 })
    expect(createEmptyPage<string>(50)).toEqual({ items: [], totalCount: 0, offset: 0, limit: 50 })
  })

  it('creates fallback slices without losing the total item count', () => {
    const items = ['one', 'two', 'three', 'four']

    expect(createFallbackPage(items, 1, 2)).toEqual({
      items: ['two', 'three'],
      totalCount: 4,
      offset: 1,
      limit: 2,
    })

    expect(createFallbackPage(items, 3, 2)).toEqual({
      items: ['four'],
      totalCount: 4,
      offset: 3,
      limit: 2,
    })
  })

  it('returns the visible row range for paged tables', () => {
    expect(getPageVisibleRange(createEmptyPage<string>())).toEqual({ from: 0, to: 0 })
    expect(getPageVisibleRange({ items: ['one', 'two'], totalCount: 4, offset: 0, limit: 2 })).toEqual({ from: 1, to: 2 })
    expect(getPageVisibleRange({ items: ['four'], totalCount: 4, offset: 3, limit: 2 })).toEqual({ from: 4, to: 4 })
    expect(getPageVisibleRange({ items: [], totalCount: 4, offset: 4, limit: 2 })).toEqual({ from: 0, to: 0 })
  })

  it('returns shared previous and next navigation offsets', () => {
    expect(getPageNavigation({ items: ['one', 'two'], totalCount: 5, offset: 0, limit: 2 })).toEqual({
      canGoPrevious: false,
      canGoNext: true,
      previousOffset: 0,
      nextOffset: 2,
    })

    expect(getPageNavigation({ items: ['three', 'four'], totalCount: 5, offset: 2, limit: 2 })).toEqual({
      canGoPrevious: true,
      canGoNext: true,
      previousOffset: 0,
      nextOffset: 4,
    })

    expect(getPageNavigation({ items: ['five'], totalCount: 5, offset: 4, limit: 2 })).toEqual({
      canGoPrevious: true,
      canGoNext: false,
      previousOffset: 2,
      nextOffset: 6,
    })
  })
})
